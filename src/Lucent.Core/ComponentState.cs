namespace Lucent.Core;

/// <summary>Marks a sealed partial class whose explicit partial properties are generated as per-mount state.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ComponentStateAttribute : Attribute { }

/// <summary>Marks one explicit partial property as generated writable component state.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class StateAttribute : Attribute
{
    /// <summary>Uses the property's default value as its initial value.</summary>
    public StateAttribute() { }

    /// <summary>Uses a compile-time constant as the property's initial value.</summary>
    public StateAttribute(object? initialValue) => InitialValue = initialValue;

    /// <summary>Gets the compile-time initial value, when one was supplied.</summary>
    public object? InitialValue { get; }

    /// <summary>Gets or sets the name of a static typed initializer taking one <see cref="ComponentContext"/>.</summary>
    public string? Initializer { get; set; }
}

/// <summary>Direct NativeAOT-safe construction contract implemented by generated component state.</summary>
/// <typeparam name="TSelf">The generated sealed state type.</typeparam>
public interface IComponentState<TSelf>
    where TSelf : IComponentState<TSelf>
{
    /// <summary>Creates and attaches one state instance to the supplied component mount.</summary>
    static abstract TSelf CreateComponentState(ComponentContext context);
}
