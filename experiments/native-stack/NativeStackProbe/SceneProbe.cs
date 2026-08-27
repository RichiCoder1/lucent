using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SDL3;
using SkiaSharp;

[Flags]
internal enum DirtyFacet { None = 0, Layout = 1, Paint = 2, Semantics = 4 }

internal readonly record struct ElementId(string Value);
internal readonly record struct Bounds(int X, int Y, int Width, int Height);
internal sealed record ResolvedStyle(string Background, string Foreground, int CornerRadius);
internal sealed record Semantics(string Role, string Name);

internal sealed class StableElement(ElementId id, Bounds bounds, ResolvedStyle style, Semantics semantics)
{
    public ElementId Id { get; } = id;
    public Bounds Bounds { get; set; } = bounds;
    public ResolvedStyle Style { get; set; } = style;
    public Semantics Semantics { get; set; } = semantics;
    public List<StableElement> Children { get; } = [];
    public DirtyFacet Dirty { get; private set; } = DirtyFacet.Layout | DirtyFacet.Paint | DirtyFacet.Semantics;
    public void Mark(DirtyFacet facets) => Dirty |= facets;
    public void Clear(DirtyFacet facets) => Dirty &= ~facets;
}

internal sealed record SceneCommand(ElementId Id, Bounds Bounds, string Color, int CornerRadius);
internal sealed class RetainedScene
{
    private readonly Dictionary<ElementId, SceneCommand> _commands = [];
    public IEnumerable<SceneCommand> Commands => _commands.Values.OrderBy(command => command.Id.Value, StringComparer.Ordinal);
    public void Upsert(ElementId id, Bounds bounds, ResolvedStyle style) => _commands[id] = new(id, bounds, style.Background, style.CornerRadius);
}

internal sealed class ProjectedSnapshots
{
    public Dictionary<ElementId, Bounds> Layout { get; } = [];
    public Dictionary<ElementId, ResolvedStyle> Style { get; } = [];
    public Dictionary<ElementId, Semantics> Semantics { get; } = [];
}

/// <summary>The one renderer seam: headless and SDL presentation send retained commands to this Skia canvas.</summary>
internal interface ISkiaSceneRenderer { void Render(RetainedScene scene, SKCanvas canvas); }

internal sealed class SkiaSceneRenderer : ISkiaSceneRenderer
{
    public void Render(RetainedScene scene, SKCanvas canvas)
    {
        canvas.Clear(new SKColor(9, 12, 20));
        foreach (var command in scene.Commands)
        {
            using var paint = new SKPaint { Color = SKColor.Parse(command.Color), IsAntialias = false };
            canvas.DrawRoundRect(new SKRect(command.Bounds.X, command.Bounds.Y, command.Bounds.X + command.Bounds.Width, command.Bounds.Y + command.Bounds.Height), command.CornerRadius, command.CornerRadius, paint);
        }
    }
}

internal sealed class SceneProjection(RetainedScene scene, ProjectedSnapshots snapshots)
{
    public ProjectionResult Project(IEnumerable<StableElement> elements)
    {
        var layout = 0;
        var paint = 0;
        var semantics = 0;
        foreach (var element in elements)
        {
            if ((element.Dirty & DirtyFacet.Layout) != 0)
            {
                snapshots.Layout[element.Id] = element.Bounds;
                layout++;
                element.Clear(DirtyFacet.Layout);
                element.Mark(DirtyFacet.Paint); // moved geometry must update its retained command.
            }
            if ((element.Dirty & DirtyFacet.Paint) != 0)
            {
                snapshots.Style[element.Id] = element.Style;
                scene.Upsert(element.Id, snapshots.Layout[element.Id], snapshots.Style[element.Id]);
                paint++;
                element.Clear(DirtyFacet.Paint);
            }
            if ((element.Dirty & DirtyFacet.Semantics) != 0)
            {
                snapshots.Semantics[element.Id] = element.Semantics;
                semantics++;
                element.Clear(DirtyFacet.Semantics);
            }
        }
        return new(layout, paint, semantics);
    }
}

