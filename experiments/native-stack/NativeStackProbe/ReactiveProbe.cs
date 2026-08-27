internal sealed record ReactiveCheckResult(bool Ok, bool UiThreadOnly, bool LazyMemoized, bool RuntimeTrackingBounded, bool CompilerRegistration, bool Batched, bool UnrelatedWriteIdle, bool OwnershipCleanup, bool CycleNamed, bool AsyncCancellation, bool StaleRetention, bool LatestGenerationWins, bool PendingFacet, bool ErrorFacet, bool DisposedAsyncCannotCommit, int VisibleFrames, int UnrelatedFrames, int DependencyLimit);

internal static class ReactiveProbe
{
    public static ReactiveCheckResult Run()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.Scope();
        var source = scope.Signal(1, "count");
        var evaluations = 0;
        var doubled = scope.Computed(() => { evaluations++; return source.Value * 2; }, "doubled");
        Check(evaluations == 0 && doubled.Value == 2 && doubled.Value == 2 && evaluations == 1, "Computed was not lazy and memoized.");
        source.Value = 2;
        Check(doubled.Value == 4 && evaluations == 2, "Computed did not invalidate.");

        var declaredSource = scope.Signal(1, "compiler.source");
        var declaredRuns = 0;
        var declared = scope.Computed(() => ++declaredRuns, "compiler.target");
        graph.RegisterDependencies(declared, declaredSource);
        _ = declared.Value; declaredSource.Value++; _ = declared.Value;
        var compilerRegistration = declaredRuns == 2;
        Check(compilerRegistration, "Declared dependency did not invalidate its target.");

        var frames = new FrameScheduler(() => { });
        var visible = scope.Signal(0, "visible");
        var unrelated = scope.Signal(0, "unrelated");
        var effectRuns = 0;
        _ = scope.Effect(() => { _ = visible.Value; effectRuns++; frames.Projected(new(0, 1, 0)); }, "paint");
        graph.Drain(); frames.Pump();
        var baselineFrames = frames.PresentCalls;
        graph.Batch(() => { unrelated.Value++; unrelated.Value++; });
        frames.Pump();
        var unrelatedFrames = frames.PresentCalls - baselineFrames;
        var ranInsideBatch = false;
        graph.Batch(() => { visible.Value++; graph.Drain(); ranInsideBatch = effectRuns != 1; visible.Value++; });
        frames.Pump();
        var batched = !ranInsideBatch && effectRuns == 2 && frames.PresentCalls == baselineFrames + 1;
        Check(batched && unrelatedFrames == 0, "Batch or unrelated-write scheduling failed.");

        var bounded = false;
        try
        {
            var many = Enumerable.Range(0, 65).Select(index => scope.Signal(index, $"limit.{index}")).ToArray();
            _ = scope.Computed(() => many.Sum(signal => signal.Value), "limit.target").Value;
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("64 dependency", StringComparison.Ordinal)) { bounded = true; }

        ReactiveComputed<int>? cycleA = null;
        ReactiveComputed<int>? cycleB = null;
        cycleA = scope.Computed(() => cycleB!.Value, "cycle.a");
        cycleB = scope.Computed(() => cycleA!.Value, "cycle.b");
        var namedCycle = false;
        try { _ = cycleA.Value; }
        catch (InvalidOperationException exception) when (exception.Message == "Reactive cycle: cycle.a -> cycle.b -> cycle.a") { namedCycle = true; }

        var asyncSource = scope.Signal(1, "async.source");
        var work = new List<TaskCompletionSource<int?>>();
        var cancellationObserved = false;
        var async = scope.AsyncComputed(token =>
        {
            _ = asyncSource.Value;
            var next = new TaskCompletionSource<int?>();
            token.Register(() => cancellationObserved = true);
            work.Add(next);
            return next.Task;
        }, "async.value");
        Check(async.Value is null && async.Pending && work.Count == 1, "Async start/pending failed.");
        asyncSource.Value = 2;
        _ = async.Value;
        Check(work.Count == 2, "Async dependency invalidation did not restart.");
        work[0].SetResult(1); graph.Drain();
        var staleRetention = async.Value is null && async.Pending;
        work[1].SetResult(2); graph.Drain();
        var latest = async.Value == 2 && !async.Pending && async.Error is null;
        asyncSource.Value = 3; _ = async.Value; work[2].SetException(new InvalidOperationException("expected")); graph.Drain();
        var error = async.Value == 2 && !async.Pending && async.Error?.Message == "expected";

        var facetWork = new TaskCompletionSource<int?>();
        var facetAsync = scope.AsyncComputed(_ => facetWork.Task, "facet.async");
        var facetRuns = 0;
        _ = scope.Effect(() => { _ = facetAsync.Pending; _ = facetAsync.Error; facetRuns++; }, "facet.effect");
        graph.Drain();
        facetWork.SetException(new InvalidOperationException("facet")); graph.Drain();
        var reactiveFacets = facetRuns == 2 && !facetAsync.Pending && facetAsync.Error?.Message == "facet";

        var cleanupSource = graph.Signal(0, "cleanup.source");
        var cleanupRuns = 0;
        using (var owned = graph.Scope())
        {
            _ = owned.Effect(() => { _ = cleanupSource.Value; cleanupRuns++; }, "cleanup.effect");
            graph.Drain();
        }
        cleanupSource.Value++; graph.Drain();
        var ownershipCleanup = cleanupRuns == 1;

        var disposedSource = graph.Signal(1, "disposed.source");
        var disposedWork = new TaskCompletionSource<int?>();
        var disposedCancellationObserved = false;
        ReactiveAsyncComputed<int?> disposed;
        using (var owned = graph.Scope())
        {
            disposed = owned.AsyncComputed(token => { token.Register(() => disposedCancellationObserved = true); _ = disposedSource.Value; return disposedWork.Task; }, "disposed.async");
            _ = disposed.Value;
        }
        disposedWork.SetResult(9); graph.Drain();
        var disposedCannotCommit = disposedCancellationObserved && disposed.Value is null && disposed.Error is null;

        var uiThreadOnly = false;
        try { Task.Run(() => source.Value).GetAwaiter().GetResult(); }
        catch (InvalidOperationException exception) when (exception.Message.Contains("UI thread", StringComparison.Ordinal)) { uiThreadOnly = true; }
        Check(bounded && namedCycle && cancellationObserved && staleRetention && latest && error && reactiveFacets && ownershipCleanup && disposedCannotCommit && uiThreadOnly, "Reactive self-check failed.");
        return new(true, uiThreadOnly, true, bounded, compilerRegistration, batched, unrelatedFrames == 0, ownershipCleanup, namedCycle, cancellationObserved, staleRetention, latest, reactiveFacets, error, disposedCannotCommit, frames.PresentCalls, unrelatedFrames, 64);
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
