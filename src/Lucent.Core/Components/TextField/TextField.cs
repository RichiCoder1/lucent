using System.Text;

namespace Lucent.Core;

/// <summary>Portable IME and committed-text operations routed to a text field.</summary>
public enum TextInputKind
{
    /// <summary>Inserts committed text and ends IME composition.</summary>
    Commit,

    /// <summary>Updates uncommitted IME text and selection.</summary>
    Preedit,

    /// <summary>Cancels IME composition without editing committed text.</summary>
    Cancel,
}

/// <summary>Committed or IME preedit text; preedit offsets are Unicode-scalar positions.</summary>
public readonly record struct TextInputCommand(
    TextInputKind Kind,
    string Text,
    int Start = 0,
    int Length = 0
)
{
    /// <summary>Rejects newline and control input; valid text is returned as scalar-normalized UTF-16.</summary>
    public static bool TryNormalizeSingleLine(string? text, out string normalized) =>
        EditorSession.TryNormalizeSingleLine(text, out normalized);

    /// <summary>Normalizes canonical multiline editor input for adapter dispatch.</summary>
    public static bool TryNormalizeMultiline(string? text, out string normalized) =>
        EditorSession.TryNormalizeMultiline(text, out normalized);

    /// <summary>
    /// Gets whether an active IME composition owns an editing key before a
    /// text editor or host can mutate committed selection or text.
    /// </summary>
    public static bool IsImeOwnedKey(KeyCommand command) =>
        command.Kind == KeyCommandKind.Down
        && command.Key
            is Key.Left
                or Key.Right
                or Key.Up
                or Key.Down
                or Key.Home
                or Key.End
                or Key.PageUp
                or Key.PageDown
                or Key.Backspace
                or Key.Delete
                or Key.Enter
                or Key.Escape;

    /// <summary>Validates the value and throws when its fields are outside the supported contract.</summary>
    public void Validate(bool allowMultiline = false)
    {
        if (
            !Enum.IsDefined(Kind)
            || Text is null
            || (Kind != TextInputKind.Preedit && (Start != 0 || Length != 0))
        )
            throw new ArgumentException("Text input commands are finite and explicit.");
        EditorSession.ValidateText(Text, allowMultiline);
        var count = Text.EnumerateRunes().Count();
        if (
            Kind == TextInputKind.Preedit
            && (
                Start < -1
                || Length < -1
                || (Start == -1) != (Length == -1)
                || Start > count
                || (Start >= 0 && Length > count - Start)
            )
        )
            throw new ArgumentException("Preedit boundaries must be text-element boundaries.");
    }
}

/// <summary>Clipboard operations requested by a focused text field.</summary>
public enum TextClipboardOperation
{
    /// <summary>Requests selected text without changing the field.</summary>
    Copy,

    /// <summary>Requests selected text then deletes it after successful completion.</summary>
    Cut,

    /// <summary>Requests text to insert at the current selection.</summary>
    Paste,
}

/// <summary>An adapter-returned, one-shot clipboard capability. Instances cannot be forged by application code.</summary>
public sealed class TextClipboardRequest
{
    internal TextClipboardRequest(TextClipboardOperation operation, string? text)
    {
        Operation = operation;
        Text = text;
    }

    /// <summary>Gets the requested clipboard operation.</summary>
    public TextClipboardOperation Operation { get; }

    /// <summary>Gets selected text for copy or cut, or null for paste.</summary>
    public string? Text { get; }
}

/// <summary>Scope-owned, single-line Unicode text state. Positions are UTF-16 offsets constrained to grapheme boundaries.</summary>
internal class TextFieldState
{
    private readonly ReactiveScope _scope;
    private readonly EditorSession _session;
    private readonly Signal<Preedit> _preedit;
    private readonly Signal<bool> _focused;
    private readonly ScrollViewportState? _scrollState;
    private TextClipboardRequest? _clipboard;
    private long _displayEditGeneration = -1;
    private Preedit _displayPreedit;
    private (string Text, int Caret, int SelectionStart, int SelectionEnd)? _displayCache;

