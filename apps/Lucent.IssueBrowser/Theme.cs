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
    internal static readonly Token<Brush> RowHoverSurface = new(
        "row-hover-surface",
        Color.Parse("#edf1f2")
    );
    internal static readonly Token<Brush> RowPressedSurface = new(
        "row-pressed-surface",
        Color.Parse("#e2e8f0")
    );
    internal static readonly Token<Brush> ScrollbarTrack = new(
        "issue-browser-scrollbar-track",
        Color.Parse("#e2e8f0")
    );
    internal static readonly Token<Brush> ScrollbarThumb = new(
        "issue-browser-scrollbar-thumb",
        Color.Parse("#94a3b8")
    );
    internal static readonly Token<Brush> ScrollbarThumbHover = new(
        "issue-browser-scrollbar-thumb-hover",
        Color.Parse("#64748b")
    );
    internal static readonly Token<Brush> ScrollbarThumbPressed = new(
        "issue-browser-scrollbar-thumb-pressed",
        Color.Parse("#475569")
    );
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
                Color.Parse("#000000"),
                Color.Parse("#0000ff"),
                Color.Parse("#ffff00"),
                Color.Parse("#404040"),
                Color.Parse("#ffffff"),
                Color.Parse("#ffff00"),
                Color.Parse("#00ffff")
            );
        return appearance.ColorScheme == ThemeColorScheme.Dark
            ? Apply(
                ControlThemes.Dark,
                Color.Parse("#0f172a"),
                Color.Parse("#f8fafc"),
                Color.Parse("#1e293b"),
                Color.Parse("#111827"),
                Color.Parse("#facc15"),
                Color.Parse("#0f172a"),
                Color.Parse("#1e293b"),
                Color.Parse("#334155"),
                Color.Parse("#1f2937"),
                Color.Parse("#64748b"),
                Color.Parse("#94a3b8"),
                Color.Parse("#cbd5e1")
            )
            : Apply(
                ControlThemes.Light,
                Color.Parse("#f8fafc"),
                Color.Parse("#0f172a"),
                Color.Parse("#e2e8f0"),
                Color.Parse("#ffffff"),
                Color.Parse("#ffff00"),
                Color.Parse("#0f172a"),
                Color.Parse("#edf1f2"),
                Color.Parse("#e2e8f0"),
                Color.Parse("#e2e8f0"),
                Color.Parse("#94a3b8"),
                Color.Parse("#64748b"),
                Color.Parse("#475569")
            );
    }

    private static Theme Apply(
        Theme controls,
        Color surface,
        Color foreground,
        Color header,
        Color row,
        Color focus,
        Color focusForeground,
        Color rowHover,
        Color rowPressed,
        Color scrollbarTrack,
        Color scrollbarThumb,
        Color scrollbarThumbHover,
        Color scrollbarThumbPressed
    ) =>
        controls
            .Set(Tokens.PageSurface, surface)
            .Set(Tokens.PageForeground, foreground)
            .Set(Tokens.HeaderSurface, header)
            .Set(Tokens.RowSurface, row)
            .Set(Tokens.RowHoverSurface, rowHover)
            .Set(Tokens.RowPressedSurface, rowPressed)
            .Set(Tokens.ScrollbarTrack, scrollbarTrack)
            .Set(Tokens.ScrollbarThumb, scrollbarThumb)
            .Set(Tokens.ScrollbarThumbHover, scrollbarThumbHover)
            .Set(Tokens.ScrollbarThumbPressed, scrollbarThumbPressed)
            .Set(Tokens.FocusSurface, focus)
            .Set(Tokens.FocusForeground, focusForeground);
}
