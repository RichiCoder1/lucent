namespace Lucent.ComponentBrowser;

public sealed class ComponentBrowserState
{
    private readonly ReactiveScope _scope;
    private readonly Signal<string> _search;
    private readonly Signal<string> _selectedId;
    private readonly Signal<BrowserDensity> _density;
    private readonly Signal<ExampleState> _exampleState;
    private readonly Signal<int> _activationCount;
    private readonly Signal<int> _selectedOption;
    private readonly Signal<CheckState> _checkState;
    private readonly Signal<bool> _switchEnabled;
    private readonly Signal<string> _radioSelection;
    private readonly Signal<float> _progress;
    private readonly Signal<bool> _popoverOpen;
    private readonly Signal<string> _copyMessage;
    private readonly Signal<string> _menuMessage;
    private readonly Signal<decimal> _numberValue;
    private readonly Signal<double> _sliderValue;
    private readonly Signal<string> _listSelection;
    private readonly Signal<string> _selectSelection;
    private readonly Signal<string> _tabSelection;
    private readonly Signal<bool> _disclosureExpanded;
    private readonly Signal<string> _linkMessage;
    private readonly Signal<string> _dialogMessage;
    private readonly Signal<string> _storageMessage;
    private readonly Signal<string> _storageLocation;
    private readonly Signal<string> _passwordValue;
    private readonly Signal<string> _passwordMessage;
    private readonly Signal<ComboBoxSelectedItem<string>?> _comboSelection;
    private readonly Signal<string> _comboMessage;
    private readonly Signal<TableSort?> _tableSort;
    private readonly Signal<string> _tableSelection;
    private readonly Signal<bool> _tableLargeFixture;
    private readonly Signal<string> _tableMessage;
    private readonly Signal<DateOnly?> _dateValue;
    private readonly Signal<TimeOnly?> _timeValue;
    private readonly Signal<string> _treeSelection;
    private readonly Signal<IReadOnlySet<string>> _treeExpanded;
    private readonly IReadOnlyList<TableRow> _sampleTableRows =
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
    private readonly IReadOnlyList<TreeNode> _treeRoots =
    [
        new(
            "src",
            "src",
            [
                new(
                    "core",
                    "Lucent.Core",
                    [
                        new(
                            "components",
                            "Components",
                            [
                                new("fields", "Fields"),
                                new("lists", "Lists"),
                                new("surfaces", "Surfaces"),
                            ]
                        ),
                        new("runtime", "Runtime"),
                    ]
                ),
                new(
                    "browser",
                    "Component Browser",
                    [new("examples", "Examples"), new("browser-tests", "Browser tests")]
                ),
            ]
        ),
        new(
            "apps",
            "apps",
            [
                new(
                    "issue-browser",
                    "Issue Browser",
                    [new("issue-filters", "FilterBar.lui"), new("issue-details", "Details.lui")]
                ),
            ]
        ),
        new(
            "docs",
            "docs",
            [new("component-design", "Component design"), new("testing", "Testing")]
        ),
    ];
    private IReadOnlyList<TableRow>? _largeTableRows;
    private IReadOnlyList<TableRow>? _tableRowsCache;
    private bool _tableRowsCacheLarge;
    private TableSort? _tableRowsCacheSort;
    private readonly IReadOnlyList<OptionItem> _options =
    [
        new("native", "Native rendering"),
        new("portable", "Portable semantics"),
        new("a11y", "Accessible by default"),
    ];
    private IReadOnlyList<ComponentCatalogItem>? _visibleSource;
    private string? _visibleSearch;
    private IReadOnlyList<ComponentCatalogItem> _visibleCache = [];

