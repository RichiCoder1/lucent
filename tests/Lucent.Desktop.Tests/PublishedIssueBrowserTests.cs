using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Axe.Windows.Automation;
using Axe.Windows.Automation.Data;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace Lucent.Desktop.Tests;

[TestClass]
public sealed partial class PublishedIssueBrowserTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [TestMethod]
    public void AxeScanFindsACompleteTreeAndNoRuleErrors()
    {
        using var process = StartApplication();
        try
        {
            var window = WaitForWindow(process);
            var root = AutomationElement.FromHandle(window);
            var nodes = root.FindAll(
                System.Windows.Automation.TreeScope.Descendants,
                Automation.ControlViewCondition
            );
            Assert.IsGreaterThanOrEqualTo(
                3,
                nodes.Count,
                "The published app UIA tree was empty or incomplete."
            );

            var outputRoot = Environment.GetEnvironmentVariable("LUCENT_ACCESSIBILITY_OUTPUT");
            if (string.IsNullOrWhiteSpace(outputRoot))
                throw new InvalidOperationException(
                    "LUCENT_ACCESSIBILITY_OUTPUT must select the Axe evidence directory."
                );
            var output = Path.Combine(
                Path.GetFullPath(outputRoot),
                "axe-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(output);
            var config = Config
                .Builder.ForProcessId(process.Id)
                .WithOutputFileFormat(OutputFileFormat.A11yTest)
                .WithOutputDirectory(output)
                .WithAlwaysSaveTestFile()
                .Build();
            var scan = ScannerFactory
                .CreateScanner(config)
                .Scan(new ScanOptions("issue-browser", window));

            Assert.IsTrue(scan.WindowScanOutputs.Count > 0, "Axe produced no window scan results.");
            Assert.AreEqual(
                0,
                scan.WindowScanOutputs.Sum(result => result.ErrorCount),
                "Axe reported accessibility rule errors: "
                    + string.Join(
                        "; ",
                        scan.WindowScanOutputs.SelectMany(result => result.Errors)
                            .Select(error => $"{error.Rule.ID}: {error.Rule.Description}")
                    )
            );
            Assert.IsTrue(
                Directory.GetFiles(output, "*.a11ytest").Length > 0,
                "Axe did not write its requested .a11ytest evidence."
            );
        }
        finally
        {
            StopApplication(process);
        }
    }

    [TestMethod]
    public void FlaUiFiltersSelectsAndRestoresVirtualizedIssues()
    {
        using var process = StartApplication();
        try
        {
            var window = WaitForWindow(process);
            using var automation = new UIA3Automation();
            var root = automation.FromHandle(window);
            var search =
                root.FindFirstDescendant(condition =>
                    condition.ByControlType(FlaUI.Core.Definitions.ControlType.Edit)
                ) ?? throw new InvalidOperationException("FlaUI could not find the Search field.");
            var list =
                root.FindFirstDescendant(condition =>
                    condition
                        .ByControlType(FlaUI.Core.Definitions.ControlType.List)
                        .And(condition.ByName("Issues"))
                ) ?? throw new InvalidOperationException("FlaUI could not find the Issues list.");
            WaitUntil(
                process,
                () => list.FindAllChildren().Length > 0,
                "Issue rows did not appear."
            );

            ActivateOwnedWindow(process, root, window);
            var scroll = root.FindAllDescendants(condition => condition.ByName("Issues"))
                .Single(element => element.Patterns.Scroll.IsSupported);
            scroll.Patterns.Scroll.Pattern.SetScrollPercent(-1, 100);
            WaitUntil(
                process,
                () => scroll.Patterns.Scroll.Pattern.VerticalScrollPercent.Value > 99,
                "The virtualized list did not reach End before filtering."
            );
            search.Focus();
            WaitUntil(
                process,
                () => search.Properties.HasKeyboardFocus.Value,
                "Search rejected focus."
            );
            search.Patterns.Value.Pattern.SetValue("10000");
            WaitUntil(
                process,
                () =>
                    list.FindAllChildren()
                        .Any(row => row.Name.Contains("10000", StringComparison.Ordinal)),
                "Filtering did not expose issue 10000."
            );
            Assert.IsTrue(
                search.Properties.HasKeyboardFocus.Value,
                "Filtering at End cleared the Search editor's focus."
            );
            Keyboard.Type("x");
            WaitUntil(
                process,
                () => search.Patterns.Value.Pattern.Value.Value == "10000x",
                "Physical typing did not reach Search after the scroll clamp."
            );
            WaitUntil(
                process,
                () => list.FindAllChildren().Length == 0,
                "Filtering the shortened list to zero did not settle."
            );
            search.Patterns.Value.Pattern.SetValue("10000");
            WaitUntil(
                process,
                () =>
                    list.FindAllChildren()
                        .Any(item => item.Name.Contains("10000", StringComparison.Ordinal)),
                "Refilling the empty list did not restore the matching row."
            );
            var row = list.FindAllChildren()
                .First(item => item.Name.Contains("10000", StringComparison.Ordinal));
            row.Patterns.SelectionItem.Pattern.Select();
            WaitUntil(
                process,
                () => row.Patterns.SelectionItem.Pattern.IsSelected.Value,
                "Issue selection was rejected."
            );
            search.Patterns.Value.Pattern.SetValue("");
            WaitUntil(
                process,
                () => list.FindAllChildren().Length > 1,
                "Clearing Search did not restore issue rows."
            );
        }
        finally
        {
            StopApplication(process);
        }
    }

    [TestMethod]
    [DataRow("keyboard")]
    [DataRow("pointer")]
    [DataRow("accessibility")]
    public void FlaUiNativeMenuTargetsUnselectedIssueAndPreservesSelection(string invocation)
    {
        using var process = StartApplication("--native-menus");
        nint owner = 0;
        try
        {
            owner = WaitForWindow(process);
            using var automation = new UIA3Automation();
            var root = automation.FromHandle(owner);
            var list =
                root.FindFirstDescendant(condition =>
                    condition
                        .ByControlType(FlaUI.Core.Definitions.ControlType.List)
                        .And(condition.ByName("Issues"))
                ) ?? throw new InvalidOperationException("FlaUI could not find the Issues list.");

            WaitUntil(
                process,
                () =>
                    FindIssueRow(list, 10_000) is not null && FindIssueRow(list, 9_998) is not null,
                "The native-menu fixture did not expose the target issue rows."
            );

            var selectedRow =
                FindIssueRow(list, 10_000)
                ?? throw new InvalidOperationException("Issue 10000 was not exposed.");
            selectedRow.Patterns.SelectionItem.Pattern.Select();
            WaitUntil(
                process,
                () =>
                    FindIssueRow(list, 10_000)?.Patterns.SelectionItem.Pattern.IsSelected.Value
                    == true,
                "Issue 10000 did not become selected."
            );

            var targetRow =
                FindIssueRow(list, 9_998)
                ?? throw new InvalidOperationException("Issue 9998 was not exposed.");
            Assert.IsTrue(
                targetRow.Name.Contains("— open ·", StringComparison.Ordinal),
                "The fixture target did not begin in the expected open state."
            );
            var targetPoint = Center(targetRow.BoundingRectangle);
            ActivateOwnedWindow(process, root, owner);
            WaitUntil(
                process,
                () => GetForegroundWindow() == owner,
                "Test owner did not gain foreground focus."
            );
            Mouse.RightClick(targetPoint);

            nint popup = 0;
            FlaUI.Core.AutomationElements.AutomationElement? menu = null;
            WaitUntil(
                process,
                () => (menu = FindNativeMenu(automation, process, owner, out popup)) is not null,
                "The native #32768 menu did not become discoverable through UI Automation."
            );
            Assert.AreEqual("#32768", WindowClass(popup));
            Assert.AreEqual(FlaUI.Core.Definitions.ControlType.Menu, menu!.ControlType);

            var open =
                menu.FindFirstDescendant(condition =>
                    condition
                        .ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem)
                        .And(condition.ByName("Open issue"))
                ) ?? throw new InvalidOperationException("Native menu omitted Open issue.");
            var toggle =
                menu.FindFirstDescendant(condition =>
                    condition
                        .ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem)
                        .And(condition.ByName("Toggle status"))
                ) ?? throw new InvalidOperationException("Native menu omitted Toggle status.");
            Assert.AreEqual("Open issue", open.Name);
            Assert.AreEqual("Toggle status", toggle.Name);

            Assert.AreEqual(
                owner,
                GetForegroundWindow(),
                "Native menu lost foreground focus before invocation."
            );
            if (invocation == "pointer")
                Mouse.LeftClick(Center(toggle.BoundingRectangle));
            else if (invocation == "accessibility")
                toggle.Patterns.Invoke.Pattern.Invoke();
            else
            {
                // Native menu navigation belongs to Windows; do not assume Lucent's
                // Home/End policy applies to an initially unselected system menu.
                Keyboard.Type(VirtualKeyShort.DOWN, VirtualKeyShort.DOWN);
                Wait.UntilInputIsProcessed();
                Keyboard.Press(VirtualKeyShort.RETURN);
                Keyboard.Release(VirtualKeyShort.RETURN);
            }
            Wait.UntilInputIsProcessed();
            WaitUntil(
                process,
                () => !IsWindow(popup),
                "Invoking the native Toggle status item did not dismiss the menu."
            );
            WaitUntil(
                process,
                () =>
                    FindIssueRow(list, 9_998)?.Name.Contains("— closed ·", StringComparison.Ordinal)
                    == true,
                "Native Toggle status did not update the nonselected issue."
            );
            WaitUntil(
                process,
                () =>
                    FindIssueRow(list, 10_000)?.Patterns.SelectionItem.Pattern.IsSelected.Value
                    == true,
                "Invoking the native menu changed the selected issue."
            );
            Assert.IsFalse(
                FindIssueRow(list, 9_998)?.Patterns.SelectionItem.Pattern.IsSelected.Value == true,
                "The right-clicked issue became selected while its native menu was invoked."
            );

            Mouse.RightClick(targetPoint);
            nint escapePopup = 0;
            WaitUntil(
                process,
                () => FindNativeMenu(automation, process, owner, out escapePopup) is not null,
                "The native menu did not reopen for dismissal validation."
            );
            Keyboard.Press(VirtualKeyShort.ESCAPE);
            Keyboard.Release(VirtualKeyShort.ESCAPE);
            WaitUntil(
                process,
                () => !IsWindow(escapePopup),
                "Escape did not dismiss the native menu."
            );

            if (invocation == "pointer")
            {
                // The adaptive desktop app opens with a detail pane. Narrow it so
                // the list reaches the owner edge before checking external placement.
                Assert.IsTrue(SetWindowPos(owner, 0, 0, 0, 600, 760, SwpNoMove));
                FlaUI.Core.AutomationElements.AutomationElement? back = null;
                WaitUntil(
                    process,
                    () =>
                        (
                            back = root.FindFirstDescendant(condition =>
                                condition.ByName("Back to issues")
                            )
                        )
                            is not null,
                    "The narrow workspace did not retain the selected issue's detail view."
                );
                back!.Patterns.Invoke.Pattern.Invoke();
                FlaUI.Core.AutomationElements.AutomationElement? narrowList = null;
                WaitUntil(
                    process,
                    () =>
                        (
                            narrowList = root.FindFirstDescendant(condition =>
                                condition
                                    .ByControlType(FlaUI.Core.Definitions.ControlType.List)
                                    .And(condition.ByName("Issues"))
                            )
                        )
                            is not null
                        && narrowList.BoundingRectangle.Width > 450,
                    "The narrow list did not reach the owner edge."
                );
                var edge = FindIssueRow(narrowList!, 9_998)!.BoundingRectangle;
                Mouse.RightClick(new Point(edge.Right - 8, edge.Top + edge.Height / 2));
                nint edgePopup = 0;
                FlaUI.Core.AutomationElements.AutomationElement? edgeMenu = null;
                WaitUntil(
                    process,
                    () =>
                        (edgeMenu = FindNativeMenu(automation, process, owner, out edgePopup))
                            is not null,
                    "The edge menu did not open."
                );
                Assert.IsTrue(
                    edgeMenu!.BoundingRectangle.Right > root.BoundingRectangle.Right,
                    "The native popup was clipped to its owner."
                );
                Assert.IsTrue(
                    process.CloseMainWindow(),
                    "The test owner rejected its close request."
                );
                WaitUntil(
                    process,
                    () => process.HasExited,
                    "Closing an owner with a native menu open did not exit."
                );
                Assert.AreEqual(0, process.ExitCode);
                Assert.IsFalse(IsWindow(edgePopup), "The native popup survived its owner.");
            }
        }
        finally
        {
            StopApplication(process);
        }
    }

    private static FlaUI.Core.AutomationElements.AutomationElement? FindIssueRow(
        FlaUI.Core.AutomationElements.AutomationElement list,
        int number
    )
    {
        var prefix = "#" + number + " ";
        return list.FindAllChildren()
            .FirstOrDefault(row => row.Name.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static FlaUI.Core.AutomationElements.AutomationElement? FindNativeMenu(
        UIA3Automation automation,
        Process process,
        nint owner,
        out nint handle
    )
    {
        handle = 0;
        foreach (var candidate in ProcessTopLevelWindows(process.Id))
        {
            if (candidate == owner || !IsWindowVisible(candidate))
                continue;
            if (!string.Equals(WindowClass(candidate), "#32768", StringComparison.Ordinal))
                continue;
            var nativeWindow = automation.FromHandle(candidate);
            var menu =
                nativeWindow.ControlType == FlaUI.Core.Definitions.ControlType.Menu
                    ? nativeWindow
                    : nativeWindow.FindFirstDescendant(condition =>
                        condition.ByControlType(FlaUI.Core.Definitions.ControlType.Menu)
                    );
            if (menu is null)
                continue;
            handle = candidate;
            return menu;
        }
        return null;
    }

    private static IEnumerable<nint> ProcessTopLevelWindows(int processId)
    {
        var desktop = GetDesktopWindow();
        for (
            var candidate = GetWindow(desktop, GetWindowChild);
            candidate != 0;
            candidate = GetWindow(candidate, GetWindowNext)
        )
        {
            _ = GetWindowThreadProcessId(candidate, out var candidateProcessId);
            if (candidateProcessId == processId)
                yield return candidate;
        }
    }

    private static string WindowClass(nint window)
    {
        var value = new char[256];
        var length = GetClassName(window, value, value.Length);
        return length == 0 ? string.Empty : new string(value, 0, length);
    }

    private static Point Center(System.Drawing.Rectangle rectangle) =>
        new(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2);

    private static Process StartApplication(string? argument = null)
    {
        var path = Environment.GetEnvironmentVariable("LUCENT_DESKTOP_APP");
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(
                "LUCENT_DESKTOP_APP must name the published Issue Browser executable."
            );
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(
                "The published Issue Browser executable does not exist.",
                fullPath
            );
        var start = new ProcessStartInfo(fullPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(fullPath)!,
        };
        if (argument is not null)
            start.ArgumentList.Add(argument);
        return Process.Start(start)
            ?? throw new InvalidOperationException("Could not launch the published Issue Browser.");
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
            "The published Issue Browser did not expose a window."
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
                    $"The published app exited {process.ExitCode}: {message}"
                );
            if (elapsed.Elapsed >= Timeout)
                throw new TimeoutException(message);
            Thread.Sleep(100);
        }
    }

    private static void StopApplication(Process process)
    {
        if (process.HasExited)
            return;
        _ = process.CloseMainWindow();
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit(10_000))
                throw new TimeoutException("The published app did not exit after termination.");
        }
    }

    private const uint GetWindowNext = 2;
    private const uint GetWindowChild = 5;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, [Out] char[] className, int maximum);

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags
    );

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out int processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
}
