using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Runtime;
using Lucent.Renderer.Skia;

namespace Lucent.Platform.Windows;

/// <summary>Opt-in compact frame evidence for real host operations; no listener or exporter is installed by Lucent.</summary>
internal sealed partial class PerformanceDiagnostics : IDisposable
{
    private const string EnvironmentVariable = "LUCENT_PERFORMANCE_DIAGNOSTICS";
    private static readonly ActivitySource Activities = new("Lucent.Windows", "0.1");
    private static readonly Meter Metrics = new("Lucent.Windows", "0.1");
    private static readonly Histogram<double> PresentMilliseconds = Metrics.CreateHistogram<double>(
        "lucent.frame.present.ms",
        "ms"
    );
    internal static readonly string[] AllowedTags =
    [
        "lucent.operation=input",
        "lucent.operation=resize",
        "lucent.operation=startup",
        "lucent.operation=other",
    ];
    private readonly string? _path = Environment.GetEnvironmentVariable(EnvironmentVariable);
    private bool _disposed;

    internal PerformanceDiagnostics()
    {
        if (Enabled)
        {
            var path = Path.GetFullPath(_path!);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "lucent-performance-v1" + Environment.NewLine);
        }
    }

    internal bool Enabled => !string.IsNullOrWhiteSpace(_path);

    internal void Record(
        FrameRequest request,
        FrameTiming timing,
        CpuSkiaPresenter presenter,
        SkiaSceneRenderer renderer,
        WindowsUiaProvider provider
    )
    {
        var operation = TelemetryOperation(request.Operation);
        EmitTelemetry(request, timing);
        if (!Enabled)
            return;
        var endToEnd = Stopwatch
            .GetElapsedTime(request.Timestamp, request.Presented)
            .TotalMilliseconds;
        var raster = timing.Raster.TotalMilliseconds;
        using var output = new StreamWriter(
            new FileStream(
                Path.GetFullPath(_path!),
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read
            )
        );
        output.Write(
            string.Join(
                '|',
                "frame",
                operation,
                request.Timestamp.ToString(CultureInfo.InvariantCulture),
                request.Presented.ToString(CultureInfo.InvariantCulture),
                endToEnd.ToString("R", CultureInfo.InvariantCulture),
                timing.Projection.TotalMilliseconds.ToString("R", CultureInfo.InvariantCulture),
                raster.ToString("R", CultureInfo.InvariantCulture),
                timing.Upload.TotalMilliseconds.ToString("R", CultureInfo.InvariantCulture),
                timing.Present.TotalMilliseconds.ToString("R", CultureInfo.InvariantCulture),
                presenter.LiveSurfaceCount.ToString(CultureInfo.InvariantCulture),
                presenter.LiveTextureCount.ToString(CultureInfo.InvariantCulture),
                renderer.LiveTextBlobCount.ToString(CultureInfo.InvariantCulture),
                provider.CacheCount.ToString(CultureInfo.InvariantCulture),
                GetHandleCount().ToString(CultureInfo.InvariantCulture)
            ) + Environment.NewLine
        );
    }

    internal void RecordResources(
        string phase,
        int surfaces,
        int textures,
        int textBlobs,
        int uiaProviders
    )
    {
        if (!Enabled)
            return;
        using var output = new StreamWriter(
            new FileStream(
                Path.GetFullPath(_path!),
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read
            )
        );
        output.WriteLine(
            string.Join(
                '|',
                "resources",
                phase,
                surfaces.ToString(CultureInfo.InvariantCulture),
                textures.ToString(CultureInfo.InvariantCulture),
                textBlobs.ToString(CultureInfo.InvariantCulture),
                uiaProviders.ToString(CultureInfo.InvariantCulture),
                GetHandleCount().ToString(CultureInfo.InvariantCulture)
            )
        );
    }

    internal void RecordPostGcResources(int surfaces, int textures, int textBlobs, int uiaProviders)
    {
        if (!Enabled)
            return;
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        RecordResources("post", surfaces, textures, textBlobs, uiaProviders);
    }

    internal static void EmitTelemetry(FrameRequest request, FrameTiming timing)
    {
        var operation = TelemetryOperation(request.Operation);
        var ended = DateTimeOffset.UtcNow;
        using var activity = Activities.StartActivity(
            "lucent.frame",
            ActivityKind.Internal,
            default(ActivityContext),
            startTime: ended - Stopwatch.GetElapsedTime(request.Timestamp, request.Presented)
        );
        activity?.SetTag("lucent.operation", operation);
        activity?.SetEndTime(ended.UtcDateTime);
        PresentMilliseconds.Record(
            timing.Present.TotalMilliseconds,
            new KeyValuePair<string, object?>("lucent.operation", operation)
        );
    }

    private static string TelemetryOperation(FrameOperation operation) =>
        operation is FrameOperation.Input or FrameOperation.Resize or FrameOperation.Startup
            ? operation.ToString().ToLowerInvariant()
            : "other";

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (!Enabled)
            return;
    }

    private static uint GetHandleCount()
    {
        using var process = Process.GetCurrentProcess();
        if (!GetProcessHandleCount(process.Handle, out var count))
            throw new InvalidOperationException(
                $"GetProcessHandleCount failed (Win32={System.Runtime.InteropServices.Marshal.GetLastWin32Error()})."
            );
        return count;
    }

    [System.Runtime.InteropServices.LibraryImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(
        System.Runtime.InteropServices.UnmanagedType.Bool
    )]
    private static partial bool GetProcessHandleCount(nint process, out uint count);
}
