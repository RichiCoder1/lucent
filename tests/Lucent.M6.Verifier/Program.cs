using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lucent.Core;
using Lucent.Renderer.Skia;

const int Samples = 500;
const double P95Limit = 16.7,
    P99Limit = 33.3,
    RasterP95Limit = 8.3;
const long ManagedGrowthLimit = 16L * 1024 * 1024;
const int SurfaceLimit = 1,
    TextureLimit = 1,
    TextBlobLimit = 0,
    UiaProviderLimit = 18,
    HandleGrowthLimit = 128;

if (args.Length != 2 || args[0] != "--app")
    return Fail("usage: --app <published-exe>");
var app = Path.GetFullPath(args[1]);
if (!File.Exists(app))
    return Fail("missing published app: " + app);
var diagnostics = Path.Combine(
    Path.GetTempPath(),
    "lucent-m6-" + Guid.NewGuid().ToString("N") + ".log"
);
Process? process = null;
try
{
    process = Start(app, diagnostics);
    var window = WaitForWindow(process);
    WaitForInitialFrame(diagnostics);
    DrainFrames(diagnostics);
    Warmup(window, diagnostics);
    var managed = ManagedCycles();
    var input = RunCorpus(window, diagnostics, "input");
    var resize = RunCorpus(window, diagnostics, "resize");
    var beforeIdle = FrameCount(diagnostics);
    Thread.Sleep(TimeSpan.FromSeconds(10));
    var afterIdle = FrameCount(diagnostics);
    if (beforeIdle != afterIdle)
        throw new InvalidOperationException(
            $"Idle scheduled {afterIdle - beforeIdle} frames in ten seconds."
        );
    Close(process, window);
    process = null;
    var frames = ReadFrames(diagnostics);
    var resources = ReadResources(diagnostics);
    CheckCorpus("input", input.Frames);
    CheckCorpus("resize", resize.Frames);
    var resource = CheckResources(frames, resources);
    WriteResult(input, resize, managed, resource);
    return 0;
}
catch (Exception error)
{
    return Fail(error.Message);
}
finally
{
    if (process is not null)
        Close(process, process.MainWindowHandle);
    if (File.Exists(diagnostics))
        Console.Error.WriteLine("Lucent M6 diagnostics: " + diagnostics);
}

static Process Start(string app, string diagnostics)
{
    var info = new ProcessStartInfo(app)
    {
        WorkingDirectory = Path.GetDirectoryName(app)!,
        UseShellExecute = false,
    };
    info.Environment["LUCENT_M6_DIAGNOSTICS"] = diagnostics;
    return Process.Start(info)
        ?? throw new InvalidOperationException("Could not start published app.");
}

static nint WaitForWindow(Process process)
{
    var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 15;
    do
    {
        Thread.Sleep(25);
        process.Refresh();
        if (process.HasExited)
            throw new InvalidOperationException("Published app exited " + process.ExitCode + ".");
        if (process.MainWindowHandle != 0)
            return process.MainWindowHandle;
    } while (Stopwatch.GetTimestamp() < until);
    throw new InvalidOperationException("Published app did not expose an HWND.");
}

static void Warmup(nint window, string diagnostics)
{
    for (var index = 0; index < 8; index++)
    {
        var frames = FrameCount(diagnostics);
        SendTab(window);
        _ = WaitForExpectedFrame(diagnostics, frames, "input", "warmup input");
    }
    var resizeFrames = FrameCount(diagnostics);
    if (!Native.SetWindowPos(window, 0, 0, 0, 803, 501, 0x0014))
        throw new InvalidOperationException("Warmup SetWindowPos failed.");
    _ = WaitForExpectedFrame(diagnostics, resizeFrames, "resize", "warmup resize");
    DrainFrames(diagnostics);
}

