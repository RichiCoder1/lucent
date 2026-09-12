using Lucent.Core;
using SDL3;

namespace Lucent.Platform.Windows;

/// <summary>Serializes native modal work outside Core dispatch; the host owns the HWND until Show returns.</summary>
internal sealed class WindowsFilePickerHost : IDisposable
{
    private readonly Composition _owner;
    private readonly nint _hwnd;
    private readonly Action _wake;
    private readonly Func<nint, WindowsFilePickerRequest, Func<bool>, FilePickerResult> _show;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly object _gate = new();
    private readonly Queue<Pending> _queue = new();
    private readonly Func<bool> _closeRequested;
    private readonly SDL.EventFilter? _watch;
    private bool _active;
    private int _closeGeneration;
    private volatile bool _closing;
    private bool _disposed;

    internal WindowsFilePickerHost(
        Composition owner,
        nint hwnd,
        Action wake,
        Func<nint, WindowsFilePickerRequest, Func<bool>, FilePickerResult> show,
        Func<bool>? closeRequested = null,
        uint windowId = 0
    )
    {
        _owner = owner;
        _hwnd = hwnd;
        _wake = wake;
        _show = show;
        _closeRequested = closeRequested ?? (() => false);
        if (windowId != 0)
        {
            _watch = (nint data, ref SDL.Event @event) =>
            {
                if (
                    (SDL.EventType)@event.Type == SDL.EventType.Quit
                    || (
                        (SDL.EventType)@event.Type == SDL.EventType.WindowCloseRequested
                        && @event.Window.WindowID == windowId
                    )
                )
                    DismissForCloseRequest();
                return true;
            };
            if (!SDL.AddEventWatch(_watch, 0))
                throw new InvalidOperationException(
                    "Could not watch native picker owner closure: " + SDL.GetError()
                );
        }
        owner.Root.Scope.OnDispose(RequestClose);
    }

    internal ValueTask<FilePickerResult> Enqueue(
        WindowsFilePickerRequest request,
        CancellationToken cancellation
    )
    {
        lock (_gate)
        {
            if (_closing || _disposed || cancellation.IsCancellationRequested)
                return ValueTask.FromResult(new FilePickerResult(FilePickerStatus.Canceled));
            var pending = new Pending(request, Volatile.Read(ref _closeGeneration), cancellation);
            _queue.Enqueue(pending);
            _wake();
            return new(pending.Completion.Task);
        }
    }

    internal void RequestClose() => _closing = true;

    // A vetoed application close must not permanently disable future picker requests.
    internal void DismissForCloseRequest() => Interlocked.Increment(ref _closeGeneration);

    internal bool ProcessOne()
    {
        CheckThread();
        Pending pending;
        lock (_gate)
        {
            if (_disposed || _active || !_queue.TryDequeue(out pending!))
                return false;
            _active = true;
        }
        var focus = _owner.IsDisposed ? null : _owner.Input.FocusedElement;
        bool Canceled() =>
            _closing
            || _closeRequested()
            || pending.Cancellation.IsCancellationRequested
            || pending.CloseGeneration != Volatile.Read(ref _closeGeneration);
        try
        {
            if (Canceled() || _owner.IsDisposed)
                pending.Completion.TrySetResult(new(FilePickerStatus.Canceled));
            else
            {
                FilePickerResult result;
                using (_owner.SuspendInteraction())
                    result = _show(_hwnd, pending.Request, Canceled);
                pending.Completion.TrySetResult(
                    Canceled() ? new(FilePickerStatus.Canceled) : result
                );
            }
        }
        catch (Exception error)
        {
            pending.Completion.TrySetException(error);
            throw;
        }
        finally
        {
            lock (_gate)
            {
                _active = false;
                if (_queue.Count != 0 && !_disposed)
                    _wake();
            }
            if (
                !_closing
                && !_owner.IsDisposed
                && focus is { } identity
                && _owner.Input.FocusedElement is null
            )
                _owner.Input.FocusSemantic(identity);
        }
        return true;
    }

    public void Dispose()
    {
        CheckThread();
        lock (_gate)
        {
            if (_disposed)
                return;
            _closing = true;
            if (_active)
                throw new InvalidOperationException(
                    "The native picker must unwind before its owner is destroyed."
                );
            _disposed = true;
            if (_watch is not null)
                SDL.RemoveEventWatch(_watch, 0);
            while (_queue.TryDequeue(out var pending))
                pending.Completion.TrySetResult(new(FilePickerStatus.Canceled));
        }
    }

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException(
                "Native picker dispatch belongs to the window owner thread."
            );
    }

    private sealed record Pending(
        WindowsFilePickerRequest Request,
        int CloseGeneration,
        CancellationToken Cancellation
    )
    {
        internal TaskCompletionSource<FilePickerResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
