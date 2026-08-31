using System.Globalization;
using Lucent.Core;
using Lucent.Renderer.Skia;
using SkiaSharp;

try
{
    var corpus = new[]
    {
        new Corpus("ffi", "Calibri", "en", TextDirection.LeftToRight),
        new Corpus("q\u0307", "Segoe UI", "en", TextDirection.LeftToRight),
        new Corpus("😀", "Segoe UI Emoji", "en", TextDirection.LeftToRight),
        new Corpus("A漢", "Calibri", "ja", TextDirection.LeftToRight),
        new Corpus("漢", "Missing Lucent Font", "ja", TextDirection.LeftToRight),
        new Corpus("ffi", "Segoe UI", "en", TextDirection.LeftToRight),
        new Corpus("abc العربية", "Segoe UI", "ar", TextDirection.LeftToRight)
    };
    RetainedScene scene;
    Dictionary<string, TextSnapshot> snapshots;
    using (var shaper = new SkiaSceneRenderer())
    {
        var graph = new ReactiveGraph(); using var composition = new Composition(graph, "renderer-proof");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("renderer-proof"));
        composition.Root.Present(theme, author: Style.Empty.Set(LayoutProperties.Clip, true));
        foreach (var (item, index) in corpus.Select((item, index) => (item, index)))
        {
            var element = composition.Child(composition.Root, "corpus-" + index);
            element.Present(theme, author: Style.Empty.Set(LayoutProperties.Height, 22f).Set(ProjectionProperties.Text, item.Text).Set(TypographyProperties.FontFamily, item.Font).Set(TypographyProperties.FontSize, 16f).Set(TypographyProperties.Language, item.Language).Set(TypographyProperties.Direction, item.Direction));
        }
        scene = SceneLayout.Project(composition, new(240, 160, 1.25f), shaper);
        snapshots = scene.Boxes.Where(box => box.Text is not null).Select(box => box.Text!).ToDictionary(text => text.Identity, Snapshot);
        Assert(snapshots.Count == corpus.Length && snapshots.Values.All(snapshot => snapshot.Glyphs.Count > 0 && snapshot.Glyphs.All(glyph => glyph.GlyphId != 0 && float.IsFinite(glyph.XAdvance))), "SceneLayout did not shape every corpus item.");
        var texts = scene.Boxes.Where(box => box.Text is not null).Select(box => box.Text!).ToArray();
        Assert(texts[0].Runs.Sum(run => run.Glyphs.Count) < 3 && texts[1].Runs.Sum(run => run.Glyphs.Count) <= 2 && texts[2].Runs.Sum(run => run.Glyphs.Count) == 1 && texts[3].Runs.Select(run => run.TypefaceIdentity).Distinct(StringComparer.Ordinal).Count() >= 2 && !texts[4].Runs.Any(run => run.Family.StartsWith("Missing Lucent Font", StringComparison.Ordinal)) && texts[6].Runs.Count >= 2 && texts[6].Runs.Any(run => run.Direction == TextDirection.RightToLeft), "SceneLayout corpus did not preserve ligature, combining, emoji, fallback, missing-font, culture, and mixed-direction shaping.");
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var cultureScene = SceneLayout.Project(composition, new(240, 160, 1.25f), shaper);
            Assert(Same(Snapshot(texts[5]), Snapshot(cultureScene.Boxes.Where(box => box.Text is not null).Select(box => box.Text!).ElementAt(5))), "SceneLayout shaping used current culture rather than explicit language.");
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
        Assert(!scene.Dump().Contains("ffi", StringComparison.Ordinal), "Scene diagnostic dump exposed user text.");
    }
    Assert(scene.Boxes.Where(box => box.Text is not null).All(box => Same(snapshots[box.Text!.Identity], Snapshot(box.Text))), "Disposing the shaping renderer changed immutable glyph identities or positions.");
    using var renderer = new SkiaSceneRenderer();
    using var bitmap = new SKBitmap(300, 200, SKColorType.Rgba8888, SKAlphaType.Premul);
    using (var canvas = new SKCanvas(bitmap)) { canvas.Clear(SKColors.Transparent); renderer.Render(scene, canvas); }
    var painted = Enumerable.Range(0, bitmap.Height).SelectMany(y => Enumerable.Range(0, bitmap.Width).Where(x => bitmap.GetPixel(x, y).Alpha != 0)).ToArray();
    Assert(painted.Length > 0 && painted.Length < bitmap.Width * bitmap.Height, "New renderer did not produce bounded nonempty immutable scene output.");
    foreach (var box in scene.Boxes.Where(box => box.Text is not null)) AssertPainted(bitmap, box.Bounds, scene.Viewport.Scale);
    AssertGradient(renderer);
    Console.WriteLine("Lucent.Renderer.Skia headless/AOT seam: PASS");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine("Lucent.Renderer.Skia headless/AOT seam: FAIL: " + error.Message); return 1; }

