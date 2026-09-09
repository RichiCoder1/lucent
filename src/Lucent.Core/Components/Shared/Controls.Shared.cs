namespace Lucent.Core;

internal static partial class Controls
{
    // Text and structural containers deliberately inherit their paint from the
    // surrounding presentation. This keeps composed controls (for example a
    // Selectable containing a Row and Text) from painting over the owner's
    // state background or text color. Applications opt into an explicit stock
    // surface at their shell/container boundary with PresentationStyles.Surface.
    private static readonly Style TextStyle = Style.Empty;
    private static readonly Style PanelStyle = Style.Empty.Set(
        LayoutProperties.Axis,
        LayoutAxis.Column
    );
    private static readonly Style RowStyle = Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Row);
    private static readonly Style ScrollBarStyle = Style
        .Empty.Set(ScrollBarProperties.Visibility, ScrollBarVisibility.Auto)
        .Set(ScrollBarProperties.Thickness, 12f)
        .Set(ScrollBarProperties.MinimumThumbLength, 24f)
        .Set(ScrollBarProperties.TrackBrush, ControlThemes.ScrollTrack)
        .Set(ScrollBarProperties.ThumbBrush, ControlThemes.ScrollThumb)
        .Set(ScrollBarProperties.HoverThumbBrush, ControlThemes.ScrollThumbHover)
        .Set(ScrollBarProperties.PressedThumbBrush, ControlThemes.ScrollThumbPressed)
        .Set(ScrollBarProperties.ThumbCornerRadius, 6f);

    private static Brush ResolveBrush(ThemeContext theme, Token<Brush> standard, Brush minimal) =>
        theme.PresentationMode == ControlPresentationMode.Minimal ? minimal : theme.Token(standard);

    private static Border ResolveBorder(ThemeContext theme, Token<Brush> standard) =>
        theme.PresentationMode == ControlPresentationMode.Minimal
            ? Border.None
            : Border.Hairline(theme.Token(standard));

    private static Color ResolveButtonText(ThemeContext theme, Token<Color> standard) =>
        theme.PresentationMode == ControlPresentationMode.Minimal
            ? theme.Token(ControlThemes.Foreground)
            : theme.Token(standard);

    private static Style FocusStyle(ThemeContext theme) =>
        Style
            .Empty.Set(VisualProperties.FocusRing, ControlThemes.FocusRing)
            .When(
                VariantState.Pressed,
                Style.Empty.Bind(
                    VisualProperties.FocusRing,
                    () =>
                        global::Lucent.Core.FocusRing.Inset(
                            theme.Token(ControlThemes.SurfaceColor),
                            2
                        )
                )
            )
            .When(
                () => IsHighContrast(theme),
                Style
                    .Empty.Set(VisualProperties.Background, ControlThemes.Focus)
                    .Set(TypographyProperties.TextColor, ControlThemes.FocusForeground)
            );

    private static Style AccentFocusStyle(ThemeContext theme) =>
        FocusStyle(theme)
            .When(
                () =>
                    theme.PresentationMode != ControlPresentationMode.Minimal
                    && !IsHighContrast(theme),
                Style.Empty.Bind(
                    VisualProperties.FocusRing,
                    () =>
                        global::Lucent.Core.FocusRing.Inset(
                            theme.Token(ControlThemes.SurfaceColor),
                            2
                        )
                )
            );

    private static bool IsHighContrast(ThemeContext theme) =>
        theme.Appearance.Contrast == ThemeContrast.High
        || string.Equals(
            theme.CurrentTheme.Name,
            ControlThemes.HighContrast.Name,
            StringComparison.Ordinal
        );

    private static void Bind(
        Element element,
        ControlState state,
        Action<ControlState> visual,
        Func<ControlState, SemanticDeclaration> semantics
    ) =>
        _ = element.Scope.Effect(
            () =>
            {
                visual(state);
                element.UpdateControlSemantics(semantics(state));
            },
            element.Name + ".control-state"
        );

    private static void Bind(
        Element element,
        ScrollViewportState state,
        Action<ScrollViewportState> visual
    ) => _ = element.Scope.Effect(() => visual(state), element.Name + ".scroll-state");

    private static void ConfigureSemantic(
        Element element,
        ThemeContext theme,
        Style component,
        Style? author,
        SemanticDeclaration semantics
    ) => Configure(element, theme, component, author, new SemanticBehavior(semantics));

    private static void Configure(
        Element element,
        ThemeContext theme,
        Style component,
        Style? author,
        Behavior behavior
    )
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(behavior);
        element.ValidateBehaviorAttachment(behavior); // Preflight keeps a failed ownership claim from installing presentation.
        element.Present(theme, component, author);
        element.AttachBehaviors(behavior);
    }

    private static void Preflight(
        Element element,
        ThemeContext theme,
        Style component,
        Style? author,
        Behavior behavior
    )
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(behavior);
        element.ValidatePresentation(theme, component, author);
        element.ValidateBehaviorAttachment(behavior);
    }

    private static string Required(string value, string parameter) =>
        ControlState.Required(value, parameter);

    private sealed class SemanticBehavior(SemanticDeclaration semantics) : Behavior
    {
        public override string Name => "semantics";
        public override BehaviorOwnership Ownership => BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context) => context.SetSemantics(semantics);
    }
}
