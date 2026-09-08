using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class AutoHeightWrapContracts
{
    [TestMethod]
    public void WrappingRowExpandsAutoHeightBeforeFollowingSibling()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "auto-height-wrap");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(
            theme,
            author: Style.Empty.Axis(LayoutAxis.Column).CrossAlignment(LayoutAlignment.Stretch)
        );
        var row = composition.Child(composition.Root, "wrapping-row");
        row.Present(
            theme,
            author: Style
                .Empty.Axis(LayoutAxis.Row)
                .Spacing(4)
                .RowGap(6)
                .Wrap(true)
                .CrossAlignment(LayoutAlignment.Start)
        );
        var items = Enumerable
            .Range(0, 3)
            .Select(index =>
            {
                var item = composition.Child(row, "item-" + index);
                item.Present(theme, author: Style.Empty.Width(50).Height(10));
                return item;
            })
            .ToArray();
        var following = composition.Child(composition.Root, "following");
        following.Present(theme, author: Style.Empty.Height(2));

        var scene = SceneLayout.Project(composition, new(110, 100, 1), new EmptyShaper());
        var boxes = scene.Boxes.ToDictionary(box => box.Identity.ElementId, box => box.Bounds);
        var lastItemBottom = boxes[items[^1].Id].Y + boxes[items[^1].Id].Height;

        Assert.IsTrue(
            boxes[row.Id].Height >= lastItemBottom - boxes[row.Id].Y
                && boxes[following.Id].Y >= lastItemBottom,
            $"Following sibling overlaps wrapped row: row={boxes[row.Id]}, "
                + $"last={boxes[items[^1].Id]}, following={boxes[following.Id]}."
        );
    }

    [TestMethod]
    [DataRow(1f)]
    [DataRow(1.5f)]
    [DataRow(2f)]
    public void NestedAutoHeightTracksWideNarrowWideWithPaddingAndGaps(float scale)
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "nested-auto-height-wrap");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(
            theme,
            author: Style
                .Empty.Axis(LayoutAxis.Column)
                .Spacing(5)
                .CrossAlignment(LayoutAlignment.Stretch)
        );
        var wrapper = composition.Child(composition.Root, "wrapper");
        wrapper.Present(
            theme,
            author: Style
                .Empty.Axis(LayoutAxis.Column)
                .Padding(new Insets(3, 4, 5, 6))
                .CrossAlignment(LayoutAlignment.Stretch)
        );
        var nested = composition.Child(wrapper, "nested");
        nested.Present(
            theme,
            author: Style
                .Empty.Axis(LayoutAxis.Column)
                .Spacing(7)
                .Padding(Insets.Uniform(2))
                .CrossAlignment(LayoutAlignment.Stretch)
        );
        var row = composition.Child(nested, "wrapping-row");
        row.Present(
            theme,
            author: Style
                .Empty.Axis(LayoutAxis.Row)
                .Spacing(4)
                .RowGap(6)
                .Padding(Insets.Symmetric(2, 1))
                .Wrap(true)
                .CrossAlignment(LayoutAlignment.Start)
        );
        var items = Enumerable
            .Range(0, 3)
            .Select(index =>
            {
                var item = composition.Child(row, "item-" + index);
                item.Present(theme, author: Style.Empty.Width(42).Height(10));
                return item;
            })
            .ToArray();
        var divider = composition.Child(nested, "divider");
        divider.Present(theme, author: Style.Empty.Height(3));
        var following = composition.Child(composition.Root, "following");
        following.Present(theme, author: Style.Empty.Height(4));

        var wideBefore = SceneLayout.Project(composition, new(220, 160, scale), new EmptyShaper());
        var narrow = SceneLayout.Project(composition, new(100, 160, scale), new EmptyShaper());
        var wideAfter = SceneLayout.Project(composition, new(220, 160, scale), new EmptyShaper());
        var before = wideBefore.Boxes.ToDictionary(
            box => box.Identity.ElementId,
            box => box.Bounds
        );
        var atNarrow = narrow.Boxes.ToDictionary(box => box.Identity.ElementId, box => box.Bounds);
        var after = wideAfter.Boxes.ToDictionary(box => box.Identity.ElementId, box => box.Bounds);
        var lastBottom = atNarrow[items[^1].Id].Y + atNarrow[items[^1].Id].Height;
        var rowBottom = atNarrow[row.Id].Y + atNarrow[row.Id].Height;
        var dividerBottom = atNarrow[divider.Id].Y + atNarrow[divider.Id].Height;
        var wrapperBottom = atNarrow[wrapper.Id].Y + atNarrow[wrapper.Id].Height;

        Assert.IsTrue(lastBottom <= rowBottom, "Nested wrapper clipped the final wrap line.");
        Assert.IsTrue(
            atNarrow[divider.Id].Y >= rowBottom + 7,
            "Nested column spacing did not follow the wrapped row's corrected height."
        );
        Assert.IsTrue(
            dividerBottom <= wrapperBottom,
            "Nested padding did not contribute to the wrapper's corrected auto height."
        );
        Assert.IsTrue(
            atNarrow[following.Id].Y >= wrapperBottom + 5,
            "Outer sibling overlapped the corrected nested wrapper."
        );
        Assert.IsTrue(
            before[row.Id].Height < atNarrow[row.Id].Height
                && after[row.Id] == before[row.Id]
                && wideAfter.Boxes.Count == wideBefore.Boxes.Count,
            "Wide-narrow-wide projection retained stale wrap geometry or changed retained structure."
        );
    }

    [TestMethod]
    public void ExplicitHeightRemainsAuthoritativeForWrappingRow()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "explicit-wrap-height");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(
            theme,
            author: Style.Empty.Axis(LayoutAxis.Column).CrossAlignment(LayoutAlignment.Stretch)
        );
        var row = composition.Child(composition.Root, "wrapping-row");
        row.Present(
            theme,
            author: Style.Empty.Axis(LayoutAxis.Row).Height(12).Spacing(4).RowGap(6).Wrap(true)
        );
        for (var index = 0; index < 3; index++)
        {
            var item = composition.Child(row, "item-" + index);
            item.Present(theme, author: Style.Empty.Width(50).Height(10));
        }
        var following = composition.Child(composition.Root, "following");
        following.Present(theme, author: Style.Empty.Height(2));

        var scene = SceneLayout.Project(composition, new(110, 100, 1), new EmptyShaper());
        var boxes = scene.Boxes.ToDictionary(box => box.Identity.ElementId, box => box.Bounds);

        Assert.AreEqual(12, boxes[row.Id].Height);
        Assert.AreEqual(12, boxes[following.Id].Y);
    }

    [TestMethod]
    public void CorrectedAutoHeightParticipatesInBoundedGrowAndShrink()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "growing-auto-height-wrap");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(
            theme,
            author: Style.Empty.Axis(LayoutAxis.Column).CrossAlignment(LayoutAlignment.Stretch)
        );
        var row = composition.Child(composition.Root, "wrapping-row");
        row.Present(
            theme,
            author: Style
                .Empty.Axis(LayoutAxis.Row)
                .Spacing(4)
                .RowGap(6)
                .Wrap(true)
                .MainGrow(1)
                .MainShrink(1)
                .MinHeight(26)
        );
        var items = Enumerable
            .Range(0, 3)
            .Select(index =>
            {
                var item = composition.Child(row, "item-" + index);
                item.Present(theme, author: Style.Empty.Width(50).Height(10));
                return item;
            })
            .ToArray();
        var following = composition.Child(composition.Root, "following");
        following.Present(theme, author: Style.Empty.Height(10));

        foreach (var height in new[] { 80f, 30f })
        {
            var scene = SceneLayout.Project(composition, new(110, height, 1), new EmptyShaper());
            var boxes = scene.Boxes.ToDictionary(box => box.Identity.ElementId, box => box.Bounds);
            var lastBottom = boxes[items[^1].Id].Y + boxes[items[^1].Id].Height;
            Assert.IsTrue(
                boxes[row.Id].Height >= 26
                    && boxes[following.Id].Y >= lastBottom
                    && boxes[following.Id].Y == boxes[row.Id].Height,
                $"Corrected wrap height did not participate safely at viewport height {height}."
            );
        }
    }

    [TestMethod]
    public void WidthConstraintsAreAppliedBeforeNestedWrapMeasurement()
    {
        AssertNestedWrap("max-width", null, 0, 110, 500, 110, 26);
        AssertNestedWrap("min-width", null, 200, float.MaxValue, 110, 200, 10);
        AssertNestedWrap("explicit-clamped-width", 200, 0, 110, 500, 110, 26);
    }

    [TestMethod]
    public void ConstrainedNestedParagraphReusesProjectionMeasurements()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "cached-wrap-measurement");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(
            theme,
            author: Style.Empty.Axis(LayoutAxis.Column).CrossAlignment(LayoutAlignment.Stretch)
        );
        var row = composition.Child(composition.Root, "wrapping-row");
        row.Present(theme, author: Style.Empty.Axis(LayoutAxis.Row).Wrap(true));
        var paragraph = composition.Child(row, "paragraph");
        paragraph.Present(
            theme,
            author: Style
                .Empty.Set(ProjectionProperties.Text, "abcdefghij")
                .TextWrap(TextWrap.WordWithGraphemeFallback)
                .MainShrink(1)
                .MinWidth(1)
        );
        var shaper = new CachingProbeShaper();

        var scene = SceneLayout.Project(composition, new(5, 100, 1), shaper);
        var boxes = scene.Boxes.ToDictionary(box => box.Identity.ElementId, box => box.Bounds);

        Assert.AreEqual(20, boxes[row.Id].Height);
        Assert.AreEqual(20, boxes[paragraph.Id].Height);
        Assert.AreEqual(
            shaper.Requests.Count,
            shaper.Requests.Distinct().Count(),
            "A constrained paragraph was shaped more than once for an identical request."
        );
    }

    private static void AssertNestedWrap(
        string name,
        float? width,
        float minWidth,
        float maxWidth,
        float viewportWidth,
        float expectedWidth,
        float expectedHeight
    )
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, name);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(
            theme,
            author: Style.Empty.Axis(LayoutAxis.Column).CrossAlignment(LayoutAlignment.Stretch)
        );
        var wrapper = composition.Child(composition.Root, "wrapper");
        var wrapperStyle = Style
            .Empty.Axis(LayoutAxis.Column)
            .MinWidth(minWidth)
            .MaxWidth(maxWidth)
            .CrossAlignment(LayoutAlignment.Stretch);
        if (width is { } authoredWidth)
            wrapperStyle = wrapperStyle.Width(authoredWidth);
        wrapper.Present(theme, author: wrapperStyle);
        var row = composition.Child(wrapper, "wrapping-row");
        row.Present(
            theme,
            author: Style
                .Empty.Axis(LayoutAxis.Row)
                .Spacing(4)
                .RowGap(6)
                .Wrap(true)
                .CrossAlignment(LayoutAlignment.Start)
        );
        for (var index = 0; index < 3; index++)
        {
            var item = composition.Child(row, "item-" + index);
            item.Present(theme, author: Style.Empty.Width(50).Height(10));
        }
        var following = composition.Child(composition.Root, "following");
        following.Present(theme, author: Style.Empty.Height(2));

        var scene = SceneLayout.Project(composition, new(viewportWidth, 100, 1), new EmptyShaper());
        var boxes = scene.Boxes.ToDictionary(box => box.Identity.ElementId, box => box.Bounds);

        Assert.AreEqual(expectedWidth, boxes[wrapper.Id].Width, $"{name} width was not clamped.");
        Assert.AreEqual(
            expectedHeight,
            boxes[row.Id].Height,
            $"{name} used an unclamped width while measuring wrap height."
        );
        Assert.AreEqual(
            boxes[wrapper.Id].Height,
            boxes[following.Id].Y,
            $"{name} did not propagate its corrected height to the following sibling."
        );
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }

    private sealed class CachingProbeShaper : ITextShaper
    {
        public List<TextMeasureRequest> Requests { get; } = [];

        public ShapedText Shape(TextMeasureRequest request)
        {
            Requests.Add(request);
            var charactersPerLine = request.InlineConstraint.Limit is { } inline
                ? Math.Max(1, (int)MathF.Floor(inline))
                : request.Text.Length;
            var lineCount = Math.Max(
                1,
                (int)Math.Ceiling((double)request.Text.Length / charactersPerLine)
            );
            var lines = Enumerable
                .Range(0, lineCount)
                .Select(index =>
                {
                    var start = index * charactersPerLine;
                    var length = Math.Min(charactersPerLine, request.Text.Length - start);
                    return new ParagraphLine(
                        start,
                        length,
                        index * 10,
                        index * 10 + 10,
                        -10,
                        0,
                        0,
                        length,
                        0,
                        false
                    );
                })
                .ToArray();
            return new(
                "cache-" + charactersPerLine,
                Math.Min(request.Text.Length, charactersPerLine),
                lineCount * 10,
                [],
                lines,
                false,
                request.InlineConstraint,
                request.BlockConstraint
            );
        }
    }
}
