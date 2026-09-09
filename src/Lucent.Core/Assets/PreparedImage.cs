namespace Lucent.Core;

/// <summary>A prepared portable image owned by an image cache and its leases.</summary>
public abstract class PreparedImage : IDisposable
{
    private int _disposed;

    /// <summary>Initializes a prepared image and its retained byte cost.</summary>
    protected PreparedImage(int width, int height, long byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteCount);
        Width = width;
        Height = height;
        ByteCount = byteCount;
    }

    /// <summary>Gets the prepared pixel width.</summary>
    public int Width { get; }

    /// <summary>Gets the prepared pixel height.</summary>
    public int Height { get; }

    /// <summary>Gets the retained resource byte cost.</summary>
    public long ByteCount { get; }

    /// <summary>Gets whether this resource has been disposed.</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Releases the prepared resource.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            DisposeCore();
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases implementation-owned resources once.</summary>
    protected abstract void DisposeCore();

    /// <summary>Throws when the prepared resource has been disposed.</summary>
    protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);
}

/// <summary>An immutable premultiplied-sRGB RGBA8 image.</summary>
public sealed class RasterImage : PreparedImage
{
    private byte[] _pixels;

    /// <summary>Initializes an immutable premultiplied-sRGB RGBA8 image.</summary>
    public RasterImage(int width, int height, ReadOnlySpan<byte> premultipliedSrgbRgba)
        : base(width, height, RequiredByteCount(width, height))
    {
        if (premultipliedSrgbRgba.Length != ByteCount)
            throw new ArgumentException(
                "Raster pixels must contain exactly four RGBA bytes per pixel.",
                nameof(premultipliedSrgbRgba)
            );
        for (var index = 0; index < premultipliedSrgbRgba.Length; index += 4)
        {
            var alpha = premultipliedSrgbRgba[index + 3];
            if (
                premultipliedSrgbRgba[index] > alpha
                || premultipliedSrgbRgba[index + 1] > alpha
                || premultipliedSrgbRgba[index + 2] > alpha
            )
                throw new ArgumentException(
                    "Raster RGB channels must be premultiplied by alpha.",
                    nameof(premultipliedSrgbRgba)
                );
        }
        _pixels = premultipliedSrgbRgba.ToArray();
    }

    /// <summary>Gets the immutable premultiplied-sRGB RGBA8 pixels.</summary>
    public ReadOnlyMemory<byte> Pixels
    {
        get
        {
            ThrowIfDisposed();
            return _pixels;
        }
    }

    /// <inheritdoc />
    protected override void DisposeCore() => Interlocked.Exchange(ref _pixels, []);

    private static long RequiredByteCount(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        return checked((long)width * height * 4);
    }
}
