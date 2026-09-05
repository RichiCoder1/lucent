using System.Globalization;
using System.Numerics;
using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class LayoutSceneContracts
{
    [TestMethod]
    public void ColorAndBrushValues()
    {
        var color = Color.Parse("#12345678");
        Assert(
            color == Color.FromArgb(0x78, 0x12, 0x34, 0x56)
                && color.GetHashCode() == Color.FromArgb(0x78, 0x12, 0x34, 0x56).GetHashCode()
                && color.ToString() == "#12345678",
            "Color canonical equality or diagnostic form changed."
        );
        Assert(
            Color.TryParse("#123456", out var opaque) && opaque == Color.FromRgb(0x12, 0x34, 0x56),
            "Color RGB parse changed."
        );
        foreach (
            var malformed in new[]
            {
                "123456",
                "#12345",
                "#1234567",
                "#123456789",
                "#gg0000",
                " #123456",
            }
        )
            Assert(!Color.TryParse(malformed, out _), "Malformed color parsed: " + malformed);
        Expect<FormatException>(() => Color.Parse("#123"));
        Brush solid = opaque;
        Assert(
            solid.Equals(Brush.Solid(opaque))
                && solid.GetHashCode() == Brush.Solid(opaque).GetHashCode()
                && solid.ToString() == "solid(#123456FF)",
            "Color-to-Brush conversion or solid value semantics changed."
        );
        var stops = new List<GradientStop>
        {
            new(0, Color.Parse("#ff0000")),
            new(0, Color.Parse("#00ff00")),
            new(1, Color.Parse("#0000ff")),
        };
        var gradient = new LinearGradient(new(0, -0f), new(1, 1), stops);
        var equalGradient = new LinearGradient(new(-0f, 0), new(1, 1), stops);
        stops[0] = new(0, opaque);
        Brush brush = gradient;
        Brush equalBrush = equalGradient;
        Assert(
            gradient.Equals(equalGradient)
                && gradient.GetHashCode() == equalGradient.GetHashCode()
                && brush.Equals(equalBrush)
                && brush.GetHashCode() == equalBrush.GetHashCode()
                && gradient.Stops[0].Color == Color.Parse("#ff0000")
                && brush.ToString() == "linear(0,0 -> 1,1; 0:#FF0000FF,0:#00FF00FF,1:#0000FFFF)",
            "Gradient copy, equality, hash, or canonical dump changed."
        );
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var fractional = new LinearGradient(
                new(.25f, .5f),
                new(.75f, 1),
                [new(.25f, opaque), new(.75f, Color.Parse("#abcdef"))]
            );
            Assert(
                fractional.ToString()
                    == "linear(0.25,0.5 -> 0.75,1; 0.25:#123456FF,0.75:#ABCDEFFF)",
                "Gradient dump used current culture."
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
        }
        Expect<ArgumentException>(() =>
            new LinearGradient(new(0, 0), new(0, 0), [new(0, opaque), new(1, opaque)])
        );
        Expect<ArgumentException>(() =>
            new LinearGradient(new(float.NaN, 0), new(1, 1), [new(0, opaque), new(1, opaque)])
        );
        Expect<ArgumentException>(() =>
            new LinearGradient(new(0, 0), new(2, 1), [new(0, opaque), new(1, opaque)])
        );
        Expect<ArgumentException>(() =>
            new LinearGradient(new(0, 0), new(1, 1), [new(.5f, opaque)])
        );
        Expect<ArgumentException>(() =>
            new LinearGradient(
                new(0, 0),
                new(1, 1),
                Enumerable
                    .Range(0, 17)
                    .Select(index => new GradientStop(index / 16f, opaque))
                    .ToArray()
            )
        );
        Expect<ArgumentException>(() =>
            new LinearGradient(new(0, 0), new(1, 1), [new(.75f, opaque), new(.25f, opaque)])
        );
        Expect<ArgumentException>(() =>
            new LinearGradient(
                new(0, 0),
                new(1, 1),
                [new(0, opaque), new(1, Color.Parse("#00000080"))]
            )
        );
        foreach (var position in new[] { float.NaN, float.PositiveInfinity, -.1f, 1.1f })
            Expect<ArgumentOutOfRangeException>(() =>
                new GradientStop(position, opaque).Validate()
            );
    }

    [TestMethod]
    public void InsetsAndPadding()
    {
        var value = new Insets(1, 2, 3, 4);
        Assert(
            value == new Insets(1, 2, 3, 4)
                && value.GetHashCode() == new Insets(1, 2, 3, 4).GetHashCode()
                && value.ToString() == "insets(1,2,3,4)"
                && Insets.Zero == default
                && Insets.Uniform(2) == new Insets(2, 2, 2, 2)
                && Insets.Symmetric(2, 3) == new Insets(2, 3, 2, 3),
            "Insets value contract changed."
        );
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, -1f })
            Expect<ArgumentOutOfRangeException>(() => Insets.Uniform(invalid));

        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "padding");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("padding"));
        composition.Root.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Axis, LayoutAxis.Row)
                .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Start)
                .Set(LayoutProperties.Padding, new Insets(3, 4, 5, 6))
                .Set(LayoutProperties.Clip, true)
                .Set(VisualProperties.Background, Color.Parse("#010203"))
        );
        var child = composition.Child(composition.Root, "child");
        child.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 4f)
                .Set(LayoutProperties.Height, 5f)
                .Set(ProjectionProperties.Text, "x")
                .Set(ProjectionProperties.TextCaret, 0)
        );
        foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f })
        {
            var scene = SceneLayout.Project(composition, new(20, 20, scale), new ProbeShaper());
            var box = scene.Boxes.Single(box => box.Identity.ElementId == child.Id);
            var text = Flatten(scene.Nodes)
                .OfType<TextSceneNode>()
                .Single(node => node.Identity.Element.ElementId == child.Id);
            var rootInput = scene.Input.Single(input =>
                input.Identity.ElementId == composition.Root.Id
            );
            Assert(
                box.Bounds.X == MathF.Round(3 * scale, MidpointRounding.AwayFromZero) / scale
                    && box.Bounds.Y == MathF.Round(4 * scale, MidpointRounding.AwayFromZero) / scale
                    && text.Bounds.X == box.Bounds.X
                    && scene
                        .Nodes.OfType<PaintSceneNode>()
                        .Single(node => node.Identity.Element.ElementId == composition.Root.Id)
                        .Bounds
                        is { Width: 20, Height: 20 }
                    && Flatten(scene.Nodes).OfType<ClipSceneNode>().Single().Bounds.Width
                        == Math.Max(
                            0,
                            MathF.Round((20 - 5) * scale, MidpointRounding.AwayFromZero) / scale
                                - MathF.Round(3 * scale, MidpointRounding.AwayFromZero) / scale
                        )
                    && rootInput.ChildClipBounds
                        == Flatten(scene.Nodes).OfType<ClipSceneNode>().Single().Bounds,
                "Padding outer/inner edge rounding, input clipping, or background geometry changed at scale "
                    + scale
            );
        }
        var editable = composition.Child(composition.Root, "editable");
        editable.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 12f)
                .Set(LayoutProperties.Height, 10f)
                .Set(LayoutProperties.Padding, new Insets(2, 3, 2, 1))
                .Set(ProjectionProperties.Text, "xx")
                .Set(ProjectionProperties.TextSelectionStart, 0)
                .Set(ProjectionProperties.TextSelectionEnd, 2)
                .Set(ProjectionProperties.TextCaret, 1)
        );
        var textScene = SceneLayout.Project(composition, new(40, 20, 1), new ProbeShaper());
        var editableBox = textScene
            .Boxes.Single(box => box.Identity.ElementId == editable.Id)
            .Bounds;
        Assert(
            Flatten(textScene.Nodes)
                .OfType<TextSceneNode>()
                .Single(node => node.Identity.Element.ElementId == editable.Id)
                .Bounds.X
                == editableBox.X + 2
                && Flatten(textScene.Nodes)
                    .OfType<PaintSceneNode>()
                    .Any(node =>
                        node.Identity.Element.ElementId == editable.Id
                        && node.Identity.Kind == SceneNodeKind.Selection
                        && node.Bounds.Y == editableBox.Y + 3
                    )
                && Flatten(textScene.Nodes)
                    .OfType<PaintSceneNode>()
                    .Any(node =>
                        node.Identity.Element.ElementId == editable.Id
                        && node.Identity.Kind == SceneNodeKind.Caret
                    ),
            "Text, selection, or caret did not use the padded content origin."
        );
        var intrinsic = composition.Child(composition.Root, "intrinsic");
        intrinsic.Present(
            theme,
            author: Style.Empty.Set(LayoutProperties.Padding, Insets.Uniform(2))
        );
        var leaf = composition.Child(intrinsic, "leaf");
        leaf.Present(
            theme,
            author: Style.Empty.Set(LayoutProperties.Width, 4f).Set(LayoutProperties.Height, 5f)
        );
        var measured = SceneLayout
            .Project(composition, new(40, 20, 1), new ProbeShaper())
            .Boxes.Single(box => box.Identity.ElementId == intrinsic.Id);
        Assert(
            measured.Bounds is { Width: 8, Height: 9 },
            "Padding did not contribute to intrinsic outer size."
        );
        var constrainedElement = composition.Child(composition.Root, "constrained");
        constrainedElement.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 4f)
                .Set(LayoutProperties.Height, 4f)
                .Set(LayoutProperties.Padding, Insets.Uniform(4))
                .Set(LayoutProperties.Clip, true)
        );
        var constrained = SceneLayout.Project(composition, new(40, 20, 1), new ProbeShaper());
        Assert(
            Flatten(constrained.Nodes)
                .OfType<ClipSceneNode>()
                .Any(node =>
                    node.Identity.Element.ElementId == constrainedElement.Id
                    && node.Bounds is { Width: 0, Height: 0 }
                ),
            "Over-constrained padding did not clamp the inner box to zero."
        );

        var nestedGraph = new ReactiveGraph();
        using var nested = new Composition(nestedGraph, "nested-padding");
        var nestedTheme = new ThemeContext(nested.Root.Scope, new Theme("nested-padding"));
        nested.Root.Present(
            nestedTheme,
            author: Style
                .Empty.Set(LayoutProperties.Axis, LayoutAxis.Row)
                .Set(LayoutProperties.Padding, Insets.Uniform(2))
                .Set(LayoutProperties.Spacing, 2f)
                .Set(LayoutProperties.MainAlignment, LayoutAlignment.Center)
                .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Stretch)
        );
        var nestedFirst = nested.Child(nested.Root, "first");
        nestedFirst.Present(
            nestedTheme,
            author: Style
                .Empty.Set(LayoutProperties.MinWidth, 6f)
                .Set(LayoutProperties.MaxWidth, 6f)
                .Set(LayoutProperties.Padding, Insets.Uniform(1))
        );
        var nestedLeaf = nested.Child(nestedFirst, "leaf");
        nestedLeaf.Present(
            nestedTheme,
            author: Style.Empty.Set(LayoutProperties.Width, 2f).Set(LayoutProperties.Height, 3f)
        );
        var nestedSecond = nested.Child(nested.Root, "second");
        nestedSecond.Present(nestedTheme, author: Style.Empty.Set(LayoutProperties.Width, 4f));
        var nestedScene = SceneLayout.Project(nested, new(30, 20, 1), new ProbeShaper());
        var nestedBoxes = nestedScene.Boxes.ToDictionary(
            box => box.Identity.ElementId,
            box => box.Bounds
        );
        Assert(
            nestedBoxes[nestedFirst.Id] is { X: 9, Y: 2, Width: 6, Height: 16 }
                && nestedBoxes[nestedSecond.Id] is { X: 17, Y: 2, Width: 4, Height: 16 }
                && nestedBoxes[nestedLeaf.Id] is { X: 10, Y: 3 },
            "Nested padding, min/max, spacing, centered main alignment, or cross-axis stretch changed."
        );

        var transparentGraph = new ReactiveGraph();
        using var transparent = new Composition(transparentGraph, "transparent-padded-clip");
        var transparentTheme = new ThemeContext(
            transparent.Root.Scope,
            new Theme("transparent-padded-clip")
        );
        transparent.Root.Present(
            transparentTheme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 20f)
                .Set(LayoutProperties.Height, 20f)
                .Set(LayoutProperties.Padding, Insets.Uniform(5))
                .Set(LayoutProperties.Clip, true)
        );
        var paintedChild = transparent.Child(transparent.Root, "painted-child");
        paintedChild.Present(
            transparentTheme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 20f)
                .Set(LayoutProperties.Height, 20f)
                .Set(VisualProperties.Background, Color.Parse("#ff0000"))
        );
        var transparentScene = SceneLayout.Project(transparent, new(20, 20, 1), new ProbeShaper());
        Assert(
            transparentScene.Nodes.Single() is ClipSceneNode ownerClip
                && ownerClip.Bounds is { X: 5, Y: 5, Width: 10, Height: 10 }
                && ownerClip.Children.OfType<PaintSceneNode>().Single().Identity.Element.ElementId
                    == paintedChild.Id,
            "Transparent padded owner hoisted an overflowing child paint outside its inner clip."
        );
    }

    [TestMethod]
    public void OpacityGroups()
    {
        foreach (
            var invalid in new[]
            {
                float.NaN,
                float.PositiveInfinity,
                float.NegativeInfinity,
                -.01f,
                1.01f,
            }
        )
        {
            var graph = new ReactiveGraph();
            using var composition = new Composition(graph, "invalid-opacity");
            var theme = new ThemeContext(composition.Root.Scope, new Theme("opacity"));
            composition.Root.Present(
                theme,
                author: Style.Empty.Set(VisualProperties.Opacity, invalid)
            );
            Expect<ArgumentOutOfRangeException>(() =>
                SceneLayout.Project(composition, new(20, 20, 1), new ProbeShaper())
            );
        }

        var graph2 = new ReactiveGraph();
        using var composition2 = new Composition(graph2, "opacity");
        var theme2 = new ThemeContext(composition2.Root.Scope, new Theme("opacity"));
        composition2.Root.Present(
            theme2,
            author: Style
                .Empty.Set(LayoutProperties.Width, 20f)
                .Set(LayoutProperties.Height, 20f)
                .Set(VisualProperties.Background, Color.Parse("#010203"))
                .Set(VisualProperties.Opacity, .5f)
        );
        var child = composition2.Child(composition2.Root, "overflow");
        child.Present(
            theme2,
            author: Style
                .Empty.Set(LayoutProperties.Width, 40f)
                .Set(LayoutProperties.Height, 10f)
                .Set(VisualProperties.Background, Color.Parse("#ff0000"))
                .Set(VisualProperties.Opacity, .5f)
        );
        var scene = SceneLayout.Project(composition2, new(20, 20, 1), new ProbeShaper());
        var outer = (OpacitySceneNode)scene.Nodes.Single();
        var inner = outer.Children.OfType<OpacitySceneNode>().Single();
        Assert(
            outer.Bounds is { X: 0, Y: 0, Width: 20, Height: 20 }
                && inner.Bounds is { Width: 20, Height: 10 }
                && outer.Opacity == .5f
                && inner.Opacity == .5f
                && scene.Dump().Contains("kind=Opacity", StringComparison.Ordinal)
                && !scene.Dump().Contains("cache", StringComparison.OrdinalIgnoreCase),
            "Opacity groups did not retain bounded visible overflow, nesting, or deterministic diagnostics."
        );

        var clippedGraph = new ReactiveGraph();
        using var clipped = new Composition(clippedGraph, "opacity-clip");
        var clippedTheme = new ThemeContext(clipped.Root.Scope, new Theme("opacity-clip"));
        clipped.Root.Present(
            clippedTheme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 20f)
                .Set(LayoutProperties.Height, 20f)
                .Set(LayoutProperties.Clip, true)
                .Set(VisualProperties.Background, Color.Parse("#010203"))
                .Set(VisualProperties.Opacity, .5f)
        );
        var clippedChild = clipped.Child(clipped.Root, "overflow");
        clippedChild.Present(
            clippedTheme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 40f)
                .Set(LayoutProperties.Height, 10f)
                .Set(VisualProperties.Background, Color.Parse("#ff0000"))
        );
        var clippedNode = (OpacitySceneNode)
            SceneLayout.Project(clipped, new(20, 20, 1), new ProbeShaper()).Nodes.Single();
        Assert(
            clippedNode.Children[0] is PaintSceneNode
                && clippedNode.Children[1] is ClipSceneNode { Bounds: { Width: 20, Height: 20 } },
            "Opacity did not wrap owner background before its separate inner child clip."
        );

        var zeroGraph = new ReactiveGraph();
        using var zero = new Composition(zeroGraph, "opacity-zero");
        var zeroTheme = new ThemeContext(zero.Root.Scope, new Theme("opacity-zero"));
        var invoked = 0;
        zero.Root.Present(
            zeroTheme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 20f)
                .Set(LayoutProperties.Height, 20f)
                .Set(VisualProperties.Opacity, 0f)
        );
        var button = zero.Child(zero.Root, "button");
        Controls.Button(
            button,
            zeroTheme,
            "Invoke",
            () => invoked++,
            Style.Empty.Set(LayoutProperties.Width, 20f).Set(LayoutProperties.Height, 20f)
        );
        var zeroScene = SceneLayout.Project(zero, new(20, 20, 1), new ProbeShaper());
        var router = zero.Input;
        Assert(
            zeroScene.Nodes.Single() is OpacitySceneNode { Opacity: 0 }
                && router.SetScene(zeroScene)
                && router.MoveFocus(FocusTraversalDirection.Next),
            "Opacity zero removed retained layout, input, or focus data."
        );
        Assert(
            router
                .DispatchPointer(new(PointerCommandKind.Down, 77, 1, 1, PointerButton.Primary))
                .Handled
                && router.DispatchPointer(new(PointerCommandKind.Up, 77, 1, 1)).Handled
                && invoked == 1,
            "Opacity zero removed pointer hit routing or activation."
        );
        var semantic = zero.SemanticSnapshot()!.Children.Single();
        Assert(
            zero.ExecuteSemanticCommand(semantic.Identity, new(SemanticCommandKind.Invoke))
                == SemanticCommandResult.Applied
                && invoked == 2,
            "Opacity zero removed semantic actions."
        );

        var transitionGraph = new ReactiveGraph();
        using var transitioned = new Composition(transitionGraph, "opacity-transition");
        var transitionTheme = new ThemeContext(
            transitioned.Root.Scope,
            new Theme("opacity-transition")
        );
        transitioned.Root.Present(
            transitionTheme,
            author: Style.Empty.Set(VisualProperties.Background, Color.Parse("#ffffff")),
            transitions: [Transition.For(VisualProperties.Opacity, 100)]
        );
        transitioned.Root.StartTransition(VisualProperties.Opacity, .4f);
        var activeTransition = SceneLayout.Project(transitioned, new(10, 10, 1), new ProbeShaper());
        Assert(
            activeTransition.Nodes.Single() is OpacitySceneNode { Opacity: .4f },
            "Active manual opacity transition did not project a composited group."
        );
        transitionTheme.ReducedMotion = true;
        var suppressedTransition = SceneLayout.Project(
            transitioned,
            new(10, 10, 1),
            new ProbeShaper()
        );
        Assert(
            suppressedTransition.Nodes.Single() is PaintSceneNode
                && transitioned.Root.Resolve(VisualProperties.Opacity).SuppressedTransition?.Source
                    == "transition-suppressed",
            "Reduced motion did not suppress projected opacity transition output."
        );
    }

    private static IEnumerable<SceneNode> Flatten(IEnumerable<SceneNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            if (node is ClipSceneNode clip)
                foreach (var child in Flatten(clip.Children))
                    yield return child;
            else if (node is OpacitySceneNode opacity)
                foreach (var child in Flatten(opacity.Children))
                    yield return child;
        }
    }

    [TestMethod]
    public void RowsColumnsClipsAndRounding()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "layout");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("layout"));
        composition.Root.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Axis, LayoutAxis.Row)
                .Set(LayoutProperties.Spacing, 3f)
                .Set(LayoutProperties.MainAlignment, LayoutAlignment.Center)
                .Set(LayoutProperties.Clip, true)
                .Set(VisualProperties.Background, Color.Parse("#010203"))
        );
        var first = composition.Child(composition.Root, "first");
        first.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 20f)
                .Set(LayoutProperties.Height, 10f)
                .Set(ProjectionProperties.Text, "ffi")
                .Set(TypographyProperties.FontSize, 10f)
        );
        var second = composition.Child(composition.Root, "second");
        second.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 20f)
                .Set(LayoutProperties.Height, 10f)
                .Set(LayoutProperties.Scroll, new ScrollOffset(1, 0))
                .Set(ProjectionProperties.Text, "q\u0307")
        );
        var shaper = new ProbeShaper();
        var firstScene = SceneLayout.Project(composition, new(51, 20, 1.25f), shaper);
        var secondScene = SceneLayout.Project(composition, new(51, 20, 1.25f), shaper);
        Assert(
            StableDump(firstScene) == StableDump(secondScene)
                && firstScene.Generation + 1 == secondScene.Generation
                && firstScene.Boxes.Count == 3
                && firstScene.Nodes.OfType<ClipSceneNode>().Single() is not null,
            "Retained scene was not stable or clipped."
        );
        Assert(
            firstScene.Boxes[1].Bounds.X == 4f
                && firstScene.Boxes[2].Bounds.X == 27.2f
                && firstScene.Boxes[1].Text!.Identity == secondScene.Boxes[1].Text!.Identity,
            "Row alignment/edge rounding or shaped identity diverged."
        );
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert(
                StableDump(firstScene)
                    == StableDump(SceneLayout.Project(composition, new(51, 20, 1.25f), shaper)),
                "Scene dump was culture-sensitive."
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
        Assert(
            !firstScene.Dump().Contains("ffi", StringComparison.Ordinal) && shaper.Requests == 6,
            "Scene dump leaked text or layout did not share shaped results: " + shaper.Requests
        );
    }

    [TestMethod]
    public void MainGrowth()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "main-growth");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("main-growth"));
        composition.Root.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Axis, LayoutAxis.Row)
                .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Start)
        );
        var changingGrow = graph.Signal(0f, "changing-grow");
        var intrinsic = composition.Child(composition.Root, "intrinsic");
        intrinsic.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 10f)
                .Set(LayoutProperties.Height, 5f)
                .Bind(LayoutProperties.MainGrow, () => changingGrow.Value)
        );
        var capped = composition.Child(composition.Root, "capped");
        capped.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 20f)
                .Set(LayoutProperties.Height, 5f)
                .Set(LayoutProperties.MainGrow, 1f)
                .Set(LayoutProperties.MaxWidth, 30f)
        );
        var remaining = composition.Child(composition.Root, "remaining");
        remaining.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 10f)
                .Set(LayoutProperties.Height, 5f)
                .Set(LayoutProperties.MainGrow, 1f)
        );

        var expanded = SceneLayout.Project(composition, new(100, 20, 1), new ProbeShaper());
        var boxes = expanded.Boxes.ToDictionary(box => box.Identity.ElementId, box => box.Bounds);
        Assert(
            boxes[intrinsic.Id] is { X: 0, Width: 10 }
                && boxes[capped.Id] is { X: 10, Width: 30 }
                && boxes[remaining.Id] is { X: 40, Width: 60 },
            "Positive main-axis remainder was not redistributed after a grown child reached its maximum."
        );
        var constrained = SceneLayout.Project(composition, new(35, 20, 1), new ProbeShaper());
        boxes = constrained.Boxes.ToDictionary(box => box.Identity.ElementId, box => box.Bounds);
        Assert(
            boxes[intrinsic.Id].Width == 10
                && boxes[capped.Id].Width == 20
                && boxes[remaining.Id].Width == 10,
            "Main growth changed intrinsic or explicit bases when the parent had no positive remainder."
        );

        var unchangedSignature = SceneLayout.InputSignature(intrinsic);
        changingGrow.Value = 1f;
        graph.Drain();
        Assert(
            SceneLayout.InputSignature(intrinsic) != unchangedSignature,
            "Main growth did not participate in retained input freshness."
        );
        changingGrow.Value = float.NaN;
        graph.Drain();
        Expect<ArgumentOutOfRangeException>(() =>
            SceneLayout.Project(composition, new(100, 20, 1), new ProbeShaper())
        );
        changingGrow.Value = -1f;
        graph.Drain();
        Expect<ArgumentOutOfRangeException>(() =>
            SceneLayout.Project(composition, new(100, 20, 1), new ProbeShaper())
        );
    }

    [TestMethod]
    public void InvalidBoundsFail()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "invalid-layout");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("layout"));
        composition.Root.Present(theme, author: Style.Empty.Set(LayoutProperties.Width, float.NaN));
        var shaper = new ProbeShaper();
        Expect<ArgumentOutOfRangeException>(() =>
            SceneLayout.Project(composition, new(10, 10, 1), shaper)
        );
        Expect<ArgumentOutOfRangeException>(() =>
            SceneLayout.Project(composition, new(0, 10, 1), shaper)
        );
        composition.Dispose();
        Expect<ObjectDisposedException>(() =>
            SceneLayout.Project(composition, new(10, 10, 1), shaper)
        );
    }

    [TestMethod]
    public void ExplicitZeroAndOverflow()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "zero-layout");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("layout"));
        composition.Root.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Axis, LayoutAxis.Row)
                .Set(LayoutProperties.Clip, true)
        );
        var zero = composition.Child(composition.Root, "zero");
        zero.Present(
            theme,
            author: Style.Empty.Set(LayoutProperties.Width, 0f).Set(LayoutProperties.Height, 0f)
        );
        var scene = SceneLayout.Project(composition, new(10, 10, 1), new ProbeShaper());
        Assert(
            scene.Boxes.Single(box => box.Identity.ElementId == zero.Id).Bounds
                is { Width: 0, Height: 0 },
            "Explicit zero was stretched as auto."
        );
        var mutable = scene.Nodes as ICollection<SceneNode>;
        Assert(
            mutable is null || mutable.IsReadOnly,
            "Scene nodes can be cast back to a mutable collection."
        );
        var overflow = composition.Child(composition.Root, "overflow");
        overflow.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Width, float.MaxValue)
                .Set(LayoutProperties.Height, 1f)
        );
        Expect<ArgumentOutOfRangeException>(() =>
            SceneLayout.Project(composition, new(float.MaxValue, 10, 2), new ProbeShaper())
        );
        var badGraph = new ReactiveGraph();
        using var bad = new Composition(badGraph, "bad-layout");
        var badTheme = new ThemeContext(bad.Root.Scope, new Theme("layout"));
        bad.Root.Present(
            badTheme,
            author: Style
                .Empty.Set(LayoutProperties.MinWidth, 2f)
                .Set(LayoutProperties.MaxWidth, 1f)
        );
        Expect<ArgumentOutOfRangeException>(() =>
            SceneLayout.Project(bad, new(10, 10, 1), new ProbeShaper())
        );
        var emptyGraph = new ReactiveGraph();
        using var empty = new Composition(emptyGraph, "empty-layout");
        var emptyTheme = new ThemeContext(empty.Root.Scope, new Theme("layout"));
        empty.Root.Present(emptyTheme);
        Assert(
            SceneLayout.Project(empty, new(10, 10, 1), new ProbeShaper()).Boxes.Count == 1,
            "Empty tree was not bounded to its root."
        );
    }

    [TestMethod]
    public void CrossAxisIntrinsicAndMalformedRuns()
    {
        foreach (
            var alignment in new[]
            {
                LayoutAlignment.Start,
                LayoutAlignment.Center,
                LayoutAlignment.End,
                LayoutAlignment.Stretch,
            }
        )
        {
            var graph = new ReactiveGraph();
            using var composition = new Composition(graph, "cross-" + alignment);
            var theme = new ThemeContext(composition.Root.Scope, new Theme("layout"));
            composition.Root.Present(
                theme,
                author: Style
                    .Empty.Set(LayoutProperties.Axis, LayoutAxis.Row)
                    .Set(LayoutProperties.CrossAlignment, alignment)
            );
            var auto = composition.Child(composition.Root, "auto");
            auto.Present(theme, author: Style.Empty.Set(LayoutProperties.Width, 2f));
            var explicitChild = composition.Child(composition.Root, "explicit");
            explicitChild.Present(
                theme,
                author: Style.Empty.Set(LayoutProperties.Width, 2f).Set(LayoutProperties.Height, 5f)
            );
            var textChild = composition.Child(composition.Root, "text");
            textChild.Present(
                theme,
                author: Style
                    .Empty.Set(ProjectionProperties.Text, "abcd")
                    .Set(TypographyProperties.FontSize, 2f)
            );
            var boxes = SceneLayout.Project(composition, new(20, 10, 1), new ProbeShaper()).Boxes;
            Assert(
                boxes.Single(box => box.Identity.ElementId == auto.Id).Bounds.Height
                    == (alignment == LayoutAlignment.Stretch ? 10 : 0)
                    && boxes.Single(box => box.Identity.ElementId == explicitChild.Id).Bounds.Height
                        == 5
                    && boxes.Single(box => box.Identity.ElementId == textChild.Id).Bounds.Height
                        == (alignment == LayoutAlignment.Stretch ? 10 : 2),
                "Cross-axis auto/explicit/text resolution changed under " + alignment
            );
        }
        var nestedGraph = new ReactiveGraph();
        using var nested = new Composition(nestedGraph, "nested");
        var nestedTheme = new ThemeContext(nested.Root.Scope, new Theme("layout"));
        nested.Root.Present(
            nestedTheme,
            author: Style
                .Empty.Set(LayoutProperties.Axis, LayoutAxis.Row)
                .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Start)
        );
        var column = nested.Child(nested.Root, "column");
        column.Present(
            nestedTheme,
            author: Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
        );
        var first = nested.Child(column, "first");
        first.Present(
            nestedTheme,
            author: Style.Empty.Set(LayoutProperties.Width, 4f).Set(LayoutProperties.Height, 2f)
        );
        var second = nested.Child(column, "second");
        second.Present(
            nestedTheme,
            author: Style.Empty.Set(LayoutProperties.Width, 7f).Set(LayoutProperties.Height, 3f)
        );
        Assert(
            SceneLayout
                .Project(nested, new(20, 20, 1), new ProbeShaper())
                .Boxes.Single(box => box.Identity.ElementId == column.Id)
                .Bounds
                is { Width: 7, Height: 5 },
            "Nested row/column intrinsic measurement failed."
        );
        var stretchGraph = new ReactiveGraph();
        using var stretch = new Composition(stretchGraph, "nested-stretch");
        var stretchTheme = new ThemeContext(stretch.Root.Scope, new Theme("layout"));
        stretch.Root.Present(
            stretchTheme,
            author: Style
                .Empty.Set(LayoutProperties.Axis, LayoutAxis.Row)
                .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Stretch)
                .Set(LayoutProperties.Clip, true)
        );
        var autoColumn = stretch.Child(stretch.Root, "auto-column");
        autoColumn.Present(
            stretchTheme,
            author: Style
                .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                .Set(LayoutProperties.MaxHeight, 6f)
        );
        var overColumn = stretch.Child(stretch.Root, "over-column");
        overColumn.Present(
            stretchTheme,
            author: Style
                .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                .Set(LayoutProperties.Height, 12f)
        );
        var stretchBoxes = SceneLayout.Project(stretch, new(20, 10, 1), new ProbeShaper()).Boxes;
        Assert(
            stretchBoxes.Single(box => box.Identity.ElementId == autoColumn.Id).Bounds.Height == 6
                && stretchBoxes.Single(box => box.Identity.ElementId == overColumn.Id).Bounds.Height
                    == 12,
            "Nested constrained stretch or explicit overconstraint was resolved incorrectly."
        );
        var scrollGraph = new ReactiveGraph();
        using var scroll = new Composition(scrollGraph, "scroll-overflow");
        var scrollTheme = new ThemeContext(scroll.Root.Scope, new Theme("layout"));
        scroll.Root.Present(
            scrollTheme,
            author: Style
                .Empty.Set(LayoutProperties.Axis, LayoutAxis.Row)
                .Set(LayoutProperties.Scroll, new ScrollOffset(float.MaxValue, 0))
        );
        var scrollChild = scroll.Child(scroll.Root, "child");
        scrollChild.Present(
            scrollTheme,
            author: Style.Empty.Set(LayoutProperties.Width, 1f).Set(LayoutProperties.Height, 1f)
        );
        Expect<ArgumentOutOfRangeException>(() =>
            SceneLayout.Project(scroll, new(10, 10, 2), new ProbeShaper())
        );
        var glyph = new ShapedGlyph((uint)ushort.MaxValue + 1, 0, 0, 0, 1, 0, 0);
        Expect<ArgumentException>(() =>
            new ShapedRun(
                "bad",
                "probe",
                400,
                5,
                0,
                "fingerprint",
                0,
                "source",
                TextDirection.LeftToRight,
                "en",
                1,
                1,
                1,
                -1,
                0,
                1,
                [glyph]
            )
        );
        var goodGlyph = new ShapedGlyph(1, 0, 0, 0, 1, 0, 0);
        Expect<InvalidOperationException>(() =>
            new ShapedText(
                "bad-text",
                1,
                1,
                [
                    new ShapedRun(
                        "shifted",
                        "probe",
                        400,
                        5,
                        0,
                        "fingerprint",
                        0,
                        "source",
                        TextDirection.LeftToRight,
                        "en",
                        1,
                        1,
                        1,
                        -1,
                        0,
                        1,
                        [goodGlyph]
                    ),
                ]
            ).Validate(new("x", "probe", 1, "en", TextDirection.LeftToRight, 1))
        );
    }

    [TestMethod]
    public void FixedHeightVirtualization()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "virtual-layout");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("virtual-layout"));
        Controls.Column(composition.Root, theme, "root");
        var viewport = composition.Child(composition.Root, "viewport");
        Controls.ScrollViewport(
            viewport,
            theme,
            "Rows",
            style: Style
                .Empty.Set(LayoutProperties.Width, 120f)
                .Set(LayoutProperties.Height, 30f)
                .Set(LayoutProperties.Padding, Insets.Symmetric(2, 5))
        );
        var values = graph.Signal(Enumerable.Range(1, 10_000).ToArray(), "virtual-values");
        var list = Controls.VirtualizedList(
            viewport,
            theme,
            "rows",
            "Rows",
            () => values.Value,
            value => value,
            (value, context) =>
            {
                var row = context.Element("row");
                Controls.Selectable(row, theme, "row " + value);
                return row;
            },
            30f
        );
        graph.Drain();
        var shaper = new ProbeShaper();
        var scene = SceneLayout.Project(composition, new(120, 30, 1), shaper);
        var input = composition.Input;
        Assert(input.SetScene(scene), "Virtual list initial scene rejected.");
        var initialScroll = input.GetSemanticScroll(
            scene.Input.Single(item => item.Identity.ElementId == viewport.Id).Identity
        );
        Assert(
            initialScroll is { Maximum.Y: 299_980, Viewport.Width: 116, Viewport.Height: 20 }
                && list.SourceCount == 10_000
                && list.Items.Count == 3
                && scene.Boxes.Single(box => box.Identity.ElementId == list.Region.Id).Bounds.Height
                    == 300_000,
            "Virtual list did not realize a bounded fixed-height initial window."
        );
        list.SetRowHeight(20f);
        graph.Drain();
        scene = SceneLayout.Project(composition, new(120, 30, 1), shaper);
        Assert(
            input.SetScene(scene)
                && input
                    .GetSemanticScroll(
                        scene.Input.Single(item => item.Identity.ElementId == viewport.Id).Identity
                    )
                    ?.Maximum.Y == 199_980
                && list.RowHeight == 20f
                && list.Items.Count == 3
                && scene.Boxes.Single(box => box.Identity.ElementId == list.Region.Id).Bounds.Height
                    == 200_000,
            "A live fixed-row-height update did not retain a bounded virtual list."
        );
        list.SetRowHeight(30f);
        graph.Drain();
        scene = SceneLayout.Project(composition, new(120, 30, 1), shaper);
        Assert(input.SetScene(scene), "Restored virtual row height scene was rejected.");
        Assert(
            input.ScrollSemantic(
                scene.Input.Single(item => item.Identity.ElementId == viewport.Id).Identity,
                new(SemanticCommandKind.Scroll, Endpoint: SemanticScrollEndpoint.End)
            ),
            "Virtual list could not scroll to its end."
        );
        graph.Drain();
        scene = SceneLayout.Project(composition, new(120, 30, 1), shaper);
        Assert(
            input.SetScene(scene)
                && list.Items.Count == 3
                && composition.SemanticDump().Contains("suppressions=[]", StringComparison.Ordinal),
            "Virtual end window or semantic dump was not bounded."
        );
        var before = list.Items.Select(item => item.Id).ToArray();
        var reordered = values.Value.ToArray();
        (reordered[^1], reordered[^2]) = (reordered[^2], reordered[^1]);
        values.Value = reordered;
        graph.Drain();
        scene = SceneLayout.Project(composition, new(120, 30, 1), shaper);
        Assert(
            input.SetScene(scene)
                && list.Items.Select(item => item.Id)
                    .SequenceEqual([before[0], before[2], before[1]]),
            "Keyed virtual rows did not retain their identities across a move."
        );
        values.Value = reordered[..^1];
        graph.Drain();
        scene = SceneLayout.Project(composition, new(120, 30, 1), shaper);
        Assert(
            !input.SetScene(scene),
            "A removed end row did not force bounded scroll reconciliation."
        );
        graph.Drain();
        scene = SceneLayout.Project(composition, new(120, 30, 1), shaper);
        Assert(
            input.SetScene(scene) && list.SourceCount == 9_999 && list.Items.Count <= 3,
            "Removal left an unbounded virtual window."
        );

        var fractionalGraph = new ReactiveGraph();
        using var fractional = new Composition(fractionalGraph, "fractional-virtual-layout");
        var fractionalTheme = new ThemeContext(
            fractional.Root.Scope,
            new Theme("fractional-virtual-layout")
        );
        Controls.Column(fractional.Root, fractionalTheme, "root");
        var fractionalViewport = fractional.Child(fractional.Root, "viewport");
        Controls.ScrollViewport(
            fractionalViewport,
            fractionalTheme,
            "Rows",
            style: Style
                .Empty.Set(LayoutProperties.Width, 60f)
                .Set(LayoutProperties.Height, 31f)
                .Set(LayoutProperties.Padding, new Insets(0, 1.2f, 0, 3.7f))
        );
        var fractionalList = Controls.VirtualizedList(
            fractionalViewport,
            fractionalTheme,
            "rows",
            "Rows",
            () => Enumerable.Range(1, 100),
            value => value,
            (value, context) =>
            {
                var row = context.Element("row");
                Controls.Selectable(row, fractionalTheme, "row " + value);
                return row;
            },
            13f
        );
        fractionalGraph.Drain();
        foreach (var (scale, innerHeight) in new[] { (1.25f, 25.6f), (1.5f, 26f) })
        {
            var fractionalScene = SceneLayout.Project(fractional, new(60, 31, scale), shaper);
            var fractionalInput = fractional.Input;
            Assert(
                fractionalInput.SetScene(fractionalScene),
                "Fractional padded virtual scene rejected."
            );
            var scroll = fractionalInput.GetSemanticScroll(
                fractionalScene
                    .Input.Single(item => item.Identity.ElementId == fractionalViewport.Id)
                    .Identity
            );
            Assert(
                fractionalList.Items.Count == 4
                    && scroll is not null
                    && MathF.Abs(scroll.Value.Viewport.Height - innerHeight) < .001f,
                "Fractional asymmetric padding did not drive rounded inner virtualization at scale "
                    + scale
            );
        }
    }

    private sealed class ProbeShaper : ITextShaper
    {
        public int Requests { get; private set; }

        public ShapedText Shape(TextMeasureRequest request)
        {
            Requests++;
            var runWidth = request.Text.Length * request.FontSize / 2;
            var glyphs = new[] { new ShapedGlyph(1, 0, 0, 0, runWidth, 0, 0) };
            var run = new ShapedRun(
                "run-" + request.Text.Length.ToString(CultureInfo.InvariantCulture),
                "probe",
                400,
                5,
                0,
                "probe-fingerprint",
                0,
                "probe#0",
                request.Direction,
                request.Language,
                request.FontSize,
                0,
                request.FontSize,
                -request.FontSize,
                0,
                runWidth,
                glyphs
            );
            return new(
                "shape-"
                    + request.Text.Length.ToString(CultureInfo.InvariantCulture)
                    + "-"
                    + request.Language,
                runWidth,
                request.FontSize,
                [run]
            );
        }
    }

    private static string StableDump(RetainedScene scene)
    {
        var lines = scene.Dump().Split('\n');
        lines[0] = lines[0][(lines[0].IndexOf(" viewport=", StringComparison.Ordinal) + 1)..];
        return string.Join('\n', lines);
    }

    private static void Expect<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static void Assert(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }
}
