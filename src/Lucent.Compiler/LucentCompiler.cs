using Lucent.Compiler.CodeGeneration;
using Lucent.Compiler.Parsing;
using Lucent.Compiler.Styling;

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
        return EditorIntelligence.GetExpressionSymbol(
            sourceText,
            offset,
            sourcePath,
            projectContext);
    }

    public static IReadOnlyList<LucentCompletionItem> GetCompletions(
        string sourceText,
        int offset,
        string sourcePath = "<memory>",
        LucentProjectContext? projectContext = null)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        return EditorIntelligence.GetCompletions(
            sourceText,
            offset,
            sourcePath,
            projectContext);
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

        var parser = new Parser(sourceText, sourcePath);
        var syntax = parser.Parse();
        var diagnostics = parser.Diagnostics.ToList();

        if (HasErrors(diagnostics))
        {
            return new CompilationResult(syntax, null, diagnostics);
        }

        var styles = BoundStyleSheet.Empty;
        if (styleText is not null)
        {
            var parsedStyles = StyleSheetParser.Parse(
                styleText,
                stylePath ?? Path.ChangeExtension(sourcePath, ".css"));
            styles = parsedStyles.Sheet;
            diagnostics.AddRange(parsedStyles.Diagnostics);
        }

        var source = new SourceDocument(sourceText, sourcePath);
        var emissionDiagnostics = new DiagnosticBag(source);
        var binder = new GeneralBinder(emissionDiagnostics, projectContext);
        var model = binder.Bind(syntax);
        string? generatedSource = null;
        if (model is not null && !HasErrors(diagnostics))
        {
            try
            {
                generatedSource = GeneralCSharpEmitter.Emit(
                    model,
                    sourcePath,
                    sourceText,
                    styles);
            }
            catch (InvalidOperationException exception) when (styleText is not null)
            {
                diagnostics.Add(new LucentDiagnostic(
                    "LUC4001",
                    LucentDiagnosticSeverity.Error,
                    exception.Message,
                    new SourceSpan(0, 1),
                    1,
                    1,
                    stylePath ?? Path.ChangeExtension(sourcePath, ".css")));
            }
        }

        diagnostics.AddRange(emissionDiagnostics.Items);
        if (generatedSource is null || HasErrors(diagnostics))
        {
            return new CompilationResult(syntax, null, diagnostics)
            {
                Symbols = binder.Symbols,
            };
        }

        return new CompilationResult(syntax, generatedSource, diagnostics)
        {
            Symbols = binder.Symbols,
        };
    }
    private static bool HasErrors(IEnumerable<LucentDiagnostic> diagnostics) =>
        diagnostics.Any(diagnostic =>
            diagnostic.Severity == LucentDiagnosticSeverity.Error);
}
