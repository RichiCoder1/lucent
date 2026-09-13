using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

public sealed unsafe partial class UiaLifecycleContracts
{
    [TestMethod]
    public void ShrinkingPopupAppliesNewSizeBeforeFinalAnchorPosition()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(popup resize order) failed.");
        var window = CreateWindow("Lucent popup resize order");
        try
        {
            var display = SDL.GetDisplayForWindow(window);
            var usable = new SDL.Rect();
            Assert(
                display != 0 && SDL.GetDisplayUsableBounds(display, out usable),
                "Display work area missing."
            );
            Assert(
                SDL.SetWindowPosition(window, usable.X + usable.W - 600, usable.Y + 50),
                "Owner positioning failed."
            );
            Assert(SDL.SetWindowSize(window, 600, 400), "Owner sizing failed.");
            Assert(SDL.SyncWindow(window), "Owner sync failed.");
            using var owner = new Composition(new ReactiveGraph(), "popup-resize-order");
            using var request = new ResizablePopupRequest(
                owner,
                new LayoutRect(300, 40, 200, 36),
                initialWidth: 1_200
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

            request.Width = 200;
            popup.Reposition();

            Assert(
                SDL.GetWindowPosition(popup.WindowHandle, out var actualX, out _),
                "Resized popup position missing."
            );
            using var shaper = new SkiaSceneRenderer();
            var expected = WindowsPopupPlacement.Root(
                request.Anchor with
                {
                    Y = request.Anchor.Y + request.AnchorGap,
                },
                request.Measure(shaper, new(600, 400, 1)),
                WindowsPopupHost.ScaleForWindow(window),
                SDL.GetWindowPixelDensity(window)
            );
            Assert(
                actualX == expected.OffsetX,
                $"Popup resize retained an old-width constrained X: {actualX}, expected {expected.OffsetX}."
            );
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

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

    private sealed class ResizablePopupRequest : PopupSurfaceRequest
    {
        private readonly Signal<float> _width;
        private Composition? _popup;
        private bool _dismissed;

        internal ResizablePopupRequest(Composition owner, LayoutRect anchor, float initialWidth)
        {
            Owner = owner;
            Anchor = anchor;
            _width = owner.Root.Scope.Signal(initialWidth, "popup-width");
        }

        public float Width
        {
            get => _width.Value;
            set => _width.Value = value;
        }

        public override Composition Owner { get; }
        public override LayoutRect Anchor { get; }
        public override ThemeAppearance Appearance => ThemeAppearance.Light;
        public override bool IsValid => !Owner.IsDisposed;
        public override bool IsDismissed => _dismissed || !IsValid;
        public override bool IsInteractive => false;

        public override Composition CreateComposition()
        {
            if (_popup is not null)
                return _popup;
            var popup = new Composition(Owner.Graph, "resizable-popup");
            var theme = new ThemeContext(popup.Root.Scope, ControlThemes.Light);
            popup.Root.Present(theme);
            popup.Mount(
                popup.Root,
                theme,
                Components.Layout(
                    [],
                    style: Style.Empty.Bind(LayoutProperties.Width, () => _width.Value).Height(60)
                )
            );
            _popup = popup;
            return popup;
        }

        public override LayoutRect Measure(ITextShaper shaper, LayoutViewport available) =>
            new(0, 0, Math.Min(Width, available.Width), 60);

        public override void Dismiss() => _dismissed = true;

        public override bool RestoreFocus() => false;

        public override void Dispose()
        {
            _dismissed = true;
            _popup?.Dispose();
            _popup = null;
        }
    }
}
