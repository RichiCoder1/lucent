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
        var model = new CounterBinder(emissionDiagnostics).Bind(syntax);
        diagnostics.AddRange(emissionDiagnostics.Items);

        if (model is null || HasErrors(diagnostics))
        {
            return new CompilationResult(syntax, null, diagnostics);
        }

        return new CompilationResult(
            syntax,
            CSharpEmitter.Emit(model, sourcePath),
            diagnostics);
    }

    private static bool HasErrors(IEnumerable<LucentDiagnostic> diagnostics) =>
        diagnostics.Any(diagnostic =>
            diagnostic.Severity == LucentDiagnosticSeverity.Error);
}
