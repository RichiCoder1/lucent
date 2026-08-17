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
        SettingsSaveCoordinator? saveCoordinator = null)
    {
        reporter ??= _ => { };
        session ??= new DocumentSession(new OpenDocument("Program.cs", "// Workbench document\n"), () => { });
        saveCoordinator ??= new SettingsSaveCoordinator(new TestSettingsRepository(), lifetime);
        return new WorkbenchAppComponent(host, lifetime, reporter, settings ?? WorkbenchSettings.Defaults,
            saveCoordinator, loader ?? new PlaceholderProblemLoader(), session,
            __lucent_reportUnhandled: reporter);
    }

    private sealed class TestSettingsRepository : ISettingsRepository
    {
        public Task<WorkbenchSettings> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(WorkbenchSettings.Defaults);

        public Task SaveAsync(WorkbenchSettings value, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
