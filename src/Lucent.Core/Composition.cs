using System.Globalization;
using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Lucent.Core;

/// <summary>Owns a retained structural tree and the scopes of its mounted elements.</summary>
public sealed class Composition : IDisposable
{
    private static long _nextEpoch;
    private readonly ReactiveGraph _graph;
    private readonly long _epoch = Interlocked.Increment(ref _nextEpoch);
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
    /// <summary>Monotonic notification token for retained semantic changes; it contains no platform transport.</summary>
    public long SemanticRevision => _semanticRevision;
    public event Action? SemanticsChanged;
    /// <summary>Composition-owned portable input, focus, and capture router.</summary>
    public InputRouter Input { get { _graph.CheckThread(); ThrowIfDisposed(); return _input ??= new InputRouter(this); } }
    internal ReactiveGraph Graph => _graph;
    internal CompositionContext? Factory => _factory;
    internal long Epoch => _epoch;
    internal TransitionController Transitions => _transitions;
    internal InputRouter? InputIfCreated => _input;
    internal long NextSceneGeneration() { _graph.CheckThread(); ThrowIfDisposed(); return checked(++_nextSceneGeneration); }
    internal long LatestSceneGeneration => _nextSceneGeneration;
    internal long InteractionVisualGeneration => _interactionVisualGeneration;
    internal void InvalidateInteractionVisuals() => _interactionVisualGeneration = checked(_interactionVisualGeneration + 1);

    /// <summary>Commits this composition's pending reactive work on its owning UI thread.</summary>
    public bool Flush() { _graph.CheckThread(); ThrowIfBehaviorAttachment(); ThrowIfDisposed(); return _graph.DrainPosted(); }

    /// <summary>Forwards worker-posted work notification without exposing a platform transport to Core.</summary>
    public event Action? WorkAvailable { add => _graph.WorkAvailable += value; remove => _graph.WorkAvailable -= value; }

    /// <summary>Advances bounded presentation samples; it queues no background work.</summary>
    public void AdvanceTransitions(int milliseconds) { _graph.CheckThread(); ThrowIfBehaviorAttachment(); ThrowIfDisposed(); _transitions.Advance(milliseconds); }

    /// <summary>Adds fixed authored structure below an already-mounted element.</summary>
    public Element Child(Element parent, string name)
    {
        ThrowIfFactoryCreation();
        ThrowIfDisposed();
        return Create(parent, name, attach: true);
    }

