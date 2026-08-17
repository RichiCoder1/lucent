using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Platform;
using System.Linq;

namespace Lucent.Examples.PackagePulse;

internal sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary
        {
            ["Lucent.Canvas"] = new SolidColorBrush(Color.Parse("#F2F0E9")), ["Lucent.Surface"] = new SolidColorBrush(Color.Parse("#FFFEFA")), ["Lucent.SurfaceRaised"] = new SolidColorBrush(Colors.White),
            ["Lucent.Text"] = new SolidColorBrush(Color.Parse("#101820")), ["Lucent.TextMuted"] = new SolidColorBrush(Color.Parse("#56636A")),
            ["Lucent.Border"] = new SolidColorBrush(Color.Parse("#C7CCC8")), ["Lucent.Accent"] = new SolidColorBrush(Color.Parse("#007A7B")),
            ["Lucent.AccentVivid"] = new SolidColorBrush(Color.Parse("#00A6A6")), ["Lucent.Signal"] = new SolidColorBrush(Color.Parse("#C23F45")), ["Lucent.Success"] = new SolidColorBrush(Color.Parse("#287A4B")), ["Lucent.Warning"] = new SolidColorBrush(Color.Parse("#8A6200")), ["Lucent.Danger"] = new SolidColorBrush(Color.Parse("#A52E34")),
            ["Lucent.Focus"] = new SolidColorBrush(Color.Parse("#007A7B")), ["Lucent.DurationFast"] = TimeSpan.FromMilliseconds(120), ["Lucent.DurationAlign"] = TimeSpan.FromMilliseconds(180),
            ["SystemControlFocusVisualPrimaryBrush"] = new SolidColorBrush(Color.Parse("#007A7B")), ["SystemControlFocusVisualSecondaryBrush"] = new SolidColorBrush(Color.Parse("#007A7B")), ["SystemControlFocusVisualMargin"] = new Thickness(2), ["SystemControlFocusVisualPrimaryThickness"] = new Thickness(2), ["SystemControlFocusVisualSecondaryThickness"] = new Thickness(0),
            ["Lucent.Space1"] = 4d, ["Lucent.Space2"] = 8d, ["Lucent.Space3"] = 12d, ["Lucent.Space4"] = 16d, ["Lucent.Space6"] = 24d, ["Lucent.Space8"] = 32d, ["Lucent.RadiusSm"] = new CornerRadius(2), ["Lucent.RadiusMd"] = new CornerRadius(4), ["Lucent.RadiusLg"] = new CornerRadius(8),
        };
        Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary
        {
            ["Lucent.Canvas"] = new SolidColorBrush(Color.Parse("#101820")), ["Lucent.Surface"] = new SolidColorBrush(Color.Parse("#17232C")), ["Lucent.SurfaceRaised"] = new SolidColorBrush(Color.Parse("#1E2E38")),
            ["Lucent.Text"] = new SolidColorBrush(Color.Parse("#F2F0E9")), ["Lucent.TextMuted"] = new SolidColorBrush(Color.Parse("#AAB6B6")),
            ["Lucent.Border"] = new SolidColorBrush(Color.Parse("#32444D")), ["Lucent.Accent"] = new SolidColorBrush(Color.Parse("#39C6C4")),
            ["Lucent.AccentVivid"] = new SolidColorBrush(Color.Parse("#39C6C4")), ["Lucent.Signal"] = new SolidColorBrush(Color.Parse("#FF8A72")), ["Lucent.Success"] = new SolidColorBrush(Color.Parse("#59C987")), ["Lucent.Warning"] = new SolidColorBrush(Color.Parse("#F2C14E")), ["Lucent.Danger"] = new SolidColorBrush(Color.Parse("#FF6670")),
            ["Lucent.Focus"] = new SolidColorBrush(Color.Parse("#39C6C4")), ["Lucent.DurationFast"] = TimeSpan.FromMilliseconds(120), ["Lucent.DurationAlign"] = TimeSpan.FromMilliseconds(180),
            ["SystemControlFocusVisualPrimaryBrush"] = new SolidColorBrush(Color.Parse("#39C6C4")), ["SystemControlFocusVisualSecondaryBrush"] = new SolidColorBrush(Color.Parse("#39C6C4")), ["SystemControlFocusVisualMargin"] = new Thickness(2), ["SystemControlFocusVisualPrimaryThickness"] = new Thickness(2), ["SystemControlFocusVisualSecondaryThickness"] = new Thickness(0),
            ["Lucent.Space1"] = 4d, ["Lucent.Space2"] = 8d, ["Lucent.Space3"] = 12d, ["Lucent.Space4"] = 16d, ["Lucent.Space6"] = 24d, ["Lucent.Space8"] = 32d, ["Lucent.RadiusSm"] = new CornerRadius(2), ["Lucent.RadiusMd"] = new CornerRadius(4), ["Lucent.RadiusLg"] = new CornerRadius(8),
        };
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
                RequestedThemeVariant = qualityProfile.Contains("dark", StringComparison.OrdinalIgnoreCase) ? ThemeVariant.Dark : ThemeVariant.Light;
                window.Opened += (_, _) =>
                {
                    if (qualityProfile == "pulse-light-error")
                    {
                        DispatcherTimer.RunOnce(() =>
                        {
                            var search = Descendants(window).OfType<TextBox>().FirstOrDefault();
                            if (search is not null) search.Text = "fail";
                            DispatcherTimer.RunOnce(() => CaptureAndExit(window, qualityProfile, desktop),
                                TimeSpan.FromMilliseconds(1100), DispatcherPriority.Render);
                        }, TimeSpan.FromMilliseconds(900), DispatcherPriority.Render);
                    }
                    else
                    {
                        DispatcherTimer.RunOnce(() => CaptureAndExit(window, qualityProfile, desktop),
                            TimeSpan.FromMilliseconds(1200), DispatcherPriority.Render);
                    }
                };
            }
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static bool TryQualityProfile(string[] args, out string profile)
    {
        var index = Array.IndexOf(args, "--quality-capture");
        profile = index >= 0 && index + 1 < args.Length ? args[index + 1] : string.Empty;
        return profile is "pulse-dark-results" or "pulse-light-error";
    }

    private static void ApplyWindowIcon(Window window)
    {
        using var stream = AssetLoader.Open(new Uri(
            $"avares://{typeof(App).Assembly.GetName().Name}/Assets/lucent-icon-32.png"));
        window.Icon = new WindowIcon(stream);
    }

    private static void SaveCapture(Window window, string profile)
    {
        var path = Path.Combine("docs", "quality", "007-example-ux", "captures", profile + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)window.Bounds.Width), Math.Max(1, (int)window.Bounds.Height)));
        bitmap.Render(window);
        bitmap.Save(path, new PngBitmapEncoderOptions());
    }

    private static void CaptureAndExit(Window window, string profile,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        window.UpdateLayout();
        SaveCapture(window, profile);
        desktop.Shutdown(0);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.GetVisualChildren().OfType<Control>())
            foreach (var nested in Descendants(child)) yield return nested;
    }

    private static void SetQualitySize(Window window, string profile)
    {
        if (profile.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            window.Width = 600;
            window.Height = 560;
        }
        else
        {
            window.Width = 820;
            window.Height = 760;
        }
    }
}
