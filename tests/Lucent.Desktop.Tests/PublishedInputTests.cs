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

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

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

    private static Process StartApplication()
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
        start.ArgumentList.Add("--input-fixture");
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

    private static void StopApplication(Process process)
    {
        if (process.HasExited)
            return;
        process.CloseMainWindow();
        if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
    }
}