static void WaitForInitialFrame(string diagnostics)
{
    var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 15;
    while (Stopwatch.GetTimestamp() < until)
    {
        if (FrameCount(diagnostics) > 0)
            return;
        Thread.Sleep(10);
    }
    throw new InvalidOperationException("Published app did not present its initial frame.");
}

static Corpus RunCorpus(nint window, string diagnostics, string expected)
{
    var first = FrameCount(diagnostics);
    var frames = new List<Frame>(Samples);
    for (var index = 0; index < Samples; index++)
    {
        var prior = FrameCount(diagnostics);
        if (expected == "input")
            SendTab(window);
        else
        {
            var size = (index & 1) == 0 ? 801 : 803;
            if (!Native.SetWindowPos(window, 0, 0, 0, size, 501, 0x0014))
                throw new InvalidOperationException("SetWindowPos failed.");
        }
        frames.Add(
            WaitForExpectedFrame(diagnostics, prior, expected, expected + " sample " + index)
        );
        if (expected == "resize")
            DrainFrames(diagnostics);
    }
    var last = FrameCount(diagnostics);
    return new Corpus(frames.ToArray(), new(first + 1, last, frames.Count));
}

static Frame WaitForExpectedFrame(
    string diagnostics,
    int priorFrames,
    string expected,
    string operation
)
{
    var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
    do
    {
        var frames = ReadFrames(diagnostics);
        if (frames.Length > priorFrames)
        {
            var frame = frames[priorFrames];
            if (frame.Operation != expected)
                throw new InvalidOperationException(
                    $"{operation} produced {frame.Operation}, expected {expected}."
                );
            return frame;
        }
        Thread.Sleep(1);
    } while (Stopwatch.GetTimestamp() < until);
    throw new InvalidOperationException(
        $"{operation} did not produce a {expected} frame within five seconds."
    );
}

static void DrainFrames(string diagnostics)
{
    var prior = FrameCount(diagnostics);
    var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
    while (Stopwatch.GetTimestamp() < until)
    {
        Thread.Sleep(100);
        var current = FrameCount(diagnostics);
        if (current == prior)
            return;
        prior = current;
    }
    throw new InvalidOperationException(
        "Window notifications did not settle before the next corpus delimiter."
    );
}

static ManagedObservation ManagedCycles()
{
    const int sourceRows = 10_000;
    PrimeManagedBaseline();
    CompactCollect();
    long baseline = GC.GetTotalMemory(false),
        peak = baseline;
    var visibleMaximum = 0;
    var realizedMaximum = 0;
    for (var cycle = 0; cycle < 20; cycle++)
    {
        var graph = new ReactiveGraph();
        using var composition = CreateVirtualizationFixture(graph, out _);
        using var renderer = new SkiaSceneRenderer();
        WaitForIssues(graph, composition, renderer);
        var viewport = new LayoutViewport(800, 500, 1);
        var first = SceneLayout.Project(composition, viewport, renderer);
        if (!composition.Input.SetScene(first))
            throw new InvalidOperationException("Managed M6 baseline scene was rejected.");
        var list = Flatten(composition.SemanticSnapshot()!)
            .Single(node => node.Name == "Issues" && node.Actions.HasFlag(SemanticAction.Scroll));
        MeasureVirtualization(
            first,
            composition.SemanticSnapshot()!,
            list,
            ref visibleMaximum,
            ref realizedMaximum
        );
        if (
            !composition.Input.ScrollSemantic(
                new(list.Identity.CompositionEpoch, list.Identity.ElementId),
                new(SemanticCommandKind.Scroll, Endpoint: SemanticScrollEndpoint.End)
            )
        )
            throw new InvalidOperationException("10,000-row semantic scroll to end was rejected.");
        graph.Drain();
        var last = SceneLayout.Project(composition, viewport, renderer);
        if (!composition.Input.SetScene(last))
            throw new InvalidOperationException("Managed M6 end scene was rejected.");
        MeasureVirtualization(
            last,
            composition.SemanticSnapshot()!,
            list,
            ref visibleMaximum,
            ref realizedMaximum
        );
        peak = Math.Max(peak, GC.GetTotalMemory(false));
    }
    CompactCollect();
    var post = GC.GetTotalMemory(false);
    if (post - baseline > ManagedGrowthLimit)
        throw new InvalidOperationException(
            $"Managed post-GC growth {post - baseline} exceeds {ManagedGrowthLimit} bytes after 20 cycles."
        );
    if (visibleMaximum > 2 || realizedMaximum > 6)
        throw new InvalidOperationException(
            $"10k virtualization exceeded frozen bounds: visible={visibleMaximum}, realized={realizedMaximum}."
        );
    return new ManagedObservation(
        new(20, baseline, peak, post, post - baseline, ManagedGrowthLimit),
        new(sourceRows, visibleMaximum, realizedMaximum)
    );
}

