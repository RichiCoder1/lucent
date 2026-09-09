using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace Lucent.Desktop.Tests;

public sealed partial class PublishedIssueBrowserTests
{
    [TestMethod]
    [TestCategory("PhysicalMixedDpi")]
    public void FlaUiMovesOpenPopupChainAcrossPhysical100And150PercentMonitors()
    {
        if (
            !string.Equals(
                Environment.GetEnvironmentVariable("LUCENT_PHYSICAL_MIXED_DPI"),
                "1",
                StringComparison.Ordinal
            )
        )
            Assert.Inconclusive(
                "Set LUCENT_PHYSICAL_MIXED_DPI=1 to opt into the focus-taking physical mixed-DPI check."
            );

        var priorContext = SetThreadDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
        if (priorContext == 0)
            throw new InvalidOperationException(
                "Could not establish a per-monitor-v2 test driver context."
            );
        try
        {
            RunPhysicalMixedDpiCheck();
        }
        finally
        {
            _ = SetThreadDpiAwarenessContext(priorContext);
        }
    }

    [TestMethod]
    [TestCategory("PhysicalMixedDpi")]
    public void FlaUiPublishesTextScreenRectanglesAcrossPhysical100And150PercentMonitors()
    {
        if (
            !string.Equals(
                Environment.GetEnvironmentVariable("LUCENT_PHYSICAL_MIXED_DPI"),
                "1",
                StringComparison.Ordinal
            )
        )
            Assert.Inconclusive(
                "Set LUCENT_PHYSICAL_MIXED_DPI=1 to opt into the focus-taking physical mixed-DPI check."
            );

        var priorContext = SetThreadDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
        if (priorContext == 0)
            throw new InvalidOperationException(
                "Could not establish a per-monitor-v2 test driver context."
            );
        try
        {
            using var process = StartTextHost();
            try
            {
                var owner = WaitForWindow(process);
                using var automation = new UIA3Automation();
                var root = automation.FromHandle(owner);
                ActivateOwnedWindow(process, root, owner);
                var monitors = MeasureMonitorDpi(process, root, owner, EnumeratePhysicalMonitors());
                var scale100 = monitors.FirstOrDefault(m => Math.Abs(m.DpiX - 96) <= 1);
                var scale150 = monitors.FirstOrDefault(m => Math.Abs(m.DpiX - 144) <= 1);
                if (
                    scale100.Handle == 0
                    || scale150.Handle == 0
                    || scale100.Handle == scale150.Handle
                )
                    Assert.Inconclusive(
                        "The physical check requires distinct attached 100% and 150% monitors."
                    );

                CheckTextGeometry(process, root, owner, scale100, "text-100-start");
                CheckTextGeometry(process, root, owner, scale150, "text-150");
                CheckTextGeometry(process, root, owner, scale100, "text-100-return");
            }
            finally
            {
                StopApplication(process);
            }
        }
        finally
        {
            _ = SetThreadDpiAwarenessContext(priorContext);
        }
    }

    private static void RunPhysicalMixedDpiCheck()
    {
        using var process = StartApplication();
        try
        {
            var owner = WaitForWindow(process);
            using var automation = new UIA3Automation();
            var root = automation.FromHandle(owner);
            ActivateOwnedWindow(process, root, owner);
            Assert.IsTrue(
                AreDpiAwarenessContextsEqual(
                    GetWindowDpiAwarenessContext(owner),
                    DpiAwarenessContextPerMonitorAwareV2
                ),
                "The published owner is not running with per-monitor-v2 DPI awareness."
            );
            var monitors = MeasureMonitorDpi(process, root, owner, EnumeratePhysicalMonitors());
            foreach (var monitor in monitors)
                Evidence(
                    $"Monitor {monitor.Device}: dpi={monitor.DpiX}x{monitor.DpiY}, scale={monitor.DpiX / 96d:P0}, bounds={monitor.Bounds}, work={monitor.WorkArea}, primary={monitor.Primary}"
                );
            var scale100 = monitors.FirstOrDefault(m => Math.Abs(m.DpiX - 96) <= 1);
            var scale150 = monitors.FirstOrDefault(m => Math.Abs(m.DpiX - 144) <= 1);
            if (scale100.Handle == 0 || scale150.Handle == 0 || scale100.Handle == scale150.Handle)
                Assert.Inconclusive(
                    "The physical check requires distinct attached 100% (96 DPI) and 150% (144 DPI) monitors."
                );

            MoveAndCheckEditor(process, root, owner, scale100, "100-start");
            OpenPopupChain(process, automation, root, owner, scale100);
            MoveOpenChain(process, automation, root, owner, scale150, "100-to-150");
            DismissPopupChain(process, automation, root, owner);

            MoveAndCheckEditor(process, root, owner, scale150, "150");
            OpenPopupChain(process, automation, root, owner, scale150);
            MoveOpenChain(process, automation, root, owner, scale100, "150-to-100");
            DismissPopupChain(process, automation, root, owner);

            MoveAndCheckEditor(process, root, owner, scale100, "100-return");
        }
        finally
        {
            StopApplication(process);
        }
    }