    internal TextFieldState(ReactiveScope scope, string name, EditorSession session)
    {
        _scope = scope;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _ = session.AcquireMount(scope);
        _preedit = scope.Signal(default(Preedit), name + ".preedit");
        _focused = scope.Signal(false, name + ".focused");
        IsMultiline = session.IsMultiline;
        _scrollState = IsMultiline
            ? new ScrollViewportState(scope, name + ".scroll", default, session.Viewport)
            : null;
        session.ChangedForMount += SessionChanged;
        scope.OnDispose(() => session.ChangedForMount -= SessionChanged);
    }

    internal bool IsMultiline { get; }
    internal ScrollViewportState? ScrollState => _scrollState;
    internal EditorSession Session => _session;

    public string Value
    {
        get => _session.Text;
        set => _session.Text = value;
    }
    public int Caret => _session.Caret;
    public int Anchor => _session.Anchor;
    public string PreeditText => _preedit.Value.Text ?? "";
    public int PreeditStart => _preedit.Value.Start;
    public int PreeditLength => _preedit.Value.Length;

    /// <summary>Committed text with the active composition replacing its original selection.</summary>
    public string DisplayText => Display().Text;

    /// <summary>UTF-16 display offset of the IME cursor, converted from SDL's scalar preedit offset.</summary>
    public int DisplayCaret => Display().Caret;
    internal int DisplaySelectionStart => Display().SelectionStart;
    internal int DisplaySelectionEnd => Display().SelectionEnd;
    internal bool HasPreedit => _preedit.Value.Active;
    internal bool Focused => _focused.Value;
    public bool CanUndo => _session.CanUndo;
    public bool CanRedo => _session.CanRedo;
    public string SelectedText => _session.SelectedText;

    public void MoveLeft(bool extend = false) => _session.MoveLeft(extend);

    public void MoveRight(bool extend = false) => _session.MoveRight(extend);

    public void MoveWordLeft(bool extend = false) => _session.MoveWordLeft(extend);

    public void MoveWordRight(bool extend = false) => _session.MoveWordRight(extend);

    public void MoveHome(bool extend = false)
    {
        if (IsMultiline)
            _session.MoveLineHome(extend);
        else
            _session.MoveHome(extend);
    }

    public void MoveEnd(bool extend = false)
    {
        if (IsMultiline)
            _session.MoveLineEnd(extend);
        else
            _session.MoveEnd(extend);
    }

    internal void MoveDocumentHome(bool extend = false) => _session.MoveHome(extend);

    internal void MoveDocumentEnd(bool extend = false) => _session.MoveEnd(extend);

    internal void MoveUp(ShapedText paragraph, bool extend = false) =>
        _session.MoveUp(paragraph, extend);

    internal void MoveDown(ShapedText paragraph, bool extend = false) =>
        _session.MoveDown(paragraph, extend);

    internal void MoveVisualHome(ShapedText paragraph, bool extend = false) =>
        _session.MoveVisualHome(paragraph, extend);

    internal void MoveVisualEnd(ShapedText paragraph, bool extend = false) =>
        _session.MoveVisualEnd(paragraph, extend);

    internal void MovePageUp(ShapedText paragraph, float viewportHeight, bool extend = false) =>
        _session.MovePageUp(paragraph, viewportHeight, extend);

    internal void MovePageDown(ShapedText paragraph, float viewportHeight, bool extend = false) =>
        _session.MovePageDown(paragraph, viewportHeight, extend);

    internal void SetSelection(
        int anchor,
        int caret,
        TextAffinity anchorAffinity = TextAffinity.Downstream,
        TextAffinity caretAffinity = TextAffinity.Downstream
    ) => _session.SetSelection(anchor, caret, anchorAffinity, caretAffinity);

    public void SelectAll() => _session.SelectAll();

    public void Insert(string text) => _session.Insert(text);

    public void DeleteBackward() => _session.DeleteBackward();

    public void DeleteForward() => _session.DeleteForward();

    public void DeleteWordBackward() => _session.DeleteWordBackward();

    public void DeleteWordForward() => _session.DeleteWordForward();

    public void Undo() => _session.Undo();

    public void Redo() => _session.Redo();