static void MeasureVirtualization(
    RetainedScene scene,
    SemanticSnapshot snapshot,
    SemanticSnapshot list,
    ref int visibleMaximum,
    ref int realizedMaximum
)
{
    var viewport = scene
        .Boxes.Single(box => box.Identity.ElementId == list.Identity.ElementId)
        .Bounds;
    var rows = Flatten(snapshot)
        .Where(node => node.Role == SemanticRole.ListItem)
        .Select(node =>
            scene.Boxes.Single(box => box.Identity.ElementId == node.Identity.ElementId).Bounds
        )
        .ToArray();
    realizedMaximum = Math.Max(realizedMaximum, rows.Length);
    visibleMaximum = Math.Max(
        visibleMaximum,
        rows.Count(row => row.Y < viewport.Y + viewport.Height && row.Y + row.Height > viewport.Y)
    );
}

static void PrimeManagedBaseline()
{
    var graph = new ReactiveGraph();
    using var composition = CreateVirtualizationFixture(graph, out _);
    using var renderer = new SkiaSceneRenderer();
    WaitForIssues(graph, composition, renderer);
    _ = SceneLayout.Project(composition, new(800, 500, 1), renderer);
}

static void CompactCollect()
{
    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    GC.WaitForPendingFinalizers();
    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
}

static Composition CreateVirtualizationFixture(ReactiveGraph graph, out ThemeContext theme)
{
    var composition = new Composition(graph, "m6-virtualization");
    var context = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
    theme = context;
    Controls.Column(
        composition.Root,
        context,
        "M6",
        Style
            .Empty.Set(LayoutProperties.Width, 800f)
            .Set(LayoutProperties.Height, 500f)
            .Set(LayoutProperties.Clip, true)
    );
    var rows = Enumerable.Range(1, 10_000).ToArray();
    if (rows.Length != 10_000)
        throw new InvalidOperationException("M6 source fixture did not contain 10,000 rows.");
    var values = composition.Root.Scope.Signal(rows, "m6.rows");
    var viewport = composition.Child(composition.Root, "viewport");
    Controls.ScrollViewport(
        viewport,
        context,
        "Issues",
        style: Style.Empty.Set(LayoutProperties.Width, 800f).Set(LayoutProperties.Height, 60f)
    );
    _ = Controls.VirtualizedList(
        viewport,
        context,
        "rows",
        "Issues",
        () => values.Value,
        value => value,
        (value, factory) =>
        {
            var row = factory.Element("row");
            Controls.Selectable(row, context, "Issue " + value);
            return row;
        },
        30f
    );
    return composition;
}

static void WaitForIssues(ReactiveGraph graph, Composition composition, SkiaSceneRenderer renderer)
{
    var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
    while (Stopwatch.GetTimestamp() < until)
    {
        graph.Drain();
        var scene = SceneLayout.Project(composition, new(800, 500, 1), renderer);
        if (
            composition.Input.SetScene(scene)
            && composition.SemanticSnapshot() is { } snapshot
            && Flatten(snapshot).Any(node => node.Role == SemanticRole.ListItem)
        )
            return;
        Thread.Sleep(1);
    }
    throw new InvalidOperationException("Fixture issues did not load for managed M6 cycles.");
}

