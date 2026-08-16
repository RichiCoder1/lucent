namespace Lucent.Compiler;

public sealed record LucentSourceCompilation(
    string SourcePath,
    CompilationResult Result);

public sealed record LucentProjectCompilationResult(
    IReadOnlyList<LucentSourceCompilation> Sources)
{
    public bool Succeeded => Sources.Count > 0 && Sources.All(source => source.Result.Succeeded);
}
