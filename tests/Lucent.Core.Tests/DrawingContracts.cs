using System.Numerics;
using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class DrawingContracts
{
    [TestMethod]
    public void DrawingUsesOneStyledRootAndProjectsBetweenBackgroundAndChildren()
    {
        using var composition = new Composition(new ReactiveGraph(), "drawing-order");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var root = composition.Mount(
            composition.Root,
            theme,
            Components.Drawing(
                new(
                    new(10, 10),
                    recorder => recorder.FillRectangle(new(0, 0, 10, 10), Color.Parse("#00FF00"))
                ),
                ComponentContent.Create([Components.Text("child")]),
                Style
                    .Empty.Background(Color.Parse("#FF0000"))
                    .Border(global::Lucent.Core.Border.Uniform(Color.Parse("#0000FF"), 1))
                    .Padding(Insets.Uniform(2))
                    .Clip(true)
                    .Opacity(.5f)
            )
        );
        using var scene = SceneLayout.Project(composition, new(20, 20, 1), new EmptyShaper());
        var ordered = Flatten(scene.Nodes).Select(node => node.Identity.Kind).ToArray();

        Assert.AreEqual(1, composition.Root.Children.Count);
        Assert.AreSame(root, composition.Root.Children.Single());
        Assert.IsTrue(
            Array.IndexOf(ordered, SceneNodeKind.Paint)
                < Array.IndexOf(ordered, SceneNodeKind.Drawing)
        );
        Assert.IsTrue(
            Array.IndexOf(ordered, SceneNodeKind.Border)
                < Array.IndexOf(ordered, SceneNodeKind.Drawing)
        );
        Assert.IsTrue(
            Array.IndexOf(ordered, SceneNodeKind.Drawing)
                < Array.IndexOf(ordered, SceneNodeKind.Text)
        );
        var drawing = Flatten(scene.Nodes).OfType<DrawingSceneNode>().Single();
        Assert.AreEqual(new LayoutRect(2, 2, 6, 6), drawing.Bounds);
        Assert.AreEqual(new LayoutSize(10, 10), drawing.CoordinateSize);
        Assert.IsTrue(Flatten(scene.Nodes).OfType<ClipSceneNode>().Any());
        Assert.IsTrue(Flatten(scene.Nodes).OfType<OpacitySceneNode>().Any());
    }

    [TestMethod]
    public void TrackedRecordingCoalescesEqualCommandsAndDoesNoIdleWork()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "drawing-tracking");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var input = composition.Root.Scope.Signal(1f, "drawing-input");
        var reads = 0;
        var root = composition.Mount(
            composition.Root,
            theme,
            Components.Drawing(
                new(
                    new(10, 10),
                    recorder =>
                    {
                        reads++;
                        var endpoint = input.Value > 0 ? 1 : 0;
                        recorder.Line(Vector2.Zero, new(endpoint, 1), Color.Parse("#000000"), 1);
                    }
                )
            )
        );
        var initial = root.Drawing!.Current!;
        var shaper = new EmptyShaper();
        using var first = SceneLayout.Project(composition, new(10, 10, 1), shaper);
        using var idle = SceneLayout.ProjectFrame(composition, new(10, 10, 1), shaper, first);
        Assert.AreEqual(1, reads);
        Assert.AreSame(initial, root.Drawing.Current);

        input.Value = 2;
        graph.Drain();
        Assert.AreEqual(2, reads, "The distinct input did not rerecord the drawing.");
        Assert.AreSame(initial, root.Drawing.Current, "Equal drawing commands changed identity.");

        input.Value = -1;
        graph.Drain();
        Assert.AreEqual(3, reads);
        Assert.AreNotSame(initial, root.Drawing.Current);
        Assert.IsFalse(initial.IsDisposed, "The prior retained scene lost its drawing resource.");
    }

    [TestMethod]
    public void FinalSceneLeaseOutlivesMountAndReleasesExactlyOnce()
    {
        using var composition = new Composition(new ReactiveGraph(), "drawing-lease");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var root = composition.Mount(
            composition.Root,
            theme,
            Components.Drawing(
                new(
                    new(8, 8),
                    recorder => recorder.FillEllipse(new(0, 0, 8, 8), Color.Parse("#336699"))
                )
            )
        );
        var drawing = root.Drawing!.Current!;
        var scene = SceneLayout.Project(composition, new(8, 8, 1), new EmptyShaper());
        var retained = scene.Retain();
        root.Dispose();
        scene.Dispose();
        Assert.IsFalse(drawing.IsDisposed);
        retained.Dispose();
        retained.Dispose();
        Assert.IsTrue(drawing.IsDisposed);
        Assert.AreEqual(1, drawing.FinalReleaseCount);
    }

    [TestMethod]
    public void RecorderRejectsExpiredUnbalancedNonfiniteAndOverBudgetInput()
    {
        DrawingRecorder? expired = null;
        DrawingPathRecorder? expiredPath = null;
        var descriptor = new DrawingDescriptor(
            new(10, 10),
            recorder =>
            {
                expired = recorder;
                var path = recorder.Path(builder =>
                {
                    expiredPath = builder;
                    builder.MoveTo(Vector2.Zero);
                    builder.LineTo(Vector2.One);
                });
                recorder.FillPath(path, Color.Parse("#000000"));
            }
        );
        using var frozen = new DrawingLease(new DrawingResource(descriptor.Freeze()));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            expired!.Line(Vector2.Zero, Vector2.One, Color.Parse("#000000"), 1)
        );
        Assert.ThrowsExactly<InvalidOperationException>(() => expiredPath!.LineTo(Vector2.One));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new DrawingDescriptor(new(0, 10), _ => { })
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new DrawingDescriptor(
                new(10, 10),
                recorder =>
                    recorder.Line(Vector2.Zero, new(float.NaN, 0), Color.Parse("#000000"), 1)
            ).Freeze()
        );
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new DrawingDescriptor(
                new(10, 10),
                recorder =>
                {
                    for (var index = 0; index <= DrawingRecorder.MaximumOperations; index++)
                        recorder.FillRectangle(new(0, 0, 1, 1), Color.Parse("#000000"));
                }
            ).Freeze()
        );
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new DrawingDescriptor(
                new(10, 10),
                recorder => _ = recorder.Clip(new(0, 0, 1, 1))
            ).Freeze()
        );
    }

    [TestMethod]
    public void FailedInitialPreparationRollsBackTheProvisionalRoot()
    {
        using var composition = new Composition(new ReactiveGraph(), "drawing-failure");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var recipe = Components.Drawing(
            new(new(10, 10), _ => throw new InvalidOperationException("record failed"))
        );

        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            composition.Mount(composition.Root, theme, recipe)
        );
        Assert.AreEqual("record failed", error.Message);
        Assert.AreEqual(0, composition.Root.Children.Count);
    }

    private static IEnumerable<SceneNode> Flatten(IEnumerable<SceneNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            var children =
                node is ClipSceneNode clip ? clip.Children
                : node is OpacitySceneNode opacity ? opacity.Children
                : null;
            if (children is not null)
                foreach (var child in Flatten(children))
                    yield return child;
        }
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.Text.Length == 0)
                return new("empty", 0, 0, []);
            var glyph = new ShapedGlyph(1, 0, 0, 0, request.Text.Length, 0, 0);
            var run = new ShapedRun(
                "drawing-test",
                "drawing-test",
                400,
                5,
                0,
                "drawing-test",
                0,
                "drawing-test#0",
                request.Direction,
                request.Language,
                request.FontSize,
                0,
                request.FontSize,
                -request.FontSize,
                0,
                request.Text.Length,
                [glyph]
            );
            return new("drawing-test", request.Text.Length, request.FontSize, [run]);
        }
    }
}
