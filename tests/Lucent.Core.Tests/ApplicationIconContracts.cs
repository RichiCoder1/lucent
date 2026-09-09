using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ApplicationIconContracts
{
    [TestMethod]
    public void GeneratedDefaultCopiesItsFiniteRenditionList()
    {
        var first = Source("first.png");
        var second = Source("second.png");
        var renditions = new List<ApplicationIconRendition> { new(16, first), new(32, second) };
        var icon = new ApplicationIconDefault(first, renditions);

        renditions.Clear();

        Assert.AreEqual(2, icon.Renditions.Count);
        Assert.AreEqual(16, icon.Renditions[0].PixelSize);
        Assert.AreSame(first, icon.Renditions[0].Source);
    }

    [TestMethod]
    public void GeneratedDefaultRejectsDuplicateRenditionSizes()
    {
        var source = Source("icon.png");
        Assert.Throws<ArgumentException>(() =>
            new ApplicationIconDefault(
                source,
                [new ApplicationIconRendition(16, source), new ApplicationIconRendition(16, source)]
            )
        );
    }

    private static ImageSource Source(string path)
    {
        var bytes = new byte[] { 1 };
        return ImageSource.FromAsset(
            new AssetReference(
                new AssetId("Tests", path),
                new string('0', 64),
                bytes.Length,
                AssetFormat.Png,
                () => new MemoryStream(bytes, writable: false),
                new AssetImageMetadata(1, 1)
            )
        );
    }
}
