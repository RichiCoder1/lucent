using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Lucent.Examples.Workbench;

namespace Lucent.Workbench.Tests;

[TestClass]
public sealed class DesktopHostTests
{
    private static Thread? _uiThread;
    private static TaskCompletionSource _uiReady = null!;

    [ClassInitialize]
    public static void StartAvalonia(TestContext _)
    {
        _uiReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiThread = new Thread(() =>
        {
            var lifetime = new ClassicDesktopStyleApplicationLifetime
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
            AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithLifetime(lifetime);
            _uiReady.SetResult();
            lifetime.Start(Array.Empty<string>());
        }) { IsBackground = true };
        if (OperatingSystem.IsWindows())
            _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
        _uiReady.Task.GetAwaiter().GetResult();
    }

    [ClassCleanup]
    public static void StopAvalonia()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                lifetime.Shutdown();
        });
        _uiThread?.Join(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task Fake_host_records_owner_explicit_operations()
    {
        var fake = new FakeHost();
        Window owner = null!;
        Window child = null!;

        fake.FolderGate.SetResult([]);
        fake.DialogGate.SetResult(true);
        await fake.PickWorkspaceAsync(owner);
        await fake.SetClipboardTextAsync(owner, "diagnostic");
        await fake.ShowDialogAsync<bool>(owner, child);
        fake.ShowOwnedWindow(owner, child);

        Assert.AreEqual(6, fake.Owners.Count);
        Assert.AreEqual("diagnostic", fake.ClipboardText);
    }

    [TestMethod]
    public async Task Fake_host_preserves_faults_and_completion_after_cancellation()
    {
        var fake = new FakeHost();
        Window owner = null!;
        using var cancellation = new CancellationTokenSource();
        var pending = fake.PickWorkspaceAsync(owner);
        cancellation.Cancel();
        fake.FolderGate.SetResult([]);

        var folders = await pending;
        Assert.AreEqual(0, folders.Count);
        Assert.AreEqual(1, fake.FolderCalls);

        fake.Fault = new InvalidOperationException("picker failed");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fake.PickWorkspaceAsync(owner));
    }

    [TestMethod]
    public async Task Mounted_workbench_commands_use_real_owner_and_shared_presentations()
    {
        var host = new FakeHost();
        host.FolderGate.SetResult([]);
        host.DialogGate.SetResult(true);
        using var lifetime = new CancellationTokenSource();
        var component = RunOnUiThread(() => new WorkbenchAppComponent(host, lifetime.Token));
        var window = RunOnUiThread(() =>
        {
            var mounted = (Window)component.Mount().Roots.Single();
            mounted.RaiseEvent(new RoutedEventArgs(Control.LoadedEvent));
            return mounted;
        });

        var openCommand = RunOnUiThread(() => window.KeyBindings.Single(binding =>
            binding.Gesture is KeyGesture { Key: Key.O }).Command!);
        RunOnUiThread(() => openCommand.Execute(null));
        await Task.Delay(50);
        Assert.AreSame(window, host.LastOwner);
        Assert.AreEqual(1, host.FolderCalls);

        var menuCommand = RunOnUiThread(() => Find((Control)window.Content!, control => control is MenuItem item && item.Header?.ToString() == "Open workspace")
            .Cast<MenuItem>().Single().Command!);
        Assert.AreSame(openCommand, menuCommand);
        Assert.AreEqual(RunOnUiThread(() => openCommand.CanExecute(null)),
            RunOnUiThread(() => menuCommand.CanExecute(null)));

        var settingsCommand = RunOnUiThread(() => Find((Control)window.Content!, control => control is MenuItem item && item.Header?.ToString() == "Settings")
            .Cast<MenuItem>().Single().Command!);
        RunOnUiThread(() => settingsCommand.Execute(null));
        await Task.Delay(50);
        Assert.AreSame(window, host.LastOwner);
        Assert.IsNotNull(host.LastDialog);
        Assert.AreEqual("Settings", RunOnUiThread(() => host.LastDialog!.Title));

        var previewCommand = RunOnUiThread(() => Find((Control)window.Content!, control => control is Button button && button.Content?.ToString() == "Generated preview")
            .Cast<Button>().Single().Command!);
        RunOnUiThread(() => previewCommand.Execute(null));
        Assert.AreSame(window, host.LastOwner);
        Assert.IsNotNull(host.LastChild);
        Assert.AreEqual("Generated C# preview", RunOnUiThread(() => host.LastChild!.Title));
        RunOnUiThread(component.Dispose);
    }

    [TestMethod]
    public async Task Mounted_workbench_suppresses_cancelled_continuations_and_observes_live_faults()
    {
        var host = new FakeHost();
        using var lifetime = new CancellationTokenSource();
        var component = RunOnUiThread(() => new WorkbenchAppComponent(host, lifetime.Token));
        var window = RunOnUiThread(() =>
        {
            var mounted = (Window)component.Mount().Roots.Single();
            mounted.RaiseEvent(new RoutedEventArgs(Control.LoadedEvent));
            return mounted;
        });
        var open = RunOnUiThread(() => window.KeyBindings.Single(binding =>
            binding.Gesture is KeyGesture { Key: Key.O }).Command!);

        RunOnUiThread(() => open.Execute(null));
        lifetime.Cancel();
        host.FolderGate.SetResult([]);
        await Task.Delay(100);
        Assert.AreEqual(1, host.FolderCalls);
        Assert.IsFalse(RunOnUiThread(() => Find(window, control => control is TextBlock text &&
            text.Text == "picker failed").Any()));

        using var liveLifetime = new CancellationTokenSource();
        host = new FakeHost { Fault = new InvalidOperationException("picker failed") };
        var liveComponent = RunOnUiThread(() => new WorkbenchAppComponent(host, liveLifetime.Token));
        var liveWindow = RunOnUiThread(() =>
        {
            var mounted = (Window)liveComponent.Mount().Roots.Single();
            mounted.RaiseEvent(new RoutedEventArgs(Control.LoadedEvent));
            return mounted;
        });
        RunOnUiThread(() => liveWindow.KeyBindings.Single(binding =>
            binding.Gesture is KeyGesture { Key: Key.O }).Command!.Execute(null));
        await Task.Delay(100);
        Assert.IsTrue(RunOnUiThread(() => Find(liveWindow, control => control is TextBlock text &&
            text.Text == "picker failed").Any()));
        RunOnUiThread(component.Dispose);
        RunOnUiThread(liveComponent.Dispose);
    }

    [TestMethod]
    public async Task Generated_windows_follow_modal_modeless_and_failure_lifetimes()
    {
        using var lifetime = new CancellationTokenSource();
        var modalHost = new FakeHost();
        var modalComponent = RunOnUiThread(() => new WorkbenchAppComponent(modalHost, lifetime.Token));
        var modalOwner = MountWorkbench(modalComponent);
        Execute(modalOwner, control => control is MenuItem { Header: "Settings" });
        await Task.Delay(50);
        AssertGeneratedHandler(modalHost.LastDialog!, "Settings", attached: true);
        modalHost.DialogGate.SetResult(true);
        await Task.Delay(50);
        AssertGeneratedHandler(modalHost.LastDialog!, "Settings", attached: false);
        RunOnUiThread(modalComponent.Dispose);

        var failedModalHost = new FakeHost();
        var failedModalComponent = RunOnUiThread(() =>
            new WorkbenchAppComponent(failedModalHost, lifetime.Token));
        var failedModalOwner = MountWorkbench(failedModalComponent);
        Execute(failedModalOwner, control => control is MenuItem { Header: "Settings" });
        await Task.Delay(50);
        failedModalHost.DialogGate.SetException(new InvalidOperationException("dialog failed"));
        await Task.Delay(50);
        AssertGeneratedHandler(failedModalHost.LastDialog!, "Settings", attached: false);
        RunOnUiThread(failedModalComponent.Dispose);

        var modelessHost = new FakeHost { ShowWindows = true };
        var modelessComponent = RunOnUiThread(() =>
            new WorkbenchAppComponent(modelessHost, lifetime.Token));
        var modelessOwner = MountWorkbench(modelessComponent);
        Execute(modelessOwner, control => control is Button { Content: "Generated preview" });
        var preview = modelessHost.LastChild!;
        EventHandler<WindowClosingEventArgs> cancel = (_, eventArgs) => eventArgs.Cancel = true;
        RunOnUiThread(() => preview.Closing += cancel);
        RunOnUiThread(preview.Close);
        AssertGeneratedHandler(preview, "Generated C# preview", attached: true);
        RunOnUiThread(() => preview.Closing -= cancel);
        RunOnUiThread(preview.Close);
        AssertGeneratedHandler(preview, "Generated C# preview", attached: false);
        RunOnUiThread(preview.Close);
        RunOnUiThread(modelessComponent.Dispose);

        var failedModelessHost = new FakeHost
        {
            ShowWindowFault = new InvalidOperationException("show failed"),
        };
        var failedModelessComponent = RunOnUiThread(() =>
            new WorkbenchAppComponent(failedModelessHost, lifetime.Token));
        var failedModelessOwner = MountWorkbench(failedModelessComponent);
        Execute(failedModelessOwner, control => control is Button { Content: "Generated preview" });
        AssertGeneratedHandler(failedModelessHost.LastChild!, "Generated C# preview", attached: false);
        RunOnUiThread(failedModelessComponent.Dispose);
    }

    private static Window MountWorkbench(WorkbenchAppComponent component) => RunOnUiThread(() =>
    {
        var window = component.MountRoot();
        window.RaiseEvent(new RoutedEventArgs(Control.LoadedEvent));
        return window;
    });

    private static void Execute(Window owner, Func<Control, bool> predicate) =>
        RunOnUiThread(() =>
        {
            switch (Find(owner, predicate).Single())
            {
                case MenuItem menu: menu.Command!.Execute(null); break;
                case Button button: button.Command!.Execute(null); break;
                default: throw new InvalidOperationException();
            }
        });

    private static void AssertGeneratedHandler(Window window, string title, bool attached)
    {
        RunOnUiThread(() =>
        {
            window.Title = "changed";
            window.RaiseEvent(new RoutedEventArgs(Control.LoadedEvent));
        });
        Assert.AreEqual(attached ? title : "changed", RunOnUiThread(() => window.Title));
    }

    private static T RunOnUiThread<T>(Func<T> action) =>
        Dispatcher.UIThread.InvokeAsync(action).GetAwaiter().GetResult();

    private static void RunOnUiThread(Action action) =>
        Dispatcher.UIThread.InvokeAsync(action).GetAwaiter().GetResult();

    private static IEnumerable<Control> Find(Control root, Func<Control, bool> predicate)
    {
        var pending = new Stack<Control>([root]);
        var seen = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current)) continue;
            if (predicate(current)) yield return current;
            foreach (var child in current.GetVisualChildren().OfType<Control>())
                pending.Push(child);
            if (current is ILogical logical)
                foreach (var child in logical.LogicalChildren.OfType<Control>())
                    pending.Push(child);
        }
    }

    private sealed class FakeHost : IWorkbenchDesktopHost
    {
        public List<object> Owners { get; } = [];
        public string? ClipboardText { get; private set; }
        public int FolderCalls { get; private set; }
        public TaskCompletionSource<IReadOnlyList<IStorageFolder>> FolderGate { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? Fault { get; set; }
        public object? LastOwner { get; private set; }
        public Window? LastDialog { get; private set; }
        public Window? LastChild { get; private set; }
        public TaskCompletionSource<bool> DialogGate { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ShowWindows { get; init; }
        public Exception? ShowWindowFault { get; init; }
        public Task<IReadOnlyList<IStorageFolder>> PickWorkspaceAsync(Window owner)
        {
            FolderCalls++;
            LastOwner = owner;
            Owners.Add(owner);
            if (Fault is not null) return Task.FromException<IReadOnlyList<IStorageFolder>>(Fault);
            return FolderGate.Task;
        }
        public Task SetClipboardTextAsync(TopLevel owner, string text)
        {
            LastOwner = owner;
            Owners.Add(owner);
            ClipboardText = text;
            return Task.CompletedTask;
        }
        public Task<TResult> ShowDialogAsync<TResult>(Window owner, Window dialog)
        {
            LastOwner = owner;
            LastDialog = dialog;
            Owners.Add(owner);
            Owners.Add(dialog);
            return DialogGate.Task.ContinueWith(task => (TResult)(object)task.Result);
        }
        public void ShowOwnedWindow(Window owner, Window child)
        {
            LastOwner = owner;
            LastChild = child;
            Owners.Add(owner);
            Owners.Add(child);
            if (ShowWindowFault is not null) throw ShowWindowFault;
            if (ShowWindows) child.Show();
        }
    }
}
