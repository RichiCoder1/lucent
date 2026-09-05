using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Lucent.Core;

/// <summary>The only factory context; it creates unattached content until a region commits it.</summary>
public sealed class CompositionContext : IDisposable
{
    private readonly Composition _composition;
    private readonly Element _parent;
    private Element? _root;
    private readonly List<Element> _created = [];
    private bool _committed;
    private bool _disposed;
    private List<Action>? _rollback;

    internal CompositionContext(Composition composition, Element parent, ThemeContext? theme = null)
    {
        _composition = composition;
        _parent = parent;
        _theme = theme;
    }

    /// <summary>The root mount's theme. Nested recipes can observe it but cannot replace it.</summary>
    public ThemeContext Theme =>
        _theme
        ?? throw new InvalidOperationException("This composition context has no root mount theme.");
    private readonly ThemeContext? _theme;

    /// <summary>Creates the one root supplied by this conditional or keyed-item factory.</summary>
    public Element Element(string name)
    {
        _composition.ThrowIfBehaviorAttachment();
        ThrowIfInactive();
        if (_root is not null)
            throw new InvalidOperationException(
                "A content factory creates exactly one root element."
            );
        _root = _composition.Create(_parent, name, attach: false, this);
        return _root;
    }

    /// <summary>Adds fixed authored structure below a factory-created element.</summary>
    public Element Child(Element parent, string name)
    {
        _composition.ThrowIfBehaviorAttachment();
        ThrowIfInactive();
        var root = Root;
        if (!root.IsAncestorOf(parent))
            throw new ArgumentException(
                "A content factory can only add children below its provisional root.",
                nameof(parent)
            );
        return _composition.Create(parent, name, attach: true, this);
    }

    /// <summary>Atomically mounts one nested recipe root below a provisional element.</summary>
    public Element Mount(Element parent, Func<CompositionContext, Element> content)
    {
        ThrowIfActiveFactory();
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(content);
        if (!Root.IsAncestorOf(parent))
            throw new ArgumentException(
                "A content factory can only mount below its provisional root.",
                nameof(parent)
            );
        return _composition.MountCore(parent, Theme, content);
    }

    /// <summary>Mounts one reusable component recipe below a provisional element.</summary>
    public Element Mount(Element parent, ComponentRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return Mount(parent, recipe.Mount);
    }

    /// <summary>Mounts ordered content below a provisional element without adding a wrapper element.</summary>
    public void Mount(Element parent, ComponentContent content)
    {
        ThrowIfActiveFactory();
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(content);
        if (!Root.IsAncestorOf(parent))
            throw new ArgumentException(
                "A content factory can only mount below its provisional root.",
                nameof(parent)
            );
        foreach (var recipe in content)
            recipe.Mount(this, parent);
    }

    /// <summary>Creates a retained conditional region below a provisional element.</summary>
    public ConditionalRegion When(
        Element parent,
        string name,
        Func<bool> active,
        Func<CompositionContext, Element> content
    )
    {
        ThrowIfActiveFactory();
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(content);
        if (!Root.IsAncestorOf(parent))
            throw new ArgumentException(
                "A content factory can only add regions below its provisional root.",
                nameof(parent)
            );
        return new ConditionalRegion(_composition, parent, name, active, content, this, Theme);
    }

    /// <summary>Creates a retained branch region selected by one reactive evaluation.</summary>
    public ConditionalRegion Switch(Element parent, string name, Func<ConditionalChoice> select)
    {
        ThrowIfActiveFactory();
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(select);
        if (!Root.IsAncestorOf(parent))
            throw new ArgumentException(
                "A content factory can only add regions below its provisional root.",
                nameof(parent)
            );
        return new ConditionalRegion(_composition, parent, name, select, this, Theme);
    }

    /// <summary>Creates a retained keyed region below a provisional element.</summary>
    public KeyedRegion<TKey, TItem> ForEach<TKey, TItem>(
        Element parent,
        string name,
        Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key,
        Func<CurrentItem<TItem>, CompositionContext, Element> content
    )
        where TKey : notnull
    {
        ThrowIfActiveFactory();
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(content);
        if (!Root.IsAncestorOf(parent))
            throw new ArgumentException(
                "A content factory can only add regions below its provisional root.",
                nameof(parent)
            );
        return new KeyedRegion<TKey, TItem>(
            _composition,
            parent,
            name,
            source,
            key,
            content,
            this,
            Theme
        );
    }

