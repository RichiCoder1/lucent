namespace Lucent.Core;

/// <summary>Owned state and lifetime operations available while defining one component mount.</summary>
/// <remarks>
/// This facade uses the retained root owner created by the existing deferred-recipe transaction.
/// It does not create another scope. Fallback diagnostic names use
/// <c>{component}.{operation}-{ordinal}</c>; every named allocation consumes the shared per-mount
/// ordinal, including allocations with an explicit name.
/// </remarks>
public sealed class ComponentContext
{
    private readonly ReactiveScope _owner;
    private readonly string _component;
    private int _ordinal;

    internal ComponentContext(ReactiveScope owner, string component)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        ReactiveGraph.ValidateName(component, nameof(component));
        _component = component;
    }

    /// <summary>Creates writable state owned by this component mount.</summary>
    public Signal<T> State<T>(T value, string? name = null) =>
        _owner.Signal(value, AllocationName("state", name));

    /// <summary>Creates a lazy computed value owned by this component mount.</summary>
    public Derived<T> Computed<T>(Func<T> compute, string? name = null) =>
        _owner.Derived(compute, AllocationName("computed", name));

    /// <summary>Creates a tracked observer owned by this component mount.</summary>
    public ReactiveEffect Observe(Action callback, string? name = null) =>
        _owner.Effect(callback, AllocationName("observe", name));

    /// <summary>Creates latest-generation asynchronous state owned by this component mount.</summary>
    public AsyncValue<T> Resource<T>(Func<CancellationToken, Task<T>> load, string? name = null) =>
        _owner.Async(load, AllocationName("resource", name));

    /// <summary>Creates latest-generation asynchronous state with an initial stale value.</summary>
    /// <remarks>The diagnostic name remains required so a stale <see cref="System.String"/> value cannot bind as a name by accident.</remarks>
    public AsyncValue<T> Resource<T>(
        Func<CancellationToken, Task<T>> load,
        T staleValue,
        string? name
    ) => _owner.Async(load, staleValue, AllocationName("resource", name));

    /// <summary>Creates source-driven latest-generation asynchronous state.</summary>
    public AsyncValue<T> Resource<TSource, T>(
        Func<TSource> source,
        Func<TSource, CancellationToken, Task<T>> load,
        string? name = null
    ) => _owner.Async(source, load, AllocationName("resource", name));

    /// <summary>Creates source-driven latest-generation asynchronous state with an initial stale value.</summary>
    /// <remarks>The diagnostic name remains required so a stale <see cref="System.String"/> value cannot bind as a name by accident.</remarks>
    public AsyncValue<T> Resource<TSource, T>(
        Func<TSource> source,
        Func<TSource, CancellationToken, Task<T>> load,
        T staleValue,
        string? name
    ) => _owner.Async(source, load, staleValue, AllocationName("resource", name));

    /// <summary>Transfers a disposable resource into this component mount's lifetime.</summary>
    public T Own<T>(T value)
        where T : IDisposable => _owner.Own(value);

    /// <summary>Registers cleanup that runs when this component mount is disposed.</summary>
    public void OnDispose(Action cleanup) => _owner.OnDispose(cleanup);

    /// <summary>Queues a callback from any thread for this component's owning graph.</summary>
    /// <remarks>The returned handle cancels the callback; component disposal also cancels it.</remarks>
    public IDisposable Post(Action callback) => _owner.Post(callback);

    private string AllocationName(string operation, string? explicitName)
    {
        _owner.CheckComponentContextAccess();
        if (explicitName is not null)
            ReactiveGraph.ValidateName(explicitName, nameof(explicitName));
        var ordinal = checked(++_ordinal);
        if (explicitName is not null)
            return explicitName;
        return _component
            + "."
            + operation
            + "-"
            + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
