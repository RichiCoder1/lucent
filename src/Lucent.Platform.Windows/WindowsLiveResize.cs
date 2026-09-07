using System.Runtime.ExceptionServices;
using SDL3;

namespace Lucent.Platform.Windows;

/// <summary>
/// Uses SDL's supported exposed-event watch to keep the owner window current while the native
/// move/size loop temporarily prevents the outer event loop from returning.
/// </summary>
internal sealed class WindowsLiveResize : IDisposable
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly uint _windowId;
    private readonly Action _render;
    private readonly SDL.EventFilter _watch;
    private readonly Action<SDL.EventFilter, nint> _remove;
    private ExceptionDispatchInfo? _failure;
    private bool _pumping;
    private bool _rendering;
    private bool _disposed;

    internal WindowsLiveResize(uint windowId, Action render)
        : this(windowId, render, SDL.AddEventWatch, SDL.RemoveEventWatch) { }

    internal WindowsLiveResize(
        uint windowId,
        Action render,
        Func<SDL.EventFilter, nint, bool> add,
        Action<SDL.EventFilter, nint> remove
    )
    {
        ArgumentOutOfRangeException.ThrowIfZero(windowId);
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(remove);
        _windowId = windowId;
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
        _remove(_watch, 0);
    }

    private bool Watch(nint _, ref SDL.Event @event)
    {
        if (
            _disposed
            || Environment.CurrentManagedThreadId != _ownerThread
            || !_pumping
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
}
