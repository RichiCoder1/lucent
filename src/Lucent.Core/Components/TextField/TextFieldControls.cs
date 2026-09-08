namespace Lucent.Core;

internal static partial class Controls
{
    private static Style TextFieldStyle(ThemeContext theme, Func<bool> isPlaceholder) =>
        RowStyle
            .Set(LayoutProperties.Clip, true)
            .Set(LayoutProperties.Padding, Insets.Symmetric(12, 8))
            .Set(LayoutProperties.MainAlignment, LayoutAlignment.Start)
            .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Center)
            .Set(VisualProperties.CornerRadius, 6f)
            .Bind(
                VisualProperties.Background,
                () =>
                    ResolveBrush(theme, ControlThemes.Surface, PresentationStyles.TransparentBrush)
            )
            .Bind(
                TypographyProperties.TextColor,
                () =>
                    isPlaceholder()
                        ? theme.Token(ControlThemes.SecondaryForeground)
                        : theme.Token(ControlThemes.Foreground)
            )
            .Bind(VisualProperties.Border, () => ResolveBorder(theme, ControlThemes.Border))
            .When(
                VariantState.Hover,
                Style
                    .Empty.Set(VisualProperties.Background, ControlThemes.Selected)
                    .Bind(
                        VisualProperties.Border,
                        () => ResolveBorder(theme, ControlThemes.BorderHover)
                    )
            )
            .When(VariantState.FocusVisible, FocusStyle(theme))
            .When(
                VariantState.Disabled,
                Style
                    .Empty.Set(VisualProperties.Background, ControlThemes.Disabled)
                    .Set(TypographyProperties.TextColor, ControlThemes.DisabledForeground)
                    .Bind(
                        VisualProperties.Border,
                        () => ResolveBorder(theme, ControlThemes.Disabled)
                    )
            );

    public static TextFieldState TextField(
        Element element,
        ThemeContext theme,
        string name,
        string value = "",
        Style? style = null,
        EditorSession? session = null,
        FocusTarget? focusTarget = null,
        string? placeholder = null
    )
    {
        name = Required(name, nameof(name));
        TextFieldState.ValidateText(value);
        if (session?.IsMultiline == true)
            throw new ArgumentException(
                "TextField requires a single-line editor session.",
                nameof(session)
            );
        var initialText = session?.Text ?? value;
        var placeholderText = Placeholder(placeholder, name);
        var placeholderActive = element.Scope.Signal(
            initialText.Length == 0 && placeholderText.Length != 0,
            element.Name + ".placeholder-active"
        );
        var component = TextFieldStyle(theme, () => placeholderActive.Value)
            .Set(ProjectionProperties.Text, initialText)
            .Set(ProjectionProperties.TextMeasure, placeholderText);
        Preflight(element, theme, component, style, new TextFieldBehavior(null!, name));
        var editor =
            session
            ?? new EditorSession(element.Scope, element.Name, value, element.Name + ".editor");
        var state = new TextFieldState(element.Scope, element.Name + ".text", editor);
        Configure(element, theme, component, style, new TextFieldBehavior(state, name));
        if (focusTarget is not null)
            element.Composition.Input.RegisterFocusTarget(
                element.Id,
                element.Scope,
                state,
                focusTarget
            );
        _ = element.Scope.Effect(
            () =>
            {
                var isPlaceholder =
                    placeholderText.Length != 0 && state.DisplayText.Length == 0 && !state.Focused;
                placeholderActive.Value = isPlaceholder;
                element.UpdateControl(
                    ProjectionProperties.Text,
                    isPlaceholder ? placeholderText : state.DisplayText
                );
                element.UpdateControl(
                    ProjectionProperties.TextSelectionStart,
                    state.Focused ? state.DisplaySelectionStart : null
                );
                element.UpdateControl(
                    ProjectionProperties.TextSelectionEnd,
                    state.Focused ? state.DisplaySelectionEnd : null
                );
                element.UpdateControl(
                    ProjectionProperties.TextCaret,
                    state.Focused ? state.DisplayCaret : null
                );
            },
            element.Name + ".text-value"
        );
        return state;
    }

    public static TextAreaState TextArea(
        Element element,
        ThemeContext theme,
        string name,
        string value = "",
        Style? style = null,
        EditorSession? session = null,
        FocusTarget? focusTarget = null,
        string? placeholder = null
    )
    {
        name = Required(name, nameof(name));
        TextFieldState.ValidateMultilineText(value);
        if (session is not null && !session.IsMultiline)
            throw new ArgumentException(
                "TextArea requires a multiline editor session.",
                nameof(session)
            );
        var editor =
            session
            ?? new EditorSession(
                element.Scope,
                element.Name,
                value,
                element.Name + ".editor",
                multiline: true
            );
        var initialText = editor.Text;
        var placeholderText = Placeholder(placeholder, name);
        var placeholderActive = element.Scope.Signal(
            initialText.Length == 0 && placeholderText.Length != 0,
            element.Name + ".placeholder-active"
        );
        var component = TextFieldStyle(theme, () => placeholderActive.Value)
            // A multiline editor may occupy a tall viewport. Keep its first
            // line at the content origin while allowing an explicit author
            // alignment to override this stock default below.
            .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Start)
            .With(ScrollBarStyle)
            .Set(ProjectionProperties.Text, initialText)
            .Set(ProjectionProperties.TextMeasure, placeholderText)
            .Set(ProjectionProperties.TextMultiline, true)
            .Set(ProjectionProperties.TextCaretAffinity, editor.CaretAffinity)
            .Set(TypographyProperties.TextWrap, TextWrap.WordWithGraphemeFallback)
            .Set(LayoutProperties.Scroll, editor.Viewport.Offset);
        Preflight(element, theme, component, style, new TextFieldBehavior(null!, name));
        var state = new TextAreaState(element.Scope, element.Name + ".text", editor);
        Configure(element, theme, component, style, new TextFieldBehavior(state, name));
        if (focusTarget is not null)
            element.Composition.Input.RegisterFocusTarget(
                element.Id,
                element.Scope,
                state,
                focusTarget
            );
        _ = element.Scope.Effect(
            () =>
            {
                var isPlaceholder =
                    placeholderText.Length != 0 && state.DisplayText.Length == 0 && !state.Focused;
                placeholderActive.Value = isPlaceholder;
                element.UpdateControl(
                    ProjectionProperties.Text,
                    isPlaceholder ? placeholderText : state.DisplayText
                );
                element.UpdateControl(
                    ProjectionProperties.TextSelectionStart,
                    state.Focused ? state.DisplaySelectionStart : null
                );
                element.UpdateControl(
                    ProjectionProperties.TextSelectionEnd,
                    state.Focused ? state.DisplaySelectionEnd : null
                );
                element.UpdateControl(
                    ProjectionProperties.TextCaret,
                    state.Focused ? state.DisplayCaret : null
                );
                element.UpdateControl(ProjectionProperties.TextMultiline, state.IsMultiline);
                element.UpdateControl(
                    ProjectionProperties.TextCaretAffinity,
                    state.Focused ? state.Session.CaretAffinity : TextAffinity.Downstream
                );
                element.UpdateControl(
                    LayoutProperties.Scroll,
                    state.ScrollState?.Offset ?? default
                );
            },
            element.Name + ".text-area-value"
        );
        return state;
    }

    private static string Placeholder(string? placeholder, string label) => placeholder ?? label;
}