    private static void MoveAndCheckEditor(
        System.Diagnostics.Process process,
        AutomationElement root,
        nint owner,
        PhysicalMonitor monitor,
        string phase
    )
    {
        MoveOwner(process, root, owner, monitor, phase);
        var editor =
            root.FindFirstDescendant(c =>
                c.ByControlType(ControlType.Edit).And(c.ByName("Search issues"))
            ) ?? throw new InvalidOperationException("Issue Browser omitted its Search field.");
        editor.FocusNative();
        WaitUntil(
            process,
            () => editor.Properties.HasKeyboardFocus.Value,
            $"Search did not gain focus at {phase}."
        );
        using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
            Keyboard.Type(VirtualKeyShort.KEY_A);
        Keyboard.Type("abcdef");
        WaitUntil(
            process,
            () => editor.Patterns.Value.Pattern.Value.Value == "abcdef",
            $"Physical text did not reach Search at {phase}."
        );

        var editBounds = editor.BoundingRectangle;
        Mouse.LeftClick(new Point(editBounds.Left + 4, editBounds.Top + editBounds.Height / 2));
        Keyboard.Type("X");
        WaitUntil(
            process,
            () => editor.Patterns.Value.Pattern.Value.Value.StartsWith('X'),
            $"Physical pointer hit testing did not place the Search caret at {phase}."
        );
        editBounds = editor.BoundingRectangle;
        var y = editBounds.Top + editBounds.Height / 2;
        Mouse.MoveTo(new Point(editBounds.Right - 6, y));
        Mouse.Down(MouseButton.Left);
        try
        {
            Mouse.MoveTo(new Point(editBounds.Left + 4, y));
        }
        finally
        {
            Mouse.Up(MouseButton.Left);
        }
        Keyboard.Type("Z");
        WaitUntil(
            process,
            () => editor.Patterns.Value.Pattern.Value.Value == "Z",
            $"Physical pointer drag did not select Search text for replacement at {phase}."
        );
        Evidence(
            $"{phase}: ownerDpi={GetDpiForWindow(owner)}, owner={root.BoundingRectangle}, editor={editor.BoundingRectangle}, nativeCaretPlacementAndPointerDrag=pass"
        );
        editor.Patterns.Value.Pattern.SetValue("");
        WaitUntil(
            process,
            () => editor.Patterns.Value.Pattern.Value.Value.Length == 0,
            $"Search did not clear after editor validation at {phase}."
        );
    }

    private static void OpenPopupChain(
        System.Diagnostics.Process process,
        UIA3Automation automation,
        AutomationElement root,
        nint owner,
        PhysicalMonitor monitor
    )
    {
        var list =
            root.FindFirstDescendant(c => c.ByControlType(ControlType.List).And(c.ByName("Issues")))
            ?? throw new InvalidOperationException("Issue Browser omitted its issue list.");
        WaitUntil(
            process,
            () => FindIssueRow(list, 9998) is not null,
            "Issue 9998 did not appear."
        );
        var row = FindIssueRow(list, 9998)!;
        Mouse.RightClick(Center(row.BoundingRectangle));
        AutomationElement? trigger = null;
        WaitUntil(
            process,
            () => (trigger = FindPopupItem(automation, process, owner, "Set status")) is not null,
            "The root popup omitted Set status."
        );
        trigger!.Patterns.ExpandCollapse.Pattern.Expand();
        WaitUntil(
            process,
            () => FindPopupItem(automation, process, owner, "Mark closed") is not null,
            "The submenu omitted Mark closed."
        );
        AssertPopupGeometry(process, automation, owner, monitor, row.BoundingRectangle);
    }

