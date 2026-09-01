using System.Buffers;
using System.Globalization;
using System.Text;

namespace Lucent.Core;

public enum TextInputKind
{
    Commit,
    Preedit,
    Cancel,
}

public readonly record struct TextInputCommand(
    TextInputKind Kind,
    string Text,
    int Start = 0,
    int Length = 0
)
{
    /// <summary>Rejects newline and control input; valid text is returned as scalar-normalized UTF-16.</summary>
    public static bool TryNormalizeSingleLine(string? text, out string normalized) =>
        TextFieldState.TryNormalizeSingleLine(text, out normalized);

    public void Validate()
    {
        if (
            !Enum.IsDefined(Kind)
            || Text is null
            || (Kind != TextInputKind.Preedit && (Start != 0 || Length != 0))
        )
            throw new ArgumentException("Text input commands are finite and explicit.");
        TextFieldState.ValidateText(Text);
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

public enum TextClipboardOperation
{
    Copy,
    Cut,
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

    public TextClipboardOperation Operation { get; }
    public string? Text { get; }
}

/// <summary>Scope-owned, single-line Unicode text state. Positions are UTF-16 offsets constrained to grapheme boundaries.</summary>
internal sealed class TextFieldState
{
    private const int UndoLimit = 64;
    private readonly ReactiveScope _scope;
    private readonly Signal<string> _value;
    private readonly Signal<int> _anchor;
    private readonly Signal<int> _caret;
    private readonly Signal<Preedit> _preedit;
    private readonly Signal<bool> _focused;
    private readonly List<Snapshot> _undo = [];
    private readonly List<Snapshot> _redo = [];
    private TextClipboardRequest? _clipboard;
    private bool _lastWasInsert;
    private long _editGeneration;

    internal TextFieldState(ReactiveScope scope, string name, string value)
    {
        _scope = scope;
        ValidateText(value);
        _value = scope.Signal(Normalize(value), name + ".value");
        _anchor = scope.Signal(_value.Value.Length, name + ".anchor");
        _caret = scope.Signal(_value.Value.Length, name + ".caret");
        _preedit = scope.Signal(default(Preedit), name + ".preedit");
        _focused = scope.Signal(false, name + ".focused");
    }

    public string Value
    {
        get => _value.Value;
        set
        {
            Check();
            ReplaceAll(value);
        }
    }
    public int Caret => _caret.Value;
    public int Anchor => _anchor.Value;
    public string PreeditText => _preedit.Value.Text ?? "";
    public int PreeditStart => _preedit.Value.Start;
    public int PreeditLength => _preedit.Value.Length;

    /// <summary>Committed text with the active composition replacing its original selection.</summary>
    public string DisplayText => Display().Text;

    /// <summary>UTF-16 display offset of the IME cursor, converted from SDL's scalar preedit offset.</summary>
    public int DisplayCaret => Display().Caret;
    internal int DisplaySelectionStart => Display().SelectionStart;
    internal int DisplaySelectionEnd => Display().SelectionEnd;
    internal bool Focused => _focused.Value;
    public bool CanUndo => _undo.Count != 0;
    public bool CanRedo => _redo.Count != 0;
    public string SelectedText => Slice(Math.Min(Anchor, Caret), Math.Max(Anchor, Caret));

    public void MoveLeft(bool extend = false) =>
        Move(
            !extend && Anchor != Caret ? Math.Min(Anchor, Caret) : PreviousBoundary(Caret),
            extend
        );

    public void MoveRight(bool extend = false) =>
        Move(!extend && Anchor != Caret ? Math.Max(Anchor, Caret) : NextBoundary(Caret), extend);

    public void MoveHome(bool extend = false) => Move(0, extend);

    public void MoveEnd(bool extend = false) => Move(Value.Length, extend);

    public void SelectAll() => SetSelection(0, Value.Length);

    public void Insert(string text) => Replace(text, coalesceInsert: true);

    public void DeleteBackward()
    {
        if (Anchor != Caret)
            Replace("");
        else if (Caret > 0)
        {
            SetSelection(PreviousBoundary(Caret), Caret);
            Replace("");
        }
    }

    public void DeleteForward()
    {
        if (Anchor != Caret)
            Replace("");
        else if (Caret < Value.Length)
        {
            SetSelection(Caret, NextBoundary(Caret));
            Replace("");
        }
    }

    public void Undo()
    {
        Check();
        if (_undo.Count == 0)
            return;
        _redo.Add(Current());
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        Changed();
        _lastWasInsert = false;
        CancelComposition();
    }

    public void Redo()
    {
        Check();
        if (_redo.Count == 0)
            return;
        _undo.Add(Current());
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        Changed();
        _lastWasInsert = false;
        CancelComposition();
    }

    public void SetPreedit(string text, int start, int length)
    {
        Check();
        new TextInputCommand(TextInputKind.Preedit, text, start, length).Validate();
        var prior = _preedit.Value;
        var replacementStart = prior.Active ? prior.ReplacementStart : Math.Min(Anchor, Caret);
        var replacementEnd = prior.Active ? prior.ReplacementEnd : Math.Max(Anchor, Caret);
        if (!prior.Active)
            _lastWasInsert = false;
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
        new TextInputCommand(TextInputKind.Commit, text).Validate();
        var composition = _preedit.Value;
        _preedit.Value = default;
        Replace(
            text,
            coalesceInsert: !composition.Active,
            composition.Active ? composition.ReplacementStart : null,
            composition.Active ? composition.ReplacementEnd : null
        );
    }

    public void CancelComposition()
    {
        Check();
        if (_preedit.Value.Active)
            _preedit.Value = default;
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
            Replace("", coalesceInsert: false);
            return true;
        }
        if (
            request.Operation == TextClipboardOperation.Paste
            && TryNormalizeSingleLine(text, out var normalized)
        )
        {
            Replace(normalized, coalesceInsert: false);
            return true;
        }
        return false;
    }

    internal void ReplaceAll(string text)
    {
        ValidateText(text);
        var normalized = Normalize(text);
        if (normalized == Value)
            return;
        Save(false);
        _value.Value = normalized;
        _anchor.Value = _caret.Value = normalized.Length;
        Changed();
        CancelComposition();
    }

    private void Replace(
        string text,
        bool coalesceInsert = false,
        int? replacementStart = null,
        int? replacementEnd = null
    )
    {
        Check();
        ValidateText(text);
        text = Normalize(text);
        var start = replacementStart ?? Math.Min(Anchor, Caret);
        var end = replacementEnd ?? Math.Max(Anchor, Caret);
        if (start == end && text.Length == 0)
            return;
        Save(coalesceInsert && start == end && text.Length != 0);
        _value.Value = Value[..start] + text + Value[end..];
        _anchor.Value = _caret.Value = BoundaryAtOrAfter(start + text.Length);
        Changed();
        CancelComposition();
    }

    private void Move(int caret, bool extend)
    {
        Check();
        if (!IsBoundary(caret))
            throw new ArgumentException("Caret must remain on a grapheme boundary.", nameof(caret));
        if (Caret == caret && (extend || Anchor == caret))
            return;
        _caret.Value = caret;
        if (!extend)
            _anchor.Value = caret;
        Changed();
        _lastWasInsert = false;
        CancelComposition();
    }

    private void SetSelection(int anchor, int caret)
    {
        Check();
        if (!IsBoundary(anchor) || !IsBoundary(caret))
            throw new ArgumentException("Selection must remain on grapheme boundaries.");
        if (Anchor == anchor && Caret == caret)
            return;
        _anchor.Value = anchor;
        _caret.Value = caret;
        Changed();
        _lastWasInsert = false;
    }

    private void Save(bool coalesce)
    {
        if (!coalesce || !_lastWasInsert)
        {
            _undo.Add(Current());
            if (_undo.Count > UndoLimit)
                _undo.RemoveAt(0);
        }
        _redo.Clear();
        _lastWasInsert = coalesce;
    }

    private Snapshot Current() => new(Value, Anchor, Caret);

    private void Restore(Snapshot value)
    {
        _value.Value = value.Value;
        _anchor.Value = value.Anchor;
        _caret.Value = value.Caret;
    }

    private int PreviousBoundary(int position) =>
        Boundaries().Where(boundary => boundary < position).DefaultIfEmpty(0).Max();

    private int NextBoundary(int position) =>
        Boundaries().Where(boundary => boundary > position).DefaultIfEmpty(Value.Length).Min();

    private int BoundaryAtOrAfter(int position) =>
        Boundaries().First(boundary => boundary >= position);

    private bool IsBoundary(int position) =>
        position >= 0 && position <= Value.Length && Boundaries().Contains(position);

    private IEnumerable<int> Boundaries() =>
        StringInfo.ParseCombiningCharacters(Value).Append(Value.Length);

    private string Slice(int start, int end) => Value[start..end];

    private (string Text, int Caret, int SelectionStart, int SelectionEnd) Display()
    {
        var composition = _preedit.Value;
        if (!composition.Active)
            return (Value, Caret, Math.Min(Anchor, Caret), Math.Max(Anchor, Caret));
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
        return (
            Value[..start] + composition.Text + Value[composition.ReplacementEnd..],
            cursor,
            selectionStart,
            selectionEnd
        );
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

    internal long EditGeneration => _editGeneration;
    internal bool IsDisposed => _scope.IsDisposed;

    internal void SetFocused(bool focused)
    {
        Check();
        _focused.Value = focused;
    }

    private void Changed() => _editGeneration = checked(_editGeneration + 1);

    private void Check()
    {
        _scope.CheckMutationGuard();
        ObjectDisposedException.ThrowIf(_scope.IsDisposed, typeof(TextFieldState));
    }

    internal static void ValidateText(string text)
    {
        if (!TryNormalizeSingleLine(text, out _))
            throw new ArgumentException(
                "Text fields accept one line of Unicode scalar values without control characters.",
                nameof(text)
            );
    }

    /// <summary>Rejects newline and control input; valid text is returned as scalar-normalized UTF-16.</summary>
    public static bool TryNormalizeSingleLine(string? text, out string normalized)
    {
        normalized = "";
        if (text is null)
            return false;
        var output = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; )
        {
            if (
                Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out var consumed)
                    != OperationStatus.Done
                || Rune.GetUnicodeCategory(rune)
                    is UnicodeCategory.Control
                        or UnicodeCategory.LineSeparator
                        or UnicodeCategory.ParagraphSeparator
            )
                return false;
            output.Append(rune);
            index += consumed;
        }
        normalized = output.ToString();
        return true;
    }

    private static string Normalize(string text) =>
        TryNormalizeSingleLine(text, out var normalized)
            ? normalized
            : throw new ArgumentException(
                "Text fields accept one line of Unicode scalar values without control characters.",
                nameof(text)
            );

    private readonly record struct Snapshot(string Value, int Anchor, int Caret);

    private readonly record struct Preedit(
        string? Text,
        int Start,
        int Length,
        int ReplacementStart,
        int ReplacementEnd,
        bool Active
    );
}

