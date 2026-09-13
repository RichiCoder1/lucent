using System.Numerics;
using Lucent.Core;
using Lucent.Renderer.Skia;
using SkiaSharp;

namespace Lucent.Renderer.Skia.Tests;

[TestClass]
public sealed class DrawingRendererContracts
{
    [TestMethod]
    public void ReplaysFrozenShapesPathsClipTransformAndOpacityAtEveryScale()
    {
        foreach (var scale in new[] { 1f, 1.5f, 2f })
        {
            using var scene = Scene(
                scale,
                recorder =>
                {
                    recorder.FillRectangle(new(0, 0, 5, 5), Color.Parse("#FF0000"));
                    using (recorder.Clip(new(5, 0, 5, 5)))
                    using (recorder.Transform(Matrix3x2.CreateTranslation(5, 0)))
                        recorder.FillRectangle(new(0, 0, 5, 5), Color.Parse("#00FF00"));
                    recorder.Line(
                        new(1, 10),
                        new(7, 10),
                        Color.Parse("#000000"),
                        2,
                        DrawingStrokeCap.Round
                    );
                    recorder.StrokeEllipse(
                        new(11, 1, 8, 8),
                        Color.Parse("#0000FF"),
                        2,
                        DrawingStrokeCap.Round
                    );
                    recorder.Arc(
                        new(11, 11, 8, 8),
                        -90,
                        180,
                        Color.Parse("#FF00FF"),
                        2,
                        DrawingStrokeCap.Round
                    );
                    recorder.FillRoundedRectangle(new(1, 13, 6, 6), 2, Color.Parse("#00FFFF"));
                    var path = recorder.Path(path =>
                    {
                        path.MoveTo(new(8, 12));
                        path.LineTo(new(10, 19));
                        path.LineTo(new(14, 19));
                        path.Close();
                    });
                    recorder.FillPath(path, Color.Parse("#FFFF00"));
                    recorder.StrokePath(
                        path,
                        Color.Parse("#202020"),
                        1,
                        DrawingStrokeCap.Square,
                        DrawingStrokeJoin.Bevel
                    );
                },
                opacity: .5f
            );
            var size = (int)MathF.Ceiling(20 * scale);
            using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            using var renderer = new SkiaSceneRenderer();
            canvas.Clear(SKColors.Transparent);
            renderer.Render(scene, canvas);

            var red = Pixel(bitmap, scale, 2, 2);
            var green = Pixel(bitmap, scale, 7, 2);
            var clipped = Pixel(bitmap, scale, 10, 0);
            var line = Pixel(bitmap, scale, 3, 10);
            Assert.IsTrue(red.Red > 100 && red.Green < 10 && red.Alpha is >= 126 and <= 130);
            Assert.IsTrue(green.Green > 100 && green.Red < 10 && green.Alpha is >= 126 and <= 130);
            Assert.AreEqual(
                (byte)0,
                clipped.Alpha,
                "Clip or transform was applied more than once."
            );
            Assert.IsTrue(line.Alpha > 100 && line.Red < 20 && line.Green < 20 && line.Blue < 20);
            Assert.IsTrue(
                bitmap.Bytes.Count(value => value != 0) > size,
                "The frozen drawing produced no bounded shape/path/arc pixels."
            );
        }
    }

    [TestMethod]
    public void ReplayDoesNotInvokeRecorderAndQuickRejectsOffscreenDrawing()
    {
        var calls = 0;
        using var visible = Scene(
            1,
            recorder =>
            {
                calls++;
                recorder.FillEllipse(new(0, 0, 20, 20), Color.Parse("#336699"));
            }
        );
        using var renderer = new SkiaSceneRenderer();
        using var bitmap = new SKBitmap(20, 20);
        using var canvas = new SKCanvas(bitmap);
        renderer.Render(visible, canvas);
        renderer.Render(visible, canvas);
        Assert.AreEqual(1, calls);

        using var offscreen = Scene(
            1,
            recorder => recorder.FillRectangle(new(0, 0, 20, 20), Color.Parse("#FFFFFF")),
            bounds: new(100, 100, 20, 20)
        );
        canvas.Clear(SKColors.Transparent);
        renderer.Render(offscreen, canvas);
        Assert.IsTrue(bitmap.Bytes.All(value => value == 0));
    }

    [TestMethod]
    public void UnsupportedFrozenShapeFailsClosed()
    {
        var frozen = new FrozenDrawing([
            new DrawingShapeCommand(
                (DrawingShapeKind)99,
                new(0, 0, 10, 10),
                0,
                Color.Parse("#FFFFFF"),
                true,
                0,
                DrawingStrokeCap.Butt
            ),
        ]);
        using var lease = new DrawingLease(new DrawingResource(frozen));
        using var scene = new RetainedScene(
            1,
            new(10, 10, 1),
            [],
            [
                new DrawingSceneNode(
                    new(new(1, 1), SceneNodeKind.Drawing),
                    new(0, 0, 10, 10),
                    new(10, 10),
                    lease
                ),
            ],
            []
        );
        using var bitmap = new SKBitmap(10, 10);
        using var canvas = new SKCanvas(bitmap);
        using var renderer = new SkiaSceneRenderer();
        Assert.ThrowsExactly<InvalidOperationException>(() => renderer.Render(scene, canvas));
    }

    private static RetainedScene Scene(
        float scale,
        Action<DrawingRecorder> record,
        float opacity = 1,
        LayoutRect? bounds = null
    )
    {
        var descriptor = new DrawingDescriptor(new(20, 20), record);
        var lease = new DrawingLease(new DrawingResource(descriptor.Freeze()));
        var box = bounds ?? new LayoutRect(0, 0, 20, 20);
        SceneNode node = new DrawingSceneNode(
            new(new(1, 1), SceneNodeKind.Drawing),
            box,
            descriptor.CoordinateSize,
            lease
        );
        if (opacity < 1)
            node = new OpacitySceneNode(
                new(new(1, 1), SceneNodeKind.Opacity),
                box,
                opacity,
                [node]
            );
        var scene = new RetainedScene(1, new(20, 20, scale), [], [node], []);
        lease.Dispose();
        return scene;
    }

    private static SKColor Pixel(SKBitmap bitmap, float scale, float x, float y) =>
        bitmap.GetPixel(
            Math.Clamp((int)MathF.Floor(x * scale), 0, bitmap.Width - 1),
            Math.Clamp((int)MathF.Floor(y * scale), 0, bitmap.Height - 1)
        );
}
