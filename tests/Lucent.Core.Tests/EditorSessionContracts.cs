using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class EditorSessionContracts
{
    [TestMethod]
    public void ResponsiveRemountRetainsDraftSelectionUndoAndViewport()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "editor-session-remount");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var session = new EditorSession(composition.Root.Scope, "issue-73", "seed");
        var compact = composition.Root.Scope.Signal(false, "compact");

        ComponentRecipe Branch(string name) =>
            Components.Column([
                Components.TextField(session: session, label: "Draft").Named(name + "-field"),
                Components
                    .ScrollViewport(
                        [Components.Text("scroll-content").Named(name + "-content")],
                        viewport: session.Viewport
                    )
                    .Named(name + "-viewport"),
            ]);

        var responsive = ContentRecipe.Switch(
            "responsive-editor",
            () =>
                new ConditionalChoice(
                    compact.Value ? 1 : 0,
                    Branch(compact.Value ? "compact" : "wide")
                )
        );
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Column([responsive]).Named("session-host")
        );
        graph.Drain();

        session.MoveEnd();
        session.Insert("-draft");
        session.SetSelection(1, 4);
        session.Viewport.Offset = new(7, 11);
        graph.Drain();
        AssertMountedState(composition, session, "wide");

        session.SynchronizeExternalText("issue-73", "seed-draft");
        Assert(
            session.CanUndo && session.Anchor == 1 && session.Caret == 4,
            "Equal external synchronization erased local history or selection."
        );

        compact.Value = true;
        graph.Drain();
        AssertMountedState(composition, session, "compact");

        session.Undo();
        Assert(session.Text == "seed", "Undo history did not survive the wide-to-compact remount.");
        session.Redo();
        Assert(
            session.Text == "seed-draft" && session.Anchor == 1 && session.Caret == 4,
            "Redo or selection did not survive the wide-to-compact remount."
        );

        compact.Value = false;
        graph.Drain();
        AssertMountedState(composition, session, "wide");

        Expect<ArgumentException>(() =>
            session.SynchronizeExternalText("stale-document", "late result")
        );
        Assert(
            session.DocumentId == "issue-73" && session.Text == "seed-draft",
            "A stale external document update switched or overwrote the active draft."
        );

        session.SynchronizeExternalText("issue-73", "xy");
        Assert(
            session.Text == "xy"
                && session.Anchor <= 2
                && session.Caret <= 2
                && !session.CanUndo
                && session.Viewport.Offset == new ScrollOffset(7, 11),
            "Authoritative text synchronization did not clamp selection, reset history, or retain viewport."
        );

        session.SwitchDocument("issue-74", "next");
        Assert(
            session.DocumentId == "issue-74"
                && session.Text == "next"
                && session.Anchor == 4
                && session.Caret == 4
                && !session.CanUndo
                && !session.CanRedo
                && session.Viewport.Offset == default,
            "Explicit document switching did not reset document-local state."
        );
    }

    [TestMethod]
    public void UndoAndRedoAvailabilityAreReactive()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("reactive-history-owner");
        using var session = new EditorSession(owner, "document");
        var observed = new List<(bool Undo, bool Redo)>();
        _ = owner.Effect(
            () => observed.Add((session.CanUndo, session.CanRedo)),
            "observe-editor-history"
        );

        graph.Drain();
        session.Insert("x");
        graph.Drain();
        session.Undo();
        graph.Drain();
        session.Redo();
        graph.Drain();

        Assert(
            observed.SequenceEqual(
                new[] { (false, false), (true, false), (false, true), (true, false) }
            ),
            "Undo and redo availability did not invalidate reactive consumers."
        );
    }

    [TestMethod]
    public void SessionRejectsTwoEstablishedTextFieldMounts()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("single-mount-owner");
        using var session = new EditorSession(owner, "document");
        using var composition = new Composition(graph, "duplicate-session-mount");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Column([
                Components.TextField(session: session),
                Components.TextField(session: session),
            ])
        );

        Expect<AggregateException>(graph.Drain);
    }

    [TestMethod]
    public void SessionLifetimeIsIndependentFromMountAndDisposalIsExplicit()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("explicit-session-owner");
        var session = new EditorSession(owner, "document", "draft");

        using (var composition = new Composition(graph, "first-mount"))
        using (var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light))
        {
            _ = composition.Mount(composition.Root, theme, Components.TextField(session: session));
            graph.Drain();
        }

        Assert(
            !session.IsDisposed && session.Text == "draft",
            "Disposing a text-field mount disposed its hoisted editor session."
        );

        using (var composition = new Composition(graph, "second-mount"))
        using (var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light))
        {
            _ = composition.Mount(composition.Root, theme, Components.TextField(session: session));
            graph.Drain();
        }

        session.Dispose();
        Assert(session.IsDisposed, "Explicit editor-session disposal did not end its lifetime.");
        Expect<ObjectDisposedException>(() => _ = session.Text);
        Expect<ObjectDisposedException>(() => _ = session.Viewport.Offset);
    }

    private static void AssertMountedState(
        Composition composition,
        EditorSession session,
        string branch
    )
    {
        var field = Descendants(composition.Root)
            .Single(element => element.Name == branch + "-field");
        var viewport = Descendants(composition.Root)
            .Single(element => element.Name == branch + "-viewport");
        Assert(
            field.Resolve(ProjectionProperties.Text).Value == session.Text
                && viewport.Resolve(LayoutProperties.Scroll).Value == session.Viewport.Offset,
            "The remounted field or viewport did not project its hoisted state."
        );
    }

    private static IEnumerable<Element> Descendants(Element element)
    {
        yield return element;
        foreach (var child in element.Children)
        foreach (var descendant in Descendants(child))
            yield return descendant;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
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
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
