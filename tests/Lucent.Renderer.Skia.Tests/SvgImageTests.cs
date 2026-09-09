using System.Security.Cryptography;
using System.Text;
using Lucent.Core;
using SkiaSharp;

namespace Lucent.Renderer.Skia.Tests;

[TestClass]
public sealed class SvgImageTests
{
    private const string Header =
        "<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100'>";
    private const string Footer = "</svg>";

    [TestMethod]
    public void VectorRetainsGradientsClipsMasksFiltersAndSourceOrTintCoverage()
    {
        const string body = """
            <defs>
              <linearGradient id="gradient"><stop offset="0" stop-color="red"/><stop offset="1" stop-color="blue"/></linearGradient>
              <clipPath id="clip"><rect width="80" height="100"/></clipPath>
              <mask id="mask"><rect width="100" height="100" fill="white"/><rect x="40" y="40" width="20" height="20" fill="black"/></mask>
              <filter id="blur"><feGaussianBlur stdDeviation="1"/></filter>
            </defs>
            <g clip-path="url(#clip)" mask="url(#mask)"><rect x="5" y="5" width="90" height="90" fill="url(#gradient)" filter="url(#blur)"/></g>
            """;
        using var image = Prepare(Header + body + Footer);
        Assert.IsFalse(image is RasterImage);
        using var source = Draw(image, 100);
        Assert.IsTrue(source.GetPixel(20, 20).Red > source.GetPixel(20, 20).Blue);
        Assert.IsTrue(source.GetPixel(70, 20).Blue > source.GetPixel(70, 20).Red);
        Assert.AreEqual((byte)0, source.GetPixel(50, 50).Alpha, "Mask hole must survive.");
        Assert.AreEqual((byte)0, source.GetPixel(90, 50).Alpha, "Clip must survive.");
        foreach (var size in new[] { 100, 150, 200 })
        {
            using var tinted = Draw(image, size, ImageColorMode.Monochrome);
            var pixel = tinted.GetPixel(size / 5, size / 5);
            Assert.IsTrue(pixel.Green > 240 && pixel.Red < 10 && pixel.Blue < 10);
            Assert.AreEqual((byte)0, tinted.GetPixel(size / 2, size / 2).Alpha);
        }
    }

    [TestMethod]
    public void StaticStylesUseReferencesPathsAndCurrentColorRenderWithoutReparse()
    {
        using var image = Prepare(
            Header
                + """
                <style>.shape { fill: currentColor; }</style>
                <defs><path id="shape" d="M10 10 H90 V90 H10 Z"/></defs>
                <use href="#shape" class="shape"/>
                """
                + Footer
        );
        using var source = Draw(image, 100);
        Assert.AreEqual(SKColors.Black, source.GetPixel(50, 50));
        using var tinted = Draw(image, 200, ImageColorMode.Monochrome);
        Assert.AreEqual(SKColors.Lime, tinted.GetPixel(100, 100));
        using var colored = Prepare(
            Header.Replace("height='100'", "height='100' color='red'")
                + "<rect width='100' height='100' fill='currentColor'/>"
                + Footer
        );
        using var coloredBitmap = Draw(colored, 100);
        Assert.AreEqual(
            SKColors.Red,
            coloredBitmap.GetPixel(50, 50),
            "Authored root color must override the image-context default."
        );
    }

