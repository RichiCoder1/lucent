using System.Buffers;
using System.Globalization;
using System.Text;

namespace Lucent.Core;

/// <summary>A hoistable plain-text document draft, selection, undo history, and viewport.</summary>
/// <remarks>One text field may mount a session at a time. Platform focus, clipboard, and IME resources remain owned by that mount.</remarks>
public sealed class EditorSession : IDisposable
{
    private const int UndoLimit = 64;
    private readonly ReactiveScope _scope;
    private readonly Signal<string> _documentId;
    private readonly Signal<string> _text;
    private readonly Signal<int> _anchor;
    private readonly Signal<int> _caret;
    private readonly Signal<TextAffinity> _anchorAffinity;
    private readonly Signal<TextAffinity> _caretAffinity;
    private readonly Signal<long> _revision;
    private readonly List<Snapshot> _undo = [];
    private readonly List<Snapshot> _redo = [];
    private readonly HashSet<MountLease> _mounts = [];
    private bool _lastWasInsert;
    private long _editGeneration;
    private int[] _boundaries;
    private int _graphemeIndexBuildCount;
    private float? _desiredX;
    private string? _verticalLayoutIdentity;

    /// <summary>Creates a session for one identified document under an application-owned scope.</summary>
    public EditorSession(
        ReactiveScope owner,
        string documentId,
        string initialText = "",
        string name = "editor-session",
        bool multiline = false
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        documentId = RequiredDocumentId(documentId);
        var normalized = Normalize(initialText, multiline);
        IsMultiline = multiline;
        _scope = owner.CreateChild(name);
        try
        {
            _boundaries = ParseBoundaries(normalized);
            _graphemeIndexBuildCount = 1;
            _documentId = _scope.Signal(documentId, name + ".document");
            _text = _scope.Signal(normalized, name + ".text");
            _anchor = _scope.Signal(normalized.Length, name + ".anchor");
            _caret = _scope.Signal(normalized.Length, name + ".caret");
            _anchorAffinity = _scope.Signal(TextAffinity.Downstream, name + ".anchor-affinity");
            _caretAffinity = _scope.Signal(TextAffinity.Downstream, name + ".caret-affinity");
            _revision = _scope.Signal(0L, name + ".revision");
            Viewport = new ViewportState(_scope, name: name + ".viewport");
        }
        catch
        {
            _scope.Dispose();
            throw;
        }
    }

    /// <summary>Gets whether this session accepts canonical LF line breaks and tabs.</summary>
    public bool IsMultiline { get; }

    /// <summary>Gets the identity of the active document.</summary>
    public string DocumentId
    {
        get
        {
            CheckRead();
            return _documentId.Value;
        }
    }

    /// <summary>Gets or replaces the editable draft. A replacement is an undoable local edit.</summary>
    public string Text
    {
        get
        {
            CheckRead();
            return _text.Value;
        }
        set => ReplaceAll(value);
    }

    /// <summary>Gets the active end of the selection as a UTF-16 grapheme boundary.</summary>
    public int Caret
    {
        get
        {
            CheckRead();
            return _caret.Value;
        }
    }

    /// <summary>Gets the fixed end of the selection as a UTF-16 grapheme boundary.</summary>
    public int Anchor
    {
        get
        {
            CheckRead();
            return _anchor.Value;
        }
    }

    /// <summary>Gets the visual affinity of the active selection end at a wrapped boundary.</summary>
    public TextAffinity CaretAffinity
    {
        get
        {
            CheckRead();
            return _caretAffinity.Value;
        }
    }

    /// <summary>Gets the visual affinity of the fixed selection end at a wrapped boundary.</summary>
    public TextAffinity AnchorAffinity
    {
        get
        {
            CheckRead();
            return _anchorAffinity.Value;
        }
    }

    /// <summary>Gets the currently selected text.</summary>
    public string SelectedText => Slice(Math.Min(Anchor, Caret), Math.Max(Anchor, Caret));

    /// <summary>Gets whether a local edit can be undone.</summary>
    public bool CanUndo
    {
        get
        {
            CheckRead();
            _ = _revision.Value;
            return _undo.Count != 0;
        }
    }

    /// <summary>Gets whether a local edit can be redone.</summary>
    public bool CanRedo
    {
        get
        {
            CheckRead();
            _ = _revision.Value;
            return _redo.Count != 0;
        }
    }