static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot node)
{
    yield return node;
    foreach (var child in node.Children)
    foreach (var descendant in Flatten(child))
        yield return descendant;
}

static int FrameCount(string path) => ReadFrames(path).Length;
static Frame[] ReadFrames(string path) =>
    ReadDiagnosticLines(path)
        .Where(line => line.StartsWith("frame|", StringComparison.Ordinal))
        .Select(Frame.Parse)
        .ToArray();
static Resource[] ReadResources(string path) =>
    ReadDiagnosticLines(path)
        .Where(line => line.StartsWith("resources|", StringComparison.Ordinal))
        .Select(Resource.Parse)
        .ToArray();
static string[] ReadDiagnosticLines(string path)
{
    try
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite
        );
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
    }
    catch (IOException)
    {
        return [];
    }
}
static void CheckCorpus(string name, Frame[] frames)
{
    if (frames.Length != Samples)
        throw new InvalidOperationException(
            $"{name} corpus has {frames.Length} samples, requires exactly {Samples}."
        );
    var summary = Summary(frames);
    if (
        summary.p95Ms > P95Limit
        || summary.p99Ms > P99Limit
        || summary.rendererP95Ms > RasterP95Limit
    )
        throw new InvalidOperationException(
            $"{name} bounds failed: p95={summary.p95Ms:F3} p99={summary.p99Ms:F3} raster-p95={summary.rendererP95Ms:F3}."
        );
}
static ResourceObservation CheckResources(Frame[] frames, Resource[] resources)
{
    if (
        frames.Length == 0
        || resources.Length != 2
        || resources[0].Phase != "pre"
        || resources[1].Phase != "post"
    )
        throw new InvalidOperationException("M6 lifecycle resource observations were incomplete.");
    var pre = resources[0];
    var post = resources[1];
    var surfaces = frames.Max(frame => frame.Surfaces);
    var textures = frames.Max(frame => frame.Textures);
    var blobs = frames.Max(frame => frame.TextBlobs);
    var providers = frames.Max(frame => frame.UiaProviders);
    var handles = frames
        .Select(frame => frame.Handles)
        .Append(pre.Handles)
        .Append(post.Handles)
        .ToArray();
    var handlePeak = handles.Max();
    if (
        surfaces > SurfaceLimit
        || textures > TextureLimit
        || blobs > TextBlobLimit
        || providers > UiaProviderLimit
        || handlePeak - pre.Handles > HandleGrowthLimit
    )
        throw new InvalidOperationException("A predeclared native resource bound was exceeded.");
    if (post.Surfaces != 0 || post.Textures != 0 || post.TextBlobs != 0 || post.UiaProviders != 0)
        throw new InvalidOperationException(
            "Native/UIA resources did not return to their post-GC lifecycle baseline."
        );
    return new(surfaces, textures, blobs, providers, pre, handlePeak, post);
}
static Statistics Summary(Frame[] frames) =>
    new(
        frames.Length,
        Percentile(frames.Select(frame => frame.EndToEnd).ToArray(), .95),
        Percentile(frames.Select(frame => frame.EndToEnd).ToArray(), .99),
        Percentile(frames.Select(frame => frame.Raster).ToArray(), .95)
    );