static TextSnapshot Snapshot(ShapedText text) => new(text.Identity, text.Runs.Select(run => new RunSnapshot(run.Identity, run.OriginX, run.Baseline, run.Glyphs.ToArray())).ToArray());
static bool Same(TextSnapshot left, TextSnapshot right) => left.Identity == right.Identity && left.Runs.Length == right.Runs.Length && left.Runs.Zip(right.Runs).All(pair => pair.First.Identity == pair.Second.Identity && pair.First.OriginX == pair.Second.OriginX && pair.First.Baseline == pair.Second.Baseline && pair.First.Glyphs.SequenceEqual(pair.Second.Glyphs));
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void AssertGradient(SkiaSceneRenderer renderer)
{
    Brush brush = new LinearGradient(new(0, 0), new(1, 0), [new(0, Color.Parse("#FF0000")), new(1, Color.Parse("#0000FF"))]);
    SKColor? priorMidpoint = null;
    foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f })
    {
        var width = (int)MathF.Ceiling(16 * scale); var height = (int)MathF.Ceiling(4 * scale);
        using var colorSpace = SKColorSpace.CreateSrgb();
        using var pixels = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace));
        using (var canvas = new SKCanvas(pixels))
        {
            canvas.Clear(SKColors.Transparent);
            renderer.Render(new RetainedScene(1, new(16, 4, scale), [], [new PaintSceneNode(new(new(1, 1), SceneNodeKind.Paint), new(0, 0, 16, 4), brush)], []), canvas);
        }
        var left = pixels.GetPixel(Math.Max(0, (int)MathF.Floor(4 * scale)), height / 2);
        var midpoint = pixels.GetPixel(Math.Min(width - 1, (int)MathF.Floor(8 * scale)), height / 2);
        var right = pixels.GetPixel(Math.Min(width - 1, (int)MathF.Floor(12 * scale)), height / 2);
        Assert(left.Red > left.Blue && right.Blue > right.Red && midpoint.Red is > 90 and < 170 && midpoint.Blue is > 90 and < 170 && midpoint.Green < 8 && midpoint.Alpha == byte.MaxValue,
            $"sRGB gradient did not paint expected opaque color progression: left={left}, midpoint={midpoint}, right={right}, scale={scale}.");
        if (priorMidpoint is { } prior) Assert(Math.Abs(prior.Red - midpoint.Red) < 20 && Math.Abs(prior.Blue - midpoint.Blue) < 20, "Gradient midpoint changed across declared scales.");
        priorMidpoint = midpoint;
    }

    Brush hard = new LinearGradient(new(.25f, .25f), new(.75f, .75f), [new(0, Color.Parse("#FF0000")), new(.5f, Color.Parse("#FF0000")), new(.5f, Color.Parse("#0000FF")), new(1, Color.Parse("#0000FF"))]);
    using var hardSpace = SKColorSpace.CreateSrgb();
    using var hardPixels = new SKBitmap(new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul, hardSpace));
    using (var canvas = new SKCanvas(hardPixels)) { canvas.Clear(SKColors.Transparent); renderer.Render(Scene(hard, 16, 16), canvas); }
    Assert(hardPixels.GetPixel(7, 7).Red > 240 && hardPixels.GetPixel(7, 7).Blue < 8 && hardPixels.GetPixel(8, 8).Blue > 240 && hardPixels.GetPixel(8, 8).Red < 8,
        "Adjacent pixels did not preserve the box-relative equal-position hard-stop discontinuity.");

    using var alphaSpace = SKColorSpace.CreateSrgb();
    using var alphaPixels = new SKBitmap(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul, alphaSpace));
    using (var canvas = new SKCanvas(alphaPixels)) { canvas.Clear(SKColors.Transparent); renderer.Render(Scene(Brush.Solid(Color.Parse("#00FF0080")), 4, 4), canvas); }
    var alpha = alphaPixels.GetPixel(2, 2);
    Assert(alpha.Green > 240 && alpha.Alpha is >= 127 and <= 129, "Partially transparent solid Brush did not preserve paint alpha.");
}
static RetainedScene Scene(Brush brush, float width, float height) => new(1, new(width, height, 1), [], [new PaintSceneNode(new(new(1, 1), SceneNodeKind.Paint), new(0, 0, width, height), brush)], []);
static void AssertPainted(SKBitmap bitmap, LayoutRect bounds, float scale)
{
    var left = Math.Clamp((int)MathF.Floor(bounds.X * scale), 0, bitmap.Width); var right = Math.Clamp((int)MathF.Ceiling((bounds.X + bounds.Width) * scale), 0, bitmap.Width);
    var top = Math.Clamp((int)MathF.Floor(bounds.Y * scale), 0, bitmap.Height); var bottom = Math.Clamp((int)MathF.Ceiling((bounds.Y + bounds.Height) * scale), 0, bitmap.Height);
    Assert(right > left && bottom > top && Enumerable.Range(top, bottom - top).SelectMany(y => Enumerable.Range(left, right - left).Select(x => bitmap.GetPixel(x, y))).Any(pixel => pixel.Alpha != 0), "Renderer did not paint inside a required corpus element's device-scaled bounds.");
}
readonly record struct Corpus(string Text, string Font, string Language, TextDirection Direction);
readonly record struct RunSnapshot(string Identity, float OriginX, float Baseline, ShapedGlyph[] Glyphs);
readonly record struct TextSnapshot(string Identity, RunSnapshot[] Runs)
{
    internal IReadOnlyList<ShapedGlyph> Glyphs => Runs.SelectMany(run => run.Glyphs).ToArray();
}
