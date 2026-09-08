using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Lucent.Core;

/// <summary>Portable phases of one application lifecycle.</summary>
public enum ApplicationPhase
{
    /// <summary>Lifecycle startup is in progress.</summary>
    Starting,

    /// <summary>The application is running and may accept close requests.</summary>
    Running,

    /// <summary>The lifecycle is deciding whether a close request may proceed.</summary>
    PreparingClose,

    /// <summary>Accepted or failed work is stopping and releasing resources.</summary>
    Stopping,

    /// <summary>All terminal cleanup stages have completed.</summary>
    Completed,
}

/// <summary>The current observable application lifecycle state.</summary>
public readonly record struct ApplicationStatus(ApplicationPhase Phase, Exception? Error);

/// <summary>Owns asynchronous startup, close negotiation, shutdown, and final cleanup.</summary>
public interface IApplicationLifecycle : IAsyncDisposable
{
    /// <summary>Starts application services and returns the root recipe to mount.</summary>
    ValueTask<ComponentRecipe> StartAsync(ApplicationSession session);

    /// <summary>Returns whether the current close request may proceed.</summary>
    ValueTask<bool> PrepareCloseAsync();

    /// <summary>Stops application services after close has been accepted or startup has failed.</summary>
    ValueTask StopAsync();
}

