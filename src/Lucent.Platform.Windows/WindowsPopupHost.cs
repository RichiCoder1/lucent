using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Lucent.Platform.Windows;

/// <summary>Owns one Lucent-rendered SDL popup and its independent input, pixels, and UIA root.</summary>
internal sealed partial class WindowsPopupHost : IDisposable
{
    internal const SDL.WindowFlags PopupFlags =
        SDL.WindowFlags.PopupMenu
        | SDL.WindowFlags.HighPixelDensity
        | SDL.WindowFlags.Hidden
        | SDL.WindowFlags.Transparent;
    internal const float ShadowMargin = 16;

    // ContextMenuRequest retains submenu compositions after their popup window
    // closes. Keep the host padding attached once per retained composition so
    // reopening a branch does not try to install a second presentation model.
    private static readonly ConditionalWeakTable<Composition, object> HostPadding = new();
    private static readonly object HostPaddingMarker = new();
    private LayoutRect _menuBounds;
    private bool _opensLeft;
    private readonly Action<SkiaSharp.SKCanvas> _drawShadow;
    private float _scale = 1;
    private float _cornerRadius;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly uint _ownerWindowId;
    private readonly ContextMenuRequest _request;
    private readonly MenuLevelSnapshot? _level;
    private readonly WindowsPopupHost? _parent;
    private readonly bool _ownsRequest;
    private readonly Composition _composition;
    private readonly WindowsCursor _cursor;
    private readonly SkiaSceneRenderer _sceneRenderer;
    private readonly CpuSkiaPresenter _presenter;
    private readonly WindowsInputAdapter _input;
    private readonly WindowsUiaProvider _uiaProvider;
    private readonly WindowsUiaListener _uiaListener;
    private nint _window;
    private nint _sdlRenderer;
    private RetainedScene? _scene;
    private WindowsViewport _viewport;
    private bool _disposed;

    internal WindowsPopupHost(
        nint ownerWindow,
        ContextMenuRequest request,
        WindowsUiaDispatcher uiaDispatcher,
        WindowsClipboard clipboard,
        WindowsCursor cursor
    )
        : this(
            ownerWindow,
            ownerWindow,
            request,
            level: null,
            uiaDispatcher,
            clipboard,
            cursor,
            parent: null,
            ownsRequest: true
        ) { }

    internal WindowsPopupHost(
        nint rootOwnerWindow,
        nint popupParentWindow,
        ContextMenuRequest request,
        MenuLevelSnapshot level,
        WindowsUiaDispatcher uiaDispatcher,
        WindowsClipboard clipboard,
        WindowsCursor cursor,
        WindowsPopupHost? parent
    )
        : this(
            rootOwnerWindow,
            popupParentWindow,
            request,
            level,
            uiaDispatcher,
            clipboard,
            cursor,
            parent,
            ownsRequest: false
        ) { }

    private WindowsPopupHost(
        nint rootOwnerWindow,
        nint popupParentWindow,
        ContextMenuRequest request,
        MenuLevelSnapshot? level,
        WindowsUiaDispatcher uiaDispatcher,
        WindowsClipboard clipboard,
        WindowsCursor cursor,
        WindowsPopupHost? parent,
        bool ownsRequest
    )
    {
        ArgumentOutOfRangeException.ThrowIfZero(rootOwnerWindow);
        ArgumentOutOfRangeException.ThrowIfZero(popupParentWindow);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(uiaDispatcher);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(cursor);
        _request = request;
        _level = level;
        _parent = parent;
        _ownsRequest = ownsRequest;
        _ownerWindowId = SDL.GetWindowID(rootOwnerWindow);
        if (_ownerWindowId == 0)
            throw new InvalidOperationException($"SDL_GetWindowID root owner: {SDL.GetError()}");
        _cursor = cursor;
        _composition = level?.Composition ?? request.CreateComposition();
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
            var ownerScale = ScaleForWindow(popupParentWindow);
            var density = SDL.GetWindowPixelDensity(popupParentWindow);
            if (!float.IsFinite(density) || density <= 0)
                throw new InvalidOperationException(
                    "SDL_GetWindowPixelDensity returned no popup density."
                );
            var display = SDL.GetDisplayForWindow(popupParentWindow);
            if (display == 0 || !SDL.GetDisplayUsableBounds(display, out var usable))
                throw new InvalidOperationException(
                    $"SDL_GetDisplayUsableBounds: {SDL.GetError()}"
                );
            // SDL display bounds and Windows popup positions are physical screen/window units;
            // only the Core measurement viewport is logical, so content scale is the sole
            // conversion at this boundary. Pixel density belongs to the backing render buffer.
            var available = new LayoutViewport(
                Math.Max(1, usable.W / ownerScale - 2 * ShadowMargin),
                Math.Max(1, usable.H / ownerScale - 2 * ShadowMargin),
                ownerScale
            );
            var desired = level is null
                ? request.Measure(_sceneRenderer, available)
                : request.Measure(level, _sceneRenderer, available);
            var placement =
                level is null || level.Depth == 0
                    ? WindowsPopupPlacement.Root(request.Anchor, desired, ownerScale, density)
                    : WindowsPopupPlacement.Submenu(
                        parent?.TriggerScreenBounds(level.ParentTrigger)
                            ?? throw new InvalidOperationException(
                                "A submenu level did not provide a live parent trigger."
                            ),
                        desired,
                        parent.ScreenBounds,
                        usable,
                        ownerScale,
                        density
                    );
            var offsetX = placement.OffsetX;
            var offsetY = placement.OffsetY;
            _opensLeft = placement.OpensLeft;
            var width = Math.Max(
                1,
                ToWindowUnits(desired.Width + 2 * ShadowMargin, ownerScale, density)
            );
            var height = Math.Max(
                1,
                ToWindowUnits(desired.Height + 2 * ShadowMargin, ownerScale, density)
            );
            // Host padding keeps rendering, hit testing and UIA in the same coordinate space.
            EnsureHostPadding(_composition);
            window = SDL.CreatePopupWindow(
                popupParentWindow,
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
            if (level is null || level.Depth == 0 || level.FocusFirst)
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
            if (_ownsRequest)
                request.Dispose();
            throw;
        }
    }

