namespace Lucent.Core;

public static partial class Components
{
    private const float DefaultTreeRowHeight = 36f;

    /// <summary>Creates a controlled keyed tree that virtualizes its flattened expanded rows.</summary>
    [LucentComponent]
    public static ComponentRecipe TreeView<TKey, TItem>(
        string label,
        Func<IEnumerable<TItem>> roots,
        TreeDataSource<TKey, TItem> dataSource,
        Func<SelectedKey<TKey>> readSelectedKey,
        Action<TKey> onSelectionRequested,
        Func<TKey, bool> readExpanded,
        Action<TKey, bool> onExpansionRequested,
        TreeViewOptions? options = null,
        Style? style = null,
        ViewportState? viewport = null
    )
        where TKey : notnull
    {
        label = Required(label, nameof(label));
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(readSelectedKey);
        ArgumentNullException.ThrowIfNull(onSelectionRequested);
        ArgumentNullException.ThrowIfNull(readExpanded);
        ArgumentNullException.ThrowIfNull(onExpansionRequested);
        options ??= new();
        var rowHeight = options.RowHeight ?? DefaultTreeRowHeight;

        return ComponentRecipe.Create(
            "tree-view",
            (context, root) =>
            {
                var lazy = root.Scope.Own(
                    new TreeLazyStore<TKey, TItem>(root.Scope, root.Name, dataSource)
                );
                _ = root.Scope.Effect(
                    () => lazy.Synchronize(roots(), readExpanded),
                    root.Name + ".lazy-children"
                );
                var current = root.Scope.Derived(
                    () => lazy.Flatten(roots(), readExpanded),
                    root.Name + ".visible"
                );
                var policy = new KeyedSelectionPolicy<TKey, TreeVisibleNode<TKey, TItem>>(
                    root.Scope,
                    root.Name,
                    () => current.Value.Nodes,
                    node => node.Key,
                    node => node.Enabled,
                    readSelectedKey,
                    onSelectionRequested,
                    KeyedSelectionCommitMode.OnConfirmation
                );
                var targets = new Dictionary<TKey, ElementIdentity>();
                var priorTargets = new Dictionary<TKey, ElementIdentity>();
                var focusTargets = new Dictionary<TKey, FocusTarget>();
                var priorNodes = current.Value.Nodes.ToDictionary(node => node.Key);
                var scroll = Controls.ScrollViewport(
                    root,
                    context.Theme,
                    label,
                    style: Style.Empty.Height(240).With(style),
                    viewport: viewport
                );
                var region = context.Virtualize(
                    root,
                    "tree-rows",
                    () => current.Value.Rows,
                    row => row.Key,
                    (row, itemContext) =>
                        CreateTreeRow(
                            row,
                            itemContext,
                            current,
                            dataSource,
                            policy,
                            targets,
                            priorTargets,
                            focusTargets,
                            readExpanded,
                            onExpansionRequested,
                            options,
                            lazy,
                            scroll,
                            rowHeight,
                            root
                        ),
                    rowHeight
                );
                Controls.Tree(
                    region.Region,
                    context.Theme,
                    new(
                        label,
                        () => current.Value.Nodes.Length,
                        () => SelectedIndex(current.Value, readSelectedKey()),
                        index => RevealNode(root, current.Value, index, scroll, rowHeight)
                    ),
                    null
                );
                region.Configure();

                _ = root.Scope.Effect(
                    () =>
                    {
                        var next = current.Value;
                        var focused = root.Composition.Input.FocusedElement;
                        var departed = focused is not null
                            ? priorTargets.FirstOrDefault(pair => pair.Value == focused.Value)
                            : default;
                        var departedKey =
                            departed.Value != default
                                ? SelectedKey.Some(departed.Key)
                                : SelectedKey.None<TKey>();
                        if (departedKey.HasValue)
                        {
                            if (
                                !next.NodeIndices.ContainsKey(departedKey.Value)
                                && priorNodes.TryGetValue(departedKey.Value, out var node)
                            )
                            {
                                var destination = node.Parent;
                                while (
                                    destination.HasValue
                                    && !next.NodeIndices.ContainsKey(destination.Value)
                                    && priorNodes.TryGetValue(destination.Value, out var ancestor)
                                )
                                    destination = ancestor.Parent;
                                if (!destination.HasValue && next.Nodes.Length != 0)
                                    destination = SelectedKey.Some(next.Nodes[0].Key);
                                if (
                                    destination.HasValue
                                    && focusTargets.TryGetValue(destination.Value, out var target)
                                )
                                {
                                    _ = policy.Activate(destination.Value, request: false);
                                    target.Request();
                                }
                            }
                        }
                        priorNodes = next.Nodes.ToDictionary(node => node.Key);
                        foreach (var key in priorTargets.Keys.ToArray())
                            if (!next.NodeIndices.ContainsKey(key))
                                priorTargets.Remove(key);
                        foreach (var key in focusTargets.Keys.ToArray())
                            if (!next.NodeIndices.ContainsKey(key))
                            {
                                focusTargets[key].Dispose();
                                focusTargets.Remove(key);
                            }
                    },
                    root.Name + ".collapse-focus"
                );
            }
        );
    }

