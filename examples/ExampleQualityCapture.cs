using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Lucent.Examples;

// Quality evidence stays outside the sample implementation paths.
internal static class ExampleQualityCapture
{
    public static string? Profile(string[] args, params string[] allowed)
    {
        var index = Array.IndexOf(args, "--quality-capture");
        var profile = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        return profile is not null && allowed.Contains(profile, StringComparer.Ordinal) ? profile : null;
    }

    public static void ApplyIcon<TAnchor>(Window window)
    {
        using var stream = AssetLoader.Open(new Uri(
            $"avares://{typeof(TAnchor).Assembly.GetName().Name}/Assets/lucent-icon-32.png"));
        window.Icon = new WindowIcon(stream);
    }

    public static void Configure(
        Application app,
        IClassicDesktopStyleApplicationLifetime desktop,
        Window window,
        string profile,
        PixelSize size,
        TimeSpan delay,
        Action<Window>? prepare = null)
    {
        window.Width = size.Width;
        window.Height = size.Height;
        app.RequestedThemeVariant = profile.Contains("dark", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
        window.Opened += (_, _) => DispatcherTimer.RunOnce(() =>
        {
            prepare?.Invoke(window);
            window.UpdateLayout();
            var path = Path.Combine("docs", "quality", "007-example-ux", "captures", profile + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var bitmap = new RenderTargetBitmap(size);
            bitmap.Render(window);
            bitmap.Save(path, new PngBitmapEncoderOptions());
            desktop.Shutdown(0);
        }, delay, DispatcherPriority.Render);
    }

    public static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.GetVisualChildren().OfType<Control>())
            foreach (var nested in Descendants(child))
                yield return nested;
    }
}
