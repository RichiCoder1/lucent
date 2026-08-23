using Lucent.Compiler.Parsing;
using Lucent.Compiler.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using CSharpEqualsValueClauseSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.EqualsValueClauseSyntax;
using CSharpIdentifierNameSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax;
using CSharpMemberAccessExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax;
using CSharpSyntaxRewriter = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxRewriter;

namespace Lucent.Compiler.Semantics;

internal sealed record ComponentParameterSymbol(
    string TypeName,
    string Name,
    string? DefaultValueText,
    SourceSpan Span,
    string? BoundDefaultValueText = null);

internal sealed record ComponentSlotSymbol(string Name, SourceSpan Span);

internal sealed record ComponentSymbol(
    string NamespaceName,
    string Name,
    string GeneratedTypeName,
    SourceSpan DeclarationSpan,
    string SourcePath,
    IReadOnlyList<ComponentParameterSymbol> Parameters,
    IReadOnlyList<ComponentSlotSymbol> Slots,
    int OutputCardinality = 1);

internal sealed class ComponentIndex
{
    private readonly IReadOnlyDictionary<string, ComponentSymbol> _symbols;

    private ComponentIndex(IReadOnlyDictionary<string, ComponentSymbol> symbols) =>
        _symbols = symbols;

    public IReadOnlyCollection<ComponentSymbol> Symbols => _symbols.Values.ToArray();

    public static ComponentIndex Create(
        IReadOnlyList<(string Path, string Text, CompilationUnitSyntax Syntax, DiagnosticBag Bag)> sources,
        ProjectSemanticCompilation project)
    {
        var candidates = sources.Select(source => (Source: source, Component: source.Syntax.Component))
            .Where(candidate => candidate.Component.Name != "Missing")
            .ToArray();

        var declarations = candidates.Select(candidate => new ComponentSymbol(
                candidate.Source.Syntax.NamespaceName,
                candidate.Component.Name,
                candidate.Component.Name + "Component",
                new SourceSpan(candidate.Component.Span.Start + "component ".Length,
                    candidate.Component.Name.Length),
                candidate.Source.Path,
                candidate.Component.AllParameters.Select(parameter => new ComponentParameterSymbol(
                    parameter.TypeName, parameter.Name, parameter.DefaultValueText, parameter.Span)).ToArray(),
                new[] { new ComponentSlotSymbol("children", candidate.Component.Span) }
                    .Concat(candidate.Component.AllSlots.Select(slot =>
                        new ComponentSlotSymbol(slot.Name, slot.Span))).ToArray(),
                0))
            .ToArray();
        var symbolsByKey = declarations
            .GroupBy(SymbolKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        declarations = declarations.Select(declaration =>
        {
            var syntax = candidates.First(candidate =>
                PathComparer.Equals(candidate.Source.Path, declaration.SourcePath) &&
                candidate.Component.Name == declaration.Name).Component;
            var declarationSource = candidates.First(candidate =>
                PathComparer.Equals(candidate.Source.Path, declaration.SourcePath) &&
                candidate.Component.Name == declaration.Name).Source;
            var sourceImports = new ProjectSemanticCompilation(
                declaration.NamespaceName,
                declarationSource.Syntax.AllUsings.Select(item => item.Text).ToArray(),
                project.Compilation).Imports;
            var cardinality = syntax.RenderMethod.RenderedFragment.Roots
                .Sum(root => Cardinality(root, declaration.NamespaceName,
                    sourceImports,
                    new HashSet<string>(StringComparer.Ordinal)));
            return declaration with { OutputCardinality = cardinality };
        }).ToArray();

        foreach (var duplicate in declarations.GroupBy(SymbolKey).Where(group => group.Count() > 1))
        {
            foreach (var symbol in duplicate)
            {
                Source(symbol.SourcePath).Bag.Add("LUC2001",
                    $"Component '{symbol.NamespaceName}.{symbol.Name}' is declared more than once.",
                    symbol.DeclarationSpan);
            }
        }

        foreach (var symbol in declarations)
        {
            var declarationSource = sources.First(source =>
                PathComparer.Equals(source.Path, symbol.SourcePath));
            var metadataName = string.IsNullOrEmpty(symbol.NamespaceName)
                ? symbol.GeneratedTypeName
                : symbol.NamespaceName + "." + symbol.GeneratedTypeName;
            if (project.Compilation.GetTypeByMetadataName(metadataName) is { } collision)
            {
                Source(symbol.SourcePath).Bag.Add("LUC2001",
                    $"Generated component type '{metadataName}' conflicts with an authored C# type.",
                    symbol.DeclarationSpan);
                foreach (var location in collision.Locations.Where(location => location.IsInSource))
                {
                    var sourcePath = location.SourceTree?.FilePath;
                    if (string.IsNullOrEmpty(sourcePath)) continue;
                    var locationSpan = location.SourceSpan;
                    Source(symbol.SourcePath).Bag.AddExternal("LUC2001",
                        $"Authored C# type '{metadataName}' conflicts with this Lucent component.",
                        new SourceSpan(locationSpan.Start, Math.Max(1, locationSpan.Length)),
                        sourcePath, location.SourceTree!.GetText().ToString());
                }
            }
        }

        declarations = declarations
            .Select(symbol => BindParameterDefaults(symbol,
                sources.First(source => PathComparer.Equals(source.Path, symbol.SourcePath))))
            .ToArray();

        return new ComponentIndex(declarations
            .GroupBy(SymbolKey)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal));

        (string Path, string Text, CompilationUnitSyntax Syntax, DiagnosticBag Bag) Source(string path) =>
            sources.First(source => PathComparer.Equals(source.Path, path));

        int Cardinality(
            UiElementSyntax element,
            string currentNamespace,
            IReadOnlyList<string> imports,
            HashSet<string> path)
        {
            var key = string.IsNullOrEmpty(currentNamespace)
                ? element.Name
                : currentNamespace + "." + element.Name;
            var component = Resolve(element.Name, currentNamespace, imports).FirstOrDefault();
            if (component is null)
            {
                return 1;
            }
            if (!path.Add(key))
            {
                return 2;
            }
            var source = declarations.First(candidate =>
                SymbolKey(candidate) == SymbolKey(component));
            var syntax = candidates.First(candidate =>
                PathComparer.Equals(candidate.Source.Path, source.SourcePath) &&
                candidate.Component.Name == source.Name).Component;
            var sourceData = candidates.First(candidate =>
                PathComparer.Equals(candidate.Source.Path, source.SourcePath)).Source;
            var nestedImports = new ProjectSemanticCompilation(
                sourceData.Syntax.NamespaceName,
                sourceData.Syntax.AllUsings.Select(item => item.Text).ToArray(),
                project.Compilation).Imports;
            var value = syntax.RenderMethod.RenderedFragment.Roots
                .Sum(root => Cardinality(root, sourceData.Syntax.NamespaceName, nestedImports, path));
            path.Remove(key);
            return value;
        }

        ComponentSymbol BindParameterDefaults(
            ComponentSymbol symbol,
            (string Path, string Text, CompilationUnitSyntax Syntax, DiagnosticBag Bag) source)
        {
            var parameters = symbol.Parameters.ToArray();
            foreach (var parameterIndex in Enumerable.Range(0, parameters.Length))
            {
                var parameter = parameters[parameterIndex];
                if (parameter.DefaultValueText is null) continue;
                var signatureProject = new ProjectSemanticCompilation(
                    symbol.NamespaceName,
                    source.Syntax.AllUsings.Select(usingDirective => usingDirective.Text).ToArray(),
                    project.Compilation);
                var text = string.Join(Environment.NewLine, signatureProject.Imports.Select(import => $"using {import};")) +
                    Environment.NewLine +
                    $"namespace {symbol.NamespaceName}; class __LucentSignature {{ void M({parameter.TypeName} {parameter.Name} = {parameter.DefaultValueText}) {{ }} }}";
                var tree = CSharpSyntaxTree.ParseText(
                    text,
                    project.ParseOptions,
                    path: source.Path);
                var model = project.Compilation.AddSyntaxTrees(tree).GetSemanticModel(tree);
                foreach (var diagnostic in model.GetDiagnostics().Where(diagnostic =>
                             diagnostic.Severity == DiagnosticSeverity.Error))
                {
                    source.Bag.Add("LUC2001",
                        $"Invalid default for component parameter '{parameter.Name}': {diagnostic.GetMessage()}",
                        parameter.Span);
                }

                var defaultNode = tree.GetRoot().DescendantNodes()
                    .OfType<CSharpEqualsValueClauseSyntax>()
                    .SingleOrDefault();
                if (defaultNode is not null)
                {
                    var lowered = new DefaultValueRewriter(model)
                        .Visit(defaultNode.Value)!.ToFullString().Trim();
                    parameters[parameterIndex] = parameter with { BoundDefaultValueText = lowered };
                }
            }
            return symbol with { Parameters = parameters };
        }

        IReadOnlyList<ComponentSymbol> Resolve(
            string name,
            string currentNamespace,
            IReadOnlyList<string> imports)
        {
            if (name.Contains('.', StringComparison.Ordinal))
            {
                return symbolsByKey.TryGetValue(name, out var qualified) ? [qualified] : [];
            }
            var namespaces = new[] { currentNamespace }.Concat(imports)
                .Distinct(StringComparer.Ordinal);
            return namespaces.Select(ns => string.IsNullOrEmpty(ns) ? name : ns + "." + name)
                .Where(symbolsByKey.ContainsKey)
                .Select(key => symbolsByKey[key])
                .Distinct()
                .ToArray();
        }
    }

