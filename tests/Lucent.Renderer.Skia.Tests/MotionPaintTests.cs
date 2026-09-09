using Lucent.Core;
using SkiaSharp;

namespace Lucent.Renderer.Skia.Tests;

[TestClass]
public sealed class MotionPaintTests
{
    [TestMethod]
    public void RetainedMotionChangesRealPixelsWithoutCallingTheTextShaper()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "skia-motion");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var active = graph.Signal(false, "active");
        composition.Root.Present(
            theme,
            author: Style
                .Empty.Set(ProjectionProperties.Text, "A retained paragraph")
                .Bind(
                    VisualProperties.Background,
                    () => Brush.Solid(Color.Parse(active.Value ? "#FFFFFF" : "#000000"))
                )
                .Transition(VisualProperties.Background, Motion.Duration(100, Easing.Linear))
        );
        using var renderer = new SkiaSceneRenderer();
        var shaper = new CountingShaper(renderer);
        composition.SamplePresentation(TimeSpan.Zero);
        using var initial = SceneLayout.ProjectFrame(composition, new(240, 80, 1), shaper, null);
        Assert.IsTrue(composition.Input.SetScene(initial));
        Assert.IsTrue(composition.TryAcknowledgePresentation(initial.Generation));
        active.Value = true;
        using var target = SceneLayout.ProjectFrame(composition, initial.Viewport, shaper, initial);
        Assert.IsTrue(composition.Input.SetScene(target));
        Assert.IsTrue(composition.TryAcknowledgePresentation(target.Generation));
        var shapeRequests = shaper.Calls;
        var nativeShapes = renderer.ParagraphShapeCount;
        using var bitmap = new SKBitmap(240, 80);
        using var canvas = new SKCanvas(bitmap);
        renderer.Render(target, canvas);
        Assert.AreEqual((byte)0, bitmap.GetPixel(230, 70).Red);
        composition.SamplePresentation(TimeSpan.FromMilliseconds(50));
        using var middle = SceneLayout.ProjectFrame(composition, target.Viewport, shaper, target);
        Assert.IsTrue(middle.IsPaintOnly);
        canvas.Clear(SKColors.Transparent);
        renderer.Render(middle, canvas);
        var pixel = bitmap.GetPixel(230, 70);
        Assert.IsTrue(
            pixel.Red is >= 187 and <= 189,
            $"Linear-light midpoint should encode near sRGB 188, observed {pixel.Red}."
        );
        Assert.AreEqual(pixel.Red, pixel.Green);
        Assert.AreEqual(pixel.Red, pixel.Blue);
        Assert.AreEqual((byte)255, pixel.Alpha);
        Assert.AreEqual(shapeRequests, shaper.Calls);
        Assert.AreEqual(nativeShapes, renderer.ParagraphShapeCount);
        composition.SamplePresentation(TimeSpan.FromMilliseconds(100));
        using var final = SceneLayout.ProjectFrame(composition, middle.Viewport, shaper, middle);
        renderer.Render(final, canvas);
        Assert.AreEqual(SKColors.White, bitmap.GetPixel(230, 70));
        Assert.AreEqual(shapeRequests, shaper.Calls);

        // Renderer replacement is a full projection boundary, even if portable
        // geometry otherwise matches; old immutable paragraph data still renders.
        using var replacement = new SkiaSceneRenderer();
        using var reset = SceneLayout.ProjectFrame(composition, final.Viewport, replacement, final);
        Assert.IsFalse(reset.IsPaintOnly);
        replacement.Render(middle, canvas);
        Assert.IsTrue(bitmap.GetPixel(230, 70).Red is >= 187 and <= 189);
    }

    private sealed class CountingShaper(ITextShaper inner) : ITextShaper
    {
        internal int Calls { get; private set; }

        public ShapedText Shape(TextMeasureRequest request)
        {
            Calls++;
            return inner.Shape(request);
        }
    }
}
