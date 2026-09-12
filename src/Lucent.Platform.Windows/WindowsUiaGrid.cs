using System.Runtime.InteropServices;
using Lucent.Core;

namespace Lucent.Platform.Windows;

internal sealed unsafe partial class WindowsUiaProvider
{
    private Action? _refreshLayout;
    private bool _realizingItem;

    /// <summary>
    /// Installs the owner-thread refresh used by virtualization patterns. The callback is
    /// deliberately owned by the host so an adapter never reads the filesystem or pumps a
    /// nested UI loop while realizing an item.
    /// </summary>
    internal void SetRealizationRefresh(Action? refresh) => _root._refreshLayout = refresh;

    internal int GridCount(bool columns, out int value)
    {
        var node = CurrentNode();
        value = columns ? node?.Grid?.ColumnCount ?? 0 : node?.Grid?.RowCount ?? 0;
        return node is null ? NotAvailable
            : node.Grid is null ? InvalidOperation
            : Ok;
    }

    internal int CellCoordinate(bool column, out int value)
    {
        var node = CurrentNode();
        value = column ? node?.GridItem?.Column ?? 0 : node?.GridItem?.Row ?? 0;
        return node is null ? NotAvailable
            : node.GridItem is null ? InvalidOperation
            : Ok;
    }

    internal int CellSpan(out int value)
    {
        var node = CurrentNode();
        value = node?.GridItem is null ? 0 : 1;
        return node is null ? NotAvailable
            : node.GridItem is null ? InvalidOperation
            : Ok;
    }

    internal int ContainingGrid(out nint value)
    {
        value = 0;
        var snapshot = CurrentSnapshot();
        var node = CurrentNode(snapshot);
        if (node is null)
            return NotAvailable;
        if (node.GridItem is not { } cell)
            return InvalidOperation;
        if (
            !snapshot.Nodes.TryGetValue(
                new(cell.Grid.CompositionEpoch, cell.Grid.ElementId),
                out var grid
            )
        )
            return NotAvailable;
        var provider = Provider(grid);
        value = provider is null ? 0 : Query(provider._unknown, UiaWrappers.Simple);
        return value == 0 ? NotAvailable : Ok;
    }

    internal int GridItem(int row, int column, out nint value)
    {
        value = 0;
        if (row < 0 || column < 0)
            return InvalidArgument;
        if (
            !Try(
                "Grid.GetItem",
                () =>
                {
                    var snapshot = CurrentSnapshot();
                    var grid = CurrentNode(snapshot);
                    if (grid is null)
                        return (NotAvailable, (nint)0);
                    if (grid.Grid is not { } dimensions)
                        return (InvalidOperation, (nint)0);
                    if (row >= dimensions.RowCount || column >= dimensions.ColumnCount)
                        return (InvalidArgument, (nint)0);
                    Node? FindCell(Snapshot source) =>
                        source.Nodes.Values.FirstOrDefault(node =>
                            node.GridItem is { } gridItem
                            && gridItem.Grid.CompositionEpoch == grid.Key.Epoch
                            && gridItem.Grid.ElementId == grid.Key.Element
                            && gridItem.Row == row
                            && gridItem.Column == column
                        );
                    var cell = FindCell(snapshot);
                    if (cell is null)
                    {
                        var realized = Realize(grid, row);
                        if (realized != Ok)
                            return (realized, (nint)0);
                        cell = FindCell(CurrentSnapshot());
                    }
                    var provider = Provider(cell);
                    return provider is null
                        ? (NotAvailable, (nint)0)
                        : (Ok, Query(provider._unknown, UiaWrappers.Simple));
                },
                out var result
            )
        )
            return Fail;
        value = result.Item2;
        return result.Item1;
    }

    // Enumeration realizes exactly one requested row. It does not manufacture providers for every
    // offscreen item or walk a large model to implement an unbounded property search.
    internal int FindCollectionItem(
        nint startAfter,
        int propertyId,
        RawVariant propertyValue,
        out nint value
    )
    {
        value = 0;
        if (propertyId != 0 || propertyValue.Type != 0)
            return InvalidArgument;
        WindowsUiaProvider? prior = null;
        if (
            startAfter != 0
            && (
                !ComWrappers.TryGetObject(startAfter, out var managed)
                || (prior = managed as WindowsUiaProvider) is null
                || !ReferenceEquals(prior._root, _root)
            )
        )
            return InvalidArgument;
        if (
            !Try(
                "ItemContainer.FindNext",
                () =>
                {
                    var snapshot = CurrentSnapshot();
                    var container = CurrentNode(snapshot);
                    if (container is null)
                        return (NotAvailable, (nint)0);
                    if (container.Collection is not { } collection)
                        return (InvalidOperation, (nint)0);
                    var index = 0;
                    if (prior is not null)
                    {
                        var previous = prior.CurrentNode(snapshot);
                        if (
                            previous?.CollectionIndex is not { } previousIndex
                            || !BelongsToCollection(snapshot, previous, container.Key)
                        )
                            return (InvalidArgument, (nint)0);
                        index = checked(previousIndex + 1);
                    }
                    if (index >= collection.ItemCount)
                        return (Ok, (nint)0);
                    var result = Realize(container, index);
                    if (result != Ok)
                        return (result, (nint)0);
                    snapshot = CurrentSnapshot();
                    var item = snapshot.Nodes.Values.FirstOrDefault(node =>
                        node.CollectionIndex == index
                        && BelongsToCollection(snapshot, node, container.Key)
                    );
                    var provider = Provider(item);
                    return provider is null
                        ? (NotAvailable, (nint)0)
                        : (Ok, Query(provider._unknown, UiaWrappers.Simple));
                },
                out var found
            )
        )
            return Fail;
        value = found.Item2;
        return found.Item1;
    }

