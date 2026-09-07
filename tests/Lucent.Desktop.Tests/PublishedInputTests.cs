using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace Lucent.Desktop.Tests;

[TestClass]
public sealed partial class PublishedInputTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [TestMethod]
    public void FlaUiDeliversRealCaptureFindSaveKeysAndMouseWheel()
    {
        using var process = StartApplication();
        try
        {
            var window = WaitForWindow(process);
            using var automation = new UIA3Automation();
            var root = automation.FromHandle(window);
            var status =
                root.FindFirstDescendant(condition =>
                    condition.ByControlType(ControlType.StatusBar)
                ) ?? throw new InvalidOperationException("FlaUI could not find command status.");
            var scroll =
                root.FindFirstDescendant(condition =>
                    condition.ByControlType(ControlType.Pane).And(condition.ByName("Wheel target"))
                ) ?? throw new InvalidOperationException("FlaUI could not find wheel viewport.");
            var bounds = scroll.BoundingRectangle;
            var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            root.SetForeground();
            Mouse.LeftClick(center);
            scroll.FocusNative();
            WaitUntil(
                process,
                () => GetForegroundWindow() == window && scroll.Properties.HasKeyboardFocus.Value,
                "Fixture window and wheel viewport did not gain native foreground focus."
            );

            var pattern = scroll.Patterns.Scroll.Pattern;
            var before = pattern.VerticalScrollPercent.Value;
            Mouse.MoveTo(center);
            Mouse.Scroll(-1);
            WaitUntil(
                process,
                () => pattern.VerticalScrollPercent.Value > before,
                "Physical mouse wheel input did not update the retained viewport."
            );

            root.SetForeground();
            scroll.FocusNative();
            TypeChord(VirtualKeyShort.KEY_N);
            WaitUntil(process, () => status.Name == "Capture", "Ctrl+N was not delivered.");
            TypeChord(VirtualKeyShort.KEY_F);
            WaitUntil(process, () => status.Name == "Find", "Ctrl+F was not delivered.");
            TypeChord(VirtualKeyShort.KEY_S);
            WaitUntil(process, () => status.Name == "Save", "Ctrl+S was not delivered.");
        }
        finally
        {
            StopApplication(process);
        }
    }

    [TestMethod]
    public void FlaUiEditsMultilineTextAndReadsSelectedTextRanges()
    {
        using var process = StartApplication();
        try
        {
            var window = WaitForWindow(process);
            using var automation = new UIA3Automation();
            var root = automation.FromHandle(window);
            FlaUI.Core.AutomationElements.AutomationElement Editor() =>
                root.FindFirstDescendant(condition =>
                    condition
                        .ByControlType(ControlType.Edit)
                        .And(condition.ByName("Multiline note"))
                ) ?? throw new InvalidOperationException("Multiline editor was not exposed.");
            root.SetForeground();
            Editor().FocusNative();
            WaitUntil(
                process,
                () => GetForegroundWindow() == window && Editor().Properties.HasKeyboardFocus.Value,
                "Multiline editor did not receive native focus."
            );
            Keyboard.Type("First");
            Keyboard.Press(VirtualKeyShort.RETURN);
            Keyboard.Release(VirtualKeyShort.RETURN);
            Keyboard.Type("Second");
            Wait.UntilInputIsProcessed();
            WaitUntil(
                process,
                () => Editor().Patterns.Value.Pattern.Value.Value == "First\nSecond",
                "Physical Enter and text input did not produce two lines."
            );
            TypeChord(VirtualKeyShort.KEY_A);
            WaitUntil(
                process,
                () =>
                    Editor().Patterns.Text.Pattern.GetSelection() is { Length: 1 } ranges
                    && ranges[0].GetText(-1) == "First\nSecond",
                "Select all did not reach the published text range."
            );
            var pattern = Editor().Patterns.Text.Pattern;
            Assert.AreEqual("First\nSecond", pattern.DocumentRange.GetText(-1));
            var selection = pattern.GetSelection();
            Assert.AreEqual(1, selection.Length);
            Assert.AreEqual("First\nSecond", selection[0].GetText(-1));
            Assert.IsTrue(
                selection[0].GetBoundingRectangles().Length >= 2,
                "A selected two-line draft did not expose cross-line geometry."
            );
            Keyboard.Press(VirtualKeyShort.BACK);
            Keyboard.Release(VirtualKeyShort.BACK);
            WaitUntil(
                process,
                () => Editor().Patterns.Value.Pattern.Value.Value == "",
                "Selection deletion did not update the document."
            );
            TypeChord(VirtualKeyShort.KEY_Z);
            WaitUntil(
                process,
                () => Editor().Patterns.Value.Pattern.Value.Value == "First\nSecond",
                "Undo did not restore the multiline draft."
            );
            TypeChord(VirtualKeyShort.KEY_Y);
            WaitUntil(
                process,
                () => Editor().Patterns.Value.Pattern.Value.Value == "",
                "Redo did not repeat the selection deletion."
            );
            Assert.IsTrue(process.CloseMainWindow());
            Assert.IsTrue(process.WaitForExit((int)Timeout.TotalMilliseconds));
            Assert.AreEqual(0, process.ExitCode);
        }
        finally
        {
            StopApplication(process);
        }
    }

    [TestMethod]
    public void FlaUiOpensLucentPopupOutsideOwnerInvokesAndRestoresFocus()
    {
        using var process = StartApplication();
        nint window = 0;
        try
        {
            window = WaitForWindow(process);
            using var automation = new UIA3Automation();
            var root = automation.FromHandle(window);
            var editor =
                root.FindFirstDescendant(condition =>
                    condition
                        .ByControlType(ControlType.Edit)
                        .And(condition.ByName("Multiline note"))
                ) ?? throw new InvalidOperationException("FlaUI could not find the popup target.");
            root.SetForeground();
            editor.FocusNative();
            Keyboard.Type("Popup text");
            Wait.UntilInputIsProcessed();
            Assert.IsTrue(SetWindowPos(window, 0, 0, 0, 380, 520, 0x0002 | 0x0004));
            WaitUntil(
                process,
                () => root.BoundingRectangle.Width <= 390,
                "Owner resize did not settle."
            );

            FlaUI.Core.AutomationElements.AutomationElement? FindMenu(out nint handle)
            {
                handle = 0;
                foreach (
                    var candidateHandle in ProcessWindowHandles(process.Id, window)
                        .Where(candidate => candidate != window && IsOwnedPopup(candidate, window))
                )
                {
                    var candidate = automation.FromHandle(candidateHandle);
                    var candidateMenu =
                        candidate.ControlType == ControlType.Menu
                            ? candidate
                            : candidate.FindFirstDescendant(condition =>
                                condition.ByControlType(ControlType.Menu)
                            );
                    if (candidateMenu is null)
                        continue;
                    handle = candidateHandle;
                    return candidateMenu;
                }
                return null;
            }
            var target = editor.BoundingRectangle;
            Mouse.RightClick(new Point(target.Right - 4, target.Top + target.Height / 2));
            nint popupWindow = 0;
            FlaUI.Core.AutomationElements.AutomationElement? menu = null;
            WaitUntil(
                process,
                () => (menu = FindMenu(out popupWindow)) is not null,
                "Context menu did not open."
            );
            Assert.IsTrue(
                menu!.BoundingRectangle.Right > root.BoundingRectangle.Right,
                "Lucent popup remained clipped to the owner window."
            );
            var selectAll =
                menu.FindFirstDescendant(condition =>
                    condition
                        .ByControlType(ControlType.MenuItem)
                        .And(condition.ByName("Select all"))
                )
                ?? throw new InvalidOperationException(
                    "Popup did not expose Select all as a menu item."
                );
            selectAll.Patterns.Invoke.Pattern.Invoke();
            WaitUntil(
                process,
                () => !IsWindow(popupWindow),
                "Invoked context menu did not dismiss."
            );
            WaitUntil(
                process,
                () =>
                    editor.Patterns.Text.Pattern.GetSelection() is { Length: 1 } ranges
                    && ranges[0].GetText(-1) == "Popup text",
                "Popup Select all did not execute against the owner editor."
            );

            void InvokeTextMenuItem(string name)
            {
                Mouse.RightClick(new Point(target.Right - 4, target.Top + target.Height / 2));
                nint handle = 0;
                FlaUI.Core.AutomationElements.AutomationElement? candidateMenu = null;
                WaitUntil(
                    process,
                    () => (candidateMenu = FindMenu(out handle)) is not null,
                    "Context menu did not open for " + name + "."
                );
                var item =
                    candidateMenu!.FindFirstDescendant(condition =>
                        condition.ByControlType(ControlType.MenuItem).And(condition.ByName(name))
                    ) ?? throw new InvalidOperationException("Popup did not expose " + name + ".");
                item.Patterns.Invoke.Pattern.Invoke();
                WaitUntil(
                    process,
                    () => !IsWindow(handle),
                    name + " did not dismiss the context menu."
                );
            }

            InvokeTextMenuItem("Copy");
            InvokeTextMenuItem("Cut");
            WaitUntil(
                process,
                () => editor.Patterns.Value.Pattern.Value.Value == "",
                "Popup Cut did not update the owner editor."
            );
            InvokeTextMenuItem("Paste");
            WaitUntil(
                process,
                () => editor.Patterns.Value.Pattern.Value.Value == "Popup text",
                "Popup Paste did not consume the owner clipboard request."
            );

            Mouse.RightClick(new Point(target.Right - 4, target.Top + target.Height / 2));
            nint escapePopup = 0;
            WaitUntil(
                process,
                () => FindMenu(out escapePopup) is not null,
                "Context menu did not reopen."
            );
            Keyboard.Press(VirtualKeyShort.ESCAPE);
            Keyboard.Release(VirtualKeyShort.ESCAPE);
            WaitUntil(
                process,
                () => !IsWindow(escapePopup),
                "Escape did not dismiss the context menu."
            );
            WaitUntil(
                process,
                () => editor.Properties.HasKeyboardFocus.Value,
                "Popup dismissal did not restore editor focus."
            );

            Mouse.RightClick(new Point(target.Right - 4, target.Top + target.Height / 2));
            WaitUntil(
                process,
                () => FindMenu(out _) is not null,
                "Context menu did not open for owner-close validation."
            );
            Assert.IsTrue(PostMessage(window, 0x0010, 0, 0));
            WaitUntil(
                process,
                () => process.HasExited,
                "Owner close did not complete while a popup remained open."
            );
            Assert.AreEqual(0, process.ExitCode);
        }
        finally
        {
            StopApplication(process, window);
        }
    }

    [TestMethod]
    public void FlaUiReprojectsResponsiveLayoutWhileNativeBorderIsHeld()
    {
        using var process = StartApplication("--live-layout-fixture");
        nint window = 0;
        try
        {
            window = WaitForWindow(process);
            using var automation = new UIA3Automation();
            var root = automation.FromHandle(window);
            root.SetForeground();
            bool HasWideCapture() =>
                root.FindAllDescendants(condition => condition.ByControlType(ControlType.Text))
                    .Any(element =>
                        element.Name.StartsWith("Capture column", StringComparison.Ordinal)
                    );
            Assert.IsTrue(
                HasWideCapture(),
                "Live layout fixture did not start in its wide branch."
            );
            Assert.IsTrue(GetWindowRect(window, out var bounds));
            var y = bounds.Top + (bounds.Bottom - bounds.Top) / 2;
            Mouse.MoveTo(new Point(bounds.Right - 2, y));
            Mouse.Down(MouseButton.Left);
            try
            {
                Mouse.MoveTo(new Point(bounds.Left + 620, y));
                WaitUntil(
                    process,
                    () =>
                        GetWindowRect(window, out var current)
                        && current.Right - current.Left < 700,
                    "Physical border drag did not enter compact native width."
                );
                Assert.IsFalse(
                    HasWideCapture(),
                    "Responsive branch did not reproject until after the held border was released."
                );
            }
            finally
            {
                Mouse.Up(MouseButton.Left);
            }
        }
        finally
        {
            StopApplication(process, window);
        }
    }

    [TestMethod]
    public void FlaUiPlacesAndDragsTextSelectionInSingleAndMultilineEditorsWithPhysicalPointer()
    {
        using var process = StartApplication();
        nint window = 0;
        try
        {
            window = WaitForWindow(process);
            using var automation = new UIA3Automation();
            var root = automation.FromHandle(window);
            root.SetForeground();
            foreach (var label in new[] { "Multiline note", "Single-line note" })
            {
                var isMultiline = label == "Multiline note";
                var editor =
                    root.FindFirstDescendant(condition =>
                        condition.ByControlType(ControlType.Edit).And(condition.ByName(label))
                    )
                    ?? throw new InvalidOperationException(
                        $"FlaUI could not find the {label} pointer editor."
                    );
                editor.FocusNative();
                Keyboard.Type("abcdef");
                WaitUntil(
                    process,
                    () => editor.Patterns.Value.Pattern.Value.Value == "abcdef",
                    $"{label} pointer fixture text did not settle."
                );
                var textBounds = isMultiline
                    ? editor.Patterns.Text.Pattern.DocumentRange.GetBoundingRectangles()[0]
                    : editor.BoundingRectangle;
                Mouse.LeftClick(
                    new Point(textBounds.Left + 1, textBounds.Top + textBounds.Height / 2)
                );
                WaitUntil(
                    process,
                    () => editor.Properties.HasKeyboardFocus.Value,
                    $"{label} lost native keyboard focus after the physical click."
                );
                Keyboard.Type("X");
                WaitUntil(
                    process,
                    () => editor.Patterns.Value.Pattern.Value.Value.StartsWith('X'),
                    $"{label} physical click did not place the caret near the start of the line."
                );

                textBounds = isMultiline
                    ? editor.Patterns.Text.Pattern.DocumentRange.GetBoundingRectangles()[0]
                    : editor.BoundingRectangle;
                var y = textBounds.Top + textBounds.Height / 2;
                Mouse.MoveTo(new Point(textBounds.Right - 1, y));
                Mouse.Down(MouseButton.Left);
                try
                {
                    Mouse.MoveTo(new Point(textBounds.Left + 1, y));
                }
                finally
                {
                    Mouse.Up(MouseButton.Left);
                }
                if (isMultiline)
                    WaitUntil(
                        process,
                        () =>
                            editor.Patterns.Text.Pattern.GetSelection() is { Length: 1 } ranges
                            && ranges[0].GetText(-1).Length > 0,
                        $"{label} physical pointer drag did not publish a text selection."
                    );
                else
                {
                    Keyboard.Type("Z");
                    WaitUntil(
                        process,
                        () => editor.Patterns.Value.Pattern.Value.Value == "Z",
                        "Single-line physical drag did not select the text for replacement."
                    );
                }
            }
        }
        finally
        {
            StopApplication(process, window);
        }
    }

    private static nint[] ProcessWindowHandles(int processId, nint owner)
    {
        var handles = new HashSet<nint>();
        AddChildren(0);
        AddChildren(owner);
        return handles.ToArray();

        void AddChildren(nint parent)
        {
            nint after = 0;
            while ((after = FindWindowEx(parent, after, null, null)) != 0)
            {
                _ = GetWindowThreadProcessId(after, out var candidateProcessId);
                if (candidateProcessId == processId)
                    handles.Add(after);
            }
        }
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags
    );

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out NativeRect rectangle);

    [LibraryImport(
        "user32.dll",
        EntryPoint = "FindWindowExW",
        StringMarshalling = StringMarshalling.Utf16
    )]
    private static partial nint FindWindowEx(
        nint parent,
        nint childAfter,
        string? className,
        string? windowName
    );

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out int processId);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint window, uint command);

    [LibraryImport("user32.dll")]
    private static partial nint GetParent(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint window);

    private static void TypeChord(VirtualKeyShort key)
    {
        Keyboard.Press(VirtualKeyShort.CONTROL);
        try
        {
            Keyboard.Press(key);
            Keyboard.Release(key);
        }
        finally
        {
            Keyboard.Release(VirtualKeyShort.CONTROL);
        }
        Wait.UntilInputIsProcessed();
    }

    private static Process StartApplication(string fixture = "--input-fixture")
    {
        var path = Environment.GetEnvironmentVariable("LUCENT_DESKTOP_HOST");
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(
                "LUCENT_DESKTOP_HOST must name the published Windows TestHost executable."
            );
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(
                "The published Windows TestHost executable does not exist.",
                fullPath
            );
        var start = new ProcessStartInfo(fullPath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(fullPath)!,
        };
        start.ArgumentList.Add(fixture);
        return Process.Start(start)
            ?? throw new InvalidOperationException("Could not launch the input fixture.");
    }

    private static nint WaitForWindow(Process process)
    {
        nint handle = 0;
        WaitUntil(
            process,
            () =>
            {
                process.Refresh();
                handle = process.MainWindowHandle;
                return handle != 0;
            },
            "The input fixture did not expose a window."
        );
        return handle;
    }

    private static void WaitUntil(Process process, Func<bool> condition, string message)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (process.HasExited)
                throw new InvalidOperationException(
                    $"The input fixture exited {process.ExitCode}: {message}"
                );
            if (elapsed.Elapsed >= Timeout)
                throw new TimeoutException(message);
            Thread.Sleep(25);
        }
    }

    private static void StopApplication(Process process, nint ownerWindow = 0)
    {
        if (process.HasExited)
            return;
        if (ownerWindow != 0)
        {
            foreach (
                var popup in ProcessWindowHandles(process.Id, ownerWindow)
                    .Where(handle => handle != ownerWindow && IsOwnedPopup(handle, ownerWindow))
            )
                _ = PostMessage(popup, 0x0010, 0, 0);
            _ = PostMessage(ownerWindow, 0x0010, 0, 0);
        }
        else
            process.CloseMainWindow();
        if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
    }

    private static bool IsOwnedPopup(nint candidate, nint owner) =>
        IsWindowVisible(candidate)
        && (GetWindow(candidate, 4) == owner || GetParent(candidate) == owner);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
