using Lucent.Core;
using SkiaSharp;

namespace Lucent.Renderer.Skia.Tests;

[TestClass]
public sealed class CacheOwnershipTests
{
    [TestMethod]
    public void ConfidentialTextBypassesRetainedShapeAndParagraphCaches()
    {
        using var renderer = new SkiaSceneRenderer();
        var request = new TextMeasureRequest(
            "example-only-secret",
            "Segoe UI",
            16,
            "en",
            TextDirection.LeftToRight,
            1,
            IsConfidential: true
        );

        var first = renderer.Shape(request);
        var paragraphs = renderer.ParagraphShapeCount;
        var second = renderer.Shape(request);

        Assert.AreEqual(paragraphs + 1, renderer.ParagraphShapeCount);
        Assert.AreEqual(0L, renderer.RetainedParagraphBytes);
        Assert.AreEqual(first.Identity, second.Identity);
    }

    [TestMethod]
    [DataRow("width")]
    [DataRow("scale")]
    [DataRow("family")]
    [DataRow("weight")]
    [DataRow("size")]
    [DataRow("language")]
    [DataRow("direction")]
    [DataRow("wrap")]
    [DataRow("height")]
    public void ParagraphReuseRespectsEveryChangedShapingInput(string input)
    {
        using var renderer = new SkiaSceneRenderer();
        var request = new TextMeasureRequest(
            "Notes with fallback \uffff and a long line to wrap.\nA second paragraph.",
            "Segoe UI",
            16,
            "en",
            TextDirection.LeftToRight,
            1,
            InlineConstraint: new(180),
            Wrap: TextWrap.WordWithGraphemeFallback
        );
        renderer.Shape(request);
        var prior = renderer.ParagraphShapeCount;
        request = input switch
        {
            "width" => request with { InlineConstraint = new(120) },
            "scale" => request with { Scale = 1.5f },
            "family" => request with { FontFamily = "Missing Lucent Font" },
            "weight" => request with { FontWeight = FontWeight.Bold },
            "size" => request with { FontSize = 20 },
            "language" => request with { Language = "fr" },
            "direction" => request with { Direction = TextDirection.RightToLeft },
            "wrap" => request with { Wrap = TextWrap.ExplicitBreaks },
            "height" => request with { BlockConstraint = new(32) },
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };
        var actual = renderer.Shape(request);
        actual.Validate(request);
        using var fresh = new SkiaSceneRenderer();
        Assert.AreEqual(
            fresh.Shape(request).Identity,
            actual.Identity,
            $"Stale paragraph data after changing {input}."
        );
        Assert.AreEqual(
            2L,
            renderer.ParagraphShapeCount - prior,
            $"Changing {input} did not invalidate both paragraph entries."
        );
    }

    [TestMethod]
    public void ParagraphCacheEvictsAtItsByteBudgetAndReleasesOnDisposal()
    {
        using var renderer = new SkiaSceneRenderer();
        var text = new string('x', 14000);
        var request = new TextMeasureRequest(
            "First " + text + "\ntail",
            "Segoe UI",
            16,
            "en",
            TextDirection.LeftToRight,
            1
        );
        renderer.Shape(request);
        for (var index = 0; index < 8; index++)
        {
            renderer.Shape(request with { Text = index + text + "\ntail" });
            Assert.IsTrue(
                renderer.RetainedParagraphBytes <= 4L * 1024 * 1024,
                $"Paragraph cache retained {renderer.RetainedParagraphBytes} bytes."
            );
        }
        var prior = renderer.ParagraphShapeCount;
        renderer.Shape(request with { Text = "First " + text + "\nchanged tail" });
        Assert.AreEqual(
            2L,
            renderer.ParagraphShapeCount - prior,
            "The byte budget did not evict the original large paragraph."
        );
        renderer.Dispose();
        Assert.AreEqual(0L, renderer.RetainedParagraphBytes);
    }

