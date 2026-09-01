using System.Globalization;
using System.Text;

namespace Lucent.Core;

/// <summary>Composition-owned UI-thread router. It accepts only retained Core metadata and portable commands.</summary>
public sealed class InputRouter
{
    private readonly Composition _composition;
    private readonly List<Registration<Action<PointerRoute>>> _pointer = [];
    private readonly List<Registration<Action<KeyRoute>>> _key = [];
    private readonly List<Registration<Action<FocusRoute>>> _focus = [];
    private readonly List<Registration<Action<TextRoute>>> _text = [];
    private readonly List<Registration<Action<PointerCaptureLoss>>> _captureLoss = [];
    private readonly Dictionary<long, Focusable> _focusable = [];
    private readonly Dictionary<long, Scrollable> _scrollable = [];
    private readonly Dictionary<long, TextFieldState> _textFields = [];
    private readonly Dictionary<TextClipboardRequest, ClipboardTicket> _clipboardTickets = [];
    private readonly Dictionary<int, Capture> _captures = [];
    private RetainedScene? _scene;
    private Dictionary<long, RetainedInputElement> _input = [];
    private FocusState? _focused; private PendingFocus? _pendingFocus;
    private int _publicDepth; private bool _focusing; private bool _disposed; private long _nextRegistration;
    private InputModality _modality;
    private string _lastRoute = "route kind=None status=Rejected reason=NoScene handled=false target=- path=[]";
    private string _lastFocusLoss = "-"; private string _lastCaptureLoss = "-";
    internal InputRouter(Composition composition) { _composition = composition ?? throw new ArgumentNullException(nameof(composition)); }
    public ElementIdentity? FocusedElement { get { Check(); return _focused?.Identity; } }
    public InputModality Modality { get { Check(); return _modality; } }

    public bool SetScene(RetainedScene scene)
    {
        Enter(); try
        {
            ArgumentNullException.ThrowIfNull(scene);
            if (!ValidateScene(scene))
            {
                var rejectedErrors = new List<Exception>();
                if (_scene is not null && EnsureScene(rejectedErrors) == InputRejection.StaleScene) Throw(rejectedErrors);
                return false;
            }
            var priorInput = _input;
            var nextInput = scene.Input.ToDictionary(item => item.Identity.ElementId);
            bool clamped;
            try { _input = nextInput; clamped = ClampScrolls(); }
            finally { _input = priorInput; }
            if (clamped)
            {
                var rejectedErrors = new List<Exception>();
                if (_scene is not null) ReleaseAll(PointerCaptureLossReason.SceneChanged, rejectedErrors);
                if (_focused is not null) RequestFocus(null, FocusChangeReason.SceneChanged, rejectedErrors);
                _scene = null; _input.Clear();
                Throw(rejectedErrors); return false;
            }
            var errors = new List<Exception>();
            var visualGeneration = _composition.InteractionVisualGeneration;
            _scene = scene; _input = nextInput;
            foreach (var scrollable in _scrollable)
                if (_input.ContainsKey(scrollable.Key)) scrollable.Value.InstalledOffset = scrollable.Value.State.Offset;
            foreach (var capture in _captures.ToArray())
                if (SameStructuralPath(capture.Value.Owner, priorInput)) _captures[capture.Key] = capture.Value with { Generation = scene.Generation };
                else Release(capture.Key, CaptureReason(capture.Value.Owner), errors);
            SyncAvailability(errors);
            if (_focused is { } focus && (!Eligible(focus.Identity) || !_input.ContainsKey(focus.Identity.ElementId) || !Path(focus.Identity).SequenceEqual(focus.Path)))
                RequestFocus(null, FocusChangeReason.Reordered, errors);
            if (_composition.InteractionVisualGeneration != visualGeneration)
            {
                _scene = null;
                Throw(errors); return false;
            }
            Throw(errors); return true;
        }
        finally { Exit(); }
    }

    public InputDispatchResult DispatchPointer(PointerCommand command)
    {
        Enter(); try
        {
            command.Validate(); var errors = new List<Exception>();
            if (EnsureScene(errors) is { } rejection) return Reject(rejection, "Pointer/" + command.Kind, errors);
            SyncAvailability(errors); if (command.Kind == PointerCommandKind.Down) SetModality(InputModality.Pointer, errors);
            ElementIdentity? target = null;
            if (_captures.TryGetValue(command.PointerId, out var capture))
            {
                if (capture.Generation == _scene!.Generation && Eligible(capture.Owner)) target = capture.Owner;
                else Release(command.PointerId, PointerCaptureLossReason.SceneChanged, errors);
            }
            target ??= Hit(command.X, command.Y);
            if (target is null) return Reject(InputRejection.NoTarget, "Pointer/" + command.Kind, errors);
            var result = RoutePointer(command, target.Value, errors);
            if (command.Kind == PointerCommandKind.Up) Release(command.PointerId, PointerCaptureLossReason.Released, errors);
            if (command.Kind == PointerCommandKind.Cancel) Release(command.PointerId, PointerCaptureLossReason.Cancelled, errors);
            Throw(errors); return result;
        }
        finally { Exit(); }
    }

