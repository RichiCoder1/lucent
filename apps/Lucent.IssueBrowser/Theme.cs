namespace Lucent.IssueBrowser;

internal static class Tokens
{
    internal static readonly Token<Brush> PageSurface = new("page-surface", Color.Parse("#f8fafc"));
    internal static readonly Token<Color> PageForeground = new(
        "page-foreground",
        Color.Parse("#0f172a")
    );
    internal static readonly Token<Brush> HeaderSurface = new(
        "header-surface",
        Color.Parse("#e2e8f0")
    );
    internal static readonly Token<Brush> RowSurface = new("row-surface", Color.Parse("#ffffff"));
    internal static readonly Token<Brush> FocusSurface = new(
        "focus-surface",
        Color.Parse("#ffff00")
    );
    internal static readonly Token<Color> FocusForeground = new(
        "focus-foreground",
        Color.Parse("#0f172a")
    );
    internal static readonly Token<float?> DensityHeaderHeight = new(
        "issue-density-header-height",
        84f
    );
    internal static readonly Token<float?> DensityFilterHeight = new(
        "issue-density-filter-height",
        28f
    );
    internal static readonly Token<float> DensitySpacing = new("issue-density-spacing", 8f);
    internal static readonly Token<float> DensityFontSize = new("issue-density-font-size", 14f);
    internal static readonly Token<float> DensityTitleFontSize = new(
        "issue-density-title-font-size",
        18f
    );
}

internal static class AppTheme
{
    internal static Theme Create(
        Theme controls,
        Color surface,
        Color foreground,
        Color header,
        Color row,
        Color focus,
        Color focusForeground,
        IssueDensity density
    ) =>
        controls
            .Set(Tokens.PageSurface, (Brush)surface)
            .Set(Tokens.PageForeground, foreground)
            .Set(Tokens.HeaderSurface, (Brush)header)
            .Set(Tokens.RowSurface, (Brush)row)
            .Set(Tokens.FocusSurface, (Brush)focus)
            .Set(Tokens.FocusForeground, focusForeground)
            .Set(Tokens.DensityHeaderHeight, density == IssueDensity.Comfortable ? 84f : 68f)
            .Set(Tokens.DensityFilterHeight, density == IssueDensity.Comfortable ? 28f : 22f)
            .Set(Tokens.DensitySpacing, density == IssueDensity.Comfortable ? 8f : 4f)
            .Set(Tokens.DensityFontSize, density == IssueDensity.Comfortable ? 14f : 12f)
            .Set(Tokens.DensityTitleFontSize, density == IssueDensity.Comfortable ? 18f : 16f);
}
