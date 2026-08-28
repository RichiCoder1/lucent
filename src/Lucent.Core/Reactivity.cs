using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Lucent.Core;

/// <summary>A UI-thread-owned reactive graph for all Lucent authoring surfaces.</summary>
public sealed class ReactiveGraph
{
    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    private readonly List<ReactiveNode> _nodes = [];
    private readonly List<ReactiveScope> _scopes = [];
    private readonly LinkedList<ReactiveEffect> _effects = [];
    private readonly ConcurrentQueue<IPosted> _posted = new();
    private readonly List<ReactiveNode> _evaluating = [];
    private ReactiveCollector? _collecting;
    private int _batchDepth;
    private int _nextNodeId;
    private int _nextScopeId;

    public ReactiveScope CreateScope(string name)
    {
        CheckThread();
        ValidateName(name, nameof(name));
        return new ReactiveScope(this, null, name);
    }

    public Signal<T> Signal<T>(T value, string name)
    {
        CheckThread();
        ValidateName(name, nameof(name));
        return new Signal<T>(this, value, name, null);
    }

    public Derived<T> Derived<T>(Func<T> compute, string name)
    {
        CheckThread();
        ArgumentNullException.ThrowIfNull(compute);
        ValidateName(name, nameof(name));
        return new Derived<T>(this, compute, name, null);
    }

    public ReactiveEffect Effect(Action callback, string name)
    {
        CheckThread();
        ArgumentNullException.ThrowIfNull(callback);
        ValidateName(name, nameof(name));
        return new ReactiveEffect(this, callback, name, null);
    }

    public AsyncValue<T> Async<T>(Func<CancellationToken, Task<T>> load, string name)
    {
        CheckThread();
        ArgumentNullException.ThrowIfNull(load);
        ValidateName(name, nameof(name));
        return new AsyncValue<T>(this, load, default!, false, name, null);
    }

    public AsyncValue<T> Async<T>(Func<CancellationToken, Task<T>> load, T staleValue, string name)
    {
        CheckThread();
        ArgumentNullException.ThrowIfNull(load);
        ValidateName(name, nameof(name));
        return new AsyncValue<T>(this, load, staleValue, true, name, null);
    }

    /// <summary>Defers effect execution until the outermost batch exits.</summary>
    public void Batch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        CheckThread();
        _batchDepth++;
        Exception? bodyError = null;
        try { action(); }
        catch (Exception exception) { bodyError = exception; }
        finally { _batchDepth--; }

        if (_batchDepth != 0)
        {
            if (bodyError is not null) ExceptionDispatchInfo.Capture(bodyError).Throw();
            return;
        }

