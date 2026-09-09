using System.Buffers.Binary;
using Lucent.Core;
using Lucent.Renderer.Skia;

namespace Lucent.Renderer.Skia.Tests;

[TestClass]
public sealed class ArtworkTests
{
    [TestMethod]
    public void IcoContainsTheFiniteWindowsRenditionSetAsPngEntries()
    {
        var renditions = SkiaArtwork
            .ApplicationIconSizes.Select(size => new ArtworkRendition(
                size,
                size,
                Enumerable.Repeat((byte)0, size * size * 4).ToArray()
            ))
            .ToArray();

        var ico = SkiaArtwork.CreateIco(renditions);

        Assert.AreEqual(0, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(0, 2)));
        Assert.AreEqual(1, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2, 2)));
        Assert.AreEqual(
            (ushort)renditions.Length,
            BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4, 2))
        );
        var previousOffset = 6 + renditions.Length * 16;
        for (var index = 0; index < renditions.Length; index++)
        {
            var entry = 6 + index * 16;
            var expectedSize = renditions[index].Width >= 256 ? 0 : renditions[index].Width;
            Assert.AreEqual((byte)expectedSize, ico[entry]);
            Assert.AreEqual((byte)expectedSize, ico[entry + 1]);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(entry + 8, 4));
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(entry + 12, 4));
            Assert.AreEqual((uint)previousOffset, offset);
            Assert.IsTrue(length > 8);
            CollectionAssert.AreEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
                ico.AsSpan(checked((int)offset), 8).ToArray()
            );
            previousOffset = checked((int)(offset + length));
        }
        Assert.AreEqual(ico.Length, previousOffset);
    }

    [TestMethod]
    public void IcoRejectsNonSquareAndDuplicateRenditions()
    {
        var pixels = new byte[4 * 4 * 4];
        var nonSquarePixels = new byte[8 * 4 * 4];
        Assert.Throws<ArgumentException>(() =>
            SkiaArtwork.CreateIco([
                new ArtworkRendition(4, 4, pixels),
                new ArtworkRendition(8, 4, nonSquarePixels),
            ])
        );
        Assert.Throws<ArgumentException>(() =>
            SkiaArtwork.CreateIco([
                new ArtworkRendition(4, 4, pixels),
                new ArtworkRendition(4, 4, pixels),
            ])
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SkiaArtwork.CreateIco([new ArtworkRendition(257, 257, new byte[257 * 257 * 4])])
        );
    }

    [TestMethod]
    public void IcoDecodesPngEntriesAndRejectsOverlappingPayloads()
    {
        var pixels = new byte[16 * 16 * 4];
        pixels[0] = 32;
        pixels[1] = 16;
        pixels[2] = 8;
        pixels[3] = 64;
        var ico = SkiaArtwork.CreateIco([new ArtworkRendition(16, 16, pixels)]);

        var decoded = SkiaArtwork.DecodeIco(ico);
        Assert.AreEqual(1, decoded.Count);
        CollectionAssert.AreEqual(pixels, decoded[0].PremultipliedSrgbRgba.ToArray());

        var second = SkiaArtwork.CreateIco([
            new ArtworkRendition(16, 16, pixels),
            new ArtworkRendition(32, 32, new byte[32 * 32 * 4]),
        ]);
        var firstOffset = BinaryPrimitives.ReadUInt32LittleEndian(second.AsSpan(6 + 12, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(second.AsSpan(6 + 16 + 12, 4), firstOffset);
        Assert.Throws<InvalidDataException>(() => SkiaArtwork.DecodeIco(second));
    }

    [TestMethod]
    public void IcoRejectsDibHeaderLengthThatCannotFitThePayload()
    {
        var ico = new byte[6 + 16 + 40];
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(2, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(4, 2), 1);
        ico[6] = 1;
        ico[7] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(6 + 8, 4), 40);
        BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(6 + 12, 4), 22);
        BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(22, 4), uint.MaxValue);

        Assert.Throws<InvalidDataException>(() => SkiaArtwork.DecodeIco(ico));
    }

    [TestMethod]
    public void ArtworkRenditionsCopyPixelsAndShareVectorPreparation()
    {
        var sourceBytes = new byte[] { 1 };
        var source = ImageSource.FromAsset(
            new AssetReference(
                new AssetId("Tests", "icon.png"),
                new string('0', 64),
                sourceBytes.Length,
                AssetFormat.Png,
                () => new MemoryStream(sourceBytes, writable: false),
                new AssetImageMetadata(1, 1)
            )
        );
        var pixels = new byte[] { 0, 0, 0, 0 };
        var rendition = new ArtworkRendition(1, 1, pixels);
        pixels[0] = 255;
        Assert.AreEqual((byte)0, rendition.PremultipliedSrgbRgba.Span[0]);

        var preparer = new CountingPreparer();
        var output = SkiaArtwork.PrepareRenditions(
            source,
            [16, 24, 32],
            preparer,
            new ImageLoadLimits()
        );
        Assert.AreEqual(1, preparer.PreparationCount);
        Assert.AreEqual(3, output.Count);
    }

    private sealed class CountingPreparer : IImagePreparer
    {
        public int PreparationCount { get; private set; }

        public ImageRendition GetCacheRendition(ImageSource source, ImageRendition requested) =>
            new(1, 1);

        public ValueTask<PreparedImage> PrepareAsync(
            ImagePreparationRequest request,
            CancellationToken cancellationToken
        )
        {
            PreparationCount++;
            return ValueTask.FromResult<PreparedImage>(new RasterImage(1, 1, new byte[4]));
        }
    }
}
