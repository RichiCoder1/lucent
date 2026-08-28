using System.Globalization;
using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Lucent.Core;

/// <summary>Owns a retained structural tree and the scopes of its mounted elements.</summary>
public sealed class Composition : IDisposable
{
    private readonly ReactiveGraph _graph;
    private long _nextElementId;
    private CompositionContext? _factory;

    public Composition(ReactiveGraph graph, string name)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        ReactiveGraph.ValidateName(name, nameof(name));
        var scope = graph.CreateScope(name);
        Root = new Element(this, null, scope, NextId(), name);
    }

    /// <summary>The stable root element for this composition.</summary>
    public Element Root { get; }
    public bool IsDisposed { get; private set; }

    /// <summary>Adds fixed authored structure below an already-mounted element.</summary>
    public Element Child(Element parent, string name)
    {
        ThrowIfFactoryCreation();
        ThrowIfDisposed();
        return Create(parent, name, attach: true);
    }

    /// <summary>Creates a zero-or-one structural region whose content follows <paramref name="active"/>.</summary>
    public ConditionalRegion When(Element parent, string name, Func<bool> active, Func<CompositionContext, Element> content)
    {
        ThrowIfFactoryCreation();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(content);
        return new ConditionalRegion(this, parent, name, active, content);
    }

    /// <summary>
    /// Creates a keyed structural region whose source is tracked by the reactive graph.
    /// Keys must keep stable, side-effect-free equality and hash behavior while mounted.
    /// </summary>
    public KeyedRegion<TKey, TItem> ForEach<TKey, TItem>(Element parent, string name, Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key, Func<TItem, CompositionContext, Element> content) where TKey : notnull
    {
        ThrowIfFactoryCreation();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(content);
        return new KeyedRegion<TKey, TItem>(this, parent, name, source, key, content);
    }

    /// <summary>Returns active structure and stable identities without application values.</summary>
    public string Dump()
    {
        _graph.CheckThread();
        ThrowIfDisposed();
        var dump = new StringBuilder("composition\n");
        Append(Root, null, dump);
        return dump.ToString();
    }

    internal Element Create(Element parent, string name, bool attach, CompositionContext? factory = null)
    {
        _graph.CheckThread();
        ThrowIfDisposed();
        if (_factory is not null && !ReferenceEquals(_factory, factory))
            throw new InvalidOperationException("Structural creation must use the active composition context.");
        ArgumentNullException.ThrowIfNull(parent);
        ReactiveGraph.ValidateName(name, nameof(name));
        if (!ReferenceEquals(parent.Composition, this)) throw new ArgumentException("The parent belongs to another composition.", nameof(parent));
        parent.ThrowIfDisposed();
        var scope = parent.Scope.CreateChild(name);
        var element = new Element(this, parent, scope, NextId(), name);
        parent.Scope.Own(element);
        if (attach) parent.Attach(element);
        factory?.Record(element);
        return element;
    }

    internal T RunFactory<T>(CompositionContext context, Func<T> factory)
    {
        _graph.CheckThread();
        if (_factory is not null) throw new InvalidOperationException("Content factories cannot nest.");
        _factory = context;
        try { return factory(); }
        finally { _factory = null; }
    }

    internal void ThrowIfDisposed()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(Composition));
    }

    internal void CheckThread() => _graph.CheckThread();

    private void ThrowIfFactoryCreation()
    {
        _graph.CheckThread();
        if (_factory is not null) throw new InvalidOperationException("Structural creation must use the active composition context.");
    }

    public void Dispose()
    {
        _graph.CheckThread();
        if (IsDisposed) return;
        IsDisposed = true;
        Root.Dispose();
    }

    private long NextId() => checked(++_nextElementId);

    private static void Append(Element element, Element? parent, StringBuilder dump)
    {
        if (element.IsDisposed) return;
        dump.Append("element ").Append(element.Id.ToString(CultureInfo.InvariantCulture))
            .Append(" scope=").Append(element.Scope.Id.ToString(CultureInfo.InvariantCulture))
            .Append(" name=").Append(Quote(element.Name))
            .Append(" parent=").Append(parent?.Id.ToString(CultureInfo.InvariantCulture) ?? "-").Append('\n');
        foreach (var child in element.Children) Append(child, element, dump);
    }

    internal static void ThrowAll(List<Exception>? errors, string message)
    {
        if (errors is null or { Count: 0 }) return;
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        throw new AggregateException(message, errors);
    }

    private static string Quote(string value) => '"' + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + '"';
}

