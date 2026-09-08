using System.Diagnostics;
using System.Drawing;
using System.IO;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using UiElement = FlaUI.Core.AutomationElements.AutomationElement;

namespace Lucent.Desktop.Tests;

public sealed partial class PublishedIssueBrowserTests
{
    [TestMethod]
    public void FlaUiSplitPaneResizesAndRestoresAcrossNarrowNavigation()
    {
        using var process = StartApplication();
        try
        {
            var owner = WaitForWindow(process);
            using var automation = new UIA3Automation();
            var root = automation.FromHandle(owner);
            ActivateOwnedWindow(process, root, owner);
            Assert.IsTrue(SetWindowPos(owner, 0, 0, 0, 1120, 760, SwpNoMove));
            UiElement? splitter = null;
            WaitUntil(
                process,
                () =>
                    (splitter = root.FindFirstDescendant(c => c.ByName("Resize issue list")))
                        is not null,
                "The wide workspace did not expose its accessible splitter."
            );
            var range = splitter!.Patterns.RangeValue.Pattern;
            var initial = range.Value.Value;
            range.SetValue(initial + 40);
            WaitUntil(
                process,
                () => Math.Abs(range.Value.Value - initial - 40) < 1,
                "UIA RangeValue did not resize the list."
            );

            splitter.Focus();
            WaitUntil(
                process,
                () => splitter.Properties.HasKeyboardFocus.Value,
                "The splitter did not gain accessible focus."
            );
            Wait.UntilInputIsProcessed();
            CaptureWorkspace(root, "stock-before-keyboard");
            Assert.AreEqual(
                owner,
                GetForegroundWindow(),
                "The test owner lost foreground before the splitter key."
            );
            TypeNavigation(VirtualKeyShort.RIGHT);
            Wait.UntilInputIsProcessed();
            WaitUntil(
                process,
                () => Math.Abs(range.Value.Value - initial - 48) < 1,
                "The splitter did not accept keyboard resizing."
            );
            var start = Center(splitter.BoundingRectangle);
            Mouse.MoveTo(start);
            Mouse.Down(MouseButton.Left);
            try
            {
                Mouse.MoveTo(new Point(start.X + 30, start.Y));
                Wait.UntilInputIsProcessed();
            }
            finally
            {
                Mouse.Up(MouseButton.Left);
            }
            WaitUntil(
                process,
                () => range.Value.Value > initial + 60,
                "Captured pointer dragging did not resize the list."
            );
            var preferred = range.Value.Value;
            CaptureWorkspace(root, "stock-wide");

            Assert.IsTrue(SetWindowPos(owner, 0, 0, 0, 600, 760, SwpNoMove));
            WaitUntil(
                process,
                () => root.FindFirstDescendant(c => c.ByName("Resize issue list")) is null,
                "The narrow workspace retained the wide splitter."
            );
            var list = root.FindFirstDescendant(c =>
                c.ByControlType(ControlType.List).And(c.ByName("Issues"))
            )!;
            UiElement? row = null;
            WaitUntil(
                process,
                () => (row = FindIssueRow(list, 10000)) is not null,
                "The narrow list lost its items."
            );
            Wait.UntilInputIsProcessed();
            CaptureWorkspace(root, "stock-narrow-before-click");
            Console.WriteLine(
                $"Narrow row={row!.BoundingRectangle}; foreground={GetForegroundWindow()}; owner={owner}; hit={WindowFromPoint(Center(row.BoundingRectangle))}"
            );
            Mouse.LeftClick(Center(row!.BoundingRectangle));
            Wait.UntilInputIsProcessed();
            CaptureWorkspace(root, "stock-narrow-after-click");
            UiElement? back = null;
            WaitUntil(
                process,
                () =>
                    (back = root.FindFirstDescendant(c => c.ByName("Back to issues"))) is not null,
                "Selecting a narrow issue did not open detail navigation."
            );
            CaptureWorkspace(root, "stock-narrow-detail");
            back!.Patterns.Invoke.Pattern.Invoke();
            WaitUntil(
                process,
                () =>
                    root.FindFirstDescendant(c =>
                        c.ByControlType(ControlType.List).And(c.ByName("Issues"))
                    )
                        is not null,
                "Back did not restore the issue list."
            );
            CaptureWorkspace(root, "stock-narrow-list");
            Assert.IsTrue(SetWindowPos(owner, 0, 0, 0, 1120, 760, SwpNoMove));
            WaitUntil(
                process,
                () =>
                    (splitter = root.FindFirstDescendant(c => c.ByName("Resize issue list")))
                        is not null,
                "Restoring the desktop width did not restore the splitter."
            );
            Assert.AreEqual(
                preferred,
                splitter!.Patterns.RangeValue.Pattern.Value.Value,
                1,
                "The narrow layout erased the preferred pane size."
            );
        }
        finally
        {
            StopApplication(process);
        }
    }

