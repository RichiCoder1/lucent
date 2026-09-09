namespace Lucent.Core;

/// <summary>The declared encoded format of an asset.</summary>
public enum AssetFormat
{
    /// <summary>General bytes with no image decoding contract.</summary>
    Binary,

    /// <summary>A PNG image.</summary>
    Png,

    /// <summary>A JPEG image.</summary>
    Jpeg,

    /// <summary>Static SVG artwork, subject to the image adapter's processing policy.</summary>
    Svg,
}

/// <summary>Immutable packaged-content metadata and an explicit capability to open its bytes.</summary>
/// <remarks>Constructing or retaining a reference performs no IO and owns no decoded/native resource. Generated catalogs validate metadata during the build; custom providers are responsible for declaring matching bytes. Image preparation must verify content and enforce its resource budgets.</remarks>
public sealed class AssetReference
{
    private readonly Func<Stream> openRead;

    /// <summary>Creates a reference whose provider returns a new readable stream positioned at its beginning.</summary>
    /// <param name="id">The stable domain/path identity.</param>
    /// <param name="contentHash">The encoded content's 64-digit SHA-256 hash.</param>
    /// <param name="byteLength">The exact encoded byte length.</param>
    /// <param name="format">The declared encoded format.</param>
    /// <param name="openRead">The provider capability. Ownership of each returned stream transfers to its caller.</param>
    /// <param name="image">Required intrinsic metadata for image formats; absent for binary assets.</param>
    public AssetReference(
        AssetId id,
        string contentHash,
        long byteLength,
        AssetFormat format,
        Func<Stream> openRead,
        AssetImageMetadata? image = null
    )
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(contentHash);
        ArgumentNullException.ThrowIfNull(openRead);
        if (
            contentHash.Length != 64
            || contentHash.Any(character => !char.IsAsciiHexDigit(character))
        )
            throw new ArgumentException(
                "Asset content hashes must be 64 hexadecimal SHA-256 digits.",
                nameof(contentHash)
            );
        ArgumentOutOfRangeException.ThrowIfNegative(byteLength);
        if (!Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(format));
        if (format == AssetFormat.Binary ? image is not null : image is null || byteLength == 0)
            throw new ArgumentException(
                "Image assets require nonempty encoded content and intrinsic metadata; binary assets do not carry image metadata.",
                nameof(image)
            );

        Id = id;
        ContentHash = contentHash.ToLowerInvariant();
        ByteLength = byteLength;
        Format = format;
        Image = image;
        this.openRead = openRead;
    }

    /// <summary>The content's stable semantic identity.</summary>
    public AssetId Id { get; }

    /// <summary>The lowercase SHA-256 identity of this encoded revision.</summary>
    public string ContentHash { get; }

    /// <summary>The exact encoded size, separate from any decoded or native allocation.</summary>
    public long ByteLength { get; }

    /// <summary>The declared encoded format.</summary>
    public AssetFormat Format { get; }

    /// <summary>The media type corresponding to the encoded format.</summary>
    public string MediaType =>
        Format switch
        {
            AssetFormat.Png => "image/png",
            AssetFormat.Jpeg => "image/jpeg",
            AssetFormat.Svg => "image/svg+xml",
            _ => "application/octet-stream",
        };

    /// <summary>Intrinsic image metadata available before IO or preparation.</summary>
    public AssetImageMetadata? Image { get; }

    /// <summary>Opens fresh encoded content. The caller owns and must dispose the returned stream.</summary>
    /// <remarks>This is explicit IO, not a layout/render operation. This method does not hash or decode content. Nonseekable streams are permitted; readers must verify bytes and enforce their own byte and decode budgets.</remarks>
    public Stream OpenRead()
    {
        var stream =
            openRead()
            ?? throw new InvalidOperationException($"Asset provider returned no stream for {Id}.");
        try
        {
            if (!stream.CanRead)
                throw new InvalidOperationException(
                    $"Asset provider returned an unreadable stream for {Id}."
                );
            if (stream.CanSeek && (stream.Position != 0 || stream.Length != ByteLength))
                throw new InvalidDataException(
                    $"Asset provider returned an unexpected position or byte length for {Id}."
                );
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
