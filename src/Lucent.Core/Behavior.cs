using System.Numerics;
using System.Text;

namespace Lucent.Core;

/// <summary>Exclusive capabilities a behavior claims while attaching to an element.</summary>
[Flags]
public enum BehaviorOwnership
{
    /// <summary>Claims no exclusive behavior capability.</summary>
    None = 0,

    /// <summary>Claims exclusive focus registration.</summary>
    Focus = 1,

    /// <summary>Claims exclusive pointer, key, and text routing.</summary>
    Action = 2,

    /// <summary>Claims exclusive semantic declaration ownership.</summary>
    Semantics = 4,
}

/// <summary>Visual interaction states contributed by attached behaviors.</summary>
public enum BehaviorState
{
    /// <summary>Marks the active focus target.</summary>
    Focused,

    /// <summary>Requests keyboard-visible focus treatment.</summary>
    FocusVisible,

    /// <summary>Marks an active press.</summary>
    Pressed,

    /// <summary>Marks selection by a containing control.</summary>
    Selected,
}

/// <summary>Portable accessibility roles emitted in retained semantic snapshots.</summary>
public enum SemanticRole
{
    /// <summary>Exposes a noninteractive container.</summary>
    Group,

    /// <summary>Exposes static text.</summary>
    Text,

    /// <summary>Exposes editable single-line text.</summary>
    TextField,

    /// <summary>Exposes an invokable action.</summary>
    Button,

    /// <summary>Exposes a selectable-item container.</summary>
    List,

    /// <summary>Exposes a selectable list entry.</summary>
    ListItem,

    /// <summary>Exposes noninteractive status text.</summary>
    Status,
}

/// <summary>Semantic commands a retained target declares that it handles.</summary>
[Flags]
public enum SemanticAction
{
    /// <summary>Declares no semantic commands.</summary>
    None = 0,

    /// <summary>Permits invocation.</summary>
    Invoke = 1,

    /// <summary>Permits setting a value.</summary>
    SetValue = 2,

    /// <summary>Permits selection.</summary>
    Select = 4,

    /// <summary>Permits bounded scrolling.</summary>
    Scroll = 8,
}

/// <summary>Finite portable requests accepted by retained semantic behaviors; platform adapters never receive control state.</summary>
public enum SemanticCommandKind
{
    /// <summary>Requests focus.</summary>
    Focus,

    /// <summary>Requests activation.</summary>
    Invoke,

    /// <summary>Requests a replacement value.</summary>
    SetValue,

    /// <summary>Requests selection.</summary>
    Select,

    /// <summary>Requests a scroll delta or endpoint.</summary>
    Scroll,
}

/// <summary>Named destinations for semantic scrolling.</summary>
public enum SemanticScrollEndpoint
{
    /// <summary>Uses the supplied scroll delta.</summary>
    None,

    /// <summary>Moves to the minimum scroll offset.</summary>
    Start,

    /// <summary>Moves to the maximum scroll offset.</summary>
    End,
}

/// <summary>A portable semantic request; scroll deltas are logical pixels and apply only to scroll commands.</summary>
public readonly record struct SemanticCommand(
    SemanticCommandKind Kind,
    string? Value = null,
    float Horizontal = 0,
    float Vertical = 0,
    SemanticScrollEndpoint Endpoint = SemanticScrollEndpoint.None
)
{
    /// <summary>Validates the value and throws when its fields are outside the supported contract.</summary>
    public void Validate()
    {
        if (
            !Enum.IsDefined(Kind)
            || !Enum.IsDefined(Endpoint)
            || !float.IsFinite(Horizontal)
            || !float.IsFinite(Vertical)
            || (Kind != SemanticCommandKind.SetValue && Value is not null)
            || (
                Kind != SemanticCommandKind.Scroll
                && (Horizontal != 0 || Vertical != 0 || Endpoint != SemanticScrollEndpoint.None)
            )
            || (
                Kind == SemanticCommandKind.Scroll
                && Endpoint != SemanticScrollEndpoint.None
                && (Horizontal != 0 || Vertical != 0)
            )
        )
            throw new ArgumentException(
                "Semantic commands must be finite and match their declared operation."
            );
    }
}

/// <summary>Outcome of dispatching a semantic command to a retained target.</summary>
public enum SemanticCommandResult
{
    /// <summary>The current enabled target accepted the command.</summary>
    Applied,

    /// <summary>The target or command is not eligible.</summary>
    Rejected,

    /// <summary>The retained identity is no longer current.</summary>
    Stale,

    /// <summary>The current target is disabled.</summary>
    Disabled,
}