    private static void MoveOpenChain(
        System.Diagnostics.Process process,
        UIA3Automation automation,
        AutomationElement root,
        nint owner,
        PhysicalMonitor monitor,
        string phase
    )
    {
        MoveOwner(process, root, owner, monitor, phase);
        WaitUntil(
            process,
            () =>
            {
                try
                {
                    AssertPopupGeometry(process, automation, owner, monitor, null);
                    return true;
                }
                catch (AssertFailedException)
                {
                    return false;
                }
            },
            $"The open popup chain did not re-anchor inside {monitor.Device} at {phase}."
        );
        Assert.AreEqual(owner, GetForegroundWindow(), $"Owner lost foreground focus at {phase}.");
        Evidence($"{phase}: open popup chain re-anchored in {monitor.WorkArea}; focus=retained");
    }

    private static void DismissPopupChain(
        System.Diagnostics.Process process,
        UIA3Automation automation,
        AutomationElement root,
        nint owner
    )
    {
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        WaitUntil(
            process,
            () => FindPopupItem(automation, process, owner, "Mark closed") is null,
            "Escape did not dismiss the child popup."
        );
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        WaitUntil(
            process,
            () => FindPopupItem(automation, process, owner, "Set status") is null,
            "Escape did not dismiss the root popup."
        );
        Assert.AreEqual(
            owner,
            GetForegroundWindow(),
            "Popup dismissal did not retain owner focus."
        );
    }

    private static void AssertPopupGeometry(
        System.Diagnostics.Process process,
        UIA3Automation automation,
        nint owner,
        PhysicalMonitor monitor,
        Rectangle? rowBounds
    )
    {
        var rootPopup = FindPopupContaining(automation, process, owner, "Set status");
        var childPopup = FindPopupContaining(automation, process, owner, "Mark closed");
        Assert.IsNotNull(rootPopup, "The open root popup was not discoverable.");
        Assert.IsNotNull(childPopup, "The open child popup was not discoverable.");
        var rootBounds = rootPopup.Value.Element.BoundingRectangle;
        var childBounds = childPopup.Value.Element.BoundingRectangle;
        var trigger = rootPopup.Value.Element.FindFirstDescendant(c => c.ByName("Set status"))!;
        AssertInside(rootBounds, monitor.WorkArea, "Root popup escaped the monitor work area.");
        AssertInside(childBounds, monitor.WorkArea, "Child popup escaped the monitor work area.");
        Assert.IsTrue(
            Math.Abs(childBounds.Left - trigger.BoundingRectangle.Right) <= 8
                || Math.Abs(childBounds.Right - trigger.BoundingRectangle.Left) <= 8,
            "Submenu was not horizontally anchored to its trigger."
        );
        Assert.IsTrue(
            childBounds.Bottom > trigger.BoundingRectangle.Top
                && childBounds.Top < trigger.BoundingRectangle.Bottom,
            "Submenu did not vertically overlap its trigger row."
        );
        if (rowBounds is { } row)
        {
            var anchor = Center(row);
            Assert.IsTrue(
                DistanceTo(rootBounds, anchor) <= 32,
                "Root popup was not anchored near the invoked issue row."
            );
        }
        Evidence(
            $"Popup monitor={monitor.Device}, dpi={monitor.DpiX}, root={rootBounds}, trigger={trigger.BoundingRectangle}, child={childBounds}, work={monitor.WorkArea}"
        );
    }