    /// <summary>Atomically mounts one recipe root below <paramref name="parent"/> using the supplied theme.</summary>
    public Element Mount(Element parent, ThemeContext theme, Func<CompositionContext, Element> content)
    {
        ThrowIfFactoryCreation();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(content);
        if (!ReferenceEquals(parent.Composition, this)) throw new ArgumentException("The parent belongs to another composition.", nameof(parent));
        parent.ThrowIfDisposed();
        if (!ReferenceEquals(theme.Graph, _graph)) throw new ArgumentException("Theme context belongs to another reactive graph.", nameof(theme));
        if (!theme.Scope.DescendsFrom(Root.Scope)) throw new ArgumentException("Theme context must be owned by this composition.", nameof(theme));
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

    /// <summary>Creates a fixed-height keyed region whose mounted entries are derived from its containing viewport.</summary>
    internal VirtualizedRegion<TKey, TItem> Virtualize<TKey, TItem>(Element viewport, string name, Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key, Func<TItem, CompositionContext, Element> content, float rowHeight, ThemeContext theme) where TKey : notnull
    {
        ThrowIfFactoryCreation();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(content);
        return new VirtualizedRegion<TKey, TItem>(this, viewport, name, source, key, content, rowHeight, theme);
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
        var snapshot = snapshots.Count == 0 ? null : Root.HasSemantics ? snapshots.Single() : Root.CreateStructuralSemanticSnapshot(snapshots);
        if (snapshot is not null) Register(snapshot);
        return snapshot;
    }

    /// <summary>Returns a deterministic, value-free semantic diagnostic dump. The declared M3 matrix has no suppressions.</summary>
    public string SemanticDump()
    {
        _graph.CheckThread(); ThrowIfDisposed();
        var snapshot = SemanticSnapshot(); var output = new StringBuilder("semantics revision=").Append(_semanticRevision.ToString(CultureInfo.InvariantCulture)).Append('\n');
        if (snapshot is not null) Append(snapshot, output);
        return output.ToString();
    }

    /// <summary>Rejects a semantic identity once its element has changed or departed.</summary>
    public bool IsCurrent(SemanticIdentity identity)
    {
        _graph.CheckThread();
        return !IsDisposed && identity.CompositionEpoch == _epoch && Find(Root, identity.ElementId) is { } element && element.IsCurrent(identity);
    }

    /// <summary>Executes one declared portable semantic command on the owning UI thread.</summary>
    public SemanticCommandResult ExecuteSemanticCommand(SemanticIdentity identity, SemanticCommand command)
    {
        _graph.CheckThread();
        if (IsDisposed) return SemanticCommandResult.Stale;
        command.Validate();
        if (!IsCurrent(identity) || Find(Root, identity.ElementId) is not { } element) return SemanticCommandResult.Stale;
        if (!element.SemanticEnabled()) return SemanticCommandResult.Disabled;
        return element.ExecuteSemanticCommand(command) ? SemanticCommandResult.Applied : SemanticCommandResult.Rejected;
    }

    /// <summary>Selects one retained list item and clears every selectable sibling in its nearest semantic list.</summary>
    internal bool SelectSemantic(ElementIdentity identity)
    {
        _graph.CheckThread();
        if (identity.CompositionEpoch != _epoch || Find(Root, identity.ElementId) is not { } target || !target.SemanticEnabled()) return false;
        var list = target.Parent;
        while (list is not null && list.DeclaredSemanticRole != SemanticRole.List) list = list.Parent;
        if (list is null) return target.SetSelected(true);
        foreach (var element in SemanticChildren(list))
            if (element.HasSelectableSemantics) element.SetSelected(ReferenceEquals(element, target));
        return true;
    }

    private static IEnumerable<Element> SemanticChildren(Element parent)
    {
        foreach (var child in parent.Children)
        {
            if (child.DeclaredSemanticRole is null)
            {
                foreach (var descendant in SemanticChildren(child)) yield return descendant;
            }
            else yield return child;
        }
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
        var scope = parent.Scope.CreateElementChild(name);
        var element = new Element(this, parent, scope, NextId(), name);
        parent.Scope.OwnElement(element);
        if (attach) parent.Attach(element);
        factory?.Record(element);
        return element;
    }

    internal T RunFactory<T>(CompositionContext context, Func<T> factory)
    {
        _graph.CheckThread();
        ArgumentNullException.ThrowIfNull(factory);
        var prior = _factory;
        if (prior is not null && !prior.Contains(context.Parent))
            throw new InvalidOperationException("Nested content factories must mount below the active provisional root.");
        _factory = context;
        try { return factory(); }
        finally { _factory = prior; }
    }

    internal Element MountCore(Element parent, ThemeContext theme, Func<CompositionContext, Element> content)
    {
        var context = new CompositionContext(this, parent, theme);
        try
        {
            var created = context.Run(() => content(context));
            if (IsDisposed || parent.IsDisposed) throw new ObjectDisposedException(nameof(Composition));
            context.Validate(created);
            parent.Attach(created);
            context.Complete();
            return created;
        }
        catch (Exception error)
        {
            var errors = new List<Exception> { error };
            try { context.Dispose(); } catch (Exception cleanup) { errors.Add(cleanup); }
            ThrowAll(errors, "Composition mount failed.");
            throw;
        }
        finally
        {
            if (context.IsCommitted) context.Dispose();
        }
    }

    internal void RunBehavior(BehaviorContext context, Action attach)
    {
        _graph.CheckThread();
        if (_behaviorDepth != 0) throw new InvalidOperationException("Behavior attachment cannot nest.");
        _behaviorDepth++;
        try { attach(); }
        finally { _behaviorDepth--; }
    }

    internal void RunBehaviorCleanup(Action cleanup)
    {
        _graph.CheckThread();
        _behaviorDepth++;
        try { cleanup(); }
        finally { _behaviorDepth--; }
    }

    internal void ThrowIfDisposed()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(Composition));
    }

    internal void CheckThread() => _graph.CheckThread();

    internal void RejectForeignFactory(Element parent)
    {
        _graph.CheckThread();
        if (_factoryRollbackDepth == 0 && _factory is not null && !_factory.Contains(parent) && !IsOwnedCleanup(parent))
            throw new InvalidOperationException("Structural regions cannot update outside the active provisional root.");
    }

    internal void ValidateFactoryMutation(Element element)
    {
        _graph.CheckThread();
        if (_factoryRollbackDepth == 0 && _factory is not null && !_factory.Contains(element) && !IsOwnedCleanup(element))
            throw new InvalidOperationException("Element mutations must remain below the active provisional root.");
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
        try { cleanup(); }
        finally { _ownedCleanup.RemoveAt(_ownedCleanup.Count - 1); }
    }

    internal void RunFactoryRollback(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        _factoryRollbackDepth++;
        try { cleanup(); }
        finally { _factoryRollbackDepth--; }
    }

    private bool IsOwnedCleanup(Element element) => _ownedCleanup.Any(root => root.IsAncestorOf(element));

    internal void ThrowIfBehaviorAttachment()
    {
        if (_behaviorDepth != 0) throw new InvalidOperationException("Behaviors cannot configure styles.");
    }

    private void Register(SemanticSnapshot snapshot)
    {
        _emittedSemantics.Add(snapshot.Identity);
        foreach (var child in snapshot.Children) Register(child);
    }

    internal void InvalidateSemantics()
    {
        _emittedSemantics.Clear();
        _semanticRevision = checked(_semanticRevision + 1);
        SemanticsChanged?.Invoke();
    }

    internal void Register(IVirtualizedRegion region) => _virtualized.Add(region);
    internal void Unregister(IVirtualizedRegion region) => _virtualized.Remove(region);
    internal void RealizeVirtualized(LayoutViewport viewport)
    {
        _graph.CheckThread();
        foreach (var region in _virtualized.ToArray()) region.Realize(viewport);
    }

    internal Element? Find(ElementIdentity identity) => identity.CompositionEpoch == _epoch ? Find(Root, identity.ElementId) : null;
    internal IReadOnlyList<Element> Path(Element element)
    {
        var path = new List<Element>();
        for (Element? current = element; current is not null; current = current.Parent) path.Add(current);
        path.Reverse(); return path;
    }
    internal IEnumerable<Element> Elements() => Traverse(Root);
    private static IEnumerable<Element> Traverse(Element element)
    {
        if (element.IsDisposed) yield break;
        yield return element;
        foreach (var child in element.Children) foreach (var descendant in Traverse(child)) yield return descendant;
    }

    private void ThrowIfFactoryCreation()
    {
        _graph.CheckThread();
        if (_factory is not null) throw new InvalidOperationException("Structural creation must use the active composition context.");
        if (_behaviorDepth != 0) throw new InvalidOperationException("Behaviors cannot create structure.");
    }

    public void Dispose()
    {
        _graph.CheckThread();
        ThrowIfBehaviorAttachment();
        if (IsDisposed) return;
        List<Exception>? errors = null;
        IsDisposed = true;
        RunOwnedCleanup(Root, () =>
        {
            try { _input?.Cleanup(); } catch (Exception exception) { errors = [exception]; }
            try { Root.Dispose(); } catch (Exception exception) { (errors ??= []).Add(exception); }
        });
        ThrowAll(errors, "Composition cleanup failed.");
    }

    private long NextId() => checked(++_nextElementId);

    private static void Append(Element element, Element? parent, StringBuilder dump)
    {
        if (element.IsDisposed) return;
        dump.Append("element ").Append(element.Id.ToString(CultureInfo.InvariantCulture))
            .Append(" scope=").Append(element.Scope.Id.ToString(CultureInfo.InvariantCulture))
            .Append(" name=").Append(Quote(element.Name))
            .Append(" parent=").Append(parent?.Id.ToString(CultureInfo.InvariantCulture) ?? "-").Append('\n');
        element.AppendPresentationDump(dump);
        foreach (var child in element.Children) Append(child, element, dump);
    }

    private static List<SemanticSnapshot> BuildSemantic(Element element)
    {
        var children = element.Children.SelectMany(BuildSemantic).ToArray();
        return element.CreateSemanticSnapshot(children) is { } semantic ? [semantic] : [.. children];
    }

    private static void Append(SemanticSnapshot snapshot, StringBuilder output)
    {
        output.Append("semantic epoch=").Append(snapshot.Identity.CompositionEpoch.ToString(CultureInfo.InvariantCulture)).Append(" element=").Append(snapshot.Identity.ElementId.ToString(CultureInfo.InvariantCulture))
            .Append(" generation=").Append(snapshot.Identity.Generation.ToString(CultureInfo.InvariantCulture)).Append(" role=").Append(snapshot.Role).Append(" enabled=").Append(snapshot.Enabled ? "true" : "false")
            .Append(" focused=").Append(snapshot.Focused ? "true" : "false").Append(" selected=").Append(snapshot.Selected ? "true" : "false").Append(" actions=").Append(snapshot.Actions).Append(" suppressions=[]\n");
        foreach (var child in snapshot.Children) Append(child, output);
    }

    private static Element? Find(Element element, long id)
    {
        if (element.Id == id) return element;
        foreach (var child in element.Children)
            if (Find(child, id) is { } found) return found;
        return null;
    }

    internal static void ThrowAll(List<Exception>? errors, string message)
    {
        if (errors is null or { Count: 0 }) return;
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        throw new AggregateException(message, errors);
    }

    private static string Quote(string value) => DiagnosticText.Quote(value);
}

