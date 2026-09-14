using Lucent.Core;
using Lucent.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace HostedContextNavigation;

internal static class Program
{
    private static readonly ProbeState State = new();

    private static int Main()
    {
        var lifecycle = new HostedApplication(
            session =>
            {
                var builder = HostedApplication.CreateBuilder();
                builder.Services.AddSingleton(State);
                builder.Services.AddScoped(_ => new SharedScoped(State));
                builder.Services.AddTransient(_ => new GenericTransient<string>(State));
                builder.Services.AddScoped(services => new ProbeWorkspace(
                    session.Scope,
                    services.GetRequiredService<SharedScoped>(),
                    State
                ));
                return builder.Build();
            },
            (services, _) =>
            {
                var workspace = services.GetRequiredService<ProbeWorkspace>();
                State.Workspace = workspace;
                return ProbeRouting.Root(workspace);
            },
            (services, cancellationToken) =>
                services.GetRequiredService<ProbeWorkspace>().PrepareClose(cancellationToken)
        );
        var application = LucentApplication.CreateBuilder().UseHost(new ProbeHost(State)).Build();

        Require(application.Run(lifecycle) == 0, "Hosted application returned a failure code.");
        State.VerifyFinal();
        Console.WriteLine("hosted-context-navigation-native-aot=pass");
        return 0;
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

internal sealed class ProbeHost(ProbeState state) : IApplicationHost
{
    private const int MeasuredRemountCount = 16;

    public int Run(ApplicationSession session)
    {
        session.Start();
        Pump(session, () => session.Status.Phase == ApplicationPhase.Running);
        var workspace =
            state.Workspace
            ?? throw new InvalidOperationException("The hosted workspace did not resolve.");
        workspace.BeginAcceptedWrite();

        Complete(session, workspace.Navigation.Navigate(ProbeRoutes.Item(1)));
        var first = workspace.Outlet.Snapshot;
        Program.Require(first.Levels.Count == 2, "The first leaf route did not publish.");
        var rootId = first.Levels[0].ElementId;
        var firstLeafId = first.Levels[1].ElementId;

        workspace.DelayNextItemEnter();
        var superseded = workspace.Navigation.Navigate(ProbeRoutes.Item(2));
        Pump(session, () => workspace.DelayedPreparationStarted);
        var replacement = workspace.Navigation.Navigate(ProbeRoutes.Item(3));
        Await(session, superseded);
        Complete(session, replacement);
        Program.Require(
            superseded.Completion.Result.Kind == NavigationOutcomeKind.Superseded,
            "The delayed navigation was not superseded."
        );
        var replaced = workspace.Outlet.Snapshot;
        Program.Require(replaced.Levels[0].ElementId == rootId, "The root route remounted.");
        Program.Require(
            replaced.Levels[1].ElementId != firstLeafId,
            "A changed leaf route did not remount its suffix."
        );

        Complete(session, workspace.Navigation.Navigate(ProbeRoutes.Item(4)));
        var warmed = workspace.Outlet.Snapshot;
        Program.Require(warmed.Levels[0].ElementId == rootId, "Warmup remounted the root route.");
        var creationsBeforeMeasurement = state.TransientCreations;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < MeasuredRemountCount; index++)
        {
            var item = index % 2 == 0 ? 5 : 4;
            Complete(session, workspace.Navigation.Navigate(ProbeRoutes.Item(item)));
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var measured = workspace.Outlet.Snapshot;
        Program.Require(
            measured.Levels[0].ElementId == rootId,
            "Measured leaf navigation remounted the retained root."
        );
        Program.Require(
            state.TransientCreations - creationsBeforeMeasurement == MeasuredRemountCount,
            "Measured leaf navigation did not create exactly one transient per suffix."
        );
        Console.WriteLine(
            $"hosted-context-navigation-allocated-bytes-per-remount={allocated / MeasuredRemountCount}"
        );
        Console.WriteLine(
            $"hosted-context-navigation-measured-transient-creations={MeasuredRemountCount}"
        );
        Console.WriteLine("hosted-context-navigation-retained-root=true");

        var resolutions = state.TransientCreations;
        session.Composition.Flush();
        _ = session.Composition.ContextDump();
        _ = session.Composition.SemanticSnapshot();
        using (SceneLayout.Project(session.Composition, new(640, 480, 1), new EmptyShaper())) { }
        Program.Require(
            state.TransientCreations == resolutions,
            "Stable reads or drains repeated generated service resolution."
        );
        Program.Require(!workspace.AcceptedWrite.IsCompleted, "Navigation canceled accepted work.");

        session.RequestClose();
        Pump(
            session,
            () => workspace.CloseAttempts == 1 && session.Status.Phase == ApplicationPhase.Running
        );
        Program.Require(!session.IsCompleted, "A declined close completed the session.");
        session.RequestClose();
        Pump(session, () => workspace.CloseAttempts == 2);
        Program.Require(!session.IsCompleted, "Close ignored the accepted write barrier.");
        workspace.CompleteAcceptedWrite();
        Pump(session, () => session.IsCompleted);
        return 0;
    }

    private static void Complete(ApplicationSession session, NavigationOperation operation)
    {
        Await(session, operation);
        Program.Require(operation.Completion.Result.IsCommitted, "Navigation did not commit.");
    }

    private static void Await(ApplicationSession session, NavigationOperation operation) =>
        Pump(session, () => operation.Completion.IsCompleted);

    private static void Pump(ApplicationSession session, Func<bool> complete)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!complete())
        {
            session.ProcessEvents();
            if (!session.Composition.IsDisposed)
                session.Composition.Flush();
            if (Environment.TickCount64 >= deadline)
                throw new TimeoutException("The hosted context/navigation probe did not settle.");
            Thread.Sleep(1);
        }
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }
}

