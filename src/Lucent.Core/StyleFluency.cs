namespace Lucent.Core;

/// <summary>Typed shorthand for the public property groups; custom properties use <see cref="Style.Set{T}(Property{T}, T)"/>.</summary>
public static class StyleFluency
{
    /// <summary>Returns a style that sets container direction to <paramref name="value"/>.</summary>
    public static Style Axis(this Style style, LayoutAxis value) =>
        style.Set(LayoutProperties.Axis, value);

    /// <summary>Returns a style that gets container direction from the active theme token.</summary>
    public static Style Axis(this Style style, Token<LayoutAxis> value) =>
        style.Set(LayoutProperties.Axis, value);

    /// <summary>Returns a style that gets container direction from a live reader.</summary>
    public static Style Axis(this Style style, Func<LayoutAxis> read) =>
        style.Bind(LayoutProperties.Axis, read);

    /// <summary>Returns a style that sets width to <paramref name="value"/>.</summary>
    public static Style Width(this Style style, float? value) =>
        style.Set(LayoutProperties.Width, value);

    /// <summary>Returns a style that gets width from the active theme token.</summary>
    public static Style Width(this Style style, Token<float?> value) =>
        style.Set(LayoutProperties.Width, value);

    /// <summary>Returns a style that gets width from a live reader.</summary>
    public static Style Width(this Style style, Func<float?> read) =>
        style.Bind(LayoutProperties.Width, read);

    /// <summary>Returns a style that sets height to <paramref name="value"/>.</summary>
    public static Style Height(this Style style, float? value) =>
        style.Set(LayoutProperties.Height, value);

    /// <summary>Returns a style that gets height from the active theme token.</summary>
    public static Style Height(this Style style, Token<float?> value) =>
        style.Set(LayoutProperties.Height, value);

    /// <summary>Returns a style that gets height from a live reader.</summary>
    public static Style Height(this Style style, Func<float?> read) =>
        style.Bind(LayoutProperties.Height, read);

    /// <summary>Returns a style that sets minimum width to <paramref name="value"/>.</summary>
    public static Style MinWidth(this Style style, float value) =>
        style.Set(LayoutProperties.MinWidth, value);

    /// <summary>Returns a style that gets minimum width from the active theme token.</summary>
    public static Style MinWidth(this Style style, Token<float> value) =>
        style.Set(LayoutProperties.MinWidth, value);

    /// <summary>Returns a style that gets minimum width from a live reader.</summary>
    public static Style MinWidth(this Style style, Func<float> read) =>
        style.Bind(LayoutProperties.MinWidth, read);

    /// <summary>Returns a style that sets minimum height to <paramref name="value"/>.</summary>
    public static Style MinHeight(this Style style, float value) =>
        style.Set(LayoutProperties.MinHeight, value);

    /// <summary>Returns a style that gets minimum height from the active theme token.</summary>
    public static Style MinHeight(this Style style, Token<float> value) =>
        style.Set(LayoutProperties.MinHeight, value);

    /// <summary>Returns a style that gets minimum height from a live reader.</summary>
    public static Style MinHeight(this Style style, Func<float> read) =>
        style.Bind(LayoutProperties.MinHeight, read);

    /// <summary>Returns a style that sets maximum width to <paramref name="value"/>.</summary>
    public static Style MaxWidth(this Style style, float value) =>
        style.Set(LayoutProperties.MaxWidth, value);

    /// <summary>Returns a style that gets maximum width from the active theme token.</summary>
    public static Style MaxWidth(this Style style, Token<float> value) =>
        style.Set(LayoutProperties.MaxWidth, value);

    /// <summary>Returns a style that gets maximum width from a live reader.</summary>
    public static Style MaxWidth(this Style style, Func<float> read) =>
        style.Bind(LayoutProperties.MaxWidth, read);

    /// <summary>Returns a style that sets maximum height to <paramref name="value"/>.</summary>
    public static Style MaxHeight(this Style style, float value) =>
        style.Set(LayoutProperties.MaxHeight, value);

