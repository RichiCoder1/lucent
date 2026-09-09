namespace Lucent.Core;

/// <summary>Controls whether a viewport exposes its default vertical scrollbar.</summary>
public enum ScrollBarVisibility
{
    /// <summary>Reserves a stable gutter and shows the bar only when the viewport has vertical overflow.</summary>
    Auto,

    /// <summary>Never reserves scrollbar space or paints a scrollbar.</summary>
    Hidden,

    /// <summary>Reserves scrollbar space and paints a bar even without overflow.</summary>
    Always,
}

/// <summary>Typed style hooks for the Core-owned vertical scrollbar decoration.</summary>
public static class ScrollBarProperties
{
    /// <summary>Controls whether the viewport reserves a scrollbar gutter.</summary>
    public static readonly Property<ScrollBarVisibility> Visibility = new(
        "scrollbar-visibility",
        ScrollBarVisibility.Hidden
    );

    /// <summary>Sets the reserved logical-pixel width of the vertical scrollbar gutter.</summary>
    public static readonly Property<float> Thickness = new("scrollbar-thickness", 12);

    /// <summary>Sets the smallest logical-pixel length of a vertical scrollbar thumb.</summary>
    public static readonly Property<float> MinimumThumbLength = new(
        "scrollbar-minimum-thumb-length",
        24
    );

    /// <summary>Paints the scrollbar track.</summary>
    public static readonly Property<Brush> TrackBrush = new(
        "scrollbar-track-brush",
        Brush.Solid(Color.FromArgb(0x40, 0x0f, 0x17, 0x2a))
    );

    /// <summary>Paints the scrollbar thumb.</summary>
    public static readonly Property<Brush> ThumbBrush = new(
        "scrollbar-thumb-brush",
        Brush.Solid(Color.FromRgb(0x64, 0x74, 0x8b))
    );

    /// <summary>Sets the thumb and track corner radius in logical pixels.</summary>
    public static readonly Property<float> ThumbCornerRadius = new(
        "scrollbar-thumb-corner-radius",
        6
    );

    /// <summary>Paints the thumb while the pointer is over this scrollbar.</summary>
    public static readonly Property<Brush> HoverThumbBrush = new(
        "scrollbar-hover-thumb-brush",
        Brush.Solid(Color.FromRgb(0x47, 0x55, 0x69))
    );

    /// <summary>Paints the thumb while it is being dragged.</summary>
    public static readonly Property<Brush> PressedThumbBrush = new(
        "scrollbar-pressed-thumb-brush",
        Brush.Solid(Color.FromRgb(0x33, 0x41, 0x55))
    );
}

/// <summary>Immutable vertical scrollbar geometry and resolved paint values for one retained viewport.</summary>
public readonly record struct RetainedScrollBar(
    ElementIdentity Viewport,
    LayoutRect Track,
    LayoutRect Thumb,
    ScrollOffset Maximum,
    Brush TrackBrush,
    Brush ThumbBrush,
    Brush HoverThumbBrush,
    Brush PressedThumbBrush,
    float CornerRadius
);
