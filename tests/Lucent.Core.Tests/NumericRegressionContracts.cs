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
    public void NumberStepPreviewIsSideEffectFreeAndMatchesBoundedInvocation()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "numeric-step-preview");
        var requests = new List<decimal?>();
        using var session = new NumericEditSession(
            composition.Root.Scope,
            () => 8m,
            requests.Add,
            new NumericEditOptions(
                3m,
                minimum: 0m,
                maximum: 10m,
                culture: CultureInfo.InvariantCulture
            ),
            "step-preview"
        );

        Assert.IsTrue(session.TryPreviewStep(-1, out var decrease));
        Assert.AreEqual(5m, decrease);
        Assert.IsTrue(session.TryPreviewStep(1, out var increase));
        Assert.AreEqual(10m, increase);
        Assert.IsTrue(session.CanStep(-1));
        Assert.IsTrue(session.CanStep(1));
        Assert.AreEqual("8", session.Draft);
        Assert.AreEqual(ValidationStatus.Valid, session.Validation.Status);
        Assert.IsEmpty(requests);

        Assert.IsTrue(session.Step(1));
        Assert.AreEqual("10", session.Draft);
        Assert.AreEqual(10m, requests.Single());
        Assert.IsFalse(session.CanStep(1));
        Assert.IsTrue(session.CanStep(-1));
        Assert.IsFalse(session.Step(1));
        Assert.HasCount(1, requests);

        session.Edit("0");
        Assert.IsFalse(session.CanStep(-1));
        Assert.IsTrue(session.CanStep(1));
    }

    [TestMethod]
    public void InvalidAndConflictedDraftsDisableBothNumberStepsWithoutReplacingText()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "numeric-step-invalid");
        var applied = graph.Signal<decimal?>(5m, "numeric-applied");
        var requests = new List<decimal?>();
        using var session = new NumericEditSession(
            composition.Root.Scope,
            () => applied.Value,
            requests.Add,
            new NumericEditOptions(
                1m,
                minimum: 0m,
                maximum: 10m,
                culture: CultureInfo.InvariantCulture
            ),
            "invalid-step"
        );

        foreach (var draft in new[] { "bad", "", "11" })
        {
            session.Edit(draft);
            Assert.IsFalse(session.CanStep(-1), draft);
            Assert.IsFalse(session.CanStep(1), draft);
            Assert.AreEqual(draft, session.Draft);
        }

        session.Edit("6");
        applied.Value = 7m;
        graph.Drain();
        Assert.IsTrue(session.Validation.IsInvalid);
        Assert.IsFalse(session.CanStep(-1));
        Assert.IsFalse(session.CanStep(1));
        Assert.AreEqual("6", session.Draft);
        Assert.IsFalse(session.Step(1));
        Assert.IsEmpty(requests);
    }

    [TestMethod]
    public void NumberFieldActionsUseCompactIconsAndDisableOnlyUnavailableDirections()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "number-field-actions");
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var applied = graph.Signal<decimal?>(10m, "number-field-applied");
        composition.Mount(
            composition.Root,
            theme,
            Components.NumberField(
                "Quantity",
                () => applied.Value,
                next => applied.Value = next,
                new NumericEditOptions(
                    2m,
                    minimum: 0m,
                    maximum: 10m,
                    culture: CultureInfo.InvariantCulture
                )
            )
        );
        graph.Drain();

        var nodes = Nodes(composition.SemanticSnapshot()!).ToArray();
        var decrease = nodes.Single(node => node.Name == "Decrease Quantity");
        var increase = nodes.Single(node => node.Name == "Increase Quantity");
        Assert.AreEqual(SemanticRole.Button, decrease.Role);
        Assert.AreEqual(SemanticRole.Button, increase.Role);
        Assert.IsTrue(decrease.Enabled);
        Assert.IsFalse(increase.Enabled);

        applied.Value = 6m;
        graph.Drain();
        nodes = Nodes(composition.SemanticSnapshot()!).ToArray();
        Assert.IsTrue(nodes.Single(node => node.Name == "Decrease Quantity").Enabled);
        Assert.IsTrue(nodes.Single(node => node.Name == "Increase Quantity").Enabled);
    }

    [TestMethod]
    public void NumberFieldStepperPointerActivationRetainsEditorFocusAndSkipsBlurCommit()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "number-field-pointer-step");
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var applied = graph.Signal<decimal?>(3m, "number-field-pointer-applied");
        var requests = new List<decimal?>();
        composition.Mount(
            composition.Root,
            theme,
            Components.NumberField(
                "Quantity",
                () => applied.Value,
                next =>
                {
                    requests.Add(next);
                    applied.Value = next;
                },
                new NumericEditOptions(1m, 0m, 10m, culture: CultureInfo.InvariantCulture)
            )
        );
        graph.Drain();
        using var scene = Install(composition);
        var editor = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.TextField);
        var increase = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Name == "Increase Quantity");
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.AreEqual(editor.Identity.ElementId, composition.Input.FocusedElement?.ElementId);
        Assert.IsTrue(
            composition
                .Input.DispatchKey(new(KeyCommandKind.Down, Key.A, KeyModifiers.Control))
                .Handled
        );
        graph.Drain();
        var before = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Identity.ElementId == editor.Identity.ElementId);
        Assert.IsNotNull(before.Text);
        Assert.AreEqual("3", before.Text!.Text);
        Assert.AreEqual(0, before.Text!.Anchor);
        Assert.AreEqual(1, before.Text.Caret);

        using var selectedScene = ReplaceScene(composition);
        var bounds = selectedScene
            .Boxes.Single(box => box.Identity.ElementId == increase.Identity.ElementId)
            .Bounds;
        Assert.IsTrue(
            composition
                .Input.DispatchPointer(
                    new(
                        PointerCommandKind.Down,
                        1,
                        bounds.X + bounds.Width / 2,
                        bounds.Y + bounds.Height / 2,
                        PointerButton.Primary
                    )
                )
                .Handled
        );
        using var pressedScene = ReplaceScene(composition);
        Assert.IsTrue(
            composition
                .Input.DispatchPointer(
                    new(
                        PointerCommandKind.Up,
                        1,
                        bounds.X + bounds.Width / 2,
                        bounds.Y + bounds.Height / 2,
                        PointerButton.Primary
                    )
                )
                .Handled
        );
        graph.Drain();

        Assert.HasCount(1, requests);
        Assert.AreEqual(4m, requests.Single());
        Assert.AreEqual(editor.Identity.ElementId, composition.Input.FocusedElement?.ElementId);
        var after = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Identity.ElementId == editor.Identity.ElementId);
        Assert.IsNotNull(after.Text);
        Assert.AreEqual("4", after.Text!.Text);
        Assert.AreEqual(0, after.Text.Anchor);
        Assert.AreEqual(1, after.Text.Caret);
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

    [TestMethod]
    public void SliderEscapeRollsBackAnImmediatelyAcceptedGestureWithoutRecommitting() =>
        AssertSliderEscapeRollsBackWithoutRecommitting(
            acceptImmediately: true,
            acceptAfterMove: false
        );

    [TestMethod]
    public void SliderEscapeRollsBackADelayedAcceptedGestureWithoutRecommitting() =>
        AssertSliderEscapeRollsBackWithoutRecommitting(
            acceptImmediately: false,
            acceptAfterMove: true
        );

    [TestMethod]
    public void SliderEscapeRollsBackARejectedGestureWithoutRecommitting() =>
        AssertSliderEscapeRollsBackWithoutRecommitting(
            acceptImmediately: false,
            acceptAfterMove: false
        );

    private static void AssertSliderEscapeRollsBackWithoutRecommitting(
        bool acceptImmediately,
        bool acceptAfterMove
    )
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "slider-escape-rollback");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var applied = graph.Signal(20d, "slider.applied");
        var requests = new List<double>();
        var commits = new List<double>();
        composition.Mount(
            composition.Root,
            theme,
            Components.Slider(
                "Progress",
                () => applied.Value,
                value =>
                {
                    requests.Add(value);
                    if (acceptImmediately)
                        applied.Value = value;
                },
                new SliderOptions(0, 100, 1),
                commits.Add,
                style: Style.Empty.Width(280)
            )
        );

        using var scene = Install(composition);
        var bounds = SliderBounds(composition, scene);
        var y = bounds.Y + bounds.Height / 2;
        var startX = bounds.X + bounds.Width * 0.2f;
        var movedX = bounds.X + bounds.Width * 0.8f;
        Assert.IsTrue(
            composition
                .Input.DispatchPointer(
                    new(PointerCommandKind.Down, 41, startX, y, PointerButton.Primary)
                )
                .Handled
        );
        requests.Clear();
        Assert.IsTrue(
            composition.Input.DispatchPointer(new(PointerCommandKind.Move, 41, movedX, y)).Handled
        );
        if (acceptAfterMove)
            applied.Value = requests[^1];
        graph.Drain();
        using var refreshed = ReplaceScene(composition);

        Assert.IsTrue(composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Escape)).Handled);
        Assert.AreEqual(20d, requests[^1], "Escape did not request the gesture-start value.");
        _ = composition.Input.DispatchPointer(
            new(PointerCommandKind.Up, 41, movedX, y, PointerButton.Primary)
        );
        Assert.AreEqual(0, commits.Count, "Release recommitted an escaped slider gesture.");
        Assert.AreEqual(20d, requests[^1], "Release replaced the rollback request.");
    }

    [TestMethod]
    public void SliderIdleEscapeBubblesAndSecondaryReleaseCannotFinishPrimaryDrag()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "slider-button-ownership");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var requests = new List<double>();
        var commits = new List<double>();
        composition.Mount(
            composition.Root,
            theme,
            Components.Slider(
                "Progress",
                () => 20,
                requests.Add,
                new SliderOptions(0, 100, 1),
                commits.Add,
                style: Style.Empty.Width(280)
            )
        );

        using var scene = Install(composition);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.IsFalse(
            composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Escape)).Handled,
            "An idle slider consumed an ancestor Escape action."
        );
        var bounds = SliderBounds(composition, scene);
        var y = bounds.Y + bounds.Height / 2;
        var startX = bounds.X + bounds.Width * 0.2f;
        var firstX = bounds.X + bounds.Width * 0.8f;
        var finalX = bounds.X + bounds.Width * 0.6f;
        Assert.IsTrue(
            composition
                .Input.DispatchPointer(
                    new(PointerCommandKind.Down, 42, startX, y, PointerButton.Primary)
                )
                .Handled
        );
        Assert.IsTrue(
            composition.Input.DispatchPointer(new(PointerCommandKind.Move, 42, firstX, y)).Handled
        );
        Assert.IsFalse(
            composition
                .Input.DispatchPointer(
                    new(PointerCommandKind.Up, 42, firstX, y, PointerButton.Secondary)
                )
                .Handled,
            "A secondary release completed the primary drag."
        );
        Assert.AreEqual(0, commits.Count);
        Assert.IsTrue(
            composition.Input.DispatchPointer(new(PointerCommandKind.Move, 42, finalX, y)).Handled
        );
        Assert.IsTrue(
            composition
                .Input.DispatchPointer(
                    new(PointerCommandKind.Up, 42, finalX, y, PointerButton.Primary)
                )
                .Handled
        );
        Assert.AreEqual(1, commits.Count);
        Assert.AreEqual(60d, commits.Single());
    }

    [TestMethod]
    public void SliderCaptureLossRollsBackTheAcceptedGestureWithoutCommit()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "slider-capture-loss-rollback");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var enabled = graph.Signal(true, "slider.enabled");
        var applied = graph.Signal(20d, "slider.applied");
        var requests = new List<double>();
        var commits = new List<double>();
        composition.Mount(
            composition.Root,
            theme,
            Components.Slider(
                "Progress",
                () => applied.Value,
                value =>
                {
                    requests.Add(value);
                    applied.Value = value;
                },
                new SliderOptions(0, 100, 1),
                commits.Add,
                enabled: () => enabled.Value,
                style: Style.Empty.Width(280)
            )
        );

        using var scene = Install(composition);
        var bounds = SliderBounds(composition, scene);
        var y = bounds.Y + bounds.Height / 2;
        Assert.IsTrue(
            composition
                .Input.DispatchPointer(
                    new(
                        PointerCommandKind.Down,
                        43,
                        bounds.X + bounds.Width * 0.2f,
                        y,
                        PointerButton.Primary
                    )
                )
                .Handled
        );
        requests.Clear();
        Assert.IsTrue(
            composition
                .Input.DispatchPointer(
                    new(PointerCommandKind.Move, 43, bounds.X + bounds.Width * 0.8f, y)
                )
                .Handled
        );
        graph.Drain();
        enabled.Value = false;
        graph.Drain();
        using var disabled = ReplaceScene(composition);

        Assert.AreEqual(20d, requests[^1], "Capture loss did not restore the gesture start.");
        Assert.AreEqual(0, commits.Count, "Capture loss committed the abandoned gesture.");
    }

    private static LayoutRect SliderBounds(Composition composition, RetainedScene scene)
    {
        var slider = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.Slider);
        return scene
            .Boxes.Single(box => box.Identity.ElementId == slider.Identity.ElementId)
            .Bounds;
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

    private sealed class ImmediatePreparer : IImagePreparer
    {
        public ValueTask<PreparedImage> PrepareAsync(
            ImagePreparationRequest request,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult<PreparedImage>(new RasterImage(1, 1, [0, 0, 0, 255]));
    }
}