static double Percentile(double[] values, double percentile)
{
    Array.Sort(values);
    return values[(int)Math.Ceiling(values.Length * percentile) - 1];
}
static void WriteResult(
    Corpus input,
    Corpus resize,
    ManagedObservation managed,
    ResourceObservation resources
)
{
    var inputStats = Summary(input.Frames);
    var resizeStats = Summary(resize.Frames);
    using var output = Console.OpenStandardOutput();
    using var writer = new Utf8JsonWriter(output);
    writer.WriteStartObject();
    writer.WriteBoolean("ok", true);
    WriteStatistics(writer, "input", inputStats);
    WriteStatistics(writer, "resize", resizeStats);
    writer.WriteStartObject("corpusDelimiters");
    WriteDelimiter(writer, "input", input.Delimiters);
    WriteDelimiter(writer, "resize", resize.Delimiters);
    writer.WriteEndObject();
    writer.WriteNumber("idleFrames", 0);
    writer.WriteStartObject("virtualization");
    writer.WriteNumber("sourceRows", managed.Virtualization.SourceRows);
    writer.WriteNumber("visibleRowsMaximum", managed.Virtualization.VisibleRowsMaximum);
    writer.WriteNumber("realizedMaximum", managed.Virtualization.RealizedMaximum);
    writer.WriteEndObject();
    writer.WriteStartObject("managed");
    writer.WriteNumber("cycles", managed.Memory.Cycles);
    writer.WriteNumber("baselineBytes", managed.Memory.BaselineBytes);
    writer.WriteNumber("peakBytes", managed.Memory.PeakBytes);
    writer.WriteNumber("postGcBytes", managed.Memory.PostGcBytes);
    writer.WriteNumber("growthBytes", managed.Memory.GrowthBytes);
    writer.WriteNumber("limitBytes", managed.Memory.LimitBytes);
    writer.WriteEndObject();
    writer.WriteStartObject("resources");
    writer.WriteNumber("surfaceMaximum", resources.SurfaceMaximum);
    writer.WriteNumber("textureMaximum", resources.TextureMaximum);
    writer.WriteNumber("textBlobMaximum", resources.TextBlobMaximum);
    writer.WriteNumber("uiaProviderMaximum", resources.UiaProviderMaximum);
    writer.WriteStartObject("handles");
    writer.WriteNumber("baseline", resources.Pre.Handles);
    writer.WriteNumber("peak", resources.HandlePeak);
    writer.WriteNumber("final", resources.Post.Handles);
    writer.WriteNumber("peakDelta", (long)resources.HandlePeak - resources.Pre.Handles);
    writer.WriteNumber("finalDelta", (long)resources.Post.Handles - resources.Pre.Handles);
    writer.WriteEndObject();
    writer.WriteStartObject("lifecycle");
    WriteResource(writer, "pre", resources.Pre);
    writer.WriteStartObject("peak");
    writer.WriteNumber("surfaces", resources.SurfaceMaximum);
    writer.WriteNumber("textures", resources.TextureMaximum);
    writer.WriteNumber("textBlobs", resources.TextBlobMaximum);
    writer.WriteNumber("uiaProviders", resources.UiaProviderMaximum);
    writer.WriteEndObject();
    WriteResource(writer, "post", resources.Post);
    writer.WriteEndObject();
    writer.WriteEndObject();
    writer.WriteEndObject();
    writer.Flush();
    Console.WriteLine();
}
static void WriteStatistics(Utf8JsonWriter writer, string name, Statistics value)
{
    writer.WriteStartObject(name);
    writer.WriteNumber("samples", value.samples);
    writer.WriteNumber("p95Ms", value.p95Ms);
    writer.WriteNumber("p99Ms", value.p99Ms);
    writer.WriteNumber("rendererP95Ms", value.rendererP95Ms);
    writer.WriteEndObject();
}
static void WriteDelimiter(Utf8JsonWriter writer, string name, CorpusDelimiter value)
{
    writer.WriteStartObject(name);
    writer.WriteNumber("firstFrame", value.FirstFrame);
    writer.WriteNumber("lastFrame", value.LastFrame);
    writer.WriteNumber("samples", value.Samples);
    writer.WriteEndObject();
}
static void WriteResource(Utf8JsonWriter writer, string name, Resource value)
{
    writer.WriteStartObject(name);
    writer.WriteNumber("surfaces", value.Surfaces);
    writer.WriteNumber("textures", value.Textures);
    writer.WriteNumber("textBlobs", value.TextBlobs);
    writer.WriteNumber("uiaProviders", value.UiaProviders);
    writer.WriteNumber("handles", value.Handles);
    writer.WriteEndObject();
}
static void Close(Process process, nint window)
{
    if (!process.HasExited && window != 0)
        Native.PostMessage(window, 0x0010, 0, 0);
    if (!process.WaitForExit(10_000))
    {
        process.Kill();
        process.WaitForExit();
        throw new InvalidOperationException("Published app did not close normally.");
    }
    if (process.ExitCode != 0)
        throw new InvalidOperationException("Published app exited " + process.ExitCode + ".");
    process.Dispose();
}
static int Fail(string message)
{
    Console.Error.WriteLine("Lucent M6 verifier: FAIL: " + message);
    return 1;
}
static void SendTab(nint window)
{
    if (
        !Native.PostMessage(window, 0x0100, 9, 1)
        || !Native.PostMessage(window, 0x0101, 9, unchecked((nint)0xC0000001))
    )
        throw new InvalidOperationException("PostMessage(Tab) failed.");
}

