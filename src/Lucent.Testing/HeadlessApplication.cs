using System.Collections.Concurrent;
using Lucent.Core;
using Microsoft.Extensions.Time.Testing;

namespace Lucent.Testing;

/// <summary>Runs one production Lucent application on an isolated, deterministic owner thread.</summary>
public sealed class HeadlessApplication : IAsyncDisposable
{
    private const int InstallAttempts = 3;
    private readonly HeadlessApplicationOptions _options;
    private readonly Func<HeadlessContext, ComponentRecipe>? _recipeFactory;
    private readonly IApplicationLifecycle? _lifecycle;
    private readonly ConcurrentQueue<IWorkItem> _work = new();
    private readonly AutoResetEvent _available = new(false);
    private readonly object _admissionGate = new();
    private readonly TaskCompletionSource _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly TaskCompletionSource _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly Thread _thread;
    private HeadlessContext? _context;
    private ITextShaper? _shaper;
    private int _stopping;
    private int _availableDisposed;

    private HeadlessApplication(
        HeadlessApplicationOptions options,
        Func<HeadlessContext, ComponentRecipe>? recipeFactory,
        IApplicationLifecycle? lifecycle
    )
    {
        _options = options;
        _recipeFactory = recipeFactory;
        _lifecycle = lifecycle;
        _thread = new Thread(Run) { IsBackground = true, Name = "Lucent headless application" };
    }

    /// <summary>Starts a fixed recipe on a dedicated application owner thread.</summary>
    public static Task<HeadlessApplication> StartAsync(
        ComponentRecipe recipe,
        HeadlessApplicationOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return StartAsync(_ => recipe, options);
    }

    /// <summary>Creates and starts a recipe on the dedicated application owner thread.</summary>
    public static async Task<HeadlessApplication> StartAsync(
        Func<HeadlessContext, ComponentRecipe> recipeFactory,
        HeadlessApplicationOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(recipeFactory);
        var application = new HeadlessApplication(
            (options ?? new HeadlessApplicationOptions()).Snapshot(),
            recipeFactory,
            null
        );
        application._thread.Start();
        await application._ready.Task.ConfigureAwait(false);
        return application;
    }

    /// <summary>Starts an asynchronous production lifecycle on a dedicated owner thread.</summary>
    public static async Task<HeadlessApplication> StartAsync(
        IApplicationLifecycle lifecycle,
        HeadlessApplicationOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        var application = new HeadlessApplication(
            (options ?? new HeadlessApplicationOptions()).Snapshot(),
            null,
            lifecycle
        );
        application._thread.Start();
        await application._ready.Task.ConfigureAwait(false);
        return application;
    }

    /// <summary>Runs owner-thread code and then settles and reprojects the production composition.</summary>
    public Task<T> InvokeAsync<T>(Func<HeadlessContext, T> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return Enqueue(callback, settle: true);
    }

    /// <summary>Settles and reprojects first, then runs owner-thread inspection against that exact state.</summary>
    public Task<T> InvokeAfterSettleAsync<T>(Func<HeadlessContext, T> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return EnqueueAfterSettle(static _ => { }, callback);
    }

    /// <summary>Settles currently queued lifecycle and reactive work and returns a fresh snapshot.</summary>
    public Task<HeadlessSnapshot> DrainAsync() =>
        EnqueueAfterSettle(static _ => { }, static context => Snapshot(context));

    /// <summary>Changes the logical viewport, settles responsive state, and returns a fresh snapshot.</summary>
    public Task<HeadlessSnapshot> ResizeAsync(LayoutViewport viewport)
    {
        viewport.Validate();
        return EnqueueAfterSettle(
            context => context.Viewport = viewport,
            static context => Snapshot(context)
        );
    }

    /// <summary>Advances the deterministic clock on the owner thread and settles resulting work.</summary>
    public Task<HeadlessSnapshot> AdvanceAsync(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        return EnqueueAfterSettle(
            context =>
            {
                context.TimeProvider.Advance(elapsed);
                var milliseconds = elapsed.TotalMilliseconds;
                if (milliseconds > 0)
                    context.Composition.AdvanceTransitions(
                        milliseconds >= int.MaxValue ? int.MaxValue : (int)milliseconds
                    );
            },
            static context => Snapshot(context)
        );
    }

    /// <summary>Dispatches a pointer command through the production input router.</summary>
    public Task<InputDispatchResult> PointerAsync(PointerCommand command) =>
        Enqueue(context => context.Input.DispatchPointer(command), settle: true);

    /// <summary>Dispatches a wheel command through the production input router.</summary>
    public Task<InputDispatchResult> WheelAsync(WheelCommand command) =>
        Enqueue(context => context.Input.DispatchWheel(command), settle: true);

    /// <summary>Dispatches a key command through the production input router.</summary>
    public Task<InputDispatchResult> KeyAsync(KeyCommand command) =>
        Enqueue(context => context.Input.DispatchKey(command), settle: true);

