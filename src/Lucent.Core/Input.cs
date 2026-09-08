using System.Globalization;
using System.Text;

namespace Lucent.Core;

/// <summary>Typed presentation properties that determine whether an element participates in routing and scene input.</summary>
public static class InputProperties
{
    /// <summary>Optional pointer appearance; Auto derives intent from the eligible control.</summary>
    public static readonly Property<CursorIntent> Cursor = new("input-cursor", CursorIntent.Auto);

    /// <summary>When false, the subtree cannot receive input; its visible bounds still block pointer activation behind it.</summary>
    public static readonly Property<bool> Enabled = new("input-enabled", true);

    /// <summary>When true, the subtree is skipped by pointer hit testing. Keyboard and semantic availability are unchanged.</summary>
    public static readonly Property<bool> PointerTransparent = new(
        "input-pointer-transparent",
        false
    );

    /// <summary>When false, the element is hidden from hit testing and input.</summary>
    public static readonly Property<bool> Visible = new("input-visible", true);
}

/// <summary>Portable pointer appearance resolved by the host.</summary>
public enum CursorIntent
{
    /// <summary>Use the eligible control's default.</summary>
    Auto,

    /// <summary>Use the ordinary arrow.</summary>
    Default,

    /// <summary>Indicate editable text.</summary>
    Text,

    /// <summary>Indicate a clickable target.</summary>
    Pointer,

    /// <summary>Indicate horizontal pane resizing.</summary>
    ResizeHorizontal,

    /// <summary>Indicate vertical pane resizing.</summary>
    ResizeVertical,
}

/// <summary>The portable phases of one pointer sequence.</summary>
public enum PointerCommandKind
{
    /// <summary>Starts a pointer sequence.</summary>
    Down,

    /// <summary>Updates an active pointer position.</summary>
    Move,

    /// <summary>Ends a pointer sequence normally.</summary>
    Up,

    /// <summary>Ends a pointer sequence without activation.</summary>
    Cancel,
}

/// <summary>The button carried only by a pointer-down command.</summary>
public enum PointerButton
{
    /// <summary>Carries no button outside pointer down.</summary>
    None,

    /// <summary>Identifies the primary pointing button.</summary>
    Primary,

    /// <summary>Identifies the secondary pointing button.</summary>
    Secondary,

    /// <summary>Identifies the middle pointing button.</summary>
    Middle,
}

/// <summary>Portable logical-pixel wheel or trackpad deltas at the current pointer position.</summary>
public readonly record struct WheelCommand(float X, float Y, float DeltaX, float DeltaY)
{
    /// <summary>Validates that the hit point and fractional deltas are finite.</summary>
    public void Validate()
    {
        if (
            !float.IsFinite(X)
            || !float.IsFinite(Y)
            || !float.IsFinite(DeltaX)
            || !float.IsFinite(DeltaY)
        )
            throw new ArgumentException(
                "Wheel commands require finite logical coordinates and deltas."
            );
    }
}

/// <summary>Portable modifier bits accompanying a key command.</summary>
[Flags]
public enum KeyModifiers
{
    /// <summary>Carries no modifier keys.</summary>
    None = 0,

    /// <summary>Carries Shift.</summary>
    Shift = 1,

    /// <summary>Carries Control.</summary>
    Control = 2,

    /// <summary>Carries Alt.</summary>
    Alt = 4,

    /// <summary>Carries the platform Meta key.</summary>
    Meta = 8,
}

/// <summary>The portable press and release phases of a key.</summary>
public enum KeyCommandKind
{
    /// <summary>Starts a key press.</summary>
    Down,

    /// <summary>Ends a key press.</summary>
    Up,
}

/// <summary>The bounded portable key set handled by Core controls.</summary>
public enum Key
{
    /// <summary>Opens the focused context menu with Shift.</summary>
    F10,

    /// <summary>Opens the focused context menu.</summary>
    ContextMenu,

    /// <summary>Moves focus to the next tab stop.</summary>
    Tab,

    /// <summary>Activates the focused control.</summary>
    Enter,

    /// <summary>Activates the focused control.</summary>
    Space,

    /// <summary>Cancels the current operation.</summary>
    Escape,

    /// <summary>Moves left.</summary>
    Left,

    /// <summary>Moves right.</summary>
    Right,

    /// <summary>Moves up.</summary>
    Up,

    /// <summary>Moves down.</summary>
    Down,

    /// <summary>Moves to the start.</summary>
    Home,

