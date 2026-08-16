using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace Lucent.Examples.Todo;

internal sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var component = new MainWindowComponent();
            var roots = component.Mount();
            if (roots.Count != 1 || roots[0] is not Avalonia.Controls.Window window)
            {
                component.Dispose();
                throw new InvalidOperationException("MainWindow must mount exactly one Window root.");
            }
            window.Closed += (_, _) => component.Dispose();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