internal sealed class FrameScheduler(Action present)
{
    private bool _pending;
    public int RequestedFrames { get; private set; }
    public int PresentCalls { get; private set; }
    public void Projected(ProjectionResult result)
    {
        if (result.Paint == 0 || _pending) return;
        _pending = true;
        RequestedFrames++;
    }
    public void Pump()
    {
        if (!_pending) return;
        _pending = false;
        present();
        PresentCalls++;
    }
}

internal sealed record ProjectionResult(int Layout, int Paint, int Semantics);
internal sealed record DumpSet(string Elements, string Layout, string Style, string Semantics);
internal sealed record SceneCheckResult(bool Ok, string ArtifactDirectory, ArtifactHash[] Artifacts, bool DumpsMatch, int RasterTolerance, int RasterDifferencePixels, bool IdentityStable, bool FacetsIndependent, bool SnapshotsUpdated, int IdlePresentCalls, int UnrelatedWritePresentCalls, int LayoutWritePresentCalls, int PaintWritePresentCalls, bool NativePresented, int NativeWidth, int NativeHeight, uint NativeDpi, float NativeScale, bool ResizeObserved);
internal sealed record ArtifactHash(string Name, string Sha256);

internal static class SceneProbe
{
    private const int Width = 128;
    private const int Height = 96;