    public void SetPreedit(string text, int start, int length)
    {
        Check();
        new TextInputCommand(TextInputKind.Preedit, text, start, length).Validate(IsMultiline);
        // SDL sends an empty TextEditing update when the IME closes its
        // composition window.  It is a lifecycle update, rather than an
        // empty replacement that should remain visible as an active
        // composition.  Keeping Active=true here would make the next key
        // event cancel or replace an already-finished composition.
        if (text.Length == 0)
        {
            _ = CancelComposition();
            return;
        }
        var prior = _preedit.Value;
        var replacementStart = prior.Active ? prior.ReplacementStart : Math.Min(Anchor, Caret);
        var replacementEnd = prior.Active ? prior.ReplacementEnd : Math.Max(Anchor, Caret);
        if (!prior.Active)
            _session.BreakInsertCoalescing();
        _preedit.Value = new(
            Normalize(text),
            start,
            length,
            replacementStart,
            replacementEnd,
            true
        );
    }

    public void Commit(string text)
    {
        Check();
        new TextInputCommand(TextInputKind.Commit, text).Validate(IsMultiline);
        var composition = _preedit.Value;
        _preedit.Value = default;
        _session.Replace(
            text,
            coalesceInsert: !composition.Active,
            composition.Active ? composition.ReplacementStart : null,
            composition.Active ? composition.ReplacementEnd : null
        );
    }

    public bool CancelComposition()
    {
        Check();
        if (_preedit.Value.Active)
        {
            _preedit.Value = default;
            return true;
        }
        return false;
    }

    public void RequestClipboard(TextClipboardOperation operation)
    {
        Check();
        if (!Enum.IsDefined(operation))
            throw new ArgumentException("Clipboard operation must be finite.", nameof(operation));
        var selected = SelectedText;
        if (
            operation is TextClipboardOperation.Copy or TextClipboardOperation.Cut
            && selected.Length == 0
        )
            return;
        _clipboard = new(operation, operation == TextClipboardOperation.Paste ? null : selected);
    }

    internal bool TryTakeClipboard(out TextClipboardRequest request)
    {
        Check();
        if (_clipboard is not { } value)
        {
            request = null!;
            return false;
        }
        _clipboard = null;
        request = value;
        return true;
    }

    internal bool CompleteClipboard(
        TextClipboardRequest request,
        bool succeeded,
        string? text = null
    )
    {
        Check();
        if (!succeeded || request.Operation == TextClipboardOperation.Copy)
            return false;
        if (request.Operation == TextClipboardOperation.Cut && SelectedText == request.Text)
        {
            _session.Replace("", coalesceInsert: false);
            return true;
        }
        if (
            request.Operation == TextClipboardOperation.Paste
            && NormalizeClipboard(text, out var normalized)
        )
        {
            _session.Replace(normalized, coalesceInsert: false);
            return true;
        }
        return false;
    }

    internal void ReplaceAll(string text) => _session.Text = text;

    private (string Text, int Caret, int SelectionStart, int SelectionEnd) Display()
    {
        var composition = _preedit.Value;
        var source = _session.Text;
        var caret = _session.Caret;
        var anchor = _session.Anchor;
        var editGeneration = _session.EditGeneration;
        if (
            _displayCache is { } cached
            && _displayEditGeneration == editGeneration
            && _displayPreedit.Equals(composition)
        )
            return cached;
        (string Text, int Caret, int SelectionStart, int SelectionEnd) display;
        if (!composition.Active)
            display = (source, caret, Math.Min(anchor, caret), Math.Max(anchor, caret));
        else
        {
            var start = composition.ReplacementStart;
            var cursor = start + ScalarToUtf16(composition.Text!, Math.Max(0, composition.Start));
            var selectionStart =
                start + ScalarToUtf16(composition.Text!, Math.Max(0, composition.Start));
            var selectionEnd =
                selectionStart
                + (
                    composition.Length < 0
                        ? 0
                        : ScalarToUtf16(
                            composition.Text![(selectionStart - start)..],
                            composition.Length
                        )
                );
            display = (
                Value[..start] + composition.Text + Value[composition.ReplacementEnd..],
                cursor,
                selectionStart,
                selectionEnd
            );
        }
        _displayEditGeneration = editGeneration;
        _displayPreedit = composition;
        _displayCache = display;
        return display;
    }

