namespace Lucent.Core;

public static partial class Components
{
    private const float DefaultChoiceRowHeight = 36f;

    /// <summary>Creates a virtualized, controlled, single-selection ListBox.</summary>
    [LucentComponent]
    public static AuthorRecipe<StyledCapability> ListBox<TKey>(
        string label,
        Func<IEnumerable<ChoiceItem<TKey>>> items,
        Func<TKey> readSelectedKey,
        Action<TKey> onSelectionRequested,
        ListBoxSelectionMode selectionMode = ListBoxSelectionMode.FollowsFocus,
        float rowHeight = DefaultChoiceRowHeight,
        Style? style = null,
        ViewportState? viewport = null
    )
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(readSelectedKey);
        return StockRecipe.Styled(
            ListBoxCore(
                label,
                items,
                () => SelectedKey.Some(readSelectedKey()),
                onSelectionRequested,
                selectionMode,
                rowHeight,
                style,
                viewport,
                required: true
            )
        );
    }

    /// <summary>Creates a virtualized ListBox whose applied selection can explicitly be empty.</summary>
    [LucentComponent]
    public static AuthorRecipe<StyledCapability> ListBox<TKey>(
        string label,
        Func<IEnumerable<ChoiceItem<TKey>>> items,
        Func<SelectedKey<TKey>> readSelectedKey,
        Action<TKey> onSelectionRequested,
        ListBoxSelectionMode selectionMode = ListBoxSelectionMode.FollowsFocus,
        float rowHeight = DefaultChoiceRowHeight,
        Style? style = null,
        ViewportState? viewport = null
    )
        where TKey : notnull
    {
        return StockRecipe.Styled(
            ListBoxCore(
                label,
                items,
                readSelectedKey,
                onSelectionRequested,
                selectionMode,
                rowHeight,
                style,
                viewport,
                required: false
            )
        );
    }

    private static ComponentRecipe ListBoxCore<TKey>(
        string label,
        Func<IEnumerable<ChoiceItem<TKey>>> items,
        Func<SelectedKey<TKey>> readSelectedKey,
        Action<TKey> onSelectionRequested,
        ListBoxSelectionMode selectionMode,
        float rowHeight,
        Style? style,
        ViewportState? viewport,
        bool required
    )
        where TKey : notnull
    {
        ValidateListArguments(
            label,
            items,
            readSelectedKey,
            onSelectionRequested,
            selectionMode,
            rowHeight
        );
        return ComponentRecipe.Create(
            "list-box",
            (context, root) =>
            {
                var current = root.Scope.Derived(
                    () => SnapshotChoices(items()),
                    root.Name + ".items"
                );
                var policy = CreatePolicy(
                    root.Scope,
                    root.Name,
                    current,
                    readSelectedKey,
                    onSelectionRequested
                );
                var typeAhead = root.Scope.Own(
                    new TypeAheadController<TKey>(() => current.Value, policy, null)
                );
                MountListBox(
                    context,
                    root,
                    label,
                    current,
                    policy,
                    typeAhead,
                    onSelectionRequested,
                    selectionMode,
                    rowHeight,
                    style,
                    viewport,
                    required,
                    dismiss: null
                );
            }
        );
    }

    /// <summary>Creates a noneditable, single-choice Select anchored to an owned popup surface.</summary>
    [LucentComponent]
    public static AuthorRecipe<StyledAccessibleCapability> Select<TKey>(
        string label,
        Func<IEnumerable<ChoiceItem<TKey>>> items,
        Func<TKey> readSelectedKey,
        Action<TKey> onSelectionRequested,
        string placeholder = "Select an option",
        float rowHeight = DefaultChoiceRowHeight,
        Style? style = null
    )
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(readSelectedKey);
        return StockRecipe.Accessible(
            Select(
                label,
                items,
                () => SelectedKey.Some(readSelectedKey()),
                onSelectionRequested,
                placeholder,
                rowHeight,
                style
            )
        );
    }

    /// <summary>Creates a noneditable Select whose applied choice can explicitly be empty.</summary>
    [LucentComponent]
    public static AuthorRecipe<StyledAccessibleCapability> Select<TKey>(
        string label,
        Func<IEnumerable<ChoiceItem<TKey>>> items,
        Func<SelectedKey<TKey>> readSelectedKey,
        Action<TKey> onSelectionRequested,
        string placeholder = "Select an option",
        float rowHeight = DefaultChoiceRowHeight,
        Style? style = null
    )
        where TKey : notnull
    {
        ValidateListArguments(
            label,
            items,
            readSelectedKey,
            onSelectionRequested,
            ListBoxSelectionMode.ExplicitConfirmation,
            rowHeight
        );
        placeholder = Required(placeholder, nameof(placeholder));
        return StockRecipe.Accessible(
            "select",
            (context, root) =>
            {
                var current = root.Scope.Derived(
                    () => SnapshotChoices(items()),
                    root.Name + ".items"
                );
                var open = root.Scope.Signal(false, root.Name + ".open");
                var policy = CreatePolicy(
                    root.Scope,
                    root.Name,
                    current,
                    readSelectedKey,
                    onSelectionRequested
                );
                var typeAhead = root.Scope.Own(
                    new TypeAheadController<TKey>(() => current.Value, policy, null)
                );
                OwnedSurfaceRequest? surface = null;

                void Close()
                {
                    policy.CancelRoving();
                    open.Value = false;
                }

                var binding = new SelectBinding
                {
                    Label = label,
                    Value = () => SelectedLabel(current.Value, readSelectedKey(), placeholder),
                    Expanded = () => open.Value,
                    Open = () =>
                    {
                        policy.CancelRoving();
                        open.Value = true;
                    },
                    Close = Close,
                };
                SelectContent(binding, style).Apply(context, root);
                root.Scope.OnDispose(() => surface?.Dispose());
                _ = root.Scope.Effect(
                    () =>
                    {
                        if (open.Value)
                        {
                            if (surface is not null)
                                return;
                            var popup = ComponentRecipe.Create(
                                "select-popup",
                                (popupContext, popupRoot) =>
                                {
                                    MountListBox(
                                        popupContext,
                                        popupRoot,
                                        label,
                                        current,
                                        policy,
                                        typeAhead,
                                        onSelectionRequested,
                                        ListBoxSelectionMode.ExplicitConfirmation,
                                        rowHeight,
                                        Style
                                            .Empty.Bind(
                                                LayoutProperties.Height,
                                                () =>
                                                    Math.Min(240, current.Value.Length * rowHeight)
                                            )
                                            .MaxHeight(240),
                                        null,
                                        required: false,
                                        dismiss: Close
                                    );
                                }
                            );
                            surface = new OwnedSurfaceRequest(
                                root,
                                context.Theme,
                                popup,
                                interactive: true,
                                consumeOutsideClick: true,
                                closed: Close,
                                minimumWidth: 160,
                                matchAnchorWidth: true
                            );
                            root.Composition.Input.RequestSurface(surface);
                        }
                        else
                        {
                            surface?.Dispose();
                            surface = null;
                        }
                    },
                    root.Name + ".surface"
                );
            }
        );
    }

    [LucentComponent]
    internal static ComponentRecipe ChoiceRowHost(
        ChoiceRowBinding binding,
        [DefaultContent] ComponentContent content,
        Style? style = null
    ) =>
        Host(
            "choice-row",
            content,
            (context, root) =>
            {
                binding.FocusTarget = root.Scope.Own(
                    new FocusTarget(root.Scope, root.Name + ".focus-target")
                );
                Controls.ChoiceRow(root, context.Theme, binding, style);
            }
        );

    [LucentComponent]
    internal static ComponentRecipe SelectHost(
        SelectBinding binding,
        [DefaultContent] ComponentContent content,
        Style? style = null
    ) =>
        Host(
            "select-anchor",
            content,
            (context, root) => Controls.SelectAnchor(root, context.Theme, binding, style)
        );

    private static KeyedSelectionPolicy<TKey, ChoiceItem<TKey>> CreatePolicy<TKey>(
        ReactiveScope scope,
        string name,
        Derived<ChoiceItem<TKey>[]> current,
        Func<SelectedKey<TKey>> selected,
        Action<TKey> request
    )
        where TKey : notnull =>
        new(
            scope,
            name,
            () => current.Value,
            item => item.Key,
            item => item.Enabled,
            selected,
            request,
            KeyedSelectionCommitMode.OnConfirmation,
            KeyedSelectionBoundaryMode.Clamp
        );

    private static void MountListBox<TKey>(
        MountContext context,
        Element root,
        string label,
        Derived<ChoiceItem<TKey>[]> current,
        KeyedSelectionPolicy<TKey, ChoiceItem<TKey>> policy,
        TypeAheadController<TKey> typeAhead,
        Action<TKey> request,
        ListBoxSelectionMode mode,
        float rowHeight,
        Style? style,
        ViewportState? viewport,
        bool required,
        Action? dismiss,
        Action<Func<Key, bool>>? registerOwnerNavigation = null
    )
        where TKey : notnull
    {
        var viewportStyle = Style.Empty.Set(LayoutProperties.Height, 240f).With(style);
        var scroll = Controls.ScrollViewport(
            root,
            context.Theme,
            label,
            style: viewportStyle,
            viewport: viewport
        );
        var region = context.Virtualize(
            root,
            "choices",
            () => current.Value,
            item => item.Key,
            (item, itemContext) =>
            {
                var binding = new ChoiceRowBinding
                {
                    CollectionIndex = () =>
                        ChoicePosition(current.Value, item.Value.Key) is { } position
                            ? position - 1
                            : null,
                    Label = () => item.Value.Label,
                    Enabled = () => item.Value.Enabled,
                    Selected = () => policy.IsApplied(item.Value.Key),
                    Active = () => policy.IsRoving(item.Value.Key),
                    Position = () => ChoicePosition(current.Value, item.Value.Key),
                    Size = () =>
                        ChoicePosition(current.Value, item.Value.Key) is null
                            ? null
                            : current.Value.Length,
                    Register = (behavior, focusTarget) =>
                        policy.RegisterTarget(item.Value.Key, behavior, focusTarget),
                    Activate = (behavior, shouldRequest) =>
                        policy.Activate(item.Value.Key, shouldRequest)
                        && policy.Focus(item.Value.Key, behavior),
                    Move = (key, behavior) =>
                        MoveChoice(key, behavior, root, current.Value, policy, scroll, rowHeight),
                    Confirm = policy.RequestRoving,
                    RequestOnFocus = () => mode == ListBoxSelectionMode.FollowsFocus,
                    Search = typeAhead.Search,
                    DismissWithoutCommit = dismiss,
                    CommitAndDismiss = dismiss is null
                        ? null
                        : () =>
                        {
                            if (policy.TryGetRoving(out var key))
                                request(key);
                            dismiss();
                        },
                };
                var visual =
                    item.Value.Content?.Invoke() ?? SelectionDecoration(() => item.Value.Label);
                ArgumentNullException.ThrowIfNull(visual);
                return ChoiceRowContent(binding, visual).Mount(itemContext);
            },
            rowHeight
        );
        Controls.ListBox(
            region.Region,
            context.Theme,
            new(
                label,
                required,
                () => current.Value.Length,
                () => SelectedChoiceIndex(current.Value, policy),
                index => RevealChoiceAt(root, current.Value.Length, index, scroll, rowHeight)
            )
        );
        registerOwnerNavigation?.Invoke(key =>
            MoveChoice(key, null, root, current.Value, policy, scroll, rowHeight)
        );
        region.Configure();
    }

    private static bool MoveChoice<TKey>(
        Key key,
        BehaviorContext? behavior,
        Element viewport,
        ChoiceItem<TKey>[] items,
        KeyedSelectionPolicy<TKey, ChoiceItem<TKey>> policy,
        ScrollViewportState scroll,
        float rowHeight
    )
        where TKey : notnull
    {
        bool moved;
        var paging = key is Key.PageUp or Key.PageDown;
        if (paging)
        {
            var identity = new ElementIdentity(viewport.Composition.Epoch, viewport.Id);
            if (viewport.Composition.Input.GetSemanticScroll(identity) is not { } state)
                return true;
            var rows = Math.Max(1, (int)Math.Floor(state.Viewport.Height / rowHeight) - 1);
            var delta = key == Key.PageUp ? -rows : rows;
            moved = behavior is null
                ? policy.MoveBySourceRows(delta)
                : policy.MoveBySourceRows(delta, behavior);
        }
        else
            moved = behavior is null ? policy.Move(key) : policy.Move(key, behavior);
        if (moved && policy.TryGetRoving(out var active))
            RevealChoice(viewport, items, active, scroll, rowHeight);
        return paging || moved;
    }

    private static void ValidateListArguments<TKey>(
        string label,
        Func<IEnumerable<ChoiceItem<TKey>>> items,
        Func<SelectedKey<TKey>> selected,
        Action<TKey> request,
        ListBoxSelectionMode mode,
        float rowHeight
    )
        where TKey : notnull
    {
        _ = Required(label, nameof(label));
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (!float.IsFinite(rowHeight) || rowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowHeight));
    }

    private static ChoiceItem<TKey>[] SnapshotChoices<TKey>(IEnumerable<ChoiceItem<TKey>> source)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = source.ToArray();
        var keys = new HashSet<TKey>();
        foreach (var item in result)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!keys.Add(item.Key))
                throw new ArgumentException("Choice keys must be unique.", nameof(source));
        }
        return result;
    }

    private static int? ChoicePosition<TKey>(ChoiceItem<TKey>[] items, TKey key)
        where TKey : notnull
    {
        for (var index = 0; index < items.Length; index++)
            if (EqualityComparer<TKey>.Default.Equals(items[index].Key, key))
                return index + 1;
        return null;
    }

    private static string SelectedLabel<TKey>(
        IReadOnlyList<ChoiceItem<TKey>> items,
        SelectedKey<TKey> selected,
        string placeholder
    )
        where TKey : notnull
    {
        if (!selected.HasValue)
            return placeholder;
        foreach (var item in items)
            if (EqualityComparer<TKey>.Default.Equals(item.Key, selected.Value))
                return item.Label;
        return "Unavailable selection";
    }

    private static void RevealChoice<TKey>(
        Element viewport,
        ChoiceItem<TKey>[] items,
        TKey key,
        ScrollViewportState scroll,
        float rowHeight
    )
        where TKey : notnull
    {
        if (ChoicePosition(items, key) is not { } semanticPosition)
            return;
        var position = semanticPosition - 1;
        var bounds = viewport.Composition.Input.Bounds(
            new(viewport.Composition.Epoch, viewport.Id)
        );
        var height = bounds?.Height ?? 240f;
        var top = position * rowHeight;
        var bottom = top + rowHeight;
        if (top < scroll.Offset.Y)
            scroll.Offset = new(scroll.Offset.X, top);
        else if (bottom > scroll.Offset.Y + height)
            scroll.Offset = new(scroll.Offset.X, Math.Max(0, bottom - height));
    }

    private static int? SelectedChoiceIndex<TKey>(
        ChoiceItem<TKey>[] items,
        KeyedSelectionPolicy<TKey, ChoiceItem<TKey>> policy
    )
        where TKey : notnull
    {
        for (var index = 0; index < items.Length; index++)
            if (policy.IsApplied(items[index].Key))
                return index;
        return null;
    }

    private static void RevealChoiceAt(
        Element viewport,
        int count,
        int index,
        ScrollViewportState scroll,
        float rowHeight
    )
    {
        if (index < 0 || index >= count)
            return;
        var bounds = viewport.Composition.Input.Bounds(
            new(viewport.Composition.Epoch, viewport.Id)
        );
        var height = bounds?.Height ?? 240f;
        var top = index * rowHeight;
        var bottom = top + rowHeight;
        if (top < scroll.Offset.Y)
            scroll.Offset = new(scroll.Offset.X, top);
        else if (bottom > scroll.Offset.Y + height)
            scroll.Offset = new(scroll.Offset.X, Math.Max(0, bottom - height));
    }
}
