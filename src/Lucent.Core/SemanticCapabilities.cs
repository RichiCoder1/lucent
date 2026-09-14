namespace Lucent.Core;

/// <summary>Explicit invoke-pattern support and its portable operation.</summary>
public readonly record struct SemanticInvokeCapability(bool CanInvoke);

/// <summary>Explicit scroll-pattern support and its portable operation.</summary>
public readonly record struct SemanticScrollCapability(bool CanScroll);

/// <summary>Applied toggle state and explicit toggle operation support.</summary>
public readonly record struct SemanticToggleCapability(SemanticToggleState State, bool CanToggle);

/// <summary>Applied expansion state and explicit expand/collapse operation support.</summary>
public readonly record struct SemanticExpansionCapability(bool Expanded, bool CanExpandCollapse);

/// <summary>Value and text pattern support for an editor or other value-bearing control.</summary>
public sealed class SemanticValueCapability
{
    private SemanticValueCapability(
        SemanticTextSnapshot? text,
        bool isConfidential,
        bool canSetValue,
        bool canSelectText,
        bool canScrollTextIntoView
    )
    {
        Text = text;
        IsConfidential = isConfidential;
        CanSetValue = canSetValue;
        CanSelectText = canSelectText;
        CanScrollTextIntoView = canScrollTextIntoView;
    }

    /// <summary>Gets visible editable text and its current selection, when text ranges are exposed.</summary>
    public SemanticTextSnapshot? Text { get; }

    /// <summary>Gets whether content must be excluded from semantic values and text ranges.</summary>
    public bool IsConfidential { get; }

    /// <summary>Gets whether replacement values may be requested.</summary>
    public bool CanSetValue { get; }

    /// <summary>Gets whether text-range selection may be requested.</summary>
    public bool CanSelectText { get; }

    /// <summary>Gets whether a text range may be requested in view.</summary>
    public bool CanScrollTextIntoView { get; }

    internal static SemanticValueCapability Pattern(bool canSetValue) =>
        new(null, false, canSetValue, false, false);

    internal static SemanticValueCapability Editing(
        SemanticTextSnapshot text,
        bool canSetValue,
        bool canSelectText,
        bool canScrollTextIntoView
    ) => new(text, false, canSetValue, canSelectText, canScrollTextIntoView);

    internal static SemanticValueCapability Confidential(bool canSetValue) =>
        new(null, true, canSetValue, false, false);
}

/// <summary>Finite numeric range state and explicit range mutation support.</summary>
public readonly record struct SemanticRangeCapability(
    SemanticRangeSnapshot Snapshot,
    bool CanSetValue
);

/// <summary>Selection policy for an explicit selection container pattern.</summary>
public readonly record struct SemanticSelectionContainerCapability(
    SemanticSelectionSnapshot Snapshot
);

/// <summary>Applied selection state and explicit selection-item operation support.</summary>
public readonly record struct SemanticSelectionItemCapability(bool Selected, bool CanSelect);

/// <summary>Logical collection state and explicit item-realization operation support.</summary>
public readonly record struct SemanticCollectionCapability(
    SemanticCollectionSnapshot Snapshot,
    bool CanRealizeItem
);

/// <summary>One-based membership in a complete logical set.</summary>
public readonly record struct SemanticSetMembershipCapability(int Position, int Size);

/// <summary>One-based hierarchical level for a semantic item.</summary>
public readonly record struct SemanticHierarchyCapability(int Level);

/// <summary>Zero-based position in a complete flattened collection.</summary>
public readonly record struct SemanticCollectionItemCapability(int Index);

/// <summary>Logical grid dimensions and retained column headers.</summary>
public readonly record struct SemanticGridCapability(SemanticGridSnapshot Snapshot);

/// <summary>Logical grid position for one realized item.</summary>
public readonly record struct SemanticGridItemCapability(SemanticGridItemSnapshot Snapshot);

/// <summary>Fixed immutable aggregate of explicitly declared semantic capabilities.</summary>
public sealed class SemanticCapabilitySet
{
    internal static SemanticCapabilitySet Empty { get; } = new();

