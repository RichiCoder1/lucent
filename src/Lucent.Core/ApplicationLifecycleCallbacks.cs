using System.Runtime.ExceptionServices;

namespace Lucent.Core;

/// <summary>Reports what an application's startup reached before terminal cleanup began.</summary>
public sealed class ApplicationStartupOutcome
{
    internal ApplicationStartupOutcome(
        bool servicesReady,
        bool rootFactoryInvoked,
        bool rootCreated,
        bool rootMounted,
        Exception? error
    )
    {
        ServicesReady = servicesReady;
        RootFactoryInvoked = rootFactoryInvoked;
        RootCreated = rootCreated;
        RootMounted = rootMounted;
        Error = error;
    }

    /// <summary>Whether all registered startup callbacks completed successfully.</summary>
    public bool ServicesReady { get; }

    /// <summary>Whether the deferred root factory was invoked.</summary>
    public bool RootFactoryInvoked { get; }

    /// <summary>Whether the root factory returned a non-null recipe.</summary>
    public bool RootCreated { get; }

    /// <summary>Whether the root recipe mounted successfully.</summary>
    public bool RootMounted { get; }

    /// <summary>The startup or mounted-callback failure, if one occurred.</summary>
    public Exception? Error { get; }
}

/// <summary>Provides application capabilities and resource registration during one startup callback.</summary>
public sealed class ApplicationStartContext
{
    private readonly ApplicationLifecycleComposer _composer;
    private readonly ApplicationSession _session;
    private int _active = 1;

    internal ApplicationStartContext(
        ApplicationSession session,
        ApplicationLifecycleComposer composer
    )
    {
        _session = session;
        _composer = composer;
    }

    /// <summary>Gets the owner-thread application session that is starting.</summary>
    public ApplicationSession Session => _session;

    /// <summary>Provides a borrowed typed value to the application root and its descendants.</summary>
    /// <remarks>The value is added after the root factory returns and remains borrowed for the session lifetime.</remarks>
    public ApplicationStartContext ProvideRootContext<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        CheckActive();
        _composer.AddRootContext(value);
        return this;
    }

    /// <summary>Creates the session's single service binding, which the framework attaches after root creation.</summary>
    public ComponentServiceBinding CreateServiceBinding(IComponentServiceSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        CheckActive();
        return _composer.CreateServiceBinding(source);
    }

    /// <summary>Registers cleanup for the stop phase when an acquired resource should stop before UI disposal.</summary>
    public ApplicationStartContext OnStop(Func<ApplicationCleanupContext, ValueTask> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        CheckActive();
        _composer.AddStopCallback(cleanup);
        return this;
    }

    /// <summary>Registers asynchronous cleanup for the stop phase.</summary>
    public ApplicationStartContext OnStop(Func<ValueTask> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        return OnStop(_ => cleanup());
    }

    /// <summary>Registers cleanup for the final phase after the application composition is disposed.</summary>
    public ApplicationStartContext OnDispose(Func<ApplicationCleanupContext, ValueTask> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        CheckActive();
        _composer.AddDisposeCallback(cleanup);
        return this;
    }

    /// <summary>Registers asynchronous cleanup for the final phase after the application composition is disposed.</summary>
    public ApplicationStartContext OnDispose(Func<ValueTask> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        return OnDispose(_ => cleanup());
    }

    internal void Complete() => Interlocked.Exchange(ref _active, 0);

    private void CheckActive()
    {
        _session.CheckOwnerForServices();
        if (Volatile.Read(ref _active) == 0)
            throw new InvalidOperationException(
                "Startup resource registrations are available only while their callback is running."
            );
    }
}

/// <summary>Collects recovery actions for one close-preparation attempt.</summary>
public sealed class ApplicationCloseContext
{
    private readonly ApplicationSession _session;
    private readonly List<Func<ValueTask>> _restorations;
    private int _active = 1;

    internal ApplicationCloseContext(ApplicationSession session, List<Func<ValueTask>> restorations)
    {
        _session = session;
        _restorations = restorations;
    }

    /// <summary>Gets the owner-thread session whose close is being prepared.</summary>
    public ApplicationSession Session => _session;

    /// <summary>Registers restoration to run in reverse order if any participant declines this attempt.</summary>
    public void OnDeclined(Func<ValueTask> restore)
    {
        ArgumentNullException.ThrowIfNull(restore);
        _session.CheckOwnerForServices();
        if (Volatile.Read(ref _active) == 0)
            throw new InvalidOperationException(
                "Close-decline restoration can be registered only during its preparation callback."
            );
        _restorations.Add(restore);
    }

    /// <summary>Registers synchronous restoration to run if any participant declines this attempt.</summary>
    public void OnDeclined(Action restore)
    {
        ArgumentNullException.ThrowIfNull(restore);
        OnDeclined(() =>
        {
            restore();
            return ValueTask.CompletedTask;
        });
    }