    /// <summary>Returns a style that gets maximum height from the active theme token.</summary>
    public static Style MaxHeight(this Style style, Token<float> value) =>
        style.Set(LayoutProperties.MaxHeight, value);

    /// <summary>Returns a style that gets maximum height from a live reader.</summary>
    public static Style MaxHeight(this Style style, Func<float> read) =>
        style.Bind(LayoutProperties.MaxHeight, read);

    /// <summary>Returns a style that sets gap between children to <paramref name="value"/>.</summary>
    public static Style Spacing(this Style style, float value) =>
        style.Set(LayoutProperties.Spacing, value);

    /// <summary>Returns a style that gets gap between children from the active theme token.</summary>
    public static Style Spacing(this Style style, Token<float> value) =>
        style.Set(LayoutProperties.Spacing, value);

    /// <summary>Returns a style that gets gap between children from a live reader.</summary>
    public static Style Spacing(this Style style, Func<float> read) =>
        style.Bind(LayoutProperties.Spacing, read);

    /// <summary>Returns a style that sets the proportional share of positive unused parent space received along the main axis.</summary>
    public static Style MainGrow(this Style style, float value) =>
        style.Set(LayoutProperties.MainGrow, value);

    /// <summary>Returns a style that gets main-axis growth from the active theme token.</summary>
    public static Style MainGrow(this Style style, Token<float> value) =>
        style.Set(LayoutProperties.MainGrow, value);

    /// <summary>Returns a style that gets main-axis growth from a live reader.</summary>
    public static Style MainGrow(this Style style, Func<float> read) =>
        style.Bind(LayoutProperties.MainGrow, read);

    /// <summary>Returns a style that sets child alignment along the container direction to <paramref name="value"/>.</summary>
    public static Style MainAlignment(this Style style, LayoutAlignment value) =>
        style.Set(LayoutProperties.MainAlignment, value);

    /// <summary>Returns a style that gets child alignment along the container direction from the active theme token.</summary>
    public static Style MainAlignment(this Style style, Token<LayoutAlignment> value) =>
        style.Set(LayoutProperties.MainAlignment, value);

    /// <summary>Returns a style that gets child alignment along the container direction from a live reader.</summary>
    public static Style MainAlignment(this Style style, Func<LayoutAlignment> read) =>
        style.Bind(LayoutProperties.MainAlignment, read);

    /// <summary>Returns a style that sets child alignment across the container direction to <paramref name="value"/>.</summary>
    public static Style CrossAlignment(this Style style, LayoutAlignment value) =>
        style.Set(LayoutProperties.CrossAlignment, value);

    /// <summary>Returns a style that gets child alignment across the container direction from the active theme token.</summary>
    public static Style CrossAlignment(this Style style, Token<LayoutAlignment> value) =>
        style.Set(LayoutProperties.CrossAlignment, value);

    /// <summary>Returns a style that gets child alignment across the container direction from a live reader.</summary>
    public static Style CrossAlignment(this Style style, Func<LayoutAlignment> read) =>
        style.Bind(LayoutProperties.CrossAlignment, read);

    /// <summary>Returns a style that sets inner edge space to <paramref name="value"/>.</summary>
    public static Style Padding(this Style style, Insets value) =>
        style.Set(LayoutProperties.Padding, value);

    /// <summary>Returns a style that gets inner edge space from the active theme token.</summary>
    public static Style Padding(this Style style, Token<Insets> value) =>
        style.Set(LayoutProperties.Padding, value);

    /// <summary>Returns a style that gets inner edge space from a live reader.</summary>
    public static Style Padding(this Style style, Func<Insets> read) =>
        style.Bind(LayoutProperties.Padding, read);

    /// <summary>Returns a style that sets content clipping to <paramref name="value"/>.</summary>
    public static Style Clip(this Style style, bool value) =>
        style.Set(LayoutProperties.Clip, value);