readonly record struct Corpus(Frame[] Frames, CorpusDelimiter Delimiters);

readonly record struct CorpusDelimiter(int FirstFrame, int LastFrame, int Samples);

readonly record struct ManagedObservation(
    MemoryObservation Memory,
    VirtualizationObservation Virtualization
);

readonly record struct MemoryObservation(
    int Cycles,
    long BaselineBytes,
    long PeakBytes,
    long PostGcBytes,
    long GrowthBytes,
    long LimitBytes
);

readonly record struct VirtualizationObservation(
    int SourceRows,
    int VisibleRowsMaximum,
    int RealizedMaximum
);

readonly record struct Statistics(int samples, double p95Ms, double p99Ms, double rendererP95Ms);

readonly record struct ResourceObservation(
    int SurfaceMaximum,
    int TextureMaximum,
    int TextBlobMaximum,
    int UiaProviderMaximum,
    Resource Pre,
    uint HandlePeak,
    Resource Post
);

readonly record struct Frame(
    string Operation,
    double EndToEnd,
    double Raster,
    int Surfaces,
    int Textures,
    int TextBlobs,
    int UiaProviders,
    uint Handles
)
{
    internal static Frame Parse(string line)
    {
        var fields = line.Split('|');
        if (fields.Length != 14)
            throw new InvalidOperationException("Malformed M6 diagnostic row.");
        return new(
            fields[1],
            double.Parse(fields[4], CultureInfo.InvariantCulture),
            double.Parse(fields[6], CultureInfo.InvariantCulture),
            int.Parse(fields[9], CultureInfo.InvariantCulture),
            int.Parse(fields[10], CultureInfo.InvariantCulture),
            int.Parse(fields[11], CultureInfo.InvariantCulture),
            int.Parse(fields[12], CultureInfo.InvariantCulture),
            uint.Parse(fields[13], CultureInfo.InvariantCulture)
        );
    }
}

readonly record struct Resource(
    string Phase,
    int Surfaces,
    int Textures,
    int TextBlobs,
    int UiaProviders,
    uint Handles
)
{
    internal static Resource Parse(string line)
    {
        var fields = line.Split('|');
        if (fields.Length != 7)
            throw new InvalidOperationException("Malformed M6 resource row.");
        return new(
            fields[1],
            int.Parse(fields[2], CultureInfo.InvariantCulture),
            int.Parse(fields[3], CultureInfo.InvariantCulture),
            int.Parse(fields[4], CultureInfo.InvariantCulture),
            int.Parse(fields[5], CultureInfo.InvariantCulture),
            uint.Parse(fields[6], CultureInfo.InvariantCulture)
        );
    }
}

static class Native
{
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(
        nint window,
        nint after,
        int x,
        int y,
        int width,
        int height,
        uint flags
    );

    [DllImport("user32.dll")]
    internal static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
