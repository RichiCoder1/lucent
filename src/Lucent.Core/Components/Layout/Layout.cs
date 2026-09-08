namespace Lucent.Core;

/// <summary>Built-in component recipes for ordinary typed composition.</summary>
public static partial class Components
{
    /// <summary>Creates a neutral retained layout container whose algorithm and arrangement are selected by style.</summary>
    [LucentComponent]
    public static ComponentRecipe Layout(
        [DefaultContent] ComponentContent content,
        ResponsiveConstraints? constraints = null,
        Style? style = null,
        WindowBreakpoints? breakpoints = null
    )
    {
        content = Content(content);
        return ComponentRecipe.Create(
            "layout",
            (context, root) =>
            {
                root.Present(
                    context.Theme,
                    component: Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Column),
                    author: style
                );
                if (constraints is not null)
                {
                    constraints.AcquireMount(root.Scope);
                    root.UpdateControl(ProjectionProperties.ResponsiveConstraints, constraints);
                }
                if (breakpoints is not null)
                {
                    breakpoints.AcquireMount(root);
                    root.UpdateControl(ProjectionProperties.WindowBreakpoints, breakpoints);
                }
                context.Mount(root, content);
            }
        );
    }

    /// <summary>Creates a horizontal container for the supplied content. Use it to place child components in a row.</summary>
    [LucentComponent]
    public static ComponentRecipe Row(
        [DefaultContent] ComponentContent content,
        Style? style = null
    )
    {
        content = Content(content);
        return ComponentRecipe.Create(
            "row",
            (context, root) =>
            {
                root.Present(
                    context.Theme,
                    component: Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Row),
                    author: style
                );
                context.Mount(root, content);
            }
        );
    }

    /// <summary>Creates a vertical container for the supplied content. Use it to stack child components in a column.</summary>
    [LucentComponent]
    public static ComponentRecipe Column(
        [DefaultContent] ComponentContent content,
        Style? style = null
    )
    {
        content = Content(content);
        return ComponentRecipe.Create(
            "column",
            (context, root) =>
            {
                root.Present(
                    context.Theme,
                    component: Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Column),
                    author: style
                );
                context.Mount(root, content);
            }
        );
    }

    /// <summary>Creates a container that publishes its assigned logical content constraints to a hoistable reader.</summary>
    [LucentComponent]
    public static ComponentRecipe ResponsiveContainer(
        [DefaultContent] ComponentContent content,
        ResponsiveConstraints constraints,
        Style? style = null
    )
    {
        content = Content(content);
        ArgumentNullException.ThrowIfNull(constraints);
        return ComponentRecipe.Create(
            "responsive-container",
            (context, root) =>
            {
                constraints.AcquireMount(root.Scope);
                root.Present(
                    context.Theme,
                    author: Style.Compose(
                        Style
                            .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                            .Set(LayoutProperties.MainGrow, 1f),
                        style ?? Style.Empty
                    )
                );
                root.UpdateControl(ProjectionProperties.ResponsiveConstraints, constraints);
                context.Mount(root, content);
            }
        );
    }
}