/// <summary>A stable retained structural identity. It intentionally has no visual or platform state.</summary>
public sealed class Element : IDisposable
{
    private readonly List<Element> _children = [];
    private readonly ReadOnlyCollection<Element> _childrenView;
    private Element? _parent;
    private Action? _disposed;
    private ElementPresentation? _presentation;
    private readonly List<BehaviorMount> _behaviors = [];
    private BehaviorOwnership _behaviorClaims;
    private SemanticDeclaration? _semantics;
    private Func<SemanticCommand, bool>? _semanticCommand;
    private Action<bool>? _selectionChanged;
    private long _semanticGeneration;
    private bool _inputDisabled;
    private EffectiveSemanticState? _effectiveSemanticState;
    private long _nextRecipeOrdinal;

    internal Element(Composition composition, Element? parent, ReactiveScope scope, long id, string name)
    {
        Composition = composition;
        _parent = parent;
        _childrenView = _children.AsReadOnly();
        Scope = scope;
        Scope.SetMutationGuard(composition.ThrowIfBehaviorAttachment);
        Scope.SetFactoryGuard(() => composition.ValidateFactoryMutation(this));
        Scope.SetFactoryRollback(composition.RegisterFactoryRollback);
        Id = id;
        Name = name;
        Scope.OnElementDispose(Dispose);
    }

    public long Id { get; }
    public string Name { get; }
    public ReactiveScope Scope { get; }
    public IReadOnlyList<Element> Children => _childrenView;
    public bool IsDisposed { get; private set; }
    internal Composition Composition { get; }
    internal Element? Parent => _parent;
    internal ElementPresentation? Presentation => _presentation;
    internal bool HasPresentation => _presentation is not null;
    internal bool HasSemantics => _semantics is not null;
    internal IEnumerable<IProperty> AncestorProperties()
    {
        for (var parent = _parent; parent is not null; parent = parent._parent)
            if (parent._presentation is not null)
                foreach (var property in parent._presentation.OwnProperties()) yield return property;
    }

    /// <summary>Associates the one typed property model with this retained element.</summary>
    public void Present(ThemeContext theme, Style? component = null, Style? author = null, params Transition[] transitions)
    {
        ValidatePresentation(theme, component, author, transitions);
        _presentation = new ElementPresentation(this, theme, component ?? Style.Empty, author ?? Style.Empty, transitions);
    }

