using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class LayoutDiagnosticsContracts
{
    [TestMethod]
    [DataRow("throw")]
    [DataRow("null")]
    [DataRow("incomplete")]
    [DataRow("measurement")]
    public void AlgorithmFailuresIdentifyOwnerPreserveCauseAndExpireContext(string failure)
    {
        var original = new FormatException("application algorithm failed");
        var algorithm = new ProbeAlgorithm(context =>
        {
            if (failure == "throw")
                throw original;
            if (failure == "null")
                return null!;
            if (failure == "measurement")
            {
                _ = context.MeasureChild(context.Children[0], new(new(5), new(5)));
                _ = context.MeasureChild(context.Children[0], new(new(6), new(6)));
            }
            return new(new(0, 0));
        });
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "diagnostic-container");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(theme, author: Style.Empty.Algorithm(algorithm));
        var child = composition.Child(composition.Root, "child");
        child.Present(theme, author: Style.Empty.Width(10).Height(10));

        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            SceneLayout.Project(composition, new(100, 40, 1), new EmptyShaper())
        );
        StringAssert.Contains(error.Message, algorithm.Name);
        StringAssert.Contains(error.Message, composition.Root.Name);
        StringAssert.Contains(error.Message, $"element {composition.Root.Id}");
        Assert.IsNotNull(error.InnerException);
        if (failure == "throw")
            Assert.AreSame(original, error.InnerException);
        else
            Assert.IsInstanceOfType<InvalidOperationException>(error.InnerException);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = algorithm.Context!.Children);
    }

    [TestMethod]
    public void BreakpointDumpDistinguishesUnregisteredMountedAndAssignedWithoutChangingBaseReads()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("reader-owner");
        var wide = new Breakpoint("wide", 600);
        using var reader = new WindowBreakpoints(owner, BreakpointSet.Create(wide));
        Assert.AreEqual(0f, reader.Width);
        Assert.IsFalse(reader.IsActive(wide));
        StringAssert.Contains(graph.Dump(), "window-breakpoints: unregistered");
        using var composition = new Composition(graph, "diagnostic-window");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var mount = composition.Mount(
            composition.Root,
            theme,
            Components.Layout(ComponentContent.Empty, breakpoints: reader)
        );
        StringAssert.Contains(graph.Dump(), "window-breakpoints: mounted, awaiting viewport");
        Assert.AreEqual(0f, reader.Width);
        using var scene = SceneLayout.Project(composition, new(700, 100, 1), new EmptyShaper());
        Assert.IsTrue(reader.IsActive(wide));
        StringAssert.Contains(graph.Dump(), "window-breakpoints: mounted, viewport assigned");
        mount.Dispose();
        StringAssert.Contains(graph.Dump(), "window-breakpoints: unregistered");
        Assert.AreEqual(700f, reader.Width, "Unmounting must not rewrite the reader's last value.");
        reader.Dispose();
        Assert.IsFalse(graph.Dump().Contains("window-breakpoints:", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CollapsedAlgorithmStateReleasesAtNextParticipationOrUnmount(bool unmount)
    {
        var resource = new Resource();
        var algorithm = new ProbeAlgorithm(context =>
        {
            _ = context.GetOrCreateState(() => resource);
            return new(new(10, 10));
        });
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "collapsed-algorithm");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(theme);
        var container = composition.Child(composition.Root, "retained-container");
        var participation = graph.Signal(ElementParticipation.Visible, "participation");
        var selected = graph.Signal<LayoutAlgorithm?>(algorithm, "algorithm");
        var componentState = container.Scope.Signal(7, "component-local-state");
        container.Present(
            theme,
            author: Style
                .Empty.Bind(LayoutProperties.Algorithm, () => selected.Value)
                .Bind(VisualProperties.Participation, () => participation.Value)
        );
        Project();
        participation.Value = ElementParticipation.Collapsed;
        selected.Value = LayoutAlgorithms.Flex;
        graph.Drain();
        Project();
        Assert.AreEqual(0, resource.Disposals);
        Assert.AreEqual(7, componentState.Value);
        if (unmount)
            container.Dispose();
        else
        {
            participation.Value = ElementParticipation.Visible;
            graph.Drain();
            Project();
            Assert.AreEqual(7, componentState.Value);
            Assert.IsFalse(container.IsDisposed);
        }
        Assert.AreEqual(1, resource.Disposals);
        composition.Dispose();
        Assert.AreEqual(1, resource.Disposals);

        void Project()
        {
            using var scene = SceneLayout.Project(composition, new(100, 40, 1), new EmptyShaper());
        }
    }

    [TestMethod]
    public void CustomAlgorithmAssignsViewportWithoutMeasuringEntireVirtualSource()
    {
        LayoutSize? desired = null;
        var algorithm = new ProbeAlgorithm(context =>
        {
            desired = context.Children[0].DesiredSize;
            return new(new(100, 40), new LayoutChildPlacement(0, new(0, 0, 100, 40)));
        });
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "custom-virtual-viewport");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(theme, author: Style.Empty.Algorithm(algorithm));
        var viewport = composition.Child(composition.Root, "viewport");
        _ = Controls.ScrollViewport(viewport, theme, "Rows");
        var list = Controls.VirtualizedList(
            viewport,
            theme,
            "rows",
            "Rows",
            () => Enumerable.Range(0, 10000),
            value => value,
            (value, context) =>
            {
                var row = context.Element("row");
                row.Present(theme);
                return row;
            },
            10
        );
        graph.Drain();
        using var scene = SceneLayout.Project(composition, new(100, 40, 1), new EmptyShaper());
        Assert.AreEqual(new LayoutSize(0, 0), desired);
        Assert.AreEqual(
            new LayoutRect(0, 0, 100, 40),
            scene.Boxes.Single(box => box.Identity.ElementId == viewport.Id).Bounds
        );
        Assert.IsTrue(list.Items.Count > 0 && list.Items.Count < 20);
    }

    private sealed class ProbeAlgorithm(Func<LayoutAlgorithmContext, LayoutAlgorithmResult> run)
        : LayoutAlgorithm("diagnostic-probe")
    {
        internal LayoutAlgorithmContext? Context { get; private set; }

        public override LayoutAlgorithmResult Layout(LayoutAlgorithmContext context)
        {
            Context = context;
            return run(context);
        }
    }

    private sealed class Resource : IDisposable
    {
        internal int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }
}
