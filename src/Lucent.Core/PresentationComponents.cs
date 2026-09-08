namespace Lucent.Core;

/// <summary>Selects the axis on which a decorative divider extends.</summary>
public enum DividerOrientation
{
    /// <summary>Extends across the cross axis of a column layout.</summary>
    Horizontal,

    /// <summary>Extends across the cross axis of a row layout.</summary>
    Vertical,
}

/// <summary>Additional stock presentation recipes.</summary>
public static partial class Components
{
    /// <summary>Creates a noninteractive theme-backed divider with no semantic or input ownership.</summary>
    [LucentComponent]
    public static ComponentRecipe Divider(
        DividerOrientation orientation = DividerOrientation.Horizontal,
        float thickness = 1,
        Style? style = null
    )
    {
        if (!Enum.IsDefined(orientation))
            throw new ArgumentOutOfRangeException(nameof(orientation));
        if (!float.IsFinite(thickness) || thickness <= 0)
            throw new ArgumentOutOfRangeException(nameof(thickness));

        var component = Style
            .Empty.Set(
                orientation == DividerOrientation.Horizontal
                    ? LayoutProperties.Height
                    : LayoutProperties.Width,
                (float?)thickness
            )
            .Set(VisualProperties.Background, ControlThemes.Divider);
        return ComponentRecipe.Create(
            "divider",
            (context, root) => root.Present(context.Theme, component, style)
        );
    }
}
