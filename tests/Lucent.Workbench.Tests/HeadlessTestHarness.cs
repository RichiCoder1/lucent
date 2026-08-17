using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

[assembly: DoNotParallelize]

namespace Lucent.Workbench.Tests;

[TestClass]
public sealed class HeadlessTestHarness
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static Thread? _thread;
    private static CancellationTokenSource? _stop;
    private static TaskCompletionSource _ready = null!;

    [AssemblyInitialize]
    public static void Start(TestContext _)
    {
        _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _stop = new CancellationTokenSource();
        _thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<HeadlessTestApp>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                    .SetupWithoutStarting();
                _ready.SetResult();
                Dispatcher.UIThread.MainLoop(_stop.Token);
            }
            catch (Exception error)
            {
                _ready.TrySetException(error);
            }
        }) { IsBackground = true, Name = "Lucent Workbench headless UI" };
        if (OperatingSystem.IsWindows()) _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Task.WaitAsync(Timeout).GetAwaiter().GetResult();
    }

    [AssemblyCleanup]
    public static void Stop(TestContext _)
    {
        _stop?.Cancel();
        Dispatcher.UIThread.Post(() => { });
        if (_thread?.Join(Timeout) == false)
            throw new TimeoutException("The Avalonia headless UI thread did not stop.");
        _stop?.Dispose();
    }

    public static T Run<T>(Func<T> action)
    {
        var operation = Dispatcher.UIThread.InvokeAsync(action);
        operation.Wait(Timeout);
        if (operation.Status != DispatcherOperationStatus.Completed)
            throw new TimeoutException("The Avalonia UI operation did not complete.");
        return operation.GetAwaiter().GetResult();
    }

    public static void Run(Action action)
    {
        var operation = Dispatcher.UIThread.InvokeAsync(action);
        operation.Wait(Timeout);
        if (operation.Status != DispatcherOperationStatus.Completed)
            throw new TimeoutException("The Avalonia UI operation did not complete.");
        operation.GetAwaiter().GetResult();
    }

    public static async Task RunWindowAsync(Func<Window, Task> action)
    {
        var operation = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = new Window { Width = 800, Height = 600 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                await action(window);
                Dispatcher.UIThread.RunJobs();
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
        await operation.WaitAsync(Timeout);
    }

    public static async Task RunUiAsync(Func<Task> action)
    {
        var operation = Dispatcher.UIThread.InvokeAsync(action);
        await operation.WaitAsync(Timeout);
    }
}
