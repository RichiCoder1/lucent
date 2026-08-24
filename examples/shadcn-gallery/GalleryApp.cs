using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Lucent.Themes.Shadcn;

namespace ShadcnGallery;

internal sealed class GalleryApp(bool dark) : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        Styles.Add(new FluentTheme());
        Styles.Add(new ShadcnTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new GalleryWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