    public ComponentBrowserState(
        ReactiveScope scope,
        IFilePicker? filePicker = null,
        IUriLauncher? uriLauncher = null
    )
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
        _search = scope.Signal("", "component-browser.search");
        _selectedId = scope.Signal(ComponentCatalog.Items[0].Id, "component-browser.selection");
        _density = scope.Signal(BrowserDensity.Comfortable, "component-browser.density");
        _exampleState = scope.Signal(ExampleState.Default, "component-browser.example-state");
        _activationCount = scope.Signal(0, "component-browser.activation-count");
        _selectedOption = scope.Signal(0, "component-browser.option");
        _checkState = scope.Signal(CheckState.Off, "component-browser.check-state");
        _switchEnabled = scope.Signal(true, "component-browser.switch-enabled");
        _radioSelection = scope.Signal("native", "component-browser.radio-selection");
        _progress = scope.Signal(0.38f, "component-browser.progress");
        _popoverOpen = scope.Signal(false, "component-browser.popover-open");
        _copyMessage = scope.Signal(
            "Source is ready to inspect.",
            "component-browser.copy-message"
        );
        _menuMessage = scope.Signal(
            "No menu command invoked yet.",
            "component-browser.menu-message"
        );
        _numberValue = scope.Signal(12.5m, "component-browser.number-value");
        _sliderValue = scope.Signal(64d, "component-browser.slider-value");
        _listSelection = scope.Signal("overview", "component-browser.list-selection");
        _selectSelection = scope.Signal("keyboard", "component-browser.select-selection");
        _tabSelection = scope.Signal("overview", "component-browser.tab-selection");
        _disclosureExpanded = scope.Signal(false, "component-browser.disclosure-expanded");
        _linkMessage = scope.Signal(
            "No reference link invoked yet.",
            "component-browser.link-message"
        );
        _dialogMessage = scope.Signal("The dialog is closed.", "component-browser.dialog-message");
        _storageMessage = scope.Signal(
            "No native picker request yet.",
            "component-browser.storage-message"
        );
        _storageLocation = scope.Signal(
            "A selected name and location will appear here.",
            "component-browser.storage-location"
        );
        _passwordValue = scope.Signal("sample-only-value", "component-browser.password-value");
        _passwordMessage = scope.Signal(
            "The fake value is masked and never echoed.",
            "component-browser.password-message"
        );
        _comboSelection = scope.Signal<ComboBoxSelectedItem<string>?>(
            null,
            "component-browser.combo-selection"
        );
        _comboMessage = scope.Signal(
            "Choose a result to keep the applied key controlled.",
            "component-browser.combo-message"
        );
        _tableSort = scope.Signal<TableSort?>(
            new TableSort("name", TableSortDirection.Ascending),
            "component-browser.table-sort"
        );
        _tableSelection = scope.Signal("row-00006", "component-browser.table-selection");
        _tableLargeFixture = scope.Signal(false, "component-browser.table-large-fixture");
        _tableMessage = scope.Signal(
            "Sort a column or move selection to inspect the controlled table contract.",
            "component-browser.table-message"
        );
        _dateValue = scope.Signal<DateOnly?>(
            new DateOnly(2024, 6, 15),
            "component-browser.date-value"
        );
        _timeValue = scope.Signal<TimeOnly?>(new TimeOnly(9, 30), "component-browser.time-value");
        _treeSelection = scope.Signal("src", "component-browser.tree-selection");
        _treeExpanded = scope.Signal<IReadOnlySet<string>>(
            new HashSet<string>(["src", "apps"], StringComparer.Ordinal),
            "component-browser.tree-expanded"
        );
        TreeDataSource = new(
            node => node.Id,
            node => node.Label,
            node => node.Children,
            node => node.Children is { Count: > 0 },
            node => node.Enabled
        );
        SearchEditor = new(scope, "component-search", "");
        ExampleField = new(scope, "example-field", "Lucent");
        ExampleNotes = new(
            scope,
            "example-notes",
            "Keep the session in application state when a draft must survive a remount.",
            multiline: true
        );
        Form = scope.Own(new FormSession(scope, "component-browser-form"));
        Dialog = scope.Own(new DialogController<string>(scope, "component-browser-dialog"));
        FilePicker = filePicker ?? new UnsupportedFilePicker();
        UriLauncher = uriLauncher;
    }

    public EditorSession SearchEditor { get; }
    public EditorSession ExampleField { get; }
    public EditorSession ExampleNotes { get; }
    public FormSession Form { get; }
    public DialogController<string> Dialog { get; }
    public IFilePicker FilePicker { get; }
    public IUriLauncher? UriLauncher { get; }
    public NumericEditOptions NumberOptions { get; } =
        new(increment: 0.5m, minimum: 0m, maximum: 100m, pageIncrement: 10m, format: "0.0");
    public SliderOptions SliderOptions { get; } =
        new(minimum: 0, maximum: 100, increment: 5, pageIncrement: 20, wheelEnabled: true);
    public PasswordFieldOptions PasswordOptions { get; } = new(historyLimit: 4);
    public ComboBoxOptions ComboOptions { get; } = new(debounce: TimeSpan.Zero);
    public TableViewOptions ComfortableTableOptions { get; } = new(rowHeight: 36, headerHeight: 40);
    public TableViewOptions CompactTableOptions { get; } = new(rowHeight: 28, headerHeight: 32);
    public DatePickerOptions DatePickerOptions { get; } =
        new(
            System.Globalization.CultureInfo.GetCultureInfo("en-US"),
            minimum: new DateOnly(2024, 1, 1),
            maximum: new DateOnly(2025, 12, 31),
            today: static () => new DateOnly(2024, 6, 15)
        );
    public TimePickerOptions TimePickerOptions { get; } =
        new(
            System.Globalization.CultureInfo.GetCultureInfo("en-US"),
            minimum: new TimeOnly(8, 0),
            maximum: new TimeOnly(18, 0),
            step: TimeSpan.FromMinutes(30)
        );
    public DateTimeFieldOptions DateFieldOptions { get; } =
        new(
            help: static () => "The example accepts dates from January 2024 through December 2025.",
            required: true,
            fieldId: "component-browser-date"
        );
    public DateTimeFieldOptions TimeFieldOptions { get; } =
        new(
            help: static () => "Use the arrow keys to step by 30 minutes inside the bounded range.",
            required: true,
            fieldId: "component-browser-time"
        );
    public TreeViewOptions TreeOptions { get; } =
        new(selectionMode: ListBoxSelectionMode.FollowsFocus, rowHeight: 34);
    public TreeDataSource<string, TreeNode> TreeDataSource { get; }
    public TableViewOptions TableOptions =>
        Density == BrowserDensity.Compact ? CompactTableOptions : ComfortableTableOptions;
    public IReadOnlyList<TableColumn<TableRow>> TableColumns { get; } =
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
    public IReadOnlyList<ChoiceItem<string>> ListItems { get; } =
    [
        new("overview", "Overview"),
        new("keyboard", "Keyboard behavior"),
        new("accessibility", "Accessibility contract"),
        new("disabled", "Disabled option", enabled: false),
    ];
    public IReadOnlyList<ChoiceItem<string>> SelectItems { get; } =
    [
        new("keyboard", "Keyboard behavior"),
        new("pointer", "Pointer behavior"),
        new("semantics", "Semantic output"),
    ];
    public IReadOnlyList<TabItem<string>> TabItems { get; } =
    [
        new(
            "overview",
            "Overview",
            () =>
                Lucent.Core.Components.Text(
                    "Tabs keep one keyed selection in application state and retain visited panels."
                )
        ),
        new(
            "keyboard",
            "Keyboard",
            () =>
                Lucent.Core.Components.Text(
                    "Manual and automatic activation are explicit policy choices."
                )
        ),
        new(
            "semantics",
            "Semantics",
            () =>
                Lucent.Core.Components.Text(
                    "The selected tab and its panel expose their relationship to accessibility clients."
                )
        ),
    ];
    public IReadOnlyList<OptionItem> Options => _options;
    public IReadOnlyList<RadioOption<string>> RadioOptions { get; } =
    [
        new("native", "Native rendering"),
        new("portable", "Portable semantics"),
        new("a11y", "Accessible by default"),
    ];

    public IReadOnlyList<ComponentCatalogItem> VisibleItems
    {
        get
        {
            var search = Search;
            if (
                !ReferenceEquals(_visibleSource, ComponentCatalog.Items)
                || search != _visibleSearch
            )
            {
                _visibleSource = ComponentCatalog.Items;
                _visibleSearch = search;
                _visibleCache = string.IsNullOrWhiteSpace(search)
                    ? ComponentCatalog.Items
                    : ComponentCatalog
                        .Items.Where(item =>
                            (item.Title + " " + item.Family + " " + item.Summary).Contains(
                                search,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .ToArray();
            }

            return _visibleCache;
        }
    }

    public string Search
    {
        get => _search.Value;
        set => _search.Value = value.Trim();
    }

    public string SelectedId => _selectedId.Value;
    public ComponentCatalogItem SelectedItem => ComponentCatalog.Find(SelectedId);
    public string SelectedSource => ComponentCatalog.ReadSource(SelectedItem.SourceFile);
    public BrowserDensity Density => _density.Value;
    public ExampleState CurrentExampleState => _exampleState.Value;
    public string ExampleStateLabel =>
        CurrentExampleState switch
        {
            ExampleState.Default => "Default interaction",
            ExampleState.Disabled => "Disabled controls",
            ExampleState.Busy => "Busy / loading",
            ExampleState.Error => "Recoverable error",
            _ => "Unknown state",
        };
    public string CopyMessage => _copyMessage.Value;
    public string MenuMessage => _menuMessage.Value;
    public decimal NumberValue => _numberValue.Value;
    public double SliderValue => _sliderValue.Value;
    public string ListSelection => _listSelection.Value;
    public string SelectSelection => _selectSelection.Value;
    public string TabSelection => _tabSelection.Value;
    public bool DisclosureExpanded => _disclosureExpanded.Value;
    public string LinkMessage => _linkMessage.Value;
    public string DialogMessage => _dialogMessage.Value;
    public string StorageMessage => _storageMessage.Value;
    public string StorageLocation => _storageLocation.Value;
    public string PasswordValue => _passwordValue.Value;
    public string PasswordMessage => _passwordMessage.Value;
    public ValidationState PasswordValidation =>
        _passwordValue.Value.Length >= 8
            ? ValidationState.Valid
            : ValidationState.Invalid("Use at least eight characters for this example.");
    public ComboBoxSelectedItem<string>? ComboSelection => _comboSelection.Value;
    public string ComboMessage => _comboMessage.Value;
    public IReadOnlyList<TableRow> TableRows
    {
        get
        {
            var large = _tableLargeFixture.Value;
            var sort = _tableSort.Value;
            if (
                _tableRowsCache is not null
                && large == _tableRowsCacheLarge
                && Equals(sort, _tableRowsCacheSort)
            )
                return _tableRowsCache;

            IEnumerable<TableRow> rows = large ? LargeTableRows : _sampleTableRows;
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
            var cache = rows.ToArray();
            _tableRowsCache = cache;
            _tableRowsCacheLarge = large;
            _tableRowsCacheSort = sort;
            return cache;
        }
    }
    public SelectedKey<string> TableSelection => SelectedKey.Some(_tableSelection.Value);
    public TableSort? TableSort => _tableSort.Value;
    public bool TableLargeFixture => _tableLargeFixture.Value;
    public string TableFixtureLabel =>
        TableLargeFixture ? "10,000 generated rows" : "12 sample rows";
    public string TableMessage => _tableMessage.Value;
    public string TableSelectionLabel =>
        TableRows.FirstOrDefault(row => row.Id == _tableSelection.Value) is { } selected
            ? $"Selected {selected.Name} ({selected.OpenIssues} open issues)."
            : "No row is selected.";

    private IReadOnlyList<TableRow> LargeTableRows =>
        _largeTableRows ??= Enumerable
            .Range(1, 10_000)
            .Select(index => new TableRow(
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
    public DateOnly? DateValue => _dateValue.Value;
    public TimeOnly? TimeValue => _timeValue.Value;
    public string DateTimeSummary =>
        $"Due {DateValue?.ToString("D", DatePickerOptions.Culture) ?? "not set"} at {TimeValue?.ToString("t", TimePickerOptions.Culture) ?? "not set"}.";
    public IReadOnlyList<TreeNode> TreeRoots => _treeRoots;
    public SelectedKey<string> TreeSelection => SelectedKey.Some(_treeSelection.Value);
    public string TreeSelectionLabel =>
        FindTreeNode(_treeRoots, _treeSelection.Value) is { } selected
            ? $"Selected {selected.Label}."
            : "No tree item is selected.";
    public string TreeExpansionSummary =>
        $"{_treeExpanded.Value.Count:N0} branch{(_treeExpanded.Value.Count == 1 ? "" : "es")} expanded.";
    public IReadOnlyList<ChoiceItem<string>> ComboItems { get; } =
    [
        new("alpha", "Alpha workspace"),
        new("beta", "Beta workspace"),
        new("gamma", "Gamma workspace"),
    ];
    public int ActivationCount => _activationCount.Value;
    public float ProgressValue => _progress.Value;
    public double? ProgressFraction => _progress.Value;
    public bool PopoverOpen => _popoverOpen.Value;
    public CheckState CheckState => _checkState.Value;
    public bool SwitchEnabled => _switchEnabled.Value;
    public string RadioSelection => _radioSelection.Value;
    public ValidationState NameValidation =>
        CurrentExampleState == ExampleState.Error || string.IsNullOrWhiteSpace(ExampleField.Text)
            ? ValidationState.Invalid("A workspace name is required.")
            : ValidationState.Valid;
    public string FieldSummary =>
        string.IsNullOrWhiteSpace(ExampleField.Text)
            ? "The field is empty."
            : $"Draft value: {ExampleField.Text}";
    public string NotesSummary =>
        ExampleNotes.Text.Length == 0
            ? "The multiline session is empty."
            : $"{ExampleNotes.Text.Length} characters retained in the session.";

    public bool IsSelected(string id) => _selectedId.Value == id;

    public bool IsOptionSelected(string id) => _options[_selectedOption.Value].Id == id;

    public void SetSearch(string value) => Search = value;

    public void Select(string id)
    {
        if (ComponentCatalog.Items.Any(item => item.Id == id))
        {
            _selectedId.Value = id;
            _popoverOpen.Value = false;
            _copyMessage.Value = "Source is ready to inspect.";
        }
    }

    public void ToggleDensity() =>
        _density.Value =
            Density == BrowserDensity.Comfortable
                ? BrowserDensity.Compact
                : BrowserDensity.Comfortable;

    public void SetExampleState(ExampleState state) => _exampleState.Value = state;

    public void ActivateExample() => _activationCount.Value++;

    public void ToggleOption() =>
        _selectedOption.Value = (_selectedOption.Value + 1) % _options.Count;

    public void SelectOption(string id)
    {
        var index = _options
            .Select((item, index) => (item, index))
            .FirstOrDefault(pair => pair.item.Id == id);
        if (index.item is not null)
            _selectedOption.Value = index.index;
    }

    public void SetCheckState(CheckState state) => _checkState.Value = state;

    public void SetSwitchEnabled(bool enabled) => _switchEnabled.Value = enabled;

    public void SetRadioSelection(string key)
    {
        if (RadioOptions.Any(option => option.Key == key))
            _radioSelection.Value = key;
    }

    public void AdvanceProgress()
    {
        var next = _progress.Value + 0.16f;
        _progress.Value = next > 1f ? 0.08f : next;
    }

    public void SetPopoverOpen(bool open) => _popoverOpen.Value = open;

    public void RetryExample()
    {
        _exampleState.Value = ExampleState.Default;
        _copyMessage.Value = "The example returned to its default state.";
    }

    public void CopySource()
    {
        ExampleClipboard.TryCopy(SelectedSource, out var message);
        _copyMessage.Value = message;
    }

    public void SetMenuMessage(string message) => _menuMessage.Value = message;

    public void SetNumberValue(decimal? value) => _numberValue.Value = value ?? 0m;

    public void SetSliderValue(double value) => _sliderValue.Value = value;

    public void CommitSlider(double value) => _sliderValue.Value = value;

    public void SetPasswordValue(string value)
    {
        _passwordValue.Value = value;
        _passwordMessage.Value = "The fake value changed; the browser keeps it masked.";
    }

    public void SetComboSelection(string key)
    {
        var item = ComboItems.FirstOrDefault(candidate => candidate.Key == key);
        if (item is null || !item.Enabled)
            return;
        _comboSelection.Value = new(item.Key, item.Label);
        _comboMessage.Value = "Applied " + item.Label + ".";
    }

    public void SetTableSelection(string key)
    {
        if (TableRows.Any(row => row.Id == key))
        {
            _tableSelection.Value = key;
            _tableMessage.Value = TableSelectionLabel;
        }
    }

    public void SetTableSort(TableSort sort)
    {
        ArgumentNullException.ThrowIfNull(sort);
        if (TableColumns.Any(column => column.Key == sort.ColumnKey))
        {
            _tableSort.Value = sort;
            _tableMessage.Value = $"Sorted by {sort.ColumnKey} ({sort.Direction}).";
        }
    }

    public void ToggleTableFixture()
    {
        SetTableFixture(!_tableLargeFixture.Value);
    }

    public void SetTableFixture(bool enabled)
    {
        _tableLargeFixture.Value = enabled;
        _tableMessage.Value =
            TableFixtureLabel + " are available without changing the selected key.";
    }

    public void SetDateValue(DateOnly? value)
    {
        if (value is not { } date)
        {
            _dateValue.Value = null;
            return;
        }

        if (DatePickerOptions.Minimum is { } minimum && date < minimum)
            return;
        if (DatePickerOptions.Maximum is { } maximum && date > maximum)
            return;
        _dateValue.Value = date;
    }

    public void SetTimeValue(TimeOnly? value)
    {
        if (value is not { } time)
        {
            _timeValue.Value = null;
            return;
        }

        if (TimePickerOptions.Minimum is { } minimum && time < minimum)
            return;
        if (TimePickerOptions.Maximum is { } maximum && time > maximum)
            return;
        _timeValue.Value = time;
    }

    public bool IsTreeExpanded(string key) => _treeExpanded.Value.Contains(key);

    public void SetTreeExpanded(string key, bool expanded)
    {
        var node = FindTreeNode(_treeRoots, key);
        if (node is null || node.Children is not { Count: > 0 })
            return;

        var next = new HashSet<string>(_treeExpanded.Value, StringComparer.Ordinal);
        if (expanded)
            next.Add(key);
        else
            next.Remove(key);
        _treeExpanded.Value = next;
    }

    public void SetTreeSelection(string key)
    {
        if (FindTreeNode(_treeRoots, key) is { Enabled: true })
            _treeSelection.Value = key;
    }

    public ValueTask<ComboBoxSuggestionResult<string>> SuggestCombo(
        string query,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = query.Trim();
        var matches = ComboItems
            .Where(item =>
                normalized.Length == 0
                || item.Label.Contains(normalized, StringComparison.OrdinalIgnoreCase)
            )
            .ToArray();
        return ValueTask.FromResult(ComboBoxSuggestionResult.Success(matches));
    }

    public void SetListSelection(string key)
    {
        if (ListItems.Any(item => item.Key == key && item.Enabled))
            _listSelection.Value = key;
    }

    public void SetSelectSelection(string key)
    {
        if (SelectItems.Any(item => item.Key == key && item.Enabled))
            _selectSelection.Value = key;
    }

    public void SetTabSelection(string key)
    {
        if (TabItems.Any(item => item.Key == key && item.Enabled))
            _tabSelection.Value = key;
    }

    public void SetDisclosureExpanded(bool expanded) => _disclosureExpanded.Value = expanded;

    public void SetLinkMessage() => _linkMessage.Value = "The typed Link action was invoked.";

    public void OpenDocumentation() => _ = ObserveDocumentationAsync();

    private async Task ObserveDocumentationAsync()
    {
        var result = UriLauncher is null
            ? new UriLaunchResult(UriLaunchStatus.Unsupported)
            : await UriLauncher
                .LaunchAsync(
                    new Uri("https://github.com/RichiCoder1/lucent/blob/main/docs/COMPONENTS.md")
                )
                .ConfigureAwait(false);
        _scope.Post(() =>
            _linkMessage.Value = result.Status switch
            {
                UriLaunchStatus.Launched => "The system accepted the documentation link.",
                UriLaunchStatus.Canceled => "Opening documentation was canceled.",
                UriLaunchStatus.Denied => "The application's URI policy denied this link.",
                UriLaunchStatus.Unsupported => "External links are unavailable on this host.",
                _ => result.Error ?? "The system could not open documentation.",
            }
        );
    }

    public bool FocusDialog(Composition popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        return Dialog.IsOpen && popup.Input.MoveFocus(FocusTraversalDirection.Next);
    }

    public void OpenDialog()
    {
        _ = ObserveDialog(Dialog.OpenAsync().AsTask());
    }

    public void CancelDialog() => Dialog.TryCancel();

    public void SubmitDialog()
    {
        if (!Dialog.IsOpen || Dialog.IsPending)
            return;
        _ = ObserveSubmission(
            Dialog.AcceptAsync("reviewed", static (_, _) => ValueTask.CompletedTask).AsTask()
        );
    }

    public void OpenFiles() =>
        ObservePicker(
            "Open files",
            FilePicker.OpenFilesAsync(
                new OpenFileOptions(
                    Title: "Open Lucent examples",
                    Filters: [new FilePickerFilter("Lucent source", ["lui"])],
                    AllowMultiple: true
                )
            )
        );

    public void SaveDestination() =>
        ObservePicker(
            "Save destination",
            FilePicker.SaveFileAsync(
                new SaveFileOptions(
                    Title: "Choose a destination",
                    SuggestedName: "component-example.lui",
                    Filters: [new FilePickerFilter("Lucent source", ["lui"])]
                )
            )
        );

    public void ChooseFolder() =>
        ObservePicker(
            "Choose folder",
            FilePicker.PickFolderAsync(new PickFolderOptions("Choose an examples folder"))
        );

    private void ObservePicker(string operation, ValueTask<FilePickerResult> result) =>
        _ = ObservePickerAsync(operation, result.AsTask());

    private async Task ObservePickerAsync(string operation, Task<FilePickerResult> resultTask)
    {
        var result = await resultTask.ConfigureAwait(false);
        _scope.Post(() =>
        {
            switch (result.Status)
            {
                case FilePickerStatus.Selected:
                    var selected = result.Items[0];
                    _storageMessage.Value =
                        operation
                        + " selected "
                        + result.Items.Count
                        + " item"
                        + (result.Items.Count == 1 ? "" : "s")
                        + ".";
                    _storageLocation.Value = selected.Name + " · " + selected.Location;
                    break;
                case FilePickerStatus.Canceled:
                    _storageMessage.Value = operation + " was canceled.";
                    _storageLocation.Value = "No location selected.";
                    break;
                case FilePickerStatus.Unsupported:
                    _storageMessage.Value = operation + " is unavailable on this host.";
                    _storageLocation.Value = "The picker returned an unsupported capability.";
                    break;
                case FilePickerStatus.Failed:
                    _storageMessage.Value = operation + " failed.";
                    _storageLocation.Value =
                        result.Error ?? "The native picker could not complete.";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(resultTask),
                        result.Status,
                        "Unknown file picker status."
                    );
            }
        });
    }

    private async Task ObserveDialog(Task<DialogResult<string>> session)
    {
        var result = await session.ConfigureAwait(false);
        if (result.IsCanceled)
            _scope.Post(() => _dialogMessage.Value = "The dialog was canceled.");
    }

    private async Task ObserveSubmission(Task<DialogSubmissionResult> submission)
    {
        var result = await submission.ConfigureAwait(false);
        _scope.Post(() =>
        {
            _dialogMessage.Value =
                result.IsAccepted ? "The application action completed and the dialog closed."
                : result.IsFailed ? result.FailureMessage ?? "The application action failed."
                : "The application action did not complete.";
        });
    }

    public sealed record OptionItem(string Id, string Label);

    public sealed record TableRow(string Id, string Name, string Status, int OpenIssues);

    public sealed record TreeNode(
        string Id,
        string Label,
        IReadOnlyList<TreeNode>? Children = null,
        bool Enabled = true
    );

    private static TreeNode? FindTreeNode(IEnumerable<TreeNode> nodes, string key)
    {
        foreach (var node in nodes)
        {
            if (node.Id == key)
                return node;
            if (node.Children is { } children && FindTreeNode(children, key) is { } child)
                return child;
        }
        return null;
    }

    private sealed class UnsupportedFilePicker : IFilePicker
    {
        public ValueTask<FilePickerResult> OpenFilesAsync(
            OpenFileOptions? options = null,
            CancellationToken cancellationToken = default
        ) => Unsupported();

        public ValueTask<FilePickerResult> SaveFileAsync(
            SaveFileOptions? options = null,
            CancellationToken cancellationToken = default
        ) => Unsupported();

        public ValueTask<FilePickerResult> PickFolderAsync(
            PickFolderOptions? options = null,
            CancellationToken cancellationToken = default
        ) => Unsupported();

        private static ValueTask<FilePickerResult> Unsupported() =>
            ValueTask.FromResult(new FilePickerResult(FilePickerStatus.Unsupported));
    }
}
