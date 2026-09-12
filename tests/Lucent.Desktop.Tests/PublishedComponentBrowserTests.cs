using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using UiElement = FlaUI.Core.AutomationElements.AutomationElement;

namespace Lucent.Desktop.Tests;

[TestClass]
public sealed class PublishedComponentBrowserTests
{
    [TestMethod]
    public void FlaUiSubmenuContentAlignsWithItsTriggerRow()
    {
        using var fixture = new BrowserFixture();
        fixture.SelectExample("Menus", "Surfaces");
        var trigger = fixture.Find("Open action menu").BoundingRectangle;
        Mouse.RightClick(
            new System.Drawing.Point(
                trigger.Left + trigger.Width / 2,
                trigger.Top + trigger.Height / 2
            )
        );
        var menu = fixture.WaitForPopup("Run command");
        var more = menu.FindFirstDescendant(c => c.ByName("More actions"))!.BoundingRectangle;
        BrowserFixture.CaptureSurface(menu, "menu");
        Mouse.MoveTo(
            new System.Drawing.Point(more.Left + more.Width / 2, more.Top + more.Height / 2)
        );
        var submenu = fixture.WaitForPopup("Nested action");
        var nested = submenu.FindFirstDescendant(c => c.ByName("Nested action"))!.BoundingRectangle;
        Assert.IsTrue(
            Math.Abs(nested.Top - more.Top) < more.Height / 2,
            $"Submenu first row {nested} is vertically offset from trigger {more}."
        );
        BrowserFixture.CaptureSurface(submenu, "submenu");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Keyboard.Type(VirtualKeyShort.ESCAPE);
    }

    [TestMethod]
    public void FlaUiCalendarAndSingleSuggestionUseCompactReadableGeometry()
    {
        using var fixture = new BrowserFixture();
        fixture.SelectExample("Date and time", "Editing");
        fixture.Find("Open calendar for Due date").Patterns.Invoke.Pattern.Invoke();
        var calendar = fixture.WaitForPopup("Previous month");
        var previous = calendar
            .FindFirstDescendant(c => c.ByName("Previous month"))!
            .BoundingRectangle;
        var next = calendar.FindFirstDescendant(c => c.ByName("Next month"))!.BoundingRectangle;
        Assert.IsTrue(
            Math.Abs(previous.Top - next.Top) <= 1,
            "Calendar navigation is vertically misaligned."
        );
        Assert.IsTrue(
            next.Left - previous.Right >= previous.Width * 3,
            "Calendar navigation crowds the month title."
        );
        Assert.IsTrue(
            calendar.BoundingRectangle.Height < previous.Height * 12,
            "The calendar is unexpectedly tall."
        );
        fixture.AssertCompactSurface(calendar, "calendar");
        Keyboard.Type(VirtualKeyShort.ESCAPE);

        fixture.SelectExample("Async ComboBox", "Navigation");
        var editor = fixture.Find("Workspace", editorOnly: true);
        editor.Focus();
        editor.Patterns.Value.Pattern.SetValue("Alpha");
        var suggestions = fixture.WaitForPopup("Alpha workspace");
        var option = suggestions
            .FindFirstDescendant(c => c.ByName("Alpha workspace"))!
            .BoundingRectangle;
        BrowserFixture.CaptureSurface(suggestions, "suggestions");
        Assert.IsTrue(
            suggestions.BoundingRectangle.Width >= editor.BoundingRectangle.Width,
            "Suggestions are narrower than their editor."
        );
        Assert.IsTrue(
            suggestions.BoundingRectangle.Height < option.Height * 4,
            $"One suggestion retained a full result page: popup {suggestions.BoundingRectangle}, option {option}."
        );
        Keyboard.Type(VirtualKeyShort.ESCAPE);
    }

