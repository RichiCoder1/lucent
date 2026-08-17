using Lucent.Compiler.CodeGeneration;
using Lucent.Compiler.Parsing;
using Lucent.Compiler.Semantics;
using Lucent.Compiler.Styling;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lucent.Compiler;

public static class LucentCompiler
{
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
            return EditorIntelligence.GetCssCompletions(sourceText, offset);
        var analysis = ComponentSemanticAnalysis.Create(sourceText, sourcePath, projectContext);
        return EditorIntelligence.GetCompletions(sourceText, offset, analysis);
    }

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
        baseCompilation = AddComponentSemanticStubs(baseCompilation, parsed, index);
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
            string? generated = null;
            if (!projectHasErrors && analysis.Model is not null)
            {
                try
                {
                    generated = GeneralCSharpEmitter.Emit(analysis.Model,
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

            results.Add(new LucentSourceCompilation(parsed[indexValue].Input.SourcePath,
                new CompilationResult(analysis.Syntax, generated, diagnostics[indexValue])
                {
                    Symbols = analysis.Symbols,
                    Analysis = analysis,
                }));
        }

        return new LucentProjectCompilationResult(results);
    }

    private static CSharpCompilation AddComponentSemanticStubs(
        CSharpCompilation compilation,
        IReadOnlyList<ParsedSource> parsed,
        ComponentIndex index)
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
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
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
