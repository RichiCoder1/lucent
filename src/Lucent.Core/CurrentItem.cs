namespace Lucent.Core;

/// <summary>Provides the current payload for one retained keyed or conditional entry.</summary>
/// <remarks>Reading this value in a live callback tracks it reactively. Reading it while constructing a recipe captures the current construction-time value.</remarks>
public sealed class CurrentItem<T>
{
    private readonly CurrentItemNode<T> _node;

    internal CurrentItem(CurrentItemNode<T> node) =>
        _node = node ?? throw new ArgumentNullException(nameof(node));

    /// <summary>Gets the entry's latest accepted payload.</summary>
    public T Value => _node.Value;

    internal void Stage(T value) => _node.Stage(value);

    internal void Notify() => _node.Notify();
}

internal sealed class CurrentItemNode<T> : ReactiveNode
{
    private T _value;

    internal CurrentItemNode(ReactiveGraph graph, T value, string name, ReactiveScope scope)
        : base(graph, name, scope) => _value = value;

    internal override string Kind => "current-item";

    internal T Value
    {
        get
        {
            Graph.CheckThread();
            ThrowIfDisposed();
            Read();
            return _value;
        }
    }

    internal void Stage(T value)
    {
        Graph.CheckThread();
        CheckScopeMutationGuard();
        ThrowIfDisposed();
        _value = value;
    }

    internal void Notify()
    {
        Graph.CheckThread();
        CheckScopeMutationGuard();
        ThrowIfDisposed();
        Changed();
    }

    public override void Dispose()
    {
        Graph.CheckThread();
        CheckScopeMutationGuard();
        _value = default!;
        base.Dispose();
    }
}
