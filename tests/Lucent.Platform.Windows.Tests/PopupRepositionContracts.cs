using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

public sealed unsafe partial class UiaLifecycleContracts
{
    [TestMethod]
    public void PopupRepositionKeepsParentRelativeOffsetsAfterOwnerMovement()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(popup reposition) failed.");
        var window = CreateWindow("Lucent popup reposition");
        try
        {
            Assert(SDL.SetWindowPosition(window, 200, 200), "Owner positioning failed.");
            Assert(SDL.SetWindowSize(window, 600, 400), "Owner sizing failed.");
            Assert(SDL.SyncWindow(window), "Owner sync failed.");
            using var composition = new Composition(new ReactiveGraph(), "popup-reposition");
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            composition.Mount(
                composition.Root,
                theme,
                Components.ContextMenu(
                    [Components.Button("Target", style: Style.Empty.Height(40))],
                    () => Components.Menu([Components.MenuItem("Action", () => { })])
                )
            );
            using var renderer = new SkiaSceneRenderer();
            var scene = SceneLayout.Project(composition, new(600, 400, 1), renderer);
            Assert(composition.Input.SetScene(scene), "Owner scene rejected.");
            ContextMenuRequest? request = null;
            composition.Input.ContextMenuRequested += value => request = value;
            composition.Input.DispatchPointer(
                new(PointerCommandKind.Down, 1, 20, 20, PointerButton.Secondary)
            );
            composition.Input.DispatchPointer(new(PointerCommandKind.Up, 1, 20, 20));
            Assert(request is not null, "Menu request missing.");
            using var dispatcher = new WindowsUiaDispatcher();
            using var cursor = new WindowsCursor();
            using var popup = new WindowsPopupHost(
                window,
                request!,
                dispatcher,
                new WindowsClipboard(),
                cursor
            );
            Assert(
                SDL.GetWindowPosition(popup.WindowHandle, out var beforeX, out var beforeY),
                "Popup position missing."
            );
            var retained = popup.Composition;
            Assert(SDL.SetWindowPosition(window, 350, 270), "Owner movement failed.");
            Assert(SDL.SyncWindow(window), "Moved owner sync failed.");
            popup.Reposition();
            Assert(
                SDL.GetWindowPosition(popup.WindowHandle, out var afterX, out var afterY),
                "Moved popup position missing."
            );
            Assert(
                beforeX == afterX && beforeY == afterY,
                $"Popup offset drifted when owner moved: ({beforeX},{beforeY}) -> ({afterX},{afterY})."
            );
            Assert(
                ReferenceEquals(retained, popup.Composition) && !popup.IsDismissed,
                "Reposition replaced or dismissed the retained menu."
            );
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }
}