    /// <summary>Gets the hoistable viewport state associated with this document session.</summary>
    public ViewportState Viewport { get; }

    /// <summary>Gets whether this session has released its reactive lifetime.</summary>
    public bool IsDisposed => _scope.IsDisposed;

    /// <summary>Moves to another document and intentionally resets selection, history, and viewport.</summary>
    /// <remarks>Calling this method for the current identifier is an explicit reset.</remarks>
    public void SwitchDocument(string documentId, string text)
    {
        CheckMutation();
        documentId = RequiredDocumentId(documentId);
        var normalized = Normalize(text, IsMultiline);
        _documentId.Value = documentId;
        SetText(normalized);
        _anchor.Value = _caret.Value = normalized.Length;
        _anchorAffinity.Value = _caretAffinity.Value = TextAffinity.Downstream;
        _undo.Clear();
        _redo.Clear();
        _lastWasInsert = false;
        BreakVerticalNavigation();
        Viewport.Offset = default;
        Changed();
    }

    /// <summary>Applies authoritative text only when it matches the active document identity.</summary>
    /// <remarks>An equal update for the active document is ignored so it preserves caret, selection, and undo history.</remarks>
    public void SynchronizeExternalText(string documentId, string text)
    {
        CheckMutation();
        documentId = RequiredDocumentId(documentId);
        var normalized = Normalize(text, IsMultiline);
        if (!string.Equals(documentId, DocumentId, StringComparison.Ordinal))
            throw new ArgumentException(
                "External text belongs to a different document. Switch documents explicitly.",
                nameof(documentId)
            );
        if (normalized == Text)
            return;
        SetText(normalized);
        _anchor.Value = BoundaryAtOrBefore(Math.Min(Anchor, normalized.Length));
        _caret.Value = BoundaryAtOrBefore(Math.Min(Caret, normalized.Length));
        _anchorAffinity.Value = _caretAffinity.Value = TextAffinity.Downstream;
        _undo.Clear();
        _redo.Clear();
        _lastWasInsert = false;
        BreakVerticalNavigation();
        Changed();
    }

    /// <summary>Moves one grapheme left.</summary>
    public void MoveLeft(bool extend = false) =>
        Move(
            !extend && Anchor != Caret ? Math.Min(Anchor, Caret) : PreviousBoundary(Caret),
            extend,
            TextAffinity.Upstream
        );

    /// <summary>Moves one grapheme right.</summary>
    public void MoveRight(bool extend = false) =>
        Move(
            !extend && Anchor != Caret ? Math.Max(Anchor, Caret) : NextBoundary(Caret),
            extend,
            TextAffinity.Downstream
        );

    /// <summary>Moves to the start of the draft.</summary>
    public void MoveHome(bool extend = false) => Move(0, extend, TextAffinity.Downstream);

    /// <summary>Moves to the end of the draft.</summary>
    public void MoveEnd(bool extend = false) => Move(Text.Length, extend, TextAffinity.Downstream);

    /// <summary>Moves to the start of the current newline-delimited logical line.</summary>
    public void MoveLineHome(bool extend = false)
    {
        CheckMutation();
        var lineStart = Caret == 0 ? -1 : Text.LastIndexOf('\n', Caret - 1);
        Move(lineStart < 0 ? 0 : lineStart + 1, extend, TextAffinity.Downstream);
    }

    /// <summary>Moves to the end of the current newline-delimited logical line.</summary>
    public void MoveLineEnd(bool extend = false)
    {
        CheckMutation();
        var lineEnd = Text.IndexOf('\n', Caret);
        Move(lineEnd < 0 ? Text.Length : lineEnd, extend, TextAffinity.Upstream);
    }

    /// <summary>Moves one visual paragraph line up while retaining the initial logical x position.</summary>
    public void MoveUp(ShapedText paragraph, bool extend = false) =>
        MoveVertical(paragraph, -1, extend);

    /// <summary>Moves one visual paragraph line down while retaining the initial logical x position.</summary>
    public void MoveDown(ShapedText paragraph, bool extend = false) =>
        MoveVertical(paragraph, 1, extend);

    /// <summary>Selects the entire draft.</summary>
    public void SelectAll() => SetSelection(0, Text.Length);

