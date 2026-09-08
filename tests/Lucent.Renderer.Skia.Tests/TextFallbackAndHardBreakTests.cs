using Lucent.Core;
using Lucent.Renderer.Skia;
using SkiaSharp;

namespace Lucent.Renderer.Skia.Tests;

[TestClass]
public sealed class TextFallbackAndHardBreakTests
{
    [TestMethod]
    [DataRow("Segoe UI")]
    [DataRow("Missing Lucent Font")]
    public void UnsupportedNoncharacterShapesAndPaintsAValidFallbackGlyph(string fontFamily)
    {
        const string source = "\uffff";
        using var renderer = new SkiaSceneRenderer();
        var request = Request(source) with { FontFamily = fontFamily };

        var shaped = renderer.Shape(request);
        shaped.Validate(request);

        Assert.AreEqual(1, shaped.Runs.Count);
        var run = shaped.Runs.Single();
        Assert.IsTrue(run.Glyphs.Count > 0);
        Assert.IsTrue(run.Glyphs.All(glyph => glyph.GlyphId != 0 && glyph.Cluster == 0));

        var identity = new ElementIdentity(1, 1);
        var bounds = new LayoutRect(0, 0, 40, 40);
        var scene = new RetainedScene(
            1,
            new(40, 40, 1),
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
        using var bitmap = new SKBitmap(40, 40);
        using var canvas = new SKCanvas(bitmap);
        renderer.Render(scene, canvas);
        Assert.IsTrue(
            bitmap.Pixels.Any(pixel => pixel.Alpha != 0),
            "The fallback glyph produced no visible pixels."
        );
    }

    [TestMethod]
    [DataRow("a\uffffb")]
    [DataRow("a\U0010ffffb")]
    public void UnsupportedGraphemeRetainsSourceClusterAtNonzeroOffset(string source)
    {
        using var renderer = new SkiaSceneRenderer();
        var request = Request(source);
        var shaped = renderer.Shape(request);
        shaped.Validate(request);
        var glyphs = shaped.Runs.SelectMany(run => run.Glyphs).ToArray();
        Assert.IsTrue(glyphs.All(glyph => glyph.GlyphId != 0));
        Assert.IsTrue(glyphs.Any(glyph => glyph.Cluster == 1));
        Assert.IsTrue(glyphs.Any(glyph => glyph.Cluster == source.Length - 1));
        Assert.AreEqual(source.Length, shaped.Lines[^1].Utf16Length);
    }

    [TestMethod]
    public void TrailingHardBreakPreservesTerminalEmptyParagraphGeometry()
    {
        using var renderer = new SkiaSceneRenderer();
        var onceRequest = Request("ab\n");
        var once = renderer.Shape(onceRequest);
        var twice = renderer.Shape(Request("ab\n\n"));
        var empty = renderer.Shape(Request(string.Empty));

        Assert.AreEqual(2, once.Lines.Count);
        Assert.AreEqual(3, twice.Lines.Count);
        Assert.AreEqual(1, empty.Lines.Count);
        Assert.AreSequenceEqual([0, 3], once.Lines.Select(line => line.Utf16Start));
        Assert.AreSequenceEqual([0, 3, 4], twice.Lines.Select(line => line.Utf16Start));
        Assert.AreEqual(0, once.Lines[^1].Utf16Length);
        Assert.AreEqual(0, twice.Lines[^1].Utf16Length);
        Assert.AreEqual(0, empty.Lines[0].Utf16Length);
        Assert.IsTrue(once.Height > once.Lines[0].Descent - once.Lines[0].Ascent);

        var finalCaret = once.CaretBounds("ab\n", 3, TextAffinity.Downstream);
        Assert.AreEqual(once.Lines[^1].Top, finalCaret.Y);
        Assert.AreEqual(0, finalCaret.X);

        var firstLine = once.Lines[0];
        var incomplete = new ShapedText(
            "incomplete-terminal-break",
            firstLine.Advance,
            firstLine.Descent - firstLine.Ascent + firstLine.Leading,
            once.Runs,
            [firstLine],
            false,
            once.InlineConstraint,
            once.BlockConstraint
        );
        Assert.ThrowsExactly<InvalidOperationException>(() => incomplete.Validate(onceRequest));

        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "trailing-break-navigation");
        using var session = new EditorSession(
            composition.Root.Scope,
            "trailing-break",
            "ab\n",
            multiline: true
        );
        session.MoveUp(once);
        Assert.AreEqual(0, session.Caret);
        session.MoveDown(once);
        Assert.AreEqual(3, session.Caret);
    }

    [TestMethod]
    public void TerminalEmptyParagraphParticipatesInAutoHeightAndOverflow()
    {
        using var renderer = new SkiaSceneRenderer();
        var unbounded = renderer.Shape(Request("ab\n"));
        var oneLineHeight = unbounded.Lines[0].Descent - unbounded.Lines[0].Ascent;

        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "terminal-break-auto-height");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(theme);
        var paragraph = composition.Child(composition.Root, "paragraph");
        paragraph.Present(
            theme,
            author: Style
                .Empty.TextWrap(TextWrap.ExplicitBreaks)
                .Set(ProjectionProperties.Text, "ab\n")
        );
        var following = composition.Child(composition.Root, "following");
        following.Present(theme, author: Style.Empty.Height(10));
        var scene = SceneLayout.Project(composition, new(100, 100, 1), renderer);
        var boxes = scene.Boxes.ToDictionary(box => box.Identity.ElementId);
        var paragraphBox = boxes[paragraph.Id];
        var paragraphText = paragraphBox.Text;

        var boundedRequest = Request("ab\n") with
        {
            BlockConstraint = new LayoutConstraint(oneLineHeight),
        };

        var bounded = renderer.Shape(boundedRequest);

        Assert.IsTrue(unbounded.Height > oneLineHeight);
        Assert.IsNotNull(paragraphText);
        Assert.AreEqual(2, paragraphText.Lines.Count);
        Assert.IsTrue(paragraphBox.Bounds.Height >= paragraphText.Height - .5f);
        Assert.AreEqual(paragraphBox.Bounds.Height, boxes[following.Id].Bounds.Y, .001f);
        Assert.IsTrue(bounded.DidOverflow);
        Assert.AreEqual(1, bounded.Lines.Count);
        Assert.AreEqual(oneLineHeight, bounded.Height, .001f);
    }

    private static TextMeasureRequest Request(string text) =>
        new(
            text,
            "Segoe UI",
            16,
            "en",
            TextDirection.LeftToRight,
            1,
            Wrap: TextWrap.ExplicitBreaks
        );
}