    private sealed class DefaultValueRewriter(SemanticModel model) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIdentifierName(CSharpIdentifierNameSyntax node)
        {
            var symbol = model.GetSymbolInfo(node).Symbol;
            return symbol is INamedTypeSymbol type
                ? SyntaxFactory.ParseName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .WithTriviaFrom(node)
                : base.VisitIdentifierName(node);
        }

        public override SyntaxNode? VisitMemberAccessExpression(CSharpMemberAccessExpressionSyntax node)
        {
            var receiver = model.GetSymbolInfo(node.Expression).Symbol;
            if (receiver is INamedTypeSymbol type)
            {
                var rewritten = SyntaxFactory.ParseExpression(
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." +
                    node.Name.Identifier.ValueText);
                return rewritten.WithTriviaFrom(node);
            }
            return base.VisitMemberAccessExpression(node);
        }
    }

    public IReadOnlyList<ComponentSymbol> Resolve(
        string name,
        string currentNamespace,
        IReadOnlyList<string> imports)
    {
        if (name.Contains('.', StringComparison.Ordinal))
        {
            return _symbols.TryGetValue(name, out var qualified) ? [qualified] : [];
        }

        var namespaces = new[] { currentNamespace }.Concat(imports).Distinct(StringComparer.Ordinal);
        return namespaces.Select(ns => string.IsNullOrEmpty(ns) ? name : ns + "." + name)
            .Where(_symbols.ContainsKey).Select(key => _symbols[key]).Distinct().ToArray();
    }

    private static string SymbolKey(ComponentSymbol symbol) =>
        string.IsNullOrEmpty(symbol.NamespaceName) ? symbol.Name : symbol.NamespaceName + "." + symbol.Name;

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
