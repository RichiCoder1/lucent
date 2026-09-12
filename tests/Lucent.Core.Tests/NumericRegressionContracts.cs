using System.Globalization;
using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class NumericRegressionContracts
{
    [TestMethod]
    public void DecimalStepOverflowIsRejectedWithoutRequestingAValue()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "numeric-overflow-regression");
        var requests = new List<decimal?>();
        using var session = new NumericEditSession(
            composition.Root.Scope,
            () => decimal.MaxValue,
            requests.Add,
            new NumericEditOptions(1m, culture: CultureInfo.InvariantCulture),
            "overflow"
        );

        Assert.IsFalse(session.Step(1));
        Assert.AreEqual(0, requests.Count);
        Assert.AreEqual(decimal.MaxValue.ToString(CultureInfo.InvariantCulture), session.Draft);
        Assert.AreEqual(ValidationStatus.Invalid, session.Validation.Status);
    }

    [TestMethod]
    public void SliderRoundsHalfStepsAwayFromZeroBeforeClamping()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "numeric-rounding-regression");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var requests = new List<double>();
        composition.Mount(
            composition.Root,
            theme,
            Components.Slider(
                "Scale",
                () => 0,
                requests.Add,
                new SliderOptions(0, 1, 0.2),
                style: Style.Empty.Width(240)
            )
        );

        var slider = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.Slider);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(
                slider.Identity,
                new(SemanticCommandKind.SetRangeValue, NumericValue: 0.3)
            )
        );
        Assert.AreEqual(0.4, requests.Single(), 0.000001);
    }

    [TestMethod]
    public void SliderMidpointToleranceIsExactlyOneQuotientUlp()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "numeric-rounding-boundary");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var requests = new List<double>();
        composition.Mount(
            composition.Root,
            theme,
            Components.Slider(
                "Scale",
                () => 0,
                requests.Add,
                new SliderOptions(0, 1, 1),
                style: Style.Empty.Width(240)
            )
        );

        var slider = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.Slider);
        var oneUlpBelow = Math.BitDecrement(0.5);
        var twoUlpsBelow = Math.BitDecrement(oneUlpBelow);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(
                slider.Identity,
                new(SemanticCommandKind.SetRangeValue, NumericValue: oneUlpBelow)
            )
        );
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(
                slider.Identity,
                new(SemanticCommandKind.SetRangeValue, NumericValue: twoUlpsBelow)
            )
        );

        Assert.HasCount(2, requests);
        Assert.AreEqual(1d, requests[0]);
        Assert.AreEqual(0d, requests[1]);
    }

    [TestMethod]
    public void SliderPointerCancelAndCaptureLossRollbackWithoutCommit()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "numeric-pointer-cancel-regression");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var enabled = graph.Signal(true, "slider.enabled");
        var requests = new List<double>();
        var commits = new List<double>();
        composition.Mount(
            composition.Root,
            theme,
            Components.Slider(
                "Balance",
                () => 5,
                requests.Add,
                new SliderOptions(0, 10, 1),
                commits.Add,
                enabled: () => enabled.Value,
                style: Style.Empty.Width(280)
            )
        );

        var scene = Install(composition);
        try
        {
            var slider = Nodes(composition.SemanticSnapshot()!)
                .Single(node => node.Role == SemanticRole.Slider);
            var bounds = scene
                .Boxes.Single(box => box.Identity.ElementId == slider.Identity.ElementId)
                .Bounds;
            var x = bounds.X + bounds.Width * 0.8f;
            var y = bounds.Y + bounds.Height / 2;
            Assert.IsTrue(
                composition
                    .Input.DispatchPointer(
                        new(PointerCommandKind.Down, 31, x, y, PointerButton.Primary)
                    )
                    .Handled
            );
            Assert.IsTrue(
                composition.Input.DispatchPointer(new(PointerCommandKind.Cancel, 31, x, y)).Handled
            );

            using var replacement = ReplaceScene(composition);
            Assert.IsTrue(
                composition
                    .Input.DispatchPointer(
                        new(PointerCommandKind.Down, 32, x, y, PointerButton.Primary)
                    )
                    .Handled
            );
            enabled.Value = false;
            graph.Drain();
            using var disabled = ReplaceScene(composition);

            Assert.AreEqual(
                0,
                commits.Count,
                "Pointer cancellation and capture loss must not invoke Slider onCommit."
            );
            Assert.IsTrue(requests.Count >= 2, "Rollback did not restore the controlled value.");
        }
        finally
        {
            scene.Dispose();
        }
    }

    private static RetainedScene Install(Composition composition)
    {
        composition.Flush();
        var scene = SceneLayout.Project(composition, new(320, 140, 1), new NumericShaper());
        Assert.IsTrue(composition.Input.SetScene(scene), "Numeric regression scene was rejected.");
        return scene;
    }

    private static RetainedScene ReplaceScene(Composition composition)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            composition.Flush();
            var scene = SceneLayout.Project(composition, new(320, 140, 1), new NumericShaper());
            if (composition.Input.SetScene(scene))
                return scene;
            scene.Dispose();
        }
        throw new AssertFailedException("Numeric replacement scene did not converge.");
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
                return new("numeric-regression-empty", 0, request.FontSize, []);
            var glyph = new ShapedGlyph(1, 0, 0, 0, request.Text.Length, 0, 0);
            var run = new ShapedRun(
                "numeric-regression",
                "numeric-regression",
                400,
                5,
                0,
                "numeric-regression",
                0,
                "numeric-regression#0",
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
            return new("numeric-regression", request.Text.Length, request.FontSize, [run]);
        }
    }
}
