using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Lucent.Core;

internal interface IVirtualizedRegion
{
    void Realize(LayoutViewport viewport);
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
    private Func<TItem, CompositionContext, Element>? _content;
    private readonly ReactiveEffect _effect;
    private TItem[] _items = [];
    private TKey[] _keys = [];
    private Dictionary<TKey, Element> _entries = [];
    private bool _updating;

    internal VirtualizedRegion(
        Composition composition,
        Element viewport,
        string name,
        Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key,
        Func<TItem, CompositionContext, Element> content,
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
            entry.UpdateControl(LayoutProperties.Height, rowHeight);
    }

    /// <summary>Re-evaluates the source. Normal callers let the owned reactive effect invoke this.</summary>
    public void Refresh()
    {
        _composition.CheckThread();
        _composition.ThrowIfBehaviorAttachment();
        _composition.RejectForeignFactory(Region);
        Update(_source!());
    }

    /// <summary>Updates source identity; realization waits for the next framework projection.</summary>
    public void Update(IEnumerable<TItem> items)
    {
        _composition.CheckThread();
        _composition.ThrowIfBehaviorAttachment();
        _composition.RejectForeignFactory(Region);
        ArgumentNullException.ThrowIfNull(items);
        if (IsDisposed)
            return;
        var next = items.ToArray();
        var keys = new TKey[next.Length];
        var unique = new HashSet<TKey>();
        for (var index = 0; index < next.Length; index++)
        {
            var key = _key!(next[index]);
            if (!unique.Add(key))
                throw new ArgumentException(
                    "Virtualized region keys must be unique.",
                    nameof(items)
                );
            keys[index] = key;
        }
        _items = next;
        _keys = keys;
        Region.UpdateControl(LayoutProperties.VirtualItemCount, next.Length);
    }

    void IVirtualizedRegion.Realize(LayoutViewport viewport) => Realize(viewport);

    public void Realize(LayoutViewport viewport)
    {
        _composition.CheckThread();
        _composition.RejectForeignFactory(Region);
        if (IsDisposed)
            return;
        viewport.Validate();
        if (_updating)
            throw new InvalidOperationException("A virtualized region cannot realize reentrantly.");
        var outerWidth = _viewport.Resolve(LayoutProperties.Width).Value ?? viewport.Width;
        var outerHeight = _viewport.Resolve(LayoutProperties.Height).Value ?? viewport.Height;
        var viewportHeight = SceneLayout
            .ContentBounds(
                LayoutRect.Round(0, 0, outerWidth, outerHeight, viewport.Scale),
                _viewport.Resolve(LayoutProperties.Padding).Value,
                viewport.Scale
            )
            .Height;
        var offset = _viewport.Resolve(LayoutProperties.Scroll).Value.Y;
        var first = Math.Max(0, (int)MathF.Floor(offset / RowHeight) - Overscan);
        var last = Math.Min(
            _items.Length,
            (int)MathF.Ceiling((offset + viewportHeight) / RowHeight) + Overscan
        );
        Realize(first, last);
    }

    private void Realize(int first, int last)
    {
        _updating = true;
        try
        {
            var wanted = new HashSet<TKey>(_keys[first..last]);
            var retained = new Dictionary<TKey, Element>(_entries);
            var provisional = new List<(TKey Key, Element Element, CompositionContext Context)>();
            try
            {
                for (var index = first; index < last; index++)
                {
                    if (retained.ContainsKey(_keys[index]))
                        continue;
                    var context = new CompositionContext(_composition, Region, Theme);
                    provisional.Add((_keys[index], null!, context));
                    var entry = context.Run(() => _content!(_items[index], context));
                    context.Validate(entry);
                    if (!entry.HasPresentation)
                        entry.Present(Theme);
                    entry.UpdateControl(LayoutProperties.Height, RowHeight);
                    entry.UpdateControl(LayoutProperties.VirtualRowIndex, index);
                    provisional[^1] = (_keys[index], entry, context);
                }
            }
            catch (Exception error)
            {
                var errors = new List<Exception> { error };
                foreach (var entry in provisional)
                    try
                    {
                        entry.Context.Dispose();
                    }
                    catch (Exception cleanup)
                    {
                        errors.Add(cleanup);
                    }
                Composition.ThrowAll(errors, "Virtualized region factory failed.");
                throw;
            }

            var next = new Dictionary<TKey, Element>();
            var ordered = new List<Element>(last - first);
            for (var index = first; index < last; index++)
            {
                var key = _keys[index];
                var entry = retained.TryGetValue(key, out var current)
                    ? current
                    : provisional
                        .Single(value => EqualityComparer<TKey>.Default.Equals(value.Key, key))
                        .Element;
                entry.UpdateControl(LayoutProperties.VirtualRowIndex, index);
                next.Add(key, entry);
                ordered.Add(entry);
            }
            foreach (var entry in provisional)
                entry.Context.Complete();
            var departed = _entries
                .Where(pair => !wanted.Contains(pair.Key))
                .Select(pair => pair.Value)
                .ToArray();
            _entries = next;
            Region.ReplaceChildren(ordered);
            foreach (var entry in provisional)
                entry.Context.Dispose();
            List<Exception>? cleanupErrors = null;
            foreach (var entry in departed)
                try
                {
                    entry.Dispose();
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
                entry.Dispose();
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
}
