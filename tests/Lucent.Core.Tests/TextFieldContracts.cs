using Lucent.Core;

internal static class TextFieldContracts
{
    internal static int Run()
    {
        try
        {
            var graph = new ReactiveGraph();
            using var composition = new Composition(graph, "text-field");
            var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            Controls.Panel(
                composition.Root,
                theme,
                "root",
                Style
                    .Empty.Set(LayoutProperties.Width, 200f)
                    .Set(LayoutProperties.Height, 40f)
                    .Set(LayoutProperties.Clip, true)
            );
            var field = composition.Child(composition.Root, "field");
            var state = Controls.TextField(
                field,
                theme,
                "Search",
                style: Style
                    .Empty.Set(LayoutProperties.Width, 40f)
                    .Set(LayoutProperties.Height, 20f)
                    .Set(LayoutProperties.Padding, new Insets(2, 3, 4, 5))
                    .Set(VisualProperties.Opacity, .5f)
            );
            var other = composition.Child(composition.Root, "other");
            Controls.TextField(
                other,
                theme,
                "Other",
                style: Style
                    .Empty.Set(LayoutProperties.Width, 200f)
                    .Set(LayoutProperties.Height, 20f)
            );
            var router = composition.Input;
            var scene = Install(composition, router);
            Assert(
                field.Resolve(ProjectionProperties.Text).Value == "Search"
                    && FindTextField(composition.SemanticSnapshot()!).Value == "",
                "Empty unfocused text field did not show its non-semantic placeholder."
            );
            var fieldBox = scene.Boxes.Single(box => box.Identity.ElementId == field.Id);
            Assert(
                router
                    .DispatchPointer(
                        new(
                            PointerCommandKind.Down,
                            1,
                            fieldBox.Bounds.X + 1,
                            fieldBox.Bounds.Y + 1,
                            PointerButton.Primary
                        )
                    )
                    .Handled
                    && router.FocusedElement?.ElementId == field.Id,
                "Primary text-field pointer down did not focus and handle without caret hit testing."
            );
            scene = Install(composition, router);
            Assert(
                field.Resolve(ProjectionProperties.Text).Value == "",
                "Focused empty text field retained its placeholder as editable content."
            );
            var initialCaretNode = Flatten(scene.Nodes)
                .Single(node =>
                    node.Identity.Element.ElementId == field.Id
                    && node.Identity.Kind == SceneNodeKind.Caret
                );
            Assert(
                router.TryGetCaretGeometry(out var initialCaret)
                    && initialCaret == initialCaretNode.Bounds
                    && initialCaret.X == fieldBox.Bounds.X + 2
                    && initialCaret.Y == fieldBox.Bounds.Y + 3
                    && initialCaret.Height == 12,
                "Focused padded text field did not expose its rendered inner caret geometry."
            );

            state.Value = "a😀b";
            state.MoveHome();
            state.MoveRight();
            state.MoveRight(extend: true);
            state.SetPreedit("中😀", 1, 1);
            Assert(
                state.Value == "a😀b" && state.DisplayText == "a中😀b" && state.DisplayCaret == 2,
                "Preedit did not replace the captured selection or convert SDL scalar caret offsets to UTF-16."
            );
            scene = Install(composition, router);
            Assert(
                router.TryGetCaretGeometry(out var preeditCaret)
                    && preeditCaret.X == fieldBox.Bounds.X + 22
                    && preeditCaret
                        == Flatten(scene.Nodes)
                            .Single(node =>
                                node.Identity.Element.ElementId == field.Id
                                && node.Identity.Kind == SceneNodeKind.Caret
                            )
                            .Bounds,
                "Candidate geometry did not use the rendered padded display caret."
            );
            Assert(
                Flatten(scene.Nodes)
                    .Any(node =>
                        node.Identity.Element.ElementId == field.Id
                        && node.Identity.Kind == SceneNodeKind.Caret
                    )
                    && Flatten(scene.Nodes)
                        .Any(node =>
                            node.Identity.Element.ElementId == field.Id
                            && node.Identity.Kind == SceneNodeKind.Selection
                        ),
                "Focused text field did not retain minimal visible caret and selection scene nodes."
            );
            state.SetPreedit("語", 0, 1);
            Assert(
                state.DisplayText == "a語b",
                "Preedit update did not retain the original composition replacement range."
            );
            state.Commit("語");
            Assert(
                state.Value == "a語b" && state.PreeditText.Length == 0,
                "Composition commit did not replace the original selection."
            );

            state.Value = "";
            Install(composition, router);
            router.DispatchText(new(TextInputKind.Commit, "a"));
            router.DispatchText(new(TextInputKind.Commit, "b"));
            state.SetPreedit("中", 0, 1);
            router.DispatchText(new(TextInputKind.Commit, "中"));
            state.Undo();
            Assert(
                state.Value == "ab",
                "IME commit merged into prior ordinary typing undo transaction."
            );
            state.Undo();
            Assert(
                state.Value == "",
                "Routed ordinary commits did not coalesce into one undo transaction."
            );
            AssertMergedEdit(state, "\u0301", "e", "e\u0301", "combining-mark");
            AssertMergedEdit(state, "\ufe0f", "\u2764", "\u2764\ufe0f", "variation-selector");
            AssertMergedEdit(
                state,
                "\U0001F469",
                "\U0001F468\u200d",
                "\U0001F468\u200d\U0001F469",
                "ZWJ emoji"
            );
            AssertMergedEdit(
                state,
                "\U0001F1F8",
                "\U0001F1FA",
                "\U0001F1FA\U0001F1F8",
                "regional indicator"
            );
            state.Value = "\ufe0f";
            state.MoveHome();
            Install(composition, router);
            router.DispatchKey(new(KeyCommandKind.Down, Key.V, KeyModifiers.Control));
            Assert(
                router.TryTakeClipboardRequest(out var mergedPaste)
                    && router.CompleteClipboardRequest(mergedPaste, true, "\u2764")
                    && state.Value == "\u2764\ufe0f"
                    && state.Caret == state.Value.Length
                    && state.Anchor == state.Value.Length,
                "Paste left the selection inside a merged variation-selector grapheme."
            );
            state.DeleteBackward();
            state.Undo();
            Assert(
                state.Value == "\u2764\ufe0f" && state.Caret == state.Value.Length,
                "Undo did not restore a valid paste caret."
            );
            state.Redo();
            Assert(state.Value == "", "Redo did not restore merged-grapheme paste deletion.");
            state.Value = "\u0301";
            state.MoveHome();
            state.SetPreedit("e", 0, 1);
            state.Commit("e");
            Assert(
                state.Value == "e\u0301"
                    && state.Caret == state.Value.Length
                    && state.Anchor == state.Value.Length,
                "IME commit left the selection inside a merged combining grapheme."
            );
            state.DeleteBackward();
            state.Undo();
            Assert(
                state.Value == "e\u0301" && state.Caret == state.Value.Length,
                "Undo did not restore a valid IME caret."
            );
            state.Redo();
            Assert(state.Value == "", "Redo did not restore merged-grapheme IME deletion.");
            state.Value = "";
            Install(composition, router);
            router.DispatchText(new(TextInputKind.Commit, "a"));
            router.DispatchText(new(TextInputKind.Commit, "b"));
            router.DispatchKey(new(KeyCommandKind.Down, Key.V, KeyModifiers.Control));
            Assert(
                router.TryTakeClipboardRequest(out var isolatedPaste)
                    && router.CompleteClipboardRequest(isolatedPaste, true, "P")
                    && !router.CompleteClipboardRequest(isolatedPaste, true, "P")
                    && state.Value == "abP",
                "Paste completion was not one-shot."
            );
            state.Undo();
            Assert(state.Value == "ab", "Paste merged into preceding typed undo transaction.");
            state.Redo();
            Assert(state.Value == "abP", "Paste redo did not restore its isolated transaction.");
            state.Value = "";
            Install(composition, router);
            router.DispatchText(new(TextInputKind.Commit, "a"));
            router.DispatchText(new(TextInputKind.Commit, "b"));
            state.MoveLeft(extend: true);
            router.DispatchKey(new(KeyCommandKind.Down, Key.X, KeyModifiers.Control));
            Assert(
                router.TryTakeClipboardRequest(out var isolatedCut)
                    && router.CompleteClipboardRequest(isolatedCut, true)
                    && !router.CompleteClipboardRequest(isolatedCut, true)
                    && state.Value == "a",
                "Cut completion was not one-shot."
            );
            state.Undo();
            Assert(state.Value == "ab", "Cut undo did not restore its isolated transaction.");
            state.Redo();
            Assert(state.Value == "a", "Cut redo did not restore its isolated transaction.");
            state.Undo();
            state.Undo();
            Assert(state.Value == "", "Cut merged with preceding typed undo transaction.");
            state.Value = "a😀b";
            state.MoveEnd();
            state.MoveLeft(extend: true);
            state.MoveLeft();
            Assert(
                state.Caret == 3 && state.Anchor == 3,
                "Unextended left did not collapse selection to its minimum edge."
            );
            state.MoveHome();
            state.MoveRight();
            state.MoveRight(extend: true);
            state.MoveRight();
            Assert(
                state.Caret == 3 && state.Anchor == 3,
                "Unextended right did not collapse selection to its maximum edge."
            );

            state.Value = "abcdefgh";
            state.MoveEnd();
            scene = Install(composition, router);
            fieldBox = scene.Boxes.Single(box => box.Identity.ElementId == field.Id);
            var fieldInner = scene
                .Input.Single(input => input.Identity.ElementId == field.Id)
                .ChildClipBounds!.Value;
            Assert(
                router.TryGetCaretGeometry(out var clippedCaret)
                    && clippedCaret.X >= fieldInner.X
                    && clippedCaret.X < fieldInner.X + fieldInner.Width
                    && Flatten(scene.Nodes)
                        .OfType<TextSceneNode>()
                        .Single(node => node.Identity.Element.ElementId == field.Id)
                        .Bounds.X < fieldInner.X,
                "Long single-line text did not keep the rendered text and candidate caret aligned inside its padded clip."
            );
            state.Value = "ffi";
            state.MoveHome();
            state.MoveRight();
            scene = Install(composition, router, new LigatureShaper());
            fieldBox = scene.Boxes.Single(box => box.Identity.ElementId == field.Id);
            Assert(
                router.TryGetCaretGeometry(out var ligatureCaret)
                    && ligatureCaret.X == fieldBox.Bounds.X + 12,
                "Ligature candidate caret did not use a padded bounded intra-cluster grapheme position."
            );
            state.MoveRight(extend: true);
            scene = Install(composition, router, new LigatureShaper());
            var ligatureSelection = Flatten(scene.Nodes)
                .OfType<PaintSceneNode>()
                .Single(node =>
                    node.Identity.Element.ElementId == field.Id
                    && node.Identity.Kind == SceneNodeKind.Selection
                );
            Assert(
                ligatureSelection.Bounds.X == fieldBox.Bounds.X + 12
                    && ligatureSelection.Bounds.Width == 10,
                "Ligature selection geometry did not follow padded intra-cluster grapheme positions."
            );

            state.Value = "copy";
            state.SelectAll();
            Install(composition, router);
            router.DispatchKey(new(KeyCommandKind.Down, Key.C, KeyModifiers.Control));
            Assert(
                router.TryTakeClipboardRequest(out var copy),
                "Copy did not produce an opaque clipboard request."
            );
            Assert(
                router.MoveFocus(FocusTraversalDirection.Next),
                "Could not move focus for origin-bound clipboard completion."
            );
            Assert(
                !router.CompleteClipboardRequest(copy, true) && state.Value == "copy",
                "Copy completion should not edit but must be consumable after focus moved."
            );
            Assert(
                !router.CompleteClipboardRequest(copy, true),
                "Replayed clipboard capability was accepted."
            );
            Assert(
                router.MoveFocus(FocusTraversalDirection.Previous),
                "Could not restore text-field focus."
            );
            router.DispatchKey(new(KeyCommandKind.Down, Key.V, KeyModifiers.Control));
            Assert(
                router.TryTakeClipboardRequest(out var movedPaste),
                "Paste did not produce a clipboard request."
            );
            Assert(
                router.MoveFocus(FocusTraversalDirection.Next)
                    && !router.CompleteClipboardRequest(movedPaste, true, "日本")
                    && state.Value == "copy",
                "Moved-focus Paste completion mutated its origin field."
            );
            Assert(
                router.MoveFocus(FocusTraversalDirection.Previous),
                "Could not restore text-field focus after Paste."
            );
            router.DispatchKey(new(KeyCommandKind.Down, Key.X, KeyModifiers.Control));
            Assert(
                router.TryTakeClipboardRequest(out var movedCut),
                "Cut did not produce a clipboard request."
            );
            Assert(
                router.MoveFocus(FocusTraversalDirection.Next)
                    && !router.CompleteClipboardRequest(movedCut, true)
                    && state.Value == "copy",
                "Moved-focus Cut completion mutated its origin field."
            );
            Assert(
                router.MoveFocus(FocusTraversalDirection.Previous),
                "Could not restore text-field focus after Cut."
            );
            router.DispatchKey(new(KeyCommandKind.Down, Key.V, KeyModifiers.Control));
            Assert(
                router.TryTakeClipboardRequest(out var paste),
                "Paste did not produce a clipboard request."
            );
            state.MoveEnd();
            state.Insert("!");
            Assert(
                !router.CompleteClipboardRequest(paste, true, "日本") && state.Value == "copy!",
                "Stale clipboard completion applied after an edit generation change."
            );
            Install(composition, router);
            router.DispatchKey(new(KeyCommandKind.Down, Key.V, KeyModifiers.Control));
            Assert(
                router.TryTakeClipboardRequest(out var rejectedPaste)
                    && !router.CompleteClipboardRequest(rejectedPaste, true, "bad\ntext")
                    && state.Value == "copy!",
                "Clipboard newline/control input threw or mutated the field."
            );
            Expect<ArgumentException>(() => state.Insert("bad\nline"));
            Expect<ArgumentException>(() => state.Insert("bad\u2028line"));
            Expect<ArgumentException>(() => state.Insert("bad\u2029line"));
            Expect<ArgumentException>(() => state.Insert("\ud800"));
            Expect<ArgumentException>(() =>
                new TextInputCommand(TextInputKind.Commit, "bad\u2028line").Validate()
            );
            Expect<ArgumentException>(() =>
                new TextInputCommand(TextInputKind.Preedit, "", 0, int.MaxValue).Validate()
            );

            state.Value = "secret";
            Install(composition, router);
            Assert(
                !composition.Dump().Contains("secret", StringComparison.Ordinal)
                    && !Install(composition, router)
                        .Dump()
                        .Contains("secret", StringComparison.Ordinal)
                    && FindTextField(composition.SemanticSnapshot()!).Value == "secret",
                "Diagnostic dumps leaked user text or text-field semantics lost ordinary Value."
            );
            router.DispatchKey(
                new(KeyCommandKind.Down, Key.A, KeyModifiers.Control | KeyModifiers.Alt)
            );
            Assert(
                state.SelectedText != "secret",
                "AltGr Ctrl+Alt shortcut reached mapped text editing behavior."
            );
            router.DispatchKey(new(KeyCommandKind.Down, Key.V, KeyModifiers.Control));
            Assert(
                router.TryTakeClipboardRequest(out var disposedPaste),
                "Disposed clipboard test did not obtain an opaque request."
            );
            var stale = new TextInputCommand(TextInputKind.Commit, "x");
            field.Dispose();
            Assert(
                !router.CompleteClipboardRequest(disposedPaste, true, "x")
                    && router.DispatchText(stale).Rejection == InputRejection.StaleScene,
                "Disposed field accepted stale text or clipboard completion."
            );
            Expect<ObjectDisposedException>(() => state.Insert("x"));

            SetupRollback();
            Console.WriteLine("Lucent.Core text field contracts: PASS");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Lucent.Core text field contracts: FAIL: " + error.Message);
            return 1;
        }
    }

