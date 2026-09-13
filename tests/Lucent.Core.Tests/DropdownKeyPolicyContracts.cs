using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class DropdownKeyPolicyContracts
{
    [TestMethod]
    public void F4AppendsThePortableKeyEnumAndClassifierRejectsPlatformChords()
    {
        var ordinals = Enum.GetValues<Key>().ToDictionary(key => key, key => (int)key);
        Assert.AreEqual(24, ordinals[Key.Z], "Adding F4 renumbered an existing portable key.");
        Assert.AreEqual(25, ordinals[Key.F4]);
        Assert.AreEqual(
            DropdownKeyAction.Toggle,
            DropdownKeyPolicy.Classify(new(KeyCommandKind.Down, Key.F4))
        );
        Assert.AreEqual(
            DropdownKeyAction.Open,
            DropdownKeyPolicy.Classify(new(KeyCommandKind.Down, Key.Down, KeyModifiers.Alt))
        );
        Assert.AreEqual(
            DropdownKeyAction.Close,
            DropdownKeyPolicy.Classify(new(KeyCommandKind.Down, Key.Up, KeyModifiers.Alt))
        );
        Assert.AreEqual(
            DropdownKeyAction.None,
            DropdownKeyPolicy.Classify(
                new(KeyCommandKind.Down, Key.F4, KeyModifiers.None, IsRepeat: true)
            )
        );
        Assert.AreEqual(
            DropdownKeyAction.None,
            DropdownKeyPolicy.Classify(new(KeyCommandKind.Down, Key.F4, KeyModifiers.Alt))
        );
        Assert.AreEqual(
            DropdownKeyAction.None,
            DropdownKeyPolicy.Classify(
                new(KeyCommandKind.Down, Key.Down, KeyModifiers.Alt | KeyModifiers.Control)
            )
        );
    }
}
