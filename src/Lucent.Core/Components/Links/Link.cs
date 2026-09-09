namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Creates an accessible hyperlink that invokes only the supplied application action.</summary>
    /// <remarks>Rendering never launches a URI. Use an application-injected IUriLauncher to apply external-handler policy.</remarks>
    [LucentComponent]
    public static ComponentRecipe Link(
        [DefaultContent] string content,
        Action onInvoke,
        Style? style = null
    )
    {
        content = Required(content, nameof(content));
        ArgumentNullException.ThrowIfNull(onInvoke);
        return LinkContent(content, onInvoke, style);
    }

    [LucentComponent]
    internal static ComponentRecipe LinkFrame(
        string label,
        Action onInvoke,
        [DefaultContent] ComponentContent content,
        Style? style = null
    ) =>
        ComponentRecipe.Create(
            "link",
            (context, root) =>
            {
                Controls.Link(root, context.Theme, label, onInvoke, style);
                context.Mount(root, content);
            }
        );

    [LucentComponent]
    internal static ComponentRecipe LinkLabel(string label) =>
        ComponentRecipe.Create(
            "link-label",
            (context, root) =>
                Controls.Text(
                    root,
                    context.Theme,
                    label,
                    Style
                        .Empty.Set(LayoutProperties.MinWidth, 0f)
                        .Set(LayoutProperties.MainShrink, 1f)
                        .Set(TypographyProperties.Overflow, TextOverflow.Ellipsis)
                        .Bind(
                            VisualProperties.Border,
                            () =>
                                Border.Hairline(
                                    context.Theme.Token(ControlThemes.Accent),
                                    BorderSides.Bottom
                                )
                        )
                )
        );
}

internal static partial class Controls
{
    internal static void Link(
        Element element,
        ThemeContext theme,
        string label,
        Action invoke,
        Style? author
    )
    {
        var presentation = Style
            .Empty.Set(LayoutProperties.Axis, LayoutAxis.Row)
            .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Center)
            .Set(LayoutProperties.MinHeight, 32f)
            .Bind(
                TypographyProperties.TextColor,
                () =>
                    theme.Token(ControlThemes.Accent).Color ?? theme.Token(ControlThemes.Foreground)
            )
            .When(
                VariantState.Hover,
                Style.Empty.Set(TypographyProperties.TextColor, ControlThemes.Foreground)
            )
            .When(
                VariantState.Pressed,
                Style.Empty.Set(TypographyProperties.TextColor, ControlThemes.Foreground)
            )
            .When(VariantState.FocusVisible, FocusStyle(theme))
            .When(
                VariantState.Disabled,
                Style.Empty.Set(TypographyProperties.TextColor, ControlThemes.DisabledForeground)
            );
        Configure(
            element,
            theme,
            presentation,
            author,
            new ButtonBehavior(
                "link",
                new(SemanticRole.Hyperlink, label, actions: SemanticAction.Invoke),
                invoke
            )
        );
    }
}
