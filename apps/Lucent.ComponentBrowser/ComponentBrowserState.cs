namespace Lucent.ComponentBrowser;

public sealed class ComponentBrowserState
{
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
    private readonly IReadOnlyList<OptionItem> _options =
    [
        new("native", "Native rendering"),
        new("portable", "Portable semantics"),
        new("a11y", "Accessible by default"),
    ];
    private IReadOnlyList<ComponentCatalogItem>? _visibleSource;
    private string? _visibleSearch;
    private IReadOnlyList<ComponentCatalogItem> _visibleCache = [];

    public ComponentBrowserState(ReactiveScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
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
        SearchEditor = new(scope, "component-search", "");
        ExampleField = new(scope, "example-field", "Lucent");
        ExampleNotes = new(
            scope,
            "example-notes",
            "Keep the session in application state when a draft must survive a remount.",
            multiline: true
        );
        Form = scope.Own(new FormSession(scope, "component-browser-form"));
    }

    public EditorSession SearchEditor { get; }
    public EditorSession ExampleField { get; }
    public EditorSession ExampleNotes { get; }
    public FormSession Form { get; }
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

    public sealed record OptionItem(string Id, string Label);
}