    [TestMethod]
    public void FlaUiPasswordRevealTogglesAfterPointerReleaseAndRemasksOnBlur()
    {
        using var fixture = new BrowserFixture();
        fixture.SelectExample("Password field", "Editing");
        for (var click = 0; click < 3; click++)
        {
            var before = click % 2 == 0 ? "Show password" : "Hide password";
            var after = click % 2 == 0 ? "Hide password" : "Show password";
            var bounds = fixture.Find(before).BoundingRectangle;
            Mouse.LeftClick(
                new System.Drawing.Point(
                    bounds.Left + bounds.Width / 2,
                    bounds.Top + bounds.Height / 2
                )
            );
            fixture.Wait(
                () => fixture.FindOptional(after) is not null,
                $"Password click {click + 1} changes {before} to {after} after release"
            );
            fixture.Wait(
                () =>
                    fixture
                        .Find("Example password", editorOnly: true)
                        .Properties.HasKeyboardFocus.Value,
                "Password editor keeps pointer focus"
            );
        }
        fixture.Find("Search components").Focus();
        fixture.Wait(
            () => fixture.FindOptional("Show password") is not null,
            "Password remasks on blur"
        );
    }

    [TestMethod]
    public void FlaUiDialogBlocksItsNativeOwnerAndRestoresTriggerFocus()
    {
        using var fixture = new BrowserFixture();
        fixture.SelectExample("Navigation and dialogs", "Navigation");
        var trigger = fixture.Find("Review changes");
        trigger.Focus();
        fixture.Wait(() => trigger.Properties.HasKeyboardFocus.Value, "Dialog trigger focus");
        trigger.Patterns.Invoke.Pattern.Invoke();
        var popup = fixture.WaitForPopup("Apply changes");
        fixture.AssertCompactSurface(popup, "dialog");
        var popupHandle = popup.Properties.NativeWindowHandle.Value;
        Assert.AreEqual(fixture.OwnerHandle, GetWindow(popupHandle, 4), "Modal HWND owner");
        fixture.Wait(() => GetForegroundWindow() == popupHandle, "Modal foreground activation");
        Assert.IsFalse(
            IsWindowEnabled(fixture.OwnerHandle),
            "The native modal owner remained enabled."
        );
        Assert.IsNotNull(popup.FindFirstDescendant(c => c.ByName("Review component changes")));

        Keyboard.Type(VirtualKeyShort.ESCAPE);
        fixture.Wait(
            () => IsWindowEnabled(fixture.OwnerHandle),
            "Modal owner enablement after Escape"
        );
        fixture.Wait(
            () => GetForegroundWindow() == fixture.OwnerHandle,
            "Modal owner foreground return"
        );
        fixture.Wait(
            () => fixture.Find("Review changes").Properties.HasKeyboardFocus.Value,
            "Modal trigger focus return"
        );
        fixture.Wait(
            () => fixture.FindOptional("The dialog was canceled.") is not null,
            "Typed canceled result"
        );

        trigger.Patterns.Invoke.Pattern.Invoke();
        popup = fixture.WaitForPopup("Apply changes");
        popup.FindFirstDescendant(c => c.ByName("Apply changes"))!.Patterns.Invoke.Pattern.Invoke();
        fixture.Wait(
            () => IsWindowEnabled(fixture.OwnerHandle),
            "Modal owner enablement after accept"
        );
        fixture.Wait(
            () =>
                fixture.FindOptional("The application action completed and the dialog closed.")
                    is not null,
            "Typed application completion"
        );
        fixture.Wait(
            () => fixture.Find("Review changes").Properties.HasKeyboardFocus.Value,
            "Accepted dialog focus return"
        );
    }

