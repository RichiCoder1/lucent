using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace Lucent.Desktop.Tests;

public sealed partial class PublishedInputTests
{
    [TestMethod]
    public void FlaUiRoutesWordNavigationDeletionAndLineUndoThroughNativeInput()
    {
        using var process = StartApplication();
        nint window = 0;
        try
        {
            window = WaitForWindow(process);
            using var automation = new UIA3Automation();
            var root = automation.FromHandle(window);
            PublishedIssueBrowserTests.ActivateOwnedWindow(process, root, window);
            var editor =
                root.FindFirstDescendant(condition =>
                    condition
                        .ByControlType(ControlType.Edit)
                        .And(condition.ByName("Multiline note"))
                ) ?? throw new InvalidOperationException("Multiline editor was not exposed.");
            editor.FocusNative();
            WaitUntil(
                process,
                () => editor.Properties.HasKeyboardFocus.Value,
                "Editor did not gain native focus."
            );
            Keyboard.Type("one two three");
            WaitUntil(
                process,
                () => editor.Patterns.Value.Pattern.Value.Value == "one two three",
                "Initial text did not settle."
            );
            using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
                PublishedIssueBrowserTests.TypeNavigation(VirtualKeyShort.LEFT);
            Keyboard.Press(VirtualKeyShort.CONTROL);
            Keyboard.Press(VirtualKeyShort.SHIFT);
            try
            {
                PublishedIssueBrowserTests.TypeNavigation(VirtualKeyShort.RIGHT);
            }
            finally
            {
                Keyboard.Release(VirtualKeyShort.SHIFT);
                Keyboard.Release(VirtualKeyShort.CONTROL);
            }
            Wait.UntilInputIsProcessed();
            WaitUntil(
                process,
                () =>
                    editor.Patterns.Text.Pattern.GetSelection() is { Length: 1 } selection
                    && selection[0].GetText(-1) == "three",
                "Native word selection did not select the final word."
            );
            Keyboard.Type("four");
            WaitUntil(
                process,
                () => editor.Patterns.Value.Pattern.Value.Value == "one two four",
                "Word selection was not replaced."
            );
            TypeChord(VirtualKeyShort.BACK);
            WaitUntil(
                process,
                () => editor.Patterns.Value.Pattern.Value.Value == "one two ",
                "Native word deletion was not routed."
            );
            TypeChord(VirtualKeyShort.KEY_Z);
            WaitUntil(
                process,
                () => editor.Patterns.Value.Pattern.Value.Value == "one two four",
                "Undo did not restore the deleted word."
            );
            Keyboard.Press(VirtualKeyShort.RETURN);
            Keyboard.Release(VirtualKeyShort.RETURN);
            Keyboard.Type("next");
            WaitUntil(
                process,
                () => editor.Patterns.Value.Pattern.Value.Value == "one two four\nnext",
                "Second line did not settle."
            );
            TypeChord(VirtualKeyShort.KEY_Z);
            WaitUntil(
                process,
                () => editor.Patterns.Value.Pattern.Value.Value == "one two four\n",
                "Typing undo crossed the newline boundary."
            );
            TypeChord(VirtualKeyShort.KEY_Z);
            WaitUntil(
                process,
                () => editor.Patterns.Value.Pattern.Value.Value == "one two four",
                "The newline was not a separate undo unit."
            );
        }
        finally
        {
            StopApplication(process, window);
        }
    }
}
