using Lucent.Compiler.CodeGeneration;
using System.Collections.Immutable;
using Lucent.Compiler.Parsing;
using Lucent.Compiler.Semantics;
using Lucent.Compiler.Styling;
using Lucent.Compiler.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lucent.Compiler;

public static class LucentCompiler
{
    // The LSP consumes this project-generation snapshot; it never reads PE files itself.
    internal static ReferencedManifestSnapshot LoadReferencedManifestSnapshot(
        LucentProjectContext? projectContext,
        CancellationToken cancellationToken)
    {
        var cache = new ReferencedManifestCache(projectContext?.References ?? []);
        cache.Load(cancellationToken);
        return new ReferencedManifestSnapshot(cache.Catalog, cache.Diagnostics.ToImmutableArray());
    }

    // Build this once per project generation. Completion only reads the published snapshot.
    internal static ReferencedManifestSnapshot LoadActiveThemeManifestSnapshot(
        LucentProjectContext? projectContext,
        CancellationToken cancellationToken)
    {
        var cache = new ReferencedManifestCache(projectContext?.References ?? []);
        cache.Load(cancellationToken);
        return new ReferencedManifestSnapshot(
            cache.CatalogForActiveThemes(ProjectSemanticCompilation.CreateBaseCompilation(projectContext), projectContext?.ProjectPath),
            cache.Diagnostics.ToImmutableArray());
    }

