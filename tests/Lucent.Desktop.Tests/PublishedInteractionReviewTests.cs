using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace Lucent.Desktop.Tests;

[TestClass]
public sealed partial class PublishedInteractionReviewTests
{
    [TestMethod]
    public void DatePickerPhysicalDropdownKeysToggleAndTabDismissesIntoTraversal()
    {
        using var fixture = new ReviewFixture();
        var editor = fixture.Find("Review date", ControlType.Edit);
        editor.Focus();
        fixture.Wait(() => editor.Properties.HasKeyboardFocus.Value, "date editor focus");

        Keyboard.Type(VirtualKeyShort.F4);
        var calendar = fixture.WaitForPopup("Previous month");
        var handle = calendar.Properties.NativeWindowHandle.Value;
        Keyboard.Type(VirtualKeyShort.F4);
        fixture.Wait(() => !IsWindow(handle), "F4 calendar toggle closed");

        HoldAlt(() => PublishedIssueBrowserTests.TypeNavigation(VirtualKeyShort.DOWN));
        calendar = fixture.WaitForPopup("Previous month");
        handle = calendar.Properties.NativeWindowHandle.Value;
        HoldAlt(() => PublishedIssueBrowserTests.TypeNavigation(VirtualKeyShort.UP));
        fixture.Wait(() => !IsWindow(handle), "Alt+Up calendar dismissal");

        HoldAlt(() => PublishedIssueBrowserTests.TypeNavigation(VirtualKeyShort.DOWN));
        calendar = fixture.WaitForPopup("Previous month");
        handle = calendar.Properties.NativeWindowHandle.Value;
        Keyboard.Type(VirtualKeyShort.TAB);
        fixture.Wait(() => !IsWindow(handle), "Tab calendar dismissal");
        fixture.Wait(
            () => fixture.Find("Open calendar for Review date").Properties.HasKeyboardFocus.Value,
            "Tab continuing owner traversal"
        );
    }

    [TestMethod]
    public void ListBoxPageDownUsesItsActualViewportAndSkipsDisabledBoundary()
    {
        using var fixture = new ReviewFixture();
        var first = fixture.Find("Paged choice 0");
        first.Focus();
        fixture.Wait(() => first.Properties.HasKeyboardFocus.Value, "first paged choice focus");

        PublishedIssueBrowserTests.TypeNavigation(VirtualKeyShort.NEXT);
        fixture.Wait(
            () => fixture.Find("Paged choice 4").Properties.HasKeyboardFocus.Value,
            "PageDown movement derived from the 125-pixel viewport"
        );
        for (var page = 0; page < 10; page++)
            PublishedIssueBrowserTests.TypeNavigation(VirtualKeyShort.NEXT);
        fixture.Wait(
            () => fixture.Find("Paged choice 19").Properties.HasKeyboardFocus.Value,
            "PageDown clamped at the final collection boundary"
        );
        PublishedIssueBrowserTests.TypeNavigation(VirtualKeyShort.NEXT);
        fixture.Wait(
            () => fixture.Find("Paged choice 19").Properties.HasKeyboardFocus.Value,
            "PageDown remained clamped at the final collection boundary"
        );
        Assert.IsNotNull(
            fixture.FindOptional("Paged selection 0"),
            "Explicit paging committed the active choice."
        );
    }

