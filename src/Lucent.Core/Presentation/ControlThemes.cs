namespace Lucent.Core;

/// <summary>Built-in color palettes for Lucent controls. Use one as the starting theme for an application.</summary>
public static class ControlThemes
{
    internal static readonly Token<Brush> Surface = new("control-surface", Color.Parse("#ffffff"));
    internal static readonly Token<Color> SurfaceColor = new(
        "control-surface-color",
        Color.Parse("#ffffff")
    );
    internal static readonly Token<Color> Foreground = new(
        "control-foreground",
        Color.Parse("#0f172a")
    );
    internal static readonly Token<Color> SecondaryForeground = new(
        "control-secondary-foreground",
        Color.Parse("#475569")
    );
    internal static readonly Token<Color> DisabledForeground = new(
        "control-disabled-foreground",
        Color.Parse("#475569")
    );
    internal static readonly Token<Brush> Accent = new("control-accent", Color.Parse("#2563eb"));
    internal static readonly Token<Brush> AccentPressed = new(
        "control-accent-pressed",
        Color.Parse("#1e40af")
    );
    internal static readonly Token<Brush> AccentHover = new(
        "control-accent-hover",
        Color.Parse("#1d4ed8")
    );
    internal static readonly Token<Brush> Hover = new("control-hover", Color.Parse("#f1f5f9"));
    internal static readonly Token<Brush> Pressed = new("control-pressed", Color.Parse("#e2e8f0"));
    internal static readonly Token<Brush> Destructive = new(
        "control-destructive",
        Color.Parse("#b91c1c")
    );
    internal static readonly Token<Brush> DestructiveHover = new(
        "control-destructive-hover",
        Color.Parse("#991b1b")
    );
    internal static readonly Token<Brush> DestructivePressed = new(
        "control-destructive-pressed",
        Color.Parse("#7f1d1d")
    );
    internal static readonly Token<Color> ErrorForeground = new(
        "control-error-foreground",
        Color.Parse("#b91c1c")
    );
    internal static readonly Token<Brush> Selected = new(
        "control-selected",
        Color.Parse("#dbeafe")
    );
    internal static readonly Token<Brush> Focus = new("control-focus", Color.Parse("#1d4ed8"));
    internal static readonly Token<global::Lucent.Core.FocusRing> FocusRing = new(
        "control-focus-ring",
        global::Lucent.Core.FocusRing.Inset(Color.Parse("#1d4ed8"), 2)
    );
    internal static readonly Token<global::Lucent.Core.FocusRing> AccentFocusRing = new(
        "control-accent-focus-ring",
        global::Lucent.Core.FocusRing.Inset(Color.Parse("#ffffff"), 2)
    );
    internal static readonly Token<Color> FocusForeground = new(
        "control-focus-foreground",
        Color.Parse("#000000")
    );
    internal static readonly Token<Brush> Disabled = new(
        "control-disabled",
        Color.Parse("#94a3b8")
    );
    internal static readonly Token<Brush> Border = new("control-border", Color.Parse("#cbd5e1"));
    internal static readonly Token<Brush> BorderHover = new(
        "control-border-hover",
        Color.Parse("#94a3b8")
    );
    internal static readonly Token<Brush> Divider = new("control-divider", Color.Parse("#cbd5e1"));
    internal static readonly Token<Brush> ScrollTrack = new(
        "control-scrollbar-track",
        Color.Parse("#00000040")
    );
    internal static readonly Token<Brush> ScrollThumb = new(
        "control-scrollbar-thumb",
        Color.Parse("#64748b")
    );
    internal static readonly Token<Brush> ScrollThumbHover = new(
        "control-scrollbar-thumb-hover",
        Color.Parse("#475569")
    );
    internal static readonly Token<Brush> ScrollThumbPressed = new(
        "control-scrollbar-thumb-pressed",
        Color.Parse("#334155")
    );

    /// <summary>Gets a light palette for controls.</summary>
    public static Theme Light { get; } =
        Palette(
            "controls-light",
            Color.Parse("#ffffff"),
            Color.Parse("#0f172a"),
            Color.Parse("#475569"),
            Color.Parse("#475569"),
            Color.Parse("#2563eb"),
            Color.Parse("#1e40af"),
            Color.Parse("#dbeafe"),
            Color.Parse("#1d4ed8"),
            Color.Parse("#000000"),
            Color.Parse("#94a3b8"),
            Color.Parse("#cbd5e1"),
            Color.Parse("#94a3b8"),
            Color.Parse("#cbd5e1"),
            accentHover: Color.Parse("#1d4ed8"),
            hover: Color.Parse("#f1f5f9"),
            pressed: Color.Parse("#e2e8f0"),
            destructive: Color.Parse("#b91c1c"),
            destructiveHover: Color.Parse("#991b1b"),
            destructivePressed: Color.Parse("#7f1d1d"),
            errorForeground: Color.Parse("#b91c1c")
        );

