using Lucent.Examples.Workbench;

namespace Lucent.Workbench.Tests;

[TestClass]
public sealed class SettingsRepositoryTests
{
    [TestMethod]
    public async Task Missing_and_invalid_settings_return_defaults_and_report_once()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var errors = 0;
        using var repository = new JsonFileSettingsRepository(path, _ => errors++);
        Assert.AreEqual(WorkbenchSettings.Defaults, await repository.LoadAsync(CancellationToken.None));
        await File.WriteAllTextAsync(path, "not json");
        Assert.AreEqual(WorkbenchSettings.Defaults, await repository.LoadAsync(CancellationToken.None));
        Assert.AreEqual(WorkbenchSettings.Defaults, await repository.LoadAsync(CancellationToken.None));
        Assert.AreEqual(1, errors);
    }

    [TestMethod]
    public async Task Save_round_trips_and_clamps_sidebar_width()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var path = Path.Combine(directory, "settings.json");
        using var repository = new JsonFileSettingsRepository(path, _ => { });
        await repository.SaveAsync(new WorkbenchSettings("repo", 9999, false, "Dark"), CancellationToken.None);
        var loaded = await repository.LoadAsync(CancellationToken.None);
        Assert.AreEqual("repo", loaded.RecentWorkspace);
        Assert.AreEqual(640, loaded.SidebarWidth);
        Assert.IsFalse(loaded.ProblemsVisible);
        Assert.AreEqual("Dark", loaded.Theme);
        Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [TestMethod]
    public async Task Pre_rename_commit_failure_preserves_previous_bytes_and_cleans_temp()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var path = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, "old-settings");
        using var repository = new JsonFileSettingsRepository(path, _ => { },
            (_, _) => throw new IOException("injected commit failure"));

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            repository.SaveAsync(new WorkbenchSettings("new", 320, false), CancellationToken.None));

        Assert.AreEqual("old-settings", await File.ReadAllTextAsync(path));
        Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [TestMethod]
    public async Task Save_coordinator_serializes_requests_and_exposes_tail()
    {
        var saves = new List<WorkbenchSettings>();
        using var repository = new RecordingRepository(saves);
        using var coordinator = new SettingsSaveCoordinator(repository, CancellationToken.None);

        coordinator.Save(new WorkbenchSettings("one", 280, true));
        coordinator.Save(new WorkbenchSettings("two", 300, false));
        await coordinator.Tail;

        Assert.HasCount(2, saves);
        Assert.AreEqual("two", saves[1].RecentWorkspace);
    }

    [TestMethod]
    public async Task Save_coordinator_retains_earlier_fault_while_later_save_runs()
    {
        var calls = 0;
        using var repository = new DelegateRepository((_, _) => ++calls == 1
            ? Task.FromException(new IOException("first save failed"))
            : Task.CompletedTask);
        using var coordinator = new SettingsSaveCoordinator(repository, CancellationToken.None);

        coordinator.Save(new WorkbenchSettings("one", 280, true));
        coordinator.Save(new WorkbenchSettings("two", 300, false));

        var error = await Assert.ThrowsExactlyAsync<IOException>(() => coordinator.Tail);
        Assert.AreEqual("first save failed", error.Message);
        Assert.AreEqual(2, calls);
    }

    private sealed class RecordingRepository(List<WorkbenchSettings> saves) : ISettingsRepository, IDisposable
    {
        public Task<WorkbenchSettings> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(WorkbenchSettings.Defaults);

        public Task SaveAsync(WorkbenchSettings value, CancellationToken cancellationToken)
        {
            saves.Add(value);
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }

    private sealed class DelegateRepository(
        Func<WorkbenchSettings, CancellationToken, Task> save) : ISettingsRepository, IDisposable
    {
        public Task<WorkbenchSettings> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(WorkbenchSettings.Defaults);

        public Task SaveAsync(WorkbenchSettings value, CancellationToken cancellationToken) =>
            save(value, cancellationToken);

        public void Dispose() { }
    }
}
