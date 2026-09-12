namespace Lucent.Core;

internal sealed class TableBinding(string label, ResponsiveConstraints constraints)
{
    internal string Label { get; } = label;
    internal ResponsiveConstraints Constraints { get; } = constraints;
    internal required Func<int> Count { get; init; }
    internal required Func<int?> SelectedIndex { get; init; }
    internal required int ColumnCount { get; init; }
    internal required Func<float> Width { get; init; }
    internal required Func<float> HeaderHeight { get; init; }
    internal required Func<IReadOnlyList<ElementIdentity>> Headers { get; init; }
    internal Action<int>? Reveal { get; set; }
    internal ElementIdentity? Identity { get; set; }

    internal float Height() => Math.Max(0, Constraints.Current.Height);

    internal float BodyHeight() => Math.Max(0, Height() - HeaderHeight());
}

internal sealed class TableColumnBinding
{
    internal required string Key { get; init; }
    internal required string Header { get; init; }
    internal required Func<float> Width { get; init; }
    internal required Action<float> SetWidth { get; init; }
    internal required Func<float> HeaderHeight { get; init; }
    internal required float Minimum { get; init; }
    internal required float Maximum { get; init; }
    internal required Func<TableSort?> ReadSort { get; init; }
    internal Action<TableSort>? RequestSort { get; init; }
    internal required Action<ElementIdentity> RegisterHeader { get; init; }
    internal bool CanSort => RequestSort is not null;

    internal string? SortLabel() =>
        ReadSort() is { } sort && sort.ColumnKey == Key
            ? sort.Direction == TableSortDirection.Ascending
                ? "Ascending"
                : "Descending"
            : null;

    internal string Caption() =>
        Header
        + (
            SortLabel() switch
            {
                "Ascending" => " ↑",
                "Descending" => " ↓",
                _ => "",
            }
        );

    internal void Sort() =>
        RequestSort?.Invoke(
            new(
                Key,
                ReadSort() is { Direction: TableSortDirection.Ascending } sort
                && sort.ColumnKey == Key
                    ? TableSortDirection.Descending
                    : TableSortDirection.Ascending
            )
        );

    internal void Resize(double value) => SetWidth((float)Math.Clamp(value, Minimum, Maximum));
}

internal sealed class TableCellBinding
{
    internal required Func<string> Text { get; init; }
    internal required Func<int?> Row { get; init; }
    internal required int Column { get; init; }
    internal required Func<float> Width { get; init; }
    internal required Func<ElementIdentity?> Table { get; init; }
}
