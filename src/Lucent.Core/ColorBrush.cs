using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;

namespace Lucent.Core;

/// <summary>Canonical immutable 8-bit sRGB RGBA color.</summary>
public readonly record struct Color(byte R, byte G, byte B, byte A)
{
    /// <summary>Creates an opaque sRGB color from red, green, and blue channels.</summary>
    public static Color FromRgb(byte red, byte green, byte blue) =>
        new(red, green, blue, byte.MaxValue);

    /// <summary>Creates an sRGB color with explicit alpha followed by red, green, and blue channels.</summary>
    public static Color FromArgb(byte alpha, byte red, byte green, byte blue) =>
        new(red, green, blue, alpha);

    /// <summary>Parses <c>#RRGGBB</c> or <c>#RRGGBBAA</c>; throws when the text is not a canonical color.</summary>
    public static Color Parse(string text)
    {
        if (TryParse(text, out var color))
            return color;
        throw new FormatException("Color must be #RRGGBB or #RRGGBBAA.");
    }

    /// <summary>Attempts to parse <c>#RRGGBB</c> or <c>#RRGGBBAA</c> without throwing.</summary>
    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (text is null || (text.Length != 7 && text.Length != 9) || text[0] != '#')
            return false;
        var alpha = byte.MaxValue;
        if (
            !byte.TryParse(
                text.AsSpan(1, 2),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var red
            )
            || !byte.TryParse(
                text.AsSpan(3, 2),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var green
            )
            || !byte.TryParse(
                text.AsSpan(5, 2),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var blue
            )
            || (
                text.Length == 9
                && !byte.TryParse(
                    text.AsSpan(7, 2),
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out alpha
                )
            )
        )
            return false;
        color = new(red, green, blue, alpha);
        return true;
    }

    /// <summary>Returns this color as uppercase <c>#RRGGBBAA</c>.</summary>
    public override string ToString() => $"#{R:x2}{G:x2}{B:x2}{A:x2}".ToUpperInvariant();
}

/// <summary>An immutable normalized color stop for a linear gradient. Position is box-relative from zero through one.</summary>
public readonly record struct GradientStop(float Position, Color Color)
{
    internal void Validate()
    {
        if (!float.IsFinite(Position) || Position is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Position));
    }

    /// <summary>Returns the stop position followed by its color.</summary>
    public override string ToString() => Format(Position) + ":" + Color;

    internal static string Format(float value) =>
        value == 0 ? "0" : value.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>Immutable normalized box-relative linear gradient.</summary>
public sealed class LinearGradient : IEquatable<LinearGradient>
{
    private readonly GradientStop[] _stops;
    private readonly ReadOnlyCollection<GradientStop> _stopsView;

    /// <summary>Creates a normalized box-relative gradient with two to sixteen opaque, nondecreasing stops.</summary>
    public LinearGradient(Vector2 start, Vector2 end, IReadOnlyList<GradientStop> stops)
    {
        if (
            !float.IsFinite(start.X)
            || !float.IsFinite(start.Y)
            || !float.IsFinite(end.X)
            || !float.IsFinite(end.Y)
            || start.X is < 0 or > 1
            || start.Y is < 0 or > 1
            || end.X is < 0 or > 1
            || end.Y is < 0 or > 1
            || start == end
        )
            throw new ArgumentException("Gradient endpoints must be distinct normalized points.");
        ArgumentNullException.ThrowIfNull(stops);
        _stops = stops.ToArray();
        if (_stops.Length is < 2 or > 16)
            throw new ArgumentException("A gradient requires two to sixteen stops.", nameof(stops));
        for (var index = 0; index < _stops.Length; index++)
        {
            _stops[index].Validate();
            if (_stops[index].Color.A != byte.MaxValue)
                throw new ArgumentException(
                    "Initial linear gradients require opaque stops.",
                    nameof(stops)
                );
            if (index > 0 && _stops[index].Position < _stops[index - 1].Position)
                throw new ArgumentException("Gradient stops must be nondecreasing.", nameof(stops));
        }
        Start = start;
        End = end;
        _stopsView = Array.AsReadOnly(_stops);
    }

    /// <summary>Gets the normalized box-relative gradient start point.</summary>
    public Vector2 Start { get; }

    /// <summary>Gets the normalized box-relative gradient end point.</summary>
    public Vector2 End { get; }

    /// <summary>Gets the immutable, ordered gradient stops.</summary>
    public IReadOnlyList<GradientStop> Stops => _stopsView;

    /// <summary>Compares endpoints and ordered stops for value equality.</summary>
    public bool Equals(LinearGradient? other) =>
        other is not null
        && Start == other.Start
        && End == other.End
        && _stops.SequenceEqual(other._stops);

    /// <summary>Compares endpoints and ordered stops for value equality.</summary>
    public override bool Equals(object? obj) => obj is LinearGradient other && Equals(other);

    /// <summary>Returns a hash code based on endpoints and ordered stops.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Start);
        hash.Add(End);
        foreach (var stop in _stops)
            hash.Add(stop);
        return hash.ToHashCode();
    }

    /// <summary>Returns the normalized gradient diagnostic representation.</summary>
    public override string ToString() =>
        "linear("
        + GradientStop.Format(Start.X)
        + ","
        + GradientStop.Format(Start.Y)
        + " -> "
        + GradientStop.Format(End.X)
        + ","
        + GradientStop.Format(End.Y)
        + "; "
        + string.Join(",", _stops)
        + ")";
}

/// <summary>Closed immutable box-local paint: a solid color or a bounded linear gradient.</summary>
public sealed class Brush : IEquatable<Brush>
{
    private Brush(Color? color, LinearGradient? gradient)
    {
        Color = color;
        Gradient = gradient;
    }

    /// <summary>Gets the solid color, or <see langword="null"/> when this brush is a gradient.</summary>
    public Color? Color { get; }

    /// <summary>Gets the gradient, or <see langword="null"/> when this brush is solid.</summary>
    public LinearGradient? Gradient { get; }

    /// <summary>Creates an immutable solid brush.</summary>
    public static Brush Solid(Color color) => new(color, null);

    /// <summary>Converts a color or gradient to its immutable brush representation.</summary>
    public static implicit operator Brush(Color color) => Solid(color);

    /// <summary>Converts a color or gradient to its immutable brush representation.</summary>
    public static implicit operator Brush(LinearGradient gradient) =>
        new(null, gradient ?? throw new ArgumentNullException(nameof(gradient)));

    /// <summary>Compares the solid color or gradient representation for value equality.</summary>
    public bool Equals(Brush? other) =>
        other is not null && Color == other.Color && Equals(Gradient, other.Gradient);

    /// <summary>Compares the solid color or gradient representation for value equality.</summary>
    public override bool Equals(object? obj) => obj is Brush other && Equals(other);

    /// <summary>Returns a hash code based on the solid color or gradient representation.</summary>
    public override int GetHashCode() => HashCode.Combine(Color, Gradient);

    /// <summary>Returns the solid-color or gradient diagnostic representation.</summary>
    public override string ToString() =>
        Color is { } color ? "solid(" + color + ")" : Gradient!.ToString();
}
