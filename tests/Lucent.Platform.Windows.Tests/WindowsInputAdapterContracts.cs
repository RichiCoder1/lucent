using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed class WindowsInputAdapterContracts
{
    [TestMethod]
    public void SdlWheelPreservesFractionsPositionAndFlippedDirection()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "windows-wheel");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("windows-wheel"));
        Present(composition.Root, theme, 100, 100, true);
        var viewport = composition.Child(composition.Root, "viewport");
        var state = Controls.ScrollViewport(
            viewport,
            theme,
            "Viewport",
            style: Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 100f)
        );
        var content = composition.Child(viewport, "content");
        content.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 200f)
                .Set(LayoutProperties.Height, 200f)
                .Set(LayoutProperties.MainShrink, 0f)
        );
        Assert.IsTrue(
            composition.Input.SetScene(
                SceneLayout.Project(composition, new(100, 100, 1), new EmptyShaper())
            )
        );
        using var adapter = new WindowsInputAdapter(composition);

        var precise = new SDL.Event
        {
            Type = (uint)SDL.EventType.MouseWheel,
            Wheel = new SDL.MouseWheelEvent
            {
                Type = SDL.EventType.MouseWheel,
                X = 0.5f,
                Y = -0.25f,
                MouseX = 10,
                MouseY = 10,
                Direction = SDL.MouseWheelDirection.Normal,
            },
        };
        Assert.IsTrue(adapter.Dispatch(precise));
        Assert.AreEqual(20f, state.Offset.X);
        Assert.AreEqual(10f, state.Offset.Y);
        Assert.IsTrue(adapter.ConsumeRepaintRequest());

        var flipped = precise;
        flipped.Wheel.X = -0.25f;
        flipped.Wheel.Y = 0.5f;
        flipped.Wheel.Direction = SDL.MouseWheelDirection.Flipped;
        Assert.IsTrue(adapter.Dispatch(flipped));
        Assert.AreEqual(30f, state.Offset.X);
        Assert.AreEqual(30f, state.Offset.Y);

        precise.Wheel.X = float.NaN;
        Assert.IsFalse(adapter.Dispatch(precise), "Non-finite native wheel input was accepted.");
    }

    [TestMethod]
    public void ShortcutMappingAddsApplicationKeysWithoutStealingAltGr()
    {
        var control = KeyModifiers.Control;
        Assert.AreEqual(Key.F10, WindowsInputAdapter.MapKey(SDL.Keycode.F10));
        Assert.AreEqual(Key.ContextMenu, WindowsInputAdapter.MapKey(SDL.Keycode.Application));
        Assert.AreEqual(Key.F, WindowsInputAdapter.MapShortcut(SDL.Keycode.F, control));
        Assert.AreEqual(Key.N, WindowsInputAdapter.MapShortcut(SDL.Keycode.N, control));
        Assert.AreEqual(Key.S, WindowsInputAdapter.MapShortcut(SDL.Keycode.S, control));
        Assert.AreEqual(Key.S, WindowsInputAdapter.MapShortcut(SDL.Keycode.S, KeyModifiers.Meta));
        Assert.IsNull(
            WindowsInputAdapter.MapShortcut(SDL.Keycode.S, KeyModifiers.Control | KeyModifiers.Alt),
            "Control+Alt AltGr-like input was mapped as an application shortcut."
        );
    }

    [TestMethod]
    public void PointerModifiersAndPagingKeysRemainPortableAtTheWindowsBoundary()
    {
        Assert.AreEqual(Key.PageUp, WindowsInputAdapter.MapKey(SDL.Keycode.Pageup));
        Assert.AreEqual(Key.PageDown, WindowsInputAdapter.MapKey(SDL.Keycode.Pagedown));

        var shiftPointer = new PointerCommand(
            PointerCommandKind.Down,
            7,
            12,
            18,
            PointerButton.Primary,
            KeyModifiers.Shift
        );
        shiftPointer.Validate();
        Assert.AreEqual(KeyModifiers.Shift, shiftPointer.Modifiers);

        var modifiers = WindowsInputAdapter.MapModifiers(
            SDL.Keymod.LShift | SDL.Keymod.LCtrl | SDL.Keymod.RAlt | SDL.Keymod.RGUI
        );
        Assert.AreEqual(
            KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta,
            modifiers
        );
    }

    [TestMethod]
    public void NativeTextTransportNormalizesMultilineInputAndKeepsTheCaretInItsViewport()
    {
        foreach (var multiline in new[] { false, true })
        {
            using var composition = new Composition(new ReactiveGraph(), "native-editor");
            using var renderer = new SkiaSceneRenderer();
            using var session = new EditorSession(
                composition.Root.Scope,
                "draft",
                multiline: multiline
            );
            var style = Style.Empty.Width(180).Height(100);
            composition.Mount(
                composition.Root,
                new ThemeContext(composition.Root.Scope, ControlThemes.Light),
                multiline
                    ? Components.TextArea(session: session, style: style)
                    : Components.TextField(session: session, style: style)
            );
            void Install()
            {
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    composition.Flush();
                    if (
                        composition.Input.SetScene(
                            SceneLayout.Project(composition, new(180, 100, 1), renderer)
                        )
                    )
                        return;
                }
                throw new InvalidOperationException("Editor input scene did not settle.");
            }
            Install();
            Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
            Install();
            SDL.Rect area = default;
            using var adapter = new WindowsInputAdapter(
                composition,
                1,
                textInput: new TextInputTransport(
                    _ => true,
                    _ => true,
                    _ => true,
                    (_, value, _) =>
                    {
                        area = value;
                        return true;
                    }
                )
            );
            Assert.AreEqual(
                multiline,
                adapter.DispatchText(new(TextInputKind.Commit, "one\r\ntwo"))
            );
            Assert.AreEqual(multiline ? "one\ntwo" : "", session.Text);
            if (!multiline)
                continue;
            Install();
            Assert.IsTrue(adapter.DispatchText(new(TextInputKind.Preedit, "漢字", 1, 1)));
            Assert.AreEqual("one\ntwo", session.Text, "Preedit changed committed text.");
            Install();
            adapter.RefreshTextInput();
            Assert.IsTrue(area.W >= 1 && area.H > 0 && area.Y >= 0 && area.Y + area.H <= 101);
            Assert.IsTrue(adapter.DispatchText(new(TextInputKind.Cancel, "")));
            session.Text = string.Join("\n", Enumerable.Repeat("visible caret", 30));
            Install();
            adapter.RefreshTextInput();
            Assert.IsTrue(
                session.Viewport.Offset.Y > 0,
                "Long multiline text did not scroll to the caret."
            );
            Assert.IsTrue(
                area.Y >= 0 && area.Y + area.H <= 101,
                "Native IME area escaped the scrolled editor viewport."
            );
            session.Viewport.Offset = default;
            Install();
            Assert.AreEqual(
                0f,
                session.Viewport.Offset.Y,
                "Projection undid an intentional scroll without a caret change."
            );
            session.MoveHome();
            session.MoveEnd();
            Install();
            Assert.IsTrue(session.Viewport.Offset.Y > 0);
            session.SelectAll();
            session.DeleteBackward();
            Install();
            Assert.AreEqual(0f, session.Viewport.Offset.Y);
            Assert.IsNotNull(
                composition.Input.FocusedElement,
                "Deleting a scrolled document lost editor focus."
            );
            Assert.IsTrue(adapter.DispatchText(new(TextInputKind.Commit, "Still focused")));
        }
    }

    private static void Present(
        Element element,
        ThemeContext theme,
        float width,
        float height,
        bool clip
    ) =>
        element.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Width, width)
                .Set(LayoutProperties.Height, height)
                .Set(LayoutProperties.Axis, LayoutAxis.Column)
                .Set(LayoutProperties.Clip, clip)
        );

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }
}