    /// <summary>Sets both grapheme-boundary ends of the selection.</summary>
    public void SetSelection(
        int anchor,
        int caret,
        TextAffinity anchorAffinity = TextAffinity.Downstream,
        TextAffinity caretAffinity = TextAffinity.Downstream
    )
    {
        CheckMutation();
        if (!Enum.IsDefined(anchorAffinity))
            throw new ArgumentOutOfRangeException(nameof(anchorAffinity));
        if (!Enum.IsDefined(caretAffinity))
            throw new ArgumentOutOfRangeException(nameof(caretAffinity));
        if (!IsBoundary(anchor) || !IsBoundary(caret))
            throw new ArgumentException("Selection must remain on grapheme boundaries.");
        if (
            Anchor == anchor
            && Caret == caret
            && AnchorAffinity == anchorAffinity
            && CaretAffinity == caretAffinity
        )
            return;
        _anchor.Value = anchor;
        _caret.Value = caret;
        _anchorAffinity.Value = anchorAffinity;
        _caretAffinity.Value = caretAffinity;
        _lastWasInsert = false;
        BreakVerticalNavigation();
        Changed();
    }

    /// <summary>Inserts valid single-line text at the current selection.</summary>
    public void Insert(string text) => Replace(text, coalesceInsert: true);

    /// <summary>Deletes the selection or preceding grapheme.</summary>
    public void DeleteBackward()
    {
        CheckMutation();
        if (Anchor != Caret)
            Replace("");
        else if (Caret > 0)
            Replace("", replacementStart: PreviousBoundary(Caret), replacementEnd: Caret);
    }

    /// <summary>Deletes the selection or following grapheme.</summary>
    public void DeleteForward()
    {
        CheckMutation();
        if (Anchor != Caret)
            Replace("");
        else if (Caret < Text.Length)
            Replace("", replacementStart: Caret, replacementEnd: NextBoundary(Caret));
    }

    /// <summary>Restores the previous local edit.</summary>
    public void Undo()
    {
        CheckMutation();
        if (_undo.Count == 0)
            return;
        _redo.Add(Current());
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        _lastWasInsert = false;
        Changed();
    }

    /// <summary>Reapplies the next locally undone edit.</summary>
    public void Redo()
    {
        CheckMutation();
        if (_redo.Count == 0)
            return;
        _undo.Add(Current());
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        _lastWasInsert = false;
        Changed();
    }

    /// <summary>Releases this editor session and its reactive resources.</summary>
    public void Dispose() => _scope.Dispose();

    /// <summary>Rejects newline and control input; valid text is returned as scalar-normalized UTF-16.</summary>
    public static bool TryNormalizeSingleLine(string? text, out string normalized) =>
        TryNormalize(text, multiline: false, out normalized);

    /// <summary>Normalizes CR/CRLF to LF and accepts tabs while rejecting other plain-text controls.</summary>
    public static bool TryNormalizeMultiline(string? text, out string normalized) =>
        TryNormalize(text, multiline: true, out normalized);

