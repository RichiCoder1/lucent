using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Lucent.Core;
using SkiaSharp;

namespace Lucent.Renderer.Skia;

/// <summary>
/// Prepares packaged PNG/JPEG rasters and Secure Static SVG vectors.
/// </summary>
/// <remarks>
/// This type deliberately has no renderer reference. A preparation can therefore run on a
/// worker thread, while renderer-native images are created later by <see cref="SkiaSceneRenderer"/>
/// on its owner thread. SVG retains an adapter-owned vector representation and is not permanently
/// rasterized at its first display size.
/// </remarks>
public sealed class SkiaImagePreparer : IImagePreparer
{
    /// <summary>Identifies the finite static-SVG processing contract and pinned renderer versions.</summary>
    public const string SvgPolicyIdentity = "lucent-secure-static-v1/svg.skia-5.2.3/skia-4.151.1";

    private readonly byte[] _svgFont;
    private static readonly ImageRendition VectorRendition = new(1, 1);

    /// <summary>Creates a preparer for rasters and text-free static SVG.</summary>
    public SkiaImagePreparer()
        : this(ReadOnlyMemory<byte>.Empty) { }

    /// <summary>Creates a preparer with one immutable, explicitly supplied SVG font. Text uses the family "Lucent SVG"; no system-font discovery occurs.</summary>
    public SkiaImagePreparer(ReadOnlyMemory<byte> svgFont)
    {
        if (svgFont.Length > 8 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(
                nameof(svgFont),
                "SVG fonts are limited to 8 MiB."
            );
        _svgFont = svgFont.ToArray();
        SvgFontIdentity =
            _svgFont.Length == 0 ? "none" : Convert.ToHexString(SHA256.HashData(_svgFont));
    }

    /// <summary>Gets the fixed font-content identity used by this preparer's cache environment.</summary>
    public string SvgFontIdentity { get; }

    /// <summary>Shares one static vector preparation across output sizes; raster renditions remain size-specific.</summary>
    public ImageRendition GetCacheRendition(ImageSource source, ImageRendition requested) =>
        source.PackagedAsset?.Format == AssetFormat.Svg ? VectorRendition : requested;

    private const int BytesPerPixel = 4;
    private const long MaximumRendererOutputBytes = 64L * 1024 * 1024;

    // The codec owns native scratch memory that is not exposed by SKCodec. Reserve one
    // additional intermediate-sized allowance so a large source cannot bypass the cache-wide
    // temporary budget merely because the managed destination is small.
    private const long CodecScratchAllowance = 256 * 1024;

    /// <summary>Prepares one packaged raster source without accessing the render owner.</summary>
    public ValueTask<PreparedImage> PrepareAsync(
        ImagePreparationRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var asset = request.Source.PackagedAsset;
        if (asset is null)
            throw new ImageLoadException(
                ImageLoadFailureKind.SourceUnavailable,
                "The Skia raster preparer requires a packaged asset reference."
            );
        if (asset.Format is not (AssetFormat.Png or AssetFormat.Jpeg or AssetFormat.Svg))
            throw new ImageLoadException(
                ImageLoadFailureKind.UnsupportedFormat,
                $"The Skia raster preparer does not support {asset.Format}."
            );

        if (asset.Format == AssetFormat.Svg && asset.ByteLength > SecureSvgDocument.MaximumBytes)
            throw SecureSvgDocument.Budget("SVG encoded input exceeds 2 MiB.");
        using var encodedReservation = ReserveEncoded(request, asset.ByteLength);
        byte[] encoded;
        try
        {
            encoded = ReadEncoded(asset, cancellationToken);
        }
        catch (ImageLoadException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            throw new ImageLoadException(
                ImageLoadFailureKind.InvalidData,
                $"The encoded image {asset.Id} could not be read.",
                exception
            );
        }
        catch (IOException exception)
        {
            throw new ImageLoadException(
                ImageLoadFailureKind.SourceUnavailable,
                $"The encoded image source {asset.Id} could not be opened.",
                exception
            );
        }
        VerifyHash(asset, encoded);
        cancellationToken.ThrowIfCancellationRequested();
        if (asset.Format == AssetFormat.Svg)
            return ValueTask.FromResult(
                SkiaSvgImage.Prepare(request, encoded, _svgFont, cancellationToken)
            );

        try
        {
            using var stream = new MemoryStream(encoded, writable: false);
            var codec = SKCodec.Create(stream, out var codecResult);
            if (codec is null)
                throw InvalidCodec(asset, codecResult);
            using (codec)
            {
                ValidateCodecFormat(asset, codec);
                var sourceInfo = codec.Info;
                ValidateSourceDimensions(request, sourceInfo);
                var origin = codec.EncodedOrigin;
                ValidateDeclaredDimensions(asset, sourceInfo, origin);
                var dimensions = TargetDimensions(sourceInfo, origin, request.Rendition);
                var rawTargetWidth = IsQuarterTurn(origin) ? dimensions.Height : dimensions.Width;
                var rawTargetHeight = IsQuarterTurn(origin) ? dimensions.Width : dimensions.Height;
                var sourceScale = Math.Min(
                    1f,
                    Math.Min(
                        rawTargetWidth / (float)sourceInfo.Width,
                        rawTargetHeight / (float)sourceInfo.Height
                    )
                );
                var supported = codec.GetScaledDimensions(sourceScale);
                var rawWidth = Math.Clamp(supported.Width, 1, sourceInfo.Width);
                var rawHeight = Math.Clamp(supported.Height, 1, sourceInfo.Height);
                var orientedWidth = IsQuarterTurn(origin) ? rawHeight : rawWidth;
                var orientedHeight = IsQuarterTurn(origin) ? rawWidth : rawHeight;
                var intermediateRawBytes = RequiredBytes(rawWidth, rawHeight);
                var intermediateOrientedBytes = RequiredBytes(orientedWidth, orientedHeight);
                var outputBytes = RequiredBytes(dimensions.Width, dimensions.Height);
                if (intermediateRawBytes > int.MaxValue || intermediateOrientedBytes > int.MaxValue)
                    throw new ImageLoadException(
                        ImageLoadFailureKind.BudgetDeclined,
                        "The intermediate decoded image exceeds the managed buffer limit."
                    );
                ValidateOutputBudget(request.Limits, outputBytes);

                // RasterImage intentionally copies its input. Reserve the managed raw decode,
                // the oriented intermediate, the final output, that immutable copy, a codec
                // scratch allowance, and a small fixed allowance. Known 8-bit three-component
                // baseline/progressive JPEG headers use the pinned libjpeg/Skia estimates;
                // malformed, CMYK/YCCK, arithmetic and otherwise unknown JPEGs retain the
                // conservative eight bytes per source pixel fallback. PNG keeps one full source
                // RGBA surface because its codec can materialize the original-size image.
                var sourceCodecScratchBytes = Math.Max(
                    intermediateRawBytes,
                    asset.Format == AssetFormat.Jpeg
                        ? JpegMemoryAdmission.EstimateScratchBytes(encoded, sourceInfo)
                        : RequiredBytes(sourceInfo.Width, sourceInfo.Height)
                );
                var temporaryBytes = checked(
                    intermediateRawBytes
                    + intermediateOrientedBytes
                    + outputBytes
                    + outputBytes
                    + sourceCodecScratchBytes
                    + CodecScratchAllowance
                );
                using var temporaryReservation = request.ReserveTemporaryBytes(temporaryBytes);
                cancellationToken.ThrowIfCancellationRequested();

                var raw = GC.AllocateUninitializedArray<byte>(checked((int)intermediateRawBytes));
                using var colorSpace = SKColorSpace.CreateSrgb();
                var info = new SKImageInfo(
                    rawWidth,
                    rawHeight,
                    SKColorType.Rgba8888,
                    SKAlphaType.Premul,
                    colorSpace
                );
                var decodeResult = codec.GetPixels(info, raw);
                if (decodeResult != SKCodecResult.Success)
                    throw new ImageLoadException(
                        ImageLoadFailureKind.InvalidData,
                        $"Skia could not decode {asset.Id}: {decodeResult}."
                    );
                cancellationToken.ThrowIfCancellationRequested();

                var oriented = Orient(
                    raw,
                    rawWidth,
                    rawHeight,
                    origin,
                    orientedWidth,
                    orientedHeight
                );
                var output = Resample(
                    oriented,
                    orientedWidth,
                    orientedHeight,
                    dimensions.Width,
                    dimensions.Height
                );
                ClampPremultiplied(output);
                return ValueTask.FromResult<PreparedImage>(
                    new RasterImage(dimensions.Width, dimensions.Height, output)
                );
            }
        }
        catch (ImageLoadException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            throw new ImageLoadException(
                ImageLoadFailureKind.InvalidData,
                $"The encoded image {asset.Id} could not be read.",
                exception
            );
        }
    }

    private static IDisposable ReserveEncoded(ImagePreparationRequest request, long byteLength)
    {
        if (
            byteLength <= 0
            || byteLength > request.Limits.MaximumEncodedBytes
            || byteLength > int.MaxValue
        )
            throw new ImageLoadException(
                ImageLoadFailureKind.BudgetDeclined,
                "The encoded image exceeds the configured preparation budget."
            );
        return request.ReserveTemporaryBytes(byteLength);
    }

    private static byte[] ReadEncoded(AssetReference asset, CancellationToken cancellationToken)
    {
        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)asset.ByteLength));
        try
        {
            using var stream = asset.OpenRead();
            var offset = 0;
            while (offset < bytes.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = stream.Read(bytes.AsSpan(offset));
                if (read <= 0)
                    throw new InvalidDataException(
                        "The asset stream ended before its declared length."
                    );
                offset = checked(offset + read);
            }

            if (stream.ReadByte() != -1)
                throw new InvalidDataException("The asset stream exceeded its declared length.");
            return bytes;
        }
        catch
        {
            Array.Clear(bytes);
            throw;
        }
    }

    private static void VerifyHash(AssetReference asset, byte[] encoded)
    {
        var actual = Convert.ToHexString(SHA256.HashData(encoded));
        if (!actual.Equals(asset.ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new ImageLoadException(
                ImageLoadFailureKind.InvalidData,
                $"The encoded image {asset.Id} did not match its declared content hash."
            );
    }

    private static void ValidateCodecFormat(AssetReference asset, SKCodec codec)
    {
        var expected =
            asset.Format == AssetFormat.Png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
        if (codec.EncodedFormat != expected)
            throw new ImageLoadException(
                ImageLoadFailureKind.InvalidData,
                $"The asset {asset.Id} declared {asset.Format} but decoded as {codec.EncodedFormat}."
            );
    }

    private static void ValidateSourceDimensions(ImagePreparationRequest request, SKImageInfo info)
    {
        if (info.Width <= 0 || info.Height <= 0)
            throw new ImageLoadException(
                ImageLoadFailureKind.InvalidData,
                "The codec reported nonpositive image dimensions."
            );
        var pixels = checked((long)info.Width * info.Height);
        if (pixels > request.Limits.MaximumSourcePixels)
            throw new ImageLoadException(
                ImageLoadFailureKind.BudgetDeclined,
                "The source image exceeds the configured pixel budget."
            );
    }

    private static (int Width, int Height) TargetDimensions(
        SKImageInfo sourceInfo,
        SKEncodedOrigin origin,
        ImageRendition rendition
    )
    {
        var orientedWidth = IsQuarterTurn(origin) ? sourceInfo.Height : sourceInfo.Width;
        var orientedHeight = IsQuarterTurn(origin) ? sourceInfo.Width : sourceInfo.Height;
        var scale = Math.Min(
            1d,
            Math.Min(
                rendition.PixelWidth / (double)orientedWidth,
                rendition.PixelHeight / (double)orientedHeight
            )
        );
        return (
            Math.Max(1, (int)Math.Round(orientedWidth * scale, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round(orientedHeight * scale, MidpointRounding.AwayFromZero))
        );
    }

    private static void ValidateDeclaredDimensions(
        AssetReference asset,
        SKImageInfo sourceInfo,
        SKEncodedOrigin origin
    )
    {
        var metadata = asset.Image!;
        var declaredWidth = metadata.Width * (double)metadata.Density;
        var declaredHeight = metadata.Height * (double)metadata.Density;
        var actualWidth = IsQuarterTurn(origin) ? sourceInfo.Height : sourceInfo.Width;
        var actualHeight = IsQuarterTurn(origin) ? sourceInfo.Width : sourceInfo.Height;
        if (
            !double.IsFinite(declaredWidth)
            || !double.IsFinite(declaredHeight)
            || Math.Abs(declaredWidth - actualWidth) > 0.01
            || Math.Abs(declaredHeight - actualHeight) > 0.01
        )
            throw new ImageLoadException(
                ImageLoadFailureKind.InvalidData,
                $"The encoded image {asset.Id} dimensions do not match its declared metadata."
            );
    }

    private static void ValidateOutputBudget(ImageLoadLimits limits, long outputBytes)
    {
        if (
            outputBytes > limits.MaximumOutputBytes
            || outputBytes > MaximumRendererOutputBytes
            || outputBytes > int.MaxValue
        )
            throw new ImageLoadException(
                ImageLoadFailureKind.BudgetDeclined,
                "The decoded image exceeds the configured output or renderer budget."
            );
    }

    private static long RequiredBytes(int width, int height) =>
        checked((long)width * height * BytesPerPixel);

    private static bool IsQuarterTurn(SKEncodedOrigin origin) =>
        origin
            is SKEncodedOrigin.LeftTop
                or SKEncodedOrigin.RightTop
                or SKEncodedOrigin.RightBottom
                or SKEncodedOrigin.LeftBottom;

    private static byte[] Orient(
        byte[] raw,
        int rawWidth,
        int rawHeight,
        SKEncodedOrigin origin,
        int outputWidth,
        int outputHeight
    )
    {
        if (
            rawWidth == outputWidth
            && rawHeight == outputHeight
            && (origin is SKEncodedOrigin.Default or SKEncodedOrigin.TopLeft)
        )
            return raw;

        var output = GC.AllocateUninitializedArray<byte>(
            checked(outputWidth * outputHeight * BytesPerPixel)
        );
        for (var y = 0; y < rawHeight; y++)
        {
            for (var x = 0; x < rawWidth; x++)
            {
                var (destinationX, destinationY) = Map(origin, x, y, rawWidth, rawHeight);
                var sourceOffset = checked((y * rawWidth + x) * BytesPerPixel);
                var destinationOffset = checked(
                    (destinationY * outputWidth + destinationX) * BytesPerPixel
                );
                raw.AsSpan(sourceOffset, BytesPerPixel)
                    .CopyTo(output.AsSpan(destinationOffset, BytesPerPixel));
            }
        }
        return output;
    }

    private static byte[] Resample(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        int outputWidth,
        int outputHeight
    )
    {
        if (sourceWidth == outputWidth && sourceHeight == outputHeight)
            return source;
        var output = GC.AllocateUninitializedArray<byte>(
            checked(outputWidth * outputHeight * BytesPerPixel)
        );
        var sourceHandle = GCHandle.Alloc(source, GCHandleType.Pinned);
        var outputHandle = GCHandle.Alloc(output, GCHandleType.Pinned);
        try
        {
            using var colorSpace = SKColorSpace.CreateSrgb();
            var sourceInfo = new SKImageInfo(
                sourceWidth,
                sourceHeight,
                SKColorType.Rgba8888,
                SKAlphaType.Premul,
                colorSpace
            );
            var outputInfo = new SKImageInfo(
                outputWidth,
                outputHeight,
                SKColorType.Rgba8888,
                SKAlphaType.Premul,
                colorSpace
            );
            using var sourcePixmap = new SKPixmap(
                sourceInfo,
                sourceHandle.AddrOfPinnedObject(),
                sourceInfo.RowBytes
            );
            using var outputPixmap = new SKPixmap(
                outputInfo,
                outputHandle.AddrOfPinnedObject(),
                outputInfo.RowBytes
            );

            // Mitchell-Netravali cubic filtering is implemented by Skia and gives the
            // preparer one stable, high-quality downsampling policy instead of a managed
            // per-pixel sampler that can alias fine patterns differently on each runtime.
            var sampling = new SKSamplingOptions(new SKCubicResampler(1f / 3f, 1f / 3f));
            if (!sourcePixmap.ScalePixels(outputPixmap, sampling))
                throw new ImageLoadException(
                    ImageLoadFailureKind.InvalidData,
                    "Skia could not resample the decoded image."
                );
        }
        finally
        {
            outputHandle.Free();
            sourceHandle.Free();
        }
        return output;
    }

    private static (int X, int Y) Map(
        SKEncodedOrigin origin,
        int x,
        int y,
        int width,
        int height
    ) =>
        origin switch
        {
            SKEncodedOrigin.Default or SKEncodedOrigin.TopLeft => (x, y),
            SKEncodedOrigin.TopRight => (width - 1 - x, y),
            SKEncodedOrigin.BottomRight => (width - 1 - x, height - 1 - y),
            SKEncodedOrigin.BottomLeft => (x, height - 1 - y),
            SKEncodedOrigin.RightTop => (height - 1 - y, x),
            SKEncodedOrigin.LeftBottom => (y, width - 1 - x),
            SKEncodedOrigin.LeftTop => (y, x),
            SKEncodedOrigin.RightBottom => (height - 1 - y, width - 1 - x),
            _ => throw new ImageLoadException(
                ImageLoadFailureKind.InvalidData,
                $"The codec returned an unknown encoded image origin: {origin}."
            ),
        };

    private static void ClampPremultiplied(byte[] pixels)
    {
        for (var index = 0; index < pixels.Length; index += BytesPerPixel)
        {
            var alpha = pixels[index + 3];
            pixels[index] = Math.Min(pixels[index], alpha);
            pixels[index + 1] = Math.Min(pixels[index + 1], alpha);
            pixels[index + 2] = Math.Min(pixels[index + 2], alpha);
        }
    }

    private static ImageLoadException InvalidCodec(AssetReference asset, SKCodecResult result) =>
        new(
            result == SKCodecResult.Unimplemented
                ? ImageLoadFailureKind.UnsupportedFormat
                : ImageLoadFailureKind.InvalidData,
            $"Skia could not create a codec for {asset.Id}: {result}."
        );
}
