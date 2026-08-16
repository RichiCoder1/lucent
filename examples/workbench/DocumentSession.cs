using AvaloniaEdit;

namespace Lucent.Examples.Workbench;

internal sealed class DocumentSession : IDisposable
{
    private readonly OpenDocument _document;
    private readonly Action _notifyCommands;
    private TextEditor? _editor;
    private bool _applyingModelText;
    private bool _disposed;

    public DocumentSession(OpenDocument document, Action notifyCommands)
    {
        _document = document;
        _notifyCommands = notifyCommands;
    }

    public TextEditor? Editor => _editor;

    public bool CanCopy => _editor?.CanCopy == true;
    public bool CanUndo => _editor?.CanUndo == true;
    public bool CanRedo => _editor?.CanRedo == true;
    public bool CanSelectAll => _editor?.CanSelectAll == true;

    public void Attach(TextEditor editor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(_editor, editor)) return;
        Detach(_editor);
        _editor = editor;
        editor.TextChanged += EditorOnTextChanged;
        editor.TextArea.SelectionChanged += SelectionOnChanged;
        ReplaceModelText();
        _notifyCommands();
    }

    public void Detach(TextEditor? editor)
    {
        if (!ReferenceEquals(_editor, editor) || editor is null) return;
        editor.TextChanged -= EditorOnTextChanged;
        editor.TextArea.SelectionChanged -= SelectionOnChanged;
        _editor = null;
        _notifyCommands();
    }

    public void ReplaceText(string text)
    {
        _document.Text = text;
        _document.IsDirty = false;
        ReplaceModelText();
    }

    public void Copy()
    {
        if (_editor is not { CanCopy: true } editor) return;
        editor.Focus();
        editor.Copy();
    }

    public void Undo()
    {
        if (_editor is not { CanUndo: true } editor) return;
        editor.Focus();
        editor.Undo();
        _notifyCommands();
    }

    public void Redo()
    {
        if (_editor is not { CanRedo: true } editor) return;
        editor.Focus();
        editor.Redo();
        _notifyCommands();
    }

    public void SelectAll()
    {
        if (_editor is not { CanSelectAll: true } editor) return;
        editor.Focus();
        editor.SelectAll();
        _notifyCommands();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach(_editor);
    }

    private void EditorOnTextChanged(object? sender, EventArgs e)
    {
        if (_editor is null || _applyingModelText) return;
        _document.Text = _editor.Text;
        _document.IsDirty = true;
        _notifyCommands();
    }

    private void SelectionOnChanged(object? sender, EventArgs e) => _notifyCommands();

    private void ReplaceModelText()
    {
        if (_editor is not { } editor || editor.Text == _document.Text) return;
        var caret = Math.Min(editor.CaretOffset, _document.Text.Length);
        var selectionStart = Math.Min(editor.SelectionStart, _document.Text.Length);
        var selectionLength = Math.Min(editor.SelectionLength, _document.Text.Length - selectionStart);
        var vertical = editor.VerticalOffset;
        var horizontal = editor.HorizontalOffset;
        _applyingModelText = true;
        try
        {
            editor.Document.Replace(0, editor.Document.TextLength, _document.Text);
            editor.Document.UndoStack.ClearAll();
            editor.CaretOffset = caret;
            editor.Select(selectionStart, selectionLength);
            editor.ScrollToVerticalOffset(vertical);
            editor.ScrollToHorizontalOffset(horizontal);
        }
        finally
        {
            _applyingModelText = false;
        }
        _notifyCommands();
    }
}
