using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Lucent.Core;

/// <summary>A UI-thread-owned reactive graph for all Lucent authoring surfaces.</summary>
/// <remarks>Read and mutation operations belong to the creating thread. Worker completion is queued and becomes observable only when the owner calls <see cref="Drain()"/>.</remarks>
public sealed class ReactiveGraph
{
    internal const int DefaultMaximumWorkItems = 10_000;
    private const int MaximumRetainedDrainFailures = 64;
    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<int, ReactiveNode> _nodes = [];
    private readonly Dictionary<int, ReactiveScope> _scopes = [];
    private readonly LinkedList<ReactiveEffect> _effects = [];
    private readonly ConcurrentQueue<IPosted> _posted = new();
    private readonly object _postedGate = new();
    private readonly List<ReactiveNode> _evaluating = [];
    private ReactiveCollector? _collecting;
    private ReactiveCollector? _suspendedCollector;
    private int _mutationGuardDepth;
    private DrainState? _drainState;
    private int _batchDepth;
    private int _nextNodeId;
    private int _nextScopeId;

    /// <summary>Raised when worker-posted work changes from empty to nonempty.</summary>
    /// <remarks>Observers are notified independently. Their failures are posted for aggregation by <see cref="Drain()"/>; a new edge for those failures re-notifies only still-subscribed observers that succeeded.</remarks>
    public event Action? WorkAvailable;

    /// <summary>Creates a root lifetime scope on the UI thread. Disposing it releases every owned node and child scope.</summary>
    public ReactiveScope CreateScope(string name)
    {
        CheckThread();
        CheckMutationGuard();
        ValidateName(name, nameof(name));
        return new ReactiveScope(this, null, name);
    }

    /// <summary>Creates writable graph state that notifies dependent nodes when its value changes.</summary>
    public Signal<T> Signal<T>(T value, string name)
    {
        CheckThread();
        CheckMutationGuard();
        ValidateName(name, nameof(name));
        return new Signal<T>(this, value, name, null);
    }

    /// <summary>Creates a lazy memoized value whose dependencies are discovered during each evaluation.</summary>
    public Derived<T> Derived<T>(Func<T> compute, string name)
    {
        CheckThread();
        CheckMutationGuard();
        ArgumentNullException.ThrowIfNull(compute);
        ValidateName(name, nameof(name));
        return new Derived<T>(this, compute, name, null);
    }

    /// <summary>Creates an immediately scheduled callback that reruns after values it reads change.</summary>
    public ReactiveEffect Effect(Action callback, string name)
    {
        CheckThread();
        CheckMutationGuard();
        ArgumentNullException.ThrowIfNull(callback);
        ValidateName(name, nameof(name));
        return new ReactiveEffect(this, callback, name, null);
    }

    /// <summary>Creates latest-generation asynchronous state; only the current generation may commit on <see cref="Drain()"/>.</summary>
    public AsyncValue<T> Async<T>(Func<CancellationToken, Task<T>> load, string name)
    {
        CheckThread();
        CheckMutationGuard();
        ArgumentNullException.ThrowIfNull(load);
        ValidateName(name, nameof(name));
        return new AsyncValue<T>(this, load, default!, false, name, null);
    }

    /// <summary>Creates latest-generation asynchronous state with a value retained until its replacement commits.</summary>
    public AsyncValue<T> Async<T>(Func<CancellationToken, Task<T>> load, T staleValue, string name)
    {
        CheckThread();
        CheckMutationGuard();
        ArgumentNullException.ThrowIfNull(load);
        ValidateName(name, nameof(name));
        return new AsyncValue<T>(this, load, staleValue, true, name, null);
    }

    /// <summary>Defers effect execution until the outermost batch exits.</summary>
    public void Batch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        CheckThread();
        CheckMutationGuard();
        _batchDepth++;
        Exception? bodyError = null;
        try
        {
            action();
        }
        catch (Exception exception)
        {
            bodyError = exception;
        }
        finally
        {
            _batchDepth--;
        }

        if (_batchDepth != 0)
        {
            if (bodyError is not null)
                ExceptionDispatchInfo.Capture(bodyError).Throw();
            return;
        }

