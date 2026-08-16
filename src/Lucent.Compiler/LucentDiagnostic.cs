namespace Lucent.Compiler;

public enum LucentDiagnosticSeverity
{
    Warning,
    Error,
}

public sealed record LucentDiagnostic(
    string Code,
    LucentDiagnosticSeverity Severity,
    string Message,
    SourceSpan Span,
    int Line,
    int Column,
    string? SourcePath = null);
