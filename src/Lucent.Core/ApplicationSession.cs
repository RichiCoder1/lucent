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

    /// <summary>Returns whether the current close request may proceed, observing fatal cancellation cooperatively.</summary>
    ValueTask<bool> PrepareCloseAsync(CancellationToken cancellationToken);

    /// <summary>Stops application services after close has been accepted or startup has failed.</summary>
    ValueTask StopAsync();
}

/// <summary>Portable owner-thread session shared with a platform application host.</summary>
public sealed class ApplicationSession
{
    private const int DefaultMaximumCallbacks = 1_024;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly IApplicationLifecycle _lifecycle;
    private readonly SessionSynchronizationContext _context;
    private readonly Signal<ApplicationStatus> _status;
    private readonly List<Exception> _terminalErrors = [];
    private readonly ConcurrentQueue<Exception> _observerErrors = [];
    private readonly object _workGate = new();
    private readonly object _errorGate = new();
    private readonly Action<ApplicationFailureReport> _failureReporter;
    private readonly Action<Action> _failureDispatcher;
    private Action? _workAvailable;
    private int _notificationsInFlight;
    private bool _completionPending;
    private bool _finalizing;
    private int _started;
    private int _closeRequested;
    private bool _abortRequested;
    private bool _finishing;
    private bool _completed;
    private int _closePreparationGeneration;
    private CancellationTokenSource? _closePreparationCancellation;
    private Task<bool>? _closePreparationTask;

    internal ApplicationSession(
        string title,
        Composition composition,
        ThemeContext theme,
        IApplicationLifecycle lifecycle,
        Action<ApplicationFailureReport>? failureReporter = null,
        Action<Action>? failureDispatcher = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title;
        Composition = composition ?? throw new ArgumentNullException(nameof(composition));
        Theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _failureReporter = failureReporter ?? ReportToStandardError;
        _failureDispatcher = failureDispatcher ?? DispatchOnThreadPool;
        if (!Composition.Root.HasPresentation)
            Composition.Root.Present(Theme, author: PresentationStyles.Surface);
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
    public bool ProcessEvents() => ProcessEventsCore(DefaultMaximumCallbacks);

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

    private bool ProcessEventsCore(int maximumCallbacks)
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
        if (Status.Phase == ApplicationPhase.PreparingClose)
            CancelClosePreparation();
        if (
            Volatile.Read(ref _started) == 0
            || Status.Phase is ApplicationPhase.Running or ApplicationPhase.PreparingClose
        )
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
        CancellationTokenSource? cancellation = null;
        Task<bool>? preparation = null;
        var generation = 0;
        try
        {
            if (_finishing || _completed)
                return;
            if (!PublishStatus(ApplicationPhase.PreparingClose, null))
            {
                await FinishCoreAsync();
                return;
            }

            generation = checked(++_closePreparationGeneration);
            cancellation = new CancellationTokenSource();
            _closePreparationCancellation = cancellation;
            bool accepted;
            try
            {
                preparation = _lifecycle.PrepareCloseAsync(cancellation.Token).AsTask();
                _closePreparationTask = preparation;
                accepted = await preparation;
            }
            catch (Exception error)
            {
                if (generation != _closePreparationGeneration || _finishing || _completed)
                    return;
                ClearClosePreparation(generation, cancellation);
                AddTerminalError(error);
                await FinishCoreAsync();
                return;
            }

            if (generation != _closePreparationGeneration || _finishing || _completed)
                return;
            ClearClosePreparation(generation, cancellation);

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
            if (generation != 0)
                ClearClosePreparation(generation, cancellation);
            AddTerminalError(error);
            await FinishCoreAsync();
        }
    }

    private void CancelClosePreparation()
    {
        CheckOwner();
        checked
        {
            _closePreparationGeneration++;
        }
        var cancellation = _closePreparationCancellation;
        var preparation = _closePreparationTask;
        _closePreparationCancellation = null;
        _closePreparationTask = null;
        if (cancellation is null)
            return;

        var observer = new LatePreparationObserver(
            Title,
            _failureReporter,
            _failureDispatcher,
            cancellation
        );
        if (preparation is not null)
            observer.Observe(preparation);
        else
            observer.AbandonObservation();
        observer.Cancel();
    }

