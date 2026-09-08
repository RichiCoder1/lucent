using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ImeCompositionContracts
{
    [TestMethod]
    public void EmptyPreeditUpdateEndsCompositionWithoutEditingCommittedText()
    {
        using var composition = CreateComposition(out var first, out _, multiline: false);
        var original = first.Value;
        first.SetSelection(1, 1);
        first.SetPreedit("候", 0, 1);
        Assert.IsTrue(first.HasPreedit);

        first.SetPreedit("", 0, 0);

        Assert.IsFalse(first.HasPreedit, "An empty SDL editing update remained active.");
        Assert.AreEqual(original, first.Value, "Ending preedit changed committed text.");
        Assert.AreEqual(1, first.Caret, "Ending preedit moved the committed caret.");
        Assert.AreEqual(1, first.Anchor, "Ending preedit moved the committed selection.");
        Assert.AreEqual(original, first.DisplayText);
    }

    [TestMethod]
    public void EscapeCancelsActiveCompositionButBubblesWhenThereIsNothingToCancel()
    {
        using var composition = CreateComposition(out var first, out _, multiline: false);
        var input = composition.Input;
        Install(composition);
        Assert.IsTrue(input.MoveFocus(FocusTraversalDirection.Next));

        first.SetPreedit("候", 0, 1);
        var canceled = input.DispatchKey(new(KeyCommandKind.Down, Key.Escape));
        Assert.IsTrue(canceled.Handled, "Escape did not cancel the active composition.");
        Assert.IsFalse(first.HasPreedit);

        var bubbled = input.DispatchKey(new(KeyCommandKind.Down, Key.Escape));
        Assert.IsFalse(
            bubbled.Handled,
            "A plain Escape was consumed by a text field with no active composition."
        );
    }

    [TestMethod]
    public void ImeOwnedEditingKeysDoNotMutateCommittedSelectionDuringPreedit()
    {
        using var composition = CreateComposition(out var first, out _, multiline: true);
        var ancestorKeys = 0;
        composition.Root.AttachBehaviors(new KeyProbe(() => ancestorKeys++));
        var input = composition.Input;
        Install(composition);
        Assert.IsTrue(input.MoveFocus(FocusTraversalDirection.Next));

        first.Value = "top bottom";
        first.MoveEnd();
        first.SetPreedit("候", 0, 1);
        var caret = first.Caret;
        var anchor = first.Anchor;
        foreach (
            var (key, modifiers) in new[]
            {
                (Key.Up, KeyModifiers.None),
                (Key.Down, KeyModifiers.None),
                (Key.Left, KeyModifiers.None),
                (Key.Right, KeyModifiers.None),
                (Key.Left, KeyModifiers.Control),
                (Key.Backspace, KeyModifiers.None),
                (Key.Delete, KeyModifiers.None),
            }
        )
        {
            var result = input.DispatchKey(new(KeyCommandKind.Down, key, modifiers));
            Assert.IsTrue(result.Handled, $"IME key {key} bubbled past the editor.");
            Assert.IsTrue(first.HasPreedit, $"IME key {key} canceled preedit unexpectedly.");
            Assert.AreEqual(caret, first.Caret, $"IME key {key} moved the committed caret.");
            Assert.AreEqual(anchor, first.Anchor, $"IME key {key} moved the committed anchor.");
        }
        Assert.AreEqual(0, ancestorKeys, "An IME-owned key reached an ancestor action.");
        Assert.AreEqual("top bottom", first.Value);
        Assert.AreEqual("top bottom候", first.DisplayText);
    }

    [TestMethod]
    public void FocusTransferCancelsMountedPreeditWithoutMigratingIt()
    {
        using var composition = CreateComposition(out var first, out var second, multiline: false);
        var input = composition.Input;
        Install(composition);
        Assert.IsTrue(input.MoveFocus(FocusTraversalDirection.Next));
        first.SetPreedit("候", 0, 1);
        Assert.IsTrue(first.HasPreedit);

        Assert.IsTrue(input.MoveFocus(FocusTraversalDirection.Next));

        Assert.IsFalse(first.HasPreedit, "Focus loss left the old IME composition mounted.");
        Assert.IsFalse(second.HasPreedit, "The old preedit migrated into the new field.");
        Assert.AreEqual("first", first.Value);
        Assert.AreEqual("second", second.Value);
    }

    private static Composition CreateComposition(
        out TextFieldState first,
        out TextFieldState second,
        bool multiline
    )
    {
        var composition = new Composition(new ReactiveGraph(), "ime-contracts");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(
            theme,
            author: Style.Empty.Width(240).Height(100).Axis(LayoutAxis.Column)
        );
        var firstElement = composition.Child(composition.Root, "first");
        var secondElement = composition.Child(composition.Root, "second");
        if (multiline)
        {
            first = Controls.TextArea(
                firstElement,
                theme,
                "First",
                "first",
                Style.Empty.Width(200).Height(40)
            );
            second = Controls.TextArea(
                secondElement,
                theme,
                "Second",
                "second",
                Style.Empty.Width(200).Height(40)
            );
        }
        else
        {
            first = Controls.TextField(
                firstElement,
                theme,
                "First",
                "first",
                Style.Empty.Width(200).Height(30)
            );
            second = Controls.TextField(
                secondElement,
                theme,
                "Second",
                "second",
                Style.Empty.Width(200).Height(30)
            );
        }
        return composition;
    }

    private static void Install(Composition composition)
    {
        composition.Flush();
        var scene = SceneLayout.Project(composition, new(240, 100, 1), new EmptyShaper());
        Assert.IsTrue(composition.Input.SetScene(scene), "IME test scene did not install.");
    }

    private sealed class EmptyShaper : ITextShaper
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
                "ime-fixed",
                "ime-fixed",
                400,
                5,
                0,
                "ime-fixed#0",
                0,
                "ime-fixed#0",
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
                "ime-fixed",
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

    private sealed class KeyProbe(Action onKey) : Behavior
    {
        public override string Name => "ancestor-key-probe";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            context.SetSemantics(new(SemanticRole.Group, "Editor host"));
            context.OnKey(route =>
            {
                onKey();
                route.Handled = true;
            });
        }
    }
}
