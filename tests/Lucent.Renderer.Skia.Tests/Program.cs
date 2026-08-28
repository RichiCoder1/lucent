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
        var empty = shaper.Shape(new("", "Segoe UI", 16, "en", TextDirection.LeftToRight, 1));
        if (ligature.Runs.Single().Glyphs.Count >= 3 || combining.Runs.Sum(run => run.Glyphs.Count) > 2 || emoji.Runs.Single().Glyphs.Count != 1 || mixed.Runs.Count < 2 || !mixed.Runs.Any(run => run.Direction == TextDirection.RightToLeft) || fallback.Runs.Count < 2 || fallback.Runs.Select(run => run.TypefaceIdentity).Distinct(StringComparer.Ordinal).Count() < 2 || rtlBase.Runs.Count < 3 || rtlBase.Runs[0].Direction != TextDirection.LeftToRight || rtlBase.Runs[1].Direction != TextDirection.LeftToRight || rtlBase.Runs[^1].Direction != TextDirection.RightToLeft || !rtlBase.Runs.Where(run => run.Direction == TextDirection.RightToLeft).SelectMany(run => run.Glyphs).Any(glyph => glyph.Cluster == 3) || empty is not { Width: 0, Height: 0, Runs.Count: 0 } || new[] { ligature, combining, emoji, mixed, fallback, rtlBase }.SelectMany(text => text.Runs).SelectMany(run => run.Glyphs).Any(glyph => glyph.GlyphId == 0 || !float.IsFinite(glyph.XAdvance)))
            throw new InvalidOperationException("Bounded itemization/fallback corpus failed.");
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
    if (!Enumerable.Range(0, bitmap.Height).SelectMany(y => Enumerable.Range(0, bitmap.Width).Select(x => bitmap.GetPixel(x, y))).Any(pixel => pixel.Alpha != 0)) throw new InvalidOperationException("Text-only immutable scene did not paint glyph pixels.");
    Console.WriteLine("Lucent.Renderer.Skia headless/AOT seam: PASS");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine("Lucent.Renderer.Skia headless/AOT seam: FAIL: " + error.Message); return 1; }
