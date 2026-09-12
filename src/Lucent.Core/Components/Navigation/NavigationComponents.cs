namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Creates controlled keyed tabs with manual activation and retained visited panels.</summary>
    [LucentComponent]
    public static ComponentRecipe Tabs<TKey>(
        string label,
        Func<IEnumerable<TabItem<TKey>>> items,
        Func<TKey> readSelectedKey,
        Action<TKey> onSelectionRequested,
        TabActivationMode activation = TabActivationMode.Manual,
        TabPanelRetention retention = TabPanelRetention.RetainVisited,
        Style? style = null
    )
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(readSelectedKey);
        return Tabs(
            label,
            items,
            () => SelectedKey.Some(readSelectedKey()),
            onSelectionRequested,
            activation,
            retention,
            style
        );
    }

    /// <summary>Creates controlled keyed tabs whose applied selection can explicitly be empty.</summary>
    [LucentComponent]
    public static ComponentRecipe Tabs<TKey>(
        string label,
        Func<IEnumerable<TabItem<TKey>>> items,
        Func<SelectedKey<TKey>> readSelectedKey,
        Action<TKey> onSelectionRequested,
        TabActivationMode activation = TabActivationMode.Manual,
        TabPanelRetention retention = TabPanelRetention.RetainVisited,
        Style? style = null
    )
        where TKey : notnull
    {
        label = Required(label, nameof(label));
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(readSelectedKey);
        ArgumentNullException.ThrowIfNull(onSelectionRequested);
        if (!Enum.IsDefined(activation))
            throw new ArgumentOutOfRangeException(nameof(activation));
        if (!Enum.IsDefined(retention))
            throw new ArgumentOutOfRangeException(nameof(retention));
        return ComponentRecipe.Create(
            "tabs",
            (context, root) =>
            {
                var current = root.Scope.Derived(() => SnapshotTabs(items()), root.Name + ".items");
                var policy = new KeyedSelectionPolicy<TKey, TabItem<TKey>>(
                    root.Scope,
                    root.Name,
                    () => current.Value,
                    item => item.Key,
                    item => item.Enabled,
                    readSelectedKey,
                    onSelectionRequested,
                    activation == TabActivationMode.Automatic
                        ? KeyedSelectionCommitMode.OnNavigation
                        : KeyedSelectionCommitMode.OnConfirmation
                );
                var visited = root.Scope.Signal(Array.Empty<TKey>(), root.Name + ".visited");
                if (retention == TabPanelRetention.RetainVisited)
                    _ = root.Scope.Effect(
                        () => ReconcileVisited(current.Value, readSelectedKey(), visited),
                        root.Name + ".visited-reconcile"
                    );
                var headers = TabHeaders(
                    root.Name + ".headers",
                    () => current.Value,
                    item => item.Key,
                    item =>
                    {
                        var binding = new TabHeaderBinding
                        {
                            Label = () => item.Value.Label,
                            Enabled = () => item.Value.Enabled,
                            Selected = () => policy.IsApplied(item.Value.Key),
                            Roving = () => policy.IsRoving(item.Value.Key),
                            Register = behavior => policy.RegisterTarget(item.Value.Key, behavior),
                            Activate = (behavior, request) =>
                                policy.Activate(item.Value.Key, request)
                                && policy.Focus(item.Value.Key, behavior),
                            Move = policy.Move,
                            Confirm = policy.RequestRoving,
                        };
                        return TabHeaderContent(binding);
                    }
                );
                IEnumerable<TabItem<TKey>> Panels()
                {
                    if (retention == TabPanelRetention.UnmountInactive)
                        return current.Value.Where(item => policy.IsApplied(item.Key));
                    var keys = visited.Value;
                    return current.Value.Where(item => keys.Contains(item.Key));
                }
                var panels = ContentRecipe.ForEach(
                    root.Name + ".panels",
                    Panels,
                    item => item.Key,
                    item =>
                    {
                        var content =
                            item.Value.Content()
                            ?? throw new InvalidOperationException(
                                "A tab content factory returned null."
                            );
                        return TabPanelContent(
                            new(() => policy.IsApplied(item.Value.Key)),
                            content
                        );
                    }
                );
                TabsContent(new(label), [headers], [panels], style).Apply(context, root);
            }
        );
    }

    private static ContentRecipe TabHeaders<TKey, TItem>(
        string name,
        Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key,
        Func<CurrentItem<TItem>, ComponentRecipe> content
    )
        where TKey : notnull =>
        new(
            (context, parent) =>
            {
                var region = context.ForEach(
                    parent,
                    name,
                    source,
                    key,
                    (item, child) => content(item).Mount(child)
                );
                region.Region.Present(
                    context.Theme,
                    component: Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Row)
                );
            }
        );

    /// <summary>Creates a controlled disclosure that retains its lazily mounted content by default.</summary>
    [LucentComponent]
    public static ComponentRecipe Disclosure(
        [DefaultContent] ComponentContent content,
        string heading,
        Func<bool> readExpanded,
        Action<bool> onExpandedRequested,
        TabPanelRetention retention = TabPanelRetention.RetainVisited,
        Style? style = null
    )
    {
        content = Content(content);
        heading = Required(heading, nameof(heading));
        ArgumentNullException.ThrowIfNull(readExpanded);
        ArgumentNullException.ThrowIfNull(onExpandedRequested);
        if (!Enum.IsDefined(retention))
            throw new ArgumentOutOfRangeException(nameof(retention));
        return ComponentRecipe.Defer(
            "disclosure",
            scope =>
            {
                var binding = new DisclosureBinding(heading, readExpanded, onExpandedRequested);
                var visited = scope.Signal(readExpanded(), "disclosure.visited");
                if (retention == TabPanelRetention.RetainVisited)
                    _ = scope.Effect(
                        () =>
                        {
                            if (readExpanded())
                                visited.Value = true;
                        },
                        "disclosure.visit"
                    );
                var panel = ContentRecipe.When(
                    "disclosure.panel",
                    retention == TabPanelRetention.RetainVisited
                        ? () => visited.Value
                        : readExpanded,
                    DisclosurePanelHost(binding, content)
                );
                return DisclosureContent(binding, [panel], style);
            }
        );
    }

    [LucentComponent]
    internal static ComponentRecipe TabsFrame(
        [DefaultContent] ComponentContent content,
        Style? style = null
    ) =>
        RawFrame(
            "tabs",
            content,
            Style
                .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                .Set(LayoutProperties.Spacing, 8f),
            style
        );

    [LucentComponent]
    internal static ComponentRecipe TabListHost(
        TabListBinding binding,
        [DefaultContent] ComponentContent content
    ) =>
        Host(
            "tab-list",
            content,
            (context, root) => Controls.TabList(root, context.Theme, binding, null)
        );

    [LucentComponent]
    internal static ComponentRecipe TabHeaderHost(
        TabHeaderBinding binding,
        [DefaultContent] ComponentContent content
    ) => Host("tab", content, (context, root) => Controls.TabHeader(root, context.Theme, binding));

    [LucentComponent]
    internal static ComponentRecipe TabSelectionUnderline(Func<bool> selected) =>
        ComponentRecipe.Create(
            "tab-underline",
            (context, root) =>
                root.Present(
                    context.Theme,
                    Style
                        .Empty.Set(LayoutProperties.Height, 2f)
                        .Set(VisualProperties.Background, ControlThemes.Accent)
                        .Bind(
                            VisualProperties.Participation,
                            () =>
                                selected()
                                    ? ElementParticipation.Visible
                                    : ElementParticipation.Collapsed
                        )
                )
        );

    [LucentComponent]
    internal static ComponentRecipe TabPanelHost(
        TabPanelBinding binding,
        [DefaultContent] ComponentContent content
    ) =>
        RawFrame(
            "tab-panel",
            content,
            Style.Empty.Bind(
                VisualProperties.Participation,
                () =>
                    binding.Active() ? ElementParticipation.Visible : ElementParticipation.Collapsed
            ),
            null
        );

    [LucentComponent]
    internal static ComponentRecipe DisclosureFrame(
        [DefaultContent] ComponentContent content,
        Style? style = null
    ) =>
        RawFrame(
            "disclosure",
            content,
            Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Column),
            style
        );

    [LucentComponent]
    internal static ComponentRecipe DisclosureHeaderHost(
        DisclosureBinding binding,
        [DefaultContent] ComponentContent content
    ) =>
        Host(
            "disclosure-header",
            content,
            (context, root) => Controls.DisclosureHeader(root, context.Theme, binding, null)
        );

    [LucentComponent]
    internal static ComponentRecipe DisclosurePanelHost(
        DisclosureBinding binding,
        [DefaultContent] ComponentContent content
    ) =>
        ComponentRecipe.Create(
            "disclosure-panel",
            (context, root) =>
            {
                binding.Panel = root;
                root.Present(
                    context.Theme,
                    Style.Empty.Bind(
                        VisualProperties.Participation,
                        () =>
                            binding.Expanded()
                                ? ElementParticipation.Visible
                                : ElementParticipation.Collapsed
                    )
                );
                context.Mount(root, content);
            }
        );

    private static ComponentRecipe Host(
        string kind,
        ComponentContent content,
        Action<CompositionContext, Element> configure
    )
    {
        content = Content(content);
        return ComponentRecipe.Create(
            kind,
            (context, root) =>
            {
                configure(context, root);
                context.Mount(root, content);
            }
        );
    }

    private static ComponentRecipe RawFrame(
        string kind,
        ComponentContent content,
        Style component,
        Style? author
    )
    {
        content = Content(content);
        return ComponentRecipe.Create(
            kind,
            (context, root) =>
            {
                root.Present(context.Theme, component, author);
                context.Mount(root, content);
            }
        );
    }

    private static TabItem<TKey>[] SnapshotTabs<TKey>(IEnumerable<TabItem<TKey>> source)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = source.ToArray();
        var keys = new HashSet<TKey>();
        foreach (var item in result)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!keys.Add(item.Key))
                throw new ArgumentException("Tab keys must be unique.", nameof(source));
        }
        return result;
    }

    private static void ReconcileVisited<TKey>(
        IReadOnlyList<TabItem<TKey>> items,
        SelectedKey<TKey> selected,
        Signal<TKey[]> visited
    )
        where TKey : notnull
    {
        var available = items.Select(item => item.Key).ToHashSet();
        var next = visited.Value.Where(available.Contains).ToList();
        if (
            selected.HasValue
            && available.Contains(selected.Value)
            && !next.Contains(selected.Value)
        )
            next.Add(selected.Value);
        if (!visited.Value.SequenceEqual(next))
            visited.Value = [.. next];
    }
}
