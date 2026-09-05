using System.Buffers;
using System.Globalization;
using System.Text;

namespace Lucent.Core;

/// <summary>A hoistable single-line document draft, selection, undo history, and viewport.</summary>
/// <remarks>One text field may mount a session at a time. Platform focus, clipboard, and IME resources remain owned by that mount.</remarks>
public sealed class EditorSession : IDisposable
{
    private const int UndoLimit = 64;
    private readonly ReactiveScope _scope;
    private readonly Signal<string> _documentId;
    private readonly Signal<string> _text;
    private readonly Signal<int> _anchor;
    private readonly Signal<int> _caret;
    private readonly Signal<long> _revision;
    private readonly List<Snapshot> _undo = [];
    private readonly List<Snapshot> _redo = [];
    private readonly HashSet<MountLease> _mounts = [];
    private bool _lastWasInsert;
    private long _editGeneration;

    /// <summary>Creates a session for one identified document under an application-owned scope.</summary>
    public EditorSession(
        ReactiveScope owner,
        string documentId,
        string initialText = "",
        string name = "editor-session"
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        documentId = RequiredDocumentId(documentId);
        ValidateText(initialText);
        _scope = owner.CreateChild(name);
        try
        {
            var normalized = Normalize(initialText);
            _documentId = _scope.Signal(documentId, name + ".document");
            _text = _scope.Signal(normalized, name + ".text");
            _anchor = _scope.Signal(normalized.Length, name + ".anchor");
            _caret = _scope.Signal(normalized.Length, name + ".caret");
            _revision = _scope.Signal(0L, name + ".revision");
            Viewport = new ViewportState(_scope, name: name + ".viewport");
        }
        catch
        {
            _scope.Dispose();
            throw;
        }
    }

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
        ValidateText(text);
        var normalized = Normalize(text);
        _documentId.Value = documentId;
        _text.Value = normalized;
        _anchor.Value = _caret.Value = normalized.Length;
        _undo.Clear();
        _redo.Clear();
        _lastWasInsert = false;
        Viewport.Offset = default;
        Changed();
    }

    /// <summary>Applies authoritative text only when it matches the active document identity.</summary>
    /// <remarks>An equal update for the active document is ignored so it preserves caret, selection, and undo history.</remarks>
    public void SynchronizeExternalText(string documentId, string text)
    {
        CheckMutation();
        documentId = RequiredDocumentId(documentId);
        ValidateText(text);
        if (!string.Equals(documentId, DocumentId, StringComparison.Ordinal))
            throw new ArgumentException(
                "External text belongs to a different document. Switch documents explicitly.",
                nameof(documentId)
            );
        var normalized = Normalize(text);
        if (normalized == Text)
            return;
        _text.Value = normalized;
        _anchor.Value = BoundaryAtOrBefore(Math.Min(Anchor, normalized.Length));
        _caret.Value = BoundaryAtOrBefore(Math.Min(Caret, normalized.Length));
        _undo.Clear();
        _redo.Clear();
        _lastWasInsert = false;
        Changed();
    }

    /// <summary>Moves one grapheme left.</summary>
    public void MoveLeft(bool extend = false) =>
        Move(
            !extend && Anchor != Caret ? Math.Min(Anchor, Caret) : PreviousBoundary(Caret),
            extend
        );

    /// <summary>Moves one grapheme right.</summary>
    public void MoveRight(bool extend = false) =>
        Move(!extend && Anchor != Caret ? Math.Max(Anchor, Caret) : NextBoundary(Caret), extend);

    /// <summary>Moves to the start of the draft.</summary>
    public void MoveHome(bool extend = false) => Move(0, extend);

    /// <summary>Moves to the end of the draft.</summary>
    public void MoveEnd(bool extend = false) => Move(Text.Length, extend);

    /// <summary>Selects the entire draft.</summary>
    public void SelectAll() => SetSelection(0, Text.Length);

    /// <summary>Sets both grapheme-boundary ends of the selection.</summary>
    public void SetSelection(int anchor, int caret)
    {
        CheckMutation();
        if (!IsBoundary(anchor) || !IsBoundary(caret))
            throw new ArgumentException("Selection must remain on grapheme boundaries.");
        if (Anchor == anchor && Caret == caret)
            return;
        _anchor.Value = anchor;
        _caret.Value = caret;
        _lastWasInsert = false;
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

    internal event Action? ChangedForMount;
    internal long EditGeneration => _editGeneration;

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
        ValidateText(text);
        text = Normalize(text);
        var start = replacementStart ?? Math.Min(Anchor, Caret);
        var end = replacementEnd ?? Math.Max(Anchor, Caret);
        if (!IsBoundary(start) || !IsBoundary(end) || end < start)
            throw new ArgumentException("Replacement must use ordered grapheme boundaries.");
        if (start == end && text.Length == 0)
            return;
        Save(coalesceInsert && start == end && text.Length != 0);
        _text.Value = Text[..start] + text + Text[end..];
        _anchor.Value = _caret.Value = BoundaryAtOrAfter(start + text.Length);
        Changed();
    }

    internal static void ValidateText(string text)
    {
        if (!TryNormalizeSingleLine(text, out _))
            throw new ArgumentException(
                "Text fields accept one line of Unicode scalar values without control characters.",
                nameof(text)
            );
    }

    private void ReplaceAll(string text)
    {
        CheckMutation();
        ValidateText(text);
        var normalized = Normalize(text);
        if (normalized == Text)
            return;
        Save(false);
        _text.Value = normalized;
        _anchor.Value = _caret.Value = normalized.Length;
        Changed();
    }

    private void Move(int caret, bool extend)
    {
        CheckMutation();
        if (!IsBoundary(caret))
            throw new ArgumentException("Caret must remain on a grapheme boundary.", nameof(caret));
        if (Caret == caret && (extend || Anchor == caret))
            return;
        _caret.Value = caret;
        if (!extend)
            _anchor.Value = caret;
        _lastWasInsert = false;
        Changed();
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

    private Snapshot Current() => new(Text, Anchor, Caret);

    private void Restore(Snapshot value)
    {
        _text.Value = value.Text;
        _anchor.Value = value.Anchor;
        _caret.Value = value.Caret;
    }

    private int PreviousBoundary(int position) =>
        Boundaries().Where(boundary => boundary < position).DefaultIfEmpty(0).Max();

    private int NextBoundary(int position) =>
        Boundaries().Where(boundary => boundary > position).DefaultIfEmpty(Text.Length).Min();

    private int BoundaryAtOrAfter(int position) =>
        Boundaries().First(boundary => boundary >= position);

    private int BoundaryAtOrBefore(int position) =>
        Boundaries().Where(boundary => boundary <= position).DefaultIfEmpty(0).Max();

    private bool IsBoundary(int position) =>
        position >= 0 && position <= Text.Length && Boundaries().Contains(position);

    private IEnumerable<int> Boundaries() =>
        StringInfo.ParseCombiningCharacters(Text).Append(Text.Length);

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

    private static string Normalize(string text) =>
        TryNormalizeSingleLine(text, out var normalized)
            ? normalized
            : throw new ArgumentException(
                "Text fields accept one line of Unicode scalar values without control characters.",
                nameof(text)
            );

    private static string RequiredDocumentId(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A document identity is required.", nameof(value))
            : value;

    private readonly record struct Snapshot(string Text, int Anchor, int Caret);

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
