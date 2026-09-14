namespace Lucent.ComponentBrowser;

/// <summary>Retained browser-wide preferences and route authority. Examples own their own state.</summary>
public sealed class ComponentBrowserState
{
    private readonly Signal<string> _search;
    private readonly Signal<BrowserDensity> _density;
    private readonly Signal<ExampleState> _exampleState;
    private readonly Signal<string> _copyMessage;
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
        _search = scope.Signal("", "component-browser.search");
        _density = scope.Signal(BrowserDensity.Comfortable, "component-browser.density");
        _exampleState = scope.Signal(ExampleState.Default, "component-browser.example-state");
        _copyMessage = scope.Signal(
            "Source is ready to inspect.",
            "component-browser.copy-message"
        );
        SearchEditor = new(scope, "component-search", "");
        FilePicker = filePicker ?? new UnsupportedFilePicker();
        UriLauncher = uriLauncher;
        Navigation = new(
            scope,
            ComponentBrowserRouting.Table,
            ComponentBrowserRoutes.Example(ComponentCatalog.Items[0].Id).Location
        );
        Interaction = new(scope, Navigation);
        Navigation.RegisterCommitted(
            scope,
            _ => _copyMessage.Value = "Source is ready to inspect."
        );
    }

    public EditorSession SearchEditor { get; }
    public IFilePicker FilePicker { get; }
    public IUriLauncher? UriLauncher { get; }
    public NavigationSession Navigation { get; }
    public NavigationInteraction Interaction { get; }

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
    public string SelectedId =>
        Navigation.Current?.Match.GetValue(0).Text ?? ComponentCatalog.Items[0].Id;
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

    public bool IsSelected(string id) => SelectedId == id;

    public void SetSearch(string value) => Search = value;

    public void Select(string id)
    {
        if (id != SelectedId && ComponentCatalog.Items.Any(item => item.Id == id))
            Navigation.Navigate(ComponentBrowserRoutes.Example(id));
    }

    public void Back() => Navigation.Back();

    public void Forward() => Navigation.Forward();

    public void ToggleDensity() =>
        _density.Value =
            Density == BrowserDensity.Comfortable
                ? BrowserDensity.Compact
                : BrowserDensity.Comfortable;

    public void SetExampleState(ExampleState state) => _exampleState.Value = state;

    public void CopySource()
    {
        ExampleClipboard.TryCopy(SelectedSource, out var message);
        _copyMessage.Value = message;
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
