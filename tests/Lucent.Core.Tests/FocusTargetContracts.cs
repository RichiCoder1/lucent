using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class FocusTargetContracts
{
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
            style: Style.Empty.Set(LayoutProperties.Width, 180f).Set(LayoutProperties.Height, 40f)
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
