namespace Lucent.Core;

/// <summary>Typed shorthand for the public property groups; custom properties use <see cref="Style.Set{T}(Property{T}, T)"/>.</summary>
public static class StyleFluency
{
    /// <summary>Returns an immutable style with a direct Axis assignment.</summary>
    public static Style Axis(this Style style, LayoutAxis value) =>
        style.Set(LayoutProperties.Axis, value);

    /// <summary>Returns an immutable style whose Axis assignment resolves from the active theme.</summary>
    public static Style Axis(this Style style, Token<LayoutAxis> value) =>
        style.Set(LayoutProperties.Axis, value);

    /// <summary>Returns an immutable style whose Axis assignment is read reactively when presented.</summary>
    public static Style Axis(this Style style, Func<LayoutAxis> read) =>
        style.Bind(LayoutProperties.Axis, read);

    /// <summary>Returns an immutable style with a direct Width assignment.</summary>
    public static Style Width(this Style style, float? value) =>
        style.Set(LayoutProperties.Width, value);

    /// <summary>Returns an immutable style whose Width assignment resolves from the active theme.</summary>
    public static Style Width(this Style style, Token<float?> value) =>
        style.Set(LayoutProperties.Width, value);

    /// <summary>Returns an immutable style whose Width assignment is read reactively when presented.</summary>
    public static Style Width(this Style style, Func<float?> read) =>
        style.Bind(LayoutProperties.Width, read);

    /// <summary>Returns an immutable style with a direct Height assignment.</summary>
    public static Style Height(this Style style, float? value) =>
        style.Set(LayoutProperties.Height, value);

    /// <summary>Returns an immutable style whose Height assignment resolves from the active theme.</summary>
    public static Style Height(this Style style, Token<float?> value) =>
        style.Set(LayoutProperties.Height, value);

    /// <summary>Returns an immutable style whose Height assignment is read reactively when presented.</summary>
    public static Style Height(this Style style, Func<float?> read) =>
        style.Bind(LayoutProperties.Height, read);

    /// <summary>Returns an immutable style with a direct MinWidth assignment.</summary>
    public static Style MinWidth(this Style style, float value) =>
        style.Set(LayoutProperties.MinWidth, value);

    /// <summary>Returns an immutable style whose MinWidth assignment resolves from the active theme.</summary>
    public static Style MinWidth(this Style style, Token<float> value) =>
        style.Set(LayoutProperties.MinWidth, value);

    /// <summary>Returns an immutable style whose MinWidth assignment is read reactively when presented.</summary>
    public static Style MinWidth(this Style style, Func<float> read) =>
        style.Bind(LayoutProperties.MinWidth, read);

    /// <summary>Returns an immutable style with a direct MinHeight assignment.</summary>
    public static Style MinHeight(this Style style, float value) =>
        style.Set(LayoutProperties.MinHeight, value);

    /// <summary>Returns an immutable style whose MinHeight assignment resolves from the active theme.</summary>
    public static Style MinHeight(this Style style, Token<float> value) =>
        style.Set(LayoutProperties.MinHeight, value);

    /// <summary>Returns an immutable style whose MinHeight assignment is read reactively when presented.</summary>
    public static Style MinHeight(this Style style, Func<float> read) =>
        style.Bind(LayoutProperties.MinHeight, read);

    /// <summary>Returns an immutable style with a direct MaxWidth assignment.</summary>
    public static Style MaxWidth(this Style style, float value) =>
        style.Set(LayoutProperties.MaxWidth, value);

    /// <summary>Returns an immutable style whose MaxWidth assignment resolves from the active theme.</summary>
    public static Style MaxWidth(this Style style, Token<float> value) =>
        style.Set(LayoutProperties.MaxWidth, value);

    /// <summary>Returns an immutable style whose MaxWidth assignment is read reactively when presented.</summary>
    public static Style MaxWidth(this Style style, Func<float> read) =>
        style.Bind(LayoutProperties.MaxWidth, read);

    /// <summary>Returns an immutable style with a direct MaxHeight assignment.</summary>
    public static Style MaxHeight(this Style style, float value) =>
        style.Set(LayoutProperties.MaxHeight, value);

    /// <summary>Returns an immutable style whose MaxHeight assignment resolves from the active theme.</summary>
    public static Style MaxHeight(this Style style, Token<float> value) =>
        style.Set(LayoutProperties.MaxHeight, value);

    /// <summary>Returns an immutable style whose MaxHeight assignment is read reactively when presented.</summary>
    public static Style MaxHeight(this Style style, Func<float> read) =>
        style.Bind(LayoutProperties.MaxHeight, read);

