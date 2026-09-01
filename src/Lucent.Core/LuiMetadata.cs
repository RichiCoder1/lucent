namespace Lucent.Core;

/// <summary>Marks a static component recipe available to compile-time lowering.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class LucentComponentAttribute : Attribute { }

/// <summary>Marks the one parameter that receives unwrapped component content.</summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class DefaultContentAttribute : Attribute { }
