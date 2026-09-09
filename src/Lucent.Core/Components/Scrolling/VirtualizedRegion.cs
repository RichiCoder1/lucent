using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Lucent.Core;

internal interface IVirtualizedRegion
{
    long ViewportId { get; }
    void Realize(LayoutViewport viewport, LayoutRect? assignedBounds);
}

/// <summary>A fixed-height keyed region that owns only the visible rows plus two rows of overscan on each side.</summary>
internal sealed class VirtualizedRegion<TKey, TItem> : IDisposable, IVirtualizedRegion
    where TKey : notnull
{
    private const int Overscan = 2;
    private readonly Composition _composition;
    private readonly Element _viewport;
    private Func<IEnumerable<TItem>>? _source;
    private Func<TItem, TKey>? _key;
    private Func<CurrentItem<TItem>, CompositionContext, Element>? _content;
    private readonly ReactiveEffect _effect;
    private TItem[] _items = [];
    private TKey[] _keys = [];
    private Dictionary<TKey, Entry> _entries = [];
    private long _nextEntryId;
    private bool _updating;

    internal VirtualizedRegion(
        Composition composition,
        Element viewport,
        string name,
        Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key,
        Func<CurrentItem<TItem>, CompositionContext, Element> content,
        float rowHeight,
        ThemeContext theme,
        CompositionContext? factory = null
    )
    {
        if (!float.IsFinite(rowHeight) || rowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowHeight));
        _composition = composition;
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        Region = factory is null
            ? composition.Child(viewport, name)
            : factory.Child(viewport, name);
        _source = source;
        _key = key;
        _content = content;
        Theme = theme ?? throw new ArgumentNullException(nameof(theme));
        RowHeight = rowHeight;
        Region.Scope.Own(this);
        _composition.Register(this);
        _effect = Region.Scope.Effect(Refresh, name + ".items");
    }

    public Element Region { get; }
    public float RowHeight { get; private set; }
    public int SourceCount => _items.Length;
    public IReadOnlyList<Element> Items => Region.Children;
    public bool IsDisposed { get; private set; }
    private ThemeContext Theme { get; }

    internal void Configure()
    {
        _composition.CheckThread();
        Region.UpdateControl(LayoutProperties.VirtualRowHeight, RowHeight);
        Region.UpdateControl(LayoutProperties.VirtualItemCount, _items.Length);
    }

    /// <summary>Changes the fixed row height while retaining keyed entries; callers preserve any logical scroll anchor.</summary>
    public void SetRowHeight(float rowHeight)
    {
        _composition.CheckThread();
        _composition.ThrowIfBehaviorAttachment();
        _composition.RejectForeignFactory(Region);
        if (!float.IsFinite(rowHeight) || rowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowHeight));
        if (IsDisposed || RowHeight == rowHeight)
            return;
        RowHeight = rowHeight;
        Region.UpdateControl(LayoutProperties.VirtualRowHeight, rowHeight);
        foreach (var entry in _entries.Values)
            entry.Root.UpdateControl(LayoutProperties.Height, rowHeight);
    }

    /// <summary>Re-evaluates and accepts the current source and payloads. Row realization is a later transactional phase.</summary>
    public void Refresh()
    {
        _composition.CheckThread();
        _composition.ThrowIfBehaviorAttachment();
        _composition.RejectForeignFactory(Region);
        Update(_source!());
    }

    /// <summary>Atomically accepts source identity and current realized payloads; realization waits for the next framework projection.</summary>
    public void Update(IEnumerable<TItem> items)
    {
        _composition.CheckThread();
        _composition.ThrowIfBehaviorAttachment();
        _composition.RejectForeignFactory(Region);
        ArgumentNullException.ThrowIfNull(items);
        if (IsDisposed)
            return;
        if (_updating)
            throw new InvalidOperationException("A virtualized region cannot update reentrantly.");
        _updating = true;
        try
        {
            var next = items.ToArray();
            var keys = new TKey[next.Length];
            var values = new Dictionary<TKey, TItem>();
            for (var index = 0; index < next.Length; index++)
            {
                var key = _key!(next[index]);
                if (!values.TryAdd(key, next[index]))
                    throw new ArgumentException(
                        "Virtualized region keys must be unique.",
                        nameof(items)
                    );
                keys[index] = key;
            }

            RetireDisposedEntries();
            var updates = _entries
                .Where(pair => values.ContainsKey(pair.Key))
                .Select(pair => (Entry: pair.Value, Value: values[pair.Key]))
                .ToArray();

            _items = next;
            _keys = keys;
            Region.UpdateControl(LayoutProperties.VirtualItemCount, next.Length);
            foreach (var update in updates)
                update.Entry.Current.Stage(update.Value);

            List<Exception>? errors = null;
            foreach (var update in updates)
                try
                {
                    update.Entry.Current.Notify();
                }
                catch (Exception error)
                {
                    (errors ??= []).Add(error);
                }
            Composition.ThrowAll(errors, "Virtualized region payload publication failed.");
        }
        finally
        {
            _updating = false;
        }
    }

    long IVirtualizedRegion.ViewportId => _viewport.Id;

    void IVirtualizedRegion.Realize(LayoutViewport viewport, LayoutRect? assignedBounds) =>
        Realize(viewport, assignedBounds);

    public void Realize(LayoutViewport viewport, LayoutRect? assignedBounds = null)
    {
        _composition.CheckThread();
        _composition.RejectForeignFactory(Region);
        if (IsDisposed)
            return;
        viewport.Validate();
        if (_updating)
            throw new InvalidOperationException("A virtualized region cannot realize reentrantly.");
        var outerWidth = _viewport.ResolveValue(LayoutProperties.Width) ?? viewport.Width;
        var outerHeight = _viewport.ResolveValue(LayoutProperties.Height) ?? viewport.Height;
        var outer =
            assignedBounds ?? LayoutRect.Round(0, 0, outerWidth, outerHeight, viewport.Scale);
        var viewportHeight = SceneLayout.ContentBounds(_viewport, outer, viewport.Scale).Height;
        // Source or viewport changes can precede the input router's scroll clamp.
        // Realize against the new extent now, before slicing the accepted keys.
        var maximum = Math.Max(0, (double)_items.Length * RowHeight - viewportHeight);
        var requested = _viewport.ResolveValue(LayoutProperties.Scroll).Y;
        var offset = float.IsNaN(requested) ? 0 : Math.Clamp((double)requested, 0, maximum);
        var first = (int)Math.Clamp(Math.Floor(offset / RowHeight) - Overscan, 0, _items.Length);
        var last = (int)
            Math.Clamp(
                Math.Ceiling((offset + viewportHeight) / RowHeight) + Overscan,
                first,
                _items.Length
            );
        Realize(first, last);
    }

    private void Realize(int first, int last)
    {
        _updating = true;
        try
        {
            RetireDisposedEntries();
            var wanted = new HashSet<TKey>(_keys[first..last]);
            var retained = new Dictionary<TKey, Entry>(_entries);
            var provisional = new Dictionary<TKey, (Entry Entry, CompositionContext Context)>();
            var provisionalOrder =
                new List<(Entry? Entry, ReactiveScope Scope, CompositionContext Context)>();
            try
            {
                for (var index = first; index < last; index++)
                {
                    if (retained.ContainsKey(_keys[index]))
                        continue;
                    var itemScope = Region.Scope.CreateChild(
                        Region.Name
                            + ".current-item-"
                            + checked(++_nextEntryId).ToString(CultureInfo.InvariantCulture)
                    );
                    var current = itemScope.CurrentItemForFramework(
                        _items[index],
                        itemScope.Name + ".value"
                    );
                    var context = new CompositionContext(_composition, Region, Theme);
                    provisionalOrder.Add((null, itemScope, context));
                    var root = context.Run(() => _content!(current, context));
                    context.Validate(root);
                    if (!root.HasPresentation)
                        root.Present(Theme);
                    root.UpdateControl(LayoutProperties.Height, RowHeight);
                    root.UpdateControl(LayoutProperties.VirtualRowIndex, index);
                    var entry = new Entry(root, itemScope, current);
                    provisional.Add(_keys[index], (entry, context));
                    provisionalOrder[^1] = (entry, itemScope, context);
                }

                foreach (var pair in retained)
                    if (
                        pair.Value.Root.IsDisposed
                        || pair.Value.Root.Scope.IsDisposed
                        || pair.Value.Scope.IsDisposed
                        || !_entries.TryGetValue(pair.Key, out var current)
                        || !ReferenceEquals(current, pair.Value)
                    )
                        throw new InvalidOperationException(
                            "A retained virtualized entry was disposed during realization."
                        );
            }
            catch (Exception error)
            {
                var errors = new List<Exception> { error };
                foreach (var value in provisionalOrder)
                {
                    try
                    {
                        value.Context.Dispose();
                    }
                    catch (Exception cleanup)
                    {
                        errors.Add(cleanup);
                    }
                    try
                    {
                        value.Scope.Dispose();
                    }
                    catch (Exception cleanup)
                    {
                        errors.Add(cleanup);
                    }
                }
                Composition.ThrowAll(errors, "Virtualized region factory failed.");
                throw;
            }

            var next = new Dictionary<TKey, Entry>();
            var ordered = new List<Element>(last - first);
            for (var index = first; index < last; index++)
            {
                var key = _keys[index];
                var entry = retained.TryGetValue(key, out var current)
                    ? current
                    : provisional[key].Entry;
                entry.Root.UpdateControl(LayoutProperties.VirtualRowIndex, index);
                next.Add(key, entry);
                ordered.Add(entry.Root);
            }
            foreach (var pair in provisional)
            {
                var key = pair.Key;
                var entry = pair.Value.Entry;
                entry.Root.OnDisposed(() => ReleaseEntry(key, entry));
            }
            foreach (var value in provisionalOrder)
                value.Context.Complete();
            var departed = _entries
                .Where(pair => !wanted.Contains(pair.Key))
                .Select(pair => pair.Value)
                .ToArray();
            _entries = next;
            Region.ReplaceChildren(ordered);
            foreach (var value in provisionalOrder)
            {
                value.Context.Dispose();
            }
            List<Exception>? cleanupErrors = null;
            foreach (var entry in departed)
                try
                {
                    entry.Root.Dispose();
                }
                catch (Exception error)
                {
                    (cleanupErrors ??= []).Add(error);
                }
            Composition.ThrowAll(cleanupErrors, "Virtualized region cleanup failed.");
        }
        finally
        {
            _updating = false;
        }
    }

    private void RetireDisposedEntries()
    {
        foreach (var pair in _entries.ToArray())
            if (
                pair.Value.Root.IsDisposed
                || pair.Value.Root.Scope.IsDisposed
                || pair.Value.Scope.IsDisposed
            )
                ReleaseEntry(pair.Key, pair.Value);
    }

    private void ReleaseEntry(TKey key, Entry entry)
    {
        if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            _entries.Remove(key);
        entry.Scope.Dispose();
    }

    public void Dispose()
    {
        _composition.CheckThread();
        _composition.ThrowIfBehaviorAttachment();
        _composition.RejectForeignFactory(Region);
        if (IsDisposed)
            return;
        IsDisposed = true;
        _composition.Unregister(this);
        List<Exception>? errors = null;
        try
        {
            _effect.Dispose();
        }
        catch (Exception error)
        {
            errors = [error];
        }
        var entries = _entries.Values.ToArray();
        _entries.Clear();
        if (!Region.IsDisposed)
            Region.ReplaceChildren([]);
        foreach (var entry in entries.Reverse())
            try
            {
                entry.Root.Dispose();
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        _source = null;
        _key = null;
        _content = null;
        _items = [];
        _keys = [];
        Region.Scope.Detach(this);
        Composition.ThrowAll(errors, "Virtualized region cleanup failed.");
    }

    private sealed record Entry(Element Root, ReactiveScope Scope, CurrentItem<TItem> Current);
}