    /// <summary>Moves to the end.</summary>
    End,

    /// <summary>Deletes the preceding grapheme.</summary>
    Backspace,

    /// <summary>Deletes the following grapheme.</summary>
    Delete,

    /// <summary>Select-all shortcut key.</summary>
    A,

    /// <summary>Copy shortcut key.</summary>
    C,

    /// <summary>Find application shortcut key.</summary>
    F,

    /// <summary>Capture application shortcut key.</summary>
    N,

    /// <summary>Save application shortcut key.</summary>
    S,

    /// <summary>Paste shortcut key.</summary>
    V,

    /// <summary>Cut shortcut key.</summary>
    X,

    /// <summary>Redo shortcut key.</summary>
    Y,

    /// <summary>Undo shortcut key.</summary>
    Z,
}

/// <summary>The direction used when moving among retained tab stops.</summary>
public enum FocusTraversalDirection
{
    /// <summary>Moves to the next tab stop.</summary>
    Next,

    /// <summary>Moves to the previous tab stop.</summary>
    Previous,
}

/// <summary>The input source that established the current focus-visible state.</summary>
public enum InputModality
{
    /// <summary>No modality has established focus.</summary>
    None,

    /// <summary>Pointer input established focus.</summary>
    Pointer,

    /// <summary>Keyboard input established focus.</summary>
    Keyboard,
}

/// <summary>Why the router changed retained focus.</summary>
public enum FocusChangeReason
{
    /// <summary>A pointer route requested focus.</summary>
    Pointer,

    /// <summary>A keyboard route requested focus.</summary>
    Keyboard,

    /// <summary>Tab traversal selected the target.</summary>
    Traversal,

    /// <summary>The target was disposed.</summary>
    Disposed,

    /// <summary>The target became disabled.</summary>
    Disabled,

    /// <summary>The target became hidden.</summary>
    Hidden,

    /// <summary>The target lost its retained position.</summary>
    Reordered,

    /// <summary>The installed scene no longer contains the target.</summary>
    SceneChanged,
}

/// <summary>Whether a focus route announces gaining or losing focus.</summary>
public enum FocusCommandKind
{
    /// <summary>Notifies a target that it gained focus.</summary>
    Gained,

    /// <summary>Notifies a target that it lost focus.</summary>
    Lost,
}

/// <summary>Why the router revoked a pointer capture.</summary>
public enum PointerCaptureLossReason
{
    /// <summary>The pointer sequence ended normally.</summary>
    Released,

    /// <summary>The pointer sequence was cancelled.</summary>
    Cancelled,

    /// <summary>The capture owner was disposed.</summary>
    Disposed,

    /// <summary>The capture owner became disabled.</summary>
    Disabled,

    /// <summary>The capture owner became hidden.</summary>
    Hidden,

    /// <summary>The installed scene invalidated the owner.</summary>
    SceneChanged,
}

/// <summary>Whether a portable command reached a current retained route.</summary>
public enum InputDispatchStatus
{
    /// <summary>A current eligible route received the command.</summary>
    Delivered,

    /// <summary>No behavior callback was invoked.</summary>
    Rejected,
}

/// <summary>Why the router rejected a command without invoking behavior callbacks.</summary>
public enum InputRejection
{
    /// <summary>No rejection applies.</summary>
    None,

    /// <summary>No scene is installed.</summary>
    NoScene,

    /// <summary>The command refers to an older scene.</summary>
    StaleScene,

    /// <summary>Hit testing or focus found no target.</summary>
    NoTarget,

    /// <summary>The target is disabled, hidden, or ineligible.</summary>
    Ineligible,

    /// <summary>Nested dispatch was refused.</summary>
    Reentrant,
}