        Exception? drainError = null;
        try { Drain(); }
        catch (Exception exception) { drainError = exception; }
        ThrowCombined(bodyError, drainError, "Reactive batch failed.");
    }

    /// <summary>Commits posted async completions and scheduled effects on the owning UI thread.</summary>
    public void Drain()
    {
        CheckThread();
        if (_batchDepth != 0) return;

        List<Exception>? errors = null;
        while (true)
        {
            while (_posted.TryDequeue(out var post))
            {
                try { post.Commit(); }
                catch (Exception exception) { (errors ??= []).Add(exception); }
            }

            var effect = _effects.First;
            if (effect is null) break;
            _effects.RemoveFirst();
            effect.Value.Dequeue();
            if (effect.Value.IsDisposed) continue;
            try { effect.Value.Run(); }
            catch (Exception exception) { (errors ??= []).Add(exception); }
        }

        if (errors is { Count: > 0 }) throw new AggregateException("Reactive callbacks failed.", errors);
    }

    /// <summary>Returns a deterministic snapshot of active graph topology without values or exception messages.</summary>
    public string Dump()
    {
        CheckThread();
        var dump = new StringBuilder("reactive-graph\n");
        foreach (var scope in _scopes.OrderBy(scope => scope.Id))
            dump.Append("scope ").Append(scope.Id.ToString(CultureInfo.InvariantCulture)).Append(" name=").Append(Quote(scope.Name))
                .Append(" parent=").Append(scope.Parent?.Id.ToString(CultureInfo.InvariantCulture) ?? "-").Append('\n');
        foreach (var node in _nodes.OrderBy(node => node.Id))
        {
            dump.Append("node ").Append(node.Id.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(node.Kind)
                .Append(" name=").Append(Quote(node.Name)).Append(" scope=")
                .Append(node.Scope?.Id.ToString(CultureInfo.InvariantCulture) ?? "-")
                .Append(" deps=[").Append(string.Join(',', node.Dependencies.Select(dependency => dependency.Id.ToString(CultureInfo.InvariantCulture)))).Append(']');
            node.AppendDump(dump);
            dump.Append('\n');
        }
        return dump.ToString();
    }

    internal int Register(ReactiveNode node) { _nodes.Add(node); return ++_nextNodeId; }
    internal int Register(ReactiveScope scope) { _scopes.Add(scope); return ++_nextScopeId; }
    internal void Unregister(ReactiveNode node) => _nodes.Remove(node);
    internal void Unregister(ReactiveScope scope) => _scopes.Remove(scope);

    internal void Track(ReactiveNode node)
    {
        CheckThread();
        _collecting?.Add(node);
    }

    internal Evaluation<T> Evaluate<T>(ReactiveNode node, Func<T> callback)
    {
        CheckThread();
        var cycleStart = _evaluating.IndexOf(node);
        if (cycleStart >= 0)
            throw new ReactiveCycleException(_evaluating.Skip(cycleStart).Append(node).Select(current => current.Name));

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
            if (collector.Changed() && node is ReactiveEffect effect) Schedule(effect);
            throw;
        }
        finally { _collecting = prior; }
    }

    internal void Schedule(ReactiveEffect effect)
    {
        if (!effect.IsDisposed && effect.QueueNode is null) effect.QueueNode = _effects.AddLast(effect);
    }

    internal void Unschedule(ReactiveEffect effect)
    {
        if (effect.QueueNode is null) return;
        _effects.Remove(effect.QueueNode);
        effect.QueueNode = null;
    }

    internal void Post(IPosted post) => _posted.Enqueue(post);

    internal void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _uiThread)
            throw new InvalidOperationException("Reactive graph access must occur on its owning UI thread.");
    }

    internal static void ValidateName(string name, string parameter)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A reactive name is required.", parameter);
    }

    internal static void ThrowCombined(Exception? first, Exception? second, string message)
    {
        if (first is null && second is null) return;
        if (first is null) { ExceptionDispatchInfo.Capture(second!).Throw(); return; }
        if (second is null) { ExceptionDispatchInfo.Capture(first).Throw(); return; }
        throw new AggregateException(message, first, second);
    }

    private static string Quote(string value) => '"' + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + '"';
}

internal interface IPosted { void Commit(); }
internal readonly record struct Evaluation<T>(T Value, bool ChangedDuringRun);

internal sealed class ReactiveCollector
{
    private readonly List<(ReactiveNode Node, long Version)> _reads = [];
    internal IEnumerable<ReactiveNode> Nodes => _reads.Select(read => read.Node);
    internal void Add(ReactiveNode node)
    {
        if (_reads.All(read => !ReferenceEquals(read.Node, node))) _reads.Add((node, node.Version));
    }
    internal bool Changed() => _reads.Any(read => read.Node.Version != read.Version);
}

/// <summary>Reports a reactive evaluation cycle using author-provided node names.</summary>
public sealed class ReactiveCycleException : InvalidOperationException
{
    internal ReactiveCycleException(IEnumerable<string> names) : base("Reactive cycle: " + string.Join(" -> ", names)) { }
}

/// <summary>Base type for graph-owned state; disposal removes all dependency links and graph registration.</summary>
public abstract class ReactiveNode : IDisposable
{
    private readonly List<ReactiveNode> _dependencies = [];
    private readonly List<ReactiveNode> _dependents = [];
    private ReactiveScope? _scope;

    internal ReactiveNode(ReactiveGraph graph, string name, ReactiveScope? scope)
    {
        Graph = graph;
        ReactiveGraph.ValidateName(name, nameof(name));
        Name = name;
        _scope = scope;
        Id = graph.Register(this);
    }

    public int Id { get; }
    public string Name { get; }
    public bool IsDisposed { get; private set; }
    internal long Version { get; private set; }
    internal ReactiveGraph Graph { get; }
    internal ReactiveScope? Scope => _scope;
    internal IReadOnlyList<ReactiveNode> Dependencies => _dependencies;
    internal abstract string Kind { get; }

