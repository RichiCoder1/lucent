using System.Collections.ObjectModel;
using System.Globalization;

namespace Lucent.Core;

/// <summary>Identifies one generated route declaration at its authored source location.</summary>
public sealed record RouteDeclarationSource
{
    /// <summary>Creates immutable generated source metadata.</summary>
    public RouteDeclarationSource(string projectRelativePath, int line, int column)
    {
        ProjectRelativePath = ComponentSourcePath.Normalize(projectRelativePath);
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(column, 1);
        Line = line;
        Column = column;
    }

    /// <summary>Gets the normalized project-relative source path.</summary>
    public string ProjectRelativePath { get; }

    /// <summary>Gets the one-based line.</summary>
    public int Line { get; }

    /// <summary>Gets the one-based column.</summary>
    public int Column { get; }

    /// <inheritdoc />
    public override string ToString() =>
        $"route-source {ProjectRelativePath}:{Line.ToString(CultureInfo.InvariantCulture)}:{Column.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>A canonical location created by a generated typed route factory.</summary>
public sealed class RouteReference
{
    private RouteReference(RoutePattern pattern, RouteLocation location)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(location);
        Pattern = pattern;
        Location = location;
    }

    /// <summary>Gets the exact generated pattern used to format this reference.</summary>
    public RoutePattern Pattern { get; }

    /// <summary>Gets the canonical target location.</summary>
    public RouteLocation Location { get; }

    /// <summary>Formats a canonical reference through the supplied generated pattern.</summary>
    public static RouteReference Create(RoutePattern pattern, IReadOnlyList<RouteValue> values)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(values);
        return new(pattern, pattern.Format(values.ToArray()));
    }

    /// <inheritdoc />
    public override string ToString() => $"route-reference id={Pattern.Id}";
}

/// <summary>Live capabilities published for one retained route-level context.</summary>
public sealed class RouteContextLiveState
{
    private Signal<LiveValue>? _signal;
    private LiveValue _value;

    internal RouteContextLiveState(NavigationSnapshot? activeEntry = null)
    {
        _value = new(activeEntry, null);
    }

    /// <summary>Gets the live journal entry currently published for this route level.</summary>
    public NavigationSnapshot? ActiveEntry => Read().ActiveEntry;

    /// <summary>Gets the live nested outlet snapshot, when this level owns a child outlet.</summary>
    public RouteOutletSnapshot? Child => Read().Child;

    internal void Attach(ReactiveScope owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_signal is not null)
            throw new InvalidOperationException("A route context live state was attached twice.");
        _signal = owner.Signal(_value, "route-context-live");
    }

    internal void Update(NavigationSnapshot activeEntry, RouteOutletSnapshot? child)
    {
        ArgumentNullException.ThrowIfNull(activeEntry);
        var value = new LiveValue(activeEntry, child);
        _value = value;
        if (_signal is { IsDisposed: false } signal)
            signal.Value = value;
    }

    internal void Clear()
    {
        _signal = null;
        _value = default;
    }

    private LiveValue Read() => _signal is { IsDisposed: false } signal ? signal.Value : _value;

    private readonly record struct LiveValue(
        NavigationSnapshot? ActiveEntry,
        RouteOutletSnapshot? Child
    );
}

/// <summary>Fixed typed parameters supplied to one retained route level.</summary>
public sealed class RouteContext<T>(
    RouteLevelDescriptor definition,
    T parameters,
    RouteContextLiveState? live = null
)
{
    private readonly RouteContextLiveState _live = live ?? new();

    /// <summary>Gets the generated route-level definition.</summary>
    public RouteLevelDescriptor Definition { get; } =
        definition ?? throw new ArgumentNullException(nameof(definition));

    /// <summary>Gets the typed parameters fixed for this retained mount.</summary>
    public T Parameters { get; } = parameters;

    /// <summary>Gets the live entry and child capabilities for this retained mount.</summary>
    public RouteContextLiveState Live => _live;

    /// <summary>Gets the currently published journal entry for this retained mount.</summary>
    public NavigationSnapshot? ActiveEntry => _live.ActiveEntry;

    /// <summary>Gets the currently published nested outlet state for this retained mount.</summary>
    public RouteOutletSnapshot? Child => _live.Child;
}

