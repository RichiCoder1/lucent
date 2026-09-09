using Lucent.Core;
using SkiaSharp;

namespace Lucent.Platform.Windows;

internal static class WindowsModalScrim
{
    internal static void Draw(SKCanvas canvas, LayoutRect bounds, float radius, float scale)
    {
        canvas.Save();
        try
        {
            canvas.Scale(scale);
            using var paint = new SKPaint { Color = new SKColor(0, 0, 0, 64), IsAntialias = true };
            canvas.DrawRoundRect(
                new SKRect(bounds.X, bounds.Y, bounds.X + bounds.Width, bounds.Y + bounds.Height),
                radius,
                radius,
                paint
            );
        }
        finally
        {
            canvas.Restore();
        }
    }
}