    [TestMethod]
    public void EditingOneParagraphReusesUnchangedShapingAndRebasesSourceRanges()
    {
        using var renderer = new SkiaSceneRenderer();
        var lines = Enumerable
            .Range(0, 32)
            .Select(index =>
                $"Paragraph {index}: a useful note with enough text to wrap onto another line."
            )
            .ToArray();
        var request = new TextMeasureRequest(
            string.Join('\n', lines),
            "Segoe UI",
            16,
            "en",
            TextDirection.LeftToRight,
            1,
            InlineConstraint: new(320),
            Wrap: TextWrap.WordWithGraphemeFallback
        );
        renderer.Shape(request);
        var paragraphs = renderer.ParagraphShapeCount;
        var shapers = renderer.ShaperCreationCount;
        var coverage = renderer.CoverageProbeCount;
        var fontBytes = renderer.FingerprintByteCount;
        lines[5] = "An edited prefix. " + lines[5];
        request = request with { Text = string.Join('\n', lines) };
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var edited = renderer.Shape(request);
        timer.Stop();
        Console.WriteLine(
            $"One of 32 paragraphs edited: paragraphs={renderer.ParagraphShapeCount - paragraphs}, shapers={renderer.ShaperCreationCount - shapers}, coverage={renderer.CoverageProbeCount - coverage}, fingerprintBytes={renderer.FingerprintByteCount - fontBytes}, allocations={GC.GetAllocatedBytesForCurrentThread() - allocated}, ms={timer.Elapsed.TotalMilliseconds:F2}"
        );
        edited.Validate(request);
        using var fresh = new SkiaSceneRenderer();
        var uncached = fresh.Shape(request);
        Assert.AreEqual(
            uncached.Identity,
            edited.Identity,
            "Paragraph reuse changed full-document identity or glyph/range geometry."
        );
        Assert.AreEqual(
            1L,
            renderer.ParagraphShapeCount - paragraphs,
            "A single edited paragraph reshaped unchanged paragraphs."
        );
    }

    [TestMethod]
    public void RetainedSceneRebuildsEvictedBlobsWithoutChangingPixels()
    {
        using var renderer = new SkiaSceneRenderer();
        var request = new TextMeasureRequest(
            "Retained scene — still readable",
            "Segoe UI",
            16,
            "en",
            TextDirection.LeftToRight,
            1
        );
        var shaped = renderer.Shape(request);
        var identity = new ElementIdentity(1, 1);
        var scene = new RetainedScene(
            1,
            new(320, 48, 1),
            [],
            [
                new TextSceneNode(
                    new(identity, SceneNodeKind.Text),
                    new(0, 0, 320, 48),
                    Color.Parse("#000000"),
                    shaped
                ),
            ],
            []
        );
        using var bitmap = new SKBitmap(320, 48);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        renderer.Render(scene, canvas);
        var original = bitmap.Pixels;
        var created = renderer.TextBlobCreationCount;
        Assert.IsTrue(created > 0, $"Initial scene created {created} text blobs.");
        Assert.IsTrue(original.Any(pixel => pixel != SKColors.White));

        for (var index = 0; index < 300; index++)
            renderer.Shape(request with { Text = $"Distinct cache request {index}" });

        Assert.AreEqual(
            0,
            renderer.LiveTextBlobCount,
            "Evicting the original shape must release its renderer-owned blobs."
        );
        Assert.AreEqual(0L, renderer.RetainedTextBlobBytes);
        Assert.AreNotSame(
            shaped,
            renderer.Shape(request),
            "300 requests did not evict the first shape."
        );
        canvas.Clear(SKColors.White);
        renderer.Render(scene, canvas);
        CollectionAssert.AreEqual(
            original,
            bitmap.Pixels,
            "An old retained scene changed after shape and blob eviction."
        );
        Assert.AreEqual(
            created * 2,
            renderer.TextBlobCreationCount,
            "Painting the retained immutable runs should recreate their evicted blobs exactly once."
        );
        renderer.Dispose();
        Assert.AreEqual(0, renderer.LiveTextBlobCount);
        Assert.AreEqual(0L, renderer.RetainedTextBlobBytes);
    }
}
