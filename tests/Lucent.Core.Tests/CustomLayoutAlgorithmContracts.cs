using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class CustomLayoutAlgorithmContracts
{
    private static readonly Property<int> Order = new("test-layout-order", 0);

    [TestMethod]
    public void ContainerPropertyReadsTrackProjectionInputsAndExpireWithInvocation()
    {
        var algorithm = new ContainerMetadataAlgorithm();
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "custom-container-metadata");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var inset = graph.Signal(3, "container-inset");
        composition.Root.Present(
            theme,
            author: Style.Empty.Algorithm(algorithm).Bind(Order, () => inset.Value)
        );
        var child = composition.Child(composition.Root, "child");
        child.Present(theme, author: Style.Empty.Width(10).Height(10));
        var scene = SceneLayout.Project(composition, new(100, 40, 1), new EmptyShaper());
        Assert.AreEqual(3f, scene.Boxes.Single(box => box.Identity.ElementId == child.Id).Bounds.X);
        Assert.IsTrue(composition.Input.SetScene(scene));
        Expect<InvalidOperationException>(() => algorithm.Captured!.Read(Order));
        inset.Value = 7;
        graph.Drain();
        Assert.AreEqual(
            InputRejection.StaleScene,
            composition.Input.DispatchPointer(new(PointerCommandKind.Move, 1, 1, 1)).Rejection
        );
        var updated = SceneLayout.Project(composition, new(100, 40, 1), new EmptyShaper());
        Assert.AreEqual(
            7f,
            updated.Boxes.Single(box => box.Identity.ElementId == child.Id).Bounds.X
        );
    }

    [TestMethod]
    public void CustomAlgorithmReadsTypedMetadataAndPlacesStableChildren()
    {
        var algorithm = new MetadataAlgorithm();
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "custom-layout");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var firstOrder = graph.Signal(2, "first-order");
        composition.Root.Present(theme, author: Style.Empty.Algorithm(algorithm));
        var first = composition.Child(composition.Root, "first");
        first.Present(
            theme,
            author: Style.Empty.Width(10).Height(12).Bind(Order, () => firstOrder.Value)
        );
        var second = composition.Child(composition.Root, "second");
        second.Present(theme, author: Style.Empty.Width(15).Height(14).Set(Order, 1));

        var scene = SceneLayout.Project(composition, new(100, 40, 1), new EmptyShaper());
        var boxes = scene.Boxes.ToDictionary(box => box.Identity.ElementId, box => box.Bounds);

        Assert.AreEqual(new LayoutRect(20, 0, 10, 12), boxes[first.Id]);
        Assert.AreEqual(new LayoutRect(10, 0, 15, 14), boxes[second.Id]);
        Assert.AreSame(first, composition.Root.Children[0]);
        Assert.AreSame(second, composition.Root.Children[1]);
        Assert.IsTrue(composition.Input.SetScene(scene));

        firstOrder.Value = 3;
        graph.Drain();
        Assert.AreEqual(
            InputRejection.StaleScene,
            composition.Input.DispatchPointer(new(PointerCommandKind.Move, 1, 1, 1)).Rejection
        );
        var updated = SceneLayout.Project(composition, new(100, 40, 1), new EmptyShaper());
        Assert.AreEqual(
            30f,
            updated.Boxes.Single(box => box.Identity.ElementId == first.Id).Bounds.X
        );
    }

    [TestMethod]
    public void InvocationCachesTwoMeasurementsAndExpiresContext()
    {
        var algorithm = new MeasurementAlgorithm();
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "bounded-custom-layout");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(theme, author: Style.Empty.Algorithm(algorithm));
        var child = composition.Child(composition.Root, "child");
        child.Present(theme, author: Style.Empty.Width(20).Height(10));

        _ = SceneLayout.Project(composition, new(100, 40, 1), new EmptyShaper());

        Assert.AreEqual(new LayoutSize(15, 10), algorithm.FirstMeasured);
        Assert.AreEqual(algorithm.FirstMeasured, algorithm.SecondMeasured);
        Expect<InvalidOperationException>(() => _ = algorithm.Captured!.Children);
        Expect<InvalidOperationException>(() => _ = algorithm.CapturedChild!.DesiredSize);
    }

    [TestMethod]
    public void InvalidOrIncompleteResultsFailClosedAndStateDisposesWithElement()
    {
        var state = new DisposableState();
        var algorithm = new StateAlgorithm(state);
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "custom-layout-state");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(theme, author: Style.Empty.Algorithm(algorithm));
        var child = composition.Child(composition.Root, "child");
        child.Present(theme, author: Style.Empty.Width(10).Height(10));
        _ = SceneLayout.Project(composition, new(100, 40, 1), new EmptyShaper());
        _ = SceneLayout.Project(composition, new(120, 40, 1), new EmptyShaper());
        Assert.AreEqual(1, algorithm.Created);
        composition.Dispose();
        Assert.IsTrue(state.Disposed);

        var invalidGraph = new ReactiveGraph();
        using var invalid = new Composition(invalidGraph, "invalid-custom-layout");
        using var invalidTheme = new ThemeContext(invalid.Root.Scope, ControlThemes.Light);
        invalid.Root.Present(
            invalidTheme,
            author: Style.Empty.Algorithm(new EmptyResultAlgorithm())
        );
        var missing = invalid.Child(invalid.Root, "missing");
        missing.Present(invalidTheme, author: Style.Empty.Width(10).Height(10));
        Expect<InvalidOperationException>(() =>
            SceneLayout.Project(invalid, new(100, 40, 1), new EmptyShaper())
        );
    }

    [TestMethod]
    public void CallbackMutationFailsBeforeChangingReactiveState()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "mutating-custom-layout");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var value = graph.Signal(0, "value");
        composition.Root.Present(
            theme,
            author: Style.Empty.Algorithm(new MutatingAlgorithm(() => value.Value++))
        );
        var child = composition.Child(composition.Root, "child");
        child.Present(theme, author: Style.Empty.Width(1).Height(1));

        Expect<InvalidOperationException>(() =>
            SceneLayout.Project(composition, new(100, 40, 1), new EmptyShaper())
        );
        Assert.AreEqual(0, value.Value);
    }

    [TestMethod]
    public void StrategySwitchReleasesCustomStateAndExplicitAlgorithmOverridesLegacyMode()
    {
        var retained = new DisposableState();
        var custom = new StateAlgorithm(retained);
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "strategy-switch");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var selected = graph.Signal<LayoutAlgorithm?>(custom, "algorithm");
        composition.Root.Present(
            theme,
            author: Style
                .Empty.Mode(LayoutMode.Grid)
                .Bind(LayoutProperties.Algorithm, () => selected.Value)
        );
        var child = composition.Child(composition.Root, "child");
        child.Present(theme, author: Style.Empty.Width(10).Height(10));

        _ = SceneLayout.Project(composition, new(100, 40, 1), new EmptyShaper());
        Assert.IsFalse(retained.Disposed);
        selected.Value = LayoutAlgorithms.Flex;
        graph.Drain();
        _ = SceneLayout.Project(composition, new(100, 40, 1), new EmptyShaper());
        Assert.IsTrue(retained.Disposed);
    }

    [TestMethod]
    public void StrategySwitchAttemptsEveryStateCleanupWhenOneFails()
    {
        var first = new ThrowingState();
        var second = new SecondDisposableState();
        var custom = new MultipleStateAlgorithm(first, second);
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "strategy-cleanup-failure");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var selected = graph.Signal<LayoutAlgorithm?>(custom, "algorithm");
        composition.Root.Present(
            theme,
            author: Style.Empty.Bind(LayoutProperties.Algorithm, () => selected.Value)
        );
        var child = composition.Child(composition.Root, "child");
        child.Present(theme, author: Style.Empty.Width(10).Height(10));
        _ = SceneLayout.Project(composition, new(100, 40, 1), new EmptyShaper());

        selected.Value = LayoutAlgorithms.Flex;
        graph.Drain();
        Expect<InvalidOperationException>(() =>
            SceneLayout.Project(composition, new(100, 40, 1), new EmptyShaper())
        );
        Assert.IsTrue(first.DisposeAttempted);
        Assert.IsTrue(second.Disposed);
    }

    private sealed class MetadataAlgorithm() : LayoutAlgorithm("metadata")
    {
        public override LayoutAlgorithmResult Layout(LayoutAlgorithmContext context) =>
            new(
                new LayoutSize(30, 14),
                context
                    .Children.Select(child => new LayoutChildPlacement(
                        child.Index,
                        new LayoutRect(
                            child.Read(Order) * 10,
                            0,
                            child.DesiredSize.Width,
                            child.DesiredSize.Height
                        )
                    ))
                    .ToArray()
            );
    }

    private sealed class ContainerMetadataAlgorithm() : LayoutAlgorithm("container-metadata")
    {
        public LayoutAlgorithmContext? Captured { get; private set; }

        public override LayoutAlgorithmResult Layout(LayoutAlgorithmContext context)
        {
            Captured = context;
            var inset = context.Read(Order);
            return new(new(10 + inset, 10), new LayoutChildPlacement(0, new(inset, 0, 10, 10)));
        }
    }

    private sealed class MeasurementAlgorithm() : LayoutAlgorithm("measurement")
    {
        public LayoutAlgorithmContext? Captured { get; private set; }
        public LayoutChild? CapturedChild { get; private set; }
        public LayoutSize FirstMeasured { get; private set; }
        public LayoutSize SecondMeasured { get; private set; }

        public override LayoutAlgorithmResult Layout(LayoutAlgorithmContext context)
        {
            Captured = context;
            CapturedChild = context.Children[0];
            var constraints = new LayoutConstraints(
                new LayoutConstraint(15),
                LayoutConstraint.Unbounded
            );
            FirstMeasured = context.MeasureChild(CapturedChild, constraints);
            SecondMeasured = context.MeasureChild(CapturedChild, constraints);
            Expect<InvalidOperationException>(() =>
                context.MeasureChild(
                    CapturedChild,
                    new(new LayoutConstraint(14), LayoutConstraint.Unbounded)
                )
            );
            return new(
                FirstMeasured,
                new LayoutChildPlacement(0, new(0, 0, FirstMeasured.Width, FirstMeasured.Height))
            );
        }
    }

    private sealed class StateAlgorithm(DisposableState state) : LayoutAlgorithm("state")
    {
        public int Created { get; private set; }

        public override LayoutAlgorithmResult Layout(LayoutAlgorithmContext context)
        {
            _ = context.GetOrCreateState(() =>
            {
                Created++;
                return state;
            });
            return new(new(10, 10), new LayoutChildPlacement(0, new(0, 0, 10, 10)));
        }
    }

    private sealed class EmptyResultAlgorithm() : LayoutAlgorithm("empty-result")
    {
        public override LayoutAlgorithmResult Layout(LayoutAlgorithmContext context) =>
            new(new(0, 0));
    }

    private sealed class MutatingAlgorithm(Action mutate) : LayoutAlgorithm("mutating")
    {
        public override LayoutAlgorithmResult Layout(LayoutAlgorithmContext context)
        {
            mutate();
            return new(new(0, 0));
        }
    }

    private sealed class MultipleStateAlgorithm(ThrowingState first, SecondDisposableState second)
        : LayoutAlgorithm("multiple-state")
    {
        public override LayoutAlgorithmResult Layout(LayoutAlgorithmContext context)
        {
            _ = context.GetOrCreateState(() => first);
            _ = context.GetOrCreateState(() => second);
            return new(new(10, 10), new LayoutChildPlacement(0, new(0, 0, 10, 10)));
        }
    }

    private sealed class DisposableState : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class ThrowingState : IDisposable
    {
        public bool DisposeAttempted { get; private set; }

        public void Dispose()
        {
            DisposeAttempted = true;
            throw new InvalidOperationException("Expected cleanup failure.");
        }
    }

    private sealed class SecondDisposableState : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private static void Expect<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
            Assert.Fail($"Expected {typeof(T).Name}.");
        }
        catch (T) { }
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }
}