/// <summary>A stable retained structural identity. It intentionally has no visual or platform state.</summary>
public sealed class Element : IDisposable
{
    private readonly List<Element> _children = [];
    private readonly ReadOnlyCollection<Element> _childrenView;
    private Element? _parent;
    private Action? _disposed;

    internal Element(Composition composition, Element? parent, ReactiveScope scope, long id, string name)
    {
        Composition = composition;
        _parent = parent;
        _childrenView = _children.AsReadOnly();
        Scope = scope;
        Id = id;
        Name = name;
        Scope.OnDispose(Dispose);
    }

    public long Id { get; }
    public string Name { get; }
    public ReactiveScope Scope { get; }
    public IReadOnlyList<Element> Children => _childrenView;
    public bool IsDisposed { get; private set; }
    internal Composition Composition { get; }

    internal void Attach(Element child)
    {
        ThrowIfDisposed();
        if (!ReferenceEquals(child._parent, this)) throw new InvalidOperationException("An element can only be attached to its owning parent.");
        _children.Add(child);
    }

    internal void ReplaceChildren(IReadOnlyList<Element> children)
    {
        ThrowIfDisposed();
        _children.Clear();
        _children.AddRange(children);
    }

    internal void Detach(Element child) => _children.Remove(child);

    internal void OnDisposed(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (IsDisposed) { callback(); return; }
        _disposed += callback;
    }

    internal bool IsAncestorOf(Element child)
    {
        for (Element? current = child; current is not null; current = current._parent)
            if (ReferenceEquals(current, this)) return true;
        return false;
    }

    internal void ThrowIfDisposed()
    {
        if (IsDisposed) throw new ObjectDisposedException(Name);
    }

    public void Dispose()
    {
        Composition.CheckThread();
        if (IsDisposed) return;
        IsDisposed = true;
        var children = _children.ToArray();
        _children.Clear();
        List<Exception>? errors = null;
        for (var index = children.Length - 1; index >= 0; index--)
        {
            try { children[index].Dispose(); }
            catch (Exception exception) { (errors ??= []).Add(exception); }
        }
        _parent?.Scope.Detach(this);
        _parent?.Detach(this);
        _parent = null;
        try { Scope.Dispose(); }
        catch (Exception exception) { (errors ??= []).Add(exception); }
        var disposed = _disposed;
        _disposed = null;
        try { disposed?.Invoke(); }
        catch (Exception exception) { (errors ??= []).Add(exception); }
        Composition.ThrowAll(errors, "Element cleanup failed.");
    }
}

/// <summary>The only factory context; it creates unattached content until a region commits it.</summary>
public sealed class CompositionContext : IDisposable
{
    private readonly Composition _composition;
    private readonly Element _parent;
    private Element? _root;
    private readonly List<Element> _created = [];
    private bool _committed;
    private bool _disposed;

    internal CompositionContext(Composition composition, Element parent) { _composition = composition; _parent = parent; }

    /// <summary>Creates the one root supplied by this conditional or keyed-item factory.</summary>
    public Element Element(string name)
    {
        ThrowIfInactive();
        if (_root is not null) throw new InvalidOperationException("A content factory creates exactly one root element.");
        _root = _composition.Create(_parent, name, attach: false, this);
        return _root;
    }

    /// <summary>Adds fixed authored structure below a factory-created element.</summary>
    public Element Child(Element parent, string name)
    {
        ThrowIfInactive();
        var root = Root;
        if (!root.IsAncestorOf(parent)) throw new ArgumentException("A content factory can only add children below its provisional root.", nameof(parent));
        return _composition.Create(parent, name, attach: true, this);
    }

    internal Element Commit(Element created)
    {
        var root = Validate(created);
        _committed = true;
        return root;
    }

    internal Element Validate(Element created)
    {
        ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(created);
        var root = Root;
        if (!ReferenceEquals(created, root)) throw new InvalidOperationException("A content factory must return its created root element.");
        if (_created.Any(element => element.IsDisposed || element.Scope.IsDisposed))
            throw new InvalidOperationException("Disposed content cannot be committed.");
        return root;
    }

    internal void Complete() => _committed = true;