        if (_drainState is not null)
        {
            if (bodyError is not null)
                ExceptionDispatchInfo.Capture(bodyError).Throw();
            return;
        }

        Exception? drainError = null;
        try
        {
            Drain();
        }
        catch (Exception exception)
        {
            drainError = exception;
        }
        ThrowCombined(bodyError, drainError, "Reactive batch failed.");
    }

    /// <summary>Commits posted async completions and scheduled effects on the owning UI thread.</summary>
    public void Drain()
    {
        CheckMutationGuard();
        DrainPosted(DefaultMaximumWorkItems);
    }

    /// <summary>Commits at most <paramref name="maximumWorkItems"/> posted completions and scheduled effects.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumWorkItems"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">The graph still has pending work after the limit is reached.</exception>
    public void Drain(int maximumWorkItems)
    {
        CheckMutationGuard();
        DrainPosted(maximumWorkItems);
    }

    internal bool DrainPosted() => DrainPosted(DefaultMaximumWorkItems);

    internal bool DrainPosted(int maximumWorkItems)
    {
        if (maximumWorkItems <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maximumWorkItems),
                "The reactive drain limit must be positive."
            );
        CheckThread();
        if (_batchDepth != 0)
            return false;
        if (_drainState is not null)
            return false;

        var state = new DrainState(maximumWorkItems);
        _drainState = state;

        List<Exception>? errors = null;
        var omittedErrors = 0;
        try
        {
            while (true)
            {
                while (true)
                {
                    if (state!.Processed >= state.Limit)
                    {
                        if (HasPendingWork())
                            ThrowDrainFailures(errors, state.Limit, omittedErrors);
                        break;
                    }
                    if (!TakePosted(out var post))
                        break;
                    state.Processed++;
                    try
                    {
                        state.Posted |= post.Commit();
                    }
                    catch (Exception exception)
                    {
                        AddDrainFailure(ref errors, ref omittedErrors, exception);
                    }
                    if (state.Processed >= state.Limit && HasPendingWork())
                        ThrowDrainFailures(errors, state.Limit, omittedErrors);
                }

                var effect = _effects.First;
                if (effect is null)
                    break;
                if (state.Processed >= state.Limit)
                    ThrowDrainFailures(errors, state.Limit, omittedErrors);
                _effects.RemoveFirst();
                effect.Value.Dequeue();
                state.Processed++;
                if (effect.Value.IsDisposed)
                    continue;
                try
                {
                    effect.Value.Run();
                }
                catch (Exception exception)
                {
                    AddDrainFailure(ref errors, ref omittedErrors, exception);
                }
                if (state.Processed >= state.Limit && HasPendingWork())
                    ThrowDrainFailures(errors, state.Limit, omittedErrors);
            }

            if (errors is { Count: > 0 })
            {
                AddOmittedFailureSummary(errors, omittedErrors);
                throw new AggregateException("Reactive callbacks failed.", errors);
            }
            return state.Posted;
        }
        finally
        {
            _drainState = null;
        }
    }

    private bool HasPendingWork()
    {
        lock (_postedGate)
            return !_posted.IsEmpty || _effects.First is not null;
    }

    private static void ThrowDrainFailures(
        List<Exception>? errors,
        int maximumWorkItems,
        int omittedErrors = 0
    )
    {
        var limit = new InvalidOperationException(
            "Reactive work did not settle within "
                + maximumWorkItems.ToString(CultureInfo.InvariantCulture)
                + " work items. A callback may be scheduling itself repeatedly."
        );
        if (errors is null)
            throw limit;
        AddOmittedFailureSummary(errors, omittedErrors);
        errors.Add(limit);
        throw new AggregateException("Reactive callbacks failed before the drain limit.", errors);
    }

    private static void AddDrainFailure(
        ref List<Exception>? errors,
        ref int omittedErrors,
        Exception error
    )
    {
        if ((errors?.Count ?? 0) < MaximumRetainedDrainFailures)
            (errors ??= []).Add(error);
        else
            omittedErrors = checked(omittedErrors + 1);
    }

    private static void AddOmittedFailureSummary(List<Exception> errors, int omittedErrors)
    {
        if (omittedErrors == 0)
            return;
        errors.Add(
            new InvalidOperationException(
                omittedErrors.ToString(CultureInfo.InvariantCulture)
                    + " additional reactive callback failures were omitted."
            )
        );
    }

    private sealed class DrainState(int limit)
    {
        public int Limit { get; set; } = limit;
        public int Processed { get; set; }
        public bool Posted { get; set; }
    }

    /// <summary>Returns a deterministic snapshot of active graph topology without values or exception messages.</summary>
    public string Dump()
    {
        CheckThread();
        var dump = new StringBuilder("reactive-graph\n");
        foreach (var scope in _scopes.Values.OrderBy(scope => scope.Id))
        {
            dump.Append("scope ")
                .Append(scope.Id.ToString(CultureInfo.InvariantCulture))
                .Append(" name=")
                .Append(Quote(scope.Name))
                .Append(" parent=")
                .Append(scope.Parent?.Id.ToString(CultureInfo.InvariantCulture) ?? "-");
            if (scope.DiagnosticState is { } state)
                dump.Append(" state=").Append(Quote(state));
            dump.Append('\n');
        }
        foreach (var node in _nodes.Values.OrderBy(node => node.Id))
        {
            dump.Append("node ")
                .Append(node.Id.ToString(CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(node.Kind)
                .Append(" name=")
                .Append(Quote(node.Name))
                .Append(" scope=")
                .Append(node.Scope?.Id.ToString(CultureInfo.InvariantCulture) ?? "-")
                .Append(" deps=[")
                .Append(
                    string.Join(
                        ',',
                        node.Dependencies.Select(dependency =>
                            dependency.Id.ToString(CultureInfo.InvariantCulture)
                        )
                    )
                )
                .Append(']');
            node.AppendDump(dump);
            dump.Append('\n');
        }
        return dump.ToString();
    }

    internal int Register(ReactiveNode node)
    {
        var id = ++_nextNodeId;
        _nodes.Add(id, node);
        return id;
    }

    internal int Register(ReactiveScope scope)
    {
        var id = ++_nextScopeId;
        _scopes.Add(id, scope);
        return id;
    }

    internal void Unregister(ReactiveNode node) => _nodes.Remove(node.Id);

    internal void Unregister(ReactiveScope scope) => _scopes.Remove(scope.Id);

    internal void Track(ReactiveNode node)
    {
        CheckThread();
        _collecting?.Add(node);
    }

    internal void RefreshTrackedVersion(ReactiveNode node)
    {
        CheckThread();
        _collecting?.Refresh(node);
    }

    internal void MarkCollectionChanged()
    {
        CheckThread();
        _collecting?.MarkChanged();
    }

    internal T Untracked<T>(Func<T> callback)
    {
        CheckThread();
        ArgumentNullException.ThrowIfNull(callback);
        var prior = _collecting;
        var suspendedPrior = _suspendedCollector;
        _suspendedCollector = prior;
        _collecting = null;
        try
        {
            return callback();
        }
        finally
        {
            _collecting = prior;
            _suspendedCollector = suspendedPrior;
        }
    }

    internal T ResumeTracking<T>(Func<T> callback)
    {
        CheckThread();
        ArgumentNullException.ThrowIfNull(callback);
        if (_collecting is not null || _suspendedCollector is null)
            return callback();
        var prior = _collecting;
        _collecting = _suspendedCollector;
        try
        {
            return callback();
        }
        finally
        {
            _collecting = prior;
        }
    }

    internal T RunMutationGuard<T>(Func<T> callback)
    {
        CheckThread();
        ArgumentNullException.ThrowIfNull(callback);
        _mutationGuardDepth++;
        try
        {
            return callback();
        }
        finally
        {
            _mutationGuardDepth--;
        }
    }

    internal void CheckMutationGuard()
    {
        CheckThread();
        if (_mutationGuardDepth != 0)
            throw new InvalidOperationException(
                "Protected reactive evaluation callbacks cannot mutate reactive state."
            );
    }

    internal Evaluation<T> Evaluate<T>(ReactiveNode node, Func<T> callback)
    {
        CheckThread();
        var cycleStart = _evaluating.IndexOf(node);
        if (cycleStart >= 0)
            throw new ReactiveCycleException(
                _evaluating.Skip(cycleStart).Append(node).Select(current => current.Name)
            );

        var prior = _collecting;
        var collector = _collecting = new ReactiveCollector();
        _evaluating.Add(node);
        try
        {
            var value = callback();
            node.ApplyDependencies(collector, true);
            return new Evaluation<T>(value, collector.Changed());
        }
        catch
        {
            node.ApplyDependencies(collector, false);
            node.EvaluationFailed();
            throw;
        }
        finally
        {
            _evaluating.RemoveAt(_evaluating.Count - 1);
            _collecting = prior;
        }
    }

    internal bool Collect(ReactiveNode node, Action callback)
    {
        var prior = _collecting;
        var collector = _collecting = new ReactiveCollector();
        try
        {
            callback();
            node.ApplyDependencies(collector, true);
            return collector.Changed();
        }
        catch
        {
            node.ApplyDependencies(collector, false);
            if (collector.Changed() && node is ReactiveEffect effect)
                Schedule(effect);
            throw;
        }
        finally
        {
            _collecting = prior;
        }
    }

    internal void Schedule(ReactiveEffect effect)
    {
        if (!effect.IsDisposed && effect.QueueNode is null)
            effect.QueueNode = _effects.AddLast(effect);
    }

    internal void Unschedule(ReactiveEffect effect)
    {
        if (effect.QueueNode is null)
            return;
        _effects.Remove(effect.QueueNode);
        effect.QueueNode = null;
    }

    internal void Post(IPosted post)
    {
        ArgumentNullException.ThrowIfNull(post);
        Action? available = null;
        lock (_postedGate)
        {
            var wasEmpty = _posted.IsEmpty;
            _posted.Enqueue(post);
            if (wasEmpty)
                available = WorkAvailable;
        }
        NotifyWorkAvailable(available);
    }

    private void NotifyWorkAvailable(Action? available)
    {
        while (available is not null)
        {
            List<Exception>? errors = null;
            Action? succeeded = null;
            foreach (Action observer in available.GetInvocationList())
            {
                try
                {
                    observer();
                    succeeded += observer;
                }
                catch (Exception error)
                {
                    (errors ??= []).Add(error);
                }
            }
            if (errors is null)
                return;

            lock (_postedGate)
            {
                var wasEmpty = _posted.IsEmpty;
                foreach (var error in errors)
                    _posted.Enqueue(new WakeFailure(error));
                // A host may already have drained the original work while observers ran.
                // Wake it for the new failure work without invoking a failed observer again.
                available = null;
                if (wasEmpty && succeeded is not null)
                {
                    var subscribed = WorkAvailable?.GetInvocationList() ?? [];
                    foreach (Action observer in succeeded.GetInvocationList())
                        if (subscribed.Contains(observer))
                            available += observer;
                }
            }
        }
    }

    private sealed class WakeFailure(Exception error) : IPosted
    {
        public bool Commit() =>
            throw new AggregateException("WorkAvailable observer failed.", error);
    }

    private bool TakePosted(out IPosted post)
    {
        lock (_postedGate)
            return _posted.TryDequeue(out post!);
    }

    internal void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _uiThread)
            throw new InvalidOperationException(
                "Reactive graph access must occur on its owning UI thread."
            );
    }

    internal static void ValidateName(string name, string parameter)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A reactive name is required.", parameter);
    }

    internal static void ThrowCombined(Exception? first, Exception? second, string message)
    {
        if (first is null && second is null)
            return;
        if (first is null)
        {
            ExceptionDispatchInfo.Capture(second!).Throw();
            return;
        }
        if (second is null)
        {
            ExceptionDispatchInfo.Capture(first).Throw();
            return;
        }
        throw new AggregateException(message, first, second);
    }

    private static string Quote(string value) =>
        '"'
        + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
        + '"';
}
