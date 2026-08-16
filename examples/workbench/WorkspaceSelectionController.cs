using Avalonia.Controls;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Lucent.Examples.Workbench;

internal sealed class WorkspaceSelectionController
{
    private readonly Dictionary<string, WorkspaceNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _parents = new(StringComparer.Ordinal);
    private readonly Dictionary<ObservableCollection<WorkspaceNode>, WorkspaceNode?> _collections =
        new(ReferenceEqualityComparer.Instance);
    private ListBox? _listBox;
    private string? _selectedId;
    private bool _suppressChanges;

    public WorkspaceSelectionController(IEnumerable<WorkspaceNode> roots)
    {
        Roots = new ResettableObservableCollection<WorkspaceNode>();
        VisibleRows = new ResettableObservableCollection<WorkspaceRow>();
        ReplaceRoots(roots);
    }

    public ResettableObservableCollection<WorkspaceNode> Roots { get; }

    public ResettableObservableCollection<WorkspaceRow> VisibleRows { get; }

    public string? SelectedId => _selectedId;

    public void Attach(ListBox listBox)
    {
        if (ReferenceEquals(_listBox, listBox)) return;
        Detach(_listBox);
        _listBox = listBox;
        listBox.SelectionChanged += OnSelectionChanged;
        listBox.ItemsSource = VisibleRows;
        listBox.SelectedItem = FindVisibleRow(_selectedId);
    }

    public void Detach(ListBox? listBox)
    {
        if (!ReferenceEquals(_listBox, listBox) || listBox is null) return;
        listBox.SelectionChanged -= OnSelectionChanged;
        if (ReferenceEquals(listBox.ItemsSource, VisibleRows)) listBox.ItemsSource = null;
        _listBox = null;
    }

    public void ReplaceRoots(IEnumerable<WorkspaceNode> roots)
    {
        var selected = _selectedId;
        var replacement = roots.ToArray();
        Validate(replacement);
        _suppressChanges = true;
        try { Roots.ReplaceAll(replacement); }
        finally { _suppressChanges = false; }
        Refresh(selected);
    }

    public void Rebuild()
    {
        Refresh(_selectedId);
    }

    public void SetExpanded(string id, bool expanded)
    {
        if (!_nodes.TryGetValue(id, out var node)) return;
        var selected = _selectedId;
        node.IsExpanded = expanded;
        RebuildVisible();
        SelectExistingOrClear(selected);
    }

    public void SelectById(string? id)
    {
        SelectExistingOrClear(id);
    }

    public bool RemoveById(string id)
    {
        if (!_nodes.TryGetValue(id, out var node)) return false;
        var wasSelected = _selectedId is not null && Contains(node, _selectedId);
        var fallback = wasSelected ? FindRemovalFallback(node) : _selectedId;
        var parent = FindParentCollection(node);
        if (parent is null) return false;
        _suppressChanges = true;
        try { parent.Remove(node); }
        finally { _suppressChanges = false; }
        Refresh(fallback);
        if (wasSelected) _listBox?.Focus();
        return true;
    }

    private void OnTreeChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressChanges || sender is not ObservableCollection<WorkspaceNode> collection) return;
        var selected = _selectedId;
        var selectedRemoved = e.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace &&
            selected is not null && e.OldItems?.Cast<WorkspaceNode>()
                .Any(node => Contains(node, selected)) == true;
        var fallback = selectedRemoved ? RemovalFallback(collection, e.OldStartingIndex) : selected;
        Refresh(fallback);
        if (selectedRemoved) _listBox?.Focus();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_listBox?.SelectedItem is WorkspaceRow row)
            _selectedId = row.Node.Id;
        else if (_listBox is not null)
            _selectedId = null;
    }

    private void SelectExistingOrClear(string? id)
    {
        _selectedId = id is not null && _nodes.ContainsKey(id) ? id : null;
        if (_listBox is not null)
            _listBox.SelectedItem = FindVisibleRow(_selectedId);
    }

    private WorkspaceRow? FindVisibleRow(string? id) => id is null
        ? null
        : VisibleRows.FirstOrDefault(row => row.Node.Id == id);

    private void RebuildVisible()
    {
        var rows = new List<WorkspaceRow>();
        foreach (var root in Roots) AddVisible(root, 0, rows);
        VisibleRows.ReplaceAll(rows);
    }

    private static void AddVisible(WorkspaceNode node, int depth, List<WorkspaceRow> rows)
    {
        rows.Add(new WorkspaceRow(node, depth));
        if (!node.IsExpanded) return;
        foreach (var child in node.Children) AddVisible(child, depth + 1, rows);
    }

    private void RebuildIndex()
    {
        _nodes.Clear();
        _parents.Clear();
        foreach (var root in Roots) AddIndex(root, null);
    }

    private void Refresh(string? selected)
    {
        Validate(Roots);
        ObserveCollections();
        RebuildIndex();
        RebuildVisible();
        SelectExistingOrClear(selected);
    }

    private void ObserveCollections()
    {
        foreach (var collection in _collections.Keys)
            collection.CollectionChanged -= OnTreeChanged;
        _collections.Clear();
        Observe(Roots, null);
    }

    private void Observe(ObservableCollection<WorkspaceNode> collection, WorkspaceNode? owner)
    {
        _collections.Add(collection, owner);
        collection.CollectionChanged += OnTreeChanged;
        foreach (var node in collection)
            Observe(node.Children, node);
    }

    private void AddIndex(WorkspaceNode node, string? parentId)
    {
        _nodes.Add(node.Id, node);
        _parents.Add(node.Id, parentId);
        foreach (var child in node.Children) AddIndex(child, node.Id);
    }

    private string? FindRemovalFallback(WorkspaceNode node)
    {
        var siblings = FindParentCollection(node);
        if (siblings is null) return null;
        var index = siblings.IndexOf(node);
        if (index + 1 < siblings.Count) return siblings[index + 1].Id;
        if (index > 0) return siblings[index - 1].Id;
        var parentId = _parents.GetValueOrDefault(node.Id);
        return parentId is not null && _nodes.ContainsKey(parentId) ? parentId : null;
    }

    private ObservableCollection<WorkspaceNode>? FindParentCollection(WorkspaceNode node)
    {
        var parentId = _parents.GetValueOrDefault(node.Id);
        if (parentId is null) return Roots;
        return _nodes.TryGetValue(parentId, out var parent) ? parent.Children : null;
    }

    private string? RemovalFallback(ObservableCollection<WorkspaceNode> collection, int removedIndex)
    {
        if (removedIndex >= 0 && removedIndex < collection.Count) return collection[removedIndex].Id;
        if (collection.Count > 0) return collection[^1].Id;
        return _collections.GetValueOrDefault(collection)?.Id;
    }

    private static bool Contains(WorkspaceNode node, string id) =>
        node.Id == id || node.Children.Any(child => Contains(child, id));

    private static void Validate(IEnumerable<WorkspaceNode> roots)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots) ValidateNode(root, ids);
    }

    private static void ValidateNode(WorkspaceNode node, HashSet<string> ids)
    {
        if (string.IsNullOrWhiteSpace(node.Id) || !ids.Add(node.Id))
            throw new InvalidOperationException($"Workspace node ID '{node.Id}' is missing or duplicated.");
        foreach (var child in node.Children) ValidateNode(child, ids);
    }
}
