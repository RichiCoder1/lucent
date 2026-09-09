using System.Globalization;
using Lucent.Core;
using Lucent.Renderer.Skia;
using SkiaSharp;

namespace Lucent.Renderer.Skia.Tests;

[TestClass]
public sealed class FractionalParagraphTests
{
    private const string ProbeText = "AAAAAAAAAA BBBBBBBBBB";
    private const float ProbeOuterWidth = 23;
    private const float ProbePadding = 0.6f;
    private const float ProbeScrollbarThickness = 12;

    [TestMethod]
    [DataRow(1f)]
    [DataRow(1.25f)]
    [DataRow(1.5f)]
    [DataRow(2f)]
    public void FractionalProbeKeepsRoundedContentWidthAndAutoHeightTogether(float scale)
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "fractional-paragraph-probe");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(
            theme,
            author: Style
                .Empty.Axis(LayoutAxis.Column)
                .CrossAlignment(LayoutAlignment.Start)
                .Padding(Insets.Symmetric(0.6f, 0))
        );
        var paragraph = composition.Child(composition.Root, "paragraph");
        _ = Controls.ScrollViewport(
            paragraph,
            theme,
            "fractional-paragraph",
            style: ParagraphStyle()
        );
        var following = composition.Child(composition.Root, "following");
        following.Present(theme, author: Style.Empty.Height(3));
        _ = composition.Input;

        using var scene = SceneLayout.Project(
            composition,
            new(ProbeOuterWidth + 8, 100, scale),
            new FractionalAdvanceShaper()
        );
        var boxes = scene.Boxes.ToDictionary(box => box.Identity.ElementId);
        var paragraphBox = boxes[paragraph.Id];
        var followingBox = boxes[following.Id];
        var text = paragraphBox.Text;

        Assert.IsTrue(
            paragraphBox.Bounds.X > 0,
            $"The fractional-origin probe did not place the paragraph inside its rounded parent padding at scale {scale}."
        );
        Assert.IsNotNull(text);
        var content = SceneLayout.ContentBounds(paragraph, paragraphBox.Bounds, scale);
        Assert.IsTrue(
            text!.InlineConstraint.Limit is { } inlineLimit
                && MathF.Abs(content.Width - inlineLimit) <= 0.001f,
            $"The final paragraph shape did not use the rounded, scrollbar-aware content width: content={content.Width}, inline={text!.InlineConstraint.Limit}."
        );
        Assert.AreEqual(
            1,
            scene.ScrollBars.Count(bar => bar.Viewport.ElementId == paragraph.Id),
            "The probe did not retain the required visible scrollbar."
        );
        Assert.IsTrue(
            text.Lines.Count >= 2,
            $"The fractional probe did not wrap at scale {scale}: content={content.Width}, inline={text.InlineConstraint.Limit}, shapedWidth={text.Width}, lines={text.Lines.Count}, text='{ProbeText}'."
        );
        Assert.IsFalse(
            text.DidOverflow,
            $"The final paragraph was clipped at scale {scale}: box={paragraphBox.Bounds}, text={text.Height}."
        );
        Assert.IsTrue(
            paragraphBox.Bounds.Height + 0.001f >= text.Height,
            $"Paragraph box height {paragraphBox.Bounds.Height} is smaller than shaped height {text.Height} at scale {scale}."
        );
        Assert.IsTrue(
            followingBox.Bounds.Y + 0.001f >= paragraphBox.Bounds.Y + paragraphBox.Bounds.Height,
            $"Following sibling overlaps the fractional paragraph at scale {scale}: paragraph={paragraphBox.Bounds}, following={followingBox.Bounds}."
        );
    }

    [TestMethod]
    public void RealSkiaWrappedParagraphTracksRoundedContentBoundsAcrossFractionalScales()
    {
        using var renderer = new SkiaSceneRenderer();
        const string word = "MMMM";
        const string family = "Segoe UI";
        var baseRequest = new TextMeasureRequest(
            word,
            family,
            16,
            "en",
            TextDirection.LeftToRight,
            1
        );
        var baseWidth = renderer.Shape(baseRequest).Width;
        Assert.IsTrue(baseWidth > 0, "The real-Skia probe word did not shape.");
        var fontSize = 16f * 10.5f / baseWidth;
        var wordRequest = baseRequest with { FontSize = fontSize };
        var wordWidth = renderer.Shape(wordRequest).Width;
        var source = word + " " + word;
        var sourceShape = renderer.Shape(wordRequest with { Text = source });
        var outerWidth = sourceShape.Width + 1.7f;

        Assert.IsTrue(
            wordWidth is > 9.5f and < 11.5f && sourceShape.Width < outerWidth - ProbePadding * 2,
            $"The real-Skia probe did not establish the intended fractional threshold: word={wordWidth}, source={sourceShape.Width}, outer={outerWidth}."
        );

        foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f })
        {
            var graph = new ReactiveGraph();
            using var composition = new Composition(graph, "fractional-real-skia");
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            composition.Root.Present(
                theme,
                author: Style
                    .Empty.Axis(LayoutAxis.Column)
                    .CrossAlignment(LayoutAlignment.Start)
                    .Padding(Insets.Symmetric(0.6f, 0))
            );
            var paragraph = composition.Child(composition.Root, "paragraph");
            _ = Controls.ScrollViewport(
                paragraph,
                theme,
                "fractional-real-skia-paragraph",
                style: Style
                    .Empty.Width(outerWidth)
                    .Padding(Insets.Uniform(ProbePadding))
                    .Set(ProjectionProperties.Text, source)
                    .Set(ProjectionProperties.TextMultiline, true)
                    .Set(TypographyProperties.FontFamily, family)
                    .Set(TypographyProperties.FontSize, fontSize)
                    .TextWrap(TextWrap.WordWithGraphemeFallback)
                    .Set(ScrollBarProperties.Visibility, ScrollBarVisibility.Always)
                    .Set(ScrollBarProperties.Thickness, ProbeScrollbarThickness)
            );
            var following = composition.Child(composition.Root, "following");
            following.Present(theme, author: Style.Empty.Height(3));
            _ = composition.Input;

            using var scene = SceneLayout.Project(
                composition,
                new(outerWidth + 8, 100, scale),
                renderer
            );
            var boxes = scene.Boxes.ToDictionary(box => box.Identity.ElementId);
            var paragraphBox = boxes[paragraph.Id];
            var followingBox = boxes[following.Id];
            var text = paragraphBox.Text;
            Assert.IsNotNull(text);
            Assert.IsTrue(
                paragraphBox.Bounds.X > 0,
                $"The real-Skia probe did not preserve the fractional paragraph origin at scale {scale}."
            );
            var content = SceneLayout.ContentBounds(paragraph, paragraphBox.Bounds, scale);
            Assert.IsTrue(
                text!.InlineConstraint.Limit is { } inlineLimit
                    && MathF.Abs(content.Width - inlineLimit) <= 0.001f,
                $"Real-Skia paragraph used a different content width at scale {scale}: content={content.Width}, inline={text!.InlineConstraint.Limit}, shapedWidth={text.Width}, lines={text.Lines.Count}."
            );
            Assert.AreEqual(
                1,
                scene.ScrollBars.Count(bar => bar.Viewport.ElementId == paragraph.Id),
                $"Real-Skia paragraph lost its visible scrollbar at scale {scale}."
            );
            Assert.IsTrue(
                text.Lines.Count > 1,
                $"Real-Skia paragraph did not wrap after scrollbar reservation at scale {scale}."
            );
            Assert.IsFalse(
                text.DidOverflow,
                $"Real-Skia paragraph overflowed its auto-height box at scale {scale}: box={paragraphBox.Bounds}, text={text.Height}."
            );
            Assert.IsTrue(
                paragraphBox.Bounds.Height + 0.001f >= text.Height,
                $"Real-Skia paragraph box is shorter than its shaped content at scale {scale}."
            );
            Assert.IsTrue(
                followingBox.Bounds.Y + 0.001f
                    >= paragraphBox.Bounds.Y + paragraphBox.Bounds.Height,
                $"Real-Skia following sibling overlaps the paragraph at scale {scale}."
            );

            using var bitmap = new SKBitmap(
                (int)MathF.Ceiling((outerWidth + 8) * scale),
                (int)MathF.Ceiling(100 * scale),
                SKColorType.Rgba8888,
                SKAlphaType.Premul
            );
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            renderer.Render(scene, canvas);
            Assert.IsTrue(
                bitmap.Pixels.Any(pixel => pixel.Alpha != 0),
                $"Real-Skia paragraph produced no painted pixels at scale {scale}."
            );
        }
    }

    private static Style ParagraphStyle() =>
        Style
            .Empty.Width(ProbeOuterWidth)
            .Padding(Insets.Uniform(ProbePadding))
            .Set(ProjectionProperties.Text, ProbeText)
            .Set(ProjectionProperties.TextMultiline, true)
            .TextWrap(TextWrap.WordWithGraphemeFallback)
            .Set(ScrollBarProperties.Visibility, ScrollBarVisibility.Always)
            .Set(ScrollBarProperties.Thickness, ProbeScrollbarThickness);

    private sealed class FractionalAdvanceShaper : ITextShaper
    {
        private const float LetterAdvance = 1.05f;
        private const float SpaceAdvance = 0.3f;
        private const float LineHeight = 10f;
        private const float Ascent = -8f;
        private const float Descent = 2f;

        public ShapedText Shape(TextMeasureRequest request)
        {
            request.Validate();
            var lines = Wrap(request.Text, request.InlineConstraint.Limit);
            var visible = lines;
            var didOverflow =
                request.InlineConstraint.Limit is { } inlineLimit
                && lines.Any(line => Width(request.Text, line) > inlineLimit + 0.001f);
            if (request.BlockConstraint.Limit is { } blockLimit)
            {
                var count = Math.Max(1, (int)MathF.Floor(blockLimit / LineHeight));
                didOverflow = count < lines.Count;
                visible = lines.Take(count).ToList();
            }

            var runs = new List<ShapedRun>(visible.Count);
            var paragraphLines = new List<ParagraphLine>(visible.Count);
            var width = 0f;
            for (var lineIndex = 0; lineIndex < visible.Count; lineIndex++)
            {
                var line = visible[lineIndex];
                var top = lineIndex * LineHeight;
                var glyphs = new List<ShapedGlyph>(line.Length);
                var x = 0f;
                for (var offset = line.Start; offset < line.Start + line.Length; offset++)
                {
                    var advance = Advance(request.Text[offset]);
                    glyphs.Add(new(1, (uint)offset, x, 0, advance, 0, 0));
                    x += advance;
                }
                var run = new ShapedRun(
                    $"fractional-{lineIndex}-{line.Start}-{line.Length}",
                    "fractional-probe",
                    (int)FontWeight.Regular,
                    0,
                    0,
                    "fractional-probe",
                    0,
                    "fractional-probe",
                    TextDirection.LeftToRight,
                    request.Language,
                    request.FontSize,
                    0,
                    top - Ascent,
                    Ascent,
                    Descent,
                    x,
                    glyphs
                );
                runs.Add(run);
                paragraphLines.Add(
                    new(line.Start, line.Length, top, top - Ascent, Ascent, Descent, 0, x, 0, false)
                );
                width = MathF.Max(width, x);
            }

            return new(
                "fractional-probe-"
                    + (
                        request.InlineConstraint.Limit?.ToString("R", CultureInfo.InvariantCulture)
                        ?? "unbounded"
                    ),
                width,
                visible.Count * LineHeight,
                runs,
                paragraphLines,
                didOverflow,
                request.InlineConstraint,
                request.BlockConstraint
            );
        }

        private static List<(int Start, int Length)> Wrap(string text, float? limit)
        {
            if (text.Length == 0)
                return [(0, 0)];
            if (limit is null)
                return [(0, text.Length)];

            var lines = new List<(int Start, int Length)>();
            var cursor = 0;
            while (cursor < text.Length)
            {
                var index = cursor;
                var width = 0f;
                var lastBreak = -1;
                while (index < text.Length)
                {
                    var next = width + Advance(text[index]);
                    if (next <= limit.Value + 0.001f)
                    {
                        width = next;
                        index++;
                        if (char.IsWhiteSpace(text[index - 1]))
                            lastBreak = index;
                        continue;
                    }

                    var cut = lastBreak > cursor ? lastBreak : index;
                    if (cut <= cursor)
                        cut = Math.Min(text.Length, cursor + 1);
                    lines.Add((cursor, cut - cursor));
                    cursor = cut;
                    break;
                }
                if (index == text.Length)
                {
                    lines.Add((cursor, text.Length - cursor));
                    cursor = text.Length;
                }
            }
            return lines;
        }

        private static float Advance(char value) =>
            char.IsWhiteSpace(value) ? SpaceAdvance : LetterAdvance;

        private static float Width(string text, (int Start, int Length) line) =>
            text.Substring(line.Start, line.Length).Sum(value => Advance(value));
    }
}