    private int Realize(Node container, int index)
    {
        if (!container.Enabled)
            return ElementNotEnabled;
        if (
            !container.Actions.HasFlag(SemanticAction.RealizeItem)
            || _root._refreshLayout is null
            || _root._realizingItem
        )
            return InvalidOperation;
        _root._realizingItem = true;
        try
        {
            var result = _composition.ExecuteSemanticCommand(
                container.Identity,
                new(SemanticCommandKind.RealizeItem, ItemIndex: index)
            );
            if (result != SemanticCommandResult.Applied)
                return result == SemanticCommandResult.Stale ? NotAvailable : InvalidOperation;
            _root._refreshLayout();
            return Ok;
        }
        finally
        {
            _root._realizingItem = false;
        }
    }

    private static bool BelongsToCollection(Snapshot snapshot, Node item, NodeKey container)
    {
        var parent = item.Parent;
        while (parent is { } key && snapshot.Nodes.TryGetValue(key, out var ancestor))
        {
            if (key == container)
                return true;
            if (ancestor.Collection is not null)
                return false;
            parent = ancestor.Parent;
        }
        return false;
    }

    private static bool BelongsToSelection(Snapshot snapshot, Node item, NodeKey container)
    {
        var parent = item.Parent;
        while (parent is { } key && snapshot.Nodes.TryGetValue(key, out var ancestor))
        {
            if (key == container)
                return true;
            if (
                ancestor.Selection is not null
                || ancestor.Role
                    is SemanticRole.List
                        or SemanticRole.RadioGroup
                        or SemanticRole.TabList
                        or SemanticRole.Tree
                        or SemanticRole.Calendar
                        or SemanticRole.Table
            )
                return false;
            parent = ancestor.Parent;
        }
        return false;
    }

    internal int TableHeaders(bool columnHeaders, bool forCell, out nint value)
    {
        value = 0;
        var snapshot = CurrentSnapshot();
        var node = CurrentNode(snapshot);
        if (node is null)
            return NotAvailable;
        var column = -1;
        if (forCell)
        {
            if (node.GridItem is not { } cell)
                return InvalidOperation;
            column = cell.Column;
            snapshot.Nodes.TryGetValue(
                new(cell.Grid.CompositionEpoch, cell.Grid.ElementId),
                out node
            );
        }
        if (node?.Grid is not { } grid)
            return InvalidOperation;
        // The first table contract carries retained column headers. Core has no row-header
        // identity collection yet, so the native row-header methods return an empty SAFEARRAY
        // while preserving S_OK and the provider's array ownership contract.
        var headers = columnHeaders
            ? grid
                .ColumnHeaders.Where((_, index) => !forCell || index == column)
                .Select(identity =>
                    snapshot.Nodes.GetValueOrDefault(
                        new(identity.CompositionEpoch, identity.ElementId)
                    )
                )
                .OfType<Node>()
            : [];
        return ProviderArray(headers.ToArray(), out value);
    }

    internal int TableMajor(out int value)
    {
        value = 0; // RowOrColumnMajor_RowMajor.
        var node = CurrentNode();
        return node is null ? NotAvailable
            : node.Grid is null ? InvalidOperation
            : Ok;
    }

    private int ProviderArray(IReadOnlyList<Node> nodes, out nint value)
    {
        value = SafeArrayCreateVector(VtUnknown, 0, (uint)nodes.Count);
        if (value == 0)
            return OutOfMemory;
        for (var index = 0; index < nodes.Count; index++)
        {
            var provider = Provider(nodes[index]);
            if (provider is null)
            {
                _ = SafeArrayDestroy(value);
                value = 0;
                return NotAvailable;
            }
            var pointer = Query(provider._unknown, UiaWrappers.Simple);
            if (pointer == 0)
            {
                _ = SafeArrayDestroy(value);
                value = 0;
                return NotAvailable;
            }
            var at = index;
            var result = SafeArrayPutElement(value, &at, (void*)pointer);
            Release(ref pointer);
            if (result >= 0)
                continue;
            _ = SafeArrayDestroy(value);
            value = 0;
            return result;
        }
        return Ok;
    }
}
