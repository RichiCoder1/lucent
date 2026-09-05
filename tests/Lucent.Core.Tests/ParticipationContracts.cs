using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ParticipationContracts
{
    [TestMethod]
    public void HiddenRetainsSpaceCollapsedRemovesSpaceAndBothRetainOwnership()
    {
        using var composition = new Composition(new ReactiveGraph(), "participation");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var participation = composition.Root.Scope.Signal(ElementParticipation.Visible, "pane");
        Controls.Column(composition.Root, theme, "Root", Style.Empty.Spacing(10));
        var pane = composition.Child(composition.Root, "pane");
        Controls.Panel(
            pane,
            theme,
            "Pane",
            Style
                .Empty.Width(100)
                .Height(40)
                .Background(Color.FromRgb(255, 0, 0))
                .Participation(() => participation.Value)
        );
        var button = composition.Child(pane, "action");
        Controls.Button(button, theme, "Action", () => { }, Style.Empty.Width(80).Height(20));
        var next = composition.Child(composition.Root, "next");
        Controls.Panel(next, theme, "Next", Style.Empty.Width(100).Height(20));
        var disposed = 0;
        pane.Scope.OnDispose(() => disposed++);
        var visible = Project(composition);
        Assert.AreEqual(50f, Box(visible, next).Y);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        _ = Project(composition);
        Assert.AreEqual(button.Id, composition.Input.FocusedElement?.ElementId);

        participation.Value = ElementParticipation.Hidden;
        composition.Flush();
        // A retained semantic capability cannot operate a pane whose participation just changed.
        var hidden = Project(composition);
        Assert.AreEqual(50f, Box(hidden, next).Y);
        Assert.IsNull(composition.Input.FocusedElement);
        Assert.IsFalse(hidden.Input.Single(item => item.Identity.ElementId == button.Id).Visible);
        Assert.IsFalse(ContainsPaint(hidden.Nodes, pane.Id));
        Assert.IsFalse(ContainsSemantic(composition.SemanticSnapshot(), "Action"));
        Assert.AreEqual(0, disposed);

        participation.Value = ElementParticipation.Collapsed;
        composition.Flush();
        var collapsed = Project(composition);
        Assert.AreEqual(0f, Box(collapsed, next).Y);
        Assert.AreEqual(0f, Box(collapsed, pane).Height);
        Assert.AreEqual(0f, Box(collapsed, button).Width);
        Assert.AreEqual(0, disposed);

        participation.Value = ElementParticipation.Visible;
        composition.Flush();
        var restored = Project(composition);
        Assert.AreEqual(50f, Box(restored, next).Y);
        Assert.IsTrue(ContainsSemantic(composition.SemanticSnapshot(), "Action"));
        Assert.IsNull(composition.Input.FocusedElement, "Showing a pane must not steal focus.");
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        composition.Dispose();
        Assert.AreEqual(1, disposed);
    }

    [TestMethod]
    public void CollapsedViewportRetainsOffsetUntilRestoredGeometryCanClampIt()
    {
        using var composition = new Composition(new ReactiveGraph(), "collapsed-scroll");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var participation = composition.Root.Scope.Signal(
            ElementParticipation.Visible,
            "participation"
        );
        using var viewport = new ViewportState(composition.Root.Scope, new(0, 30));
        Controls.Column(composition.Root, theme, "Root");
        var pane = composition.Child(composition.Root, "viewport");
        Controls.ScrollViewport(
            pane,
            theme,
            "Scroll",
            viewport: viewport,
            style: Style.Empty.Width(100).Height(40).Participation(() => participation.Value)
        );
        var content = composition.Child(pane, "content");
        Controls.Panel(content, theme, "Content", Style.Empty.Width(100).Height(200));
        _ = Project(composition);
        Assert.AreEqual(new ScrollOffset(0, 30), viewport.Offset);
        participation.Value = ElementParticipation.Collapsed;
        _ = Project(composition);
        Assert.AreEqual(new ScrollOffset(0, 30), viewport.Offset);
        participation.Value = ElementParticipation.Visible;
        _ = Project(composition);
        Assert.AreEqual(new ScrollOffset(0, 30), viewport.Offset);
        content.UpdateControl(LayoutProperties.Height, (float?)50);
        _ = Project(composition);
        Assert.AreEqual(new ScrollOffset(0, 10), viewport.Offset);
    }

    [TestMethod]
    public void RoutingOnlyVisibleStillPaintsAndReservesSpace()
    {
        using var composition = new Composition(new ReactiveGraph(), "routing-visible");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        Controls.Column(composition.Root, theme, "Root");
        var pane = composition.Child(composition.Root, "pane");
        Controls.Panel(
            pane,
            theme,
            "Pane",
            Style.Empty.Width(100).Height(40).Background(Color.FromRgb(255, 0, 0)).Visible(false)
        );
        var scene = Project(composition);
        Assert.AreEqual(40f, Box(scene, pane).Height);
        Assert.IsTrue(ContainsPaint(scene.Nodes, pane.Id));
        Assert.IsTrue(ContainsSemantic(composition.SemanticSnapshot(), "Pane"));
        Assert.IsFalse(scene.Input.Single(item => item.Identity.ElementId == pane.Id).Visible);
    }

    private static RetainedScene Project(Composition composition)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            composition.Flush();
            var scene = SceneLayout.Project(composition, new(200, 160, 1), new MetricShaper());
            if (composition.Input.SetScene(scene))
                return scene;
        }
        throw new InvalidOperationException("Participation projection did not converge.");
    }

    private static LayoutRect Box(RetainedScene scene, Element element) =>
        scene.Boxes.Single(box => box.Identity.ElementId == element.Id).Bounds;

    private static bool ContainsSemantic(SemanticSnapshot? snapshot, string name) =>
        snapshot is not null
        && (snapshot.Name == name || snapshot.Children.Any(child => ContainsSemantic(child, name)));

    private static bool ContainsPaint(IReadOnlyList<SceneNode> nodes, long id) =>
        nodes.Any(node =>
            node.Identity.Element.ElementId == id
            || node is ClipSceneNode clip && ContainsPaint(clip.Children, id)
            || node is OpacitySceneNode opacity && ContainsPaint(opacity.Children, id)
        );

    private sealed class MetricShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.Text.Length == 0)
                return new("empty", 0, 0, []);
            var glyph = new ShapedGlyph(1, 0, 0, 0, request.Text.Length, 0, 0);
            var run = new ShapedRun(
                "metric",
                "metric",
                400,
                5,
                0,
                "metric",
                0,
                "metric#0",
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
            return new("metric", request.Text.Length, request.FontSize, [run]);
        }
    }
}