    protected void Read()
    {
        ThrowIfDisposed();
        Graph.Track(this);
    }

    internal void ApplyDependencies(ReactiveCollector collector, bool succeeded)
    {
        if (IsDisposed) return;
        var next = succeeded ? collector.Nodes : _dependencies.Concat(collector.Nodes);
        ReplaceDependencies(next.Distinct().ToArray());
    }

    private void ReplaceDependencies(ReactiveNode[] next)
    {
        foreach (var dependency in _dependencies.Where(dependency => !next.Contains(dependency)).ToArray()) dependency._dependents.Remove(this);
        foreach (var dependency in next.Where(dependency => !ReferenceEquals(dependency, this) && !_dependencies.Contains(dependency))) dependency._dependents.Add(this);
        _dependencies.Clear();
        _dependencies.AddRange(next);
    }

    protected void Changed()
    {
        Version++;
        List<Exception>? errors = null;
        foreach (var dependent in _dependents.OrderBy(dependent => dependent.Id).ToArray())
        {
            try { dependent.DependencyChanged(); }
            catch (Exception exception) { (errors ??= []).Add(exception); }
        }
        if (errors is { Count: > 0 }) throw new AggregateException("Reactive invalidation failed.", errors);
    }

    internal virtual void DependencyChanged() { }
    internal virtual void EvaluationFailed() { }
    internal virtual void AppendDump(StringBuilder dump) { }
    protected void ThrowIfDisposed()
    {
        if (IsDisposed) throw new ObjectDisposedException(Name);
    }

    public virtual void Dispose()
    {
        Graph.CheckThread();
        if (IsDisposed) return;
        IsDisposed = true;
        List<Exception>? errors = null;
        try
        {
            ReplaceDependencies([]);
            foreach (var dependent in _dependents.OrderBy(dependent => dependent.Id).ToArray())
            {
                dependent.RemoveDependency(this);
                try { dependent.DependencyChanged(); }
                catch (Exception exception) { (errors ??= []).Add(exception); }
            }
            _dependents.Clear();
        }
        finally
        {
            _scope?.Detach(this);
            _scope = null;
            Graph.Unregister(this);
        }
        if (errors is { Count: > 0 }) throw new AggregateException("Reactive node disposal failed.", errors);
    }

    private void RemoveDependency(ReactiveNode dependency) => _dependencies.Remove(dependency);
}

/// <summary>Writable graph state.</summary>
public sealed class Signal<T> : ReactiveNode
{
    private T _value;
    internal Signal(ReactiveGraph graph, T value, string name, ReactiveScope? scope) : base(graph, name, scope) { _value = value; }
    internal override string Kind => "signal";

    public T Value
    {
        get { Graph.CheckThread(); Read(); return _value; }
        set
        {
            Graph.CheckThread();
            ThrowIfDisposed();
            if (EqualityComparer<T>.Default.Equals(_value, value)) return;
            _value = value;
            Changed();
        }
    }

    public override void Dispose() { Graph.CheckThread(); _value = default!; base.Dispose(); }
}

/// <summary>A lazy, memoized value with runtime-tracked dependencies.</summary>
public sealed class Derived<T> : ReactiveNode
{
    private Func<T>? _compute;
    private T? _value;
    private bool _dirty = true;
    private bool _failed;

    internal Derived(ReactiveGraph graph, Func<T> compute, string name, ReactiveScope? scope) : base(graph, name, scope) { _compute = compute; }
    internal override string Kind => "derived";

    public T Value
    {
        get
        {
            Graph.CheckThread();
            Read();
            if (_dirty)
            {
                var evaluation = Graph.Evaluate(this, _compute!);
                _value = evaluation.Value;
                _dirty = false;
                _failed = false;
                if (evaluation.ChangedDuringRun) DependencyChanged();
            }
            return _value!;
        }
    }

    internal override void DependencyChanged()
    {
        if (IsDisposed) return;
        var notify = !_dirty || _failed;
        _dirty = true;
        _failed = false;
        if (notify) Changed();
    }

    internal override void EvaluationFailed() => _failed = true;

