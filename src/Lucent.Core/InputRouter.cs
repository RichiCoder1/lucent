using System.Globalization;
using System.Text;

namespace Lucent.Core;

/// <summary>Composition-owned UI-thread router. It accepts only retained Core metadata and portable commands.</summary>
public sealed partial class InputRouter
{
    private readonly Composition _composition;
    private readonly List<Registration<Action<PointerRoute>>> _pointer = [];
    private readonly List<Registration<Action<WheelRoute>>> _wheel = [];
    private readonly List<Registration<Action<KeyRoute>>> _key = [];
    private readonly List<Registration<Action<FocusRoute>>> _focus = [];
    private readonly List<Registration<Action<TextRoute>>> _text = [];
    private readonly List<Registration<Action<PointerCaptureLoss>>> _captureLoss = [];
    private readonly Dictionary<long, Focusable> _focusable = [];
    private readonly Dictionary<long, Scrollable> _scrollable = [];
    private readonly Dictionary<long, RetainedScrollBar> _scrollBars = [];
    private readonly Dictionary<long, TextFieldState> _textFields = [];
    private readonly Dictionary<FocusTarget, FocusTargetRegistration> _focusTargets = [];
    private readonly Dictionary<TextClipboardRequest, ClipboardTicket> _clipboardTickets = [];
    private readonly Dictionary<int, Capture> _captures = [];
    private readonly Dictionary<int, ScrollBarDrag> _scrollBarDrags = [];
    private RetainedScene? _scene;
    private Dictionary<long, RetainedInputElement> _input = [];
    private Dictionary<long, bool> _available = [];
    private Dictionary<long, bool> _pointerVisible = [];
    private readonly HashSet<long> _commandScopes = [];
    private Dictionary<long, ElementIdentity[]> _paths = [];
    private Dictionary<long, InputClip[]> _effectiveClips = [];
    private RetainedInputElement[] _hitOrder = [];
    private RetainedScrollBar[] _scrollBarHitOrder = [];
    private FocusState? _focused;
    private ElementIdentity? _hovered;
    private ElementIdentity? _hoveredScrollBar;
    private PendingFocus? _pendingFocus;
    private int _publicDepth;
    private bool _focusing;
    private bool _disposed;
    private long _nextRegistration;
    private InputModality _modality;
    private string _lastRoute =
        "route kind=None status=Rejected reason=NoScene handled=false target=- path=[]";
    private string _lastFocusLoss = "-";
    private string _lastCaptureLoss = "-";

    internal InputRouter(Composition composition)
    {
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
    }

    /// <summary>Gets the currently focused retained identity, if its installed scene still has one.</summary>
    public ElementIdentity? FocusedElement
    {
        get
        {
            Check();
            return _focused?.Identity;
        }
    }

    /// <summary>Whether the focused editor owns an active text composition; hosts use this to reconcile native IME cancellation.</summary>
    public bool HasTextComposition
    {
        get
        {
            Check();
            return _focused is { } focus
                && _textFields.TryGetValue(focus.Identity.ElementId, out var editor)
                && editor.HasPreedit;
        }
    }

    internal void RegisterCommandScope(long elementId, ReactiveScope scope)
    {
        _commandScopes.Add(elementId);
        scope.OnDispose(() => _commandScopes.Remove(elementId));
    }

    private ElementIdentity UnfocusedCommandTarget()
    {
        // Only an unambiguous outer scope acts as the application shortcut owner.
        // Sibling scopes need focus to choose their subtree; nested scopes never win by mount order.
        ElementIdentity? target = null;
        foreach (var id in _commandScopes)
        {
            var candidate = new ElementIdentity(_composition.Epoch, id);
            if (
                !Eligible(candidate)
                || Path(candidate)
                    .Any(ancestor =>
                        ancestor.ElementId != id && _commandScopes.Contains(ancestor.ElementId)
                    )
            )
                continue;
            if (target is not null)
                return new(_composition.Epoch, _composition.Root.Id);
            target = candidate;
        }
        return target ?? new(_composition.Epoch, _composition.Root.Id);
    }

    /// <summary>Gets the modality that last established focus-visible state.</summary>
    public InputModality Modality
    {
        get
        {
            Check();
            return _modality;
        }
    }

