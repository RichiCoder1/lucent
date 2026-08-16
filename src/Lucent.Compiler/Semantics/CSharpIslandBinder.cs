using Lucent.Compiler.Parsing;
using Lucent.Compiler.Syntax;
using Lucent.Compiler.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Lucent.Compiler.Semantics;

internal enum CSharpIslandRole
{
    StateInitializer,
    ComputedInitialValue,
    ComputedFactory,
    Property,
    Content,
    EventBody,
    LoopSource,
    LoopKey,
    Condition,
}

internal sealed record BoundLocal(string Name, ITypeSymbol Type);

internal sealed record CSharpIslandRequest(
    int Id,
    string Text,
    SourceSpan Span,
    CSharpIslandKind Kind,
    CSharpIslandRole Role,
    ITypeSymbol? ExpectedType,
    IReadOnlyList<BoundLocal> Locals);

internal sealed record BoundCSharpIsland(
    string SourceText,
    string LoweredText,
    SourceSpan Span,
    CSharpIslandKind Kind,
    IReadOnlyList<int> Dependencies,
    IReadOnlyList<BoundReactiveRead> ReactiveReads);

internal sealed record BoundReactiveRead(int SourceId, SourceSpan Span);

internal sealed class CSharpIslandBinder(
    ProjectSemanticCompilation project,
    IReadOnlyList<BoundReactiveSource> sources,
    DiagnosticBag diagnostics)
{
    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public IReadOnlyDictionary<int, BoundCSharpIsland> BindAll(
        IReadOnlyList<CSharpIslandRequest> requests)
    {
        if (requests.Count == 0)
        {
            return new Dictionary<int, BoundCSharpIsland>();
        }

        var text = new System.Text.StringBuilder("#nullable enable\n");
        foreach (var import in project.Imports)
        {
            text.Append("using ").Append(import).Append(";\n");
        }

        text.Append("internal sealed class __LucentState<T> { public T Value = default!; public void Update(T value) {} public void Update(global::System.Func<T,T> update) {} }\n")
            .Append("internal sealed class __LucentComputed<T> { public T Value = default!; public bool IsPending; public string? ErrorMessage; }\n")
            .Append("internal sealed class __LucentProbe {\n");
        foreach (var source in sources)
        {
            var wrapper = source.Kind == BoundReactiveSourceKind.State
                ? "__LucentState"
                : "__LucentComputed";
            text.Append("private ").Append(wrapper).Append('<').Append(source.ValueTypeName)
                .Append("> ").Append(source.Name).Append(" = new();\n");
        }

        var mappings = new Dictionary<int, TextSpan>();
        foreach (var request in requests)
        {
            var returnType = request.Kind == CSharpIslandKind.StatementBlock
                ? "void"
                : request.ExpectedType?.ToDisplayString(TypeFormat) ?? "object?";
            text.Append("private ").Append(returnType).Append(" __Island")
                .Append(request.Id).Append('(')
                .Append(string.Join(", ", request.Locals.Select(local =>
                    $"{FormatType(local.Type)} {local.Name}")))
                .Append(')');
            if (request.Kind == CSharpIslandKind.StatementBlock)
            {
                text.Append(" {\n");
                var start = text.Length;
                text.Append(request.Text);
                mappings.Add(request.Id, new TextSpan(start, request.Text.Length));
                text.Append("\n}\n");
            }
            else
            {
                text.Append(" => ");
                var start = text.Length;
                text.Append(request.Text);
                mappings.Add(request.Id, new TextSpan(start, request.Text.Length));
                text.Append(";\n");
            }
        }
        text.Append("}\n");

        var tree = CSharpSyntaxTree.ParseText(
            text.ToString(),
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var compilation = project.Compilation.AddSyntaxTrees(tree);
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var sourceFields = new Dictionary<ISymbol, BoundReactiveSource>(SymbolEqualityComparer.Default);
        foreach (var candidate in root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(node => node.Parent?.Parent is FieldDeclarationSyntax)
            .Select(node => (Node: node, Symbol: model.GetDeclaredSymbol(node) as IFieldSymbol))
            .Where(candidate => candidate.Symbol is not null)
            .Select(candidate => (candidate.Node, Symbol: candidate.Symbol!))
            .Join(sources, candidate => candidate.Node.Identifier.ValueText, source => source.Name,
                (candidate, source) => (Symbol: (ISymbol)candidate.Symbol, Source: source)))
        {
            sourceFields[candidate.Symbol] = candidate.Source;
        }

        foreach (var diagnostic in compilation.GetDiagnostics()
                     .Where(item => item.Severity == DiagnosticSeverity.Error &&
                                    item.Location.SourceTree == tree &&
                                    item.Id is not "CS1973"))
        {
            if (diagnostic.Id == "CS0103" &&
                tree.GetText().ToString(diagnostic.Location.SourceSpan) is { Length: > 0 } missing &&
                char.IsUpper(missing[0]))
            {
                // Opaque project type/member calls remain snapshots when no project context is supplied.
                continue;
            }

            var mapping = mappings.FirstOrDefault(candidate =>
                candidate.Value.IntersectsWith(diagnostic.Location.SourceSpan));
            if (mapping.Value == default)
            {
                continue;
            }

            var request = requests.First(candidate => candidate.Id == mapping.Key);
            var relative = Math.Clamp(
                diagnostic.Location.SourceSpan.Start - mapping.Value.Start,
                0,
                request.Text.Length);
            diagnostics.Add(
                "LUC3001",
                $"Embedded C# is invalid: {diagnostic.GetMessage()}",
                new SourceSpan(request.Span.Start + relative,
                    Math.Max(1, diagnostic.Location.SourceSpan.Length)));
        }

        var results = new Dictionary<int, BoundCSharpIsland>();
        foreach (var request in requests)
        {
            var mapping = mappings[request.Id];
            SyntaxNode node = request.Kind == CSharpIslandKind.StatementBlock
                ? root.FindNode(mapping).AncestorsAndSelf().OfType<BlockSyntax>().First()
                : root.FindNode(mapping, getInnermostNodeForTie: true)
                    .AncestorsAndSelf().OfType<ArrowExpressionClauseSyntax>().First().Expression;
            var reads = new List<BoundReactiveRead>();
            var rewriter = new ReactiveRewriter(model, sourceFields, request, mapping, reads);
            var rewritten = rewriter.Visit(node)!;
            var lowered = request.Kind == CSharpIslandKind.StatementBlock
                ? string.Join(Environment.NewLine,
                    ((BlockSyntax)rewritten).Statements.Select(statement => statement.ToFullString().TrimEnd()))
                : rewritten.ToFullString();
            var readIds = reads.Select(read => read.SourceId).ToHashSet();
            var dependencies = sources.Where(source => readIds.Contains(source.Id))
                .Select(source => source.Id).ToArray();
            results.Add(request.Id, new BoundCSharpIsland(
                request.Text,
                lowered,
                request.Span,
                request.Kind,
                dependencies,
                reads));
        }

        return results;
    }

    private static string FormatType(ITypeSymbol type) =>
        type.TypeKind == TypeKind.Dynamic ? "dynamic" : type.ToDisplayString(TypeFormat);

    private sealed class ReactiveRewriter(
        SemanticModel model,
        IReadOnlyDictionary<ISymbol, BoundReactiveSource> sourceFields,
        CSharpIslandRequest request,
        TextSpan mapping,
        List<BoundReactiveRead> reads) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var receiver = model.GetSymbolInfo(node.Expression).Symbol;
            if (receiver is null || !sourceFields.TryGetValue(receiver, out var source))
            {
                return base.VisitMemberAccessExpression(node);
            }

            var name = node.Name.Identifier.ValueText;
            if (name is "Value" or "IsPending" or "ErrorMessage")
            {
                reads.Add(new BoundReactiveRead(
                    source.Id,
                    new SourceSpan(
                        request.Span.Start + node.Span.Start - mapping.Start,
                        node.Span.Length)));
            }

            var lowered = name switch
            {
                "Value" => "_" + source.Name,
                "IsPending" when source.Kind == BoundReactiveSourceKind.Computed =>
                    "_" + source.Name + "Pending",
                "ErrorMessage" when source.Kind == BoundReactiveSourceKind.Computed =>
                    "_" + source.Name + "ErrorMessage",
                "Update" when source.Kind == BoundReactiveSourceKind.State =>
                    "Set" + char.ToUpperInvariant(source.Name[0]) + source.Name[1..],
                _ => null,
            };
            return lowered is null
                ? base.VisitMemberAccessExpression(node)
                : SyntaxFactory.IdentifierName(lowered).WithTriviaFrom(node);
        }
    }
}
