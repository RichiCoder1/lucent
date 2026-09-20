namespace Lucent.Core;

/// <summary>One immutable generated route table, descriptor set, and default destination map.</summary>
public sealed class RouteBundle
{
    private readonly Func<RouteLevelDescriptor, RouteDestination> _destination;

    private RouteBundle(
        RouteTable table,
        RouteDescriptorSet descriptors,
        Func<RouteLevelDescriptor, RouteDestination> destination
    )
    {
        Table = table;
        Descriptors = descriptors;
        _destination = destination;
    }

    /// <summary>Gets the sole fixed table used by sessions created for this bundle.</summary>
    public RouteTable Table { get; }

    /// <summary>Gets the generated metadata paired by reference with <see cref="Table"/>.</summary>
    public RouteDescriptorSet Descriptors { get; }

    /// <summary>Creates a bundle from one or more generated modules and a closed default mapping.</summary>
    public static RouteBundle Create(
        IReadOnlyList<RouteModuleDescriptor> modules,
        Func<RouteLevelDescriptor, RouteDestination> destination
    )
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(destination);
        var copy = modules.ToArray();
        if (copy.Length == 0 || copy.Any(module => module is null))
            throw new ArgumentException(
                "A route bundle requires at least one generated module.",
                nameof(modules)
            );
        var table = RouteTable.Create(copy.SelectMany(module => module.Patterns).ToArray());
        return new RouteBundle(table, RouteDescriptorSet.Create(table, copy), destination);
    }

    /// <summary>Creates a bundle over an existing exact table and descriptor pairing.</summary>
    public static RouteBundle Create(
        RouteDescriptorSet descriptors,
        Func<RouteLevelDescriptor, RouteDestination> destination
    )
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(destination);
        return new RouteBundle(descriptors.Table, descriptors, destination);
    }

    internal RouteDestination ResolveDefault(RouteLevelDescriptor level)
    {
        var result = _destination(level);
        return result
            ?? throw new InvalidOperationException("A route destination mapping returned null.");
    }
}

/// <summary>A resolved component recipe with stable type and optional author key identity.</summary>
public sealed class RouteDestination
{
    /// <summary>Creates one resolved route destination.</summary>
    public RouteDestination(Type componentType, ComponentRecipe content, object? key = null)
    {
        ComponentType = componentType ?? throw new ArgumentNullException(nameof(componentType));
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Key = key;
    }

    /// <summary>Gets the statically mapped component identity.</summary>
    public Type ComponentType { get; }

    /// <summary>Gets the optional application identity within the component type.</summary>
    public object? Key { get; }

    /// <summary>Gets the recipe mounted when this identity is selected.</summary>
    public ComponentRecipe Content { get; }
}

/// <summary>Input for synchronous, typed destination selection at one matched route level.</summary>
public sealed class RouteDestinationRequest
{
    private readonly object _context;

    internal RouteDestinationRequest(
        RouteLevelDescriptor level,
        RouteMatch match,
        object context,
        RouteDestination defaultDestination
    )
    {
        Level = level;
        Match = match;
        _context = context;
        Default = defaultDestination;
    }

    /// <summary>Gets the generated route level being selected.</summary>
    public RouteLevelDescriptor Level { get; }

    /// <summary>Gets the immutable match that supplied the typed route record.</summary>
    public RouteMatch Match { get; }

    /// <summary>Gets the generated default component selection.</summary>
    public RouteDestination Default { get; }

    /// <summary>Gets the exact generated typed context for this route declaration.</summary>
    /// <remarks>
    /// Parameters describe <see cref="Match"/>. During navigation, live values such as
    /// <see cref="RouteContext{T}.ActiveEntry"/> continue to describe the last committed
    /// publication until the staged branch publishes.
    /// </remarks>
    public RouteContext<T> GetContext<T>()
    {
        if (_context is RouteContext<T> typed)
            return typed;
        throw new InvalidOperationException(
            $"Route level '{Level.Id}' provides '{_context.GetType()}', not RouteContext<{typeof(T)}>'."
        );
    }
}

/// <summary>Selects a destination synchronously while tracking reactive reads.</summary>
public delegate RouteDestination RouteDestinationResolver(RouteDestinationRequest request);

internal sealed class RouterPlacement
{
    internal RouterPlacement(
        RouteBundle routes,
        NavigationSession session,
        RouteOutletCursor? cursor
    )
    {
        Routes = routes;
        Session = session;
        Cursor = cursor;
    }

    internal RouteBundle Routes { get; }
    internal NavigationSession Session { get; }
    internal RouteOutletCursor? Cursor { get; }
}
