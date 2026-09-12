namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Creates a read-only virtualized table with keyed row selection and caller-owned sorting.</summary>
    /// <remarks>Columns are snapshotted at construction. Only visible rows are formatted. Column widths
    /// are retained for the mounted table lifetime; sorting and filtering never change the caller's selected key.</remarks>
    [LucentComponent]
    public static ComponentRecipe TableView<TKey, TItem>(
        string label,
        Func<IEnumerable<TItem>> items,
        Func<TItem, TKey> key,
        IReadOnlyList<TableColumn<TItem>> columns,
        Func<SelectedKey<TKey>> readSelectedKey,
        Action<TKey> onSelectionRequested,
        Func<TableSort?>? readSort = null,
        Action<TableSort>? onSortRequested = null,
        TableViewOptions? options = null,
        Style? style = null,
        ViewportState? viewport = null
    )
        where TKey : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(readSelectedKey);
        ArgumentNullException.ThrowIfNull(onSelectionRequested);
        if ((readSort is null) != (onSortRequested is null))
            throw new ArgumentException(
                "Supply both a sort reader and request callback, or neither.",
                nameof(readSort)
            );
        var columnSnapshot = columns.ToArray();
        if (
            columnSnapshot.Length == 0
            || columnSnapshot.Any(column => column is null)
            || columnSnapshot.Select(column => column.Key).Distinct(StringComparer.Ordinal).Count()
                != columnSnapshot.Length
        )
            throw new ArgumentException(
                "Provide at least one column with unique non-null keys.",
                nameof(columns)
            );
        options ??= new();
        var rowHeight = options.RowHeight ?? DensityMetrics.Comfortable.RowHeight;
        var headerHeight = options.HeaderHeight ?? DensityMetrics.Comfortable.ControlHeight;
        return ComponentRecipe.Create(
            "table-view",
            (context, root) =>
            {
                var current = root.Scope.Derived(
                    () => TableSnapshot<TKey, TItem>.Create(items(), key),
                    root.Name + ".rows"
                );
                var policy = new KeyedSelectionPolicy<TKey, TableRow<TKey, TItem>>(
                    root.Scope,
                    root.Name,
                    () => current.Value.Rows,
                    row => row.Key,
                    _ => true,
                    readSelectedKey,
                    onSelectionRequested,
                    KeyedSelectionCommitMode.OnConfirmation
                );
                var widths = columnSnapshot
                    .Select(column =>
                        root.Scope.Signal(column.Width, root.Name + ".width." + column.Key)
                    )
                    .ToArray();
                var headerIdentities = new ElementIdentity?[columnSnapshot.Length];
                var headerRevision = root.Scope.Signal(0, root.Name + ".headers");
                var constraints = new ResponsiveConstraints(root.Scope, root.Name + ".bounds");
                var binding = new TableBinding(label, constraints)
                {
                    Count = () => current.Value.Rows.Length,
                    SelectedIndex = () =>
                        readSelectedKey() is { HasValue: true } selected
                        && current.Value.Indices.TryGetValue(selected.Value, out var index)
                            ? index
                            : null,
                    ColumnCount = columnSnapshot.Length,
                    // Keep the vertical scrollbar gutter outside the shared header/cell columns.
                    Width = () => widths.Sum(width => width.Value) + 12,
                    HeaderHeight = () => headerHeight,
                    Headers = () =>
                    {
                        _ = headerRevision.Value;
                        return Array.AsReadOnly(
                            headerIdentities
                                .Where(identity => identity.HasValue)
                                .Select(identity => identity!.Value)
                                .ToArray()
                        );
                    },
                };
                var headerCells = columnSnapshot
                    .Select(
                        (column, index) =>
                            TableHeaderCell(
                                new()
                                {
                                    Key = column.Key,
                                    Header = column.Header,
                                    Width = () => widths[index].Value,
                                    SetWidth = value => widths[index].Value = value,
                                    HeaderHeight = () => headerHeight,
                                    Minimum = column.MinimumWidth,
                                    Maximum = column.MaximumWidth,
                                    ReadSort = readSort ?? (() => null),
                                    RequestSort = column.Sortable ? onSortRequested : null,
                                    RegisterHeader = identity =>
                                    {
                                        headerIdentities[index] = identity;
                                    },
                                }
                            )
                    )
                    .ToArray();
                var header = Layout(
                    ComponentContent.Create(
                        headerCells.Select(cell => (ContentRecipe)cell).ToArray()
                    ),
                    style: Style
                        .Empty.Set(LayoutProperties.Axis, LayoutAxis.Row)
                        .Height(headerHeight)
                );
                var rows = ComponentRecipe.Create(
                    "table-rows",
                    (rowsContext, rowsRoot) =>
                    {
                        var scroll = Controls.ScrollViewport(
                            rowsRoot,
                            rowsContext.Theme,
                            label + " rows",
                            style: Style
                                .Empty.Bind(
                                    LayoutProperties.Height,
                                    () => (float?)binding.BodyHeight()
                                )
                                .Bind(LayoutProperties.Width, () => (float?)binding.Width()),
                            viewport: viewport
                        );
                        void Reveal(int index)
                        {
                            if (index < 0 || index >= current.Value.Rows.Length)
                                return;
                            var top = index * rowHeight;
                            var bottom = top + rowHeight;
                            var height = binding.BodyHeight();
                            if (top < scroll.Offset.Y)
                                scroll.Offset = new(scroll.Offset.X, top);
                            else if (bottom > scroll.Offset.Y + height)
                                scroll.Offset = new(scroll.Offset.X, Math.Max(0, bottom - height));
                        }
                        binding.Reveal = Reveal;
                        var region = rowsContext.Virtualize(
                            rowsRoot,
                            "rows",
                            () => current.Value.Rows,
                            row => row.Key,
                            (item, itemContext) =>
                            {
                                int? Index() =>
                                    current.Value.Indices.TryGetValue(item.Value.Key, out var index)
                                        ? index
                                        : null;
                                var rowBinding = new ChoiceRowBinding
                                {
                                    Role = SemanticRole.DataItem,
                                    CollectionIndex = Index,
                                    Label = () => label + " row " + ((Index() ?? 0) + 1),
                                    Enabled = () => true,
                                    Selected = () => policy.IsApplied(item.Value.Key),
                                    Active = () => policy.IsRoving(item.Value.Key),
                                    Position = () => Index() is { } index ? index + 1 : null,
                                    Size = () => Index() is null ? null : current.Value.Rows.Length,
                                    Register = behavior =>
                                        policy.RegisterTarget(item.Value.Key, behavior),
                                    Activate = (behavior, request) =>
                                        policy.Activate(item.Value.Key, request)
                                        && policy.Focus(item.Value.Key, behavior),
                                    Move = (navigation, behavior) =>
                                    {
                                        if (
                                            navigation is Key.Left or Key.Right
                                            || !policy.Move(navigation, behavior)
                                        )
                                            return false;
                                        if (
                                            policy.TryGetRoving(out var active)
                                            && current.Value.Indices.TryGetValue(
                                                active,
                                                out var index
                                            )
                                        )
                                            Reveal(index);
                                        return true;
                                    },
                                    Confirm = policy.RequestRoving,
                                    RequestOnFocus = () =>
                                        options.SelectionMode == ListBoxSelectionMode.FollowsFocus,
                                    Search = (_, _) => false,
                                };
                                var cells = columnSnapshot
                                    .Select(
                                        (column, index) =>
                                            TableCell(
                                                new()
                                                {
                                                    Text = () => column.Text(item.Value.Item) ?? "",
                                                    Row = Index,
                                                    Column = index,
                                                    Width = () => widths[index].Value,
                                                    Table = () => binding.Identity,
                                                }
                                            )
                                    )
                                    .ToArray();
                                return ChoiceRowHost(
                                        rowBinding,
                                        ComponentContent.Create(
                                            cells.Select(cell => (ContentRecipe)cell).ToArray()
                                        ),
                                        style: Style
                                            .Empty.Padding(Insets.Zero)
                                            .Spacing(0)
                                            .Bind(
                                                LayoutProperties.Width,
                                                () => (float?)(binding.Width() - 12)
                                            )
                                            .Set(VisualProperties.CornerRadius, 0f)
                                            .Bind(
                                                VisualProperties.Border,
                                                () =>
                                                    Border.Hairline(
                                                        itemContext.Theme.Token(
                                                            ControlThemes.Divider
                                                        ),
                                                        BorderSides.Bottom
                                                    )
                                            )
                                    )
                                    .Mount(itemContext);
                            },
                            rowHeight
                        );
                        region.Region.Present(
                            rowsContext.Theme,
                            Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                        );
                        region.Configure();
                    }
                );
                TableContent(binding, header, rows, style).Apply(context, root);
                // Publish after child factories finish: child mounts cannot mutate an ancestor scope.
                headerRevision.Value++;
            }
        );
    }

    [LucentComponent]
    internal static ComponentRecipe TableHost(
        TableBinding binding,
        [DefaultContent] ComponentContent content,
        Style? style = null
    ) =>
        Host(
            "table",
            content,
            (context, root) =>
            {
                binding.Identity = new(root.Composition.Epoch, root.Id);
                Controls.Table(root, context.Theme, binding, style);
                binding.Constraints.AcquireMount(root.Scope);
                root.UpdateControl(ProjectionProperties.ResponsiveConstraints, binding.Constraints);
            }
        );

    [LucentComponent]
    internal static ComponentRecipe TableHeading(TableColumnBinding binding, Style? style = null) =>
        ComponentRecipe.Create(
            "table-heading",
            (context, root) => Controls.TableHeading(root, context.Theme, binding, style)
        );

    [LucentComponent]
    internal static ComponentRecipe TableColumnResize(TableColumnBinding binding) =>
        ComponentRecipe.Create(
            "table-column-resize",
            (context, root) => Controls.TableColumnResize(root, context.Theme, binding)
        );

    [LucentComponent]
    internal static ComponentRecipe TableCellHost(TableCellBinding binding, Style? style = null) =>
        ComponentRecipe.Create(
            "table-cell",
            (context, root) => Controls.TableCell(root, context.Theme, binding, style)
        );

    private sealed record TableRow<TKey, TItem>(TKey Key, TItem Item)
        where TKey : notnull;

    private sealed record TableSnapshot<TKey, TItem>(
        TableRow<TKey, TItem>[] Rows,
        Dictionary<TKey, int> Indices
    )
        where TKey : notnull
    {
        internal static TableSnapshot<TKey, TItem> Create(
            IEnumerable<TItem> items,
            Func<TItem, TKey> key
        )
        {
            ArgumentNullException.ThrowIfNull(items);
            var rows = new List<TableRow<TKey, TItem>>();
            var indices = new Dictionary<TKey, int>();
            foreach (var item in items)
            {
                var identity = key(item);
                ArgumentNullException.ThrowIfNull(identity);
                if (!indices.TryAdd(identity, rows.Count))
                    throw new ArgumentException("Table row keys must be unique.", nameof(items));
                rows.Add(new(identity, item));
            }
            return new(rows.ToArray(), indices);
        }
    }
}
