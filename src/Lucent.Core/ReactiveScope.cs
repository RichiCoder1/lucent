using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Lucent.Core;

/// <summary>Hierarchical lifetime ownership for graph nodes, subscriptions, and cleanup.</summary>
public sealed class ReactiveScope : IDisposable
{
    private readonly ReactiveGraph _graph;
    private readonly List<IDisposable> _owned = [];
    private ReactiveScope? _parent;
    private Action? _mutationGuard;
    private Action? _factoryGuard;
    private Action<Action>? _factoryRollback;

    internal ReactiveScope(ReactiveGraph graph, ReactiveScope? parent, string name)
    {
        ReactiveGraph.ValidateName(name, nameof(name));
        _graph = graph;
        _parent = parent;
        _factoryGuard = parent?._factoryGuard;
        _factoryRollback = parent?._factoryRollback;
        Name = name;
        Id = graph.Register(this);
        parent?._owned.Add(this);
    }

    public int Id { get; }
    public string Name { get; }
    public ReactiveScope? Parent => _parent;
    public bool IsDisposed { get; private set; }
    internal ReactiveGraph Graph => _graph;
    internal bool DescendsFrom(ReactiveScope ancestor)
    {
        for (ReactiveScope? scope = this; scope is not null; scope = scope._parent)
            if (ReferenceEquals(scope, ancestor)) return true;
        return false;
    }
    public ReactiveScope CreateChild(string name) { CheckActive(); ReactiveGraph.ValidateName(name, nameof(name)); return new ReactiveScope(_graph, this, name); }
    internal ReactiveScope CreateElementChild(string name) { CheckActive(skipFactoryGuard: true); ReactiveGraph.ValidateName(name, nameof(name)); return new ReactiveScope(_graph, this, name); }
    public Signal<T> Signal<T>(T value, string name) { CheckActive(); ReactiveGraph.ValidateName(name, nameof(name)); return Own(new Signal<T>(_graph, value, name, this)); }
    internal Signal<T> SignalForFramework<T>(T value, string name) { CheckActive(skipFactoryGuard: true); ReactiveGraph.ValidateName(name, nameof(name)); return OwnElement(new Signal<T>(_graph, value, name, this)); }
    public Derived<T> Derived<T>(Func<T> compute, string name) { CheckActive(); ArgumentNullException.ThrowIfNull(compute); ReactiveGraph.ValidateName(name, nameof(name)); return Own(new Derived<T>(_graph, compute, name, this)); }
    public ReactiveEffect Effect(Action callback, string name) { CheckActive(); ArgumentNullException.ThrowIfNull(callback); ReactiveGraph.ValidateName(name, nameof(name)); return Own(new ReactiveEffect(_graph, callback, name, this)); }
    public AsyncValue<T> Async<T>(Func<CancellationToken, Task<T>> load, string name) { CheckActive(); ArgumentNullException.ThrowIfNull(load); ReactiveGraph.ValidateName(name, nameof(name)); return Own(new AsyncValue<T>(_graph, load, default!, false, name, this)); }
    public AsyncValue<T> Async<T>(Func<CancellationToken, Task<T>> load, T staleValue, string name) { CheckActive(); ArgumentNullException.ThrowIfNull(load); ReactiveGraph.ValidateName(name, nameof(name)); return Own(new AsyncValue<T>(_graph, load, staleValue, true, name, this)); }
    public T Own<T>(T value) where T : IDisposable { CheckActive(); ArgumentNullException.ThrowIfNull(value); _owned.Add(value); return value; }
    public void OnDispose(Action cleanup) { ArgumentNullException.ThrowIfNull(cleanup); Own(new Cleanup(cleanup)); }
    internal void OnElementDispose(Action cleanup) { ArgumentNullException.ThrowIfNull(cleanup); OwnElement(new Cleanup(cleanup)); }

    public void Dispose()
    {
        _graph.CheckThread();
        _mutationGuard?.Invoke();
        _factoryGuard?.Invoke();
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
    internal void SetMutationGuard(Action guard) => _mutationGuard = guard ?? throw new ArgumentNullException(nameof(guard));
    internal void SetFactoryGuard(Action guard) => _factoryGuard = guard ?? throw new ArgumentNullException(nameof(guard));
    internal void SetFactoryRollback(Action<Action> register) => _factoryRollback = register ?? throw new ArgumentNullException(nameof(register));
    internal void RegisterFactoryRollback(Action cleanup) { ArgumentNullException.ThrowIfNull(cleanup); _factoryRollback?.Invoke(cleanup); }
    internal void CheckMutationGuard() { _graph.CheckThread(); _mutationGuard?.Invoke(); _factoryGuard?.Invoke(); }

    private void CheckActive(bool skipFactoryGuard = false)
    {
        _graph.CheckThread();
        _mutationGuard?.Invoke();
        if (!skipFactoryGuard) _factoryGuard?.Invoke();
        if (IsDisposed) throw new ObjectDisposedException(Name);
    }

    internal T OwnElement<T>(T value) where T : IDisposable { CheckActive(skipFactoryGuard: true); _owned.Add(value); return value; }

    private sealed class Cleanup(Action callback) : IDisposable
    {
        private Action? _callback = callback;
        public void Dispose() => Interlocked.Exchange(ref _callback, null)?.Invoke();
    }
}
