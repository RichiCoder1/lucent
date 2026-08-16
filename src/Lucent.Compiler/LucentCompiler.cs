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
        var analysis = ComponentSemanticAnalysis.Create(sourceText, sourcePath, projectContext);
        return EditorIntelligence.GetCompletions(sourceText, offset, analysis);
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

        var analysis = ComponentSemanticAnalysis.Create(sourceText, sourcePath, projectContext);
        var syntax = analysis.Syntax;
        var diagnostics = analysis.Diagnostics.ToList();

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

        var model = analysis.Model;
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

        if (generatedSource is null || HasErrors(diagnostics))
        {
            return new CompilationResult(syntax, null, diagnostics)
            {
                Symbols = analysis.Symbols,
            };
        }

        return new CompilationResult(syntax, generatedSource, diagnostics)
        {
            Symbols = analysis.Symbols,
        };
    }
    private static bool HasErrors(IEnumerable<LucentDiagnostic> diagnostics) =>
        diagnostics.Any(diagnostic =>
            diagnostic.Severity == LucentDiagnosticSeverity.Error);
}
