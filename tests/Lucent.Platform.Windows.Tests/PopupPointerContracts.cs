using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

public sealed unsafe partial class UiaLifecycleContracts
{
    [TestMethod]
    public void SuggestionPopupPreservesOwnerKeyboardAndDoesNotTakeInitialFocus()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(suggestion keyboard) failed.");
        var window = CreateWindow("Lucent suggestion keyboard");
        try
        {
            using var owner = new Composition(new ReactiveGraph(), "suggestion-owner");
            using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
            var target = owner.Mount(owner.Root, theme, Components.Button("Editor anchor"));
            using var renderer = new SkiaSceneRenderer();
            using var scene = SceneLayout.Project(owner, new(600, 400, 1), renderer);
            Assert(owner.Input.SetScene(scene), "Owner scene rejected.");
            using var request = new OwnedSurfaceRequest(
                target,
                theme,
                Components.Button("Suggestion"),
                interactive: true,
                consumeOutsideClick: true,
                closed: null,
                retainOwnerFocus: true
            );
            using var dispatcher = new WindowsUiaDispatcher();
            using var cursor = new WindowsCursor();
            using var popup = new WindowsPopupHost(
                window,
                request,
                dispatcher,
                new WindowsClipboard(),
                cursor
            );
            Assert(
                SDL.GetWindowFlags(popup.WindowHandle).HasFlag(SDL.WindowFlags.NotFocusable),
                "Suggestion surface could take native focus from the editor."
            );
            Assert(
                popup.Composition.Input.FocusedElement is null,
                "Suggestion surface moved initial semantic focus away from the editor."
            );
            var key = new SDL.Event { Type = (uint)SDL.EventType.KeyDown };
            key.Key.WindowID = SDL.GetWindowID(window);
            key.Key.Key = SDL.Keycode.Backspace;
            Assert(!popup.Dispatch(key), "Suggestion surface consumed owner editor deletion.");
            var text = new SDL.Event { Type = (uint)SDL.EventType.TextInput };
            text.Text.WindowID = SDL.GetWindowID(window);
            Assert(!popup.Dispatch(text), "Suggestion surface consumed owner editor text.");
            var preedit = new SDL.Event { Type = (uint)SDL.EventType.TextEditing };
            preedit.Edit.WindowID = SDL.GetWindowID(window);
            Assert(
                !popup.Dispatch(preedit),
                "Suggestion surface consumed owner editor IME preedit."
            );
            var anchor = request.Anchor;
            Assert(
                WindowsSurfaceManager.KeepsAnchorInput(request, anchor.X + 1, anchor.Y + 1),
                "An editor click was treated as outside dismissal instead of caret/selection input."
            );
            Assert(
                !WindowsSurfaceManager.KeepsAnchorInput(request, anchor.X - 1, anchor.Y),
                "Outside dismissal leaked to unrelated owner controls."
            );
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    [TestMethod]
    public void TooltipShadowDoesNotTakePointerFromTriggerButContentRemainsHoverable()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(tooltip pointer) failed.");
        var window = CreateWindow("Lucent tooltip pointer");
        try
        {
            using var composition = new Composition(new ReactiveGraph(), "tooltip-pointer");
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var target = composition.Mount(
                composition.Root,
                theme,
                Components.Button("Target", style: Style.Empty.Height(40))
            );
            using var renderer = new SkiaSceneRenderer();
            using var scene = SceneLayout.Project(composition, new(600, 400, 1), renderer);
            Assert(composition.Input.SetScene(scene), "Owner scene rejected.");
            using var request = new OwnedSurfaceRequest(
                target,
                theme,
                Components.Text("Help"),
                interactive: false,
                consumeOutsideClick: false,
                closed: null,
                anchorOverride: new(100, 100, 0, 0)
            );
            using var dispatcher = new WindowsUiaDispatcher();
            using var cursor = new WindowsCursor();
            using var popup = new WindowsPopupHost(
                window,
                request,
                dispatcher,
                new WindowsClipboard(),
                cursor
            );
            var bounds = popup.ScreenBounds;
            Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(
                (nint)(-1),
                Hit(bounds.Left + 1, bounds.Top + 1),
                "Transparent tooltip shadow intercepted the trigger's pointer."
            );
            Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(
                (nint)1,
                Hit((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2),
                "Tooltip content must remain hoverable for pointer transit and dismissal grace."
            );

            nint Hit(int x, int y) =>
                SendMessage(popup.HwndHandle, 0x0084, 0, (nint)((y & 0xffff) << 16 | (x & 0xffff)));
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }
}