/// <summary>Wraps a recipe with one statically closed generated RouteContext provider.</summary>
public delegate ComponentRecipe RouteContextProviderFactory(
    RouteLevelDescriptor definition,
    RouteMatch match,
    ComponentRecipe content
);

/// <summary>
/// Wraps content with a generated RouteContext provider that receives its live outlet state.
/// </summary>
public delegate ComponentRecipe RouteContextLiveProviderFactory(
    RouteLevelDescriptor definition,
    RouteMatch match,
    ComponentRecipe content,
    RouteContextLiveState live
);

/// <summary>Creates the exact generated typed route context used by render selection.</summary>
public delegate object RouteContextFactory(
    RouteLevelDescriptor definition,
    RouteMatch match,
    RouteContextLiveState live
);

/// <summary>Provides a previously created exact typed route context to mounted content.</summary>
public delegate ComponentRecipe RouteContextInstanceProviderFactory(
    object context,
    ComponentRecipe content
);

/// <summary>One level in a generated terminal route branch.</summary>
public sealed class RouteLevelDescriptor
{
    private readonly ReadOnlyCollection<int> _ownedCaptureSlots;
    private readonly RouteContextProviderFactory? _provider;
    private readonly RouteContextLiveProviderFactory? _liveProvider;
    private readonly RouteContextFactory? _contextFactory;
    private readonly RouteContextInstanceProviderFactory? _instanceProvider;

    /// <summary>Creates immutable route-level metadata and its closed provider factory.</summary>
    public RouteLevelDescriptor(
        RouteDefinitionId id,
        IReadOnlyList<int> ownedCaptureSlots,
        RouteDeclarationSource source,
        RouteContextProviderFactory provider
    )
        : this(id, ownedCaptureSlots, source, provider, null, null, null) { }

    /// <summary>Creates metadata with a closed provider that consumes live outlet state.</summary>
    public RouteLevelDescriptor(
        RouteDefinitionId id,
        IReadOnlyList<int> ownedCaptureSlots,
        RouteDeclarationSource source,
        RouteContextLiveProviderFactory provider
    )
        : this(id, ownedCaptureSlots, source, null, provider, null, null) { }

    /// <summary>Creates generated metadata with an exact typed context factory.</summary>
    public RouteLevelDescriptor(
        RouteDefinitionId id,
        IReadOnlyList<int> ownedCaptureSlots,
        RouteDeclarationSource source,
        RouteContextFactory contextFactory,
        RouteContextInstanceProviderFactory instanceProvider
    )
        : this(id, ownedCaptureSlots, source, null, null, contextFactory, instanceProvider) { }

