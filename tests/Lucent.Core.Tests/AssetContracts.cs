using System.Security.Cryptography;
using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class AssetContracts
{
    [TestMethod]
    public void IdentityNormalizesSeparatorsWithoutLosingOrdinalDomainAndPathIdentity()
    {
        var id = new AssetId("Example.Library", "images\\Brand.png");
        Assert.AreEqual(new AssetId("Example.Library", "images/Brand.png"), id);
        Assert.AreNotEqual(new AssetId("Example.Library", "images/brand.png"), id);
        Assert.AreNotEqual(new AssetId("example.library", "images/Brand.png"), id);
        Assert.AreEqual("asset://Example.Library/images/Brand.png", id.ToString());
        foreach (
            var path in new[]
            {
                "/a.png",
                "C:\\a.png",
                "../a.png",
                "a/../b",
                "a//b",
                "a/./b",
                "a/",
                "a/ b",
                "a/\0b",
            }
        )
            Assert.ThrowsExactly<ArgumentException>(() => new AssetId("Example", path), path);
        Assert.ThrowsExactly<ArgumentException>(() => new AssetId("../Example", "a.png"));
    }

    [TestMethod]
    public void ReferencesAndImageDescriptionsDoNotOpenContentAndEachReaderOwnsItsStream()
    {
        byte[] bytes = [1, 2, 3, 4];
        var opened = 0;
        var asset = new AssetReference(
            new AssetId("Example", "image.png"),
            Convert.ToHexString(SHA256.HashData(bytes)),
            bytes.Length,
            AssetFormat.Png,
            () =>
            {
                opened++;
                return new MemoryStream(bytes, writable: false);
            },
            new AssetImageMetadata(20, 10, 2)
        );
        var source = ImageSource.FromAsset(asset);
        Assert.AreEqual(0, opened);
        Assert.AreSame(asset, source.PackagedAsset);
        Assert.AreEqual(20, source.Metadata.Width);
        Assert.AreEqual(2, source.Metadata.Density);
        Assert.AreEqual("image/png", asset.MediaType);
        Assert.AreEqual(asset.ContentHash.ToLowerInvariant(), asset.ContentHash);

        using var first = asset.OpenRead();
        using var second = asset.OpenRead();
        Assert.AreEqual(2, opened);
        Assert.AreNotSame(first, second);
        Assert.AreEqual(1, first.ReadByte());
        Assert.AreEqual(0, second.Position);
        first.Dispose();
        Assert.AreEqual(
            1,
            second.ReadByte(),
            "One consumer must not dispose another consumer's stream."
        );
    }

    [TestMethod]
    public void BinaryAssetsRemainExplicitBytesAndInvalidImageMetadataIsRejected()
    {
        var binary = new AssetReference(
            new AssetId("Example", "data.bin"),
            new string('a', 64),
            0,
            AssetFormat.Binary,
            () => new MemoryStream()
        );
        Assert.AreEqual("application/octet-stream", binary.MediaType);
        Assert.ThrowsExactly<ArgumentException>(() => ImageSource.FromAsset(binary));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new AssetReference(binary.Id, "wrong", 1, AssetFormat.Binary, () => new MemoryStream())
        );
        Assert.ThrowsExactly<ArgumentException>(() =>
            new AssetReference(
                binary.Id,
                binary.ContentHash,
                1,
                AssetFormat.Png,
                () => new MemoryStream()
            )
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new AssetImageMetadata(float.NaN, 1)
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AssetImageMetadata(1, 1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new AssetImageMetadata(1, 1, relativeWidth: float.PositiveInfinity)
        );
        var svg = new AssetImageMetadata(300, 150, relativeWidth: 1, relativeHeight: .5f);
        Assert.AreEqual(1, svg.RelativeWidth);
        Assert.AreEqual(.5f, svg.RelativeHeight);
    }

    [TestMethod]
    public void InvalidProviderStreamsAreClosedAndOriginalProviderFailuresRemainVisible()
    {
        var stream = new MemoryStream(new byte[4], writable: false);
        var asset = new AssetReference(
            new AssetId("Example", "data.bin"),
            new string('a', 64),
            3,
            AssetFormat.Binary,
            () => stream
        );
        Assert.ThrowsExactly<InvalidDataException>(() => asset.OpenRead());
        Assert.IsFalse(
            stream.CanRead,
            "A provider contract failure must dispose the transferred stream."
        );
        var original = new IOException("provider failure");
        var failing = new AssetReference(
            asset.Id,
            asset.ContentHash,
            3,
            AssetFormat.Binary,
            () => throw original
        );
        Assert.AreSame(original, Assert.ThrowsExactly<IOException>(() => failing.OpenRead()));
    }
}