    internal SemanticCapabilitySet(
        SemanticInvokeCapability? invoke = null,
        SemanticScrollCapability? scroll = null,
        SemanticToggleCapability? toggle = null,
        SemanticExpansionCapability? expansion = null,
        SemanticValueCapability? value = null,
        SemanticRangeCapability? range = null,
        SemanticSelectionContainerCapability? selectionContainer = null,
        SemanticSelectionItemCapability? selectionItem = null,
        SemanticCollectionCapability? collection = null,
        SemanticSetMembershipCapability? setMembership = null,
        SemanticHierarchyCapability? hierarchy = null,
        SemanticCollectionItemCapability? collectionItem = null,
        SemanticGridCapability? grid = null,
        SemanticGridItemCapability? gridItem = null
    )
    {
        Invoke = invoke;
        Scroll = scroll;
        Toggle = toggle;
        Expansion = expansion;
        Value = value;
        Range = range;
        SelectionContainer = selectionContainer;
        SelectionItem = selectionItem;
        Collection = collection;
        SetMembership = setMembership;
        Hierarchy = hierarchy;
        CollectionItem = collectionItem;
        Grid = grid;
        GridItem = gridItem;

        var actions = SemanticAction.None;
        if (invoke is { CanInvoke: true })
            actions |= SemanticAction.Invoke;
        if (scroll is { CanScroll: true })
            actions |= SemanticAction.Scroll;
        if (toggle is { CanToggle: true })
            actions |= SemanticAction.Toggle;
        if (expansion is { CanExpandCollapse: true })
            actions |= SemanticAction.ExpandCollapse;
        if (value is { CanSetValue: true })
            actions |= SemanticAction.SetValue;
        if (value is { CanSelectText: true })
            actions |= SemanticAction.SelectText;
        if (value is { CanScrollTextIntoView: true })
            actions |= SemanticAction.ScrollTextIntoView;
        if (range is { CanSetValue: true })
            actions |= SemanticAction.SetRangeValue;
        if (selectionItem is { CanSelect: true })
            actions |= SemanticAction.Select;
        if (collection is { CanRealizeItem: true })
            actions |= SemanticAction.RealizeItem;
        Actions = actions;
    }

    /// <summary>Gets explicit invoke-pattern support.</summary>
    public SemanticInvokeCapability? Invoke { get; }

    /// <summary>Gets explicit scroll-pattern support.</summary>
    public SemanticScrollCapability? Scroll { get; }

    /// <summary>Gets explicit toggle-pattern state and operations.</summary>
    public SemanticToggleCapability? Toggle { get; }

    /// <summary>Gets explicit expansion-pattern state and operations.</summary>
    public SemanticExpansionCapability? Expansion { get; }

    /// <summary>Gets explicit value and text-pattern state and operations.</summary>
    public SemanticValueCapability? Value { get; }

    /// <summary>Gets explicit range-pattern state and operations.</summary>
    public SemanticRangeCapability? Range { get; }

    /// <summary>Gets explicit selection-container pattern state.</summary>
    public SemanticSelectionContainerCapability? SelectionContainer { get; }

    /// <summary>Gets explicit selection-item pattern state and operations.</summary>
    public SemanticSelectionItemCapability? SelectionItem { get; }

    /// <summary>Gets explicit logical-collection state and operations.</summary>
    public SemanticCollectionCapability? Collection { get; }

    /// <summary>Gets complete logical set membership.</summary>
    public SemanticSetMembershipCapability? SetMembership { get; }

    /// <summary>Gets hierarchical item metadata.</summary>
    public SemanticHierarchyCapability? Hierarchy { get; }

    /// <summary>Gets flattened logical collection membership.</summary>
    public SemanticCollectionItemCapability? CollectionItem { get; }

    /// <summary>Gets explicit grid-container pattern state.</summary>
    public SemanticGridCapability? Grid { get; }

    /// <summary>Gets explicit grid-item pattern state.</summary>
    public SemanticGridItemCapability? GridItem { get; }

    /// <summary>Gets portable operations compiled from explicit capability support.</summary>
    public SemanticAction Actions { get; }
}

/// <summary>Accessible state supplied by a behavior for one retained element.</summary>
public sealed class SemanticDeclaration
{
    private SemanticDeclaration(
        SemanticRole role,
        string name,
        bool enabled,
        bool focused,
        string? value,
        SemanticRelationships? relationships,
        string? description,
        SemanticAnnouncement announcement,
        SemanticCapabilitySet capabilities
    )
    {
        Role = role;
        Name = name;
        Enabled = enabled;
        Focused = focused;
        Value = value;
        Relationships = relationships;
        Description = description;
        Announcement = announcement;
        Capabilities = capabilities;
    }