    [LucentComponent]
    internal static ComponentRecipe TreeRowHost(
        TreeRowBinding binding,
        [DefaultContent] ComponentContent content,
        Style? style = null
    ) =>
        Host(
            "tree-item",
            content,
            (context, root) =>
            {
                binding.FocusTarget = root.Scope.Own(
                    new FocusTarget(root.Scope, root.Name + ".focus-target")
                );
                Controls.TreeRow(root, context.Theme, binding, style);
            }
        );

    [LucentComponent]
    internal static ComponentRecipe TreeChevronHost(TreeRowBinding binding, Style? style = null) =>
        ComponentRecipe.Create(
            binding.Enabled() && binding.HasChildren()
                ? "tree-chevron-expandable"
                : "tree-chevron-leaf",
            (context, root) => Controls.TreeChevron(root, context.Theme, binding, style)
        );

    private static Element CreateTreeRow<TKey, TItem>(
        CurrentItem<TreeVisibleRow<TKey, TItem>> row,
        CompositionContext context,
        Derived<TreeVisibleSnapshot<TKey, TItem>> current,
        TreeDataSource<TKey, TItem> dataSource,
        KeyedSelectionPolicy<TKey, TreeVisibleNode<TKey, TItem>> policy,
        Dictionary<TKey, ElementIdentity> targets,
        Dictionary<TKey, ElementIdentity> priorTargets,
        Dictionary<TKey, FocusTarget> focusTargets,
        Func<TKey, bool> readExpanded,
        Action<TKey, bool> requestExpanded,
        TreeViewOptions options,
        TreeLazyStore<TKey, TItem> lazy,
        ScrollViewportState scroll,
        float rowHeight,
        Element viewport
    )
        where TKey : notnull
    {
        if (row.Value.Kind == TreeVisibleRowKind.Loading)
            return Status(
                    "Loading children",
                    Style.Empty.Padding(new Insets(8 + (row.Value.Level - 1) * 16, 6, 8, 6))
                )
                .Mount(context);
        if (row.Value.Kind == TreeVisibleRowKind.Failure)
        {
            var key = row.Value.Key.Key;
            return InlineNotice(
                    () => row.Value.Failure ?? "Children could not be loaded.",
                    NoticeSeverity.Error,
                    () => _ = lazy.Retry(key),
                    "Retry",
                    Style.Empty.Padding(new Insets(8 + (row.Value.Level - 1) * 16, 4, 8, 4))
                )
                .Mount(context);
        }

        TreeVisibleNode<TKey, TItem> Node() => row.Value.Node!;
        string Label() => Required(dataSource.Label(Node().Item), nameof(dataSource.Label));
        var binding = new TreeRowBinding
        {
            Label = Label,
            Selected = () => policy.IsApplied(Node().Key),
            Active = () => policy.IsRoving(Node().Key),
            Enabled = () => Node().Enabled,
            HasChildren = () => Node().HasChildren,
            Expanded = () => readExpanded(Node().Key),
            Level = () => Node().Level,
            Position = () => Node().Position,
            Size = () => Node().SetSize,
            CollectionIndex = () =>
                current.Value.NodeIndices.TryGetValue(Node().Key, out var index) ? index : 0,
            Register = (behavior, focusTarget) =>
            {
                var key = Node().Key;
                policy.RegisterTarget(key, behavior);
                var identity = behavior.Identity;
                targets[key] = identity;
                priorTargets[key] = identity;
                focusTargets[key] = focusTarget;
                behavior.OnDispose(() =>
                {
                    if (targets.TryGetValue(key, out var retained) && retained == identity)
                        targets.Remove(key);
                });
            },
            Activate = (behavior, request) =>
                policy.Activate(Node().Key, request) && policy.Focus(Node().Key, behavior),
            Navigate = (key, behavior) =>
                Navigate(
                    key,
                    behavior,
                    Node().Key,
                    current.Value,
                    policy,
                    readExpanded,
                    requestExpanded,
                    options.SelectionMode,
                    viewport,
                    scroll,
                    rowHeight
                ),
            RequestExpanded = value =>
            {
                var node = Node();
                if (!node.Enabled || !node.HasChildren || readExpanded(node.Key) == value)
                    return false;
                requestExpanded(node.Key, value);
                return true;
            },
            Confirm = policy.RequestRoving,
            RequestOnFocus = () => options.SelectionMode == ListBoxSelectionMode.FollowsFocus,
        };
        var visual = dataSource.Content?.Invoke(Node().Item) ?? SelectionDecoration(Label);
        ArgumentNullException.ThrowIfNull(visual);
        return TreeRowContent(binding, visual, null).Mount(context);
    }

