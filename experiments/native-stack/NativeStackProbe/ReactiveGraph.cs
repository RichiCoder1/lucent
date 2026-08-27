using System.Collections.Concurrent;

/// <summary>Small UI-thread reactive graph. Runtime dependency collection is intentionally capped at 64 reads per callback.</summary>
internal sealed class ReactiveGraph
{
    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    private readonly Queue<ReactiveEffect> _effects = [];
    private readonly HashSet<ReactiveEffect> _scheduled = [];
    private readonly ConcurrentQueue<Action> _posted = new();
    private List<ReactiveNode>? _collecting;
    private readonly List<ReactiveComputedBase> _evaluating = [];
    private int _batch;

    public ReactiveScope Scope() { CheckThread(); return new(this); }
    public ReactiveSignal<T> Signal<T>(T value, string name) { CheckThread(); return new(this, value, name); }
    public ReactiveComputed<T> Computed<T>(Func<T> compute, string name) { CheckThread(); return new(this, compute, name); }
    public ReactiveAsyncComputed<T> AsyncComputed<T>(Func<CancellationToken, Task<T>> compute, string name) { CheckThread(); return new(this, compute, name); }
    public ReactiveEffect Effect(Action run, string name) { CheckThread(); return new(this, run, name); }

    /// <summary>Compiler seam: declare stable edges without running a callback's runtime read tracker.</summary>
    public void RegisterDependencies(ReactiveNode target, params ReactiveNode[] dependencies)
    {
        CheckThread();
        ArgumentNullException.ThrowIfNull(target);
        if (target.Graph != this || dependencies.Any(dependency => dependency is null || dependency.Graph != this)) throw new ArgumentException("Dependencies must belong to this graph.");
        target.SetDeclaredDependencies(dependencies.Distinct().ToArray());
    }

    public void Batch(Action action)
    {
        CheckThread();
        _batch++;
        try { action(); }
        finally { if (--_batch == 0) Drain(); }
    }

    public void Drain()
    {
        CheckThread();
        if (_batch != 0) return;
        while (_posted.TryDequeue(out var post)) post();
        while (_effects.Count != 0)
        {
            var effect = _effects.Dequeue();
            _scheduled.Remove(effect);
            if (!effect.Disposed) effect.Run();
            while (_posted.TryDequeue(out var post)) post();
        }
    }

    internal void Track(ReactiveNode node)
    {
        CheckThread();
        if (_collecting is not null && !_collecting.Contains(node))
        {
            if (_collecting.Count == 64) throw new InvalidOperationException("Reactive callback exceeded its 64 dependency limit.");
            _collecting.Add(node);
        }
    }

    internal T Evaluate<T>(ReactiveComputedBase computed, Func<T> compute)
    {
        CheckThread();
        if (computed.Evaluating)
        {
            var first = _evaluating.IndexOf(computed);
            throw new InvalidOperationException("Reactive cycle: " + string.Join(" -> ", _evaluating.Skip(first).Select(node => node.Name).Append(computed.Name)));
        }
        var previous = _collecting;
        _collecting = [];
        computed.Evaluating = true;
        _evaluating.Add(computed);
        try
        {
            var value = compute();
            computed.SetRuntimeDependencies(_collecting);
            return value;
        }
        finally
        {
            _evaluating.RemoveAt(_evaluating.Count - 1);
            computed.Evaluating = false;
            _collecting = previous;
        }
    }

    internal void Collect(Action action, ReactiveNode node)
    {
        CheckThread();
        var previous = _collecting;
        _collecting = [];
        try { action(); node.SetRuntimeDependencies(_collecting); }
        finally { _collecting = previous; }
    }

    internal void Schedule(ReactiveEffect effect)
    {
        if (effect.Disposed || !_scheduled.Add(effect)) return;
        _effects.Enqueue(effect);
    }

    internal void Post(Action action) => _posted.Enqueue(action);
    internal void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _uiThread) throw new InvalidOperationException("Reactive graph access must occur on its UI thread.");
    }
}

internal abstract class ReactiveNode(ReactiveGraph graph, string name) : IDisposable
{
    private ReactiveNode[] _declared = [];
    private ReactiveNode[] _runtime = [];
    private readonly HashSet<ReactiveNode> _dependents = [];
    public ReactiveGraph Graph { get; } = graph;
    public string Name { get; } = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Reactive names are required.", nameof(name)) : name;
    public bool Disposed { get; private set; }

    internal void SetDeclaredDependencies(ReactiveNode[] dependencies) { Replace(ref _declared, dependencies); }
    internal void SetRuntimeDependencies(List<ReactiveNode> dependencies) { Replace(ref _runtime, dependencies.ToArray()); }
    private void Replace(ref ReactiveNode[] old, ReactiveNode[] next)
    {
        var before = _declared.Concat(_runtime).Distinct().ToArray();
        old = next;
        var after = _declared.Concat(_runtime).Distinct().ToArray();
        foreach (var dependency in before.Where(dependency => !after.Contains(dependency))) dependency._dependents.Remove(this);
        foreach (var dependency in after.Where(dependency => !before.Contains(dependency))) dependency._dependents.Add(this);
    }
    protected void Read() { Graph.Track(this); }
    protected void Changed() { foreach (var dependent in _dependents.ToArray()) dependent.DependencyChanged(); }
    internal virtual void DependencyChanged() { }
    public virtual void Dispose()
    {
        if (Disposed) return;
        Graph.CheckThread();
        Disposed = true;
        Replace(ref _declared, []); Replace(ref _runtime, []);
        foreach (var dependent in _dependents.ToArray()) dependent.DependencyChanged();
        _dependents.Clear();
    }
}

