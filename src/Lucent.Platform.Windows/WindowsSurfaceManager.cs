using Lucent.Core;
using SDL3;
using SkiaSharp;

namespace Lucent.Platform.Windows;

/// <summary>Adapts one owner's retained surfaces and their nested menu chains to native windows.</summary>
internal sealed class WindowsSurfaceManager : IDisposable
{
    private readonly InputRouter _ownerInput;
    private readonly Composition _owner;
    private readonly nint _window;
    private readonly WindowsUiaDispatcher _dispatcher;
    private readonly WindowsClipboard _clipboard;
    private readonly WindowsCursor _cursor;
    private readonly Action _wake;
    private readonly Action? _invalidateOwner;
    private PopupSurfaceRequest? _pending;
    private PopupSurfaceRequest? _request;
    private WindowsPopupHost? _host;
    private WindowsPopupChain? _menu;
    private ContextMenuRequest? _pendingMenu;
    private IDisposable? _interactionSuspension;
    private WindowsSurfaceManager? _children;
    private bool _disposed;
    private ElementIdentity? _returnFocus;
    private bool _ownsReturnFocus;
    internal Action<SKCanvas> OwnerOverlay { get; }

    internal WindowsSurfaceManager(
        Composition owner,
        nint window,
        WindowsUiaDispatcher dispatcher,
        WindowsClipboard clipboard,
        WindowsCursor cursor,
        Action wake,
        Action? invalidateOwner = null
    )
    {
        _ownerInput = owner.Input;
        _owner = owner;
        _window = window;
        _dispatcher = dispatcher;
        _clipboard = clipboard;
        _cursor = cursor;
        _wake = wake;
        _invalidateOwner = invalidateOwner;
        OwnerOverlay = DrawOwnerOverlay;
        owner.Input.SurfaceRequested += Request;
        if (owner.Input.ActiveSurface is { } active)
            Request(active);
    }

    private void Request(PopupSurfaceRequest request)
    {
        if (request.IsInteractive)
        {
            var focused = _ownerInput.FocusedElement;
            if (!_ownsReturnFocus || focused is not null)
            {
                _returnFocus = focused;
                _ownsReturnFocus = true;
            }
        }
        _pending?.Dispose();
        _pending = request;
        InvalidateOwner();
    }

    private void RequestMenu(ContextMenuRequest request)
    {
        _pendingMenu?.Dispose();
        _pendingMenu = request;
        _wake();
    }

    internal bool Dispatch(SDL.Event value) =>
        DispatchAndInvalidateOwner(_request, () => DispatchCore(value), _invalidateOwner, _wake);

    private bool DispatchCore(SDL.Event value)
    {
        if (_children?.Dispatch(value) == true)
            return true;
        if (_menu?.Dispatch(value) == true)
            return true;
        var type = (SDL.EventType)value.Type;
        var windowId = WindowsPopupHost.EventWindowId(value);
        if (_host is not null && windowId == SDL.GetWindowID(_window))
        {
            if (type == SDL.EventType.MouseButtonDown)
            {
                if (_request!.IsModal)
                    return true;
                var consume = _request!.ConsumeOutsideClick;
                Dismiss();
                return consume;
            }
            if (
                type
                is SDL.EventType.WindowHidden
                    or SDL.EventType.WindowMinimized
                    or SDL.EventType.WindowCloseRequested
            )
                Dismiss();
            else if (
                WindowsPopupHost.RequiresOwnerReposition(type)
                || type is SDL.EventType.WindowResized or SDL.EventType.WindowPixelSizeChanged
            )
                _host.Reposition();
        }
        if (_host is null)
            return false;
        if (
            _menu is not null
            && windowId == _host.WindowId
            && type == SDL.EventType.MouseButtonDown
        )
        {
            _menu.Dismiss();
            _menu.Dispose();
            _menu = null;
            return true;
        }
        // Focus transitions are resolved after the SDL batch, when a newly opened child
        // has acquired focus. A transient focus-lost event must not close its parent.
        return _host.Dispatch(value, dismissOnFocusLoss: false);
    }

