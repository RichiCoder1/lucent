using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lucent.Core;
using Lucent.Renderer.Skia;
using SkiaSharp;

if (args is ["generate"])
{
    GenerateFixtures();
    return;
}

if (args is ["performance", var performancePath, var performanceFormat])
{
    var performanceAssetFormat = ParseFormat(performanceFormat);
    var performanceResult = MeasurePerformance(performancePath, performanceAssetFormat);
    Console.WriteLine(
        JsonSerializer.Serialize(performanceResult, ProbeJsonContext.Default.PerformanceMeasurement)
    );
    return;
}

if (args is not [var targetPath, var formatName])
    throw new ArgumentException("Usage: MemoryProbe <path> <png|jpeg>");

var format = ParseFormat(formatName);
Warm(Path.Combine(AppContext.BaseDirectory, "fixtures", "warm.png"));
ForceCollection();
var result = Measure(targetPath, format);
Console.WriteLine(JsonSerializer.Serialize(result, ProbeJsonContext.Default.Measurement));

static void Warm(string path)
{
    var source = CreateSource(path, AssetFormat.Png);
    using var result = Decode(source, monitor: false);
}

static AssetFormat ParseFormat(string formatName) =>
    formatName.ToLowerInvariant() switch
    {
        "png" => AssetFormat.Png,
        "jpeg" or "jpg" => AssetFormat.Jpeg,
        _ => throw new ArgumentException($"Unsupported image format: {formatName}."),
    };

static Measurement Measure(string path, AssetFormat format)
{
    var source = CreateSource(path, format);
    ForceCollection();
    var process = Process.GetCurrentProcess();
    process.Refresh();
    var privateBefore = process.PrivateMemorySize64;
    var workingBefore = process.WorkingSet64;
    var managedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var heapBefore = GC.GetTotalMemory(forceFullCollection: false);
    var stopwatch = Stopwatch.StartNew();
    using var decode = Decode(source, monitor: true);
    stopwatch.Stop();
    process.Refresh();
    return new Measurement(
        Path.GetFileName(path),
        new FileInfo(path).Length,
        decode.SourceWidth,
        decode.SourceHeight,
        decode.Resource.Width,
        decode.Resource.Height,
        decode.Resource.ByteCount,
        decode.PeakTemporaryBytes,
        GC.GetTotalAllocatedBytes(precise: true) - managedBefore,
        GC.GetTotalMemory(forceFullCollection: false) - heapBefore,
        privateBefore,
        decode.PeakPrivateBytes,
        decode.PeakPrivateBytes - privateBefore,
        workingBefore,
        decode.PeakWorkingSetBytes,
        decode.PeakWorkingSetBytes - workingBefore,
        stopwatch.Elapsed.TotalMilliseconds
    );
}

static SourceDescriptor CreateSource(string path, AssetFormat format)
{
    var bytes = File.ReadAllBytes(path);
    using var bounds = SKCodec.Create(new MemoryStream(bytes, writable: false));
    if (bounds is null)
        throw new InvalidDataException(path);
    var width = bounds.Info.Width;
    var height = bounds.Info.Height;
    var asset = new AssetReference(
        new AssetId("MemoryProbe", Path.GetFileName(path)),
        Convert.ToHexString(SHA256.HashData(bytes)),
        bytes.LongLength,
        format,
        () => File.OpenRead(path),
        new AssetImageMetadata(width, height)
    );
    return new SourceDescriptor(ImageSource.FromAsset(asset), width, height, path);
}

