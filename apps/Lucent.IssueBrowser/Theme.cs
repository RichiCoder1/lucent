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
}

internal static class AppTheme
{
    internal static Theme Create(ThemeAppearance appearance)
    {
        appearance.Validate();
        if (appearance.Contrast == ThemeContrast.High)
            return Apply(
                ControlThemes.HighContrast,
                Color.Parse("#000000"),
                Color.Parse("#ffffff"),
                Color.Parse("#000000"),
                Color.Parse("#000000"),
                Color.Parse("#ffff00"),
                Color.Parse("#000000")
            );
        return appearance.ColorScheme == ThemeColorScheme.Dark
            ? Apply(
                ControlThemes.Dark,
                Color.Parse("#0f172a"),
                Color.Parse("#f8fafc"),
                Color.Parse("#1e293b"),
                Color.Parse("#111827"),
                Color.Parse("#facc15"),
                Color.Parse("#0f172a")
            )
            : Apply(
                ControlThemes.Light,
                Color.Parse("#f8fafc"),
                Color.Parse("#0f172a"),
                Color.Parse("#e2e8f0"),
                Color.Parse("#ffffff"),
                Color.Parse("#ffff00"),
                Color.Parse("#0f172a")
            );
    }

    private static Theme Apply(
        Theme controls,
        Color surface,
        Color foreground,
        Color header,
        Color row,
        Color focus,
        Color focusForeground
    ) =>
        controls
            .Set(Tokens.PageSurface, surface)
            .Set(Tokens.PageForeground, foreground)
            .Set(Tokens.HeaderSurface, header)
            .Set(Tokens.RowSurface, row)
            .Set(Tokens.FocusSurface, focus)
            .Set(Tokens.FocusForeground, focusForeground);
}