    /// <summary>Begins one validated immutable semantic declaration.</summary>
    public static SemanticDeclarationBuilder Create(SemanticRole role, string name) =>
        new(role, name);

    /// <summary>Gets the accessible role.</summary>
    public SemanticRole Role { get; }

    /// <summary>Gets the nonempty accessible name.</summary>
    public string Name { get; }

    /// <summary>Gets whether semantic actions are currently enabled.</summary>
    public bool Enabled { get; }

    /// <summary>Gets whether the element is currently focused.</summary>
    public bool Focused { get; }

    /// <summary>Gets whether the element is currently selected.</summary>
    public bool Selected => Capabilities.SelectionItem?.Selected ?? false;

    /// <summary>Gets portable operations compiled from explicit capability support.</summary>
    public SemanticAction Actions => Capabilities.Actions;

    /// <summary>Gets the optional formatted accessible value.</summary>
    public string? Value { get; }

    /// <summary>Gets visible editable text and its current selection, when exposed.</summary>
    public SemanticTextSnapshot? Text => Capabilities.Value?.Text;

    /// <summary>Gets applied expansion state when the capability is declared.</summary>
    public bool? Expanded => Capabilities.Expansion?.Expanded;

    /// <summary>Gets an immutable numeric range when the capability is declared.</summary>
    public SemanticRangeSnapshot? Range => Capabilities.Range?.Snapshot;

    /// <summary>Gets stable label, help, and validation relationships.</summary>
    public SemanticRelationships? Relationships { get; }

    /// <summary>Gets applied toggle state when the capability is declared.</summary>
    public SemanticToggleState? ToggleState => Capabilities.Toggle?.State;

    /// <summary>Gets selection-container policy when the capability is declared.</summary>
    public SemanticSelectionSnapshot? Selection => Capabilities.SelectionContainer?.Snapshot;

    /// <summary>Gets optional supplemental text associated with this semantic control.</summary>
    public string? Description { get; }

    /// <summary>Gets one-based position in the complete logical set.</summary>
    public int? PositionInSet => Capabilities.SetMembership?.Position;

    /// <summary>Gets the complete logical set size.</summary>
    public int? SizeOfSet => Capabilities.SetMembership?.Size;

    /// <summary>Gets whether this editor contains confidential text.</summary>
    public bool IsPassword => Capabilities.Value?.IsConfidential == true;

    /// <summary>Gets complete logical collection state.</summary>
    public SemanticCollectionSnapshot? Collection => Capabilities.Collection?.Snapshot;

    /// <summary>Gets one-based hierarchical level for a tree item.</summary>
    public int? Level => Capabilities.Hierarchy?.Level;

    /// <summary>Gets zero-based index in the complete flattened collection.</summary>
    public int? CollectionIndex => Capabilities.CollectionItem?.Index;

    /// <summary>Gets logical grid dimensions and retained column headers.</summary>
    public SemanticGridSnapshot? Grid => Capabilities.Grid?.Snapshot;

    /// <summary>Gets a realized cell's logical grid position.</summary>
    public SemanticGridItemSnapshot? GridItem => Capabilities.GridItem?.Snapshot;

    /// <summary>Gets the explicit platform accessibility announcement policy.</summary>
    public SemanticAnnouncement Announcement { get; }

    /// <summary>Gets the shared immutable capability aggregate.</summary>
    public SemanticCapabilitySet Capabilities { get; }

    internal SemanticDeclaration WithMetadata(string name, string? description)
    {
        ValidateMetadata(Role, name, description, Announcement);
        return new(
            Role,
            name,
            Enabled,
            Focused,
            Value,
            Relationships,
            description,
            Announcement,
            Capabilities
        );
    }

    internal static SemanticDeclaration Build(SemanticDeclarationBuilder builder)
    {
        ValidateMetadata(
            builder.Role,
            builder.Name,
            builder.DescriptionValue,
            builder.AnnouncementValue
        );
        if (builder.ConfidentialValueSupplied)
            throw new ArgumentException(
                "Confidential editing must omit the accessible value regardless of builder order."
            );
        var capabilities = builder.BuildCapabilities();
        ValidateCapabilities(builder.Role, builder.ValueText, capabilities);
        return new(
            builder.Role,
            builder.Name,
            builder.EnabledValue,
            builder.FocusedValue,
            builder.ValueText,
            builder.RelationshipsValue,
            builder.DescriptionValue,
            builder.AnnouncementValue,
            capabilities
        );
    }

