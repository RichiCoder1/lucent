using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Lucent.Examples;

namespace Lucent.Examples.Todo;

internal sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new Lucent.Themes.Shadcn.ShadcnTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var profile = ExampleQualityCapture.Profile(desktop.Args ?? [], "todo-light-populated", "todo-dark-empty");
            var component = new MainWindowComponent(profile == "todo-dark-empty"
                ? [new TodoItem(4, "Review the completed Lucent proof", true)]
                : null);
            var window = component.MountRoot();
            window.Closed += (_, _) => component.Dispose();
            ExampleQualityCapture.ApplyIcon<App>(window);
            if (profile is not null)
            {
                var size = profile.Contains("empty", StringComparison.OrdinalIgnoreCase)
                    ? new PixelSize(700, 560)
                    : new PixelSize(900, 760);
                ExampleQualityCapture.Configure(this, desktop, window, profile, size, TimeSpan.FromMilliseconds(100));
            }
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