    private RouteLevelDescriptor(
        RouteDefinitionId id,
        IReadOnlyList<int> ownedCaptureSlots,
        RouteDeclarationSource source,
        RouteContextProviderFactory? provider,
        RouteContextLiveProviderFactory? liveProvider,
        RouteContextFactory? contextFactory,
        RouteContextInstanceProviderFactory? instanceProvider
    )
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(ownedCaptureSlots);
        ArgumentNullException.ThrowIfNull(source);
        var legacyProviderCount = (provider is null ? 0 : 1) + (liveProvider is null ? 0 : 1);
        var generatedPair = contextFactory is not null && instanceProvider is not null;
        if (
            legacyProviderCount + (generatedPair ? 1 : 0) != 1
            || (contextFactory is null) != (instanceProvider is null)
        )
            throw new ArgumentException(
                "Exactly one route context provider must be supplied.",
                nameof(provider)
            );
        var slots = ownedCaptureSlots.ToArray();
        if (slots.Any(slot => slot < 0) || slots.Distinct().Count() != slots.Length)
            throw new ArgumentException(
                "Owned capture slots must be distinct nonnegative values.",
                nameof(ownedCaptureSlots)
            );
        Id = id;
        _ownedCaptureSlots = Array.AsReadOnly(slots);
        Source = source;
        _provider = provider;
        _liveProvider = liveProvider;
        _contextFactory = contextFactory;
        _instanceProvider = instanceProvider;
    }

    /// <summary>Gets the stable generated route-level identity.</summary>
    public RouteDefinitionId Id { get; }

    /// <summary>Gets capture slots owned by this level for retention identity.</summary>
    public IReadOnlyList<int> OwnedCaptureSlots => _ownedCaptureSlots;

    /// <summary>Gets authored source metadata.</summary>
    public RouteDeclarationSource Source { get; }

    /// <summary>Wraps content in the statically closed typed context provider.</summary>
    public ComponentRecipe ProvideContext(RouteMatch match, ComponentRecipe content)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(content);
        return _provider is { } provider
            ? provider(this, match, content)
            : _liveProvider!(this, match, content, new RouteContextLiveState());
    }

    /// <summary>
    /// Wraps content with this definition and the live entry/child capability for its mount.
    /// </summary>
    public ComponentRecipe ProvideContext(
        RouteMatch match,
        ComponentRecipe content,
        RouteContextLiveState live
    )
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(live);
        return _liveProvider is { } liveProvider
            ? liveProvider(this, match, content, live)
            : _provider!(this, match, content);
    }

    internal object CreateContext(RouteMatch match, RouteContextLiveState live)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(live);
        return _contextFactory?.Invoke(this, match, live)
            ?? throw new InvalidOperationException(
                $"Route level '{Id}' was not generated with typed render-context metadata."
            );
    }

    internal ComponentRecipe ProvideContext(object context, ComponentRecipe content)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(content);
        return _instanceProvider?.Invoke(context, content)
            ?? throw new InvalidOperationException(
                $"Route level '{Id}' cannot provide a preconstructed typed route context."
            );
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"route-level id={Id} captures={_ownedCaptureSlots.Count.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>Generated metadata paired with one authoritative terminal pattern.</summary>
public sealed class RouteDefinitionDescriptor
{
    private readonly ReadOnlyCollection<RouteLevelDescriptor> _branch;

    /// <summary>Creates one terminal descriptor with its root-to-leaf retained branch.</summary>
    public RouteDefinitionDescriptor(
        RoutePattern pattern,
        IReadOnlyList<RouteLevelDescriptor> branch
    )
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(branch);
        var levels = branch.ToArray();
        if (levels.Length == 0 || levels.Any(level => level is null))
            throw new ArgumentException(
                "A terminal route branch must contain at least one level.",
                nameof(branch)
            );
        if (
            !Equals(levels[^1].Id, pattern.Id)
            || levels.Select(level => level.Id).Distinct().Count() != levels.Length
        )
            throw new ArgumentException(
                "A route branch must contain distinct level identities and end at its terminal pattern.",
                nameof(branch)
            );
        if (
            levels
                .SelectMany(level => level.OwnedCaptureSlots)
                .Any(slot => slot >= pattern.CaptureCount)
        )
            throw new ArgumentException(
                "A route level owns a capture outside its terminal pattern.",
                nameof(branch)
            );
        var ownedSlots = levels.SelectMany(level => level.OwnedCaptureSlots).ToArray();
        if (
            ownedSlots.Distinct().Count() != ownedSlots.Length
            || !ownedSlots
                .OrderBy(slot => slot)
                .SequenceEqual(Enumerable.Range(0, pattern.CaptureCount))
        )
            throw new ArgumentException(
                "A route branch must own every terminal capture exactly once.",
                nameof(branch)
            );
        Pattern = pattern;
        _branch = Array.AsReadOnly(levels);
    }

    /// <summary>Gets the exact pattern supplied to the sole RouteTable.</summary>
    public RoutePattern Pattern { get; }

    /// <summary>Gets retained levels in root-to-leaf order.</summary>
    public IReadOnlyList<RouteLevelDescriptor> Branch => _branch;

    /// <inheritdoc />
    public override string ToString() =>
        $"route-definition id={Pattern.Id} levels={_branch.Count.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>Generated definitions from one explicit source module.</summary>
public sealed class RouteModuleDescriptor
{
    private readonly ReadOnlyCollection<RouteDefinitionDescriptor> _definitions;
    private readonly ReadOnlyCollection<RoutePattern> _patterns;