    private static void CheckTextGeometry(
        System.Diagnostics.Process process,
        AutomationElement root,
        nint owner,
        PhysicalMonitor monitor,
        string phase
    )
    {
        MoveOwner(process, root, owner, monitor, phase);
        var editor =
            root.FindFirstDescendant(c =>
                c.ByControlType(ControlType.Edit).And(c.ByName("Multiline note"))
            ) ?? throw new InvalidOperationException("Input fixture omitted its multiline editor.");
        editor.FocusNative();
        WaitUntil(
            process,
            () => editor.Properties.HasKeyboardFocus.Value,
            $"Multiline editor did not gain native focus at {phase}."
        );
        using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
            Keyboard.Type(VirtualKeyShort.KEY_A);
        Keyboard.Type("abcdef");
        WaitUntil(
            process,
            () => editor.Patterns.Value.Pattern.Value.Value == "abcdef",
            $"Multiline fixture text did not settle at {phase}."
        );
        TypeNavigation(VirtualKeyShort.HOME);
        Keyboard.Press(VirtualKeyShort.SHIFT);
        try
        {
            TypeNavigation(VirtualKeyShort.RIGHT);
        }
        finally
        {
            Keyboard.Release(VirtualKeyShort.SHIFT);
        }
        WaitUntil(
            process,
            () =>
                editor.Patterns.Text.Pattern.GetSelection() is { Length: 1 } ranges
                && ranges[0].GetText(-1) == "a",
            $"Physical caret selection did not settle at {phase}."
        );
        var selection = editor.Patterns.Text.Pattern.GetSelection();
        var rectangles = selection[0].GetBoundingRectangles();
        Assert.IsGreaterThan(
            0,
            rectangles.Length,
            $"UIA exposed no text screen rectangle at {phase}."
        );
        foreach (var rectangle in rectangles)
            AssertInside(
                rectangle,
                editor.BoundingRectangle,
                $"UIA text screen rectangle escaped the visible editor at {phase}."
            );
        Evidence(
            $"{phase}: ownerDpi={GetDpiForWindow(owner)}, owner={root.BoundingRectangle}, editor={editor.BoundingRectangle}, selectedText=a, textScreenRect={rectangles[0]}, nativeCaretFocused={editor.Properties.HasKeyboardFocus.Value}"
        );
    }

    private static System.Diagnostics.Process StartTextHost()
    {
        var path = Environment.GetEnvironmentVariable("LUCENT_DESKTOP_HOST");
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(
                "LUCENT_DESKTOP_HOST must name the published Windows TestHost executable."
            );
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(
                "The published Windows TestHost does not exist.",
                fullPath
            );
        var start = new System.Diagnostics.ProcessStartInfo(fullPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(fullPath)!,
        };
        start.ArgumentList.Add("--input-fixture");
        return System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException(
                "Could not launch the published Windows TestHost."
            );
    }

    private static void Evidence(string message)
    {
        Console.WriteLine(message);
        var path = Environment.GetEnvironmentVariable("LUCENT_MIXED_DPI_LOG");
        if (string.IsNullOrWhiteSpace(path))
            return;
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.AppendAllText(fullPath, message + Environment.NewLine);
    }

    private static (nint Handle, AutomationElement Element)? FindPopupContaining(
        UIA3Automation automation,
        System.Diagnostics.Process process,
        nint owner,
        string itemName
    )
    {
        foreach (var handle in ProcessTopLevelWindows(process.Id))
        {
            if (handle == owner || !IsWindowVisible(handle))
                continue;
            try
            {
                var element = automation.FromHandle(handle);
                if (element.FindFirstDescendant(c => c.ByName(itemName)) is not null)
                    return (handle, element);
            }
            catch (TimeoutException) when (!IsWindowVisible(handle)) { }
        }
        return null;
    }

    private static void MoveOwner(
        System.Diagnostics.Process process,
        AutomationElement root,
        nint owner,
        PhysicalMonitor monitor,
        string phase
    )
    {
        var width = Math.Min(1120, monitor.WorkArea.Width - 32);
        var height = Math.Min(760, monitor.WorkArea.Height - 32);
        var x = monitor.WorkArea.Left + (monitor.WorkArea.Width - width) / 2;
        var y = monitor.WorkArea.Top + (monitor.WorkArea.Height - height) / 2;
        Assert.IsTrue(SetWindowPos(owner, 0, x, y, width, height, 0x0004));
        WaitUntil(
            process,
            () =>
                GetDpiForWindow(owner) == monitor.DpiX
                && monitor.Bounds.Contains(Center(root.BoundingRectangle)),
            $"Owner did not settle on {monitor.Device} at {monitor.DpiX} DPI during {phase}."
        );
        Assert.AreEqual(
            owner,
            GetForegroundWindow(),
            $"Owner lost foreground focus during {phase}."
        );
    }