/// <summary>Portable owner-thread session shared with a platform application host.</summary>
public sealed class ApplicationSession
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly IApplicationLifecycle _lifecycle;
    private readonly SessionSynchronizationContext _context;
    private readonly Signal<ApplicationStatus> _status;
    private readonly List<Exception> _terminalErrors = [];
    private readonly ConcurrentQueue<Exception> _observerErrors = [];
    private readonly object _workGate = new();
    private readonly object _errorGate = new();
    private Action? _workAvailable;
    private int _notificationsInFlight;
    private bool _completionPending;
    private bool _finalizing;
    private int _started;
    private int _closeRequested;
    private bool _abortRequested;
    private bool _finishing;
    private bool _completed;

    internal ApplicationSession(
        string title,
        Composition composition,
        ThemeContext theme,
        IApplicationLifecycle lifecycle
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title;
        Composition = composition ?? throw new ArgumentNullException(nameof(composition));
        Theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _context = new SessionSynchronizationContext(_ownerThread, NotifyWorkAvailable);
        _status = composition.Graph.Signal(
            new ApplicationStatus(ApplicationPhase.Starting, null),
            "application-status"
        );
        Composition.WorkAvailable += NotifyWorkAvailable;
    }

    /// <summary>The snapshotted title for the platform's top-level window.</summary>
    public string Title { get; }

    /// <summary>The retained composition owned by this session.</summary>
    public Composition Composition { get; }

    /// <summary>The appearance and token context owned by this session.</summary>
    public ThemeContext Theme { get; }

    /// <summary>The root lifetime scope for application UI resources.</summary>
    public ReactiveScope Scope => Composition.Root.Scope;

    /// <summary>The reactively tracked lifecycle phase and latest actionable or terminal error.</summary>
    public ApplicationStatus Status
    {
        get
        {
            CheckOwner();
            return _status.Value;
        }
    }

    /// <summary>Whether terminal stop and both cleanup stages have completed.</summary>
    public bool IsCompleted => Volatile.Read(ref _completed);

    /// <summary>Raised when synchronization-context or reactive work becomes available.</summary>
    public event Action? WorkAvailable
    {
        add
        {
            lock (_workGate)
                _workAvailable += value;
        }
        remove
        {
            lock (_workGate)
                _workAvailable -= value;
        }
    }

    /// <summary>Installs this session's owner context until the returned scope is disposed.</summary>
    public IDisposable EnterContext()
    {
        CheckOwner();
        var prior = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(_context);
        return new ContextLease(this, prior);
    }

    /// <summary>Begins lifecycle startup on the session owner thread.</summary>
    public void Start()
    {
        CheckOwner();
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("An application session can start only once.");
        RunUnderContext(() => _ = StartCoreAsync());
    }

    /// <summary>Runs queued asynchronous lifecycle continuations on the owner thread.</summary>
    public bool ProcessEvents() => ProcessEventsCore(null);

    /// <summary>Runs at most <paramref name="maximumCallbacks"/> queued lifecycle continuations.</summary>
    /// <exception cref="InvalidOperationException">Callbacks remain queued after the limit is reached.</exception>
    public bool ProcessEvents(int maximumCallbacks)
    {
        if (maximumCallbacks <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maximumCallbacks),
                "The application event limit must be positive."
            );
        return ProcessEventsCore(maximumCallbacks);
    }

    private bool ProcessEventsCore(int? maximumCallbacks)
    {
        CheckOwner();
        Exception? failure = null;
        var processed = false;
        try
        {
            processed = RunUnderContext(() => _context.Drain(maximumCallbacks));
        }
        catch (Exception error)
        {
            failure = error;
        }

        var observerFailure = DrainObserverErrors();
        if (failure is null && observerFailure is null)
            return processed;
        if (failure is null)
            ExceptionDispatchInfo.Capture(observerFailure!).Throw();
        if (observerFailure is null)
            ExceptionDispatchInfo.Capture(failure).Throw();
        throw new AggregateException(
            "Application event processing failed.",
            failure,
            observerFailure
        );
    }

    /// <summary>Coalesces a close request and schedules negotiation on the owner thread.</summary>
    public void RequestClose()
    {
        if (IsCompleted || Interlocked.Exchange(ref _closeRequested, 1) != 0)
            return;
        _context.Post(static state => ((ApplicationSession)state!).HandleCloseRequest(), this);
    }

    internal Exception? Failure
    {
        get
        {
            lock (_errorGate)
                return _terminalErrors.Count switch
                {
                    0 => null,
                    1 => _terminalErrors[0],
                    _ => new AggregateException(
                        "Application lifecycle and cleanup failed.",
                        _terminalErrors.ToArray()
                    ),
                };
        }
    }

    internal void Abort(Exception? error)
    {
        CheckOwner();
        if (error is not null)
            AddTerminalError(error);
        _abortRequested = true;
        if (_completed)
        {
            PublishStatus(ApplicationPhase.Completed, Failure);
            return;
        }
        if (Volatile.Read(ref _started) == 0 || Status.Phase == ApplicationPhase.Running)
            RunUnderContext(() => _ = FinishCoreAsync());
    }

    private async Task StartCoreAsync()
    {
        try
        {
            var recipe = await _lifecycle.StartAsync(this);
            if (_abortRequested)
            {
                await FinishCoreAsync();
                return;
            }
            ArgumentNullException.ThrowIfNull(recipe);
            _ = Composition.Mount(Composition.Root, Theme, recipe);
            if (!PublishStatus(ApplicationPhase.Running, null))
            {
                await FinishCoreAsync();
                return;
            }
            if (_abortRequested)
                await FinishCoreAsync();
            else if (Volatile.Read(ref _closeRequested) != 0)
                await PrepareCloseCoreAsync();
        }
        catch (Exception error)
        {
            AddTerminalError(error);
            await FinishCoreAsync();
        }
    }

    private void HandleCloseRequest()
    {
        CheckOwner();
        if (_completed || _finishing)
            return;
        if (Volatile.Read(ref _started) == 0 || Status.Phase == ApplicationPhase.Starting)
            return;
        if (Status.Phase == ApplicationPhase.Running)
            _ = PrepareCloseCoreAsync();
    }

    private async Task PrepareCloseCoreAsync()
    {
        try
        {
            if (_finishing || _completed)
                return;
            if (!PublishStatus(ApplicationPhase.PreparingClose, null))
            {
                await FinishCoreAsync();
                return;
            }

            bool accepted;
            try
            {
                accepted = await _lifecycle.PrepareCloseAsync();
            }
            catch (Exception error)
            {
                if (_abortRequested)
                {
                    AddTerminalError(error);
                    await FinishCoreAsync();
                }
                else
                {
                    Interlocked.Exchange(ref _closeRequested, 0);
                    if (!PublishStatus(ApplicationPhase.Running, error))
                        await FinishCoreAsync();
                }
                return;
            }

            if (_abortRequested || accepted)
            {
                await FinishCoreAsync();
                return;
            }

            Interlocked.Exchange(ref _closeRequested, 0);
            if (!PublishStatus(ApplicationPhase.Running, null))
                await FinishCoreAsync();
        }
        catch (Exception error)
        {
            AddTerminalError(error);
            await FinishCoreAsync();
        }
    }

    private async Task FinishCoreAsync()
    {
        if (_finishing || _completed)
            return;
        _finishing = true;
        PublishStatus(ApplicationPhase.Stopping, Failure);

        try
        {
            await _lifecycle.StopAsync();
        }
        catch (Exception error)
        {
            AddTerminalError(error);
        }

        try
        {
            Composition.Dispose();
        }
        catch (Exception error)
        {
            AddTerminalError(error);
        }

        try
        {
            await _lifecycle.DisposeAsync();
        }
        catch (Exception error)
        {
            AddTerminalError(error);
        }

        Composition.WorkAvailable -= NotifyWorkAvailable;
        var finalize = false;
        lock (_workGate)
        {
            _completionPending = true;
            finalize = _notificationsInFlight == 0;
        }
        if (finalize)
            FinalizeCompletion();
    }

    private bool PublishStatus(ApplicationPhase phase, Exception? error)
    {
        try
        {
            _status.Value = new ApplicationStatus(phase, error);
            return true;
        }
        catch (Exception failure)
        {
            AddTerminalError(failure);
            return false;
        }
    }

    private void AddTerminalError(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        lock (_errorGate)
            _terminalErrors.Add(error);
    }

    private void RunUnderContext(Action action)
    {
        using var context = EnterContext();
        action();
    }

    private T RunUnderContext<T>(Func<T> action)
    {
        using var context = EnterContext();
        return action();
    }

    private void NotifyWorkAvailable()
    {
        Action? available;
        lock (_workGate)
        {
            if (_completed || _finalizing)
                return;
            available = _workAvailable;
            if (available is null)
                return;
            _notificationsInFlight++;
        }

        Action? succeeded = null;
        try
        {
            foreach (Action handler in available.GetInvocationList())
            {
                try
                {
                    handler();
                    succeeded += handler;
                }
                catch (Exception error)
                {
                    _observerErrors.Enqueue(error);
                }
            }
        }
        finally
        {
            var finalize = false;
            Action? rearm = null;
            lock (_workGate)
            {
                _notificationsInFlight--;
                finalize = _completionPending && _notificationsInFlight == 0;
                if (succeeded is not null && (!_observerErrors.IsEmpty || finalize))
                {
                    var subscribed = _workAvailable?.GetInvocationList() ?? [];
                    foreach (Action handler in succeeded.GetInvocationList())
                        if (subscribed.Contains(handler))
                            rearm += handler;
                }
            }

            if (finalize)
                _context.Post(
                    static state => ((ApplicationSession)state!).FinalizeCompletion(),
                    this,
                    notify: false
                );
            NotifySuccessfulObservers(rearm);
        }
    }

    private void NotifySuccessfulObservers(Action? observers)
    {
        if (observers is null)
            return;
        foreach (Action observer in observers.GetInvocationList())
        {
            try
            {
                observer();
            }
            catch (Exception error)
            {
                var terminal = false;
                lock (_workGate)
                    terminal = _finalizing || _completed;
                if (terminal)
                    AddTerminalError(error);
                else
                    _observerErrors.Enqueue(error);
            }
        }
    }

    private void FinalizeCompletion()
    {
        CheckOwner();
        lock (_workGate)
        {
            if (_completed || _finalizing || _notificationsInFlight != 0)
                return;
            _finalizing = true;
        }

        while (DrainObserverErrors() is { } observerFailure)
            AddTerminalError(observerFailure);
        PublishStatus(ApplicationPhase.Completed, Failure);
        Volatile.Write(ref _completed, true);
        _context.Complete();
    }

    private Exception? DrainObserverErrors()
    {
        List<Exception>? errors = null;
        while (_observerErrors.TryDequeue(out var error))
            (errors ??= []).Add(error);
        return errors?.Count switch
        {
            null or 0 => null,
            1 => errors[0],
            _ => new AggregateException("Application work observer failed.", errors),
        };
    }

    private void CheckOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException(
                "Application session work belongs to its creating thread."
            );
    }

    private sealed class ContextLease(ApplicationSession owner, SynchronizationContext? prior)
        : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            owner.CheckOwner();
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            SynchronizationContext.SetSynchronizationContext(prior);
        }
    }

    private sealed class SessionSynchronizationContext(int ownerThread, Action workAvailable)
        : SynchronizationContext
    {
        private readonly object _gate = new();
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = [];
        private bool _accepting = true;
        private bool _signaled;

        public override void Post(SendOrPostCallback d, object? state) => Post(d, state, true);

        internal void Post(SendOrPostCallback callback, object? state, bool notify = true)
        {
            ArgumentNullException.ThrowIfNull(callback);
            var signal = false;
            lock (_gate)
            {
                if (!_accepting)
                    return;
                _queue.Enqueue((callback, state));
                if (!_signaled)
                {
                    _signaled = true;
                    signal = notify;
                }
            }
            if (signal)
                workAvailable();
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);
            if (Environment.CurrentManagedThreadId != ownerThread)
                throw new NotSupportedException(
                    "Synchronous dispatch from outside the application owner thread is not supported."
                );
            d(state);
        }

        internal bool Drain(int? maximumCallbacks)
        {
            List<Exception>? errors = null;
            int count;
            lock (_gate)
                count = _queue.Count;

            var limit = maximumCallbacks ?? count;
            var processed = 0;

            for (var index = 0; index < limit; index++)
            {
                (SendOrPostCallback Callback, object? State) work;
                lock (_gate)
                {
                    if (_queue.Count == 0)
                        break;
                    work = _queue.Dequeue();
                }
                processed++;
                try
                {
                    work.Callback(work.State);
                }
                catch (Exception error)
                {
                    (errors ??= []).Add(error);
                }
            }

            var notifyMore = false;
            var limitReached = false;
            lock (_gate)
            {
                if (_queue.Count == 0)
                    _signaled = false;
                else
                {
                    notifyMore = _accepting;
                    limitReached = maximumCallbacks is not null;
                }
            }
            if (notifyMore)
                workAvailable();

            if (limitReached)
                (errors ??= []).Add(
                    new InvalidOperationException(
                        "Application events did not settle within "
                            + maximumCallbacks!.Value
                            + " callbacks. A continuation may be posting itself repeatedly."
                    )
                );
            if (errors is null)
                return processed != 0;
            if (errors.Count == 1)
                ExceptionDispatchInfo.Capture(errors[0]).Throw();
            throw new AggregateException("Application event processing failed.", errors);
        }

        internal void Complete()
        {
            lock (_gate)
            {
                _accepting = false;
                _queue.Clear();
                _signaled = false;
            }
        }
    }
}
