using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Lucent.Poc;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        return args.Contains("--smoke-test", StringComparer.Ordinal)
            ? RunSmokeTest(args)
            : BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect();

    private static int RunSmokeTest(string[] args)
    {
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            Args = args,
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        BuildAvaloniaApp().SetupWithLifetime(lifetime);

        var window = lifetime.MainWindow
            ?? throw new InvalidOperationException(
                "Smoke test expected App to create the main window during setup.");

        var opened = false;
        var loadedTurn = false;
        var counterUpdated = false;
        var closed = false;
        var exited = false;

        lifetime.Exit += (_, _) =>
        {
            exited = true;
            MarkSmokeProgress("app-exit");
        };

        window.Opened += (_, _) =>
        {
            opened = true;
            MarkSmokeProgress("window-opened");

            Dispatcher.UIThread.Post(
                () =>
                {
                    loadedTurn = true;
                    MarkSmokeProgress("loaded-turn");

                    counterUpdated = ExerciseCounter(window);
                    if (counterUpdated)
                    {
                        MarkSmokeProgress("counter-updated");
                    }

                    window.Close();
                },
                DispatcherPriority.Loaded);
        };

        window.Closed += (_, _) =>
        {
            closed = true;
            MarkSmokeProgress("window-closed");
            lifetime.Shutdown(0);
        };

        var exitCode = lifetime.Start(args);
        if (exitCode != 0 ||
            !opened ||
            !loadedTurn ||
            !counterUpdated ||
            !closed ||
            !exited)
        {
            Console.Error.WriteLine(
                "SMOKE: failed " +
                $"exitCode={exitCode} opened={opened} loadedTurn={loadedTurn} " +
                $"counterUpdated={counterUpdated} closed={closed} exited={exited}");
            return 1;
        }

        return 0;
    }

    private static bool ExerciseCounter(Window window)
    {
        if (window.Content is not Border { Child: StackPanel counterRoot } ||
            counterRoot.Children.Count != 2 ||
            counterRoot.Children[0] is not TextBlock countText ||
            counterRoot.Children[1] is not Button incrementButton)
        {
            return false;
        }

        incrementButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return string.Equals(
            countText.Text,
            "Count: 1",
            StringComparison.Ordinal);
    }

    private static void MarkSmokeProgress(string marker)
    {
        Console.Error.WriteLine($"SMOKE: {marker}");
        Console.Error.Flush();
    }
}