    private static int ScalarToUtf16(string text, int scalarCount)
    {
        var offset = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (scalarCount-- == 0)
                break;
            offset += rune.Utf16SequenceLength;
        }
        return offset;
    }

    internal long EditGeneration => _session.EditGeneration;
    internal bool IsDisposed => _scope.IsDisposed;

    internal void SetFocused(bool focused)
    {
        Check();
        _focused.Value = focused;
    }

    private void SessionChanged()
    {
        if (!_scope.IsDisposed && _preedit.Value.Active)
            _preedit.Value = default;
    }

    private void Check()
    {
        _scope.CheckMutationGuard();
        ObjectDisposedException.ThrowIf(_scope.IsDisposed, typeof(TextFieldState));
    }

    internal static void ValidateText(string text) => EditorSession.ValidateText(text);

    internal static void ValidateMultilineText(string text) =>
        EditorSession.ValidateText(text, multiline: true);

    public static bool TryNormalizeSingleLine(string? text, out string normalized) =>
        EditorSession.TryNormalizeSingleLine(text, out normalized);

    public static bool TryNormalizeMultiline(string? text, out string normalized) =>
        EditorSession.TryNormalizeMultiline(text, out normalized);

    private string Normalize(string text) =>
        IsMultiline
            ? EditorSession.TryNormalizeMultiline(text, out var normalizedMultiline)
                ? normalizedMultiline
                : throw new ArgumentException(
                    "Text areas accept Unicode scalar values, LF line breaks, and tabs.",
                    nameof(text)
                )
            : EditorSession.TryNormalizeSingleLine(text, out var normalizedSingleLine)
                ? normalizedSingleLine
                : throw new ArgumentException(
                    "Text fields accept one line of Unicode scalar values without control characters.",
                    nameof(text)
                );

    private bool NormalizeClipboard(string? text, out string normalized) =>
        IsMultiline
            ? EditorSession.TryNormalizeMultiline(text, out normalized)
            : EditorSession.TryNormalizeSingleLine(text, out normalized);

    internal SemanticTextSnapshot SemanticText
    {
        get
        {
            var display = Display();
            return new SemanticTextSnapshot(
                display.Text,
                display.Caret == display.SelectionStart
                    ? display.SelectionEnd
                    : display.SelectionStart,
                display.Caret,
                _session.AnchorAffinity,
                _session.CaretAffinity
            );
        }
    }

    private readonly record struct Preedit(
        string? Text,
        int Start,
        int Length,
        int ReplacementStart,
        int ReplacementEnd,
        bool Active
    );
}

/// <summary>Text behavior bridges portable key/text commands to scope-owned field state and pointer editing.</summary>
/// <remarks>
/// Tab is intentionally left unhandled here so the input router can perform focus traversal. A
/// multiline editor can still receive a tab through committed text input or an explicit insert
/// command; the key itself never inserts text or changes the editor selection. An empty, unfocused
/// field projects its configured placeholder (the label by default) as visual content while its semantic value remains empty.
/// </remarks>
internal sealed class TextFieldBehavior(TextFieldState state, string name) : Behavior
{
    private int? _dragPointer;
    private int _dragAnchor;
    private TextAffinity _dragAnchorAffinity;

    public override string Name => name;
    public override BehaviorOwnership Ownership =>
        BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics;

