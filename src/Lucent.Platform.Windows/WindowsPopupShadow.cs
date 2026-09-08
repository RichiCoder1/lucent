using Lucent.Core;
using SkiaSharp;

namespace Lucent.Platform.Windows;

internal static class WindowsPopupShadow
{
    // Three sigma plus the downward offset fit inside the host's 16-DIP margin.
    internal static void Draw(SKCanvas canvas, LayoutRect bounds, float radius, float scale)
    {
        canvas.Save();
        try
        {
            canvas.Scale(scale);
            using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(0, 0, 0, 55),
                MaskFilter = blur,
            };
            canvas.DrawRoundRect(
                new SKRect(
                    bounds.X,
                    bounds.Y + 3,
                    bounds.X + bounds.Width,
                    bounds.Y + bounds.Height + 3
                ),
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