/// <summary>A pointer phase with finite logical-pixel coordinates; only down commands carry a button.</summary>
public readonly record struct PointerCommand(
    PointerCommandKind Kind,
    int PointerId,
    float X,
    float Y,
    PointerButton Button = PointerButton.None
)
{
    /// <summary>Validates the value and throws when its fields are outside the supported contract.</summary>
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

/// <summary>A portable key press or release; repeat is valid only for a key down.</summary>
public readonly record struct KeyCommand(
    KeyCommandKind Kind,
    Key Key,
    KeyModifiers Modifiers = KeyModifiers.None,
    bool IsRepeat = false
)
{
    /// <summary>Validates the value and throws when its fields are outside the supported contract.</summary>
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

/// <summary>A retained focus transition and the modality that established it.</summary>
public readonly record struct FocusCommand(
    FocusCommandKind Kind,
    FocusChangeReason Reason,
    InputModality Modality
);

/// <summary>Reason a pointer owner lost capture for one pointer identifier.</summary>
public readonly record struct PointerCaptureLoss(
    int PointerId,
    PointerCaptureLossReason Reason,
    ElementIdentity Owner
);

/// <summary>The deterministic result of routing one portable input command.</summary>
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

    /// <summary>Gets whether dispatch reached a route or was rejected.</summary>
    public InputDispatchStatus Status { get; }

    /// <summary>Gets the reason no callback ran when dispatch was rejected.</summary>
    public InputRejection Rejection { get; }

    /// <summary>Gets the hit-tested or focused target, when one was found.</summary>
    public ElementIdentity? Target { get; }

    /// <summary>Gets the immutable propagation path from target to root.</summary>
    public IReadOnlyList<ElementIdentity> Route { get; }

    /// <summary>Gets whether a route callback handled the command.</summary>
    public bool Handled { get; }
}

/// <summary>The per-callback view of a routed pointer command.</summary>
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

    /// <summary>Gets the portable command being delivered.</summary>
    public PointerCommand Command { get; }

    /// <summary>Gets the hit-tested or focused target, when one was found.</summary>
    public ElementIdentity Target { get; }

    /// <summary>Gets the element whose callback is currently running.</summary>
    public ElementIdentity CurrentTarget { get; }

    /// <summary>Gets the immutable propagation path from target to root.</summary>
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

    /// <summary>Gets or sets whether this callback handled the command before it expires.</summary>
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

    /// <summary>Attempts capture for this pointer; succeeds only during a pointer-down callback.</summary>
    public bool Capture()
    {
        Check();
        return _router.TryCapture(
            Command.PointerId,
            CurrentTarget,
            Command.Kind == PointerCommandKind.Down
        );
    }

    /// <summary>Requests pointer-established focus for the current callback target.</summary>
    public void Focus()
    {
        Check();
        _router.RequestFocus(CurrentTarget, FocusChangeReason.Pointer, _errors);
    }

    /// <summary>Requests pointer-established focus for the hit target while an ancestor callback is running.</summary>
    public void FocusTarget()
    {
        Check();
        _router.RequestFocus(Target, FocusChangeReason.Pointer, _errors);
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

/// <summary>The per-callback view of a routed key command.</summary>
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

    /// <summary>Gets the portable command being delivered.</summary>
    public KeyCommand Command { get; }

    /// <summary>Gets the hit-tested or focused target, when one was found.</summary>
    public ElementIdentity Target { get; }

    /// <summary>Gets the element whose callback is currently running.</summary>
    public ElementIdentity CurrentTarget { get; }

    /// <summary>Gets the immutable propagation path from target to root.</summary>
    public IReadOnlyList<ElementIdentity> Route { get; }

    /// <summary>Gets or sets whether this callback handled the command before it expires.</summary>
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

    /// <summary>Scrolls the current target by finite logical-pixel deltas.</summary>
    public bool ScrollBy(float horizontal, float vertical)
    {
        Check();
        return _router.ScrollBy(CurrentTarget, horizontal, vertical);
    }

    /// <summary>Scrolls the current target to its minimum offset.</summary>
    public bool ScrollToStart()
    {
        Check();
        return _router.ScrollTo(CurrentTarget, default);
    }

    /// <summary>Scrolls the current target to its maximum offset.</summary>
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

/// <summary>The per-callback view of a retained focus change.</summary>
public sealed class FocusRoute
{
    internal FocusRoute(FocusCommand command, ElementIdentity target)
    {
        Command = command;
        Target = target;
    }

    /// <summary>Gets the portable command being delivered.</summary>
    public FocusCommand Command { get; }

    /// <summary>Gets the hit-tested or focused target, when one was found.</summary>
    public ElementIdentity Target { get; }
}

/// <summary>The per-callback view of one portable text input command.</summary>
public sealed class TextRoute
{
    private bool _active = true;
    private bool _handled;

    internal TextRoute(TextInputCommand command, ElementIdentity target)
    {
        Command = command;
        Target = target;
    }

    /// <summary>Gets the portable command being delivered.</summary>
    public TextInputCommand Command { get; }

    /// <summary>Gets the hit-tested or focused target, when one was found.</summary>
    public ElementIdentity Target { get; }

    /// <summary>Gets or sets whether this callback handled the command before it expires.</summary>
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
