namespace Lucent.Core;

internal static partial class Controls
{
    public static ScrollViewportState ScrollViewport(
        Element element,
        ThemeContext theme,
        string name,
        ScrollOffset offset = default,
        Style? style = null,
        ViewportState? viewport = null
    )
    {
        name = Required(name, nameof(name));
        offset.Validate();
        var initialOffset = viewport?.Offset ?? offset;
        var component = PanelStyle
            .With(ScrollBarStyle)
            .Set(LayoutProperties.Clip, true)
            .Set(LayoutProperties.Scroll, initialOffset);
        Preflight(element, theme, component, style, new ScrollViewportBehavior(name, null!));
        var state = new ScrollViewportState(
            element.Scope,
            element.Name + ".scroll",
            offset,
            viewport
        );
        Configure(element, theme, component, style, new ScrollViewportBehavior(name, state));
        Bind(element, state, value => element.UpdateControl(LayoutProperties.Scroll, value.Offset));
        return state;
    }

    /// <summary>Creates the fixed-height list used by <see cref="Components.VirtualizedList{TKey,TItem}"/>.</summary>
    public static VirtualizedRegion<TKey, TItem> VirtualizedList<TKey, TItem>(
        Element viewport,
        ThemeContext theme,
        string name,
        string label,
        Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key,
        Func<CurrentItem<TItem>, CompositionContext, Element> row,
        float rowHeight
    )
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(row);
        var region = viewport.Composition.Virtualize(
            viewport,
            Required(name, nameof(name)),
            source,
            key,
            row,
            rowHeight,
            theme
        );
        try
        {
            List(region.Region, theme, Required(label, nameof(label)));
            region.Configure();
            return region;
        }
        catch
        {
            region.Dispose();
            throw;
        }
    }
}