/// <summary>Text behavior bridges portable key/text commands to scope-owned field state; pointer down focuses but intentionally does not place a caret.</summary>
internal sealed class TextFieldBehavior(TextFieldState state, string name) : Behavior
{
    public override string Name => name;
    public override BehaviorOwnership Ownership =>
        BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics;

    public override void Attach(BehaviorContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        context.SetSemantics(
            new(SemanticRole.TextField, name, actions: SemanticAction.SetValue, value: state.Value)
        );
        context.MakeFocusable();
        context.RegisterText(state);
        context.OnSemanticCommand(command =>
        {
            if (command.Kind == SemanticCommandKind.Focus)
                return context.CompositionInput().FocusSemantic(context.Identity);
            if (
                command.Kind != SemanticCommandKind.SetValue
                || !TextFieldState.TryNormalizeSingleLine(command.Value, out var value)
            )
                return false;
            state.Value = value;
            return true;
        });
        context.Effect(
            () =>
                context.UpdateSemantics(
                    new(
                        SemanticRole.TextField,
                        name,
                        actions: SemanticAction.SetValue,
                        value: state.Value
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
        context.OnPointer(route =>
        {
            if (route.Command is { Kind: PointerCommandKind.Down, Button: PointerButton.Primary })
            {
                route.Focus();
                route.Handled = true;
            }
        });
        context.OnText(route =>
        {
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
                        state.MoveHome(shift);
                        break;
                    case Key.End:
                        state.MoveEnd(shift);
                        break;
                    case Key.Backspace:
                        state.DeleteBackward();
                        break;
                    case Key.Delete:
                        state.DeleteForward();
                        break;
                    case Key.Escape:
                        state.CancelComposition();
                        break;
                    default:
                        return;
                }
            route.Handled = true;
        });
    }
}
