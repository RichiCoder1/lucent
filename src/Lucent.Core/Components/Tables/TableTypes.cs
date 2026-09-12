namespace Lucent.Core;

/// <summary>The caller-owned order requested by a table header.</summary>
public enum TableSortDirection
{
    /// <summary>Requests ascending order.</summary>
    Ascending,

    /// <summary>Requests descending order.</summary>
    Descending,
}

/// <summary>A sort request. The table never reorders or mutates application data.</summary>
public sealed record TableSort
{
    /// <summary>Creates a request for a stable column key.</summary>
    public TableSort(string columnKey, TableSortDirection direction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnKey);
        if (!Enum.IsDefined(direction))
            throw new ArgumentOutOfRangeException(nameof(direction));
        ColumnKey = columnKey;
        Direction = direction;
    }

    /// <summary>The column's stable key.</summary>
    public string ColumnKey { get; }

    /// <summary>The requested order.</summary>
    public TableSortDirection Direction { get; }
}

/// <summary>A read-only text column with a stable key and bounded user-resizable width.</summary>
public sealed class TableColumn<TItem>
{
    /// <summary>Creates a column; text is read only for realized rows.</summary>
    public TableColumn(
        string key,
        string header,
        Func<TItem, string> text,
        float width = 160,
        float minimumWidth = 64,
        float maximumWidth = 800,
        bool sortable = true
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(header);
        ArgumentNullException.ThrowIfNull(text);
        if (!float.IsFinite(minimumWidth) || minimumWidth < 32)
            throw new ArgumentOutOfRangeException(nameof(minimumWidth));
        if (!float.IsFinite(maximumWidth) || maximumWidth < minimumWidth)
            throw new ArgumentOutOfRangeException(nameof(maximumWidth));
        if (!float.IsFinite(width) || width < minimumWidth || width > maximumWidth)
            throw new ArgumentOutOfRangeException(nameof(width));
        Key = key;
        Header = header;
        Text = text;
        Width = width;
        MinimumWidth = minimumWidth;
        MaximumWidth = maximumWidth;
        Sortable = sortable;
    }

    /// <summary>The stable column key used in sort requests.</summary>
    public string Key { get; }

    /// <summary>The visible and accessible column heading.</summary>
    public string Header { get; }

    /// <summary>Reads a realized cell's visible and accessible text.</summary>
    public Func<TItem, string> Text { get; }

    /// <summary>The initial logical width, including cell padding and resize handle.</summary>
    public float Width { get; }

    /// <summary>The smallest user-selected width.</summary>
    public float MinimumWidth { get; }

    /// <summary>The largest user-selected width.</summary>
    public float MaximumWidth { get; }

    /// <summary>Whether a supplied sort callback may be invoked for this column.</summary>
    public bool Sortable { get; }
}

/// <summary>Fixed-row geometry and keyboard selection policy for a read-only table.</summary>
public sealed class TableViewOptions
{
    /// <summary>Creates bounded geometry; omitted heights use comfortable stock metrics.</summary>
    public TableViewOptions(
        float? rowHeight = null,
        float? headerHeight = null,
        ListBoxSelectionMode selectionMode = ListBoxSelectionMode.FollowsFocus
    )
    {
        if (rowHeight is { } row && (!float.IsFinite(row) || row < 24))
            throw new ArgumentOutOfRangeException(nameof(rowHeight));
        if (headerHeight is { } header && (!float.IsFinite(header) || header < 24))
            throw new ArgumentOutOfRangeException(nameof(headerHeight));
        if (!Enum.IsDefined(selectionMode))
            throw new ArgumentOutOfRangeException(nameof(selectionMode));
        RowHeight = rowHeight;
        HeaderHeight = headerHeight;
        SelectionMode = selectionMode;
    }

    /// <summary>The fixed row height, or comfortable stock metrics. Pass compact metrics for a compact table.</summary>
    public float? RowHeight { get; }

    /// <summary>The fixed header height, or comfortable stock metrics.</summary>
    public float? HeaderHeight { get; }

    /// <summary>When keyboard navigation requests the caller-owned selection.</summary>
    public ListBoxSelectionMode SelectionMode { get; }
}
