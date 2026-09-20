using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Lucent.Core;

/// <summary>A platform adapter that runs one caller-owned Lucent application session.</summary>
public interface IApplicationHost
{
    /// <summary>Runs the host synchronously until the application session completes.</summary>
    /// <returns>The process exit code selected by the host.</returns>
    int Run(ApplicationSession session);
}

/// <summary>Classifies a failure reported outside the application cleanup path.</summary>
public enum ApplicationFailureKind
{
    /// <summary>The session completed terminal cleanup with one or more failures.</summary>
    Terminal,

    /// <summary>A cancelled close-preparation operation faulted after terminal shutdown began.</summary>
    LateClosePreparation,
}

/// <summary>An immutable application failure delivered independently from lifecycle cleanup.</summary>
public sealed class ApplicationFailureReport
{
    internal ApplicationFailureReport(string title, ApplicationFailureKind kind, Exception error)
    {
        Title = title;
        Kind = kind;
        Error = error;
    }

    /// <summary>Gets the snapshotted application title.</summary>
    public string Title { get; }

    /// <summary>Gets whether the failure terminated the session or arrived from cancelled preparation.</summary>
    public ApplicationFailureKind Kind { get; }

    /// <summary>Gets the captured original or aggregated failure.</summary>
    public Exception Error { get; }
}

/// <summary>Collects reusable configuration snapshots for <see cref="LucentApplication"/>.</summary>
public sealed class LucentApplicationBuilder
{
    private string _title = "Lucent";
    private Func<ThemeAppearance, Theme> _themeFactory = DefaultTheme;
    private ControlPresentationMode _presentationMode = ControlPresentationMode.Standard;
    private IApplicationHost? _host;
    private Action<ApplicationFailureReport>? _failureReporter;
    private readonly List<Func<ApplicationStartContext, ValueTask>> _startCallbacks = [];
    private readonly List<
        Func<ApplicationCloseContext, CancellationToken, ValueTask<bool>>
    > _prepareCloseCallbacks = [];
    private readonly List<Func<ApplicationCleanupContext, ValueTask>> _stopCallbacks = [];
    private readonly List<Func<ApplicationCleanupContext, ValueTask>> _disposeCallbacks = [];
    private readonly List<Func<ApplicationSession, ValueTask>> _mountedCallbacks = [];
    private readonly List<
        Func<ApplicationSession, ComponentRecipe, ComponentRecipe>
    > _rootConfigurators = [];

    internal LucentApplicationBuilder() { }

