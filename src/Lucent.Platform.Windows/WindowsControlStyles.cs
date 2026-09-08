using Lucent.Core;

namespace Lucent.Platform.Windows;

/// <summary>Opt-in Windows-oriented styles for Lucent-rendered controls.</summary>
/// <remarks>These styles retain Lucent layout, input and accessibility. They do not create native child controls.</remarks>
public static class WindowsControlStyles
{
    private static readonly Style LightScrollBar = CreateScrollBar(
        "#f3f3f3",
        "#737373",
        "#525252",
        "#333333",
        3
    );
    private static readonly Style DarkScrollBar = CreateScrollBar(
        "#202020",
        "#a6a6a6",
        "#cccccc",
        "#eeeeee",
        3
    );
    private static readonly Style HighContrastScrollBar = CreateScrollBar(
        "#000000",
        "#ffffff",
        "#ffff00",
        "#ffff00",
        0
    );

    /// <summary>Returns a visible, stable-gutter scrollbar style for the supplied appearance.</summary>
    /// <remarks>Append application overrides with <see cref="Style.With"/>. Re-evaluate when the application's appearance changes.</remarks>
    public static Style ScrollBar(ThemeAppearance appearance)
    {
        appearance.Validate();
        return appearance.Contrast == ThemeContrast.High ? HighContrastScrollBar
            : appearance.ColorScheme == ThemeColorScheme.Dark ? DarkScrollBar
            : LightScrollBar;
    }

    private static Style CreateScrollBar(
        string track,
        string thumb,
        string hover,
        string pressed,
        float radius
    ) =>
        Style
            .Empty.Set(ScrollBarProperties.Visibility, ScrollBarVisibility.Auto)
            .Set(ScrollBarProperties.Thickness, 14f)
            .Set(ScrollBarProperties.MinimumThumbLength, 28f)
            .Set(ScrollBarProperties.ThumbCornerRadius, radius)
            .Set(ScrollBarProperties.TrackBrush, (Brush)Color.Parse(track))
            .Set(ScrollBarProperties.ThumbBrush, (Brush)Color.Parse(thumb))
            .Set(ScrollBarProperties.HoverThumbBrush, (Brush)Color.Parse(hover))
            .Set(ScrollBarProperties.PressedThumbBrush, (Brush)Color.Parse(pressed));
}
