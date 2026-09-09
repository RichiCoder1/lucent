namespace Lucent.Core;

internal sealed class ImageBinding : IDisposable
{
    private readonly Element _element;
    private readonly ImageCache _cache;
    private ImageSource _source;
    private ImageLoadHandle? _handle;
    private ImageRendition? _requested;
    private ImageLease? _lease;
    private bool _disposed;

    internal ImageBinding(Element element, ImageSource source)
    {
        _element = element;
        _source = source;
        _cache =
            element.Composition.Images
            ?? throw new InvalidOperationException(
                "Configure image preparation with Composition.ConfigureImages before mounting Image or Icon. Windows hosts configure the Skia adapter automatically."
            );
    }

    internal void SetSource(ImageSource source)
    {
        if (ReferenceEquals(_source, source))
            return;
        var sameContent =
            _source.PackagedAsset is { } previous
            && source.PackagedAsset is { } next
            && previous.ContentHash == next.ContentHash
            && previous.Format == next.Format;
        _source = source;
        if (sameContent)
            return;
        ReleaseHandle();
        _lease?.Dispose();
        _lease = null;
        _element.Composition.InvalidateInteractionVisuals();
    }

    internal void Request(LayoutRect box, float scale, ImageFit fit, float zoom)
    {
        if (_disposed)
            return;
        if (!Enum.IsDefined(fit) || !float.IsFinite(zoom) || zoom <= 0)
            throw new ArgumentException(
                "Image fit must be defined and ImageZoom must be finite and positive."
            );
        var size = ArtworkSize(box, fit, zoom);
        var request = new ImageRendition(Bucket(size.Width * scale), Bucket(size.Height * scale));
        if (
            _requested is { } prior
            && (
                prior == request
                || prior.PixelWidth >= request.PixelWidth
                    && prior.PixelHeight >= request.PixelHeight
                    && _handle?.Status is ImageLoadStatus.Loading or ImageLoadStatus.Ready
            )
        )
            return;
        ReleaseHandle();
        _requested = request;
        _handle = _cache.Acquire(_element.Scope, _source, request);
        _handle.Changed += Changed;
        UpdateLease();
    }

    internal SceneNode Resolve(
        SceneNodeIdentity identity,
        LayoutRect box,
        float scale,
        ImageFit fit,
        ImageColorMode colorMode,
        float zoom,
        Color tint
    )
    {
        Request(box, scale, fit, zoom);
        if (!Enum.IsDefined(colorMode))
            throw new ArgumentOutOfRangeException(nameof(colorMode));
        if (_lease is null)
            return new PaintSceneNode(identity, box, Brush.Solid(new Color(128, 128, 128, 24)));
        var size = ArtworkSize(box, fit, zoom);
        var left = box.X + (box.Width - size.Width) / 2;
        var top = box.Y + (box.Height - size.Height) / 2;
        var x = Math.Max(box.X, left);
        var y = Math.Max(box.Y, top);
        var right = Math.Min(box.X + box.Width, left + size.Width);
        var bottom = Math.Min(box.Y + box.Height, top + size.Height);
        var width = Math.Max(0, right - x);
        var height = Math.Max(0, bottom - y);
        var image = _lease.Resource;
        var crop =
            size.Width > 0 && size.Height > 0
                ? new LayoutRect(
                    (x - left) / size.Width * image.Width,
                    (y - top) / size.Height * image.Height,
                    width / size.Width * image.Width,
                    height / size.Height * image.Height
                )
                : default;
        return new ImageSceneNode(
            identity,
            new(x, y, width, height),
            crop,
            _lease,
            colorMode,
            tint
        );
    }

    private (float Width, float Height) ArtworkSize(LayoutRect box, ImageFit fit, float zoom)
    {
        var width = _source.Metadata.Width;
        var height = _source.Metadata.Height;
        var ratio = fit switch
        {
            ImageFit.Contain => Math.Min(box.Width / width, box.Height / height),
            ImageFit.Cover => Math.Max(box.Width / width, box.Height / height),
            _ => 1f,
        };
        var result =
            fit == ImageFit.Fill
                ? (box.Width * zoom, box.Height * zoom)
                : (width * ratio * zoom, height * ratio * zoom);
        if (!float.IsFinite(result.Item1) || !float.IsFinite(result.Item2))
            throw new ArgumentOutOfRangeException(
                nameof(zoom),
                "ImageZoom produces nonfinite image geometry."
            );
        return result;
    }

    private static int Bucket(float pixels) =>
        (int)Math.Clamp(Math.Ceiling(pixels / 64d) * 64d, 64, int.MaxValue);

    private void Changed()
    {
        if (_disposed)
            return;
        UpdateLease();
        _element.Composition.InvalidateInteractionVisuals();
    }

    private void UpdateLease()
    {
        if (_handle?.AcquireLease() is not { } next)
            return;
        var previous = _lease;
        _lease = next;
        previous?.Dispose();
    }

    private void ReleaseHandle()
    {
        if (_handle is not null)
        {
            _handle.Changed -= Changed;
            _handle.Dispose();
        }
        _handle = null;
        _requested = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ReleaseHandle();
        _lease?.Dispose();
        _lease = null;
    }
}
