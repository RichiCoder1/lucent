namespace Lucent.Core;

/// <summary>Marks a static element recipe available to compile-time LUI lowering.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class LuiComponentAttribute : Attribute { }

/// <summary>Marks a component parameter as content; only one default content parameter is supported initially.</summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class LuiContentAttribute : Attribute
{
    public bool IsDefault { get; init; }
}