    internal VirtualizedRegion<TKey, TItem> Virtualize<TKey, TItem>(
        Element viewport,
        string name,
        Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key,
        Func<CurrentItem<TItem>, CompositionContext, Element> content,
        float rowHeight
    )
        where TKey : notnull
    {
        ThrowIfActiveFactory();
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(content);
        if (!Root.IsAncestorOf(viewport))
            throw new ArgumentException(
                "A content factory can only add regions below its provisional root.",
                nameof(viewport)
            );
        return new VirtualizedRegion<TKey, TItem>(
            _composition,
            viewport,
            name,
            source,
            key,
            content,
            rowHeight,
            Theme,
            this
        );
    }

    internal Element Commit(Element created)
    {
        var root = Validate(created);
        PromoteRollback();
        _committed = true;
        return root;
    }

    internal Element Validate(Element created)
    {
        ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(created);
        var root = Root;
        if (!ReferenceEquals(created, root))
            throw new InvalidOperationException(
                "A content factory must return its created root element."
            );
        if (_created.Any(element => element.IsDisposed || element.Scope.IsDisposed))
            throw new InvalidOperationException("Disposed content cannot be committed.");
        return root;
    }

    internal void Complete()
    {
        PromoteRollback();
        _committed = true;
    }

    internal bool IsCommitted => _committed;

    internal Element Root
    {
        get
        {
            ThrowIfInactive();
            return _root
                ?? throw new InvalidOperationException(
                    "A content factory must create and return its root element."
                );
        }
    }

    /// <summary>Releases this object's retained resources and owned reactive lifetime.</summary>
    public void Dispose()
    {
        _composition.CheckThread();
        _composition.ThrowIfBehaviorAttachment();
        if (_disposed)
            return;
        _disposed = true;
        if (_committed)
        {
            _rollback = null;
            return;
        }
        List<Exception>? errors = null;
        try
        {
            if (_root is not null)
                _composition.RunOwnedCleanup(_root, _root.Dispose);
        }
        catch (Exception exception)
        {
            errors = [exception];
        }
        if (_rollback is not null)
            foreach (var cleanup in _rollback.AsEnumerable().Reverse())
                try
                {
                    _composition.RunFactoryRollback(cleanup);
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
        _rollback = null;
        Composition.ThrowAll(errors, "Composition factory rollback failed.");
    }

    internal T Run<T>(Func<T> factory)
    {
        ThrowIfInactive();
        return _composition.RunFactory(this, factory);
    }

    internal void Record(Element element) => _created.Add(element);

    internal void RegisterRollback(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        if (!_committed && !_disposed)
            (_rollback ??= []).Add(cleanup);
    }

    private void PromoteRollback()
    {
        if (_rollback is null)
            return;
        foreach (var cleanup in _rollback)
            _composition.RegisterFactoryRollback(cleanup);
        _rollback = null;
    }

    internal Element RecipeElement(string kind, string? name)
    {
        ThrowIfActiveFactory();
        if (_root is not null)
            throw new InvalidOperationException(
                "A content factory creates exactly one root element."
            );
        var ordinal = _parent.NextRecipeOrdinal();
        _root = _composition.Create(
            _parent,
            name ?? kind + "-" + ordinal.ToString(CultureInfo.InvariantCulture),
            attach: false,
            this
        );
        return _root;
    }

    internal Element Parent => _parent;

    internal bool Contains(Element parent) => _root is not null && _root.IsAncestorOf(parent);

    private void ThrowIfActiveFactory()
    {
        ThrowIfInactive();
        if (!ReferenceEquals(_composition.Factory, this))
            throw new InvalidOperationException(
                "Composition context operations are only available while their recipe is executing."
            );
    }

    private void ThrowIfInactive()
    {
        _composition.CheckThread();
        ObjectDisposedException.ThrowIf(_disposed, typeof(CompositionContext));
    }
}