internal sealed class ProbeWorkspace : IAsyncDisposable
{
    private readonly ProbeState _state;
    private readonly SharedScoped _expectedShared;
    private readonly TaskCompletionSource _acceptedWrite = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private TaskCompletionSource? _delayedPreparation;
    private bool _delayNextItem;

    public ProbeWorkspace(ReactiveScope owner, SharedScoped expectedShared, ProbeState state)
    {
        _state = state;
        _expectedShared = expectedShared;
        Events = state.Events;
        Navigation = new(owner, ProbeRouting.Table, ProbeRoutes.Shell().Location);
        Outlet = new();
        owner.OnDispose(Outlet.Dispose);
        Events.Add("workspace-create");
    }

    public List<string> Events { get; }
    public NavigationSession Navigation { get; }
    public RouteOutletHandle Outlet { get; }
    public Task AcceptedWrite => _acceptedWrite.Task;
    public int CloseAttempts { get; private set; }
    public bool DelayedPreparationStarted => _delayedPreparation is not null;

    public void RecordRoot(
        RouteContext<ShellRoute> route,
        SharedScoped shared,
        GenericTransient<string> transient
    )
    {
        Program.Require(route.Definition.Id.Value == "shell", "Wrong typed shell context.");
        Program.Require(ReferenceEquals(shared, _expectedShared), "Root lost the scoped service.");
        _state.RecordGenerated(shared, transient, "root");
    }

    public void RecordLeaf(
        RouteContext<ShellRoute> shell,
        RouteContext<ItemRoute> route,
        SharedScoped shared,
        GenericTransient<string> transient
    )
    {
        Program.Require(shell.Definition.Id.Value == "shell", "Leaf lost its parent context.");
        Program.Require(route.Parameters.Id is >= 1 and <= 5, "Unexpected leaf route parameter.");
        Program.Require(ReferenceEquals(shared, _expectedShared), "Leaf lost the scoped service.");
        _state.RecordGenerated(shared, transient, "leaf:" + route.Parameters.Id);
    }

    public void BeginAcceptedWrite() => Events.Add("accepted-write-start");

    public void CompleteAcceptedWrite()
    {
        Events.Add("accepted-write-complete");
        _acceptedWrite.TrySetResult();
    }

    public void DelayNextItemEnter() => _delayNextItem = true;

    public ValueTask<NavigationPreparationResult> PrepareRoute(
        RouteLevelDescriptor level,
        RouteOutletPreparationRequest request,
        CancellationToken cancellationToken
    )
    {
        if (
            !_delayNextItem
            || request.Phase != NavigationPreparationPhase.Enter
            || level.Id.Value != "item"
        )
            return ValueTask.FromResult(NavigationPreparationResult.Allow);
        _delayNextItem = false;
        _delayedPreparation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return WaitForSupersession(_delayedPreparation.Task, cancellationToken);
    }

    public async ValueTask<bool> PrepareClose(CancellationToken cancellationToken)
    {
        CloseAttempts++;
        if (CloseAttempts == 1)
            return false;
        await _acceptedWrite.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public ValueTask DisposeAsync()
    {
        Events.Add("workspace-dispose");
        return ValueTask.CompletedTask;
    }

    private static async ValueTask<NavigationPreparationResult> WaitForSupersession(
        Task gate,
        CancellationToken cancellationToken
    )
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return NavigationPreparationResult.Allow;
    }
}