    public InputDispatchResult DispatchKey(KeyCommand command)
    {
        Enter(); try
        {
            command.Validate(); var errors = new List<Exception>();
            if (EnsureScene(errors) is { } rejection) return Reject(rejection, "Key/" + command.Kind, errors);
            SyncAvailability(errors); if (command.Kind == KeyCommandKind.Down) SetModality(InputModality.Keyboard, errors);
            var target = _focused is { } focus && Eligible(focus.Identity) ? focus.Identity : new ElementIdentity(_composition.Epoch, _composition.Root.Id);
            if (!Eligible(target)) return Reject(InputRejection.Ineligible, "Key/" + command.Kind, errors);
            var result = RouteKey(command, target, errors);
            if (!result.Handled && command is { Kind: KeyCommandKind.Down, Key: Key.Tab }) { MoveFocusCore(command.Modifiers.HasFlag(KeyModifiers.Shift) ? FocusTraversalDirection.Previous : FocusTraversalDirection.Next, errors); result = new(result.Status, result.Rejection, result.Target, result.Route, true); }
            Throw(errors); return result;
        }
        finally { Exit(); }
    }

    public InputDispatchResult DispatchText(TextInputCommand command)
    {
        Enter(); try
        {
            command.Validate(); var errors = new List<Exception>();
            if (EnsureScene(errors) is { } rejection) return Reject(rejection, "Text/" + command.Kind, errors);
            SyncAvailability(errors);
            if (_focused is not { } focus || !Eligible(focus.Identity) || !_textFields.ContainsKey(focus.Identity.ElementId)) return Reject(InputRejection.NoTarget, "Text/" + command.Kind, errors);
            var handled = false;
            foreach (var callback in Snapshot(_text, [focus.Identity]))
            {
                var route = new TextRoute(command, callback.Identity);
                try { callback.Callback(route); } catch (Exception error) { errors.Add(error); }
                handled |= route.Finish(); if (handled) break;
            }
            SetLast("Text/" + command.Kind, InputDispatchStatus.Delivered, InputRejection.None, focus.Identity, [focus.Identity], handled); Throw(errors);
            return new(InputDispatchStatus.Delivered, InputRejection.None, focus.Identity, [focus.Identity], handled);
        }
        finally { Exit(); }
    }

    /// <summary>Adapters perform clipboard I/O after retrieving a one-shot request from its focused origin.</summary>
    public bool TryTakeClipboardRequest(out TextClipboardRequest request)
    {
        Enter(); try
        {
            request = default!;
            if (_focused is not { } focus || !Eligible(focus.Identity) || !_textFields.TryGetValue(focus.Identity.ElementId, out var state) || !state.TryTakeClipboard(out request)) return false;
            _clipboardTickets.Add(request, new(focus.Identity, state, state.EditGeneration)); return true;
        }
        finally { Exit(); }
    }
    /// <summary>Drops stale, replayed, moved-focus, or disposed clipboard completions without affecting another field.</summary>
    public bool CompleteClipboardRequest(TextClipboardRequest request, bool succeeded, string? text = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        Enter(); try
        {
            if (!_clipboardTickets.Remove(request, out var ticket) || ticket.State.IsDisposed || ticket.State.EditGeneration != ticket.Generation || !_textFields.TryGetValue(ticket.Origin.ElementId, out var state) || !ReferenceEquals(state, ticket.State)) return false;
            if (request.Operation is TextClipboardOperation.Paste or TextClipboardOperation.Cut && (_focused is not { } focus || focus.Identity != ticket.Origin || !Eligible(ticket.Origin))) return false;
            return ticket.State.CompleteClipboard(request, succeeded, text);
        }
        finally { Exit(); }
    }
    /// <summary>Uses the installed shaped text snapshot to anchor native candidates at the focused caret.</summary>
    public bool TryGetCaretGeometry(out LayoutRect rectangle)
    {
        Enter(); try
        {
            rectangle = default;
            if (_scene is null || !ValidateScene(_scene) || _focused is not { } focus || !Eligible(focus.Identity) || !_textFields.ContainsKey(focus.Identity.ElementId)) return false;
            var caret = FindCaret(_scene.Nodes, focus.Identity);
            if (caret is null) return false;
            rectangle = caret.Bounds; return true;
        }
        finally { Exit(); }
    }

