namespace Lucent.Core;

internal static partial class Controls
{
    // Only hover entry is cosmetic. Leaving hover or entering an actionable
    // state snaps, so selection, focus and press feedback never wait for motion.
    private static readonly Style HoverMotionStyle = Style
        .Empty.Transition(VisualProperties.Background, Motion.None)
        .When(VariantState.Hover, Style.Empty.Transition(VisualProperties.Background, Motion.Quick))
        .When(
            VariantState.FocusVisible,
            Style.Empty.Transition(VisualProperties.Background, Motion.None)
        )
        .When(
            VariantState.Selected,
            Style.Empty.Transition(VisualProperties.Background, Motion.None)
        )
        .When(
            VariantState.Pressed,
            Style.Empty.Transition(VisualProperties.Background, Motion.None)
        )
        .When(
            VariantState.Invalid,
            Style.Empty.Transition(VisualProperties.Background, Motion.None)
        )
        .When(
            VariantState.Disabled,
            Style.Empty.Transition(VisualProperties.Background, Motion.None)
        );

    private static Style ButtonStyle(Element element, ThemeContext theme) =>
        RowStyle
            .Set(LayoutProperties.Clip, true)
            .Set(LayoutProperties.Padding, Insets.Symmetric(12, 8))
            .Set(LayoutProperties.MinHeight, 36f)
            .Set(LayoutProperties.MainAlignment, LayoutAlignment.Center)
            .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Center)
            .Set(VisualProperties.CornerRadius, 6f)
            .With(ButtonPaint(theme, ButtonRole.Primary))
            .When(
                () => element.ResolveValue(ButtonProperties.Role) == ButtonRole.Secondary,
                ButtonPaint(theme, ButtonRole.Secondary)
            )
            .When(
                () => element.ResolveValue(ButtonProperties.Role) == ButtonRole.Quiet,
                ButtonPaint(theme, ButtonRole.Quiet)
            )
            .When(
                () => element.ResolveValue(ButtonProperties.Role) == ButtonRole.Destructive,
                ButtonPaint(theme, ButtonRole.Destructive)
            )
            .When(
                VariantState.Disabled,
                Style
                    .Empty.Set(VisualProperties.Background, ControlThemes.Disabled)
                    .Set(TypographyProperties.TextColor, ControlThemes.DisabledForeground)
                    .Bind(
                        VisualProperties.Border,
                        () => ResolveBorder(theme, ControlThemes.Disabled)
                    )
            )
            .With(HoverMotionStyle);

    private static Style ButtonPaint(ThemeContext theme, ButtonRole role)
    {
        var strong = role is ButtonRole.Primary or ButtonRole.Destructive;
        var fill = role switch
        {
            ButtonRole.Primary => ControlThemes.Accent,
            ButtonRole.Destructive => ControlThemes.Destructive,
            _ => ControlThemes.Surface,
        };
        var hover = role switch
        {
            ButtonRole.Primary => ControlThemes.AccentHover,
            ButtonRole.Destructive => ControlThemes.DestructiveHover,
            _ => ControlThemes.Hover,
        };
        var pressed = role switch
        {
            ButtonRole.Primary => ControlThemes.AccentPressed,
            ButtonRole.Destructive => ControlThemes.DestructivePressed,
            _ => ControlThemes.Pressed,
        };
        var foreground = strong ? ControlThemes.SurfaceColor : ControlThemes.Foreground;
        var resting =
            role == ButtonRole.Quiet
                ? Style
                    .Empty.Set(VisualProperties.Background, PresentationStyles.TransparentBrush)
                    .Set(VisualProperties.Border, Border.None)
                : Style
                    .Empty.Bind(
                        VisualProperties.Background,
                        () => ResolveBrush(theme, fill, PresentationStyles.TransparentBrush)
                    )
                    .Bind(
                        VisualProperties.Border,
                        () => ResolveBorder(theme, ControlThemes.Border)
                    );
        return resting
            .Bind(TypographyProperties.TextColor, () => ResolveButtonText(theme, foreground))
            .When(
                VariantState.Hover,
                Style
                    .Empty.Set(VisualProperties.Background, hover)
                    .Set(TypographyProperties.TextColor, foreground)
                    .Bind(
                        VisualProperties.Border,
                        () =>
                            role == ButtonRole.Quiet
                                ? Border.None
                                : ResolveBorder(theme, ControlThemes.BorderHover)
                    )
            )
            .When(
                VariantState.Pressed,
                Style
                    .Empty.Set(VisualProperties.Background, pressed)
                    .Set(TypographyProperties.TextColor, foreground)
            )
            .With(ButtonFocusPaint(theme, strong));
    }

    private static Style ButtonFocusPaint(ThemeContext theme, bool strong)
    {
        var ordinary = Style.Empty.Set(
            VisualProperties.FocusRing,
            strong ? ControlThemes.AccentFocusRing : ControlThemes.FocusRing
        );
        var highContrast = Style
            .Empty.Set(VisualProperties.Background, ControlThemes.Focus)
            .Set(TypographyProperties.TextColor, ControlThemes.FocusForeground)
            .Set(VisualProperties.FocusRing, ControlThemes.FocusRing);
        // Appearance and reset decisions are outside interaction variants, so an already
        // mounted role supplies focus/press tokens on the first committed interaction frame.
        return Style
            .Empty.When(VariantState.FocusVisible, ordinary)
            .When(
                () => theme.PresentationMode == ControlPresentationMode.Minimal,
                Style
                    .Empty.When(
                        VariantState.FocusVisible,
                        Style.Empty.Set(VisualProperties.FocusRing, ControlThemes.FocusRing)
                    )
                    .When(VariantState.FocusVisible | VariantState.Hover, ordinary)
                    .When(VariantState.FocusVisible | VariantState.Pressed, ordinary)
            )
            .When(
                () => IsHighContrast(theme),
                Style
                    .Empty.When(VariantState.FocusVisible, highContrast)
                    .When(VariantState.FocusVisible | VariantState.Pressed, highContrast)
            );
    }

    private static Style ComposedButtonStyle(Element element, ThemeContext theme) =>
        ButtonStyle(element, theme).Set(LayoutProperties.Spacing, 8f);

    private static Style IconButtonStyle(Element element, ThemeContext theme) =>
        ButtonStyle(element, theme)
            .Set(LayoutProperties.Width, 36f)
            .Set(LayoutProperties.Height, 36f)
            .Set(LayoutProperties.Padding, Insets.Uniform(10f))
            .Set(ImageProperties.ColorMode, ImageColorMode.Monochrome);

    private static Style SelectableStyle(ThemeContext theme) =>
        RowStyle
            .Set(LayoutProperties.Clip, true)
            .Set(LayoutProperties.Padding, Insets.Symmetric(10, 6))
            .Set(LayoutProperties.MinHeight, 32f)
            .Set(LayoutProperties.MainAlignment, LayoutAlignment.Start)
            .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Center)
            .Set(VisualProperties.CornerRadius, 4f)
            .Bind(
                VisualProperties.Background,
                () =>
                    ResolveBrush(theme, ControlThemes.Surface, PresentationStyles.TransparentBrush)
            )
            .Bind(TypographyProperties.TextColor, () => theme.Token(ControlThemes.Foreground))
            .When(
                VariantState.Hover,
                Style.Empty.Set(VisualProperties.Background, ControlThemes.Hover)
            )
            .When(
                VariantState.Selected,
                Style.Empty.Set(VisualProperties.Background, ControlThemes.Selected)
            )
            .When(
                VariantState.Pressed,
                Style
                    .Empty.Set(VisualProperties.Background, ControlThemes.AccentPressed)
                    .Set(TypographyProperties.TextColor, ControlThemes.SurfaceColor)
            )
            .When(VariantState.FocusVisible, FocusStyle(theme))
            .When(VariantState.Selected | VariantState.FocusVisible, FocusStyle(theme))
            .When(
                VariantState.Disabled,
                Style
                    .Empty.Set(VisualProperties.Background, ControlThemes.Disabled)
                    .Set(TypographyProperties.TextColor, ControlThemes.DisabledForeground)
                    .Bind(
                        VisualProperties.Border,
                        () => ResolveBorder(theme, ControlThemes.Disabled)
                    )
            )
            .With(HoverMotionStyle);

    public static void Button(
        Element element,
        ThemeContext theme,
        string label,
        Action? activate = null,
        Style? style = null,
        bool focusOnPointer = true,
        FocusTarget? focusTarget = null
    )
    {
        label = Required(label, nameof(label));
        Configure(
            element,
            theme,
            ButtonStyle(element, theme).Set(ProjectionProperties.Text, label),
            style,
            new ButtonBehavior(
                "button",
                SemanticDeclaration.Create(SemanticRole.Button, label).Invoke(true).Build(),
                activate,
                focusOnPointer,
                focusTarget
            )
        );
    }

    internal static void ComposedButton(
        Element element,
        ThemeContext theme,
        string label,
        Action? activate = null,
        Style? style = null,
        FocusTarget? focusTarget = null
    )
    {
        label = Required(label, nameof(label));
        Configure(
            element,
            theme,
            ComposedButtonStyle(element, theme),
            style,
            new ButtonBehavior(
                "button",
                SemanticDeclaration.Create(SemanticRole.Button, label).Invoke(true).Build(),
                activate,
                focusTarget: focusTarget
            )
        );
    }

    internal static void IconButton(
        Element element,
        ThemeContext theme,
        ImageSource source,
        string label,
        Action? activate = null,
        Style? style = null,
        bool focusOnPointer = true
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        label = Required(label, nameof(label));
        Configure(
            element,
            theme,
            IconButtonStyle(element, theme).Set(ImageProperties.Source, source),
            style,
            new ButtonBehavior(
                "icon-button",
                SemanticDeclaration.Create(SemanticRole.Button, label).Invoke(true).Build(),
                activate,
                focusOnPointer
            )
        );
    }

    public static ControlState Selectable(
        Element element,
        ThemeContext theme,
        string label,
        Action? activate = null,
        Style? style = null,
        bool controlled = false
    ) =>
        ConfigureSelectable(element, theme, label, activate, style, projectLabel: true, controlled);

    internal static ControlState ComposedSelectable(
        Element element,
        ThemeContext theme,
        string label,
        Action? activate = null,
        Style? style = null,
        bool controlled = false
    ) =>
        ConfigureSelectable(
            element,
            theme,
            label,
            activate,
            style,
            projectLabel: false,
            controlled
        );

    private static ControlState ConfigureSelectable(
        Element element,
        ThemeContext theme,
        string label,
        Action? activate,
        Style? style,
        bool projectLabel,
        bool controlled
    )
    {
        label = Required(label, nameof(label));
        var styleBase = SelectableStyle(theme);
        var component = projectLabel ? styleBase.Set(ProjectionProperties.Text, label) : styleBase;
        var behavior = new RowActionBehavior(
            "selectable",
            SemanticDeclaration
                .Create(SemanticRole.ListItem, label)
                .SelectionItem(false, true)
                .Build()
        );
        Preflight(element, theme, component, style, behavior);
        var state = new ControlState(element.Scope, element.Name + ".selectable", label);
        Configure(
            element,
            theme,
            component,
            style,
            new RowActionBehavior(
                "selectable",
                SemanticDeclaration
                    .Create(SemanticRole.ListItem, label)
                    .SelectionItem(false, true)
                    .Build(),
                activate,
                state,
                controlled
            )
        );
        Bind(
            element,
            state,
            value =>
            {
                if (projectLabel)
                    element.UpdateControl(ProjectionProperties.Text, value.Label);
            },
            value =>
                SemanticDeclaration
                    .Create(SemanticRole.ListItem, value.Label)
                    .SelectionItem(false, true)
                    .Build()
        );
        return state;
    }
}