    internal static bool DispatchAndInvalidateOwner(
        PopupSurfaceRequest? request,
        Func<bool> dispatch,
        Action? invalidateOwner,
        Action wake
    )
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(wake);
        var revision = request?.CaptureSharedMutationRevision();
        var handled = dispatch();
        if (
            handled
            && revision is { } before
            && !request!.Owner.IsDisposed
            && request.CaptureSharedMutationRevision() != before
        )
        {
            invalidateOwner?.Invoke();
            wake();
        }
        return handled;
    }

    internal void Synchronize()
    {
        _menu?.ResolveFocus();
        if (_menu?.IsDismissed == true)
        {
            _menu.Dispose();
            _menu = null;
        }
        if (_host?.IsDismissed == true)
            CloseHost();
        if (_pending is { } pending)
        {
            if (pending.IsDismissed)
            {
                var revision = pending.CaptureSharedMutationRevision();
                _pending = null;
                pending.Dispose();
                _ownerInput.CompleteSurface(pending);
                InvalidateOwnerIfChanged(pending, revision);
            }
            else if (pending.HasAnchor)
            {
                CloseHost();
                _pending = null;
                _request = pending;
                try
                {
                    if (pending.IsModal)
                        _interactionSuspension = pending.Owner.SuspendInteraction();
                    _host = new(_window, pending, _dispatcher, _clipboard, _cursor, _wake);
                    _host.Composition.Input.ContextMenuRequested += RequestMenu;
                    _children = new(
                        _host.Composition,
                        _host.WindowHandle,
                        _dispatcher,
                        _clipboard,
                        _cursor,
                        _wake,
                        () => _host?.Refresh()
                    );
                }
                catch
                {
                    CloseHost();
                    throw;
                }
            }
        }
        if (_pendingMenu is { } menu && _host is not null)
        {
            _pendingMenu = null;
            _menu?.Dispose();
            _menu = new(
                _host.WindowHandle,
                _host.HwndHandle,
                menu,
                _dispatcher,
                _clipboard,
                _cursor,
                _wake
            );
        }
        _children?.Synchronize();
        RestoreFinalFocus();
        if (_host is null || !_request!.IsInteractive || _request.IsModal)
            return;
        var focus = SDL.GetKeyboardFocus();
        if (focus != _window && !ContainsFocus(focus))
            Dismiss();
    }

    private bool ContainsFocus(nint window) =>
        _host?.WindowHandle == window
        || (_children?.ContainsFocus(window) ?? false)
        || (_menu?.Levels.Any(level => level.WindowHandle == window) ?? false);

    internal int WaitMilliseconds(TimeSpan now) =>
        WindowsPresentationTiming.Earlier(
            WindowsPresentationTiming.Earlier(
                _host?.PresentationWaitMilliseconds(now) ?? -1,
                _children?.WaitMilliseconds(now) ?? -1
            ),
            WindowsPresentationTiming.Earlier(
                _menu?.PresentationWaitMilliseconds(now) ?? -1,
                _menu?.SafeIntentWaitMilliseconds() ?? -1
            )
        );

    internal void Tick(TimeSpan now)
    {
        _children?.Tick(now);
        _ = _host?.TickPresentation(now);
        _ = _menu?.TickPresentation(now);
        _ = _menu?.Tick();
    }

    internal void Refresh()
    {
        _children?.Refresh();
        _host?.Refresh();
        _menu?.Refresh();
    }

    internal void Dismiss()
    {
        _pending?.Dismiss();
        _request?.Dismiss();
        CloseHost();
    }

    internal void OwnerResizing()
    {
        if (_request?.IsModal == true)
            _host?.Reposition();
        else if (_pending?.IsModal != true)
            Dismiss();
    }

    private void DrawOwnerOverlay(SKCanvas canvas)
    {
        if (
            _request is not { IsModal: true } request
            || request.Appearance.Contrast == ThemeContrast.High
        )
            return;
        var bounds = canvas.LocalClipBounds;
        WindowsModalScrim.Draw(
            canvas,
            new(bounds.Left, bounds.Top, bounds.Width, bounds.Height),
            0,
            1
        );
    }

    private void CloseHost()
    {
        var errors = new List<Exception>();
        var request = _request;
        var revision = request?.CaptureSharedMutationRevision();
        _request = null;
        Capture(errors, () => _children?.Dispose());
        _children = null;
        Capture(errors, () => _pendingMenu?.Dispose());
        _pendingMenu = null;
        Capture(errors, () => _menu?.Dispose());
        _menu = null;
        Capture(errors, () => _interactionSuspension?.Dispose());
        _interactionSuspension = null;
        if (_host is not null)
        {
            if (!_host.Composition.IsDisposed)
                _host.Composition.Input.ContextMenuRequested -= RequestMenu;
            Capture(errors, () => _host.Dispose(restoreFocus: false));
            _host = null;
        }
        else
            Capture(errors, () => request?.Dispose());
        if (!_disposed && request is not null && !request.Owner.IsDisposed)
            Capture(errors, () => _ownerInput.CompleteSurface(request));
        if (
            !_disposed
            && request is not null
            && (
                request.IsModal
                || (
                    revision is { } before
                    && !request.Owner.IsDisposed
                    && request.CaptureSharedMutationRevision() != before
                )
            )
        )
            Capture(errors, InvalidateOwner);
        Capture(errors, RestoreFinalFocus);
        Throw(errors);
    }

    private void RestoreFinalFocus()
    {
        if (!_ownsReturnFocus || _host is not null || _pending is { IsDismissed: false })
            return;
        var focus = _returnFocus;
        if (_disposed || _owner.IsDisposed || focus is null)
        {
            ClearReturnFocus();
            return;
        }
        if (_ownerInput.FocusedElement is not null)
        {
            // An application-selected focus target wins over the surface's captured return target.
            ClearReturnFocus();
            return;
        }
        // The opening trigger can still be disabled in the installed owner scene while close
        // state is settling. Keep the identity when this attempt fails; Synchronize runs again
        // after the next owner scene is installed and can restore the re-enabled target then.
        if (_ownerInput.FocusSemantic(focus.Value))
            ClearReturnFocus();
    }

    private void ClearReturnFocus()
    {
        _returnFocus = null;
        _ownsReturnFocus = false;
    }

    private void InvalidateOwnerIfChanged(PopupSurfaceRequest request, long before)
    {
        if (
            !_disposed
            && !request.Owner.IsDisposed
            && request.CaptureSharedMutationRevision() != before
        )
            InvalidateOwner();
    }

    private void InvalidateOwner()
    {
        _invalidateOwner?.Invoke();
        _wake();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        var errors = new List<Exception>();
        _ownerInput.SurfaceRequested -= Request;
        Capture(errors, () => _pending?.Dispose());
        _pending = null;
        Capture(errors, CloseHost);
        Throw(errors);
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

    private static void Throw(List<Exception> errors)
    {
        if (errors.Count != 0)
            throw new AggregateException("Owned surface cleanup failed.", errors);
    }
}