    /// <summary>Returns an immutable style with a direct Spacing assignment.</summary>
    public static Style Spacing(this Style style, float value) =>
        style.Set(LayoutProperties.Spacing, value);

    /// <summary>Returns an immutable style whose Spacing assignment resolves from the active theme.</summary>
    public static Style Spacing(this Style style, Token<float> value) =>
        style.Set(LayoutProperties.Spacing, value);

    /// <summary>Returns an immutable style whose Spacing assignment is read reactively when presented.</summary>
    public static Style Spacing(this Style style, Func<float> read) =>
        style.Bind(LayoutProperties.Spacing, read);

    /// <summary>Returns an immutable style with a direct MainAlignment assignment.</summary>
    public static Style MainAlignment(this Style style, LayoutAlignment value) =>
        style.Set(LayoutProperties.MainAlignment, value);

    /// <summary>Returns an immutable style whose MainAlignment assignment resolves from the active theme.</summary>
    public static Style MainAlignment(this Style style, Token<LayoutAlignment> value) =>
        style.Set(LayoutProperties.MainAlignment, value);

    /// <summary>Returns an immutable style whose MainAlignment assignment is read reactively when presented.</summary>
    public static Style MainAlignment(this Style style, Func<LayoutAlignment> read) =>
        style.Bind(LayoutProperties.MainAlignment, read);

    /// <summary>Returns an immutable style with a direct CrossAlignment assignment.</summary>
    public static Style CrossAlignment(this Style style, LayoutAlignment value) =>
        style.Set(LayoutProperties.CrossAlignment, value);

    /// <summary>Returns an immutable style whose CrossAlignment assignment resolves from the active theme.</summary>
    public static Style CrossAlignment(this Style style, Token<LayoutAlignment> value) =>
        style.Set(LayoutProperties.CrossAlignment, value);

    /// <summary>Returns an immutable style whose CrossAlignment assignment is read reactively when presented.</summary>
    public static Style CrossAlignment(this Style style, Func<LayoutAlignment> read) =>
        style.Bind(LayoutProperties.CrossAlignment, read);

    /// <summary>Returns an immutable style with a direct Padding assignment.</summary>
    public static Style Padding(this Style style, Insets value) =>
        style.Set(LayoutProperties.Padding, value);

    /// <summary>Returns an immutable style whose Padding assignment resolves from the active theme.</summary>
    public static Style Padding(this Style style, Token<Insets> value) =>
        style.Set(LayoutProperties.Padding, value);

    /// <summary>Returns an immutable style whose Padding assignment is read reactively when presented.</summary>
    public static Style Padding(this Style style, Func<Insets> read) =>
        style.Bind(LayoutProperties.Padding, read);

    /// <summary>Returns an immutable style with a direct Clip assignment.</summary>
    public static Style Clip(this Style style, bool value) =>
        style.Set(LayoutProperties.Clip, value);

    /// <summary>Returns an immutable style whose Clip assignment resolves from the active theme.</summary>
    public static Style Clip(this Style style, Token<bool> value) =>
        style.Set(LayoutProperties.Clip, value);

    /// <summary>Returns an immutable style whose Clip assignment is read reactively when presented.</summary>
    public static Style Clip(this Style style, Func<bool> read) =>
        style.Bind(LayoutProperties.Clip, read);

    /// <summary>Returns an immutable style with a direct Scroll assignment.</summary>
    public static Style Scroll(this Style style, ScrollOffset value) =>
        style.Set(LayoutProperties.Scroll, value);

    /// <summary>Returns an immutable style whose Scroll assignment resolves from the active theme.</summary>
    public static Style Scroll(this Style style, Token<ScrollOffset> value) =>
        style.Set(LayoutProperties.Scroll, value);

    /// <summary>Returns an immutable style whose Scroll assignment is read reactively when presented.</summary>
    public static Style Scroll(this Style style, Func<ScrollOffset> read) =>
        style.Bind(LayoutProperties.Scroll, read);

    /// <summary>Returns an immutable style with a direct Background assignment.</summary>
    public static Style Background(this Style style, Brush value) =>
        style.Set(VisualProperties.Background, value);

    /// <summary>Returns an immutable style whose Background assignment resolves from the active theme.</summary>
    public static Style Background(this Style style, Token<Brush> value) =>
        style.Set(VisualProperties.Background, value);

    /// <summary>Returns an immutable style whose Background assignment is read reactively when presented.</summary>
    public static Style Background(this Style style, Func<Brush> read) =>
        style.Bind(VisualProperties.Background, read);

    /// <summary>Returns an immutable style with a direct Opacity assignment.</summary>
    public static Style Opacity(this Style style, float value) =>
        style.Set(VisualProperties.Opacity, value);

