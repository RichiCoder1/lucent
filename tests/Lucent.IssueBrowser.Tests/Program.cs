using Lucent.Core;
using Lucent.Renderer.Skia;
using SkiaSharp;

try
{
    var first = Dump();
    if (first != Dump()) throw new InvalidOperationException("Issue Browser composition is not deterministic.");
    if (!first.Contains("issue-browser.header", StringComparison.Ordinal) || !first.Contains("issue-browser.issue-list", StringComparison.Ordinal) ||
        first.Split('\n').Count(line => line.Contains("issue-browser.issue-row", StringComparison.Ordinal)) != 3 || first.Contains("issue-browser.loading\"", StringComparison.Ordinal) ||
        first.Contains("retained composition", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Issue Browser did not produce only its active static/state-driven structure.");
    var snapshot = Snapshot();
    if (!first.Contains("property name=\"surface\" winner=\"author:token:page-surface:theme\"#0", StringComparison.Ordinal) ||
        !first.Contains("property name=\"opacity\" winner=\"author\"#0[Selected]", StringComparison.Ordinal) ||
        !first.Contains("behavior name=\"issue-row-semantics\" ownership=Action, Semantics", StringComparison.Ordinal) ||
        first.Split('\n').Count(line => line.Contains("semantic element=", StringComparison.Ordinal)) != 6 ||
        first.Contains("Implement retained", StringComparison.Ordinal) ||
        !Flatten(snapshot).Any(node => node.Actions == SemanticAction.Select))
        throw new InvalidOperationException("Issue Browser did not consume typed presentation, behavior, and semantic contracts without leaking issue values.");
    ShapeLayoutAndPaint();
    Console.WriteLine("Lucent.IssueBrowser composition contract: PASS");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("Lucent.IssueBrowser composition contract: FAIL: " + exception.Message);
    return 1;
}

static string Dump()
{
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(graph);
    graph.Drain();
    return composition.Dump();
}

static SemanticSnapshot Snapshot()
{
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(graph);
    graph.Drain();
    return composition.SemanticSnapshot()!;
}

static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot snapshot)
{
    yield return snapshot;
    foreach (var child in snapshot.Children)
        foreach (var node in Flatten(child)) yield return node;
}

static void ShapeLayoutAndPaint()
{
    using var renderer = new SkiaSceneRenderer();
    var ligature = renderer.Shape(new("ffi", "Calibri", 16, "en", TextDirection.LeftToRight, 1));
    var combining = renderer.Shape(new("q\u0307", "Segoe UI", 16, "en", TextDirection.LeftToRight, 1));
    var emoji = renderer.Shape(new("😀", "Segoe UI Emoji", 16, "en", TextDirection.LeftToRight, 1));
    var rtl = renderer.Shape(new("العَرَبِيَّة", "Segoe UI", 16, "ar", TextDirection.RightToLeft, 1));
    var fallback = renderer.Shape(new("漢", "Missing Lucent Font", 16, "ja", TextDirection.LeftToRight, 1));
    var language = renderer.Shape(new("ffi", "Segoe UI", 16, "tr", TextDirection.LeftToRight, 1));
    if (ligature.Runs.Sum(run => run.Glyphs.Count) >= 3 || combining.Runs.Sum(run => run.Glyphs.Count) > 2 || emoji.Runs.Sum(run => run.Glyphs.Count) != 1 || rtl.Runs.Single().Glyphs[0].Cluster <= rtl.Runs.Single().Glyphs[^1].Cluster || fallback.Runs.SelectMany(run => run.Glyphs).Any(glyph => glyph.GlyphId == 0) || fallback.Runs.Any(run => run.Family.StartsWith("Missing Lucent Font", StringComparison.Ordinal)) || ligature.Identity == language.Identity)
        throw new InvalidOperationException("Pinned HarfBuzz run corpus failed.");
    var old = System.Globalization.CultureInfo.CurrentCulture;
    try
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
        if (ligature.Identity != renderer.Shape(new("ffi", "Calibri", 16, "en", TextDirection.LeftToRight, 1)).Identity)
            throw new InvalidOperationException("Shaping used current culture rather than explicit language.");
    }
    finally { System.Globalization.CultureInfo.CurrentCulture = old; }

    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(graph);
    var scene = SceneLayout.Project(composition, new(800, 500, 1.25f), renderer);
    if (!scene.Dump().Contains("shape=", StringComparison.Ordinal) || scene.Dump().Contains("Issue 29", StringComparison.Ordinal) || scene.Boxes.Count < 6)
        throw new InvalidOperationException($"Issue Browser did not produce a diagnostic-safe shaped retained scene: boxes={scene.Boxes.Count}; dump={scene.Dump()}");
    using var bitmap = new SKBitmap(1000, 625, SKColorType.Rgba8888, SKAlphaType.Premul);
    using (var canvas = new SKCanvas(bitmap)) { canvas.Clear(SKColors.Transparent); renderer.Render(scene, canvas); }
    if (!Enumerable.Range(0, bitmap.Width).Any(x => bitmap.GetPixel(x, 0).Alpha != 0))
        throw new InvalidOperationException("Headless Core-to-Skia scene did not paint.");
}
