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

/// <summary>Collects reusable configuration snapshots for <see cref="LucentApplication"/>.</summary>
public sealed class LucentApplicationBuilder
{
    private string _title = "Lucent";
    private Func<ThemeAppearance, Theme> _themeFactory = DefaultTheme;
    private IApplicationHost? _host;

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

    /// <summary>Sets the platform host for subsequently built applications.</summary>
    public LucentApplicationBuilder UseHost(IApplicationHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        return this;
    }

    /// <summary>Snapshots the current configuration into a one-shot application.</summary>
    /// <exception cref="InvalidOperationException">No host has been selected.</exception>
    public LucentApplication Build() =>
        new(
            _title,
            _themeFactory,
            _host ?? throw new InvalidOperationException("An application host must be selected.")
        );

    private static Theme DefaultTheme(ThemeAppearance appearance) =>
        appearance.Contrast == ThemeContrast.High ? ControlThemes.HighContrast
        : appearance.ColorScheme == ThemeColorScheme.Dark ? ControlThemes.Dark
        : ControlThemes.Light;
}

/// <summary>Owns the portable lifecycle for one Lucent application session.</summary>
public sealed class LucentApplication
{
    private readonly string _title;
    private readonly Func<ThemeAppearance, Theme> _themeFactory;
    private readonly IApplicationHost _host;
    private int _started;

    internal LucentApplication(
        string title,
        Func<ThemeAppearance, Theme> themeFactory,
        IApplicationHost host
    )
    {
        _title = title;
        _themeFactory = themeFactory;
        _host = host;
    }

    /// <summary>Creates a reusable application builder with the title "Lucent" and standard control themes.</summary>
    public static LucentApplicationBuilder CreateBuilder() => new();

    /// <summary>Runs one fixed component recipe through the configured host.</summary>
    public int Run(ComponentRecipe root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return Run(new RecipeLifecycle(root));
    }

    /// <summary>Runs one asynchronous lifecycle through the configured host and deterministic cleanup.</summary>
    public int Run(IApplicationLifecycle lifecycle)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
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
                appearance: initialAppearance
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
            session = new ApplicationSession(_title, composition, theme, lifecycle);
        }
        catch (Exception error)
        {
            var errors = new List<Exception> { error };
            CleanupWithoutSession(lifecycle, composition, errors);
            ThrowAll(errors);
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

    private static void ThrowAll(List<Exception> errors)
    {
        if (errors.Count == 0)
            return;
        if (errors.Count == 1)
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        throw new AggregateException("Application run and cleanup failed.", errors);
    }

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

    private sealed class RecipeLifecycle(ComponentRecipe recipe) : IApplicationLifecycle
    {
        public ValueTask<ComponentRecipe> StartAsync(ApplicationSession session) =>
            ValueTask.FromResult(recipe);

        public ValueTask<bool> PrepareCloseAsync() => ValueTask.FromResult(true);

        public ValueTask StopAsync() => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
