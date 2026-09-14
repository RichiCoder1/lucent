namespace Lucent.ComponentBrowser;

public sealed class TableExampleModel
{
    private readonly Signal<TableSort?> _sort;
    private readonly Signal<string> _selection;
    private readonly Signal<bool> _largeFixture;
    private readonly Signal<string> _message;
    private readonly IReadOnlyList<Row> _sampleRows =
    [
        new("row-00001", "Alpha workspace", "Ready", 2),
        new("row-00002", "Beta workspace", "Review", 7),
        new("row-00003", "Gamma workspace", "Ready", 0),
        new("row-00004", "Delta workspace", "Paused", 14),
        new("row-00005", "Epsilon workspace", "Ready", 4),
        new("row-00006", "Zeta workspace", "Review", 9),
        new("row-00007", "Eta workspace", "Ready", 1),
        new("row-00008", "Theta workspace", "Paused", 18),
        new("row-00009", "Iota workspace", "Ready", 3),
        new("row-00010", "Kappa workspace", "Review", 6),
        new("row-00011", "Lambda workspace", "Ready", 5),
        new("row-00012", "Mu workspace", "Paused", 11),
    ];
    private IReadOnlyList<Row>? _largeRows;
    private IReadOnlyList<Row>? _rowsCache;
    private bool _rowsCacheLarge;
    private TableSort? _rowsCacheSort;

    public TableExampleModel(ReactiveScope owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _sort = owner.Signal<TableSort?>(
            new TableSort("name", TableSortDirection.Ascending),
            "component-browser.table-sort"
        );
        _selection = owner.Signal("row-00006", "component-browser.table-selection");
        _largeFixture = owner.Signal(false, "component-browser.table-large-fixture");
        _message = owner.Signal(
            "Sort a column or move selection to inspect the controlled table contract.",
            "component-browser.table-message"
        );
    }

    public IReadOnlyList<TableColumn<Row>> Columns { get; } =
    [
        new("name", "Workspace", static row => row.Name, width: 220),
        new("status", "Status", static row => row.Status, width: 130),
        new(
            "issues",
            "Open issues",
            static row =>
                row.OpenIssues.ToString(System.Globalization.CultureInfo.InvariantCulture),
            width: 120
        ),
    ];

    public TableViewOptions ComfortableOptions { get; } = new(rowHeight: 36, headerHeight: 40);

    public TableViewOptions CompactOptions { get; } = new(rowHeight: 28, headerHeight: 32);

    public IReadOnlyList<Row> Rows
    {
        get
        {
            var large = _largeFixture.Value;
            var sort = _sort.Value;
            if (_rowsCache is not null && large == _rowsCacheLarge && Equals(sort, _rowsCacheSort))
                return _rowsCache;

            IEnumerable<Row> rows = large ? LargeRows : _sampleRows;
            rows = (sort?.ColumnKey, sort?.Direction) switch
            {
                ("name", TableSortDirection.Ascending) => rows.OrderBy(
                    row => row.Name,
                    StringComparer.Ordinal
                ),
                ("name", TableSortDirection.Descending) => rows.OrderByDescending(
                    row => row.Name,
                    StringComparer.Ordinal
                ),
                ("status", TableSortDirection.Ascending) => rows.OrderBy(
                        row => row.Status,
                        StringComparer.Ordinal
                    )
                    .ThenBy(row => row.Id, StringComparer.Ordinal),
                ("status", TableSortDirection.Descending) => rows.OrderByDescending(
                        row => row.Status,
                        StringComparer.Ordinal
                    )
                    .ThenBy(row => row.Id, StringComparer.Ordinal),
                ("issues", TableSortDirection.Ascending) => rows.OrderBy(row => row.OpenIssues)
                    .ThenBy(row => row.Id, StringComparer.Ordinal),
                ("issues", TableSortDirection.Descending) => rows.OrderByDescending(row =>
                        row.OpenIssues
                    )
                    .ThenBy(row => row.Id, StringComparer.Ordinal),
                _ => rows,
            };
            _rowsCache = rows.ToArray();
            _rowsCacheLarge = large;
            _rowsCacheSort = sort;
            return _rowsCache;
        }
    }

    public SelectedKey<string> Selection => SelectedKey.Some(_selection.Value);

    public TableSort? Sort => _sort.Value;

    public bool LargeFixture => _largeFixture.Value;

    public string FixtureLabel => LargeFixture ? "10,000 generated rows" : "12 sample rows";

    public string Message => _message.Value;

    public string SelectionLabel =>
        Rows.FirstOrDefault(row => row.Id == _selection.Value) is { } selected
            ? $"Selected {selected.Name} ({selected.OpenIssues} open issues)."
            : "No row is selected.";

    public void SetSelection(string key)
    {
        if (!Rows.Any(row => row.Id == key))
            return;

        _selection.Value = key;
        _message.Value = SelectionLabel;
    }

    public void SetSort(TableSort sort)
    {
        ArgumentNullException.ThrowIfNull(sort);
        if (!Columns.Any(column => column.Key == sort.ColumnKey))
            return;

        _sort.Value = sort;
        _message.Value = $"Sorted by {sort.ColumnKey} ({sort.Direction}).";
    }

    public void SetFixture(bool enabled)
    {
        _largeFixture.Value = enabled;
        _message.Value = FixtureLabel + " are available without changing the selected key.";
    }

    private IReadOnlyList<Row> LargeRows =>
        _largeRows ??= Enumerable
            .Range(1, 10_000)
            .Select(index => new Row(
                $"row-{index:00000}",
                $"Workspace {index:00000}",
                (index % 3) switch
                {
                    0 => "Ready",
                    1 => "Review",
                    _ => "Paused",
                },
                (index * 7) % 23
            ))
            .ToArray();

    public sealed record Row(string Id, string Name, string Status, int OpenIssues);
}
