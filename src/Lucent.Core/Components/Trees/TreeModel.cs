namespace Lucent.Core;

internal sealed record TreeVisibleNode<TKey, TItem>(
    TKey Key,
    TItem Item,
    SelectedKey<TKey> Parent,
    int Level,
    int Position,
    int SetSize,
    int CollectionIndex,
    bool Enabled,
    bool HasChildren,
    bool Expanded
)
    where TKey : notnull;

internal enum TreeVisibleRowKind
{
    Node,
    Loading,
    Failure,
}

internal readonly record struct TreeVisibleRowKey<TKey>(TKey Key, TreeVisibleRowKind Kind)
    where TKey : notnull;

internal sealed record TreeVisibleRow<TKey, TItem>(
    TreeVisibleRowKey<TKey> Key,
    TreeVisibleRowKind Kind,
    TreeVisibleNode<TKey, TItem>? Node,
    int Level,
    string? Failure
)
    where TKey : notnull;

internal sealed record TreeVisibleSnapshot<TKey, TItem>(
    TreeVisibleNode<TKey, TItem>[] Nodes,
    TreeVisibleRow<TKey, TItem>[] Rows,
    Dictionary<TKey, int> NodeIndices,
    Dictionary<TKey, int> RowIndices
)
    where TKey : notnull;