/// <summary>Accessible state supplied by a behavior for one retained element.</summary>
public sealed class SemanticDeclaration
{
    /// <summary>Initializes the accessible role, name, state, and declared commands for an element.</summary>
    public SemanticDeclaration(
        SemanticRole role,
        string name,
        bool enabled = true,
        bool focused = false,
        bool selected = false,
        SemanticAction actions = SemanticAction.None,
        string? value = null
    )
    {
        if (
            !Enum.IsDefined(role)
            || (
                (uint)actions
                & ~(uint)(
                    SemanticAction.Invoke
                    | SemanticAction.SetValue
                    | SemanticAction.Select
                    | SemanticAction.Scroll
                )
            ) != 0
        )
            throw new ArgumentException("Semantic role/actions must be finite.");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A semantic name is required.", nameof(name));
        Role = role;
        Name = name;
        Enabled = enabled;
        Focused = focused;
        Selected = selected;
        Actions = actions;
        Value = value;
    }

    /// <summary>Gets the accessible role.</summary>
    public SemanticRole Role { get; }

    /// <summary>Gets the nonempty accessible name.</summary>
    public string Name { get; }

    /// <summary>Gets whether semantic actions are currently enabled.</summary>
    public bool Enabled { get; }

    /// <summary>Gets whether the element is currently focused.</summary>
    public bool Focused { get; }

    /// <summary>Gets whether the element is currently selected.</summary>
    public bool Selected { get; }

    /// <summary>Gets the semantic commands declared by the element.</summary>
    public SemanticAction Actions { get; }

    /// <summary>Gets the optional accessible value.</summary>
    public string? Value { get; }
}

/// <summary>Stable identity for one semantic snapshot generation; commands become stale when any component changes.</summary>
public readonly record struct SemanticIdentity(
    long CompositionEpoch,
    long ElementId,
    long Generation
);

/// <summary>Immutable accessible subtree exported from the current retained composition.</summary>
public sealed record SemanticSnapshot(
    SemanticIdentity Identity,
    SemanticRole Role,
    string Name,
    string? Value,
    bool Enabled,
    bool Focused,
    bool Selected,
    SemanticAction Actions,
    IReadOnlyList<SemanticSnapshot> Children
);

/// <summary>Reusable interaction capability that owns input, focus, semantics, and cleanup for one element.</summary>
public abstract class Behavior
{
    /// <summary>Gets the diagnostic name used in retained dumps.</summary>
    public abstract string Name { get; }

    /// <summary>Gets the exclusive capabilities required by this behavior.</summary>
    public virtual BehaviorOwnership Ownership => BehaviorOwnership.None;

    /// <summary>Attaches this behavior and registers its owned capabilities with the supplied element context.</summary>
    public abstract void Attach(BehaviorContext context);
}

/// <summary>Behavior-only capability surface: identity, deterministic state, semantics, and scope-owned cleanup.</summary>
public sealed class BehaviorContext
{
    private readonly Composition _composition;
    private readonly ReactiveScope _scope;
    private readonly Dictionary<BehaviorState, bool> _state = [];
    private bool _attaching = true;
    private SemanticDeclaration? _semantic;
    private Func<SemanticCommand, bool>? _semanticCommand;
    private Action<bool>? _selectionChanged;
    private readonly Action _stateChanged;

    internal BehaviorContext(
        long elementId,
        Composition composition,
        ReactiveScope scope,
        Behavior behavior,
        Action stateChanged
    )
    {
        ElementId = elementId;
        _composition = composition;
        _scope = scope;
        Behavior = behavior;
        _stateChanged = stateChanged;
    }

    /// <summary>Gets the composition-local element identifier for registrations.</summary>
    public long ElementId { get; }
    internal ElementIdentity Identity => new(_composition.Epoch, ElementId);

    internal InputRouter CompositionInput() => _composition.Input;

    internal bool SelectSemantic() => _composition.SelectSemantic(Identity);

    /// <summary>Gets the behavior currently being attached.</summary>
    public Behavior Behavior { get; }

    /// <summary>Registers cleanup that runs when the behavior-owned scope is disposed.</summary>
    public void OnDispose(Action cleanup)
    {
        CheckAttachment();
        ArgumentNullException.ThrowIfNull(cleanup);
        _scope.OnDispose(() => _composition.RunBehaviorCleanup(cleanup));
    }

    /// <summary>Registers a disposable resource in the behavior-owned scope and returns it.</summary>
    public T Own<T>(T value)
        where T : IDisposable
    {
        CheckAttachment();
        ArgumentNullException.ThrowIfNull(value);
        _scope.Own(new GuardedDisposable(_composition, value));
        return value;
    }

    /// <summary>Updates behavior-owned visual state. Router callbacks may call this after attachment.</summary>
    public void SetState(BehaviorState state, bool value)
    {
        _composition.CheckThread();
        CheckLive();
        if (!Enum.IsDefined(state))
            throw new ArgumentException("Behavior state must be finite.", nameof(state));
        if (_state.GetValueOrDefault(state) == value)
            return;
        _state[state] = value;
        _stateChanged();
    }

    /// <summary>Declares the element semantics; only a semantic-owning behavior may call this during attachment.</summary>
    public void SetSemantics(SemanticDeclaration semantics)
    {
        CheckAttachment();
        if (!Behavior.Ownership.HasFlag(BehaviorOwnership.Semantics))
            throw new InvalidOperationException("Only semantic ownership can declare semantics.");
        _semantic = semantics ?? throw new ArgumentNullException(nameof(semantics));
    }