    /// <summary>Checks presentation inputs without allocating reactive presentation state.</summary>
    internal void ValidatePresentation(ThemeContext theme, Style? component = null, Style? author = null, params Transition[] transitions)
    {
        Composition.CheckThread();
        Composition.ValidateFactoryMutation(this);
        Composition.ThrowIfBehaviorAttachment();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(theme);
        if (!ReferenceEquals(theme.Graph, Composition.Graph)) throw new ArgumentException("Theme context belongs to another reactive graph.", nameof(theme));
        if (!theme.Scope.DescendsFrom(Composition.Root.Scope)) throw new ArgumentException("Theme context must be owned by this composition.", nameof(theme));
        theme.ValidateLive();
        ArgumentNullException.ThrowIfNull(transitions);
        if (_presentation is not null) throw new InvalidOperationException("An element has one presentation model.");
        if (transitions.Any(transition => transition is null)) throw new ArgumentException("Transitions cannot contain null.", nameof(transitions));
        ElementPresentation.Validate(component ?? Style.Empty, author ?? Style.Empty, transitions);
    }

    /// <summary>Updates finite interaction state without creating a second modifier model.</summary>
    public void SetVariants(VariantState variants)
    {
        Composition.CheckThread();
        Composition.ValidateFactoryMutation(this);
        ThrowIfDisposed();
        (_presentation ?? throw new InvalidOperationException("An element needs a presentation before it can have variants.")).SetVariants(variants);
    }

    public ResolvedProperty<T> Resolve<T>(Property<T> property)
    {
        Composition.CheckThread();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(property);
        if (_presentation is not null) return _presentation.Resolve(property);
        if (property.Inherits && _parent is not null)
        {
            var inherited = _parent.Resolve(property);
            return new ResolvedProperty<T>(inherited.Value, new PropertyProvenance("inherited", inherited.Winner.Ordinal), [new PropertyProvenance("default", 0)]);
        }
        return new ResolvedProperty<T>(property.DefaultValue, new PropertyProvenance("default", 0), []);
    }

    /// <summary>Attaches interaction behavior transactionally; all cleanup remains in element-owned child scopes.</summary>
    public void AttachBehaviors(params Behavior[] behaviors)
    {
        Composition.CheckThread();
        Composition.ValidateFactoryMutation(this);
        Composition.ThrowIfBehaviorAttachment();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(behaviors);
        var claims = ValidateBehaviorAttachment(behaviors);

        var provisional = new List<(ReactiveScope Scope, BehaviorContext Context)>();
        try
        {
            foreach (var behavior in behaviors)
            {
                var scope = Scope.CreateChild(Name + ".behavior." + behavior.Name);
                var context = new BehaviorContext(Id, Composition, scope, behavior, BehaviorStateChanged);
                provisional.Add((scope, context));
                Composition.RunBehavior(context, () => behavior.Attach(context));
                if (behavior.Ownership.HasFlag(BehaviorOwnership.Semantics) && context.Semantics is null)
                    throw new InvalidOperationException("Behavior requires semantics.");
                if (context.Semantics is { Actions: not SemanticAction.None } && !behavior.Ownership.HasFlag(BehaviorOwnership.Action))
                    throw new InvalidOperationException("Semantic actions require action ownership.");
                context.Complete();
            }
            var semanticContext = provisional.SingleOrDefault(item => item.Context.Semantics is not null);
            if (semanticContext.Context?.Semantics is { } semantic)
            {
                SetSemantics(semantic);
                _semanticCommand = semanticContext.Context.SemanticCommand;
                _selectionChanged = semanticContext.Context.SelectionChanged;
            }
            foreach (var item in provisional)
            {
                var mount = new BehaviorMount(item.Context.Behavior.Name, item.Context.Behavior.Ownership, item.Context.State);
                _behaviors.Add(mount);
                item.Scope.OnDispose(() => _behaviors.Remove(mount));
            }
            _behaviorClaims = claims;
            RefreshBehaviorVariants();
        }
        catch (Exception error)
        {
            var errors = new List<Exception> { error };
            foreach (var item in provisional.AsEnumerable().Reverse())
                try { Composition.RunBehaviorCleanup(item.Scope.Dispose); } catch (Exception cleanup) { errors.Add(cleanup); }
            Composition.ThrowAll(errors, "Behavior attachment failed.");
        }
    }

    /// <summary>Checks exclusive behavior ownership without changing presentation or input state.</summary>
    internal BehaviorOwnership ValidateBehaviorAttachment(params Behavior[] behaviors)
    {
        Composition.CheckThread();
        Composition.ValidateFactoryMutation(this);
        Composition.ThrowIfBehaviorAttachment();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(behaviors);
        var claims = _behaviorClaims;
        foreach (var behavior in behaviors)
        {
            ArgumentNullException.ThrowIfNull(behavior);
            ReactiveGraph.ValidateName(behavior.Name, nameof(behaviors));
            ValidateOwnership(behavior.Ownership);
            if ((claims & behavior.Ownership) != 0) throw new InvalidOperationException("Exclusive behavior ownership conflicts on this element.");
            claims |= behavior.Ownership;
        }
        return claims;
    }

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

    internal long NextRecipeOrdinal() => checked(++_nextRecipeOrdinal);

    internal void ThrowIfDisposed()
    {
        if (IsDisposed) throw new ObjectDisposedException(Name);
    }

    internal SemanticSnapshot? CreateSemanticSnapshot(IReadOnlyList<SemanticSnapshot> children)
    {
        if (_semantics is null) return null;
        var state = ReconcileSemanticState();
        return new(new SemanticIdentity(Composition.Epoch, Id, _semanticGeneration), _semantics.Role, _semantics.Name, _semantics.Value, state.Enabled, state.Focused, state.Selected, _semantics.Actions, children);
    }

