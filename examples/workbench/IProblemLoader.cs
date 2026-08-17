namespace Lucent.Examples.Workbench;

internal interface IProblemLoader
{
    Task<IReadOnlyList<ProblemItem>> LoadAsync(string? workspace, CancellationToken cancellationToken);
}
