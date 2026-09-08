using System.Runtime.ExceptionServices;
using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Lucent.Platform.Windows;

/// <summary>Owns one Lucent-rendered SDL popup and its independent input, pixels, and UIA root.</summary>
internal sealed class WindowsPopupHost : IDisposable
{
    internal const SDL.WindowFlags PopupFlags =
        SDL.WindowFlags.PopupMenu
        | SDL.WindowFlags.HighPixelDensity
        | SDL.WindowFlags.Hidden
        | SDL.WindowFlags.Transparent;
    internal const float ShadowMargin = 16;
    private LayoutRect _menuBounds;
    private readonly Action<SkiaSharp.SKCanvas> _drawShadow;
    private float _scale = 1;
    private float _cornerRadius;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly uint _ownerWindowId;
    private readonly ContextMenuRequest _request;
    private readonly Composition _composition;
    private readonly WindowsCursor _cursor;
    private readonly SkiaSceneRenderer _sceneRenderer;
    private readonly CpuSkiaPresenter _presenter;
    private readonly WindowsInputAdapter _input;
    private readonly WindowsUiaProvider _uiaProvider;
    private readonly WindowsUiaListener _uiaListener;
    private nint _window;
    private nint _sdlRenderer;
    private bool _disposed;

    internal WindowsPopupHost(
        nint ownerWindow,
        ContextMenuRequest request,
        WindowsUiaDispatcher uiaDispatcher,
        WindowsClipboard clipboard,
        WindowsCursor cursor
    )
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerWindow);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(uiaDispatcher);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(cursor);
        _request = request;
        _ownerWindowId = SDL.GetWindowID(ownerWindow);
        if (_ownerWindowId == 0)
            throw new InvalidOperationException($"SDL_GetWindowID owner: {SDL.GetError()}");
        _cursor = cursor;
        _composition = request.CreateComposition();
        _sceneRenderer = new SkiaSceneRenderer();
        _drawShadow = canvas => WindowsPopupShadow.Draw(canvas, _menuBounds, _cornerRadius, _scale);

        nint window = 0;
        nint renderer = 0;
        CpuSkiaPresenter? presenter = null;
        WindowsInputAdapter? input = null;
        WindowsUiaProvider? provider = null;
        WindowsUiaListener? listener = null;
        try
        {
            var ownerScale = Scale(ownerWindow);
            var density = SDL.GetWindowPixelDensity(ownerWindow);
            if (!float.IsFinite(density) || density <= 0)
                throw new InvalidOperationException(
                    "SDL_GetWindowPixelDensity returned no popup density."
                );
            var display = SDL.GetDisplayForWindow(ownerWindow);
            if (display == 0 || !SDL.GetDisplayUsableBounds(display, out var usable))
                throw new InvalidOperationException(
                    $"SDL_GetDisplayUsableBounds: {SDL.GetError()}"
                );
            var available = new LayoutViewport(
                Math.Max(1, usable.W * density / ownerScale - 2 * ShadowMargin),
                Math.Max(1, usable.H * density / ownerScale - 2 * ShadowMargin),
                ownerScale
            );
            var desired = request.Measure(_sceneRenderer, available);
            var offsetX = ToWindowUnits(request.Anchor.X - ShadowMargin, ownerScale, density);
            var offsetY = ToWindowUnits(
                request.Anchor.Y + request.Anchor.Height - ShadowMargin,
                ownerScale,
                density
            );
            var width = Math.Max(
                1,
                ToWindowUnits(desired.Width + 2 * ShadowMargin, ownerScale, density)
            );
            var height = Math.Max(
                1,
                ToWindowUnits(desired.Height + 2 * ShadowMargin, ownerScale, density)
            );
            // Host padding keeps rendering, hit testing and UIA in the same coordinate space.
            _composition.Root.Present(
                new ThemeContext(_composition.Root.Scope, new Theme("popup-host")),
                Style.Empty.Padding(Insets.Uniform(ShadowMargin))
            );
            window = SDL.CreatePopupWindow(
                ownerWindow,
                offsetX,
                offsetY,
                width,
                height,
                PopupFlags
            );
            if (window == 0)
                throw new InvalidOperationException($"SDL_CreatePopupWindow: {SDL.GetError()}");
            renderer = SDL.CreateRenderer(window, null);
            if (renderer == 0)
                throw new InvalidOperationException($"SDL_CreateRenderer popup: {SDL.GetError()}");
            if (!SDL.SetRenderVSync(renderer, WindowsPresentationContract.VsyncInterval))
                throw new InvalidOperationException($"SDL_SetRenderVSync popup: {SDL.GetError()}");
            var hwnd = Hwnd(window);
            provider = new WindowsUiaProvider(hwnd, _composition, uiaDispatcher, "Context menu");
            listener = new WindowsUiaListener(hwnd, provider);
            presenter = new CpuSkiaPresenter(renderer);
            input = new WindowsInputAdapter(_composition, window, clipboard);
            _window = window;
            _sdlRenderer = renderer;
            _presenter = presenter;
            _input = input;
            _uiaProvider = provider;
            _uiaListener = listener;
            Refresh();
            FocusFirstItem();
            Refresh();
            if (!SDL.ShowWindow(window))
                throw new InvalidOperationException($"SDL_ShowWindow popup: {SDL.GetError()}");
            if (!SDL.SyncWindow(window))
                throw new InvalidOperationException($"SDL_SyncWindow popup: {SDL.GetError()}");
        }
        catch
        {
            input?.Dispose();
            presenter?.Dispose();
            _sceneRenderer.Dispose();
            if (renderer != 0)
                SDL.DestroyRenderer(renderer);
            if (window != 0)
                SDL.DestroyWindow(window);
            listener?.Dispose();
            provider?.Dispose();
            request.Dispose();
            throw;
        }
    }

    internal uint WindowId => _window == 0 ? 0 : SDL.GetWindowID(_window);
    internal bool IsDismissed => _request.IsDismissed;

    internal bool Dispatch(SDL.Event @event)
    {
        CheckThread();
        if (_disposed || !TargetsPopup(@event, WindowId, _ownerWindowId))
            return false;
        var type = (SDL.EventType)@event.Type;
        if (
            type == SDL.EventType.MouseButtonDown
            && !ContainsMenuPoint(_menuBounds, @event.Button.X, @event.Button.Y)
        )
        {
            _request.Dismiss();
            return true;
        }
        if (type is SDL.EventType.WindowCloseRequested or SDL.EventType.WindowFocusLost)
        {
            _request.Dismiss();
            return true;
        }
        var changed = _input.Dispatch(@event) || _input.ConsumeRepaintRequest();
        if (
            changed
            || type
                is SDL.EventType.WindowExposed
                    or SDL.EventType.WindowResized
                    or SDL.EventType.WindowPixelSizeChanged
                    or SDL.EventType.WindowDisplayChanged
                    or SDL.EventType.WindowDisplayScaleChanged
        )
            Refresh();
        return true;
    }

    internal void Refresh()
    {
        CheckThread();
        if (_disposed || _request.IsDismissed || _composition.IsDisposed)
            return;
        var viewport = Viewport(_window, _sdlRenderer);
        if (!viewport.IsRenderable)
            return;
        var scene = WindowsBootstrap.ProjectAndInstall(
            _composition,
            new(viewport.LogicalWidth, viewport.LogicalHeight, viewport.Scale),
            _sceneRenderer
        );
        _uiaProvider.Refresh(scene);
        _input.RefreshTextInput();
        var menuRoot = _composition.Root.Children.Single();
        _menuBounds = scene.Boxes.Single(box => box.Identity.ElementId == menuRoot.Id).Bounds;
        _cornerRadius = menuRoot.Resolve(VisualProperties.CornerRadius).Value;
        _scale = viewport.Scale;
        _ = _presenter.Present(
            scene,
            viewport,
            _sceneRenderer,
            showCaret: false,
            drawUnderlay: _request.Appearance.Contrast == ThemeContrast.High ? null : _drawShadow
        );
        _ = _cursor.Activate(
            _input.PointerPosition is { } point
                ? _composition.Input.CursorAt(point.X, point.Y)
                : CursorIntent.Default
        );
    }

    internal void Dismiss() => _request.Dismiss();

    internal static bool ContainsMenuPoint(LayoutRect bounds, float x, float y) =>
        x >= bounds.X
        && y >= bounds.Y
        && x < bounds.X + bounds.Width
        && y < bounds.Y + bounds.Height;

    public void Dispose()
    {
        CheckThread();
        if (_disposed)
            return;
        _disposed = true;
        var errors = new List<Exception>();
        Capture(errors, _input.Dispose);
        Capture(errors, _presenter.Dispose);
        Capture(errors, _sceneRenderer.Dispose);
        if (_sdlRenderer != 0)
        {
            Capture(errors, () => SDL.DestroyRenderer(_sdlRenderer));
            _sdlRenderer = 0;
        }
        if (_window != 0)
        {
            Capture(errors, () => SDL.DestroyWindow(_window));
            _window = 0;
        }
        Capture(errors, _uiaListener.Dispose);
        Capture(errors, _uiaProvider.Dispose);
        Capture(
            errors,
            () =>
            {
                _ = _request.RestoreFocus();
            }
        );
        Capture(errors, _request.Dispose);
        if (errors.Count == 1)
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1)
            throw new AggregateException("Windows popup cleanup failed.", errors);
    }

    internal static uint EventWindowId(SDL.Event @event) =>
        (SDL.EventType)@event.Type switch
        {
            SDL.EventType.KeyDown or SDL.EventType.KeyUp => @event.Key.WindowID,
            SDL.EventType.TextInput => @event.Text.WindowID,
            SDL.EventType.TextEditing => @event.Edit.WindowID,
            SDL.EventType.MouseMotion => @event.Motion.WindowID,
            SDL.EventType.MouseButtonDown or SDL.EventType.MouseButtonUp => @event.Button.WindowID,
            SDL.EventType.MouseWheel => @event.Wheel.WindowID,
            SDL.EventType.WindowCloseRequested
            or SDL.EventType.WindowFocusLost
            or SDL.EventType.WindowFocusGained
            or SDL.EventType.WindowMouseEnter
            or SDL.EventType.WindowMouseLeave
            or SDL.EventType.WindowExposed
            or SDL.EventType.WindowResized
            or SDL.EventType.WindowPixelSizeChanged
            or SDL.EventType.WindowDisplayChanged
            or SDL.EventType.WindowDisplayScaleChanged
            or SDL.EventType.WindowMinimized
            or SDL.EventType.WindowRestored => @event.Window.WindowID,
            _ => 0,
        };

    internal static bool TargetsPopup(SDL.Event @event, uint popupWindowId, uint ownerWindowId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(popupWindowId);
        ArgumentOutOfRangeException.ThrowIfZero(ownerWindowId);
        var eventWindowId = EventWindowId(@event);
        if (eventWindowId == popupWindowId)
            return true;
        return eventWindowId == ownerWindowId
            && (SDL.EventType)@event.Type
                is SDL.EventType.KeyDown
                    or SDL.EventType.KeyUp
                    or SDL.EventType.TextInput
                    or SDL.EventType.TextEditing;
    }

    private void FocusFirstItem()
    {
        var viewport = Viewport(_window, _sdlRenderer);
        var scene = WindowsBootstrap.ProjectAndInstall(
            _composition,
            new(viewport.LogicalWidth, viewport.LogicalHeight, viewport.Scale),
            _sceneRenderer
        );
        foreach (var item in scene.Input.OrderBy(item => item.Order))
            if (_composition.Input.FocusSemantic(item.Identity))
                return;
    }

    private static WindowsViewport Viewport(nint window, nint renderer)
    {
        var scale = Scale(window);
        if (!SDL.GetRenderOutputSize(renderer, out var width, out var height))
            throw new InvalidOperationException($"Windows popup viewport: {SDL.GetError()}");
        return new(width, height, scale);
    }

    private static float Scale(nint window)
    {
        var dpi = PInvoke.GetDpiForWindow(new HWND(Hwnd(window)));
        if (dpi == 0)
            throw new InvalidOperationException("GetDpiForWindow returned zero for an SDL window.");
        return dpi / 96F;
    }

    private static nint Hwnd(nint window)
    {
        var hwnd = SDL.GetPointerProperty(
            SDL.GetWindowProperties(window),
            SDL.Props.WindowWin32HWNDPointer,
            0
        );
        if (hwnd == 0)
            throw new InvalidOperationException("SDL popup window did not expose an HWND.");
        return hwnd;
    }

    internal static int ToWindowUnits(float logical, float dpiScale, float density)
    {
        if (
            !float.IsFinite(logical)
            || !float.IsFinite(dpiScale)
            || dpiScale <= 0
            || !float.IsFinite(density)
            || density <= 0
        )
            throw new ArgumentOutOfRangeException(nameof(logical));
        return checked((int)MathF.Ceiling(logical * dpiScale / density));
    }

    private static void Capture(List<Exception> errors, Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
    }

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException(
                "Popup access must remain on the SDL owner thread."
            );
    }
}

internal sealed class WindowsPopupInputGate
{
    private bool _suppressAfterFocusLoss;

    internal void Opened() => _suppressAfterFocusLoss = false;

    internal void LostFocus() => _suppressAfterFocusLoss = true;

    internal bool ConsumeOwnerPointerDown(bool popupOpen)
    {
        if (!popupOpen && !_suppressAfterFocusLoss)
            return false;
        _suppressAfterFocusLoss = false;
        return true;
    }
}
