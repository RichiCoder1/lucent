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
                Color.Parse("#000000")
            );
        return appearance.ColorScheme == ThemeColorScheme.Dark
            ? Apply(
                ControlThemes.Dark,
                Color.Parse("#0f172a"),
                Color.Parse("#f8fafc"),
                Color.Parse("#1e293b")
            )
            : Apply(
                ControlThemes.Light,
                Color.Parse("#f8fafc"),
                Color.Parse("#0f172a"),
                Color.Parse("#e2e8f0")
            );
    }

    private static Theme Apply(Theme controls, Color surface, Color foreground, Color header) =>
        controls
            .Set(Tokens.PageSurface, surface)
            .Set(Tokens.PageForeground, foreground)
            .Set(Tokens.HeaderSurface, header);
}