    internal Element Root
    {
        get
        {
            ThrowIfInactive();
            return _root ?? throw new InvalidOperationException("A content factory must create and return its root element.");
        }
    }

    public void Dispose()
    {
        _composition.CheckThread();
        if (_disposed) return;
        _disposed = true;
        if (!_committed) _root?.Dispose();
    }

    internal T Run<T>(Func<T> factory)
    {
        ThrowIfInactive();
        return _composition.RunFactory(this, factory);
    }

    internal void Record(Element element) => _created.Add(element);

    private void ThrowIfInactive()
    {
        _composition.CheckThread();
        if (_disposed) throw new ObjectDisposedException(nameof(CompositionContext));
    }
}

/// <summary>A retained zero-or-one child region.</summary>
public sealed class ConditionalRegion : IDisposable
{
    private readonly Composition _composition;
    private Func<bool>? _active;
    private Func<CompositionContext, Element>? _content;
    private readonly ReactiveEffect _effect;
    private Element? _child;
    private bool _updating;

    internal ConditionalRegion(Composition composition, Element parent, string name, Func<bool> active, Func<CompositionContext, Element> content)
    {
        _composition = composition;
        Region = composition.Child(parent, name);
        _active = active;
        _content = content;
        Region.Scope.Own(this);
        _effect = Region.Scope.Effect(Refresh, name + ".condition");
    }

    public Element Region { get; }
    public Element? Active => _child;
    public bool IsDisposed { get; private set; }

    /// <summary>Re-evaluates the condition. Usual callers let the owned effect invoke this.</summary>
    public void Refresh() => Update(_active!());

    public void Update(bool active)
    {
        _composition.CheckThread();
        if (IsDisposed) return;
        if (_updating) throw new InvalidOperationException("A conditional region cannot update reentrantly.");
        _updating = true;
        try
        {
            if (_child?.IsDisposed == true) _child = null;
            if (active == (_child is not null)) return;
            if (!active)
            {
                var departed = _child!;
                _child = null;
                Region.ReplaceChildren([]);
                departed.Dispose();
                return;
            }

            var context = new CompositionContext(_composition, Region);
            Element created;
            try
            {
                created = context.Run(() => _content!(context));
                if (IsDisposed || Region.IsDisposed) throw new ObjectDisposedException(nameof(ConditionalRegion));
                context.Commit(created);
            }
            catch (Exception error)
            {
                var errors = new List<Exception> { error };
                try { context.Dispose(); } catch (Exception cleanup) { errors.Add(cleanup); }
                Composition.ThrowAll(errors, "Conditional region factory failed.");
                return;
            }
            context.Dispose();
            _child = created;
            created.OnDisposed(() =>
            {
                if (ReferenceEquals(_child, created)) _child = null;
            });
            Region.ReplaceChildren([created]);
        }
        finally { _updating = false; }
    }

    public void Dispose()
    {
        _composition.CheckThread();
        if (IsDisposed) return;
        IsDisposed = true;
        List<Exception>? errors = null;
        try { _effect.Dispose(); } catch (Exception exception) { (errors ??= []).Add(exception); }
        var departed = _child;
        _child = null;
        if (!Region.IsDisposed) Region.ReplaceChildren([]);
        try { departed?.Dispose(); } catch (Exception exception) { (errors ??= []).Add(exception); }
        _active = null!;
        _content = null!;
        Region.Scope.Detach(this);
        Composition.ThrowAll(errors, "Conditional region cleanup failed.");
    }
}

/// <summary>
/// A retained keyed collection region. It preserves entries by exact key, not by position.
/// Keys must keep stable, side-effect-free equality and hash behavior while mounted.
/// </summary>
public sealed class KeyedRegion<TKey, TItem> : IDisposable where TKey : notnull
{
    private readonly Composition _composition;
    private Func<IEnumerable<TItem>>? _source;
    private Func<TItem, TKey>? _key;
    private Func<TItem, CompositionContext, Element>? _content;
    private readonly ReactiveEffect _effect;
    private Dictionary<TKey, Element> _entries = [];
    private bool _updating;

    internal KeyedRegion(Composition composition, Element parent, string name, Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key, Func<TItem, CompositionContext, Element> content)
    {
        _composition = composition;
        Region = composition.Child(parent, name);
        _source = source;
        _key = key;
        _content = content;
        Region.Scope.Own(this);
        _effect = Region.Scope.Effect(Refresh, name + ".items");
    }