    internal override void AppendDump(StringBuilder dump) => dump.Append(" dirty=").Append(_dirty ? "true" : "false");
    public override void Dispose() { Graph.CheckThread(); _compute = null; _value = default; base.Dispose(); }
}

/// <summary>An explicitly scheduled reactive callback. Effects are the graph's only subscriber mechanism.</summary>
public sealed class ReactiveEffect : ReactiveNode
{
    private Action? _callback;
    internal ReactiveEffect(ReactiveGraph graph, Action callback, string name, ReactiveScope? scope) : base(graph, name, scope)
    {
        _callback = callback;
        graph.Schedule(this);
    }

    internal LinkedListNode<ReactiveEffect>? QueueNode { get; set; }
    internal override string Kind => "effect";
    internal override void DependencyChanged() => Graph.Schedule(this);
    internal void Run()
    {
        var changed = Graph.Collect(this, _callback!);
        if (changed) Graph.Schedule(this);
    }

    internal void Dequeue() => QueueNode = null;
    public override void Dispose()
    {
        Graph.CheckThread();
        Graph.Unschedule(this);
        _callback = null;
        base.Dispose();
    }
}

/// <summary>Latest-generation asynchronous state. Cancellation releases Lucent-owned resources; an uncooperative producer may still retain its own task closure until it completes.</summary>
public sealed class AsyncValue<T> : ReactiveNode
{
    private Func<CancellationToken, Task<T>>? _load;
    private AsyncLease<T>? _lease;
    private T? _value;
    private Exception? _error;
    private bool _dirty = true;
    private bool _pending;
    private bool _cancelled;
    private bool _hasValue;
    private long _generation;

    internal AsyncValue(ReactiveGraph graph, Func<CancellationToken, Task<T>> load, T? staleValue, bool hasValue, string name, ReactiveScope? scope) : base(graph, name, scope)
    {
        _load = load;
        _value = staleValue;
        _hasValue = hasValue;
    }

    internal override string Kind => "async";
    public T? Value { get { Graph.CheckThread(); Read(); EnsureStarted(); return _value; } }
    public bool HasValue { get { Graph.CheckThread(); Read(); EnsureStarted(); return _hasValue; } }
    public bool IsPending { get { Graph.CheckThread(); Read(); EnsureStarted(); return _pending; } }
    public bool IsCancelled { get { Graph.CheckThread(); Read(); EnsureStarted(); return _cancelled; } }
    public Exception? Error { get { Graph.CheckThread(); Read(); EnsureStarted(); return _error; } }

    private void EnsureStarted()
    {
        if (!_dirty) return;
        Exception? stopError = StopCurrent();
        var lease = _lease = new AsyncLease<T>(this, ++_generation);
        _dirty = false;
        _pending = true;
        _cancelled = false;
        _error = null;
        Evaluation<Task<T>> evaluation;
        try
        {
            evaluation = Graph.Evaluate(this, () => _load!(lease.Token));
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(lease, _lease))
            {
                ReactiveGraph.ThrowCombined(stopError, exception, "Async start was invalidated.");
                return;
            }
            Exception? completeError = null;
            try { Complete(lease, default, exception, false); }
            catch (Exception completionException) { completeError = completionException; }
            ReactiveGraph.ThrowCombined(stopError, completeError, "Async start failed.");
            return;
        }

        if (evaluation.Value is null)
        {
            Exception? completeError = null;
            try { Complete(lease, default, new InvalidOperationException("Async loader returned null task."), false); }
            catch (Exception completionException) { completeError = completionException; }
            ReactiveGraph.ThrowCombined(stopError, completeError, "Async start failed.");
            return;
        }

        Exception? transitionError = null;
        try
        {
            evaluation.Value.ContinueWith(completed => lease.Post(completed), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            if (evaluation.ChangedDuringRun) DependencyChanged();
        }
        catch (Exception exception) { transitionError = exception; }
        ReactiveGraph.ThrowCombined(stopError, transitionError, "Async transition failed.");
    }

    internal void Complete(AsyncLease<T> lease, T? value, Exception? error, bool cancelled)
    {
        Graph.CheckThread();
        if (IsDisposed || !ReferenceEquals(lease, _lease)) { lease.ReleasePosted(); return; }
        _lease = null;
        var releaseError = lease.Finish();
        _pending = false;
        if (cancelled) _cancelled = true;
        else if (error is null) { _value = value; _hasValue = true; }
        else _error = error;
        Exception? changedError = null;
        try { Changed(); }
        catch (Exception exception) { changedError = exception; }
        ReactiveGraph.ThrowCombined(releaseError, changedError, "Async completion failed.");
    }

