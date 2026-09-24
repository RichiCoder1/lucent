using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Lucent.Core;
using Lucent.Renderer.Skia;

const int Samples = 500;
const long ManagedGrowthLimit = 16L * 1024 * 1024;

if (args.Length == 1 && args[0] == "--input-dispatch")
    return InputDispatchProbe.Run();

if (args.Length == 1 && args[0] == "--scene-projection")
    return SceneProjectionProbe.Run();

if (args.Length == 2 && args[0] == "--characterization")
    return CharacterizationEvidence.Run(args[1]);

if (args.Length != 2 || args[0] is not ("--app" or "--fixed-app"))
    return Fail(
        "usage: --app <published-exe> | --fixed-app <published-exe> | --characterization <journal.json> | --input-dispatch | --scene-projection"
    );
var app = Path.GetFullPath(args[1]);
var diagnostics = Path.Combine(
    Path.GetTempPath(),
    "lucent-performance-" + Guid.NewGuid().ToString("N") + ".log"
);
Process? process = null;
var evidence = new PerformanceEvidence(
    app,
    diagnostics,
    args[0] == "--fixed-app" ? "fixed-native-v1" : "issue-browser-compat-v1"
);
var launchTimestamp = Stopwatch.GetTimestamp();
try
{
    if (!File.Exists(app))
        throw new FileNotFoundException("Missing published app", app);
    evidence.MarkPhase("launch", "start", 0);
    process = Start(app, diagnostics);
    var window = WaitForWindow(process);
    if (
        Native.GetClientRect(window, out var client)
        && Native.GetDpiForWindow(window) is var dpi
        && dpi > 0
    )
    {
        evidence.WindowDpi = (int)dpi;
        evidence.ClientWidthPixels = client.Right - client.Left;
        evidence.ClientHeightPixels = client.Bottom - client.Top;
    }
    WaitForInitialFrame(diagnostics);
    evidence.LaunchToReadyMs = Stopwatch.GetElapsedTime(launchTimestamp).TotalMilliseconds;
    evidence.MarkPhase("firstReady", "complete", FrameCount(diagnostics));
    DrainFrames(diagnostics);
    evidence.MarkPhase("warmup", "start", FrameCount(diagnostics));
    Warmup(window, diagnostics);
    evidence.MarkPhase("warmup", "complete", FrameCount(diagnostics));
    evidence.MarkPhase("managed", "start", FrameCount(diagnostics));
    evidence.Managed = ManagedCycles(evidence);
    evidence.MarkPhase("managed", "complete", FrameCount(diagnostics));
    RunCorpus(window, diagnostics, "input", evidence);
    RunCorpus(window, diagnostics, "resize", evidence);
    evidence.MarkPhase("idle", "start", FrameCount(diagnostics));
    var beforeIdle = FrameCount(diagnostics);
    Thread.Sleep(TimeSpan.FromSeconds(10));
    var afterIdle = FrameCount(diagnostics);
    evidence.IdleFrames = afterIdle - beforeIdle;
    evidence.MarkPhase("idle", "complete", afterIdle);
    if (beforeIdle != afterIdle)
        throw new InvalidOperationException(
            $"Idle scheduled {afterIdle - beforeIdle} frames in ten seconds."
        );
    evidence.MarkPhase("close", "start", FrameCount(diagnostics));
    Close(process, window);
    process = null;
    evidence.MarkPhase("close", "complete", FrameCount(diagnostics));
}
catch (Exception error)
{
    evidence.Failures.Add(new("operation", error.ToString()));
    try
    {
        evidence.MarkPhase("operation", "failed");
    }
    catch (Exception journalError)
    {
        evidence.Failures.Add(new("journal", journalError.ToString()));
    }
}
finally
{
    try
    {
        evidence.MarkPhase("cleanup", "start");
    }
    catch (Exception journalError)
    {
        evidence.Failures.Add(new("journal", journalError.ToString()));
    }
    if (process is not null)
    {
        try
        {
            Close(process, process.MainWindowHandle);
        }
        catch (Exception error)
        {
            evidence.Failures.Add(new("cleanup", error.ToString()));
        }
        finally
        {
            process.Dispose();
        }
    }
    try
    {
        evidence.MarkPhase("cleanup", "complete");
    }
    catch (Exception journalError)
    {
        evidence.Failures.Add(new("journal", journalError.ToString()));
    }
    try
    {
        evidence.CollectAndEvaluate();
    }
    catch (Exception error)
    {
        evidence.Failures.Add(new("evidence", error.ToString()));
    }
    if (File.Exists(diagnostics))
        Console.Error.WriteLine("Lucent performance diagnostics: " + diagnostics);
    evidence.Write(Console.OpenStandardOutput());
}
return evidence.Passed
    ? 0
    : Fail(
        evidence.Failures.Count > 0 ? evidence.Failures[0].Message : "Evidence or bounds failed."
    );

