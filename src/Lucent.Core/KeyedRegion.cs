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
    private Func<CurrentItem<TItem>, CompositionContext, Element>? _content;
    private readonly ReactiveEffect _effect;
    private Dictionary<TKey, Entry> _entries = [];
    private long _nextEntryId;
    private bool _updating;

    internal KeyedRegion(
        Composition composition,
        Element parent,
        string name,
        Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key,
        Func<CurrentItem<TItem>, CompositionContext, Element> content,
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
            var retained = new Dictionary<TKey, Entry>(_entries);
            var provisional = new Dictionary<TKey, (Entry Entry, CompositionContext Context)>();
            var provisionalOrder =
                new List<(Entry? Entry, ReactiveScope Scope, CompositionContext Context)>();
            Entry[] ordered = null!;
            Dictionary<TKey, Entry> nextEntries = null!;
            Entry[] departed = null!;
            (Entry Entry, TItem Value)[] retainedUpdates = null!;
            try
            {
                for (var index = 0; index < next.Length; index++)
                {
                    if (retained.ContainsKey(keys[index]))
                        continue;
                    var itemScope = Region.Scope.CreateChild(
                        Region.Name
                            + ".current-item-"
                            + checked(++_nextEntryId).ToString(CultureInfo.InvariantCulture)
                    );
                    var current = itemScope.CurrentItemForFramework(
                        next[index],
                        itemScope.Name + ".value"
                    );
                    var context = new CompositionContext(_composition, Region, Theme);
                    provisionalOrder.Add((null, itemScope, context));
                    var created = context.Run(() => _content!(current, context));
                    ObjectDisposedException.ThrowIf(
                        IsDisposed || Region.IsDisposed,
                        typeof(KeyedRegion<TKey, TItem>)
                    );
                    var entry = new Entry(created, itemScope, current);
                    provisional[keys[index]] = (entry, context);
                    provisionalOrder[^1] = (entry, itemScope, context);
                }

                foreach (var value in provisionalOrder)
                    value.Context.Validate(value.Entry!.Root);
                foreach (var pair in retained)
                    if (
                        pair.Value.Root.IsDisposed
                        || pair.Value.Root.Scope.IsDisposed
                        || pair.Value.Scope.IsDisposed
                        || !_entries.TryGetValue(pair.Key, out var current)
                        || !ReferenceEquals(current, pair.Value)
                    )
                        throw new InvalidOperationException(
                            "A retained keyed entry was disposed during update."
                        );

                ordered = keys.Select(key =>
                        retained.TryGetValue(key, out var entry) ? entry : provisional[key].Entry
                    )
                    .ToArray();
                nextEntries = new Dictionary<TKey, Entry>();
                for (var index = 0; index < keys.Length; index++)
                    nextEntries.Add(keys[index], ordered[index]);
                foreach (var pair in provisional)
                {
                    var key = pair.Key;
                    var entry = pair.Value.Entry;
                    entry.Root.OnDisposed(() => ReleaseEntry(key, entry));
                }
                departed = retained
                    .Where(pair => !unique.Contains(pair.Key))
                    .Select(pair => pair.Value)
                    .ToArray();
                retainedUpdates = Enumerable
                    .Range(0, next.Length)
                    .Where(index => retained.ContainsKey(keys[index]))
                    .Select(index => (nextEntries[keys[index]], next[index]))
                    .ToArray();
                foreach (var value in provisionalOrder)
                    value.Context.Complete();
            }
            catch (Exception error)
            {
                var factoryErrors = new List<Exception> { error };
                foreach (var value in provisionalOrder)
                {
                    try
                    {
                        value.Context.Dispose();
                    }
                    catch (Exception cleanup)
                    {
                        factoryErrors.Add(cleanup);
                    }
                    try
                    {
                        value.Scope.Dispose();
                    }
                    catch (Exception cleanup)
                    {
                        factoryErrors.Add(cleanup);
                    }
                }
                Composition.ThrowAll(factoryErrors, "Keyed region factory failed.");
                throw;
            }

            _entries = nextEntries;
            Region.ReplaceChildren(ordered.Select(entry => entry.Root).ToArray());
            foreach (var value in provisionalOrder)
            {
                value.Context.Dispose();
            }

            foreach (var update in retainedUpdates)
                update.Entry.Current.Stage(update.Value);

            List<Exception>? errors = null;
            foreach (var update in retainedUpdates)
                try
                {
                    update.Entry.Current.Notify();
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
            foreach (var entry in departed)
                try
                {
                    entry.Root.Dispose();
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
            Composition.ThrowAll(errors, "Keyed region publication or cleanup failed.");
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
                entry.Root.Dispose();
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

    private sealed record Entry(Element Root, ReactiveScope Scope, CurrentItem<TItem> Current);
}
