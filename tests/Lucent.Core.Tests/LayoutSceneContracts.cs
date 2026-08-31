using System.Globalization;
using System.Numerics;
using Lucent.Core;

internal static class LayoutSceneContracts
{
    public static int Run()
    {
        try
        {
            ColorAndBrushValues();
            RowsColumnsClipsAndRounding();
            InvalidBoundsFail();
            ExplicitZeroAndOverflow();
            CrossAxisIntrinsicAndMalformedRuns();
            FixedHeightVirtualization();
            Console.WriteLine("Lucent.Core layout/scene contracts: PASS");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("Lucent.Core layout/scene contracts: FAIL: " + error.Message); return 1; }
    }

    private static void ColorAndBrushValues()
    {
        var color = Color.Parse("#12345678");
        Assert(color == Color.FromArgb(0x78, 0x12, 0x34, 0x56) && color.GetHashCode() == Color.FromArgb(0x78, 0x12, 0x34, 0x56).GetHashCode() && color.ToString() == "#12345678", "Color canonical equality or diagnostic form changed.");
        Assert(Color.TryParse("#123456", out var opaque) && opaque == Color.FromRgb(0x12, 0x34, 0x56), "Color RGB parse changed.");
        foreach (var malformed in new[] { "123456", "#12345", "#1234567", "#123456789", "#gg0000", " #123456" }) Assert(!Color.TryParse(malformed, out _), "Malformed color parsed: " + malformed);
        Expect<FormatException>(() => Color.Parse("#123"));
        Brush solid = opaque;
        Assert(solid.Equals(Brush.Solid(opaque)) && solid.GetHashCode() == Brush.Solid(opaque).GetHashCode() && solid.ToString() == "solid(#123456FF)", "Color-to-Brush conversion or solid value semantics changed.");
        var stops = new List<GradientStop> { new(0, Color.Parse("#ff0000")), new(0, Color.Parse("#00ff00")), new(1, Color.Parse("#0000ff")) };
        var gradient = new LinearGradient(new(0, -0f), new(1, 1), stops);
        var equalGradient = new LinearGradient(new(-0f, 0), new(1, 1), stops);
        stops[0] = new(0, opaque);
        Brush brush = gradient;
        Brush equalBrush = equalGradient;
        Assert(gradient.Equals(equalGradient) && gradient.GetHashCode() == equalGradient.GetHashCode() && brush.Equals(equalBrush) && brush.GetHashCode() == equalBrush.GetHashCode() &&
            gradient.Stops[0].Color == Color.Parse("#ff0000") && brush.ToString() == "linear(0,0 -> 1,1; 0:#FF0000FF,0:#00FF00FF,1:#0000FFFF)", "Gradient copy, equality, hash, or canonical dump changed.");
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var fractional = new LinearGradient(new(.25f, .5f), new(.75f, 1), [new(.25f, opaque), new(.75f, Color.Parse("#abcdef"))]);
            Assert(fractional.ToString() == "linear(0.25,0.5 -> 0.75,1; 0.25:#123456FF,0.75:#ABCDEFFF)", "Gradient dump used current culture.");
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
        Expect<ArgumentException>(() => new LinearGradient(new(0, 0), new(0, 0), [new(0, opaque), new(1, opaque)]));
        Expect<ArgumentException>(() => new LinearGradient(new(float.NaN, 0), new(1, 1), [new(0, opaque), new(1, opaque)]));
        Expect<ArgumentException>(() => new LinearGradient(new(0, 0), new(2, 1), [new(0, opaque), new(1, opaque)]));
        Expect<ArgumentException>(() => new LinearGradient(new(0, 0), new(1, 1), [new(.5f, opaque)]));
        Expect<ArgumentException>(() => new LinearGradient(new(0, 0), new(1, 1), Enumerable.Range(0, 17).Select(index => new GradientStop(index / 16f, opaque)).ToArray()));
        Expect<ArgumentException>(() => new LinearGradient(new(0, 0), new(1, 1), [new(.75f, opaque), new(.25f, opaque)]));
        Expect<ArgumentException>(() => new LinearGradient(new(0, 0), new(1, 1), [new(0, opaque), new(1, Color.Parse("#00000080"))]));
        foreach (var position in new[] { float.NaN, float.PositiveInfinity, -.1f, 1.1f }) Expect<ArgumentOutOfRangeException>(() => new GradientStop(position, opaque).Validate());
    }