static DecodeResult Decode(SourceDescriptor source, bool monitor)
{
    var temporaryLimit = long.TryParse(
        Environment.GetEnvironmentVariable("LUCENT_IMAGE_PROBE_TEMP_LIMIT"),
        out var configuredTemporaryLimit
    )
        ? configuredTemporaryLimit
        : 1024L * 1024 * 1024;
    var graph = new ReactiveGraph();
    var owner = graph.CreateScope("memory-probe");
    var cache = CreateCache(new SkiaImagePreparer(), temporaryLimit);
    var handle = cache.Acquire(owner, source.Source, new ImageRendition(128, 128));
    var process = Process.GetCurrentProcess();
    long peakPrivate = 0;
    long peakWorking = 0;
    long peakTemporary = 0;
    var timeout = Stopwatch.StartNew();
    while (handle.Status == ImageLoadStatus.Loading)
    {
        graph.Drain();
        if (monitor)
        {
            process.Refresh();
            peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
            peakWorking = Math.Max(peakWorking, process.WorkingSet64);
            peakTemporary = Math.Max(peakTemporary, cache.Metrics.TemporaryBytes);
        }
        if (timeout.Elapsed > TimeSpan.FromMinutes(2))
            throw new TimeoutException(source.Path);
        Thread.Sleep(1);
    }
    if (handle.Status != ImageLoadStatus.Ready)
        throw (Exception?)handle.Error
            ?? new InvalidOperationException($"Decode ended as {handle.Status}.");
    var lease = handle.AcquireLease() ?? throw new InvalidOperationException("No ready lease.");
    process.Refresh();
    peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
    peakWorking = Math.Max(peakWorking, process.WorkingSet64);
    peakTemporary = Math.Max(peakTemporary, cache.Metrics.TemporaryBytes);
    return new DecodeResult(
        cache,
        handle,
        owner,
        lease,
        lease.Resource,
        source.Width,
        source.Height,
        peakPrivate,
        peakWorking,
        peakTemporary
    );
}

static ImageCache CreateCache(IImagePreparer preparer, long temporaryLimit = 1024L * 1024 * 1024) =>
    new(
        preparer,
        new ImageLoadLimits(
            maximumEncodedBytes: 512L * 1024 * 1024,
            maximumSourcePixels: 100L * 1024 * 1024,
            maximumOutputBytes: 64L * 1024 * 1024,
            maximumTemporaryBytes: temporaryLimit,
            maximumCachedBytes: 64L * 1024 * 1024,
            maximumLeasedBytes: 64L * 1024 * 1024,
            maximumQueuedRequests: 2,
            maximumConcurrentPreparations: 1
        )
    );

static PerformanceMeasurement MeasurePerformance(string rasterPath, AssetFormat rasterFormat)
{
    var warmPath = Path.Combine(AppContext.BaseDirectory, "fixtures", "warm.png");
    Warm(warmPath);
    ForceCollection();

    var process = Process.GetCurrentProcess();
    process.Refresh();
    var privateBefore = process.PrivateMemorySize64;
    var managedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var graph = new ReactiveGraph();
    var owner = graph.CreateScope("performance-probe");
    var cache = CreateCache(new SkiaImagePreparer());
    try
    {
        var raster = CreateSource(rasterPath, rasterFormat);
        var svg = CreateSvgSource();
        var rasterCold = MeasureAcquire(
            graph,
            owner,
            cache,
            raster.Source,
            new ImageRendition(128, 128),
            "raster-cold"
        );
        var rasterWarm = MeasureAcquire(
            graph,
            owner,
            cache,
            raster.Source,
            new ImageRendition(128, 128),
            "raster-warm"
        );
        var svgCold = MeasureAcquire(
            graph,
            owner,
            cache,
            svg,
            new ImageRendition(128, 128),
            "svg-cold"
        );
        var svgWarm = MeasureAcquire(
            graph,
            owner,
            cache,
            svg,
            new ImageRendition(128, 128),
            "svg-warm"
        );
        var churn = MeasureRenditionChurn(graph, owner, cache, raster.Source);
        var idleBefore = cache.Metrics;
        Thread.Sleep(100);
        graph.Drain();
        var idleAfter = cache.Metrics;
        process.Refresh();
        return new PerformanceMeasurement(
            Path.GetFileName(rasterPath),
            rasterFormat.ToString(),
            rasterCold,
            rasterWarm,
            svgCold,
            svgWarm,
            churn,
            Snapshot(idleBefore),
            Snapshot(idleAfter),
            idleBefore.Active == 0
                && idleBefore.Queued == 0
                && idleBefore.TemporaryBytes == 0
                && idleAfter == idleBefore,
            privateBefore,
            process.PrivateMemorySize64,
            GC.GetTotalAllocatedBytes(precise: true) - managedBefore
        );
    }
    finally
    {
        cache.Dispose();
        owner.Dispose();
    }
}

