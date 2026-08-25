using Avalonia.Controls;
using Lucent.Examples.Workbench;

namespace Lucent.Workbench.Tests;

internal static class WorkbenchTestFactory
{
    internal static WorkbenchAppComponent Create(
        IWorkbenchDesktopHost host,
        CancellationToken lifetime,
        WorkbenchSettings? settings = null,
        IProblemLoader? loader = null,
        DocumentSession? session = null,
        Action<Exception>? reporter = null,
        SettingsSaveCoordinator? saveCoordinator = null,
        WorkspaceService? workspace = null)
    {
        reporter ??= _ => { };
        session ??= new DocumentSession(new OpenDocument("Program.cs", "// Workbench document\n"), () => { });
        saveCoordinator ??= new SettingsSaveCoordinator(new TestSettingsRepository(), lifetime);
        workspace ??= new WorkspaceService();
        return new WorkbenchAppComponent(host, lifetime, reporter, settings ?? WorkbenchSettings.Defaults,
            saveCoordinator, loader ?? new TestProblemLoader(), session, workspace,
            __lucent_reportUnhandled: reporter);
    }

    private sealed class TestSettingsRepository : ISettingsRepository
    {
        public Task<WorkbenchSettings> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(WorkbenchSettings.Defaults);

        public Task SaveAsync(WorkbenchSettings value, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestProblemLoader : IProblemLoader
    {
        public Task<IReadOnlyList<ProblemItem>> LoadAsync(string? workspace, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProblemItem>>([
                new ProblemItem("one", "WorkbenchApp.lui", 1, "first", ProblemSeverity.Warning),
                new ProblemItem("two", "DocumentPane.lui", 2, "second", ProblemSeverity.Warning),
                new ProblemItem("three", "WorkspaceSidebar.lui", 3, "third", ProblemSeverity.Error)]);
    }
}
