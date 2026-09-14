using System.Collections.ObjectModel;

namespace Lucent.Core;

/// <summary>Public preparation input for one retained route level.</summary>
public sealed record RouteOutletPreparationRequest(
    long OperationId,
    long Generation,
    NavigationSnapshot? Current,
    NavigationSnapshot Target,
    NavigationHistoryAction History,
    NavigationOrigin Origin,
    NavigationPreparationPhase Phase,
    int RedirectCount
);

/// <summary>Prepares one matched route level without mounting a component or resolving a service.</summary>
public delegate ValueTask<NavigationPreparationResult> RouteOutletPreparationHandler(
    RouteLevelDescriptor level,
    RouteOutletPreparationRequest request,
    CancellationToken cancellationToken
);

/// <summary>Options for root-outlet preparation policy.</summary>
public sealed class RouteOutletOptions
{
    /// <summary>Creates options with preparation and optional publication interaction hooks.</summary>
    public RouteOutletOptions(
        RouteOutletPreparationHandler? prepare = null,
        NavigationInteraction? interaction = null
    )
    {
        Prepare = prepare;
        Interaction = interaction;
    }

    /// <summary>Gets the callback invoked in route-level transaction order.</summary>
    public RouteOutletPreparationHandler? Prepare { get; }

    /// <summary>Gets the optional focus, viewport, and announcement interaction owner.</summary>
    public NavigationInteraction? Interaction { get; }
}

/// <summary>A redacted view of one retained route-level mount.</summary>
public sealed class RouteOutletLevelSnapshot
{
    internal RouteOutletLevelSnapshot(int level, RouteDefinitionId definition, long elementId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(elementId);
        Level = level;
        Definition = definition;
        ElementId = elementId;
    }

    /// <summary>Gets the zero-based level within the matched branch.</summary>
    public int Level { get; }

    /// <summary>Gets the stable generated level identity.</summary>
    public RouteDefinitionId Definition { get; }

    /// <summary>Gets the retained Core element identity for this level.</summary>
    public long ElementId { get; }
}

/// <summary>A coherent, value-free view of a committed retained route branch.</summary>
public sealed class RouteOutletSnapshot
{
    private readonly ReadOnlyCollection<RouteOutletLevelSnapshot> _levels;

    internal RouteOutletSnapshot(
        long revision,
        long? entryId,
        RouteDefinitionId? terminalDefinition,
        IReadOnlyList<RouteOutletLevelSnapshot> levels
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        if (entryId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(entryId));
        ArgumentNullException.ThrowIfNull(levels);
        if (terminalDefinition is null && levels.Count != 0)
            throw new ArgumentException(
                "An outlet with retained levels must have a terminal definition.",
                nameof(levels)
            );
        Revision = revision;
        EntryId = entryId;
        TerminalDefinition = terminalDefinition;
        _levels = Array.AsReadOnly(levels.ToArray());
    }

    /// <summary>Gets the outlet publication revision.</summary>
    public long Revision { get; }

    /// <summary>Gets the active journal-entry identity, or null before the first commit.</summary>
    public long? EntryId { get; }

    /// <summary>Gets the terminal definition identity, or null before the first commit.</summary>
    public RouteDefinitionId? TerminalDefinition { get; }

    /// <summary>Gets retained levels in root-to-leaf order.</summary>
    public IReadOnlyList<RouteOutletLevelSnapshot> Levels => _levels;
}

/// <summary>A handle used by headless consumers to observe one recipe-backed outlet.</summary>
public sealed class RouteOutletHandle : IDisposable
{
    private RouteOutletMount? _mount;
    private bool _disposed;
    private RouteOutletSnapshot _snapshot = new(0, null, null, []);

    /// <summary>Gets the last coherent outlet snapshot.</summary>
    public RouteOutletSnapshot Snapshot => _snapshot;

    /// <summary>Releases the associated outlet, if it has been mounted.</summary>
    public void Dispose()
    {
        var mount = _mount;
        _mount = null;
        _disposed = true;
        mount?.Dispose();
    }

    internal bool IsDisposed => _disposed;

    internal void Attach(RouteOutletMount mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mount is not null)
            throw new InvalidOperationException("A RouteOutletHandle can observe one mount only.");
        _mount = mount;
        _snapshot = mount.Snapshot;
    }

    internal void Update(RouteOutletSnapshot snapshot)
    {
        if (!_disposed)
            _snapshot = snapshot;
    }

    internal void Detach(RouteOutletMount mount)
    {
        if (ReferenceEquals(_mount, mount))
        {
            _snapshot = mount.Snapshot;
            _mount = null;
        }
    }
}

/// <summary>Names the internal child-cursor provider used by nested route outlets.</summary>
internal sealed class RouteOutletCursor
{
    private RouteOutletMount? _consumer;
    private RouteOutletBuildNode _parentNode;

    internal RouteOutletCursor(
        RouteOutletMount owner,
        int startLevel,
        RouteOutletBuildNode parentNode
    )
    {
        Owner = owner;
        StartLevel = startLevel;
        _parentNode = parentNode;
    }

    internal RouteOutletMount Owner { get; }
    internal NavigationSession Session => Owner.Session;
    internal int StartLevel { get; }
    internal RouteOutletBuildNode ParentNode => _parentNode;

    internal void ReplaceParent(RouteOutletBuildNode parentNode)
    {
        ArgumentNullException.ThrowIfNull(parentNode);
        _parentNode = parentNode;
    }

    internal void Claim(RouteOutletMount consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        if (_consumer is not null)
            throw new InvalidOperationException("A route outlet child cursor has one consumer.");
        if (!ReferenceEquals(consumer.Session, Session))
            throw new InvalidOperationException(
                "A nested route outlet cannot consume a cursor from another navigation session."
            );
        _consumer = consumer;
        RouteOutletMount.RegisterChild(_parentNode, consumer);
    }

    internal void Release(RouteOutletMount consumer)
    {
        if (ReferenceEquals(_consumer, consumer))
            _consumer = null;
    }
}
