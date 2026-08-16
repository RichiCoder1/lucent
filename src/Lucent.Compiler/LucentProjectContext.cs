namespace Lucent.Compiler;

public sealed record LucentProjectContext(
    string? ProjectPath = null,
    IReadOnlyList<string>? ReferencePaths = null,
    IReadOnlyList<string>? SourcePaths = null,
    IReadOnlyList<string>? LucentSourcePaths = null)
{
    public IReadOnlyList<string> References => ReferencePaths ?? [];

    public IReadOnlyList<string> Sources => SourcePaths ?? [];
    public IReadOnlyList<string> LucentSources => LucentSourcePaths ?? [];
}
