using Lucent.Compiler.Syntax;
using Lucent.Compiler.CodeGeneration;
using Lucent.Compiler.Parsing;
using Lucent.Compiler.Semantics;

namespace Lucent.Compiler;

public sealed record CompilationResult(
    CompilationUnitSyntax? Syntax,
    string? GeneratedSource,
    IReadOnlyList<LucentDiagnostic> Diagnostics)
{
    public IReadOnlyList<LucentSemanticSymbol> Symbols { get; init; } = [];

    public bool Succeeded =>
        GeneratedSource is not null &&
        Diagnostics.All(diagnostic =>
            diagnostic.Severity != LucentDiagnosticSeverity.Error);
}

internal sealed class ComponentSemanticAnalysis
{
    private ComponentSemanticAnalysis(
        CompilationUnitSyntax syntax,
        IReadOnlyList<LucentDiagnostic> diagnostics,
        string sourcePath,
        ProjectSemanticCompilation project,
        NativeSymbolResolver resolver,
        BoundComponentModel? model,
        IReadOnlyList<LucentSemanticSymbol> symbols,
        IReadOnlyList<BoundIslandScope> editorScopes)
    {
        Syntax = syntax;
        Diagnostics = diagnostics;
        SourcePath = sourcePath;
        Project = project;
        Resolver = resolver;
        Model = model;
        Symbols = symbols;
        EditorScopes = editorScopes;
    }

    public CompilationUnitSyntax Syntax { get; }
    public IReadOnlyList<LucentDiagnostic> Diagnostics { get; }
    public string SourcePath { get; }
    public ProjectSemanticCompilation Project { get; }
    public NativeSymbolResolver Resolver { get; }
    public BoundComponentModel? Model { get; }
    public IReadOnlyList<LucentSemanticSymbol> Symbols { get; }
    public IReadOnlyList<BoundIslandScope> EditorScopes { get; }

    public static ComponentSemanticAnalysis Create(
        string sourceText,
        string sourcePath,
        LucentProjectContext? projectContext)
    {
        var parser = new Parser(sourceText, sourcePath);
        var syntax = parser.Parse();
        var diagnostics = parser.Diagnostics.ToList();
        var project = new ProjectSemanticCompilation(
            syntax.NamespaceName,
            syntax.AllUsings.Select(directive => directive.Text).ToArray(),
            projectContext);
        var resolver = new NativeSymbolResolver(project);
        BoundComponentModel? model = null;
        IReadOnlyList<LucentSemanticSymbol> symbols = [];
        IReadOnlyList<BoundIslandScope> editorScopes = [];
        if (!diagnostics.Any(item => item.Severity == LucentDiagnosticSeverity.Error))
        {
            var bag = new DiagnosticBag(new SourceDocument(sourceText, sourcePath));
            var binder = new GeneralBinder(bag, projectContext, project);
            model = binder.Bind(syntax);
            diagnostics.AddRange(bag.Items);
            symbols = binder.Symbols;
            editorScopes = binder.EditorScopes;
        }

        return new ComponentSemanticAnalysis(
            syntax, diagnostics, sourcePath, project, resolver, model, symbols, editorScopes);
    }
}

internal sealed record BoundIslandScope(
    SourceSpan Span,
    IReadOnlyList<BoundLocal> Locals);