static Process Start(string app, string diagnostics)
{
    var info = new ProcessStartInfo(app)
    {
        WorkingDirectory = Path.GetDirectoryName(app)!,
        UseShellExecute = false,
    };
    info.Environment["LUCENT_PERFORMANCE_DIAGNOSTICS"] = diagnostics;
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

static void RunCorpus(
    nint window,
    string diagnostics,
    string expected,
    PerformanceEvidence evidence
)
{
    var first = FrameCount(diagnostics);
    evidence.BeginCorpus(expected, first + 1);
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
        var frame = WaitForExpectedFrame(
            diagnostics,
            prior,
            expected,
            expected + " sample " + index
        );
        evidence.AddCorpusFrame(expected, frame, prior + 1);
        // Extra frames belong to this request, never to the next Tab or resize.
        DrainFrames(diagnostics);
    }
    var last = FrameCount(diagnostics);
    evidence.CompleteCorpus(expected, last);
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

static ManagedObservation ManagedCycles(PerformanceEvidence evidence)
{
    const int sourceRows = 10_000;
    PrimeManagedBaseline();
    CompactCollect();
    long baseline = GC.GetTotalMemory(false),
        peak = baseline;
    var visibleMaximum = 0;
    var realizedMaximum = 0;
    var completedCycles = 0;
    for (var cycle = 0; cycle < 20; cycle++)
    {
        try
        {
            var graph = new ReactiveGraph();
            using var composition = CreateVirtualizationFixture(graph, out _);
            using var renderer = new SkiaSceneRenderer();
            using var ready = WaitForIssues(graph, composition, renderer);
            var viewport = new LayoutViewport(800, 500, 1);
            using var first = SceneLayout.Project(composition, viewport, renderer);
            if (!composition.Input.SetScene(first))
                throw new InvalidOperationException(
                    "Managed performance baseline scene was rejected."
                );
            ready.Dispose();
            var list = Flatten(composition.SemanticSnapshot()!)
                .Single(node =>
                    node.Name == "Issues" && node.Actions.HasFlag(SemanticAction.Scroll)
                );
            MeasureVirtualization(
                first,
                composition.SemanticSnapshot()!,
                list,
                ["Issue 1", "Issue 2"],
                ref visibleMaximum,
                ref realizedMaximum
            );
            if (
                !composition.Input.ScrollSemantic(
                    new(list.Identity.CompositionEpoch, list.Identity.ElementId),
                    new(SemanticCommandKind.Scroll, Endpoint: SemanticScrollEndpoint.End)
                )
            )
                throw new InvalidOperationException(
                    "10,000-row semantic scroll to end was rejected."
                );
            graph.Drain();
            using var last = SceneLayout.Project(composition, viewport, renderer);
            if (!composition.Input.SetScene(last))
                throw new InvalidOperationException("Managed performance end scene was rejected.");
            first.Dispose();
            MeasureVirtualization(
                last,
                composition.SemanticSnapshot()!,
                list,
                ["Issue 9999", "Issue 10000"],
                ref visibleMaximum,
                ref realizedMaximum
            );
            peak = Math.Max(peak, GC.GetTotalMemory(false));
            completedCycles++;
        }
        finally
        {
            // Retain completed work even when a later endpoint or setup fails.
            // Post-GC growth is unknown until the final collection actually runs.
            evidence.Managed = new(
                new(completedCycles, baseline, peak, null, null, ManagedGrowthLimit),
                new(sourceRows, visibleMaximum, realizedMaximum)
            );
        }
    }
    CompactCollect();
    var post = GC.GetTotalMemory(false);
    return new ManagedObservation(
        new(20, baseline, peak, post, post - baseline, ManagedGrowthLimit),
        new(sourceRows, visibleMaximum, realizedMaximum)
    );
}

static void MeasureVirtualization(
    RetainedScene scene,
    SemanticSnapshot snapshot,
    SemanticSnapshot list,
    string[] expectedVisible,
    ref int visibleMaximum,
    ref int realizedMaximum
)
{
    var viewport = scene
        .Boxes.Single(box => box.Identity.ElementId == list.Identity.ElementId)
        .Bounds;
    var items = Flatten(snapshot).Where(node => node.Role == SemanticRole.ListItem).ToArray();
    var rows = items
        .Where(node => node.Role == SemanticRole.ListItem)
        .Select(node =>
            scene.Boxes.Single(box => box.Identity.ElementId == node.Identity.ElementId).Bounds
        )
        .ToArray();
    if (rows.Any(row => row.Height != 30f))
        throw new InvalidOperationException(
            "The fixed 30-pixel virtualization fixture has mismatched row heights: "
                + String.Join(", ", rows.Select(row => row.Height))
        );
    var visible = items
        .Zip(rows)
        .Where(pair =>
            pair.Second.Y < viewport.Y + viewport.Height
            && pair.Second.Y + pair.Second.Height > viewport.Y
        )
        .Select(pair => pair.First.Name)
        .ToArray();
    realizedMaximum = Math.Max(realizedMaximum, rows.Length);
    visibleMaximum = Math.Max(visibleMaximum, visible.Length);
    VirtualizationEndpoint.Verify(expectedVisible, visible, rows.Length);
}

static void PrimeManagedBaseline()
{
    var graph = new ReactiveGraph();
    using var composition = CreateVirtualizationFixture(graph, out _);
    using var renderer = new SkiaSceneRenderer();
    using var scene = WaitForIssues(graph, composition, renderer);
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
    var composition = new Composition(graph, "performance-virtualization");
    var context = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
    theme = context;
    Controls.Column(
        composition.Root,
        context,
        "Performance",
        Style
            .Empty.Set(LayoutProperties.Width, 800f)
            .Set(LayoutProperties.Height, 500f)
            .Set(LayoutProperties.Clip, true)
    );
    var rows = Enumerable.Range(1, 10_000).ToArray();
    if (rows.Length != 10_000)
        throw new InvalidOperationException(
            "Performance source fixture did not contain 10,000 rows."
        );
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
            Controls.Selectable(
                row,
                context,
                "Issue " + value.Value,
                style: Style.Empty.Set(LayoutProperties.MinHeight, 30f)
            );
            return row;
        },
        30f
    );
    return composition;
}

