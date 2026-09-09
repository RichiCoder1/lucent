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

internal readonly record struct ReactiveRead(ReactiveNode Node, long Version);

internal sealed class ReactiveCollector
{
    private readonly List<ReactiveRead> _reads = [];
    private readonly Dictionary<ReactiveNode, int> _indices = new(
        ReferenceEqualityComparer.Instance
    );
    private bool _changedDuringRun;
    internal IReadOnlyList<ReactiveRead> Reads => _reads;

    internal void Add(ReactiveNode node)
    {
        if (_indices.ContainsKey(node))
            return;
        _indices.Add(node, _reads.Count);
        _reads.Add(new(node, node.Version));
    }

    internal void Refresh(ReactiveNode node)
    {
        if (_indices.TryGetValue(node, out var index))
            _reads[index] = new(node, node.Version);
    }

    internal void MarkChanged() => _changedDuringRun = true;

    internal bool Changed() =>
        _changedDuringRun || _reads.Any(read => read.Node.Version != read.Version);
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
    private readonly List<ReactiveRead?> _dependencies = [];
    private readonly Dictionary<ReactiveNode, int> _dependencyIndex = new(
        ReferenceEqualityComparer.Instance
    );
    private readonly DependentSet _dependents = new();
    private ReactiveScope? _scope;
    private bool _dependenciesPotentiallyChanged;
    private bool _validatingPotentialDependencies;
    private long _dependencyChangeRevision;

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
    internal IEnumerable<ReactiveNode> Dependencies =>
        _dependencies.Where(read => read.HasValue).Select(read => read!.Value.Node);
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
        if (succeeded)
        {
            ReplaceDependencies(collector.Reads);
            return;
        }
        var combined = new List<ReactiveRead>(_dependencyIndex.Count + collector.Reads.Count);
        var seen = new HashSet<ReactiveNode>(ReferenceEqualityComparer.Instance);
        foreach (var read in _dependencies)
            if (read.HasValue && seen.Add(read.Value.Node))
                combined.Add(read.Value);
        foreach (var read in collector.Reads)
            if (seen.Add(read.Node))
                combined.Add(read);
        ReplaceDependencies(combined);
    }

    private void ReplaceDependencies(IReadOnlyList<ReactiveRead> next)
    {
        var nextNodes = new HashSet<ReactiveNode>(
            next.Select(read => read.Node),
            ReferenceEqualityComparer.Instance
        );
        foreach (var read in _dependencies)
            if (read.HasValue && !nextNodes.Contains(read.Value.Node))
                read.Value.Node._dependents.Remove(this);
        foreach (var read in next)
            if (!ReferenceEquals(read.Node, this) && !_dependencyIndex.ContainsKey(read.Node))
                read.Node._dependents.Add(this);
        _dependencies.Clear();
        _dependencyIndex.Clear();
        for (var index = 0; index < next.Count; index++)
        {
            _dependencies.Add(next[index]);
            _dependencyIndex.Add(next[index].Node, index);
        }
    }

    /// <summary>Invalidates dependents after this node changes.</summary>
    protected void Changed()
    {
        Graph.RecordMutation();
        Version++;
        NotifyDependents(definite: true);
    }

    /// <summary>Propagates possible invalidation without publishing a new observable value revision.</summary>
    protected void PotentiallyChanged() => NotifyDependents(definite: false);

    private void NotifyDependents(bool definite)
    {
        List<Exception>? errors = null;
        foreach (var dependent in _dependents.Snapshot())
        {
            try
            {
                if (definite)
                    dependent.ReceiveDependencyChanged();
                else
                    dependent.ReceivePotentialDependencyChange();
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }
        if (errors is { Count: > 0 })
            throw new AggregateException("Reactive invalidation failed.", errors);
    }

    private void ReceiveDependencyChanged()
    {
        _dependencyChangeRevision++;
        DependencyChanged();
    }

    private void ReceivePotentialDependencyChange()
    {
        if (IsDisposed || _dependenciesPotentiallyChanged)
            return;
        _dependenciesPotentiallyChanged = true;
        PotentialDependencyChanged();
    }

    /// <summary>Ensures lazy dependencies have resolved any potential invalidation.</summary>
    internal virtual void EnsureCurrent() => ValidatePotentialDependencies();

    /// <summary>Handles a possible dependency change without assuming its observable value changed.</summary>
    internal virtual void PotentialDependencyChanged() => PotentiallyChanged();

    /// <summary>Resolves potential dependencies and reports whether an observable input revision changed.</summary>
    protected bool ValidatePotentialDependencies()
    {
        if (!_dependenciesPotentiallyChanged)
            return false;
        if (_validatingPotentialDependencies)
        {
            ReceiveDependencyChanged();
            return true;
        }
        var changeRevision = _dependencyChangeRevision;
        var dependencies = _dependencies
            .Where(read => read.HasValue)
            .Select(read => read!.Value)
            .ToArray();
        _validatingPotentialDependencies = true;
        try
        {
            foreach (var read in dependencies)
                Graph.Untracked(() =>
                {
                    read.Node.EnsureCurrent();
                    return true;
                });
        }
        catch
        {
            _dependenciesPotentiallyChanged = false;
            throw;
        }
        finally
        {
            _validatingPotentialDependencies = false;
        }
        var changed = dependencies.Any(read => read.Node.Version != read.Version);
        _dependenciesPotentiallyChanged = false;
        if (changed && _dependencyChangeRevision == changeRevision)
            ReceiveDependencyChanged();
        return changed;
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
            ReplaceDependencies(Array.Empty<ReactiveRead>());
            foreach (var dependent in _dependents.Snapshot())
            {
                dependent.RemoveDependency(this);
                try
                {
                    dependent.ReceiveDependencyChanged();
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

    private void RemoveDependency(ReactiveNode dependency)
    {
        if (!_dependencyIndex.Remove(dependency, out var index))
            return;
        _dependencies[index] = null;
    }

    /// <summary>Rejects mutation during a protected scope operation.</summary>
    protected void CheckScopeMutationGuard()
    {
        Graph.CheckMutationGuard();
        _scope?.CheckMutationGuard();
    }

    /// <summary>Checks owner constraints while allowing lazy evaluation to read across provisional subtrees.</summary>
    protected void CheckScopeEvaluationGuard() => Graph.CheckThread();

    private sealed class DependentSet
    {
        private ReactiveNode? _single;
        private HashSet<ReactiveNode>? _many;

        internal void Add(ReactiveNode node)
        {
            if (_many is not null)
            {
                _many.Add(node);
                return;
            }
            if (_single is null)
            {
                _single = node;
                return;
            }
            if (ReferenceEquals(_single, node))
                return;
            _many = new HashSet<ReactiveNode>(ReferenceEqualityComparer.Instance) { _single, node };
            _single = null;
        }

        internal void Remove(ReactiveNode node)
        {
            if (_many is null)
            {
                if (ReferenceEquals(_single, node))
                    _single = null;
                return;
            }
            if (!_many.Remove(node) || _many.Count != 1)
                return;
            _single = _many.Single();
            _many = null;
        }

        internal ReactiveNode[] Snapshot() =>
            _many is not null ? _many.OrderBy(node => node.Id).ToArray()
            : _single is not null ? [_single]
            : [];

        internal void Clear()
        {
            _single = null;
            _many?.Clear();
            _many = null;
        }
    }
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
    private bool _hasValue;

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
            ValidatePotentialDependencies();
            if (_dirty)
            {
                CheckScopeEvaluationGuard();
                var evaluation = Graph.Evaluate(
                    this,
                    () =>
                    {
                        var value = _compute!();
                        return (
                            Value: value,
                            Changed: _hasValue
                                && !EqualityComparer<T>.Default.Equals(_value!, value)
                        );
                    }
                );
                _value = evaluation.Value.Value;
                _dirty = false;
                _failed = false;
                _hasValue = true;
                if (evaluation.Value.Changed)
                    Changed();
                if (evaluation.ChangedDuringRun)
                {
                    DependencyChanged();
                    Graph.MarkCollectionChanged();
                }
                else
                    Graph.RefreshTrackedVersion(this);
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
            PotentiallyChanged();
    }

    internal override void EnsureCurrent() => _ = Value;

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
        _hasValue = false;
        base.Dispose();
    }
}

/// <summary>An explicitly scheduled reactive callback. Effects are the graph's only subscriber mechanism.</summary>
public sealed class ReactiveEffect : ReactiveNode
{
    private Action? _callback;
    private bool _failed;
    private bool _mustRun = true;

    internal ReactiveEffect(ReactiveGraph graph, Action callback, string name, ReactiveScope? scope)
        : base(graph, name, scope)
    {
        _callback = callback;
        graph.Schedule(this);
    }

    internal LinkedListNode<ReactiveEffect>? QueueNode { get; set; }
    internal override string Kind => "effect";

    internal override void DependencyChanged()
    {
        _mustRun = true;
        Graph.Schedule(this);
    }

    internal override void PotentialDependencyChanged() => Graph.Schedule(this);

    internal void Run()
    {
        CheckScopeMutationGuard();
        try
        {
            ValidatePotentialDependencies();
        }
        catch
        {
            _failed = true;
            throw;
        }
        if (!_mustRun && !_failed)
            return;
        Graph.Unschedule(this);
        _mustRun = false;
        bool changed;
        try
        {
            changed = Graph.Collect(this, _callback!);
            _failed = false;
        }
        catch
        {
            _failed = true;
            throw;
        }
        if (changed)
        {
            _mustRun = true;
            Graph.Schedule(this);
        }
        else
        {
            // Dependency notifications raised while the callback was collecting are already
            // represented by the collector's final stamps. Keep a retry only when a value
            // changed after its last read.
            _mustRun = false;
            Graph.Unschedule(this);
        }
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