    internal void Complete() => Interlocked.Exchange(ref _active, 0);
}

/// <summary>Provides the session and startup result to unconditional terminal callbacks.</summary>
public sealed class ApplicationCleanupContext
{
    internal ApplicationCleanupContext(
        ApplicationSession? session,
        ApplicationStartupOutcome startup
    )
    {
        Session = session;
        Startup = startup;
    }

    /// <summary>Gets the session being cleaned up, or <see langword="null"/> if session construction failed.</summary>
    public ApplicationSession? Session { get; }

    /// <summary>Gets the startup state captured when terminal cleanup began.</summary>
    public ApplicationStartupOutcome Startup { get; }
}

internal sealed class ApplicationLifecycleCallbacks
{
    internal ApplicationLifecycleCallbacks(
        Func<ApplicationStartContext, ValueTask>[] start,
        Func<ApplicationCloseContext, CancellationToken, ValueTask<bool>>[] prepareClose,
        Func<ApplicationCleanupContext, ValueTask>[] stop,
        Func<ApplicationCleanupContext, ValueTask>[] dispose,
        Func<ApplicationSession, ValueTask>[] mounted,
        Func<ApplicationSession, ComponentRecipe, ComponentRecipe>[] configureRoot
    )
    {
        Start = start;
        PrepareClose = prepareClose;
        Stop = stop;
        Dispose = dispose;
        Mounted = mounted;
        ConfigureRoot = configureRoot;
    }

    internal Func<ApplicationStartContext, ValueTask>[] Start { get; }

    internal Func<
        ApplicationCloseContext,
        CancellationToken,
        ValueTask<bool>
    >[] PrepareClose { get; }

    internal Func<ApplicationCleanupContext, ValueTask>[] Stop { get; }

    internal Func<ApplicationCleanupContext, ValueTask>[] Dispose { get; }

    internal Func<ApplicationSession, ValueTask>[] Mounted { get; }

    internal Func<ApplicationSession, ComponentRecipe, ComponentRecipe>[] ConfigureRoot { get; }

    internal bool IsEmpty =>
        Start.Length == 0
        && PrepareClose.Length == 0
        && Stop.Length == 0
        && Dispose.Length == 0
        && Mounted.Length == 0
        && ConfigureRoot.Length == 0;
}

internal interface IApplicationLifecycleMounted
{
    ValueTask NotifyMountedAsync(ApplicationSession session);
}

