using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Lucent.Examples;

namespace Lucent.Examples.Counter;

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
            var profile = ExampleQualityCapture.Profile(desktop.Args ?? [], "counter-light", "counter-dark-focus");
            var component = new MainWindowComponent(profile == "counter-light" ? 3 : 0);
            var window = component.MountRoot();
            window.Closed += (_, _) => component.Dispose();
            ExampleQualityCapture.ApplyIcon<App>(window);
            if (profile is not null)
                ExampleQualityCapture.Configure(this, desktop, window, profile, new PixelSize(420, 300),
                    TimeSpan.FromMilliseconds(100), candidate =>
                    {
                        if (profile.Contains("focus", StringComparison.OrdinalIgnoreCase))
                            ExampleQualityCapture.Descendants(candidate).OfType<Avalonia.Controls.Button>().FirstOrDefault()?.Focus();
                    });
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