    [TestMethod]
    public void ExplicitPinnedFontRendersTextAndHasContentIdentity()
    {
        var bytes = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Abel-Regular.ttf")
        );
        Assert.AreEqual(
            "8809DCAD25318225052F88333E208C5AAD4ADCB7B2C934C135735EC19AA410B4",
            Convert.ToHexString(SHA256.HashData(bytes))
        );
        var preparer = new SkiaImagePreparer(bytes);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(bytes)), preparer.SvgFontIdentity);
        Array.Clear(bytes); // The preparer owns an immutable copy.
        using var image = Prepare(
            Header
                + "<text x='5' y='55' font-family='Lucent SVG' font-size='24'>Lucent</text>"
                + Footer,
            preparer
        );
        using var bitmap = Draw(image, 100);
        Assert.IsTrue(
            bitmap.Pixels.Count(p => p.Alpha > 0) > 100,
            "Pinned text must produce actual ink."
        );
    }

    [TestMethod]
    public void ViewBoxOnlyAndPercentageSourcesUseLogicalMetadataInsteadOfFirstRasterBucket()
    {
        var source = Source(
            "<svg xmlns='http://www.w3.org/2000/svg' width='100%' height='100%' viewBox='0 0 100 50'><rect width='100' height='50' fill='red'/></svg>",
            new AssetImageMetadata(300, 150, relativeWidth: 1, relativeHeight: 1)
        );
        using var image = new SkiaImagePreparer()
            .PrepareAsync(new(source, new(16, 16), ImageLoadLimits.Default), default)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        Assert.AreEqual(300, image.Width);
        Assert.AreEqual(150, image.Height);
        using var bitmap = Draw(image, 200);
        Assert.AreEqual(SKColors.Red, bitmap.GetPixel(100, 100));
    }

    [TestMethod]
    public void CacheSharesOneVectorAcrossScalesAndIndependentLeasesOutliveCache()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("svg-cache");
        var cache = new ImageCache(new SkiaImagePreparer());
        var source = Source(Header + "<circle cx='50' cy='50' r='40' fill='red'/>" + Footer);
        using var first = cache.Acquire(scope, source, new(64, 64));
        using var second = cache.Acquire(scope, source, new(256, 256));
        Assert.IsTrue(
            SpinWait.SpinUntil(
                () =>
                {
                    graph.Drain();
                    return first.Status != ImageLoadStatus.Loading;
                },
                10000
            )
        );
        Assert.AreEqual(ImageLoadStatus.Ready, first.Status, first.Error?.Message);
        Assert.AreEqual(ImageLoadStatus.Ready, second.Status, second.Error?.Message);
        using var a = first.AcquireLease();
        using var b = second.AcquireLease();
        Assert.IsNotNull(a);
        Assert.IsNotNull(b);
        Assert.AreSame(a.Resource, b.Resource);
        Assert.AreEqual(1, cache.Metrics.Misses);
        cache.Dispose();
        using var bitmap = Draw(a.Resource, 150);
        Assert.AreEqual(SKColors.Red, bitmap.GetPixel(75, 75));
    }

    [TestMethod]
    public void ForbiddenAndUnresolvedResourcesFailBeforePartialRendering()
    {
        string[] bodies =
        [
            "<script>alert(1)</script>",
            "<rect width='10' height='10'><animate attributeName='x' dur='1s'/></rect>",
            "<foreignObject/>",
            "<image href='file:///secret.png'/>",
            "<image href='https://example.invalid/image.png'/>",
            "<style>@import url(https://example.invalid/theme.css);</style>",
            "<rect style='fill: u\\72l(https://example.invalid/x)'/>",
            "<rect fill='url(#missing)'/>",
            "<use href='#missing'/>",
            "<g id='cycle'><use href='#cycle'/></g>",
            "<defs><rect id='wrong'/></defs><rect fill='url(#wrong)'/>",
            "<image href='data:image/svg+xml;base64,PHN2Zy8+'/>",
            "<image href='data:image/png;base64,AA=='/>",
            "<text>Unpinned font</text>",
            "<path d='this is not a path'/>",
            "<rect onclick='alert(1)'/>",
            "<rect width='Infinity'/>",
            "<rect style='animation: spin 1s'/>",
            "<filter id='f'><feGaussianBlur in='missing'/></filter><rect filter='url(#f)'/>",
            "<filter id='f'><feGaussianBlur/></filter><rect transform='scale(1000)' filter='url(#f)'/>",
        ];
        foreach (var body in bodies)
        {
            var exception = Assert.ThrowsExactly<ImageLoadException>(
                () => Prepare(Header + "<rect width='5' height='5'/>" + body + Footer),
                body
            );
            Assert.IsTrue(
                exception.Kind
                    is ImageLoadFailureKind.InvalidData
                        or ImageLoadFailureKind.UnsupportedFormat,
                exception.Message
            );
        }
        Assert.ThrowsExactly<ImageLoadException>(() =>
            Prepare(
                "<!DOCTYPE svg [<!ENTITY secret SYSTEM 'file:///secret'>]>"
                    + Header
                    + "<text>&secret;</text>"
                    + Footer
            )
        );
        Assert.ThrowsExactly<ImageLoadException>(() =>
            Prepare("<?xml-stylesheet href='https://example.invalid/x'?>" + Header + Footer)
        );
    }

    [TestMethod]
    public void BudgetsRejectDepthExpansionBlurAndPreparationMemory()
    {
        var cases = new[]
        {
            Header
                + string.Concat(Enumerable.Repeat("<g>", 40))
                + string.Concat(Enumerable.Repeat("</g>", 40))
                + Footer,
            Header + "<filter><feGaussianBlur stdDeviation='10000'/></filter>" + Footer,
            Header + string.Concat(Enumerable.Repeat("<rect/>", 5000)) + Footer,
            Header
                + "<g transform='scale(1000000)'><g transform='scale(1000000)'><rect/></g></g>"
                + Footer,
            Header
                + "<filter id='f' width='1000000'><feGaussianBlur/></filter><rect filter='url(#f)'/>"
                + Footer,
        };
        foreach (var value in cases)
            Assert.AreEqual(
                ImageLoadFailureKind.BudgetDeclined,
                Assert.ThrowsExactly<ImageLoadException>(() => Prepare(value)).Kind
            );
        var request = new ImagePreparationRequest(
            Source(Header + Footer),
            new(64, 64),
            new(maximumTemporaryBytes: 1024)
        );
        Assert.AreEqual(
            ImageLoadFailureKind.BudgetDeclined,
            Assert
                .ThrowsExactly<ImageLoadException>(() =>
                    new SkiaImagePreparer()
                        .PrepareAsync(request, default)
                        .AsTask()
                        .GetAwaiter()
                        .GetResult()
                )
                .Kind
        );
    }

    [TestMethod]
    public void EmbeddedRasterIsValidatedAndPainted()
    {
        var png = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "pixel.png")
        );
        using var image = Prepare(
            Header
                + $"<image href='data:image/png;base64,{Convert.ToBase64String(png)}' width='100' height='100'/>"
                + Footer
        );
        using var bitmap = Draw(image, 100);
        Assert.AreEqual(new SKColor(35, 100, 160), bitmap.GetPixel(50, 50));
    }

    private static PreparedImage Prepare(string xml, SkiaImagePreparer? preparer = null) =>
        (preparer ?? new())
            .PrepareAsync(new(Source(xml), new(64, 64), ImageLoadLimits.Default), default)
            .AsTask()
            .GetAwaiter()
            .GetResult();

    private static ImageSource Source(string xml, AssetImageMetadata? metadata = null)
    {
        var bytes = Encoding.UTF8.GetBytes(xml);
        return ImageSource.FromAsset(
            new(
                new("svg-tests", "fixture.svg"),
                Convert.ToHexString(SHA256.HashData(bytes)),
                bytes.Length,
                AssetFormat.Svg,
                () => new MemoryStream(bytes, false),
                metadata ?? new(100, 100)
            )
        );
    }

    private static SKBitmap Draw(
        PreparedImage image,
        int size,
        ImageColorMode mode = ImageColorMode.Source
    )
    {
        var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        ((ISkiaPreparedImage)image).Draw(
            canvas,
            new(0, 0, image.Width, image.Height),
            new(0, 0, size, size),
            mode,
            new(0, 255, 0, 255)
        );
        return bitmap;
    }
}
