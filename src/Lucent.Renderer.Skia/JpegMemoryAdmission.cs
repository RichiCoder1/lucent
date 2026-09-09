using SkiaSharp;

namespace Lucent.Renderer.Skia;

internal enum JpegMemoryProfile
{
    Unknown,
    BaselineRgb,
    ProgressiveRgb,
}

internal static class JpegMemoryAdmission
{
    private const int JpegSofHeaderBytes = 8;
    private const int JpegComponentBytes = 3;
    private const long JpegCodecScratchBytesPerPixel = 8;

    // This is the pinned libjpeg/Skia strip-buffer estimate for a common color JPEG.
    private const long BaselineStripBytesPerSourceWidth = 34;

    // The pinned libjpeg progressive coefficient estimate is 6 bytes per source pixel in
    // the worst 1x1-sampling case. Keep the existing fixed allowance in SkiaImagePreparer.
    private const long ProgressiveCoefficientBytesPerPixel = 6;

    internal static long EstimateScratchBytes(ReadOnlySpan<byte> encoded, SKImageInfo sourceInfo)
    {
        var pixels = checked((long)sourceInfo.Width * sourceInfo.Height);
        return TryReadProfile(encoded, sourceInfo, out var profile)
            ? profile switch
            {
                JpegMemoryProfile.BaselineRgb => checked(
                    BaselineStripBytesPerSourceWidth * sourceInfo.Width
                ),
                JpegMemoryProfile.ProgressiveRgb => checked(
                    ProgressiveCoefficientBytesPerPixel * pixels
                    + BaselineStripBytesPerSourceWidth * sourceInfo.Width
                ),
                _ => checked(JpegCodecScratchBytesPerPixel * pixels),
            }
            : checked(JpegCodecScratchBytesPerPixel * pixels);
    }

    internal static JpegMemoryProfile ReadProfile(
        ReadOnlySpan<byte> encoded,
        SKImageInfo sourceInfo
    ) => TryReadProfile(encoded, sourceInfo, out var profile) ? profile : JpegMemoryProfile.Unknown;

    private static bool TryReadProfile(
        ReadOnlySpan<byte> encoded,
        SKImageInfo sourceInfo,
        out JpegMemoryProfile profile
    )
    {
        profile = JpegMemoryProfile.Unknown;
        if (sourceInfo.Width <= 0 || sourceInfo.Height <= 0 || encoded.Length < 4)
            return false;
        if (encoded[0] != 0xFF || encoded[1] != 0xD8)
            return false;

        var offset = 2;
        var foundSof = false;
        Span<byte> componentIds = stackalloc byte[3];
        while (offset <= encoded.Length - 2)
        {
            if (encoded[offset++] != 0xFF)
                return false;
            while (offset < encoded.Length && encoded[offset] == 0xFF)
                offset++;
            if (offset >= encoded.Length)
                return false;

            var marker = encoded[offset++];
            if (marker == 0)
                return false;
            if (marker == 0xDA)
            {
                if (!foundSof || offset > encoded.Length - 2)
                    return false;
                var scanLength = ReadBigEndianUInt16(encoded, offset);
                if (
                    scanLength < 8
                    || scanLength > encoded.Length - offset
                    || profile == JpegMemoryProfile.Unknown
                )
                    return false;
                var scan = encoded.Slice(offset, scanLength);
                var scanComponents = scan[2];
                if (scanComponents is < 1 or > 3 || scanLength != 6 + 2 * scanComponents)
                    return false;
                var seen = 0;
                for (var index = 0; index < scanComponents; index++)
                {
                    var componentIndex = componentIds.IndexOf(scan[3 + index * 2]);
                    if (componentIndex < 0 || (seen & (1 << componentIndex)) != 0)
                        return false;
                    seen |= 1 << componentIndex;
                }
                // Sequential JPEG can also have multiple scans. libjpeg allocates the full
                // coefficient buffer when the first scan omits a component, even with SOF0.
                if (
                    profile == JpegMemoryProfile.BaselineRgb
                    && (scanComponents != 3 || scan[^3] != 0 || scan[^2] != 63 || scan[^1] != 0)
                )
                    return false;
                return ContainsEndOfImage(encoded, offset + scanLength);
            }
            if (marker == 0xD9 || marker is >= 0xD0 and <= 0xD7)
                return false;
            if (offset > encoded.Length - 2)
                return false;

            var length = ReadBigEndianUInt16(encoded, offset);
            if (length < 2 || length > encoded.Length - offset)
                return false;

            if (IsSofMarker(marker))
            {
                if (foundSof || length < JpegSofHeaderBytes)
                    return false;
                foundSof = true;
                profile = ReadSofProfile(marker, encoded[offset..], length, sourceInfo);
                if (profile == JpegMemoryProfile.Unknown)
                    return false;
                for (var index = 0; index < componentIds.Length; index++)
                    componentIds[index] = encoded[offset + 8 + index * 3];
                if (
                    componentIds[0] == componentIds[1]
                    || componentIds[0] == componentIds[2]
                    || componentIds[1] == componentIds[2]
                )
                    return false;
            }

            offset += length;
        }

        return false;
    }

    private static bool ContainsEndOfImage(ReadOnlySpan<byte> encoded, int start)
    {
        for (var index = start; index <= encoded.Length - 2; index++)
            if (encoded[index] == 0xFF && encoded[index + 1] == 0xD9)
                return true;
        return false;
    }

    private static JpegMemoryProfile ReadSofProfile(
        byte marker,
        ReadOnlySpan<byte> segment,
        int length,
        SKImageInfo sourceInfo
    )
    {
        if (length < JpegSofHeaderBytes || segment.Length < length)
            return JpegMemoryProfile.Unknown;

        var precision = segment[2];
        var height = ReadBigEndianUInt16(segment, 3);
        var width = ReadBigEndianUInt16(segment, 5);
        var componentCount = segment[7];
        if (
            precision != 8
            || width != sourceInfo.Width
            || height != sourceInfo.Height
            || componentCount != 3
            || length != JpegSofHeaderBytes + JpegComponentBytes * componentCount
        )
            return JpegMemoryProfile.Unknown;

        var firstSampling = segment[9];
        var secondSampling = segment[12];
        var thirdSampling = segment[15];
        if (
            firstSampling is not (0x11 or 0x21 or 0x22)
            || secondSampling != 0x11
            || thirdSampling != 0x11
        )
            return JpegMemoryProfile.Unknown;

        return marker switch
        {
            0xC0 => JpegMemoryProfile.BaselineRgb,
            0xC2 => JpegMemoryProfile.ProgressiveRgb,
            _ => JpegMemoryProfile.Unknown,
        };
    }

    private static bool IsSofMarker(byte marker) =>
        marker
            is 0xC0
                or 0xC1
                or 0xC2
                or 0xC3
                or 0xC5
                or 0xC6
                or 0xC7
                or 0xC9
                or 0xCA
                or 0xCB
                or 0xCD
                or 0xCE
                or 0xCF;

    private static int ReadBigEndianUInt16(ReadOnlySpan<byte> bytes, int offset) =>
        (bytes[offset] << 8) | bytes[offset + 1];
}
