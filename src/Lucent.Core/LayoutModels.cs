using System.Collections;

namespace Lucent.Core;

/// <summary>Selects the bounded managed container algorithm.</summary>
public enum LayoutMode
{
    /// <summary>Uses one-dimensional flex lines.</summary>
    Flex,

    /// <summary>Uses explicit rows and columns.</summary>
    Grid,
}

/// <summary>Identifies a supported explicit Grid track form.</summary>
public enum GridTrackKind
{
    /// <summary>A constant logical size.</summary>
    Fixed,

    /// <summary>A content-sized track.</summary>
    Content,

    /// <summary>A weighted share of remaining space.</summary>
    Fraction,

    /// <summary>A bounded content or fractional track.</summary>
    MinMax,
}

/// <summary>An immutable validated Grid track.</summary>
public readonly record struct GridTrack
{
    private GridTrack(GridTrackKind kind, float minimum, float maximum, float fraction)
    {
        Kind = kind;
        Minimum = minimum;
        Maximum = maximum;
        FractionWeight = fraction;
    }

    /// <summary>Gets the sizing form.</summary>
    public GridTrackKind Kind { get; }

    /// <summary>Gets the minimum logical size.</summary>
    public float Minimum { get; }

    /// <summary>Gets the maximum logical size.</summary>
    public float Maximum { get; }

    /// <summary>Gets the positive fractional weight, or zero for non-fractional tracks.</summary>
    public float FractionWeight { get; }

    /// <summary>Creates a fixed logical-size track.</summary>
    public static GridTrack Fixed(float size)
    {
        ValidateFinite(size, nameof(size), false);
        return new(GridTrackKind.Fixed, size, size, 0);
    }

    /// <summary>Creates a content-sized track.</summary>
    public static GridTrack Content() => new(GridTrackKind.Content, 0, float.PositiveInfinity, 0);

    /// <summary>Creates a positive weighted fractional track.</summary>
    public static GridTrack Fraction(float fraction = 1)
    {
        ValidateFinite(fraction, nameof(fraction), true);
        return new(GridTrackKind.Fraction, 0, float.PositiveInfinity, fraction);
    }

    /// <summary>Creates a track with a fixed minimum and supported maximum form.</summary>
    public static GridTrack MinMax(float minimum, GridTrack maximum)
    {
        ValidateFinite(minimum, nameof(minimum), false);
        if (
            maximum.Kind
            is not (GridTrackKind.Fixed or GridTrackKind.Content or GridTrackKind.Fraction)
        )
            throw new ArgumentException(
                "A minmax maximum must be fixed, content, or fractional.",
                nameof(maximum)
            );
        if (maximum.Kind == GridTrackKind.Fixed && maximum.Maximum < minimum)
            throw new ArgumentException(
                "A fixed minmax maximum must not be smaller than its minimum.",
                nameof(maximum)
            );
        return new(
            GridTrackKind.MinMax,
            minimum,
            maximum.Kind == GridTrackKind.Fixed ? maximum.Maximum : float.PositiveInfinity,
            maximum.Kind == GridTrackKind.Fraction ? maximum.FractionWeight : 0f
        );
    }

    internal void Validate()
    {
        if (
            !Enum.IsDefined(Kind)
            || !float.IsFinite(Minimum)
            || Minimum < 0
            || (!float.IsFinite(Maximum) && !float.IsPositiveInfinity(Maximum))
            || Maximum < Minimum
            || !float.IsFinite(FractionWeight)
            || FractionWeight < 0
            || Kind == GridTrackKind.Fraction && FractionWeight <= 0
        )
            throw new ArgumentException(
                "Grid tracks must use the supported fixed/content/fr/minmax forms."
            );
    }

    private static void ValidateFinite(float value, string parameter, bool positive)
    {
        if (!float.IsFinite(value) || (positive ? value <= 0 : value < 0))
            throw new ArgumentOutOfRangeException(parameter);
    }
}

