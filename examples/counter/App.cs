using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Platform;

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
            var qualityProfile = TryQualityProfile(desktop.Args ?? [], out var requestedProfile)
                ? requestedProfile : null;
            Environment.SetEnvironmentVariable("LUCENT_QUALITY_CAPTURE", qualityProfile);
            var component = new MainWindowComponent();
            var roots = component.Mount();
            if (roots.Count != 1 || roots[0] is not Avalonia.Controls.Window window)
            {
                component.Dispose();
                throw new InvalidOperationException("MainWindow must mount exactly one Window root.");
            }
            window.Closed += (_, _) => component.Dispose();
            ApplyWindowIcon(window);
            if (qualityProfile is not null)
            {
                SetQualitySize(window, qualityProfile);
                RequestedThemeVariant = qualityProfile.Contains("dark", StringComparison.OrdinalIgnoreCase)
                    ? ThemeVariant.Dark : ThemeVariant.Light;
                window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    window.UpdateLayout();
                    var control = Descendants(window).OfType<Button>().FirstOrDefault();
                    if (qualityProfile.Contains("focus", StringComparison.OrdinalIgnoreCase)) control?.Focus();
                    SaveCapture(window, qualityProfile);
                    desktop.Shutdown(0);
                }, DispatcherPriority.Render);
            }
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static bool TryQualityProfile(string[] args, out string profile)
    {
        var index = Array.IndexOf(args, "--quality-capture");
        profile = index >= 0 && index + 1 < args.Length ? args[index + 1] : string.Empty;
        return profile is "counter-light" or "counter-dark-focus";
    }

    private static void ApplyWindowIcon(Window window)
    {
        using var stream = AssetLoader.Open(new Uri(
            $"avares://{typeof(App).Assembly.GetName().Name}/Assets/lucent-icon-32.png"));
        window.Icon = new WindowIcon(stream);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.GetVisualChildren().OfType<Control>())
            foreach (var nested in Descendants(child)) yield return nested;
    }

    private static void SaveCapture(Window window, string profile)
    {
        var path = Path.Combine("docs", "quality", "007-example-ux", "captures", profile + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new RenderTargetBitmap(new PixelSize(
            Math.Max(1, (int)window.Bounds.Width), Math.Max(1, (int)window.Bounds.Height)));
        bitmap.Render(window);
        bitmap.Save(path, new PngBitmapEncoderOptions());
    }

    private static void SetQualitySize(Window window, string profile)
    {
        window.Width = 420;
        window.Height = 300;
    }
}