    internal bool IsCurrent(SemanticIdentity identity) { if (_semantics is not null) _ = ReconcileSemanticState(); return !IsDisposed && identity.Generation == _semanticGeneration && (_semantics is not null || _semanticGeneration == 0); }

    internal bool ExecuteSemanticCommand(SemanticCommand command)
    {
        if (_semantics is null || _semanticCommand is null || !Allows(command)) return false;
        return _semanticCommand(command);
    }
    internal SemanticRole? DeclaredSemanticRole => _semantics?.Role;
    internal bool HasSelectableSemantics => _semantics?.Actions.HasFlag(SemanticAction.Select) == true && _selectionChanged is not null;
    internal bool SetSelected(bool value)
    {
        if (_selectionChanged is null) return false;
        _selectionChanged(value);
        return true;
    }
    internal bool SemanticEnabled() => _semantics is not null && ReconcileSemanticState().Enabled;

    internal SemanticSnapshot CreateStructuralSemanticSnapshot(IReadOnlyList<SemanticSnapshot> children) => new(
        new SemanticIdentity(Composition.Epoch, Id, _semanticGeneration), SemanticRole.Group, Name, null, true, false, false, SemanticAction.None, children);

    /// <summary>Starts a bounded, composition-owned sample for an eligible transition specification.</summary>
    public void StartTransition<T>(Property<T> property, T value)
    {
        Composition.CheckThread();
        Composition.ValidateFactoryMutation(this);
        Composition.ThrowIfBehaviorAttachment();
        ThrowIfDisposed();
        (_presentation ?? throw new InvalidOperationException("An element needs a presentation before transition samples can start.")).Start(property, value);
    }

    internal void AppendPresentationDump(StringBuilder dump)
    {
        _presentation?.AppendDump(dump);
        foreach (var behavior in _behaviors.OrderBy(behavior => behavior.Name, StringComparer.Ordinal))
            dump.Append("  behavior name=").Append(Quote(behavior.Name)).Append(" ownership=").Append(behavior.Ownership)
                .Append(" state=[").Append(string.Join(',', behavior.State.OrderBy(item => item.Key).Select(item => item.Key + "=" + (item.Value ? "true" : "false")))).Append("]\n");
        if (_semantics is not null)
        {
            var state = ReconcileSemanticState();
            dump.Append("  semantic element=").Append(Id.ToString(CultureInfo.InvariantCulture)).Append(" generation=")
                .Append(_semanticGeneration.ToString(CultureInfo.InvariantCulture)).Append(" role=").Append(_semantics.Role)
                .Append(" enabled=").Append(state.Enabled ? "true" : "false").Append(" focused=").Append(state.Focused ? "true" : "false")
                .Append(" selected=").Append(state.Selected ? "true" : "false").Append(" actions=").Append(_semantics.Actions).Append('\n');
        }
    }

    private void SetSemantics(SemanticDeclaration semantics)
    {
        _semantics = semantics;
        _effectiveSemanticState = null;
        _semanticGeneration = checked(_semanticGeneration + 1);
        Composition.InvalidateSemantics();
    }

    private bool Allows(SemanticCommand command) => command.Kind switch
    {
        SemanticCommandKind.Focus => true,
        SemanticCommandKind.Invoke => _semantics!.Actions.HasFlag(SemanticAction.Invoke),
        SemanticCommandKind.SetValue => _semantics!.Actions.HasFlag(SemanticAction.SetValue),
        SemanticCommandKind.Select => _semantics!.Actions.HasFlag(SemanticAction.Select),
        SemanticCommandKind.Scroll => _semantics!.Actions.HasFlag(SemanticAction.Scroll),
        _ => false
    };

    internal void UpdateControl<T>(Property<T> property, T value)
    {
        Composition.CheckThread();
        Composition.ThrowIfBehaviorAttachment();
        ThrowIfDisposed();
        (_presentation ?? throw new InvalidOperationException("An element needs a presentation before control state can update it.")).SetControl(property, value);
    }

    internal void UpdateControlSemantics(SemanticDeclaration semantics)
    {
        Composition.CheckThread();
        Composition.ThrowIfBehaviorAttachment();
        ThrowIfDisposed();
        if (_semantics is null) throw new InvalidOperationException("An element needs semantic behavior before control state can update it.");
        SetSemantics(semantics ?? throw new ArgumentNullException(nameof(semantics)));
    }

    internal bool RefreshBehaviorVariants()
    {
        if (_presentation is null || IsDisposed) return false;
        var variants = VariantState.None;
        foreach (var behavior in _behaviors)
        {
            if (behavior.State.GetValueOrDefault(BehaviorState.Pressed)) variants |= VariantState.Pressed;
            if (behavior.State.GetValueOrDefault(BehaviorState.Selected)) variants |= VariantState.Selected;
            if (behavior.State.GetValueOrDefault(BehaviorState.FocusVisible)) variants |= VariantState.FocusVisible;
        }
        if (_inputDisabled) variants |= VariantState.Disabled;
        if (!_presentation.SetBehaviorVariants(variants)) return false;
        Composition.InvalidateInteractionVisuals();
        return true;
    }

    internal bool SetInputDisabledVariant(bool disabled)
    {
        if (_inputDisabled == disabled) return false;
        _inputDisabled = disabled;
        var visualChanged = RefreshBehaviorVariants();
        ReconcileSemanticState();
        return visualChanged;
    }

