using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class WindowBreakpointContracts
{
    [TestMethod]
    public void SharedDescriptorCanBelongToIndependentSetsAndWindows()
    {
        var wide = new Breakpoint("wide", 840);
        var firstSet = BreakpointSet.Create(wide);
        var secondSet = BreakpointSet.Create(new Breakpoint("medium", 600), wide);
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("owners");
        using var first = new WindowBreakpoints(owner, firstSet, "first");
        using var second = new WindowBreakpoints(owner, secondSet, "second");
        using var firstComposition = Mount(graph, first, "first-window");
        using var secondComposition = Mount(graph, second, "second-window");

        _ = SceneLayout.Project(firstComposition, new(900, 100, 1), new EmptyShaper());
        _ = SceneLayout.Project(secondComposition, new(700, 100, 1), new EmptyShaper());

        Assert.IsTrue(first.IsActive(wide));
        Assert.IsFalse(second.IsActive(wide));
        Assert.AreEqual(900f, first.Width);
        Assert.AreEqual(700f, second.Width);
    }

    [TestMethod]
    public void WindowWidthDrivesConditionalStyleBeforeLayoutAndOnlyRepublishesBuckets()
    {
        var graph = new ReactiveGraph();
        var wide = new Breakpoint("wide", 840);
        using var owner = graph.CreateScope("owner");
        using var state = new WindowBreakpoints(owner, BreakpointSet.Create(wide));
        using var composition = new Composition(graph, "breakpoint-style");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var bindingReads = 0;
        var mounted = composition.Mount(
            composition.Root,
            theme,
            Components.Layout(
                ComponentContent.Empty,
                style: Style.Empty.When(
                    () => state.IsActive(wide),
                    Style.Empty.Bind(
                        LayoutProperties.Width,
                        () =>
                        {
                            bindingReads++;
                            return 400f;
                        }
                    )
                ),
                breakpoints: state
            )
        );

        _ = SceneLayout.Project(composition, new(839, 100, 1), new EmptyShaper());
        Assert.IsNull(mounted.Resolve(LayoutProperties.Width).Value);
        _ = SceneLayout.Project(composition, new(840, 100, 1.5f), new EmptyShaper());
        Assert.AreEqual(400f, mounted.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(1, bindingReads);
        _ = SceneLayout.Project(composition, new(900, 100, 2), new EmptyShaper());
        Assert.AreEqual(
            1,
            bindingReads,
            "A same-bucket resize republished conditional assignments."
        );
        Assert.AreEqual(900f, state.Width);
    }

    [TestMethod]
    public void SetsAndMountsRejectAmbiguousOrForeignUse()
    {
        var medium = new Breakpoint("medium", 600);
        var wide = new Breakpoint("wide", 840);
        _ = BreakpointSet.Create(medium, wide);
        Expect<ArgumentException>(() => BreakpointSet.Create(wide, medium));
        Expect<ArgumentException>(() => BreakpointSet.Create(wide, new Breakpoint("other", 840)));
        Expect<ArgumentException>(() => BreakpointSet.Create(wide, new Breakpoint("wide", 1060)));

        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("owner");
        using var state = new WindowBreakpoints(owner, BreakpointSet.Create(wide));
        Expect<ArgumentException>(() => state.IsActive(new Breakpoint("wide", 840)));
        using var first = Mount(graph, state, "first-mount");
        using var second = new Composition(graph, "second-mount");
        using var theme = new ThemeContext(second.Root.Scope, ControlThemes.Light);
        Expect<InvalidOperationException>(() =>
            second.Mount(
                second.Root,
                theme,
                Components.Layout(ComponentContent.Empty, breakpoints: state)
            )
        );
    }

    private static Composition Mount(ReactiveGraph graph, WindowBreakpoints state, string name)
    {
        var composition = new Composition(graph, name);
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Layout(ComponentContent.Empty, breakpoints: state)
        );
        return composition;
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
