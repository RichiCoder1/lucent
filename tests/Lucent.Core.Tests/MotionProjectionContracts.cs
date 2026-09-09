using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class MotionProjectionContracts
{
    [TestMethod]
    public void SampleOnlyFramesReuseLayoutShapingInputAndSemanticRevision()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "motion-paint-reuse");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var algorithm = new CountingAlgorithm();
        composition.Root.Present(theme, author: Style.Empty.Algorithm(algorithm));
        var active = graph.Signal(false, "active");
        var text = composition.Child(composition.Root, "text");
        text.Present(
            theme,
            author: Style
                .Empty.Set(ProjectionProperties.Text, "Retained text")
                .Bind(
                    VisualProperties.Background,
                    () => Brush.Solid(Color.Parse(active.Value ? "#FFFFFF" : "#00000000"))
                )
                .Bind(VisualProperties.Opacity, () => active.Value ? 0.5f : 1f)
                .Bind(
                    TypographyProperties.TextColor,
                    () => Color.Parse(active.Value ? "#FFFFFF" : "#000000")
                )
                .Transition(VisualProperties.Background, Motion.Duration(100, Easing.Linear))
                .Transition(VisualProperties.Opacity, Motion.Duration(100, Easing.Linear))
                .Transition(TypographyProperties.TextColor, Motion.Duration(100, Easing.Linear))
        );
        var shaper = new CountingShaper();
        composition.SamplePresentation(TimeSpan.Zero);
        using var initial = SceneLayout.ProjectFrame(composition, new(160, 60, 1), shaper, null);
        Assert.IsTrue(composition.Input.SetScene(initial));
        Assert.IsTrue(composition.TryAcknowledgePresentation(initial.Generation));
        active.Value = true;
        using var target = SceneLayout.ProjectFrame(composition, initial.Viewport, shaper, initial);
        Assert.IsFalse(target.IsPaintOnly);
        Assert.IsTrue(composition.Input.SetScene(target));
        Assert.IsTrue(composition.TryAcknowledgePresentation(target.Generation));
        var measures = algorithm.Calls;
        var shapes = shaper.Calls;
        var semantics = composition.SemanticRevision;
        composition.SamplePresentation(TimeSpan.FromMilliseconds(50));
        using var middle = SceneLayout.ProjectFrame(composition, target.Viewport, shaper, target);
        Assert.IsTrue(middle.IsPaintOnly);
        Assert.AreSame(target.Boxes, middle.Boxes);
        Assert.AreSame(target.Input, middle.Input);
        Assert.AreEqual(measures, algorithm.Calls);
        Assert.AreEqual(shapes, shaper.Calls);
        Assert.AreEqual(semantics, composition.SemanticRevision);
        Assert.AreEqual(0.5f, text.Resolve(VisualProperties.Opacity).Value);
        var opacity = Flatten(middle.Nodes).OfType<OpacitySceneNode>().Single();
        Assert.AreEqual(0.75f, opacity.Opacity, 0.001f);
        Assert.IsTrue(
            Flatten(middle.Nodes)
                .OfType<PaintSceneNode>()
                .Any(node =>
                    node.Identity.Element.ElementId == text.Id
                    && node.Identity.Kind == SceneNodeKind.Paint
                ),
            "A transparent baseline must still have a reusable background paint slot."
        );
        Assert.IsTrue(composition.Input.SetScene(middle));
        Assert.IsTrue(composition.TryAcknowledgePresentation(middle.Generation));
        composition.SamplePresentation(TimeSpan.FromMilliseconds(100));
        using var final = SceneLayout.ProjectFrame(composition, middle.Viewport, shaper, middle);
        Assert.IsTrue(final.IsPaintOnly);
        Assert.AreEqual(0.5f, Flatten(final.Nodes).OfType<OpacitySceneNode>().Single().Opacity);
        Assert.AreEqual(measures, algorithm.Calls);
        Assert.AreEqual(shapes, shaper.Calls);
        Assert.IsFalse(composition.PresentationDemand.IsActive);
    }

    [TestMethod]
    public void TextResizeInputAndRemovedOwnersInvalidatePaintReuse()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "motion-paint-invalidation");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var content = graph.Signal("Before", "content");
        var enabled = graph.Signal(true, "enabled");
        var child = composition.Child(composition.Root, "child");
        child.Present(
            theme,
            author: Style
                .Empty.Bind(ProjectionProperties.Text, () => content.Value)
                .Bind(InputProperties.Enabled, () => enabled.Value)
        );
        var shaper = new CountingShaper();
        using var first = SceneLayout.ProjectFrame(composition, new(100, 50, 1), shaper, null);
        using var repeat = SceneLayout.ProjectFrame(composition, first.Viewport, shaper, first);
        Assert.IsTrue(repeat.IsPaintOnly);
        var calls = shaper.Calls;
        content.Value = "After editing";
        using var edited = SceneLayout.ProjectFrame(composition, first.Viewport, shaper, repeat);
        Assert.IsFalse(edited.IsPaintOnly);
        Assert.IsTrue(shaper.Calls > calls);
        using var resized = SceneLayout.ProjectFrame(
            composition,
            new(140, 70, 1.5f),
            shaper,
            edited
        );
        Assert.IsFalse(resized.IsPaintOnly);
        enabled.Value = false;
        using var disabled = SceneLayout.ProjectFrame(
            composition,
            resized.Viewport,
            shaper,
            resized
        );
        Assert.IsFalse(disabled.IsPaintOnly);
        Assert.IsFalse(disabled.Input.Single(node => node.Identity.ElementId == child.Id).Enabled);
        child.Dispose();
        using var removed = SceneLayout.ProjectFrame(
            composition,
            resized.Viewport,
            shaper,
            disabled
        );
        Assert.IsFalse(removed.IsPaintOnly);
        Assert.IsFalse(removed.Boxes.Any(box => box.Identity.ElementId == child.Id));
        Assert.IsTrue(
            Flatten(first.Nodes).OfType<TextSceneNode>().Any(),
            "The old immutable scene remains readable after its mounted owner is removed."
        );
    }

    private static IEnumerable<SceneNode> Flatten(IEnumerable<SceneNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            var children = node switch
            {
                ClipSceneNode clip => clip.Children,
                OpacitySceneNode opacity => opacity.Children,
                _ => [],
            };
            foreach (var child in Flatten(children))
                yield return child;
        }
    }

    private sealed class CountingShaper : ITextShaper
    {
        internal int Calls { get; private set; }

        public ShapedText Shape(TextMeasureRequest request)
        {
            Calls++;
            var width = request.Text.Length * request.FontSize / 2;
            var run = new ShapedRun(
                "counting-run",
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
                width,
                [new ShapedGlyph(1, 0, 0, 0, width, 0, 0)]
            );
            return new("counting", width, request.FontSize, [run]);
        }
    }

    private sealed class CountingAlgorithm() : LayoutAlgorithm("motion-counting")
    {
        internal int Calls { get; private set; }

        public override LayoutAlgorithmResult Layout(LayoutAlgorithmContext context)
        {
            Calls++;
            var child = context.MeasureChild(
                context.Children[0],
                new(LayoutConstraint.Unbounded, LayoutConstraint.Unbounded)
            );
            return new(child, new LayoutChildPlacement(0, new(0, 0, 140, 30)));
        }
    }
}
