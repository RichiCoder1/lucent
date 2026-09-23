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

            AssertPixel(bitmap, scale, 2, 2, new(255, 0, 0, 128), "rectangle opacity");
            AssertPixel(bitmap, scale, 7, 2, new(0, 255, 0, 128), "clipped translation");
            AssertPixel(bitmap, scale, 10, 0, new(0, 0, 0, 0), "outside clip");
            AssertPixel(bitmap, scale, 3, 10, new(0, 0, 0, 128), "line opacity");
        }
    }

    [TestMethod]
    [DataRow("filled-ellipse", 10f, 10f, 4f, 4f)]
    [DataRow("stroked-ellipse", 15f, 10f, 10f, 10f)]
    [DataRow("arc", 15f, 10f, 5f, 10f)]
    [DataRow("filled-rounded-rectangle", 10f, 10f, 4f, 4f)]
    [DataRow("stroked-rounded-rectangle", 10f, 4f, 10f, 10f)]
    [DataRow("filled-path", 10f, 8f, 4f, 14f)]
    [DataRow("stroked-path", 10f, 4f, 10f, 10f)]
    public void EachPrimitivePaintsItsOwnGeometryAtEveryScale(
        string primitive,
        float paintedX,
        float paintedY,
        float clearX,
        float clearY
    )
    {
        foreach (var scale in new[] { 1f, 1.5f, 2f })
        {
            using var scene = Scene(scale, recorder => RecordPrimitive(recorder, primitive));
            var size = (int)MathF.Ceiling(20 * scale);
            using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            using var renderer = new SkiaSceneRenderer();
            canvas.Clear(SKColors.Transparent);
            renderer.Render(scene, canvas);

            AssertPixel(bitmap, scale, paintedX, paintedY, new(51, 102, 153, 255), primitive);
            AssertPixel(bitmap, scale, clearX, clearY, new(0, 0, 0, 0), primitive);
            AssertPixel(bitmap, scale, 1, 1, new(0, 0, 0, 0), primitive + " exterior");
            if (primitive == "arc")
                AssertPixel(bitmap, scale, 10, 10, new(0, 0, 0, 0), "arc interior");
        }
    }

    private static void RecordPrimitive(DrawingRecorder recorder, string primitive)
    {
        var color = Color.Parse("#336699");
        switch (primitive)
        {
            case "filled-ellipse":
                recorder.FillEllipse(new(4, 4, 12, 12), color);
                break;
            case "stroked-ellipse":
                recorder.StrokeEllipse(new(4, 4, 12, 12), color, 4);
                break;
            case "arc":
                recorder.Arc(new(4, 4, 12, 12), -90, 180, color, 4);
                break;
            case "filled-rounded-rectangle":
                recorder.FillRoundedRectangle(new(4, 4, 12, 12), 4, color);
                break;
            case "stroked-rounded-rectangle":
                recorder.StrokeRoundedRectangle(new(4, 4, 12, 12), 4, color, 4);
                break;
            case "filled-path":
                recorder.FillPath(
                    recorder.Path(path =>
                    {
                        path.MoveTo(new(4, 4));
                        path.LineTo(new(16, 4));
                        path.LineTo(new(10, 16));
                        path.Close();
                    }),
                    color
                );
                break;
            case "stroked-path":
                recorder.StrokePath(
                    recorder.Path(path =>
                    {
                        path.MoveTo(new(4, 4));
                        path.LineTo(new(16, 4));
                        path.LineTo(new(16, 16));
                        path.LineTo(new(4, 16));
                        path.Close();
                    }),
                    color,
                    4
                );
                break;
            default:
                throw new ArgumentException("Unknown primitive.", nameof(primitive));
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

    private static void AssertPixel(
        SKBitmap bitmap,
        float scale,
        float x,
        float y,
        SKColor expected,
        string context
    )
    {
        var physicalX = (int)MathF.Floor(x * scale);
        var physicalY = (int)MathF.Floor(y * scale);
        Assert.IsTrue(physicalX >= 0 && physicalX < bitmap.Width);
        Assert.IsTrue(physicalY >= 0 && physicalY < bitmap.Height);
        var actual = bitmap.GetPixel(physicalX, physicalY);
        Assert.IsTrue(
            Math.Abs(actual.Red - expected.Red) <= 2
                && Math.Abs(actual.Green - expected.Green) <= 2
                && Math.Abs(actual.Blue - expected.Blue) <= 2
                && Math.Abs(actual.Alpha - expected.Alpha) <= 2,
            $"{context}: logical ({x}, {y}), physical ({physicalX}, {physicalY}), scale {scale}: "
                + $"expected RGBA ({expected.Red}, {expected.Green}, {expected.Blue}, {expected.Alpha}) ±2; "
                + $"actual ({actual.Red}, {actual.Green}, {actual.Blue}, {actual.Alpha})."
        );
    }
}
