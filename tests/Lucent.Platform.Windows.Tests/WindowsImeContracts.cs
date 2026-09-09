using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed class WindowsImeContracts
{
    [TestMethod]
    public void OwnerFocusRoundTripCancelsImeBeforeAcceptingNewText()
    {
        using var composition = CreateComposition(out var state, multiline: false);
        Install(composition);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        var clears = 0;
        using var adapter = CreateAdapter(composition, () => clears++);

        Assert.IsTrue(adapter.DispatchText(new(TextInputKind.Preedit, "候", 0, 1)));
        Assert.IsTrue(state.HasPreedit);
        adapter.Dispatch(new SDL.Event { Window = new() { Type = SDL.EventType.WindowFocusLost } });

        Assert.AreEqual(1, clears, "Owner focus loss did not clear the native IME composition.");
        Assert.IsFalse(state.HasPreedit, "Owner focus loss retained Core preedit state.");
        Assert.IsFalse(
            adapter.DispatchText(new(TextInputKind.Commit, "stale")),
            "Owner focus loss accepted queued text."
        );

        adapter.Dispatch(
            new SDL.Event { Window = new() { Type = SDL.EventType.WindowFocusGained } }
        );
        Assert.IsTrue(adapter.DispatchText(new(TextInputKind.Commit, "new")));
        Assert.AreEqual(
            "draftnew",
            state.Value,
            "Focus return did not restore text input lifetime."
        );
    }

    [TestMethod]
    public void EscapeCancelsNativeCompositionOnceAndPlainEscapeBubbles()
    {
        using var composition = CreateComposition(out var state, multiline: false);
        Install(composition);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        var clears = 0;
        using var adapter = CreateAdapter(composition, () => clears++);

        Assert.IsTrue(adapter.DispatchText(new(TextInputKind.Preedit, "候", 0, 1)));
        Assert.IsTrue(adapter.Dispatch(Key(SDL.Keycode.Escape)));
        Assert.AreEqual(1, clears, "Escape did not clear the native IME composition.");
        Assert.IsFalse(state.HasPreedit);
        Assert.AreEqual("draft", state.Value);

        Assert.IsTrue(adapter.Dispatch(Key(SDL.Keycode.Escape)));
        Assert.AreEqual(
            1,
            clears,
            "A plain Escape was sent to the native IME after composition had ended."
        );
        Assert.AreEqual("draft", state.Value);
    }

    [TestMethod]
    public void ExplicitCancelCommandClearsNativeComposition()
    {
        using var composition = CreateComposition(out var state, multiline: false);
        Install(composition);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        var clears = 0;
        using var adapter = CreateAdapter(composition, () => clears++);

        Assert.IsTrue(adapter.DispatchText(new(TextInputKind.Preedit, "候", 0, 1)));
        Assert.IsTrue(adapter.DispatchText(new(TextInputKind.Cancel, "")));

        Assert.AreEqual(1, clears, "Core cancellation did not reset the native IME.");
        Assert.IsFalse(state.HasPreedit);
        Assert.AreEqual("draft", state.Value);
    }

    [TestMethod]
    public void EmptyPreeditAfterCommitDoesNotDuplicateOrEraseCommittedText()
    {
        using var composition = CreateComposition(out var state, multiline: false);
        Install(composition);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        using var adapter = CreateAdapter(composition, () => { });

        Assert.IsTrue(adapter.DispatchText(new(TextInputKind.Preedit, "候", 0, 1)));
        Assert.IsTrue(adapter.DispatchText(new(TextInputKind.Commit, "候")));
        Assert.IsTrue(adapter.DispatchText(new(TextInputKind.Preedit, "", 0, 0)));
        Assert.IsFalse(state.HasPreedit);
        Assert.AreEqual("draft候", state.Value);

        Assert.IsTrue(adapter.DispatchText(new(TextInputKind.Commit, "!")));
        Assert.AreEqual("draft候!", state.Value);
    }

    [TestMethod]
    public void ArrowKeysRemainImeOwnedWhilePreeditIsVisible()
    {
        using var composition = CreateComposition(out var state, multiline: true);
        Install(composition);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        using var adapter = CreateAdapter(composition, () => { });

        state.MoveEnd();
        var caret = state.Caret;
        Assert.IsTrue(adapter.DispatchText(new(TextInputKind.Preedit, "候", 0, 1)));
        Assert.IsTrue(adapter.Dispatch(Key(SDL.Keycode.Up)));
        Assert.IsTrue(adapter.Dispatch(Key(SDL.Keycode.Down)));
        Assert.IsTrue(adapter.Dispatch(Key(SDL.Keycode.Left, SDL.Keymod.Ctrl)));
        Assert.AreEqual(caret, state.Caret, "IME arrows changed the committed caret.");
        Assert.IsTrue(state.HasPreedit, "IME arrows canceled the active preedit.");
        Assert.AreEqual("draft候", state.DisplayText);
    }

    [TestMethod]
    public void FocusTransferClearsNativeCompositionAndRejectsQueuedOldText()
    {
        using var composition = CreateTwoFieldComposition(out var first, out var second);
        Install(composition);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        var clears = 0;
        using var adapter = CreateAdapter(composition, () => clears++);

        Assert.IsTrue(adapter.DispatchText(new(TextInputKind.Preedit, "候", 0, 1)));
        Assert.IsTrue(adapter.Dispatch(Key(SDL.Keycode.Tab)));
        Assert.AreEqual(1, clears, "Focus transfer did not dismiss the old native IME.");
        Assert.IsFalse(first.HasPreedit);
        Assert.IsFalse(second.HasPreedit);

        Assert.IsFalse(
            adapter.DispatchText(new(TextInputKind.Commit, "stale")),
            "Queued text from the old focused editor reached the new editor."
        );
        Assert.AreEqual("second", second.Value);

        adapter.RefreshTextInput();
        Assert.IsTrue(adapter.DispatchText(new(TextInputKind.Commit, "new")));
        Assert.AreEqual("secondnew", second.Value);
    }

    [TestMethod]
    public void InvalidTextIsRejectedWithoutChangingCompositionOrCommittedText()
    {
        using var composition = CreateComposition(out var state, multiline: false);
        Install(composition);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        using var adapter = CreateAdapter(composition, () => { });

        Assert.IsFalse(adapter.DispatchText(new(TextInputKind.Commit, "bad\ntext")));
        Assert.IsFalse(state.HasPreedit);
        Assert.AreEqual("draft", state.Value);
    }

    private static WindowsInputAdapter CreateAdapter(Composition composition, Action clear)
    {
        return new WindowsInputAdapter(
            composition,
            1,
            textInput: new TextInputTransport(
                _ => true,
                _ => true,
                _ => true,
                (_, _, _) => true,
                _ =>
                {
                    clear();
                    return true;
                }
            )
        );
    }

    private static Composition CreateComposition(out TextFieldState state, bool multiline)
    {
        var composition = new Composition(new ReactiveGraph(), "windows-ime");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(
            theme,
            author: Style.Empty.Width(220).Height(60).Axis(LayoutAxis.Column)
        );
        var element = composition.Child(composition.Root, "editor");
        if (multiline)
            state = Controls.TextArea(
                element,
                theme,
                "Editor",
                "draft",
                Style.Empty.Width(200).Height(40)
            );
        else
            state = Controls.TextField(
                element,
                theme,
                "Editor",
                "draft",
                Style.Empty.Width(200).Height(30)
            );
        return composition;
    }

    private static Composition CreateTwoFieldComposition(
        out TextFieldState first,
        out TextFieldState second
    )
    {
        var composition = new Composition(new ReactiveGraph(), "windows-ime-focus");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(
            theme,
            author: Style.Empty.Width(220).Height(100).Axis(LayoutAxis.Column)
        );
        first = Controls.TextField(
            composition.Child(composition.Root, "first"),
            theme,
            "First",
            "first",
            Style.Empty.Width(200).Height(30)
        );
        second = Controls.TextField(
            composition.Child(composition.Root, "second"),
            theme,
            "Second",
            "second",
            Style.Empty.Width(200).Height(30)
        );
        return composition;
    }

    private static void Install(Composition composition)
    {
        using var renderer = new SkiaSceneRenderer();
        composition.Flush();
        var scene = SceneLayout.Project(composition, new(220, 100, 1), renderer);
        Assert.IsTrue(composition.Input.SetScene(scene), "Windows IME scene did not install.");
    }

    private static SDL.Event Key(SDL.Keycode key, SDL.Keymod modifiers = 0) =>
        new()
        {
            Key = new()
            {
                Type = SDL.EventType.KeyDown,
                Key = key,
                Down = true,
                Mod = modifiers,
            },
        };
}