    [TestMethod]
    public void FlaUiTooltipAndPopoverUseOwnedWindowsAndRestoreFocus()
    {
        using var fixture = new BrowserFixture();
        fixture.SelectExample("Tooltips and popovers", "Surfaces");
        var tooltipTrigger = fixture.Find("Hover or focus for help");
        tooltipTrigger.Focus();
        var tooltip = fixture.WaitForPopup(
            "Supporting details open in an owned surface. Escape dismisses the description."
        );
        fixture.AssertCompactSurface(tooltip, "tooltip");
        Assert.AreNotEqual(fixture.OwnerHandle, tooltip.Properties.NativeWindowHandle.Value);
        Assert.AreEqual(
            fixture.OwnerHandle,
            GetWindow(tooltip.Properties.NativeWindowHandle.Value, 4),
            "Tooltip HWND owner"
        );
        Assert.AreEqual(
            fixture.OwnerHandle,
            GetForegroundWindow(),
            "The tooltip stole native activation."
        );
        Keyboard.Type(VirtualKeyShort.ESCAPE);

        var search = fixture.Find("Search components");
        search.Focus();
        var searchBounds = search.BoundingRectangle;
        Mouse.MoveTo(
            new System.Drawing.Point(
                searchBounds.Left + 10,
                searchBounds.Top + searchBounds.Height / 2
            )
        );
        var helpBounds = tooltipTrigger.BoundingRectangle;
        var hoverPoint = new System.Drawing.Point(
            helpBounds.Left + helpBounds.Width * 3 / 4,
            helpBounds.Top + helpBounds.Height / 2
        );
        Mouse.MoveTo(hoverPoint);
        tooltip = fixture.WaitForPopup(
            "Supporting details open in an owned surface. Escape dismisses the description."
        );
        Assert.IsTrue(
            Math.Abs(tooltip.BoundingRectangle.Left - hoverPoint.X) < helpBounds.Height * 2,
            "Hover tooltip opened at the element edge instead of near the pointer."
        );
        BrowserFixture.CaptureSurface(tooltip, "hover-tooltip");
        Keyboard.Type(VirtualKeyShort.ESCAPE);

        var popoverTrigger = fixture.Find("Open details");
        popoverTrigger.Focus();
        popoverTrigger.Patterns.Invoke.Pattern.Invoke();
        var popup = fixture.WaitForPopup("Close popover");
        var popupHandle = popup.Properties.NativeWindowHandle.Value;
        var popupBounds = popup.BoundingRectangle;
        var ownerBounds = fixture.OwnerBounds;
        Assert.IsTrue(
            popupBounds.Width < ownerBounds.Width * 0.75
                && popupBounds.Height < ownerBounds.Height * 0.75,
            $"The compact example stretched to {popupBounds} within owner {ownerBounds}."
        );
        Assert.AreEqual(fixture.OwnerHandle, GetWindow(popupHandle, 4), "Popover HWND owner");
        fixture.Wait(
            () =>
                popup
                    .FindFirstDescendant(c => c.ByName("Close popover"))!
                    .Properties.HasKeyboardFocus.Value,
            "Popover content keyboard focus"
        );
        Assert.IsTrue(
            IsWindowEnabled(fixture.OwnerHandle),
            "A nonmodal popover disabled its native owner."
        );
        var captures = Environment.GetEnvironmentVariable("LUCENT_DESKTOP_CAPTURES");
        if (!string.IsNullOrWhiteSpace(captures))
        {
            Directory.CreateDirectory(captures);
            Capture.Element(popup).ToFile(Path.Combine(captures, "component-popover.png"));
        }
        popup.FindFirstDescendant(c => c.ByName("Close popover"))!.Patterns.Invoke.Pattern.Invoke();
        fixture.Wait(
            () => fixture.Find("Open details").Properties.HasKeyboardFocus.Value,
            "Popover trigger focus return"
        );

        popoverTrigger.Patterns.Invoke.Pattern.Invoke();
        popup = fixture.WaitForPopup("Close popover");
        popupHandle = popup.Properties.NativeWindowHandle.Value;
        fixture.Close();
        Assert.IsFalse(IsWindow(popupHandle), "The popup survived its owning application.");
    }

    private sealed class BrowserFixture : IDisposable
    {
        private readonly Process _process;
        private readonly Task<string> _errors;
        private readonly UIA3Automation _automation = new();
        private readonly UiElement _root;
        internal nint OwnerHandle { get; }
        internal System.Drawing.Rectangle OwnerBounds => _root.BoundingRectangle;

