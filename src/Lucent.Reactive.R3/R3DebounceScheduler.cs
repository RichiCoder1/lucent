using Lucent.Core;
using R3;

namespace Lucent.Reactive.R3;

/// <summary>Schedules the latest callback after a quiet period using R3's debounce operator.</summary>
/// <remarks>
/// The callback is always posted to the owning <see cref="ReactiveScope"/> and therefore runs
/// when its <see cref="ReactiveGraph"/> is drained. The scheduler owns itself in the supplied
/// scope; disposing the scope cancels pending work.
/// </remarks>
public sealed class OwnedDebouncedAction : IDisposable
{
    private readonly ReactiveScope _scope;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private Pending? _pending;
    private IDisposable? _posted;
    private Action? _callback;
    private long _generation;
    private bool _disposed;

    /// <summary>Creates an owner-thread callback scheduler backed by R3 and the supplied clock.</summary>
    public OwnedDebouncedAction(ReactiveScope scope, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ObjectDisposedException.ThrowIf(scope.IsDisposed, scope);
        _scope = scope;
        scope.Own(this);
        _timeProvider = timeProvider;
    }

    /// <summary>Replaces the pending callback and runs the latest one after <paramref name="delay"/> of silence.</summary>
    public void Restart(TimeSpan delay, Action callback)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(callback);

        long generation;
        Pending? previousPending;
        IDisposable? previousPosted;
        lock (_gate)
        {
            ThrowIfDisposed();
            generation = ++_generation;
            _callback = callback;
            previousPending = _pending;
            previousPosted = _posted;
            _pending = null;
            _posted = null;
        }
        previousPosted?.Dispose();
        previousPending?.Dispose();

        Pending pending;
        var source = new Subject<Unit>();
        try
        {
            var subscription = source
                .Debounce(delay, _timeProvider)
                .Subscribe(_ => OnDebounced(generation));
            pending = new Pending(source, subscription);
        }
        catch
        {
            source.Dispose();
            lock (_gate)
            {
                if (!_disposed && _generation == generation)
                    _callback = null;
            }
            throw;
        }

        var stale = false;
        lock (_gate)
        {
            if (_disposed || _generation != generation)
                stale = true;
            else
                _pending = pending;
        }
        if (stale)
            pending.Dispose();
        else
            pending.Signal();
    }

    /// <summary>Cancels the pending callback and releases any captured callback closure.</summary>
    public void Cancel()
    {
        Pending? pending;
        IDisposable? posted;
        lock (_gate)
        {
            if (_disposed)
                return;
            ++_generation;
            _callback = null;
            pending = _pending;
            posted = _posted;
            _pending = null;
            _posted = null;
        }
        posted?.Dispose();
        pending?.Dispose();
    }

    /// <summary>Cancels pending work and releases this scheduler's resources.</summary>
    public void Dispose()
    {
        Pending? pending;
        IDisposable? posted;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            ++_generation;
            _callback = null;
            pending = _pending;
            posted = _posted;
            _pending = null;
            _posted = null;
        }
        posted?.Dispose();
        pending?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnDebounced(long generation)
    {
        Action? callback;
        Pending? pending;
        lock (_gate)
        {
            if (_disposed || _generation != generation)
                return;
            callback = _callback;
            _callback = null;
            pending = _pending;
            _pending = null;
        }
        pending?.Dispose();
        if (callback is null)
            return;

        var posted = _scope.Post(() => InvokePosted(generation, callback));
        var stale = false;
        lock (_gate)
        {
            if (_disposed || _generation != generation)
                stale = true;
            else
                _posted = posted;
        }
        if (stale)
            posted.Dispose();
    }

    private void InvokePosted(long generation, Action callback)
    {
        lock (_gate)
        {
            if (_disposed || _generation != generation)
                return;
            _posted = null;
        }
        callback();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class Pending(Subject<Unit> source, IDisposable subscription) : IDisposable
    {
        private int _disposed;

        public void Signal()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;
            try
            {
                source.OnNext(Unit.Default);
            }
            catch (ObjectDisposedException)
            {
                // A concurrent restart or cancellation won the race to release this source.
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            subscription.Dispose();
            source.Dispose();
        }
    }
}