    internal override void DependencyChanged()
    {
        if (IsDisposed) return;
        _dirty = true;
        _pending = false;
        _cancelled = true;
        var error = StopCurrent();
        Exception? changedError = null;
        try { Changed(); }
        catch (Exception exception) { changedError = exception; }
        ReactiveGraph.ThrowCombined(error, changedError, "Async invalidation failed.");
    }

    public override void Dispose()
    {
        Graph.CheckThread();
        if (IsDisposed) return;
        var error = StopCurrent();
        _load = null;
        _value = default;
        _error = null;
        _pending = false;
        _hasValue = false;
        try { base.Dispose(); }
        catch (Exception exception) { ReactiveGraph.ThrowCombined(error, exception, "Async disposal failed."); return; }
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    internal override void AppendDump(StringBuilder dump) => dump.Append(" dirty=").Append(_dirty ? "true" : "false")
        .Append(" generation=").Append(_generation.ToString(CultureInfo.InvariantCulture)).Append(" pending=").Append(_pending ? "true" : "false")
        .Append(" cancelled=").Append(_cancelled ? "true" : "false").Append(" hasValue=").Append(_hasValue ? "true" : "false")
        .Append(" hasError=").Append(_error is null ? "false" : "true");

    private Exception? StopCurrent()
    {
        var lease = _lease;
        _lease = null;
        return lease?.Cancel();
    }
}

internal sealed class AsyncLease<T>
{
    private readonly object _gate = new();
    private AsyncValue<T>? _owner;
    private CancellationTokenSource? _cancellation = new();
    private AsyncPosted<T>? _posted;

    internal AsyncLease(AsyncValue<T> owner, long generation) { _owner = owner; Generation = generation; }
    internal long Generation { get; }
    internal CancellationToken Token => _cancellation?.Token ?? CancellationToken.None;

    internal void Post(Task<T> task)
    {
        AsyncPosted<T>? posted;
        ReactiveGraph graph;
        lock (_gate)
        {
            if (_owner is null) return;
            graph = _owner.Graph;
            var cancelled = task.IsCanceled;
            var error = cancelled ? null : task.Exception?.GetBaseException();
            var value = task.Status == TaskStatus.RanToCompletion ? task.Result : default;
            posted = _posted = new AsyncPosted<T>(this, value, error, cancelled);
        }
        graph.Post(posted);
    }

    internal Exception? Cancel()
    {
        CancellationTokenSource? cancellation;
        AsyncPosted<T>? posted;
        lock (_gate)
        {
            _owner = null;
            cancellation = _cancellation;
            _cancellation = null;
            posted = _posted;
            _posted = null;
        }
        posted?.Release();
        return CancelAndDispose(cancellation);
    }

    internal Exception? Finish()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            _owner = null;
            cancellation = _cancellation;
            _cancellation = null;
            _posted = null;
        }
        if (cancellation is null) return null;
        try { cancellation.Dispose(); return null; }
        catch (Exception exception) { return exception; }
    }

    internal void Commit(AsyncPosted<T> posted, T? value, Exception? error, bool cancelled)
    {
        AsyncValue<T>? owner;
        lock (_gate)
        {
            if (!ReferenceEquals(_posted, posted)) { posted.Release(); return; }
            _posted = null;
            owner = _owner;
        }
        if (owner is null) { posted.Release(); return; }
        owner.Complete(this, value, error, cancelled);
        posted.Release();
    }

    internal void ReleasePosted()
    {
        lock (_gate)
        {
            _posted?.Release();
            _posted = null;
        }
    }

    private static Exception? CancelAndDispose(CancellationTokenSource? cancellation)
    {
        if (cancellation is null) return null;
        Exception? cancelError = null;
        try { cancellation.Cancel(); }
        catch (Exception exception) { cancelError = exception; }
        try { cancellation.Dispose(); }
        catch (Exception exception) { return cancelError is null ? exception : new AggregateException("Async cancellation failed.", cancelError, exception); }
        return cancelError;
    }
}