    /// <summary>Sets the top-level window title for subsequently built applications.</summary>
    public LucentApplicationBuilder SetTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        _title = title;
        return this;
    }

    /// <summary>Sets the appearance-oriented theme factory for subsequently built applications.</summary>
    public LucentApplicationBuilder SetTheme(Func<ThemeAppearance, Theme> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _themeFactory = factory;
        return this;
    }

    /// <summary>Sets the composition-scoped stock control presentation mode.</summary>
    public LucentApplicationBuilder SetPresentationMode(ControlPresentationMode mode)
    {
        ValidatePresentationMode(mode, nameof(mode));
        _presentationMode = mode;
        return this;
    }

    /// <summary>Sets the platform host for subsequently built applications.</summary>
    public LucentApplicationBuilder UseHost(IApplicationHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        return this;
    }

    /// <summary>Reports terminal and late close-preparation failures independently after cleanup.</summary>
    /// <remarks>The callback is best effort before process exit and cannot rely on UI or application services remaining available.</remarks>
    public LucentApplicationBuilder SetFailureReporter(Action<ApplicationFailureReport> reporter)
    {
        ArgumentNullException.ThrowIfNull(reporter);
        _failureReporter = reporter;
        return this;
    }

    /// <summary>Appends an asynchronous owner-thread startup callback.</summary>
    /// <remarks>Callbacks run in registration order before the deferred root factory. Register acquired-resource cleanup through the supplied context.</remarks>
    public LucentApplicationBuilder OnStart(Func<ApplicationStartContext, ValueTask> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _startCallbacks.Add(callback);
        return this;
    }

    /// <summary>Appends an asynchronous close-preparation callback.</summary>
    /// <remarks>Callbacks run in registration order and stop at the first decline. Use the attempt context to register reverse-order recovery for a declined attempt.</remarks>
    public LucentApplicationBuilder OnPrepareClose(
        Func<ApplicationCloseContext, CancellationToken, ValueTask<bool>> callback
    )
    {
        ArgumentNullException.ThrowIfNull(callback);
        _prepareCloseCallbacks.Add(callback);
        return this;
    }

    /// <summary>Appends an unconditional asynchronous stop callback.</summary>
    /// <remarks>Callbacks run in reverse registration order before composition disposal and receive the startup outcome.</remarks>
    public LucentApplicationBuilder OnStop(Func<ApplicationCleanupContext, ValueTask> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _stopCallbacks.Add(callback);
        return this;
    }

    /// <summary>Appends an unconditional asynchronous final cleanup callback.</summary>
    /// <remarks>Callbacks run in reverse registration order after composition disposal and receive the startup outcome.</remarks>
    public LucentApplicationBuilder OnDispose(Func<ApplicationCleanupContext, ValueTask> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _disposeCallbacks.Add(callback);
        return this;
    }

    /// <summary>Appends a notification for a root that mounted successfully.</summary>
    /// <remarks>This runs after the composition mount returns. It is distinct from services-ready startup.</remarks>
    public LucentApplicationBuilder OnMounted(Func<ApplicationSession, ValueTask> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _mountedCallbacks.Add(callback);
        return this;
    }

    /// <summary>Appends a postfactory transformation for the application root recipe.</summary>
    /// <remarks>Configurators run in registration order after the root factory and startup callbacks, before the first mount.</remarks>
    public LucentApplicationBuilder ConfigureRoot(
        Func<ApplicationSession, ComponentRecipe, ComponentRecipe> configure
    )
    {
        ArgumentNullException.ThrowIfNull(configure);
        _rootConfigurators.Add(configure);
        return this;
    }

    /// <summary>Snapshots the current configuration into a one-shot application.</summary>
    /// <exception cref="InvalidOperationException">No host has been selected.</exception>
    public LucentApplication Build() => BuildCore(rootFactory: null);

    /// <summary>Snapshots the current configuration and one fixed root recipe into a one-shot application.</summary>
    public LucentApplication Build(ComponentRecipe root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return BuildCore(() => root);
    }

    /// <summary>Snapshots the current configuration and a deferred root factory into a one-shot application.</summary>
    /// <remarks>The factory runs once on the session owner thread after startup callbacks complete.</remarks>
    public LucentApplication Build(Func<ComponentRecipe> rootFactory)
    {
        ArgumentNullException.ThrowIfNull(rootFactory);
        return BuildCore(rootFactory);
    }

    private LucentApplication BuildCore(Func<ComponentRecipe>? rootFactory)
    {
        var host =
            _host ?? throw new InvalidOperationException("An application host must be selected.");
        return new LucentApplication(
            _title,
            _themeFactory,
            _presentationMode,
            host,
            _failureReporter,
            new ApplicationLifecycleCallbacks(
                [.. _startCallbacks],
                [.. _prepareCloseCallbacks],
                [.. _stopCallbacks],
                [.. _disposeCallbacks],
                [.. _mountedCallbacks],
                [.. _rootConfigurators]
            ),
            rootFactory
        );
    }

    private static Theme DefaultTheme(ThemeAppearance appearance) =>
        appearance.Contrast == ThemeContrast.High ? ControlThemes.HighContrast
        : appearance.ColorScheme == ThemeColorScheme.Dark ? ControlThemes.Dark
        : ControlThemes.Light;

    private static void ValidatePresentationMode(
        ControlPresentationMode value,
        string parameterName
    )
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(parameterName);
    }
}

/// <summary>Owns the portable lifecycle for one Lucent application session.</summary>
public sealed class LucentApplication
{
    private readonly string _title;
    private readonly Func<ThemeAppearance, Theme> _themeFactory;
    private readonly ControlPresentationMode _presentationMode;
    private readonly IApplicationHost _host;
    private readonly Action<ApplicationFailureReport>? _failureReporter;
    private readonly ApplicationLifecycleCallbacks _lifecycleCallbacks;
    private readonly Func<ComponentRecipe>? _rootFactory;
    private int _started;

    internal LucentApplication(
        string title,
        Func<ThemeAppearance, Theme> themeFactory,
        ControlPresentationMode presentationMode,
        IApplicationHost host,
        Action<ApplicationFailureReport>? failureReporter,
        ApplicationLifecycleCallbacks lifecycleCallbacks,
        Func<ComponentRecipe>? rootFactory
    )
    {
        _title = title;
        _themeFactory = themeFactory;
        _presentationMode = presentationMode;
        _host = host;
        _failureReporter = failureReporter;
        _lifecycleCallbacks = lifecycleCallbacks;
        _rootFactory = rootFactory;
    }

    /// <summary>Creates a reusable application builder with the title "Lucent" and standard control themes.</summary>
    public static LucentApplicationBuilder CreateBuilder() => new();

    /// <summary>Runs the root factory captured by <see cref="LucentApplicationBuilder.Build(Func{ComponentRecipe})"/>.</summary>
    /// <exception cref="InvalidOperationException">This application was built without a root factory.</exception>
    public int Run() =>
        Run(
            _rootFactory
                ?? throw new InvalidOperationException(
                    "This application was built without a root factory. Supply one to Run or Build."
                )
        );

    /// <summary>Runs one fixed component recipe through the configured host.</summary>
    public int Run(ComponentRecipe root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return Run(() => root);
    }

    /// <summary>Runs one deferred component recipe through the configured host.</summary>
    /// <remarks>The factory runs once on the session owner thread after startup callbacks complete.</remarks>
    public int Run(Func<ComponentRecipe> rootFactory)
    {
        ArgumentNullException.ThrowIfNull(rootFactory);
        return RunCore(new ApplicationLifecycleComposer(_lifecycleCallbacks, rootFactory));
    }