    private static void RowsColumnsClipsAndRounding()
    {
        var graph = new ReactiveGraph(); using var composition = new Composition(graph, "layout");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("layout"));
        composition.Root.Present(theme, author: Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Row).Set(LayoutProperties.Spacing, 3f).Set(LayoutProperties.MainAlignment, LayoutAlignment.Center).Set(LayoutProperties.Clip, true).Set(VisualProperties.Background, Color.Parse("#010203")));
        var first = composition.Child(composition.Root, "first");
        first.Present(theme, author: Style.Empty.Set(LayoutProperties.Width, 20f).Set(LayoutProperties.Height, 10f).Set(ProjectionProperties.Text, "ffi").Set(TypographyProperties.FontSize, 10f));
        var second = composition.Child(composition.Root, "second");
        second.Present(theme, author: Style.Empty.Set(LayoutProperties.Width, 20f).Set(LayoutProperties.Height, 10f).Set(LayoutProperties.Scroll, new ScrollOffset(1, 0)).Set(ProjectionProperties.Text, "q\u0307"));
        var shaper = new ProbeShaper();
        var firstScene = SceneLayout.Project(composition, new(51, 20, 1.25f), shaper);
        var secondScene = SceneLayout.Project(composition, new(51, 20, 1.25f), shaper);
        Assert(StableDump(firstScene) == StableDump(secondScene) && firstScene.Generation + 1 == secondScene.Generation && firstScene.Boxes.Count == 3 && firstScene.Nodes.Single() is ClipSceneNode, "Retained scene was not stable or clipped.");
        Assert(firstScene.Boxes[1].Bounds.X == 4f && firstScene.Boxes[2].Bounds.X == 27.2f && firstScene.Boxes[1].Text!.Identity == secondScene.Boxes[1].Text!.Identity, "Row alignment/edge rounding or shaped identity diverged.");
        var original = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR"); Assert(StableDump(firstScene) == StableDump(SceneLayout.Project(composition, new(51, 20, 1.25f), shaper)), "Scene dump was culture-sensitive."); }
        finally { CultureInfo.CurrentCulture = original; }
        Assert(!firstScene.Dump().Contains("ffi", StringComparison.Ordinal) && shaper.Requests == 6, "Scene dump leaked text or layout did not share shaped results: " + shaper.Requests);
    }

    private static void InvalidBoundsFail()
    {
        var graph = new ReactiveGraph(); using var composition = new Composition(graph, "invalid-layout"); var theme = new ThemeContext(composition.Root.Scope, new Theme("layout"));
        composition.Root.Present(theme, author: Style.Empty.Set(LayoutProperties.Width, float.NaN));
        var shaper = new ProbeShaper();
        Expect<ArgumentOutOfRangeException>(() => SceneLayout.Project(composition, new(10, 10, 1), shaper));
        Expect<ArgumentOutOfRangeException>(() => SceneLayout.Project(composition, new(0, 10, 1), shaper));
        composition.Dispose();
        Expect<ObjectDisposedException>(() => SceneLayout.Project(composition, new(10, 10, 1), shaper));
    }

    private static void ExplicitZeroAndOverflow()
    {
        var graph = new ReactiveGraph(); using var composition = new Composition(graph, "zero-layout"); var theme = new ThemeContext(composition.Root.Scope, new Theme("layout"));
        composition.Root.Present(theme, author: Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Row).Set(LayoutProperties.Clip, true));
        var zero = composition.Child(composition.Root, "zero"); zero.Present(theme, author: Style.Empty.Set(LayoutProperties.Width, 0f).Set(LayoutProperties.Height, 0f));
        var scene = SceneLayout.Project(composition, new(10, 10, 1), new ProbeShaper());
        Assert(scene.Boxes.Single(box => box.Identity.ElementId == zero.Id).Bounds is { Width: 0, Height: 0 }, "Explicit zero was stretched as auto.");
        var mutable = scene.Nodes as ICollection<SceneNode>;
        Assert(mutable is null || mutable.IsReadOnly, "Scene nodes can be cast back to a mutable collection.");
        var overflow = composition.Child(composition.Root, "overflow"); overflow.Present(theme, author: Style.Empty.Set(LayoutProperties.Width, float.MaxValue).Set(LayoutProperties.Height, 1f));
        Expect<ArgumentOutOfRangeException>(() => SceneLayout.Project(composition, new(float.MaxValue, 10, 2), new ProbeShaper()));
        var badGraph = new ReactiveGraph(); using var bad = new Composition(badGraph, "bad-layout"); var badTheme = new ThemeContext(bad.Root.Scope, new Theme("layout"));
        bad.Root.Present(badTheme, author: Style.Empty.Set(LayoutProperties.MinWidth, 2f).Set(LayoutProperties.MaxWidth, 1f));
        Expect<ArgumentOutOfRangeException>(() => SceneLayout.Project(bad, new(10, 10, 1), new ProbeShaper()));
        var emptyGraph = new ReactiveGraph(); using var empty = new Composition(emptyGraph, "empty-layout"); var emptyTheme = new ThemeContext(empty.Root.Scope, new Theme("layout")); empty.Root.Present(emptyTheme);
        Assert(SceneLayout.Project(empty, new(10, 10, 1), new ProbeShaper()).Boxes.Count == 1, "Empty tree was not bounded to its root.");
    }

    private static void CrossAxisIntrinsicAndMalformedRuns()
    {
        foreach (var alignment in new[] { LayoutAlignment.Start, LayoutAlignment.Center, LayoutAlignment.End, LayoutAlignment.Stretch })
        {
            var graph = new ReactiveGraph(); using var composition = new Composition(graph, "cross-" + alignment); var theme = new ThemeContext(composition.Root.Scope, new Theme("layout"));
            composition.Root.Present(theme, author: Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Row).Set(LayoutProperties.CrossAlignment, alignment));
            var auto = composition.Child(composition.Root, "auto"); auto.Present(theme, author: Style.Empty.Set(LayoutProperties.Width, 2f));
            var explicitChild = composition.Child(composition.Root, "explicit"); explicitChild.Present(theme, author: Style.Empty.Set(LayoutProperties.Width, 2f).Set(LayoutProperties.Height, 5f));
            var textChild = composition.Child(composition.Root, "text"); textChild.Present(theme, author: Style.Empty.Set(ProjectionProperties.Text, "abcd").Set(TypographyProperties.FontSize, 2f));
            var boxes = SceneLayout.Project(composition, new(20, 10, 1), new ProbeShaper()).Boxes;
            Assert(boxes.Single(box => box.Identity.ElementId == auto.Id).Bounds.Height == (alignment == LayoutAlignment.Stretch ? 10 : 0) && boxes.Single(box => box.Identity.ElementId == explicitChild.Id).Bounds.Height == 5 && boxes.Single(box => box.Identity.ElementId == textChild.Id).Bounds.Height == (alignment == LayoutAlignment.Stretch ? 10 : 2), "Cross-axis auto/explicit/text resolution changed under " + alignment);
        }
        var nestedGraph = new ReactiveGraph(); using var nested = new Composition(nestedGraph, "nested"); var nestedTheme = new ThemeContext(nested.Root.Scope, new Theme("layout"));
        nested.Root.Present(nestedTheme, author: Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Row).Set(LayoutProperties.CrossAlignment, LayoutAlignment.Start));
        var column = nested.Child(nested.Root, "column"); column.Present(nestedTheme, author: Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Column));
        var first = nested.Child(column, "first"); first.Present(nestedTheme, author: Style.Empty.Set(LayoutProperties.Width, 4f).Set(LayoutProperties.Height, 2f));
        var second = nested.Child(column, "second"); second.Present(nestedTheme, author: Style.Empty.Set(LayoutProperties.Width, 7f).Set(LayoutProperties.Height, 3f));
        Assert(SceneLayout.Project(nested, new(20, 20, 1), new ProbeShaper()).Boxes.Single(box => box.Identity.ElementId == column.Id).Bounds is { Width: 7, Height: 5 }, "Nested row/column intrinsic measurement failed.");
        var stretchGraph = new ReactiveGraph(); using var stretch = new Composition(stretchGraph, "nested-stretch"); var stretchTheme = new ThemeContext(stretch.Root.Scope, new Theme("layout"));
        stretch.Root.Present(stretchTheme, author: Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Row).Set(LayoutProperties.CrossAlignment, LayoutAlignment.Stretch).Set(LayoutProperties.Clip, true));
        var autoColumn = stretch.Child(stretch.Root, "auto-column"); autoColumn.Present(stretchTheme, author: Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Column).Set(LayoutProperties.MaxHeight, 6f));
        var overColumn = stretch.Child(stretch.Root, "over-column"); overColumn.Present(stretchTheme, author: Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Column).Set(LayoutProperties.Height, 12f));
        var stretchBoxes = SceneLayout.Project(stretch, new(20, 10, 1), new ProbeShaper()).Boxes;
        Assert(stretchBoxes.Single(box => box.Identity.ElementId == autoColumn.Id).Bounds.Height == 6 && stretchBoxes.Single(box => box.Identity.ElementId == overColumn.Id).Bounds.Height == 12, "Nested constrained stretch or explicit overconstraint was resolved incorrectly.");
        var scrollGraph = new ReactiveGraph(); using var scroll = new Composition(scrollGraph, "scroll-overflow"); var scrollTheme = new ThemeContext(scroll.Root.Scope, new Theme("layout"));
        scroll.Root.Present(scrollTheme, author: Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Row).Set(LayoutProperties.Scroll, new ScrollOffset(float.MaxValue, 0))); var scrollChild = scroll.Child(scroll.Root, "child"); scrollChild.Present(scrollTheme, author: Style.Empty.Set(LayoutProperties.Width, 1f).Set(LayoutProperties.Height, 1f));
        Expect<ArgumentOutOfRangeException>(() => SceneLayout.Project(scroll, new(10, 10, 2), new ProbeShaper()));
        var glyph = new ShapedGlyph((uint)ushort.MaxValue + 1, 0, 0, 0, 1, 0, 0);
        Expect<ArgumentException>(() => new ShapedRun("bad", "probe", 400, 5, 0, "fingerprint", 0, "source", TextDirection.LeftToRight, "en", 1, 1, 1, -1, 0, 1, [glyph]));
        var goodGlyph = new ShapedGlyph(1, 0, 0, 0, 1, 0, 0);
        Expect<InvalidOperationException>(() => new ShapedText("bad-text", 1, 1, [new ShapedRun("shifted", "probe", 400, 5, 0, "fingerprint", 0, "source", TextDirection.LeftToRight, "en", 1, 1, 1, -1, 0, 1, [goodGlyph])]).Validate(new("x", "probe", 1, "en", TextDirection.LeftToRight, 1)));
    }

    private static void FixedHeightVirtualization()
    {
        var graph = new ReactiveGraph(); using var composition = new Composition(graph, "virtual-layout");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("virtual-layout"));
        Controls.Column(composition.Root, theme, "root");
        var viewport = composition.Child(composition.Root, "viewport");
        Controls.ScrollViewport(viewport, theme, "Rows", style: Style.Empty.Set(LayoutProperties.Width, 120f).Set(LayoutProperties.Height, 30f));
        var values = graph.Signal(Enumerable.Range(1, 10_000).ToArray(), "virtual-values");
        var list = Controls.VirtualizedList(viewport, theme, "rows", "Rows", () => values.Value, value => value, (value, context) =>
        {
            var row = context.Element("row"); Controls.Selectable(row, theme, "row " + value); return row;
        }, 30f);
        graph.Drain();
        var shaper = new ProbeShaper();
        var scene = SceneLayout.Project(composition, new(120, 30, 1), shaper);
        var input = composition.Input;
        Assert(input.SetScene(scene) && list.SourceCount == 10_000 && list.Items.Count == 3 && scene.Boxes.Single(box => box.Identity.ElementId == list.Region.Id).Bounds.Height == 300_000,
            "Virtual list did not realize a bounded fixed-height initial window.");
        list.SetRowHeight(20f); graph.Drain(); scene = SceneLayout.Project(composition, new(120, 30, 1), shaper);
        Assert(input.SetScene(scene) && list.RowHeight == 20f && list.Items.Count == 4 && scene.Boxes.Single(box => box.Identity.ElementId == list.Region.Id).Bounds.Height == 200_000,
            "A live fixed-row-height update did not retain a bounded virtual list.");
        list.SetRowHeight(30f); graph.Drain(); scene = SceneLayout.Project(composition, new(120, 30, 1), shaper);
        Assert(input.SetScene(scene), "Restored virtual row height scene was rejected.");
        Assert(input.ScrollSemantic(scene.Input.Single(item => item.Identity.ElementId == viewport.Id).Identity, new(SemanticCommandKind.Scroll, Endpoint: SemanticScrollEndpoint.End)), "Virtual list could not scroll to its end.");
        graph.Drain(); scene = SceneLayout.Project(composition, new(120, 30, 1), shaper);
        Assert(input.SetScene(scene) && list.Items.Count == 3 && composition.SemanticDump().Contains("suppressions=[]", StringComparison.Ordinal), "Virtual end window or semantic dump was not bounded.");
        var before = list.Items.Select(item => item.Id).ToArray();
        var reordered = values.Value.ToArray(); (reordered[^1], reordered[^2]) = (reordered[^2], reordered[^1]); values.Value = reordered; graph.Drain();
        scene = SceneLayout.Project(composition, new(120, 30, 1), shaper);
        Assert(input.SetScene(scene) && list.Items.Select(item => item.Id).SequenceEqual([before[0], before[2], before[1]]), "Keyed virtual rows did not retain their identities across a move.");
        values.Value = reordered[..^1]; graph.Drain(); scene = SceneLayout.Project(composition, new(120, 30, 1), shaper);
        Assert(!input.SetScene(scene), "A removed end row did not force bounded scroll reconciliation.");
        graph.Drain(); scene = SceneLayout.Project(composition, new(120, 30, 1), shaper);
        Assert(input.SetScene(scene) && list.SourceCount == 9_999 && list.Items.Count <= 3, "Removal left an unbounded virtual window.");
    }

    private sealed class ProbeShaper : ITextShaper
    {
        public int Requests { get; private set; }
        public ShapedText Shape(TextMeasureRequest request)
        {
            Requests++;
            var runWidth = request.Text.Length * request.FontSize / 2;
            var glyphs = new[] { new ShapedGlyph(1, 0, 0, 0, runWidth, 0, 0) };
            var run = new ShapedRun("run-" + request.Text.Length.ToString(CultureInfo.InvariantCulture), "probe", 400, 5, 0, "probe-fingerprint", 0, "probe#0", request.Direction, request.Language, request.FontSize, 0, request.FontSize, -request.FontSize, 0, runWidth, glyphs);
            return new("shape-" + request.Text.Length.ToString(CultureInfo.InvariantCulture) + "-" + request.Language, runWidth, request.FontSize, [run]);
        }
    }
    private static string StableDump(RetainedScene scene)
    {
        var lines = scene.Dump().Split('\n');
        lines[0] = lines[0][(lines[0].IndexOf(" viewport=", StringComparison.Ordinal) + 1)..];
        return string.Join('\n', lines);
    }
    private static void Expect<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