    internal static bool HasDirectStyleInstall(LucentProjectContext context, string catalogType)
    {
        var compilation = ProjectSemanticCompilation.CreateBaseCompilation(context);
        var parseOptions = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions
            ?? CSharpParseOptions.Default;
        compilation = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(StyleCatalogStub(catalogType), parseOptions));
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var add in tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>())
            {
                if (add.Expression is not Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Add", Expression: var styles } ||
                    add.ArgumentList.Arguments.Count != 1 ||
                    add.ArgumentList.Arguments[0].Expression is not Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax created ||
                    UsesAlias(created.Type, model) ||
                    model.GetSymbolInfo(styles).Symbol is not IPropertySymbol { Name: "Styles" } property ||
                    !IsApplicationStyles(property)) continue;
                var resolved = model.GetTypeInfo(created).Type?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                if (string.Equals(resolved, catalogType, StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }

    private static string StyleCatalogStub(string catalogType)
    {
        var separator = catalogType.LastIndexOf('.');
        return separator < 0
            ? $"public sealed class {catalogType} : global::Avalonia.Styling.Styles {{ }}"
            : $"namespace {catalogType[..separator]}; public sealed class {catalogType[(separator + 1)..]} : global::Avalonia.Styling.Styles {{ }}";
    }

    private static bool IsApplicationStyles(IPropertySymbol property) =>
        property.OriginalDefinition.ContainingType
            .ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == "Avalonia.Application";

    private static bool UsesAlias(Microsoft.CodeAnalysis.CSharp.Syntax.TypeSyntax type, SemanticModel model) =>
        type.DescendantNodesAndSelf().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.NameSyntax>()
            .Any(name => name is Microsoft.CodeAnalysis.CSharp.Syntax.AliasQualifiedNameSyntax ||
                         model.GetAliasInfo(name) is not null);

    /// <summary>
    /// Rebinds one open source against its existing project snapshot when its
    /// exported component signatures have not changed. Callers must rebuild the
    /// project when this returns <see langword="false"/>.
    /// </summary>
    public static bool TryRecompileUnchangedComponentSignatures(
        string sourceText,
        string sourcePath,
        CompilationResult previous,
        LucentProjectContext? projectContext,
        out CompilationResult result)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(previous);
        var previousAnalysis = previous.Analysis;
        if (previousAnalysis?.ComponentIndex is null)
        {
            result = previous;
            return false;
        }

        var parser = new Parser(sourceText, sourcePath);
        var syntax = parser.Parse();
        if (!SameComponentSignatures(previousAnalysis.Syntax, syntax))
        {
            result = previous;
            return false;
        }

        var analysis = ComponentSemanticAnalysis.Create(
            sourceText,
            sourcePath,
            syntax,
            parser.Diagnostics.ToArray(),
            projectContext,
            previousAnalysis.Project.Compilation,
            previousAnalysis.ComponentIndex);
        var diagnostics = analysis.Diagnostics.ToList();
        GeneratedCSharp? emitted = null;
        if (!diagnostics.Any(diagnostic => diagnostic.Severity == LucentDiagnosticSeverity.Error) &&
            analysis.Model is not null)
        {
            try
            {
                emitted = GeneralCSharpEmitter.EmitWithProvenance(analysis.Model, sourcePath,
                    sourceText, BoundStyleSheet.Empty);
            }
            catch (InvalidOperationException exception)
            {
                diagnostics.Add(new LucentDiagnostic("LUC4001",
                    LucentDiagnosticSeverity.Error, exception.Message,
                    new SourceSpan(0, 1), 1, 1, sourcePath));
            }
        }

        var generated = emitted?.Text;
        result = new CompilationResult(analysis.Syntax, generated, diagnostics)
        {
            Symbols = analysis.Symbols,
            SourceMap = emitted is null ? null : LucentSourceMap.Create(sourceText, emitted, sourcePath),
            Analysis = analysis,
        };
        return true;
    }

    /// <summary>Normalizes line endings and trailing whitespace without changing syntax.</summary>
    public static string Format(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        var lines = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++)
            lines[index] = lines[index].TrimEnd();
        return string.Join("\n", lines).TrimEnd() + "\n";
    }

    private static bool SameComponentSignatures(
        CompilationUnitSyntax previous,
        CompilationUnitSyntax current)
    {
        if (!string.Equals(previous.NamespaceName, current.NamespaceName, StringComparison.Ordinal) ||
            previous.AllComponents.Count != current.AllComponents.Count)
        {
            return false;
        }

        return previous.AllComponents.Zip(current.AllComponents, (left, right) =>
                string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
                left.AllParameters.Count == right.AllParameters.Count &&
                left.AllSlots.Select(slot => slot.Name).SequenceEqual(right.AllSlots.Select(slot => slot.Name), StringComparer.Ordinal) &&
                left.AllParameters.Zip(right.AllParameters, (leftParameter, rightParameter) =>
                    string.Equals(leftParameter.Name, rightParameter.Name, StringComparison.Ordinal) &&
                    string.Equals(leftParameter.TypeName, rightParameter.TypeName, StringComparison.Ordinal) &&
                    string.Equals(leftParameter.DefaultValueText, rightParameter.DefaultValueText, StringComparison.Ordinal))
                    .All(equal => equal))
            .All(equal => equal);
    }

    public static LucentSemanticSymbol? GetExpressionSymbol(
        string sourceText,
        int offset,
        string sourcePath = "<memory>",
        LucentProjectContext? projectContext = null)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var analysis = ComponentSemanticAnalysis.Create(sourceText, sourcePath, projectContext);
        return EditorIntelligence.GetExpressionSymbol(sourceText, offset, analysis);
    }

    public static IReadOnlyList<LucentCompletionItem> GetCompletions(
        string sourceText,
        int offset,
        string sourcePath = "<memory>",
        LucentProjectContext? projectContext = null)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (sourcePath.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            return EditorIntelligence.GetCssCompletions(sourceText, offset, LocalPath(sourcePath));
        var analysis = ComponentSemanticAnalysis.Create(sourceText, sourcePath, projectContext);
        return EditorIntelligence.GetCompletions(sourceText, offset, analysis);
    }

    public static IReadOnlyList<LucentCompletionItem> GetCssCompletions(
        string sourceText,
        int offset,
        string sourcePath,
        CssProjectTokenIndex projectTokens) =>
        EditorIntelligence.GetCssCompletions(sourceText, offset, LocalPath(sourcePath), projectTokens);

    public static CssNavigation? GetCssDefinition(
        string sourceText,
        int offset,
        string sourcePath,
        CssProjectTokenIndex? projectTokens = null) =>
        EditorIntelligence.GetCssDefinition(sourceText, offset, LocalPath(sourcePath), projectTokens);

    private static string LocalPath(string path) => Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile
        ? uri.LocalPath : path;

    public static IReadOnlyList<LucentCompletionItem> GetCompletions(
        string sourceText,
        int offset,
        CompilationResult compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return compilation.Analysis is { } analysis
            ? EditorIntelligence.GetCompletions(sourceText, offset, analysis)
            : [];
    }

    internal static IReadOnlyList<LucentCompletionItem> GetCompletions(
        string sourceText, int offset, CompilationResult compilation, CssProjectTokenIndex cssTokens) =>
        compilation.Analysis is { } analysis
            ? EditorIntelligence.GetCompletions(sourceText, offset, analysis, cssTokens)
            : [];

    public static LucentSemanticSymbol? GetExpressionSymbol(
        string sourceText,
        int offset,
        CompilationResult compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return compilation.Analysis is { } analysis
            ? EditorIntelligence.GetExpressionSymbol(sourceText, offset, analysis)
            : null;
    }

    public static CompilationResult Compile(
        string sourceText,
        string sourcePath = "<memory>") =>
        Compile(sourceText, sourcePath, projectContext: null);

    public static CompilationResult Compile(
        string sourceText,
        string sourcePath,
        LucentProjectContext? projectContext) =>
        Compile(sourceText, sourcePath, projectContext, styleText: null, stylePath: null);

    public static CompilationResult Compile(
        string sourceText,
        string sourcePath,
        LucentProjectContext? projectContext,
        string? styleText,
        string? stylePath)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        return CompileProject(
            [new LucentSourceInput(sourcePath, sourceText, stylePath, styleText)],
            projectContext).Sources[0].Result;
    }

    public static LucentProjectCompilationResult CompileProject(
        IReadOnlyList<LucentSourceInput> sources,
        LucentProjectContext? projectContext = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            return new LucentProjectCompilationResult([]);
        }

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var parsed = new List<ParsedSource>(sources.Count);
        foreach (var input in sources)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentException.ThrowIfNullOrWhiteSpace(input.SourcePath);
            ArgumentNullException.ThrowIfNull(input.SourceText);
            var path = input.SourcePath;
            var parser = new Parser(input.SourceText, path);
            var syntax = parser.Parse();
            parsed.Add(new ParsedSource(input, syntax,
                parser.Diagnostics.ToList(),
                new DiagnosticBag(new SourceDocument(input.SourceText, path))));
        }

        foreach (var duplicate in parsed.GroupBy(
                     source => Path.GetFullPath(source.Input.SourcePath), comparer)
                     .Where(group => group.Count() > 1))
        {
            foreach (var source in duplicate)
            {
                source.BatchDiagnostics.Add("LUC2001",
                    $"Lucent source '{source.Input.SourcePath}' was supplied more than once.",
                    new SourceSpan(0, 1));
            }
        }

        var baseCompilation = ProjectSemanticCompilation.CreateBaseCompilation(projectContext);
        var first = parsed[0];
        var indexProject = new ProjectSemanticCompilation(first.Syntax.NamespaceName,
            first.Syntax.AllUsings.Select(item => item.Text).ToArray(), baseCompilation);
        var index = ComponentIndex.Create(parsed.Select(source =>
            (source.Input.SourcePath, source.Input.SourceText, source.Syntax, source.BatchDiagnostics)).ToArray(),
            indexProject);
        baseCompilation = AddComponentSemanticStubs(baseCompilation, parsed, index, projectContext);
        var analyses = parsed.Select(source => ComponentSemanticAnalysis.Create(
            source.Input.SourceText,
            source.Input.SourcePath,
            source.Syntax,
            source.ParserDiagnostics.Concat(source.BatchDiagnostics.Items).ToArray(),
            projectContext,
            baseCompilation,
            index)).ToArray();

        var styleSheets = new BoundStyleSheet[parsed.Count];
        var diagnostics = analyses.Select(analysis => analysis.Diagnostics.ToList()).ToArray();
        DetectComponentCycles(analyses, diagnostics);
        for (var indexValue = 0; indexValue < parsed.Count; indexValue++)
        {
            styleSheets[indexValue] = BoundStyleSheet.Empty;
            var input = parsed[indexValue].Input;
            if (input.StyleText is null) continue;
            var styles = StyleSheetParser.Parse(input.StyleText,
                input.StylePath ?? Path.ChangeExtension(input.SourcePath, ".css"));
            styleSheets[indexValue] = styles.Sheet;
            diagnostics[indexValue].AddRange(styles.Diagnostics);
            if (analyses[indexValue].Model is { } styledModel)
            {
                var styleValidation = StyleSheetValidator.Validate(
                    styles.Sheet,
                    styledModel,
                    analyses[indexValue].Resolver,
                    input.StylePath ?? Path.ChangeExtension(input.SourcePath, ".css"),
                    input.StyleText);
                styleSheets[indexValue] = styleValidation.Sheet;
                diagnostics[indexValue].AddRange(styleValidation.Diagnostics);
            }
            if (styles.Sheet.Rules.Count > 0 && analyses[indexValue].Model is { } model &&
                EffectiveOutputCardinality(model.Roots) == 0)
            {
                diagnostics[indexValue].Add(new LucentDiagnostic(
                    "LUC4001",
                    LucentDiagnosticSeverity.Error,
                    "Component CSS cannot be applied to an empty Fragment.",
                    new SourceSpan(0, 1),
                    1,
                    1,
                    input.StylePath ?? Path.ChangeExtension(input.SourcePath, ".css")));
            }
        }

        var projectHasErrors = diagnostics.SelectMany(items => items).Any(item =>
            item.Severity == LucentDiagnosticSeverity.Error);
        var results = new List<LucentSourceCompilation>(parsed.Count);
        for (var indexValue = 0; indexValue < parsed.Count; indexValue++)
        {
            var analysis = analyses[indexValue];
            GeneratedCSharp? emitted = null;
            if (!projectHasErrors && analysis.Model is not null)
            {
                try
                {
                    emitted = GeneralCSharpEmitter.EmitWithProvenance(analysis.Model,
                        parsed[indexValue].Input.SourcePath,
                        parsed[indexValue].Input.SourceText,
                        styleSheets[indexValue]);
                }
                catch (InvalidOperationException exception)
                {
                    diagnostics[indexValue].Add(new LucentDiagnostic(
                        "LUC4001", LucentDiagnosticSeverity.Error, exception.Message,
                        new SourceSpan(0, 1), 1, 1,
                        parsed[indexValue].Input.StylePath ?? parsed[indexValue].Input.SourcePath));
                }
            }

            var generated = emitted?.Text;
            results.Add(new LucentSourceCompilation(parsed[indexValue].Input.SourcePath,
                new CompilationResult(analysis.Syntax, generated, diagnostics[indexValue])
                {
                    Symbols = analysis.Symbols,
                    SourceMap = emitted is null
                        ? null
                        : LucentSourceMap.Create(parsed[indexValue].Input.SourceText, emitted,
                            parsed[indexValue].Input.SourcePath,
                            parsed[indexValue].Input.GeneratedOutputPath),
                    Analysis = analysis,
                }));
        }

        return new LucentProjectCompilationResult(results);
    }

    private static CSharpCompilation AddComponentSemanticStubs(
        CSharpCompilation compilation,
        IReadOnlyList<ParsedSource> parsed,
        ComponentIndex index,
        LucentProjectContext? projectContext)
    {
        var stubs = new List<SyntaxTree>();
        foreach (var symbol in index.Symbols)
        {
            var source = parsed.First(candidate =>
                PathEquals(candidate.Input.SourcePath, symbol.SourcePath));
            var roots = source.Syntax.Component.RenderMethod.RenderedFragment.Roots;
            var root = roots.Count == 1 ? roots[0] : null;
            var resolver = new NativeSymbolResolver(new ProjectSemanticCompilation(
                symbol.NamespaceName,
                source.Syntax.AllUsings.Select(usingDirective => usingDirective.Text).ToArray(),
                compilation));
            var rootType = root is not null
                ? resolver.ResolveControl(root.Name)?.TypeName
                : null;
            var constructorParameters = symbol.Parameters.Select(parameter =>
                $"{parameter.TypeName} {parameter.Name}" +
                ((parameter.BoundDefaultValueText ?? parameter.DefaultValueText) is { } defaultValue
                    ? $" = {defaultValue}"
                    : string.Empty)).ToList();
            constructorParameters.Add("global::Lucent.Runtime.IUiDispatcher? __lucent_dispatcher = null");
            var sourceText = (string.IsNullOrWhiteSpace(symbol.NamespaceName)
                    ? string.Empty
                    : $"namespace {symbol.NamespaceName}\n") +
                "{" + Environment.NewLine +
                "[global::System.CodeDom.Compiler.GeneratedCode(\"Lucent.Compiler\", \"1.0\")]" + Environment.NewLine +
                $"internal sealed class {symbol.GeneratedTypeName} : global::System.IDisposable" + Environment.NewLine +
                "{" + Environment.NewLine +
                $"    internal {symbol.GeneratedTypeName}({string.Join(", ", constructorParameters)}) {{ }}" + Environment.NewLine +
                "    public global::Lucent.Runtime.Fragment Mount() => global::Lucent.Runtime.Fragment.Empty;" + Environment.NewLine +
                (rootType is null
                    ? string.Empty
                    : $"    public {rootType} MountRoot() => default!;" + Environment.NewLine) +
                "    public void Dispose() { }" + Environment.NewLine +
                "}" + Environment.NewLine +
                "}";
            var imports = string.Join(Environment.NewLine,
                source.Syntax.AllUsings.Select(usingDirective => usingDirective.Text));
            stubs.Add(CSharpSyntaxTree.ParseText(
                imports + Environment.NewLine + sourceText,
                ProjectSemanticCompilation.CreateParseOptions(projectContext),
                symbol.GeneratedTypeName + ".SemanticStub.g.cs"));
        }

        if (stubs.Count == 0)
            return compilation;

        return compilation.AddSyntaxTrees(stubs);
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed record ParsedSource(
        LucentSourceInput Input,
        Syntax.CompilationUnitSyntax Syntax,
        IReadOnlyList<LucentDiagnostic> ParserDiagnostics,
        DiagnosticBag BatchDiagnostics);

    private static int EffectiveOutputCardinality(
        IEnumerable<BoundRenderableModel> roots) =>
        roots.Sum(root => root switch
        {
            BoundControlModel => 1,
            BoundComponentInvocationModel invocation => invocation.Component.OutputCardinality,
            _ => 0,
        });

    private static void DetectComponentCycles(
        IReadOnlyList<ComponentSemanticAnalysis> analyses,
        IReadOnlyList<List<LucentDiagnostic>> diagnostics)
    {
        var models = analyses.Select((analysis, index) => (analysis.Model, index))
            .Where(candidate => candidate.Model is not null)
            .GroupBy(candidate => QualifiedName(candidate.Model!), StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => (Model: group.First().Model!, group.First().index), StringComparer.Ordinal);
        foreach (var start in models.Values)
        {
            Visit(QualifiedName(start.Model), QualifiedName(start.Model), new HashSet<string>());

            void Visit(string current, string root, HashSet<string> path)
            {
                if (!models.TryGetValue(current, out var entry) || !path.Add(current)) return;
                foreach (var invocation in ComponentInvocations(entry.Model.Roots))
                {
                    var target = string.IsNullOrEmpty(invocation.Component.NamespaceName)
                        ? invocation.Component.Name
                        : invocation.Component.NamespaceName + "." + invocation.Component.Name;
                    if (target == root || path.Contains(target))
                    {
                        diagnostics[entry.index].Add(new LucentDiagnostic(
                            "LUC2001", LucentDiagnosticSeverity.Error,
                            $"Component invocation cycle closes at '{invocation.Component.Name}'.",
                            invocation.Span, 1, 1, analyses[entry.index].SourcePath));
                    }
                    else Visit(target, root, path);
                }
                path.Remove(current);
            }
        }

        static string QualifiedName(BoundComponentModel model) =>
            string.IsNullOrEmpty(model.NamespaceName)
                ? model.ComponentName
                : model.NamespaceName + "." + model.ComponentName;
    }

    private static IEnumerable<BoundComponentInvocationModel> ComponentInvocations(
        IEnumerable<BoundRenderableModel> roots)
    {
        foreach (var root in roots)
        {
            if (root is BoundComponentInvocationModel invocation)
            {
                yield return invocation;
                foreach (var nested in ComponentInvocations(invocation.Slots.SelectMany(slot => slot.Roots)))
                    yield return nested;
            }
            else if (root is BoundControlModel control)
            {
                foreach (var child in control.Members.OfType<BoundComponentChildMember>())
                {
                    yield return child.Invocation;
                }
                foreach (var nested in ComponentInvocations(control.Members.OfType<BoundChildMember>()
                             .Select(child => (BoundRenderableModel)child.Child)))
                    yield return nested;
                foreach (var conditional in control.Members.OfType<BoundConditionalMember>())
                {
                    var branchRoots = conditional is BoundAsyncBoundary asyncBoundary
                        ? asyncBoundary.ContentRoots
                            .Concat(asyncBoundary.LoadingRoots ?? [])
                            .Concat(asyncBoundary.FallbackRoots)
                        : conditional.TrueRoots.Concat(conditional.FalseRoots ?? []);
                    foreach (var nested in ComponentInvocations(branchRoots))
                        yield return nested;
                }
                foreach (var loop in control.Members.OfType<BoundForEachMember>())
                {
                    foreach (var nested in ComponentInvocations([loop.Body]))
                        yield return nested;
                }
            }
        }
    }
}
