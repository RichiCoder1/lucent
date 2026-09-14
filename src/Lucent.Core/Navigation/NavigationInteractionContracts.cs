using System.Collections.ObjectModel;

namespace Lucent.Core;

/// <summary>Names the fallback priority of one authored navigation focus target.</summary>
public enum NavigationTargetKind
{
    /// <summary>The route heading is the preferred focus target for a fresh entry.</summary>
    Heading,

    /// <summary>A useful route control is used when no heading target is available.</summary>
    Useful,
}

/// <summary>An immutable target-scoped viewport position retained for one journal entry.</summary>
public readonly record struct NavigationViewportPosition
{
    internal NavigationViewportPosition(string targetId, ScrollOffset offset)
    {
        TargetId = targetId;
        Offset = offset;
    }

    /// <summary>Gets the stable authored target identifier.</summary>
    public string TargetId { get; }

    /// <summary>Gets the retained logical scroll position.</summary>
    public ScrollOffset Offset { get; }
}

/// <summary>Immutable focus and viewport state owned by one navigation journal entry.</summary>
public sealed class NavigationEntryInteractionState
{
    private readonly ReadOnlyCollection<NavigationViewportPosition> _viewports;

    internal NavigationEntryInteractionState(
        string? focusTargetId,
        IReadOnlyList<NavigationViewportPosition> viewports
    )
    {
        ArgumentNullException.ThrowIfNull(viewports);
        FocusTargetId = focusTargetId;
        _viewports = Array.AsReadOnly(viewports.ToArray());
    }

    /// <summary>Gets the stable authored target that held focus, when one was registered.</summary>
    public string? FocusTargetId { get; }

    /// <summary>Gets retained viewport offsets keyed by stable authored target.</summary>
    public IReadOnlyList<NavigationViewportPosition> Viewports => _viewports;
}