    private static bool Navigate<TKey, TItem>(
        Key key,
        BehaviorContext behavior,
        TKey currentKey,
        TreeVisibleSnapshot<TKey, TItem> current,
        KeyedSelectionPolicy<TKey, TreeVisibleNode<TKey, TItem>> policy,
        Func<TKey, bool> expanded,
        Action<TKey, bool> requestExpanded,
        ListBoxSelectionMode selectionMode,
        Element viewport,
        ScrollViewportState scroll,
        float rowHeight
    )
        where TKey : notnull
    {
        if (!current.NodeIndices.TryGetValue(currentKey, out var index))
            return false;
        var node = current.Nodes[index];
        if (key == Key.Right)
        {
            if (!node.Enabled || !node.HasChildren)
                return false;
            if (!expanded(currentKey))
            {
                requestExpanded(currentKey, true);
                return true;
            }
            if (
                index + 1 >= current.Nodes.Length
                || current.Nodes[index + 1].Parent is not { HasValue: true } parent
                || !EqualityComparer<TKey>.Default.Equals(parent.Value, currentKey)
            )
                return false;
            var childIndex = index + 1;
            while (
                childIndex < current.Nodes.Length
                && current.Nodes[childIndex].Parent is { HasValue: true } childParent
                && EqualityComparer<TKey>.Default.Equals(childParent.Value, currentKey)
            )
            {
                if (current.Nodes[childIndex].Enabled)
                    return Activate(current.Nodes[childIndex].Key);
                childIndex++;
            }
            return false;
        }
        if (key == Key.Left)
        {
            if (node.HasChildren && expanded(currentKey))
            {
                requestExpanded(currentKey, false);
                return true;
            }
            return node.Parent.HasValue && Activate(node.Parent.Value);
        }
        var step =
            key is Key.Up ? -1
            : key is Key.Down ? 1
            : 0;
        var targetIndex = key switch
        {
            Key.Home => Array.FindIndex(current.Nodes, candidate => candidate.Enabled),
            Key.End => Array.FindLastIndex(current.Nodes, candidate => candidate.Enabled),
            Key.Up or Key.Down => index + step,
            _ => -1,
        };
        while (
            targetIndex >= 0
            && targetIndex < current.Nodes.Length
            && !current.Nodes[targetIndex].Enabled
            && step != 0
        )
            targetIndex += step;
        if (targetIndex < 0)
            return false;
        if (targetIndex >= current.Nodes.Length || targetIndex == index)
            return false;
        return Activate(current.Nodes[targetIndex].Key);

        bool Activate(TKey target)
        {
            if (!policy.Activate(target, selectionMode == ListBoxSelectionMode.FollowsFocus))
                return false;
            _ = policy.Focus(target, behavior);
            if (current.NodeIndices.TryGetValue(target, out var targetIndex))
                RevealNode(viewport, current, targetIndex, scroll, rowHeight);
            return true;
        }
    }

    private static void RevealNode<TKey, TItem>(
        Element viewport,
        TreeVisibleSnapshot<TKey, TItem> current,
        int nodeIndex,
        ScrollViewportState scroll,
        float rowHeight
    )
        where TKey : notnull
    {
        if (nodeIndex < 0 || nodeIndex >= current.Nodes.Length)
            return;
        var key = current.Nodes[nodeIndex].Key;
        if (!current.RowIndices.TryGetValue(key, out var rowIndex))
            return;
        var bounds = viewport.Composition.Input.Bounds(
            new(viewport.Composition.Epoch, viewport.Id)
        );
        var height = bounds?.Height ?? 240f;
        var top = rowIndex * rowHeight;
        var bottom = top + rowHeight;
        if (top < scroll.Offset.Y)
            scroll.Offset = new(scroll.Offset.X, top);
        else if (bottom > scroll.Offset.Y + height)
            scroll.Offset = new(scroll.Offset.X, Math.Max(0, bottom - height));
    }

    private static int? SelectedIndex<TKey, TItem>(
        TreeVisibleSnapshot<TKey, TItem> current,
        SelectedKey<TKey> selected
    )
        where TKey : notnull =>
        selected.HasValue && current.NodeIndices.TryGetValue(selected.Value, out var index)
            ? index
            : null;
}