    private static void SetupRollback()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "text-field-rollback");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var field = composition.Child(composition.Root, "field");
        Expect<ArgumentException>(() =>
            Controls.TextField(
                field,
                theme,
                "Field",
                style: Style.Empty.Set(
                    new Property<string?>(ProjectionProperties.Text.Name, null),
                    "duplicate"
                )
            )
        );
        Assert(
            field.Resolve(ProjectionProperties.Text).Value is null,
            "Failed text-field setup retained presentation state."
        );
        _ = Controls.TextField(field, theme, "Field");
    }

    private static void AssertMergedEdit(
        TextFieldState state,
        string suffix,
        string inserted,
        string expected,
        string description
    )
    {
        state.Value = suffix;
        state.MoveHome();
        state.Insert(inserted);
        Assert(
            state.Value == expected
                && state.Caret == expected.Length
                && state.Anchor == expected.Length,
            $"Direct {description} insertion left the selection inside a merged grapheme."
        );
        state.DeleteBackward();
        Assert(
            state.Value == "",
            $"Direct {description} deletion did not remove the whole grapheme."
        );
        state.Undo();
        state.MoveLeft();
        Assert(
            state.Value == expected && state.Caret == 0 && state.Anchor == 0,
            $"Undo did not restore valid {description} selection boundaries."
        );
        state.Redo();
        Assert(state.Value == "", $"Redo did not restore {description} deletion.");
    }

    private static RetainedScene Install(
        Composition composition,
        InputRouter router,
        ITextShaper? shaper = null
    )
    {
        shaper ??= new MetricShaper();
        composition.Flush();
        var scene = SceneLayout.Project(composition, new(200, 40, 1), shaper);
        if (!router.SetScene(scene))
        {
            composition.Flush();
            scene = SceneLayout.Project(composition, new(200, 40, 1), shaper);
            Assert(router.SetScene(scene), "Text field scene did not converge.");
        }
        return scene;
    }

    private static IEnumerable<SceneNode> Flatten(IEnumerable<SceneNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            if (node is ClipSceneNode clip)
                foreach (var child in Flatten(clip.Children))
                    yield return child;
            else if (node is OpacitySceneNode opacity)
                foreach (var child in Flatten(opacity.Children))
                    yield return child;
        }
    }

    private static SemanticSnapshot FindTextField(SemanticSnapshot snapshot) =>
        snapshot.Role == SemanticRole.TextField
            ? snapshot
            : snapshot.Children.Select(FindTextField).First();

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

    private sealed class MetricShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.Text.Length == 0)
                return new("empty", 0, 0, []);
            var glyphs = new List<ShapedGlyph>();
            var x = 0f;
            var offset = 0;
            foreach (var rune in request.Text.EnumerateRunes())
            {
                glyphs.Add(new(1, (uint)offset, x, 0, 10, 0, 0));
                x += 10;
                offset += rune.Utf16SequenceLength;
            }
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
                x,
                glyphs
            );
            return new("metric", x, request.FontSize, [run]);
        }
    }

    private sealed class LigatureShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.Text.Length == 0)
                return new("empty", 0, 0, []);
            var glyphs =
                request.Text == "ffi"
                    ? new[] { new ShapedGlyph(1, 0, 0, 0, 30, 0, 0) }
                    : request
                        .Text.EnumerateRunes()
                        .Select(
                            (_, index) => new ShapedGlyph(1, (uint)index, index * 10, 0, 10, 0, 0)
                        )
                        .ToArray();
            var width = glyphs.Sum(glyph => glyph.XAdvance);
            var run = new ShapedRun(
                "ligature",
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
                width,
                glyphs
            );
            return new("ligature", width, request.FontSize, [run]);
        }
    }
}
