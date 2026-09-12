using System.Buffers;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Lucent.Core;

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
    private string? _supplementalDescription;
    private Func<SemanticCommand, bool>? _semanticCommand;
    private Action<bool>? _selectionChanged;
    private long _semanticGeneration;
    private long _acceptedSelectionGeneration;
    private bool _inputDisabled;
    private EffectiveSemanticState? _effectiveSemanticState;
    private long _nextRecipeOrdinal;
    private Dictionary<LayoutAlgorithm, Dictionary<object, object>>? _layoutAlgorithmStates;
    private LayoutAlgorithm? _activeLayoutAlgorithm;

    internal Element(
        Composition composition,
        Element? parent,
        ReactiveScope scope,
        long id,
        string name
    )
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

    /// <summary>Gets the stable identifier assigned at creation.</summary>
    public long Id { get; }

    /// <summary>Gets the diagnostic name assigned at creation.</summary>
    public string Name { get; }

    /// <summary>Gets the child reactive scope owned by this element.</summary>
    public ReactiveScope Scope { get; }

    /// <summary>Gets the immutable current child sequence.</summary>
    public IReadOnlyList<Element> Children => _childrenView;

    /// <summary>Gets whether this retained owner has released its children and reactive resources.</summary>
    public bool IsDisposed { get; private set; }
    internal Composition Composition { get; }
    internal Element? Parent => _parent;
    internal ElementPresentation? Presentation => _presentation;
    internal bool HasPresentation => _presentation is not null;
    internal bool IsConditionalRegion { get; set; }
    internal bool HasSemantics => _semantics is not null;
    internal string? SupplementalDescription => _supplementalDescription;
    internal StandardMenuPart StandardMenuPart { get; set; }
    internal ImageBinding? Image { get; set; }

    internal T GetOrCreateLayoutAlgorithmState<T>(LayoutAlgorithm algorithm, Func<T> create)
        where T : class
    {
        Composition.CheckThread();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _layoutAlgorithmStates ??= new(ReferenceEqualityComparer.Instance);
        if (!_layoutAlgorithmStates.TryGetValue(algorithm, out var byType))
        {
            byType = [];
            _layoutAlgorithmStates.Add(algorithm, byType);
        }
        if (byType.TryGetValue(LayoutStateKey<T>.Value, out var existing))
            return (T)existing;
        var created =
            Composition.RunLayoutCallback(create)
            ?? throw new InvalidOperationException("A layout state factory returned null.");
        byType.Add(LayoutStateKey<T>.Value, created);
        return created;
    }

    internal void ActivateLayoutAlgorithm(LayoutAlgorithm? algorithm)
    {
        Composition.CheckThread();
        if (ReferenceEquals(_activeLayoutAlgorithm, algorithm))
            return;
        var previous = _activeLayoutAlgorithm;
        _activeLayoutAlgorithm = algorithm;
        if (
            previous is null
            || _layoutAlgorithmStates is null
            || !_layoutAlgorithmStates.Remove(previous, out var states)
        )
            return;
        List<Exception>? errors = null;
        foreach (var disposable in states.Values.OfType<IDisposable>())
            try
            {
                Composition.RunLayoutCallback(() =>
                {
                    disposable.Dispose();
                    return true;
                });
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        Composition.ThrowAll(errors, "Layout algorithm state cleanup failed.");
    }

    private static class LayoutStateKey<T>
        where T : class
    {
        internal static object Value { get; } = new();
    }

    internal IEnumerable<IProperty> AncestorProperties()
    {
        for (var parent = _parent; parent is not null; parent = parent._parent)
            if (parent._presentation is not null)
                foreach (var property in parent._presentation.OwnProperties())
                    yield return property;
    }

    private static ResolvedProperty<T> Default<T>(Property<T> property) =>
        new(property.DefaultValue, new("default", 0), []);

    private static ResolvedProperty<T> Inherit<T>(
        Property<T> property,
        ResolvedProperty<T>? inherited
    ) =>
        inherited is null
            ? Default(property)
            : new(
                inherited.Value,
                new("inherited", inherited.Winner.Ordinal),
                [new PropertyProvenance("default", 0)]
            );

    /// <summary>Associates the one typed property model with this retained element.</summary>
    public void Present(ThemeContext theme, Style? component = null, Style? author = null)
    {
        ValidatePresentation(theme, component, author);
        _presentation = new ElementPresentation(
            this,
            theme,
            component ?? Style.Empty,
            author ?? Style.Empty
        );
        if (Composition.IsReachable(this))
            Composition.InvalidateInputProjection();
    }

    /// <summary>Checks presentation inputs without allocating reactive presentation state.</summary>
    internal void ValidatePresentation(
        ThemeContext theme,
        Style? component = null,
        Style? author = null
    )
    {
        Composition.CheckThread();
        Composition.ValidateFactoryMutation(this);
        Composition.ThrowIfBehaviorAttachment();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(theme);
        if (!ReferenceEquals(theme.Graph, Composition.Graph))
            throw new ArgumentException(
                "Theme context belongs to another reactive graph.",
                nameof(theme)
            );
        if (!theme.Scope.DescendsFrom(Composition.Root.Scope))
            throw new ArgumentException(
                "Theme context must be owned by this composition.",
                nameof(theme)
            );
        theme.ValidateLive();
        if (_presentation is not null)
            throw new InvalidOperationException("An element has one presentation model.");
        ElementPresentation.Validate(component ?? Style.Empty, author ?? Style.Empty);
    }

    /// <summary>Updates finite interaction state without creating a second modifier model.</summary>
    public void SetVariants(VariantState variants)
    {
        Composition.CheckThread();
        Composition.ValidateFactoryMutation(this);
        ThrowIfDisposed();
        (
            _presentation
            ?? throw new InvalidOperationException(
                "An element needs a presentation before it can have variants."
            )
        ).SetVariants(variants);
    }

    /// <summary>Resolves a presentation property from the current retained style and theme.</summary>
    public ResolvedProperty<T> Resolve<T>(Property<T> property)
    {
        Composition.CheckThread();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(property);
        if (!property.Inherits)
            return _presentation is null
                ? Default(property)
                : _presentation.ResolveLocal(property, null);

        var lineage = new Stack<Element>();
        for (Element? current = this; current is not null; current = current._parent)
            lineage.Push(current);
        ResolvedProperty<T>? resolved = null;
        while (lineage.TryPop(out var current))
            resolved = current._presentation is null
                ? Inherit(property, resolved)
                : current._presentation.ResolveLocal(property, resolved);
        return resolved!;
    }

    /// <summary>Resolves only the current value for Core hot paths that do not need provenance.</summary>
    internal T ResolveValue<T>(Property<T> property)
    {
        Composition.CheckThread();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(property);
        if (!property.Inherits)
            return _presentation is null
                ? property.DefaultValue
                : _presentation.ResolveValueLocal(property, false, default!);

        if (_parent is null)
            return _presentation is null
                ? property.DefaultValue
                : _presentation.ResolveValueLocal(property, false, default!);

        var depth = 0;
        for (Element? current = this; current is not null; current = current._parent)
            depth = checked(depth + 1);

        var lineage = ArrayPool<Element>.Shared.Rent(depth);
        try
        {
            var index = depth;
            for (Element? current = this; current is not null; current = current._parent)
                lineage[--index] = current;

            var hasInherited = false;
            var value = default(T)!;
            for (index = 0; index < depth; index++)
            {
                var current = lineage[index];
                value = current._presentation is null
                    ? hasInherited
                        ? value
                        : property.DefaultValue
                    : current._presentation.ResolveValueLocal(property, hasInherited, value);
                hasInherited = true;
            }
            return value;
        }
        finally
        {
            ArrayPool<Element>.Shared.Return(lineage, clearArray: true);
        }
    }

    internal bool CommitPresentationTargets(MotionTimeline timeline)
    {
        if (_presentation is not null)
            return _presentation.CommitPresentationTargets(timeline);
        var changed =
            timeline.Commit(
                this,
                VisualProperties.Background,
                VisualProperties.Background.DefaultValue,
                null,
                null,
                null,
                false,
                false,
                null,
                0
            )
            | timeline.Commit(
                this,
                VisualProperties.Opacity,
                VisualProperties.Opacity.DefaultValue,
                null,
                null,
                null,
                false,
                false,
                null,
                0
            );
        if (_parent is null)
            changed |= timeline.Commit(
                this,
                TypographyProperties.TextColor,
                TypographyProperties.TextColor.DefaultValue,
                null,
                null,
                null,
                false,
                false,
                null,
                0
            );
        else
            timeline.Remove(this, TypographyProperties.TextColor);
        return changed;
    }

    internal void ValidatePresentationTargets()
    {
        if (_presentation is not null)
            _presentation.ValidatePresentationTargets();
        else
        {
            MotionTimeline.ValidateTarget(
                VisualProperties.Background.Transition,
                VisualProperties.Background.DefaultValue
            );
            MotionTimeline.ValidateTarget(
                VisualProperties.Opacity.Transition,
                VisualProperties.Opacity.DefaultValue
            );
            if (_parent is null)
                MotionTimeline.ValidateTarget(
                    TypographyProperties.TextColor.Transition,
                    TypographyProperties.TextColor.DefaultValue
                );
        }
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
                var scope = Scope.CreateBehaviorChild(Name + ".behavior." + behavior.Name);
                var context = new BehaviorContext(
                    this,
                    Composition,
                    scope,
                    behavior,
                    BehaviorStateChanged
                );
                provisional.Add((scope, context));
                Composition.RunBehavior(context, () => behavior.Attach(context));
                if (
                    behavior.Ownership.HasFlag(BehaviorOwnership.Semantics)
                    && context.Semantics is null
                )
                    throw new InvalidOperationException("Behavior requires semantics.");
                if (
                    context.Semantics is { Actions: not SemanticAction.None }
                    && !behavior.Ownership.HasFlag(BehaviorOwnership.Action)
                )
                    throw new InvalidOperationException(
                        "Semantic actions require action ownership."
                    );
                context.Complete();
            }
            var semanticContext = provisional.SingleOrDefault(item =>
                item.Context.Semantics is not null
            );
            if (semanticContext.Context?.Semantics is { } semantic)
            {
                SetSemantics(semantic);
                _semanticCommand = semanticContext.Context.SemanticCommand;
                _selectionChanged = semanticContext.Context.SelectionChanged;
            }
            foreach (var item in provisional)
            {
                var mount = new BehaviorMount(
                    item.Context.Behavior.Name,
                    item.Context.Behavior.Ownership,
                    item.Context.State
                );
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
                try
                {
                    Composition.RunBehaviorCleanup(item.Scope.Dispose);
                }
                catch (Exception cleanup)
                {
                    errors.Add(cleanup);
                }
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
            if ((claims & behavior.Ownership) != 0)
                throw new InvalidOperationException(
                    "Exclusive behavior ownership conflicts on this element."
                );
            claims |= behavior.Ownership;
        }
        return claims;
    }

    internal void Attach(Element child)
    {
        ThrowIfDisposed();
        if (!ReferenceEquals(child._parent, this))
            throw new InvalidOperationException(
                "An element can only be attached to its owning parent."
            );
        _children.Add(child);
        if (Composition.IsReachable(this))
        {
            Composition.RegisterSubtree(child);
            Composition.InvalidateInputProjection();
        }
    }

    internal void ReplaceChildren(IReadOnlyList<Element> children)
    {
        ThrowIfDisposed();
        var reachable = Composition.IsReachable(this);
        if (reachable)
            foreach (var child in _children)
                Composition.UnregisterSubtree(child);
        _children.Clear();
        _children.AddRange(children);
        if (reachable)
        {
            foreach (var child in _children)
                Composition.RegisterSubtree(child);
            Composition.InvalidateInputProjection();
        }
    }

    internal void Detach(Element child)
    {
        if (_children.Remove(child))
        {
            if (Composition.IsReachable(this))
            {
                Composition.UnregisterSubtree(child);
                Composition.InvalidateInputProjection();
            }
        }
    }

    internal void OnDisposed(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (IsDisposed)
        {
            callback();
            return;
        }
        _disposed += callback;
    }

    internal bool IsAncestorOf(Element child)
    {
        for (Element? current = child; current is not null; current = current._parent)
            if (ReferenceEquals(current, this))
                return true;
        return false;
    }

    internal long NextRecipeOrdinal() => checked(++_nextRecipeOrdinal);

    internal void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, typeof(Element));
    }

    internal SemanticSnapshot? CreateSemanticSnapshot(IReadOnlyList<SemanticSnapshot> children)
    {
        if (_semantics is null)
            return null;
        var state = ReconcileSemanticState();
        return new(
            new SemanticIdentity(Composition.Epoch, Id, _semanticGeneration),
            _semantics.Role,
            _semantics.Name,
            _semantics.Value,
            state.Enabled,
            state.Focused,
            state.Selected,
            _semantics.Actions,
            children,
            _semantics.Text,
            _semantics.Expanded,
            _semantics.Range,
            _semantics.Relationships,
            _semantics.ToggleState,
            _semantics.Selection,
            MergeDescription(_semantics.Description, _supplementalDescription),
            _semantics.PositionInSet,
            _semantics.SizeOfSet,
            _semantics.IsPassword,
            _semantics.Collection,
            _semantics.Level,
            _semantics.CollectionIndex,
            _semantics.Grid,
            _semantics.GridItem,
            _semantics.Announcement
        );
    }

    internal bool IsCurrent(SemanticIdentity identity)
    {
        if (_semantics is not null)
            _ = ReconcileSemanticState();
        return !IsDisposed
            && identity.Generation == _semanticGeneration
            && (_semantics is not null || _semanticGeneration == 0);
    }

    internal bool ExecuteSemanticCommand(SemanticCommand command)
    {
        if (_semantics is null || _semanticCommand is null || !Allows(command))
            return false;
        return _semanticCommand(command);
    }

    internal long AcceptedSelectionGeneration => _acceptedSelectionGeneration;

    internal void AcknowledgeSemanticSelectionApplied() =>
        _acceptedSelectionGeneration = checked(_acceptedSelectionGeneration + 1);

    internal SemanticRole? DeclaredSemanticRole => _semantics?.Role;
    internal bool HasSelectableSemantics =>
        _semantics?.Actions.HasFlag(SemanticAction.Select) == true && _selectionChanged is not null;

    internal bool SetSelected(bool value)
    {
        if (_selectionChanged is null)
            return false;
        _selectionChanged(value);
        return true;
    }

    internal bool SemanticEnabled() => _semantics is not null && ReconcileSemanticState().Enabled;

    internal bool SemanticSelected() => _semantics is not null && ReconcileSemanticState().Selected;

    internal SemanticToggleState? SemanticToggleState => _semantics?.ToggleState;

    internal SemanticSnapshot CreateStructuralSemanticSnapshot(
        IReadOnlyList<SemanticSnapshot> children
    ) =>
        new(
            new SemanticIdentity(Composition.Epoch, Id, _semanticGeneration),
            SemanticRole.Group,
            Name,
            null,
            true,
            false,
            false,
            SemanticAction.None,
            children,
            Description: _supplementalDescription
        );

    internal void SetSupplementalDescription(string description)
    {
        Composition.CheckThread();
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException(
                "A supplemental semantic description is required.",
                nameof(description)
            );
        if (string.Equals(_supplementalDescription, description, StringComparison.Ordinal))
            return;
        _supplementalDescription = description;
        Composition.InvalidateSemantics();
    }

    private static string? MergeDescription(string? primary, string? supplemental)
    {
        if (string.IsNullOrWhiteSpace(supplemental))
            return primary;
        if (string.IsNullOrWhiteSpace(primary))
            return supplemental;
        return primary + Environment.NewLine + supplemental;
    }

    internal void AppendPresentationDump(StringBuilder dump)
    {
        _presentation?.AppendDump(dump);
        foreach (
            var behavior in _behaviors.OrderBy(behavior => behavior.Name, StringComparer.Ordinal)
        )
            dump.Append("  behavior name=")
                .Append(Quote(behavior.Name))
                .Append(" ownership=")
                .Append(behavior.Ownership)
                .Append(" state=[")
                .Append(
                    string.Join(
                        ',',
                        behavior
                            .State.OrderBy(item => item.Key)
                            .Select(item => item.Key + "=" + (item.Value ? "true" : "false"))
                    )
                )
                .Append("]\n");
        if (_semantics is not null)
        {
            var state = ReconcileSemanticState();
            dump.Append("  semantic element=")
                .Append(Id.ToString(CultureInfo.InvariantCulture))
                .Append(" generation=")
                .Append(_semanticGeneration.ToString(CultureInfo.InvariantCulture))
                .Append(" role=")
                .Append(_semantics.Role)
                .Append(" enabled=")
                .Append(state.Enabled ? "true" : "false")
                .Append(" focused=")
                .Append(state.Focused ? "true" : "false")
                .Append(" selected=")
                .Append(state.Selected ? "true" : "false")
                .Append(" actions=")
                .Append(_semantics.Actions)
                .Append('\n');
        }
    }

    private void SetSemantics(SemanticDeclaration semantics)
    {
        _semantics = semantics;
        _effectiveSemanticState = null;
        _semanticGeneration = checked(_semanticGeneration + 1);
        Composition.InvalidateSemantics();
    }

    private bool Allows(SemanticCommand command) =>
        command.Kind switch
        {
            SemanticCommandKind.Focus => true,
            SemanticCommandKind.Invoke => _semantics!.Actions.HasFlag(SemanticAction.Invoke),
            SemanticCommandKind.Toggle => _semantics!.Actions.HasFlag(SemanticAction.Toggle),
            SemanticCommandKind.SetValue => _semantics!.Actions.HasFlag(SemanticAction.SetValue),
            SemanticCommandKind.Select => _semantics!.Actions.HasFlag(SemanticAction.Select),
            SemanticCommandKind.Scroll => _semantics!.Actions.HasFlag(SemanticAction.Scroll),
            SemanticCommandKind.SelectText => _semantics!.Actions.HasFlag(
                SemanticAction.SelectText
            ),
            SemanticCommandKind.ScrollTextIntoView => _semantics!.Actions.HasFlag(
                SemanticAction.ScrollTextIntoView
            ),
            SemanticCommandKind.Expand or SemanticCommandKind.Collapse =>
                _semantics!.Actions.HasFlag(SemanticAction.ExpandCollapse),
            SemanticCommandKind.SetRangeValue => _semantics!.Actions.HasFlag(
                SemanticAction.SetRangeValue
            ),
            SemanticCommandKind.RealizeItem => _semantics!.Actions.HasFlag(
                SemanticAction.RealizeItem
            ),
            _ => false,
        };

    internal void UpdateControl<T>(Property<T> property, T value)
    {
        Composition.CheckThread();
        Composition.ThrowIfBehaviorAttachment();
        ThrowIfDisposed();
        (
            _presentation
            ?? throw new InvalidOperationException(
                "An element needs a presentation before control state can update it."
            )
        ).SetControl(property, value);
    }

    internal void UpdateControlSemantics(SemanticDeclaration semantics)
    {
        Composition.CheckThread();
        Composition.ThrowIfBehaviorAttachment();
        ThrowIfDisposed();
        if (_semantics is null)
            throw new InvalidOperationException(
                "An element needs semantic behavior before control state can update it."
            );
        SetSemantics(semantics ?? throw new ArgumentNullException(nameof(semantics)));
    }

    internal bool RefreshBehaviorVariants()
    {
        if (_presentation is null || IsDisposed)
            return false;
        var variants = VariantState.None;
        foreach (var behavior in _behaviors)
        {
            if (behavior.State.GetValueOrDefault(BehaviorState.Hover))
                variants |= VariantState.Hover;
            if (behavior.State.GetValueOrDefault(BehaviorState.Pressed))
                variants |= VariantState.Pressed;
            if (behavior.State.GetValueOrDefault(BehaviorState.Selected))
                variants |= VariantState.Selected;
            if (behavior.State.GetValueOrDefault(BehaviorState.FocusVisible))
                variants |= VariantState.FocusVisible;
            if (behavior.State.GetValueOrDefault(BehaviorState.Invalid))
                variants |= VariantState.Invalid;
        }
        if (_inputDisabled)
            variants |= VariantState.Disabled;
        if (!_presentation.SetBehaviorVariants(variants))
            return false;
        Composition.InvalidateInteractionVisuals();
        return true;
    }

    internal bool SetInputDisabledVariant(bool disabled)
    {
        if (_inputDisabled == disabled)
            return false;
        _inputDisabled = disabled;
        var visualChanged = RefreshBehaviorVariants();
        ReconcileSemanticState();
        return visualChanged;
    }

    private bool HasBehaviorState(BehaviorState state) =>
        _behaviors.Any(behavior => behavior.State.GetValueOrDefault(state));

    internal ElementParticipation Participation
    {
        get
        {
            var value = ResolveValue(VisualProperties.Participation);
            if (
                value
                is not (
                    ElementParticipation.Visible
                    or ElementParticipation.Hidden
                    or ElementParticipation.Collapsed
                )
            )
                throw new InvalidOperationException(
                    "Element participation must be visible, hidden, or collapsed."
                );
            return value;
        }
    }

    internal bool ParticipatesInInput() =>
        !IsDisposed
        && Participation == ElementParticipation.Visible
        && (_parent is null || _parent.ParticipatesInInput());

    internal bool InputAvailable() =>
        !IsDisposed
        && (_parent is null || _parent.InputAvailable())
        && ResolveValue(InputProperties.Enabled)
        && ResolveValue(InputProperties.Visible)
        && Participation == ElementParticipation.Visible;

    private void BehaviorStateChanged()
    {
        RefreshBehaviorVariants();
        _ = ReconcileSemanticState();
    }

    /// <summary>Single source of truth for exported semantic availability and behavior state.</summary>
    private EffectiveSemanticState ReconcileSemanticState()
    {
        if (_semantics is null)
            return default;
        var next = new EffectiveSemanticState(
            _semantics.Enabled && InputAvailable(),
            _semantics.Focused || HasBehaviorState(BehaviorState.Focused),
            _semantics.Selected || HasBehaviorState(BehaviorState.Selected)
        );
        if (_effectiveSemanticState is { } prior && prior != next)
        {
            _semanticGeneration = checked(_semanticGeneration + 1);
            Composition.InvalidateSemantics();
        }
        _effectiveSemanticState = next;
        return next;
    }

    internal void ReconcileSemanticStateForInput() => _ = ReconcileSemanticState();

    private static void ValidateOwnership(BehaviorOwnership ownership)
    {
        const BehaviorOwnership all =
            BehaviorOwnership.Focus | BehaviorOwnership.Action | BehaviorOwnership.Semantics;
        if (
            (ownership & ~all) != 0
            || (
                ownership.HasFlag(BehaviorOwnership.Action)
                && !ownership.HasFlag(BehaviorOwnership.Semantics)
            )
        )
            throw new ArgumentException(
                "Behavior ownership must use finite claims and actions require semantic ownership.",
                nameof(ownership)
            );
    }

    /// <summary>Releases this object's retained resources and owned reactive lifetime.</summary>
    public void Dispose()
    {
        Composition.CheckThread();
        Composition.ValidateFactoryMutation(this);
        Composition.ThrowIfBehaviorAttachment();
        if (IsDisposed)
            return;
        var reachable = Composition.IsReachable(this);
        IsDisposed = true;
        Composition.UnregisterSubtree(this);
        if (reachable)
            Composition.InvalidateInputProjection();
        List<Exception>? errors = null;
        try
        {
            Composition.InvalidateSemantics();
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
        }
        try
        {
            Composition.RemovePresentation(this);
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
        }
        try
        {
            if (Composition.InputIfCreated is { } input)
                input.RemoveElement(this, PointerCaptureLossReason.Disposed);
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
        }
        var children = _children.ToArray();
        _children.Clear();
        for (var index = children.Length - 1; index >= 0; index--)
        {
            try
            {
                children[index].Dispose();
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }
        if (_layoutAlgorithmStates is { } algorithmStates)
        {
            _layoutAlgorithmStates = null;
            foreach (var state in algorithmStates.Values.SelectMany(values => values.Values))
                if (state is IDisposable disposable)
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception exception)
                    {
                        (errors ??= []).Add(exception);
                    }
        }
        try
        {
            Scope.Dispose();
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
        }
        _parent?.Scope.Detach(this);
        _parent?.Detach(this);
        _parent = null;
        var disposed = _disposed;
        _disposed = null;
        try
        {
            disposed?.Invoke();
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
        }
        Composition.ThrowAll(errors, "Element cleanup failed.");
    }

    private sealed record BehaviorMount(
        string Name,
        BehaviorOwnership Ownership,
        IReadOnlyDictionary<BehaviorState, bool> State
    );

    private readonly record struct EffectiveSemanticState(
        bool Enabled,
        bool Focused,
        bool Selected
    );

    private static string Quote(string value) => DiagnosticText.Quote(value);
}