    private static void ValidateMetadata(
        SemanticRole role,
        string name,
        string? description,
        SemanticAnnouncement announcement
    )
    {
        if (!Enum.IsDefined(role))
            throw new ArgumentException("Semantic role must be finite.", nameof(role));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A semantic name is required.", nameof(name));
        if (description is not null && string.IsNullOrWhiteSpace(description))
            throw new ArgumentException(
                "A semantic description must be nonempty when supplied.",
                nameof(description)
            );
        if (!Enum.IsDefined(announcement))
            throw new ArgumentException(
                "Semantic announcement policy must be finite.",
                nameof(announcement)
            );
    }

    private static void ValidateCapabilities(
        SemanticRole role,
        string? value,
        SemanticCapabilitySet capabilities
    )
    {
        if (capabilities.Toggle is { State: var toggle } && !Enum.IsDefined(toggle))
            throw new ArgumentException("Toggle capability state must be finite.");
        if (capabilities.Toggle is { CanToggle: false })
            throw new ArgumentException(
                "Toggle capability requires explicit toggle operation support."
            );
        if (capabilities.Expansion is { CanExpandCollapse: false })
            throw new ArgumentException(
                "Expansion capability requires explicit expand/collapse operation support."
            );
        if (role is SemanticRole.CheckBox or SemanticRole.Switch && capabilities.Toggle is null)
            throw new ArgumentException($"{role} requires a toggle capability.");
        if (
            role == SemanticRole.Switch
            && capabilities.Toggle is { State: SemanticToggleState.Indeterminate }
        )
            throw new ArgumentException("Switch toggle capability cannot be indeterminate.");
        if (role == SemanticRole.Splitter && capabilities.Range is null)
            throw new ArgumentException("Splitter requires a range capability.");
        if (
            role
                is SemanticRole.List
                    or SemanticRole.RadioGroup
                    or SemanticRole.TabList
                    or SemanticRole.Tree
                    or SemanticRole.Calendar
                    or SemanticRole.Table
            && capabilities.SelectionContainer is null
        )
            throw new ArgumentException($"{role} requires a selection-container capability.");
        if (role == SemanticRole.ComboBox && capabilities.Value is null)
            throw new ArgumentException("ComboBox requires a value-pattern capability.");
        if (role == SemanticRole.TreeItem && capabilities.Hierarchy is null)
            throw new ArgumentException("TreeItem requires a hierarchy capability.");
        if (
            capabilities.Value is { IsConfidential: true }
            && (role != SemanticRole.TextField || value is not null)
        )
            throw new ArgumentException(
                "Confidential editing requires TextField role and must omit the accessible value."
            );
    }
}

/// <summary>Validated fluent construction for one immutable semantic declaration.</summary>
public sealed class SemanticDeclarationBuilder
{
    private SemanticInvokeCapability? _invoke;
    private SemanticScrollCapability? _scroll;
    private SemanticToggleCapability? _toggle;
    private SemanticExpansionCapability? _expansion;
    private SemanticValueCapability? _value;
    private SemanticRangeCapability? _range;
    private SemanticSelectionContainerCapability? _selectionContainer;
    private SemanticSelectionItemCapability? _selectionItem;
    private SemanticCollectionCapability? _collection;
    private SemanticSetMembershipCapability? _setMembership;
    private SemanticHierarchyCapability? _hierarchy;
    private SemanticCollectionItemCapability? _collectionItem;
    private SemanticGridCapability? _grid;
    private SemanticGridItemCapability? _gridItem;

    internal SemanticDeclarationBuilder(SemanticRole role, string name)
    {
        Role = role;
        Name = name;
    }

    internal SemanticRole Role { get; }
    internal string Name { get; }
    internal bool EnabledValue { get; private set; } = true;
    internal bool FocusedValue { get; private set; }
    internal string? ValueText { get; private set; }
    internal SemanticRelationships? RelationshipsValue { get; private set; }
    internal string? DescriptionValue { get; private set; }
    internal SemanticAnnouncement AnnouncementValue { get; private set; }
    internal bool ConfidentialValueSupplied { get; private set; }

    /// <summary>Sets whether semantic operations are currently enabled.</summary>
    public SemanticDeclarationBuilder Enabled(bool enabled)
    {
        EnabledValue = enabled;
        return this;
    }

