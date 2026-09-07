using System.Globalization;
using Lucent.Core;
using Lucent.Renderer.Skia;
using SkiaSharp;

namespace Lucent.Renderer.Skia.Tests;

[TestClass]
public sealed class RendererTests
{
    [TestMethod]
    public void ShapesTextAndPaintsBoundedImmutableScene()
    {
        var corpus = new[]
        {
            new Corpus("ffi", "Calibri", "en", TextDirection.LeftToRight),
            new Corpus("q\u0307", "Segoe UI", "en", TextDirection.LeftToRight),
            new Corpus("😀", "Segoe UI Emoji", "en", TextDirection.LeftToRight),
            new Corpus("A漢", "Calibri", "ja", TextDirection.LeftToRight),
            new Corpus("漢", "Missing Lucent Font", "ja", TextDirection.LeftToRight),
            new Corpus("ffi", "Segoe UI", "en", TextDirection.LeftToRight),
            new Corpus("abc العربية", "Segoe UI", "ar", TextDirection.LeftToRight),
        };
        RetainedScene scene;
        Dictionary<string, TextSnapshot> snapshots;
        using (var shaper = new SkiaSceneRenderer())
        {
            var graph = new ReactiveGraph();
            using var composition = new Composition(graph, "renderer-proof");
            var theme = new ThemeContext(composition.Root.Scope, new Theme("renderer-proof"));
            composition.Root.Present(theme, author: Style.Empty.Set(LayoutProperties.Clip, true));
            foreach (var (item, index) in corpus.Select((item, index) => (item, index)))
            {
                var element = composition.Child(composition.Root, "corpus-" + index);
                element.Present(
                    theme,
                    author: Style
                        .Empty.Set(LayoutProperties.Height, 22f)
                        .Set(ProjectionProperties.Text, item.Text)
                        .Set(TypographyProperties.FontFamily, item.Font)
                        .Set(TypographyProperties.FontSize, 16f)
                        .Set(TypographyProperties.Language, item.Language)
                        .Set(TypographyProperties.Direction, item.Direction)
                );
            }
            scene = SceneLayout.Project(composition, new(240, 160, 1.25f), shaper);
            snapshots = scene
                .Boxes.Where(box => box.Text is not null)
                .Select(box => box.Text!)
                .ToDictionary(text => text.Identity, Snapshot);
            Assert(
                snapshots.Count == corpus.Length
                    && snapshots.Values.All(snapshot =>
                        snapshot.Glyphs.Length > 0
                        && snapshot.Glyphs.All(glyph =>
                            glyph.GlyphId != 0 && float.IsFinite(glyph.XAdvance)
                        )
                    ),
                "SceneLayout did not shape every corpus item."
            );
            var texts = scene
                .Boxes.Where(box => box.Text is not null)
                .Select(box => box.Text!)
                .ToArray();
            Assert(
                texts[0].Runs.Sum(run => run.Glyphs.Count) < 3
                    && texts[1].Runs.Sum(run => run.Glyphs.Count) <= 2
                    && texts[2].Runs.Sum(run => run.Glyphs.Count) == 1
                    && texts[3]
                        .Runs.Select(run => run.TypefaceIdentity)
                        .Distinct(StringComparer.Ordinal)
                        .Count() >= 2
                    && !texts[4]
                        .Runs.Any(run =>
                            run.Family.StartsWith("Missing Lucent Font", StringComparison.Ordinal)
                        )
                    && texts[6].Runs.Count >= 2
                    && texts[6].Runs.Any(run => run.Direction == TextDirection.RightToLeft),
                "SceneLayout corpus did not preserve ligature, combining, emoji, fallback, missing-font, culture, and mixed-direction shaping."
            );
            var oldCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                var cultureScene = SceneLayout.Project(composition, new(240, 160, 1.25f), shaper);
                Assert(
                    Same(
                        Snapshot(texts[5]),
                        Snapshot(
                            cultureScene
                                .Boxes.Where(box => box.Text is not null)
                                .Select(box => box.Text!)
                                .ElementAt(5)
                        )
                    ),
                    "SceneLayout shaping used current culture rather than explicit language."
                );
            }
            finally
            {
                CultureInfo.CurrentCulture = oldCulture;
            }
            Assert(
                !scene.Dump().Contains("ffi", StringComparison.Ordinal),
                "Scene diagnostic dump exposed user text."
            );
        }
        Assert(
            scene
                .Boxes.Where(box => box.Text is not null)
                .All(box => Same(snapshots[box.Text!.Identity], Snapshot(box.Text))),
            "Disposing the shaping renderer changed immutable glyph identities or positions."
        );
        using var renderer = new SkiaSceneRenderer();
        using var bitmap = new SKBitmap(300, 200, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            renderer.Render(scene, canvas);
        }
        var painted = Enumerable
            .Range(0, bitmap.Height)
            .SelectMany(y =>
                Enumerable.Range(0, bitmap.Width).Where(x => bitmap.GetPixel(x, y).Alpha != 0)
            )
            .ToArray();
        Assert(
            painted.Length > 0 && painted.Length < bitmap.Width * bitmap.Height,
            "New renderer did not produce bounded nonempty immutable scene output."
        );
        foreach (var box in scene.Boxes.Where(box => box.Text is not null))
            AssertPainted(bitmap, box.Bounds, scene.Viewport.Scale);

