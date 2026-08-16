namespace Lucent.Compiler;

public sealed record LucentSourceInput(
    string SourcePath,
    string SourceText,
    string? StylePath = null,
    string? StyleText = null);