    internal uint WindowId => _window == 0 ? 0 : SDL.GetWindowID(_window);
    internal nint WindowHandle => _window;
    internal nint HwndHandle => _window == 0 ? 0 : Hwnd(_window);
    internal Composition Composition => _composition;
    internal MenuLevelSnapshot? Level => _level;
    internal bool IsDismissed => _request.IsDismissed;
    internal LayoutRect MenuBounds => _menuBounds;
    internal WindowsViewport ViewportState => _viewport;
    internal bool IsDisposed => _disposed;
    internal bool OpensLeft => _opensLeft;

    internal static void EnsureHostPadding(Composition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        if (HostPadding.TryGetValue(composition, out _))
            return;

        composition.Root.Present(
            new ThemeContext(composition.Root.Scope, new Theme("popup-host")),
            Style.Empty.Padding(Insets.Uniform(ShadowMargin))
        );
        HostPadding.Add(composition, HostPaddingMarker);
    }

    internal bool Dispatch(SDL.Event @event, bool dismissOnFocusLoss = true)
    {
        CheckThread();
        if (_disposed || !TargetsPopup(@event, WindowId, _ownerWindowId))
            return false;
        var type = (SDL.EventType)@event.Type;
        if (_presenter.HandleRendererEvent(type))
        {
            Refresh();
            return true;
        }
        if (type == SDL.EventType.MouseButtonDown)
        {
            var scale = WindowsCoordinateScale.ForWindow(_window);
            var x = scale.WindowToLogical(@event.Button.X);
            var y = scale.WindowToLogical(@event.Button.Y);
            if (!ContainsMenuPoint(_menuBounds, x, y))
            {
                _request.Dismiss();
                return true;
            }
        }
        if (
            type == SDL.EventType.WindowCloseRequested
            || type == SDL.EventType.WindowFocusLost && dismissOnFocusLoss
        )
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
        _viewport = viewport;
        _scene = scene;
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
        if (_ownsRequest)
        {
            Capture(
                errors,
                () =>
                {
                    _ = _request.RestoreFocus();
                }
            );
            Capture(errors, _request.Dispose);
        }
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
            SDL.EventType.RenderTargetsReset
            or SDL.EventType.RenderDeviceReset
            or SDL.EventType.RenderDeviceLost => @event.Render.WindowID,
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
        if (_level is { } level)
        {
            _ = _request.FocusFirst(level);
            return;
        }
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
        var scale = ScaleForWindow(window);
        if (!SDL.GetRenderOutputSize(renderer, out var width, out var height))
            throw new InvalidOperationException($"Windows popup viewport: {SDL.GetError()}");
        return new(width, height, scale);
    }

    internal static float ScaleForWindow(nint window)
    {
        ArgumentOutOfRangeException.ThrowIfZero(window);
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

    internal PopupScreenRect ScreenBounds => GetWindowBounds(HwndHandle);

    internal PopupScreenPoint ToScreenPoint(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(x));
        var origin = ClientOrigin(HwndHandle);
        return new(
            origin.X + WindowsCoordinateScale.WindowToScreenPixels(x),
            origin.Y + WindowsCoordinateScale.WindowToScreenPixels(y)
        );
    }

    internal PopupScreenRect TriggerScreenBounds(ElementIdentity? identity)
    {
        if (identity is not { } trigger || _scene is null)
            throw new InvalidOperationException("A submenu trigger has no projected popup bounds.");
        var box = _scene.Boxes.FirstOrDefault(item => item.Identity == trigger);
        if (box.Identity != trigger)
            throw new InvalidOperationException(
                "The submenu trigger is not present in its parent scene."
            );
        var origin = ClientOrigin(HwndHandle);
        var scale = WindowsCoordinateScale.ForWindow(_window);
        return new(
            origin.X + scale.LogicalToScreenPixels(box.Bounds.X),
            origin.Y + scale.LogicalToScreenPixels(box.Bounds.Y),
            origin.X + scale.LogicalToScreenPixels(box.Bounds.X + box.Bounds.Width),
            origin.Y + scale.LogicalToScreenPixels(box.Bounds.Y + box.Bounds.Height)
        );
    }

    private static PopupScreenRect GetWindowBounds(nint hwnd)
    {
        if (hwnd == 0 || !GetWindowRect(hwnd, out var rect))
            throw new InvalidOperationException("GetWindowRect did not return popup geometry.");
        return new(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    private static ScreenPoint ClientOrigin(nint hwnd)
    {
        var point = new ScreenPoint();
        if (hwnd == 0 || !ClientToScreen(hwnd, ref point))
            throw new InvalidOperationException("ClientToScreen did not return popup geometry.");
        return point;
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

    [LibraryImport("user32.dll", EntryPoint = "ClientToScreen", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(
        System.Runtime.InteropServices.UnmanagedType.Bool
    )]
    private static partial bool ClientToScreen(nint hwnd, ref ScreenPoint point);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(
        System.Runtime.InteropServices.UnmanagedType.Bool
    )]
    private static partial bool GetWindowRect(nint hwnd, out WindowRect rectangle);

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential
    )]
    private struct WindowRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential
    )]
    private struct ScreenPoint
    {
        internal int X;
        internal int Y;
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