static AcquireMeasurement MeasureAcquire(
    ReactiveGraph graph,
    ReactiveScope owner,
    ImageCache cache,
    ImageSource source,
    ImageRendition rendition,
    string name
)
{
    var before = cache.Metrics;
    var stopwatch = Stopwatch.StartNew();
    using var handle = cache.Acquire(owner, source, rendition);
    WaitForReady(graph, handle);
    using var lease = handle.AcquireLease() ?? throw new InvalidOperationException(name);
    stopwatch.Stop();
    var resource = lease.Resource;
    return new AcquireMeasurement(
        name,
        stopwatch.Elapsed.TotalMilliseconds,
        resource.Width,
        resource.Height,
        resource.ByteCount,
        handle.Status.ToString(),
        Snapshot(before),
        Snapshot(cache.Metrics)
    );
}

static ChurnMeasurement MeasureRenditionChurn(
    ReactiveGraph graph,
    ReactiveScope owner,
    ImageCache cache,
    ImageSource source
)
{
    var sizes = new[] { 32, 64, 96, 128, 192, 256, 384, 512, 768, 1024, 128, 256 };
    var elapsed = new List<double>(sizes.Length);
    var before = cache.Metrics;
    foreach (var size in sizes)
    {
        var stopwatch = Stopwatch.StartNew();
        using var handle = cache.Acquire(owner, source, new ImageRendition(size, size));
        WaitForReady(graph, handle);
        using var lease =
            handle.AcquireLease() ?? throw new InvalidOperationException($"churn-{size}");
        _ = lease.Resource;
        stopwatch.Stop();
        elapsed.Add(stopwatch.Elapsed.TotalMilliseconds);
    }

    var after = cache.Metrics;
    return new ChurnMeasurement(sizes, elapsed.ToArray(), Snapshot(before), Snapshot(after));
}

static void WaitForReady(ReactiveGraph graph, ImageLoadHandle handle)
{
    var timeout = Stopwatch.StartNew();
    while (handle.Status == ImageLoadStatus.Loading)
    {
        graph.Drain();
        if (timeout.Elapsed > TimeSpan.FromMinutes(2))
            throw new TimeoutException("Image cache request did not complete.");
        Thread.Sleep(1);
    }
    if (handle.Status != ImageLoadStatus.Ready)
        throw (Exception?)handle.Error
            ?? new InvalidOperationException($"Image ended as {handle.Status}.");
}

static ImageSource CreateSvgSource()
{
    const string text =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\" viewBox=\"0 0 16 16\"><rect x=\"1\" y=\"1\" width=\"14\" height=\"14\" fill=\"#2364AA\"/><circle cx=\"8\" cy=\"8\" r=\"3\" fill=\"#FFFFFF\"/></svg>";
    var bytes = Encoding.UTF8.GetBytes(text);
    var asset = new AssetReference(
        new AssetId("MemoryProbe", "tiny.svg"),
        Convert.ToHexString(SHA256.HashData(bytes)),
        bytes.LongLength,
        AssetFormat.Svg,
        () => new MemoryStream(bytes, writable: false),
        new AssetImageMetadata(16, 16)
    );
    return ImageSource.FromAsset(asset);
}

static CacheSnapshot Snapshot(ImageCacheMetrics metrics) =>
    new(
        metrics.Queued,
        metrics.Active,
        metrics.ReadyEntries,
        metrics.CachedBytes,
        metrics.LeasedBytes,
        metrics.TemporaryBytes,
        metrics.Hits,
        metrics.Misses,
        metrics.Evictions,
        metrics.BudgetDeclines
    );

static void GenerateFixtures()
{
    var directory = Path.Combine(AppContext.BaseDirectory, "fixtures");
    Directory.CreateDirectory(directory);
    Generate(Path.Combine(directory, "warm.png"), 16, SKEncodedImageFormat.Png, 100);
    Generate(Path.Combine(directory, "large.png"), 6000, SKEncodedImageFormat.Png, 100);
    Generate(Path.Combine(directory, "large.jpg"), 6000, SKEncodedImageFormat.Jpeg, 92);
    Generate(
        Path.Combine(directory, "large-444.jpg"),
        6000,
        SKEncodedImageFormat.Jpeg,
        92,
        jpeg444: true
    );
}