    /// <summary>Runs one asynchronous lifecycle through the configured host and deterministic cleanup.</summary>
    public int Run(IApplicationLifecycle lifecycle)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        if (_rootFactory is not null)
            throw new InvalidOperationException(
                "An application built with a root factory must run that root, not a separate lifecycle."
            );
        if (!_lifecycleCallbacks.IsEmpty)
            lifecycle = new ApplicationLifecycleComposer(
                _lifecycleCallbacks,
                rootFactory: null,
                lifecycle
            );
        return RunCore(lifecycle);
    }

    private int RunCore(IApplicationLifecycle lifecycle)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("A built application can run only once.");

        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "application");
        ApplicationSession? session = null;
        try
        {
            var initialAppearance = ThemeAppearance.Light;
            var theme = new ThemeContext(
                composition.Root.Scope,
                RequireTheme(_themeFactory(initialAppearance)),
                appearance: initialAppearance,
                presentationMode: _presentationMode
            );
            var appliedAppearance = initialAppearance;
            _ = composition.Root.Scope.Effect(
                () =>
                {
                    var appearance = theme.Appearance;
                    if (appearance == appliedAppearance)
                        return;
                    var next = RequireTheme(_themeFactory(appearance));
                    appliedAppearance = appearance;
                    theme.Theme = next;
                },
                "application-theme"
            );
            session = new ApplicationSession(
                _title,
                composition,
                theme,
                lifecycle,
                _failureReporter
            );
            if (lifecycle is ApplicationLifecycleComposer configuredLifecycle)
                configuredLifecycle.AttachSession(session);
        }
        catch (Exception error)
        {
            if (lifecycle is ApplicationLifecycleComposer configuredLifecycle)
                configuredLifecycle.RecordStartupFailure(error);
            var errors = new List<Exception> { error };
            CleanupWithoutSession(lifecycle, composition, errors);
            var startupFailure = CreateFailure(errors);
            ApplicationSession.ReportFailure(
                _title,
                _failureReporter,
                ApplicationFailureKind.Terminal,
                startupFailure
            );
            ExceptionDispatchInfo.Capture(startupFailure).Throw();
        }

        var exitCode = 0;
        Exception? hostError = null;
        try
        {
            exitCode = _host.Run(session!);
        }
        catch (Exception error)
        {
            hostError = error;
        }

        session!.Abort(hostError);
        PumpToCompletion(session);
        if (session.Failure is { } failure)
            ExceptionDispatchInfo.Capture(failure).Throw();
        return exitCode;
    }

    private static void PumpToCompletion(ApplicationSession session)
    {
        using var available = new AutoResetEvent(false);
        void Wake() => available.Set();
        session.WorkAvailable += Wake;
        try
        {
            while (!session.IsCompleted)
            {
                session.ProcessEvents();
                if (!session.Composition.IsDisposed)
                    session.Composition.Flush();
                if (!session.IsCompleted)
                    available.WaitOne();
            }
        }
        catch (Exception error)
        {
            session.Abort(error);
            while (!session.IsCompleted)
            {
                try
                {
                    session.ProcessEvents();
                    if (!session.Composition.IsDisposed)
                        session.Composition.Flush();
                }
                catch (Exception additional)
                {
                    session.Abort(additional);
                }
                if (!session.IsCompleted)
                    available.WaitOne();
            }
        }
        finally
        {
            session.WorkAvailable -= Wake;
        }
    }

    private static void CleanupWithoutSession(
        IApplicationLifecycle lifecycle,
        Composition composition,
        List<Exception> errors
    ) =>
        Pump(async () =>
        {
            try
            {
                await lifecycle.StopAsync();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            try
            {
                composition.Dispose();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            try
            {
                await lifecycle.DisposeAsync();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        });

    private static void Pump(Func<Task> operation)
    {
        var prior = SynchronizationContext.Current;
        using var context = new CleanupSynchronizationContext();
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            var task = operation();
            while (!task.IsCompleted)
                context.RunOne();
            task.GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prior);
        }
    }

    private static Theme RequireTheme(Theme? theme) =>
        theme
        ?? throw new InvalidOperationException("The application theme factory returned null.");

    private static Exception CreateFailure(List<Exception> errors) =>
        errors.Count == 1
            ? errors[0]
            : new AggregateException("Application run and cleanup failed.", errors);

    private sealed class CleanupSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = [];
        private readonly AutoResetEvent _available = new(false);

        public override void Post(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);
            _queue.Enqueue((d, state));
            _available.Set();
        }

        internal void RunOne()
        {
            (SendOrPostCallback Callback, object? State) work;
            while (!_queue.TryDequeue(out work))
            {
                _available.WaitOne();
            }
            work.Callback(work.State);
        }

        public void Dispose() => _available.Dispose();
    }
}