    [TestMethod]
    public void NumberStepperPointerRetainsEditorFocusAndDisablesAtUpperBound()
    {
        using var fixture = new ReviewFixture();
        var editor = fixture.Find("Review number", ControlType.Edit);
        editor.Focus();
        fixture.Wait(() => editor.Properties.HasKeyboardFocus.Value, "number editor focus");
        Keyboard.Press(VirtualKeyShort.CONTROL);
        try
        {
            Keyboard.Type(VirtualKeyShort.KEY_A);
        }
        finally
        {
            Keyboard.Release(VirtualKeyShort.CONTROL);
        }
        var increase = fixture.Find("Increase Review number", ControlType.Button);
        Assert.IsTrue(increase.Properties.IsEnabled.Value);
        var increasePoint = Center(increase.BoundingRectangle);
        Assert.AreEqual(
            fixture.OwnerHandle,
            WindowFromPoint(increasePoint),
            $"The number increase point was outside or occluded: {increase.BoundingRectangle}."
        );

        Mouse.LeftClick(increasePoint);
        fixture.Wait(
            () => editor.Patterns.Value.Pattern.Value.Value == "2",
            "number editor value after the pointer step"
        );
        fixture.Wait(
            () => fixture.FindOptional("Number value 2; requests 1") is not null,
            "single pointer request at the upper bound"
        );
        fixture.Wait(
            () => editor.Properties.HasKeyboardFocus.Value,
            "number editor focus retained after pointer step"
        );
        fixture.Wait(() => !increase.Properties.IsEnabled.Value, "upper-bound increase disabled");
        Assert.IsTrue(
            fixture.Find("Decrease Review number", ControlType.Button).Properties.IsEnabled.Value
        );

        Mouse.LeftClick(Center(increase.BoundingRectangle));
        Thread.Sleep(150);
        Assert.IsNotNull(fixture.FindOptional("Number value 2; requests 1"));
        Assert.IsTrue(editor.Properties.HasKeyboardFocus.Value);
    }

    [TestMethod]
    public void NestedTooltipEscapeThenDialogEscapeDismissTheirOwnSurfaces()
    {
        using var fixture = new ReviewFixture();
        fixture.Find("Open tooltip dialog").Patterns.Invoke.Pattern.Invoke();
        var dialog = fixture.WaitForPopup("Nested tooltip target");
        var dialogHandle = dialog.Properties.NativeWindowHandle.Value;
        var target = dialog.FindFirstDescendant(c => c.ByName("Nested tooltip target"))!;
        target.Focus();
        fixture.Wait(() => target.Properties.HasKeyboardFocus.Value, "nested tooltip target focus");
        var tooltip = fixture.WaitForPopup("Nested tooltip details");
        var tooltipHandle = tooltip.Properties.NativeWindowHandle.Value;

        Keyboard.Type(VirtualKeyShort.ESCAPE);
        fixture.Wait(() => !IsWindow(tooltipHandle), "first Escape to close only the tooltip");
        Assert.IsTrue(IsWindow(dialogHandle), "The tooltip Escape also closed its dialog.");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        fixture.Wait(() => !IsWindow(dialogHandle), "second Escape to cancel the dialog");
        fixture.Wait(
            () => fixture.FindOptional("Tooltip dialog canceled") is not null,
            "typed tooltip-dialog cancellation"
        );
    }

    [TestMethod]
    public void HostDismissedPendingFailureSettlesAndAllowsFreshDialog()
    {
        using var fixture = new ReviewFixture();
        fixture.Find("Open pending dialog").Patterns.Invoke.Pattern.Invoke();
        var dialog = fixture.WaitForPopup("Start pending acceptance");
        var firstHandle = dialog.Properties.NativeWindowHandle.Value;
        dialog
            .FindFirstDescendant(c => c.ByName("Start pending acceptance"))!
            .Patterns.Invoke.Pattern.Invoke();
        fixture.Wait(
            () => fixture.FindOptional("Pending write running") is not null,
            "pending application write"
        );

        Assert.IsTrue(PostMessage(firstHandle, 0x0010, 0, 0), "Could not request host dismissal.");
        fixture.Wait(() => !IsWindow(firstHandle), "host-dismissed pending dialog");
        fixture.Wait(
            () => IsWindowEnabled(fixture.OwnerHandle),
            "owner re-enabled after dismissal"
        );
        fixture.Find("Fail pending write").Patterns.Invoke.Pattern.Invoke();
        fixture.Wait(
            () => fixture.FindOptional("Pending submission Failed") is not null,
            "failed pending submission result"
        );
        fixture.Wait(
            () => fixture.FindOptional("Pending dialog canceled") is not null,
            "settled typed dialog result"
        );

        fixture.Find("Open pending dialog").Patterns.Invoke.Pattern.Invoke();
        var reopened = fixture.WaitForPopup("Start pending acceptance");
        var reopenedHandle = reopened.Properties.NativeWindowHandle.Value;
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        fixture.Wait(() => !IsWindow(reopenedHandle), "fresh dialog cancellation");
    }

