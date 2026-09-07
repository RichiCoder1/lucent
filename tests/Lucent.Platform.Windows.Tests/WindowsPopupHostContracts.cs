using SDL3;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed class WindowsPopupHostContracts
{
    [TestMethod]
    public void PopupUsesParentMenuFocusAndHighDensityContract()
    {
        Assert.IsTrue(WindowsPopupHost.PopupFlags.HasFlag(SDL.WindowFlags.PopupMenu));
        Assert.IsTrue(WindowsPopupHost.PopupFlags.HasFlag(SDL.WindowFlags.HighPixelDensity));
        Assert.IsTrue(WindowsPopupHost.PopupFlags.HasFlag(SDL.WindowFlags.Hidden));
        Assert.IsFalse(WindowsPopupHost.PopupFlags.HasFlag(SDL.WindowFlags.Modal));
        Assert.IsFalse(WindowsPopupHost.PopupFlags.HasFlag(SDL.WindowFlags.NotFocusable));
    }

    [TestMethod]
    public void PopupGeometryConvertsLogicalCoordinatesThroughDpiAndPixelDensity()
    {
        Assert.AreEqual(150, WindowsPopupHost.ToWindowUnits(100, 1.5f, 1));
        Assert.AreEqual(100, WindowsPopupHost.ToWindowUnits(100, 1.5f, 1.5f));
        Assert.AreEqual(2, WindowsPopupHost.ToWindowUnits(1.1f, 1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            WindowsPopupHost.ToWindowUnits(float.NaN, 1, 1)
        );
    }

    [TestMethod]
    public void PopupRoutesWindowSpecificInputByTheUnionWindowId()
    {
        var mouse = new SDL.Event { Type = (uint)SDL.EventType.MouseButtonDown };
        mouse.Button.WindowID = 17;
        Assert.AreEqual(17U, WindowsPopupHost.EventWindowId(mouse));

        var key = new SDL.Event { Type = (uint)SDL.EventType.KeyDown };
        key.Key.WindowID = 29;
        Assert.AreEqual(29U, WindowsPopupHost.EventWindowId(key));

        var global = new SDL.Event { Type = (uint)SDL.EventType.Quit };
        global.Window.WindowID = 99;
        Assert.AreEqual(0U, WindowsPopupHost.EventWindowId(global));

        var ownerEscape = new SDL.Event { Type = (uint)SDL.EventType.KeyDown };
        ownerEscape.Key.WindowID = 17;
        Assert.IsTrue(
            WindowsPopupHost.TargetsPopup(ownerEscape, 29, 17),
            "Topmost popup did not receive keyboard input retained on its SDL owner window."
        );
        var ownerPointer = new SDL.Event { Type = (uint)SDL.EventType.MouseButtonDown };
        ownerPointer.Button.WindowID = 17;
        Assert.IsFalse(WindowsPopupHost.TargetsPopup(ownerPointer, 29, 17));
        var foreignKey = ownerEscape;
        foreignKey.Key.WindowID = 41;
        Assert.IsFalse(WindowsPopupHost.TargetsPopup(foreignKey, 29, 17));
    }

    [TestMethod]
    public void StalePopupWindowEventsNeverFallThroughToOwnerInputOrClose()
    {
        foreach (
            var type in new[]
            {
                SDL.EventType.WindowFocusLost,
                SDL.EventType.WindowMouseLeave,
                SDL.EventType.WindowCloseRequested,
            }
        )
        {
            var stale = new SDL.Event { Type = (uint)type };
            stale.Window.WindowID = 29;
            Assert.IsTrue(WindowsBootstrap.IsForeignWindowEvent(stale, 17));
            Assert.IsFalse(WindowsBootstrap.IsForeignWindowEvent(stale, 29));
        }

        var quit = new SDL.Event { Type = (uint)SDL.EventType.Quit };
        Assert.IsFalse(
            WindowsBootstrap.IsForeignWindowEvent(quit, 17),
            "Global quit was mistaken for stale popup input."
        );
        var wake = new SDL.Event { Type = (uint)SDL.EventType.User };
        Assert.IsFalse(
            WindowsBootstrap.IsForeignWindowEvent(wake, 17),
            "A dispatcher wake event was mistaken for stale popup input."
        );
    }

    [TestMethod]
    public void OwnerDismissalPointerDownCannotArmUnderlyingContent()
    {
        var gate = new WindowsPopupInputGate();
        gate.Opened();
        Assert.IsTrue(gate.ConsumeOwnerPointerDown(popupOpen: true));
        Assert.IsFalse(
            gate.ConsumeOwnerPointerDown(popupOpen: false),
            "Pointer suppression escaped the dismissal event."
        );

        gate.Opened();
        gate.LostFocus();
        Assert.IsTrue(
            gate.ConsumeOwnerPointerDown(popupOpen: false),
            "Focus-loss dismissal allowed the following owner down to click through."
        );
        Assert.IsFalse(gate.ConsumeOwnerPointerDown(popupOpen: false));
    }
}
