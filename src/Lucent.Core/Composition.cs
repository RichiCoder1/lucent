using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Lucent.Core;

/// <summary>Owns a retained structural tree and the scopes of its mounted elements.</summary>
/// <remarks>Use one composition on its graph's UI thread. Mounts are transactional, and disposing the composition disposes the root, all regions, and their reactive ownership.</remarks>
public sealed class Composition : IDisposable
{
    private static long _nextEpoch;
    private readonly ReactiveGraph _graph;
    private readonly long _epoch = Interlocked.Increment(ref _nextEpoch);
    private readonly Dictionary<long, Element> _elements = [];
    private readonly InputProjectionTracker _inputProjection;
    private readonly HashSet<SemanticIdentity> _emittedSemantics = [];
    private readonly TransitionController _transitions = new();
    private long _nextElementId;
    private CompositionContext? _factory;
    private int _behaviorDepth;
    private InputRouter? _input;
    private readonly List<IVirtualizedRegion> _virtualized = [];
    private long _nextSceneGeneration;
    private long _interactionVisualGeneration;
    private long _semanticRevision;
    private readonly List<Element> _ownedCleanup = [];
    private int _factoryRollbackDepth;

    /// <summary>Initializes a composition with a stable root on the graph UI thread.</summary>
    public Composition(ReactiveGraph graph, string name)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        ReactiveGraph.ValidateName(name, nameof(name));
        var scope = graph.CreateScope(name);
        Root = new Element(this, null, scope, NextId(), name);
        _elements.Add(Root.Id, Root);
        _inputProjection = scope.Own(
            new InputProjectionTracker(graph, scope, name + ".input-projection")
        );
    }

    /// <summary>The stable root element for this composition.</summary>
    public Element Root { get; }

    /// <summary>Gets whether this retained owner has released its children and reactive resources.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Monotonic notification token for retained semantic changes; it contains no platform transport.</summary>
    public long SemanticRevision => _semanticRevision;

    /// <summary>Raised after the retained semantic tree changes and consumers should request a fresh snapshot.</summary>
    public event Action? SemanticsChanged;

    /// <summary>Composition-owned portable input, focus, and capture router.</summary>
    public InputRouter Input
    {
        get
        {
            _graph.CheckThread();
            ThrowIfDisposed();
            return _input ??= new InputRouter(this);
        }
    }
    internal ReactiveGraph Graph => _graph;
    internal ContextMenuRequest? MenuSession { get; set; }
    internal CompositionContext? Factory => _factory;
    internal long Epoch => _epoch;
    internal TransitionController Transitions => _transitions;
    internal InputRouter? InputIfCreated => _input;

    internal long NextSceneGeneration()
    {
        _graph.CheckThread();
        ThrowIfDisposed();
        return checked(++_nextSceneGeneration);
    }

    internal long LatestSceneGeneration => _nextSceneGeneration;
    internal long InputProjectionRevision => _inputProjection.Revision;
    internal long InteractionVisualGeneration => _interactionVisualGeneration;

    internal T CaptureInputProjection<T>(Func<T> project) => _inputProjection.Capture(project);

    internal T WithoutProjectionTracking<T>(Func<T> project) => _graph.Untracked(project);

    internal void InvalidateInputProjection() => _inputProjection.Invalidate();

    internal void InvalidateInteractionVisuals() =>
        _interactionVisualGeneration = checked(_interactionVisualGeneration + 1);

    /// <summary>Commits this composition's pending reactive work on its owning UI thread.</summary>
    public bool Flush()
    {
        _graph.CheckThread();
        ThrowIfBehaviorAttachment();
        ThrowIfDisposed();
        return _graph.DrainPosted();
    }

    /// <summary>Forwards worker-posted work notification without exposing a platform transport to Core.</summary>
    public event Action? WorkAvailable
    {
        add => _graph.WorkAvailable += value;
        remove => _graph.WorkAvailable -= value;
    }

    /// <summary>Advances bounded presentation samples; it queues no background work.</summary>
    public void AdvanceTransitions(int milliseconds)
    {
        _graph.CheckThread();
        ThrowIfBehaviorAttachment();
        ThrowIfDisposed();
        _transitions.Advance(milliseconds);
    }

    /// <summary>Adds fixed authored structure below an already-mounted element.</summary>
    public Element Child(Element parent, string name)
    {
        ThrowIfFactoryCreation();
        ThrowIfDisposed();
        return Create(parent, name, attach: true);
    }

    /// <summary>Atomically mounts one recipe root below <paramref name="parent"/> using the supplied theme.</summary>
    public Element Mount(
        Element parent,
        ThemeContext theme,
        Func<CompositionContext, Element> content
    )
    {
        ThrowIfFactoryCreation();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(content);
        if (!ReferenceEquals(parent.Composition, this))
            throw new ArgumentException(
                "The parent belongs to another composition.",
                nameof(parent)
            );
        parent.ThrowIfDisposed();
        if (!ReferenceEquals(theme.Graph, _graph))
            throw new ArgumentException(
                "Theme context belongs to another reactive graph.",
                nameof(theme)
            );
        if (!theme.Scope.DescendsFrom(Root.Scope))
            throw new ArgumentException(
                "Theme context must be owned by this composition.",
                nameof(theme)
            );
        theme.ValidateLive();
        return MountCore(parent, theme, content);
    }

    /// <summary>Atomically mounts one reusable component recipe below <paramref name="parent"/>.</summary>
    public Element Mount(Element parent, ThemeContext theme, ComponentRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return Mount(parent, theme, recipe.Mount);
    }

    /// <summary>Creates a zero-or-one structural region whose content follows <paramref name="active"/>.</summary>
    public ConditionalRegion When(
        Element parent,
        string name,
        Func<bool> active,
        Func<CompositionContext, Element> content
    )
    {
        ThrowIfFactoryCreation();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(content);
        return new ConditionalRegion(this, parent, name, active, content);
    }

    /// <summary>Creates one retained branch selected by a single reactive recipe evaluation.</summary>
    public ConditionalRegion Switch(Element parent, string name, Func<ConditionalChoice> select)
    {
        ThrowIfFactoryCreation();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(select);
        return new ConditionalRegion(this, parent, name, select);
    }

    /// <summary>
    /// Creates a keyed structural region whose source is tracked by the reactive graph.
    /// Keys must keep stable, side-effect-free equality and hash behavior while mounted.
    /// </summary>
    public KeyedRegion<TKey, TItem> ForEach<TKey, TItem>(
        Element parent,
        string name,
        Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key,
        Func<CurrentItem<TItem>, CompositionContext, Element> content
    )
        where TKey : notnull
    {
        ThrowIfFactoryCreation();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(content);
        return new KeyedRegion<TKey, TItem>(this, parent, name, source, key, content);
    }

    /// <summary>Creates a fixed-height keyed region whose mounted entries are derived from its containing viewport.</summary>
    internal VirtualizedRegion<TKey, TItem> Virtualize<TKey, TItem>(
        Element viewport,
        string name,
        Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key,
        Func<CurrentItem<TItem>, CompositionContext, Element> content,
        float rowHeight,
        ThemeContext theme
    )
        where TKey : notnull
    {
        ThrowIfFactoryCreation();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(content);
        return new VirtualizedRegion<TKey, TItem>(
            this,
            viewport,
            name,
            source,
            key,
            content,
            rowHeight,
            theme
        );
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

    /// <summary>Returns the portable retained semantic snapshot; no platform transport is involved.</summary>
    public SemanticSnapshot? SemanticSnapshot()
    {
        _graph.CheckThread();
        ThrowIfDisposed();
        _emittedSemantics.Clear();
        var snapshots = BuildSemantic(Root);
        var snapshot =
            snapshots.Count == 0 ? null
            : Root.HasSemantics ? snapshots.Single()
            : Root.CreateStructuralSemanticSnapshot(snapshots);
        if (snapshot is not null)
            Register(snapshot);
        return snapshot;
    }

    /// <summary>Returns a deterministic, value-free semantic diagnostic dump. The semantic matrix has no suppressions.</summary>
    public string SemanticDump()
    {
        _graph.CheckThread();
        ThrowIfDisposed();
        var snapshot = SemanticSnapshot();
        var output = new StringBuilder("semantics revision=")
            .Append(_semanticRevision.ToString(CultureInfo.InvariantCulture))
            .Append('\n');
        if (snapshot is not null)
            Append(snapshot, output);
        return output.ToString();
    }

    /// <summary>Rejects a semantic identity once its element has changed or departed.</summary>
    public bool IsCurrent(SemanticIdentity identity)
    {
        _graph.CheckThread();
        return !IsDisposed
            && identity.CompositionEpoch == _epoch
            && Find(identity.ElementId) is { } element
            && element.IsCurrent(identity);
    }

    /// <summary>Executes one declared portable semantic command on the owning UI thread.</summary>
    public SemanticCommandResult ExecuteSemanticCommand(
        SemanticIdentity identity,
        SemanticCommand command
    )
    {
        _graph.CheckThread();
        if (IsDisposed)
            return SemanticCommandResult.Stale;
        command.Validate();
        if (!IsCurrent(identity) || Find(identity.ElementId) is not { } element)
            return SemanticCommandResult.Stale;
        if (!element.SemanticEnabled())
            return SemanticCommandResult.Disabled;
        return element.ExecuteSemanticCommand(command)
            ? SemanticCommandResult.Applied
            : SemanticCommandResult.Rejected;
    }

    /// <summary>Selects one retained list item and clears every selectable sibling in its nearest semantic list.</summary>
    internal bool SelectSemantic(ElementIdentity identity)
    {
        _graph.CheckThread();
        if (
            identity.CompositionEpoch != _epoch
            || Find(identity.ElementId) is not { } target
            || !target.SemanticEnabled()
        )
            return false;
        var list = target.Parent;
        while (list is not null && list.DeclaredSemanticRole != SemanticRole.List)
            list = list.Parent;
        if (list is null)
            return target.SetSelected(true);
        foreach (var element in SemanticChildren(list))
            if (element.HasSelectableSemantics)
                element.SetSelected(ReferenceEquals(element, target));
        return true;
    }

    private static IEnumerable<Element> SemanticChildren(Element parent)
    {
        foreach (var child in parent.Children)
        {
            if (child.Participation != ElementParticipation.Visible)
                continue;
            if (child.DeclaredSemanticRole is null)
            {
                foreach (var descendant in SemanticChildren(child))
                    yield return descendant;
            }
            else
                yield return child;
        }
    }

    internal Element Create(
        Element parent,
        string name,
        bool attach,
        CompositionContext? factory = null,
        ReactiveScope? scope = null
    )
    {
        _graph.CheckThread();
        ThrowIfDisposed();
        if (_factory is not null && !ReferenceEquals(_factory, factory))
            throw new InvalidOperationException(
                "Structural creation must use the active composition context."
            );
        ArgumentNullException.ThrowIfNull(parent);
        ReactiveGraph.ValidateName(name, nameof(name));
        if (!ReferenceEquals(parent.Composition, this))
            throw new ArgumentException(
                "The parent belongs to another composition.",
                nameof(parent)
            );
        parent.ThrowIfDisposed();
        if (scope is not null)
        {
            if (!ReferenceEquals(scope.Parent, parent.Scope))
                throw new ArgumentException(
                    "The supplied element scope must be owned by the parent scope.",
                    nameof(scope)
                );
            if (!ReferenceEquals(scope.Graph, _graph))
                throw new ArgumentException(
                    "The supplied element scope belongs to another reactive graph.",
                    nameof(scope)
                );
            ObjectDisposedException.ThrowIf(scope.IsDisposed, scope);
        }
        var elementScope = scope ?? parent.Scope.CreateElementChild(name);
        var element = new Element(this, parent, elementScope, NextId(), name);
        if (scope is not null)
            scope.SetFactoryGuardTree(() => ValidateFactoryMutation(element));
        parent.Scope.OwnElement(element);
        if (attach)
            parent.Attach(element);
        factory?.Record(element);
        return element;
    }

    internal T RunFactory<T>(CompositionContext context, Func<T> factory)
    {
        _graph.CheckThread();
        ArgumentNullException.ThrowIfNull(factory);
        var prior = _factory;
        if (prior is not null && !prior.Contains(context.Parent))
            throw new InvalidOperationException(
                "Nested content factories must mount below the active provisional root."
            );
        _factory = context;
        try
        {
            return _graph.Untracked(factory);
        }
        finally
        {
            _factory = prior;
        }
    }

    internal Element MountCore(
        Element parent,
        ThemeContext theme,
        Func<CompositionContext, Element> content
    )
    {
        var context = new CompositionContext(this, parent, theme);
        try
        {
            var created = context.Run(() => content(context));
            ObjectDisposedException.ThrowIf(IsDisposed || parent.IsDisposed, typeof(Composition));
            context.Validate(created);
            parent.Attach(created);
            context.Complete();
            return created;
        }
        catch (Exception error)
        {
            var errors = new List<Exception> { error };
            try
            {
                context.Dispose();
            }
            catch (Exception cleanup)
            {
                errors.Add(cleanup);
            }
            ThrowAll(errors, "Composition mount failed.");
            throw;
        }
        finally
        {
            if (context.IsCommitted)
                context.Dispose();
        }
    }

    internal void RunBehavior(BehaviorContext context, Action attach)
    {
        _graph.CheckThread();
        if (_behaviorDepth != 0)
            throw new InvalidOperationException("Behavior attachment cannot nest.");
        _behaviorDepth++;
        try
        {
            attach();
        }
        finally
        {
            _behaviorDepth--;
        }
    }

    internal void RunBehaviorCleanup(Action cleanup)
    {
        _graph.CheckThread();
        _behaviorDepth++;
        try
        {
            cleanup();
        }
        finally
        {
            _behaviorDepth--;
        }
    }

    internal void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, typeof(Composition));
    }

    internal void CheckThread() => _graph.CheckThread();

    internal void RejectForeignFactory(Element parent)
    {
        _graph.CheckThread();
        if (
            _factoryRollbackDepth == 0
            && _factory is not null
            && !_factory.Contains(parent)
            && !IsOwnedCleanup(parent)
        )
            throw new InvalidOperationException(
                "Structural regions cannot update outside the active provisional root."
            );
    }

    internal void ValidateFactoryMutation(Element element)
    {
        _graph.CheckThread();
        if (
            _factoryRollbackDepth == 0
            && _factory is not null
            && !_factory.Contains(element)
            && !IsOwnedCleanup(element)
        )
            throw new InvalidOperationException(
                "Element mutations must remain below the active provisional root."
            );
    }

    internal void RegisterFactoryRollback(Action cleanup)
    {
        _graph.CheckThread();
        ArgumentNullException.ThrowIfNull(cleanup);
        _factory?.RegisterRollback(cleanup);
    }

    internal void RunOwnedCleanup(Element root, Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(cleanup);
        _ownedCleanup.Add(root);
        try
        {
            cleanup();
        }
        finally
        {
            _ownedCleanup.RemoveAt(_ownedCleanup.Count - 1);
        }
    }

    internal void RunFactoryRollback(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        _factoryRollbackDepth++;
        try
        {
            cleanup();
        }
        finally
        {
            _factoryRollbackDepth--;
        }
    }

    private bool IsOwnedCleanup(Element element) =>
        _ownedCleanup.Any(root => root.IsAncestorOf(element));

    internal void ThrowIfBehaviorAttachment()
    {
        if (_behaviorDepth != 0)
            throw new InvalidOperationException("Behaviors cannot configure styles.");
    }

    private void Register(SemanticSnapshot snapshot)
    {
        _emittedSemantics.Add(snapshot.Identity);
        foreach (var child in snapshot.Children)
            Register(child);
    }

    internal void InvalidateSemantics()
    {
        _emittedSemantics.Clear();
        _semanticRevision = checked(_semanticRevision + 1);
        SemanticsChanged?.Invoke();
    }

    internal bool HasVirtualizedRegions => _virtualized.Count != 0;

    internal long[] VirtualizedViewportIds =>
        _virtualized.Select(region => region.ViewportId).ToArray();

    internal void Register(IVirtualizedRegion region) => _virtualized.Add(region);

    internal void Unregister(IVirtualizedRegion region) => _virtualized.Remove(region);

    internal void RealizeVirtualized(
        LayoutViewport viewport,
        IReadOnlyDictionary<long, LayoutRect> assignedBounds
    )
    {
        _graph.CheckThread();
        ArgumentNullException.ThrowIfNull(assignedBounds);
        foreach (var region in _virtualized.ToArray())
        {
            if (!assignedBounds.TryGetValue(region.ViewportId, out var bounds))
                throw new InvalidOperationException(
                    "A virtualized region has no assigned viewport in the bounded layout pass."
                );
            region.Realize(viewport, bounds);
        }
    }

    internal Element? Find(ElementIdentity identity) =>
        identity.CompositionEpoch == _epoch ? Find(identity.ElementId) : null;

    private Element? Find(long elementId) =>
        _elements.TryGetValue(elementId, out var element) && !element.IsDisposed ? element : null;

    internal bool IsReachable(Element element) => _elements.ContainsKey(element.Id);

    internal void RegisterSubtree(Element element)
    {
        if (element.IsDisposed)
            return;
        _elements.Add(element.Id, element);
        foreach (var child in element.Children)
            RegisterSubtree(child);
    }

    internal void UnregisterSubtree(Element element)
    {
        _elements.Remove(element.Id);
        foreach (var child in element.Children)
            UnregisterSubtree(child);
    }

    internal IReadOnlyList<Element> Path(Element element)
    {
        var path = new List<Element>();
        for (Element? current = element; current is not null; current = current.Parent)
            path.Add(current);
        path.Reverse();
        return path;
    }

    internal IEnumerable<Element> Elements() => Traverse(Root);

    private static IEnumerable<Element> Traverse(Element element)
    {
        if (element.IsDisposed)
            yield break;
        yield return element;
        foreach (var child in element.Children)
        foreach (var descendant in Traverse(child))
            yield return descendant;
    }

    private void ThrowIfFactoryCreation()
    {
        _graph.CheckThread();
        if (_factory is not null)
            throw new InvalidOperationException(
                "Structural creation must use the active composition context."
            );
        if (_behaviorDepth != 0)
            throw new InvalidOperationException("Behaviors cannot create structure.");
    }

    /// <summary>Releases this object's retained resources and owned reactive lifetime.</summary>
    public void Dispose()
    {
        _graph.CheckThread();
        ThrowIfBehaviorAttachment();
        if (IsDisposed)
            return;
        List<Exception>? errors = null;
        IsDisposed = true;
        RunOwnedCleanup(
            Root,
            () =>
            {
                try
                {
                    _input?.Cleanup();
                }
                catch (Exception exception)
                {
                    errors = [exception];
                }
                try
                {
                    Root.Dispose();
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
            }
        );
        ThrowAll(errors, "Composition cleanup failed.");
    }

    private long NextId() => checked(++_nextElementId);

    private static void Append(Element element, Element? parent, StringBuilder dump)
    {
        if (element.IsDisposed)
            return;
        dump.Append("element ")
            .Append(element.Id.ToString(CultureInfo.InvariantCulture))
            .Append(" scope=")
            .Append(element.Scope.Id.ToString(CultureInfo.InvariantCulture))
            .Append(" name=")
            .Append(Quote(element.Name))
            .Append(" parent=")
            .Append(parent?.Id.ToString(CultureInfo.InvariantCulture) ?? "-")
            .Append('\n');
        element.AppendPresentationDump(dump);
        foreach (var child in element.Children)
            Append(child, element, dump);
    }

    private static List<SemanticSnapshot> BuildSemantic(Element element)
    {
        if (element.Participation != ElementParticipation.Visible)
            return [];
        var children = element.Children.SelectMany(BuildSemantic).ToArray();
        return element.CreateSemanticSnapshot(children) is { } semantic
            ? [semantic]
            : [.. children];
    }

    private static void Append(SemanticSnapshot snapshot, StringBuilder output)
    {
        output
            .Append("semantic epoch=")
            .Append(snapshot.Identity.CompositionEpoch.ToString(CultureInfo.InvariantCulture))
            .Append(" element=")
            .Append(snapshot.Identity.ElementId.ToString(CultureInfo.InvariantCulture))
            .Append(" generation=")
            .Append(snapshot.Identity.Generation.ToString(CultureInfo.InvariantCulture))
            .Append(" role=")
            .Append(snapshot.Role)
            .Append(" enabled=")
            .Append(snapshot.Enabled ? "true" : "false")
            .Append(" focused=")
            .Append(snapshot.Focused ? "true" : "false")
            .Append(" selected=")
            .Append(snapshot.Selected ? "true" : "false")
            .Append(" actions=")
            .Append(snapshot.Actions)
            .Append(" suppressions=[]\n");
        foreach (var child in snapshot.Children)
            Append(child, output);
    }

    internal static void ThrowAll(List<Exception>? errors, string message)
    {
        if (errors is null or { Count: 0 })
            return;
        if (errors.Count == 1)
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        throw new AggregateException(message, errors);
    }

    private static string Quote(string value) => DiagnosticText.Quote(value);

    private sealed class InputProjectionTracker(
        ReactiveGraph graph,
        ReactiveScope scope,
        string name
    ) : ReactiveNode(graph, name, scope)
    {
        internal override string Kind => "input-projection";
        internal long Revision { get; private set; }

        internal T Capture<T>(Func<T> project)
        {
            ArgumentNullException.ThrowIfNull(project);
            Invalidate();
            var captureRevision = Revision;
            T result = default!;
            if (Graph.Collect(this, () => result = project()) || Revision != captureRevision)
            {
                Invalidate();
                throw new InvalidOperationException(
                    "Input projection state changed while the scene was being produced."
                );
            }
            return result;
        }

        internal void Invalidate() => Revision = checked(Revision + 1);

        internal override void DependencyChanged()
        {
            if (!IsDisposed)
                Invalidate();
        }
    }
}