    /// <summary>Dispatches text through the production input router.</summary>
    public Task<InputDispatchResult> TextAsync(TextInputCommand command) =>
        Enqueue(context => context.Input.DispatchText(command), settle: true);

    /// <summary>Returns the current immutable scene and semantic state.</summary>
    public Task<HeadlessSnapshot> SnapshotAsync() =>
        InvokeAfterSettleAsync(static context => Snapshot(context));

    /// <summary>Requests normal close negotiation without disposing the harness.</summary>
    public Task<ApplicationStatus> RequestCloseAsync() =>
        EnqueueAfterSettle(
            static context => context.Session.RequestClose(),
            static context => context.Session.Status
        );

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        lock (_admissionGate)
        {
            if (Interlocked.Exchange(ref _stopping, 1) == 0)
                _available.Set();
        }
        try
        {
            await _completion.Task.ConfigureAwait(false);
        }
        finally
        {
            if (Interlocked.Exchange(ref _availableDisposed, 1) == 0)
                _available.Dispose();
        }
    }

    private Task<T> Enqueue<T>(Func<HeadlessContext, T> callback, bool settle)
    {
        var item = new WorkItem<T>(callback, settle);
        lock (_admissionGate)
        {
            if (Volatile.Read(ref _stopping) != 0)
                return Task.FromException<T>(
                    new ObjectDisposedException(nameof(HeadlessApplication))
                );
            _work.Enqueue(item);
            _available.Set();
        }
        return item.Task;
    }

    private Task<T> EnqueueAfterSettle<T>(
        Action<HeadlessContext> callback,
        Func<HeadlessContext, T> result
    )
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(result);
        var item = new WorkItem<T>(
            context =>
            {
                callback(context);
                return default!;
            },
            true,
            result
        );
        lock (_admissionGate)
        {
            if (Volatile.Read(ref _stopping) != 0)
                return Task.FromException<T>(
                    new ObjectDisposedException(nameof(HeadlessApplication))
                );
            _work.Enqueue(item);
            _available.Set();
        }
        return item.Task;
    }

    private void Run()
    {
        Exception? failure = null;
        try
        {
            _shaper =
                _options.TextShaperFactory()
                ?? throw new InvalidOperationException("The text-shaper factory returned null.");
            var clock =
                _options.TimeProviderFactory()
                ?? throw new InvalidOperationException("The time-provider factory returned null.");
            var lifecycle = _lifecycle ?? new FactoryLifecycle(this);
            var host = new HeadlessHost(this, clock);
            _ = LucentApplication
                .CreateBuilder()
                .SetTitle(_options.Title)
                .SetTheme(appearance => RequireTheme(_options.ThemeFactory(appearance)))
                .UseHost(host)
                .Build()
                .Run(lifecycle);
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            try
            {
                if (_shaper is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception cleanupError)
            {
                failure = failure is null
                    ? cleanupError
                    : new AggregateException(
                        "The headless application and text shaper both failed.",
                        failure,
                        cleanupError
                    );
            }
        }
        if (failure is null)
        {
            _completion.TrySetResult();
            return;
        }
        _ready.TrySetException(failure);
        while (_work.TryDequeue(out var item))
            item.Fail(failure);
        _completion.TrySetException(failure);
    }

    private void HostLoop(ApplicationSession session, FakeTimeProvider clock)
    {
        _context = new HeadlessContext(session, clock, _options.Viewport);
        void Wake() => _available.Set();
        session.WorkAvailable += Wake;
        try
        {
            session.Theme.Appearance = _options.Appearance;
            session.Start();
            PumpToRunning(session);
            Project();
            _ready.TrySetResult();

            while (Volatile.Read(ref _stopping) == 0)
            {
                while (Volatile.Read(ref _stopping) == 0 && _work.TryDequeue(out var item))
                {
                    using var contextLease = session.EnterContext();
                    item.Run(_context, item.Settle ? SettleAndProject : null);
                }
                if (Volatile.Read(ref _stopping) == 0)
                    _available.WaitOne();
            }
            var disposed = new ObjectDisposedException(nameof(HeadlessApplication));
            while (_work.TryDequeue(out var item))
                item.Fail(disposed);
            Shutdown(session);
        }
        finally
        {
            session.WorkAvailable -= Wake;
        }
    }

    private void PumpToRunning(ApplicationSession session)
    {
        var activePasses = 0;
        while (session.Status.Phase == ApplicationPhase.Starting)
        {
            var processed = session.ProcessEvents(_options.MaximumWorkItems);
            if (!session.Composition.IsDisposed)
                session.Composition.Flush(_options.MaximumWorkItems);
            if (session.Status.Phase != ApplicationPhase.Starting)
                break;
            if (!processed)
            {
                activePasses = 0;
                _available.WaitOne();
            }
            else if (++activePasses >= _options.MaximumWorkItems)
            {
                throw LimitExceeded("starting the application");
            }
        }
        if (session.Status.Phase != ApplicationPhase.Running)
            throw new InvalidOperationException(
                "The headless application did not reach the running phase. Status: "
                    + session.Status.Phase
                    + ".",
                session.Status.Error
            );
    }

    private void Shutdown(ApplicationSession session)
    {
        session.RequestClose();
        var activePasses = 0;
        while (!session.IsCompleted)
        {
            var processed = session.ProcessEvents(_options.MaximumWorkItems);
            var reactive = false;
            if (!session.Composition.IsDisposed)
                reactive = session.Composition.Flush(_options.MaximumWorkItems);
            if (session.IsCompleted || session.Status.Phase == ApplicationPhase.Running)
                return;
            if (!processed && !reactive)
            {
                activePasses = 0;
                _available.WaitOne();
            }
            else if (++activePasses >= _options.MaximumWorkItems)
            {
                throw LimitExceeded("shutting down the application");
            }
        }
    }

    private void SettleAndProject()
    {
        _ = SettleQueues();
        for (var projection = 0; projection < _options.MaximumWorkItems; projection++)
        {
            Project();
            if (!SettleQueues())
                return;
        }
        throw LimitExceeded("settling cross-queue application work");
    }

    private bool SettleQueues()
    {
        var context = _context!;
        var hadWork = false;
        var idlePasses = 0;
        var passLimit = Math.Max(2, _options.MaximumWorkItems);
        for (var pass = 0; pass < passLimit; pass++)
        {
            var events = context.Session.ProcessEvents(_options.MaximumWorkItems);
            var reactive = context.Composition.Flush(_options.MaximumWorkItems);
            if (events || reactive)
            {
                hadWork = true;
                idlePasses = 0;
                continue;
            }
            if (++idlePasses == 2)
                return hadWork;
        }
        throw LimitExceeded("settling application and reactive queues");
    }

    private void Project()
    {
        var context = _context!;
        for (var attempt = 0; attempt < InstallAttempts; attempt++)
        {
            context.Composition.Flush(_options.MaximumWorkItems);
            var scene = SceneLayout.Project(
                context.Composition,
                context.Viewport,
                _shaper!,
                _options.MaximumWorkItems
            );
            if (context.Input.SetScene(scene))
            {
                context.SetScene(scene);
                return;
            }
        }
        throw new InvalidOperationException(
            $"The production input router rejected {InstallAttempts} consecutive projected scenes."
        );
    }

    private InvalidOperationException LimitExceeded(string operation)
    {
        var context = _context!;
        string sceneDump;
        try
        {
            sceneDump = context.Scene.Dump();
        }
        catch (InvalidOperationException)
        {
            sceneDump = "scene: <not projected>\n";
        }
        return new(
            "Headless work did not settle within "
                + _options.MaximumWorkItems
                + " passes while "
                + operation
                + ".\n"
                + context.Composition.SemanticDump()
                + sceneDump
        );
    }

    private static HeadlessSnapshot Snapshot(HeadlessContext context) =>
        new(context.Scene, context.Composition.SemanticSnapshot());

    private static Theme RequireTheme(Theme? theme) =>
        theme ?? throw new InvalidOperationException("The headless theme factory returned null.");

    private interface IWorkItem
    {
        bool Settle { get; }
        void Run(HeadlessContext context, Action? settle);
        void Fail(Exception error);
    }

    private sealed class WorkItem<T>(
        Func<HeadlessContext, T> callback,
        bool settle,
        Func<HeadlessContext, T>? afterSettle = null
    ) : IWorkItem
    {
        private readonly TaskCompletionSource<T> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public bool Settle { get; } = settle;
        public Task<T> Task => _completion.Task;

        public void Run(HeadlessContext context, Action? settleAction)
        {
            try
            {
                var result = callback(context);
                settleAction?.Invoke();
                if (afterSettle is not null)
                    result = afterSettle(context);
                _completion.TrySetResult(result);
            }
            catch (Exception error)
            {
                _completion.TrySetException(error);
            }
        }

        public void Fail(Exception error) => _completion.TrySetException(error);
    }

    private sealed class HeadlessHost(HeadlessApplication owner, FakeTimeProvider clock)
        : IApplicationHost
    {
        public int Run(ApplicationSession session)
        {
            owner.HostLoop(session, clock);
            return 0;
        }
    }

    private sealed class FactoryLifecycle(HeadlessApplication owner) : IApplicationLifecycle
    {
        public ValueTask<ComponentRecipe> StartAsync(ApplicationSession session)
        {
            var recipe = owner._recipeFactory!(owner._context!);
            return ValueTask.FromResult(
                recipe
                    ?? throw new InvalidOperationException(
                        "The headless recipe factory returned null."
                    )
            );
        }

        public ValueTask<bool> PrepareCloseAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask StopAsync() => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
