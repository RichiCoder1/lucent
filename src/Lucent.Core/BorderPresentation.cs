namespace Lucent.Core;

/// <summary>Edges that participate in a box border.</summary>
[Flags]
public enum BorderSides
{
    /// <summary>No border edge.</summary>
    None = 0,

    /// <summary>The left edge.</summary>
    Left = 1,

    /// <summary>The top edge.</summary>
    Top = 2,

    /// <summary>The right edge.</summary>
    Right = 4,

    /// <summary>The bottom edge.</summary>
    Bottom = 8,

    /// <summary>All four edges.</summary>
    All = Left | Top | Right | Bottom,
}

/// <summary>An immutable inset box border with logical or one-device-pixel edge widths.</summary>
public readonly struct Border : IEquatable<Border>
{
    private Border(Brush brush, Insets widths, bool hairline)
    {
        Brush = brush ?? throw new ArgumentNullException(nameof(brush));
        Widths = widths;
        IsHairline = hairline;
    }

    /// <summary>Gets the border brush, or null for no border.</summary>
    public Brush? Brush { get; }

    /// <summary>Gets the participating logical edge widths.</summary>
    public Insets Widths { get; }

    /// <summary>Gets whether every participating edge is exactly one device pixel.</summary>
    public bool IsHairline { get; }

    /// <summary>Gets a border that paints no edges.</summary>
    public static Border None => default;

    /// <summary>Creates an equal-width inset border on all four edges.</summary>
    public static Border Uniform(Brush brush, float width)
    {
        if (!float.IsFinite(width) || width <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Border width must be finite and positive."
            );
        return new(brush, Insets.Uniform(width), false);
    }

    /// <summary>Creates an inset border with independent logical edge widths.</summary>
    public static Border Edges(Brush brush, Insets widths)
    {
        if (widths == Insets.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(widths),
                "At least one border edge is required."
            );
        return new(brush, widths, false);
    }

    /// <summary>Creates a one-device-pixel inset border on the selected edges.</summary>
    public static Border Hairline(Brush brush, BorderSides sides = BorderSides.All)
    {
        if (sides == BorderSides.None || (sides & ~BorderSides.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(sides));
        return new(
            brush,
            new Insets(
                sides.HasFlag(BorderSides.Left) ? 1 : 0,
                sides.HasFlag(BorderSides.Top) ? 1 : 0,
                sides.HasFlag(BorderSides.Right) ? 1 : 0,
                sides.HasFlag(BorderSides.Bottom) ? 1 : 0
            ),
            true
        );
    }

    /// <summary>Compares brush, widths, and hairline behavior.</summary>
    public bool Equals(Border other) =>
        Equals(Brush, other.Brush) && Widths == other.Widths && IsHairline == other.IsHairline;

    /// <summary>Compares brush, widths, and hairline behavior.</summary>
    public override bool Equals(object? obj) => obj is Border other && Equals(other);

    /// <summary>Returns a hash code based on brush, widths, and hairline behavior.</summary>
    public override int GetHashCode() => HashCode.Combine(Brush, Widths, IsHairline);

    /// <summary>Compares complete border values.</summary>
    public static bool operator ==(Border left, Border right) => left.Equals(right);

    /// <summary>Compares complete border values.</summary>
    public static bool operator !=(Border left, Border right) => !left.Equals(right);

    /// <summary>Returns the canonical border diagnostic.</summary>
    public override string ToString() =>
        Brush is null
            ? "border(none)"
            : "border(" + (IsHairline ? "hairline," : "") + Widths + "," + Brush + ")";
}

/// <summary>An immutable keyboard-focus ring painted inside an element's outer bounds.</summary>
public readonly struct FocusRing : IEquatable<FocusRing>
{
    private FocusRing(Brush brush, float thickness)
    {
        Brush = brush ?? throw new ArgumentNullException(nameof(brush));
        Thickness = thickness;
    }

    /// <summary>Gets the focus-ring brush, or null for no ring.</summary>
    public Brush? Brush { get; }

    /// <summary>Gets the inset ring thickness in logical pixels.</summary>
    public float Thickness { get; }

    /// <summary>Gets a focus ring that paints nothing.</summary>
    public static FocusRing None => default;

    /// <summary>Creates a uniform focus ring painted inside the outer bounds.</summary>
    public static FocusRing Inset(Brush brush, float thickness)
    {
        if (!float.IsFinite(thickness) || thickness <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(thickness),
                "Focus-ring thickness must be finite and positive."
            );
        return new(brush, thickness);
    }

    /// <summary>Compares brush and inset thickness.</summary>
    public bool Equals(FocusRing other) =>
        Equals(Brush, other.Brush) && Thickness == other.Thickness;

    /// <summary>Compares brush and inset thickness.</summary>
    public override bool Equals(object? obj) => obj is FocusRing other && Equals(other);

    /// <summary>Returns a hash code based on brush and inset thickness.</summary>
    public override int GetHashCode() => HashCode.Combine(Brush, Thickness);

    /// <summary>Compares complete focus-ring values.</summary>
    public static bool operator ==(FocusRing left, FocusRing right) => left.Equals(right);

    /// <summary>Compares complete focus-ring values.</summary>
    public static bool operator !=(FocusRing left, FocusRing right) => !left.Equals(right);

    /// <summary>Returns the canonical focus-ring diagnostic.</summary>
    public override string ToString() =>
        Brush is null
            ? "focus-ring(none)"
            : "focus-ring(inset,"
                + Thickness.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + ","
                + Brush
                + ")";
}