/// <summary>An immutable ordered set of explicit Grid tracks.</summary>
public sealed class GridTracks : IReadOnlyList<GridTrack>, IEquatable<GridTracks>
{
    private readonly GridTrack[] _items;

    private GridTracks(GridTrack[] items) => _items = items;

    /// <summary>Gets the empty track set.</summary>
    public static GridTracks Empty { get; } = new([]);

    /// <summary>Copies and validates an ordered track set.</summary>
    public static GridTracks Create(params GridTrack[] tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        var copy = tracks.ToArray();
        foreach (var track in copy)
            track.Validate();
        return copy.Length == 0 ? Empty : new(copy);
    }

    /// <summary>Gets the number of tracks.</summary>
    public int Count => _items.Length;

    /// <summary>Gets a track by zero-based index.</summary>
    public GridTrack this[int index] => _items[index];

    /// <summary>Enumerates tracks in authored order.</summary>
    public IEnumerator<GridTrack> GetEnumerator() =>
        ((IEnumerable<GridTrack>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    /// <summary>Compares track values in authored order.</summary>
    public bool Equals(GridTracks? other) =>
        other is not null && _items.SequenceEqual(other._items);

    /// <summary>Compares track values in authored order.</summary>
    public override bool Equals(object? obj) => obj is GridTracks other && Equals(other);

    /// <summary>Returns an ordered value hash.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items)
            hash.Add(item);
        return hash.ToHashCode();
    }
}

/// <summary>Zero-based explicit Grid placement with positive contiguous spans.</summary>
public readonly record struct GridPlacement
{
    /// <summary>Creates a validated explicit placement.</summary>
    public GridPlacement(int row, int column, int rowSpan = 1, int columnSpan = 1)
    {
        if (row < 0 || column < 0 || rowSpan <= 0 || columnSpan <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(row),
                "Grid indices are zero based and spans must be positive."
            );
        Row = row;
        Column = column;
        RowSpan = rowSpan;
        ColumnSpan = columnSpan;
    }

    /// <summary>Gets the zero-based starting row.</summary>
    public int Row { get; }

    /// <summary>Gets the zero-based starting column.</summary>
    public int Column { get; }

    /// <summary>Gets the positive contiguous row span.</summary>
    public int RowSpan { get; }

    /// <summary>Gets the positive contiguous column span.</summary>
    public int ColumnSpan { get; }
}

/// <summary>An explicit finite nonnegative layout limit, or the default unbounded limit.</summary>
public readonly record struct LayoutConstraint
{
    /// <summary>Creates a finite nonnegative limit.</summary>
    public LayoutConstraint(float limit)
    {
        if (!float.IsFinite(limit) || limit < 0)
            throw new ArgumentOutOfRangeException(nameof(limit));
        Limit = limit == 0 ? 0 : limit;
    }

    /// <summary>Gets the finite limit, or null when unbounded.</summary>
    public float? Limit { get; }

    /// <summary>Gets whether this value has a finite limit.</summary>
    public bool IsBounded => Limit.HasValue;

    /// <summary>Gets the explicit unbounded value.</summary>
    public static LayoutConstraint Unbounded => default;

    internal float Or(float fallback) => Limit ?? fallback;
}

/// <summary>Selects bounded paragraph line breaking.</summary>
public enum TextWrap
{
    /// <summary>Keeps text on one line.</summary>
    NoWrap,

    /// <summary>Wraps at words and uses grapheme boundaries for an overlong word.</summary>
    WordWithGraphemeFallback,

    /// <summary>Breaks only at authored line separators.</summary>
    ExplicitBreaks,
}

/// <summary>Selects constrained paragraph overflow behavior.</summary>
public enum TextOverflow
{
    /// <summary>Clips overflowing content.</summary>
    Clip,

    /// <summary>Marks the final visible line for an ellipsis.</summary>
    Ellipsis,
}
