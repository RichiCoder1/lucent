using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Automation;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Threading;

namespace Lucent.Examples.Workbench;

internal sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Resources["WorkbenchAccent"] = new SolidColorBrush(Colors.CornflowerBlue);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var lifetimeToken = new CancellationTokenSource();
#pragma warning disable LUC004A003 // event-owned modeless component is disposed by the native Closed handler below
            var component = new WorkbenchAppComponent(new AvaloniaWorkbenchDesktopHost(), lifetimeToken.Token);
            var window = component.MountRoot();
            window.Closed += (_, _) =>
            {
                lifetimeToken.Cancel();
                component.Dispose();
                lifetimeToken.Dispose();
            };
            desktop.MainWindow = window;
#pragma warning restore LUC004A003
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
        var lifetimeToken = new CancellationTokenSource();
#pragma warning disable LUC004A003 // event-owned smoke component is disposed by the native Closed handler below
        var component = new WorkbenchAppComponent(new AvaloniaWorkbenchDesktopHost(), lifetimeToken.Token);
        var window = component.MountRoot();
        lifetime.MainWindow = window;
        var passed = false;
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            var buttons = Descendants(window).OfType<Button>().ToArray();
            var before = Descendants(window).OfType<TextBlock>().Any(text => text.Text == "3 problems");
            var openBinding = window.KeyBindings.First(binding =>
                binding.Gesture is KeyGesture gesture && gesture.Key == Key.O);
            var paletteBinding = window.KeyBindings.First(binding =>
                binding.Gesture is KeyGesture gesture && gesture.Key == Key.K);
            var toggleBinding = window.KeyBindings.First(binding =>
                binding.Gesture is KeyGesture gesture && gesture.Key == Key.T);
            var editor = Descendants(window).OfType<TextBox>().First(textBox =>
                textBox.Text == "Static editor placeholder");
            editor.Focus();
            paletteBinding.Command!.Execute(null);
            var openMenu = Descendants(window).OfType<MenuItem>().First(item =>
                item.Header?.ToString() == "Open workspace");
            var sameCommand = ReferenceEquals(openBinding.Command, openMenu.Command);
            Descendants(window).OfType<Button>().First(button =>
                button.Content?.ToString() == "Close palette")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var focusRestored = editor.IsFocused;
            buttons.First(button => button.Content?.ToString() == "Increment edits")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            buttons.First(button => button.Content?.ToString() == "Switch document")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            toggleBinding.Command!.Execute(null);
            passed = before && Descendants(window).OfType<TextBlock>().Any(text => text.Text == "Edits: 1") &&
                Descendants(window).OfType<TextBlock>().Any(text => text.Text == "README.md") &&
                !Descendants(window).OfType<TextBlock>().Any(text => text.Text == "3 problems") &&
                sameCommand && focusRestored;
            window.Close();
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "lucent-workbench-main.txt"), $"|passed:{passed}");
        }, DispatcherPriority.Loaded);
        window.Closed += (_, _) =>
        {
            lifetimeToken.Cancel();
            component.Dispose();
            lifetimeToken.Dispose();
            lifetime.Shutdown(passed ? 0 : 1);
        };
#pragma warning restore LUC004A003
        return lifetime.Start(Array.Empty<string>());
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.GetVisualChildren().OfType<Control>())
            foreach (var nested in Descendants(child)) yield return nested;
    }
}