    public bool MoveFocus(FocusTraversalDirection direction)
    {
        Enter(); try
        {
            if (!Enum.IsDefined(direction)) throw new ArgumentException("Focus traversal direction must be finite.", nameof(direction));
            var errors = new List<Exception>(); if (EnsureScene(errors) is not null) { Throw(errors); return false; }
            SyncAvailability(errors); SetModality(InputModality.Keyboard, errors); var moved = MoveFocusCore(direction, errors); Throw(errors); return moved;
        }
        finally { Exit(); }
    }

    /// <summary>Focuses one current retained semantic target without exposing platform focus transport.</summary>
    public bool FocusSemantic(ElementIdentity identity)
    {
        Enter(); try
        {
            var errors = new List<Exception>();
            if (EnsureScene(errors) is not null || !Eligible(identity) || !_focusable.ContainsKey(identity.ElementId)) { Throw(errors); return false; }
            SetModality(InputModality.Keyboard, errors); RequestFocus(identity, FocusChangeReason.Traversal, errors); Throw(errors);
            return _focused?.Identity == identity;
        }
        finally { Exit(); }
    }

    /// <summary>Applies a bounded semantic scroll request through the installed retained geometry.</summary>
    public bool ScrollSemantic(ElementIdentity identity, SemanticCommand command)
    {
        Enter(); try
        {
            command.Validate();
            if (command.Kind != SemanticCommandKind.Scroll || !_scrollable.TryGetValue(identity.ElementId, out var scrollable)) return false;
            var errors = new List<Exception>(); if (EnsureScene(errors) is not null || !Eligible(identity)) { Throw(errors); return false; }
            var offset = scrollable.State.Offset;
            var changed = command.Endpoint switch
            {
                SemanticScrollEndpoint.Start => SetScroll(identity, scrollable, 0, 0, offset),
                SemanticScrollEndpoint.End => ScrollToEnd(identity),
                _ => SetScroll(identity, scrollable, offset.X + command.Horizontal, offset.Y + command.Vertical, offset)
            };
            Throw(errors); return changed;
        }
        finally { Exit(); }
    }

    /// <summary>Returns the installed retained scroll geometry for a semantic adapter snapshot.</summary>
    public SemanticScrollState? GetSemanticScroll(ElementIdentity identity)
    {
        Check();
        if (!_scrollable.TryGetValue(identity.ElementId, out var scrollable) || !_input.TryGetValue(identity.ElementId, out var viewport) || !Eligible(identity)) return null;
        return new(scrollable.State.Offset, ScrollBounds(identity, scrollable.InstalledOffset), viewport.ChildClipBounds ?? viewport.Bounds);
    }