    /// <summary>Creates immutable module metadata without constructing a route table.</summary>
    public RouteModuleDescriptor(
        string name,
        RouteFallbackPolicy fallbackPolicy,
        RouteDeclarationSource source,
        IReadOnlyList<RouteDefinitionDescriptor> definitions
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(fallbackPolicy))
            throw new ArgumentOutOfRangeException(nameof(fallbackPolicy));
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(source);
        var copy = definitions.ToArray();
        if (
            copy.Any(definition => definition is null)
            || copy.Select(definition => definition.Pattern.Id).Distinct().Count() != copy.Length
        )
            throw new ArgumentException(
                "A route module must contain nonnull definitions with distinct identities.",
                nameof(definitions)
            );
        Name = name;
        FallbackPolicy = fallbackPolicy;
        Source = source;
        _definitions = Array.AsReadOnly(copy);
        _patterns = Array.AsReadOnly(copy.Select(definition => definition.Pattern).ToArray());
    }

    /// <summary>Gets the stable generated module name.</summary>
    public string Name { get; }

    /// <summary>Gets the explicit unmatched-location policy.</summary>
    public RouteFallbackPolicy FallbackPolicy { get; }

    /// <summary>Gets the authored module declaration source.</summary>
    public RouteDeclarationSource Source { get; }

    /// <summary>Gets generated definitions in stable identity order.</summary>
    public IReadOnlyList<RouteDefinitionDescriptor> Definitions => _definitions;

    /// <summary>Gets the exact pattern instances for RouteTable construction.</summary>
    public IReadOnlyList<RoutePattern> Patterns => _patterns;

    /// <inheritdoc />
    public override string ToString() =>
        $"route-module name={Name} definitions={_definitions.Count.ToString(CultureInfo.InvariantCulture)} fallback={FallbackPolicy}";
}

/// <summary>Pairs generated metadata with one externally constructed authoritative RouteTable.</summary>
public sealed class RouteDescriptorSet
{
    private readonly Dictionary<RouteDefinitionId, RouteDefinitionDescriptor> _byId;

    private RouteDescriptorSet(
        RouteTable table,
        Dictionary<RouteDefinitionId, RouteDefinitionDescriptor> byId
    )
    {
        Table = table;
        _byId = byId;
    }

    /// <summary>Gets the sole route table used for location matching.</summary>
    public RouteTable Table { get; }

    /// <summary>Validates a one-to-one reference pairing between the table and generated modules.</summary>
    public static RouteDescriptorSet Create(
        RouteTable table,
        IReadOnlyList<RouteModuleDescriptor> modules
    )
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(modules);
        var byId = new Dictionary<RouteDefinitionId, RouteDefinitionDescriptor>();
        foreach (var module in modules)
        {
            ArgumentNullException.ThrowIfNull(module);
            foreach (var definition in module.Definitions)
                if (!byId.TryAdd(definition.Pattern.Id, definition))
                    throw new ArgumentException(
                        $"Generated route identity '{definition.Pattern.Id}' occurs in more than one module.",
                        nameof(modules)
                    );
        }
        if (
            byId.Count != table.Patterns.Count
            || table.Patterns.Any(pattern =>
                !byId.TryGetValue(pattern.Id, out var definition)
                || !ReferenceEquals(pattern, definition.Pattern)
            )
        )
            throw new ArgumentException(
                "Generated descriptors must pair one-to-one by reference with the supplied route table.",
                nameof(modules)
            );
        return new(table, byId);
    }

    /// <summary>Gets generated metadata for a match from this set's table.</summary>
    public RouteDefinitionDescriptor GetDefinition(RouteMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        if (
            !_byId.TryGetValue(match.DefinitionId, out var definition)
            || !ReferenceEquals(match.Pattern, definition.Pattern)
        )
            throw new ArgumentException(
                "The route match did not come from this descriptor set's table.",
                nameof(match)
            );
        return definition;
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"route-descriptor-set definitions={_byId.Count.ToString(CultureInfo.InvariantCulture)}";
}
