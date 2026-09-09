using Lucent.Core;
using Lucent.Renderer.Skia;
using SkiaSharp;

namespace Lucent.Testing.Skia;

/// <summary>Creates headless applications with production Skia shaping and captures real frames.</summary>
public static class SkiaHeadlessApplication
{
    /// <summary>Starts a fixed recipe using a thread-owned production Skia renderer.</summary>
    public static Task<HeadlessApplication> StartAsync(
        ComponentRecipe recipe,
        HeadlessApplicationOptions? options = null
    ) => HeadlessApplication.StartAsync(recipe, WithSkia(options));

    /// <summary>Creates and starts a recipe using a thread-owned production Skia renderer.</summary>
    public static Task<HeadlessApplication> StartAsync(
        Func<HeadlessContext, ComponentRecipe> recipeFactory,
        HeadlessApplicationOptions? options = null
    ) => HeadlessApplication.StartAsync(recipeFactory, WithSkia(options));

    /// <summary>Starts a production lifecycle using a thread-owned production Skia renderer.</summary>
    public static Task<HeadlessApplication> StartAsync(
        IApplicationLifecycle lifecycle,
        HeadlessApplicationOptions? options = null
    ) => HeadlessApplication.StartAsync(lifecycle, WithSkia(options));

    /// <summary>Captures the settled scene as physical-pixel PNG bytes on the application owner thread.</summary>
    public static Task<byte[]> CapturePngAsync(
        this HeadlessApplication application,
        bool showCaret = true
    )
    {
        ArgumentNullException.ThrowIfNull(application);
        return application.InvokeAfterSettleAsync(context => Capture(context.Scene, showCaret));
    }

    private static HeadlessApplicationOptions WithSkia(HeadlessApplicationOptions? source) =>
        new()
        {
            Title = source?.Title ?? "Lucent headless test",
            Viewport = source?.Viewport ?? new LayoutViewport(1024, 768, 1),
            Appearance = source?.Appearance ?? ThemeAppearance.Light,
            ThemeFactory =
                source?.ThemeFactory
                ?? (
                    appearance =>
                        appearance.Contrast == ThemeContrast.High ? ControlThemes.HighContrast
                        : appearance.ColorScheme == ThemeColorScheme.Dark ? ControlThemes.Dark
                        : ControlThemes.Light
                ),
            TextShaperFactory = static () => new SkiaSceneRenderer(),
            TimeProviderFactory = source?.TimeProviderFactory ?? (static () => new()),
            ImagePreparer = source?.ImagePreparer ?? new SkiaImagePreparer(),
            ImageLimits = source?.ImageLimits,
            MaximumWorkItems = source?.MaximumWorkItems ?? 10_000,
        };

    private static byte[] Capture(RetainedScene scene, bool showCaret)
    {
        var width = checked((int)MathF.Ceiling(scene.Viewport.Width * scene.Viewport.Scale));
        var height = checked((int)MathF.Ceiling(scene.Viewport.Height * scene.Viewport.Scale));
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("The headless viewport has no renderable pixels.");
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        using (var renderer = new SkiaSceneRenderer())
        {
            canvas.Clear(SKColors.Transparent);
            renderer.Render(scene, canvas, showCaret);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray()
            ?? throw new InvalidOperationException("Skia did not encode the headless frame.");
    }
}