    public string Dump()
    {
        Check(); var output = new StringBuilder("input scene=");
        output.Append(_scene is null ? "-" : _composition.Epoch.ToString(CultureInfo.InvariantCulture) + "/" + _scene.Generation.ToString(CultureInfo.InvariantCulture)).Append(" signature=").Append(_scene?.InputSignature ?? "-").Append(" modality=").Append(_modality).Append('\n');
        output.Append("registrations pointer=").Append(_pointer.Count.ToString(CultureInfo.InvariantCulture)).Append(" key=").Append(_key.Count.ToString(CultureInfo.InvariantCulture)).Append(" focus=").Append(_focus.Count.ToString(CultureInfo.InvariantCulture)).Append(" captureLoss=").Append(_captureLoss.Count.ToString(CultureInfo.InvariantCulture)).Append(" focusables=").Append(_focusable.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        AppendRegistrations(output, "pointer", _pointer); AppendRegistrations(output, "key", _key); AppendRegistrations(output, "focus", _focus); AppendRegistrations(output, "capture-loss", _captureLoss);
        foreach (var focusable in _focusable.OrderBy(item => item.Key)) output.Append("focusable owner=").Append(focusable.Key.ToString(CultureInfo.InvariantCulture)).Append(" tabStop=").Append(focusable.Value.TabStop ? "true" : "false").Append('\n');
        output.Append("focus owner=").Append(_focused?.Identity.ElementId.ToString(CultureInfo.InvariantCulture) ?? "-").Append(" reason=").Append(_focused?.Reason.ToString() ?? "-").Append(" lastLoss=").Append(_lastFocusLoss).Append('\n');
        foreach (var capture in _captures.OrderBy(item => item.Key)) output.Append("capture pointer=").Append(capture.Key.ToString(CultureInfo.InvariantCulture)).Append(" owner=").Append(capture.Value.Owner.ElementId.ToString(CultureInfo.InvariantCulture)).Append(" generation=").Append(capture.Value.Generation.ToString(CultureInfo.InvariantCulture)).Append('\n');
        return output.Append("captureLoss=").Append(_lastCaptureLoss).Append('\n').Append(_lastRoute).Append('\n').ToString();
    }

    internal void RegisterPointer(long elementId, ReactiveScope scope, Action<PointerRoute> callback) => Register(_pointer, elementId, scope, callback);
    internal void RegisterKey(long elementId, ReactiveScope scope, Action<KeyRoute> callback) => Register(_key, elementId, scope, callback);
    internal void RegisterFocus(long elementId, ReactiveScope scope, Action<FocusRoute> callback) => Register(_focus, elementId, scope, callback);
    internal void RegisterText(long elementId, ReactiveScope scope, Action<TextRoute> callback) => Register(_text, elementId, scope, callback);
    internal void RegisterCaptureLoss(long elementId, ReactiveScope scope, Action<PointerCaptureLoss> callback) => Register(_captureLoss, elementId, scope, callback);
    internal void RegisterFocusable(long elementId, ReactiveScope scope, bool tabStop, BehaviorContext context)
    {
        if (_focusable.ContainsKey(elementId)) throw new InvalidOperationException("An element has one focus behavior.");
        var entry = new Focusable(tabStop, context); _focusable.Add(elementId, entry);
        scope.OnDispose(() => { if (_focusable.TryGetValue(elementId, out var current) && ReferenceEquals(current, entry)) _focusable.Remove(elementId); });
    }
    internal void RegisterScrollable(long elementId, ReactiveScope scope, ScrollViewportState state)
    {
        if (_scrollable.ContainsKey(elementId)) throw new InvalidOperationException("An element has one scroll behavior.");
        var entry = new Scrollable(state); _scrollable.Add(elementId, entry);
        scope.OnDispose(() => { if (_scrollable.TryGetValue(elementId, out var current) && ReferenceEquals(current, entry)) _scrollable.Remove(elementId); });
    }
    internal void RegisterTextField(long elementId, ReactiveScope scope, TextFieldState state)
    {
        if (_textFields.ContainsKey(elementId)) throw new InvalidOperationException("An element has one text behavior.");
        _textFields.Add(elementId, state); scope.OnDispose(() =>
        {
            _textFields.Remove(elementId);
            foreach (var request in _clipboardTickets.Where(ticket => ReferenceEquals(ticket.Value.State, state)).Select(ticket => ticket.Key).ToArray()) _clipboardTickets.Remove(request);
        });
    }
    internal void RemoveElement(Element element, PointerCaptureLossReason reason)
    {
        var errors = new List<Exception>();
        foreach (var pointer in _captures.Where(pair => pair.Value.Owner.ElementId == element.Id).Select(pair => pair.Key).ToArray()) Release(pointer, reason, errors);
        if (_focused is { } focus && focus.Identity.ElementId == element.Id) RequestFocus(null, FocusChangeReason.Disposed, errors);
        Throw(errors);
    }
    internal bool TryCapture(int pointerId, ElementIdentity owner, bool isDown)
    {
        if (!isDown || _scene is null || _composition.Find(owner) is not { IsDisposed: false } || !Eligible(owner)) return false;
        if (_captures.TryGetValue(pointerId, out var current)) return current.Owner == owner && current.Generation == _scene.Generation;
        _captures.Add(pointerId, new(owner, _scene.Generation)); return true;
    }
    internal void RequestFocus(ElementIdentity? identity, FocusChangeReason reason, List<Exception> errors)
    {
        _pendingFocus = new(identity, reason);
        if (_focusing) return;
        _focusing = true;
        try
        {
            for (var turns = 0; _pendingFocus is { } pending; turns++)
            {
                if (turns == 64) { errors.Add(new InvalidOperationException("Focus callbacks did not converge.")); _pendingFocus = null; break; }
                _pendingFocus = null;
                ElementIdentity? next = pending.Identity is { } candidate && Eligible(candidate) && _focusable.ContainsKey(candidate.ElementId) ? candidate : null;
                if (_focused is { } same && next is { } current && same.Identity == current) { SetFocusedVisual(current, true); continue; }
                if (_focused is { } old)
                {
                    _focused = null; SetFocusedVisual(old.Identity, false); _lastFocusLoss = pending.Reason.ToString();
                    InvokeFocus(old.Identity, new(FocusCommandKind.Lost, pending.Reason, _modality), errors);
                }
                if (_pendingFocus is not null) continue;
                if (next is { } selected && Eligible(selected) && _focusable.ContainsKey(selected.ElementId))
                {
                    _input.TryGetValue(selected.ElementId, out var retained); _focused = new(selected, Path(selected).ToArray(), retained.Order, pending.Reason); SetFocusedVisual(selected, true);
                    InvokeFocus(selected, new(FocusCommandKind.Gained, pending.Reason, _modality), errors);
                }
            }
        }
        finally { _focusing = false; }
    }
    internal void Cleanup()
    {
        _composition.CheckThread();
        if (_disposed) return;
        var errors = new List<Exception>();
        ReleaseAll(PointerCaptureLossReason.Disposed, errors);
        RequestFocus(null, FocusChangeReason.Disposed, errors);
        foreach (var focusable in _focusable.Values.ToArray())
        {
            try { focusable.Context.SetState(BehaviorState.Focused, false); focusable.Context.SetState(BehaviorState.FocusVisible, false); }
            catch (Exception error) { errors.Add(error); }
        }
        _disposed = true;
        _pointer.Clear(); _key.Clear(); _focus.Clear(); _text.Clear(); _captureLoss.Clear(); _focusable.Clear(); _scrollable.Clear(); _textFields.Clear(); _clipboardTickets.Clear(); _input.Clear(); _scene = null; _focused = null; _pendingFocus = null;
        Throw(errors);
    }

    private InputDispatchResult RoutePointer(PointerCommand command, ElementIdentity target, List<Exception> errors)
    {
        var route = Path(target).Reverse().ToArray(); var callbacks = Snapshot(_pointer, route);
        var handled = false;
        foreach (var callback in callbacks)
        {
            var context = new PointerRoute(this, command, target, callback.Identity, route, errors);
            try { callback.Callback(context); } catch (Exception error) { errors.Add(error); }
            handled |= context.Finish(); if (handled) break;
        }
        SetLast("Pointer/" + command.Kind, InputDispatchStatus.Delivered, InputRejection.None, target, route, handled);
        return new(InputDispatchStatus.Delivered, InputRejection.None, target, route, handled);
    }
    private InputDispatchResult RouteKey(KeyCommand command, ElementIdentity target, List<Exception> errors)
    {
        var route = Path(target).Reverse().ToArray(); var callbacks = Snapshot(_key, route);
        var handled = false;
        foreach (var callback in callbacks)
        {
            var context = new KeyRoute(this, command, target, callback.Identity, route);
            try { callback.Callback(context); } catch (Exception error) { errors.Add(error); }
            handled |= context.Finish(); if (handled) break;
        }
        SetLast("Key/" + command.Kind, InputDispatchStatus.Delivered, InputRejection.None, target, route, handled);
        return new(InputDispatchStatus.Delivered, InputRejection.None, target, route, handled);
    }
    private bool MoveFocusCore(FocusTraversalDirection direction, List<Exception> errors)
    {
        var candidates = _scene!.Input.Where(item => Eligible(item.Identity) && _focusable.TryGetValue(item.Identity.ElementId, out var focusable) && focusable.TabStop &&
            (!_scrollable.ContainsKey(item.Identity.ElementId) || !HasFocusableDescendant(item.Identity))).OrderBy(item => item.Order).ToArray();
        if (candidates.Length == 0) { RequestFocus(null, FocusChangeReason.Traversal, errors); return false; }
        var index = _focused is { } focused ? Array.FindIndex(candidates, item => item.Identity == focused.Identity) : -1;
        index = direction == FocusTraversalDirection.Next ? (index + 1 + candidates.Length) % candidates.Length : (index - 1 + candidates.Length) % candidates.Length;
        RequestFocus(candidates[index].Identity, FocusChangeReason.Traversal, errors); return true;
    }
    private bool HasFocusableDescendant(ElementIdentity ancestor)
    {
        foreach (var candidate in _scene!.Input)
        {
            for (var parent = candidate.Parent; parent is { } current;)
            {
                if (current == ancestor && Eligible(candidate.Identity) && _focusable.TryGetValue(candidate.Identity.ElementId, out var focusable) && focusable.TabStop) return true;
                if (!_input.TryGetValue(current.ElementId, out var retained)) break;
                parent = retained.Parent;
            }
        }
        return false;
    }
    private void SetModality(InputModality modality, List<Exception> errors)
    {
        if (_modality == modality) return; _modality = modality;
        if (_focused is { } focus) SetFocusedVisual(focus.Identity, true);
    }
    private void SetFocusedVisual(ElementIdentity identity, bool focused)
    {
        if (_focusable.TryGetValue(identity.ElementId, out var focusable))
        { focusable.Context.SetState(BehaviorState.Focused, focused); focusable.Context.SetState(BehaviorState.FocusVisible, focused && _modality == InputModality.Keyboard); }
    }
    private void InvokeFocus(ElementIdentity target, FocusCommand command, List<Exception> errors)
    {
        foreach (var callback in _focus.Where(item => item.ElementId == target.ElementId).OrderBy(item => item.Ordinal).ToArray())
            try { callback.Callback(new FocusRoute(command, target)); } catch (Exception error) { errors.Add(error); }
    }
    private void ReleaseAll(PointerCaptureLossReason reason, List<Exception> errors) { foreach (var pointer in _captures.Keys.ToArray()) Release(pointer, reason, errors); }
    private void Release(int pointerId, PointerCaptureLossReason reason, List<Exception> errors)
    {
        if (!_captures.Remove(pointerId, out var capture)) return;
        _lastCaptureLoss = reason.ToString(); var loss = new PointerCaptureLoss(pointerId, reason, capture.Owner);
        foreach (var callback in _captureLoss.Where(item => item.ElementId == capture.Owner.ElementId).OrderBy(item => item.Ordinal).ToArray())
            try { callback.Callback(loss); } catch (Exception error) { errors.Add(error); }
    }
    private void SyncAvailability(List<Exception> errors)
    {
        foreach (var retained in _scene!.Input) if (_composition.Find(retained.Identity) is { } element) element.SetInputDisabledVariant(!Available(retained.Identity));
        if (_focused is { } focus && !Eligible(focus.Identity)) RequestFocus(null, FocusReason(focus.Identity), errors);
        foreach (var capture in _captures.ToArray()) if (!Eligible(capture.Value.Owner)) Release(capture.Key, CaptureReason(capture.Value.Owner), errors);
    }
    private InputRejection? EnsureScene(List<Exception> errors)
    {
        if (_scene is null) return InputRejection.NoScene;
        if (ValidateScene(_scene)) return null;
        foreach (var capture in _captures.ToArray()) Release(capture.Key, CaptureReason(capture.Value.Owner), errors);
        if (_focused is { } focus) RequestFocus(null, FocusReason(focus.Identity), errors);
        _scene = null; _input.Clear(); return InputRejection.StaleScene;
    }
    private bool ValidateScene(RetainedScene scene)
    {
        if (scene.Generation == 0 || scene.Generation != _composition.LatestSceneGeneration || scene.Input.Count != scene.Boxes.Count || scene.Input.Count == 0) return false;
        var live = _composition.Elements().ToArray();
        if (!scene.Input.Select(item => item.Identity).SequenceEqual(live.Select(item => new ElementIdentity(_composition.Epoch, item.Id)))) return false;
        var current = true;
        foreach (var item in scene.Input)
        {
            var element = _composition.Find(item.Identity);
            if (element is null || SceneLayout.InputSignature(element) != item.Signature) current = false;
            element?.ReconcileSemanticStateForInput();
        }
        return current;
    }
    private ElementIdentity? Hit(float x, float y)
    {
        foreach (var candidate in _scene!.Input.OrderByDescending(item => item.Order))
            if (Available(candidate.Identity) && Contains(candidate.Bounds, x, y) && ClippedIn(candidate.Identity, x, y)) return candidate.Identity;
        return null;
    }
    internal bool Contains(ElementIdentity identity, float x, float y) => _input.TryGetValue(identity.ElementId, out var retained) && Contains(retained.Bounds, x, y) && ClippedIn(identity, x, y);
    private bool Eligible(ElementIdentity identity) => _input.ContainsKey(identity.ElementId) && Available(identity);
    private bool Available(ElementIdentity identity)
    {
        for (var current = identity; ;)
        {
            if (!_input.TryGetValue(current.ElementId, out var retained) || !retained.Enabled || !retained.Visible) return false;
            if (retained.Parent is not { } parent) return true; current = parent;
        }
    }
    private PointerCaptureLossReason CaptureReason(ElementIdentity identity) => UnavailableReason(identity) switch { Unavailable.Hidden => PointerCaptureLossReason.Hidden, Unavailable.Disabled => PointerCaptureLossReason.Disabled, _ => PointerCaptureLossReason.SceneChanged };
    private FocusChangeReason FocusReason(ElementIdentity identity) => UnavailableReason(identity) switch { Unavailable.Hidden => FocusChangeReason.Hidden, Unavailable.Disabled => FocusChangeReason.Disabled, _ => FocusChangeReason.SceneChanged };
    private Unavailable UnavailableReason(ElementIdentity identity)
    {
        var chain = new List<ElementIdentity>();
        for (var current = identity; _input.TryGetValue(current.ElementId, out var retained);)
        { chain.Add(current); if (retained.Parent is not { } parent) break; current = parent; }
        foreach (var current in chain)
            if ((_input.TryGetValue(current.ElementId, out var retained) && !retained.Visible) || _composition.Find(current) is { IsDisposed: false } element && !element.Resolve(InputProperties.Visible).Value) return Unavailable.Hidden;
        foreach (var current in chain)
            if ((_input.TryGetValue(current.ElementId, out var retained) && !retained.Enabled) || _composition.Find(current) is { IsDisposed: false } element && !element.Resolve(InputProperties.Enabled).Value) return Unavailable.Disabled;
        return Unavailable.Scene;
    }
    private bool ClippedIn(ElementIdentity identity, float x, float y)
    {
        if (!_input.TryGetValue(identity.ElementId, out var candidate) || candidate.Parent is not { } current) return true;
        while (true)
        {
            var retained = _input[current.ElementId]; if (retained.ChildClipBounds is { } clip && !Contains(clip, x, y)) return false;
            if (retained.Parent is not { } parent) return true; current = parent;
        }
    }
    private static PaintSceneNode? FindCaret(IEnumerable<SceneNode> nodes, ElementIdentity identity)
    {
        foreach (var node in nodes)
        {
            if (node is PaintSceneNode { Identity.Kind: SceneNodeKind.Caret } caret && caret.Identity.Element == identity) return caret;
            if (node is ClipSceneNode clip && FindCaret(clip.Children, identity) is { } nested) return nested;
            if (node is OpacitySceneNode opacity && FindCaret(opacity.Children, identity) is { } nestedOpacity) return nestedOpacity;
        }
        return null;
    }
    private bool SameStructuralPath(ElementIdentity identity, IReadOnlyDictionary<long, RetainedInputElement> prior)
    {
        var current = identity;
        while (true)
        {
            if (!prior.TryGetValue(current.ElementId, out var old) || !_input.TryGetValue(current.ElementId, out var next) ||
                old.Identity != next.Identity || old.Parent != next.Parent || old.Order != next.Order)
                return false;
            if (old.Parent is not { } parent) return Eligible(identity);
            current = parent;
        }
    }
    private IEnumerable<ElementIdentity> Path(ElementIdentity identity)
    {
        var path = new List<ElementIdentity>();
        for (var current = identity; ;)
        { path.Add(current); var retained = _input[current.ElementId]; if (retained.Parent is not { } parent) break; current = parent; }
        path.Reverse(); return path;
    }
    private static bool Contains(LayoutRect bounds, float x, float y) => bounds.Width > 0 && bounds.Height > 0 && x >= bounds.X && y >= bounds.Y && x < bounds.X + bounds.Width && y < bounds.Y + bounds.Height;
    private InputDispatchResult Reject(InputRejection rejection, string kind, List<Exception> errors) { SetLast(kind, InputDispatchStatus.Rejected, rejection, null, [], false); Throw(errors); return new(InputDispatchStatus.Rejected, rejection, null, [], false); }
    private void SetLast(string kind, InputDispatchStatus status, InputRejection rejection, ElementIdentity? target, IEnumerable<ElementIdentity> path, bool handled) => _lastRoute = "route kind=" + kind + " status=" + status + " reason=" + rejection + " handled=" + (handled ? "true" : "false") + " target=" + (target?.ElementId.ToString(CultureInfo.InvariantCulture) ?? "-") + " path=[" + string.Join(',', path.Select(item => item.ElementId.ToString(CultureInfo.InvariantCulture))) + "]";
    private void Enter() { Check(); if (_publicDepth != 0) throw new InvalidOperationException("Input router public operations cannot reenter."); _publicDepth++; }
    private void Exit() => _publicDepth--;
    private void Check() { _composition.CheckThread(); if (_disposed) throw new ObjectDisposedException(nameof(InputRouter)); _composition.ThrowIfDisposed(); }
    private void Register<T>(List<Registration<T>> list, long elementId, ReactiveScope scope, T callback) where T : class
    {
        ArgumentNullException.ThrowIfNull(callback); var registration = new Registration<T>(elementId, checked(++_nextRegistration), callback); list.Add(registration);
        scope.OnDispose(() => list.Remove(registration));
    }
    internal bool ScrollBy(ElementIdentity identity, float horizontal, float vertical)
    {
        if (!float.IsFinite(horizontal) || !float.IsFinite(vertical)) throw new ArgumentOutOfRangeException(nameof(horizontal));
        if (!_scrollable.TryGetValue(identity.ElementId, out var scrollable)) return false;
        var current = scrollable.State.Offset;
        return SetScroll(identity, scrollable, current.X + horizontal, current.Y + vertical, scrollable.InstalledOffset);
    }
    internal bool ScrollTo(ElementIdentity identity, ScrollOffset offset)
    {
        if (!_scrollable.TryGetValue(identity.ElementId, out var scrollable)) return false;
        offset.Validate(); return SetScroll(identity, scrollable, offset.X, offset.Y, scrollable.InstalledOffset);
    }
    internal bool ScrollToEnd(ElementIdentity identity)
    {
        if (!_scrollable.TryGetValue(identity.ElementId, out var scrollable)) return false;
        var bounds = ScrollBounds(identity, scrollable.InstalledOffset);
        return SetScroll(identity, scrollable, bounds.X, bounds.Y, scrollable.InstalledOffset);
    }
    private bool ClampScrolls()
    {
        var changed = false;
        foreach (var pair in _scrollable.ToArray())
            if (_input.TryGetValue(pair.Key, out var retained))
            {
                var projected = pair.Value.State.Offset;
                changed |= SetScroll(retained.Identity, pair.Value, projected.X, projected.Y, projected);
            }
        return changed;
    }
    private bool SetScroll(ElementIdentity identity, Scrollable scrollable, float requestedX, float requestedY, ScrollOffset projectedOffset)
    {
        var bounds = ScrollBounds(identity, projectedOffset);
        var next = new ScrollOffset(ClampScroll(requestedX, bounds.X), ClampScroll(requestedY, bounds.Y));
        if (next == scrollable.State.Offset) return false;
        scrollable.State.Offset = next; return true;
    }
    private ScrollOffset ScrollBounds(ElementIdentity identity, ScrollOffset projectedOffset)
    {
        if (!_input.TryGetValue(identity.ElementId, out var viewport)) return default;
        var content = viewport.ChildClipBounds ?? viewport.Bounds;
        var right = content.X; var bottom = content.Y;
        foreach (var child in _input.Values.Where(item => IsDescendantOf(item.Identity, identity)))
        { right = Math.Max(right, child.Bounds.X + child.Bounds.Width + projectedOffset.X); bottom = Math.Max(bottom, child.Bounds.Y + child.Bounds.Height + projectedOffset.Y); }
        return new(Math.Max(0, right - content.X - content.Width), Math.Max(0, bottom - content.Y - content.Height));
    }
    private bool IsDescendantOf(ElementIdentity identity, ElementIdentity ancestor)
    {
        while (_input.TryGetValue(identity.ElementId, out var retained) && retained.Parent is { } parent)
        {
            if (parent == ancestor) return true;
            identity = parent;
        }
        return false;
    }
    private static float ClampScroll(float requested, float maximum) => !float.IsFinite(requested) ? requested > 0 ? maximum : 0 : Math.Clamp(requested, 0, maximum);
    private static void AppendRegistrations<T>(StringBuilder output, string kind, IEnumerable<Registration<T>> registrations) where T : class
    {
        foreach (var registration in registrations.OrderBy(item => item.ElementId).ThenBy(item => item.Ordinal)) output.Append("registration kind=").Append(kind).Append(" owner=").Append(registration.ElementId.ToString(CultureInfo.InvariantCulture)).Append(" ordinal=").Append(registration.Ordinal.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }
    private static IReadOnlyList<(ElementIdentity Identity, T Callback)> Snapshot<T>(IEnumerable<Registration<T>> registrations, IEnumerable<ElementIdentity> route) where T : class => route.SelectMany(identity => registrations.Where(item => item.ElementId == identity.ElementId).OrderBy(item => item.Ordinal).Select(item => (identity, item.Callback))).ToArray();
    private static void Throw(List<Exception> errors) { if (errors.Count != 0) throw new AggregateException("Input callbacks failed.", errors); }
    private sealed class Registration<T>(long elementId, long ordinal, T callback) where T : class { public long ElementId { get; } = elementId; public long Ordinal { get; } = ordinal; public T Callback { get; } = callback; }
    private sealed class Focusable(bool tabStop, BehaviorContext context) { public bool TabStop { get; } = tabStop; public BehaviorContext Context { get; } = context; }
    private sealed class Scrollable(ScrollViewportState state) { public ScrollViewportState State { get; } = state; public ScrollOffset InstalledOffset { get; set; } = state.Offset; }
    private readonly record struct ClipboardTicket(ElementIdentity Origin, TextFieldState State, long Generation);
    private readonly record struct Capture(ElementIdentity Owner, long Generation);
    private readonly record struct FocusState(ElementIdentity Identity, ElementIdentity[] Path, int Order, FocusChangeReason Reason);
    private readonly record struct PendingFocus(ElementIdentity? Identity, FocusChangeReason Reason);
    private enum Unavailable { Scene, Hidden, Disabled }
}
