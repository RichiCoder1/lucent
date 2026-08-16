using AvaloniaEdit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia;
using Lucent.Examples.Workbench;

namespace Lucent.Workbench.Tests;

[TestClass]
public sealed class EditorTests
{
    [TestMethod]
    public async Task Headless_harness_awaits_async_actions_and_propagates_faults()
    {
        var completed = false;
        await HeadlessTestHarness.RunWindowAsync(async _ =>
        {
            await Task.Yield();
            completed = true;
        });
        Assert.IsTrue(completed);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            HeadlessTestHarness.RunWindowAsync(async _ =>
            {
                await Task.Yield();
                throw new InvalidOperationException("async action failed");
            }));
    }

    [TestMethod]
    public async Task Document_session_tracks_edit_history_external_text_and_detach()
    {
        await HeadlessTestHarness.RunWindowAsync(window =>
        {
            var document = new OpenDocument("Program.cs", "initial");
            var notifications = 0;
            using var session = new DocumentSession(document, () => notifications++);
            var editor = new TextEditor { Focusable = true, Width = 300, Height = 300 };
            window.Content = editor;
            window.Show();
            window.UpdateLayout();
            window.Activate();
            session.Attach(editor);

            Assert.AreEqual("initial", editor.Text);
            Assert.AreSame(editor, session.Editor);
            editor.CaretOffset = editor.Text.Length;
            window.FocusManager!.Focus(editor, NavigationMethod.Pointer, KeyModifiers.None);
            window.KeyTextInput(" typed");
            Assert.AreEqual("initial typed", document.Text);
            Assert.IsTrue(document.IsDirty);
            Assert.IsTrue(session.CanUndo);

            session.Undo();
            Assert.AreEqual("initial", editor.Text);
            session.Redo();
            Assert.AreEqual("initial typed", editor.Text);

            editor.CaretOffset = editor.Text.Length;
            editor.Select(0, 7);
            session.ReplaceText("external");
            Assert.AreEqual("external", editor.Text);
            Assert.IsFalse(document.IsDirty);
            Assert.IsFalse(session.CanUndo);
            Assert.IsTrue(editor.CaretOffset <= editor.Text.Length);
            Assert.IsTrue(editor.SelectionStart + editor.SelectionLength <= editor.Text.Length);

            session.Detach(editor);
            session.Detach(editor);
            var detachedText = document.Text;
            editor.AppendText(" ignored");
            Assert.AreEqual(detachedText, document.Text);
            Assert.IsTrue(notifications > 0);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task Document_commands_use_native_editor_capabilities()
    {
        await HeadlessTestHarness.RunWindowAsync(window =>
        {
            var document = new OpenDocument("Program.cs", "select me");
            using var session = new DocumentSession(document, () => { });
            var editor = new TextEditor { Focusable = true, Width = 300, Height = 300 };
            window.Content = editor;
            window.Show();
            window.UpdateLayout();
            window.Activate();
            session.Attach(editor);

            window.FocusManager!.Focus(editor, NavigationMethod.Pointer, KeyModifiers.None);
            var focused = window.FocusManager.GetFocusedElement();
            Assert.IsTrue(ReferenceEquals(focused, editor) ||
                focused is Visual visual && visual.GetVisualAncestors().Contains(editor));
            Assert.IsTrue(session.CanSelectAll);
            session.SelectAll();
            Assert.AreEqual(editor.Text, editor.SelectedText);
            session.Copy();
            session.Detach(editor);
            Assert.IsFalse(session.CanCopy);
            Assert.IsFalse(session.CanUndo);
            return Task.CompletedTask;
        });
    }
}
