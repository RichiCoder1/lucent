using System.Globalization;
using System.Text;

namespace Lucent.Core;

public static class InputProperties
{
    public static readonly Property<bool> Enabled = new("input-enabled", true);
    public static readonly Property<bool> Visible = new("input-visible", true);
}

public enum PointerCommandKind
{
    Down,
    Move,
    Up,
    Cancel,
}

public enum PointerButton
{
    None,
    Primary,
    Secondary,
    Middle,
}

[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Meta = 8,
}

public enum KeyCommandKind
{
    Down,
    Up,
}

public enum Key
{
    Tab,
    Enter,
    Space,
    Escape,
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    Backspace,
    Delete,
    A,
    C,
    V,
    X,
    Y,
    Z,
}

public enum FocusTraversalDirection
{
    Next,
    Previous,
}

public enum InputModality
{
    None,
    Pointer,
    Keyboard,
}

public enum FocusChangeReason
{
    Pointer,
    Keyboard,
    Traversal,
    Disposed,
    Disabled,
    Hidden,
    Reordered,
    SceneChanged,
}

public enum FocusCommandKind
{
    Gained,
    Lost,
}

public enum PointerCaptureLossReason
{
    Released,
    Cancelled,
    Disposed,
    Disabled,
    Hidden,
    SceneChanged,
}

public enum InputDispatchStatus
{
    Delivered,
    Rejected,
}

public enum InputRejection
{
    None,
    NoScene,
    StaleScene,
    NoTarget,
    Ineligible,
    Reentrant,
}

public readonly record struct PointerCommand(
    PointerCommandKind Kind,
    int PointerId,
    float X,
    float Y,
    PointerButton Button = PointerButton.None
)
{
    public void Validate()
    {
        if (
            !Enum.IsDefined(Kind)
            || !Enum.IsDefined(Button)
            || PointerId < 0
            || !float.IsFinite(X)
            || !float.IsFinite(Y)
            || (Kind == PointerCommandKind.Down && Button == PointerButton.None)
            || (Kind != PointerCommandKind.Down && Button != PointerButton.None)
        )
            throw new ArgumentException(
                "Pointer commands require finite logical coordinates and a button only on down."
            );
    }
}

public readonly record struct KeyCommand(
    KeyCommandKind Kind,
    Key Key,
    KeyModifiers Modifiers = KeyModifiers.None,
    bool IsRepeat = false
)
{
    public void Validate()
    {
        if (
            !Enum.IsDefined(Kind)
            || !Enum.IsDefined(Key)
            || (
                (uint)Modifiers
                & ~(uint)(
                    KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta
                )
            ) != 0
            || IsRepeat && Kind != KeyCommandKind.Down
        )
            throw new ArgumentException(
                "Key commands require finite portable key values; only downs may repeat."
            );
    }
}

public readonly record struct FocusCommand(
    FocusCommandKind Kind,
    FocusChangeReason Reason,
    InputModality Modality
);

public readonly record struct PointerCaptureLoss(
    int PointerId,
    PointerCaptureLossReason Reason,
    ElementIdentity Owner
);

public sealed class InputDispatchResult
{
    internal InputDispatchResult(
        InputDispatchStatus status,
        InputRejection rejection,
        ElementIdentity? target,
        IEnumerable<ElementIdentity> route,
        bool handled
    )
    {
        Status = status;
        Rejection = rejection;
        Target = target;
        Route = Array.AsReadOnly(route.ToArray());
        Handled = handled;
    }

    public InputDispatchStatus Status { get; }
    public InputRejection Rejection { get; }
    public ElementIdentity? Target { get; }
    public IReadOnlyList<ElementIdentity> Route { get; }
    public bool Handled { get; }
}

public sealed class PointerRoute
{
    private readonly InputRouter _router;
    private readonly List<Exception> _errors;
    private bool _active = true;
    private bool _handled;

    internal PointerRoute(
        InputRouter router,
        PointerCommand command,
        ElementIdentity target,
        ElementIdentity current,
        IEnumerable<ElementIdentity> route,
        List<Exception> errors
    )
    {
        _router = router;
        _errors = errors;
        Command = command;
        Target = target;
        CurrentTarget = current;
        Route = Array.AsReadOnly(route.ToArray());
    }

    public PointerCommand Command { get; }
    public ElementIdentity Target { get; }
    public ElementIdentity CurrentTarget { get; }
    public IReadOnlyList<ElementIdentity> Route { get; }

    /// <summary>Whether this callback's retained element contains the pointer coordinates.</summary>
    public bool IsInsideCurrentTarget
    {
        get
        {
            Check();
            return _router.Contains(CurrentTarget, Command.X, Command.Y);
        }
    }
    public bool Handled
    {
        get
        {
            Check();
            return _handled;
        }
        set
        {
            Check();
            _handled = value;
        }
    }

    public bool Capture()
    {
        Check();
        return _router.TryCapture(
            Command.PointerId,
            CurrentTarget,
            Command.Kind == PointerCommandKind.Down
        );
    }

    public void Focus()
    {
        Check();
        _router.RequestFocus(CurrentTarget, FocusChangeReason.Pointer, _errors);
    }

    internal bool Finish()
    {
        _active = false;
        return _handled;
    }

    private void Check()
    {
        if (!_active)
            throw new InvalidOperationException(
                "A routed pointer context expires when its callback returns."
            );
    }
}

public sealed class KeyRoute
{
    private readonly InputRouter _router;
    private bool _active = true;
    private bool _handled;

    internal KeyRoute(
        InputRouter router,
        KeyCommand command,
        ElementIdentity target,
        ElementIdentity current,
        IEnumerable<ElementIdentity> route
    )
    {
        _router = router;
        Command = command;
        Target = target;
        CurrentTarget = current;
        Route = Array.AsReadOnly(route.ToArray());
    }

    public KeyCommand Command { get; }
    public ElementIdentity Target { get; }
    public ElementIdentity CurrentTarget { get; }
    public IReadOnlyList<ElementIdentity> Route { get; }
    public bool Handled
    {
        get
        {
            Check();
            return _handled;
        }
        set
        {
            Check();
            _handled = value;
        }
    }

    public bool ScrollBy(float horizontal, float vertical)
    {
        Check();
        return _router.ScrollBy(CurrentTarget, horizontal, vertical);
    }

    public bool ScrollToStart()
    {
        Check();
        return _router.ScrollTo(CurrentTarget, default);
    }

    public bool ScrollToEnd()
    {
        Check();
        return _router.ScrollToEnd(CurrentTarget);
    }

    internal bool Finish()
    {
        _active = false;
        return _handled;
    }

    private void Check()
    {
        if (!_active)
            throw new InvalidOperationException(
                "A routed key context expires when its callback returns."
            );
    }
}

public sealed class FocusRoute
{
    internal FocusRoute(FocusCommand command, ElementIdentity target)
    {
        Command = command;
        Target = target;
    }

    public FocusCommand Command { get; }
    public ElementIdentity Target { get; }
}

public sealed class TextRoute
{
    private bool _active = true;
    private bool _handled;

    internal TextRoute(TextInputCommand command, ElementIdentity target)
    {
        Command = command;
        Target = target;
    }

    public TextInputCommand Command { get; }
    public ElementIdentity Target { get; }
    public bool Handled
    {
        get
        {
            Check();
            return _handled;
        }
        set
        {
            Check();
            _handled = value;
        }
    }

    internal bool Finish()
    {
        _active = false;
        return _handled;
    }

    private void Check()
    {
        if (!_active)
            throw new InvalidOperationException(
                "A routed text context expires when its callback returns."
            );
    }
}
