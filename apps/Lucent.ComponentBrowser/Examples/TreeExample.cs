namespace Lucent.ComponentBrowser;

public sealed class TreeExampleModel
{
    private readonly Signal<string> _selection;
    private readonly Signal<IReadOnlySet<string>> _expanded;

    public TreeExampleModel(ReactiveScope owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _selection = owner.Signal("src", "component-browser.tree-selection");
        _expanded = owner.Signal<IReadOnlySet<string>>(
            new HashSet<string>(["src", "apps"], StringComparer.Ordinal),
            "component-browser.tree-expanded"
        );
        DataSource = new(
            node => node.Id,
            node => node.Label,
            node => node.Children,
            node => node.Children is { Count: > 0 },
            node => node.Enabled
        );
    }

    public IReadOnlyList<Node> Roots { get; } =
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

    public TreeDataSource<string, Node> DataSource { get; }

    public TreeViewOptions Options { get; } =
        new(selectionMode: ListBoxSelectionMode.FollowsFocus, rowHeight: 34);

    public SelectedKey<string> Selection => SelectedKey.Some(_selection.Value);

    public string SelectionLabel =>
        FindNode(Roots, _selection.Value) is { } selected
            ? $"Selected {selected.Label}."
            : "No tree item is selected.";

    public string ExpansionSummary =>
        $"{_expanded.Value.Count:N0} branch{(_expanded.Value.Count == 1 ? "" : "es")} expanded.";

    public bool IsExpanded(string key) => _expanded.Value.Contains(key);

    public void SetExpanded(string key, bool expanded)
    {
        var node = FindNode(Roots, key);
        if (node?.Children is not { Count: > 0 })
            return;

        var next = new HashSet<string>(_expanded.Value, StringComparer.Ordinal);
        if (expanded)
            next.Add(key);
        else
            next.Remove(key);
        _expanded.Value = next;
    }

    public void SetSelection(string key)
    {
        if (FindNode(Roots, key) is { Enabled: true })
            _selection.Value = key;
    }

    private static Node? FindNode(IEnumerable<Node> nodes, string key)
    {
        foreach (var node in nodes)
        {
            if (node.Id == key)
                return node;
            if (node.Children is { } children && FindNode(children, key) is { } child)
                return child;
        }
        return null;
    }

    public sealed record Node(
        string Id,
        string Label,
        IReadOnlyList<Node>? Children = null,
        bool Enabled = true
    );
}
