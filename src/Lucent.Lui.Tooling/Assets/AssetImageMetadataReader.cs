using System.Buffers.Binary;
using System.Globalization;
using System.Xml;

namespace Lucent.Lui.Tooling.Assets;

internal readonly record struct AssetImageMetadataResult(
    double Width,
    double Height,
    double Density,
    double? RelativeWidth = null,
    double? RelativeHeight = null
);

/// <summary>Reads bounded header metadata without decoding pixels or resolving external SVG content.</summary>
internal static class AssetImageMetadataReader
{
    public static AssetImageMetadataResult Read(
        string format,
        byte[] bytes,
        string density,
        string source
    )
    {
        try
        {
            var result = format switch
            {
                "Png" => ReadPng(bytes),
                "Jpeg" => ReadJpeg(bytes),
                "Svg" => ReadSvg(bytes),
                _ => throw new InvalidDataException("Unsupported image format."),
            };
            if (format != "Svg")
            {
                var scale = string.IsNullOrWhiteSpace(density) ? 1 : Number(density, "Density");
                RequirePositiveFloat(scale, "Density");
                result = result with
                {
                    Width = result.Width / scale,
                    Height = result.Height / scale,
                    Density = scale,
                };
            }
            else if (!string.IsNullOrWhiteSpace(density))
                throw new InvalidDataException(
                    "Density applies only to raster images; size SVG using its root dimensions or viewBox."
                );

            RequirePositiveFloat(result.Width, "intrinsic width");
            RequirePositiveFloat(result.Height, "intrinsic height");
            return result;
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException)
        {
            throw new InvalidDataException(
                $"Asset '{source}' has invalid image metadata: {exception.Message}",
                exception
            );
        }
    }

