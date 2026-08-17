namespace Lucent.Examples.Workbench;

internal sealed class PlaceholderProblemLoader : IProblemLoader
{
    public Task<IReadOnlyList<ProblemItem>> LoadAsync(string? workspace, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProblemItem>>([
            new ProblemItem("problem-1", "WorkbenchApp.lui", 1, "Example diagnostic", ProblemSeverity.Warning),
            new ProblemItem("problem-2", "DocumentPane.lui", 1, "Example diagnostic", ProblemSeverity.Info),
            new ProblemItem("problem-3", "WorkspaceSidebar.lui", 1, "Example diagnostic", ProblemSeverity.Error),
        ]);
}