    [TestMethod]
    [DataRow(false, "keyboard")]
    [DataRow(false, "accessibility")]
    [DataRow(true, "keyboard")]
    public void FlaUiSubmenuInvokesLeafAndDismissesOneLevel(bool native, string invocation)
    {
        using var process = StartApplication(native ? "--native-menus" : null);
        try
        {
            var owner = WaitForWindow(process);
            using var automation = new UIA3Automation();
            var root = automation.FromHandle(owner);
            ActivateOwnedWindow(process, root, owner);
            var list = root.FindFirstDescendant(c =>
                c.ByControlType(ControlType.List).And(c.ByName("Issues"))
            )!;
            WaitUntil(
                process,
                () => FindIssueRow(list, 9998) is not null,
                "The submenu fixture rows did not appear."
            );
            FindIssueRow(list, 10000)!.Patterns.SelectionItem.Pattern.Select();
            var target = Center(FindIssueRow(list, 9998)!.BoundingRectangle);
            Mouse.RightClick(target);
            UiElement? trigger = null;
            WaitUntil(
                process,
                () =>
                    (trigger = FindPopupItem(automation, process, owner, "Set status")) is not null,
                "The root menu omitted the submenu trigger."
            );
            if (invocation == "accessibility")
                trigger!.Patterns.ExpandCollapse.Pattern.Expand();
            else
            {
                // USER32 starts with no selection; Lucent focuses the first enabled item.
                if (native)
                    TypeNavigation(VirtualKeyShort.DOWN);
                TypeNavigation(VirtualKeyShort.DOWN, VirtualKeyShort.DOWN, VirtualKeyShort.RIGHT);
            }
            UiElement? leaf = null;
            WaitUntil(
                process,
                () => (leaf = FindPopupItem(automation, process, owner, "Mark closed")) is not null,
                "The submenu did not expose its leaf command."
            );
            CaptureWorkspace(root, native ? "native-submenu-owner" : "lucent-submenu-owner");
            CapturePopupWindows(
                automation,
                process,
                owner,
                native ? "native-submenu" : "lucent-submenu"
            );
            Keyboard.Type(VirtualKeyShort.ESCAPE);
            WaitUntil(
                process,
                () => FindPopupItem(automation, process, owner, "Mark closed") is null,
                "Escape did not dismiss the child menu."
            );
            WaitUntil(
                process,
                () =>
                    (trigger = FindPopupItem(automation, process, owner, "Set status")) is not null,
                "Escape dismissed the whole chain instead of one child level."
            );
            if (invocation == "accessibility")
                trigger!.Patterns.ExpandCollapse.Pattern.Expand();
            else
                TypeNavigation(VirtualKeyShort.RIGHT);
            WaitUntil(
                process,
                () => (leaf = FindPopupItem(automation, process, owner, "Mark closed")) is not null,
                "The submenu did not reopen after Escape."
            );
            if (invocation == "accessibility")
                leaf!.Patterns.Invoke.Pattern.Invoke();
            else
            {
                // USER32 initially highlights the first entry even when disabled.
                if (native)
                    TypeNavigation(VirtualKeyShort.DOWN);
                Keyboard.Type(VirtualKeyShort.RETURN);
            }
            WaitUntil(
                process,
                () => FindPopupItem(automation, process, owner, "Set status") is null,
                "Invoking a nested leaf did not dismiss the root chain."
            );
            WaitUntil(
                process,
                () =>
                    FindIssueRow(list, 9998)?.Name.Contains("— closed ·", StringComparison.Ordinal)
                    == true,
                "The nested command did not update its target issue."
            );
            Assert.IsTrue(
                FindIssueRow(list, 10000)!.Patterns.SelectionItem.Pattern.IsSelected.Value,
                "Invoking a context menu changed the selected issue."
            );
        }
        finally
        {
            StopApplication(process);
        }
    }

