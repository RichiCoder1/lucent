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
    ComponentArgument,
    NativeCollectionElement,
    OrdinaryMember,
}

internal sealed record BoundLocal(string Name, ITypeSymbol Type, string? SourceExpression = null);

internal sealed record CSharpIslandRequest(
    int Id,
    string Text,
    SourceSpan Span,
    CSharpIslandKind Kind,
    CSharpIslandRole Role,
    ITypeSymbol? ExpectedType,
    IReadOnlyList<BoundLocal> Locals,
    SourceSpan EditorSpan);

internal sealed record BoundCSharpIsland(
    string SourceText,
    string LoweredText,
    SourceSpan Span,
    CSharpIslandKind Kind,
    IReadOnlyList<int> Dependencies,
    IReadOnlyList<BoundReactiveRead> ReactiveReads);

internal sealed record BoundReactiveRead(int SourceId, SourceSpan Span);

internal sealed record CSharpIslandBindingResult(
    IReadOnlyDictionary<int, BoundCSharpIsland> Islands,
    IReadOnlyList<BoundIslandScope> EditorScopes,
    IReadOnlyDictionary<string, string>? OrdinaryMembers = null);

internal sealed class CSharpIslandBinder(
    ProjectSemanticCompilation project,
    IReadOnlyList<BoundReactiveSource> sources,
    DiagnosticBag diagnostics,
    IReadOnlyList<OrdinaryMemberSyntax>? ordinaryMembers = null)
{
    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public CSharpIslandBindingResult BindAll(
        IReadOnlyList<CSharpIslandRequest> requests)
    {
        if (requests.Count == 0 && (ordinaryMembers?.Count ?? 0) == 0)
        {
            return new CSharpIslandBindingResult(
                new Dictionary<int, BoundCSharpIsland>(), [], new Dictionary<string, string>());
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
            if (source.Kind == BoundReactiveSourceKind.Parameter)
            {
                text.Append("private ").Append(source.ValueTypeName).Append(' ')
                    .Append(source.Name).Append(" => default!;\n");
                continue;
            }
            var wrapper = source.Kind == BoundReactiveSourceKind.State
                ? "__LucentState"
                : "__LucentComputed";
            text.Append("private ").Append(wrapper).Append('<').Append(source.ValueTypeName)
                .Append("> ").Append(source.Name).Append(" = new();\n");
        }
        var ordinaryMappings = new List<(TextSpan Synthetic, SourceSpan Source, bool Editor)>();
        var constructorAssignments = new List<(string Name, string Expression, SourceSpan Source)>();
        foreach (var member in ordinaryMembers ?? [])
        {
            var declaration = SyntaxFactory.ParseMemberDeclaration(member.Text);
            if (declaration is FieldDeclarationSyntax field &&
                field.Declaration.Variables.Count == 1 &&
                field.Declaration.Variables[0].Initializer is { } initializer &&
                !field.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.ConstKeyword) ||
                                                  modifier.IsKind(SyntaxKind.StaticKeyword)))
            {
                var variable = field.Declaration.Variables[0];
                var declarationWithoutInitializer = field.WithDeclaration(
                    field.Declaration.WithVariables([variable.WithInitializer(null)]));
                var declarationStart = text.Length;
                text.Append(declarationWithoutInitializer.ToFullString()).Append('\n');
                ordinaryMappings.Add((
                    new TextSpan(declarationStart, declarationWithoutInitializer.FullSpan.Length),
                    member.Span,
                    false));
                constructorAssignments.Add((
                    variable.Identifier.ValueText,
                    initializer.Value.ToFullString().Trim(),
                    member.Span));
                continue;
            }

            var start = text.Length;
            text.Append(member.Text).Append('\n');
            ordinaryMappings.Add((new TextSpan(start, member.Text.Length), member.Span, true));
        }
        if (constructorAssignments.Count > 0)
        {
            text.Append("private __LucentProbe() {\n");
            foreach (var assignment in constructorAssignments)
            {
                var start = text.Length;
                text.Append(assignment.Name).Append(" = ").Append(assignment.Expression).Append(";\n");
                ordinaryMappings.Add((new TextSpan(start, assignment.Expression.Length), assignment.Source, false));
            }
            text.Append("}\n");
        }

        var mappings = new Dictionary<int, TextSpan>();
        foreach (var request in requests)
        {
            var returnType = request.Kind == CSharpIslandKind.StatementBlock
                ? "void"
                : request.ExpectedType?.ToDisplayString(TypeFormat) ?? "object?";
            var inferredLocal = request.Locals.FirstOrDefault(local =>
                local.Type.TypeKind == TypeKind.Dynamic && local.SourceExpression is not null);
            text.Append("private ").Append(returnType).Append(" __Island")
                .Append(request.Id).Append('(')
                .Append(string.Join(", ", request.Locals.Where(local => local != inferredLocal).Select(local =>
                    $"{FormatType(local.Type)} {local.Name}")))
                .Append(')');
            if (inferredLocal is not null)
            {
                text.Append(" { foreach (var ").Append(inferredLocal.Name).Append(" in ")
                    .Append(inferredLocal.SourceExpression).Append(") {");
                if (request.Kind != CSharpIslandKind.StatementBlock)
                {
                    text.Append("return ");
                }
                var start = text.Length;
                text.Append(request.Text);
                mappings.Add(request.Id, new TextSpan(start, request.Text.Length));
                text.Append(request.Kind == CSharpIslandKind.StatementBlock
                    ? "\n} }\n"
                    : ";\n} return default!; }\n");
            }
            else if (request.Kind == CSharpIslandKind.StatementBlock)
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
        foreach (var candidate in root.DescendantNodes().OfType<PropertyDeclarationSyntax>()
                     .Select(node => (Node: node, Symbol: model.GetDeclaredSymbol(node)))
                     .Where(candidate => candidate.Symbol is not null)
                     .Join(sources.Where(source => source.Kind == BoundReactiveSourceKind.Parameter),
                         candidate => candidate.Node.Identifier.ValueText, source => source.Name,
                         (candidate, source) => (Symbol: candidate.Symbol!, Source: source)))
        {
            sourceFields[candidate.Symbol] = candidate.Source;
        }

        var methodSummaries = BuildMethodSummaries(root, model, sourceFields);
        var loweredMembers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in ordinaryMembers ?? [])
        {
            var node = root.DescendantNodes().OfType<MemberDeclarationSyntax>().FirstOrDefault(candidate =>
                candidate switch
                {
                    MethodDeclarationSyntax method => method.Identifier.ValueText == member.Name,
                    FieldDeclarationSyntax field => field.Declaration.Variables.Any(variable =>
                        variable.Identifier.ValueText == member.Name),
                    _ => false,
                });
            if (node is not null)
            {
                var original = SyntaxFactory.ParseMemberDeclaration(member.Text);
                loweredMembers[member.Name] = original is FieldDeclarationSyntax originalField &&
                    originalField.Declaration.Variables.Count == 1 &&
                    originalField.Declaration.Variables[0].Initializer is not null
                    ? member.Text
                    : new OrdinaryLoweringRewriter(model, sourceFields).Visit(node)!.ToFullString().Trim();
            }
        }

        foreach (var diagnostic in compilation.GetDiagnostics()
                     .Where(item => item.Severity == DiagnosticSeverity.Error &&
                                    item.Location.SourceTree == tree))
        {
            var mapping = mappings.FirstOrDefault(candidate =>
                candidate.Value.IntersectsWith(diagnostic.Location.SourceSpan));
            if (mapping.Value == default)
            {
                var ordinary = ordinaryMappings.FirstOrDefault(candidate =>
                    candidate.Synthetic.IntersectsWith(diagnostic.Location.SourceSpan));
                if (ordinary != default)
                {
                    var ordinaryRelative = Math.Clamp(
                        diagnostic.Location.SourceSpan.Start - ordinary.Synthetic.Start,
                        0,
                        ordinary.Synthetic.Length);
                    diagnostics.Add(
                        "LUC3001",
                        $"Embedded C# is invalid: {diagnostic.GetMessage()}",
                        new SourceSpan(ordinary.Source.Start + ordinaryRelative,
                            Math.Max(1, diagnostic.Location.SourceSpan.Length)));
                }
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
            var node = FindRequestNode(root, mapping, request.Kind);
            if (node is null)
            {
                results.Add(request.Id, new BoundCSharpIsland(
                    request.Text, request.Text, request.Span, request.Kind, [], []));
                continue;
            }
            var reads = new List<BoundReactiveRead>();
            var rewriter = new ReactiveRewriter(model, sourceFields, methodSummaries,
                diagnostics, request, mapping, reads);
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

        var loopTypes = requests
            .Where(request => request.Role == CSharpIslandRole.LoopSource)
            .ToDictionary(
                request => request.Id,
                request => GetExpressionType(root, model, mappings[request.Id]));
        var ordinaryDefinitions = (ordinaryMembers ?? [])
            .ToDictionary(member => member.Name, member => member.NameSpan, StringComparer.Ordinal);
        var editorScopes = requests.Select(request =>
        {
            var resolvedLocals = request.Locals.Select(local =>
            {
                var sourceRequest = local.SourceExpression is null
                    ? null
                    : requests.FirstOrDefault(candidate =>
                        candidate.Role == CSharpIslandRole.LoopSource &&
                        candidate.Text == local.SourceExpression);
                var itemType = sourceRequest is null ||
                               loopTypes[sourceRequest.Id] is not { } sourceType
                    ? null
                    : NativeSymbolResolver.GetEnumerableElementType(sourceType);
                return itemType is null ? local : local with { Type = itemType };
            }).ToArray();
            var localTypes = resolvedLocals.ToDictionary(local => local.Name, local => local.Type);
            return new BoundIslandScope(
                request.EditorSpan,
                resolvedLocals,
                new BoundIslandSemanticContext(
                    model,
                    mappings[request.Id],
                    request.Span,
                    localTypes,
                    ordinaryDefinitions),
                request.Role);
        }).Concat(ordinaryMappings.Where(mapping => mapping.Editor).Select(mapping =>
            new BoundIslandScope(
                mapping.Source,
                [],
                new BoundIslandSemanticContext(
                    model,
                    mapping.Synthetic,
                    mapping.Source,
                    new Dictionary<string, ITypeSymbol>(),
                    ordinaryDefinitions),
                CSharpIslandRole.OrdinaryMember)))
            .ToArray();
        return new CSharpIslandBindingResult(results, editorScopes, loweredMembers);
    }

    private static ITypeSymbol? GetExpressionType(
        SyntaxNode root,
        SemanticModel model,
        TextSpan mapping) =>
        FindRequestNode(root, mapping, CSharpIslandKind.Expression) is { } expression
            ? model.GetTypeInfo(expression).Type
            : null;

    private static SyntaxNode? FindRequestNode(
        SyntaxNode root,
        TextSpan mapping,
        CSharpIslandKind kind)
    {
        var node = root.FindNode(mapping, getInnermostNodeForTie: true);
        return kind == CSharpIslandKind.StatementBlock
            ? node.AncestorsAndSelf().OfType<BlockSyntax>().FirstOrDefault()
            : node.AncestorsAndSelf().OfType<ExpressionSyntax>()
                .FirstOrDefault(expression => expression.Span == mapping) ??
              node.AncestorsAndSelf().OfType<ExpressionSyntax>().FirstOrDefault();
    }

    private static string FormatType(ITypeSymbol type) =>
        type.TypeKind == TypeKind.Dynamic ? "dynamic" : type.ToDisplayString(TypeFormat);

    private static IReadOnlyDictionary<IMethodSymbol, MethodSummary> BuildMethodSummaries(
        SyntaxNode root,
        SemanticModel model,
        IReadOnlyDictionary<ISymbol, BoundReactiveSource> sourceFields)
    {
        var methods = new Dictionary<IMethodSymbol, MethodDeclarationSyntax>(SymbolEqualityComparer.Default);
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                     .Where(method => !method.Identifier.ValueText.StartsWith("__Island", StringComparison.Ordinal)))
        {
            if (model.GetDeclaredSymbol(method) is { } symbol) methods[symbol] = method;
        }
        var summaries = new Dictionary<IMethodSymbol, MethodSummary>(SymbolEqualityComparer.Default);
        foreach (var pair in methods)
        {
            var dependencies = new HashSet<int>();
            var mutates = false;
            var calls = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var node in pair.Value.DescendantNodes())
            {
                if (node is IdentifierNameSyntax identifier &&
                    model.GetSymbolInfo(identifier).Symbol is { } symbol &&
                    sourceFields.TryGetValue(symbol, out var parameter) &&
                    parameter.Kind == BoundReactiveSourceKind.Parameter)
                {
                    dependencies.Add(parameter.Id);
                }
                if (node is MemberAccessExpressionSyntax access &&
                    model.GetSymbolInfo(access.Expression).Symbol is { } receiver &&
                    sourceFields.TryGetValue(receiver, out var source))
                {
                    if (access.Name.Identifier.ValueText is "Value" or "IsPending" or "ErrorMessage")
                        dependencies.Add(source.Id);
                    if (source.Kind == BoundReactiveSourceKind.State &&
                        access.Name.Identifier.ValueText == "Update") mutates = true;
                }
                if (node is InvocationExpressionSyntax invocation &&
                    model.GetSymbolInfo(invocation).Symbol is IMethodSymbol called &&
                    methods.ContainsKey(called)) calls.Add(called);
            }
            summaries[pair.Key] = new MethodSummary(dependencies, mutates, calls);
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var summary in summaries.Values)
            {
                foreach (var called in summary.Calls)
                {
                    var target = summaries[called];
                    foreach (var dependency in target.Dependencies)
                        changed |= summary.Dependencies.Add(dependency);
                    if (target.Mutates && !summary.Mutates) { summary.Mutates = true; changed = true; }
                }
            }
        }
        return summaries;
    }

    private sealed class MethodSummary(
        HashSet<int> dependencies,
        bool mutates,
        HashSet<IMethodSymbol> calls)
    {
        public HashSet<int> Dependencies { get; } = dependencies;
        public bool Mutates { get; set; } = mutates;
        public HashSet<IMethodSymbol> Calls { get; } = calls;
    }

    private sealed class OrdinaryLoweringRewriter(
        SemanticModel model,
        IReadOnlyDictionary<ISymbol, BoundReactiveSource> sources) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (model.GetSymbolInfo(node.Expression).Symbol is not { } symbol ||
                !sources.TryGetValue(symbol, out var source)) return base.VisitMemberAccessExpression(node);
            var pascal = char.ToUpperInvariant(source.Name[0]) + source.Name[1..];
            var lowered = node.Name.Identifier.ValueText switch
            {
                "Value" when source.Kind == BoundReactiveSourceKind.State => "__lucent_state" + pascal,
                "Value" when source.Kind == BoundReactiveSourceKind.Computed => "__lucent_computed" + pascal,
                "Update" when source.Kind == BoundReactiveSourceKind.State => "__lucent_Set" + pascal,
                "IsPending" when source.Kind == BoundReactiveSourceKind.Computed => "__lucent_computed" + pascal + "Pending",
                "ErrorMessage" when source.Kind == BoundReactiveSourceKind.Computed => "__lucent_computed" + pascal + "ErrorMessage",
                _ => null,
            };
            return lowered is null ? base.VisitMemberAccessExpression(node) :
                SyntaxFactory.IdentifierName(lowered).WithTriviaFrom(node);
        }
    }

    private sealed class ReactiveRewriter(
        SemanticModel model,
        IReadOnlyDictionary<ISymbol, BoundReactiveSource> sourceFields,
        IReadOnlyDictionary<IMethodSymbol, MethodSummary> methodSummaries,
        DiagnosticBag diagnostics,
        CSharpIslandRequest request,
        TextSpan mapping,
        List<BoundReactiveRead> reads) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (model.GetSymbolInfo(node).Symbol is IMethodSymbol method &&
                methodSummaries.TryGetValue(method, out var summary))
            {
                foreach (var dependency in summary.Dependencies)
                {
                    reads.Add(new BoundReactiveRead(dependency,
                        new SourceSpan(request.Span.Start + node.Span.Start - mapping.Start,
                            node.Span.Length)));
                }
                if (summary.Mutates && request.Role is CSharpIslandRole.Property or
                    CSharpIslandRole.Content or CSharpIslandRole.Condition or
                    CSharpIslandRole.LoopSource or CSharpIslandRole.LoopKey or
                    CSharpIslandRole.ComponentArgument)
                {
                    diagnostics.Add("LUC3001",
                        "A render computation cannot call a component method that mutates state.",
                        new SourceSpan(request.Span.Start + node.Span.Start - mapping.Start,
                            node.Span.Length));
                }
            }
            return base.VisitInvocationExpression(node);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var symbol = model.GetSymbolInfo(node).Symbol;
            if (symbol is null || !sourceFields.TryGetValue(symbol, out var source) ||
                source.Kind != BoundReactiveSourceKind.Parameter)
            {
                return base.VisitIdentifierName(node);
            }

            reads.Add(new BoundReactiveRead(source.Id,
                new SourceSpan(request.Span.Start + node.Span.Start - mapping.Start,
                    node.Span.Length)));
            return SyntaxFactory.IdentifierName("__lucent_input" +
                char.ToUpperInvariant(source.Name[0]) + source.Name[1..]).WithTriviaFrom(node);
        }

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
                "Value" => source.Kind == BoundReactiveSourceKind.State
                    ? "__lucent_state" + char.ToUpperInvariant(source.Name[0]) + source.Name[1..]
                    : "__lucent_computed" + char.ToUpperInvariant(source.Name[0]) + source.Name[1..],
                "IsPending" when source.Kind == BoundReactiveSourceKind.Computed =>
                    "__lucent_computed" + char.ToUpperInvariant(source.Name[0]) + source.Name[1..] + "Pending",
                "ErrorMessage" when source.Kind == BoundReactiveSourceKind.Computed =>
                    "__lucent_computed" + char.ToUpperInvariant(source.Name[0]) + source.Name[1..] + "ErrorMessage",
                "Update" when source.Kind == BoundReactiveSourceKind.State =>
                    "__lucent_Set" + char.ToUpperInvariant(source.Name[0]) + source.Name[1..],
                _ => null,
            };
            return lowered is null
                ? base.VisitMemberAccessExpression(node)
                : SyntaxFactory.IdentifierName(lowered).WithTriviaFrom(node);
        }
    }
}
