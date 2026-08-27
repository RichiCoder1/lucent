using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SDL3;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using HarfBuzzSharp;
using HbBuffer = HarfBuzzSharp.Buffer;

[Flags]
internal enum DirtyFacet { None = 0, Layout = 1, Paint = 2, Semantics = 4 }

internal readonly record struct ElementId(string Value);
internal readonly record struct Bounds(int X, int Y, int Width, int Height);
internal sealed record ResolvedStyle(string Background, string Foreground, int CornerRadius, float Opacity = 1, float TranslateX = 0, float TranslateY = 0, float Scale = 1, UiLength? Width = null, UiLength? Height = null, UiLength? Padding = null, UiLength? Gap = null, UiAlignment? Alignment = null, UiTypography? Typography = null, UiColor? Border = null, UiLength? BorderWidth = null, UiShadow? Shadow = null, UiLength? FocusRing = null);
internal sealed record Semantics(string Role, string Name, string? Value = null, string[]? Actions = null, bool Enabled = true, bool Focused = false, string? SuppressionReason = null, bool Selected = false);

internal sealed class StableElement(ElementId id, Bounds bounds, ResolvedStyle style, Semantics semantics)
{
    public ElementId Id { get; } = id;
    public Bounds Bounds { get; set; } = bounds;
    public ResolvedStyle Style { get; set; } = style;
    public Semantics Semantics { get; set; } = semantics;
    public string? Text { get; set; }
    /// <summary>Optional ancestor viewport clip, applied by the generic renderer.</summary>
    public Bounds? Clip { get; set; }
    public List<StableElement> Children { get; } = [];
    public DirtyFacet Dirty { get; private set; } = DirtyFacet.Layout | DirtyFacet.Paint | DirtyFacet.Semantics;
    public void Mark(DirtyFacet facets) => Dirty |= facets;
    public void Clear(DirtyFacet facets) => Dirty &= ~facets;
}

internal sealed record SceneCommand(ElementId Id, Bounds Bounds, string Color, int CornerRadius, ShapedRun? Text = null, float Opacity = 1, float TranslateX = 0, float TranslateY = 0, float Scale = 1, string? Label = null, ResolvedStyle? Style = null, Bounds? Clip = null);
internal sealed class RetainedScene
{
    private readonly Dictionary<ElementId, SceneCommand> _commands = [];
    private readonly List<ElementId> _order = [];
    public IEnumerable<SceneCommand> Commands => _order.Where(_commands.ContainsKey).Select(id => _commands[id]);
    public int Count => _commands.Count;
    public void Upsert(ElementId id, Bounds bounds, ResolvedStyle style, Bounds? clip = null) { Add(id); _commands[id] = new(id, bounds, style.Background, style.CornerRadius, null, style.Opacity, style.TranslateX, style.TranslateY, style.Scale, null, style, clip); }
    public void UpsertLabel(ElementId id, Bounds bounds, ResolvedStyle style, string label, Bounds? clip = null) { Add(id); _commands[id] = new(id, bounds, style.Background, style.CornerRadius, null, style.Opacity, style.TranslateX, style.TranslateY, style.Scale, label, style, clip); }
    public void UpsertText(ElementId id, Bounds bounds, string color, ShapedRun text) { Add(id); _commands[id] = new(id, bounds, color, 0, text); }
    public void Order(IEnumerable<ElementId> ids) { _order.Clear(); _order.AddRange(ids.Where(_commands.ContainsKey)); foreach (var id in _commands.Keys) Add(id); }
    public void Remove(ElementId id) { _commands.Remove(id); _order.Remove(id); }
    private void Add(ElementId id) { if (!_order.Contains(id)) _order.Add(id); }
}