internal sealed class TreeLazyStore<TKey, TItem> : IDisposable
    where TKey : notnull
{
    private readonly ReactiveScope _scope;
    private readonly TreeDataSource<TKey, TItem> _source;
    private readonly Dictionary<TKey, Entry> _entries = [];
    private readonly Signal<int> _revision;

    internal TreeLazyStore(ReactiveScope scope, string name, TreeDataSource<TKey, TItem> source)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _revision = scope.Signal(0, name + ".revision");
    }

    internal void Synchronize(IEnumerable<TItem> roots, Func<TKey, bool> expanded)
    {
        _ = _revision.Value;
        var reachable = new HashSet<TKey>();
        foreach (var item in Snapshot(roots, "Tree roots"))
            Visit(item);
        foreach (var pair in _entries.ToArray())
        {
            if (reachable.Contains(pair.Key))
                continue;
            _entries.Remove(pair.Key);
            pair.Value.Scope.Dispose();
            _revision.Value++;
        }

        void Visit(TItem item)
        {
            var key = ReadKey(item);
            if (!reachable.Add(key))
                throw new ArgumentException("Tree keys must be unique across the reachable tree.");
            var children = ReadChildren(item);
            if (children is not null)
            {
                if (_entries.Remove(key, out var superseded))
                {
                    superseded.Scope.Dispose();
                    _revision.Value++;
                }
                foreach (var child in children)
                    Visit(child);
                return;
            }
            if (_entries.TryGetValue(key, out var retained))
            {
                retained.Item = item;
                var state = Inspect(retained);
                if (!expanded(key) && state.State == TreeLazyState.Loading)
                {
                    _entries.Remove(key);
                    retained.Scope.Dispose();
                    _revision.Value++;
                    return;
                }
                if (state.Children is not null)
                    foreach (var child in state.Children)
                        Visit(child);
            }
            if (expanded(key) && _source.HasChildren(item))
                _ = Ensure(key, item);
        }
    }

    internal TreeVisibleSnapshot<TKey, TItem> Flatten(
        IEnumerable<TItem> roots,
        Func<TKey, bool> expanded
    )
    {
        _ = _revision.Value;
        var nodes = new List<TreeVisibleNode<TKey, TItem>>();
        var rows = new List<TreeVisibleRow<TKey, TItem>>();
        var nodeIndices = new Dictionary<TKey, int>();
        var rowIndices = new Dictionary<TKey, int>();
        Append(Snapshot(roots, "Tree roots"), SelectedKey.None<TKey>(), 1);
        return new(nodes.ToArray(), rows.ToArray(), nodeIndices, rowIndices);

        void Append(IReadOnlyList<TItem> siblings, SelectedKey<TKey> parent, int level)
        {
            for (var position = 0; position < siblings.Count; position++)
            {
                var item = siblings[position];
                var key = ReadKey(item);
                if (!nodeIndices.TryAdd(key, nodes.Count))
                    throw new ArgumentException(
                        "Tree keys must be unique across the visible tree."
                    );
                var hasChildren = _source.HasChildren(item);
                var isExpanded = hasChildren && expanded(key);
                var node = new TreeVisibleNode<TKey, TItem>(
                    key,
                    item,
                    parent,
                    level,
                    position + 1,
                    siblings.Count,
                    nodes.Count,
                    _source.Enabled(item),
                    hasChildren,
                    isExpanded
                );
                nodes.Add(node);
                rowIndices.Add(key, rows.Count);
                rows.Add(
                    new(
                        new(key, TreeVisibleRowKind.Node),
                        TreeVisibleRowKind.Node,
                        node,
                        level,
                        null
                    )
                );
                if (!isExpanded)
                    continue;
                var current = ReadChildren(item);
                LazyView lazy = default;
                if (current is null && _entries.TryGetValue(key, out var entry))
                    lazy = Inspect(entry);
                current ??= lazy.Children;
                if (current is not null)
                {
                    Append(current, SelectedKey.Some(key), level + 1);
                    continue;
                }
                var kind =
                    lazy.State == TreeLazyState.Failed
                        ? TreeVisibleRowKind.Failure
                        : TreeVisibleRowKind.Loading;
                rows.Add(new(new(key, kind), kind, null, level + 1, lazy.Failure));
            }
        }
    }

    internal bool Retry(TKey key)
    {
        if (!_entries.TryGetValue(key, out var entry))
            return false;
        entry.Result.Refresh();
        return true;
    }

    private Entry Ensure(TKey key, TItem item)
    {
        if (_entries.TryGetValue(key, out var retained))
        {
            retained.Item = item;
            _ = retained.Result.IsPending;
            return retained;
        }
        var loader =
            _source.LoadChildren
            ?? throw new InvalidOperationException(
                "An expanded tree item has unloaded children but no lazy child loader."
            );
        var childScope = _scope.CreateChild("tree.lazy." + _entries.Count);
        var entry = new Entry(item, childScope);
        entry.Result = childScope.Async(
            async token =>
            {
                var result = await loader(entry.Item, token).ConfigureAwait(false);
                return result
                    ?? throw new InvalidOperationException("A tree child loader returned null.");
            },
            childScope.Name + ".load"
        );
        _entries.Add(key, entry);
        _revision.Value++;
        _ = entry.Result.IsPending;
        return entry;
    }

    private static LazyView Inspect(Entry entry)
    {
        if (entry.Result.Error is { } error)
            throw new InvalidOperationException("Unexpected tree child-loader failure.", error);
        if (entry.Result.IsPending || !entry.Result.HasValue)
            return new(TreeLazyState.Loading, null, null);
        var result = entry.Result.Value!;
        return result.IsFailure
            ? new(TreeLazyState.Failed, null, result.Failure)
            : new(TreeLazyState.Ready, Snapshot(result.Items, "Loaded tree children"), null);
    }

    private TKey ReadKey(TItem item)
    {
        var key = _source.Key(item);
        ArgumentNullException.ThrowIfNull(key);
        return key;
    }

    private TItem[]? ReadChildren(TItem item)
    {
        var children = _source.Children(item);
        return children is null ? null : Snapshot(children, "Tree children");
    }

    private static TItem[] Snapshot(IEnumerable<TItem> items, string source)
    {
        ArgumentNullException.ThrowIfNull(items);
        var result = items.ToArray();
        if (result.Any(static item => item is null))
            throw new ArgumentException(source + " cannot contain null items.", nameof(items));
        return result;
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values)
            entry.Scope.Dispose();
        _entries.Clear();
    }

    private sealed class Entry(TItem item, ReactiveScope scope)
    {
        internal TItem Item { get; set; } = item;
        internal ReactiveScope Scope { get; } = scope;
        internal AsyncValue<TreeChildrenResult<TItem>> Result { get; set; } = null!;
    }

    private readonly record struct LazyView(
        TreeLazyState State,
        TItem[]? Children,
        string? Failure
    );
}