    [TestMethod]
    public void OpenCalendarTracksLiveAvailabilityAndReopens()
    {
        using var fixture = new ReviewFixture();
        fixture.Find("Open calendar for Review date").Patterns.Invoke.Pattern.Invoke();
        var calendar = fixture.WaitForPopup("Previous month");
        var calendarHandle = calendar.Properties.NativeWindowHandle.Value;
        var staleDay = fixture.FindIn(calendar, "Sunday, June 16, 2024");

        fixture.Find("Disable date").Patterns.Invoke.Pattern.Invoke();
        fixture.Wait(() => !IsWindow(calendarHandle), "calendar dismissal after disabling");
        try
        {
            staleDay.Patterns.SelectionItem.Pattern.Select();
        }
        catch (Exception) when (!IsWindow(calendarHandle))
        {
            // A destroyed native provider is the expected stale-client outcome.
        }
        fixture.Wait(
            () => fixture.FindOptional("Date disabled: 2024-06-15") is not null,
            "unavailable calendar preserving its applied date"
        );

        fixture.Find("Enable date").Patterns.Invoke.Pattern.Invoke();
        fixture.Wait(
            () => fixture.FindOptional("Date enabled: 2024-06-15") is not null,
            "date re-enabled before reopening its calendar"
        );
        fixture.Find("Open calendar for Review date").Patterns.Invoke.Pattern.Invoke();
        calendar = fixture.WaitForPopup("Previous month");
        fixture.FindIn(calendar, "Sunday, June 16, 2024").Patterns.SelectionItem.Pattern.Select();
        fixture.Wait(
            () => fixture.FindOptional("Date enabled: 2024-06-16") is not null,
            "reopened calendar date selection"
        );
    }

    [TestMethod]
    public void HeldSliderEscapeRollsBackAndSecondaryReleaseKeepsPrimaryGesture()
    {
        using var fixture = new ReviewFixture();
        var slider = fixture.Find("Review slider", ControlType.Slider);
        var range = slider.Patterns.RangeValue.Pattern;
        var bounds = slider.BoundingRectangle;
        var y = bounds.Top + bounds.Height / 2;
        Point At(double fraction) => new(bounds.Left + (int)(bounds.Width * fraction), y);
        Assert.AreEqual(
            fixture.OwnerHandle,
            WindowFromPoint(At(0.2)),
            $"The slider gesture start was outside or occluded: {bounds}."
        );
        Assert.AreEqual(
            fixture.OwnerHandle,
            WindowFromPoint(At(0.8)),
            $"The slider gesture destination was outside or occluded: {bounds}."
        );

        Mouse.MoveTo(At(0.2));
        Mouse.Down(MouseButton.Left);
        try
        {
            Mouse.MoveTo(At(0.8));
            fixture.Wait(() => range.Value.Value >= 70, "accepted slider preview");
            Keyboard.Type(VirtualKeyShort.ESCAPE);
            fixture.Wait(
                () => fixture.FindOptional("Slider value 20; commits 0") is not null,
                "held slider Escape rollback"
            );
        }
        finally
        {
            Mouse.Up(MouseButton.Left);
        }
        fixture.Wait(
            () => fixture.FindOptional("Slider value 20; commits 0") is not null,
            "release after canceled slider gesture"
        );

        Mouse.MoveTo(At(0.2));
        Mouse.Down(MouseButton.Left);
        try
        {
            Mouse.MoveTo(At(0.8));
            fixture.Wait(() => range.Value.Value >= 70, "second slider preview");
            Mouse.Down(MouseButton.Right);
            Mouse.Up(MouseButton.Right);
            fixture.Wait(
                () =>
                    range.Value.Value >= 70
                    && Enumerable
                        .Range(70, 31)
                        .Any(value =>
                            fixture.FindOptional($"Slider value {value}; commits 0") is not null
                        ),
                "secondary release retaining the primary gesture"
            );
            Mouse.MoveTo(At(0.6));
        }
        finally
        {
            Mouse.Up(MouseButton.Left);
        }
        fixture.Wait(
            () =>
                range.Value.Value is >= 50 and <= 70
                && Enumerable
                    .Range(50, 21)
                    .Any(value =>
                        fixture.FindOptional($"Slider value {value}; commits 1") is not null
                    ),
            "primary slider completion after secondary release"
        );
    }

