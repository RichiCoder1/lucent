using Lucent.Core;
using Lucent.Renderer.Skia;
using SkiaSharp;

try
{
    RetainedScene scene;
    using (var shaper = new SkiaSceneRenderer())
    {
        var ligature = shaper.Shape(new("ffi", "Calibri", 16, "en", TextDirection.LeftToRight, 1));
        var combining = shaper.Shape(new("q\u0307", "Segoe UI", 16, "en", TextDirection.LeftToRight, 1));
        var emoji = shaper.Shape(new("😀", "Segoe UI Emoji", 16, "en", TextDirection.LeftToRight, 1));
        var mixed = shaper.Shape(new("abc العربية", "Segoe UI", 16, "ar", TextDirection.LeftToRight, 1));
        var fallback = shaper.Shape(new("A漢", "Calibri", 16, "ja", TextDirection.LeftToRight, 1));
        var rtlBase = shaper.Shape(new("אב 😀A漢", "Calibri", 16, "he", TextDirection.RightToLeft, 1));
        var issues = shaper.Shape(new("Issues", "Segoe UI", 14, "en", TextDirection.LeftToRight, 1));
        var issue29 = shaper.Shape(new("Issue 29", "Segoe UI", 14, "en", TextDirection.LeftToRight, 1));
        var empty = shaper.Shape(new("", "Segoe UI", 16, "en", TextDirection.LeftToRight, 1));
        if (ligature.Runs.Single().Glyphs.Count >= 3 || combining.Runs.Sum(run => run.Glyphs.Count) > 2 || emoji.Runs.Single().Glyphs.Count != 1 || mixed.Runs.Count < 2 || !mixed.Runs.Any(run => run.Direction == TextDirection.RightToLeft) || fallback.Runs.Count < 2 || fallback.Runs.Select(run => run.TypefaceIdentity).Distinct(StringComparer.Ordinal).Count() < 2 || rtlBase.Runs.Count < 3 || rtlBase.Runs[0].Direction != TextDirection.LeftToRight || rtlBase.Runs[1].Direction != TextDirection.LeftToRight || rtlBase.Runs[^1].Direction != TextDirection.RightToLeft || !rtlBase.Runs.Where(run => run.Direction == TextDirection.RightToLeft).SelectMany(run => run.Glyphs).Any(glyph => glyph.Cluster == 3) || empty is not { Width: 0, Height: 0, Runs.Count: 0 } || new[] { ligature, combining, emoji, mixed, fallback, rtlBase }.SelectMany(text => text.Runs).SelectMany(run => run.Glyphs).Any(glyph => glyph.GlyphId == 0 || !float.IsFinite(glyph.XAdvance)))
            throw new InvalidOperationException("Bounded itemization/fallback corpus failed.");
        AssertMeaningfulSpan(issues); AssertMeaningfulSpan(issue29);
        var graph = new ReactiveGraph(); using var composition = new Composition(graph, "renderer-proof");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("renderer-proof"));
        composition.Root.Present(theme, author: Style.Empty.Set(Arrangement.Clip, true));
        var text = composition.Child(composition.Root, "text");
        text.Present(theme, author: Style.Empty.Set(Arrangement.Height, 30f).Set(SceneProperties.Text, "ffi").Set(SceneProperties.FontFamily, "Calibri").Set(SceneProperties.FontSize, 16f));
        scene = SceneLayout.Project(composition, new(160, 48, 1.25f), shaper);
        if (!scene.Dump().Contains("glyphs=[", StringComparison.Ordinal) || scene.Dump().Contains("ffi", StringComparison.Ordinal)) throw new InvalidOperationException("Scene dump did not expose safe immutable run evidence.");
    }
    using var renderer = new SkiaSceneRenderer();
    using var bitmap = new SKBitmap(200, 60, SKColorType.Rgba8888, SKAlphaType.Premul);
    using (var canvas = new SKCanvas(bitmap)) { canvas.Clear(SKColors.Transparent); renderer.Render(scene, canvas); }
    var painted = Enumerable.Range(0, bitmap.Height).SelectMany(y => Enumerable.Range(0, bitmap.Width).Where(x => bitmap.GetPixel(x, y).Alpha != 0)).ToArray();
    if (painted.Length == 0) throw new InvalidOperationException("Text-only immutable scene did not paint glyph pixels.");
    var extent = painted.Max() - painted.Min() + 1;
    var measured = scene.Boxes.Single(box => box.Text is not null).Text!.Width;
    if (extent < measured * .45f || extent > measured + 4f) throw new InvalidOperationException($"Painted text extent {extent} was inconsistent with measured width {measured:R}.");
    Console.WriteLine("Lucent.Renderer.Skia headless/AOT seam: PASS");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine("Lucent.Renderer.Skia headless/AOT seam: FAIL: " + error.Message); return 1; }

static void AssertMeaningfulSpan(ShapedText text)
{
    var glyphs = text.Runs.Single().Glyphs;
    var span = MathF.Abs(glyphs[^1].X + glyphs[^1].XAdvance - glyphs[0].X);
    if (MathF.Abs(glyphs.Sum(glyph => glyph.XAdvance) - text.Width) > .001f || span < text.Width * .5f || span > text.Width * 1.1f)
        throw new InvalidOperationException("Pinned SKShaper point/advance scaling produced a smushed ordinary-text span.");
}