    public static SceneCheckResult Run(string artifactDirectory)
    {
        Directory.CreateDirectory(artifactDirectory);
        var renderer = new SkiaSceneRenderer();

        var headless = Build();
        ApplyScenario(headless);
        var headlessDumps = Dumps(headless);
        var headlessRaster = Raster(headless.Scene, renderer, Width, Height);
        Write(artifactDirectory, "elements.json", headlessDumps.Elements);
        Write(artifactDirectory, "layout.json", headlessDumps.Layout);
        Write(artifactDirectory, "style.json", headlessDumps.Style);
        Write(artifactDirectory, "semantics.json", headlessDumps.Semantics);
        File.WriteAllBytes(Path.Combine(artifactDirectory, "headless-input.png"), headlessRaster.Png);

        var native = Build();
        var nativeInitial = native.Projection.Project(native.Elements);
        if (nativeInitial is not { Layout: 3, Paint: 3, Semantics: 3 }) throw new InvalidOperationException("Native initial projection was incomplete.");
        using var host = new WindowHost("NativeStackProbe scene", Width, Height, SDL.WindowFlags.Hidden | SDL.WindowFlags.Resizable);
        using var presenter = new SdlSkiaPresenter(host.Window, renderer);
        var scheduler = new FrameScheduler(() => presenter.Present(native.Scene));
        scheduler.Pump(); // idle: no projection requested a frame.
        var idlePresentCalls = scheduler.PresentCalls;

        native.Badge.Semantics = native.Badge.Semantics with { Name = "Ready semantic" }; // unrelated to paint.
        native.Badge.Mark(DirtyFacet.Semantics);
        var semanticOnly = native.Projection.Project(native.Elements);
        scheduler.Projected(semanticOnly);
        scheduler.Pump();
        var unrelatedPresentCalls = scheduler.PresentCalls - idlePresentCalls;

        native.Panel.Bounds = new(10, 8, 108, 80);
        native.Panel.Mark(DirtyFacet.Layout);
        var layoutOnly = native.Projection.Project(native.Elements);
        scheduler.Projected(layoutOnly);
        scheduler.Pump();
        var layoutPresentCalls = scheduler.PresentCalls - idlePresentCalls - unrelatedPresentCalls;
        native.Badge.Style = native.Badge.Style with { Background = "#0f766e" };
        native.Badge.Mark(DirtyFacet.Paint);
        var paintOnly = native.Projection.Project(native.Elements);
        scheduler.Projected(paintOnly);
        scheduler.Pump();
        var paintPresentCalls = scheduler.PresentCalls - idlePresentCalls - unrelatedPresentCalls - layoutPresentCalls;
        var nativeCapture = presenter.LastCapture ?? throw new InvalidOperationException("Scheduled native present did not capture the renderer output.");
        File.WriteAllBytes(Path.Combine(artifactDirectory, "native-framebuffer.png"), nativeCapture.Png);

        if (!SDL.SetWindowSize(host.Window, Width * 2, Height * 2) || !SDL.SyncWindow(host.Window)) throw new InvalidOperationException($"SDL scene resize: {SDL.GetError()}");
        presenter.Present(native.Scene); // resize requires a present, but is not a dirty-frame request.
        var resized = presenter.LastCapture ?? throw new InvalidOperationException("Resize present did not capture the renderer output.");

        var nativeDumps = Dumps(native);
        var rasterDifference = DifferenceRgba(headlessRaster.Pixels, nativeCapture.Pixels);
        var parityDumps = headlessDumps == nativeDumps;
        var identityStable = headless.Root.Id.Value == "app.root" && native.Root.Id.Value == "app.root" && headless.Badge.Id == native.Badge.Id;
        var facetsIndependent = layoutOnly is { Layout: 1, Paint: 1, Semantics: 0 } && semanticOnly is { Layout: 0, Paint: 0, Semantics: 1 } && paintOnly is { Layout: 0, Paint: 1, Semantics: 0 };
        var snapshotsUpdated = native.Snapshots.Layout[native.Panel.Id] == new Bounds(10, 8, 108, 80) && native.Snapshots.Semantics[native.Badge.Id].Name == "Ready semantic" && headlessDumps.Layout.Contains("[10,8,108,80]", StringComparison.Ordinal) && headlessDumps.Semantics.Contains("Ready semantic", StringComparison.Ordinal);
        var nativePresented = scheduler.PresentCalls == 2 && presenter.PresentCalls == 3;
        var resizedExpected = resized.Width == Width * 2 && resized.Height == Height * 2;
        var files = Directory.GetFiles(artifactDirectory).OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Select(file => new ArtifactHash(Path.GetFileName(file), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant())).ToArray();
        var result = new SceneCheckResult(rasterDifference == 0 && parityDumps && identityStable && facetsIndependent && snapshotsUpdated && idlePresentCalls == 0 && unrelatedPresentCalls == 0 && layoutPresentCalls == 1 && paintPresentCalls == 1 && nativePresented && resizedExpected && host.ReadDpi() > 0 && SDL.GetWindowDisplayScale(host.Window) > 0,
            Path.GetFullPath(artifactDirectory), files, parityDumps, 0, rasterDifference, identityStable, facetsIndependent, snapshotsUpdated, idlePresentCalls, unrelatedPresentCalls, layoutPresentCalls, paintPresentCalls, nativePresented, resized.Width, resized.Height, host.ReadDpi(), SDL.GetWindowDisplayScale(host.Window), resizedExpected);
        if (!result.Ok) throw new InvalidOperationException($"Scene self-check failed: raster={rasterDifference}; dumps={parityDumps}; facets={facetsIndependent}; snapshots={snapshotsUpdated}; idle={idlePresentCalls}; unrelated={unrelatedPresentCalls}; layout={layoutPresentCalls}; paint={paintPresentCalls}; presented={nativePresented}; resize={resizedExpected}; dpi={host.ReadDpi()}; scale={SDL.GetWindowDisplayScale(host.Window)}.");
        return result;
    }

    private static Seed Build()
    {
        var root = new StableElement(new("app.root"), new(0, 0, Width, Height), new("#091020", "#ffffff", 0), new("window", "Native scene"));
        var panel = new StableElement(new("app.panel"), new(8, 8, 112, 80), new("#1e293b", "#f8fafc", 4), new("group", "Summary"));
        var badge = new StableElement(new("app.badge"), new(16, 28, 48, 24), new("#2563eb", "#ffffff", 3), new("status", "Ready"));
        root.Children.Add(panel);
        panel.Children.Add(badge);
        var scene = new RetainedScene();
        var snapshots = new ProjectedSnapshots();
        return new(root, panel, badge, Flatten(root).ToArray(), scene, new SceneProjection(scene, snapshots), snapshots);
    }

