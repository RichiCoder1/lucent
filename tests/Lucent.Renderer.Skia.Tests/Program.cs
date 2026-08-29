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
        composition.Root.Present(theme, author: Style.Empty.Set(Arrangement.Clip, true));
        foreach (var (item, index) in corpus.Select((item, index) => (item, index)))
        {
            var element = composition.Child(composition.Root, "corpus-" + index);
            element.Present(theme, author: Style.Empty.Set(Arrangement.Height, 22f).Set(SceneProperties.Text, item.Text).Set(SceneProperties.FontFamily, item.Font).Set(SceneProperties.FontSize, 16f).Set(SceneProperties.Language, item.Language).Set(SceneProperties.TextDirection, item.Direction));
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
    Console.WriteLine("Lucent.Renderer.Skia headless/AOT seam: PASS");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine("Lucent.Renderer.Skia headless/AOT seam: FAIL: " + error.Message); return 1; }

static TextSnapshot Snapshot(ShapedText text) => new(text.Identity, text.Runs.Select(run => new RunSnapshot(run.Identity, run.OriginX, run.Baseline, run.Glyphs.ToArray())).ToArray());
static bool Same(TextSnapshot left, TextSnapshot right) => left.Identity == right.Identity && left.Runs.Length == right.Runs.Length && left.Runs.Zip(right.Runs).All(pair => pair.First.Identity == pair.Second.Identity && pair.First.OriginX == pair.Second.OriginX && pair.First.Baseline == pair.Second.Baseline && pair.First.Glyphs.SequenceEqual(pair.Second.Glyphs));
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
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
