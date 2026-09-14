namespace Lucent.Core;

/// <summary>Defines what a generated route module does when no route matches.</summary>
public enum RouteFallbackPolicy
{
    /// <summary>Rejects the location without selecting a route.</summary>
    Reject,
}

/// <summary>Marks a static partial type as one explicit generated route module.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LucentRouteModuleAttribute : Attribute
{
    /// <summary>Creates a route module with an explicit unmatched-location policy.</summary>
    public LucentRouteModuleAttribute(RouteFallbackPolicy fallbackPolicy)
    {
        if (!Enum.IsDefined(fallbackPolicy))
            throw new ArgumentOutOfRangeException(nameof(fallbackPolicy));
        FallbackPolicy = fallbackPolicy;
    }

    /// <summary>Gets the module's explicit unmatched-location policy.</summary>
    public RouteFallbackPolicy FallbackPolicy { get; }
}

/// <summary>Declares one generated typed route definition.</summary>
/// <remarks>The attribute is compiler input. Core does not scan it at runtime.</remarks>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class LucentRouteAttribute : Attribute
{
    /// <summary>Declares a route in the supplied generated module.</summary>
    public LucentRouteAttribute(Type moduleType, string template)
    {
        ArgumentNullException.ThrowIfNull(moduleType);
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        ModuleType = moduleType;
        Template = template;
    }

    /// <summary>Gets the static partial module type that owns this declaration.</summary>
    public Type ModuleType { get; }

    /// <summary>Gets the strict authored path and query template.</summary>
    public string Template { get; }

    /// <summary>Gets or sets the stable definition identity. The generator supplies one when omitted.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the optional parent route type.</summary>
    public Type? Parent { get; set; }
}
