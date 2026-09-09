using Lucent.Core;
using TestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class InputProjectionCacheContracts
{
    [TestMethod]
    public void IdentityIndexIncludesOnlyRootReachableElements()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "reachable-index");
        var provisional = composition.Create(composition.Root, "provisional", attach: false);
        var descendant = composition.Create(provisional, "descendant", attach: true);
        var provisionalIdentity = new ElementIdentity(composition.Epoch, provisional.Id);
        var descendantIdentity = new ElementIdentity(composition.Epoch, descendant.Id);
        Assert(
            composition.Find(provisionalIdentity) is null
                && composition.Find(descendantIdentity) is null,
            "Provisional elements entered the reachable identity index."
        );

        composition.Root.Attach(provisional);
        Assert(
            ReferenceEquals(composition.Find(provisionalIdentity), provisional)
                && ReferenceEquals(composition.Find(descendantIdentity), descendant),
            "Attaching a provisional subtree did not index every reachable identity."
        );

        composition.Root.Detach(provisional);
        Assert(
            composition.Find(provisionalIdentity) is null
                && composition.Find(descendantIdentity) is null,
            "Detaching a subtree retained unreachable identities."
        );
        provisional.Dispose();
    }

    [TestMethod]
    public void FirstControlProjectionChangeRejectsInstalledScene()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "first-control-projection");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("first-control-projection"));
        composition.Root.Present(
            theme,
            author: Style.Empty.Set(LayoutProperties.Width, 40f).Set(LayoutProperties.Height, 40f)
        );
        var child = composition.Child(composition.Root, "child");
        child.Present(
            theme,
            author: Style.Empty.Set(LayoutProperties.Width, 20f).Set(LayoutProperties.Height, 20f)
        );
        var router = composition.Input;
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(40, 40, 1), new EmptyShaper())),
            "Initial plain-element scene was rejected."
        );

        child.UpdateControl(LayoutProperties.Width, 30f);
        Assert(
            router.DispatchPointer(new(PointerCommandKind.Move, 2, 1, 1)).Rejection
                == InputRejection.StaleScene,
            "The first control-owned geometry slot retained an installed scene."
        );
    }

    [TestMethod]
    public void RelevantChangesRejectWhilePaintOnlyChangesKeepInputCurrent()
    {
        var graph = new ReactiveGraph();
        var width = graph.Signal(20f, "width");
        var background = graph.Signal(Color.FromRgb(1, 2, 3), "background");
        using var composition = new Composition(graph, "projection-cache");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("projection-cache"));
        composition.Root.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                .Set(LayoutProperties.Width, 40f)
                .Set(LayoutProperties.Height, 40f)
        );
        var child = composition.Child(composition.Root, "child");
        child.Present(
            theme,
            author: Style
                .Empty.Bind(LayoutProperties.Width, () => width.Value)
                .Set(LayoutProperties.Height, 20f)
                .Bind(VisualProperties.Background, () => Brush.Solid(background.Value))
        );
        graph.Drain();
        var router = composition.Input;
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(40, 40, 1), new EmptyShaper())),
            "Initial projection-cache scene was rejected."
        );

        background.Value = Color.FromRgb(4, 5, 6);
        graph.Drain();
        Assert(
            router.DispatchPointer(new(PointerCommandKind.Move, 1, 1, 1)).Status
                == InputDispatchStatus.Delivered,
            "A paint-only change invalidated unchanged input projection."
        );

        width.Value = 30f;
        graph.Drain();
        Assert(
            router.DispatchPointer(new(PointerCommandKind.Move, 1, 1, 1)).Rejection
                == InputRejection.StaleScene,
            "A geometry change retained a stale input projection."
        );
    }

    [TestMethod]
    public void ProjectionRejectsStateMutationDuringLayout()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "projection-mutation");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("projection-mutation"));
        composition.Root.Present(
            theme,
            author: Style.Empty.Set(LayoutProperties.Width, 40f).Set(LayoutProperties.Height, 40f)
        );
        var child = composition.Child(composition.Root, "child");
        child.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 20f)
                .Set(LayoutProperties.Height, 20f)
                .Set(ProjectionProperties.Text, "x")
        );

        try
        {
            _ = SceneLayout.Project(
                composition,
                new(40, 40, 1),
                new MutatingShaper(() => child.UpdateControl(LayoutProperties.Width, 30f))
            );
            throw new InvalidOperationException(
                "Projection accepted state mutation during layout."
            );
        }
        catch (InvalidOperationException error)
        {
            Assert(
                error.Message.Contains(
                    "projection state changed while the scene was being produced",
                    StringComparison.OrdinalIgnoreCase
                ),
                "Projection mutation failed for an unrelated reason: " + error.Message
            );
        }
    }

    [TestMethod]
    public void PassLocalSnapshotRefreshesGeometryTypographyScaleScrollAndInput()
    {
        var graph = new ReactiveGraph();
        var width = graph.Signal(20f, "snapshot-width");
        var fontSize = graph.Signal(12f, "snapshot-font-size");
        var scroll = graph.Signal(default(ScrollOffset), "snapshot-scroll");
        var pointerTransparent = graph.Signal(false, "snapshot-pointer-transparent");
        using var composition = new Composition(graph, "pass-local-snapshot");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("pass-local-snapshot"));
        composition.Root.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 40f)
                .Set(LayoutProperties.Height, 40f)
                .Set(LayoutProperties.Clip, true)
                .Bind(LayoutProperties.Scroll, () => scroll.Value)
        );
        var child = composition.Child(composition.Root, "child");
        child.Present(
            theme,
            author: Style
                .Empty.Bind(LayoutProperties.Width, () => width.Value)
                .Set(LayoutProperties.Height, 20f)
                .Set(ProjectionProperties.Text, "cache")
                .Bind(TypographyProperties.FontSize, () => fontSize.Value)
                .Bind(InputProperties.PointerTransparent, () => pointerTransparent.Value)
        );
        graph.Drain();
        var shaper = new CapturingShaper();
        var router = composition.Input;
        var first = SceneLayout.Project(composition, new(40, 40, 1), shaper);
        TestAssert.IsTrue(router.SetScene(first));
        var firstBox = first.Boxes.Single(box => box.Identity.ElementId == child.Id);
        var firstInput = first.Input.Single(input => input.Identity.ElementId == child.Id);
        TestAssert.AreEqual(20f, firstBox.Bounds.Width);
        TestAssert.AreEqual(12f, shaper.Last.FontSize);
        TestAssert.AreEqual(1f, shaper.Last.Scale);
        TestAssert.IsFalse(firstInput.PointerTransparent);

        width.Value = 30;
        fontSize.Value = 20;
        scroll.Value = new(5, 0);
        pointerTransparent.Value = true;
        graph.Drain();
        var second = SceneLayout.Project(composition, new(40, 40, 1.5f), shaper);
        TestAssert.IsTrue(router.SetScene(second));
        first.Dispose();

        var secondBox = second.Boxes.Single(box => box.Identity.ElementId == child.Id);
        var secondInput = second.Input.Single(input => input.Identity.ElementId == child.Id);
        TestAssert.IsTrue(first.IsDisposed, "The caller did not release the superseded snapshot.");
        TestAssert.IsFalse(
            second.IsDisposed,
            "Releasing the prior snapshot invalidated the replacement."
        );
        TestAssert.AreEqual(30f, secondBox.Bounds.Width, 1f);
        TestAssert.IsTrue(secondBox.Bounds.X < 0, "The refreshed scroll offset was not projected.");
        TestAssert.AreEqual(20f, shaper.Last.FontSize);
        TestAssert.AreEqual(1.5f, shaper.Last.Scale);
        TestAssert.IsTrue(secondInput.PointerTransparent);
        TestAssert.AreNotEqual(firstInput.Signature, secondInput.Signature);
        second.Dispose();
    }

    private static void Assert(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }

    private sealed class MutatingShaper(Action mutate) : ITextShaper
    {
        private bool _mutated;

        public ShapedText Shape(TextMeasureRequest request)
        {
            if (!_mutated)
            {
                _mutated = true;
                mutate();
            }
            return new(
                "mutation",
                1,
                request.FontSize,
                [
                    new ShapedRun(
                        "mutation",
                        request.FontFamily,
                        400,
                        5,
                        0,
                        request.Language,
                        0,
                        "mutation#0",
                        request.Direction,
                        request.Language,
                        request.FontSize,
                        0,
                        request.FontSize,
                        -request.FontSize,
                        0,
                        1,
                        [new ShapedGlyph(1, 0, 0, 0, 1, 0, 0)]
                    ),
                ]
            );
        }
    }

    private sealed class CapturingShaper : ITextShaper
    {
        internal TextMeasureRequest Last { get; private set; }

        public ShapedText Shape(TextMeasureRequest request)
        {
            Last = request;
            return new(
                "captured",
                10,
                request.FontSize,
                [
                    new ShapedRun(
                        "captured",
                        request.FontFamily,
                        400,
                        5,
                        0,
                        request.Language,
                        0,
                        "captured#0",
                        request.Direction,
                        request.Language,
                        request.FontSize,
                        0,
                        request.FontSize,
                        -request.FontSize,
                        0,
                        10,
                        [new ShapedGlyph(1, 0, 0, 0, 10, 0, 0)]
                    ),
                ]
            );
        }
    }
}