    [TestMethod]
    public void MenuKeyboardRouteSurvivesSeparatorAndDisabledHoverAtBothLevels()
    {
        using var fixture = new ReviewFixture();
        var trigger = fixture.Find("Open review menu").BoundingRectangle;
        Mouse.RightClick(Center(trigger));
        var menu = fixture.WaitForPopup("First command");
        var menuHandle = menu.Properties.NativeWindowHandle.Value;
        var first = fixture.FindIn(menu, "First command");
        var second = fixture.FindIn(menu, "Second command");
        var unavailable = fixture.FindIn(menu, "Unavailable command");
        var more = fixture.FindIn(menu, "More commands");
        fixture.Wait(() => first.Properties.HasKeyboardFocus.Value, "first menu item focus");

        Mouse.MoveTo(Between(first.BoundingRectangle, second.BoundingRectangle));
        PublishedIssueBrowserTests.TypeNavigation(VirtualKeyShort.DOWN);
        fixture.Wait(() => second.Properties.HasKeyboardFocus.Value, "Down after separator hover");
        Mouse.MoveTo(Center(unavailable.BoundingRectangle));
        PublishedIssueBrowserTests.TypeNavigation(VirtualKeyShort.DOWN);
        fixture.Wait(() => more.Properties.HasKeyboardFocus.Value, "Down after disabled hover");
        PublishedIssueBrowserTests.TypeNavigation(VirtualKeyShort.RIGHT);

        var submenu = fixture.WaitForPopup("Nested first");
        var nestedFirst = fixture.FindIn(submenu, "Nested first");
        var nestedUnavailable = fixture.FindIn(submenu, "Nested unavailable");
        var nestedSecond = fixture.FindIn(submenu, "Nested second");
        fixture.Wait(
            () => nestedFirst.Properties.HasKeyboardFocus.Value,
            "first nested menu item focus"
        );
        Mouse.MoveTo(Between(nestedFirst.BoundingRectangle, nestedUnavailable.BoundingRectangle));
        PublishedIssueBrowserTests.TypeNavigation(VirtualKeyShort.DOWN);
        fixture.Wait(
            () => nestedSecond.Properties.HasKeyboardFocus.Value,
            "Down after nested separator hover"
        );
        Mouse.MoveTo(Center(nestedUnavailable.BoundingRectangle));
        PublishedIssueBrowserTests.TypeNavigation(VirtualKeyShort.UP);
        fixture.Wait(
            () => nestedFirst.Properties.HasKeyboardFocus.Value,
            "Up after nested disabled hover"
        );
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        fixture.Wait(() => fixture.FindOptional("Nested first") is null, "nested menu Escape");
        menu = fixture.WaitForPopup("First command");
        menuHandle = menu.Properties.NativeWindowHandle.Value;
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        fixture.Wait(() => !IsWindowVisible(menuHandle), "root menu Escape");
    }