static RetainedScene WaitForIssues(
    ReactiveGraph graph,
    Composition composition,
    SkiaSceneRenderer renderer
)
{
    var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
    RetainedScene? installed = null;
    try
    {
        while (Stopwatch.GetTimestamp() < until)
        {
            graph.Drain();
            var candidate = SceneLayout.Project(composition, new(800, 500, 1), renderer);
            var accepted = false;
            try
            {
                accepted = composition.Input.SetScene(candidate);
                if (accepted)
                {
                    var replaced = installed;
                    installed = candidate;
                    replaced?.Dispose();
                    if (
                        composition.SemanticSnapshot() is { } snapshot
                        && Flatten(snapshot).Any(node => node.Role == SemanticRole.ListItem)
                    )
                    {
                        installed = null;
                        return candidate;
                    }
                }
            }
            finally
            {
                if (!accepted)
                    candidate.Dispose();
            }
            Thread.Sleep(1);
        }
        throw new InvalidOperationException(
            "Fixture issues did not load for managed performance cycles."
        );
    }
    finally
    {
        installed?.Dispose();
    }
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
    Console.Error.WriteLine("Lucent performance verifier: FAIL: " + message);
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

readonly record struct ManagedObservation(
    MemoryObservation Memory,
    VirtualizationObservation Virtualization
);

readonly record struct MemoryObservation(
    int Cycles,
    long BaselineBytes,
    long PeakBytes,
    long? PostGcBytes,
    long? GrowthBytes,
    long LimitBytes
);

readonly record struct VirtualizationObservation(
    int SourceRows,
    int VisibleRowsMaximum,
    int RealizedMaximum
);

readonly record struct Frame(
    string Operation,
    long RequestTimestamp,
    long PresentedTimestamp,
    double EndToEnd,
    double Projection,
    double Raster,
    double Upload,
    double Present,
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
            throw new InvalidOperationException("Malformed performance diagnostic row.");
        var parsed = new Frame(
            fields[1],
            long.Parse(fields[2], CultureInfo.InvariantCulture),
            long.Parse(fields[3], CultureInfo.InvariantCulture),
            double.Parse(fields[4], CultureInfo.InvariantCulture),
            double.Parse(fields[5], CultureInfo.InvariantCulture),
            double.Parse(fields[6], CultureInfo.InvariantCulture),
            double.Parse(fields[7], CultureInfo.InvariantCulture),
            double.Parse(fields[8], CultureInfo.InvariantCulture),
            int.Parse(fields[9], CultureInfo.InvariantCulture),
            int.Parse(fields[10], CultureInfo.InvariantCulture),
            int.Parse(fields[11], CultureInfo.InvariantCulture),
            int.Parse(fields[12], CultureInfo.InvariantCulture),
            uint.Parse(fields[13], CultureInfo.InvariantCulture)
        );
        if (
            parsed.Operation is not ("startup" or "input" or "resize" or "other")
            || parsed.RequestTimestamp < 0
            || parsed.PresentedTimestamp < parsed.RequestTimestamp
            || new[]
            {
                parsed.EndToEnd,
                parsed.Projection,
                parsed.Raster,
                parsed.Upload,
                parsed.Present,
            }.Any(value => !double.IsFinite(value) || value < 0)
            || parsed.Surfaces < 0
            || parsed.Textures < 0
            || parsed.TextBlobs < 0
            || parsed.UiaProviders < 0
        )
            throw new FormatException("Invalid performance frame values.");
        return parsed;
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
            throw new InvalidOperationException("Malformed performance resource row.");
        var parsed = new Resource(
            fields[1],
            int.Parse(fields[2], CultureInfo.InvariantCulture),
            int.Parse(fields[3], CultureInfo.InvariantCulture),
            int.Parse(fields[4], CultureInfo.InvariantCulture),
            int.Parse(fields[5], CultureInfo.InvariantCulture),
            uint.Parse(fields[6], CultureInfo.InvariantCulture)
        );
        if (
            parsed.Phase is not ("pre" or "post")
            || parsed.Surfaces < 0
            || parsed.Textures < 0
            || parsed.TextBlobs < 0
            || parsed.UiaProviders < 0
        )
            throw new FormatException("Invalid performance resource values.");
        return parsed;
    }
}

static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left,
            Top,
            Right,
            Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetClientRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint window);

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