    private static PhysicalMonitor[] EnumeratePhysicalMonitors()
    {
        var monitors = new List<PhysicalMonitor>();
        Assert.IsTrue(
            EnumDisplayMonitors(
                0,
                0,
                (handle, _, _, _) =>
                {
                    var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
                    if (!GetMonitorInfo(handle, ref info))
                        throw new InvalidOperationException("GetMonitorInfoW failed.");
                    monitors.Add(
                        new(
                            handle,
                            info.Device.TrimEnd('\0'),
                            Rectangle.FromLTRB(
                                info.Monitor.Left,
                                info.Monitor.Top,
                                info.Monitor.Right,
                                info.Monitor.Bottom
                            ),
                            Rectangle.FromLTRB(
                                info.Work.Left,
                                info.Work.Top,
                                info.Work.Right,
                                info.Work.Bottom
                            ),
                            0,
                            0,
                            (info.Flags & 1) != 0
                        )
                    );
                    return true;
                },
                0
            ),
            "EnumDisplayMonitors failed."
        );
        return monitors.ToArray();
    }

    private static PhysicalMonitor[] MeasureMonitorDpi(
        System.Diagnostics.Process process,
        AutomationElement root,
        nint owner,
        PhysicalMonitor[] monitors
    )
    {
        for (var index = 0; index < monitors.Length; index++)
        {
            var monitor = monitors[index];
            var width = Math.Min(800, monitor.WorkArea.Width - 32);
            var height = Math.Min(600, monitor.WorkArea.Height - 32);
            var x = monitor.WorkArea.Left + (monitor.WorkArea.Width - width) / 2;
            var y = monitor.WorkArea.Top + (monitor.WorkArea.Height - height) / 2;
            Assert.IsTrue(SetWindowPos(owner, 0, x, y, width, height, 0x0004));
            WaitUntil(
                process,
                () => monitor.Bounds.Contains(Center(root.BoundingRectangle)),
                $"Owner did not settle on {monitor.Device} for DPI measurement."
            );
            var dpi = GetDpiForWindow(owner);
            Assert.IsGreaterThan(0u, dpi, $"GetDpiForWindow failed on {monitor.Device}.");
            monitors[index] = monitor with { DpiX = dpi, DpiY = dpi };
        }
        return monitors;
    }

    private static void AssertInside(Rectangle inner, Rectangle outer, string message) =>
        Assert.IsTrue(
            inner.Left >= outer.Left - 1
                && inner.Top >= outer.Top - 1
                && inner.Right <= outer.Right + 1
                && inner.Bottom <= outer.Bottom + 1,
            $"{message} Inner={inner}; outer={outer}"
        );

    private static int DistanceTo(Rectangle rectangle, Point point)
    {
        var dx = Math.Max(rectangle.Left - point.X, Math.Max(0, point.X - rectangle.Right));
        var dy = Math.Max(rectangle.Top - point.Y, Math.Max(0, point.Y - rectangle.Bottom));
        return Math.Max(dx, dy);
    }

    private readonly record struct PhysicalMonitor(
        nint Handle,
        string Device,
        Rectangle Bounds,
        Rectangle WorkArea,
        uint DpiX,
        uint DpiY,
        bool Primary
    );

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeMonitorInfo
    {
        internal int Size;
        internal NativeRectangle Monitor;
        internal NativeRectangle Work;
        internal uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string Device;
    }

    private delegate bool MonitorCallback(nint monitor, nint dc, nint rectangle, nint data);

    private static readonly nint DpiAwarenessContextPerMonitorAwareV2 = (nint)(-4);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        nint dc,
        nint clip,
        MonitorCallback callback,
        nint data
    );

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref NativeMonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetWindowDpiAwarenessContext(nint window);

    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint context);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);
}