    private static void ApplyScenario(Seed seed)
    {
        var initial = seed.Projection.Project(seed.Elements);
        if (initial is not { Layout: 3, Paint: 3, Semantics: 3 }) throw new InvalidOperationException("Headless initial projection was incomplete.");
        seed.Badge.Semantics = seed.Badge.Semantics with { Name = "Ready semantic" };
        seed.Badge.Mark(DirtyFacet.Semantics);
        seed.Projection.Project(seed.Elements);
        seed.Panel.Bounds = new(10, 8, 108, 80);
        seed.Panel.Mark(DirtyFacet.Layout);
        seed.Projection.Project(seed.Elements);
        seed.Badge.Style = seed.Badge.Style with { Background = "#0f766e" };
        seed.Badge.Mark(DirtyFacet.Paint);
        seed.Projection.Project(seed.Elements);
    }

    private static IEnumerable<StableElement> Flatten(StableElement element)
    {
        yield return element;
        foreach (var child in element.Children) foreach (var descendant in Flatten(child)) yield return descendant;
    }

    private static RasterCapture Raster(RetainedScene scene, ISkiaSceneRenderer renderer, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap)) renderer.Render(scene, canvas);
        return Encode(bitmap, bitmap.Bytes);
    }

    private static RasterCapture Encode(SKBitmap bitmap, byte[] pixels)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new(data.ToArray(), pixels, bitmap.Width, bitmap.Height);
    }

    private static int DifferenceRgba(byte[] left, byte[] right)
    {
        if (left.Length != right.Length || left.Length % 4 != 0) return int.MaxValue;
        var different = 0;
        for (var pixel = 0; pixel < left.Length; pixel += 4)
            if (left[pixel] != right[pixel] || left[pixel + 1] != right[pixel + 1] || left[pixel + 2] != right[pixel + 2] || left[pixel + 3] != right[pixel + 3]) different++;
        return different;
    }

    private static DumpSet Dumps(Seed seed) => new(ElementDump(seed.Root), LayoutDump(seed.Snapshots.Layout), StyleDump(seed.Snapshots.Style), SemanticDump(seed.Snapshots.Semantics));
    private static void Write(string directory, string name, string text) => File.WriteAllText(Path.Combine(directory, name), text, new UTF8Encoding(false));
    private static string ElementDump(StableElement root)
    {
        var output = new StringBuilder();
        void WriteElement(StableElement element)
        {
            output.Append("{\"id\":\"").Append(element.Id.Value).Append("\",\"children\":[");
            for (var index = 0; index < element.Children.Count; index++) { if (index != 0) output.Append(','); WriteElement(element.Children[index]); }
            output.Append("]}");
        }
        WriteElement(root);
        return output.Append('\n').ToString();
    }
    private static string LayoutDump(Dictionary<ElementId, Bounds> snapshots) => "[" + string.Join(',', snapshots.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal).Select(pair => $"{{\"id\":\"{pair.Key.Value}\",\"bounds\":[{pair.Value.X},{pair.Value.Y},{pair.Value.Width},{pair.Value.Height}]}}")) + "]\n";
    private static string StyleDump(Dictionary<ElementId, ResolvedStyle> snapshots) => "[" + string.Join(',', snapshots.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal).Select(pair => $"{{\"id\":\"{pair.Key.Value}\",\"background\":\"{pair.Value.Background}\",\"foreground\":\"{pair.Value.Foreground}\",\"cornerRadius\":{pair.Value.CornerRadius}}}")) + "]\n";
    private static string SemanticDump(Dictionary<ElementId, Semantics> snapshots) => "[" + string.Join(',', snapshots.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal).Select(pair => $"{{\"id\":\"{pair.Key.Value}\",\"role\":\"{pair.Value.Role}\",\"name\":\"{pair.Value.Name}\"}}")) + "]\n";

    private sealed record Seed(StableElement Root, StableElement Panel, StableElement Badge, StableElement[] Elements, RetainedScene Scene, SceneProjection Projection, ProjectedSnapshots Snapshots);
    private sealed record RasterCapture(byte[] Png, byte[] Pixels, int Width, int Height);
}

