using Lucent.Compiler.Syntax;

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
