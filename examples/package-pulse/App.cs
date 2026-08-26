using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Lucent.Examples;

namespace Lucent.Examples.PackagePulse;

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
            var profile = ExampleQualityCapture.Profile(desktop.Args ?? [], "pulse-dark-results", "pulse-light-error");
            var component = new MainWindowComponent(profile == "pulse-light-error" ? "fail" : "lucent");
            var window = component.MountRoot();
            window.Closed += (_, _) => component.Dispose();
            ExampleQualityCapture.ApplyIcon<App>(window);
            if (profile is not null)
            {
                var size = profile.Contains("error", StringComparison.OrdinalIgnoreCase)
                    ? new PixelSize(600, 560)
                    : new PixelSize(820, 760);
                ExampleQualityCapture.Configure(this, desktop, window, profile, size, TimeSpan.FromMilliseconds(1200));
            }
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