    private void ClearClosePreparation(int generation, CancellationTokenSource? cancellation)
    {
        if (generation != _closePreparationGeneration)
            return;
        _closePreparationCancellation = null;
        _closePreparationTask = null;
        cancellation?.Dispose();
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
        if (Failure is { } failure)
            DispatchFailure(
                Title,
                _failureReporter,
                _failureDispatcher,
                ApplicationFailureKind.Terminal,
                failure
            );
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

    private static void DispatchOnThreadPool(Action action) =>
        ThreadPool.QueueUserWorkItem(static state => ((Action)state!).Invoke(), action);

    internal static void ReportFailure(
        string title,
        Action<ApplicationFailureReport>? reporter,
        ApplicationFailureKind kind,
        Exception error
    ) =>
        DispatchFailure(
            title,
            reporter ?? ReportToStandardError,
            DispatchOnThreadPool,
            kind,
            error
        );

    private static void ReportToStandardError(ApplicationFailureReport report) =>
        Console.Error.WriteLine(
            "Lucent application failure (" + report.Kind + "): " + report.Error
        );

    private static void DispatchFailure(
        string title,
        Action<ApplicationFailureReport> reporter,
        Action<Action> dispatcher,
        ApplicationFailureKind kind,
        Exception error
    )
    {
        var report = new ApplicationFailureReport(title, kind, error);
        try
        {
            dispatcher(() =>
            {
                try
                {
                    reporter(report);
                }
                catch (Exception reporterError)
                {
                    TryWriteFallback(error, reporterError);
                }
            });
        }
        catch (Exception dispatchError)
        {
            TryWriteFallback(error, dispatchError);
        }
    }

    private static void TryWriteFallback(Exception originalError, Exception reportingError)
    {
        try
        {
            Console.Error.WriteLine(
                "Lucent application failure: "
                    + originalError
                    + Environment.NewLine
                    + "Lucent application failure reporter failed: "
                    + reportingError
            );
        }
        catch
        {
            // A fallback diagnostic must never replace the application failure.
        }
    }

    private sealed class LatePreparationObserver
    {
        private readonly string _title;
        private readonly Action<ApplicationFailureReport> _reporter;
        private readonly Action<Action> _dispatcher;
        private readonly CancellationTokenSource _cancellation;
        private int _operations = 2;

        internal LatePreparationObserver(
            string title,
            Action<ApplicationFailureReport> reporter,
            Action<Action> dispatcher,
            CancellationTokenSource cancellation
        )
        {
            _title = title;
            _reporter = reporter;
            _dispatcher = dispatcher;
            _cancellation = cancellation;
        }

        internal void Observe(Task<bool> preparation) =>
            _ = preparation.ContinueWith(
                static (completed, state) =>
                {
                    var observer = (LatePreparationObserver)state!;
                    try
                    {
                        if (completed.Exception is { } failure)
                            DispatchFailure(
                                observer._title,
                                observer._reporter,
                                observer._dispatcher,
                                ApplicationFailureKind.LateClosePreparation,
                                failure.InnerExceptions.Count == 1
                                    ? failure.InnerExceptions[0]
                                    : failure
                            );
                    }
                    finally
                    {
                        observer.Release();
                    }
                },
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );

        internal void Cancel()
        {
            Task cancellation;
            try
            {
                cancellation = _cancellation.CancelAsync();
            }
            catch (Exception error)
            {
                ReportCancellationFailure(error);
                Release();
                return;
            }

            if (cancellation.IsCompletedSuccessfully)
            {
                Release();
                return;
            }
            _ = cancellation.ContinueWith(
                static (completed, state) =>
                {
                    var observer = (LatePreparationObserver)state!;
                    try
                    {
                        if (completed.Exception is { } failure)
                            observer.ReportCancellationFailure(
                                failure.InnerExceptions.Count == 1
                                    ? failure.InnerExceptions[0]
                                    : failure
                            );
                    }
                    finally
                    {
                        observer.Release();
                    }
                },
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        }

        internal void AbandonObservation() => Release();

        private void ReportCancellationFailure(Exception error) =>
            DispatchFailure(
                _title,
                _reporter,
                _dispatcher,
                ApplicationFailureKind.LateClosePreparation,
                error
            );

        private void Release()
        {
            if (Interlocked.Decrement(ref _operations) == 0)
                _cancellation.Dispose();
        }
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

        internal bool Drain(int maximumCallbacks)
        {
            List<Exception>? errors = null;
            var processed = 0;

            while (processed < maximumCallbacks)
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
                    limitReached = true;
                }
            }
            if (notifyMore)
                workAvailable();

            if (limitReached)
                (errors ??= []).Add(
                    new InvalidOperationException(
                        "Application events did not settle within "
                            + maximumCallbacks
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
