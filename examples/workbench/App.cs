using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Lucent.Examples.Workbench;

internal sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var component = new WorkbenchAppComponent();
            var roots = component.Mount();
            var window = roots.Count == 1 && roots[0] is Window root
                ? root
                : throw new InvalidOperationException("WorkbenchApp must mount exactly one Window root.");
            window.Closed += (_, _) => component.Dispose();
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }

    internal static int RunSmoke(string[] args)
    {
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            Args = args,
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        Program.BuildAvaloniaApp().SetupWithLifetime(lifetime);
        var component = new WorkbenchAppComponent();
        var roots = component.Mount();
        if (roots.Count != 1 || roots[0] is not Window window) return 1;
        lifetime.MainWindow = window;
        var passed = false;
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            var buttons = Descendants(window).OfType<Button>().ToArray();
            var before = Descendants(window).OfType<TextBlock>().Any(text => text.Text == "3 problems");
            buttons.First(button => button.Content?.ToString() == "Increment edits")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            buttons.First(button => button.Content?.ToString() == "Switch document")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            buttons.First(button => button.Content?.ToString() == "Toggle problems")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            passed = before && Descendants(window).OfType<TextBlock>().Any(text => text.Text == "Edits: 1") &&
                Descendants(window).OfType<TextBlock>().Any(text => text.Text == "README.md") &&
                !Descendants(window).OfType<TextBlock>().Any(text => text.Text == "3 problems");
            window.Close();
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "lucent-workbench-main.txt"), $"|passed:{passed}");
        }, DispatcherPriority.Loaded);
        window.Closed += (_, _) => { component.Dispose(); lifetime.Shutdown(passed ? 0 : 1); };
        return lifetime.Start(Array.Empty<string>());
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.GetVisualChildren().OfType<Control>())
            foreach (var nested in Descendants(child)) yield return nested;
    }
}
