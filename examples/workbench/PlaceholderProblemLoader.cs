namespace Lucent.Examples.Workbench;

internal sealed class ProjectProblemLoader(WorkspaceService workspace) : IProblemLoader
{
    public Task<IReadOnlyList<ProblemItem>> LoadAsync(string? workspacePath, CancellationToken cancellationToken) =>
        workspace.LoadProblemsAsync(workspacePath, cancellationToken);
}