    /// <summary>Returns a style that gets content clipping from the active theme token.</summary>
    public static Style Clip(this Style style, Token<bool> value) =>
        style.Set(LayoutProperties.Clip, value);

    /// <summary>Returns a style that gets content clipping from a live reader.</summary>
    public static Style Clip(this Style style, Func<bool> read) =>
        style.Bind(LayoutProperties.Clip, read);

    /// <summary>Returns a style that sets content offset to <paramref name="value"/>.</summary>
    public static Style Scroll(this Style style, ScrollOffset value) =>
        style.Set(LayoutProperties.Scroll, value);

    /// <summary>Returns a style that gets content offset from the active theme token.</summary>
    public static Style Scroll(this Style style, Token<ScrollOffset> value) =>
        style.Set(LayoutProperties.Scroll, value);

    /// <summary>Returns a style that gets content offset from a live reader.</summary>
    public static Style Scroll(this Style style, Func<ScrollOffset> read) =>
        style.Bind(LayoutProperties.Scroll, read);

    /// <summary>Returns a style that sets background brush to <paramref name="value"/>.</summary>
    public static Style Background(this Style style, Brush value) =>
        style.Set(VisualProperties.Background, value);

    /// <summary>Returns a style that gets background brush from the active theme token.</summary>
    public static Style Background(this Style style, Token<Brush> value) =>
        style.Set(VisualProperties.Background, value);

    /// <summary>Returns a style that gets background brush from a live reader.</summary>
    public static Style Background(this Style style, Func<Brush> read) =>
        style.Bind(VisualProperties.Background, read);

    /// <summary>Returns a style that sets an inset border.</summary>
    public static Style Border(this Style style, Border value) =>
        style.Set(VisualProperties.Border, value);

    /// <summary>Returns a style that gets an inset border from the active theme token.</summary>
    public static Style Border(this Style style, Token<Border> value) =>
        style.Set(VisualProperties.Border, value);

    /// <summary>Returns a style that gets an inset border from a live reader.</summary>
    public static Style Border(this Style style, Func<Border> read) =>
        style.Bind(VisualProperties.Border, read);

    /// <summary>Returns a style that sets an inset keyboard-focus ring.</summary>
    public static Style FocusRing(this Style style, FocusRing value) =>
        style.Set(VisualProperties.FocusRing, value);

    /// <summary>Returns a style that gets an inset keyboard-focus ring from the active theme token.</summary>
    public static Style FocusRing(this Style style, Token<FocusRing> value) =>
        style.Set(VisualProperties.FocusRing, value);

    /// <summary>Returns a style that gets an inset keyboard-focus ring from a live reader.</summary>
    public static Style FocusRing(this Style style, Func<FocusRing> read) =>
        style.Bind(VisualProperties.FocusRing, read);

    /// <summary>Returns a style that rounds backgrounds, inset decorations, and enabled content clips.</summary>
    public static Style CornerRadius(this Style style, float value) =>
        style.Set(VisualProperties.CornerRadius, value);

    /// <summary>Returns a style that gets corner radius from the active theme token.</summary>
    public static Style CornerRadius(this Style style, Token<float> value) =>
        style.Set(VisualProperties.CornerRadius, value);

    /// <summary>Returns a style that gets corner radius from a live reader.</summary>
    public static Style CornerRadius(this Style style, Func<float> read) =>
        style.Bind(VisualProperties.CornerRadius, read);

    /// <summary>Returns a style that sets transparency to <paramref name="value"/>.</summary>
    public static Style Opacity(this Style style, float value) =>
        style.Set(VisualProperties.Opacity, value);

    /// <summary>Returns a style that gets transparency from the active theme token.</summary>
    public static Style Opacity(this Style style, Token<float> value) =>
        style.Set(VisualProperties.Opacity, value);

    /// <summary>Returns a style that gets transparency from a live reader.</summary>
    public static Style Opacity(this Style style, Func<float> read) =>
        style.Bind(VisualProperties.Opacity, read);

