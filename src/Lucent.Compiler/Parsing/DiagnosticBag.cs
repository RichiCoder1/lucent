namespace Lucent.Compiler.Parsing;

internal sealed class DiagnosticBag(SourceDocument source)
{
    private readonly List<LucentDiagnostic> _diagnostics = [];

    public IReadOnlyList<LucentDiagnostic> Items => _diagnostics;

    public void Add(string code, string message, SourceSpan span)
        => Add(code, message, span, source.Path);

    public void Add(string code, string message, SourceSpan span, string sourcePath)
    {
        var (line, column) = source.GetLineAndColumn(span.Start);
        _diagnostics.Add(
            new LucentDiagnostic(
                code,
                LucentDiagnosticSeverity.Error,
                message,
                span,
                line,
                column,
                sourcePath));
    }

    public void AddExternal(string code, string message, SourceSpan span,
        string sourcePath, string sourceText)
    {
        var location = new SourceDocument(sourceText, sourcePath);
        var (line, column) = location.GetLineAndColumn(span.Start);
        _diagnostics.Add(new LucentDiagnostic(code, LucentDiagnosticSeverity.Error,
            message, span, line, column, sourcePath));
    }
}
