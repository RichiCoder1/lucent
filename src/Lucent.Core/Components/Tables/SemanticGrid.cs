namespace Lucent.Core;

/// <summary>Immutable logical dimensions and retained column headers for a read-only data grid.</summary>
/// <remarks>Logical row counts include virtualized rows. This metadata does not allocate offscreen element snapshots.</remarks>
public sealed class SemanticGridSnapshot
{
    /// <summary>Copies the current logical dimensions and available header identities.</summary>
    public SemanticGridSnapshot(
        int rowCount,
        int columnCount,
        IEnumerable<ElementIdentity>? columnHeaders = null
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        ArgumentOutOfRangeException.ThrowIfNegative(columnCount);
        var headers = columnHeaders?.ToArray() ?? [];
        if (
            headers.Length > columnCount
            || headers.Any(identity => identity.CompositionEpoch <= 0 || identity.ElementId <= 0)
            || headers.Distinct().Count() != headers.Length
        )
            throw new ArgumentException(
                "Grid headers require distinct retained identities within the column count.",
                nameof(columnHeaders)
            );
        RowCount = rowCount;
        ColumnCount = columnCount;
        ColumnHeaders = Array.AsReadOnly(headers);
    }

    /// <summary>The number of logical rows, including rows outside the viewport.</summary>
    public int RowCount { get; }

    /// <summary>The number of logical columns.</summary>
    public int ColumnCount { get; }

    /// <summary>Current retained headers in column order.</summary>
    public IReadOnlyList<ElementIdentity> ColumnHeaders { get; }
}

/// <summary>A realized read-only cell's zero-based logical position and containing grid.</summary>
public sealed record SemanticGridItemSnapshot
{
    /// <summary>Creates a unit-span cell. Merged cells are outside the first table contract.</summary>
    public SemanticGridItemSnapshot(ElementIdentity grid, int row, int column)
    {
        if (grid.CompositionEpoch <= 0 || grid.ElementId <= 0)
            throw new ArgumentException("A retained grid identity is required.", nameof(grid));
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        Grid = grid;
        Row = row;
        Column = column;
    }

    /// <summary>The containing data grid's retained identity.</summary>
    public ElementIdentity Grid { get; }

    /// <summary>The zero-based logical row.</summary>
    public int Row { get; }

    /// <summary>The zero-based logical column.</summary>
    public int Column { get; }
}
