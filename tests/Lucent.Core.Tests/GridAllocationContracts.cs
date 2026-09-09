using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class GridAllocationContracts
{
    [TestMethod]
    public void FractionTracksShareRemainderAfterExplicitMinimums()
    {
        using var fixture = new GridFixture(
            GridTrack.MinMax(100, GridTrack.Fraction()),
            GridTrack.Fraction()
        );
        var bounds = fixture.Project(300);
        Assert.AreEqual(new LayoutRect(0, 0, 200, 10), bounds[fixture.First.Id]);
        Assert.AreEqual(new LayoutRect(200, 0, 100, 10), bounds[fixture.Second.Id]);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void SpanningContentRedistributesDeficitAfterTrackReachesMaximum(bool cappedFirst)
    {
        var capped = GridTrack.MinMax(0, GridTrack.Fixed(50));
        using var fixture = new GridFixture(
            cappedFirst ? capped : GridTrack.Content(),
            cappedFirst ? GridTrack.Content() : capped
        );
        fixture.AddContent(200, 2);
        var bounds = fixture.Project(300);
        var firstWidth = cappedFirst ? 50 : 150;
        Assert.AreEqual(new LayoutRect(0, 0, firstWidth, 10), bounds[fixture.First.Id]);
        Assert.AreEqual(
            new LayoutRect(firstWidth, 0, 200 - firstWidth, 10),
            bounds[fixture.Second.Id]
        );
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void FractionTracksHaveNoImplicitContentMinimumIncludingSpans(int span)
    {
        using var fixture = new GridFixture(GridTrack.Fraction(), GridTrack.Fraction());
        var content = fixture.AddContent(400, span);
        var bounds = fixture.Project(100);
        Assert.AreEqual(new LayoutRect(0, 0, 50, 10), bounds[fixture.First.Id]);
        Assert.AreEqual(new LayoutRect(50, 0, 50, 10), bounds[fixture.Second.Id]);
        Assert.AreEqual(400f, bounds[content.Id].Width);
    }

    [TestMethod]
    [DataRow(LayoutAxis.Row, LayoutAlignment.Center, -20f)]
    [DataRow(LayoutAxis.Row, LayoutAlignment.End, -40f)]
    [DataRow(LayoutAxis.Column, LayoutAlignment.Center, -20f)]
    [DataRow(LayoutAxis.Column, LayoutAlignment.End, -40f)]
    public void OversizedExplicitFlexChildAlignsUsingItsActualCrossSize(
        LayoutAxis axis,
        LayoutAlignment alignment,
        float offset
    )
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "flex-overflow-alignment");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(theme, author: Style.Empty.Axis(axis).CrossAlignment(alignment));
        var child = composition.Child(composition.Root, "oversized");
        child.Present(theme, author: Style.Empty.Width(80).Height(80));
        using var scene = SceneLayout.Project(composition, new(40, 40, 1), new EmptyShaper());
        var bounds = scene.Boxes.Single(box => box.Identity.ElementId == child.Id).Bounds;
        Assert.AreEqual(offset, axis == LayoutAxis.Row ? bounds.Y : bounds.X);
        Assert.AreEqual(80f, axis == LayoutAxis.Row ? bounds.Height : bounds.Width);
    }

    private sealed class GridFixture : IDisposable
    {
        private readonly Composition _composition;
        private readonly ThemeContext _theme;

        internal GridFixture(params GridTrack[] columns)
        {
            _composition = new Composition(new ReactiveGraph(), "grid-allocation-contract");
            _theme = new ThemeContext(_composition.Root.Scope, ControlThemes.Light);
            _composition.Root.Present(
                _theme,
                author: Style
                    .Empty.Mode(LayoutMode.Grid)
                    .Columns(GridTracks.Create(columns))
                    .Rows(GridTracks.Create(GridTrack.Fixed(10)))
                    .CrossAlignment(LayoutAlignment.Stretch)
            );
            First = _composition.Child(_composition.Root, "first-track");
            First.Present(_theme, author: Style.Empty.GridPlacement(new(0, 0)));
            Second = _composition.Child(_composition.Root, "second-track");
            Second.Present(_theme, author: Style.Empty.GridPlacement(new(0, 1)));
        }

        internal Element First { get; }
        internal Element Second { get; }

        internal Element AddContent(float width, int span)
        {
            var child = _composition.Child(_composition.Root, "contributing-content");
            child.Present(
                _theme,
                author: Style
                    .Empty.Width(width)
                    .Height(10)
                    .GridPlacement(new(0, 0, columnSpan: span))
            );
            return child;
        }

        internal Dictionary<long, LayoutRect> Project(float width)
        {
            using var scene = SceneLayout.Project(
                _composition,
                new(width, 10, 1),
                new EmptyShaper()
            );
            return scene.Boxes.ToDictionary(box => box.Identity.ElementId, box => box.Bounds);
        }

        public void Dispose()
        {
            _theme.Dispose();
            _composition.Dispose();
        }
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }
}
