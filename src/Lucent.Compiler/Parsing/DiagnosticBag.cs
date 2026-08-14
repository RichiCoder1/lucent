namespace Lucent.Compiler.Parsing;

internal sealed class DiagnosticBag(SourceDocument source)
{
    private readonly List<LucentDiagnostic> _diagnostics = [];

    public IReadOnlyList<LucentDiagnostic> Items => _diagnostics;

    public void Add(string code, string message, SourceSpan span)
    {
        var (line, column) = source.GetLineAndColumn(span.Start);
        _diagnostics.Add(
            new LucentDiagnostic(
                code,
                LucentDiagnosticSeverity.Error,
                message,
                span,
                line,
                column));
    }
}