    /// <summary>Registers one portable semantic command handler. Its commands must be declared by this behavior's semantics.</summary>
    public void OnSemanticCommand(Func<SemanticCommand, bool> handler)
    {
        CheckAttachment();
        if (_semantic is null || _semanticCommand is not null)
            throw new InvalidOperationException(
                "Semantic commands require exactly one prior semantic declaration."
            );
        ArgumentNullException.ThrowIfNull(handler);
        _semanticCommand = handler;
    }

    /// <summary>Registers the one behavior-owned selection writer used by a containing semantic list.</summary>
    public void OnSelectionChanged(Action<bool> handler)
    {
        CheckAttachment();
        if (_semantic is null || _selectionChanged is not null)
            throw new InvalidOperationException(
                "Selection requires one semantic declaration and one writer."
            );
        _selectionChanged = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    internal void UpdateSemantics(SemanticDeclaration semantics)
    {
        CheckLive();
        _semantic = semantics ?? throw new ArgumentNullException(nameof(semantics));
        _composition
            .Find(new ElementIdentity(_composition.Epoch, ElementId))
            ?.UpdateControlSemantics(semantics);
    }

    /// <summary>Registers a pointer callback for this action-owning behavior.</summary>
    public void OnPointer(Action<PointerRoute> handler)
    {
        CheckInputOwnership();
        _composition.Input.RegisterPointer(ElementId, _scope, handler);
    }

    /// <summary>Registers a key callback for this action-owning behavior.</summary>
    public void OnKey(Action<KeyRoute> handler)
    {
        CheckInputOwnership();
        _composition.Input.RegisterKey(ElementId, _scope, handler);
    }

    /// <summary>Registers a focus callback for this focus-owning behavior.</summary>
    public void OnFocus(Action<FocusRoute> handler)
    {
        CheckFocusOwnership();
        _composition.Input.RegisterFocus(ElementId, _scope, handler);
    }

    /// <summary>Registers a text callback for this action-owning behavior.</summary>
    public void OnText(Action<TextRoute> handler)
    {
        CheckInputOwnership();
        _composition.Input.RegisterText(ElementId, _scope, handler);
    }

    /// <summary>Registers a pointer-capture-loss callback for this action-owning behavior.</summary>
    public void OnCaptureLost(Action<PointerCaptureLoss> handler)
    {
        CheckInputOwnership();
        _composition.Input.RegisterCaptureLoss(ElementId, _scope, handler);
    }

    /// <summary>Registers the element as a focus target and optionally as a tab stop.</summary>
    public void MakeFocusable(bool tabStop = true)
    {
        CheckFocusOwnership();
        _composition.Input.RegisterFocusable(ElementId, _scope, tabStop, this);
    }

    internal ReactiveEffect Effect(Action callback, string name)
    {
        CheckAttachment();
        return _scope.Effect(callback, name);
    }

    internal void RegisterScrollable(ScrollViewportState state)
    {
        CheckFocusOwnership();
        _composition.Input.RegisterScrollable(
            ElementId,
            _scope,
            state ?? throw new ArgumentNullException(nameof(state))
        );
    }

    internal void RegisterText(TextFieldState state)
    {
        CheckFocusOwnership();
        _composition.Input.RegisterTextField(
            ElementId,
            _scope,
            state ?? throw new ArgumentNullException(nameof(state))
        );
    }

    internal SemanticDeclaration? Semantics => _semantic;
    internal Func<SemanticCommand, bool>? SemanticCommand => _semanticCommand;
    internal Action<bool>? SelectionChanged => _selectionChanged;
    internal IReadOnlyDictionary<BehaviorState, bool> State => _state;

    internal void Complete() => _attaching = false;

    private void CheckAttachment()
    {
        CheckLive();
        if (!_attaching)
            throw new InvalidOperationException(
                "Behavior registration is only valid while attaching."
            );
    }

    private void CheckLive()
    {
        ObjectDisposedException.ThrowIf(_scope.IsDisposed, typeof(BehaviorContext));
    }

    private void CheckInputOwnership()
    {
        CheckAttachment();
        if ((Behavior.Ownership & (BehaviorOwnership.Action | BehaviorOwnership.Focus)) == 0)
            throw new InvalidOperationException(
                "Input handlers require action or focus ownership."
            );
    }

    private void CheckFocusOwnership()
    {
        CheckAttachment();
        if (!Behavior.Ownership.HasFlag(BehaviorOwnership.Focus))
            throw new InvalidOperationException("Focus handlers require focus ownership.");
    }

    private sealed class GuardedDisposable(Composition composition, IDisposable value) : IDisposable
    {
        private IDisposable? _value = value;

        public void Dispose()
        {
            var value = Interlocked.Exchange(ref _value, null);
            if (value is not null)
                composition.RunBehaviorCleanup(value.Dispose);
        }
    }
}
