using System.Globalization;
using Lucent.Core;

internal static class LayoutSceneContracts
{
    public static int Run()
    {
        try
        {
            RowsColumnsClipsAndRounding();
            InvalidBoundsFail();
            ExplicitZeroAndOverflow();
            CrossAxisIntrinsicAndMalformedRuns();
            Console.WriteLine("Lucent.Core layout/scene contracts: PASS");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("Lucent.Core layout/scene contracts: FAIL: " + error.Message); return 1; }
    }

    private static void RowsColumnsClipsAndRounding()
    {
        var graph = new ReactiveGraph(); using var composition = new Composition(graph, "layout");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("layout"));
        composition.Root.Present(theme, author: Style.Empty.Set(Arrangement.Axis, LayoutAxis.Row).Set(Arrangement.Spacing, 3f).Set(Arrangement.MainAlignment, LayoutAlignment.Center).Set(Arrangement.Clip, true).Set(SceneProperties.Fill, 0xff010203U));
        var first = composition.Child(composition.Root, "first");
        first.Present(theme, author: Style.Empty.Set(Arrangement.Width, 20f).Set(Arrangement.Height, 10f).Set(SceneProperties.Text, "ffi").Set(SceneProperties.FontSize, 10f));
        var second = composition.Child(composition.Root, "second");
        second.Present(theme, author: Style.Empty.Set(Arrangement.Width, 20f).Set(Arrangement.Height, 10f).Set(Arrangement.Scroll, new ScrollOffset(1, 0)).Set(SceneProperties.Text, "q\u0307"));
        var shaper = new ProbeShaper();
        var firstScene = SceneLayout.Project(composition, new(51, 20, 1.25f), shaper);
        var secondScene = SceneLayout.Project(composition, new(51, 20, 1.25f), shaper);
        Assert(firstScene.Dump() == secondScene.Dump() && firstScene.Boxes.Count == 3 && firstScene.Nodes.Single() is ClipSceneNode, "Retained scene was not stable or clipped.");
        Assert(firstScene.Boxes[1].Bounds.X == 4f && firstScene.Boxes[2].Bounds.X == 27.2f && firstScene.Boxes[1].Text!.Identity == secondScene.Boxes[1].Text!.Identity, "Row alignment/edge rounding or shaped identity diverged.");
        var original = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR"); Assert(firstScene.Dump() == SceneLayout.Project(composition, new(51, 20, 1.25f), shaper).Dump(), "Scene dump was culture-sensitive."); }
        finally { CultureInfo.CurrentCulture = original; }
        Assert(!firstScene.Dump().Contains("ffi", StringComparison.Ordinal) && shaper.Requests == 6, "Scene dump leaked text or layout did not share shaped results: " + shaper.Requests);
    }

    private static void InvalidBoundsFail()
    {
        var graph = new ReactiveGraph(); using var composition = new Composition(graph, "invalid-layout"); var theme = new ThemeContext(composition.Root.Scope, new Theme("layout"));
        composition.Root.Present(theme, author: Style.Empty.Set(Arrangement.Width, float.NaN));
        var shaper = new ProbeShaper();
        Expect<ArgumentOutOfRangeException>(() => SceneLayout.Project(composition, new(10, 10, 1), shaper));
        Expect<ArgumentOutOfRangeException>(() => SceneLayout.Project(composition, new(0, 10, 1), shaper));
        composition.Dispose();
        Expect<ObjectDisposedException>(() => SceneLayout.Project(composition, new(10, 10, 1), shaper));
    }

    private static void ExplicitZeroAndOverflow()
    {
        var graph = new ReactiveGraph(); using var composition = new Composition(graph, "zero-layout"); var theme = new ThemeContext(composition.Root.Scope, new Theme("layout"));
        composition.Root.Present(theme, author: Style.Empty.Set(Arrangement.Axis, LayoutAxis.Row).Set(Arrangement.Clip, true));
        var zero = composition.Child(composition.Root, "zero"); zero.Present(theme, author: Style.Empty.Set(Arrangement.Width, 0f).Set(Arrangement.Height, 0f));
        var scene = SceneLayout.Project(composition, new(10, 10, 1), new ProbeShaper());
        Assert(scene.Boxes.Single(box => box.Identity.ElementId == zero.Id).Bounds is { Width: 0, Height: 0 }, "Explicit zero was stretched as auto.");
        var mutable = scene.Nodes as ICollection<SceneNode>;
        Assert(mutable is null || mutable.IsReadOnly, "Scene nodes can be cast back to a mutable collection.");
        var overflow = composition.Child(composition.Root, "overflow"); overflow.Present(theme, author: Style.Empty.Set(Arrangement.Width, float.MaxValue).Set(Arrangement.Height, 1f));
        Expect<ArgumentOutOfRangeException>(() => SceneLayout.Project(composition, new(float.MaxValue, 10, 2), new ProbeShaper()));
        var badGraph = new ReactiveGraph(); using var bad = new Composition(badGraph, "bad-layout"); var badTheme = new ThemeContext(bad.Root.Scope, new Theme("layout"));
        bad.Root.Present(badTheme, author: Style.Empty.Set(Arrangement.MinWidth, 2f).Set(Arrangement.MaxWidth, 1f));
        Expect<ArgumentOutOfRangeException>(() => SceneLayout.Project(bad, new(10, 10, 1), new ProbeShaper()));
        var emptyGraph = new ReactiveGraph(); using var empty = new Composition(emptyGraph, "empty-layout"); var emptyTheme = new ThemeContext(empty.Root.Scope, new Theme("layout")); empty.Root.Present(emptyTheme);
        Assert(SceneLayout.Project(empty, new(10, 10, 1), new ProbeShaper()).Boxes.Count == 1, "Empty tree was not bounded to its root.");
    }

    private static void CrossAxisIntrinsicAndMalformedRuns()
    {
        foreach (var alignment in new[] { LayoutAlignment.Start, LayoutAlignment.Center, LayoutAlignment.End, LayoutAlignment.Stretch })
        {
            var graph = new ReactiveGraph(); using var composition = new Composition(graph, "cross-" + alignment); var theme = new ThemeContext(composition.Root.Scope, new Theme("layout"));
            composition.Root.Present(theme, author: Style.Empty.Set(Arrangement.Axis, LayoutAxis.Row).Set(Arrangement.CrossAlignment, alignment));
            var auto = composition.Child(composition.Root, "auto"); auto.Present(theme, author: Style.Empty.Set(Arrangement.Width, 2f));
            var explicitChild = composition.Child(composition.Root, "explicit"); explicitChild.Present(theme, author: Style.Empty.Set(Arrangement.Width, 2f).Set(Arrangement.Height, 5f));
            var textChild = composition.Child(composition.Root, "text"); textChild.Present(theme, author: Style.Empty.Set(SceneProperties.Text, "abcd").Set(SceneProperties.FontSize, 2f));
            var boxes = SceneLayout.Project(composition, new(20, 10, 1), new ProbeShaper()).Boxes;
            Assert(boxes.Single(box => box.Identity.ElementId == auto.Id).Bounds.Height == (alignment == LayoutAlignment.Stretch ? 10 : 0) && boxes.Single(box => box.Identity.ElementId == explicitChild.Id).Bounds.Height == 5 && boxes.Single(box => box.Identity.ElementId == textChild.Id).Bounds.Height == (alignment == LayoutAlignment.Stretch ? 10 : 2), "Cross-axis auto/explicit/text resolution changed under " + alignment);
        }
        var nestedGraph = new ReactiveGraph(); using var nested = new Composition(nestedGraph, "nested"); var nestedTheme = new ThemeContext(nested.Root.Scope, new Theme("layout"));
        nested.Root.Present(nestedTheme, author: Style.Empty.Set(Arrangement.Axis, LayoutAxis.Row).Set(Arrangement.CrossAlignment, LayoutAlignment.Start));
        var column = nested.Child(nested.Root, "column"); column.Present(nestedTheme, author: Style.Empty.Set(Arrangement.Axis, LayoutAxis.Column));
        var first = nested.Child(column, "first"); first.Present(nestedTheme, author: Style.Empty.Set(Arrangement.Width, 4f).Set(Arrangement.Height, 2f));
        var second = nested.Child(column, "second"); second.Present(nestedTheme, author: Style.Empty.Set(Arrangement.Width, 7f).Set(Arrangement.Height, 3f));
        Assert(SceneLayout.Project(nested, new(20, 20, 1), new ProbeShaper()).Boxes.Single(box => box.Identity.ElementId == column.Id).Bounds is { Width: 7, Height: 5 }, "Nested row/column intrinsic measurement failed.");
        var stretchGraph = new ReactiveGraph(); using var stretch = new Composition(stretchGraph, "nested-stretch"); var stretchTheme = new ThemeContext(stretch.Root.Scope, new Theme("layout"));
        stretch.Root.Present(stretchTheme, author: Style.Empty.Set(Arrangement.Axis, LayoutAxis.Row).Set(Arrangement.CrossAlignment, LayoutAlignment.Stretch).Set(Arrangement.Clip, true));
        var autoColumn = stretch.Child(stretch.Root, "auto-column"); autoColumn.Present(stretchTheme, author: Style.Empty.Set(Arrangement.Axis, LayoutAxis.Column).Set(Arrangement.MaxHeight, 6f));
        var overColumn = stretch.Child(stretch.Root, "over-column"); overColumn.Present(stretchTheme, author: Style.Empty.Set(Arrangement.Axis, LayoutAxis.Column).Set(Arrangement.Height, 12f));
        var stretchBoxes = SceneLayout.Project(stretch, new(20, 10, 1), new ProbeShaper()).Boxes;
        Assert(stretchBoxes.Single(box => box.Identity.ElementId == autoColumn.Id).Bounds.Height == 6 && stretchBoxes.Single(box => box.Identity.ElementId == overColumn.Id).Bounds.Height == 12, "Nested constrained stretch or explicit overconstraint was resolved incorrectly.");
        var scrollGraph = new ReactiveGraph(); using var scroll = new Composition(scrollGraph, "scroll-overflow"); var scrollTheme = new ThemeContext(scroll.Root.Scope, new Theme("layout"));
        scroll.Root.Present(scrollTheme, author: Style.Empty.Set(Arrangement.Axis, LayoutAxis.Row).Set(Arrangement.Scroll, new ScrollOffset(float.MaxValue, 0))); var scrollChild = scroll.Child(scroll.Root, "child"); scrollChild.Present(scrollTheme, author: Style.Empty.Set(Arrangement.Width, 1f).Set(Arrangement.Height, 1f));
        Expect<ArgumentOutOfRangeException>(() => SceneLayout.Project(scroll, new(10, 10, 2), new ProbeShaper()));
        var glyph = new ShapedGlyph((uint)ushort.MaxValue + 1, 0, 0, 0, 1, 0, 0);
        Expect<ArgumentException>(() => new ShapedRun("bad", "probe", 400, 5, 0, "fingerprint", 0, "source", TextDirection.LeftToRight, "en", 1, 1, 1, -1, 0, 1, [glyph]));
        var goodGlyph = new ShapedGlyph(1, 0, 0, 0, 1, 0, 0);
        Expect<InvalidOperationException>(() => new ShapedText("bad-text", 1, 1, [new ShapedRun("shifted", "probe", 400, 5, 0, "fingerprint", 0, "source", TextDirection.LeftToRight, "en", 1, 1, 1, -1, 0, 1, [goodGlyph])]).Validate(new("x", "probe", 1, "en", TextDirection.LeftToRight, 1)));
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
    private static void Expect<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