    public Element Region { get; }
    public IReadOnlyList<Element> Items => Region.Children;
    public bool IsDisposed { get; private set; }

    /// <summary>Re-evaluates the source. Usual callers let the owned effect invoke this.</summary>
    public void Refresh() => Update(_source!());

    public void Update(IEnumerable<TItem> items)
    {
        _composition.CheckThread();
        ArgumentNullException.ThrowIfNull(items);
        if (IsDisposed) return;
        if (_updating) throw new InvalidOperationException("A keyed region cannot update reentrantly.");
        _updating = true;
        try
        {
            var next = items.ToArray();
            var keys = new TKey[next.Length];
            var unique = new HashSet<TKey>();
            for (var index = 0; index < next.Length; index++)
            {
                var key = _key!(next[index]);
                if (!unique.Add(key)) throw new ArgumentException("Keyed region keys must be unique.", nameof(items));
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
                    if (retained.ContainsKey(keys[index])) continue;
                    var context = new CompositionContext(_composition, Region);
                    provisionalOrder.Add((null!, context));
                    var created = context.Run(() => _content!(next[index], context));
                    if (IsDisposed || Region.IsDisposed) throw new ObjectDisposedException(nameof(KeyedRegion<TKey, TItem>));
                    provisional[keys[index]] = (created, context);
                    provisionalOrder[^1] = (created, context);
                }

                foreach (var entry in provisionalOrder) entry.Context.Validate(entry.Element);
                foreach (var pair in retained)
                    if (pair.Value.IsDisposed || pair.Value.Scope.IsDisposed ||
                        !_entries.TryGetValue(pair.Key, out var current) || !ReferenceEquals(current, pair.Value))
                        throw new InvalidOperationException("A retained keyed entry was disposed during update.");

                ordered = keys.Select(key => retained.TryGetValue(key, out var entry) ? entry : provisional[key].Element).ToArray();
                nextEntries = new Dictionary<TKey, Element>();
                for (var index = 0; index < keys.Length; index++) nextEntries.Add(keys[index], ordered[index]);
                foreach (var pair in provisional)
                {
                    var key = pair.Key;
                    var entry = pair.Value.Element;
                    entry.OnDisposed(() => RemoveEntry(key, entry));
                }
                departed = retained.Where(pair => !unique.Contains(pair.Key)).Select(pair => pair.Value).ToArray();
                foreach (var entry in provisionalOrder) entry.Context.Complete();
            }
            catch (Exception error)
            {
                var factoryErrors = new List<Exception> { error };
                foreach (var entry in provisionalOrder)
                    try { entry.Context.Dispose(); } catch (Exception cleanup) { factoryErrors.Add(cleanup); }
                Composition.ThrowAll(factoryErrors, "Keyed region factory failed.");
                throw;
            }

            _entries = nextEntries;
            Region.ReplaceChildren(ordered);
            foreach (var entry in provisionalOrder) entry.Context.Dispose();

            List<Exception>? errors = null;
            foreach (var entry in departed)
                try { entry.Dispose(); } catch (Exception exception) { (errors ??= []).Add(exception); }
            Composition.ThrowAll(errors, "Keyed region cleanup failed.");
        }
        finally { _updating = false; }
    }

    private void RetireDisposedEntries()
    {
        foreach (var pair in _entries.ToArray())
            if (pair.Value.IsDisposed || pair.Value.Scope.IsDisposed) RemoveEntry(pair.Key, pair.Value);
    }

    private void RemoveEntry(TKey key, Element entry)
    {
        if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry)) _entries.Remove(key);
    }

    public void Dispose()
    {
        _composition.CheckThread();
        if (IsDisposed) return;
        IsDisposed = true;
        List<Exception>? errors = null;
        try { _effect.Dispose(); } catch (Exception exception) { (errors ??= []).Add(exception); }
        var entries = _entries.Values.ToArray();
        _entries.Clear();
        if (!Region.IsDisposed) Region.ReplaceChildren([]);
        foreach (var entry in entries.Reverse())
            try { entry.Dispose(); } catch (Exception exception) { (errors ??= []).Add(exception); }
        _source = null;
        _key = null;
        _content = null;
        Region.Scope.Detach(this);
        Composition.ThrowAll(errors, "Keyed region cleanup failed.");
    }
}