    private static AssetImageMetadataResult ReadPng(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (
            bytes.Length < 33
            || !bytes[..8].SequenceEqual(signature)
            || BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(8, 4)) != 13
            || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8)
        )
            throw new InvalidDataException("Expected a PNG signature and complete IHDR chunk.");
        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue)
            throw new InvalidDataException("PNG dimensions must be positive 31-bit integers.");
        var orientation = 1;
        var hasExif = false;
        var offset = 33;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 12)
                throw new InvalidDataException("Truncated PNG chunk.");
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            if (length > bytes.Length - offset - 12)
                throw new InvalidDataException("PNG chunk extends beyond its content.");
            if (bytes.Slice(offset + 4, 4).SequenceEqual("IEND"u8))
                break;
            if (bytes.Slice(offset + 4, 4).SequenceEqual("eXIf"u8))
            {
                if (hasExif)
                    throw new InvalidDataException("PNG contains more than one EXIF chunk.");
                orientation = ReadOrientation(bytes.Slice(offset + 8, (int)length));
                hasExif = true;
            }
            offset += (int)length + 12;
        }
        return orientation >= 5 ? new(height, width, 1) : new(width, height, 1);
    }

    private static AssetImageMetadataResult ReadJpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8)
            throw new InvalidDataException("Expected a JPEG start marker.");
        var offset = 2;
        var orientation = 1;
        int width = 0,
            height = 0;
        while (offset < bytes.Length)
        {
            if (bytes[offset++] != 0xff)
                throw new InvalidDataException("Expected a JPEG segment marker.");
            while (offset < bytes.Length && bytes[offset] == 0xff)
                offset++;
            if (offset >= bytes.Length)
                break;
            var marker = bytes[offset++];
            if (marker is 0xda or 0xd9)
                break; // Header metadata precedes the entropy-coded image scan.
            if (marker is 0x01 or >= 0xd0 and <= 0xd8)
                continue;
            if (bytes.Length - offset < 2)
                throw new InvalidDataException("Truncated JPEG segment length.");
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            if (length < 2 || length > bytes.Length - offset)
                throw new InvalidDataException("JPEG segment extends beyond its content.");
            var segment = bytes.Slice(offset + 2, length - 2);
            if (marker == 0xe1 && segment.StartsWith("Exif\0\0"u8))
                orientation = ReadOrientation(segment[6..]);
            if (
                marker
                is >= 0xc0
                    and <= 0xc3
                    or >= 0xc5
                    and <= 0xc7
                    or >= 0xc9
                    and <= 0xcb
                    or >= 0xcd
                    and <= 0xcf
            )
            {
                if (segment.Length < 6)
                    throw new InvalidDataException("Truncated JPEG frame header.");
                height = BinaryPrimitives.ReadUInt16BigEndian(segment.Slice(1, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(segment.Slice(3, 2));
            }
            offset += length;
        }
        if (width == 0 || height == 0)
            throw new InvalidDataException("JPEG has no positive frame dimensions.");
        return orientation >= 5 ? new(height, width, 1) : new(width, height, 1);
    }

    // TIFF IFD0 contains the main image orientation. Do not follow thumbnail or arbitrary IFD chains.
    private static int ReadOrientation(ReadOnlySpan<byte> tiff)
    {
        if (
            tiff.Length < 8
            || !(tiff[..2].SequenceEqual("II"u8) || tiff[..2].SequenceEqual("MM"u8))
        )
            throw new InvalidDataException("EXIF has no valid TIFF header.");
        var little = tiff[0] == 'I';
        if (UInt16(tiff.Slice(2, 2), little) != 42)
            throw new InvalidDataException("EXIF has an invalid TIFF marker.");
        var ifdOffset = UInt32(tiff.Slice(4, 4), little);
        if (ifdOffset < 8 || ifdOffset > tiff.Length - 2)
            throw new InvalidDataException("EXIF IFD0 lies outside its segment.");
        var ifd = tiff[(int)ifdOffset..];
        var count = UInt16(ifd[..2], little);
        if ((long)count * 12 + 6 > ifd.Length)
            throw new InvalidDataException("EXIF IFD0 entries are truncated.");
        for (var index = 0; index < count; index++)
        {
            var entry = ifd.Slice(2 + index * 12, 12);
            if (UInt16(entry[..2], little) != 0x0112)
                continue;
            if (UInt16(entry.Slice(2, 2), little) != 3 || UInt32(entry.Slice(4, 4), little) != 1)
                throw new InvalidDataException("EXIF orientation must be a single SHORT value.");
            var value = UInt16(entry.Slice(8, 2), little);
            if (value is < 1 or > 8)
                throw new InvalidDataException("EXIF orientation must be between 1 and 8.");
            return value;
        }
        return 1;
    }

    private static ushort UInt16(ReadOnlySpan<byte> bytes, bool little) =>
        little
            ? BinaryPrimitives.ReadUInt16LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt16BigEndian(bytes);

    private static uint UInt32(ReadOnlySpan<byte> bytes, bool little) =>
        little
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt32BigEndian(bytes);

    private static AssetImageMetadataResult ReadSvg(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false);
        using var reader = XmlReader.Create(
            stream,
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 4 * 1024 * 1024,
            }
        );
        if (
            reader.MoveToContent() != XmlNodeType.Element
            || reader.LocalName != "svg"
            || reader.NamespaceURI is not ("" or "http://www.w3.org/2000/svg")
        )
            throw new InvalidDataException("Expected an SVG root element.");
        var (width, relativeWidth) = SvgLength(reader.GetAttribute("width"), "width");
        var (height, relativeHeight) = SvgLength(reader.GetAttribute("height"), "height");
        double? aspect = null;
        if (reader.GetAttribute("viewBox") is { } viewBox)
        {
            var fields = viewBox.Split(
                [' ', '\t', '\r', '\n', ','],
                StringSplitOptions.RemoveEmptyEntries
            );
            if (fields.Length != 4)
                throw new InvalidDataException("SVG viewBox requires four finite numbers.");
            var values = fields.Select(field => Number(field, "viewBox")).ToArray();
            if (values[2] <= 0 || values[3] <= 0)
                throw new InvalidDataException("SVG viewBox width and height must be positive.");
            aspect = values[2] / values[3];
        }
        if (
            width is null
            && height is null
            && aspect is null
            && relativeWidth is null
            && relativeHeight is null
        )
            throw new InvalidDataException(
                "SVG requires definite root dimensions or a viewBox for intrinsic size."
            );
        if (width is null && height is { } h && aspect is { } a)
            width = h * a;
        if (height is null && width is { } w && aspect is { } b)
            height = w / b;
        width ??= 300;
        height ??= aspect is { } ratio ? width / ratio : 150;
        return new(width.Value, height.Value, 1, relativeWidth, relativeHeight);
    }

    private static (double? Definite, double? Relative) SvgLength(string? text, string axis)
    {
        if (text is null || text.Trim() == "auto")
            return (null, null);
        var value = text.Trim();
        if (value.EndsWith('%'))
        {
            var relative = Number(value[..^1], axis) / 100;
            if (
                relative < 0
                || !float.IsFinite((float)relative)
                || relative > 0 && (float)relative == 0
            )
                throw new InvalidDataException(
                    $"SVG {axis} percentage is outside the supported finite range."
                );
            return (null, relative);
        }
        var multiplier = 1d;
        foreach (
            var (unit, scale) in new (string, double)[]
            {
                ("px", 1),
                ("pt", 96d / 72),
                ("pc", 16),
                ("in", 96),
                ("cm", 96d / 2.54),
                ("mm", 96d / 25.4),
                ("Q", 96d / 101.6),
            }
        )
        {
            if (!value.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
                continue;
            value = value[..^unit.Length];
            multiplier = scale;
            break;
        }
        var number =
            Number(value, $"SVG {axis} (use a number, absolute unit, or percentage)") * multiplier;
        RequirePositiveFloat(number, $"SVG {axis}");
        return (number, null);
    }

    private static double Number(string value, string name) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && double.IsFinite(number)
            ? number
            : throw new InvalidDataException($"{name} must be a finite number; got '{value}'.");

    private static void RequirePositiveFloat(double value, string name)
    {
        if (!double.IsFinite(value) || !float.IsFinite((float)value) || (float)value <= 0)
            throw new InvalidDataException(
                $"{name} must fit a finite positive single-precision value."
            );
    }
}
