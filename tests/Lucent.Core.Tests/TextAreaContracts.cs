namespace Lucent.Core.Tests;

[TestClass]
public sealed class TextAreaContracts
{
    [TestMethod]
    public void MultilineStateNormalizesInputAndPublishesDisplayedSemanticText()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "text-area");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var session = new EditorSession(
            composition.Root.Scope,
            "note",
            "left\r\nright\t",
            multiline: true
        );
        var element = composition.Child(composition.Root, "area");
        var state = Controls.TextArea(element, theme, "Note", session: session);

        graph.Drain();
        Assert.AreEqual("left\nright\t", state.Value);
        Assert.IsTrue(state.IsMultiline);

        state.MoveEnd();
        state.Insert("\r\nnext");
        Assert.AreEqual("left\nright\t\nnext", state.Value);

        state.SelectAll();
        state.SetPreedit("候", 0, 1);
        Assert.AreEqual("候", state.DisplayText);
        graph.Drain();
        var snapshot = FindTextArea(composition.SemanticSnapshot()!);
        Assert.IsNotNull(snapshot.Text);
        Assert.AreEqual(state.DisplayText, snapshot.Text!.Text);
        Assert.AreEqual(state.DisplayCaret, snapshot.Text.Caret);
        state.CancelComposition();
        state.SelectAll();
        var original = state.Value;
        state.RequestClipboard(TextClipboardOperation.Cut);
        Assert.IsTrue(state.TryTakeClipboard(out var cut));
        Assert.AreEqual(original, cut.Text);
        Assert.IsTrue(state.CompleteClipboard(cut, succeeded: true));
        Assert.AreEqual("", state.Value);
        state.Undo();
        Assert.AreEqual(original, state.Value);
        state.SelectAll();
        state.RequestClipboard(TextClipboardOperation.Paste);
        Assert.IsTrue(state.TryTakeClipboard(out var paste));
        Assert.IsTrue(state.CompleteClipboard(paste, succeeded: true, "First\r\nSecond\tline"));
        Assert.AreEqual("First\nSecond\tline", state.Value);
        state.Undo();
        Assert.AreEqual(original, state.Value);
    }

    [TestMethod]
    public void MultilinePointerPlacesCaretAndDragSelects()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "text-area-pointer");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var element = composition.Child(composition.Root, "area");
        var state = Controls.TextArea(
            element,
            theme,
            "Note",
            "abcd",
            Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 30f)
        );
        graph.Drain();
        var scene = SceneLayout.Project(composition, new(100, 30, 1), new FixedShaper());
        Assert.IsTrue(composition.Input.SetScene(scene));
        var bounds = scene.Input.Single(item => item.Identity.ElementId == element.Id).Bounds;
        var padding = element.Resolve(LayoutProperties.Padding).Value;
        var textOriginX = bounds.X + padding.Left;
        var textOriginY = bounds.Y + padding.Top;

        Assert.IsTrue(
            composition
                .Input.DispatchPointer(
                    new(
                        PointerCommandKind.Down,
                        7,
                        textOriginX + 25,
                        textOriginY + 5,
                        PointerButton.Primary
                    )
                )
                .Handled
        );
        Assert.AreEqual(2, state.Caret);
        Assert.AreEqual(2, state.Anchor);

        Assert.IsTrue(
            composition
                .Input.DispatchPointer(
                    new(PointerCommandKind.Move, 7, textOriginX + 45, textOriginY + 5)
                )
                .Handled
        );
        Assert.IsTrue(
            composition
                .Input.DispatchPointer(
                    new(PointerCommandKind.Up, 7, textOriginX + 45, textOriginY + 5)
                )
                .Handled
        );
        Assert.AreEqual(2, state.Anchor);
        Assert.AreEqual(4, state.Caret);
        composition.Input.DispatchPointer(
            new(
                PointerCommandKind.Down,
                7,
                textOriginX + 25,
                textOriginY + 5,
                PointerButton.Primary
            )
        );
        composition.Input.DispatchPointer(
            new(PointerCommandKind.Move, 9, textOriginX + 45, textOriginY + 5)
        );
        Assert.AreEqual(2, state.Caret, "Another pointer extended the active selection.");
        composition.Input.DispatchPointer(
            new(PointerCommandKind.Cancel, 7, textOriginX + 25, textOriginY + 5)
        );
        composition.Input.DispatchPointer(
            new(PointerCommandKind.Move, 7, textOriginX + 45, textOriginY + 5)
        );
        Assert.AreEqual(
            2,
            state.Caret,
            "Movement after capture cancellation extended the selection."
        );
    }

    [TestMethod]
    public void MultilineKeyboardUsesLineAndDocumentHomeEnd()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "text-area-keyboard");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var element = composition.Child(composition.Root, "area");
        var state = Controls.TextArea(
            element,
            theme,
            "Note",
            "ab",
            Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 30f)
        );
        graph.Drain();
        var scene = SceneLayout.Project(composition, new(100, 30, 1), new FixedShaper());
        Assert.IsTrue(composition.Input.SetScene(scene));
        var bounds = scene.Input.Single(item => item.Identity.ElementId == element.Id).Bounds;
        var padding = element.Resolve(LayoutProperties.Padding).Value;
        Assert.IsTrue(
            composition
                .Input.DispatchPointer(
                    new(
                        PointerCommandKind.Down,
                        8,
                        bounds.X + padding.Left + 25,
                        bounds.Y + padding.Top + 5,
                        PointerButton.Primary
                    )
                )
                .Handled
        );
        Assert.IsTrue(composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Enter)).Handled);
        Assert.AreEqual("ab\n", state.Value);

        state.MoveEnd();
        Assert.AreEqual(3, state.Caret);
        Assert.IsTrue(
            composition
                .Input.DispatchKey(new(KeyCommandKind.Down, Key.Home, KeyModifiers.Control))
                .Handled
        );
        Assert.AreEqual(0, state.Caret);
        Assert.IsTrue(
            composition
                .Input.DispatchKey(new(KeyCommandKind.Down, Key.End, KeyModifiers.Control))
                .Handled
        );
        Assert.AreEqual(3, state.Caret);
    }

    [TestMethod]
    public void MultilineSemanticScrollUsesItsRegisteredViewport()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "text-area-semantic-scroll");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var element = composition.Child(composition.Root, "area");
        var state = Controls.TextArea(
            element,
            theme,
            "Note",
            "overflow",
            Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 40f)
        );
        graph.Drain();
        var semantic = FindTextArea(composition.SemanticSnapshot()!);
        Assert.IsTrue(semantic.Actions.HasFlag(SemanticAction.Scroll));

        var scene = SceneLayout.Project(composition, new(100, 40, 1), new TallShaper());
        Assert.IsTrue(composition.Input.SetScene(scene));
        var inputIdentity = scene
            .Input.Single(item => item.Identity.ElementId == element.Id)
            .Identity;
        Assert.IsTrue(composition.Input.GetSemanticScroll(inputIdentity)?.Maximum.Y > 0);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(
                semantic.Identity,
                new(SemanticCommandKind.Scroll, Vertical: 20)
            )
        );
        Assert.IsTrue(state.ScrollState!.Offset.Y > 0);
    }

    [TestMethod]
    public void TextAreaRequiresMultilineSessionAndSingleLineStillRejectsBreaks()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "text-area-validation");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var singleLine = new EditorSession(composition.Root.Scope, "single");
        var element = composition.Child(composition.Root, "area");

        Expect<ArgumentException>(() =>
            Controls.TextArea(element, theme, "Note", session: singleLine)
        );
        using var multiline = new EditorSession(composition.Root.Scope, "multi", multiline: true);
        Expect<ArgumentException>(() =>
            Controls.TextField(element, theme, "Title", session: multiline)
        );
        Assert.IsFalse(TextInputCommand.TryNormalizeSingleLine("a\n", out _));
        Assert.IsTrue(TextInputCommand.TryNormalizeMultiline("a\r\nb", out var normalized));
        Assert.AreEqual("a\nb", normalized);
    }

    private sealed class TallShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            var glyph = new ShapedGlyph(1, 0, 0, 0, 80, 0, 0);
            var run = new ShapedRun(
                "tall",
                "tall",
                400,
                5,
                0,
                "tall#0",
                0,
                "tall#0",
                request.Direction,
                request.Language,
                request.FontSize,
                0,
                200,
                -200,
                0,
                80,
                [glyph]
            );
            return new("tall", 80, 200, [run]);
        }
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

    private static SemanticSnapshot FindTextArea(SemanticSnapshot snapshot) =>
        snapshot.Role == SemanticRole.TextField
            ? snapshot
            : snapshot.Children.Select(FindTextArea).First();

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

        Assert.Fail($"Expected {typeof(T).Name}.");
    }
}
