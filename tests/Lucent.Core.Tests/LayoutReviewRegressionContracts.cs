using System.Globalization;
using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class LayoutReviewRegressionContracts
{
    [TestMethod]
    public void CustomMeasurementHonorsExplicitSizeBeforeConstraint()
    {
        var algorithm = new ConstrainedMeasurementAlgorithm();
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "layout-review-measure-child");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(theme, author: Style.Empty.Algorithm(algorithm));
        var child = composition.Child(composition.Root, "fixed-child");
        child.Present(theme, author: Style.Empty.Width(20).Height(10));

        var scene = SceneLayout.Project(composition, new(100, 40, 1), new EmptyShaper());

        Assert.AreEqual(new LayoutSize(15, 10), algorithm.Measured);
        Assert.AreEqual(
            new LayoutRect(0, 0, 15, 10),
            scene.Boxes.Single(box => box.Identity.ElementId == child.Id).Bounds
        );
    }

    [TestMethod]
    public void GridStretchPreservesExplicitChildSizeRegardlessOfAxis()
    {
        foreach (var axis in Enum.GetValues<LayoutAxis>())
        {
            var graph = new ReactiveGraph();
            using var composition = new Composition(graph, "layout-review-grid-" + axis);
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            composition.Root.Present(
                theme,
                author: Style
                    .Empty.Mode(LayoutMode.Grid)
                    .Axis(axis)
                    .Columns(GridTracks.Create(GridTrack.Fixed(50)))
                    .Rows(GridTracks.Create(GridTrack.Fixed(20)))
                    .CrossAlignment(LayoutAlignment.Stretch)
            );
            var child = composition.Child(composition.Root, "fixed-grid-child");
            child.Present(theme, author: Style.Empty.Width(10).Height(10).GridPlacement(new(0, 0)));

            var scene = SceneLayout.Project(composition, new(50, 20, 1), new EmptyShaper());

            Assert.AreEqual(
                new LayoutRect(0, 0, 10, 10),
                scene.Boxes.Single(box => box.Identity.ElementId == child.Id).Bounds,
                $"Grid Stretch changed an explicit child size for {axis}."
            );
        }
    }

    [TestMethod]
    public void CustomPlacementAssignmentsAreIndependentOfContainerAxis()
    {
        foreach (var axis in Enum.GetValues<LayoutAxis>())
        foreach (var widthAssigned in new[] { false, true })
        foreach (var heightAssigned in new[] { false, true })
        {
            var graph = new ReactiveGraph();
            using var composition = new Composition(
                graph,
                $"layout-review-custom-{axis}-{widthAssigned}-{heightAssigned}"
            );
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            composition.Root.Present(
                theme,
                author: Style
                    .Empty.Axis(axis)
                    .Algorithm(new FixedPlacementAlgorithm(widthAssigned, heightAssigned))
            );
            var child = composition.Child(composition.Root, "fixed-custom-child");
            child.Present(theme, author: Style.Empty.Width(10).Height(10));

            var scene = SceneLayout.Project(composition, new(100, 100, 1), new EmptyShaper());
            var expected = new LayoutRect(0, 0, widthAssigned ? 50 : 10, heightAssigned ? 40 : 10);

            Assert.AreEqual(
                expected,
                scene.Boxes.Single(box => box.Identity.ElementId == child.Id).Bounds,
                $"Custom assignment flags were interpreted through {axis}."
            );
        }
    }

    [TestMethod]
    public void NonWrappingRowRemeasuresShrinkableParagraphAtAssignedWidth()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "layout-review-row-auto-height");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(
            theme,
            author: Style.Empty.Axis(LayoutAxis.Column).CrossAlignment(LayoutAlignment.Stretch)
        );
        var row = composition.Child(composition.Root, "non-wrapping-row");
        row.Present(theme, author: Style.Empty.Axis(LayoutAxis.Row));
        var paragraph = composition.Child(row, "paragraph");
        paragraph.Present(
            theme,
            author: Style
                .Empty.Set(ProjectionProperties.Text, "one two three four five")
                .TextWrap(TextWrap.WordWithGraphemeFallback)
                .MainShrink(1)
                .MinWidth(1)
        );
        var following = composition.Child(composition.Root, "following");
        following.Present(theme, author: Style.Empty.Height(10));
        var shaper = new WrappingProbeShaper();

        var scene = SceneLayout.Project(composition, new(10, 100, 1), shaper);
        var boxes = scene.Boxes.ToDictionary(box => box.Identity.ElementId);
        var shaped = boxes[paragraph.Id].Text;

        Assert.AreEqual(new LayoutRect(0, 0, 10, 30), boxes[row.Id].Bounds);
        Assert.AreEqual(new LayoutRect(0, 0, 10, 30), boxes[paragraph.Id].Bounds);
        Assert.AreEqual(30, boxes[following.Id].Bounds.Y);
        Assert.IsNotNull(shaped, "The paragraph did not produce shaped text.");
        Assert.AreEqual(3, shaped!.Lines.Count);
        Assert.IsFalse(shaped.DidOverflow);
    }

    [TestMethod]
    public void ConditionalGridBranchReceivesCellHeightStretch()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "layout-review-conditional-grid");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(
            theme,
            author: Style
                .Empty.Mode(LayoutMode.Grid)
                .Columns(GridTracks.Create(GridTrack.Fixed(50), GridTrack.Fixed(50)))
                .Rows(GridTracks.Create(GridTrack.Fixed(100)))
                .CrossAlignment(LayoutAlignment.Stretch)
        );
        var direct = composition.Child(composition.Root, "direct-grid-child");
        direct.Present(theme, author: Style.Empty.GridPlacement(new(0, 0)));
        var active = graph.Signal(true, "conditional-active");
        var region = composition.When(
            composition.Root,
            "conditional-grid-child",
            () => active.Value,
            context =>
            {
                var child = context.Element("branch");
                child.Present(theme, author: Style.Empty.GridPlacement(new(0, 1)));
                return child;
            }
        );
        graph.Drain();

        Assert.IsNotNull(region.Active);
        var scene = SceneLayout.Project(composition, new(100, 100, 1), new EmptyShaper());
        var boxes = scene.Boxes.ToDictionary(box => box.Identity.ElementId);

        Assert.AreEqual(new LayoutRect(0, 0, 50, 100), boxes[direct.Id].Bounds);
        Assert.AreEqual(
            new LayoutRect(50, 0, 50, 100),
            boxes[region.Active!.Id].Bounds,
            "A transparent conditional Grid branch lost its assigned cell height."
        );
    }

    private sealed class ConstrainedMeasurementAlgorithm : LayoutAlgorithm
    {
        public ConstrainedMeasurementAlgorithm()
            : base("layout-review-measure") { }

        public LayoutSize Measured { get; private set; }

        public override LayoutAlgorithmResult Layout(LayoutAlgorithmContext context)
        {
            Measured = context.MeasureChild(
                context.Children[0],
                new(new LayoutConstraint(15), LayoutConstraint.Unbounded)
            );
            return new(
                Measured,
                new LayoutChildPlacement(
                    context.Children[0].Index,
                    new(0, 0, Measured.Width, Measured.Height)
                )
            );
        }
    }

    private sealed class FixedPlacementAlgorithm(bool widthAssigned, bool heightAssigned)
        : LayoutAlgorithm("layout-review-assignment")
    {
        public override LayoutAlgorithmResult Layout(LayoutAlgorithmContext context) =>
            new(
                new LayoutSize(50, 40),
                new LayoutChildPlacement(
                    context.Children[0].Index,
                    new(0, 0, 50, 40),
                    widthAssigned,
                    heightAssigned
                )
            );
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }

    private sealed class WrappingProbeShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            var charactersPerLine = request.InlineConstraint.Limit is { } inline
                ? Math.Max(1, (int)MathF.Floor(inline))
                : request.Text.Length;
            var desiredLineCount = Math.Max(
                1,
                (int)Math.Ceiling((double)request.Text.Length / charactersPerLine)
            );
            var lineCount = Math.Min(desiredLineCount, request.MaxLines ?? int.MaxValue);
            if (request.BlockConstraint.Limit is { } block)
                lineCount = Math.Min(lineCount, Math.Max(1, (int)MathF.Floor(block / 10)));
            var lines = new List<ParagraphLine>(lineCount);
            var offset = 0;
            for (var index = 0; index < lineCount; index++)
            {
                var length = Math.Min(charactersPerLine, request.Text.Length - offset);
                lines.Add(
                    new(offset, length, index * 10, index * 10 + 10, -10, 0, 0, length, 0, false)
                );
                offset += length;
            }
            var width = Math.Min(request.Text.Length, charactersPerLine);
            return new ShapedText(
                "wrap-" + charactersPerLine.ToString(CultureInfo.InvariantCulture),
                width,
                lineCount * 10,
                [],
                lines,
                lineCount < desiredLineCount,
                request.InlineConstraint,
                request.BlockConstraint
            );
        }
    }
}
