using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Lucent.Core;
using SkiaSharp;

namespace Lucent.Renderer.Skia;

/// <summary>Prepared transparent artwork used by host surfaces and executable icon generation.</summary>
public sealed class ArtworkRendition
{
    /// <summary>Initializes one immutable RGBA8 rendition.</summary>
    public ArtworkRendition(int width, int height, ReadOnlyMemory<byte> premultipliedSrgbRgba)
    {
        Width = width;
        Height = height;
        PremultipliedSrgbRgba = Validate(width, height, premultipliedSrgbRgba);
    }

    /// <summary>Gets the square rendition width.</summary>
    public int Width { get; }

    /// <summary>Gets the square rendition height.</summary>
    public int Height { get; }

    /// <summary>Gets the immutable premultiplied-sRGB RGBA8 pixels.</summary>
    public ReadOnlyMemory<byte> PremultipliedSrgbRgba { get; }

    private static ReadOnlyMemory<byte> Validate(int width, int height, ReadOnlyMemory<byte> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != checked((long)width * height * 4))
            throw new ArgumentException(
                "Artwork must contain exactly four RGBA bytes per pixel.",
                nameof(pixels)
            );
        return pixels.ToArray();
    }
}

/// <summary>Uses the Skia image preparation policy to make deterministic host artwork.</summary>
/// <remarks>
/// This helper is shared by the SDK build tool and the Windows host. The SDK tool references this
/// renderer assembly; the renderer does not reference SDK or platform-host projects. Each rendition
/// is rendered onto a transparent square canvas with aspect-preserving containment, so the same
/// source policy feeds both the generated executable ICO and the live SDL icon.
/// </remarks>
public static partial class SkiaArtwork
{
    /// <summary>The finite Windows icon sizes emitted by the SDK and installed by the host.</summary>
    public static IReadOnlyList<int> ApplicationIconSizes { get; } =
        ImmutableArray.Create(16, 24, 32, 48, 64, 128, 256);

    /// <summary>Prepares all requested square transparent renditions on the calling worker.</summary>
    public static IReadOnlyList<ArtworkRendition> PrepareRenditions(
        ImageSource source,
        IReadOnlyList<int>? sizes = null,
        IImagePreparer? preparer = null,
        ImageLoadLimits? limits = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        sizes ??= ApplicationIconSizes;
        ValidateSizes(sizes);
        preparer ??= new SkiaImagePreparer();
        limits ??= ImageLoadLimits.Default;

        var output = new ArtworkRendition[sizes.Count];
        var preparedByRendition = new Dictionary<(int Width, int Height), PreparedImage>();
        try
        {
            for (var index = 0; index < sizes.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var size = sizes[index];
                var requested = new ImageRendition(size, size);
                var resolved =
                    preparer.GetCacheRendition(source, requested)
                    ?? throw new InvalidOperationException(
                        "The image preparer returned no cache rendition."
                    );
                var key = (resolved.PixelWidth, resolved.PixelHeight);
                if (!preparedByRendition.TryGetValue(key, out var prepared))
                {
                    var request = new ImagePreparationRequest(source, resolved, limits);
                    prepared =
                        preparer
                            .PrepareAsync(request, cancellationToken)
                            .AsTask()
                            .GetAwaiter()
                            .GetResult()
                        ?? throw new InvalidOperationException(
                            "The image preparer returned no prepared image."
                        );
                    preparedByRendition.Add(key, prepared);
                }
                output[index] = new ArtworkRendition(size, size, RenderRgba(prepared, size));
            }
            return output;
        }
        finally
        {
            foreach (var prepared in preparedByRendition.Values)
                prepared.Dispose();
        }
    }

    /// <summary>Prepares the standard renditions and writes a PNG-backed multi-entry ICO.</summary>
    public static byte[] CreateIco(
        ImageSource source,
        IReadOnlyList<int>? sizes = null,
        IImagePreparer? preparer = null,
        ImageLoadLimits? limits = null,
        CancellationToken cancellationToken = default
    )
    {
        var renditions = PrepareRenditions(source, sizes, preparer, limits, cancellationToken);
        return CreateIco(renditions);
    }

