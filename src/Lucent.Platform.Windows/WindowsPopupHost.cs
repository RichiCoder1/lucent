using System.Diagnostics;
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
    private readonly Action<SkiaSharp.SKCanvas> _drawModalScrim;
    private float _scale = 1;
    private float _cornerRadius;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly nint _popupParentWindow;
    private readonly uint _ownerWindowId;
    private readonly PopupSurfaceRequest _request;
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
    private readonly WindowsPopupInputRegion? _inputRegion;
    private readonly Action _wakePresentation;
    private nint _window;
    private nint _sdlRenderer;
    private RetainedScene? _scene;
    private WindowsViewport _viewport;
    private bool _disposed;
    private readonly WindowsCaretBlink _caretBlink = new(WindowsCaretBlink.GetCaretBlinkTime());

    internal WindowsPopupHost(
        nint ownerWindow,
        PopupSurfaceRequest request,
        WindowsUiaDispatcher uiaDispatcher,
        WindowsClipboard clipboard,
        WindowsCursor cursor,
        Action? wakePresentation = null
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
            ownsRequest: true,
            wakePresentation
        ) { }

    internal WindowsPopupHost(
        nint rootOwnerWindow,
        nint popupParentWindow,
        ContextMenuRequest request,
        MenuLevelSnapshot level,
        WindowsUiaDispatcher uiaDispatcher,
        WindowsClipboard clipboard,
        WindowsCursor cursor,
        WindowsPopupHost? parent,
        Action? wakePresentation = null
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
            ownsRequest: false,
            wakePresentation
        ) { }

    private WindowsPopupHost(
        nint rootOwnerWindow,
        nint popupParentWindow,
        PopupSurfaceRequest request,
        MenuLevelSnapshot? level,
        WindowsUiaDispatcher uiaDispatcher,
        WindowsClipboard clipboard,
        WindowsCursor cursor,
        WindowsPopupHost? parent,
        bool ownsRequest,
        Action? wakePresentation
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
        _popupParentWindow = popupParentWindow;
        _ownerWindowId = SDL.GetWindowID(rootOwnerWindow);
        if (_ownerWindowId == 0)
            throw new InvalidOperationException($"SDL_GetWindowID root owner: {SDL.GetError()}");
        _cursor = cursor;
        _composition = level?.Composition ?? request.CreateComposition();
        _sceneRenderer = new SkiaSceneRenderer();
        _wakePresentation = wakePresentation ?? (static () => { });
        _drawShadow = canvas => WindowsPopupShadow.Draw(canvas, _menuBounds, _cornerRadius, _scale);
        _drawModalScrim = canvas =>
            WindowsModalScrim.Draw(canvas, _menuBounds, _cornerRadius, _scale);

        nint window = 0;
        nint renderer = 0;
        CpuSkiaPresenter? presenter = null;
        WindowsInputAdapter? input = null;
        WindowsUiaProvider? provider = null;
        WindowsUiaListener? listener = null;
        WindowsPopupInputRegion? inputRegion = null;
        try
        {
            var placement = CalculatePlacement();
            _opensLeft = placement.OpensLeft;
            var width = Math.Max(1, placement.Width);
            var height = Math.Max(1, placement.Height);
            // Host padding keeps rendering, hit testing and UIA in the same coordinate space.
            EnsureHostPadding(_composition, request);
            window = request.IsModal
                ? SDL.CreateWindow(
                    request.Title,
                    width,
                    height,
                    SDL.WindowFlags.Borderless
                        | SDL.WindowFlags.HighPixelDensity
                        | SDL.WindowFlags.Hidden
                        | SDL.WindowFlags.Transparent
                )
                : SDL.CreatePopupWindow(
                    popupParentWindow,
                    placement.OffsetX,
                    placement.OffsetY,
                    width,
                    height,
                    request.IsInteractive && !request.RetainsOwnerFocus
                        ? PopupFlags
                        : PopupFlags | SDL.WindowFlags.NotFocusable
                );
            if (window == 0)
                throw new InvalidOperationException($"SDL_CreatePopupWindow: {SDL.GetError()}");
            if (request.IsModal)
            {
                var origin = ClientOrigin(Hwnd(popupParentWindow));
                if (
                    !SDL.SetWindowParent(window, popupParentWindow)
                    || !SDL.SetWindowModal(window, true)
                    || !SDL.SetWindowPosition(
                        window,
                        origin.X + placement.OffsetX,
                        origin.Y + placement.OffsetY
                    )
                )
                    throw new InvalidOperationException(
                        $"SDL modal window setup: {SDL.GetError()}"
                    );
            }
            renderer = SDL.CreateRenderer(window, null);
            if (renderer == 0)
                throw new InvalidOperationException($"SDL_CreateRenderer popup: {SDL.GetError()}");
            if (!SDL.SetRenderVSync(renderer, WindowsPresentationContract.VsyncInterval))
                throw new InvalidOperationException($"SDL_SetRenderVSync popup: {SDL.GetError()}");
            var hwnd = Hwnd(window);
            if (!request.IsInteractive)
                inputRegion = new WindowsPopupInputRegion(hwnd);
            provider = new WindowsUiaProvider(
                hwnd,
                _composition,
                uiaDispatcher,
                request is ContextMenuRequest ? "Context menu" : request.Title
            );
            listener = new WindowsUiaListener(hwnd, provider);
            presenter = new CpuSkiaPresenter(renderer);
            input = new WindowsInputAdapter(_composition, window, clipboard);
            _window = window;
            _sdlRenderer = renderer;
            _presenter = presenter;
            _input = input;
            _uiaProvider = provider;
            _uiaListener = listener;
            _inputRegion = inputRegion;
            // UIA virtualization requests are dispatched onto this SDL owner thread. Refresh
            // projects the retained composition before the adapter retries its snapshot lookup.
            _uiaProvider.SetRealizationRefresh(Refresh);
            _composition.PresentationDemandAvailable += _wakePresentation;
            Refresh();
            if (
                request.IsInteractive
                && !request.RetainsOwnerFocus
                && !request.FocusInitial()
                && request.AllowInitialFocusFallback
                && (level is null || level.Depth == 0 || level.FocusFirst)
            )
                FocusFirstItem();
            Refresh();
            if (!SDL.ShowWindow(window))
                throw new InvalidOperationException($"SDL_ShowWindow popup: {SDL.GetError()}");
            if (request.IsModal && !SDL.RaiseWindow(window))
                throw new InvalidOperationException($"SDL_RaiseWindow dialog: {SDL.GetError()}");
            if (!SDL.SyncWindow(window))
                throw new InvalidOperationException($"SDL_SyncWindow popup: {SDL.GetError()}");
        }
        catch (Exception error)
        {
            List<Exception> cleanup = [];
            Capture(cleanup, () => _composition.PresentationDemandAvailable -= _wakePresentation);
            if (!_composition.IsDisposed)
                Capture(cleanup, () => _composition.SetPresentationAvailable(false));
            Capture(cleanup, () => input?.Dispose());
            Capture(cleanup, () => _scene?.Dispose());
            Capture(cleanup, () => presenter?.Dispose());
            Capture(cleanup, _sceneRenderer.Dispose);
            if (renderer != 0)
                Capture(cleanup, () => SDL.DestroyRenderer(renderer));
            Capture(cleanup, () => listener?.Dispose());
            Capture(cleanup, () => inputRegion?.Dispose());
            Capture(cleanup, () => provider?.SetRealizationRefresh(null));
            Capture(cleanup, () => provider?.Dispose());
            if (window != 0)
                Capture(cleanup, () => SDL.DestroyWindow(window));
            if (_ownsRequest)
                Capture(cleanup, request.Dispose);
            if (cleanup.Count == 0)
                throw;
            throw new AggregateException(
                "Windows popup construction and cleanup both failed.",
                [error, .. cleanup]
            );
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

    internal int PresentationWaitMilliseconds(TimeSpan now)
    {
        CheckThread();
        return _disposed || _composition.IsDisposed
            ? -1
            : WindowsPresentationTiming.Earlier(
                WindowsPresentationTiming.WaitMilliseconds(_composition.PresentationDemand, now),
                _caretBlink.WaitMilliseconds((long)now.TotalMilliseconds)
            );
    }

    internal bool TickPresentation(TimeSpan now)
    {
        CheckThread();
        if (PresentationWaitMilliseconds(now) != 0)
            return false;
        Refresh(now);
        return true;
    }

    internal static void EnsureHostPadding(Composition composition, PopupSurfaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(composition);
        if (HostPadding.TryGetValue(composition, out _))
            return;

        request.ConfigureHostPadding(composition, Insets.Uniform(ShadowMargin));
        HostPadding.Add(composition, HostPaddingMarker);
    }

    internal bool Dispatch(SDL.Event @event, bool dismissOnFocusLoss = true)
    {
        CheckThread();
        if (_disposed || !TargetsPopup(@event, WindowId, _ownerWindowId))
            return false;
        if (_request.RetainsOwnerFocus && EventWindowId(@event) == _ownerWindowId)
            return false;
        var type = (SDL.EventType)@event.Type;
        if (_request is OwnedSurfaceRequest surface && EventWindowId(@event) == WindowId)
        {
            if (type == SDL.EventType.WindowMouseEnter)
                surface.SetPointerInside(true);
            else if (type == SDL.EventType.WindowMouseLeave)
                surface.SetPointerInside(false);
        }
        if (!_request.IsInteractive && EventWindowId(@event) != WindowId)
            return false;
        if (type == SDL.EventType.WindowMinimized)
        {
            _composition.SetPresentationAvailable(false);
            return true;
        }
        if (type == SDL.EventType.WindowRestored)
        {
            _composition.SetPresentationAvailable(true);
            Refresh();
            return true;
        }
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
                if (!_request.IsModal)
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

    /// <summary>
    /// Reanchors this popup after its owner or display moved. SDL popup positions remain relative
    /// to the parent; refresh the parent first in <see cref="WindowsPopupChain.Reposition"/>
    /// so a child uses live geometry.
    /// </summary>
    internal void Reposition()
    {
        CheckThread();
        if (_disposed || _request.IsDismissed || _composition.IsDisposed)
            return;

        var placement = CalculatePlacement();
        _opensLeft = placement.OpensLeft;
        var origin = _request.IsModal ? ClientOrigin(Hwnd(_popupParentWindow)) : default;
        // SDL constrains popup positions against their current outer size. Resize first so a
        // restored owner cannot clamp the new anchor using the prior maximized popup width.
        if (!SDL.SetWindowSize(_window, placement.Width, placement.Height))
            throw new InvalidOperationException($"SDL_SetWindowSize popup: {SDL.GetError()}");
        if (
            !SDL.SetWindowPosition(
                _window,
                origin.X + placement.OffsetX,
                origin.Y + placement.OffsetY
            )
        )
            throw new InvalidOperationException($"SDL_SetWindowPosition popup: {SDL.GetError()}");
        if (!SDL.SyncWindow(_window))
            throw new InvalidOperationException($"SDL_SyncWindow popup: {SDL.GetError()}");
        Refresh();
    }

    internal void Refresh() => Refresh(Stopwatch.GetElapsedTime(0));

    private void Refresh(TimeSpan now)
    {
        CheckThread();
        if (_disposed || !PrepareRefresh(_request, _composition))
            return;
        if (!ResizeToCurrentContent())
            return;
        var viewport = Viewport(_window, _sdlRenderer);
        if (!viewport.IsRenderable)
        {
            _composition.SetPresentationAvailable(false);
            return;
        }
        _composition.SetPresentationAvailable(true);
        var scene = WindowsBootstrap.ProjectAndInstall(
            _composition,
            new(viewport.LogicalWidth, viewport.LogicalHeight, viewport.Scale),
            _sceneRenderer,
            _scene,
            now
        );
        var previous = _scene;
        try
        {
            _viewport = viewport;
            _scene = scene;
            _uiaProvider.Refresh(scene);
            _input.RefreshTextInput();
            var hasCaret = _request.IsInteractive && _composition.Input.TryGetCaretGeometry(out _);
            _composition.Input.TryGetCaretGeometry(out var caretBounds);
            _caretBlink.SetTarget(
                hasCaret ? _composition.Input.FocusedElement : null,
                caretBounds,
                (long)now.TotalMilliseconds
            );
            _caretBlink.BeforePresent((long)now.TotalMilliseconds);
            var menuRoot = _composition.Root.Children.Single();
            _menuBounds = scene.Boxes.Single(box => box.Identity.ElementId == menuRoot.Id).Bounds;
            _cornerRadius = menuRoot.Resolve(VisualProperties.CornerRadius).Value;
            _scale = viewport.Scale;
            _inputRegion?.Update(_menuBounds, _scale);
            _ = _presenter.Present(
                scene,
                viewport,
                _sceneRenderer,
                showCaret: _caretBlink.Visible,
                drawUnderlay: _request.Appearance.Contrast == ThemeContrast.High
                    ? null
                    : _drawShadow,
                drawOverlay: _composition.IsInteractionSuspended
                && _request.Appearance.Contrast != ThemeContrast.High
                    ? _drawModalScrim
                    : null
            );
            _caretBlink.Presented((long)now.TotalMilliseconds);
            _ = _composition.TryAcknowledgePresentation(scene.Generation);
            // A popup can repaint while the pointer is still over its owner.
            // Only the window under the mouse may change SDL's shared cursor.
            if (SDL.GetMouseFocus() == _window)
                _ = _cursor.Activate(
                    _input.PointerPosition is { } point
                        ? _composition.Input.CursorAt(point.X, point.Y)
                        : CursorIntent.Default
                );
        }
        catch (Exception error)
        {
            // ProjectAndInstall has already installed this candidate in the popup
            // router. Keep the same scene when a later renderer/UIA step fails;
            // disposing it here would leave InputRouter pointing at a released
            // frame. The host cleanup path will release the candidate, while the
            // superseded frame can be released independently.
            if (ReferenceEquals(_scene, scene))
            {
                try
                {
                    previous?.Dispose();
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException(
                        "The previous popup scene failed to release after replacement.",
                        error,
                        cleanup
                    );
                }
            }
            else
            {
                try
                {
                    scene.Dispose();
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException(
                        "The popup candidate failed to release after refresh.",
                        error,
                        cleanup
                    );
                }
            }
            throw;
        }
        previous?.Dispose();
    }

    private bool ResizeToCurrentContent()
    {
        var placement = CalculatePlacement();
        if (_disposed || _request.IsDismissed || _composition.IsDisposed)
            return false;
        if (!SDL.GetWindowSize(_window, out var width, out var height))
            throw new InvalidOperationException($"SDL_GetWindowSize popup: {SDL.GetError()}");
        if (!NeedsContentResize(width, height, placement))
            return true;

        _opensLeft = placement.OpensLeft;
        var origin = _request.IsModal ? ClientOrigin(Hwnd(_popupParentWindow)) : default;
        if (!SDL.SetWindowSize(_window, placement.Width, placement.Height))
            throw new InvalidOperationException($"SDL_SetWindowSize popup: {SDL.GetError()}");
        if (
            !SDL.SetWindowPosition(
                _window,
                origin.X + placement.OffsetX,
                origin.Y + placement.OffsetY
            )
        )
            throw new InvalidOperationException($"SDL_SetWindowPosition popup: {SDL.GetError()}");
        if (!SDL.SyncWindow(_window))
            throw new InvalidOperationException($"SDL_SyncWindow popup: {SDL.GetError()}");
        return true;
    }

    internal static bool NeedsContentResize(
        int currentWidth,
        int currentHeight,
        PopupHostPlacement placement
    ) => currentWidth != placement.Width || currentHeight != placement.Height;

    internal static bool PrepareRefresh(PopupSurfaceRequest request, Composition composition)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(composition);
        if (request.IsDismissed || composition.IsDisposed)
            return false;
        // Popup state shares the owner's reactive graph. Flushing can acknowledge a
        // controlled close and dispose this popup composition, so recheck before projection.
        composition.Flush();
        return !request.IsDismissed && !composition.IsDisposed;
    }

    internal void Dismiss() => _request.Dismiss();

    internal static bool ContainsMenuPoint(LayoutRect bounds, float x, float y) =>
        x >= bounds.X
        && y >= bounds.Y
        && x < bounds.X + bounds.Width
        && y < bounds.Y + bounds.Height;

    public void Dispose() => Dispose(restoreFocus: true);

    internal void Dispose(bool restoreFocus)
    {
        CheckThread();
        if (_disposed)
            return;
        _disposed = true;
        var errors = new List<Exception>();
        if (!_composition.IsDisposed)
        {
            Capture(errors, () => _composition.PresentationDemandAvailable -= _wakePresentation);
            Capture(errors, () => _composition.SetPresentationAvailable(false));
        }
        Capture(errors, _input.Dispose);
        Capture(
            errors,
            () =>
            {
                _scene?.Dispose();
                _scene = null;
            }
        );
        Capture(errors, _presenter.Dispose);
        Capture(errors, _sceneRenderer.Dispose);
        if (_sdlRenderer != 0)
        {
            Capture(errors, () => SDL.DestroyRenderer(_sdlRenderer));
            _sdlRenderer = 0;
        }
        Capture(errors, _uiaListener.Dispose);
        Capture(errors, () => _inputRegion?.Dispose());
        Capture(errors, () => _uiaProvider.SetRealizationRefresh(null));
        Capture(errors, _uiaProvider.Dispose);
        if (_window != 0)
        {
            Capture(errors, () => SDL.DestroyWindow(_window));
            _window = 0;
        }
        if (_ownsRequest)
        {
            if (restoreFocus)
                Capture(errors, () => _ = _request.RestoreFocus());
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
            or SDL.EventType.WindowMoved
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

    internal static bool RequiresOwnerReposition(SDL.EventType type) =>
        type
            is SDL.EventType.WindowMoved
                or SDL.EventType.WindowExposed
                or SDL.EventType.WindowDisplayChanged
                or SDL.EventType.WindowDisplayScaleChanged
                or SDL.EventType.WindowRestored;

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
            _ = ((ContextMenuRequest)_request).FocusFirst(level);
            return;
        }
        var scene =
            _scene ?? throw new InvalidOperationException("The popup has no retained scene.");
        foreach (var item in scene.Input.OrderBy(item => item.Order))
            if (_composition.Input.FocusSemantic(item.Identity))
                return;
    }

    private PopupHostPlacement CalculatePlacement()
    {
        var ownerScale = ScaleForWindow(_popupParentWindow);
        var density = SDL.GetWindowPixelDensity(_popupParentWindow);
        if (!float.IsFinite(density) || density <= 0)
            throw new InvalidOperationException(
                "SDL_GetWindowPixelDensity returned no popup density."
            );
        var display = SDL.GetDisplayForWindow(_popupParentWindow);
        if (display == 0 || !SDL.GetDisplayUsableBounds(display, out var usable))
            throw new InvalidOperationException($"SDL_GetDisplayUsableBounds: {SDL.GetError()}");
        // SDL display bounds and Windows popup positions are physical screen/window units;
        // only the Core measurement viewport is logical, so content scale is the sole
        // conversion at this boundary. Pixel density belongs to the backing render buffer.
        var available = new LayoutViewport(
            Math.Max(1, usable.W / ownerScale - 2 * ShadowMargin),
            Math.Max(1, usable.H / ownerScale - 2 * ShadowMargin),
            ownerScale
        );
        var desired = _level is null
            ? _request.Measure(_sceneRenderer, available)
            : ((ContextMenuRequest)_request).Measure(_level, _sceneRenderer, available);
        if (_request.IsModal)
        {
            var parentBounds = GetWindowBounds(Hwnd(_popupParentWindow));
            var origin = ClientOrigin(Hwnd(_popupParentWindow));
            var width = Math.Max(
                1,
                ToWindowUnits(desired.Width + 2 * ShadowMargin, ownerScale, density)
            );
            var height = Math.Max(
                1,
                ToWindowUnits(desired.Height + 2 * ShadowMargin, ownerScale, density)
            );
            var left = Math.Clamp(
                (parentBounds.Left + parentBounds.Right - width) / 2,
                usable.X,
                Math.Max(usable.X, usable.X + usable.W - width)
            );
            var top = Math.Clamp(
                (parentBounds.Top + parentBounds.Bottom - height) / 2,
                usable.Y,
                Math.Max(usable.Y, usable.Y + usable.H - height)
            );
            return new(left - origin.X, top - origin.Y, width, height, false);
        }
        var placement =
            _level is null || _level.Depth == 0
                ? WindowsPopupPlacement.Root(
                    _request.Anchor with
                    {
                        Y = _request.Anchor.Y + _request.AnchorGap,
                    },
                    desired,
                    ownerScale,
                    density
                )
                : WindowsPopupPlacement.Submenu(
                    _parent?.TriggerScreenBounds(_level.ParentTrigger)
                        ?? throw new InvalidOperationException(
                            "A submenu level did not provide a live parent trigger."
                        ),
                    desired,
                    _parent.ScreenBounds,
                    usable,
                    ownerScale,
                    density
                );
        return new(
            placement.OffsetX,
            placement.OffsetY,
            Math.Max(1, ToWindowUnits(desired.Width + 2 * ShadowMargin, ownerScale, density)),
            Math.Max(1, ToWindowUnits(desired.Height + 2 * ShadowMargin, ownerScale, density)),
            placement.OpensLeft
        );
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

internal readonly record struct PopupHostPlacement(
    int OffsetX,
    int OffsetY,
    int Width,
    int Height,
    bool OpensLeft
);

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