internal sealed class ProjectedSnapshots
{
    public Dictionary<ElementId, Bounds> Layout { get; } = [];
    public Dictionary<ElementId, ResolvedStyle> Style { get; } = [];
    public Dictionary<ElementId, Semantics> Semantics { get; } = [];
    public void Remove(ElementId id) { Layout.Remove(id); Style.Remove(id); Semantics.Remove(id); }
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
            canvas.Save();
            if (command.Clip is { } clip) canvas.ClipRect(new SKRect(clip.X, clip.Y, clip.X + clip.Width, clip.Y + clip.Height));
            var style = command.Style;
            var baseColor = SKColor.Parse(command.Color);
            using var paint = new SKPaint { Color = baseColor.WithAlpha((byte)Math.Round(baseColor.Alpha * Math.Clamp(command.Opacity, 0, 1))), IsAntialias = false };
            var left = command.Bounds.X + command.TranslateX; var top = command.Bounds.Y + command.TranslateY;
            var width = command.Bounds.Width * command.Scale; var height = command.Bounds.Height * command.Scale;
            if (command.Text is not null) command.Text.Draw(canvas, left, top + height - 8, paint);
            else
            {
                var rect = new SKRect(left, top, left + width, top + height);
                if (style?.Shadow is { } shadow)
                {
                    using var shadowPaint = new SKPaint { Color = SKColor.Parse((shadow.Color ?? new UiColor("#000000")).Value).WithAlpha(80), IsAntialias = true };
                    var shadowRect = rect; shadowRect.Offset(shadow.X, shadow.Y);
                    canvas.DrawRoundRect(shadowRect, command.CornerRadius + shadow.Blur / 2, command.CornerRadius + shadow.Blur / 2, shadowPaint);
                }
                canvas.DrawRoundRect(rect, command.CornerRadius, command.CornerRadius, paint);
                if (style?.Border is { } border)
                {
                    using var borderPaint = new SKPaint { Color = SKColor.Parse(border.Value), Style = SKPaintStyle.Stroke, StrokeWidth = style.BorderWidth?.Value ?? 1, IsAntialias = true };
                    canvas.DrawRoundRect(rect, command.CornerRadius, command.CornerRadius, borderPaint);
                }
                if (style?.FocusRing is { } ring)
                {
                    using var ringPaint = new SKPaint { Color = SKColor.Parse(style.Foreground), Style = SKPaintStyle.Stroke, StrokeWidth = ring.Value, IsAntialias = true };
                    var ringRect = rect; ringRect.Inflate(ring.Value / 2f, ring.Value / 2f);
                    canvas.DrawRoundRect(ringRect, command.CornerRadius + ring.Value, command.CornerRadius + ring.Value, ringPaint);
                }
                if (command.Label is { Length: > 0 } label)
                {
                    using var textPaint = new SKPaint { Color = SKColor.Parse(style?.Foreground ?? "#ffffff"), IsAntialias = true };
                    using var face = SKTypeface.FromFamilyName(null, style?.Typography?.Weight ?? 400, (int)SKFontStyleWidth.Normal, (int)SKFontStyleSlant.Upright) ?? throw new InvalidOperationException("Default label typeface unavailable.");
                    using var font = new SKFont(face, style?.Typography?.Size ?? 12);
                    using var shaper = new SKShaper(face); using var buffer = new HbBuffer(); buffer.AddUtf16(label); buffer.GuessSegmentProperties();
                    var shaped = shaper.Shape(buffer, font);
                    using var builder = new SKTextBlobBuilder(); builder.AddPositionedRun(shaped.Codepoints.Select(id => checked((ushort)id)).ToArray(), font, shaped.Points);
                    using var blob = builder.Build() ?? throw new InvalidOperationException("Label shaping produced no blob.");
                    canvas.DrawText(blob, left + (style?.Padding?.Value ?? 0), top + Math.Min(height - 2, (style?.Typography?.Size ?? 12) + (style?.Padding?.Value ?? 0)), textPaint);
                }
            }
            canvas.Restore();
        }
    }
}

internal sealed class SceneProjection(RetainedScene scene, ProjectedSnapshots snapshots)
{
    public ProjectionResult Project(IEnumerable<StableElement> elements)
    {
        var current = elements.ToArray();
        if (current.Any(element => string.IsNullOrWhiteSpace(element.Id.Value)) || current.GroupBy(element => element.Id).Any(group => group.Count() != 1))
            throw new InvalidOperationException("Live element IDs must be nonempty and unique.");
        var layout = 0;
        var paint = 0;
        var semantics = 0;
        foreach (var element in current)
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
                if (element.Text is { } text) scene.UpsertLabel(element.Id, snapshots.Layout[element.Id], snapshots.Style[element.Id], text, element.Clip);
                else scene.Upsert(element.Id, snapshots.Layout[element.Id], snapshots.Style[element.Id], element.Clip);
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

    public void Release(IEnumerable<StableElement> elements)
    {
        foreach (var element in elements) { scene.Remove(element.Id); snapshots.Remove(element.Id); }
    }
}

internal sealed class FrameScheduler
{
    private readonly Action _present;
    private bool _pending;
    public FrameClock Clock { get; }
    public int RequestedFrames { get; private set; }
    public int PresentCalls { get; private set; }
    public FrameScheduler(Action present) { _present = present; Clock = new(this); }
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
        _present();
        PresentCalls++;
    }
}

/// <summary>The only clock allowed to request retained-scene paint frames.</summary>
internal sealed class FrameClock : IDisposable
{
    public FrameScheduler Owner { get; }
    private readonly List<Action<TimeSpan>> _subscribers = [];
    private TimeSpan _now;
    public TimeSpan Now => _now;
    public bool Disposed { get; private set; }
    public int TickCalls { get; private set; }
    public FrameClock(FrameScheduler owner) => Owner = owner;
    public IDisposable Subscribe(Action<TimeSpan> tick)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        _subscribers.Add(tick);
        return new Subscription(_subscribers, tick);
    }
    public void Tick(TimeSpan elapsed)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        if (_subscribers.Count == 0) return;
        _now += elapsed;
        TickCalls++;
        foreach (var subscriber in _subscribers.ToArray()) subscriber(_now);
    }
    public void Dispose() { Disposed = true; _subscribers.Clear(); }
    private sealed class Subscription(List<Action<TimeSpan>> subscribers, Action<TimeSpan> tick) : IDisposable
    {
        public void Dispose() => subscribers.Remove(tick);
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
