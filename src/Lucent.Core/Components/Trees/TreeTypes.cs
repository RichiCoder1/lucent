using System.Collections.ObjectModel;

namespace Lucent.Core;

/// <summary>Groups stable identity, presentation, and child discovery for a keyed tree.</summary>
public sealed class TreeDataSource<TKey, TItem>
    where TKey : notnull
{
    /// <summary>Creates a cohesive tree data source. A null children result means the children are not loaded.</summary>
    public TreeDataSource(
        Func<TItem, TKey> key,
        Func<TItem, string> label,
        Func<TItem, IEnumerable<TItem>?> children,
        Func<TItem, bool> hasChildren,
        Func<TItem, bool>? enabled = null,
        Func<TItem, CancellationToken, ValueTask<TreeChildrenResult<TItem>>>? loadChildren = null,
        Func<TItem, ComponentRecipe?>? content = null
    )
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Children = children ?? throw new ArgumentNullException(nameof(children));
        HasChildren = hasChildren ?? throw new ArgumentNullException(nameof(hasChildren));
        Enabled = enabled ?? (_ => true);
        LoadChildren = loadChildren;
        Content = content;
    }

    /// <summary>Gets the stable non-null key reader.</summary>
    public Func<TItem, TKey> Key { get; }

    /// <summary>Gets the nonempty visible and accessible label reader.</summary>
    public Func<TItem, string> Label { get; }

    /// <summary>Gets the current children, or null while lazy children are unavailable.</summary>
    public Func<TItem, IEnumerable<TItem>?> Children { get; }

    /// <summary>Gets whether a row exposes expansion even when its children are not loaded.</summary>
    public Func<TItem, bool> HasChildren { get; }

    /// <summary>Gets whether a row can receive focus, selection, or expansion requests.</summary>
    public Func<TItem, bool> Enabled { get; }

    /// <summary>Gets the optional owned lazy child loader.</summary>
    public Func<
        TItem,
        CancellationToken,
        ValueTask<TreeChildrenResult<TItem>>
    >? LoadChildren { get; }

    /// <summary>Gets optional visual content. Null uses the stock label decoration.</summary>
    public Func<TItem, ComponentRecipe?>? Content { get; }
}

/// <summary>A typed successful or recoverable lazy-child result.</summary>
public sealed class TreeChildrenResult<TItem>
{
    private readonly IReadOnlyList<TItem> _items;

    private TreeChildrenResult(IEnumerable<TItem> items, string? failure)
    {
        var copy = items.ToArray();
        if (copy.Any(static item => item is null))
            throw new ArgumentException("Tree children cannot contain null.", nameof(items));
        _items = new ReadOnlyCollection<TItem>(copy);
        Failure = failure;
    }

    /// <summary>Gets the immutable successful children, or an empty list after a recoverable failure.</summary>
    public IReadOnlyList<TItem> Items => _items;

    /// <summary>Gets the caller-approved recoverable failure message.</summary>
    public string? Failure { get; }

    /// <summary>Gets whether the loader reported an expected recoverable failure.</summary>
    public bool IsFailure => Failure is not null;

    internal static TreeChildrenResult<TItem> CreateSuccess(IEnumerable<TItem> items) =>
        new(items, null);

    internal static TreeChildrenResult<TItem> CreateFailure(string message) => new([], message);
}

/// <summary>Creates typed lazy tree-child results.</summary>
public static class TreeChildrenResult
{
    /// <summary>Creates a successful immutable child snapshot.</summary>
    public static TreeChildrenResult<TItem> Success<TItem>(IEnumerable<TItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return TreeChildrenResult<TItem>.CreateSuccess(items);
    }

    /// <summary>Creates an expected recoverable failure with safe display text.</summary>
    public static TreeChildrenResult<TItem> Failed<TItem>(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return TreeChildrenResult<TItem>.CreateFailure(message);
    }
}

/// <summary>Controls stock TreeView selection and fixed-height virtualization.</summary>
public sealed class TreeViewOptions
{
    /// <summary>Creates tree options with density-derived row height and focus-following selection.</summary>
    public TreeViewOptions(
        ListBoxSelectionMode selectionMode = ListBoxSelectionMode.FollowsFocus,
        float? rowHeight = null
    )
    {
        if (!Enum.IsDefined(selectionMode))
            throw new ArgumentOutOfRangeException(nameof(selectionMode));
        if (rowHeight is { } height && (!float.IsFinite(height) || height <= 0))
            throw new ArgumentOutOfRangeException(nameof(rowHeight));
        SelectionMode = selectionMode;
        RowHeight = rowHeight;
    }

    /// <summary>Gets whether keyboard focus immediately requests selection.</summary>
    public ListBoxSelectionMode SelectionMode { get; }

    /// <summary>Gets the optional fixed virtual row height.</summary>
    public float? RowHeight { get; }
}

internal enum TreeLazyState
{
    Unloaded,
    Loading,
    Ready,
    Failed,
}