    /// <summary>Gets a dark palette for controls.</summary>
    public static Theme Dark { get; } =
        Palette(
            "controls-dark",
            Color.Parse("#111827"),
            Color.Parse("#f8fafc"),
            Color.Parse("#cbd5e1"),
            Color.Parse("#cbd5e1"),
            Color.Parse("#60a5fa"),
            Color.Parse("#3b82f6"),
            Color.Parse("#1e3a5f"),
            Color.Parse("#facc15"),
            Color.Parse("#000000"),
            Color.Parse("#64748b"),
            Color.Parse("#475569"),
            Color.Parse("#94a3b8"),
            Color.Parse("#475569"),
            accentHover: Color.Parse("#5398f8"),
            hover: Color.Parse("#1f2937"),
            pressed: Color.Parse("#374151"),
            destructive: Color.Parse("#f87171"),
            destructiveHover: Color.Parse("#f96161"),
            destructivePressed: Color.Parse("#f05252"),
            errorForeground: Color.Parse("#fca5a5")
        );

    /// <summary>Gets a high-contrast palette for controls.</summary>
    public static Theme HighContrast { get; } =
        Palette(
            "controls-high-contrast",
            Color.Parse("#000000"),
            Color.Parse("#ffffff"),
            Color.Parse("#ffffff"),
            Color.Parse("#ffffff"),
            Color.Parse("#ffffff"),
            Color.Parse("#ffff00"),
            Color.Parse("#0000ff"),
            Color.Parse("#ffff00"),
            Color.Parse("#000000"),
            Color.Parse("#808080"),
            Color.Parse("#ffffff"),
            Color.Parse("#ffff00"),
            Color.Parse("#ffffff"),
            accentHover: Color.Parse("#00ffff"),
            hover: Color.Parse("#1a1a1a"),
            pressed: Color.Parse("#404040"),
            destructive: Color.Parse("#ff8080"),
            destructiveHover: Color.Parse("#ffaaaa"),
            destructivePressed: Color.Parse("#ffcccc"),
            errorForeground: Color.Parse("#ff8080"),
            focusRing: Color.Parse("#000000")
        );

    private static Theme Palette(
        string name,
        Color surface,
        Color foreground,
        Color secondaryForeground,
        Color disabledForeground,
        Color accent,
        Color accentPressed,
        Color selected,
        Color focus,
        Color focusForeground,
        Color disabled,
        Color border,
        Color borderHover,
        Color divider,
        Color accentHover,
        Color hover,
        Color pressed,
        Color destructive,
        Color destructiveHover,
        Color destructivePressed,
        Color errorForeground,
        Color? focusRing = null
    ) =>
        new Theme(name)
            .Set(Surface, (Brush)surface)
            .Set(SurfaceColor, surface)
            .Set(Foreground, foreground)
            .Set(SecondaryForeground, secondaryForeground)
            .Set(DisabledForeground, disabledForeground)
            .Set(Accent, (Brush)accent)
            .Set(AccentPressed, (Brush)accentPressed)
            .Set(AccentHover, (Brush)accentHover)
            .Set(Hover, (Brush)hover)
            .Set(Pressed, (Brush)pressed)
            .Set(Destructive, (Brush)destructive)
            .Set(DestructiveHover, (Brush)destructiveHover)
            .Set(DestructivePressed, (Brush)destructivePressed)
            .Set(ErrorForeground, errorForeground)
            .Set(Selected, (Brush)selected)
            .Set(Focus, (Brush)focus)
            .Set(FocusRing, global::Lucent.Core.FocusRing.Inset((Brush)(focusRing ?? focus), 2))
            .Set(AccentFocusRing, global::Lucent.Core.FocusRing.Inset(surface, 2))
            .Set(FocusForeground, focusForeground)
            .Set(Disabled, (Brush)disabled)
            .Set(Border, (Brush)border)
            .Set(BorderHover, (Brush)borderHover)
            .Set(Divider, (Brush)divider)
            .Set(ScrollTrack, (Brush)Color.FromArgb(0x40, foreground.R, foreground.G, foreground.B))
            .Set(ScrollThumb, (Brush)foreground)
            .Set(ScrollThumbHover, (Brush)accent)
            .Set(ScrollThumbPressed, (Brush)accentPressed);
}
