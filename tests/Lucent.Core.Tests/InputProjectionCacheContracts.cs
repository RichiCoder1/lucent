using Lucent.Core;

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
}
