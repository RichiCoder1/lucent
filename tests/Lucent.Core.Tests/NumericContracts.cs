using System.Globalization;
using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class NumericContracts
{
    [TestMethod]
    public void DecimalDraftUsesCultureAndRetainsMalformedIntermediateText()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "numeric-draft");
        decimal? applied = 4m;
        decimal? requested = null;
        using var session = new NumericEditSession(
            composition.Root.Scope,
            () => applied,
            value => requested = value,
            new NumericEditOptions(0.5m, culture: CultureInfo.GetCultureInfo("fr-FR"))
        );

        session.Edit("-");
        Assert.IsTrue(session.Validation.IsValid);
        Assert.IsFalse(session.Commit());
        Assert.AreEqual("-", session.Draft);
        Assert.AreEqual(4m, applied);
        Assert.IsNull(requested);

        session.Edit("12,5");
        Assert.IsTrue(session.Commit());
        Assert.AreEqual(12.5m, requested);
        Assert.AreEqual(4m, applied);
        Assert.AreEqual("12,5", session.Draft);
    }

    [TestMethod]
    public void BoundsSteppingAndExternalDraftConflictAreExplicit()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "numeric-bounds");
        var applied = graph.Signal<decimal?>(5m, "applied");
        var requests = new List<decimal?>();
        using var session = new NumericEditSession(
            composition.Root.Scope,
            () => applied.Value,
            requests.Add,
            new NumericEditOptions(
                2m,
                0m,
                10m,
                boundsPolicy: NumericBoundsPolicy.Reject,
                culture: CultureInfo.InvariantCulture
            )
        );

        session.Edit("bad");
        Assert.IsFalse(session.Step(1));
        Assert.AreEqual(0, requests.Count);
        session.Edit("11");
        Assert.IsFalse(session.Commit());
        Assert.AreEqual("11", session.Draft);

        session.Edit("7");
        applied.Value = 6m;
        graph.Drain();
        Assert.AreEqual("7", session.Draft);
        Assert.IsTrue(session.Validation.IsInvalid);
        session.Cancel();
        Assert.AreEqual("6", session.Draft);

        Assert.IsTrue(session.Step(1));
        Assert.AreEqual(8m, requests.Single());
    }

    [TestMethod]
    public void GeneratedNumberFieldComposesFieldEditorAndNamedSteps()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "number-field");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        decimal? value = 3m;
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.NumberField(
                "Quantity",
                () => value,
                next => value = next,
                new NumericEditOptions(1m, 0m, 10m)
            )
        );
        graph.Drain();

        var nodes = Nodes(composition.SemanticSnapshot()!).ToArray();
        Assert.AreEqual("Quantity", nodes.Single(node => node.Role == SemanticRole.TextField).Name);
        Assert.IsTrue(
            nodes.Any(node => node.Role == SemanticRole.Button && node.Name == "Decrease Quantity")
        );
        Assert.IsTrue(
            nodes.Any(node => node.Role == SemanticRole.Button && node.Name == "Increase Quantity")
        );
    }

    [TestMethod]
    public void SliderKeyboardRequestsPreviewAndCommitWithoutChangingAppliedValue()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "slider");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var applied = graph.Signal(5d, "slider-applied");
        var previews = new List<double>();
        var commits = new List<double>();
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Slider(
                "Volume",
                () => applied.Value,
                previews.Add,
                new SliderOptions(0, 10, 1),
                commits.Add
            )
        );
        graph.Drain();
        using var scene = Install(composition);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.IsTrue(composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Right)).Handled);
        graph.Drain();

        Assert.AreEqual(6d, previews.Single());
        Assert.AreEqual(6d, commits.Single());
        Assert.AreEqual(5d, applied.Value);
        var slider = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.Slider);
        Assert.AreEqual(6d, slider.Range!.Value);
    }

    [TestMethod]
    public void SliderPointerAndFocusedOptInWheelCompleteExactlyOncePerGesture()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "slider-pointer");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var previews = new List<double>();
        var commits = new List<double>();
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Slider(
                "Balance",
                () => 5,
                previews.Add,
                new SliderOptions(0, 10, 1, wheelEnabled: true),
                commits.Add
            )
        );
        graph.Drain();
        using var scene = Install(composition);
        var slider = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.Slider);
        var bounds = scene
            .Boxes.Single(box => box.Identity.ElementId == slider.Identity.ElementId)
            .Bounds;
        var x = bounds.X + bounds.Width * 0.75f;
        var y = bounds.Y + bounds.Height / 2;
        Assert.IsTrue(
            composition
                .Input.DispatchPointer(new(PointerCommandKind.Down, 4, x, y, PointerButton.Primary))
                .Handled
        );
        Assert.IsTrue(
            composition
                .Input.DispatchPointer(new(PointerCommandKind.Up, 4, x, y, PointerButton.Primary))
                .Handled
        );
        graph.Drain();
        Assert.AreEqual(1, commits.Count);
        Assert.AreEqual(previews[^1], commits[^1]);

        using var wheelScene = Install(composition);
        var wheelBounds = wheelScene
            .Boxes.Single(box => box.Identity.ElementId == slider.Identity.ElementId)
            .Bounds;
        Assert.IsTrue(
            composition
                .Input.DispatchWheel(new(wheelBounds.X + 1, wheelBounds.Y + 1, 0, -120))
                .Handled
        );
        graph.Drain();
        Assert.AreEqual(2, commits.Count);
        Assert.AreEqual(commits[0] + 1, commits[1]);
    }

    [TestMethod]
    public void NumericAndSliderConfigurationRejectInvalidRanges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NumericEditOptions(0));
        Assert.Throws<ArgumentException>(() => new NumericEditOptions(1, 2, 1));
        Assert.Throws<ArgumentException>(() => new SliderOptions(1, 1, 0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SliderOptions(0, 1, double.NaN));
    }

    private static RetainedScene Install(Composition composition)
    {
        composition.Flush();
        var scene = SceneLayout.Project(composition, new(500, 500, 1), new NumericShaper());
        Assert.IsTrue(composition.Input.SetScene(scene));
        return scene;
    }

    private static IEnumerable<SemanticSnapshot> Nodes(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Nodes(child))
            yield return descendant;
    }

    private sealed class NumericShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.Text.Length == 0)
                return new("numeric-empty", 0, request.FontSize, []);
            var glyph = new ShapedGlyph(1, 0, 0, 0, request.Text.Length, 0, 0);
            var run = new ShapedRun(
                "numeric",
                "numeric",
                400,
                5,
                0,
                "numeric",
                0,
                "numeric#0",
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
            return new("numeric", request.Text.Length, request.FontSize, [run]);
        }
    }
}
