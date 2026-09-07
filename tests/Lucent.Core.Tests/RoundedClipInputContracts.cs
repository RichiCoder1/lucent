using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class RoundedClipInputContracts
{
    [TestMethod]
    public void RoundedAncestorClipRejectsCornerHitsButKeepsInteriorHits()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "rounded-input");
        using var theme = new ThemeContext(composition.Root.Scope, new Theme("rounded-input"));
        Present(composition.Root, theme, 40, 40, clip: true, radius: 10);
        var child = composition.Child(composition.Root, "child");
        Present(child, theme, 40, 40, clip: false, radius: 0);
        var probe = new PointerProbe();
        child.AttachBehaviors(probe);

        var scene = SceneLayout.Project(composition, new(40, 40, 1), new EmptyShaper());
        var retainedRoot = scene.Input.Single(item =>
            item.Identity.ElementId == composition.Root.Id
        );
        Assert(
            retainedRoot.ChildClipCornerRadius == 10,
            "Rounded child clip radius was not retained."
        );
        var router = composition.Input;
        Assert(router.SetScene(scene), "Rounded input scene was rejected.");

        var corner = router.DispatchPointer(
            new(PointerCommandKind.Down, 1, 0, 0, PointerButton.Primary)
        );
        Assert(
            corner.Target?.ElementId != child.Id && probe.Calls == 0,
            "A rounded transparent corner was routed to the clipped child."
        );

        var interior = router.DispatchPointer(
            new(PointerCommandKind.Down, 2, 20, 20, PointerButton.Primary)
        );
        Assert(
            interior.Target?.ElementId == child.Id && probe.Calls == 1,
            "An interior point inside the rounded clip did not reach the child."
        );
    }

    [TestMethod]
    public void NestedRoundedAncestorClipsAreAllApplied()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "nested-rounded-input");
        using var theme = new ThemeContext(
            composition.Root.Scope,
            new Theme("nested-rounded-input")
        );
        Present(composition.Root, theme, 40, 40, clip: true, radius: 2);
        var middle = composition.Child(composition.Root, "middle");
        Present(middle, theme, 40, 40, clip: true, radius: 10);
        var leaf = composition.Child(middle, "leaf");
        Present(leaf, theme, 40, 40, clip: false, radius: 0);
        var probe = new PointerProbe();
        leaf.AttachBehaviors(probe);

        var router = composition.Input;
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(40, 40, 1), new EmptyShaper())),
            "Nested rounded input scene was rejected."
        );

        var outerInteriorInnerCorner = router.DispatchPointer(
            new(PointerCommandKind.Down, 1, 2, 2, PointerButton.Primary)
        );
        Assert(
            outerInteriorInnerCorner.Target?.ElementId != leaf.Id && probe.Calls == 0,
            "A nested rounded ancestor clip was ignored."
        );

        var center = router.DispatchPointer(
            new(PointerCommandKind.Down, 2, 20, 20, PointerButton.Primary)
        );
        Assert(
            center.Target?.ElementId == leaf.Id && probe.Calls == 1,
            "Nested rounded clips rejected a point in their shared interior."
        );
    }

    [TestMethod]
    public void RadiusOnlyChangeInvalidatesInstalledInputProjection()
    {
        var graph = new ReactiveGraph();
        var radius = graph.Signal(10f, "radius");
        using var composition = new Composition(graph, "rounded-input-freshness");
        using var theme = new ThemeContext(
            composition.Root.Scope,
            new Theme("rounded-input-freshness")
        );
        composition.Root.Present(
            theme,
            author: Style.Empty.Width(40).Height(40).Clip(true).CornerRadius(() => radius.Value)
        );
        var child = composition.Child(composition.Root, "child");
        Present(child, theme, 40, 40, clip: false, radius: 0);
        child.AttachBehaviors(new PointerProbe());

        var router = composition.Input;
        var first = SceneLayout.Project(composition, new(40, 40, 1), new EmptyShaper());
        Assert(
            first
                .Input.Single(item => item.Identity.ElementId == composition.Root.Id)
                .ChildClipCornerRadius == 10,
            "Initial bound rounded clip radius was not projected."
        );
        Assert(router.SetScene(first), "Initial rounded input scene was rejected.");

        radius.Value = 0;
        graph.Drain();
        Assert(
            router
                .DispatchPointer(new(PointerCommandKind.Down, 1, 0, 0, PointerButton.Primary))
                .Rejection == InputRejection.StaleScene,
            "A radius-only presentation change did not invalidate the installed input scene."
        );

        var second = SceneLayout.Project(composition, new(40, 40, 1), new EmptyShaper());
        Assert(
            second
                .Input.Single(item => item.Identity.ElementId == composition.Root.Id)
                .ChildClipCornerRadius == 0
                && second.InputSignature != first.InputSignature,
            "The refreshed scene did not carry the updated rounded clip signature."
        );
        Assert(router.SetScene(second), "Refreshed rounded input scene was rejected.");
        var corner = router.DispatchPointer(
            new(PointerCommandKind.Down, 2, 0, 0, PointerButton.Primary)
        );
        Assert(
            corner.Target?.ElementId == child.Id,
            "The refreshed zero-radius clip did not restore rectangular child hits."
        );
    }

    private static void Present(
        Element element,
        ThemeContext theme,
        float width,
        float height,
        bool clip,
        float radius
    ) =>
        element.Present(
            theme,
            author: Style.Empty.Width(width).Height(height).Clip(clip).CornerRadius(radius)
        );

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class PointerProbe : Behavior
    {
        public int Calls { get; private set; }

        public override string Name => "rounded-input-probe";

        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            context.SetSemantics(new(SemanticRole.Group, "rounded input"));
            context.OnPointer(_ => Calls++);
        }
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }
}
