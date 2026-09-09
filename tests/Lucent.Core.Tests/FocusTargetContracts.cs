using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class FocusTargetContracts
{
    [TestMethod]
    public void CollapsedPaneRecoversToVisibleTabStopWithoutReplayingOnReveal()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "focus-recovery-collapse");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var router = composition.Input;
        router.FocusRecovery = FocusRecoveryPolicy.NearestAvailable;
        Controls.Column(composition.Root, theme, "root");
        var shown = composition.Root.Scope.Signal(true, "shown");
        var pane = composition.Child(composition.Root, "pane");
        Controls.Column(
            pane,
            theme,
            "Pane",
            style: Style.Empty.Participation(() =>
                shown.Value ? ElementParticipation.Visible : ElementParticipation.Collapsed
            )
        );
        var field = composition.Child(pane, "field");
        var editor = Controls.TextField(field, theme, "Field", value: "draft");
        var fallback = composition.Child(composition.Root, "fallback");
        Controls.Button(fallback, theme, "Visible action");
        using var initial = Install(composition, router);
        Assert.IsTrue(router.FocusSemantic(new(composition.Epoch, field.Id)));
        using var focused = Install(composition, router);

        shown.Value = false;
        composition.Flush();
        Assert.IsFalse(router.DispatchText(new(TextInputKind.Commit, "stale")).Handled);
        using var collapsed = Install(composition, router);
        Assert.AreEqual(fallback.Id, router.FocusedElement?.ElementId);
        Assert.AreEqual("draft", editor.Value);
        StringAssert.Contains(router.Dump(), "Recovery");

        shown.Value = true;
        using var revealed = Install(composition, router);
        Assert.AreEqual(
            fallback.Id,
            router.FocusedElement?.ElementId,
            "Revealing a retained pane must not steal focus from its replacement."
        );
    }

    [TestMethod]
    public void EvictedVirtualizedRowRecoversToItsSurvivingScrollViewport()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "focus-recovery-eviction");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var router = composition.Input;
        router.FocusRecovery = FocusRecoveryPolicy.NearestAvailable;
        Controls.Column(composition.Root, theme, "root");
        var selected = 0;
        var list = composition.Mount(
            composition.Root,
            theme,
            Components.VirtualizedList(
                () => Enumerable.Range(0, 1000),
                value => value,
                value => Components.Selectable("Row " + value.Value, () => selected++),
                () => 24f,
                label: "Rows"
            )
        );
        using var initial = Install(composition, router);
        var row = composition
            .Elements()
            .First(element => element.Resolve(ProjectionProperties.Text).Value == "Row 0");
        var oldIdentity = new ElementIdentity(composition.Epoch, row.Id);
        Assert.IsTrue(router.FocusSemantic(oldIdentity));
        using var focused = Install(composition, router);
        Assert.IsTrue(
            router.ScrollSemantic(
                new(composition.Epoch, list.Id),
                new(SemanticCommandKind.Scroll, Endpoint: SemanticScrollEndpoint.End)
            )
        );
        using var scrolled = Install(composition, router);
        Assert.IsTrue(row.IsDisposed, "The fixture must actually evict the focused row.");
        Assert.AreEqual(list.Id, router.FocusedElement?.ElementId);
        Assert.IsFalse(router.FocusSemantic(oldIdentity));
        Assert.AreEqual(0, selected, "Recovery must never activate or select a row.");
    }

    [TestMethod]
    public void ExplicitTargetWinsOverPendingRecoveryAndDisabledOwnersStayCleared()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "focus-recovery-request");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var router = composition.Input;
        router.FocusRecovery = FocusRecoveryPolicy.NearestAvailable;
        Controls.Column(composition.Root, theme, "root");
        var first = composition.Child(composition.Root, "first");
        Controls.Button(first, theme, "First");
        var neighbor = composition.Child(composition.Root, "neighbor");
        Controls.Button(neighbor, theme, "Neighbor");
        var target = new FocusTarget(composition.Root.Scope, "explicit");
        var enabled = composition.Root.Scope.Signal(true, "enabled");
        var last = composition.Child(composition.Root, "last");
        Controls.TextField(
            last,
            theme,
            "Explicit",
            focusTarget: target,
            style: Style.Empty.Bind(InputProperties.Enabled, () => enabled.Value)
        );
        using var initial = Install(composition, router);
        Assert.IsTrue(router.FocusSemantic(new(composition.Epoch, first.Id)));
        using var focused = Install(composition, router);
        first.Dispose();
        target.Request();
        using var recovered = Install(composition, router);
        Assert.AreEqual(last.Id, router.FocusedElement?.ElementId);
        enabled.Value = false;
        using var disabled = Install(composition, router);
        Assert.IsNull(router.FocusedElement);
    }

    [TestMethod]
    public void PendingTextFocusWaitsForSceneAndSelectsWithoutResettingSession()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "focus-target");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var target = new FocusTarget(composition.Root.Scope, "capture-focus");
        var session = new EditorSession(composition.Root.Scope, "capture", "seed");
        session.Text = "draft";
        var element = composition.Child(composition.Root, "capture");
        var state = Controls.TextField(
            element,
            theme,
            "Capture",
            session: session,
            focusTarget: target,
            style: Style.Empty.Set(LayoutProperties.Width, 160f).Set(LayoutProperties.Height, 24f)
        );

        target.Request(selectAll: true);
        Assert.IsTrue(target.IsPending);
        graph.Drain();
        Assert.IsTrue(target.IsPending, "A request must survive before an installed scene exists.");
        var router = composition.Input;
        Install(composition, router);

        Assert.IsFalse(target.IsPending, "The accepted request must be consumed once.");
        Assert.AreEqual(element.Id, router.FocusedElement?.ElementId);
        Assert.AreEqual(0, session.Anchor);
        Assert.AreEqual(session.Text.Length, session.Caret);
        Assert.AreEqual("capture", session.DocumentId);
        Assert.IsTrue(session.CanUndo, "Focus selection must retain the session undo history.");
        Assert.AreEqual(session.Text, state.Value);

        Install(composition, router);
        Assert.AreEqual(element.Id, router.FocusedElement?.ElementId);
        Assert.IsFalse(target.IsPending, "A consumed request must not replay after resize.");
    }

    [TestMethod]
    public void TextAreaFocusTargetUsesTheSameRetainedSessionContract()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "focus-target-area");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var target = new FocusTarget(composition.Root.Scope, "body-focus");
        var session = new EditorSession(
            composition.Root.Scope,
            "body",
            "multiline draft",
            multiline: true
        );
        var element = composition.Child(composition.Root, "body");
        Controls.TextArea(
            element,
            theme,
            "Body",
            session: session,
            focusTarget: target,
            style: Style.Empty.Set(LayoutProperties.Width, 220f).Set(LayoutProperties.Height, 40f)
        );

        target.Request(selectAll: true);
        var router = composition.Input;
        Install(composition, router, new FixedShaper());

        Assert.AreEqual(element.Id, router.FocusedElement?.ElementId);
        Assert.AreEqual(0, session.Anchor);
        Assert.AreEqual(session.Text.Length, session.Caret);
        Assert.IsFalse(target.IsPending);
    }

    [TestMethod]
    public void RequestAndCollapseDoNotFocusAStaleVisibleScene()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "focus-target-collapse");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var participation = composition.Root.Scope.Signal(
            ElementParticipation.Visible,
            "field-participation"
        );
        var target = new FocusTarget(composition.Root.Scope, "collapsed-focus");
        var element = composition.Child(composition.Root, "field");
        Controls.TextField(
            element,
            theme,
            "Field",
            focusTarget: target,
            style: Style
                .Empty.Set(LayoutProperties.Width, 160f)
                .Set(LayoutProperties.Height, 24f)
                .Participation(() => participation.Value)
        );
        var router = composition.Input;
        Install(composition, router);

        participation.Value = ElementParticipation.Collapsed;
        target.Request(selectAll: true);
        graph.Drain();
        Assert.IsTrue(
            target.IsPending,
            "A request must not be consumed from the stale visible scene after collapse."
        );
        Install(composition, router);
        Assert.IsTrue(target.IsPending);
        Assert.IsNull(router.FocusedElement);

        participation.Value = ElementParticipation.Visible;
        Install(composition, router);
        Assert.IsFalse(target.IsPending);
        Assert.AreEqual(element.Id, router.FocusedElement?.ElementId);
    }

    [TestMethod]
    public void CancelledRequestWhileCollapsedDoesNotReplayAfterReveal()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "focus-target-cancel");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var participation = composition.Root.Scope.Signal(
            ElementParticipation.Visible,
            "field-participation"
        );
        var target = new FocusTarget(composition.Root.Scope, "cancelled-focus");
        var element = composition.Child(composition.Root, "field");
        Controls.TextField(
            element,
            theme,
            "Field",
            focusTarget: target,
            style: Style
                .Empty.Set(LayoutProperties.Width, 160f)
                .Set(LayoutProperties.Height, 24f)
                .Participation(() => participation.Value)
        );
        var router = composition.Input;
        Install(composition, router);

        participation.Value = ElementParticipation.Collapsed;
        target.Request();
        graph.Drain();
        Assert.IsTrue(target.IsPending);
        target.Cancel();
        Assert.IsFalse(target.IsPending);
        Install(composition, router);
        Assert.IsNull(router.FocusedElement);

        participation.Value = ElementParticipation.Visible;
        Install(composition, router);
        Assert.IsNull(router.FocusedElement, "A cancelled request must not replay after reveal.");
    }

    [TestMethod]
    public void DisposingAMountedFocusTargetDoesNotBreakSceneInstallation()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "focus-target-dispose");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var target = new FocusTarget(composition.Root.Scope, "disposed-focus");
        var element = composition.Child(composition.Root, "field");
        Controls.TextField(element, theme, "Field", focusTarget: target);
        var router = composition.Input;
        Install(composition, router);

        target.Dispose();
        try
        {
            Install(composition, router);
        }
        catch (Exception error)
        {
            Assert.Fail("A disposed focus target broke scene installation: " + error);
        }
        Assert.IsNull(router.FocusedElement);
    }

    [TestMethod]
    public void OneFocusTargetCannotBindTwoLiveTextControls()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "focus-target-duplicate");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var target = new FocusTarget(composition.Root.Scope, "one-control");
        var first = composition.Child(composition.Root, "first");
        Controls.TextField(first, theme, "First", focusTarget: target);
        var second = composition.Child(composition.Root, "second");

        Expect<InvalidOperationException>(() =>
            Controls.TextField(second, theme, "Second", focusTarget: target)
        );
    }

    private static RetainedScene Install(
        Composition composition,
        InputRouter router,
        ITextShaper? shaper = null
    )
    {
        shaper ??= new FixedShaper();
        RetainedScene scene = null!;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            composition.Flush();
            scene = SceneLayout.Project(composition, new(200, 80, 1), shaper);
            if (router.SetScene(scene))
                return scene;
            scene.Dispose();
        }
        Assert.Fail("Focus target scene did not converge. " + router.Dump());
        return scene;
    }

    private static void Expect<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        Assert.Fail("Expected " + typeof(T).Name + ".");
    }

    private sealed class FixedShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            var glyphs = new List<ShapedGlyph>();
            var offset = 0;
            var x = 0f;
            foreach (var rune in request.Text.EnumerateRunes())
            {
                glyphs.Add(new(1, (uint)offset, x, 0, 10, 0, 0));
                offset += rune.Utf16SequenceLength;
                x += 10;
            }
            var run = new ShapedRun(
                "fixed",
                "fixed",
                400,
                5,
                0,
                "fixed#0",
                0,
                "fixed#0",
                request.Direction,
                request.Language,
                request.FontSize,
                0,
                request.FontSize,
                -request.FontSize,
                0,
                x,
                glyphs
            );
            var line = new ParagraphLine(
                0,
                request.Text.Length,
                0,
                request.FontSize,
                -request.FontSize,
                0,
                0,
                x,
                0,
                false
            );
            return new(
                "fixed",
                x,
                request.FontSize,
                [run],
                [line],
                false,
                request.InlineConstraint,
                request.BlockConstraint
            );
        }
    }
}
