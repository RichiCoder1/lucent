namespace Lucent.Compiler.Tests;

internal static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    public static string CounterSource =>
        Path.Combine(Root, "examples", "counter", "Counter.lui");

    public static string CounterStyle =>
        Path.Combine(Root, "examples", "counter", "Counter.css");

    public static string GeneratedCounter =>
        Path.Combine(
            Root,
            "src",
            "Lucent.Poc",
            "Generated",
            "CounterComponent.g.cs");

    public static string ConditionalSnapshotSource =>
        Path.Combine(Root, "tests", "Lucent.Compiler.Tests", "Snapshots", "ConditionalRegion.lui");

    public static string GeneratedConditionalSnapshot =>
        Path.Combine(Root, "tests", "Lucent.Compiler.Tests", "Snapshots", "ConditionalRegion.g.cs.snap");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Lucent.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root from the test output directory.");
    }
}
