using SkiaSharp;

namespace Lucent.Renderer.Skia;

/// <summary>Temporary platform-owned M0 bootstrap paint; it is not a framework canvas API.</summary>
internal static class BootstrapPainter
{
    public static void Paint(SKCanvas canvas, int backingWidth, int backingHeight)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        canvas.Clear(new SKColor(15, 23, 42));
        using var paint = new SKPaint { Color = new SKColor(96, 165, 250), IsAntialias = true };
        canvas.DrawRoundRect(40, 40, backingWidth - 40, backingHeight - 40, 16, 16, paint);
    }
}
