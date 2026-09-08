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

    [TestMethod]
    public void NativeDescriptorAcceptsBoundedRecursiveSubmenusAndMapsOnlyLeaves()
    {
        var leaf = new StandardMenuEntry(
            StandardMenuEntryKind.Command,
            "Open & inspect",
            new SemanticIdentity(4, 10, 1),
            enabled: true
        );
        var nested = new StandardMenuDescriptor([leaf]);
        var root = new StandardMenuDescriptor([
            new StandardMenuEntry(
                StandardMenuEntryKind.Submenu,
                "More actions",
                new SemanticIdentity(4, 9, 1),
                enabled: true,
                submenu: nested
            ),
        ]);

        Assert.IsTrue(
            WindowsNativeMenuHost.TryValidateNativeDescriptor(root, out var leaves),
            "A bounded standard submenu was incorrectly rejected from native hosting."
        );
        Assert.AreEqual(
            1,
            leaves,
            "Native validation counted a submenu trigger as a leaf command."
        );
    }

    [TestMethod]
    public void NativeDescriptorRejectsEmptyOrOverdeepSubmenus()
    {
        var empty = new StandardMenuDescriptor([]);
        var root = new StandardMenuDescriptor([
            new StandardMenuEntry(
                StandardMenuEntryKind.Submenu,
                "Empty",
                null,
                true,
                submenu: empty
            ),
        ]);
        Assert.IsFalse(
            WindowsNativeMenuHost.TryValidateNativeDescriptor(root, out _),
            "An empty native submenu would produce an unusable popup branch."
        );

        StandardMenuDescriptor current = new([
            new StandardMenuEntry(
                StandardMenuEntryKind.Command,
                "Leaf",
                new SemanticIdentity(1, 1, 1),
                true
            ),
        ]);
        for (var depth = 1; depth <= 16; depth++)
            current = new([
                new StandardMenuEntry(
                    StandardMenuEntryKind.Submenu,
                    "Next",
                    null,
                    true,
                    submenu: current
                ),
            ]);
        Assert.IsFalse(
            WindowsNativeMenuHost.TryValidateNativeDescriptor(current, out _),
            "The native descriptor depth guard did not reject an overdeep submenu tree."
        );
    }
}