internal sealed class ReactiveSignal<T>(ReactiveGraph graph, T value, string name) : ReactiveNode(graph, name)
{
    private T _value = value;
    public T Value { get { Graph.CheckThread(); Read(); return _value; } set { Graph.CheckThread(); if (EqualityComparer<T>.Default.Equals(_value, value)) return; _value = value; Changed(); } }
}

internal abstract class ReactiveComputedBase(ReactiveGraph graph, string name) : ReactiveNode(graph, name)
{
    internal bool Dirty { get; set; } = true;
    internal bool Evaluating { get; set; }
    internal override void DependencyChanged()
    {
        if (Dirty || Disposed) return;
        Dirty = true;
        Changed();
    }
}

internal sealed class ReactiveComputed<T>(ReactiveGraph graph, Func<T> compute, string name) : ReactiveComputedBase(graph, name)
{
    private readonly Func<T> _compute = compute;
    private T? _value;
    public T Value
    {
        get
        {
            Graph.CheckThread(); Read();
            if (Dirty) { _value = Graph.Evaluate(this, _compute); Dirty = false; }
            return _value!;
        }
    }
}

internal sealed class ReactiveEffect : ReactiveNode
{
    private readonly Action _run;
    internal ReactiveEffect(ReactiveGraph graph, Action run, string name) : base(graph, name) { _run = run; graph.Schedule(this); }
    internal override void DependencyChanged() => Graph.Schedule(this);
    internal void Run() => Graph.Collect(_run, this);
    public override void Dispose() { base.Dispose(); }
}

internal sealed class ReactiveAsyncComputed<T> : ReactiveComputedBase
{
    private readonly Func<CancellationToken, Task<T>> _compute;
    private CancellationTokenSource? _cancellation;
    private int _generation;
    private bool _started;
    private T? _value;
    private Exception? _error;
    public ReactiveAsyncComputed(ReactiveGraph graph, Func<CancellationToken, Task<T>> compute, string name) : base(graph, name) => _compute = compute;
    public T? Value { get { Graph.CheckThread(); Read(); EnsureStarted(); return _value; } }
    public bool Pending { get { Graph.CheckThread(); Read(); EnsureStarted(); return _cancellation is not null; } }
    public Exception? Error { get { Graph.CheckThread(); Read(); EnsureStarted(); return _error; } }
    private void EnsureStarted()
    {
        if (!Dirty && _started) return;
        _cancellation?.Cancel(); _cancellation?.Dispose();
        var cancellation = _cancellation = new CancellationTokenSource();
        var generation = ++_generation;
        Dirty = false; _started = true; _error = null;
        Task<T> task;
        try { task = Graph.Evaluate(this, () => _compute(cancellation.Token)); }
        catch (Exception exception) { Complete(generation, cancellation, default, exception); return; }
        _ = task.ContinueWith(completed => Graph.Post(() => Complete(generation, cancellation, completed.Status == TaskStatus.RanToCompletion ? completed.Result : default, completed.IsCanceled ? null : completed.Exception?.GetBaseException())), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    private void Complete(int generation, CancellationTokenSource cancellation, T? value, Exception? error)
    {
        Graph.CheckThread();
        if (Disposed || generation != _generation || cancellation.IsCancellationRequested) return;
        _cancellation = null; cancellation.Dispose();
        if (error is null) _value = value; else _error = error;
        Changed();
    }
    internal override void DependencyChanged()
    {
        if (Disposed) return;
        Dirty = true; _cancellation?.Cancel();
        Changed();
    }
    public override void Dispose() { _cancellation?.Cancel(); _cancellation?.Dispose(); _cancellation = null; base.Dispose(); }
}

internal sealed class ReactiveScope(ReactiveGraph graph) : IDisposable
{
    private readonly List<IDisposable> _owned = [];
    public ReactiveSignal<T> Signal<T>(T value, string name) => Own(graph.Signal(value, name));
    public ReactiveComputed<T> Computed<T>(Func<T> compute, string name) => Own(graph.Computed(compute, name));
    public ReactiveAsyncComputed<T> AsyncComputed<T>(Func<CancellationToken, Task<T>> compute, string name) => Own(graph.AsyncComputed(compute, name));
    public ReactiveEffect Effect(Action run, string name) => Own(graph.Effect(run, name));
    public T Own<T>(T value) where T : IDisposable { _owned.Add(value); return value; }
    public void Dispose() { foreach (var value in _owned.AsEnumerable().Reverse()) value.Dispose(); _owned.Clear(); }
}
