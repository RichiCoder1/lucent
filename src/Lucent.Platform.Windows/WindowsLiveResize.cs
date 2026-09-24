using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using SDL3;

namespace Lucent.Platform.Windows;

/// <summary>
/// Uses SDL's supported exposed-event watch to keep the owner window current while the native
/// move/size loop temporarily prevents the outer event loop from returning.
/// </summary>
internal sealed unsafe partial class WindowsLiveResize : IDisposable
{
    private const uint WmEnterSizeMove = 0x0231;
    private const uint WmExitSizeMove = 0x0232;
    private const uint WmNcDestroy = 0x0082;
    private static long _nextSubclassId;
    private static readonly nint Procedure = (nint)
        (delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nint, nint>)&SubclassProc;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly uint _windowId;
    private readonly nint _window;
    private readonly nuint _subclassId = checked((nuint)Interlocked.Increment(ref _nextSubclassId));
    private readonly Action _render;
    private readonly SDL.EventFilter _watch;
    private readonly Action<SDL.EventFilter, nint> _remove;
    private readonly Func<bool>? _nativeSizeMoveOverride;
    private GCHandle _handle;
    private ExceptionDispatchInfo? _failure;
    private bool _pumping;
    private bool _rendering;
    private bool _nativeSizeMove;
    private bool _windowDestroyed;
    private bool _disposed;

    internal WindowsLiveResize(uint windowId, nint window, Action render)
        : this(windowId, window, render, SDL.AddEventWatch, SDL.RemoveEventWatch) { }

    internal WindowsLiveResize(
        uint windowId,
        nint window,
        Action render,
        Func<SDL.EventFilter, nint, bool> add,
        Action<SDL.EventFilter, nint> remove
    )
    {
        ArgumentOutOfRangeException.ThrowIfZero(windowId);
        ArgumentOutOfRangeException.ThrowIfZero(window);
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(remove);
        _windowId = windowId;
        _window = window;
        _render = render;
        _remove = remove;
        _watch = Watch;
        _handle = GCHandle.Alloc(this);
        if (!SetWindowSubclass(window, Procedure, _subclassId, GCHandle.ToIntPtr(_handle)))
        {
            _handle.Free();
            throw new InvalidOperationException("SetWindowSubclass(live resize) failed.");
        }
        try
        {
            if (!add(_watch, 0))
                throw new InvalidOperationException($"SDL_AddEventWatch: {SDL.GetError()}");
        }
        catch
        {
            if (RemoveWindowSubclass(window, Procedure, _subclassId))
                ReleaseHandle();
            throw;
        }
    }

    // Test seam: the native message lifecycle is covered separately with a real HWND.
    internal WindowsLiveResize(
        uint windowId,
        Action render,
        Func<bool> nativeSizeMove,
        Func<SDL.EventFilter, nint, bool> add,
        Action<SDL.EventFilter, nint> remove
    )
    {
        ArgumentOutOfRangeException.ThrowIfZero(windowId);
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(nativeSizeMove);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(remove);
        _windowId = windowId;
        _nativeSizeMoveOverride = nativeSizeMove;
        _render = render;
        _remove = remove;
        _watch = Watch;
        if (!add(_watch, 0))
            throw new InvalidOperationException($"SDL_AddEventWatch: {SDL.GetError()}");
    }

    internal void ThrowIfFailed()
    {
        CheckThread();
        _failure?.Throw();
    }

    internal void EnterPump()
    {
        CheckThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pumping)
            throw new InvalidOperationException("The SDL event-pump region cannot be nested.");
        _pumping = true;
    }

    internal void ExitPump()
    {
        CheckThread();
        if (!_pumping)
            throw new InvalidOperationException("No SDL event-pump region is active.");
        _pumping = false;
    }

    public void Dispose()
    {
        CheckThread();
        if (_disposed)
            return;
        _disposed = true;
        Exception? failure = null;
        try
        {
            _remove(_watch, 0);
        }
        catch (Exception error)
        {
            failure = error;
        }
        if (_window != 0 && !_windowDestroyed)
        {
            if (RemoveWindowSubclass(_window, Procedure, _subclassId))
                ReleaseHandle();
            else
                failure ??= new InvalidOperationException(
                    "RemoveWindowSubclass(live resize) failed."
                );
        }
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint SubclassProc(
        nint window,
        uint message,
        nint wParam,
        nint lParam,
        nuint id,
        nint data
    )
    {
        WindowsLiveResize? resize = null;
        try
        {
            resize = GCHandle.FromIntPtr(data).Target as WindowsLiveResize;
            if (resize is not null)
            {
                if (message == WmEnterSizeMove)
                    resize._nativeSizeMove = true;
                else if (message is WmExitSizeMove or WmNcDestroy)
                    resize._nativeSizeMove = false;
            }
        }
        catch { }

        nint result;
        try
        {
            result = DefSubclassProc(window, message, wParam, lParam);
        }
        catch
        {
            result = 0;
        }
        if (message == WmNcDestroy && resize is not null)
        {
            resize._windowDestroyed = true;
            resize.ReleaseHandle();
        }
        return result;
    }

    private void ReleaseHandle()
    {
        if (_handle.IsAllocated)
            _handle.Free();
    }

    private bool Watch(nint _, ref SDL.Event @event)
    {
        if (
            _disposed
            || Environment.CurrentManagedThreadId != _ownerThread
            || !_pumping
            || !(_nativeSizeMoveOverride?.Invoke() ?? _nativeSizeMove)
            || _rendering
            || _failure is not null
            || (SDL.EventType)@event.Type != SDL.EventType.WindowExposed
            || @event.Window.WindowID != _windowId
        )
            return true;

        _rendering = true;
        try
        {
            _render();
        }
        catch (Exception error)
        {
            _failure ??= ExceptionDispatchInfo.Capture(error);
        }
        finally
        {
            _rendering = false;
        }
        return true;
    }

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException(
                "Live resize ownership must remain on the SDL owner thread."
            );
    }

    [LibraryImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(nint window, nint procedure, nuint id, nint data);

    [LibraryImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveWindowSubclass(nint window, nint procedure, nuint id);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(
        nint window,
        uint message,
        nint wParam,
        nint lParam
    );
}
