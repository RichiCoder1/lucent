using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Skia;

namespace ShadcnGallery;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var dark = args.Contains("--dark");
        if (args.Contains("--capture"))
        {
            Capture(dark);
            return;
        }
        BuildAvaloniaApp(dark).StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp(bool dark) => AppBuilder.Configure(() => new GalleryApp(dark)).UsePlatformDetect();

    private static void Capture(bool dark)
    {
        var app = new GalleryApp(dark);
        AppBuilder.Configure(() => app).UseHeadless(new AvaloniaHeadlessPlatformOptions()).UseSkia().SetupWithoutStarting();
        app.Initialize();
        using var stop = new CancellationTokenSource();
        Exception? failure = null;
        Dispatcher.UIThread.Post(() =>
        {
            try { CaptureWindow(dark); }
            catch (Exception error) { failure = error; }
            finally { stop.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(stop.Token);
        if (failure is not null) throw failure;
    }

    private static void CaptureWindow(bool dark)
    {
        var window = new GalleryWindow { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            window.FocusTarget.Focus();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            var controls = Descendants(window).ToArray();
            ((IPseudoClasses)controls.OfType<Button>().Single(button => button.Name == "PressedEvidence").Classes).Add(":pressed");
            var hoverStates = controls.Count(control => control.Classes is IPseudoClasses states && states.Contains(":pointerover"));
            var pressedStates = controls.Count(control => control.Classes is IPseudoClasses states && states.Contains(":pressed"));
            var focusStates = controls.Count(control => control.Classes is IPseudoClasses states && states.Contains(":focus-visible"));
            if (controls.OfType<Button>().Count() < 10 || controls.OfType<TextBox>().Count() < 4 ||
                !controls.OfType<ListBox>().Any() || !controls.OfType<Menu>().Any() || !controls.OfType<ToolTip>().Any() ||
                hoverStates != 1 || pressedStates != 1 || focusStates < 7)
                throw new InvalidOperationException($"The gallery capture is missing its promised controls: {controls.OfType<Button>().Count()} buttons, {controls.OfType<TextBox>().Count()} text boxes, {hoverStates} hover, {pressedStates} pressed, {focusStates} focus-visible.");
            var path = Path.Combine("docs", "quality", "009-shadcn-theme", "captures", dark ? "shadcn-gallery-dark.png" : "shadcn-gallery-light.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException());
            using var bitmap = new RenderTargetBitmap(new PixelSize(900, 700));
            bitmap.Render(window);
            bitmap.Save(path, new PngBitmapEncoderOptions());
            using var stream = File.OpenRead(path);
            using var decoded = Bitmap.DecodeToWidth(stream, 900);
            if (decoded.PixelSize != new PixelSize(900, 700)) throw new InvalidOperationException("The gallery capture has the wrong dimensions.");
        }
        finally { window.Close(); }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.GetVisualChildren().OfType<Control>())
            foreach (var nested in Descendants(child)) yield return nested;
    }
}
