using Lucent.Core;
using Lucent.Icons.Lucide;
using SkiaSharp;

namespace Lucent.Renderer.Skia.Tests;

[TestClass]
public sealed class LucideIconPackTests
{
    private static readonly ImageSource[] Sources =
    [
        LucideIcons.Archive,
        LucideIcons.ArrowLeft,
        LucideIcons.ArrowRight,
        LucideIcons.CircleCheck,
        LucideIcons.CircleDot,
        LucideIcons.Ellipsis,
        LucideIcons.FileText,
        LucideIcons.Inbox,
        LucideIcons.Link,
        LucideIcons.ListFilter,
        LucideIcons.NotebookPen,
        LucideIcons.Plus,
        LucideIcons.RefreshCw,
        LucideIcons.Search,
        LucideIcons.Trash,
    ];

    private static readonly SKColor[] StockTints =
    [
        new(32, 34, 38),
        new(242, 244, 248),
        new(255, 255, 0),
    ];

    [TestMethod]
    public void EveryPinnedIconPaintsWithStockTintAtOneToTwoHundredPercent()
    {
        var preparer = new SkiaImagePreparer();
        foreach (var source in Sources)
        {
            using var prepared = preparer
                .PrepareAsync(new(source, new(32, 32), ImageLoadLimits.Default), default)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            Assert.IsFalse(prepared is RasterImage, source.PackagedAsset!.Id.ToString());
            foreach (var size in new[] { 16, 24, 32 })
            foreach (var tint in StockTints)
            {
                using var bitmap = Draw(prepared, size, tint);
                var painted = bitmap.Pixels.Where(pixel => pixel.Alpha != 0).ToArray();
                Assert.IsGreaterThan(size, painted.Length, source.PackagedAsset!.Id.ToString());
                var opaque = painted.MaxBy(pixel => pixel.Alpha);
                Assert.IsGreaterThan((byte)127, opaque.Alpha, source.PackagedAsset!.Id.ToString());
                Assert.IsTrue(
                    Math.Abs(tint.Red - opaque.Red) <= 2
                        && Math.Abs(tint.Green - opaque.Green) <= 2
                        && Math.Abs(tint.Blue - opaque.Blue) <= 2,
                    source.PackagedAsset!.Id.ToString()
                );
            }
        }
    }

    private static SKBitmap Draw(PreparedImage image, int size, SKColor tint)
    {
        var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        ((ISkiaPreparedImage)image).Draw(
            canvas,
            new(0, 0, image.Width, image.Height),
            new(0, 0, size, size),
            ImageColorMode.Monochrome,
            new(tint.Red, tint.Green, tint.Blue, tint.Alpha)
        );
        return bitmap;
    }
}