    private static Point Center(Rectangle bounds) =>
        new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);

    private static Point Between(Rectangle before, Rectangle after) =>
        new(before.Left + before.Width / 2, before.Bottom + (after.Top - before.Bottom) / 2);

    private static void HoldAlt(Action action)
    {
        Keyboard.Press(VirtualKeyShort.LMENU);
        try
        {
            action();
        }
        finally
        {
            Keyboard.Release(VirtualKeyShort.LMENU);
        }
    }

    private sealed class ReviewFixture : IDisposable
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
        private readonly Process _process;
        private readonly Task<string> _errors;
        private readonly UIA3Automation _automation = new();
        private readonly AutomationElement _root;

        internal ReviewFixture()
        {
            var path = Environment.GetEnvironmentVariable("LUCENT_DESKTOP_HOST");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidOperationException(
                    "LUCENT_DESKTOP_HOST must name the published Windows TestHost executable."
                );
            var fullPath = Path.GetFullPath(path);
            var start = new ProcessStartInfo(fullPath)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(fullPath)!,
            };
            start.ArgumentList.Add("--interaction-review-fixture");
            _process =
                Process.Start(start)
                ?? throw new InvalidOperationException("Could not start interaction fixture.");
            _errors = _process.StandardError.ReadToEndAsync();
            try
            {
                Wait(
                    () =>
                    {
                        _process.Refresh();
                        return _process.MainWindowHandle != 0;
                    },
                    "interaction fixture window"
                );
                OwnerHandle = _process.MainWindowHandle;
                _root = _automation.FromHandle(OwnerHandle);
                Wait(
                    () =>
                    {
                        try
                        {
                            PublishedIssueBrowserTests.ActivateOwnedWindow(
                                _process,
                                _root,
                                OwnerHandle
                            );
                            return true;
                        }
                        catch (COMException) when (!_process.HasExited)
                        {
                            return false;
                        }
                    },
                    "fixture foreground activation"
                );
                Wait(() => GetForegroundWindow() == OwnerHandle, "fixture foreground activation");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal nint OwnerHandle { get; }

        internal AutomationElement Find(string name)
        {
            AutomationElement? found = null;
            Wait(() => (found = FindOptional(name)) is not null, name);
            return found!;
        }

        internal AutomationElement Find(string name, ControlType controlType)
        {
            AutomationElement? found = null;
            Wait(
                () =>
                    (
                        found = _root.FindFirstDescendant(c =>
                            c.ByName(name).And(c.ByControlType(controlType))
                        )
                    )
                        is not null,
                name + " " + controlType
            );
            return found!;
        }

        internal AutomationElement? FindOptional(string name) =>
            _root.FindFirstDescendant(c => c.ByName(name));

        internal AutomationElement FindIn(AutomationElement root, string name) =>
            !_process.HasExited
                ? root.FindFirstDescendant(c => c.ByName(name))
                    ?? throw new InvalidOperationException($"Popup did not expose '{name}'.")
                : throw new InvalidOperationException("Interaction fixture exited.");

        internal AutomationElement WaitForPopup(string content)
        {
            AutomationElement? found = null;
            Wait(
                () =>
                {
                    foreach (var handle in ProcessWindows(_process.Id))
                    {
                        if (handle == OwnerHandle || !IsWindowVisible(handle))
                            continue;
                        try
                        {
                            var candidate = _automation.FromHandle(handle);
                            if (candidate.FindFirstDescendant(c => c.ByName(content)) is not null)
                            {
                                found = candidate;
                                return true;
                            }
                        }
                        catch (TimeoutException) when (!IsWindowVisible(handle)) { }
                    }
                    return false;
                },
                "owned popup: " + content
            );
            return found!;
        }

        internal void Wait(Func<bool> condition, string operation)
        {
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                if (_process.HasExited)
                    throw new AssertFailedException(
                        $"Interaction fixture exited {_process.ExitCode} while waiting for {operation}. {_errors.GetAwaiter().GetResult()}"
                    );
                if (condition())
                    return;
                if (elapsed.Elapsed >= Timeout)
                    throw new AssertFailedException("Timed out waiting for " + operation);
                Thread.Sleep(25);
            }
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.CloseMainWindow();
                    if (!_process.WaitForExit(5_000))
                    {
                        _process.Kill(entireProcessTree: true);
                        _process.WaitForExit();
                    }
                }
            }
            finally
            {
                _automation.Dispose();
                _process.Dispose();
            }
        }
    }

    private static List<nint> ProcessWindows(int processId)
    {
        var handles = new List<nint>();
        EnumWindows(
            (handle, parameter) =>
            {
                GetWindowThreadProcessId(handle, out var owner);
                if (owner == processId)
                    handles.Add(handle);
                _ = parameter;
                return true;
            },
            0
        );
        return handles;
    }

    private delegate bool EnumWindowCallback(nint handle, nint parameter);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowCallback callback, nint parameter);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out int processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowEnabled(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