internal sealed class SharedScoped : IAsyncDisposable
{
    private readonly ProbeState _state;

    internal SharedScoped(ProbeState state)
    {
        _state = state;
        state.RecordSharedCreation(this);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        _state.Events.Add("shared-dispose");
    }
}

internal sealed class GenericTransient<T> : IDisposable
{
    private readonly ProbeState _state;

    internal GenericTransient(ProbeState state)
    {
        _state = state;
        Id = state.RecordTransientCreation(this);
    }

    internal int Id { get; }

    public void Dispose() => _state.Events.Add("transient-dispose:" + typeof(T).Name + ":" + Id);
}

internal sealed class OwnedProbe : IDisposable
{
    private readonly List<string> _events;
    private readonly string _name;

    public OwnedProbe(List<string> events, string name)
    {
        _events = events;
        _name = name;
        events.Add("owned-create:" + name);
    }

    public void Dispose() => _events.Add("owned-dispose:" + _name);
}

internal sealed class ProbeState
{
    private readonly List<WeakReference> _services = [];
    private SharedScoped? _expectedShared;
    private int _sharedCreations;
    private int _transientCreations;
    private int _generatedMounts;

    internal List<string> Events { get; } = [];
    internal ProbeWorkspace? Workspace { get; set; }
    internal int TransientCreations => _transientCreations;

    internal void RecordSharedCreation(SharedScoped shared)
    {
        _sharedCreations++;
        _expectedShared = shared;
        _services.Add(new(shared));
        Events.Add("shared-create");
    }

    internal int RecordTransientCreation<T>(GenericTransient<T> transient)
    {
        var id = checked(++_transientCreations);
        _services.Add(new(transient));
        Events.Add("transient-create:" + typeof(T).Name + ":" + id);
        return id;
    }

    internal void RecordGenerated(
        SharedScoped shared,
        GenericTransient<string> transient,
        string location
    )
    {
        Program.Require(
            ReferenceEquals(shared, _expectedShared),
            "Generated mounts did not share one scoped service."
        );
        _generatedMounts++;
        Events.Add("generated:" + location + ":" + transient.Id);
    }

    internal void VerifyFinal()
    {
        const int expectedLeafMounts = 19;
        const int expectedGeneratedMounts = expectedLeafMounts + 1;
        Program.Require(_sharedCreations == 1, "The scoped factory did not run exactly once.");
        Program.Require(
            _generatedMounts == expectedGeneratedMounts,
            "Unexpected generated mount count."
        );
        Program.Require(
            _transientCreations == expectedGeneratedMounts,
            "Closed generic transients did not resolve once per mounted suffix."
        );
        Program.Require(
            Events.Count(item => item == "owned-dispose:leaf") == expectedLeafMounts,
            "Leaf-owned cleanup count was incorrect."
        );
        Program.Require(
            Events.Count(item => item == "owned-dispose:root") == 1,
            "Root-owned cleanup count was incorrect."
        );
        Program.Require(
            Events.Count(item => item.StartsWith("transient-dispose:", StringComparison.Ordinal))
                == expectedGeneratedMounts,
            "Transient cleanup count was incorrect."
        );
        Program.Require(
            Events.Count(item => item == "shared-dispose") == 1,
            "Scoped cleanup count was incorrect."
        );
        var firstProviderDispose = Events.FindIndex(item =>
            item == "workspace-dispose"
            || item == "shared-dispose"
            || item.StartsWith("transient-dispose:", StringComparison.Ordinal)
        );
        Program.Require(firstProviderDispose >= 0, "Provider cleanup did not begin.");
        var leafOwnedDispose = Events.FindLastIndex(item => item == "owned-dispose:leaf");
        var rootOwnedDispose = Events.FindIndex(item => item == "owned-dispose:root");
        Program.Require(
            leafOwnedDispose >= 0 && leafOwnedDispose < firstProviderDispose,
            "A leaf-owned resource outlived the service provider."
        );
        Program.Require(
            rootOwnedDispose >= 0 && rootOwnedDispose < firstProviderDispose,
            "The retained root-owned resource outlived the service provider."
        );
        Program.Require(
            Events.Count(item => item == "workspace-dispose") == 1,
            "Async workspace disposal count was incorrect."
        );
        Program.Require(
            Events.Contains("accepted-write-complete"),
            "The accepted write did not complete independently."
        );
        Workspace = null;
        _expectedShared = null;
        RequireReleased(_services);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    private static void RequireReleased(IReadOnlyList<WeakReference> services)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Program.Require(
            services.All(reference => !reference.IsAlive),
            "The disposed provider retained a scoped or transient service."
        );
    }
}
