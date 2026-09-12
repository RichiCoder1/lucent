using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class SelectionNumericGeometryContracts
{
    [TestMethod]
    public void SliderTrackAndThumbShareTheSameCenterForEveryOrientationAndDirection()
    {
        foreach (var orientation in new[] { LayoutAxis.Row, LayoutAxis.Column })
        foreach (var direction in new[] { SliderDirection.Forward, SliderDirection.Reverse })
        {
            var graph = new ReactiveGraph();
            using var composition = new Composition(
                graph,
                $"slider-geometry-{orientation}-{direction}"
            );
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            composition.Mount(
                composition.Root,
                theme,
                Components.Slider(
                    "Value",
                    () => 5,
                    _ => { },
                    new SliderOptions(0, 10, 1, orientation: orientation, direction: direction),
                    style: orientation == LayoutAxis.Row
                        ? Style.Empty.Width(320)
                        : Style.Empty.Width(36)
                )
            );
            graph.Drain();

            using var scene = SceneLayout.Project(
                composition,
                orientation == LayoutAxis.Row ? new(360, 120, 1) : new(100, 240, 1),
                new GeometryShaper()
            );
            var track = composition.Elements().Single(element => element.Name == "track");
            var trackLine = track.Children.Single();
            var thumb = composition.Elements().Single(element => element.Name == "thumb");
            var thumbKnob = thumb.Children.Single();
            var trackBounds = scene
                .Boxes.Single(box => box.Identity.ElementId == trackLine.Id)
                .Bounds;
            var thumbBounds = scene
                .Boxes.Single(box => box.Identity.ElementId == thumbKnob.Id)
                .Bounds;

            if (orientation == LayoutAxis.Row)
            {
                Assert.AreEqual(4f, trackBounds.Height, .001f);
                Assert.AreEqual(16f, thumbBounds.Height, .001f);
                Assert.AreEqual(
                    trackBounds.Y + trackBounds.Height / 2,
                    thumbBounds.Y + thumbBounds.Height / 2,
                    .001f,
                    $"Horizontal {direction} slider centers diverged."
                );
            }
            else
            {
                Assert.AreEqual(4f, trackBounds.Width, .001f);
                Assert.AreEqual(16f, thumbBounds.Width, .001f);
                Assert.AreEqual(
                    trackBounds.X + trackBounds.Width / 2,
                    thumbBounds.X + thumbBounds.Width / 2,
                    .001f,
                    $"Vertical {direction} slider centers diverged."
                );
            }
        }
    }

    [TestMethod]
    public void RadioOptionsKeepUniformRowsAndRepresentTheirCompleteLabels()
    {
        var labels = new[] { "Native rendering", "Portable semantics", "Accessible by default" };
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "radio-geometry");
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Mount(
            composition.Root,
            theme,
            Components.RadioGroup(
                "Rendering surface",
                () =>
                    [
                        new RadioOption<string>("native", labels[0]),
                        new RadioOption<string>("portable", labels[1]),
                        new RadioOption<string>("a11y", labels[2]),
                    ],
                () => "native",
                _ => { },
                style: Style.Empty.Width(320)
            )
        );
        graph.Drain();

        using var scene = SceneLayout.Project(composition, new(340, 220, 1), new GeometryShaper());
        var radioNodes = Nodes(composition.SemanticSnapshot()!)
            .Where(node => node.Role == SemanticRole.RadioButton)
            .ToArray();
        Assert.HasCount(3, radioNodes);
        var rowBounds = radioNodes
            .Select(node =>
                scene.Boxes.Single(box => box.Identity.ElementId == node.Identity.ElementId).Bounds
            )
            .ToArray();
        Assert.AreEqual(rowBounds[0].Height, rowBounds[1].Height, .001f);
        Assert.AreEqual(rowBounds[1].Height, rowBounds[2].Height, .001f);

        foreach (var label in labels)
        {
            var text = SceneNodes(scene.Nodes)
                .OfType<TextSceneNode>()
                .Single(node => node.Text.SourceText == label)
                .Text;
            Assert.IsFalse(text.DidOverflow, $"Radio label '{label}' was clipped.");
            Assert.AreEqual(
                label.Length,
                text.Lines.Sum(line => line.Utf16Length),
                $"Radio label '{label}' did not retain its complete source text."
            );
        }
    }

    private static IEnumerable<SceneNode> SceneNodes(IEnumerable<SceneNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            var children = node switch
            {
                ClipSceneNode clip => clip.Children,
                OpacitySceneNode opacity => opacity.Children,
                _ => null,
            };
            if (children is not null)
                foreach (var child in SceneNodes(children))
                    yield return child;
        }
    }

    private static IEnumerable<SemanticSnapshot> Nodes(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var nested in Nodes(child))
            yield return nested;
    }

    private sealed class GeometryShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.Text.Length == 0)
                return new("geometry-empty", 0, 0, []);

            const float characterWidth = 8;
            if (request.Wrap == TextWrap.NoWrap)
            {
                var width = request.Text.Length * characterWidth;
                var glyphs = Enumerable
                    .Range(0, request.Text.Length)
                    .Select(index => new ShapedGlyph(
                        1,
                        (uint)index,
                        index * characterWidth,
                        0,
                        characterWidth,
                        0,
                        0
                    ))
                    .ToArray();
                var run = new ShapedRun(
                    "geometry",
                    "geometry",
                    400,
                    5,
                    0,
                    "geometry",
                    0,
                    "geometry#0",
                    request.Direction,
                    request.Language,
                    request.FontSize,
                    0,
                    request.FontSize,
                    -request.FontSize,
                    0,
                    width,
                    glyphs
                );
                return new("geometry", width, request.FontSize, [run]);
            }

            var charactersPerLine = request.InlineConstraint.Limit is { } limit
                ? Math.Max(1, (int)MathF.Floor(limit / characterWidth))
                : request.Text.Length;
            var desiredLineCount = Math.Max(
                1,
                (int)Math.Ceiling((double)request.Text.Length / charactersPerLine)
            );
            var lineCount = Math.Min(desiredLineCount, request.MaxLines ?? int.MaxValue);
            var lines = new List<ParagraphLine>(lineCount);
            var offset = 0;
            for (var index = 0; index < lineCount; index++)
            {
                var length = Math.Min(charactersPerLine, request.Text.Length - offset);
                var width = length * characterWidth;
                lines.Add(
                    new(
                        offset,
                        length,
                        index * request.FontSize,
                        (index + 1) * request.FontSize,
                        -request.FontSize,
                        0,
                        0,
                        width,
                        0,
                        false
                    )
                );
                offset += length;
            }
            var lineWidth = Math.Min(
                request.Text.Length * characterWidth,
                charactersPerLine * characterWidth
            );
            return new(
                "geometry-wrap",
                lineWidth,
                lineCount * request.FontSize,
                [],
                lines,
                lineCount < desiredLineCount,
                request.InlineConstraint,
                request.BlockConstraint
            );
        }
    }

    private sealed class ImmediatePreparer : IImagePreparer
    {
        public ValueTask<PreparedImage> PrepareAsync(
            ImagePreparationRequest request,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PreparedImage>(new RasterImage(1, 1, new byte[] { 0, 0, 0, 255 }));
    }
}
