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

    [TestMethod]
    public void MultilineOptInNormalizesLinesTabsAndPreservesSingleLineDefaults()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("multiline-normalization");
        using var multiline = new EditorSession(
            owner,
            "note",
            "first\r\nsecond\rthird\tvalue",
            multiline: true
        );

        Assert(multiline.IsMultiline, "The explicit multiline mode was not retained.");
        Assert(
            multiline.Text == "first\nsecond\nthird\tvalue",
            "Multiline construction did not canonicalize CRLF and CR to LF."
        );
        multiline.MoveEnd();
        multiline.Insert("\r\nnext");
        Assert(
            multiline.Text.EndsWith("\nnext", StringComparison.Ordinal),
            "Multiline insertion did not canonicalize its line break."
        );
        Assert(
            EditorSession.TryNormalizeMultiline("a\r\nb\tc", out var normalized)
                && normalized == "a\nb\tc",
            "The public multiline normalizer did not preserve plain-text tabs and canonical LF."
        );
        Assert(
            !EditorSession.TryNormalizeMultiline("bad\0value", out _),
            "Multiline normalization accepted an unsupported control."
        );

        using var singleLine = new EditorSession(owner, "title", "title");
        Assert(!singleLine.IsMultiline, "Single-line behavior was no longer the default.");
        Expect<ArgumentException>(() => singleLine.Insert("\n"));
        Expect<ArgumentException>(() => singleLine.Text = "tab\tvalue");
    }

    [TestMethod]
    public void MultilineLineAndVerticalMovementPreserveAffinityAndDesiredX()
    {
        const string text = "ab\ncdef\nxy";
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("multiline-navigation");
        using var session = new EditorSession(owner, "note", text, multiline: true);
        var paragraph = Paragraph(text);

        session.SetSelection(6, 6);
        session.MoveUp(paragraph);
        Assert(session.Caret == 2, "Vertical movement did not clamp to the shorter prior line.");
        session.MoveDown(paragraph);
        Assert(session.Caret == 6, "Vertical movement did not retain the original desired x.");
        session.MoveDown(paragraph);
        Assert(session.Caret == 10, "Vertical movement did not clamp to the shorter next line.");
        session.MoveUp(paragraph);
        Assert(session.Caret == 6, "Repeated vertical movement lost its retained desired x.");

        session.MoveLineHome();
        Assert(session.Caret == 3, "Logical line Home did not stop after the prior newline.");
        session.MoveLineEnd();
        Assert(session.Caret == 7, "Logical line End did not stop before the next newline.");
        session.MoveHome();
        Assert(session.Caret == 0, "Document Home no longer moved to the draft start.");
        session.MoveLineHome();
        Assert(session.Caret == 0, "Line Home moved past a newline at the document start.");
        session.MoveEnd();
        Assert(session.Caret == text.Length, "Document End no longer moved to the draft end.");

        session.SetSelection(2, 2, TextAffinity.Upstream, TextAffinity.Upstream);
        session.Insert("x");
        session.Undo();
        Assert(
            session.Caret == 2
                && session.Anchor == 2
                && session.CaretAffinity == TextAffinity.Upstream
                && session.AnchorAffinity == TextAffinity.Upstream,
            "Undo did not restore affinity-aware selection state."
        );
    }

    [TestMethod]
    public void TwentyThousandUnitMovementReusesGraphemeIndexAndStringSnapshots()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("long-session");
        var original = new string('a', 20_000);
        using var session = new EditorSession(owner, "long-note", original, multiline: true);
        var initialBuilds = session.GraphemeIndexBuildCount;
        var timer = System.Diagnostics.Stopwatch.StartNew();

        for (var index = 0; index < 10_000; index++)
            session.MoveLeft();
        for (var index = 0; index < 10_000; index++)
            session.MoveRight();

        timer.Stop();
        Assert(
            session.Caret == original.Length && session.GraphemeIndexBuildCount == initialBuilds,
            "Horizontal movement rebuilt or scanned a new grapheme index."
        );
        session.Insert("z");
        var edited = session.Text;
        Assert(
            session.GraphemeIndexBuildCount == initialBuilds + 1,
            "A committed edit did not build exactly one replacement grapheme index."
        );
        session.Undo();
        Assert(
            ReferenceEquals(original, session.Text),
            "Undo history copied the immutable source string instead of retaining its reference."
        );
        session.Redo();
        Assert(
            ReferenceEquals(edited, session.Text),
            "Redo history copied the immutable edited string instead of retaining its reference."
        );
        Assert(
            session.GraphemeIndexBuildCount == initialBuilds + 1,
            "Undo or redo rebuilt an immutable snapshot's cached grapheme index."
        );
        Console.WriteLine(
            "20,000-unit EditorSession characterization: 20,000 cached movements in "
                + timer.Elapsed.TotalMilliseconds.ToString(
                    "F1",
                    System.Globalization.CultureInfo.InvariantCulture
                )
                + " ms"
        );
    }

    [TestMethod]
    public void WholeDocumentReplacementReusesNormalizedStringAndCanonicalizesOnce()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("whole-document-replacement");
        using var session = new EditorSession(owner, "note", "seed", multiline: true);
        var replacement = new string('b', 20_000);

        session.SetSelection(0, session.Text.Length);
        session.Insert(replacement);

        Assert(
            ReferenceEquals(replacement, session.Text),
            "A whole-document edit copied its already-normalized replacement string."
        );
        session.SetSelection(0, session.Text.Length);
        session.Insert("left\r\nright\rmiddle");
        Assert(
            session.Text == "left\nright\nmiddle",
            "A whole-document edit did not canonicalize CRLF and CR input."
        );
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

    private static ShapedText Paragraph(string source)
    {
        var runs = new List<ShapedRun>();
        var lines = new List<ParagraphLine>();
        var start = 0;
        var top = 0f;
        var lineIndex = 0;
        while (start <= source.Length)
        {
            var newline = source.IndexOf('\n', start);
            var end = newline < 0 ? source.Length : newline;
            var glyphs = Enumerable
                .Range(start, end - start)
                .Select(
                    (offset, index) => new ShapedGlyph(1, (uint)offset, index * 10, 0, 10, 0, 0)
                )
                .ToArray();
            if (glyphs.Length != 0)
                runs.Add(
                    new ShapedRun(
                        "line-" + lineIndex,
                        "probe",
                        400,
                        5,
                        0,
                        "fingerprint",
                        0,
                        "probe#0",
                        TextDirection.LeftToRight,
                        "en",
                        10,
                        0,
                        top + 10,
                        -10,
                        0,
                        glyphs.Sum(glyph => glyph.XAdvance),
                        glyphs
                    )
                );
            lines.Add(
                new ParagraphLine(
                    start,
                    end - start,
                    top,
                    top + 10,
                    -10,
                    0,
                    0,
                    glyphs.Sum(glyph => glyph.XAdvance),
                    0,
                    newline >= 0
                )
            );
            lineIndex++;
            top += 10;
            if (newline < 0)
                break;
            start = newline + 1;
        }
        return new ShapedText(
            "paragraph",
            lines.Max(line => line.Advance),
            top,
            runs,
            lines,
            false,
            new LayoutConstraint(lines.Max(line => line.Advance)),
            LayoutConstraint.Unbounded
        );
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
