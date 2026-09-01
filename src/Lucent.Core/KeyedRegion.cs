using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Lucent.Core;

/// <summary>
/// A retained keyed collection region. It preserves entries by exact key, not by position.
/// Keys must keep stable, side-effect-free equality and hash behavior while mounted.
/// </summary>
public sealed class KeyedRegion<TKey, TItem> : IDisposable
    where TKey : notnull
{
    private readonly Composition _composition;
    private Func<IEnumerable<TItem>>? _source;
    private Func<TItem, TKey>? _key;
    private Func<TItem, CompositionContext, Element>? _content;
    private readonly ReactiveEffect _effect;
    private Dictionary<TKey, Element> _entries = [];
    private bool _updating;

    internal KeyedRegion(
        Composition composition,
        Element parent,
        string name,
        Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key,
        Func<TItem, CompositionContext, Element> content,
        CompositionContext? factory = null,
        ThemeContext? theme = null
    )
    {
        _composition = composition;
        Region = composition.Create(parent, name, attach: true, factory);
        Theme = theme;
        _source = source;
        _key = key;
        _content = content;
        Region.Scope.Own(this);
        _effect = Region.Scope.Effect(Refresh, name + ".items");
    }

    /// <summary>Gets the persistent region element that owns mounted content.</summary>
    public Element Region { get; }

    /// <summary>Gets the currently mounted keyed item roots in source order.</summary>
    public IReadOnlyList<Element> Items => Region.Children;

    /// <summary>Gets whether this retained owner has released its children and reactive resources.</summary>
    public bool IsDisposed { get; private set; }
    private ThemeContext? Theme { get; }

    /// <summary>Re-evaluates the source. Usual callers let the owned effect invoke this.</summary>
    public void Refresh()
    {
        _composition.CheckThread();
        _composition.ThrowIfBehaviorAttachment();
        _composition.RejectForeignFactory(Region);
        Update(_source!());
    }

    /// <summary>Reconciles the retained region with the supplied current source.</summary>
    public void Update(IEnumerable<TItem> items)
    {
        _composition.CheckThread();
        _composition.ThrowIfBehaviorAttachment();
        ArgumentNullException.ThrowIfNull(items);
        _composition.RejectForeignFactory(Region);
        if (IsDisposed)
            return;
        if (_updating)
            throw new InvalidOperationException("A keyed region cannot update reentrantly.");
        _updating = true;
        try
        {
            var next = items.ToArray();
            var keys = new TKey[next.Length];
            var unique = new HashSet<TKey>();
            for (var index = 0; index < next.Length; index++)
            {
                var key = _key!(next[index]);
                if (!unique.Add(key))
                    throw new ArgumentException("Keyed region keys must be unique.", nameof(items));
                keys[index] = key;
            }

            RetireDisposedEntries();
            var retained = new Dictionary<TKey, Element>(_entries);
            var provisional = new Dictionary<TKey, (Element Element, CompositionContext Context)>();
            var provisionalOrder = new List<(Element Element, CompositionContext Context)>();
            Element[] ordered = null!;
            Dictionary<TKey, Element> nextEntries = null!;
            Element[] departed = null!;
            try
            {
                for (var index = 0; index < next.Length; index++)
                {
                    if (retained.ContainsKey(keys[index]))
                        continue;
                    var context = new CompositionContext(_composition, Region, Theme);
                    provisionalOrder.Add((null!, context));
                    var created = context.Run(() => _content!(next[index], context));
                    ObjectDisposedException.ThrowIf(
                        IsDisposed || Region.IsDisposed,
                        typeof(KeyedRegion<TKey, TItem>)
                    );
                    provisional[keys[index]] = (created, context);
                    provisionalOrder[^1] = (created, context);
                }

                foreach (var entry in provisionalOrder)
                    entry.Context.Validate(entry.Element);
                foreach (var pair in retained)
                    if (
                        pair.Value.IsDisposed
                        || pair.Value.Scope.IsDisposed
                        || !_entries.TryGetValue(pair.Key, out var current)
                        || !ReferenceEquals(current, pair.Value)
                    )
                        throw new InvalidOperationException(
                            "A retained keyed entry was disposed during update."
                        );

                ordered = keys.Select(key =>
                        retained.TryGetValue(key, out var entry) ? entry : provisional[key].Element
                    )
                    .ToArray();
                nextEntries = new Dictionary<TKey, Element>();
                for (var index = 0; index < keys.Length; index++)
                    nextEntries.Add(keys[index], ordered[index]);
                foreach (var pair in provisional)
                {
                    var key = pair.Key;
                    var entry = pair.Value.Element;
                    entry.OnDisposed(() => RemoveEntry(key, entry));
                }
                departed = retained
                    .Where(pair => !unique.Contains(pair.Key))
                    .Select(pair => pair.Value)
                    .ToArray();
                foreach (var entry in provisionalOrder)
                    entry.Context.Complete();
            }
            catch (Exception error)
            {
                var factoryErrors = new List<Exception> { error };
                foreach (var entry in provisionalOrder)
                    try
                    {
                        entry.Context.Dispose();
                    }
                    catch (Exception cleanup)
                    {
                        factoryErrors.Add(cleanup);
                    }
                Composition.ThrowAll(factoryErrors, "Keyed region factory failed.");
                throw;
            }

            _entries = nextEntries;
            Region.ReplaceChildren(ordered);
            foreach (var entry in provisionalOrder)
                entry.Context.Dispose();

            List<Exception>? errors = null;
            foreach (var entry in departed)
                try
                {
                    entry.Dispose();
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
            Composition.ThrowAll(errors, "Keyed region cleanup failed.");
        }
        finally
        {
            _updating = false;
        }
    }

    private void RetireDisposedEntries()
    {
        foreach (var pair in _entries.ToArray())
            if (pair.Value.IsDisposed || pair.Value.Scope.IsDisposed)
                RemoveEntry(pair.Key, pair.Value);
    }

    private void RemoveEntry(TKey key, Element entry)
    {
        if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            _entries.Remove(key);
    }

    /// <summary>Releases this object's retained resources and owned reactive lifetime.</summary>
    public void Dispose()
    {
        _composition.CheckThread();
        _composition.ThrowIfBehaviorAttachment();
        _composition.RejectForeignFactory(Region);
        if (IsDisposed)
            return;
        IsDisposed = true;
        List<Exception>? errors = null;
        try
        {
            _effect.Dispose();
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
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
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        _source = null;
        _key = null;
        _content = null;
        Region.Scope.Detach(this);
        Composition.ThrowAll(errors, "Keyed region cleanup failed.");
    }
}
