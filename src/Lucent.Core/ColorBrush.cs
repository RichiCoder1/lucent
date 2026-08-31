using System.Globalization;
using System.Numerics;
using System.Collections.ObjectModel;

namespace Lucent.Core;

/// <summary>Canonical immutable 8-bit sRGB RGBA color.</summary>
public readonly record struct Color(byte R, byte G, byte B, byte A)
{
    public static Color FromRgb(byte red, byte green, byte blue) => new(red, green, blue, byte.MaxValue);
    public static Color FromArgb(byte alpha, byte red, byte green, byte blue) => new(red, green, blue, alpha);
    public static Color Parse(string text)
    {
        if (TryParse(text, out var color)) return color;
        throw new FormatException("Color must be #RRGGBB or #RRGGBBAA.");
    }
    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (text is null || (text.Length != 7 && text.Length != 9) || text[0] != '#') return false;
        var alpha = byte.MaxValue;
        if (!byte.TryParse(text.AsSpan(1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(text.AsSpan(3, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(text.AsSpan(5, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var blue) ||
            (text.Length == 9 && !byte.TryParse(text.AsSpan(7, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out alpha))) return false;
        color = new(red, green, blue, alpha);
        return true;
    }
    public override string ToString() => $"#{R:x2}{G:x2}{B:x2}{A:x2}".ToUpperInvariant();
}

public readonly record struct GradientStop(float Position, Color Color)
{
    internal void Validate() { if (!float.IsFinite(Position) || Position is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(Position)); }
    public override string ToString() => Format(Position) + ":" + Color;
    internal static string Format(float value) => value == 0 ? "0" : value.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>Immutable normalized box-relative linear gradient.</summary>
public sealed class LinearGradient : IEquatable<LinearGradient>
{
    private readonly GradientStop[] _stops;
    private readonly ReadOnlyCollection<GradientStop> _stopsView;
    public LinearGradient(Vector2 start, Vector2 end, IReadOnlyList<GradientStop> stops)
    {
        if (!float.IsFinite(start.X) || !float.IsFinite(start.Y) || !float.IsFinite(end.X) || !float.IsFinite(end.Y) || start.X is < 0 or > 1 || start.Y is < 0 or > 1 || end.X is < 0 or > 1 || end.Y is < 0 or > 1 || start == end) throw new ArgumentException("Gradient endpoints must be distinct normalized points.");
        ArgumentNullException.ThrowIfNull(stops);
        _stops = stops.ToArray();
        if (_stops.Length is < 2 or > 16) throw new ArgumentException("A gradient requires two to sixteen stops.", nameof(stops));
        for (var index = 0; index < _stops.Length; index++)
        {
            _stops[index].Validate();
            if (_stops[index].Color.A != byte.MaxValue) throw new ArgumentException("Initial linear gradients require opaque stops.", nameof(stops));
            if (index > 0 && _stops[index].Position < _stops[index - 1].Position) throw new ArgumentException("Gradient stops must be nondecreasing.", nameof(stops));
        }
        Start = start; End = end; _stopsView = Array.AsReadOnly(_stops);
    }
    public Vector2 Start { get; }
    public Vector2 End { get; }
    public IReadOnlyList<GradientStop> Stops => _stopsView;
    public bool Equals(LinearGradient? other) => other is not null && Start == other.Start && End == other.End && _stops.SequenceEqual(other._stops);
    public override bool Equals(object? obj) => obj is LinearGradient other && Equals(other);
    public override int GetHashCode() { var hash = new HashCode(); hash.Add(Start); hash.Add(End); foreach (var stop in _stops) hash.Add(stop); return hash.ToHashCode(); }
    public override string ToString() => "linear(" + GradientStop.Format(Start.X) + "," + GradientStop.Format(Start.Y) + " -> " + GradientStop.Format(End.X) + "," + GradientStop.Format(End.Y) + "; " + string.Join(",", _stops) + ")";
}

/// <summary>Closed immutable box-local paint: a solid color or a bounded linear gradient.</summary>
public sealed class Brush : IEquatable<Brush>
{
    private Brush(Color? color, LinearGradient? gradient) { Color = color; Gradient = gradient; }
    public Color? Color { get; }
    public LinearGradient? Gradient { get; }
    public static Brush Solid(Color color) => new(color, null);
    public static implicit operator Brush(Color color) => Solid(color);
    public static implicit operator Brush(LinearGradient gradient) => new(null, gradient ?? throw new ArgumentNullException(nameof(gradient)));
    public bool Equals(Brush? other) => other is not null && Color == other.Color && Equals(Gradient, other.Gradient);
    public override bool Equals(object? obj) => obj is Brush other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Color, Gradient);
    public override string ToString() => Color is { } color ? "solid(" + color + ")" : Gradient!.ToString();
}
