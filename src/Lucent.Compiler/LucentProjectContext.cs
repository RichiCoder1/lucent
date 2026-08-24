namespace Lucent.Compiler;

public sealed record LucentProjectContext(
    string? ProjectPath = null,
    IReadOnlyList<string>? ReferencePaths = null,
    IReadOnlyList<string>? SourcePaths = null,
    IReadOnlyList<string>? LucentSourcePaths = null,
    IReadOnlyList<string>? GlobalUsingDirectives = null,
    string? TargetFramework = null,
    string? LanguageVersion = null,
    string? Nullable = null,
    string? DefineConstants = null,
    IReadOnlyList<string>? ProjectReferencePaths = null,
    string? TargetPath = null,
    IReadOnlyList<string>? GlobalStylePaths = null,
    string? RootNamespace = null)
{
    public IReadOnlyList<string> References => ReferencePaths ?? [];

    public IReadOnlyList<string> Sources => SourcePaths ?? [];
    public IReadOnlyList<string> LucentSources => LucentSourcePaths ?? [];
    public IReadOnlyList<string> GlobalStyles => GlobalStylePaths ?? [];
    public IReadOnlyList<string> GlobalUsings => GlobalUsingDirectives ?? [];
    public IReadOnlyList<string> ProjectReferences => ProjectReferencePaths ?? [];
    public IReadOnlyList<string> PreprocessorSymbols => (DefineConstants ?? string.Empty)
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