    /// <summary>Writes a deterministic ICO containing each supplied square RGBA rendition.</summary>
    public static byte[] CreateIco(IReadOnlyList<ArtworkRendition> renditions)
    {
        ArgumentNullException.ThrowIfNull(renditions);
        if (renditions.Count == 0)
            throw new ArgumentException(
                "At least one artwork rendition is required.",
                nameof(renditions)
            );
        var entries = new (int Size, byte[] Png)[renditions.Count];
        var seen = new HashSet<int>();
        var total = 6 + checked(renditions.Count * 16);
        for (var index = 0; index < renditions.Count; index++)
        {
            var rendition =
                renditions[index]
                ?? throw new ArgumentException("A rendition was null.", nameof(renditions));
            if (rendition.Width != rendition.Height)
                throw new ArgumentException("ICO renditions must be square.", nameof(renditions));
            if (rendition.Width > 256)
                throw new ArgumentOutOfRangeException(
                    nameof(renditions),
                    "ICO rendition sizes cannot exceed 256 pixels."
                );
            if (!seen.Add(rendition.Width))
                throw new ArgumentException(
                    "ICO rendition sizes must be unique.",
                    nameof(renditions)
                );
            var png = EncodePng(rendition);
            entries[index] = (rendition.Width, png);
            total = checked(total + png.Length);
        }

        var result = new byte[total];
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(
            result.AsSpan(4, 2),
            checked((ushort)entries.Length)
        );
        var directoryOffset = 6;
        var dataOffset = 6 + entries.Length * 16;
        for (var index = 0; index < entries.Length; index++)
        {
            var (size, png) = entries[index];
            result[directoryOffset] = size >= 256 ? (byte)0 : checked((byte)size);
            result[directoryOffset + 1] = size >= 256 ? (byte)0 : checked((byte)size);
            result[directoryOffset + 2] = 0;
            result[directoryOffset + 3] = 0;
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(directoryOffset + 4, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(directoryOffset + 6, 2), 32);
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(directoryOffset + 8, 4),
                checked((uint)png.Length)
            );
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(directoryOffset + 12, 4),
                checked((uint)dataOffset)
            );
            png.CopyTo(result, dataOffset);
            directoryOffset += 16;
            dataOffset += png.Length;
        }
        return result;
    }

    private static byte[] RenderRgba(PreparedImage prepared, int size)
    {
        using var colorSpace = SKColorSpace.CreateSrgb();
        var info = new SKImageInfo(
            size,
            size,
            SKColorType.Rgba8888,
            SKAlphaType.Premul,
            colorSpace
        );
        using var surface = SKSurface.Create(info);
        if (surface is null)
            throw new InvalidOperationException("Skia could not create the artwork surface.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        var destination = Contain(prepared.Width, prepared.Height, size);
        var source = new LayoutRect(0, 0, prepared.Width, prepared.Height);
        var target = new LayoutRect(
            destination.Left,
            destination.Top,
            destination.Width,
            destination.Height
        );
        if (prepared is ISkiaPreparedImage vector)
        {
            vector.Draw(canvas, source, target, ImageColorMode.Source, Color.FromRgb(0, 0, 0));
        }
        else if (prepared is RasterImage raster)
        {
            using var image = SKImage.FromPixelCopy(
                new SKImageInfo(
                    raster.Width,
                    raster.Height,
                    SKColorType.Rgba8888,
                    SKAlphaType.Premul
                ),
                raster.Pixels.Span
            );
            if (image is null)
                throw new InvalidOperationException(
                    "Skia could not create a native image from prepared pixels."
                );
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawImage(
                image,
                new SKRect(0, 0, raster.Width, raster.Height),
                ToSkRect(target),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
                paint
            );
        }
        else
        {
            throw new InvalidOperationException(
                $"Skia artwork does not understand prepared image type {prepared.GetType().FullName}."
            );
        }
        canvas.Flush();
        using var snapshot = surface.Snapshot();
        if (snapshot is null)
            throw new InvalidOperationException("Skia could not snapshot the artwork surface.");
        var pixels = GC.AllocateUninitializedArray<byte>(checked(size * size * 4));
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            if (!snapshot.ReadPixels(info, handle.AddrOfPinnedObject(), size * 4, 0, 0))
                throw new InvalidOperationException("Skia could not read the artwork surface.");
        }
        finally
        {
            handle.Free();
        }
        return pixels;
    }

    /// <summary>Encodes one premultiplied RGBA artwork rendition as a PNG.</summary>
    public static byte[] EncodePng(ArtworkRendition rendition)
    {
        using var colorSpace = SKColorSpace.CreateSrgb();
        var info = new SKImageInfo(
            rendition.Width,
            rendition.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul,
            colorSpace
        );
        using var image = SKImage.FromPixelCopy(info, rendition.PremultipliedSrgbRgba.Span);
        if (image is null)
            throw new InvalidOperationException("Skia could not create the artwork image.");
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null)
            throw new InvalidOperationException("Skia could not encode the artwork image.");
        return data.ToArray();
    }

    private static (float Left, float Top, float Width, float Height) Contain(
        int width,
        int height,
        int size
    )
    {
        var scale = Math.Min(size / (float)width, size / (float)height);
        var outputWidth = width * scale;
        var outputHeight = height * scale;
        return ((size - outputWidth) / 2, (size - outputHeight) / 2, outputWidth, outputHeight);
    }

    private static SKRect ToSkRect(LayoutRect value) =>
        new(value.X, value.Y, value.X + value.Width, value.Y + value.Height);

    private static void ValidateSizes(IReadOnlyList<int> sizes)
    {
        if (sizes.Count == 0)
            throw new ArgumentException("At least one artwork size is required.", nameof(sizes));
        var previous = 0;
        foreach (var size in sizes)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
            if (size <= previous)
                throw new ArgumentException(
                    "Artwork sizes must be unique and ascending.",
                    nameof(sizes)
                );
            previous = size;
        }
    }
}