    /// <summary>Returns an immutable style whose Opacity assignment resolves from the active theme.</summary>
    public static Style Opacity(this Style style, Token<float> value) =>
        style.Set(VisualProperties.Opacity, value);

    /// <summary>Returns an immutable style whose Opacity assignment is read reactively when presented.</summary>
    public static Style Opacity(this Style style, Func<float> read) =>
        style.Bind(VisualProperties.Opacity, read);

    /// <summary>Returns an immutable style with a direct TextColor assignment.</summary>
    public static Style TextColor(this Style style, Color value) =>
        style.Set(TypographyProperties.TextColor, value);

    /// <summary>Returns an immutable style whose TextColor assignment resolves from the active theme.</summary>
    public static Style TextColor(this Style style, Token<Color> value) =>
        style.Set(TypographyProperties.TextColor, value);

    /// <summary>Returns an immutable style whose TextColor assignment is read reactively when presented.</summary>
    public static Style TextColor(this Style style, Func<Color> read) =>
        style.Bind(TypographyProperties.TextColor, read);

    /// <summary>Returns an immutable style with a direct FontFamily assignment.</summary>
    public static Style FontFamily(this Style style, string value) =>
        style.Set(TypographyProperties.FontFamily, value);

    /// <summary>Returns an immutable style whose FontFamily assignment resolves from the active theme.</summary>
    public static Style FontFamily(this Style style, Token<string> value) =>
        style.Set(TypographyProperties.FontFamily, value);

    /// <summary>Returns an immutable style whose FontFamily assignment is read reactively when presented.</summary>
    public static Style FontFamily(this Style style, Func<string> read) =>
        style.Bind(TypographyProperties.FontFamily, read);

    /// <summary>Returns an immutable style with a direct FontSize assignment.</summary>
    public static Style FontSize(this Style style, float value) =>
        style.Set(TypographyProperties.FontSize, value);

    /// <summary>Returns an immutable style whose FontSize assignment resolves from the active theme.</summary>
    public static Style FontSize(this Style style, Token<float> value) =>
        style.Set(TypographyProperties.FontSize, value);

    /// <summary>Returns an immutable style whose FontSize assignment is read reactively when presented.</summary>
    public static Style FontSize(this Style style, Func<float> read) =>
        style.Bind(TypographyProperties.FontSize, read);

    /// <summary>Returns an immutable style with a direct Language assignment.</summary>
    public static Style Language(this Style style, string value) =>
        style.Set(TypographyProperties.Language, value);

    /// <summary>Returns an immutable style whose Language assignment resolves from the active theme.</summary>
    public static Style Language(this Style style, Token<string> value) =>
        style.Set(TypographyProperties.Language, value);

    /// <summary>Returns an immutable style whose Language assignment is read reactively when presented.</summary>
    public static Style Language(this Style style, Func<string> read) =>
        style.Bind(TypographyProperties.Language, read);

    /// <summary>Returns an immutable style with a direct Direction assignment.</summary>
    public static Style Direction(this Style style, TextDirection value) =>
        style.Set(TypographyProperties.Direction, value);

    /// <summary>Returns an immutable style whose Direction assignment resolves from the active theme.</summary>
    public static Style Direction(this Style style, Token<TextDirection> value) =>
        style.Set(TypographyProperties.Direction, value);

    /// <summary>Returns an immutable style whose Direction assignment is read reactively when presented.</summary>
    public static Style Direction(this Style style, Func<TextDirection> read) =>
        style.Bind(TypographyProperties.Direction, read);

    /// <summary>Returns an immutable style with a direct Enabled assignment.</summary>
    public static Style Enabled(this Style style, bool value) =>
        style.Set(InputProperties.Enabled, value);

    /// <summary>Returns an immutable style whose Enabled assignment resolves from the active theme.</summary>
    public static Style Enabled(this Style style, Token<bool> value) =>
        style.Set(InputProperties.Enabled, value);

    /// <summary>Returns an immutable style whose Enabled assignment is read reactively when presented.</summary>
    public static Style Enabled(this Style style, Func<bool> read) =>
        style.Bind(InputProperties.Enabled, read);

    /// <summary>Returns an immutable style with a direct Visible assignment.</summary>
    public static Style Visible(this Style style, bool value) =>
        style.Set(InputProperties.Visible, value);

    /// <summary>Returns an immutable style whose Visible assignment resolves from the active theme.</summary>
    public static Style Visible(this Style style, Token<bool> value) =>
        style.Set(InputProperties.Visible, value);

    /// <summary>Returns an immutable style whose Visible assignment is read reactively when presented.</summary>
    public static Style Visible(this Style style, Func<bool> read) =>
        style.Bind(InputProperties.Visible, read);
}
