using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Lucent.Core;
using Lucent.Testing;
using Lucent.Testing.Skia;
using SkiaSharp;

if (args.Length != 3 || args[0] is not ("card" or "starter"))
    throw new ArgumentException(
        "Expected scenario (card/starter), output directory and expected title."
    );
var scenario = args[0];
var output = Path.GetFullPath(args[1]);
var title = args[2];
Directory.CreateDirectory(output);
await File.WriteAllTextAsync(
    Path.Combine(output, "worker-entered.txt"),
    Environment.ProcessId.ToString()
);
var firstFrame = Stopwatch.StartNew();
var mount = Stopwatch.StartNew();

// Static, compiled factories only. Referencing the starter assembly does not run
// its Program.Main. No assembly scanning, projection or runtime compiler is used.
var recipe =
    scenario == "card"
        ? PreviewFeasibility.Components.PreviewCard()
        : NativePreviewStarter.Components.MainView();
var app = await SkiaHeadlessApplication.StartAsync(
    recipe,
    new HeadlessApplicationOptions { Viewport = new(360, 440, 1.5f) }
);
mount.Stop();
try
{
    var imageReady = Stopwatch.StartNew();
    var sources =
        scenario == "card"
            ? new[] { PreviewFeasibility.Assets.Mark, Lucent.Icons.Lucide.LucideIcons.CircleCheck }
            : new[] { Lucent.Icons.Lucide.LucideIcons.CircleCheck };
    foreach (var source in sources)
    {
        var preparation = await app.InvokeAsync(context =>
            context.Composition.Images!.PreloadAsync(
                context.Composition.Root.Scope,
                source,
                new ImageRendition(48, 48)
            )
        );
        var result = await preparation.WaitAsync(TimeSpan.FromSeconds(10));
        Require(
            result.Status == ImagePreloadStatus.Ready,
            "Production image preload did not complete."
        );
    }
    imageReady.Stop();
    using var snapshot = await app.SnapshotAsync();
    var titleNode = snapshot.Require(SemanticRole.Text, title);
    var titleBounds = snapshot.RequireBox(titleNode).Bounds;
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var capture = Stopwatch.StartNew();
    var png = await app.CapturePngAsync(showCaret: false);
    capture.Stop();
    var captureAllocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
    using var bitmap =
        SKBitmap.Decode(png) ?? throw new InvalidOperationException("Invalid captured PNG.");
    Require(
        bitmap.Width == 540 && bitmap.Height == 660,
        "Physical viewport differs from 360x440 at 150%."
    );
    var titlePixels = Region(bitmap, titleBounds, 1.5f);
    Require(
        titlePixels.DarkPixels >= 12,
        "The authored title did not paint attributable glyph pixels."
    );
    var imageNodes = Flatten(snapshot.Scene.Nodes).OfType<ImageSceneNode>().ToArray();
    Require(
        imageNodes.Length >= sources.Length,
        "Prepared artwork is absent from the production scene."
    );
    var iconNode = imageNodes.Single(image => image.ColorMode == ImageColorMode.Monochrome);
    var iconPixels = Region(bitmap, iconNode.Bounds, 1.5f);
    Require(
        scenario == "card"
            ? iconPixels.DarkPixels >= 12
            : iconPixels.LightPixels >= 12 && iconPixels.NonWhitePixels >= 12,
        "The packaged CircleCheck icon did not paint its own stroke region."
    );
    var artwork = new List<object> { new { asset = "lucide-circle-check", region = iconPixels } };
    if (scenario == "card")
    {
        var markSemantic = snapshot.Require(SemanticRole.Image, "Embedded preview mark");
        var markNode = snapshot.SceneNodes(markSemantic).OfType<ImageSceneNode>().Single();
        Require(
            !ReferenceEquals(markNode, iconNode),
            "The mark and icon must identify distinct scene nodes."
        );
        var markPixels = Region(bitmap, markNode.Bounds, 1.5f);
        Require(
            markPixels.BluePixels >= 100,
            "The embedded blue mark did not paint its own semantic region."
        );
        artwork.Add(new { asset = "embedded-mark", region = markPixels });
        var count = snapshot.Require(SemanticRole.Text, "Count: 0");
        Require(
            snapshot.RequireBox(count).Bounds.Y > titleBounds.Y + titleBounds.Height,
            "The real column layout did not place following content below the title."
        );
    }
    await File.WriteAllBytesAsync(Path.Combine(output, "frame.png"), png);
    firstFrame.Stop();
    await File.WriteAllTextAsync(
        Path.Combine(output, "frame-ready.json"),
        JsonSerializer.Serialize(
            new
            {
                scenario,
                title,
                processId = Environment.ProcessId,
                firstFrameMs = firstFrame.Elapsed.TotalMilliseconds,
            }
        )
    );

    var warmCaptures = new List<object>();
    for (var index = 0; index < 3; index++)
    {
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();
        var bytes = await app.CapturePngAsync(showCaret: false);
        timer.Stop();
        warmCaptures.Add(
            new
            {
                elapsedMs = timer.Elapsed.TotalMilliseconds,
                processManagedAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - before,
                pngBytes = bytes.Length,
            }
        );
    }
    using var process = Process.GetCurrentProcess();
    process.Refresh();
    var cpuBefore = process.TotalProcessorTime;
    var idle = Stopwatch.StartNew();
    await Task.Delay(TimeSpan.FromSeconds(3)); // No capture, input, resize or application polling.
    idle.Stop();
    process.Refresh();
    var cpuDelta = process.TotalProcessorTime - cpuBefore;
    var memory = new
    {
        managedHeapBytes = GC.GetTotalMemory(forceFullCollection: false),
        privateBytes = process.PrivateMemorySize64,
        workingSetBytes = process.WorkingSet64,
        peakWorkingSetBytes = process.PeakWorkingSet64,
    };
    var disposal = Stopwatch.StartNew();
    await app.DisposeAsync();
    disposal.Stop();
    await File.WriteAllTextAsync(
        Path.Combine(output, "result.json"),
        JsonSerializer.Serialize(
            new
            {
                scenario,
                title,
                processId = Environment.ProcessId,
                mountMs = mount.Elapsed.TotalMilliseconds,
                imageReadyMs = imageReady.Elapsed.TotalMilliseconds,
                firstFrameMs = firstFrame.Elapsed.TotalMilliseconds,
                captureMs = capture.Elapsed.TotalMilliseconds,
                captureProcessManagedAllocatedBytes = captureAllocated,
                pngBytes = png.Length,
                pngSha256 = Convert.ToHexString(SHA256.HashData(png)),
                titleRegion = titlePixels,
                artworkRegions = artwork,
                warmCaptures,
                memory,
                idleElapsedMs = idle.Elapsed.TotalMilliseconds,
                idleCpuMs = cpuDelta.TotalMilliseconds,
                idleOneCorePercent = cpuDelta.TotalMilliseconds
                    / idle.Elapsed.TotalMilliseconds
                    * 100,
                disposalMs = disposal.Elapsed.TotalMilliseconds,
                disposed = true,
                bounds = new
                {
                    titleBounds.X,
                    titleBounds.Y,
                    titleBounds.Width,
                    titleBounds.Height,
                },
                scope = "offscreen-production-skia-managed-worker",
            },
            new JsonSerializerOptions { WriteIndented = true }
        )
    );
}
finally
{
    await app.DisposeAsync();
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static IEnumerable<SceneNode> Flatten(IEnumerable<SceneNode> nodes)
{
    foreach (var node in nodes)
    {
        yield return node;
        var children = node switch
        {
            ClipSceneNode clip => clip.Children,
            OpacitySceneNode opacity => opacity.Children,
            _ => null,
        };
        if (children is not null)
            foreach (var child in Flatten(children))
                yield return child;
    }
}

static PixelRegion Region(SKBitmap bitmap, LayoutRect bounds, float scale)
{
    var left = Math.Clamp((int)MathF.Floor(bounds.X * scale), 0, bitmap.Width);
    var top = Math.Clamp((int)MathF.Floor(bounds.Y * scale), 0, bitmap.Height);
    var right = Math.Clamp(
        (int)MathF.Ceiling((bounds.X + bounds.Width) * scale),
        left,
        bitmap.Width
    );
    var bottom = Math.Clamp(
        (int)MathF.Ceiling((bounds.Y + bounds.Height) * scale),
        top,
        bitmap.Height
    );
    var bytes = new List<byte>();
    var dark = 0;
    var light = 0;
    var blue = 0;
    var nonWhite = 0;
    for (var y = top; y < bottom; y++)
    for (var x = left; x < right; x++)
    {
        var pixel = bitmap.GetPixel(x, y);
        bytes.AddRange([pixel.Red, pixel.Green, pixel.Blue, pixel.Alpha]);
        if (pixel.Alpha > 200 && pixel.Red < 180 && pixel.Green < 180 && pixel.Blue < 180)
            dark++;
        if (pixel.Alpha > 200 && pixel.Red > 230 && pixel.Green > 230 && pixel.Blue > 230)
            light++;
        if (pixel.Alpha > 200 && pixel.Blue > 180 && pixel.Red < 100 && pixel.Green < 150)
            blue++;
        if (pixel.Alpha > 200 && (pixel.Red < 240 || pixel.Green < 240 || pixel.Blue < 240))
            nonWhite++;
    }
    return new(
        left,
        top,
        right - left,
        bottom - top,
        dark,
        light,
        blue,
        nonWhite,
        Convert.ToHexString(SHA256.HashData(bytes.ToArray()))
    );
}

internal sealed record PixelRegion(
    int X,
    int Y,
    int Width,
    int Height,
    int DarkPixels,
    int LightPixels,
    int BluePixels,
    int NonWhitePixels,
    string Sha256
);