    /// <summary>Returns a style that sets text color to <paramref name="value"/>.</summary>
    public static Style TextColor(this Style style, Color value) =>
        style.Set(TypographyProperties.TextColor, value);

    /// <summary>Returns a style that gets text color from the active theme token.</summary>
    public static Style TextColor(this Style style, Token<Color> value) =>
        style.Set(TypographyProperties.TextColor, value);

    /// <summary>Returns a style that gets text color from a live reader.</summary>
    public static Style TextColor(this Style style, Func<Color> read) =>
        style.Bind(TypographyProperties.TextColor, read);

    /// <summary>Returns a style that sets font family to <paramref name="value"/>.</summary>
    public static Style FontFamily(this Style style, string value) =>
        style.Set(TypographyProperties.FontFamily, value);

    /// <summary>Returns a style that gets font family from the active theme token.</summary>
    public static Style FontFamily(this Style style, Token<string> value) =>
        style.Set(TypographyProperties.FontFamily, value);

    /// <summary>Returns a style that gets font family from a live reader.</summary>
    public static Style FontFamily(this Style style, Func<string> read) =>
        style.Bind(TypographyProperties.FontFamily, read);

    /// <summary>Returns a style that sets font size to <paramref name="value"/>.</summary>
    public static Style FontSize(this Style style, float value) =>
        style.Set(TypographyProperties.FontSize, value);

    /// <summary>Returns a style that gets font size from the active theme token.</summary>
    public static Style FontSize(this Style style, Token<float> value) =>
        style.Set(TypographyProperties.FontSize, value);

    /// <summary>Returns a style that gets font size from a live reader.</summary>
    public static Style FontSize(this Style style, Func<float> read) =>
        style.Bind(TypographyProperties.FontSize, read);

    /// <summary>Returns a style that sets the requested font weight.</summary>
    public static Style FontWeight(this Style style, FontWeight value) =>
        style.Set(TypographyProperties.FontWeight, value);

    /// <summary>Returns a style that gets font weight from the active theme token.</summary>
    public static Style FontWeight(this Style style, Token<FontWeight> value) =>
        style.Set(TypographyProperties.FontWeight, value);

    /// <summary>Returns a style that gets font weight from a live reader.</summary>
    public static Style FontWeight(this Style style, Func<FontWeight> read) =>
        style.Bind(TypographyProperties.FontWeight, read);

    /// <summary>Returns a style that sets text language to <paramref name="value"/>.</summary>
    public static Style Language(this Style style, string value) =>
        style.Set(TypographyProperties.Language, value);

    /// <summary>Returns a style that gets text language from the active theme token.</summary>
    public static Style Language(this Style style, Token<string> value) =>
        style.Set(TypographyProperties.Language, value);

    /// <summary>Returns a style that gets text language from a live reader.</summary>
    public static Style Language(this Style style, Func<string> read) =>
        style.Bind(TypographyProperties.Language, read);

    /// <summary>Returns a style that sets text direction to <paramref name="value"/>.</summary>
    public static Style Direction(this Style style, TextDirection value) =>
        style.Set(TypographyProperties.Direction, value);

    /// <summary>Returns a style that gets text direction from the active theme token.</summary>
    public static Style Direction(this Style style, Token<TextDirection> value) =>
        style.Set(TypographyProperties.Direction, value);

    /// <summary>Returns a style that gets text direction from a live reader.</summary>
    public static Style Direction(this Style style, Func<TextDirection> read) =>
        style.Bind(TypographyProperties.Direction, read);

    /// <summary>Returns a style that sets input handling to <paramref name="value"/>.</summary>
    public static Style Enabled(this Style style, bool value) =>
        style.Set(InputProperties.Enabled, value);

    /// <summary>Returns a style that gets input handling from the active theme token.</summary>
    public static Style Enabled(this Style style, Token<bool> value) =>
        style.Set(InputProperties.Enabled, value);

