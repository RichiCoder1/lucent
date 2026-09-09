using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class InputSceneDisposalContracts
{
    [TestMethod]
    public void FailedSceneInstallationDisposesCandidateAndAllowsFreshRecovery()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "input-scene-disposal");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var participation = composition.Root.Scope.Signal(
            ElementParticipation.Visible,
            "failing-participation"
        );
        Present(composition.Root, theme, 80, 40);
        var target = composition.Child(composition.Root, "target");
        Present(target, theme, 80, 40, Style.Empty.Participation(() => participation.Value));

        var throwOnLost = false;
        var pointerCalls = 0;
        target.AttachBehaviors(
            new Probe(
                "failing-target",
                focus: route =>
                {
                    if (throwOnLost && route.Command.Kind == FocusCommandKind.Lost)
                        throw new InvalidOperationException("focus-loss callback");
                },
                pointer: _ => pointerCalls++
            )
        );

        var router = composition.Input;
        using var first = SceneLayout.Project(composition, new(80, 40, 1), new EmptyShaper());
        Assert.IsTrue(router.SetScene(first));
        Assert.IsTrue(router.MoveFocus(FocusTraversalDirection.Next));
        Assert.AreEqual(target.Id, router.FocusedElement?.ElementId);

        throwOnLost = true;
        participation.Value = ElementParticipation.Collapsed;
        using var failed = SceneLayout.Project(composition, new(80, 40, 1), new EmptyShaper());
        var failure = Assert.ThrowsExactly<AggregateException>(() => router.SetScene(failed));
        StringAssert.Contains(failure.ToString(), "focus-loss callback");

        failed.Dispose();
        Assert.IsTrue(failed.IsDisposed);
        Assert.IsFalse(
            router.SetScene(failed),
            "A caller-disposed candidate must be rejected before it can become current."
        );
        Assert.AreEqual(
            InputRejection.NoScene,
            router
                .DispatchPointer(new(PointerCommandKind.Down, 1, 1, 1, PointerButton.Primary))
                .Rejection,
            "A failed installation must leave no disposed scene routable."
        );

        throwOnLost = false;
        participation.Value = ElementParticipation.Visible;
        // Restoring availability can invalidate interaction visuals once. Follow the
        // host's bounded reprojection contract before delivering input.
        RetainedScene? installed = null;
        for (var attempt = 0; attempt < 4 && installed is null; attempt++)
        {
            var fresh = SceneLayout.Project(composition, new(80, 40, 1), new EmptyShaper());
            if (router.SetScene(fresh))
                installed = fresh;
            else
                fresh.Dispose();
        }
        using var recovered = installed;
        Assert.IsNotNull(
            recovered,
            "A fresh scene must install after bounded visual reconciliation."
        );
        var dispatch = router.DispatchPointer(
            new(PointerCommandKind.Down, 2, 1, 1, PointerButton.Primary)
        );
        Assert.AreEqual(InputDispatchStatus.Delivered, dispatch.Status);
        Assert.AreEqual(target.Id, dispatch.Target?.ElementId);
        Assert.AreEqual(1, pointerCalls);
    }

    private static void Present(
        Element element,
        ThemeContext theme,
        float width,
        float height,
        Style? extra = null
    ) =>
        element.Present(
            theme,
            author: (extra ?? Style.Empty)
                .Set(LayoutProperties.Width, width)
                .Set(LayoutProperties.Height, height)
                .Set(LayoutProperties.Axis, LayoutAxis.Column)
        );

    private sealed class Probe(
        string name,
        Action<FocusRoute>? focus = null,
        Action<PointerRoute>? pointer = null
    ) : Behavior
    {
        public override string Name => name;

        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            context.SetSemantics(new(SemanticRole.Group, name));
            context.MakeFocusable();
            context.OnFocus(focus ?? (_ => { }));
            context.OnPointer(pointer ?? (_ => { }));
        }
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }
}
