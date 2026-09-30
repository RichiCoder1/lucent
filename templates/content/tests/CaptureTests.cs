using Lucent.Core;
using Lucent.Testing.Skia;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace TemplateNamespace;

[TestClass]
public sealed class CaptureTests
{
    [TestMethod]
    public async Task CaptureUsesTheRequestedPhysicalSizeAndPaintsTheScene()
    {
        var recipe = Lucent.Core.Components.Column(
            [],
            style: Style.Empty.Width(40).Height(30).Background(Color.Parse("#2563eb"))
        );
        await using var app = await SkiaHeadlessApplication.StartAsync(
            recipe,
            new() { Viewport = new(40, 30, 1.5f) }
        );

        using var image = SKBitmap.Decode(await app.CapturePngAsync());
        Assert.AreEqual(60, image.Width);
        Assert.AreEqual(45, image.Height);
        Assert.AreEqual(new SKColor(0x25, 0x63, 0xeb, 0xff), image.GetPixel(30, 22));
    }
}
