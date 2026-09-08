namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Creates a scrollable viewport that clips its content. Supply <paramref name="viewport"/> to retain its offset across mounts.</summary>
    [LucentComponent]
    public static ComponentRecipe ScrollViewport(
        [DefaultContent] ComponentContent content,
        string label = "Scroll viewport",
        Style? style = null,
        ViewportState? viewport = null
    )
    {
        content = Content(content);
        label = Required(label, nameof(label));
        return ComponentRecipe.Create(
            "scroll-viewport",
            (context, root) =>
            {
                Controls.ScrollViewport(
                    root,
                    context.Theme,
                    label,
                    style: style,
                    viewport: viewport
                );
                context.Mount(root, content);
            }
        );
    }

    /// <summary>Creates a scrollable list with a fixed row height. Use it for large collections so only rows near the viewport are kept active.</summary>
    [LucentComponent]
    public static ComponentRecipe VirtualizedList<TKey, TItem>(
        Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key,
        Func<CurrentItem<TItem>, ComponentRecipe> row,
        Func<float> rowHeight,
        string label = "Items",
        Style? style = null,
        ViewportState? viewport = null
    )
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(rowHeight);
        label = Required(label, nameof(label));
        return ComponentRecipe.Create(
            "virtualized-list",
            (context, root) =>
            {
                var rowHeightValue = root.Scope.Derived(
                    () => RowHeight(rowHeight),
                    root.Name + ".row-height-read"
                );
                var height = rowHeightValue.Value;
                var viewportStyle = VirtualizedListStyle(root, style);
                var scroll = Controls.ScrollViewport(
                    root,
                    context.Theme,
                    label,
                    style: viewportStyle,
                    viewport: viewport
                );
                var region = context.Virtualize(
                    root,
                    "rows",
                    source,
                    key,
                    (item, child) =>
                    {
                        var recipe = row(item);
                        ArgumentNullException.ThrowIfNull(recipe);
                        return recipe.Mount(child);
                    },
                    height
                );
                try
                {
                    Controls.List(region.Region, context.Theme, label);
                    region.Configure();
                }
                catch
                {
                    region.Dispose();
                    throw;
                }
                _ = root.Scope.Effect(
                    () =>
                    {
                        var next = rowHeightValue.Value;
                        if (next == height)
                            return;
                        var index = MathF.Floor(scroll.Offset.Y / height);
                        var relative = scroll.Offset.Y - index * height;
                        height = next;
                        region.SetRowHeight(next);
                        scroll.Offset = new(scroll.Offset.X, index * next + relative);
                    },
                    root.Name + ".row-height"
                );
            }
        );
    }

    private static Style VirtualizedListStyle(Element root, Style? author) =>
        Style
            .Empty.Bind(LayoutProperties.MainBasis, () => VirtualizedListMainBasis(root))
            .Bind(LayoutProperties.MainGrow, () => VirtualizedListMainGrow(root))
            .With(author);

    private static float VirtualizedListMainBasis(Element root)
    {
        var geometry = VirtualizedListMainGeometry(root);
        return geometry.Value is { } value ? value : 0;
    }

    private static float VirtualizedListMainGrow(Element root)
    {
        var geometry = VirtualizedListMainGeometry(root);
        return geometry.Value is null ? 1 : 0;
    }

    private static ResolvedProperty<float?> VirtualizedListMainGeometry(Element root)
    {
        var parentAxis = root.Parent?.Resolve(LayoutProperties.Axis).Value ?? LayoutAxis.Column;
        return root.Resolve(
            parentAxis == LayoutAxis.Row ? LayoutProperties.Width : LayoutProperties.Height
        );
    }
}
