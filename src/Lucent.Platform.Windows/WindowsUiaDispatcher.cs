using System.Collections.Concurrent;
using SDL3;

namespace Lucent.Platform.Windows;

/// <summary>Bounded SDL wakeup bridge for UIA calls. Provider callbacks never enter Core from a window procedure.</summary>
internal sealed class WindowsUiaDispatcher : IDisposable
{
    internal delegate bool PushEvent(ref SDL.Event @event);
    private const int Capacity = 128;

    // A UIA callback can enqueue another callback while its action is running. Keep one
    // owner-loop pass finite so a refillable producer cannot starve input and rendering.
    private const int ProcessBudget = 32;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly uint _eventType;
    private readonly ConcurrentQueue<Request> _pending = [];
    private readonly ConcurrentDictionary<long, Request> _requests = [];
    private readonly SemaphoreSlim _slots = new(Capacity, Capacity);
    private readonly object _lifetime = new();
    private readonly UiaDiagnostics _diagnostics = new();
    private readonly TimeSpan _timeout;
    private readonly PushEvent _push;
    private long _nextId;
    private int _disposed;

    internal WindowsUiaDispatcher(TimeSpan? timeout = null, PushEvent? push = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
        _push = push ?? SDL.PushEvent;
        if (_timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        _eventType = SDL.RegisterEvents(1);
        ValidateEventType(_eventType);
    }

    internal static void ValidateEventType(uint eventType)
    {
        if (eventType == 0)
            throw new InvalidOperationException(
                "SDL_RegisterEvents(UIA dispatcher) failed: " + SDL.GetError()
            );
    }

    internal bool IsWakeEvent(SDL.Event @event) => @event.Type == _eventType;

    /// <summary>Slots stay occupied until dequeued, including timed-out cancelled requests.</summary>
    internal int PendingCount => Capacity - _slots.CurrentCount;
    internal uint EventType => _eventType;

    internal void SetOwnerPhase(string phase) => _diagnostics.SetPhase(phase);

    internal void RecordFrame() => _diagnostics.RecordFrame();

    internal void RecordRead(string callback) => _diagnostics.RecordRead(callback);

    internal void RecordAction(string callback) => _diagnostics.RecordAction(callback);

    internal void RecordProvider(int cache, int maxCache, int stale) =>
        _diagnostics.RecordProvider(cache, maxCache, stale);

    internal void RecordRootDelivery() => _diagnostics.RecordRootDelivery();

    internal void RecordListenerFailure() => _diagnostics.RecordListenerFailure();

    internal bool TryInvoke<T>(string callback, Func<T> action, out T result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callback);
        ArgumentNullException.ThrowIfNull(action);
        if (Environment.CurrentManagedThreadId == _ownerThread)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                result = default!;
                return false;
            }
            try
            {
                result = action();
                return true;
            }
            catch
            {
                result = default!;
                return false;
            }
        }
        Request request;
        lock (_lifetime)
        {
            if (_disposed != 0 || !_slots.Wait(0))
            {
                result = default!;
                return false;
            }
            request = new Request(
                Interlocked.Increment(ref _nextId),
                callback,
                Environment.CurrentManagedThreadId,
                () => action(),
                () => _slots.Release()
            );
            if (!_requests.TryAdd(request.Id, request))
            {
                request.Release();
                result = default!;
                return false;
            }
            _diagnostics.Enqueue(request);
            _pending.Enqueue(request);
            var @event = new SDL.Event { Type = _eventType };
            if (!_push(ref @event))
            {
                _requests.TryRemove(request.Id, out _);
                request.CancelQueued();
                result = default!;
                return false;
            }
        }
        if (!request.Wait(_timeout))
        {
            if (request.CancelQueued())
            {
                _diagnostics.Timeout(request);
                result = default!;
                return false;
            }
            request.Wait(Timeout.InfiniteTimeSpan);
        }
        _requests.TryRemove(request.Id, out _);
        if (request.Error is not null)
        {
            result = default!;
            return false;
        }
        result = request.Result is T value ? value : default!;
        return request.Completed;
    }

    /// <summary>Runs queued work only at Bootstrap's normal event-loop safe point.</summary>
    internal int Process()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("UIA dispatcher must run on the SDL owner thread.");
        var count = 0;
        var examined = 0;
        while (examined < ProcessBudget && _pending.TryDequeue(out var request))
        {
            examined++;
            if (!request.TryBegin())
            {
                _requests.TryRemove(request.Id, out _);
                request.Release();
                continue;
            }
            _diagnostics.Start(request);
            try
            {
                request.Complete(request.Action());
                _diagnostics.End(request);
            }
            catch (Exception error)
            {
                request.Fail(error);
            }
            finally
            {
                request.Release();
                _diagnostics.Complete(request);
                count++;
            }
        }
        if (!_pending.IsEmpty)
        {
            // The event that caused this pass has already been consumed. Re-wake the owner
            // after yielding the bounded batch so a producer cannot leave accepted work parked.
            var wake = new SDL.Event { Type = _eventType };
            _ = _push(ref wake);
        }
        return count;
    }

    public void Dispose()
    {
        lock (_lifetime)
        {
            if (_disposed != 0)
                return;
            _disposed = 1;
            foreach (var request in _requests.Values)
                request.CancelQueued();
            while (_pending.TryDequeue(out var request))
            {
                _requests.TryRemove(request.Id, out _);
                request.Release();
            }
            _requests.Clear();
        }
        _diagnostics.Write(_ownerThread);
    }

    // Request releases its completion handle through the owning dispatcher; it intentionally
    // has no public IDisposable lifecycle.
    private sealed class Request(
        long id,
        string callback,
        int callerThread,
        Func<object?> action,
        Action release
    )
    {
        private readonly ManualResetEventSlim _complete = new(false);
        private int _state; // queued, executing, completed/cancelled
        private int _released;
        internal long Id { get; } = id;
        internal string Callback { get; } = callback;
        internal int CallerThread { get; } = callerThread;
        internal Func<object?> Action { get; } = action;
        internal object? Result { get; private set; }
        internal Exception? Error { get; private set; }
        internal bool Completed => Volatile.Read(ref _state) == 2;

        internal bool TryBegin() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

        internal bool Wait(TimeSpan timeout) => _complete.Wait(timeout);

        internal void Complete(object? result)
        {
            Result = result;
            Volatile.Write(ref _state, 2);
            _complete.Set();
        }

        internal void Fail(Exception error)
        {
            Error = error;
            Volatile.Write(ref _state, 2);
            _complete.Set();
        }

        internal bool CancelQueued()
        {
            if (Interlocked.CompareExchange(ref _state, 3, 0) != 0)
                return false;
            _complete.Set();
            return true;
        }

        internal void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                release();
        }
    }

    /// <summary>Opt-in, fixed-size postmortem state; it never writes from a UIA callback.</summary>
    private sealed class UiaDiagnostics
    {
        private const int Capacity = 32;
        private readonly bool _enabled =
            Environment.GetEnvironmentVariable("LUCENT_UIA_DIAGNOSTICS") == "1";
        private readonly string?[] _events = new string?[Capacity];
        private long _sequence,
            _frames,
            _reads,
            _actions,
            _providers,
            _cache,
            _maxCache,
            _stale,
            _timeouts,
            _roots,
            _listenerFailures;
        private string _phase = "startup";

        internal void SetPhase(string phase)
        {
            if (_enabled)
                Volatile.Write(ref _phase, phase);
        }

        internal void RecordFrame()
        {
            if (_enabled)
                Interlocked.Increment(ref _frames);
        }

        internal void RecordRead(string callback)
        {
            if (_enabled)
            {
                Interlocked.Increment(ref _reads);
                Event("read " + callback);
            }
        }

        internal void RecordAction(string callback)
        {
            if (_enabled)
            {
                Interlocked.Increment(ref _actions);
                Event("action " + callback);
            }
        }

        internal void RecordProvider(int cache, int maxCache, int stale)
        {
            if (_enabled)
            {
                Interlocked.Exchange(ref _providers, cache);
                Interlocked.Exchange(ref _cache, cache);
                Interlocked.Exchange(ref _maxCache, maxCache);
                Interlocked.Exchange(ref _stale, stale);
            }
        }

        internal void RecordRootDelivery()
        {
            if (_enabled)
                Interlocked.Increment(ref _roots);
        }

        internal void RecordListenerFailure()
        {
            if (_enabled)
                Interlocked.Increment(ref _listenerFailures);
        }

        internal void Enqueue(Request request) =>
            Event(
                $"enqueue id={request.Id} callback={request.Callback} caller={request.CallerThread} owner-phase={Volatile.Read(ref _phase)}"
            );

        internal void Start(Request request) =>
            Event(
                $"start id={request.Id} callback={request.Callback} caller={request.CallerThread}"
            );

        internal void End(Request request) =>
            Event($"end id={request.Id} callback={request.Callback}");

        internal void Complete(Request request) =>
            Event(
                $"complete id={request.Id} callback={request.Callback} completed={request.Completed}"
            );

        internal void Timeout(Request request)
        {
            if (_enabled)
                Interlocked.Increment(ref _timeouts);
            Event($"timeout id={request.Id} callback={request.Callback}");
        }

        internal void Write(int ownerThread)
        {
            if (!_enabled)
                return;
            var last = Volatile.Read(ref _sequence);
            var first = Math.Max(1, last - Capacity + 1);
            var events = new List<string>();
            for (var sequence = first; sequence <= last; sequence++)
                if (Volatile.Read(ref _events[(int)(sequence % Capacity)]) is { } entry)
                    events.Add(entry);
            Console.Error.WriteLine(
                $"Lucent UIA diagnostics: owner={ownerThread} phase={Volatile.Read(ref _phase)} frame={Volatile.Read(ref _frames)} read={Volatile.Read(ref _reads)} action={Volatile.Read(ref _actions)} provider={Volatile.Read(ref _providers)} cache={Volatile.Read(ref _cache)} maxCache={Volatile.Read(ref _maxCache)} stale={Volatile.Read(ref _stale)} root={Volatile.Read(ref _roots)} listenerFailure={Volatile.Read(ref _listenerFailures)} timeout={Volatile.Read(ref _timeouts)} events=[{string.Join(";", events)}]"
            );
        }

        private void Event(string message)
        {
            if (!_enabled)
                return;
            var sequence = Interlocked.Increment(ref _sequence);
            Volatile.Write(ref _events[(int)(sequence % Capacity)], $"{sequence}:{message}");
        }
    }
}