    private static bool TryNormalize(string? text, bool multiline, out string normalized)
    {
        normalized = "";
        if (text is null)
            return false;
        StringBuilder? output = null;
        for (var index = 0; index < text.Length; )
        {
            if (multiline && text[index] == '\r')
            {
                output ??= new StringBuilder(text.Length).Append(text, 0, index);
                output.Append('\n');
                index++;
                if (index < text.Length && text[index] == '\n')
                    index++;
                continue;
            }
            if (
                Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out var consumed)
                    != OperationStatus.Done
                || (
                    !(multiline && rune.Value is '\n' or '\t')
                    && Rune.GetUnicodeCategory(rune)
                        is UnicodeCategory.Control
                            or UnicodeCategory.LineSeparator
                            or UnicodeCategory.ParagraphSeparator
                )
            )
                return false;
            output?.Append(rune);
            index += consumed;
        }
        normalized = output?.ToString() ?? text;
        return true;
    }

    internal event Action? ChangedForMount;
    internal long EditGeneration => _editGeneration;
    internal int GraphemeIndexBuildCount => _graphemeIndexBuildCount;

    internal IDisposable AcquireMount(ReactiveScope mountScope)
    {
        ArgumentNullException.ThrowIfNull(mountScope);
        CheckRead();
        mountScope.Graph.CheckThread();
        if (!ReferenceEquals(_scope.Graph, mountScope.Graph))
            throw new ArgumentException(
                "Editor session and its text field must belong to the same reactive graph.",
                nameof(mountScope)
            );
        var lease = new MountLease(this);
        _mounts.Add(lease);
        mountScope.OnDispose(lease.Dispose);
        _ = mountScope.Effect(
            () =>
            {
                if (_mounts.Count > 1)
                    throw new InvalidOperationException(
                        "Editor session can have one established text-field mount."
                    );
            },
            "editor-session.mount-lease"
        );
        return lease;
    }

    internal void BreakInsertCoalescing()
    {
        CheckMutation();
        _lastWasInsert = false;
    }

    internal void Replace(
        string text,
        bool coalesceInsert = false,
        int? replacementStart = null,
        int? replacementEnd = null
    )
    {
        CheckMutation();
        text = Normalize(text, IsMultiline);
        var start = replacementStart ?? Math.Min(Anchor, Caret);
        var end = replacementEnd ?? Math.Max(Anchor, Caret);
        if (!IsBoundary(start) || !IsBoundary(end) || end < start)
            throw new ArgumentException("Replacement must use ordered grapheme boundaries.");
        if (start == end && text.Length == 0)
            return;
        Save(coalesceInsert && start == end && text.Length != 0);
        SetText(ReplaceRange(Text, start, end, text));
        _anchor.Value = _caret.Value = BoundaryAtOrAfter(start + text.Length);
        _anchorAffinity.Value = _caretAffinity.Value = TextAffinity.Downstream;
        BreakVerticalNavigation();
        Changed();
    }

    internal static void ValidateText(string text, bool multiline = false)
    {
        if (!(multiline ? TryNormalizeMultiline(text, out _) : TryNormalizeSingleLine(text, out _)))
            throw new ArgumentException(
                multiline
                    ? "Multiline editors accept plain text with canonical line breaks and tabs."
                    : "Text fields accept one line of Unicode scalar values without control characters.",
                nameof(text)
            );
    }

    private void ReplaceAll(string text)
    {
        CheckMutation();
        var normalized = Normalize(text, IsMultiline);
        if (normalized == Text)
            return;
        Save(false);
        SetText(normalized);
        _anchor.Value = _caret.Value = normalized.Length;
        _anchorAffinity.Value = _caretAffinity.Value = TextAffinity.Downstream;
        BreakVerticalNavigation();
        Changed();
    }

    private void Move(
        int caret,
        bool extend,
        TextAffinity affinity,
        bool preserveVerticalNavigation = false
    )
    {
        CheckMutation();
        if (!Enum.IsDefined(affinity))
            throw new ArgumentOutOfRangeException(nameof(affinity));
        if (!IsBoundary(caret))
            throw new ArgumentException("Caret must remain on a grapheme boundary.", nameof(caret));
        if (Caret == caret && CaretAffinity == affinity && (extend || Anchor == caret))
            return;
        _caret.Value = caret;
        _caretAffinity.Value = affinity;
        if (!extend)
        {
            _anchor.Value = caret;
            _anchorAffinity.Value = affinity;
        }
        _lastWasInsert = false;
        if (!preserveVerticalNavigation)
            BreakVerticalNavigation();
        Changed();
    }

    private void MoveVertical(ShapedText paragraph, int lineDelta, bool extend)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        CheckMutation();
        if (paragraph.Lines.Count == 0)
            return;
        if (!string.Equals(_verticalLayoutIdentity, paragraph.Identity, StringComparison.Ordinal))
        {
            _verticalLayoutIdentity = paragraph.Identity;
            _desiredX = null;
        }

        var caret = paragraph.CaretBounds(Text, Caret, CaretAffinity);
        _desiredX ??= caret.X;
        var current = paragraph.HitTest(Text, caret.X, caret.Y + caret.Height / 2).LineIndex;
        var target = Math.Clamp(current + lineDelta, 0, paragraph.Lines.Count - 1);
        if (target == current)
            return;
        var line = paragraph.Lines[target];
        var lineHeight = Math.Max(0, line.Descent - line.Ascent + line.Leading);
        var hit = paragraph.HitTest(Text, _desiredX.Value, line.Top + lineHeight / 2);
        Move(hit.Utf16Offset, extend, hit.Affinity, preserveVerticalNavigation: true);
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

    private Snapshot Current() =>
        new(Text, _boundaries, Anchor, Caret, AnchorAffinity, CaretAffinity);

    private void Restore(Snapshot value)
    {
        _text.Value = value.Text;
        _boundaries = value.Boundaries;
        _anchor.Value = value.Anchor;
        _caret.Value = value.Caret;
        _anchorAffinity.Value = value.AnchorAffinity;
        _caretAffinity.Value = value.CaretAffinity;
        BreakVerticalNavigation();
    }

    private int PreviousBoundary(int position)
    {
        var index = Array.BinarySearch(_boundaries, position);
        index = index >= 0 ? index : ~index;
        return _boundaries[Math.Max(0, index - 1)];
    }

    private int NextBoundary(int position)
    {
        var index = Array.BinarySearch(_boundaries, position);
        index = index >= 0 ? index + 1 : ~index;
        return _boundaries[Math.Min(_boundaries.Length - 1, index)];
    }

    private int BoundaryAtOrAfter(int position)
    {
        var index = Array.BinarySearch(_boundaries, position);
        index = index >= 0 ? index : ~index;
        return _boundaries[Math.Min(_boundaries.Length - 1, index)];
    }

    private int BoundaryAtOrBefore(int position)
    {
        var index = Array.BinarySearch(_boundaries, position);
        index = index >= 0 ? index : ~index - 1;
        return _boundaries[Math.Max(0, index)];
    }

    private bool IsBoundary(int position) =>
        position >= 0 && position <= Text.Length && Array.BinarySearch(_boundaries, position) >= 0;

    private string Slice(int start, int end) => Text[start..end];

    private void Changed()
    {
        _editGeneration = checked(_editGeneration + 1);
        _revision.Value = _editGeneration;
        ChangedForMount?.Invoke();
    }

    private void CheckRead()
    {
        _scope.Graph.CheckThread();
        ObjectDisposedException.ThrowIf(_scope.IsDisposed, this);
    }

    private void CheckMutation()
    {
        _scope.CheckMutationGuard();
        ObjectDisposedException.ThrowIf(_scope.IsDisposed, this);
    }

    private static string Normalize(string text, bool multiline) =>
        (
            multiline
                ? TryNormalizeMultiline(text, out var normalized)
                : TryNormalizeSingleLine(text, out normalized)
        )
            ? normalized
            : throw new ArgumentException(
                multiline
                    ? "Multiline editors accept plain text with canonical line breaks and tabs."
                    : "Text fields accept one line of Unicode scalar values without control characters.",
                nameof(text)
            );

    private void SetText(string text)
    {
        _text.Value = text;
        _boundaries = ParseBoundaries(text);
        _graphemeIndexBuildCount = checked(_graphemeIndexBuildCount + 1);
    }

    private static int[] ParseBoundaries(string text)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        var boundaries = new int[starts.Length + 1];
        starts.CopyTo(boundaries, 0);
        boundaries[^1] = text.Length;
        return boundaries;
    }

    private static string ReplaceRange(string source, int start, int end, string replacement)
    {
        if (start == 0 && end == source.Length)
            return replacement;
        return string.Create(
            checked(source.Length - (end - start) + replacement.Length),
            (Source: source, Start: start, End: end, Replacement: replacement),
            static (destination, state) =>
            {
                state.Source.AsSpan(0, state.Start).CopyTo(destination);
                state.Replacement.AsSpan().CopyTo(destination[state.Start..]);
                state
                    .Source.AsSpan(state.End)
                    .CopyTo(destination[(state.Start + state.Replacement.Length)..]);
            }
        );
    }

    private void BreakVerticalNavigation()
    {
        _desiredX = null;
        _verticalLayoutIdentity = null;
    }

    private static string RequiredDocumentId(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A document identity is required.", nameof(value))
            : value;

    private readonly record struct Snapshot(
        string Text,
        int[] Boundaries,
        int Anchor,
        int Caret,
        TextAffinity AnchorAffinity,
        TextAffinity CaretAffinity
    );

    private sealed class MountLease(EditorSession owner) : IDisposable
    {
        private EditorSession? _owner = owner;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            current?._mounts.Remove(this);
        }
    }
}