static void Generate(
    string path,
    int size,
    SKEncodedImageFormat format,
    int quality,
    bool jpeg444 = false
)
{
    using var surface = SKSurface.Create(
        new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Opaque)
    );
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.DarkSlateBlue);
    using var paint = new SKPaint { IsAntialias = false };
    for (var y = 0; y < size; y += 32)
    {
        paint.Color = new SKColor((byte)(y * 17), (byte)(y * 31), (byte)(y * 47));
        canvas.DrawRect(0, y, size, Math.Min(32, size - y), paint);
    }
    using var image = surface.Snapshot();
    SKData? encoded;
    if (jpeg444)
    {
        using var pixels = image.PeekPixels() ?? throw new InvalidOperationException("No pixels.");
        encoded = pixels.Encode(
            new SKJpegEncoderOptions(
                quality,
                SKJpegEncoderDownsample.Downsample444,
                SKJpegEncoderAlphaOption.Ignore
            )
        );
    }
    else
        encoded = image.Encode(format, quality);
    if (encoded is null)
        throw new InvalidOperationException($"Could not encode {path}.");
    using (encoded)
    {
        using var stream = File.Create(path);
        encoded.SaveTo(stream);
    }
}

static void ForceCollection()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
}

sealed class DecodeResult(
    ImageCache cache,
    ImageLoadHandle handle,
    ReactiveScope owner,
    ImageLease lease,
    PreparedImage resource,
    int sourceWidth,
    int sourceHeight,
    long peakPrivateBytes,
    long peakWorkingSetBytes,
    long peakTemporaryBytes
) : IDisposable
{
    public PreparedImage Resource { get; } = resource;
    public int SourceWidth { get; } = sourceWidth;
    public int SourceHeight { get; } = sourceHeight;
    public long PeakPrivateBytes { get; } = peakPrivateBytes;
    public long PeakWorkingSetBytes { get; } = peakWorkingSetBytes;
    public long PeakTemporaryBytes { get; } = peakTemporaryBytes;

    public void Dispose()
    {
        lease.Dispose();
        handle.Dispose();
        cache.Dispose();
        owner.Dispose();
    }
}

sealed record Measurement(
    string Case,
    long EncodedBytes,
    int SourceWidth,
    int SourceHeight,
    int OutputWidth,
    int OutputHeight,
    long OutputBytes,
    long PeakTemporaryReservationBytes,
    long ManagedAllocatedBytes,
    long ManagedHeapDeltaBytes,
    long PrivateBytesBefore,
    long PeakPrivateBytes,
    long PeakPrivateDeltaBytes,
    long WorkingSetBefore,
    long PeakWorkingSetBytes,
    long PeakWorkingSetDeltaBytes,
    double ElapsedMilliseconds
);

sealed record SourceDescriptor(ImageSource Source, int Width, int Height, string Path);

sealed record PerformanceMeasurement(
    string RasterCase,
    string RasterFormat,
    AcquireMeasurement RasterCold,
    AcquireMeasurement RasterWarm,
    AcquireMeasurement SvgCold,
    AcquireMeasurement SvgWarm,
    ChurnMeasurement Churn,
    CacheSnapshot IdleBefore,
    CacheSnapshot IdleAfter,
    bool IdleQuiescent,
    long PrivateBytesBefore,
    long PrivateBytesAfter,
    long ManagedAllocatedBytes
);

sealed record AcquireMeasurement(
    string Name,
    double ElapsedMilliseconds,
    int Width,
    int Height,
    long ByteCount,
    string Status,
    CacheSnapshot Before,
    CacheSnapshot After
);

sealed record ChurnMeasurement(
    int[] Sizes,
    double[] ElapsedMilliseconds,
    CacheSnapshot Before,
    CacheSnapshot After
);

sealed record CacheSnapshot(
    int Queued,
    int Active,
    int ReadyEntries,
    long CachedBytes,
    long LeasedBytes,
    long TemporaryBytes,
    long Hits,
    long Misses,
    long Evictions,
    long BudgetDeclines
);

[JsonSerializable(typeof(Measurement))]
[JsonSerializable(typeof(PerformanceMeasurement))]
sealed partial class ProbeJsonContext : JsonSerializerContext;
