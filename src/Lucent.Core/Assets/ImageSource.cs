namespace Lucent.Core;

/// <summary>Intrinsic desired image size, independent from decode resolution and arranged size.</summary>
public sealed record AssetImageMetadata
{
    /// <summary>Creates orientation-corrected logical dimensions and optional viewport-relative SVG axes.</summary>
    /// <param name="width">Desired logical width before arrangement.</param>
    /// <param name="height">Desired logical height before arrangement.</param>
    /// <param name="density">Declared raster pixels per logical unit; defaults to one.</param>
    /// <param name="relativeWidth">An optional SVG width fraction of the arranged content viewport.</param>
    /// <param name="relativeHeight">An optional SVG height fraction of the arranged content viewport.</param>
    public AssetImageMetadata(
        float width,
        float height,
        float density = 1,
        float? relativeWidth = null,
        float? relativeHeight = null
    )
    {
        RequirePositiveFinite(width, nameof(width));
        RequirePositiveFinite(height, nameof(height));
        RequirePositiveFinite(density, nameof(density));
        if (relativeWidth is { } rw && (!float.IsFinite(rw) || rw < 0))
            throw new ArgumentOutOfRangeException(nameof(relativeWidth));
        if (relativeHeight is { } rh && (!float.IsFinite(rh) || rh < 0))
            throw new ArgumentOutOfRangeException(nameof(relativeHeight));
        Width = width;
        Height = height;
        Density = density;
        RelativeWidth = relativeWidth;
        RelativeHeight = relativeHeight;
    }

    /// <summary>The desired logical width; not a raster rendition width.</summary>
    public float Width { get; }

    /// <summary>The desired logical height; not a raster rendition height.</summary>
    public float Height { get; }

    /// <summary>Declared raster pixels per logical unit.</summary>
    public float Density { get; }

    /// <summary>An optional viewport-relative SVG width fraction.</summary>
    public float? RelativeWidth { get; }

    /// <summary>An optional viewport-relative SVG height fraction.</summary>
    public float? RelativeHeight { get; }

    private static void RequirePositiveFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Image dimensions and density must be finite and positive."
            );
    }
}

/// <summary>A reusable image description, separate from a mounted component or prepared resource.</summary>
public sealed class ImageSource
{
    private ImageSource(AssetReference asset)
    {
        PackagedAsset = asset;
        Metadata = asset.Image!;
    }

    /// <summary>The declared asset when the source is packaged content; nonpackaged representations need not provide one.</summary>
    public AssetReference? PackagedAsset { get; }

    /// <summary>The image's intrinsic metadata, available without opening its content.</summary>
    public AssetImageMetadata Metadata { get; }

    /// <summary>Describes a packaged image without opening, decoding or retaining native content.</summary>
    public static ImageSource FromAsset(AssetReference asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Image is null)
            throw new ArgumentException(
                "A binary asset cannot be used as an image source.",
                nameof(asset)
            );
        return new ImageSource(asset);
    }
}