    private static UiElement? FindPopupItem(
        UIA3Automation automation,
        Process process,
        nint owner,
        string name
    )
    {
        foreach (var handle in ProcessTopLevelWindows(process.Id))
        {
            if (handle == owner || !IsWindowVisible(handle))
                continue;
            try
            {
                var item = automation
                    .FromHandle(handle)
                    .FindFirstDescendant(c =>
                        c.ByControlType(ControlType.MenuItem).And(c.ByName(name))
                    );
                if (item is not null)
                    return item;
            }
            catch (TimeoutException) when (!IsWindowVisible(handle))
            {
                // USER32 can destroy a native menu between enumeration and ElementFromHandle.
                // Only an actually disappeared window counts as dismissed; a live UIA stall fails.
            }
        }
        return null;
    }

    private static void TypeNavigation(params VirtualKeyShort[] keys)
    {
        // SDL uses the physical scan code to distinguish the navigation cluster
        // from keypad keys. Supply that code and its extended-key bit explicitly.
        foreach (var key in keys)
            Keyboard.TypeScanCode((ushort)(MapVirtualKey((uint)key, 0) & 0xff), true);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    private static void ActivateOwnedWindow(Process process, UiElement root, nint owner)
    {
        root.SetForeground();
        if (GetForegroundWindow() != owner)
        {
            // Foreground activation may be denied while another application owns input.
            // Raise only this test-owned HWND, verify its title bar, then remove the temporary
            // topmost flag as soon as ordinary pointer activation has succeeded (or failed).
            Assert.IsTrue(
                SetWindowPos(
                    owner,
                    new IntPtr(-1),
                    0,
                    0,
                    0,
                    0,
                    SwpNoSize | SwpNoMove | SwpNoActivate
                )
            );
            try
            {
                var title = root.FindFirstChild(c =>
                    c.ByControlType(ControlType.TitleBar)
                )!.BoundingRectangle;
                var point = new Point(
                    title.Left + Math.Min(100, title.Width / 2),
                    title.Top + title.Height / 2
                );
                Assert.AreEqual(owner, WindowFromPoint(point), "The test owner is occluded.");
                Mouse.LeftClick(point);
                WaitUntil(
                    process,
                    () => GetForegroundWindow() == owner,
                    "The test owner did not gain focus."
                );
            }
            finally
            {
                Assert.IsTrue(
                    SetWindowPos(
                        owner,
                        new IntPtr(-2),
                        0,
                        0,
                        0,
                        0,
                        SwpNoSize | SwpNoMove | SwpNoActivate
                    )
                );
            }
        }
        WaitUntil(
            process,
            () => GetForegroundWindow() == owner,
            "The test owner did not gain focus."
        );
    }

    private static void CaptureWorkspace(UiElement element, string name)
    {
        var output = Environment.GetEnvironmentVariable("LUCENT_DESKTOP_CAPTURES");
        if (string.IsNullOrWhiteSpace(output))
            return;
        Directory.CreateDirectory(output);
        Capture.Element(element).ToFile(Path.Combine(output, name + ".png"));
    }

    private static void CapturePopupWindows(
        UIA3Automation automation,
        Process process,
        nint owner,
        string name
    )
    {
        var index = 0;
        foreach (var handle in ProcessTopLevelWindows(process.Id))
            if (handle != owner && IsWindowVisible(handle))
                CaptureWorkspace(automation.FromHandle(handle), name + "-" + index++);
    }
}
