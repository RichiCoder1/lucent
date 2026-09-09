using Lucent.Core;
using Lucent.Renderer.Skia;
using SkiaSharp;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed class WindowsPopupPresentationContracts
{
    [TestMethod]
    public void ModalScrimDimsOnlyTheOwnedSurfaceAndPreservesRoundedTransparentMargins()
    {
        using var bitmap = new SKBitmap(120, 100);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        WindowsModalScrim.Draw(canvas, new(10, 10, 60, 40), 8, 1.5f);
        Assert.AreEqual((byte)0, bitmap.GetPixel(0, 0).Alpha);
        Assert.AreEqual((byte)0, bitmap.GetPixel(15, 15).Alpha);
        Assert.AreEqual((byte)64, bitmap.GetPixel(45, 40).Alpha);
        Assert.AreEqual((byte)0, bitmap.GetPixel(119, 99).Alpha);
    }

    [TestMethod]
    public void ShadowFadesWithinTransparentMarginAtFractionalScale()
    {
        using var bitmap = new SKBitmap(228, 168);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var menu = new LayoutRect(16, 16, 120, 80);
        WindowsPopupShadow.Draw(canvas, menu, 6, 1.5f);
        Assert.AreEqual((byte)0, bitmap.GetPixel(0, 0).Alpha);
        Assert.AreEqual((byte)0, bitmap.GetPixel(114, 167).Alpha);
        Assert.IsTrue(bitmap.GetPixel(114, 149).Alpha > bitmap.GetPixel(114, 161).Alpha);
        Assert.IsTrue(bitmap.GetPixel(114, 149).Alpha > 0);
        Assert.IsFalse(WindowsPopupHost.ContainsMenuPoint(menu, 10, 45));
        Assert.IsTrue(WindowsPopupHost.ContainsMenuPoint(menu, 20, 45));
        Assert.AreEqual(-24, WindowsPopupHost.ToWindowUnits(-16, 1.5f, 1));
    }

    [TestMethod]
    public void WindowsScrollbarPreservesProjectedGutterAndAuthorOverrides()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "windows-scroll-style");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var viewport = composition.Child(composition.Root, "viewport");
        Controls.ScrollViewport(
            viewport,
            theme,
            "Viewport",
            style: WindowsControlStyles
                .ScrollBar(ThemeAppearance.Light)
                .With(Style.Empty.Width(160).Height(100).Set(ScrollBarProperties.Thickness, 18f))
        );
        var content = composition.Child(viewport, "content");
        Controls.Panel(content, theme, "Content", Style.Empty.Height(400));
        using var renderer = new SkiaSceneRenderer();
        var scene = SceneLayout.Project(composition, new(160, 100, 1.5f), renderer);
        var bar = scene.ScrollBars.Single();
        Assert.AreEqual(18f, bar.Track.Width);
        Assert.AreEqual(28f, bar.Thumb.Height);
        Assert.AreNotEqual(bar.ThumbBrush, bar.HoverThumbBrush);
        Assert.AreNotEqual(bar.HoverThumbBrush, bar.PressedThumbBrush);
        Assert.IsTrue(composition.Input.SetScene(scene));
        Assert.IsTrue(
            composition
                .Input.DispatchPointer(
                    new(
                        PointerCommandKind.Down,
                        1,
                        bar.Track.X + 2,
                        bar.Track.Y + bar.Track.Height - 2,
                        PointerButton.Primary
                    )
                )
                .Handled
        );
        var moved = SceneLayout
            .Project(composition, new(160, 100, 1.5f), renderer)
            .ScrollBars.Single();
        Assert.IsTrue(moved.Thumb.Y > bar.Thumb.Y);
    }
}
