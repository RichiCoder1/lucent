using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucent.Examples.Workbench;
using System.Reflection;

namespace Lucent.Workbench.Tests;

[TestClass]
public sealed class WorkbenchWalkthroughTests
{
    [TestMethod]
    public async Task Temporary_project_walkthrough_uses_the_mounted_workbench()
    {
        var root = Path.Combine(Path.GetTempPath(), "lucent-workbench", Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(root, "settings.json");
        var app = Path.Combine(root, "App.lui");
        var broken = Path.Combine(root, "Broken.lui");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "Demo.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><LucentSource Include=\"App.lui\" /><LucentSource Include=\"Broken.lui\" /></ItemGroup></Project>");
        await File.WriteAllTextAsync(app, "namespace Demo;\ncomponent App() => TextBlock { Text: \"initial\"; };");
        await File.WriteAllTextAsync(broken, "namespace Demo;\ncomponent Broken() => TextBlock { Missing: true; };");

        using var repository = new JsonFileSettingsRepository(settingsPath, _ => { });
        using var lifetime = new CancellationTokenSource();
        using var saves = new SettingsSaveCoordinator(repository, lifetime.Token);
        var host = new FolderHost(root);
        var workspace = new WorkspaceService();
        var session = new DocumentSession(new OpenDocument("Program.cs", "// start\n"), () => { });
        WorkbenchAppComponent component = HeadlessTestHarness.Run(() => WorkbenchTestFactory.Create(host, lifetime.Token,
            loader: new ProjectProblemLoader(workspace), session: session, saveCoordinator: saves, workspace: workspace));

        try
        {
            await HeadlessTestHarness.RunUiAsync(async () =>
            {
                var window = component.MountRoot();
                try
                {
                    window.Show();
                    window.Activate();
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    window.KeyPress(Key.O, RawInputModifiers.Control, PhysicalKey.O, "o");
                    await DrainAsync(() => host.OpenCalls == 1);
                    await DrainAsync(() => FindText(window, "Workspace: " + Path.GetFileName(root)));

                    var tree = FindList<WorkspaceRow>(window);
                    tree.Focus();
                    for (var attempt = 0;
                         attempt < tree.ItemCount &&
                         (tree.SelectedItem is not WorkspaceRow { Node.Path: var path } || path != broken);
                         attempt++)
                    {
                        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
                        await DrainAsync(() => tree.SelectedItem is not null);
                    }
                    Assert.IsTrue(tree.SelectedItem is WorkspaceRow { Node.Path: var selectedPath } && selectedPath == broken);
                    await DrainAsync(() => session.Document.Path == broken);

                    window.KeyPress(Key.P, RawInputModifiers.Control, PhysicalKey.P, "p");
                    var quickOpen = FindList<QuickOpenItem>(window);
                    quickOpen.Focus();
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                    await DrainAsync(() => session.Document.Path == app);
                    Assert.AreEqual(Path.GetFullPath(app), session.Document.Path);

                    window.KeyPress(Key.E, RawInputModifiers.Control, PhysicalKey.E, "e");
                    var editor = window.GetVisualDescendants().OfType<AccessibleTextEditor>().Single();
                    editor.Focus();
                    window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
                    window.KeyTextInput("namespace Demo;\ncomponent App() => TextBlock { Text: \"current generation\"; };");
                    await DrainAsync(() => editor.Text.Contains("current generation", StringComparison.Ordinal));
                    await DrainAsync(() => FindText(window, "1 problems"));

                    var problems = FindList<ProblemItem>(window);
                    problems.Focus();
                    window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
                    await DrainAsync(() => problems.SelectedItem is ProblemItem);
                    await DrainAsync(() => session.Document.Path == broken && editor.Text.Contains("Missing", StringComparison.Ordinal));
                    Assert.AreEqual(2, editor.Document.GetLocation(editor.CaretOffset).Line);
                    Assert.AreEqual(((ProblemItem)problems.SelectedItem!).Column, editor.Document.GetLocation(editor.CaretOffset).Column);

                    window.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");
                    await DrainAsync(() => host.LastDialog is not null);
                    var dialog = host.LastDialog!;
                    dialog.GetVisualDescendants().OfType<Button>()
                        .Single(button => button.Content?.ToString() == "_Dark theme")
                        .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await DrainAsync(() => Application.Current!.RequestedThemeVariant == ThemeVariant.Dark);
                    host.CloseDialog();

                    window.KeyPress(Key.E, RawInputModifiers.Control, PhysicalKey.E, "e");
                    window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
                    window.KeyTextInput("namespace Demo;\ncomponent Broken() => TextBlock { Text: \"repaired generation\"; };");
                    await DrainAsync(() => FindText(window, "0 problems"));
                    window.KeyPress(Key.G, RawInputModifiers.Control, PhysicalKey.G, "g");
                    await DrainAsync(() => host.LastChild is not null);
                    var previewWindow = host.LastChild!;
                    var generated = previewWindow.GetVisualDescendants().OfType<TextBox>().Single();
                    StringAssert.Contains(generated.Text!, "repaired generation");
                    generated.CaretIndex = generated.Text!.IndexOf("repaired generation", StringComparison.Ordinal);
                    previewWindow.FocusManager!.Focus(generated, NavigationMethod.Tab, KeyModifiers.None);
                    previewWindow.KeyPress(Key.L, RawInputModifiers.Control, PhysicalKey.L, "l");
                    await DrainAsync(() => session.Document.Path == broken && editor.Document.GetLocation(editor.CaretOffset).Line == 2);

                    window.KeyPress(Key.W, RawInputModifiers.Control, PhysicalKey.W, "w");
                    await DrainAsync(() => FindText(window, "Workspace: No workspace") && FindList<WorkspaceRow>(window).Items.Count == 0);
                }
                finally
                {
                    if (window.IsVisible) window.Close();
                    Dispatcher.UIThread.RunJobs();
                }
            }, TimeSpan.FromSeconds(30));

            await saves.Tail;
            var persisted = await repository.LoadAsync(CancellationToken.None);
            Assert.AreEqual(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(persisted.RecentWorkspace!));
            Assert.AreEqual("Dark", persisted.Theme);

            var restoredWorkspace = new WorkspaceService();
            await restoredWorkspace.OpenAsync(persisted.RecentWorkspace!, CancellationToken.None);
            using var restoredSaves = new SettingsSaveCoordinator(repository, CancellationToken.None);
            var restoredSession = new DocumentSession(new OpenDocument("Program.cs", "// start\n"), () => { });
            var restored = HeadlessTestHarness.Run(() => WorkbenchTestFactory.Create(new FolderHost(root), CancellationToken.None,
                settings: persisted, loader: new ProjectProblemLoader(restoredWorkspace), session: restoredSession,
                saveCoordinator: restoredSaves, workspace: restoredWorkspace));
            await HeadlessTestHarness.RunUiAsync(async () =>
            {
                var window = restored.MountRoot();
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    await DrainAsync(() => FindText(window, "Workspace: " + Path.GetFileName(root)) && FindList<WorkspaceRow>(window).Items.Count > 0);
                }
                finally
                {
                    if (window.IsVisible) window.Close();
                    Dispatcher.UIThread.RunJobs();
                }
            });
            Run(restored.Dispose);
        }
        finally
        {
            Run(component.Dispose);
            for (var attempt = 0; ; attempt++)
            {
                try { Directory.Delete(root, recursive: true); break; }
                catch (IOException) when (attempt < 20) { await Task.Delay(50); }
            }
        }
    }

    [TestMethod]
    public async Task Settings_theme_shortcut_uses_physical_input_when_settings_is_the_input_root()
    {
        WorkbenchSettings? changed = null;
        var component = HeadlessTestHarness.Run(() => new SettingsDialogComponent(
            WorkbenchSettings.Defaults,
            value => changed = value,
            _ => { },
            __lucent_reportUnhandled: _ => { }));
        try
        {
            await HeadlessTestHarness.RunUiAsync(async () =>
            {
                var window = component.MountRoot();
                try
                {
                    window.Show();
                    window.Activate();
                    window.GetVisualDescendants().OfType<TextBox>().Single().Focus();
                    Dispatcher.UIThread.RunJobs();
                    window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, string.Empty);
                    await DrainAsync(() => changed?.Theme == "Dark");
                }
                finally
                {
                    if (window.IsVisible) window.Close();
                    Dispatcher.UIThread.RunJobs();
                }
            });
        }
        finally
        {
            Run(component.Dispose);
        }
    }

    private static ListBox FindList<T>(Window window) => window.GetVisualDescendants().OfType<ListBox>()
        .Single(list => list.ItemsSource is IEnumerable<T>);

    private static bool FindText(Window window, string value) => window.GetVisualDescendants().OfType<TextBlock>()
        .Any(text => text.Text == value);

    private static async Task DrainAsync(Func<bool> complete)
    {
        for (var attempt = 0; attempt < 100 && !complete(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.IsTrue(complete(), "The bounded UI drain did not complete.");
    }

    private static void Run(Action action) => HeadlessTestHarness.Run(action);

    private sealed class FolderHost(string path) : IWorkbenchDesktopHost
    {
        public int OpenCalls { get; private set; }
        public Window? LastDialog { get; private set; }
        public Window? LastChild { get; private set; }
        private TaskCompletionSource<bool> DialogGate { get; set; } = NewGate();

        public Task<IReadOnlyList<IStorageFolder>> PickWorkspaceAsync(Window owner)
        {
            OpenCalls++;
            var folder = DispatchProxy.Create<IStorageFolder, FolderProxy>();
            ((FolderProxy)(object)folder).Path = path;
            return Task.FromResult<IReadOnlyList<IStorageFolder>>([folder]);
        }

        public Task SetClipboardTextAsync(TopLevel owner, string text) => Task.CompletedTask;

        public Task<TResult> ShowDialogAsync<TResult>(Window owner, Window dialog)
        {
            LastDialog = dialog;
            dialog.Show(owner);
            dialog.Activate();
            return DialogGate.Task.ContinueWith(task => (TResult)(object)task.Result);
        }

        public void ShowOwnedWindow(Window owner, Window child)
        {
            LastChild = child;
            child.Show(owner);
        }

        public void CloseDialog()
        {
            LastDialog?.Close(true);
            DialogGate.TrySetResult(true);
            DialogGate = NewGate();
        }

        private static TaskCompletionSource<bool> NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private class FolderProxy : DispatchProxy
    {
        public string Path { get; set; } = "";

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "get_Name" => System.IO.Path.GetFileName(Path),
            "get_Path" => new Uri("file:///" + Path.Replace('\\', '/').TrimEnd('/') + "/"),
            "get_CanBookmark" => false,
            "Dispose" => null,
            _ => targetMethod?.ReturnType == typeof(Task) ? Task.CompletedTask : null,
        };
    }
}
