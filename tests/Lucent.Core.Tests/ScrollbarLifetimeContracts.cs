using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ScrollbarLifetimeContracts
{
    [TestMethod]
    public void RemovingOverflowDuringThumbDragReleasesCapture()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "scrollbar-drag-lifetime");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(theme, author: Style.Empty.Width(120).Height(50));
        var viewport = composition.Child(composition.Root, "viewport");
        Controls.ScrollViewport(
            viewport,
            theme,
            "Viewport",
            style: Style.Empty.Width(120).Height(40)
        );
        var contentHeight = graph.Signal(180f, "content-height");
        var content = composition.Child(viewport, "content");
        Controls.Panel(
            content,
            theme,
            "Content",
            Style.Empty.Width(108).Height(() => contentHeight.Value)
        );

        var router = composition.Input;
        var scene = Install(composition, graph, router);
        var bar = scene.ScrollBars.Single();
        var point = (X: bar.Thumb.X + 2, Y: bar.Thumb.Y + bar.Thumb.Height / 2);
        Assert.IsTrue(
            router
                .DispatchPointer(
                    new(PointerCommandKind.Down, 1, point.X, point.Y, PointerButton.Primary)
                )
                .Handled,
            "Thumb press was not captured."
        );
        Assert.IsTrue(router.IsScrollbarPressed(bar.Viewport));

        contentHeight.Value = 20;
        scene = Install(composition, graph, router);
        Assert.AreEqual(
            0,
            scene.ScrollBars.Count,
            "The nonoverflowing viewport retained its Auto bar."
        );
        Assert.IsFalse(router.IsScrollbarPressed(bar.Viewport));
        Assert.IsFalse(
            router.Dump().Contains("capture pointer=1", StringComparison.Ordinal),
            "Removing a scrollbar retained its pointer capture."
        );

        _ = router.DispatchPointer(new(PointerCommandKind.Move, 1, point.X, point.Y + 4));
        _ = router.DispatchPointer(new(PointerCommandKind.Up, 1, point.X, point.Y + 4));
        Assert.IsFalse(router.IsScrollbarPressed(bar.Viewport));
    }

    [TestMethod]
    public void ThumbHoverRequiresPointerOverScrollbar()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "scrollbar-hover-lifetime");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(theme, author: Style.Empty.Width(120).Height(50));
        var viewport = composition.Child(composition.Root, "viewport");
        Controls.ScrollViewport(
            viewport,
            theme,
            "Viewport",
            style: Style.Empty.Width(120).Height(40)
        );
        var content = composition.Child(viewport, "content");
        Controls.Panel(content, theme, "Content", Style.Empty.Width(108).Height(180));

        var router = composition.Input;
        var scene = Install(composition, graph, router);
        var normal = scene.ScrollBars.Single().ThumbBrush;
        _ = router.DispatchPointer(new(PointerCommandKind.Move, 2, 10, 10));
        scene = Install(composition, graph, router);
        var contentHover = scene.ScrollBars.Single();
        Assert.IsFalse(router.IsScrollbarHovered(contentHover.Viewport));
        Assert.AreEqual(
            normal,
            contentHover.ThumbBrush,
            "Viewport content incorrectly hovered its thumb."
        );

        _ = router.DispatchPointer(
            new(
                PointerCommandKind.Move,
                2,
                contentHover.Thumb.X + 2,
                contentHover.Thumb.Y + contentHover.Thumb.Height / 2
            )
        );
        scene = Install(composition, graph, router);
        var barHover = scene.ScrollBars.Single();
        Assert.IsTrue(router.IsScrollbarHovered(barHover.Viewport));
        Assert.AreEqual(
            barHover.HoverThumbBrush,
            barHover.ThumbBrush,
            "Pointer over the scrollbar did not select its hover brush."
        );
    }

    [TestMethod]
    public void OverlappingScrollbarsRouteToLastPaintedViewport()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "scrollbar-z-order");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(
            theme,
            author: Style
                .Empty.Width(120)
                .Height(40)
                .Mode(LayoutMode.Grid)
                .Columns(GridTracks.Create(GridTrack.Fraction()))
                .Rows(GridTracks.Create(GridTrack.Fraction()))
        );
        for (var index = 0; index < 2; index++)
        {
            var viewport = composition.Child(composition.Root, "viewport-" + index);
            Controls.ScrollViewport(
                viewport,
                theme,
                "Viewport " + index,
                style: Style.Empty.GridPlacement(new(0, 0))
            );
            var content = composition.Child(viewport, "content");
            Controls.Panel(content, theme, "Content", Style.Empty.Width(108).Height(180));
        }

        var router = composition.Input;
        var scene = Install(composition, graph, router);
        Assert.AreEqual(2, scene.ScrollBars.Count);
        var lower = scene.ScrollBars[0];
        var upper = scene.ScrollBars[1];
        Assert.AreEqual(lower.Track, upper.Track, "The test scrollbars do not overlap.");
        var result = router.DispatchPointer(
            new(
                PointerCommandKind.Down,
                3,
                upper.Track.X + 2,
                upper.Track.Y + upper.Track.Height - 2,
                PointerButton.Primary
            )
        );
        Assert.AreEqual(
            upper.Viewport,
            result.Target,
            "Scrollbar hit order disagreed with the last-painted retained bar."
        );
    }

    private static RetainedScene Install(
        Composition composition,
        ReactiveGraph graph,
        InputRouter router
    )
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            graph.Drain();
            var scene = SceneLayout.Project(composition, new(120, 50, 1), new TestShaper());
            if (router.SetScene(scene))
                return scene;
        }
        throw new InvalidOperationException("Scrollbar input projection did not settle.");
    }

    private sealed class TestShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.Text.Length == 0)
                return new("empty", 0, 0, []);
            var glyph = new ShapedGlyph(1, 0, 0, 0, request.Text.Length, 0, 0);
            var run = new ShapedRun(
                "scrollbar-lifetime",
                "scrollbar-lifetime",
                400,
                5,
                0,
                "scrollbar-lifetime",
                0,
                "scrollbar-lifetime#0",
                request.Direction,
                request.Language,
                request.FontSize,
                0,
                request.FontSize,
                -request.FontSize,
                0,
                request.Text.Length,
                [glyph]
            );
            return new("scrollbar-lifetime", request.Text.Length, request.FontSize, [run]);
        }
    }
}