internal sealed class ApplicationLifecycleComposer
    : IApplicationLifecycle,
        IApplicationLifecycleMounted
{
    private readonly ApplicationLifecycleCallbacks _callbacks;
    private readonly Func<ComponentRecipe>? _rootFactory;
    private readonly IApplicationLifecycle? _inner;
    private readonly List<Func<ApplicationCleanupContext, ValueTask>> _stopCallbacks;
    private readonly List<Func<ApplicationCleanupContext, ValueTask>> _disposeCallbacks;
    private readonly List<Func<ComponentRecipe, ComponentRecipe>> _rootContexts = [];
    private ApplicationSession? _session;
    private ComponentServiceBinding? _serviceBinding;
    private bool _servicesReady;
    private bool _rootFactoryInvoked;
    private bool _rootCreated;
    private bool _rootMounted;
    private Exception? _startupError;
    private ApplicationStartupOutcome? _cleanupOutcome;
    private int _started;
    private int _stopped;
    private int _disposed;

    internal ApplicationLifecycleComposer(
        ApplicationLifecycleCallbacks callbacks,
        Func<ComponentRecipe>? rootFactory,
        IApplicationLifecycle? inner = null
    )
    {
        _callbacks = callbacks;
        _rootFactory = rootFactory;
        _inner = inner;
        _stopCallbacks = [.. callbacks.Stop];
        _disposeCallbacks = [.. callbacks.Dispose];
    }

    public async ValueTask<ComponentRecipe> StartAsync(ApplicationSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("An application lifecycle can start only once.");
        AttachSession(session);

        ComponentRecipe root;
        try
        {
            foreach (var callback in _callbacks.Start)
            {
                var context = new ApplicationStartContext(session, this);
                try
                {
                    await callback(context);
                }
                finally
                {
                    context.Complete();
                }
            }

            if (_inner is null)
            {
                _servicesReady = true;
                _rootFactoryInvoked = true;
                root =
                    _rootFactory!()
                    ?? throw new InvalidOperationException(
                        "The application root factory returned null."
                    );
            }
            else
            {
                root =
                    await _inner.StartAsync(session)
                    ?? throw new InvalidOperationException(
                        "The application lifecycle returned a null root recipe."
                    );
                _servicesReady = true;
            }

            _rootCreated = true;
            foreach (var configureRoot in _callbacks.ConfigureRoot)
            {
                root =
                    configureRoot(session, root)
                    ?? throw new InvalidOperationException(
                        "An application root configurator returned null."
                    );
            }
            foreach (var provideContext in _rootContexts.AsEnumerable().Reverse())
                root = provideContext(root);
            if (_serviceBinding is not null)
                root = _serviceBinding.Attach(root);
            return root;
        }
        catch (Exception error)
        {
            _startupError = error;
            throw;
        }
    }

    public async ValueTask<bool> PrepareCloseAsync(CancellationToken cancellationToken)
    {
        var session = RequireSession();
        var restorations = new List<Func<ValueTask>>();
        foreach (var callback in _callbacks.PrepareClose)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = new ApplicationCloseContext(session, restorations);
            bool allowed;
            try
            {
                allowed = await callback(context, cancellationToken);
            }
            finally
            {
                context.Complete();
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!allowed)
            {
                await RestoreDeclinedAttemptAsync(restorations);
                return false;
            }
        }

        if (_inner is null)
            return true;
        var innerAllowed = await _inner.PrepareCloseAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!innerAllowed)
            await RestoreDeclinedAttemptAsync(restorations);
        return innerAllowed;
    }

    public async ValueTask StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        var errors = new List<Exception>();
        if (_inner is not null)
            await AttemptAsync(_inner.StopAsync, errors);
        var context = CreateCleanupContext();
        await RunReverseAsync(_stopCallbacks, context, errors);
        ThrowCleanupErrors("Application stop callbacks failed.", errors);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var errors = new List<Exception>();
        if (_inner is not null)
            await AttemptAsync(_inner.DisposeAsync, errors);
        var context = CreateCleanupContext();
        await RunReverseAsync(_disposeCallbacks, context, errors);
        ThrowCleanupErrors("Application disposal callbacks failed.", errors);
    }

    public async ValueTask NotifyMountedAsync(ApplicationSession session)
    {
        _rootMounted = true;
        foreach (var callback in _callbacks.Mounted)
        {
            try
            {
                await callback(session);
            }
            catch (Exception error)
            {
                _startupError = error;
                throw;
            }
        }
    }

    internal ComponentServiceBinding CreateServiceBinding(IComponentServiceSource source)
    {
        var session = RequireSession();
        session.CheckOwnerForServices();
        if (_serviceBinding is not null)
            throw new InvalidOperationException(
                "An application can attach only one root service binding."
            );
        return _serviceBinding = session.CreateServiceBinding(source);
    }

    internal void AddRootContext<T>(T value)
    {
        var session = RequireSession();
        session.CheckOwnerForServices();
        _rootContexts.Add(root => Context.Provide(value, root));
    }

    internal void AddStopCallback(Func<ApplicationCleanupContext, ValueTask> callback) =>
        _stopCallbacks.Add(callback);

    internal void AddDisposeCallback(Func<ApplicationCleanupContext, ValueTask> callback) =>
        _disposeCallbacks.Add(callback);

    internal void AttachSession(ApplicationSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_session is not null && !ReferenceEquals(_session, session))
            throw new InvalidOperationException(
                "An application lifecycle cannot be attached to more than one session."
            );
        _session = session;
    }

    internal void RecordStartupFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _startupError ??= error;
    }

    private ApplicationCleanupContext CreateCleanupContext() =>
        new(_session, _cleanupOutcome ??= CreateStartupOutcome());

    private ApplicationStartupOutcome CreateStartupOutcome() =>
        new(_servicesReady, _rootFactoryInvoked, _rootCreated, _rootMounted, _startupError);

    private ApplicationSession RequireSession() =>
        _session ?? throw new InvalidOperationException("Application startup has not begun.");

    private static async ValueTask RestoreDeclinedAttemptAsync(List<Func<ValueTask>> restorations)
    {
        var errors = new List<Exception>();
        for (var index = restorations.Count - 1; index >= 0; index--)
            try
            {
                await restorations[index]();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        ThrowCleanupErrors("Close-decline restoration failed.", errors);
    }

    private static async ValueTask RunReverseAsync(
        List<Func<ApplicationCleanupContext, ValueTask>> callbacks,
        ApplicationCleanupContext context,
        List<Exception> errors
    )
    {
        for (var index = callbacks.Count - 1; index >= 0; index--)
            await AttemptAsync(() => callbacks[index](context), errors);
    }

    private static async ValueTask AttemptAsync(Func<ValueTask> callback, List<Exception> errors)
    {
        try
        {
            await callback();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
    }

    private static void ThrowCleanupErrors(string message, List<Exception> errors)
    {
        if (errors.Count == 1)
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1)
            throw new AggregateException(message, errors);
    }
}
