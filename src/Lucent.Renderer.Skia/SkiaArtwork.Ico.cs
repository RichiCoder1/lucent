using System.Buffers.Binary;
using Lucent.Core;
using SkiaSharp;

namespace Lucent.Renderer.Skia;

public static partial class SkiaArtwork
{
    private const int IcoHeaderBytes = 6;
    private const int IcoEntryBytes = 16;
    private const int MaximumIcoBytes = 64 * 1024 * 1024;

    /// <summary>
    /// Decodes every square optical rendition in a Windows ICO into the shared premultiplied
    /// artwork contract.
    /// </summary>
    /// <remarks>
    /// Both PNG-backed entries and the uncompressed 32-bit DIB form are accepted. Directory ranges,
    /// duplicate sizes, and decoded dimensions are checked before any generated source is used.
    /// The input is copied only for codec ownership; returned pixel buffers are immutable
    /// <see cref="ArtworkRendition"/> values.
    /// </remarks>
    public static IReadOnlyList<ArtworkRendition> DecodeIco(ReadOnlyMemory<byte> encodedIco)
    {
        var bytes = encodedIco.Span;
        if (
            bytes.Length < IcoHeaderBytes
            || bytes.Length > MaximumIcoBytes
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes[..2]) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(2, 2)) != 1
        )
            throw new InvalidDataException("The ICO header is invalid.");

        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(4, 2));
        var directoryBytes = checked(IcoHeaderBytes + count * IcoEntryBytes);
        if (count == 0 || directoryBytes > bytes.Length)
            throw new InvalidDataException("The ICO directory is incomplete.");

        var entries = new IcoEntry[count];
        var sizes = new HashSet<int>();
        var ranges = new List<(uint Start, uint End)>((int)count);
        for (var index = 0; index < count; index++)
        {
            var offset = IcoHeaderBytes + index * IcoEntryBytes;
            var width = bytes[offset] == 0 ? 256 : bytes[offset];
            var height = bytes[offset + 1] == 0 ? 256 : bytes[offset + 1];
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 8, 4));
            var dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 12, 4));
            if (
                width != height
                || !sizes.Add(width)
                || length == 0
                || dataOffset < (uint)directoryBytes
                || dataOffset > (uint)bytes.Length
                || length > (uint)(bytes.Length - (int)dataOffset)
            )
                throw new InvalidDataException("The ICO contains an invalid entry.");

            var end = checked(dataOffset + length);
            entries[index] = new(width, height, dataOffset, length);
            ranges.Add((dataOffset, end));
        }

        ranges.Sort(
            static (left, right) =>
            {
                var comparison = left.Start.CompareTo(right.Start);
                return comparison != 0 ? comparison : left.End.CompareTo(right.End);
            }
        );
        for (var index = 1; index < ranges.Count; index++)
            if (ranges[index].Start < ranges[index - 1].End)
                throw new InvalidDataException("ICO entry payloads overlap.");

        var output = new ArtworkRendition[entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var payload = bytes.Slice((int)entry.Offset, (int)entry.Length);
            output[index] = IsPng(payload)
                ? DecodePng(payload, entry.Width, entry.Height)
                : DecodeDib(payload, entry.Width, entry.Height);
        }
        return Array.AsReadOnly(output);
    }

    private static bool IsPng(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        return bytes.Length >= signature.Length
            && bytes[..signature.Length].SequenceEqual(signature);
    }

    private static ArtworkRendition DecodePng(
        ReadOnlySpan<byte> encoded,
        int expectedWidth,
        int expectedHeight
    )
    {
        using var stream = new MemoryStream(encoded.ToArray(), writable: false);
        using var codec = SKCodec.Create(stream, out var result);
        if (codec is null)
            throw new InvalidDataException($"The ICO PNG entry could not be decoded ({result}).");
        if (codec.Info.Width != expectedWidth || codec.Info.Height != expectedHeight)
            throw new InvalidDataException(
                "The ICO PNG dimensions do not match its directory entry."
            );

        var pixels = GC.AllocateUninitializedArray<byte>(
            checked(expectedWidth * expectedHeight * 4)
        );
        using var colorSpace = SKColorSpace.CreateSrgb();
        var info = new SKImageInfo(
            expectedWidth,
            expectedHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Premul,
            colorSpace
        );
        if (codec.GetPixels(info, pixels) != SKCodecResult.Success)
            throw new InvalidDataException("The ICO PNG entry could not be decoded.");
        return new ArtworkRendition(expectedWidth, expectedHeight, pixels);
    }

    private static ArtworkRendition DecodeDib(
        ReadOnlySpan<byte> dib,
        int expectedWidth,
        int expectedHeight
    )
    {
        if (dib.Length < 40)
            throw new InvalidDataException("The ICO entry is neither PNG nor a supported DIB.");

        var declaredHeaderBytes = BinaryPrimitives.ReadUInt32LittleEndian(dib[..4]);
        if (
            declaredHeaderBytes < 40
            || declaredHeaderBytes > (uint)dib.Length
            || declaredHeaderBytes > (uint)int.MaxValue
        )
            throw new InvalidDataException("The ICO DIB header is truncated.");
        var headerBytes = (int)declaredHeaderBytes;
        var rawWidth = BinaryPrimitives.ReadInt32LittleEndian(dib.Slice(4, 4));
        var rawHeight = BinaryPrimitives.ReadInt32LittleEndian(dib.Slice(8, 4));
        var planes = BinaryPrimitives.ReadUInt16LittleEndian(dib.Slice(12, 2));
        var bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(dib.Slice(14, 2));
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib.Slice(16, 4));
        if (
            rawWidth <= 0
            || rawHeight == 0
            || rawWidth == int.MinValue
            || rawHeight == int.MinValue
            || planes != 1
            || bitsPerPixel != 32
            || compression != 0
        )
            throw new InvalidDataException(
                "Only 32-bit uncompressed ICO DIB entries are supported."
            );

        var width = Math.Abs(rawWidth);
        var totalHeight = Math.Abs(rawHeight);
        if (totalHeight % 2 != 0 || totalHeight / 2 != expectedHeight || width != expectedWidth)
            throw new InvalidDataException(
                "The ICO DIB dimensions do not match its directory entry."
            );

        var pixelOffset = headerBytes;
        var xorStride = checked(width * 4);
        var xorBytes = checked(xorStride * expectedHeight);
        var andStride = checked(((width + 31) / 32) * 4);
        var andBytes = checked(andStride * expectedHeight);
        if (
            pixelOffset > dib.Length
            || xorBytes > dib.Length - pixelOffset
            || andBytes > dib.Length - pixelOffset - xorBytes
        )
            throw new InvalidDataException("The ICO DIB pixel data is truncated.");

        var pixels = GC.AllocateUninitializedArray<byte>(checked(width * expectedHeight * 4));
        var topDown = rawHeight < 0;
        var anyAlpha = false;
        for (var row = 0; row < expectedHeight; row++)
        {
            var sourceRow = topDown ? row : expectedHeight - row - 1;
            var sourceOffset = pixelOffset + sourceRow * xorStride;
            for (var column = 0; column < width; column++)
            {
                var source = sourceOffset + column * 4;
                var alpha = dib[source + 3];
                anyAlpha |= alpha != 0;
                var destination = (row * width + column) * 4;
                pixels[destination] = dib[source + 2];
                pixels[destination + 1] = dib[source + 1];
                pixels[destination + 2] = dib[source];
                pixels[destination + 3] = alpha;
            }
        }

        var andOffset = pixelOffset + xorBytes;
        for (var row = 0; row < expectedHeight; row++)
        {
            var sourceRow = topDown ? row : expectedHeight - row - 1;
            var maskOffset = andOffset + sourceRow * andStride;
            for (var column = 0; column < width; column++)
            {
                var destination = (row * width + column) * 4;
                var alpha = pixels[destination + 3];
                if (!anyAlpha && (dib[maskOffset + column / 8] & (0x80 >> (column % 8))) == 0)
                    alpha = 255;
                pixels[destination + 3] = alpha;
                pixels[destination] = Premultiply(pixels[destination], alpha);
                pixels[destination + 1] = Premultiply(pixels[destination + 1], alpha);
                pixels[destination + 2] = Premultiply(pixels[destination + 2], alpha);
            }
        }
        return new ArtworkRendition(width, expectedHeight, pixels);
    }

    private static byte Premultiply(byte channel, byte alpha) =>
        (byte)((channel * alpha + 127) / 255);

    private readonly record struct IcoEntry(int Width, int Height, uint Offset, uint Length);
}
