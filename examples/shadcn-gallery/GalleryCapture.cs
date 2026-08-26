using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Skia;

namespace ShadcnGallery;

internal static class GalleryCapture
{
    public static void Run(bool dark) => Capture(dark);

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
        using var component = new GalleryWindowComponent();
        var window = component.MountRoot();
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Descendants(window).OfType<TextBox>().First(box => box.Name == "FocusTarget").Focus();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            var controls = Descendants(window).ToArray();
            GalleryCaptureStates.Apply(window);
            var hoverStates = controls.Count(control => control.Classes is IPseudoClasses states && states.Contains(":pointerover"));
            var pressedStates = controls.Count(control => control.Classes is IPseudoClasses states && states.Contains(":pressed"));
            var focusStates = controls.Count(control => control.Classes is IPseudoClasses states && states.Contains(":focus-visible"));
            var utilityClasses = controls.SelectMany(control => control.Classes).ToHashSet(StringComparer.Ordinal);
            var requiredUtilities = new[] { "m-2", "m-4", "p-2", "p-4", "w-24", "w-48", "h-8", "h-12", "gap-2", "gap-4", "text-sm", "text-lg", "font-medium", "font-bold", "italic", "text-left", "text-center", "text-wrap", "text-nowrap", "leading-6", "bg-primary", "bg-muted", "text-foreground", "border-border", "border", "border-2", "rounded", "rounded-lg", "opacity-50", "opacity-100", "hidden", "overflow-hidden", "text-center-self", "items-center", "hover:bg-primary", "focus:border-ring", "focus-visible:border-ring", "disabled:opacity-50", "checked:bg-primary", "selected:bg-muted" };
            if (controls.OfType<Button>().Count() < 12 || controls.OfType<TextBox>().Count() < 5 ||
                !controls.OfType<ListBox>().Any() || !controls.OfType<Menu>().Any() || !controls.OfType<ToolTip>().Any() ||
                hoverStates < 2 || pressedStates != 1 || focusStates < 8 || !requiredUtilities.All(utilityClasses.Contains))
                throw new InvalidOperationException($"The gallery capture is missing its promised controls: {controls.OfType<Button>().Count()} buttons, {controls.OfType<TextBox>().Count()} text boxes, {hoverStates} hover, {pressedStates} pressed, {focusStates} focus-visible; utilities: {string.Join(',', requiredUtilities.Where(name => !utilityClasses.Contains(name)))}.");

            var utility = (Border)controls.First(control => control.Name == "UtilityCatalogEvidence");
            var conflict = (Border)controls.First(control => control.Name == "UtilityConflictEvidence");
            var typography = (TextBlock)controls.First(control => control.Name == "UtilityTypographyEvidence");
            Require(utility.Margin == new Thickness(16) && utility.Padding == new Thickness(16) &&
                    utility.BorderThickness == new Thickness(2) && utility.CornerRadius == new CornerRadius(8) &&
                    utility.ClipToBounds && utility.Background is not null && utility.BorderBrush is not null &&
                    utility.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Center &&
                    utility.VerticalAlignment == Avalonia.Layout.VerticalAlignment.Center,
                $"Whole-value utilities did not resolve: margin={utility.Margin}, padding={utility.Padding}, border={utility.BorderThickness}, radius={utility.CornerRadius}, clip={utility.ClipToBounds}, background={utility.Background}.");
            Require(utility.Child is StackPanel { Spacing: 16 }, "The canonical gap utility did not resolve.");
            Require(conflict.Padding == new Thickness(16) && conflict.Width == 192 && conflict.Height == 48 && conflict.Background is not null,
                "Canonical spacing, size, and color conflicts did not resolve.");
            Require(typography.FontSize == 18 && typography.FontWeight == FontWeight.Bold &&
                    typography.FontStyle == FontStyle.Italic && typography.TextAlignment == TextAlignment.Center &&
                    typography.TextWrapping == TextWrapping.NoWrap && typography.LineHeight == 24,
                "Typography utilities did not resolve.");
            Require(controls.First(control => control.Name == "UtilityForegroundEvidence") is Button { Foreground: not null },
                "The templated-control foreground utility did not resolve.");
            Require(controls.First(control => control.Name == "UtilityOpacityEvidence").Opacity == 1 &&
                    !controls.First(control => control.Name == "UtilityHiddenEvidence").IsVisible,
                "Opacity/visibility utilities did not resolve.");
            Require(controls.First(control => control.Classes.Contains("hover:bg-primary")) is Button { Background: not null } &&
                    controls.First(control => control.Classes.Contains("focus:border-ring")) is TextBox { BorderBrush: not null } &&
                    controls.First(control => control.Classes.Contains("focus-visible:border-ring")) is Button { BorderBrush: not null } &&
                    controls.First(control => control.Classes.Contains("disabled:opacity-50")).Opacity == .5 &&
                    controls.First(control => control.Classes.Contains("checked:bg-primary")) is CheckBox { Background: not null } &&
                    controls.First(control => control.Name == "SelectedUtilityEvidence") is ListBoxItem { IsSelected: true, Background: not null },
                "State utility values did not resolve.");
            var focused = window.FocusManager?.GetFocusedElement();
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
            Dispatcher.UIThread.RunJobs();
            Require(window.FocusManager?.GetFocusedElement() is { } next && !ReferenceEquals(next, focused),
                "Keyboard traversal did not leave the focused gallery field.");
            var scroller = Descendants(window).OfType<ScrollViewer>().First();
            scroller.Offset = new Vector(0, utility.TranslatePoint(default, window)?.Y ?? 0);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Require(utility.TranslatePoint(default, window) is { Y: >= 0 } point && point.Y + utility.Bounds.Height <= 700,
                "Utility evidence is outside the utility capture viewport.");
            Save(window, dark ? "utility-gallery-dark.png" : "utility-gallery-light.png");

            Descendants(window).OfType<TextBox>().First(box => box.Name == "FocusTarget").Focus();
            scroller.Offset = default;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Save(window, dark ? "shadcn-gallery-dark.png" : "shadcn-gallery-light.png");
        }
        finally { window.Close(); }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.GetVisualChildren().OfType<Control>())
            foreach (var nested in Descendants(child)) yield return nested;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Save(Window window, string fileName)
    {
        var path = Path.Combine("docs", "quality", "009-shadcn-theme", "captures", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException());
        using var bitmap = new RenderTargetBitmap(new PixelSize(900, 700));
        bitmap.Render(window);
        bitmap.Save(path, new PngBitmapEncoderOptions());
        using var stream = File.OpenRead(path);
        using var decoded = Bitmap.DecodeToWidth(stream, 900);
        if (decoded.PixelSize != new PixelSize(900, 700)) throw new InvalidOperationException("The gallery capture has the wrong dimensions.");
    }
}
