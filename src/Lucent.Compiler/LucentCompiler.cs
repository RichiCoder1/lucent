using Lucent.Compiler.CodeGeneration;
using Lucent.Compiler.Parsing;

namespace Lucent.Compiler;

public static class LucentCompiler
{
    public static CompilationResult Compile(
        string sourceText,
        string sourcePath = "<memory>")
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

        var source = new SourceDocument(sourceText, sourcePath);
        var emissionDiagnostics = new DiagnosticBag(source);
        var model = new GeneralBinder(emissionDiagnostics).Bind(syntax);
        var generatedSource = model is null
            ? null
            : GeneralCSharpEmitter.Emit(model, sourcePath, sourceText);

        diagnostics.AddRange(emissionDiagnostics.Items);
        if (generatedSource is null || HasErrors(diagnostics))
        {
            return new CompilationResult(syntax, null, diagnostics);
        }

        return new CompilationResult(syntax, generatedSource, diagnostics);
    }
    private static bool HasErrors(IEnumerable<LucentDiagnostic> diagnostics) =>
        diagnostics.Any(diagnostic =>
            diagnostic.Severity == LucentDiagnosticSeverity.Error);
}