        var retainedBlobCount = renderer.LiveTextBlobCount;
        var retainedBlobBytes = renderer.RetainedTextBlobBytes;
        Assert(
            retainedBlobCount > 0
                && retainedBlobBytes > 0
                && retainedBlobBytes <= 16L * 1024 * 1024,
            "The first paint did not retain bounded native text-blob payloads."
        );
        using (var secondCanvas = new SKCanvas(bitmap))
        {
            secondCanvas.Clear(SKColors.Transparent);
            renderer.Render(scene, secondCanvas);
        }
        Assert(
            renderer.LiveTextBlobCount == retainedBlobCount
                && renderer.RetainedTextBlobBytes == retainedBlobBytes,
            "An unchanged consecutive paint rebuilt or changed cached text blobs."
        );
        renderer.Dispose();
        Assert(
            renderer.LiveTextBlobCount == 0 && renderer.RetainedTextBlobBytes == 0,
            "Disposing the renderer did not release retained native text blobs."
        );
    }

    [TestMethod]
    public void ShapesConstrainedParagraphsWithBreaksRangesAndGraphemeFallback()
    {
        using var renderer = new SkiaSceneRenderer();
        var request = new TextMeasureRequest(
            "alpha beta\ngamma",
            "Segoe UI",
            16,
            "en",
            TextDirection.LeftToRight,
            1,
            InlineConstraint: new LayoutConstraint(60),
            Wrap: TextWrap.WordWithGraphemeFallback
        );

        var shaped = renderer.Shape(request);
        Assert(
            shaped.Lines.Count >= 3,
            "Word wrapping and the explicit break did not produce lines."
        );
        Assert(
            shaped.Lines.All(line =>
                line.Utf16Start >= 0
                && line.Utf16Length >= 0
                && line.Utf16Start + line.Utf16Length <= request.Text.Length
                && float.IsFinite(line.Baseline)
                && line.Advance >= 0
            ),
            "Paragraph lines did not preserve finite UTF-16 ranges and geometry."
        );
        Assert(
            shaped.Lines.Any(line => line.HardBreak),
            "The explicit newline was not represented as a hard break."
        );
        Assert(
            shaped.Runs.Select(run => run.Baseline).Distinct().Count() > 1,
            "Wrapped runs did not use the returned per-line baselines."
        );
        Assert(
            ReferenceEquals(shaped, renderer.Shape(request)),
            "The constrained request did not use the renderer shape cache."
        );

        var grapheme = renderer.Shape(
            new TextMeasureRequest(
                "a😀e\u0301",
                "Segoe UI",
                16,
                "en",
                TextDirection.LeftToRight,
                1,
                InlineConstraint: new LayoutConstraint(2),
                Wrap: TextWrap.WordWithGraphemeFallback
            )
        );
        Assert(
            grapheme.Lines.Count == 3
                && grapheme.Lines.All(line => line.Utf16Length is 1 or 2)
                && grapheme.Lines.All(line =>
                    line.Utf16Start == 0
                    || line.Utf16Start + line.Utf16Length <= "a😀e\u0301".Length
                ),
            "Grapheme fallback split a surrogate pair or combining sequence."
        );
    }

    [TestMethod]
    public void ReportsConstrainedParagraphOverflowAndKeepsVisibleLines()
    {
        using var renderer = new SkiaSceneRenderer();
        var request = new TextMeasureRequest(
            "one two three four five six",
            "Segoe UI",
            16,
            "en",
            TextDirection.LeftToRight,
            1,
            InlineConstraint: new LayoutConstraint(70),
            BlockConstraint: new LayoutConstraint(20),
            Wrap: TextWrap.WordWithGraphemeFallback,
            MaxLines: 1
        );
        var first = renderer.Shape(request);
        Assert(first.Lines.Count == 1, "MaxLines did not retain exactly one visible line.");
        Assert(first.DidOverflow, "Line and block constraints did not report overflow.");
        Assert(first.Height <= 20.001f, "Visible paragraph height exceeded the block constraint.");
        Assert(
            !ReferenceEquals(
                first,
                renderer.Shape(request with { BlockConstraint = LayoutConstraint.Unbounded })
            ),
            "Changing a paragraph constraint incorrectly reused the constrained cache entry."
        );
    }

    [TestMethod]
    public void ShapesAndPaintsMultilineTabsWithPositiveAdvance()
    {
        using var renderer = new SkiaSceneRenderer();
        const string source = "left\tright\nnext\tline";
        var shaped = renderer.Shape(
            new TextMeasureRequest(
                source,
                "Segoe UI",
                16,
                "en",
                TextDirection.LeftToRight,
                1,
                InlineConstraint: new LayoutConstraint(240),
                Wrap: TextWrap.WordWithGraphemeFallback
            )
        );

        Assert(
            shaped.Lines.Count == 2
                && shaped
                    .Runs.SelectMany(run => run.Glyphs)
                    .All(glyph => glyph.GlyphId != 0 && float.IsFinite(glyph.XAdvance))
                && shaped.Runs.All(run => run.RunWidth > 0),
            "Multiline tab text did not produce paintable bounded line geometry."
        );
        var glyphs = shaped.Runs.SelectMany(run => run.Glyphs).ToArray();
        var beforeTab = shaped.CaretBounds(source, 4, TextAffinity.Downstream);
        var afterTab = shaped.CaretBounds(source, 5, TextAffinity.Upstream);
        Assert(
            glyphs.Count(glyph => glyph.Cluster == 4) == 4
                && glyphs.Count(glyph => glyph.Cluster == 15) == 4
                && afterTab.X > beforeTab.X
                && shaped.SelectionBounds(source, 4, 5).Single().Width > 0,
            "Fixed-width tab glyphs did not map to one selectable source grapheme."
        );
        var identity = new ElementIdentity(1, 1);
        var bounds = new LayoutRect(0, 0, 240, 80);
        var scene = new RetainedScene(
            1,
            new(240, 80, 1),
            [],
            [
                new TextSceneNode(
                    new(identity, SceneNodeKind.Text),
                    bounds,
                    Color.Parse("#000000"),
                    shaped
                ),
            ],
            []
        );
        using var bitmap = new SKBitmap(240, 80);
        using var canvas = new SKCanvas(bitmap);
        renderer.Render(scene, canvas);
        Assert(
            Enumerable
                .Range(0, bitmap.Height)
                .Any(y =>
                    Enumerable.Range(0, bitmap.Width).Any(x => bitmap.GetPixel(x, y).Alpha != 0)
                ),
            "Multiline tab text produced no painted pixels."
        );
    }

    [TestMethod]
    public void RetainsEllipsisGlyphForOverflowingParagraph()
    {
        using var renderer = new SkiaSceneRenderer();
        var request = new TextMeasureRequest(
            "one two three four",
            "Segoe UI",
            16,
            "en",
            TextDirection.LeftToRight,
            1,
            InlineConstraint: new LayoutConstraint(70),
            Wrap: TextWrap.WordWithGraphemeFallback,
            MaxLines: 1,
            Overflow: TextOverflow.Ellipsis
        );
        var shaped = renderer.Shape(request);
        var standaloneEllipsis = renderer
            .Shape(new TextMeasureRequest("…", "Segoe UI", 16, "en", TextDirection.LeftToRight, 1))
            .Runs.SelectMany(run => run.Glyphs)
            .Single()
            .GlyphId;

        Assert(shaped.DidOverflow, "Ellipsis shaping did not retain overflow state.");
        Assert(shaped.Lines.Count == 1, "Ellipsis shaping did not retain one visible line.");
        Assert(
            shaped.Width <= 70.001f,
            "The visible ellipsis line exceeded its inline constraint."
        );
        Assert(
            shaped
                .Runs.SelectMany(run => run.Glyphs)
                .Any(glyph => glyph.GlyphId == standaloneEllipsis),
            "The visible overflow payload did not contain an ellipsis glyph."
        );
        Assert(
            shaped.Lines[0].Utf16Start + shaped.Lines[0].Utf16Length <= request.Text.Length
                && !shaped.Lines[0].HardBreak,
            "Ellipsis line geometry escaped the source UTF-16 range."
        );
    }

    [TestMethod]
    public void ShapesTwentyThousandUnitsWithLinearWrapCharacterization()
    {
        using var renderer = new SkiaSceneRenderer();
        var text = string.Join(' ', Enumerable.Repeat("word", 4000));
        var request = new TextMeasureRequest(
            text,
            "Segoe UI",
            16,
            "en",
            TextDirection.LeftToRight,
            1,
            InlineConstraint: new LayoutConstraint(120),
            Wrap: TextWrap.WordWithGraphemeFallback
        );

        var timer = System.Diagnostics.Stopwatch.StartNew();
        var shaped = renderer.Shape(request);
        timer.Stop();

        Assert(
            text.Length == 19999
                && shaped.Lines.Count > 100
                && shaped.Lines.All(line =>
                    line.Utf16Start >= 0 && line.Utf16Start + line.Utf16Length <= text.Length
                )
                && !shaped.DidOverflow,
            "The 20,000-unit paragraph did not preserve bounded line geometry."
        );
        Assert(
            ReferenceEquals(shaped, renderer.Shape(request)),
            "The long constrained paragraph did not use the shape cache."
        );
        Console.WriteLine(
            "20,000-unit wrap characterization: "
                + timer.Elapsed.TotalMilliseconds.ToString(
                    "F1",
                    System.Globalization.CultureInfo.InvariantCulture
                )
                + " ms; "
                + shaped.Lines.Count
                + " lines"
        );
        var ellipsisRequest = new TextMeasureRequest(
            text,
            "Segoe UI",
            16,
            "en",
            TextDirection.LeftToRight,
            1,
            InlineConstraint: new LayoutConstraint(70),
            Wrap: TextWrap.NoWrap,
            MaxLines: 1,
            Overflow: TextOverflow.Ellipsis
        );
        var ellipsisTimer = System.Diagnostics.Stopwatch.StartNew();
        var ellipsized = renderer.Shape(ellipsisRequest);
        ellipsisTimer.Stop();
        Assert(
            ellipsized.DidOverflow && ellipsized.Lines.Count == 1 && ellipsized.Width <= 70.001f,
            "The 20,000-unit narrow no-wrap ellipsis did not retain bounded output."
        );
        Console.WriteLine(
            "20,000-unit ellipsis characterization: "
                + ellipsisTimer.Elapsed.TotalMilliseconds.ToString(
                    "F1",
                    System.Globalization.CultureInfo.InvariantCulture
                )
                + " ms; "
                + ellipsized.Lines[0].Utf16Length
                + " UTF-16 units"
        );
    }

    [TestMethod]
    public void ShapesLongNoteReviewSampleWithoutRepeatedParagraphWork()
    {
        using var renderer = new SkiaSceneRenderer();
        var paragraph =
            "A quiet place to think. Keep the useful detail and leave room for the next idea.\n\nUnicode samples: café, e\u0301, 👩🏽‍💻, العربية, עברית.\n\tA tab-indented line.\n\n";
        var text = string.Concat(Enumerable.Repeat(paragraph, 160));
        text = text[..(char.IsHighSurrogate(text[19_999]) ? 19_999 : 20_000)];
        var request = new TextMeasureRequest(
            text,
            "Segoe UI",
            15,
            "en",
            TextDirection.LeftToRight,
            1,
            InlineConstraint: new LayoutConstraint(500),
            BlockConstraint: LayoutConstraint.Unbounded,
            Wrap: TextWrap.WordWithGraphemeFallback
        );

        var timer = System.Diagnostics.Stopwatch.StartNew();
        var shaped = renderer.Shape(request);
        timer.Stop();

        Assert(
            shaped.Lines.Count > 100
                && shaped.Lines.All(line =>
                    line.Utf16Start >= 0 && line.Utf16Start + line.Utf16Length <= text.Length
                )
                && !shaped.DidOverflow,
            "The synthetic Long note did not preserve its complete wrapped geometry."
        );
        Assert(
            renderer.ParagraphShapeCount <= 10,
            "Repeated review paragraphs performed redundant shaping work."
        );
        Console.WriteLine($"Review long note shaping: {timer.Elapsed.TotalMilliseconds:F1} ms.");
    }

    [TestMethod]
    public void ShapesUniqueLongNoteWithCompleteGeometry()
    {
        using var renderer = new SkiaSceneRenderer();
        var text = string.Concat(
            Enumerable
                .Range(0, 320)
                .Select(index =>
                    $"Line {index:D4} keeps distinct words and numbers {index * 17:D6} so no paragraph payload repeats during this measurement.\n"
                )
        )[..20_000];
        var request = new TextMeasureRequest(
            text,
            "Segoe UI",
            15,
            "en",
            TextDirection.LeftToRight,
            1,
            InlineConstraint: new LayoutConstraint(500),
            BlockConstraint: LayoutConstraint.Unbounded,
            Wrap: TextWrap.WordWithGraphemeFallback
        );

        var timer = System.Diagnostics.Stopwatch.StartNew();
        var shaped = renderer.Shape(request);
        timer.Stop();

        Assert(
            shaped.Lines.Count > 250
                && shaped.Lines[^1].Utf16Start + shaped.Lines[^1].Utf16Length == text.Length
                && shaped.Lines.All(line =>
                    line.Utf16Start >= 0 && line.Utf16Start + line.Utf16Length <= text.Length
                ),
            "The unique Long note did not preserve its complete wrapped geometry."
        );
        Console.WriteLine($"Unique long note shaping: {timer.Elapsed.TotalMilliseconds:F1} ms.");
    }

    [TestMethod]
    public void LongWrappedParagraphReusesTextBlobsAcrossClippedPaints()
    {
        using var renderer = new SkiaSceneRenderer();
        var text = string.Concat(Enumerable.Repeat("word ", 4000));
        var shaped = renderer.Shape(
            new TextMeasureRequest(
                text,
                "Segoe UI",
                16,
                "en",
                TextDirection.LeftToRight,
                1,
                InlineConstraint: new LayoutConstraint(320),
                Wrap: TextWrap.WordWithGraphemeFallback
            )
        );
        var identity = new ElementIdentity(1, 1);
        var bounds = new LayoutRect(0, 0, 320, 160);
        var scene = new RetainedScene(
            1,
            new(320, 160, 1),
            [],
            [
                new ClipSceneNode(
                    new(identity, SceneNodeKind.Clip),
                    bounds,
                    [
                        new TextSceneNode(
                            new(identity, SceneNodeKind.Text),
                            bounds,
                            Color.Parse("#000000"),
                            shaped
                        ),
                    ]
                ),
            ],
            []
        );
        using var bitmap = new SKBitmap(320, 160);
        using var canvas = new SKCanvas(bitmap);

        renderer.Render(scene, canvas);
        var createdAfterFirstPaint = renderer.TextBlobCreationCount;
        Assert(
            shaped.Runs.Count > 256 && createdAfterFirstPaint == shaped.Runs.Count,
            "The clipped paragraph did not exercise more than the former count cap."
        );

        renderer.Render(scene, canvas);

        Assert(
            renderer.TextBlobCreationCount == createdAfterFirstPaint,
            "The second unchanged clipped paint regenerated text blobs."
        );
        Console.WriteLine(
            $"20,000-unit clipped paint: {shaped.Runs.Count} runs; "
                + $"{renderer.LiveTextBlobCount} retained blobs; "
                + $"{renderer.RetainedTextBlobBytes} estimated bytes."
        );
    }

    [TestMethod]
    public void MultilineEditorReusesTwentyThousandUnitShapingDuringSelectionAndPaint()
    {
        using var composition = new Composition(new ReactiveGraph(), "long-editor");
        using var renderer = new SkiaSceneRenderer();
        using var session = new EditorSession(
            composition.Root.Scope,
            "long-note",
            string.Concat(Enumerable.Repeat("word ", 4000)),
            multiline: true
        );
        composition.Mount(
            composition.Root,
            new ThemeContext(composition.Root.Scope, ControlThemes.Light),
            Components.TextArea(session: session, style: Style.Empty.Width(320).Height(160))
        );
        RetainedScene Project()
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                composition.Flush();
                var scene = SceneLayout.Project(composition, new(320, 160, 1), renderer);
                if (composition.Input.SetScene(scene))
                    return scene;
            }
            throw new InvalidOperationException("Long editor scene did not settle.");
        }
        var initial = Project();
        var shaped = initial.Boxes.Single(box => box.Text is not null).Text!;
        Assert(shaped.Lines.Count > 10, "Long editor did not wrap its constrained text.");
        using var bitmap = new SKBitmap(320, 160);
        using var canvas = new SKCanvas(bitmap);
        renderer.Render(initial, canvas);
        var createdAfterFirstPaint = renderer.TextBlobCreationCount;
        Assert(
            shaped.Runs.Count > 256 && createdAfterFirstPaint == shaped.Runs.Count,
            "The long wrapped paragraph did not exercise more than the former count cap."
        );
        session.MoveHome();
        _ = Project();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (var index = 0; index < 30; index++)
        {
            session.MoveRight(extend: true);
            var scene = Project();
            Assert(
                ReferenceEquals(shaped, scene.Boxes.Single(box => box.Text is not null).Text),
                "Caret/selection movement reshaped an unchanged 20,000-unit document."
            );
            renderer.Render(scene, canvas);
        }
        timer.Stop();
        Assert(
            renderer.TextBlobCreationCount == createdAfterFirstPaint,
            "Unchanged long-editor paints regenerated native text blobs."
        );
        Console.WriteLine(
            $"20,000-unit editor: 30 selection/projection/paint steps; {timer.Elapsed.TotalMilliseconds:F1} ms; {GC.GetAllocatedBytesForCurrentThread() - allocated} managed bytes; {renderer.LiveTextBlobCount} retained blobs."
        );
        session.Text += "edited";
        var edited = Project().Boxes.Single(box => box.Text is not null).Text!;
        Assert(!ReferenceEquals(shaped, edited), "A committed edit reused stale shaping.");
    }

    [TestMethod]
    public void ShapeCacheEvictsLargeResultsAtTheByteBudget()
    {
        using var renderer = new SkiaSceneRenderer();
        var text = new string('a', 20_000);
        var request = new TextMeasureRequest(
            text,
            "Segoe UI",
            16,
            "en",
            TextDirection.LeftToRight,
            1
        );
        var first = renderer.Shape(request);
        var run = first.Runs.Single();
        var advanceSum = run.Glyphs.Sum(glyph => (double)glyph.XAdvance);
        var last = run.Glyphs[^1];
        var finalPositionedEdge = last.X - last.XOffset + last.XAdvance;
        Assert(
            Math.Abs(advanceSum - run.RunWidth) <= .001
                && MathF.Abs(finalPositionedEdge - run.RunWidth) <= .001f,
            "The 20,000-unit run did not retain one canonical width for advances and positioned edges."
        );
        for (var index = 1; index <= 20; index++)
            _ = renderer.Shape(request with { FontSize = 16 + index });

        Assert(
            !ReferenceEquals(first, renderer.Shape(request)),
            "The byte-cost shape cache retained a large first result beyond its 16 MiB budget."
        );
    }

    [TestMethod]
    public void RendersHairlineDividerAndInsetFocusRingAtDeclaredScale()
    {
        foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f })
        {
            using var composition = new Composition(new ReactiveGraph(), "decorations");
            using var theme = new ThemeContext(composition.Root.Scope, new Theme("decorations"));
            composition.Root.Present(
                theme,
                author: Style
                    .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                    .Width(12)
                    .Height(12)
            );
            var divider = composition.Child(composition.Root, "divider");
            divider.Present(
                theme,
                author: Style
                    .Empty.Width(12)
                    .Height(4)
                    .Border(Border.Hairline(Color.Parse("#CC2200"), BorderSides.Bottom))
            );
            var focused = composition.Child(composition.Root, "focused");
            focused.Present(
                theme,
                author: Style
                    .Empty.Width(12)
                    .Height(8)
                    .Clip(true)
                    .FocusRing(FocusRing.Inset(Color.Parse("#2255CC"), 2))
            );
            using var renderer = new SkiaSceneRenderer();
            var scene = SceneLayout.Project(composition, new(12, 12, scale), renderer);
            using var bitmap = new SKBitmap(
                (int)MathF.Ceiling(12 * scale),
                (int)MathF.Ceiling(12 * scale),
                SKColorType.Rgba8888,
                SKAlphaType.Premul
            );
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            renderer.Render(scene, canvas);

            var middleX = (int)MathF.Floor(6 * scale);
            var redRows = Enumerable
                .Range(0, bitmap.Height)
                .Where(y => bitmap.GetPixel(middleX, y).Red > 180)
                .ToArray();
            var focusTop = (int)MathF.Round(4 * scale, MidpointRounding.AwayFromZero);
            Assert(
                redRows.Length == 1
                    && bitmap.GetPixel(middleX, focusTop).Blue > 180
                    && bitmap.GetPixel(middleX, focusTop).Alpha == byte.MaxValue,
                "Projected hairline/focus pixels changed at scale " + scale
            );
        }
    }

    [TestMethod]
    public void ResolvesFontWeightInShapeCacheAndPaintsAntialiasedTextAtDeclaredScales()
    {
        using var renderer = new SkiaSceneRenderer();
        var regularRequest = new TextMeasureRequest(
            "Weighted heading",
            "Segoe UI",
            16,
            "en",
            TextDirection.LeftToRight,
            1,
            FontWeight: FontWeight.Regular
        );
        var boldRequest = regularRequest with { FontWeight = FontWeight.Bold };
        var regular = renderer.Shape(regularRequest);
        var bold = renderer.Shape(boldRequest);
        Assert(
            !ReferenceEquals(regular, bold)
                && ReferenceEquals(bold, renderer.Shape(boldRequest))
                && regular.Identity != bold.Identity
                && regular.Runs.All(run => run.Weight < (int)FontWeight.Bold)
                && bold.Runs.All(run => run.Weight >= (int)FontWeight.SemiBold),
            "Requested font weight did not select a distinct face or shape-cache entry."
        );

        foreach (var scale in new[] { 1f, 1.5f, 2f })
        {
            using var bitmap = new SKBitmap(
                (int)MathF.Ceiling(160 * scale),
                (int)MathF.Ceiling(28 * scale),
                SKColorType.Rgba8888,
                SKAlphaType.Premul
            );
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            renderer.Render(
                new RetainedScene(
                    1,
                    new(160, 28, scale),
                    [],
                    [
                        new TextSceneNode(
                            new(new(1, 1), SceneNodeKind.Text),
                            new(0, 0, 160, 28),
                            Color.Parse("#111111"),
                            bold
                        ),
                    ],
                    []
                ),
                canvas
            );
            var alphas = Enumerable
                .Range(0, bitmap.Height)
                .SelectMany(y =>
                    Enumerable.Range(0, bitmap.Width).Select(x => bitmap.GetPixel(x, y).Alpha)
                )
                .ToArray();
            Assert(
                alphas.Any(alpha => alpha is > 0 and < byte.MaxValue)
                    && alphas.Any(alpha => alpha > 220),
                "Explicit antialiased font edging did not produce stable edge and interior coverage at scale "
                    + scale
            );
        }
    }

    [TestMethod]
    public void RendersRoundedBackgroundFocusAndChildClipAtDeclaredScales()
    {
        foreach (var scale in new[] { 1f, 1.5f, 2f })
        {
            using var composition = new Composition(new ReactiveGraph(), "rounded");
            using var theme = new ThemeContext(composition.Root.Scope, new Theme("rounded"));
            composition.Root.Present(
                theme,
                author: Style
                    .Empty.Width(20)
                    .Height(20)
                    .Background(Color.Parse("#22AA44"))
                    .CornerRadius(6)
                    .Clip(true)
                    .FocusRing(FocusRing.Inset(Color.Parse("#2255CC"), 1))
            );
            var child = composition.Child(composition.Root, "child");
            child.Present(
                theme,
                author: Style.Empty.Width(20).Height(20).Background(Color.Parse("#E8D24A"))
            );
            using var renderer = new SkiaSceneRenderer();
            var scene = SceneLayout.Project(composition, new(20, 20, scale), renderer);
            using var bitmap = new SKBitmap(
                (int)MathF.Ceiling(20 * scale),
                (int)MathF.Ceiling(20 * scale),
                SKColorType.Rgba8888,
                SKAlphaType.Premul
            );
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            renderer.Render(scene, canvas);

            var corner = bitmap.GetPixel(0, 0);
            var center = bitmap.GetPixel(
                (int)MathF.Floor(10 * scale),
                (int)MathF.Floor(10 * scale)
            );
            var focus = bitmap.GetPixel(
                (int)MathF.Floor(10 * scale),
                Math.Max(0, (int)MathF.Floor(.5f * scale))
            );
            Assert(
                corner.Alpha == 0
                    && center.Red > 180
                    && center.Green > 170
                    && focus.Blue > focus.Red
                    && focus.Blue > focus.Green,
                $"Rounded fill/focus/clip pixels were incoherent at scale {scale}: corner={corner}, center={center}, focus={focus}."
            );
        }
    }

    [TestMethod]
    public void RendersGradientStopsAndClipping()
    {
        using var renderer = new SkiaSceneRenderer();
        AssertGradient(renderer);
    }

    [TestMethod]
    public void ComposesOpacityLayers()
    {
        using var renderer = new SkiaSceneRenderer();
        AssertOpacity(renderer);
    }

    [TestMethod]
    public void AppliesOpacityWithLayoutAndClipping()
    {
        using var renderer = new SkiaSceneRenderer();
        AssertOpacityLayout(renderer);
    }

    static TextSnapshot Snapshot(ShapedText text) =>
        new(
            text.Identity,
            text.Runs.Select(run => new RunSnapshot(
                    run.Identity,
                    run.OriginX,
                    run.Baseline,
                    run.Glyphs.ToArray()
                ))
                .ToArray()
        );

    static bool Same(TextSnapshot left, TextSnapshot right) =>
        left.Identity == right.Identity
        && left.Runs.Length == right.Runs.Length
        && left.Runs.Zip(right.Runs)
            .All(pair =>
                pair.First.Identity == pair.Second.Identity
                && pair.First.OriginX == pair.Second.OriginX
                && pair.First.Baseline == pair.Second.Baseline
                && pair.First.Glyphs.SequenceEqual(pair.Second.Glyphs)
            );

    static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    static void AssertGradient(SkiaSceneRenderer renderer)
    {
        Brush brush = new LinearGradient(
            new(0, 0),
            new(1, 0),
            [new(0, Color.Parse("#FF0000")), new(1, Color.Parse("#0000FF"))]
        );
        SKColor? priorMidpoint = null;
        foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f })
        {
            var width = (int)MathF.Ceiling(16 * scale);
            var height = (int)MathF.Ceiling(4 * scale);
            using var colorSpace = SKColorSpace.CreateSrgb();
            using var pixels = new SKBitmap(
                new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace)
            );
            using (var canvas = new SKCanvas(pixels))
            {
                canvas.Clear(SKColors.Transparent);
                renderer.Render(
                    new RetainedScene(
                        1,
                        new(16, 4, scale),
                        [],
                        [
                            new PaintSceneNode(
                                new(new(1, 1), SceneNodeKind.Paint),
                                new(0, 0, 16, 4),
                                brush
                            ),
                        ],
                        []
                    ),
                    canvas
                );
            }
            var left = pixels.GetPixel(Math.Max(0, (int)MathF.Floor(4 * scale)), height / 2);
            var midpoint = pixels.GetPixel(
                Math.Min(width - 1, (int)MathF.Floor(8 * scale)),
                height / 2
            );
            var right = pixels.GetPixel(
                Math.Min(width - 1, (int)MathF.Floor(12 * scale)),
                height / 2
            );
            Assert(
                left.Red > left.Blue
                    && right.Blue > right.Red
                    && midpoint.Red is > 90 and < 170
                    && midpoint.Blue is > 90 and < 170
                    && midpoint.Green < 8
                    && midpoint.Alpha == byte.MaxValue,
                $"sRGB gradient did not paint expected opaque color progression: left={left}, midpoint={midpoint}, right={right}, scale={scale}."
            );
            if (priorMidpoint is { } prior)
                Assert(
                    Math.Abs(prior.Red - midpoint.Red) < 20
                        && Math.Abs(prior.Blue - midpoint.Blue) < 20,
                    "Gradient midpoint changed across declared scales."
                );
            priorMidpoint = midpoint;
        }

        Brush hard = new LinearGradient(
            new(.25f, .25f),
            new(.75f, .75f),
            [
                new(0, Color.Parse("#FF0000")),
                new(.5f, Color.Parse("#FF0000")),
                new(.5f, Color.Parse("#0000FF")),
                new(1, Color.Parse("#0000FF")),
            ]
        );
        using var hardSpace = SKColorSpace.CreateSrgb();
        using var hardPixels = new SKBitmap(
            new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul, hardSpace)
        );
        using (var canvas = new SKCanvas(hardPixels))
        {
            canvas.Clear(SKColors.Transparent);
            renderer.Render(Scene(hard, 16, 16), canvas);
        }
        Assert(
            hardPixels.GetPixel(7, 7).Red > 240
                && hardPixels.GetPixel(7, 7).Blue < 8
                && hardPixels.GetPixel(8, 8).Blue > 240
                && hardPixels.GetPixel(8, 8).Red < 8,
            "Adjacent pixels did not preserve the box-relative equal-position hard-stop discontinuity."
        );

        using var alphaSpace = SKColorSpace.CreateSrgb();
        using var alphaPixels = new SKBitmap(
            new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul, alphaSpace)
        );
        using (var canvas = new SKCanvas(alphaPixels))
        {
            canvas.Clear(SKColors.Transparent);
            renderer.Render(Scene(Brush.Solid(Color.Parse("#00FF0080")), 4, 4), canvas);
        }
        var alpha = alphaPixels.GetPixel(2, 2);
        Assert(
            alpha.Green > 240 && alpha.Alpha is >= 127 and <= 129,
            "Partially transparent solid Brush did not preserve paint alpha."
        );
    }

    static RetainedScene Scene(Brush brush, float width, float height) =>
        new(
            1,
            new(width, height, 1),
            [],
            [
                new PaintSceneNode(
                    new(new(1, 1), SceneNodeKind.Paint),
                    new(0, 0, width, height),
                    brush
                ),
            ],
            []
        );

    static void AssertOpacity(SkiaSceneRenderer renderer)
    {
        var identity = new ElementIdentity(1, 1);
        var blue = Brush.Solid(Color.Parse("#0000FF"));
        var overlap = new OpacitySceneNode(
            new(identity, SceneNodeKind.Opacity),
            new(0, 0, 8, 4),
            .5f,
            [
                new PaintSceneNode(new(identity, SceneNodeKind.Paint), new(0, 0, 6, 4), blue),
                new PaintSceneNode(new(identity, SceneNodeKind.Paint), new(2, 0, 6, 4), blue),
            ]
        );
        using var pixels = new SKBitmap(8, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(pixels))
        {
            canvas.Clear(SKColors.Transparent);
            renderer.Render(new RetainedScene(1, new(8, 4, 1), [], [overlap], []), canvas);
        }
        Assert(
            pixels.GetPixel(1, 2).Alpha is >= 127 and <= 129
                && pixels.GetPixel(3, 2).Alpha is >= 127 and <= 129,
            "Opacity was applied to each overlapping paint instead of once to the composited group."
        );

        var nested = new OpacitySceneNode(
            new(identity, SceneNodeKind.Opacity),
            new(0, 0, 4, 4),
            .5f,
            [
                new OpacitySceneNode(
                    new(identity, SceneNodeKind.Opacity),
                    new(0, 0, 4, 4),
                    .5f,
                    [
                        new PaintSceneNode(
                            new(identity, SceneNodeKind.Paint),
                            new(0, 0, 4, 4),
                            Brush.Solid(Color.Parse("#FF0000"))
                        ),
                    ]
                ),
            ]
        );
        using var nestedPixels = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(nestedPixels))
        {
            canvas.Clear(SKColors.Transparent);
            renderer.Render(new RetainedScene(1, new(4, 4, 1), [], [nested], []), canvas);
        }
        Assert(
            nestedPixels.GetPixel(2, 2).Alpha is >= 63 and <= 65,
            "Nested opacity did not multiply."
        );

        foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f })
        {
            var size = (int)(4 * scale);
            using var zeroPixels = new SKBitmap(
                size,
                size,
                SKColorType.Rgba8888,
                SKAlphaType.Premul
            );
            using var canvas = new SKCanvas(zeroPixels);
            canvas.Clear(SKColors.Transparent);
            renderer.Render(
                new RetainedScene(
                    1,
                    new(4, 4, scale),
                    [],
                    [
                        new OpacitySceneNode(
                            new(identity, SceneNodeKind.Opacity),
                            new(0, 0, 4, 4),
                            0,
                            [
                                new PaintSceneNode(
                                    new(identity, SceneNodeKind.Paint),
                                    new(0, 0, 4, 4),
                                    blue
                                ),
                            ]
                        ),
                    ],
                    []
                ),
                canvas
            );
            Assert(
                Enumerable
                    .Range(0, size)
                    .SelectMany(y =>
                        Enumerable.Range(0, size).Select(x => zeroPixels.GetPixel(x, y).Alpha)
                    )
                    .All(alpha => alpha == 0),
                "Opacity zero painted pixels at scale " + scale
            );
        }
        Assert(
            renderer.LiveTextBlobCount == 0,
            "Opacity groups retained renderer resources after a frame."
        );
    }

    static void AssertOpacityLayout(SkiaSceneRenderer renderer)
    {
        foreach (var clip in new[] { false, true })
        foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f })
        {
            var graph = new ReactiveGraph();
            using var composition = new Composition(graph, "opacity-layout");
            var theme = new ThemeContext(composition.Root.Scope, new Theme("opacity-layout"));
            composition.Root.Present(
                theme,
                author: Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
            );
            var owner = composition.Child(composition.Root, "owner");
            owner.Present(
                theme,
                author: Style
                    .Empty.Set(LayoutProperties.Width, 4f)
                    .Set(LayoutProperties.Height, 4f)
                    .Set(LayoutProperties.Clip, clip)
                    .Set(VisualProperties.Opacity, .5f)
            );
            var child = composition.Child(owner, "overflow");
            child.Present(
                theme,
                author: Style
                    .Empty.Set(LayoutProperties.Width, 8f)
                    .Set(LayoutProperties.Height, 4f)
                    .Set(VisualProperties.Background, Color.Parse("#0000FF"))
            );
            var retained = SceneLayout.Project(composition, new(12, 8, scale), renderer);
            var group = retained.Nodes.OfType<OpacitySceneNode>().Single();
            var width = (int)MathF.Ceiling(12 * scale);
            var height = (int)MathF.Ceiling(8 * scale);
            using var pixels = new SKBitmap(
                width,
                height,
                SKColorType.Rgba8888,
                SKAlphaType.Premul
            );
            using (var canvas = new SKCanvas(pixels))
            {
                canvas.Clear(SKColors.Transparent);
                renderer.Render(retained, canvas);
            }
            var inside = pixels.GetPixel((int)MathF.Floor(2 * scale), (int)MathF.Floor(2 * scale));
            var overflow = pixels.GetPixel(
                (int)MathF.Floor(6 * scale),
                (int)MathF.Floor(2 * scale)
            );
            Assert(
                inside.Alpha is >= 127 and <= 129
                    && (
                        clip
                            ? overflow.Alpha == 0 && group.Bounds.Width == 4
                            : overflow.Alpha is >= 127 and <= 129 && group.Bounds.Width == 8
                    ),
                "SceneLayout opacity overflow/clip pixels or bounds changed at scale "
                    + scale
                    + " clip="
                    + clip
            );
        }
    }

    static void AssertPainted(SKBitmap bitmap, LayoutRect bounds, float scale)
    {
        var left = Math.Clamp((int)MathF.Floor(bounds.X * scale), 0, bitmap.Width);
        var right = Math.Clamp(
            (int)MathF.Ceiling((bounds.X + bounds.Width) * scale),
            0,
            bitmap.Width
        );
        var top = Math.Clamp((int)MathF.Floor(bounds.Y * scale), 0, bitmap.Height);
        var bottom = Math.Clamp(
            (int)MathF.Ceiling((bounds.Y + bounds.Height) * scale),
            0,
            bitmap.Height
        );
        Assert(
            right > left
                && bottom > top
                && Enumerable
                    .Range(top, bottom - top)
                    .SelectMany(y =>
                        Enumerable.Range(left, right - left).Select(x => bitmap.GetPixel(x, y))
                    )
                    .Any(pixel => pixel.Alpha != 0),
            "Renderer did not paint inside a required corpus element's device-scaled bounds."
        );
    }

    readonly record struct Corpus(
        string Text,
        string Font,
        string Language,
        TextDirection Direction
    );

    readonly record struct RunSnapshot(
        string Identity,
        float OriginX,
        float Baseline,
        ShapedGlyph[] Glyphs
    );

    readonly record struct TextSnapshot(string Identity, RunSnapshot[] Runs)
    {
        internal ShapedGlyph[] Glyphs => Runs.SelectMany(run => run.Glyphs).ToArray();
    }
}
