namespace Lucent.Core;

/// <summary>How image content fits its arranged content box.</summary>
public enum ImageFit
{
    /// <summary>Preserves aspect ratio and shows the entire image.</summary>
    Contain,

    /// <summary>Preserves aspect ratio and crops centrally to fill the box.</summary>
    Cover,

    /// <summary>Scales each axis to fill the box.</summary>
    Fill,

    /// <summary>Centers the intrinsic logical size, clipping any overflow.</summary>
    None,
}

/// <summary>Whether an image preserves authored colors or uses its alpha as a tint mask.</summary>
public enum ImageColorMode
{
    /// <summary>Preserves source color and alpha.</summary>
    Source,

    /// <summary>Uses inherited TextColor while preserving source alpha.</summary>
    Monochrome,
}

/// <summary>Style-driven image placement and color treatment.</summary>
public static class ImageProperties
{
    /// <summary>Sets how artwork fits its content box; defaults to Contain.</summary>
    public static readonly Property<ImageFit> Fit = new("image-fit", ImageFit.Contain);

    /// <summary>Sets source or monochrome rendering; Image defaults to Source and Icon to Monochrome.</summary>
    public static readonly Property<ImageColorMode> ColorMode = new(
        "image-color-mode",
        ImageColorMode.Source
    );

    /// <summary>Scales artwork around its center and contributes to raster rendition selection.</summary>
    public static readonly Property<float> ImageZoom = new("image-zoom", 1);
    internal static readonly Property<ImageSource?> Source = new("image-source", null);
}

/// <summary>A prepared image operation. The enclosing retained scene owns the resource lease.</summary>
public sealed class ImageSceneNode : SceneNode
{
    internal ImageSceneNode(
        SceneNodeIdentity identity,
        LayoutRect bounds,
        LayoutRect sourceBounds,
        ImageLease lease,
        ImageColorMode colorMode,
        Color tint
    )
        : base(identity, bounds)
    {
        SourceLease = lease;
        Image = lease.Resource;
        SourceBounds = sourceBounds;
        ColorMode = colorMode;
        Tint = tint;
    }

    internal ImageLease SourceLease { get; }

    /// <summary>Prepared portable pixels or an adapter-defined prepared representation.</summary>
    public PreparedImage Image { get; }

    /// <summary>The source crop in prepared-image pixel coordinates.</summary>
    public LayoutRect SourceBounds { get; }

    /// <summary>The authored color policy.</summary>
    public ImageColorMode ColorMode { get; }

    /// <summary>The inherited text color, sampled for this frame.</summary>
    public Color Tint { get; }
}

internal sealed class ImageSlotSceneNode(
    SceneNodeIdentity identity,
    LayoutRect bounds,
    ImageBinding binding,
    ImageFit fit,
    ImageColorMode colorMode,
    float zoom,
    Color tint
) : SceneNode(identity, bounds)
{
    internal ImageBinding Binding => binding;

    internal SceneNode Resolve(float scale) =>
        binding.Resolve(Identity, Bounds, scale, fit, colorMode, zoom, tint);
}
