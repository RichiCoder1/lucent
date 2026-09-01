using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Lucent.Core;

internal interface IPosted
{
    bool Commit();
}

internal readonly record struct Evaluation<T>(T Value, bool ChangedDuringRun);

internal sealed class ReactiveCollector
{
    private readonly List<(ReactiveNode Node, long Version)> _reads = [];
    internal IEnumerable<ReactiveNode> Nodes => _reads.Select(read => read.Node);

    internal void Add(ReactiveNode node)
    {
        if (_reads.All(read => !ReferenceEquals(read.Node, node)))
            _reads.Add((node, node.Version));
    }

    internal bool Changed() => _reads.Any(read => read.Node.Version != read.Version);
}

/// <summary>Reports a reactive evaluation cycle using author-provided node names.</summary>
public sealed class ReactiveCycleException : InvalidOperationException
{
    internal ReactiveCycleException(IEnumerable<string> names)
        : base("Reactive cycle: " + string.Join(" -> ", names)) { }
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

    /// <summary>Gets the stable identifier assigned at creation.</summary>
    public int Id { get; }

    /// <summary>Gets the diagnostic name assigned at creation.</summary>
    public string Name { get; }

    /// <summary>Gets whether this retained owner has released its children and reactive resources.</summary>
    public bool IsDisposed { get; private set; }
    internal long Version { get; private set; }
    internal ReactiveGraph Graph { get; }
    internal ReactiveScope? Scope => _scope;
    internal IReadOnlyList<ReactiveNode> Dependencies => _dependencies;
    internal abstract string Kind { get; }

    /// <summary>Records a dependency when a reactive value is read.</summary>
    protected void Read()
    {
        ThrowIfDisposed();
        Graph.Track(this);
    }

    internal void ApplyDependencies(ReactiveCollector collector, bool succeeded)
    {
        if (IsDisposed)
            return;
        var next = succeeded ? collector.Nodes : _dependencies.Concat(collector.Nodes);
        ReplaceDependencies(next.Distinct().ToArray());
    }

    private void ReplaceDependencies(ReactiveNode[] next)
    {
        foreach (
            var dependency in _dependencies
                .Where(dependency => !next.Contains(dependency))
                .ToArray()
        )
            dependency._dependents.Remove(this);
        foreach (
            var dependency in next.Where(dependency =>
                !ReferenceEquals(dependency, this) && !_dependencies.Contains(dependency)
            )
        )
            dependency._dependents.Add(this);
        _dependencies.Clear();
        _dependencies.AddRange(next);
    }

    /// <summary>Invalidates dependents after this node changes.</summary>
    protected void Changed()
    {
        Version++;
        List<Exception>? errors = null;
        foreach (var dependent in _dependents.OrderBy(dependent => dependent.Id).ToArray())
        {
            try
            {
                dependent.DependencyChanged();
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }
        if (errors is { Count: > 0 })
            throw new AggregateException("Reactive invalidation failed.", errors);
    }

    internal virtual void DependencyChanged() { }

    internal virtual void EvaluationFailed() { }

    internal virtual void AppendDump(StringBuilder dump) { }

    /// <summary>Throws when callers use a disposed reactive node.</summary>
    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }

    /// <summary>Releases this object's retained resources and owned reactive lifetime.</summary>
    public virtual void Dispose()
    {
        Graph.CheckThread();
        CheckScopeMutationGuard();
        if (IsDisposed)
            return;
        IsDisposed = true;
        List<Exception>? errors = null;
        try
        {
            ReplaceDependencies([]);
            foreach (var dependent in _dependents.OrderBy(dependent => dependent.Id).ToArray())
            {
                dependent.RemoveDependency(this);
                try
                {
                    dependent.DependencyChanged();
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
            }
            _dependents.Clear();
        }
        finally
        {
            _scope?.Detach(this);
            _scope = null;
            Graph.Unregister(this);
            GC.SuppressFinalize(this);
        }
        if (errors is { Count: > 0 })
            throw new AggregateException("Reactive node disposal failed.", errors);
    }

    private void RemoveDependency(ReactiveNode dependency) => _dependencies.Remove(dependency);

    /// <summary>Rejects mutation during a protected scope operation.</summary>
    protected void CheckScopeMutationGuard() => _scope?.CheckMutationGuard();
}

/// <summary>Writable graph state.</summary>
public sealed class Signal<T> : ReactiveNode
{
    private T _value;