    /// <summary>Sets the behavior-declared focused state.</summary>
    public SemanticDeclarationBuilder Focused(bool focused)
    {
        FocusedValue = focused;
        return this;
    }

    /// <summary>Sets an optional formatted accessible value.</summary>
    public SemanticDeclarationBuilder Value(string? value)
    {
        if (value is not null && _value is { IsConfidential: true })
        {
            ConfidentialValueSupplied = true;
            return this;
        }
        ValueText = value;
        return this;
    }

    /// <summary>Sets stable accessibility relationships and validation metadata.</summary>
    public SemanticDeclarationBuilder Relationships(SemanticRelationships? relationships)
    {
        RelationshipsValue = relationships;
        return this;
    }

    /// <summary>Sets an optional nonempty semantic description.</summary>
    public SemanticDeclarationBuilder Description(string? description)
    {
        DescriptionValue = description;
        return this;
    }

    /// <summary>Sets the explicit platform announcement policy.</summary>
    public SemanticDeclarationBuilder Announcement(SemanticAnnouncement announcement)
    {
        AnnouncementValue = announcement;
        return this;
    }

    /// <summary>Declares invoke-pattern support when invocation is available.</summary>
    public SemanticDeclarationBuilder Invoke(bool canInvoke = true)
    {
        if (!canInvoke)
            return this;
        Duplicate(_invoke, "invoke");
        _invoke = new(true);
        return this;
    }

    /// <summary>Declares scroll-pattern support when scrolling is available.</summary>
    public SemanticDeclarationBuilder Scroll(bool canScroll = true)
    {
        if (!canScroll)
            return this;
        Duplicate(_scroll, "scroll");
        _scroll = new(true);
        return this;
    }

    /// <summary>Declares applied toggle state and operation support.</summary>
    public SemanticDeclarationBuilder Toggle(SemanticToggleState? state, bool canToggle)
    {
        if (state is null)
        {
            MissingOperation(canToggle, "toggle");
            return this;
        }
        Duplicate(_toggle, "toggle");
        _toggle = new(state.Value, canToggle);
        return this;
    }

    /// <summary>Declares applied expansion state and operation support.</summary>
    public SemanticDeclarationBuilder Expansion(bool? expanded, bool canExpandCollapse)
    {
        if (expanded is null)
        {
            MissingOperation(canExpandCollapse, "expansion");
            return this;
        }
        Duplicate(_expansion, "expansion");
        _expansion = new(expanded.Value, canExpandCollapse);
        return this;
    }

    /// <summary>Declares a value pattern without text-range content.</summary>
    public SemanticDeclarationBuilder ValuePattern(bool canSetValue)
    {
        Duplicate(_value, "value-pattern");
        _value = SemanticValueCapability.Pattern(canSetValue);
        return this;
    }

    /// <summary>Declares visible editing data and its supported operations.</summary>
    public SemanticDeclarationBuilder Editing(
        SemanticTextSnapshot? text,
        bool canSetValue,
        bool canSelectText,
        bool canScrollTextIntoView
    )
    {
        if (text is null)
        {
            MissingOperation(canSetValue || canSelectText || canScrollTextIntoView, "editing");
            return this;
        }
        Duplicate(_value, "value-pattern/editing");
        _value = SemanticValueCapability.Editing(
            text,
            canSetValue,
            canSelectText,
            canScrollTextIntoView
        );
        return this;
    }

    /// <summary>Declares confidential editing without accepting value or text content.</summary>
    public SemanticDeclarationBuilder ConfidentialEditing(bool canSetValue)
    {
        Duplicate(_value, "value-pattern/editing");
        if (ValueText is not null)
        {
            ConfidentialValueSupplied = true;
            ValueText = null;
        }
        _value = SemanticValueCapability.Confidential(canSetValue);
        return this;
    }

    /// <summary>Declares finite range state and operation support.</summary>
    public SemanticDeclarationBuilder Range(SemanticRangeSnapshot? range, bool canSetValue)
    {
        if (range is null)
        {
            MissingOperation(canSetValue, "range");
            return this;
        }
        Duplicate(_range, "range");
        if ((!range.IsReadOnly) != canSetValue)
            throw new ArgumentException(
                "Range capability read-only state must agree with SetRangeValue support.",
                nameof(canSetValue)
            );
        _range = new(range, canSetValue);
        return this;
    }