    private bool HasBehaviorState(BehaviorState state) => _behaviors.Any(behavior => behavior.State.GetValueOrDefault(state));
    private bool InputAvailable() => !IsDisposed && (_parent is null || _parent.InputAvailable()) && Resolve(InputProperties.Enabled).Value && Resolve(InputProperties.Visible).Value;
    private void BehaviorStateChanged()
    {
        RefreshBehaviorVariants();
        _ = ReconcileSemanticState();
    }

    /// <summary>Single source of truth for exported semantic availability and behavior state.</summary>
    private EffectiveSemanticState ReconcileSemanticState()
    {
        if (_semantics is null) return default;
        var next = new EffectiveSemanticState(_semantics.Enabled && InputAvailable(), _semantics.Focused || HasBehaviorState(BehaviorState.Focused), _semantics.Selected || HasBehaviorState(BehaviorState.Selected));
        if (_effectiveSemanticState is { } prior && prior != next) { _semanticGeneration = checked(_semanticGeneration + 1); Composition.InvalidateSemantics(); }
        _effectiveSemanticState = next;
        return next;
    }
    internal void ReconcileSemanticStateForInput() => _ = ReconcileSemanticState();

    private static void ValidateOwnership(BehaviorOwnership ownership)
    {
        const BehaviorOwnership all = BehaviorOwnership.Focus | BehaviorOwnership.Action | BehaviorOwnership.Semantics;
        if ((ownership & ~all) != 0 || (ownership.HasFlag(BehaviorOwnership.Action) && !ownership.HasFlag(BehaviorOwnership.Semantics)))
            throw new ArgumentException("Behavior ownership must use finite claims and actions require semantic ownership.", nameof(ownership));
    }

    public void Dispose()
    {
        Composition.CheckThread();
        Composition.ValidateFactoryMutation(this);
        Composition.ThrowIfBehaviorAttachment();
        if (IsDisposed) return;
        IsDisposed = true;
        Composition.InvalidateSemantics();
        Composition.Transitions.Remove(this);
        List<Exception>? errors = null;
        try { if (Composition.InputIfCreated is { } input) input.RemoveElement(this, PointerCaptureLossReason.Disposed); }
        catch (Exception exception) { errors = [exception]; }
        var children = _children.ToArray();
        _children.Clear();
        for (var index = children.Length - 1; index >= 0; index--)
        {
            try { children[index].Dispose(); }
            catch (Exception exception) { (errors ??= []).Add(exception); }
        }
        try { Scope.Dispose(); }
        catch (Exception exception) { (errors ??= []).Add(exception); }
        _parent?.Scope.Detach(this);
        _parent?.Detach(this);
        _parent = null;
        var disposed = _disposed;
        _disposed = null;
        try { disposed?.Invoke(); }
        catch (Exception exception) { (errors ??= []).Add(exception); }
        Composition.ThrowAll(errors, "Element cleanup failed.");
    }

    private sealed record BehaviorMount(string Name, BehaviorOwnership Ownership, IReadOnlyDictionary<BehaviorState, bool> State);
    private readonly record struct EffectiveSemanticState(bool Enabled, bool Focused, bool Selected);
    private static string Quote(string value) => DiagnosticText.Quote(value);
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
    private List<Action>? _rollback;

    internal CompositionContext(Composition composition, Element parent, ThemeContext? theme = null)
    {
        _composition = composition;
        _parent = parent;
        _theme = theme;
    }

    /// <summary>The root mount's theme. Nested recipes can observe it but cannot replace it.</summary>
    public ThemeContext Theme => _theme ?? throw new InvalidOperationException("This composition context has no root mount theme.");
    private readonly ThemeContext? _theme;

    /// <summary>Creates the one root supplied by this conditional or keyed-item factory.</summary>
    public Element Element(string name)
    {
        _composition.ThrowIfBehaviorAttachment();
        ThrowIfInactive();
        if (_root is not null) throw new InvalidOperationException("A content factory creates exactly one root element.");
        _root = _composition.Create(_parent, name, attach: false, this);
        return _root;
    }

    /// <summary>Adds fixed authored structure below a factory-created element.</summary>
    public Element Child(Element parent, string name)
    {
        _composition.ThrowIfBehaviorAttachment();
        ThrowIfInactive();
        var root = Root;
        if (!root.IsAncestorOf(parent)) throw new ArgumentException("A content factory can only add children below its provisional root.", nameof(parent));
        return _composition.Create(parent, name, attach: true, this);
    }

    /// <summary>Atomically mounts one nested recipe root below a provisional element.</summary>
    public Element Mount(Element parent, Func<CompositionContext, Element> content)
    {
        ThrowIfActiveFactory();
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(content);
        if (!Root.IsAncestorOf(parent)) throw new ArgumentException("A content factory can only mount below its provisional root.", nameof(parent));
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
        if (!Root.IsAncestorOf(parent)) throw new ArgumentException("A content factory can only mount below its provisional root.", nameof(parent));
        foreach (var recipe in content) recipe.Mount(this, parent);
    }

    /// <summary>Creates a retained conditional region below a provisional element.</summary>
    public ConditionalRegion When(Element parent, string name, Func<bool> active, Func<CompositionContext, Element> content)
    {
        ThrowIfActiveFactory();
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(content);
        if (!Root.IsAncestorOf(parent)) throw new ArgumentException("A content factory can only add regions below its provisional root.", nameof(parent));
        return new ConditionalRegion(_composition, parent, name, active, content, this, Theme);
    }