    internal Signal(ReactiveGraph graph, T value, string name, ReactiveScope? scope)
        : base(graph, name, scope)
    {
        _value = value;
    }

    internal override string Kind => "signal";

    /// <summary>Gets the current reactive value.</summary>
    public T Value
    {
        get
        {
            Graph.CheckThread();
            Read();
            return _value;
        }
        set
        {
            Graph.CheckThread();
            CheckScopeMutationGuard();
            ThrowIfDisposed();
            if (EqualityComparer<T>.Default.Equals(_value, value))
                return;
            _value = value;
            Changed();
        }
    }

    /// <summary>Releases this object's retained resources and owned reactive lifetime.</summary>
    public override void Dispose()
    {
        Graph.CheckThread();
        CheckScopeMutationGuard();
        _value = default!;
        base.Dispose();
    }
}

/// <summary>A lazy, memoized value with runtime-tracked dependencies.</summary>
public sealed class Derived<T> : ReactiveNode
{
    private Func<T>? _compute;
    private T? _value;
    private bool _dirty = true;
    private bool _failed;

    internal Derived(ReactiveGraph graph, Func<T> compute, string name, ReactiveScope? scope)
        : base(graph, name, scope)
    {
        _compute = compute;
    }

    internal override string Kind => "derived";

    /// <summary>Gets the current reactive value.</summary>
    public T Value
    {
        get
        {
            Graph.CheckThread();
            Read();
            if (_dirty)
            {
                CheckScopeMutationGuard();
                var evaluation = Graph.Evaluate(this, _compute!);
                _value = evaluation.Value;
                _dirty = false;
                _failed = false;
                if (evaluation.ChangedDuringRun)
                    DependencyChanged();
            }
            return _value!;
        }
    }

    internal override void DependencyChanged()
    {
        if (IsDisposed)
            return;
        var notify = !_dirty || _failed;
        _dirty = true;
        _failed = false;
        if (notify)
            Changed();
    }

    internal override void EvaluationFailed() => _failed = true;

    internal override void AppendDump(StringBuilder dump) =>
        dump.Append(" dirty=").Append(_dirty ? "true" : "false");

    /// <summary>Releases this object's retained resources and owned reactive lifetime.</summary>
    public override void Dispose()
    {
        Graph.CheckThread();
        CheckScopeMutationGuard();
        _compute = null;
        _value = default;
        base.Dispose();
    }
}

/// <summary>An explicitly scheduled reactive callback. Effects are the graph's only subscriber mechanism.</summary>
public sealed class ReactiveEffect : ReactiveNode
{
    private Action? _callback;

    internal ReactiveEffect(ReactiveGraph graph, Action callback, string name, ReactiveScope? scope)
        : base(graph, name, scope)
    {
        _callback = callback;
        graph.Schedule(this);
    }

    internal LinkedListNode<ReactiveEffect>? QueueNode { get; set; }
    internal override string Kind => "effect";

    internal override void DependencyChanged() => Graph.Schedule(this);

    internal void Run()
    {
        CheckScopeMutationGuard();
        var changed = Graph.Collect(this, _callback!);
        if (changed)
            Graph.Schedule(this);
    }

    internal void Dequeue() => QueueNode = null;

    /// <summary>Releases this object's retained resources and owned reactive lifetime.</summary>
    public override void Dispose()
    {
        Graph.CheckThread();
        CheckScopeMutationGuard();
        Graph.Unschedule(this);
        _callback = null;
        base.Dispose();
    }
}
