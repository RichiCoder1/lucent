using Lucent.Core;
using Lucent.Renderer.Skia;
using SkiaSharp;

namespace Lucent.Renderer.Skia.Tests;

[TestClass]
public sealed class CaretPresentationTests
{
    [TestMethod]
    public void HiddenCaretChangesOnlyPaintingOfTheSameScene()
    {
        using var composition = new Composition(new ReactiveGraph(), "caret-paint");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var renderer = new SkiaSceneRenderer();
        Controls.TextField(
            composition.Root,
            theme,
            "Text",
            style: Style.Empty.Width(100).Height(40)
        );
        SceneLayout.Project(composition, new(100, 40, 1), renderer);
        Assert.IsTrue(
            composition.Input.SetScene(SceneLayout.Project(composition, new(100, 40, 1), renderer))
        );
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        var scene = SceneLayout.Project(composition, new(100, 40, 1), renderer);
        Assert.IsTrue(composition.Input.SetScene(scene));
        Assert.IsTrue(composition.Input.TryGetCaretGeometry(out var before));
        using var visible = new SKBitmap(100, 40);
        using var hidden = new SKBitmap(100, 40);
        using var visibleCanvas = new SKCanvas(visible);
        using var hiddenCanvas = new SKCanvas(hidden);
        renderer.Render(scene, visibleCanvas, showCaret: true);
        renderer.Render(scene, hiddenCanvas, showCaret: false);
        Assert.IsFalse(visible.Pixels.SequenceEqual(hidden.Pixels));
        Assert.IsTrue(composition.Input.TryGetCaretGeometry(out var after));
        Assert.AreEqual(before, after, "Blinking changed retained IME/accessibility geometry.");
    }
}
