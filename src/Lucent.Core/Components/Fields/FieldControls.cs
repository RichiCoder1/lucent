namespace Lucent.Core;

internal static partial class Controls
{
    internal static void FieldRoot(Element element, ThemeContext theme, Style? style) =>
        element.Present(
            theme,
            PanelStyle.Set(LayoutProperties.Spacing, 6f).With(style ?? Style.Empty)
        );

    internal static void FieldLabel(
        Element element,
        ThemeContext theme,
        string text,
        FocusTarget focusTarget
    )
    {
        Configure(
            element,
            theme,
            TextStyle
                .Set(ProjectionProperties.Text, text)
                .Set(TypographyProperties.FontWeight, FontWeight.SemiBold),
            null,
            new FieldLabelBehavior(text, focusTarget)
        );
    }

    internal static void FieldHelp(Element element, ThemeContext theme, string text) =>
        ConfigureSemantic(
            element,
            theme,
            TextStyle
                .Set(ProjectionProperties.Text, text)
                .Set(TypographyProperties.TextWrap, TextWrap.WordWithGraphemeFallback)
                .Set(TypographyProperties.TextColor, ControlThemes.SecondaryForeground),
            null,
            new(SemanticRole.Text, text)
        );

    internal static void FieldError(Element element, ThemeContext theme, string text) =>
        ConfigureSemantic(
            element,
            theme,
            TextStyle
                .Set(ProjectionProperties.Text, text)
                .Set(TypographyProperties.TextWrap, TextWrap.WordWithGraphemeFallback)
                .Set(TypographyProperties.TextColor, ControlThemes.SecondaryForeground),
            null,
            new(SemanticRole.Status, text)
        );
}

internal sealed class FieldLabelBehavior(string label, FocusTarget target) : Behavior
{
    public override string Name => "field-label-focus";
    public override BehaviorOwnership Ownership =>
        BehaviorOwnership.Action | BehaviorOwnership.Semantics;

    public override void Attach(BehaviorContext context)
    {
        context.SetSemantics(new(SemanticRole.Text, label));
        int? armed = null;
        context.OnPointer(route =>
        {
            if (route.Command is { Kind: PointerCommandKind.Down, Button: PointerButton.Primary })
            {
                if (route.Capture())
                    armed = route.Command.PointerId;
                route.Handled = armed is not null;
            }
            else if (
                route.Command.Kind == PointerCommandKind.Cancel
                || route.Command.Releases(PointerButton.Primary)
            )
            {
                if (armed != route.Command.PointerId)
                    return;
                var activate =
                    route.Command.Kind == PointerCommandKind.Up && route.IsInsideCurrentTarget;
                armed = null;
                if (activate)
                    target.Request();
                route.Handled = true;
            }
        });
        context.OnCaptureLost(loss =>
        {
            if (armed == loss.PointerId)
                armed = null;
        });
    }
}