    /// <summary>Creates a retained keyed region below a provisional element.</summary>
    public KeyedRegion<TKey, TItem> ForEach<TKey, TItem>(Element parent, string name, Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key, Func<TItem, CompositionContext, Element> content) where TKey : notnull
    {
        ThrowIfActiveFactory();
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(content);
        if (!Root.IsAncestorOf(parent)) throw new ArgumentException("A content factory can only add regions below its provisional root.", nameof(parent));
        return new KeyedRegion<TKey, TItem>(_composition, parent, name, source, key, content, this, Theme);
    }

    internal VirtualizedRegion<TKey, TItem> Virtualize<TKey, TItem>(Element viewport, string name, Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key, Func<TItem, CompositionContext, Element> content, float rowHeight) where TKey : notnull
    {
        ThrowIfActiveFactory();
        ArgumentNullException.ThrowIfNull(viewport); ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(key); ArgumentNullException.ThrowIfNull(content);
        if (!Root.IsAncestorOf(viewport)) throw new ArgumentException("A content factory can only add regions below its provisional root.", nameof(viewport));
        return new VirtualizedRegion<TKey, TItem>(_composition, viewport, name, source, key, content, rowHeight, Theme, this);
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
        if (!ReferenceEquals(created, root)) throw new InvalidOperationException("A content factory must return its created root element.");
        if (_created.Any(element => element.IsDisposed || element.Scope.IsDisposed))
            throw new InvalidOperationException("Disposed content cannot be committed.");
        return root;
    }

