using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ScrollbarContracts
{
    [TestMethod]
    public void OverflowProjectsGutterAndInteractiveNodes()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "scrollbar-project");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        Controls.Panel(
            composition.Root,
            theme,
            "root",
            Style.Empty.Set(LayoutProperties.Width, 120f).Set(LayoutProperties.Height, 60f)
        );
        var viewport = composition.Child(composition.Root, "viewport");
        Controls.ScrollViewport(
            viewport,
            theme,
            "Viewport",
            style: Style.Empty.Set(LayoutProperties.Width, 120f).Set(LayoutProperties.Height, 40f)
        );
        var content = composition.Child(viewport, "content");
        Controls.Panel(
            content,
            theme,
            "Content",
            Style.Empty.Set(LayoutProperties.Width, 108f).Set(LayoutProperties.Height, 180f)
        );
        graph.Drain();

        var scene = SceneLayout.Project(composition, new(120, 60, 1), new TestShaper());
        var bar = scene.ScrollBars.Single();
        var input = scene.Input.Single(item => item.Identity.ElementId == viewport.Id);
        Assert.IsTrue(
            input.ChildClipBounds is { Width: 108, Height: 40 }
                && bar.Track == new LayoutRect(108, 0, 12, 40)
                && bar.Thumb.Height < bar.Track.Height
                && bar.ThumbBrush.Color is { A: 255 }
                && bar.HoverThumbBrush != bar.ThumbBrush
                && bar.PressedThumbBrush != bar.HoverThumbBrush
                && scene
                    .Nodes.OfType<PaintSceneNode>()
                    .Any(node => node.Identity.Kind == SceneNodeKind.ScrollBarTrack)
                && scene
                    .Nodes.OfType<PaintSceneNode>()
                    .Any(node => node.Identity.Kind == SceneNodeKind.ScrollBarThumb),
            "Overflow did not reserve the scrollbar gutter or project track/thumb nodes."
        );
    }

    [TestMethod]
    public void TrackPagesAndThumbDragUseViewportScrollState()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "scrollbar-input");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        Controls.Panel(
            composition.Root,
            theme,
            "root",
            Style.Empty.Set(LayoutProperties.Width, 120f).Set(LayoutProperties.Height, 60f)
        );
        var viewport = composition.Child(composition.Root, "viewport");
        var state = Controls.ScrollViewport(
            viewport,
            theme,
            "Viewport",
            style: Style.Empty.Set(LayoutProperties.Width, 120f).Set(LayoutProperties.Height, 40f)
        );
        var content = composition.Child(viewport, "content");
        Controls.Panel(
            content,
            theme,
            "Content",
            Style.Empty.Set(LayoutProperties.Width, 108f).Set(LayoutProperties.Height, 180f)
        );
        graph.Drain();
        var router = composition.Input;
        var scene = SceneLayout.Project(composition, new(120, 60, 1), new TestShaper());
        Assert.IsTrue(router.SetScene(scene), "Scrollbar input scene was rejected.");
        var bar = scene.ScrollBars.Single();

        var pagePoint = new PointerCommand(
            PointerCommandKind.Down,
            1,
            bar.Track.X + 2,
            bar.Track.Y + bar.Track.Height - 2,
            PointerButton.Primary
        );
        Assert.IsTrue(
            router.DispatchPointer(pagePoint).Handled && state.Offset.Y > 0,
            "Track page click did not scroll."
        );
        graph.Drain();
        scene = SceneLayout.Project(composition, new(120, 60, 1), new TestShaper());
        Assert.IsTrue(router.SetScene(scene), "Paged scrollbar scene was rejected.");
        bar = scene.ScrollBars.Single();
        var thumbY = bar.Thumb.Y + bar.Thumb.Height / 2;
        Assert.IsTrue(
            router
                .DispatchPointer(
                    new(PointerCommandKind.Down, 2, bar.Thumb.X + 2, thumbY, PointerButton.Primary)
                )
                .Handled,
            "Thumb press was not captured."
        );
        var beforeDrag = state.Offset.Y;
        _ = router.DispatchPointer(new(PointerCommandKind.Move, 2, bar.Thumb.X + 2, thumbY + 8));
        Assert.IsTrue(
            state.Offset.Y > beforeDrag,
            "Thumb drag did not use bounded viewport scrolling."
        );
        Assert.IsTrue(
            router
                .DispatchPointer(new(PointerCommandKind.Up, 2, bar.Thumb.X + 2, thumbY + 8))
                .Handled,
            "Thumb release was not routed."
        );
    }

    [TestMethod]
    public void ScrollbarPaintStaysInsideAncestorClipButOutsideItsContentClip()
    {
        using var composition = new Composition(new ReactiveGraph(), "nested-scrollbar");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        Controls.Panel(
            composition.Root,
            theme,
            "root",
            Style.Empty.Width(120).Height(30).Set(LayoutProperties.Clip, true)
        );
        var viewport = composition.Child(composition.Root, "viewport");
        Controls.ScrollViewport(
            viewport,
            theme,
            "viewport",
            style: Style.Empty.Width(120).Height(60)
        );
        var content = composition.Child(viewport, "content");
        Controls.Panel(content, theme, "content", Style.Empty.Width(108).Height(180));
        var scene = SceneLayout.Project(composition, new(120, 30, 1), new TestShaper());
        var ancestorClip = scene.Nodes.OfType<ClipSceneNode>().Single();
        var viewportClip = ancestorClip.Children.OfType<ClipSceneNode>().Single();
        var thumb = ancestorClip
            .Children.OfType<PaintSceneNode>()
            .Single(node => node.Identity.Kind == SceneNodeKind.ScrollBarThumb);
        Assert.AreEqual(30f, ancestorClip.Bounds.Height);
        Assert.AreEqual(108f, viewportClip.Bounds.Width);
        Assert.AreEqual(108f, thumb.Bounds.X);
        Assert.IsFalse(
            viewportClip
                .Children.OfType<PaintSceneNode>()
                .Any(node => node.Identity.Kind == SceneNodeKind.ScrollBarThumb)
        );
        Assert.IsTrue(composition.Input.SetScene(scene));
        Assert.IsFalse(
            composition
                .Input.DispatchPointer(
                    new(PointerCommandKind.Down, 1, 114, 40, PointerButton.Primary)
                )
                .Handled,
            "Clipped scrollbar accepted input outside its ancestor."
        );
    }

    [TestMethod]
    public void MultilineShapingReservesTheScrollbarGutter()
    {
        using var composition = new Composition(new ReactiveGraph(), "gutter-wrap");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        Controls.Panel(composition.Root, theme, "root", Style.Empty.Width(120).Height(60));
        var editor = composition.Child(composition.Root, "editor");
        Controls.TextArea(
            editor,
            theme,
            "Notes",
            "abcdef",
            style: Style.Empty.Width(120).Height(60).Padding(Insets.Uniform(10))
        );
        var shaper = new TestShaper();
        var scene = SceneLayout.Project(composition, new(120, 60, 1), shaper);
        var content = scene
            .Input.Single(item => item.Identity.ElementId == editor.Id)
            .ChildClipBounds!.Value;
        Assert.AreEqual(88f, content.Width);
        Assert.IsTrue(
            shaper.Requests.Any(request =>
                request.Text == "abcdef" && request.InlineConstraint.Limit == content.Width
            ),
            "TextArea shaped against width that includes its scrollbar gutter."
        );
    }

    private sealed class TestShaper : ITextShaper
    {
        public List<TextMeasureRequest> Requests { get; } = [];

        public ShapedText Shape(TextMeasureRequest request)
        {
            Requests.Add(request);
            if (request.Text.Length == 0)
                return new("empty", 0, 0, []);
            var glyph = new ShapedGlyph(1, 0, 0, 0, request.Text.Length, 0, 0);
            var run = new ShapedRun(
                "scrollbar",
                "scrollbar",
                400,
                5,
                0,
                "scrollbar",
                0,
                "scrollbar#0",
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
            return new("scrollbar", request.Text.Length, request.FontSize, [run]);
        }
    }
}