internal sealed class AsyncPosted<T>(AsyncLease<T> lease, T? value, Exception? error, bool cancelled) : IPosted
{
    private AsyncLease<T>? _lease = lease;
    private T? _value = value;
    private Exception? _error = error;
    private readonly bool _cancelled = cancelled;

    public void Commit()
    {
        var lease = Interlocked.Exchange(ref _lease, null);
        if (lease is null) return;
        var value = _value;
        var error = _error;
        _value = default;
        _error = null;
        lease.Commit(this, value, error, _cancelled);
    }

    internal void Release()
    {
        Interlocked.Exchange(ref _lease, null);
        _value = default;
        _error = null;
    }
}

/// <summary>Hierarchical lifetime ownership for graph nodes, subscriptions, and cleanup.</summary>
public sealed class ReactiveScope : IDisposable
{
    private readonly ReactiveGraph _graph;
    private readonly List<IDisposable> _owned = [];
    private ReactiveScope? _parent;

    internal ReactiveScope(ReactiveGraph graph, ReactiveScope? parent, string name)
    {
        ReactiveGraph.ValidateName(name, nameof(name));
        _graph = graph;
        _parent = parent;
        Name = name;
        Id = graph.Register(this);
        parent?._owned.Add(this);
    }

    public int Id { get; }
    public string Name { get; }
    public ReactiveScope? Parent => _parent;
    public bool IsDisposed { get; private set; }
    public ReactiveScope CreateChild(string name) { CheckActive(); ReactiveGraph.ValidateName(name, nameof(name)); return new ReactiveScope(_graph, this, name); }
    public Signal<T> Signal<T>(T value, string name) { CheckActive(); ReactiveGraph.ValidateName(name, nameof(name)); return Own(new Signal<T>(_graph, value, name, this)); }
    public Derived<T> Derived<T>(Func<T> compute, string name) { CheckActive(); ArgumentNullException.ThrowIfNull(compute); ReactiveGraph.ValidateName(name, nameof(name)); return Own(new Derived<T>(_graph, compute, name, this)); }
    public ReactiveEffect Effect(Action callback, string name) { CheckActive(); ArgumentNullException.ThrowIfNull(callback); ReactiveGraph.ValidateName(name, nameof(name)); return Own(new ReactiveEffect(_graph, callback, name, this)); }
    public AsyncValue<T> Async<T>(Func<CancellationToken, Task<T>> load, string name) { CheckActive(); ArgumentNullException.ThrowIfNull(load); ReactiveGraph.ValidateName(name, nameof(name)); return Own(new AsyncValue<T>(_graph, load, default!, false, name, this)); }
    public AsyncValue<T> Async<T>(Func<CancellationToken, Task<T>> load, T staleValue, string name) { CheckActive(); ArgumentNullException.ThrowIfNull(load); ReactiveGraph.ValidateName(name, nameof(name)); return Own(new AsyncValue<T>(_graph, load, staleValue, true, name, this)); }
    public T Own<T>(T value) where T : IDisposable { CheckActive(); ArgumentNullException.ThrowIfNull(value); _owned.Add(value); return value; }
    public void OnDispose(Action cleanup) { ArgumentNullException.ThrowIfNull(cleanup); Own(new Cleanup(cleanup)); }

    public void Dispose()
    {
        _graph.CheckThread();
        if (IsDisposed) return;
        IsDisposed = true;
        var owned = _owned.ToArray();
        _owned.Clear();
        List<Exception>? errors = null;
        for (var index = owned.Length - 1; index >= 0; index--)
        {
            try { owned[index].Dispose(); }
            catch (Exception exception) { (errors ??= []).Add(exception); }
        }
        _parent?.Detach(this);
        _parent = null;
        _graph.Unregister(this);
        if (errors is { Count: > 0 }) throw new AggregateException("Reactive scope cleanup failed.", errors);
    }

    internal void Detach(IDisposable value) => _owned.Remove(value);

    private void CheckActive()
    {
        _graph.CheckThread();
        if (IsDisposed) throw new ObjectDisposedException(Name);
    }

    private sealed class Cleanup(Action callback) : IDisposable
    {
        private Action? _callback = callback;
        public void Dispose() => Interlocked.Exchange(ref _callback, null)?.Invoke();
    }
}
