using Lucent.Examples.Workbench;

namespace Lucent.Workbench.Tests;

[TestClass]
public sealed class WorkspaceServiceTests
{
    [TestMethod]
    public async Task Real_workspace_tree_diagnostics_and_generated_preview_use_the_project_compiler()
    {
        var root = Path.Combine(Path.GetTempPath(), "lucent-workbench", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var project = Path.Combine(root, "Demo.csproj");
        var source = Path.Combine(root, "App.lui");
        try
        {
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><LucentSource Include=\"App.lui\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(source, "namespace Demo; component App() => TextBlock { Missing: true; };");
            var workspace = new WorkspaceService();
            await workspace.OpenAsync(root, CancellationToken.None);

            Assert.IsTrue(workspace.Roots.Single().Children.Any(node => node.Path == source));
            var errors = await workspace.LoadProblemsAsync(root, CancellationToken.None);
            Assert.IsTrue(errors.Any(problem => problem.Path == source));

            workspace.UpdateDocument(new OpenDocument(source,
                "namespace Demo; component App() => TextBlock { Text: \"updated\"; };"));
            var updated = await workspace.LoadProblemsAsync(root, CancellationToken.None);
            Assert.IsFalse(updated.Any(problem => problem.Severity == ProblemSeverity.Error));
            StringAssert.Contains(workspace.GeneratedSource, "updated");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task Activating_another_lui_without_edits_regenerates_its_preview()
    {
        await using var workspace = await TemporaryWorkspace.CreateAsync(
            ("One.lui", "namespace Demo; component One() => TextBlock { Text: \"one\"; };"),
            ("Two.lui", "namespace Demo; component Two() => TextBlock { Text: \"two\"; };"));
        var service = new WorkspaceService();
        await service.OpenAsync(workspace.Path, CancellationToken.None);
        var one = Path.Combine(workspace.Path, "One.lui");
        var two = Path.Combine(workspace.Path, "Two.lui");

        service.ActivateDocument(new OpenDocument(one, await service.ReadAsync(one, CancellationToken.None)));
        await service.LoadProblemsAsync(workspace.Path, CancellationToken.None);
        StringAssert.Contains(service.GeneratedSource, "one");
        service.ActivateDocument(new OpenDocument(two, await service.ReadAsync(two, CancellationToken.None)));
        await service.LoadProblemsAsync(workspace.Path, CancellationToken.None);
        StringAssert.Contains(service.GeneratedSource, "two");
        Assert.IsFalse(service.GeneratedSource.Contains("Text: \"one\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Tree_skips_a_reparse_directory_and_stops_at_its_depth_ceiling()
    {
        await using var workspace = await TemporaryWorkspace.CreateAsync(("App.lui", "namespace Demo; component App() => TextBlock {};"));
        var nested = workspace.Path;
        for (var depth = 0; depth <= WorkspaceService.MaxTreeDepth; depth++) nested = Directory.CreateDirectory(Path.Combine(nested, "deep" + depth)).FullName;
        await File.WriteAllTextAsync(Path.Combine(nested, "TooDeep.lui"), "namespace Demo; component TooDeep() => TextBlock {}; ");
        var target = Directory.CreateDirectory(Path.Combine(workspace.Path, "target")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(workspace.Path, "linked"), target);

        var service = new WorkspaceService();
        await service.OpenAsync(workspace.Path, CancellationToken.None);
        Assert.IsFalse(service.QuickOpenItems.Any(item => item.DisplayName == "TooDeep.lui"));
        Assert.IsFalse(service.Roots.Single().Children.Any(node => node.Name == "linked"));
    }

    [TestMethod]
    public async Task Cancellation_and_newer_generation_do_not_publish_stale_results()
    {
        await using var workspace = await TemporaryWorkspace.CreateAsync(("App.lui", "namespace Demo; component App() => TextBlock { Text: \"old\"; };"));
        var service = new WorkspaceService();
        await service.OpenAsync(workspace.Path, CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.LoadProblemsAsync(workspace.Path, cancelled.Token));

        var source = Path.Combine(workspace.Path, "App.lui");
        service.ActivateDocument(new OpenDocument(source, await service.ReadAsync(source, CancellationToken.None)));
        var stale = service.LoadProblemsAsync(workspace.Path, CancellationToken.None);
        service.UpdateDocument(new OpenDocument(source, "namespace Demo; component App() => TextBlock { Text: \"new\"; };"));
        Assert.AreEqual(0, (await stale).Count);
        Assert.IsFalse(service.GeneratedSource.Contains("old", StringComparison.Ordinal));
        await service.LoadProblemsAsync(workspace.Path, CancellationToken.None);
        StringAssert.Contains(service.GeneratedSource, "new");
    }

    [TestMethod]
    public async Task Newer_workspace_open_wins_when_an_older_read_finishes_later()
    {
        await using var older = await TemporaryWorkspace.CreateAsync(("App.lui", "namespace Demo; component App() => TextBlock {};"));
        await using var newer = await TemporaryWorkspace.CreateAsync(("App.lui", "namespace Demo; component App() => TextBlock {};"));
        var olderReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOlder = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new WorkspaceService(async (path, token) =>
        {
            if (Path.GetDirectoryName(path) == older.Path)
            {
                olderReadStarted.TrySetResult();
                await releaseOlder.Task.WaitAsync(token);
            }
            return await File.ReadAllTextAsync(path, token);
        });

        var first = service.OpenAsync(older.Path, CancellationToken.None);
        await olderReadStarted.Task;
        await service.OpenAsync(newer.Path, CancellationToken.None);
        releaseOlder.SetResult();
        await first;

        Assert.AreEqual(newer.Path, service.RootPath);
    }

    [TestMethod]
    public async Task Generated_preview_snapshot_maps_current_text_and_rejects_stale_or_end_boundary_offsets()
    {
        await using var workspace = await TemporaryWorkspace.CreateAsync(("App.lui", "namespace Demo;\ncomponent App() => TextBlock { Missing: true; };"));
        var service = new WorkspaceService();
        await service.OpenAsync(workspace.Path, CancellationToken.None);
        var problems = await service.LoadProblemsAsync(workspace.Path, CancellationToken.None);
        Assert.IsTrue(problems.Any(problem => problem.Column > 1 && problem.SpanLength > 0));

        service.UpdateDocument(new OpenDocument(Path.Combine(workspace.Path, "App.lui"), "namespace Demo;\ncomponent App() => TextBlock { Text: \"mapped\"; };"));
        await service.LoadProblemsAsync(workspace.Path, CancellationToken.None);
        var snapshot = service.CaptureGeneratedPreview();
        var entry = snapshot.SourceMap!.Entries.First();
        var offset = Offset(snapshot.Source, entry.GeneratedRange.StartLine, entry.GeneratedRange.StartCharacter);
        Assert.IsTrue(service.TryMapGeneratedOffset(snapshot, offset, out var path, out var line, out var column));
        Assert.AreEqual(Path.Combine(workspace.Path, "App.lui"), path);
        Assert.IsTrue(line >= 1 && column >= 1);
        var last = snapshot.SourceMap.Entries.MaxBy(candidate => Offset(snapshot.Source,
            candidate.GeneratedRange.EndLine, candidate.GeneratedRange.EndCharacter))!;
        var end = Offset(snapshot.Source, last.GeneratedRange.EndLine, last.GeneratedRange.EndCharacter);
        Assert.IsFalse(service.TryMapGeneratedOffset(snapshot, end, out _, out _, out _));

        service.UpdateDocument(new OpenDocument(Path.Combine(workspace.Path, "App.lui"), "namespace Demo; component App() => TextBlock {};"));
        Assert.IsFalse(service.TryMapGeneratedOffset(snapshot, offset, out _, out _, out _));
    }

    [TestMethod]
    public async Task Evaluated_project_sources_exclude_unrelated_physical_lui_files()
    {
        await using var workspace = await TemporaryWorkspace.CreateAsync(("Included.lui", "namespace Demo; component Included() => TextBlock {};"));
        var unrelated = Path.Combine(workspace.Path, "Unrelated.lui");
        await File.WriteAllTextAsync(unrelated, "namespace Demo; component Unrelated() => TextBlock { Missing: true; };");
        var service = new WorkspaceService();
        await service.OpenAsync(workspace.Path, CancellationToken.None);

        Assert.IsTrue(service.QuickOpenItems.All(item => item.Path != unrelated));
        Assert.IsFalse((await service.LoadProblemsAsync(workspace.Path, CancellationToken.None)).Any(problem => problem.Path == unrelated));
    }

    [TestMethod]
    public async Task App_restore_seam_opens_the_saved_workspace()
    {
        await using var workspace = await TemporaryWorkspace.CreateAsync(("App.lui", "namespace Demo; component App() => TextBlock {};"));
        var restored = new WorkspaceService();
        await App.RestoreWorkspaceAsync(restored, WorkbenchSettings.Defaults with { RecentWorkspace = workspace.Path }, CancellationToken.None);
        Assert.AreEqual(workspace.Path, restored.RootPath);
        Assert.AreEqual(1, restored.QuickOpenItems.Count);
    }

    [TestMethod]
    public async Task App_restore_reports_a_workspace_read_failure_without_aborting_startup()
    {
        await using var workspace = await TemporaryWorkspace.CreateAsync(("App.lui", "namespace Demo; component App() => TextBlock {};"));
        var failure = new IOException("simulated restore failure");
        var restored = new WorkspaceService((_, _) => Task.FromException<string>(failure));
        Exception? reported = null;

        await App.RestoreWorkspaceAsync(
            restored,
            WorkbenchSettings.Defaults with { RecentWorkspace = workspace.Path },
            CancellationToken.None,
            error => reported = error);

        Assert.AreSame(failure, reported);
        Assert.IsNull(restored.RootPath);
    }

    private static int Offset(string text, int line, int column)
    {
        var offset = 0;
        while (line-- > 0) offset = text.IndexOf('\n', offset) + 1;
        return offset + column;
    }

    private sealed class TemporaryWorkspace : IAsyncDisposable
    {
        private TemporaryWorkspace(string path) => Path = path;
        public string Path { get; }

        public static async Task<TemporaryWorkspace> CreateAsync(params (string Name, string Text)[] files)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lucent-workbench", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(System.IO.Path.Combine(path, "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup>" + string.Concat(files.Select(file => $"<LucentSource Include=\"{file.Name}\" />")) + "</ItemGroup></Project>");
            foreach (var file in files) await File.WriteAllTextAsync(System.IO.Path.Combine(path, file.Name), file.Text);
            return new TemporaryWorkspace(path);
        }

        public ValueTask DisposeAsync() { Directory.Delete(Path, recursive: true); return ValueTask.CompletedTask; }
    }
}