    /// <summary>Declares selection-container pattern policy.</summary>
    public SemanticDeclarationBuilder SelectionContainer(SemanticSelectionSnapshot? selection)
    {
        if (selection is null)
            return this;
        Duplicate(_selectionContainer, "selection-container");
        _selectionContainer = new(selection);
        return this;
    }

    /// <summary>Declares applied selection-item state and operation support.</summary>
    public SemanticDeclarationBuilder SelectionItem(bool selected, bool canSelect)
    {
        Duplicate(_selectionItem, "selection-item");
        _selectionItem = new(selected, canSelect);
        return this;
    }

    /// <summary>Declares logical collection state and item-realization support.</summary>
    public SemanticDeclarationBuilder Collection(
        SemanticCollectionSnapshot? collection,
        bool canRealizeItem
    )
    {
        if (collection is null)
        {
            MissingOperation(canRealizeItem, "collection");
            return this;
        }
        Duplicate(_collection, "collection");
        _collection = new(collection, canRealizeItem);
        return this;
    }

    /// <summary>Declares optional one-based membership in a complete logical set.</summary>
    public SemanticDeclarationBuilder SetMembership(int? position, int? size)
    {
        if (position.HasValue != size.HasValue)
            throw new ArgumentException(
                "Set-membership capability requires both position and size."
            );
        if (position is null)
            return this;
        Duplicate(_setMembership, "set-membership");
        if (position < 1 || size < position)
            throw new ArgumentException(
                "Set-membership capability requires a one-based position within its size."
            );
        _setMembership = new(position.Value, size!.Value);
        return this;
    }

    /// <summary>Declares an optional one-based hierarchy level.</summary>
    public SemanticDeclarationBuilder Hierarchy(int? level)
    {
        if (level is null)
            return this;
        Duplicate(_hierarchy, "hierarchy");
        if (level <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(level),
                "Hierarchy capability level must be positive."
            );
        _hierarchy = new(level.Value);
        return this;
    }

    /// <summary>Declares an optional zero-based flattened collection index.</summary>
    public SemanticDeclarationBuilder CollectionItem(int? index)
    {
        if (index is null)
            return this;
        Duplicate(_collectionItem, "collection-item");
        if (index < 0)
            throw new ArgumentOutOfRangeException(
                nameof(index),
                "Collection-item capability index cannot be negative."
            );
        _collectionItem = new(index.Value);
        return this;
    }

    /// <summary>Declares optional grid-container pattern state.</summary>
    public SemanticDeclarationBuilder Grid(SemanticGridSnapshot? grid)
    {
        if (grid is null)
            return this;
        Duplicate(_grid, "grid");
        _grid = new(grid);
        return this;
    }

    /// <summary>Declares optional grid-item pattern state.</summary>
    public SemanticDeclarationBuilder GridItem(SemanticGridItemSnapshot? gridItem)
    {
        if (gridItem is null)
            return this;
        Duplicate(_gridItem, "grid-item");
        _gridItem = new(gridItem);
        return this;
    }

    /// <summary>Validates and publishes one immutable declaration.</summary>
    public SemanticDeclaration Build() => SemanticDeclaration.Build(this);

    internal SemanticCapabilitySet BuildCapabilities() =>
        _invoke is null
        && _scroll is null
        && _toggle is null
        && _expansion is null
        && _value is null
        && _range is null
        && _selectionContainer is null
        && _selectionItem is null
        && _collection is null
        && _setMembership is null
        && _hierarchy is null
        && _collectionItem is null
        && _grid is null
        && _gridItem is null
            ? SemanticCapabilitySet.Empty
            : new(
                _invoke,
                _scroll,
                _toggle,
                _expansion,
                _value,
                _range,
                _selectionContainer,
                _selectionItem,
                _collection,
                _setMembership,
                _hierarchy,
                _collectionItem,
                _grid,
                _gridItem
            );

    private static void Duplicate<T>(T? existing, string capability)
        where T : struct
    {
        if (existing.HasValue)
            throw new InvalidOperationException(
                $"Semantic {capability} capability is already declared."
            );
    }

    private static void Duplicate(object? existing, string capability)
    {
        if (existing is not null)
            throw new InvalidOperationException(
                $"Semantic {capability} capability is already declared."
            );
    }

    private static void MissingOperation(bool requested, string capability)
    {
        if (requested)
            throw new ArgumentException(
                $"Semantic {capability} operations require their capability payload."
            );
    }
}