internal sealed unsafe class SdlSkiaPresenter : IDisposable
{
    private readonly nint _renderer;
    private readonly ISkiaSceneRenderer _sceneRenderer;
    public int PresentCalls { get; private set; }
    public Capture? LastCapture { get; private set; }
    public SdlSkiaPresenter(nint window, ISkiaSceneRenderer sceneRenderer)
    {
        Window = window;
        _sceneRenderer = sceneRenderer;
        _renderer = SDL.CreateRenderer(window, null);
        if (_renderer == 0) throw new InvalidOperationException($"SDL_CreateRenderer: {SDL.GetError()}");
    }
    private nint Window { get; }
    public void Present(RetainedScene scene)
    {
        if (!SDL.GetWindowSize(Window, out var width, out var height) || width <= 0 || height <= 0) throw new InvalidOperationException("SDL scene size was invalid.");
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap)) _sceneRenderer.Render(scene, canvas);
        var texture = SDL.CreateTexture(_renderer, SDL.PixelFormat.ABGR8888, SDL.TextureAccess.Streaming, width, height);
        if (texture == 0) throw new InvalidOperationException($"SDL_CreateTexture: {SDL.GetError()}");
        try
        {
            if (!SDL.UpdateTexture(texture, IntPtr.Zero, bitmap.GetPixels(), bitmap.RowBytes) || !SDL.RenderTexture(_renderer, texture, IntPtr.Zero, IntPtr.Zero)) throw new InvalidOperationException($"SDL scene render: {SDL.GetError()}");
            LastCapture = Readback();
            if (!SDL.RenderPresent(_renderer)) throw new InvalidOperationException($"SDL_RenderPresent: {SDL.GetError()}");
            PresentCalls++;
        }
        finally { SDL.DestroyTexture(texture); }
    }

    private Capture Readback()
    {
        var surface = SDL.RenderReadPixels(_renderer, null);
        if (surface == 0) throw new InvalidOperationException($"SDL_RenderReadPixels: {SDL.GetError()}");
        try
        {
            // SDL's ABGR8888 is the little-endian byte layout RGBA used by SKBitmap.
            var rgba = SDL.ConvertSurface(surface, SDL.PixelFormat.ABGR8888);
            if (rgba == 0) throw new InvalidOperationException($"SDL_ConvertSurface: {SDL.GetError()}");
            try
            {
                var value = (SDL.Surface*)rgba;
                if (value->Width <= 0 || value->Height <= 0 || value->Pitch <= 0 || value->Pixels == 0) throw new InvalidOperationException("SDL readback surface dimensions were invalid.");
                var rowBytes = checked(value->Width * 4);
                if (value->Pitch < rowBytes) throw new InvalidOperationException("SDL readback surface pitch was smaller than RGBA row bytes.");
                var totalBytes = checked(rowBytes * value->Height);
                var pixels = new byte[totalBytes];
                for (var row = 0; row < value->Height; row++)
                {
                    var source = checked((nint)value->Pixels + checked((nint)row * value->Pitch));
                    Marshal.Copy((IntPtr)source, pixels, checked(row * rowBytes), rowBytes);
                }
                using var bitmap = new SKBitmap(value->Width, value->Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                return new Capture(data.ToArray(), pixels, value->Width, value->Height);
            }
            finally { SDL.DestroySurface(rgba); }
        }
        finally { SDL.DestroySurface(surface); }
    }
    public void Dispose() => SDL.DestroyRenderer(_renderer);
    public sealed record Capture(byte[] Png, byte[] Pixels, int Width, int Height);
}
