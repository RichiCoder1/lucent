using System.Numerics;
using System.Text;

namespace Lucent.Core;

[Flags]
public enum BehaviorOwnership
{
    None = 0,
    Focus = 1,
    Action = 2,
    Semantics = 4,
}

public enum BehaviorState
{
    Focused,
    FocusVisible,
    Pressed,
    Selected,
}

public enum SemanticRole
{
    Group,
    Text,
    TextField,
    Button,
    List,
    ListItem,
    Status,
}

[Flags]
public enum SemanticAction
{
    None = 0,
    Invoke = 1,
    SetValue = 2,
    Select = 4,
    Scroll = 8,
}

/// <summary>Finite portable requests accepted by retained semantic behaviors; platform adapters never receive control state.</summary>
public enum SemanticCommandKind
{
    Focus,
    Invoke,
    SetValue,
    Select,
    Scroll,
}

public enum SemanticScrollEndpoint
{
    None,
    Start,
    End,
}

public readonly record struct SemanticCommand(
    SemanticCommandKind Kind,
    string? Value = null,
    float Horizontal = 0,
    float Vertical = 0,
    SemanticScrollEndpoint Endpoint = SemanticScrollEndpoint.None
)
{
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

public enum SemanticCommandResult
{
    Applied,
    Rejected,
    Stale,
    Disabled,
}

public sealed class SemanticDeclaration
{
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

    public SemanticRole Role { get; }
    public string Name { get; }
    public bool Enabled { get; }
    public bool Focused { get; }
    public bool Selected { get; }
    public SemanticAction Actions { get; }
    public string? Value { get; }
}

public readonly record struct SemanticIdentity(
    long CompositionEpoch,
    long ElementId,
    long Generation
);

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

public abstract class Behavior
{
    public abstract string Name { get; }
    public virtual BehaviorOwnership Ownership => BehaviorOwnership.None;
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

    public long ElementId { get; }
    internal ElementIdentity Identity => new(_composition.Epoch, ElementId);

    internal InputRouter CompositionInput() => _composition.Input;

    internal bool SelectSemantic() => _composition.SelectSemantic(Identity);

    public Behavior Behavior { get; }

    public void OnDispose(Action cleanup)
    {
        CheckAttachment();
        ArgumentNullException.ThrowIfNull(cleanup);
        _scope.OnDispose(() => _composition.RunBehaviorCleanup(cleanup));
    }

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

    public void OnPointer(Action<PointerRoute> handler)
    {
        CheckInputOwnership();
        _composition.Input.RegisterPointer(ElementId, _scope, handler);
    }

    public void OnKey(Action<KeyRoute> handler)
    {
        CheckInputOwnership();
        _composition.Input.RegisterKey(ElementId, _scope, handler);
    }

    public void OnFocus(Action<FocusRoute> handler)
    {
        CheckFocusOwnership();
        _composition.Input.RegisterFocus(ElementId, _scope, handler);
    }

    public void OnText(Action<TextRoute> handler)
    {
        CheckInputOwnership();
        _composition.Input.RegisterText(ElementId, _scope, handler);
    }

    public void OnCaptureLost(Action<PointerCaptureLoss> handler)
    {
        CheckInputOwnership();
        _composition.Input.RegisterCaptureLoss(ElementId, _scope, handler);
    }

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
