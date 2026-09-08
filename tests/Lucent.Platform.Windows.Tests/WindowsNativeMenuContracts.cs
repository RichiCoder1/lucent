using Lucent.Core;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed class WindowsNativeMenuContracts
{
    [TestMethod]
    public void NativeLabelsEscapeLiteralAmpersands()
    {
        Assert.IsTrue(WindowsNativeMenuHost.TryEscapeNativeLabel("Save & Exit", out var escaped));
        Assert.AreEqual("Save && Exit", escaped);

        Assert.IsTrue(WindowsNativeMenuHost.TryEscapeNativeLabel("A && B", out escaped));
        Assert.AreEqual("A &&&& B", escaped);
    }

    [TestMethod]
    public void NativeLabelsWithShortcutMarkupFallbackToLucent()
    {
        foreach (var label in new[] { "Open\tCtrl+O", "Line\nBreak", "Null\0Value" })
        {
            Assert.IsFalse(
                WindowsNativeMenuHost.TryEscapeNativeLabel(label, out var escaped),
                $"The native menu accepted unsupported label markup: {label}"
            );
            Assert.AreEqual(string.Empty, escaped);
        }
    }

    [TestMethod]
    public void NativeMenuAnchorUsesPhysicalClientPixelsAtFractionalDpi()
    {
        var anchor = WindowsNativeMenuHost.AnchorInClientPixels(
            new LayoutRect(10.25f, 20.5f, 5.5f, 4.25f),
            144
        );

        Assert.AreEqual(16, anchor.X);
        Assert.AreEqual(38, anchor.Y);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            WindowsNativeMenuHost.AnchorInClientPixels(new LayoutRect(0, 0, 1, 1), 0)
        );
    }
}
