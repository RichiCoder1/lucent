namespace Lucent.Core;

/// <summary>Immutable accessible subtree exported from the current retained composition.</summary>
public sealed class SemanticSnapshot
{
    private readonly IReadOnlyList<SemanticSnapshot> _children;

    /// <summary>Initializes one immutable semantic node from a shared declaration payload.</summary>
    public SemanticSnapshot(
        SemanticIdentity identity,
        SemanticDeclaration payload,
        bool enabled,
        bool focused,
        bool selected,
        IReadOnlyList<SemanticSnapshot> children
    )
        : this(identity, payload, enabled, focused, selected, children, ownsChildren: false) { }

    private SemanticSnapshot(
        SemanticIdentity identity,
        SemanticDeclaration payload,
        bool enabled,
        bool focused,
        bool selected,
        IReadOnlyList<SemanticSnapshot> children,
        bool ownsChildren
    )
    {
        if (identity.CompositionEpoch <= 0 || identity.ElementId <= 0 || identity.Generation < 0)
            throw new ArgumentException(
                "Semantic snapshot identity must be retained and nonnegative.",
                nameof(identity)
            );
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        ArgumentNullException.ThrowIfNull(children);
        Identity = identity;
        Enabled = enabled;
        Focused = focused;
        Selected = selected;
        if (ownsChildren)
            _children =
                children.Count == 0 ? Array.Empty<SemanticSnapshot>()
                : children is SemanticSnapshot[] owned ? Array.AsReadOnly(owned)
                : children;
        else
        {
            var childCopy = children.ToArray();
            if (childCopy.Any(static child => child is null))
                throw new ArgumentException(
                    "Semantic snapshot children cannot contain null nodes.",
                    nameof(children)
                );
            _children =
                childCopy.Length == 0
                    ? Array.Empty<SemanticSnapshot>()
                    : Array.AsReadOnly(childCopy);
        }
    }

    internal static SemanticSnapshot CreateOwned(
        SemanticIdentity identity,
        SemanticDeclaration payload,
        bool enabled,
        bool focused,
        bool selected,
        IReadOnlyList<SemanticSnapshot> children
    ) => new(identity, payload, enabled, focused, selected, children, ownsChildren: true);

    /// <summary>Gets the stable identity for this retained generation.</summary>
    public SemanticIdentity Identity { get; }

    /// <summary>Gets the shared immutable declaration payload.</summary>
    public SemanticDeclaration Payload { get; }

    /// <summary>Gets reconciled semantic availability.</summary>
    public bool Enabled { get; }

    /// <summary>Gets reconciled focus state.</summary>
    public bool Focused { get; }

    /// <summary>Gets reconciled selection state.</summary>
    public bool Selected { get; }

    /// <summary>Gets immutable semantic children.</summary>
    public IReadOnlyList<SemanticSnapshot> Children => _children;

    /// <summary>Gets the accessible role.</summary>
    public SemanticRole Role => Payload.Role;

    /// <summary>Gets the nonempty accessible name.</summary>
    public string Name => Payload.Name;

    /// <summary>Gets the optional formatted accessible value.</summary>
    public string? Value => Payload.Value;

    /// <summary>Gets portable operations compiled from explicit capability support.</summary>
    public SemanticAction Actions => Payload.Actions;

    /// <summary>Gets visible editable text and its current selection, when exposed.</summary>
    public SemanticTextSnapshot? Text => Payload.Text;

    /// <summary>Gets applied expansion state when supported.</summary>
    public bool? Expanded => Payload.Expanded;

    /// <summary>Gets an immutable numeric range when supported.</summary>
    public SemanticRangeSnapshot? Range => Payload.Range;

    /// <summary>Gets stable label, help, and validation relationships.</summary>
    public SemanticRelationships? Relationships => Payload.Relationships;

    /// <summary>Gets applied toggle state when supported.</summary>
    public SemanticToggleState? ToggleState => Payload.ToggleState;

    /// <summary>Gets selection-container policy when supported.</summary>
    public SemanticSelectionSnapshot? Selection => Payload.Selection;

    /// <summary>Gets optional supplemental semantic text.</summary>
    public string? Description => Payload.Description;

    /// <summary>Gets one-based position in the complete logical set.</summary>
    public int? PositionInSet => Payload.PositionInSet;

    /// <summary>Gets the complete logical set size.</summary>
    public int? SizeOfSet => Payload.SizeOfSet;

    /// <summary>Gets whether this editor contains confidential text.</summary>
    public bool IsPassword => Payload.IsPassword;

    /// <summary>Gets complete logical collection state.</summary>
    public SemanticCollectionSnapshot? Collection => Payload.Collection;

    /// <summary>Gets one-based hierarchy level.</summary>
    public int? Level => Payload.Level;

    /// <summary>Gets zero-based flattened collection index.</summary>
    public int? CollectionIndex => Payload.CollectionIndex;

    /// <summary>Gets logical grid dimensions and retained column headers.</summary>
    public SemanticGridSnapshot? Grid => Payload.Grid;

    /// <summary>Gets logical grid position for one realized item.</summary>
    public SemanticGridItemSnapshot? GridItem => Payload.GridItem;

    /// <summary>Gets the explicit platform accessibility announcement policy.</summary>
    public SemanticAnnouncement Announcement => Payload.Announcement;

    internal SemanticSnapshot WithStateAndChildren(
        bool enabled,
        bool focused,
        bool selected,
        IReadOnlyList<SemanticSnapshot> children
    ) => CreateOwned(Identity, Payload, enabled, focused, selected, children);

    internal SemanticSnapshot WithChildren(IReadOnlyList<SemanticSnapshot> children) =>
        CreateOwned(Identity, Payload, Enabled, Focused, Selected, children);

    internal SemanticSnapshot WithDescription(string? description) =>
        CreateOwned(
            Identity,
            Payload.WithMetadata(Payload.Name, description),
            Enabled,
            Focused,
            Selected,
            Children
        );
}