    internal void Complete() { PromoteRollback(); _committed = true; }
    internal bool IsCommitted => _committed;

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
        _composition.ThrowIfBehaviorAttachment();
        if (_disposed) return;
        _disposed = true;
        if (_committed) { _rollback = null; return; }
        List<Exception>? errors = null;
        try { if (_root is not null) _composition.RunOwnedCleanup(_root, _root.Dispose); }
        catch (Exception exception) { errors = [exception]; }
        if (_rollback is not null)
            foreach (var cleanup in _rollback.AsEnumerable().Reverse())
                try { _composition.RunFactoryRollback(cleanup); } catch (Exception exception) { (errors ??= []).Add(exception); }
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
        if (!_committed && !_disposed) (_rollback ??= []).Add(cleanup);
    }
    private void PromoteRollback()
    {
        if (_rollback is null) return;
        foreach (var cleanup in _rollback) _composition.RegisterFactoryRollback(cleanup);
        _rollback = null;
    }
    internal Element RecipeElement(string kind, string? name)
    {
        ThrowIfActiveFactory();
        if (_root is not null) throw new InvalidOperationException("A content factory creates exactly one root element.");
        var ordinal = _parent.NextRecipeOrdinal();
        _root = _composition.Create(_parent, name ?? kind + "-" + ordinal.ToString(CultureInfo.InvariantCulture), attach: false, this);
        return _root;
    }
    internal Element Parent => _parent;
    internal bool Contains(Element parent) => _root is not null && _root.IsAncestorOf(parent);

    private void ThrowIfActiveFactory()
    {
        ThrowIfInactive();
        if (!ReferenceEquals(_composition.Factory, this)) throw new InvalidOperationException("Composition context operations are only available while their recipe is executing.");
    }

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

    internal ConditionalRegion(Composition composition, Element parent, string name, Func<bool> active, Func<CompositionContext, Element> content,
        CompositionContext? factory = null, ThemeContext? theme = null)
    {
        _composition = composition;
        Region = composition.Create(parent, name, attach: true, factory);
        Theme = theme;
        _active = active;
        _content = content;
        Region.Scope.Own(this);
        _effect = Region.Scope.Effect(Refresh, name + ".condition");
    }

    public Element Region { get; }
    public Element? Active => _child;
    public bool IsDisposed { get; private set; }
    private ThemeContext? Theme { get; }

    /// <summary>Re-evaluates the condition. Usual callers let the owned effect invoke this.</summary>
    public void Refresh()
    {
        _composition.CheckThread();
        _composition.ThrowIfBehaviorAttachment();
        _composition.RejectForeignFactory(Region);
        Update(_active!());
    }

    public void Update(bool active)
    {
        _composition.CheckThread();
        _composition.ThrowIfBehaviorAttachment();
        _composition.RejectForeignFactory(Region);
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

            var context = new CompositionContext(_composition, Region, Theme);
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
        _composition.ThrowIfBehaviorAttachment();
        _composition.RejectForeignFactory(Region);
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
        Func<TItem, TKey> key, Func<TItem, CompositionContext, Element> content, CompositionContext? factory = null, ThemeContext? theme = null)
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

    public Element Region { get; }
    public IReadOnlyList<Element> Items => Region.Children;
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

    public void Update(IEnumerable<TItem> items)
    {
        _composition.CheckThread();
        _composition.ThrowIfBehaviorAttachment();
        ArgumentNullException.ThrowIfNull(items);
        _composition.RejectForeignFactory(Region);
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
                    var context = new CompositionContext(_composition, Region, Theme);
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
        _composition.ThrowIfBehaviorAttachment();
        _composition.RejectForeignFactory(Region);
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

internal interface IVirtualizedRegion
{
    void Realize(LayoutViewport viewport);
}

/// <summary>A fixed-height keyed region that owns only the visible rows plus two rows of overscan on each side.</summary>
internal sealed class VirtualizedRegion<TKey, TItem> : IDisposable, IVirtualizedRegion where TKey : notnull
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

    internal VirtualizedRegion(Composition composition, Element viewport, string name, Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key, Func<TItem, CompositionContext, Element> content, float rowHeight, ThemeContext theme, CompositionContext? factory = null)
    {
        if (!float.IsFinite(rowHeight) || rowHeight <= 0) throw new ArgumentOutOfRangeException(nameof(rowHeight));
        _composition = composition;
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        Region = factory is null ? composition.Child(viewport, name) : factory.Child(viewport, name);
        _source = source; _key = key; _content = content; Theme = theme ?? throw new ArgumentNullException(nameof(theme)); RowHeight = rowHeight;
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
        if (!float.IsFinite(rowHeight) || rowHeight <= 0) throw new ArgumentOutOfRangeException(nameof(rowHeight));
        if (IsDisposed || RowHeight == rowHeight) return;
        RowHeight = rowHeight;
        Region.UpdateControl(LayoutProperties.VirtualRowHeight, rowHeight);
        foreach (var entry in _entries.Values) entry.UpdateControl(LayoutProperties.Height, rowHeight);
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
        if (IsDisposed) return;
        var next = items.ToArray(); var keys = new TKey[next.Length]; var unique = new HashSet<TKey>();
        for (var index = 0; index < next.Length; index++)
        {
            var key = _key!(next[index]);
            if (!unique.Add(key)) throw new ArgumentException("Virtualized region keys must be unique.", nameof(items));
            keys[index] = key;
        }
        _items = next; _keys = keys;
        Region.UpdateControl(LayoutProperties.VirtualItemCount, next.Length);
    }

    void IVirtualizedRegion.Realize(LayoutViewport viewport) => Realize(viewport);
    public void Realize(LayoutViewport viewport)
    {
        _composition.CheckThread();
        _composition.RejectForeignFactory(Region);
        if (IsDisposed) return;
        viewport.Validate();
        if (_updating) throw new InvalidOperationException("A virtualized region cannot realize reentrantly.");
        var outerWidth = _viewport.Resolve(LayoutProperties.Width).Value ?? viewport.Width;
        var outerHeight = _viewport.Resolve(LayoutProperties.Height).Value ?? viewport.Height;
        var viewportHeight = SceneLayout.ContentBounds(LayoutRect.Round(0, 0, outerWidth, outerHeight, viewport.Scale), _viewport.Resolve(LayoutProperties.Padding).Value, viewport.Scale).Height;
        var offset = _viewport.Resolve(LayoutProperties.Scroll).Value.Y;
        var first = Math.Max(0, (int)MathF.Floor(offset / RowHeight) - Overscan);
        var last = Math.Min(_items.Length, (int)MathF.Ceiling((offset + viewportHeight) / RowHeight) + Overscan);
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
                    if (retained.ContainsKey(_keys[index])) continue;
                    var context = new CompositionContext(_composition, Region, Theme);
                    provisional.Add((_keys[index], null!, context));
                    var entry = context.Run(() => _content!(_items[index], context));
                    context.Validate(entry);
                    if (!entry.HasPresentation) entry.Present(Theme);
                    entry.UpdateControl(LayoutProperties.Height, RowHeight);
                    entry.UpdateControl(LayoutProperties.VirtualRowIndex, index);
                    provisional[^1] = (_keys[index], entry, context);
                }
            }
            catch (Exception error)
            {
                var errors = new List<Exception> { error };
                foreach (var entry in provisional)
                    try { entry.Context.Dispose(); } catch (Exception cleanup) { errors.Add(cleanup); }
                Composition.ThrowAll(errors, "Virtualized region factory failed.");
                throw;
            }

            var next = new Dictionary<TKey, Element>();
            var ordered = new List<Element>(last - first);
            for (var index = first; index < last; index++)
            {
                var key = _keys[index];
                var entry = retained.TryGetValue(key, out var current) ? current : provisional.Single(value => EqualityComparer<TKey>.Default.Equals(value.Key, key)).Element;
                entry.UpdateControl(LayoutProperties.VirtualRowIndex, index);
                next.Add(key, entry); ordered.Add(entry);
            }
            foreach (var entry in provisional) entry.Context.Complete();
            var departed = _entries.Where(pair => !wanted.Contains(pair.Key)).Select(pair => pair.Value).ToArray();
            _entries = next;
            Region.ReplaceChildren(ordered);
            foreach (var entry in provisional) entry.Context.Dispose();
            List<Exception>? cleanupErrors = null;
            foreach (var entry in departed)
                try { entry.Dispose(); } catch (Exception error) { (cleanupErrors ??= []).Add(error); }
            Composition.ThrowAll(cleanupErrors, "Virtualized region cleanup failed.");
        }
        finally { _updating = false; }
    }

    public void Dispose()
    {
        _composition.CheckThread();
        _composition.ThrowIfBehaviorAttachment();
        _composition.RejectForeignFactory(Region);
        if (IsDisposed) return;
        IsDisposed = true; _composition.Unregister(this);
        List<Exception>? errors = null;
        try { _effect.Dispose(); } catch (Exception error) { errors = [error]; }
        var entries = _entries.Values.ToArray(); _entries.Clear();
        if (!Region.IsDisposed) Region.ReplaceChildren([]);
        foreach (var entry in entries.Reverse())
            try { entry.Dispose(); } catch (Exception error) { (errors ??= []).Add(error); }
        _source = null; _key = null; _content = null; _items = []; _keys = [];
        Region.Scope.Detach(this);
        Composition.ThrowAll(errors, "Virtualized region cleanup failed.");
    }
}