    /// <summary>Returns a style that gets input handling from a live reader.</summary>
    public static Style Enabled(this Style style, Func<bool> read) =>
        style.Bind(InputProperties.Enabled, read);

    /// <summary>Returns a style that sets visibility to <paramref name="value"/>.</summary>
    public static Style Visible(this Style style, bool value) =>
        style.Set(InputProperties.Visible, value);

    /// <summary>Returns a style that gets visibility from the active theme token.</summary>
    public static Style Visible(this Style style, Token<bool> value) =>
        style.Set(InputProperties.Visible, value);

    /// <summary>Returns a style that gets visibility from a live reader.</summary>
    public static Style Visible(this Style style, Func<bool> read) =>
        style.Bind(InputProperties.Visible, read);

    /// <summary>Returns a style that controls retained subtree participation.</summary>
    public static Style Participation(this Style style, ElementParticipation value) =>
        style.Set(VisualProperties.Participation, value);

    /// <summary>Returns a style that reads retained subtree participation from a theme token.</summary>
    public static Style Participation(this Style style, Token<ElementParticipation> value) =>
        style.Set(VisualProperties.Participation, value);

    /// <summary>Returns a style that reads retained subtree participation reactively.</summary>
    public static Style Participation(this Style style, Func<ElementParticipation> read) =>
        style.Bind(VisualProperties.Participation, read);

    /// <summary>Returns a style with the typed Mode assignment.</summary>
    public static Style Mode(this Style style, LayoutMode value) =>
        style.Set(LayoutProperties.Mode, value);

    /// <summary>Returns a style with the typed container algorithm assignment.</summary>
    public static Style Algorithm(this Style style, LayoutAlgorithm? value) =>
        style.Set(LayoutProperties.Algorithm, value);

    /// <summary>Returns a style with the typed Columns assignment.</summary>
    public static Style Columns(this Style style, GridTracks value) =>
        style.Set(LayoutProperties.Columns, value);

    /// <summary>Returns a style with the typed Rows assignment.</summary>
    public static Style Rows(this Style style, GridTracks value) =>
        style.Set(LayoutProperties.Rows, value);

    /// <summary>Returns a style with the typed ColumnGap assignment.</summary>
    public static Style ColumnGap(this Style style, float value) =>
        style.Set(LayoutProperties.ColumnGap, value);

    /// <summary>Returns a style with the typed RowGap assignment.</summary>
    public static Style RowGap(this Style style, float value) =>
        style.Set(LayoutProperties.RowGap, value);

    /// <summary>Returns a style with the typed GridPlacement assignment.</summary>
    public static Style GridPlacement(this Style style, GridPlacement? value) =>
        style.Set(LayoutProperties.GridPlacement, value);

    /// <summary>Returns a style with the typed MainBasis assignment.</summary>
    public static Style MainBasis(this Style style, float? value) =>
        style.Set(LayoutProperties.MainBasis, value);

    /// <summary>Returns a style with the typed MainShrink assignment.</summary>
    public static Style MainShrink(this Style style, float value) =>
        style.Set(LayoutProperties.MainShrink, value);

    /// <summary>Returns a style with the typed Wrap assignment.</summary>
    public static Style Wrap(this Style style, bool value) =>
        style.Set(LayoutProperties.Wrap, value);

    /// <summary>Returns a style with the typed TextWrap assignment.</summary>
    public static Style TextWrap(this Style style, TextWrap value) =>
        style.Set(TypographyProperties.TextWrap, value);

    /// <summary>Returns a style with the typed MaxLines assignment.</summary>
    public static Style MaxLines(this Style style, int? value) =>
        style.Set(TypographyProperties.MaxLines, value);

    /// <summary>Returns a style with the typed TextOverflow assignment.</summary>
    public static Style TextOverflow(this Style style, TextOverflow value) =>
        style.Set(TypographyProperties.Overflow, value);
}
