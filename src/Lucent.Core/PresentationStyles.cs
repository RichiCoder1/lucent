namespace Lucent.Core;

/// <summary>Selects the stock control decoration used by a composition.</summary>
public enum ControlPresentationMode
{
    /// <summary>Uses Lucent's polished themed control decoration.</summary>
    Standard,

    /// <summary>Removes stock backgrounds and borders while retaining geometry, states, and focus indication.</summary>
    Minimal,
}

/// <summary>Selects a named text role from the stock presentation.</summary>
public enum TextRole
{
    /// <summary>Ordinary readable content.</summary>
    Body,

    /// <summary>Prominent section or page heading content.</summary>
    Title,

    /// <summary>Secondary metadata or supporting content.</summary>
    Secondary,

    /// <summary>Small secondary supporting content.</summary>
    Caption,
}

/// <summary>Selects a named control and layout density.</summary>
public enum DensityPreset
{
    /// <summary>Roomier controls and spacing for ordinary desktop use.</summary>
    Comfortable,

    /// <summary>Reduced control and spacing metrics for dense information layouts.</summary>
    Compact,
}

/// <summary>Validated metrics associated with a stock <see cref="DensityPreset"/>.</summary>
public readonly record struct DensityMetrics
{
    /// <summary>Creates a density metric set with positive dimensions and finite spacing.</summary>
    public DensityMetrics(
        float fontSize,
        float controlHeight,
        float rowHeight,
        float spacing,
        Insets padding
    )
    {
        if (!float.IsFinite(fontSize) || fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (!float.IsFinite(controlHeight) || controlHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(controlHeight));
        if (!float.IsFinite(rowHeight) || rowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowHeight));
        if (!float.IsFinite(spacing) || spacing < 0)
            throw new ArgumentOutOfRangeException(nameof(spacing));

        FontSize = fontSize;
        ControlHeight = controlHeight;
        RowHeight = rowHeight;
        Spacing = spacing;
        Padding = padding;
    }

    /// <summary>Gets the inherited body font size in logical pixels.</summary>
    public float FontSize { get; }

    /// <summary>Gets the minimum height recommended for interactive controls.</summary>
    public float ControlHeight { get; }

    /// <summary>Gets the fixed row height recommended for virtualized list rows.</summary>
    public float RowHeight { get; }

    /// <summary>Gets the gap recommended between adjacent content items.</summary>
    public float Spacing { get; }

    /// <summary>Gets the inner padding recommended for density-aware content.</summary>
    public Insets Padding { get; }

    /// <summary>Gets the comfortable stock metrics.</summary>
    public static DensityMetrics Comfortable => new(14, 36, 36, 8, Insets.Symmetric(12, 8));

    /// <summary>Gets the compact stock metrics.</summary>
    public static DensityMetrics Compact => new(13, 30, 30, 6, Insets.Symmetric(8, 6));

    /// <summary>Returns the metrics associated with a named preset.</summary>
    public static DensityMetrics For(DensityPreset preset) =>
        preset switch
        {
            DensityPreset.Comfortable => Comfortable,
            DensityPreset.Compact => Compact,
            _ => throw new ArgumentOutOfRangeException(nameof(preset)),
        };
}

/// <summary>Small additive styles shared by stock applications and custom compositions.</summary>
public static class PresentationStyles
{
    /// <summary>Applies the stock surface and foreground tokens to a container.</summary>
    /// <remarks>Built-in Panel, Row, Column, and Text components inherit their surrounding paint. Apply this style at an application or panel surface boundary so nested interactive states remain visible.</remarks>
    public static Style Surface { get; } =
        Style
            .Empty.Set(VisualProperties.Background, ControlThemes.Surface)
            .Set(TypographyProperties.TextColor, ControlThemes.Foreground);

    /// <summary>Applies ordinary stock body typography and foreground color.</summary>
    public static Style Body { get; } =
        Typography(TextRole.Body).Set(TypographyProperties.TextColor, ControlThemes.Foreground);

    /// <summary>Applies stock heading typography and foreground color.</summary>
    public static Style Title { get; } =
        Typography(TextRole.Title).Set(TypographyProperties.TextColor, ControlThemes.Foreground);

    /// <summary>Applies stock secondary metadata typography and color.</summary>
    public static Style Secondary { get; } =
        Typography(TextRole.Secondary)
            .Set(TypographyProperties.TextColor, ControlThemes.SecondaryForeground);

    /// <summary>Applies stock small secondary typography and color.</summary>
    public static Style Caption { get; } =
        Typography(TextRole.Caption)
            .Set(TypographyProperties.TextColor, ControlThemes.SecondaryForeground);

    /// <summary>Applies only the metrics for a named text role, preserving an ancestor's inherited color.</summary>
    /// <remarks>Use this role inside stateful controls such as a selected row. Use <see cref="Body"/>, <see cref="Title"/>, <see cref="Secondary"/>, or <see cref="Caption"/> when the role's themed color should be an explicit author override.</remarks>
    public static Style Typography(TextRole role) =>
        role switch
        {
            TextRole.Body => Style
                .Empty.Set(TypographyProperties.FontSize, DensityMetrics.Comfortable.FontSize)
                .Set(TypographyProperties.FontWeight, FontWeight.Regular),
            TextRole.Title => Style
                .Empty.Set(TypographyProperties.FontSize, 18)
                .Set(TypographyProperties.FontWeight, FontWeight.SemiBold),
            TextRole.Secondary => Style
                .Empty.Set(TypographyProperties.FontSize, 13)
                .Set(TypographyProperties.FontWeight, FontWeight.Regular),
            TextRole.Caption => Style
                .Empty.Set(TypographyProperties.FontSize, 12)
                .Set(TypographyProperties.FontWeight, FontWeight.Regular),
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

    /// <summary>Returns the style for a named text role, including its themed color.</summary>
    public static Style Text(TextRole role) =>
        role switch
        {
            TextRole.Body => Body,
            TextRole.Title => Title,
            TextRole.Secondary => Secondary,
            TextRole.Caption => Caption,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

    /// <summary>Returns an additive style for the requested density metrics.</summary>
    public static Style ForDensity(DensityPreset preset) => ForDensity(DensityMetrics.For(preset));

    /// <summary>Returns an additive style for caller-supplied validated density metrics.</summary>
    public static Style ForDensity(DensityMetrics metrics) =>
        Style
            .Empty.Set(TypographyProperties.FontSize, metrics.FontSize)
            .Set(LayoutProperties.MinHeight, metrics.ControlHeight)
            .Set(LayoutProperties.Spacing, metrics.Spacing)
            .Set(LayoutProperties.Padding, metrics.Padding);

    internal static Brush TransparentBrush { get; } = Brush.Solid(Color.FromArgb(0, 0, 0, 0));
}