    public override void Attach(BehaviorContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        var actions = SemanticAction.SetValue;
        if (state.IsMultiline)
            actions |=
                SemanticAction.Scroll
                | SemanticAction.SelectText
                | SemanticAction.ScrollTextIntoView;
        context.SetSemantics(
            new(
                SemanticRole.TextField,
                name,
                actions: actions,
                value: state.Value,
                text: state.SemanticText
            )
        );
        context.MakeFocusable();
        context.RegisterText(state);
        if (state.ScrollState is { } scroll)
            context.RegisterScrollable(scroll);
        context.OnSemanticCommand(command =>
        {
            if (command.Kind == SemanticCommandKind.Focus)
                return context.CompositionInput().FocusSemantic(context.Identity);
            if (command.Kind == SemanticCommandKind.SetValue)
            {
                string value;
                if (state.IsMultiline)
                {
                    if (!TextFieldState.TryNormalizeMultiline(command.Value, out value!))
                        return false;
                }
                else if (!TextFieldState.TryNormalizeSingleLine(command.Value, out value!))
                    return false;
                state.Value = value;
                return true;
            }
            if (command.Kind == SemanticCommandKind.Scroll && state.IsMultiline)
                return context.CompositionInput().ScrollSemantic(context.Identity, command);
            if (
                command.Kind == SemanticCommandKind.SelectText
                && state.IsMultiline
                && !state.HasPreedit
                && command.Anchor is { } anchor
                && command.Caret is { } caret
                && anchor <= state.DisplayText.Length
                && caret <= state.DisplayText.Length
            )
            {
                state.CancelComposition();
                state.SetSelection(anchor, caret);
                return true;
            }
            if (
                command.Kind == SemanticCommandKind.ScrollTextIntoView
                && state.IsMultiline
                && !state.HasPreedit
                && command.Anchor is { } scrollAnchor
                && command.Caret is { } scrollCaret
                && scrollAnchor <= state.DisplayText.Length
                && scrollCaret <= state.DisplayText.Length
            )
                return context
                    .CompositionInput()
                    .ScrollTextIntoView(
                        context.Identity,
                        command.AlignToTop ? scrollAnchor : scrollCaret,
                        command.AlignToTop
                    );
            return false;
        });
        context.Effect(
            () =>
                context.UpdateSemantics(
                    new(
                        SemanticRole.TextField,
                        name,
                        actions: actions,
                        value: state.Value,
                        text: state.SemanticText
                    )
                ),
            name + ".semantics"
        );
        context.OnFocus(route =>
        {
            state.SetFocused(route.Command.Kind == FocusCommandKind.Gained);
            if (route.Command.Kind == FocusCommandKind.Lost)
                state.CancelComposition();
        });
        context.OnCaptureLost(loss =>
        {
            if (_dragPointer == loss.PointerId)
                _dragPointer = null;
        });
        context.OnPointer(route =>
        {
            if (route.Command is { Kind: PointerCommandKind.Down, Button: PointerButton.Primary })
            {
                route.Focus();
                if (state.HasPreedit)
                {
                    state.CancelComposition();
                    route.Handled = true;
                    return;
                }
                if (
                    context
                        .CompositionInput()
                        .HitTestText(context.Identity, route.Command.X, route.Command.Y) is
                    { } hit
                )
                {
                    _dragPointer = route.Command.PointerId;
                    var extendSelection = route.Command.Modifiers.HasFlag(KeyModifiers.Shift);
                    _dragAnchor = extendSelection ? state.Anchor : hit.Utf16Offset;
                    _dragAnchorAffinity = extendSelection
                        ? state.Session.AnchorAffinity
                        : hit.Affinity;
                    state.SetSelection(
                        _dragAnchor,
                        hit.Utf16Offset,
                        _dragAnchorAffinity,
                        hit.Affinity
                    );
                    route.Capture();
                }
                route.Handled = true;
                return;
            }
            if (
                _dragPointer == route.Command.PointerId
                && (
                    route.Command.Kind == PointerCommandKind.Move
                    || route.Command.Releases(PointerButton.Primary)
                )
            )
            {
                if (
                    context
                        .CompositionInput()
                        .HitTestText(context.Identity, route.Command.X, route.Command.Y) is
                    { } hit
                )
                {
                    state.SetSelection(
                        _dragAnchor,
                        hit.Utf16Offset,
                        _dragAnchorAffinity,
                        hit.Affinity
                    );
                }
                if (route.Command.Releases(PointerButton.Primary))
                    _dragPointer = null;
                route.Handled = true;
            }
        });
        context.OnText(route =>
        {
            if (
                !state.IsMultiline
                && route.Command.Kind is TextInputKind.Commit or TextInputKind.Preedit
                && !TextFieldState.TryNormalizeSingleLine(route.Command.Text, out _)
            )
                return;
            switch (route.Command.Kind)
            {
                case TextInputKind.Commit:
                    state.Commit(route.Command.Text);
                    break;
                case TextInputKind.Preedit:
                    state.SetPreedit(route.Command.Text, route.Command.Start, route.Command.Length);
                    break;
                case TextInputKind.Cancel:
                    state.CancelComposition();
                    break;
            }
            route.Handled = true;
        });
        context.OnKey(route =>
        {
            if (route.Command.Kind != KeyCommandKind.Down)
                return;
            // The IME owns navigation, deletion, commit, and cancellation
            // keys while a preedit is active.  Routing those keys into the
            // committed session would invalidate DisplayText geometry and
            // can cause the same text to be committed twice by the IME.
            if (state.HasPreedit && TextInputCommand.IsImeOwnedKey(route.Command))
            {
                if (route.Command.Key == Key.Escape)
                    _ = state.CancelComposition();
                route.Handled = true;
                return;
            }
            var shift = route.Command.Modifiers.HasFlag(KeyModifiers.Shift);
            var control =
                !route.Command.Modifiers.HasFlag(KeyModifiers.Alt)
                && (
                    route.Command.Modifiers.HasFlag(KeyModifiers.Control)
                    || route.Command.Modifiers.HasFlag(KeyModifiers.Meta)
                );
            if (control)
                switch (route.Command.Key)
                {
                    case Key.A:
                        state.SelectAll();
                        break;
                    case Key.C:
                        state.RequestClipboard(TextClipboardOperation.Copy);
                        break;
                    case Key.X:
                        state.RequestClipboard(TextClipboardOperation.Cut);
                        break;
                    case Key.V:
                        state.RequestClipboard(TextClipboardOperation.Paste);
                        break;
                    case Key.Z:
                        if (shift)
                            state.Redo();
                        else
                            state.Undo();
                        break;
                    case Key.Y:
                        state.Redo();
                        break;
                    case Key.Home:
                        state.MoveDocumentHome(shift);
                        break;
                    case Key.End:
                        state.MoveDocumentEnd(shift);
                        break;
                    case Key.Left:
                        state.MoveWordLeft(shift);
                        break;
                    case Key.Right:
                        state.MoveWordRight(shift);
                        break;
                    case Key.Backspace:
                        state.DeleteWordBackward();
                        break;
                    case Key.Delete:
                        state.DeleteWordForward();
                        break;
                    default:
                        return;
                }
            else
                switch (route.Command.Key)
                {
                    case Key.Left:
                        state.MoveLeft(shift);
                        break;
                    case Key.Right:
                        state.MoveRight(shift);
                        break;
                    case Key.Home:
                        if (
                            state.IsMultiline
                            && context.CompositionInput().TextParagraph(context.Identity)
                                is { } homeParagraph
                        )
                            state.MoveVisualHome(homeParagraph, shift);
                        else
                            state.MoveHome(shift);
                        break;
                    case Key.End:
                        if (
                            state.IsMultiline
                            && context.CompositionInput().TextParagraph(context.Identity)
                                is { } endParagraph
                        )
                            state.MoveVisualEnd(endParagraph, shift);
                        else
                            state.MoveEnd(shift);
                        break;
                    case Key.Enter when state.IsMultiline:
                        state.Insert("\n");
                        break;
                    case Key.Up when state.IsMultiline:
                        if (
                            context.CompositionInput().TextParagraph(context.Identity) is
                            { } upParagraph
                        )
                            state.MoveUp(upParagraph, shift);
                        else
                            return;
                        break;
                    case Key.Down when state.IsMultiline:
                        if (
                            context.CompositionInput().TextParagraph(context.Identity) is
                            { } downParagraph
                        )
                            state.MoveDown(downParagraph, shift);
                        else
                            return;
                        break;
                    case Key.PageUp when state.IsMultiline:
                        if (
                            context.CompositionInput().TextParagraph(context.Identity)
                                is { } pageUpParagraph
                            && context.CompositionInput().GetSemanticScroll(context.Identity)
                                is { } pageUpScroll
                        )
                            state.MovePageUp(pageUpParagraph, pageUpScroll.Viewport.Height, shift);
                        else
                            return;
                        break;
                    case Key.PageDown when state.IsMultiline:
                        if (
                            context.CompositionInput().TextParagraph(context.Identity)
                                is { } pageDownParagraph
                            && context.CompositionInput().GetSemanticScroll(context.Identity)
                                is { } pageDownScroll
                        )
                            state.MovePageDown(
                                pageDownParagraph,
                                pageDownScroll.Viewport.Height,
                                shift
                            );
                        else
                            return;
                        break;
                    case Key.Backspace:
                        state.DeleteBackward();
                        break;
                    case Key.Delete:
                        state.DeleteForward();
                        break;
                    default:
                        return;
                }
            route.Handled = true;
        });
    }
}