        internal BrowserFixture()
        {
            var path = Environment.GetEnvironmentVariable("LUCENT_COMPONENT_BROWSER_APP");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidOperationException(
                    "Set LUCENT_COMPONENT_BROWSER_APP to the published Component Browser executable."
                );
            _process =
                Process.Start(
                    new ProcessStartInfo(path)
                    {
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!,
                    }
                ) ?? throw new InvalidOperationException("Could not start Component Browser.");
            _errors = _process.StandardError.ReadToEndAsync();
            try
            {
                Wait(
                    () =>
                    {
                        _process.Refresh();
                        return _process.MainWindowHandle != 0;
                    },
                    "Component Browser window"
                );
                OwnerHandle = _process.MainWindowHandle;
                _root = _automation.FromHandle(OwnerHandle);
                PublishedIssueBrowserTests.ActivateOwnedWindow(_process, _root, OwnerHandle);
                Wait(() => GetForegroundWindow() == OwnerHandle, "Component Browser foreground");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void SelectExample(string title, string family)
        {
            Find("Search components").Patterns.Value.Pattern.SetValue(title);
            var item = Find(title + " · " + family);
            item.Patterns.SelectionItem.Pattern.Select();
        }

        internal void AssertCompactSurface(UiElement surface, string name)
        {
            var bounds = surface.BoundingRectangle;
            CaptureSurface(surface, name);
            Assert.IsTrue(
                bounds.Width < OwnerBounds.Width * 0.75,
                $"The {name} stretched to {bounds} within owner {OwnerBounds}."
            );
        }

        internal static void CaptureSurface(UiElement surface, string name)
        {
            var captures = Environment.GetEnvironmentVariable("LUCENT_DESKTOP_CAPTURES");
            if (!string.IsNullOrWhiteSpace(captures))
            {
                Directory.CreateDirectory(captures);
                // UIA can expose the first installed scene before Windows presents its pixels.
                Thread.Sleep(150);
                Capture.Element(surface).ToFile(Path.Combine(captures, $"component-{name}.png"));
            }
        }

        internal UiElement? FindOptional(string name, bool editorOnly = false) =>
            _root.FindFirstDescendant(c =>
                editorOnly
                    ? c.ByName(name).And(c.ByControlType(FlaUI.Core.Definitions.ControlType.Edit))
                    : c.ByName(name)
            );

        internal UiElement Find(string name, bool editorOnly = false)
        {
            UiElement? found = null;
            Wait(() => (found = FindOptional(name, editorOnly)) is not null, name);
            return found!;
        }

        internal UiElement WaitForPopup(string content)
        {
            UiElement? found = null;
            Wait(
                () =>
                {
                    foreach (var handle in ProcessWindows(_process.Id))
                    {
                        if (handle == OwnerHandle || !IsWindowVisible(handle))
                            continue;
                        try
                        {
                            var window = _automation.FromHandle(handle);
                            if (window.FindFirstDescendant(c => c.ByName(content)) is not null)
                            {
                                found = window;
                                break;
                            }
                        }
                        catch (TimeoutException) when (!IsWindowVisible(handle))
                        {
                            // A dismissed surface may disappear between HWND enumeration and UIA lookup.
                        }
                    }
                    return found is not null;
                },
                "Owned popup: " + content
            );
            return found!;
        }

        internal void Wait(Func<bool> condition, string operation)
        {
            var clock = Stopwatch.StartNew();
            while (!condition())
            {
                if (_process.HasExited)
                    throw new AssertFailedException(
                        $"Component Browser exited {_process.ExitCode} while waiting for {operation}. {_errors.GetAwaiter().GetResult()}"
                    );
                if (clock.Elapsed > TimeSpan.FromSeconds(15))
                {
                    if (_root is not null)
                        CaptureSurface(_root, "timeout");
                    throw new AssertFailedException("Timed out waiting for " + operation);
                }
                Thread.Sleep(50);
            }
        }

        internal void Close()
        {
            if (_process.HasExited)
                return;
            _process.CloseMainWindow();
            Assert.IsTrue(
                _process.WaitForExit(10_000),
                "Component Browser did not close normally."
            );
            Assert.AreEqual(0, _process.ExitCode);
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

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    private static List<nint> ProcessWindows(int processId)
    {
        var handles = new List<nint>();
        EnumWindows(
            (handle, parameter) =>
            {
                _ = GetWindowThreadProcessId(handle, out var owner);
                if (owner == processId)
                    handles.Add(handle);
                return true;
            },
            0
        );
        return handles;
    }

    private delegate bool EnumWindowCallback(nint handle, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowCallback callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint handle, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
}