    /// <summary>Installs a newer retained scene and reconciles capture and focus against its current identities.</summary>
    public bool SetScene(RetainedScene scene)
    {
        Enter();
        try
        {
            ArgumentNullException.ThrowIfNull(scene);
            if (!ValidateScene(scene, installing: true))
            {
                var rejectedErrors = new List<Exception>();
                if (_scene is not null && EnsureScene(rejectedErrors) == InputRejection.StaleScene)
                    Throw(rejectedErrors);
                return false;
            }
            if (
                scene.IsPaintOnly
                && _scene is not null
                && ReferenceEquals(scene.Input, _scene.Input)
                && scene.PaintSnapshot is { } paintSnapshot
                && paintSnapshot.CanReuseInput(_composition)
            )
            {
                _scene = scene;
                foreach (var capture in _captures.ToArray())
                    _captures[capture.Key] = capture.Value with { Generation = scene.Generation };
                return true;
            }
            var priorInput = _input;
            var priorScene = _scene;
            var nextInput = scene.Input.ToDictionary(item => item.Identity.ElementId);
            bool clamped;
            try
            {
                _input = nextInput;
                _scene = scene;
                clamped = ClampScrolls(scene);
            }
            finally
            {
                _input = priorInput;
                _scene = priorScene;
            }
            if (clamped)
            {
                var rejectedErrors = new List<Exception>();
                if (_scene is not null)
                    ReleaseAll(PointerCaptureLossReason.SceneChanged, rejectedErrors);
                ClearHover(rejectedErrors);
                UpdateScrollBarHover(null);
                // Geometry needs another projection, but a surviving focus owner does
                // not lose its editing session just because a scroll offset changed.
                _scene = scene;
                _input = nextInput;
                BuildInputCaches(scene);
                if (
                    _focused is { } focused
                    && (
                        !Eligible(focused.Identity)
                        || !Path(focused.Identity).SequenceEqual(focused.Path)
                    )
                )
                    LoseSceneFocus(FocusChangeReason.SceneChanged, rejectedErrors);
                _scene = null;
                _input.Clear();
                ClearInputCaches();
                Throw(rejectedErrors);
                return false;
            }
            var errors = new List<Exception>();
            var visualGeneration = _composition.InteractionVisualGeneration;
            _scene = scene;
            _input = nextInput;
            BuildInputCaches(scene);
            foreach (var scrollable in _scrollable)
                if (_input.ContainsKey(scrollable.Key))
                    scrollable.Value.InstalledOffset = scrollable.Value.State.Offset;
            foreach (var capture in _captures.ToArray())
                if (
                    _scrollBarDrags.TryGetValue(capture.Key, out var drag)
                    && !_scrollBars.ContainsKey(drag.Viewport.ElementId)
                )
                    Release(capture.Key, PointerCaptureLossReason.SceneChanged, errors);
                else if (SameStructuralPath(capture.Value.Owner, priorInput))
                    _captures[capture.Key] = capture.Value with { Generation = scene.Generation };
                else
                    Release(capture.Key, CaptureReason(capture.Value.Owner), errors);
            if (
                _hoveredScrollBar is { } hoveredScrollBar
                && !_scrollBars.ContainsKey(hoveredScrollBar.ElementId)
            )
                _hoveredScrollBar = null;
            SyncAvailability(errors);
            if (_hovered is { } hovered && !Eligible(hovered))
                ClearHover(errors);
            if (_hoveredScrollBar is { } hoveredBar && !Eligible(hoveredBar))
                UpdateScrollBarHover(null);
            if (
                _focused is { } focus
                && (
                    !Eligible(focus.Identity)
                    || !_input.ContainsKey(focus.Identity.ElementId)
                    || !Path(focus.Identity).SequenceEqual(focus.Path)
                )
            )
                LoseSceneFocus(FocusChangeReason.Reordered, errors);
            _ = FocusPendingMenuTarget(errors);
            // Establish pending focus before revealing its caret. Both operations may
            // invalidate this candidate's interaction visuals, but they can safely be
            // coalesced into one bounded rejection when the paragraph source is fresh.
            ProcessFocusTargets(errors);
            RecoverSceneFocus(errors);
            var caretRevealed = RevealEditorCaret();
            if (caretRevealed || _composition.InteractionVisualGeneration != visualGeneration)
            {
                _scene = null;
                ClearInputCaches();
                Throw(errors);
                return false;
            }
            Throw(errors);
            return true;
        }
        catch
        {
            // Installation borrows the candidate. A host must be able to release it
            // on failure without leaving the router pointing at a disposed frame.
            if (ReferenceEquals(_scene, scene))
            {
                _scene = null;
                _input.Clear();
                ClearInputCaches();
            }
            throw;
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Hit-tests and routes a portable pointer command through the installed retained scene.</summary>
    public InputDispatchResult DispatchPointer(PointerCommand command)
    {
        Enter();
        try
        {
            command.Validate();
            var errors = new List<Exception>();
            if (EnsureScene(errors) is { } rejection)
                return Reject(rejection, "Pointer/" + command.Kind, errors);
            if (command.Kind == PointerCommandKind.Down)
                SetModality(InputModality.Pointer, errors);
            var scrollbar = HitScrollBar(command.X, command.Y);
            var scrollbarCapture = _scrollBarDrags.ContainsKey(command.PointerId);
            var hadCapture = _captures.ContainsKey(command.PointerId);
            RetainedScrollBar? capturedScrollBar = null;
            if (scrollbarCapture)
            {
                var drag = _scrollBarDrags[command.PointerId];
                if (_scrollBars.TryGetValue(drag.Viewport.ElementId, out var retained))
                    capturedScrollBar = retained;
                else
                {
                    Release(command.PointerId, PointerCaptureLossReason.SceneChanged, errors);
                    scrollbarCapture = false;
                    hadCapture = _captures.ContainsKey(command.PointerId);
                }
            }
            ElementIdentity? target = null;
            if (_captures.TryGetValue(command.PointerId, out var capture))
            {
                if (capture.Generation == _scene!.Generation && Eligible(capture.Owner))
                    target = capture.Owner;
                else
                    Release(command.PointerId, PointerCaptureLossReason.SceneChanged, errors);
            }
            target ??=
                scrollbarCapture ? _scrollBarDrags[command.PointerId].Viewport
                : scrollbar is { } bar && !hadCapture ? bar.Viewport
                : Hit(command.X, command.Y);
            UpdateHover(
                command.Kind == PointerCommandKind.Cancel ? null
                    : scrollbar is { } hoveredBar ? hoveredBar.Viewport
                    : Hit(command.X, command.Y),
                command.X,
                command.Y,
                errors
            );
            UpdateScrollBarHover(
                command.Kind == PointerCommandKind.Cancel ? null : scrollbar?.Viewport
            );
            if (target is null)
            {
                if (command.Kind is PointerCommandKind.Up or PointerCommandKind.Cancel)
                    _contextPointer = null;
                return Reject(InputRejection.NoTarget, "Pointer/" + command.Kind, errors);
            }
            if (scrollbarCapture || (!hadCapture && scrollbar is not null))
            {
                var barForRoute = scrollbarCapture
                    ? capturedScrollBar.GetValueOrDefault()
                    : scrollbar.GetValueOrDefault();
                var scrollbarResult = RouteScrollbarPointer(command, barForRoute, errors);
                if (
                    _captures.TryGetValue(command.PointerId, out var scrollCapture)
                    && command.Releases(scrollCapture.Button)
                )
                    Release(command.PointerId, PointerCaptureLossReason.Released, errors);
                if (command.Kind == PointerCommandKind.Cancel)
                    Release(command.PointerId, PointerCaptureLossReason.Cancelled, errors);
                Throw(errors);
                return scrollbarResult;
            }
            var result =
                !hadCapture && HandleContextPointer(command, target.Value)
                    ? new InputDispatchResult(
                        InputDispatchStatus.Delivered,
                        InputRejection.None,
                        target,
                        Path(target.Value),
                        true
                    )
                    : RoutePointer(command, target.Value, errors);
            if (
                _captures.TryGetValue(command.PointerId, out var buttonCapture)
                && command.Releases(buttonCapture.Button)
            )
                Release(command.PointerId, PointerCaptureLossReason.Released, errors);
            if (command.Kind == PointerCommandKind.Cancel)
                Release(command.PointerId, PointerCaptureLossReason.Cancelled, errors);
            Throw(errors);
            return result;
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Hit-tests and chains fractional logical-pixel wheel deltas through nearest scrollable ancestors.</summary>
    public InputDispatchResult DispatchWheel(WheelCommand command)
    {
        Enter();
        try
        {
            command.Validate();
            var errors = new List<Exception>();
            if (EnsureScene(errors) is { } rejection)
                return Reject(rejection, "Wheel", errors);
            var target = HitWheel(command.X, command.Y);
            if (target is null)
                return Reject(InputRejection.NoTarget, "Wheel", errors);
            var route = Path(target.Value).Reverse().ToArray();
            var remainingX = command.DeltaX;
            var remainingY = command.DeltaY;
            var handled = RouteWheel(command, target.Value, route, errors);
            if (handled)
            {
                SetLast(
                    "Wheel",
                    InputDispatchStatus.Delivered,
                    InputRejection.None,
                    target,
                    route,
                    true
                );
                Throw(errors);
                return new(InputDispatchStatus.Delivered, InputRejection.None, target, route, true);
            }
            foreach (var identity in route)
            {
                if (
                    !Available(identity)
                    || !_scrollable.TryGetValue(identity.ElementId, out var scrollable)
                )
                    continue;
                var current = scrollable.State.Offset;
                var bounds = ScrollBounds(identity, scrollable.InstalledOffset);
                var next = new ScrollOffset(
                    ClampScroll(current.X + remainingX, bounds.X),
                    ClampScroll(current.Y + remainingY, bounds.Y)
                );
                var consumedX = next.X - current.X;
                var consumedY = next.Y - current.Y;
                if (consumedX == 0 && consumedY == 0)
                    continue;
                scrollable.State.Offset = next;
                remainingX -= consumedX;
                remainingY -= consumedY;
                handled = true;
                if (remainingX == 0 && remainingY == 0)
                    break;
            }
            SetLast(
                "Wheel",
                InputDispatchStatus.Delivered,
                InputRejection.None,
                target,
                route,
                handled
            );
            Throw(errors);
            return new(InputDispatchStatus.Delivered, InputRejection.None, target, route, handled);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Routes a portable key command from the current focus target.</summary>
    public InputDispatchResult DispatchKey(KeyCommand command)
    {
        Enter();
        try
        {
            command.Validate();
            var errors = new List<Exception>();
            if (EnsureScene(errors) is { } rejection)
                return Reject(rejection, "Key/" + command.Kind, errors);
            if (command.Kind == KeyCommandKind.Down)
                SetModality(InputModality.Keyboard, errors);
            var target =
                _focused is { } focus && Eligible(focus.Identity)
                    ? focus.Identity
                    : UnfocusedCommandTarget();
            if (!Eligible(target))
                return Reject(InputRejection.Ineligible, "Key/" + command.Kind, errors);
            var result =
                HandleTooltipKey(command, target, errors)
                    ? new InputDispatchResult(
                        InputDispatchStatus.Delivered,
                        InputRejection.None,
                        target,
                        Path(target),
                        true
                    )
                : HandleContextKey(command, target)
                    ? new InputDispatchResult(
                        InputDispatchStatus.Delivered,
                        InputRejection.None,
                        target,
                        Path(target),
                        true
                    )
                : RouteKey(command, target, errors);
            if (!result.Handled && command is { Kind: KeyCommandKind.Down, Key: Key.Tab })
            {
                MoveFocusCore(
                    command.Modifiers.HasFlag(KeyModifiers.Shift)
                        ? FocusTraversalDirection.Previous
                        : FocusTraversalDirection.Next,
                    errors
                );
                result = new(result.Status, result.Rejection, result.Target, result.Route, true);
            }
            Throw(errors);
            return result;
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Routes a portable IME or committed text command to the current focus target.</summary>
    public InputDispatchResult DispatchText(TextInputCommand command)
    {
        Enter();
        try
        {
            command.Validate(allowMultiline: true);
            var errors = new List<Exception>();
            if (EnsureScene(errors) is { } rejection)
                return Reject(rejection, "Text/" + command.Kind, errors);
            if (
                _focused is not { } focus
                || !Eligible(focus.Identity)
                || !_textFields.TryGetValue(focus.Identity.ElementId, out var textState)
            )
                return Reject(InputRejection.NoTarget, "Text/" + command.Kind, errors);
            var handled = false;
            foreach (var callback in Snapshot(_text, [focus.Identity]))
            {
                var route = new TextRoute(command, callback.Identity);
                try
                {
                    callback.Callback(route);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
                handled |= route.Finish();
                if (handled)
                    break;
            }
            SetLast(
                "Text/" + command.Kind,
                InputDispatchStatus.Delivered,
                InputRejection.None,
                focus.Identity,
                [focus.Identity],
                handled
            );
            Throw(errors);
            return new(
                InputDispatchStatus.Delivered,
                InputRejection.None,
                focus.Identity,
                [focus.Identity],
                handled
            );
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Adapters perform clipboard I/O after retrieving a one-shot request from its focused origin.</summary>
    public bool TryTakeClipboardRequest(out TextClipboardRequest request)
    {
        Enter();
        try
        {
            request = default!;
            if (
                _focused is not { } focus
                || !Eligible(focus.Identity)
                || !_textFields.TryGetValue(focus.Identity.ElementId, out var state)
                || !state.TryTakeClipboard(out request)
            )
                return false;
            _clipboardTickets.Add(request, new(focus.Identity, state, state.EditGeneration));
            return true;
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Drops stale, replayed, moved-focus, or disposed clipboard completions without affecting another field.</summary>
    public bool CompleteClipboardRequest(
        TextClipboardRequest request,
        bool succeeded,
        string? text = null
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        Enter();
        try
        {
            if (
                !_clipboardTickets.Remove(request, out var ticket)
                || ticket.State.IsDisposed
                || ticket.State.EditGeneration != ticket.Generation
                || !_textFields.TryGetValue(ticket.Origin.ElementId, out var state)
                || !ReferenceEquals(state, ticket.State)
            )
                return false;
            if (
                request.Operation is TextClipboardOperation.Paste or TextClipboardOperation.Cut
                && (
                    _focused is not { } focus
                    || focus.Identity != ticket.Origin
                    || !Eligible(ticket.Origin)
                )
            )
                return false;
            return ticket.State.CompleteClipboard(request, succeeded, text);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Reports whether an available text editor owns the hit point in the current installed scene.</summary>
    public bool IsTextInputAt(float x, float y)
    {
        Enter();
        try
        {
            if (
                !float.IsFinite(x)
                || !float.IsFinite(y)
                || _scene is null
                || !ValidateScene(_scene)
            )
                return false;
            if (HitScrollBar(x, y) is not null)
                return false;
            return Hit(x, y) is { } hit
                && Path(hit).Any(item => _textFields.ContainsKey(item.ElementId));
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Uses the installed shaped text snapshot to anchor native candidates at the focused caret.</summary>
    public bool TryGetCaretGeometry(out LayoutRect rectangle)
    {
        Enter();
        try
        {
            rectangle = default;
            if (
                _scene is null
                || !ValidateScene(_scene)
                || _focused is not { } focus
                || !Eligible(focus.Identity)
                || !_textFields.ContainsKey(focus.Identity.ElementId)
                || !_input.TryGetValue(focus.Identity.ElementId, out var field)
            )
                return false;
            var caret = FindCaret(_scene.Nodes, focus.Identity);
            if (caret is null)
                return false;
            rectangle = caret.Bounds;
            var clip = field.ChildClipBounds ?? field.Bounds;
            foreach (var ancestor in _effectiveClips[focus.Identity.ElementId])
            {
                var left = Math.Max(clip.X, ancestor.Bounds.X);
                var top = Math.Max(clip.Y, ancestor.Bounds.Y);
                var right = Math.Min(
                    clip.X + clip.Width,
                    ancestor.Bounds.X + ancestor.Bounds.Width
                );
                var bottom = Math.Min(
                    clip.Y + clip.Height,
                    ancestor.Bounds.Y + ancestor.Bounds.Height
                );
                if (right <= left || bottom <= top)
                {
                    rectangle = default;
                    return false;
                }
                clip = new(left, top, right - left, bottom - top);
            }
            if (clip.Width <= 0 || clip.Height <= 0)
            {
                rectangle = default;
                return false;
            }
            rectangle = new(
                Math.Clamp(
                    rectangle.X,
                    clip.X,
                    Math.Max(clip.X, clip.X + clip.Width - rectangle.Width)
                ),
                Math.Clamp(
                    rectangle.Y,
                    clip.Y,
                    Math.Max(clip.Y, clip.Y + clip.Height - rectangle.Height)
                ),
                Math.Min(rectangle.Width, clip.Width),
                Math.Min(rectangle.Height, clip.Height)
            );
            return true;
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Moves focus to the next or previous current tab stop.</summary>
    public bool MoveFocus(FocusTraversalDirection direction)
    {
        Enter();
        try
        {
            if (!Enum.IsDefined(direction))
                throw new ArgumentException(
                    "Focus traversal direction must be finite.",
                    nameof(direction)
                );
            var errors = new List<Exception>();
            if (EnsureScene(errors) is not null)
            {
                Throw(errors);
                return false;
            }
            SetModality(InputModality.Keyboard, errors);
            var moved = MoveFocusCore(direction, errors);
            Throw(errors);
            return moved;
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Focuses one current retained semantic target without exposing platform focus transport.</summary>
    public bool FocusSemantic(ElementIdentity identity)
    {
        Enter();
        try
        {
            var errors = new List<Exception>();
            if (
                EnsureScene(errors) is not null
                || !Eligible(identity)
                || !_focusable.ContainsKey(identity.ElementId)
            )
            {
                Throw(errors);
                return false;
            }
            SetModality(InputModality.Keyboard, errors);
            RequestFocus(identity, FocusChangeReason.Traversal, errors);
            Throw(errors);
            return _focused?.Identity == identity;
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Applies a bounded semantic scroll request through the installed retained geometry.</summary>
    public bool ScrollSemantic(ElementIdentity identity, SemanticCommand command)
    {
        Enter();
        try
        {
            command.Validate();
            if (
                command.Kind != SemanticCommandKind.Scroll
                || !_scrollable.TryGetValue(identity.ElementId, out var scrollable)
            )
                return false;
            var errors = new List<Exception>();
            if (EnsureScene(errors) is not null || !Eligible(identity))
            {
                Throw(errors);
                return false;
            }
            var offset = scrollable.State.Offset;
            var changed = command.Endpoint switch
            {
                SemanticScrollEndpoint.Start => SetScroll(identity, scrollable, 0, 0, offset),
                SemanticScrollEndpoint.End => ScrollToEnd(identity),
                _ => SetScroll(
                    identity,
                    scrollable,
                    offset.X + command.Horizontal,
                    offset.Y + command.Vertical,
                    offset
                ),
            };
            Throw(errors);
            return changed;
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Returns the installed retained scroll geometry for a semantic adapter snapshot.</summary>
    public SemanticScrollState? GetSemanticScroll(ElementIdentity identity)
    {
        Check();
        if (
            !_scrollable.TryGetValue(identity.ElementId, out var scrollable)
            || !_input.TryGetValue(identity.ElementId, out var viewport)
            || !Eligible(identity)
        )
            return null;
        return new(
            scrollable.State.Offset,
            ScrollBounds(identity, scrollable.InstalledOffset),
            viewport.ChildClipBounds ?? viewport.Bounds
        );
    }

    /// <summary>Returns a deterministic diagnostic snapshot without application values.</summary>
    public string Dump()
    {
        Check();
        var output = new StringBuilder("input scene=");
        output
            .Append(
                _scene is null
                    ? "-"
                    : _composition.Epoch.ToString(CultureInfo.InvariantCulture)
                        + "/"
                        + _scene.Generation.ToString(CultureInfo.InvariantCulture)
            )
            .Append(" signature=")
            .Append(_scene?.InputSignature ?? "-")
            .Append(" modality=")
            .Append(_modality)
            .Append('\n');
        output
            .Append("registrations pointer=")
            .Append(_pointer.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" key=")
            .Append(_key.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" focus=")
            .Append(_focus.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" captureLoss=")
            .Append(_captureLoss.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" focusables=")
            .Append(_focusable.Count.ToString(CultureInfo.InvariantCulture))
            .Append('\n');
        AppendRegistrations(output, "pointer", _pointer);
        AppendRegistrations(output, "wheel", _wheel);
        AppendRegistrations(output, "key", _key);
        AppendRegistrations(output, "focus", _focus);
        AppendRegistrations(output, "capture-loss", _captureLoss);
        foreach (var focusable in _focusable.OrderBy(item => item.Key))
            output
                .Append("focusable owner=")
                .Append(focusable.Key.ToString(CultureInfo.InvariantCulture))
                .Append(" tabStop=")
                .Append(focusable.Value.TabStop ? "true" : "false")
                .Append('\n');
        output
            .Append("focus owner=")
            .Append(_focused?.Identity.ElementId.ToString(CultureInfo.InvariantCulture) ?? "-")
            .Append(" reason=")
            .Append(_focused?.Reason.ToString() ?? "-")
            .Append(" lastLoss=")
            .Append(_lastFocusLoss)
            .Append('\n');
        foreach (var capture in _captures.OrderBy(item => item.Key))
            output
                .Append("capture pointer=")
                .Append(capture.Key.ToString(CultureInfo.InvariantCulture))
                .Append(" owner=")
                .Append(capture.Value.Owner.ElementId.ToString(CultureInfo.InvariantCulture))
                .Append(" generation=")
                .Append(capture.Value.Generation.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        return output
            .Append("captureLoss=")
            .Append(_lastCaptureLoss)
            .Append('\n')
            .Append(_lastRoute)
            .Append('\n')
            .ToString();
    }

    internal void RegisterPointer(
        long elementId,
        ReactiveScope scope,
        Action<PointerRoute> callback
    ) => Register(_pointer, elementId, scope, callback);

    internal void RegisterWheel(long elementId, ReactiveScope scope, Action<WheelRoute> callback) =>
        Register(_wheel, elementId, scope, callback);

    internal void RegisterKey(long elementId, ReactiveScope scope, Action<KeyRoute> callback) =>
        Register(_key, elementId, scope, callback);

    internal void RegisterFocus(long elementId, ReactiveScope scope, Action<FocusRoute> callback) =>
        Register(_focus, elementId, scope, callback);

    internal void RegisterText(long elementId, ReactiveScope scope, Action<TextRoute> callback) =>
        Register(_text, elementId, scope, callback);

    internal void RegisterCaptureLoss(
        long elementId,
        ReactiveScope scope,
        Action<PointerCaptureLoss> callback
    ) => Register(_captureLoss, elementId, scope, callback);

    internal void RegisterFocusable(
        long elementId,
        ReactiveScope scope,
        bool tabStop,
        BehaviorContext context
    )
    {
        if (_focusable.ContainsKey(elementId))
            throw new InvalidOperationException("An element has one focus behavior.");
        var entry = new Focusable(tabStop, context);
        _focusable.Add(elementId, entry);
        scope.OnDispose(() =>
        {
            if (
                _focusable.TryGetValue(elementId, out var current)
                && ReferenceEquals(current, entry)
            )
                _focusable.Remove(elementId);
        });
    }

    internal void SetTabStop(long elementId, BehaviorContext owner, bool tabStop)
    {
        Check();
        if (
            !_focusable.TryGetValue(elementId, out var entry)
            || !ReferenceEquals(entry.Context, owner)
        )
            throw new InvalidOperationException(
                "A behavior can change only its own registered focus target."
            );
        entry.TabStop = tabStop;
    }

    internal void RegisterScrollable(long elementId, ReactiveScope scope, ScrollViewportState state)
    {
        if (_scrollable.ContainsKey(elementId))
            throw new InvalidOperationException("An element has one scroll behavior.");
        var entry = new Scrollable(state);
        _scrollable.Add(elementId, entry);
        scope.OnDispose(() =>
        {
            if (
                _scrollable.TryGetValue(elementId, out var current)
                && ReferenceEquals(current, entry)
            )
                _scrollable.Remove(elementId);
        });
    }

    internal bool IsScrollable(ElementIdentity identity) =>
        identity.CompositionEpoch == _composition.Epoch
        && _scrollable.ContainsKey(identity.ElementId);

    internal void RegisterTextField(long elementId, ReactiveScope scope, TextFieldState state)
    {
        if (_textFields.ContainsKey(elementId))
            throw new InvalidOperationException("An element has one text behavior.");
        _textFields.Add(elementId, state);
        scope.OnDispose(() =>
        {
            _textFields.Remove(elementId);
            foreach (
                var request in _clipboardTickets
                    .Where(ticket => ReferenceEquals(ticket.Value.State, state))
                    .Select(ticket => ticket.Key)
                    .ToArray()
            )
                _clipboardTickets.Remove(request);
        });
    }

    internal void RegisterFocusTarget(
        long elementId,
        ReactiveScope scope,
        TextFieldState state,
        FocusTarget target
    ) => RegisterFocusTarget(elementId, scope, target, state);

    internal void RegisterFocusTarget(long elementId, ReactiveScope scope, FocusTarget target) =>
        RegisterFocusTarget(elementId, scope, target, null);

    private void RegisterFocusTarget(
        long elementId,
        ReactiveScope scope,
        FocusTarget target,
        TextFieldState? state
    )
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(target);
        if (!ReferenceEquals(target.Graph, _composition.Graph))
            throw new ArgumentException(
                "A focus target and its mounted control must belong to the same reactive graph.",
                nameof(target)
            );
        if (_focusTargets.ContainsKey(target))
            throw new InvalidOperationException(
                "A focus target can have only one live mounted control."
            );
        var registration = new FocusTargetRegistration(elementId, state);
        _focusTargets.Add(target, registration);
        scope.OnDispose(() =>
        {
            if (
                _focusTargets.TryGetValue(target, out var current)
                && ReferenceEquals(current, registration)
            )
                _focusTargets.Remove(target);
        });
        _ = scope.Effect(
            () =>
            {
                if (!target.TryGetPending(out _))
                    return;
                var errors = new List<Exception>();
                TryFocusTarget(target, registration, errors);
                Throw(errors);
            },
            scope.Name + ".focus-target"
        );
    }

    internal void RemoveElement(Element element, PointerCaptureLossReason reason)
    {
        var errors = new List<Exception>();
        var removed = new ElementIdentity(_composition.Epoch, element.Id);
        if (_hovered is { } hovered && (IsWithin(hovered, removed) || IsWithin(removed, hovered)))
            ClearHover(errors);
        if (
            _hoveredScrollBar is { } hoveredScrollBar
            && (IsWithin(hoveredScrollBar, removed) || IsWithin(removed, hoveredScrollBar))
        )
            UpdateScrollBarHover(null);
        foreach (
            var pointer in _captures
                .Where(pair => IsWithin(pair.Value.Owner, removed))
                .Select(pair => pair.Key)
                .ToArray()
        )
            Release(pointer, reason, errors);
        if (_focused is { } focus && IsWithin(focus.Identity, removed))
            LoseSceneFocus(FocusChangeReason.Disposed, errors);
        Throw(errors);
    }

    internal bool TryCapture(
        int pointerId,
        ElementIdentity owner,
        PointerButton button,
        bool isDown
    )
    {
        if (
            !isDown
            || _scene is null
            || _composition.Find(owner) is not { IsDisposed: false }
            || !Eligible(owner)
        )
            return false;
        if (_captures.TryGetValue(pointerId, out var current))
            return current.Owner == owner && current.Generation == _scene.Generation;
        _captures.Add(pointerId, new(owner, _scene.Generation, button));
        return true;
    }

    internal void RequestFocus(
        ElementIdentity? identity,
        FocusChangeReason reason,
        List<Exception> errors
    )
    {
        _focusToRecover = null;
        _focusRequestSerial = checked(_focusRequestSerial + 1);
        _pendingFocus = new(identity, reason);
        if (_focusing)
            return;
        _focusing = true;
        try
        {
            for (var turns = 0; _pendingFocus is { } pending; turns++)
            {
                if (turns == 64)
                {
                    errors.Add(new InvalidOperationException("Focus callbacks did not converge."));
                    _pendingFocus = null;
                    break;
                }
                _pendingFocus = null;
                ElementIdentity? next =
                    pending.Identity is { } candidate
                    && Eligible(candidate)
                    && _focusable.ContainsKey(candidate.ElementId)
                        ? candidate
                        : null;
                if (_focused is { } same && next is { } current && same.Identity == current)
                {
                    SetFocusedVisual(current, true);
                    continue;
                }
                if (_focused is { } old)
                {
                    _focused = null;
                    SetFocusedVisual(old.Identity, false);
                    _lastFocusLoss = pending.Reason.ToString();
                    InvokeFocus(
                        old.Identity,
                        new(FocusCommandKind.Lost, pending.Reason, _modality),
                        errors
                    );
                }
                if (_pendingFocus is not null)
                    continue;
                if (
                    next is { } selected
                    && Eligible(selected)
                    && _focusable.ContainsKey(selected.ElementId)
                )
                {
                    _input.TryGetValue(selected.ElementId, out var retained);
                    _focused = new(
                        selected,
                        Path(selected).ToArray(),
                        retained.Order,
                        pending.Reason
                    );
                    SetFocusedVisual(selected, true);
                    InvokeFocus(
                        selected,
                        new(FocusCommandKind.Gained, pending.Reason, _modality),
                        errors
                    );
                }
            }
        }
        finally
        {
            _focusing = false;
        }
    }

    internal void Cleanup()
    {
        _composition.CheckThread();
        _activeMenu?.Dismiss();
        _activeMenu = null;
        _contextPointer = null;
        ContextMenuRequested = null;
        if (_disposed)
            return;
        var errors = new List<Exception>();
        ReleaseAll(PointerCaptureLossReason.Disposed, errors);
        CleanupSurfaces(errors);
        ClearHover(errors);
        UpdateScrollBarHover(null);
        RequestFocus(null, FocusChangeReason.Disposed, errors);
        foreach (var focusable in _focusable.Values.ToArray())
        {
            try
            {
                focusable.Context.SetState(BehaviorState.Focused, false);
                focusable.Context.SetState(BehaviorState.FocusVisible, false);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
        _disposed = true;
        _pointer.Clear();
        _wheel.Clear();
        _key.Clear();
        _focus.Clear();
        _text.Clear();
        _captureLoss.Clear();
        _focusable.Clear();
        _scrollable.Clear();
        _scrollBars.Clear();
        _scrollBarDrags.Clear();
        _textFields.Clear();
        _focusTargets.Clear();
        _clipboardTickets.Clear();
        _input.Clear();
        ClearInputCaches();
        _scene = null;
        _focused = null;
        _pendingFocus = null;
        Throw(errors);
    }

    private InputDispatchResult RoutePointer(
        PointerCommand command,
        ElementIdentity target,
        List<Exception> errors
    )
    {
        var route = Path(target).Reverse().ToArray();
        var callbacks = Snapshot(_pointer, route);
        var handled = false;
        foreach (var callback in callbacks)
        {
            var context = new PointerRoute(this, command, target, callback.Identity, route, errors);
            try
            {
                callback.Callback(context);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            handled |= context.Finish();
            if (handled)
                break;
        }
        SetLast(
            "Pointer/" + command.Kind,
            InputDispatchStatus.Delivered,
            InputRejection.None,
            target,
            route,
            handled
        );
        return new(InputDispatchStatus.Delivered, InputRejection.None, target, route, handled);
    }

    private InputDispatchResult RouteScrollbarPointer(
        PointerCommand command,
        RetainedScrollBar scrollBar,
        List<Exception> errors
    )
    {
        var target = scrollBar.Viewport;
        var route = Path(target).Reverse().ToArray();
        var handled = false;
        if (_scrollable.TryGetValue(target.ElementId, out var scrollable))
        {
            switch (command.Kind)
            {
                case PointerCommandKind.Down when command.Button == PointerButton.Primary:
                    if (Contains(scrollBar.Thumb, command.X, command.Y))
                    {
                        if (
                            scrollBar.Maximum.Y > 0
                            && scrollBar.Track.Height > scrollBar.Thumb.Height
                        )
                        {
                            _scrollBarDrags[command.PointerId] = new(
                                target,
                                command.Y,
                                scrollable.State.Offset.Y
                            );
                            _captures[command.PointerId] = new(
                                target,
                                _scene!.Generation,
                                command.Button
                            );
                            SetPressedVisual(target, true, errors);
                        }
                        handled = true;
                    }
                    else if (Contains(scrollBar.Track, command.X, command.Y))
                    {
                        var page = _input.TryGetValue(target.ElementId, out var viewport)
                            ? (viewport.ChildClipBounds ?? viewport.Bounds).Height
                            : scrollBar.Track.Height;
                        var direction = command.Y < scrollBar.Thumb.Y ? -1 : 1;
                        handled = SetScroll(
                            target,
                            scrollable,
                            scrollable.State.Offset.X,
                            scrollable.State.Offset.Y + direction * page,
                            scrollable.State.Offset
                        );
                        if (!handled)
                            handled = true;
                    }
                    break;
                case PointerCommandKind.Move
                    when _scrollBarDrags.TryGetValue(command.PointerId, out var drag):
                    var travel = scrollBar.Track.Height - scrollBar.Thumb.Height;
                    if (travel > 0 && scrollBar.Maximum.Y > 0)
                    {
                        var next =
                            drag.StartOffsetY
                            + (command.Y - drag.StartPointerY) / travel * scrollBar.Maximum.Y;
                        handled = SetScroll(
                            target,
                            scrollable,
                            scrollable.State.Offset.X,
                            next,
                            scrollable.State.Offset
                        );
                    }
                    break;
                case PointerCommandKind.Up
                or PointerCommandKind.Cancel when _scrollBarDrags.ContainsKey(command.PointerId):
                    handled = true;
                    break;
            }
        }
        SetLast(
            "Scrollbar/" + command.Kind,
            InputDispatchStatus.Delivered,
            InputRejection.None,
            target,
            route,
            handled
        );
        return new(InputDispatchStatus.Delivered, InputRejection.None, target, route, handled);
    }

    private InputDispatchResult RouteKey(
        KeyCommand command,
        ElementIdentity target,
        List<Exception> errors
    )
    {
        var route = Path(target).Reverse().ToArray();
        var callbacks = Snapshot(_key, route);
        var handled = false;
        foreach (var callback in callbacks)
        {
            var context = new KeyRoute(this, command, target, callback.Identity, route);
            try
            {
                callback.Callback(context);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            handled |= context.Finish();
            if (handled)
                break;
        }
        SetLast(
            "Key/" + command.Kind,
            InputDispatchStatus.Delivered,
            InputRejection.None,
            target,
            route,
            handled
        );
        return new(InputDispatchStatus.Delivered, InputRejection.None, target, route, handled);
    }

    private bool MoveFocusCore(FocusTraversalDirection direction, List<Exception> errors)
    {
        var candidates = _scene!
            .Input.Where(item =>
                Eligible(item.Identity)
                && _focusable.TryGetValue(item.Identity.ElementId, out var focusable)
                && focusable.TabStop
                && (
                    !_scrollable.ContainsKey(item.Identity.ElementId)
                    || !HasFocusableDescendant(item.Identity)
                )
            )
            .OrderBy(item => item.Order)
            .ToArray();
        if (candidates.Length == 0)
        {
            RequestFocus(null, FocusChangeReason.Traversal, errors);
            return false;
        }
        var index = _focused is { } focused
            ? Array.FindIndex(candidates, item => item.Identity == focused.Identity)
            : -1;
        index =
            direction == FocusTraversalDirection.Next
                ? (index + 1 + candidates.Length) % candidates.Length
                : (index - 1 + candidates.Length) % candidates.Length;
        RequestFocus(candidates[index].Identity, FocusChangeReason.Traversal, errors);
        return true;
    }

    private bool HasFocusableDescendant(ElementIdentity ancestor)
    {
        foreach (var candidate in _scene!.Input)
        {
            for (var parent = candidate.Parent; parent is { } current; )
            {
                if (
                    current == ancestor
                    && Eligible(candidate.Identity)
                    && _focusable.TryGetValue(candidate.Identity.ElementId, out var focusable)
                    && focusable.TabStop
                )
                    return true;
                if (!_input.TryGetValue(current.ElementId, out var retained))
                    break;
                parent = retained.Parent;
            }
        }
        return false;
    }

    private void SetModality(InputModality modality, List<Exception> errors)
    {
        if (_modality == modality)
            return;
        _modality = modality;
        if (_focused is { } focus)
            SetFocusedVisual(focus.Identity, true);
    }

    private void SetFocusedVisual(ElementIdentity identity, bool focused)
    {
        if (_focusable.TryGetValue(identity.ElementId, out var focusable))
        {
            focusable.Context.SetState(BehaviorState.Focused, focused);
            focusable.Context.SetState(
                BehaviorState.FocusVisible,
                focused && _modality == InputModality.Keyboard
            );
        }
    }

    /// <summary>Clears pointer hover while preserving any active pointer capture.</summary>
    public void ClearPointerHover()
    {
        Enter();
        try
        {
            var errors = new List<Exception>();
            ClearHover(errors);
            UpdateScrollBarHover(null);
            Throw(errors);
        }
        finally
        {
            Exit();
        }
    }

    private void UpdateHover(
        ElementIdentity? hit,
        float pointerX,
        float pointerY,
        List<Exception> errors
    )
    {
        UpdateTooltipHover(hit, pointerX, pointerY, errors);
        ElementIdentity? next = null;
        if (hit is { } identity && Eligible(identity))
        {
            var path = Path(identity);
            for (var index = path.Count - 1; index >= 0; index--)
                if (_focusable.ContainsKey(path[index].ElementId) && Eligible(path[index]))
                {
                    next = path[index];
                    break;
                }
        }
        if (_hovered == next)
            return;
        var prior = _hovered;
        _hovered = next;
        if (prior is { } old)
            SetHoverVisual(old, false, errors);
        if (next is { } current)
            SetHoverVisual(current, true, errors);
    }

    private void ClearHover(List<Exception> errors) => UpdateHover(null, 0, 0, errors);

    private void UpdateScrollBarHover(ElementIdentity? viewport)
    {
        if (_hoveredScrollBar == viewport)
            return;
        _hoveredScrollBar = viewport;
        _composition.InvalidateInteractionVisuals();
    }

    private void SetHoverVisual(ElementIdentity identity, bool hovered, List<Exception> errors)
    {
        if (!_focusable.TryGetValue(identity.ElementId, out var focusable))
            return;
        try
        {
            focusable.Context.SetState(BehaviorState.Hover, hovered);
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
    }

    internal bool IsScrollbarHovered(ElementIdentity identity) => _hoveredScrollBar == identity;

    internal bool IsScrollbarPressed(ElementIdentity identity) =>
        _scrollBarDrags.Values.Any(drag => drag.Viewport == identity);

    private void SetPressedVisual(ElementIdentity identity, bool pressed, List<Exception> errors)
    {
        if (!_focusable.TryGetValue(identity.ElementId, out var focusable))
            return;
        try
        {
            focusable.Context.SetState(BehaviorState.Pressed, pressed);
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
    }

    private void InvokeFocus(ElementIdentity target, FocusCommand command, List<Exception> errors)
    {
        foreach (
            var callback in _focus
                .Where(item => item.ElementId == target.ElementId)
                .OrderBy(item => item.Ordinal)
                .ToArray()
        )
            try
            {
                callback.Callback(new FocusRoute(command, target));
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        UpdateTooltipFocus(target, command.Kind == FocusCommandKind.Gained, errors);
    }

    private void ReleaseAll(PointerCaptureLossReason reason, List<Exception> errors)
    {
        foreach (var pointer in _captures.Keys.ToArray())
            Release(pointer, reason, errors);
    }

    private void Release(int pointerId, PointerCaptureLossReason reason, List<Exception> errors)
    {
        var hadCapture = _captures.Remove(pointerId, out var capture);
        if (_scrollBarDrags.Remove(pointerId, out var drag))
            SetPressedVisual(drag.Viewport, false, errors);
        if (!hadCapture)
            return;
        _lastCaptureLoss = reason.ToString();
        var loss = new PointerCaptureLoss(pointerId, reason, capture.Owner);
        foreach (
            var callback in _captureLoss
                .Where(item => item.ElementId == capture.Owner.ElementId)
                .OrderBy(item => item.Ordinal)
                .ToArray()
        )
            try
            {
                callback.Callback(loss);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
    }

    private void ProcessFocusTargets(List<Exception> errors)
    {
        foreach (var pair in _focusTargets.ToArray())
            TryFocusTarget(pair.Key, pair.Value, errors);
    }

    private bool TryFocusTarget(
        FocusTarget target,
        FocusTargetRegistration registration,
        List<Exception> errors
    )
    {
        if (!target.TryGetPending(out var request) || _scene is null)
            return false;
        // A programmatic focus request can be raised by the same mutation that
        // invalidates layout. Keep it pending for SetScene rather than clearing
        // an existing editor focus against geometry that is already stale.
        if (_composition.IsInteractionSuspended || !ValidateScene(_scene))
            return false;
        if (
            !_input.TryGetValue(registration.ElementId, out var retained)
            || !Eligible(retained.Identity)
            || !_focusable.ContainsKey(registration.ElementId)
        )
            return false;

        SetModality(InputModality.Keyboard, errors);
        RequestFocus(retained.Identity, FocusChangeReason.Keyboard, errors);
        if (_focused?.Identity != retained.Identity)
            return false;
        if (request.SelectAll)
        {
            registration.State?.CancelComposition();
            registration.State?.SelectAll();
        }
        return target.TryConsume(request.Generation);
    }

    private void SyncAvailability(List<Exception> errors)
    {
        foreach (var retained in _scene!.Input)
            if (_composition.Find(retained.Identity) is { } element)
                element.SetInputDisabledVariant(!Available(retained.Identity));
        if (_focused is { } focus && !Eligible(focus.Identity))
            LoseSceneFocus(FocusReason(focus.Identity), errors);
        foreach (var capture in _captures.ToArray())
            if (!Eligible(capture.Value.Owner))
                Release(capture.Key, CaptureReason(capture.Value.Owner), errors);
    }

    private InputRejection? EnsureScene(List<Exception> errors)
    {
        if (_composition.IsInteractionSuspended)
            return InputRejection.Ineligible;
        if (_scene is null)
            return InputRejection.NoScene;
        if (ValidateScene(_scene))
            return null;
        foreach (var element in _composition.Elements())
            element.ReconcileSemanticStateForInput();
        foreach (var capture in _captures.ToArray())
            Release(capture.Key, CaptureReason(capture.Value.Owner), errors);
        if (_focused is { } focus)
            LoseSceneFocus(FocusReason(focus.Identity), errors);
        ClearHover(errors);
        UpdateScrollBarHover(null);
        _scene = null;
        _input.Clear();
        ClearInputCaches();
        return InputRejection.StaleScene;
    }

    private bool ValidateScene(RetainedScene scene, bool installing = false)
    {
        if (
            scene.IsDisposed
            || scene.Generation == 0
            || scene.Generation != _composition.LatestSceneGeneration
            || scene.InputProjectionRevision != _composition.InputProjectionRevision
            || scene.Input.Count != scene.Boxes.Count
            || scene.Input.Count == 0
        )
            return false;
        if (!installing)
            return true;
        var live = _composition.Elements().ToArray();
        if (
            !scene
                .Input.Select(item => item.Identity)
                .SequenceEqual(
                    live.Select(item => new ElementIdentity(_composition.Epoch, item.Id))
                )
        )
            return false;
        foreach (var element in live)
            element.ReconcileSemanticStateForInput();
        return true;
    }

    private ElementIdentity? Hit(float x, float y)
    {
        foreach (var candidate in _hitOrder)
            if (
                _pointerVisible.GetValueOrDefault(candidate.Identity.ElementId)
                && Contains(candidate.Bounds, x, y)
                && ClippedIn(candidate.Identity, x, y)
            )
                // Disabled content blocks targets behind it rather than retargeting to an ancestor.
                return Available(candidate.Identity) ? candidate.Identity : null;
        return null;
    }

    private ElementIdentity? HitWheel(float x, float y)
    {
        foreach (var candidate in _hitOrder)
            if (
                _pointerVisible.GetValueOrDefault(candidate.Identity.ElementId)
                && Contains(candidate.Bounds, x, y)
                && ClippedIn(candidate.Identity, x, y)
            )
                return candidate.Identity;
        return null;
    }

    private RetainedScrollBar? HitScrollBar(float x, float y)
    {
        foreach (var scrollBar in _scrollBarHitOrder)
            if (
                Eligible(scrollBar.Viewport)
                && Contains(scrollBar.Track, x, y)
                && ClippedIn(scrollBar.Viewport, x, y)
            )
                return scrollBar;
        return null;
    }

    internal bool Contains(ElementIdentity identity, float x, float y) =>
        _input.TryGetValue(identity.ElementId, out var retained)
        && Contains(retained.Bounds, x, y)
        && ClippedIn(identity, x, y);

    private bool Eligible(ElementIdentity identity) =>
        _input.ContainsKey(identity.ElementId) && Available(identity);

    private bool Available(ElementIdentity identity) =>
        _available.GetValueOrDefault(identity.ElementId);

    private PointerCaptureLossReason CaptureReason(ElementIdentity identity) =>
        UnavailableReason(identity) switch
        {
            Unavailable.Hidden => PointerCaptureLossReason.Hidden,
            Unavailable.Disabled => PointerCaptureLossReason.Disabled,
            _ => PointerCaptureLossReason.SceneChanged,
        };

    private FocusChangeReason FocusReason(ElementIdentity identity) =>
        UnavailableReason(identity) switch
        {
            Unavailable.Hidden => FocusChangeReason.Hidden,
            Unavailable.Disabled => FocusChangeReason.Disabled,
            _ => FocusChangeReason.SceneChanged,
        };

    private Unavailable UnavailableReason(ElementIdentity identity)
    {
        var chain = new List<ElementIdentity>();
        for (var current = identity; _input.TryGetValue(current.ElementId, out var retained); )
        {
            chain.Add(current);
            if (retained.Parent is not { } parent)
                break;
            current = parent;
        }
        foreach (var current in chain)
            if (
                (_input.TryGetValue(current.ElementId, out var retained) && !retained.Visible)
                || _composition.Find(current) is { IsDisposed: false } element
                    && !element.ResolveValue(InputProperties.Visible)
            )
                return Unavailable.Hidden;
        foreach (var current in chain)
            if (
                (_input.TryGetValue(current.ElementId, out var retained) && !retained.Enabled)
                || _composition.Find(current) is { IsDisposed: false } element
                    && !element.ResolveValue(InputProperties.Enabled)
            )
                return Unavailable.Disabled;
        return Unavailable.Scene;
    }

    private bool ClippedIn(ElementIdentity identity, float x, float y)
    {
        if (!_effectiveClips.TryGetValue(identity.ElementId, out var clips))
            return true;
        foreach (var clip in clips)
            if (!Contains(clip, x, y))
                return false;
        return true;
    }

    private static PaintSceneNode? FindCaret(IEnumerable<SceneNode> nodes, ElementIdentity identity)
    {
        foreach (var node in nodes)
        {
            if (
                node is PaintSceneNode { Identity.Kind: SceneNodeKind.Caret } caret
                && caret.Identity.Element == identity
            )
                return caret;
            if (node is ClipSceneNode clip && FindCaret(clip.Children, identity) is { } nested)
                return nested;
            if (
                node is OpacitySceneNode opacity
                && FindCaret(opacity.Children, identity) is { } nestedOpacity
            )
                return nestedOpacity;
        }
        return null;
    }

    private bool SameStructuralPath(
        ElementIdentity identity,
        IReadOnlyDictionary<long, RetainedInputElement> prior
    )
    {
        var current = identity;
        while (true)
        {
            if (
                !prior.TryGetValue(current.ElementId, out var old)
                || !_input.TryGetValue(current.ElementId, out var next)
                || old.Identity != next.Identity
                || old.Parent != next.Parent
                || old.Order != next.Order
            )
                return false;
            if (old.Parent is not { } parent)
                return Eligible(identity);
            current = parent;
        }
    }

    private IReadOnlyList<ElementIdentity> Path(ElementIdentity identity) =>
        _paths.TryGetValue(identity.ElementId, out var path) ? path : [];

    private void BuildInputCaches(RetainedScene scene)
    {
        _available = new Dictionary<long, bool>(scene.Input.Count);
        _pointerVisible = new Dictionary<long, bool>(scene.Input.Count);
        _paths = new Dictionary<long, ElementIdentity[]>(scene.Input.Count);
        _effectiveClips = new Dictionary<long, InputClip[]>(scene.Input.Count);
        _scrollBars.Clear();
        foreach (var scrollBar in scene.ScrollBars)
            _scrollBars.Add(scrollBar.Viewport.ElementId, scrollBar);
        _scrollBarHitOrder = scene.ScrollBars.Reverse().ToArray();
        foreach (var retained in scene.Input)
        {
            var parentAvailable = true;
            ElementIdentity[] path;
            InputClip[] effectiveClips;
            if (retained.Parent is { } parent)
            {
                parentAvailable = _available.GetValueOrDefault(parent.ElementId);
                var parentPath = _paths[parent.ElementId];
                path = new ElementIdentity[parentPath.Length + 1];
                parentPath.CopyTo(path, 0);
                path[^1] = retained.Identity;
                var inherited = _effectiveClips[parent.ElementId];
                if (_input[parent.ElementId].ChildClipBounds is { } parentClip)
                {
                    effectiveClips = new InputClip[inherited.Length + 1];
                    inherited.CopyTo(effectiveClips, 0);
                    effectiveClips[^1] = new(
                        parentClip,
                        _input[parent.ElementId].ChildClipCornerRadius
                    );
                }
                else
                    effectiveClips = inherited;
            }
            else
            {
                path = [retained.Identity];
                effectiveClips = [];
            }
            _available.Add(
                retained.Identity.ElementId,
                parentAvailable && retained.Enabled && retained.Visible
            );
            _pointerVisible.Add(
                retained.Identity.ElementId,
                retained.Visible
                    && !retained.PointerTransparent
                    && (
                        retained.Parent is not { } pointerParent
                        || _pointerVisible.GetValueOrDefault(pointerParent.ElementId)
                    )
            );
            _paths.Add(retained.Identity.ElementId, path);
            _effectiveClips.Add(retained.Identity.ElementId, effectiveClips);
        }
        _hitOrder = scene.Input.OrderByDescending(item => item.Order).ToArray();
    }

    private void ClearInputCaches()
    {
        _available.Clear();
        _pointerVisible.Clear();
        _paths.Clear();
        _effectiveClips.Clear();
        _hitOrder = [];
        _scrollBars.Clear();
        _scrollBarHitOrder = [];
    }

    private static bool Contains(LayoutRect bounds, float x, float y) =>
        bounds.Width > 0
        && bounds.Height > 0
        && x >= bounds.X
        && y >= bounds.Y
        && x < bounds.X + bounds.Width
        && y < bounds.Y + bounds.Height;

    private static bool Contains(InputClip clip, float x, float y)
    {
        if (!Contains(clip.Bounds, x, y))
            return false;
        if (!float.IsFinite(clip.CornerRadius) || clip.CornerRadius < 0)
            return false;
        var radius = Math.Min(
            clip.CornerRadius,
            Math.Min(clip.Bounds.Width, clip.Bounds.Height) / 2
        );
        if (radius == 0)
            return true;

        var left = clip.Bounds.X;
        var top = clip.Bounds.Y;
        var right = left + clip.Bounds.Width;
        var bottom = top + clip.Bounds.Height;
        var dx =
            x < left + radius ? left + radius - x
            : x >= right - radius ? x - (right - radius)
            : 0;
        var dy =
            y < top + radius ? top + radius - y
            : y >= bottom - radius ? y - (bottom - radius)
            : 0;
        return dx * dx + dy * dy <= radius * radius;
    }

    private InputDispatchResult Reject(
        InputRejection rejection,
        string kind,
        List<Exception> errors
    )
    {
        SetLast(kind, InputDispatchStatus.Rejected, rejection, null, [], false);
        Throw(errors);
        return new(InputDispatchStatus.Rejected, rejection, null, [], false);
    }

    private void SetLast(
        string kind,
        InputDispatchStatus status,
        InputRejection rejection,
        ElementIdentity? target,
        IEnumerable<ElementIdentity> path,
        bool handled
    ) =>
        _lastRoute =
            "route kind="
            + kind
            + " status="
            + status
            + " reason="
            + rejection
            + " handled="
            + (handled ? "true" : "false")
            + " target="
            + (target?.ElementId.ToString(CultureInfo.InvariantCulture) ?? "-")
            + " path=["
            + string.Join(
                ',',
                path.Select(item => item.ElementId.ToString(CultureInfo.InvariantCulture))
            )
            + "]";

    private void Enter()
    {
        Check();
        if (_publicDepth != 0)
            throw new InvalidOperationException("Input router public operations cannot reenter.");
        _publicDepth++;
    }

    private void Exit() => _publicDepth--;

    private void Check()
    {
        _composition.CheckThread();
        ObjectDisposedException.ThrowIf(_disposed, typeof(InputRouter));
        _composition.ThrowIfDisposed();
    }

    private void Register<T>(
        List<Registration<T>> list,
        long elementId,
        ReactiveScope scope,
        T callback
    )
        where T : class
    {
        ArgumentNullException.ThrowIfNull(callback);
        var registration = new Registration<T>(elementId, checked(++_nextRegistration), callback);
        list.Add(registration);
        scope.OnDispose(() => list.Remove(registration));
    }

    internal ShapedText? TextParagraph(ElementIdentity identity)
    {
        if (_scene is null || !_textFields.TryGetValue(identity.ElementId, out var state))
            return null;
        var paragraph = _scene.Boxes.FirstOrDefault(box => box.Identity == identity).Text;
        return
            paragraph is not null
            && (
                state.IsConfidential
                || paragraph.SourceText is { } source
                    && StringComparer.Ordinal.Equals(source, state.DisplayText)
            )
            ? paragraph
            : null;
    }

    internal ParagraphHitTest? HitTestText(ElementIdentity identity, float x, float y)
    {
        if (_scene is null || !_textFields.TryGetValue(identity.ElementId, out var state))
            return null;
        if (state.DisplayText.Length == 0)
            return new(0, TextAffinity.Downstream, 0);
        var text = FindTextNode(_scene.Nodes, identity);
        if (
            text is null
            || (
                !state.IsConfidential
                && (
                    text.Text.SourceText is not { } source
                    || !StringComparer.Ordinal.Equals(source, state.DisplayText)
                )
            )
        )
            return null;
        return text.Text.HitTest(state.DisplayText, x - text.Bounds.X, y - text.Bounds.Y);
    }

    private static TextSceneNode? FindTextNode(
        IEnumerable<SceneNode> nodes,
        ElementIdentity identity
    )
    {
        foreach (var node in nodes)
        {
            if (node is TextSceneNode text && text.Identity.Element == identity)
                return text;
            var children = node switch
            {
                ClipSceneNode clip => clip.Children,
                OpacitySceneNode opacity => opacity.Children,
                _ => null,
            };
            if (children is not null && FindTextNode(children, identity) is { } nested)
                return nested;
        }
        return null;
    }

    internal bool ScrollTextIntoView(ElementIdentity identity, int textOffset, bool alignToTop)
    {
        if (
            !_scrollable.TryGetValue(identity.ElementId, out var scrollable)
            || !_textFields.TryGetValue(identity.ElementId, out var state)
            || _scene is null
        )
            return false;
        var box = _scene.Boxes.FirstOrDefault(item => item.Identity == identity);
        if (
            box.Text is not { } text
            || text.SourceText is not { } source
            || !StringComparer.Ordinal.Equals(source, state.DisplayText)
            || textOffset < 0
            || textOffset > state.DisplayText.Length
        )
            return false;
        var caret = text.CaretBounds(state.DisplayText, textOffset, state.Session.CaretAffinity);
        var viewport = _input.TryGetValue(identity.ElementId, out var retained)
            ? retained.ChildClipBounds ?? retained.Bounds
            : box.Bounds;
        var requested = scrollable.State.Offset;
        var nextY =
            alignToTop ? caret.Y
            : caret.Y < requested.Y ? caret.Y
            : caret.Y + caret.Height > requested.Y + viewport.Height
                ? caret.Y + caret.Height - viewport.Height
            : requested.Y;
        _ = SetScroll(identity, scrollable, requested.X, nextY, scrollable.InstalledOffset);
        return true;
    }

    private bool RevealEditorCaret()
    {
        if (
            _focused is not { } focus
            || !_textFields.TryGetValue(focus.Identity.ElementId, out var state)
            || !state.IsMultiline
            || !_scrollable.TryGetValue(focus.Identity.ElementId, out var scrollable)
            || !_input.TryGetValue(focus.Identity.ElementId, out var retained)
        )
            return false;
        var clip = retained.ChildClipBounds ?? retained.Bounds;
        var stamp = new EditorCaretStamp(
            state.EditGeneration,
            state.DisplayText,
            state.DisplayCaret,
            state.Session.CaretAffinity,
            clip.Width,
            clip.Height
        );
        if (scrollable.CaretStamp == stamp)
            return false;
        var before = scrollable.State.Offset;
        if (!ScrollTextIntoView(focus.Identity, state.DisplayCaret, alignToTop: false))
            return false;
        scrollable.CaretStamp = stamp;
        return before != scrollable.State.Offset;
    }

    internal bool ScrollBy(ElementIdentity identity, float horizontal, float vertical)
    {
        if (!float.IsFinite(horizontal) || !float.IsFinite(vertical))
            throw new ArgumentOutOfRangeException(nameof(horizontal));
        if (!_scrollable.TryGetValue(identity.ElementId, out var scrollable))
            return false;
        var current = scrollable.State.Offset;
        return SetScroll(
            identity,
            scrollable,
            current.X + horizontal,
            current.Y + vertical,
            scrollable.InstalledOffset
        );
    }

    internal bool ScrollTo(ElementIdentity identity, ScrollOffset offset)
    {
        if (!_scrollable.TryGetValue(identity.ElementId, out var scrollable))
            return false;
        offset.Validate();
        return SetScroll(identity, scrollable, offset.X, offset.Y, scrollable.InstalledOffset);
    }

    internal bool ScrollToEnd(ElementIdentity identity)
    {
        if (!_scrollable.TryGetValue(identity.ElementId, out var scrollable))
            return false;
        var bounds = ScrollBounds(identity, scrollable.InstalledOffset);
        return SetScroll(identity, scrollable, bounds.X, bounds.Y, scrollable.InstalledOffset);
    }

    private bool ClampScrolls(RetainedScene candidate)
    {
        var changed = false;
        foreach (var pair in _scrollable.ToArray())
            if (
                _input.TryGetValue(pair.Key, out var retained)
                && !candidate.IsCollapsed(retained.Identity.ElementId)
            )
            {
                var projected = pair.Value.State.Offset;
                changed |= SetScroll(
                    retained.Identity,
                    pair.Value,
                    projected.X,
                    projected.Y,
                    projected
                );
            }
        return changed;
    }

    private bool SetScroll(
        ElementIdentity identity,
        Scrollable scrollable,
        float requestedX,
        float requestedY,
        ScrollOffset projectedOffset
    )
    {
        var bounds = ScrollBounds(identity, projectedOffset);
        var next = new ScrollOffset(
            ClampScroll(requestedX, bounds.X),
            ClampScroll(requestedY, bounds.Y)
        );
        if (next == scrollable.State.Offset)
            return false;
        scrollable.State.Offset = next;
        return true;
    }

    private ScrollOffset ScrollBounds(ElementIdentity identity, ScrollOffset projectedOffset)
    {
        if (!_input.TryGetValue(identity.ElementId, out var viewport))
            return default;
        var content = viewport.ChildClipBounds ?? viewport.Bounds;
        var right = content.X;
        var bottom = content.Y;
        foreach (
            var child in _input.Values.Where(item =>
                _scene?.IsCollapsed(item.Identity.ElementId) != true
                && IsScrollContentDescendant(item.Identity, identity)
            )
        )
        {
            right = Math.Max(right, child.Bounds.X + child.Bounds.Width + projectedOffset.X);
            bottom = Math.Max(bottom, child.Bounds.Y + child.Bounds.Height + projectedOffset.Y);
        }
        if (
            _textFields.TryGetValue(identity.ElementId, out var textState)
            && textState.IsMultiline
            && _scene?.Boxes.FirstOrDefault(item => item.Identity == identity).Text is { } text
        )
        {
            right = Math.Max(right, content.X + text.Width);
            bottom = Math.Max(bottom, content.Y + text.Height);
        }
        return new(
            Math.Max(0, right - content.X - content.Width),
            Math.Max(0, bottom - content.Y - content.Height)
        );
    }

    private bool IsScrollContentDescendant(ElementIdentity identity, ElementIdentity ancestor)
    {
        while (
            _input.TryGetValue(identity.ElementId, out var retained)
            && retained.Parent is { } parent
        )
        {
            if (parent == ancestor)
                return true;
            if (_scrollable.ContainsKey(parent.ElementId))
                return false;
            identity = parent;
        }
        return false;
    }

    private bool IsWithin(ElementIdentity identity, ElementIdentity ancestor) =>
        identity == ancestor || IsDescendantOf(identity, ancestor);

    private bool IsDescendantOf(ElementIdentity identity, ElementIdentity ancestor)
    {
        while (
            _input.TryGetValue(identity.ElementId, out var retained)
            && retained.Parent is { } parent
        )
        {
            if (parent == ancestor)
                return true;
            identity = parent;
        }
        return false;
    }

    private static float ClampScroll(float requested, float maximum) =>
        !float.IsFinite(requested)
            ? requested > 0
                ? maximum
                : 0
            : Math.Clamp(requested, 0, maximum);

    private static void AppendRegistrations<T>(
        StringBuilder output,
        string kind,
        IEnumerable<Registration<T>> registrations
    )
        where T : class
    {
        foreach (
            var registration in registrations
                .OrderBy(item => item.ElementId)
                .ThenBy(item => item.Ordinal)
        )
            output
                .Append("registration kind=")
                .Append(kind)
                .Append(" owner=")
                .Append(registration.ElementId.ToString(CultureInfo.InvariantCulture))
                .Append(" ordinal=")
                .Append(registration.Ordinal.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
    }

    private static IReadOnlyList<(ElementIdentity Identity, T Callback)> Snapshot<T>(
        IEnumerable<Registration<T>> registrations,
        IEnumerable<ElementIdentity> route
    )
        where T : class =>
        route
            .SelectMany(identity =>
                registrations
                    .Where(item => item.ElementId == identity.ElementId)
                    .OrderBy(item => item.Ordinal)
                    .Select(item => (identity, item.Callback))
            )
            .ToArray();

    private static void Throw(List<Exception> errors)
    {
        if (errors.Count != 0)
            throw new AggregateException("Input callbacks failed.", errors);
    }

    private sealed class Registration<T>(long elementId, long ordinal, T callback)
        where T : class
    {
        public long ElementId { get; } = elementId;
        public long Ordinal { get; } = ordinal;
        public T Callback { get; } = callback;
    }

    private sealed class Focusable(bool tabStop, BehaviorContext context)
    {
        public bool TabStop { get; set; } = tabStop;
        public BehaviorContext Context { get; } = context;
    }

    private sealed class FocusTargetRegistration(long elementId, TextFieldState? state)
    {
        public long ElementId { get; } = elementId;
        public TextFieldState? State { get; } = state;
    }

    private sealed class Scrollable(ScrollViewportState state)
    {
        public ScrollViewportState State { get; } = state;
        public ScrollOffset InstalledOffset { get; set; } = state.Offset;
        public EditorCaretStamp? CaretStamp { get; set; }
    }

    private readonly record struct ScrollBarDrag(
        ElementIdentity Viewport,
        float StartPointerY,
        float StartOffsetY
    );

    private readonly record struct EditorCaretStamp(
        long EditGeneration,
        string Text,
        int Caret,
        TextAffinity Affinity,
        float Width,
        float Height
    );

    private readonly record struct InputClip(LayoutRect Bounds, float CornerRadius);

    private readonly record struct ClipboardTicket(
        ElementIdentity Origin,
        TextFieldState State,
        long Generation
    );

    private readonly record struct Capture(
        ElementIdentity Owner,
        long Generation,
        PointerButton Button
    );

    private readonly record struct FocusState(
        ElementIdentity Identity,
        ElementIdentity[] Path,
        int Order,
        FocusChangeReason Reason
    );

    private readonly record struct PendingFocus(
        ElementIdentity? Identity,
        FocusChangeReason Reason
    );

    private enum Unavailable
    {
        Scene,
        Hidden,
        Disabled,
    }
}
