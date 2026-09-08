using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class BreakpointCompatibilityContracts
{
    [TestMethod]
    public void ResponsiveBranchMountingWindowBreakpointsStabilizesAndRemounts()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("breakpoint-compatibility-owner");
        using var constraints = new ResponsiveConstraints(owner, "responsive");
        var wide = new Breakpoint("wide", 840);
        using var breakpoints = new WindowBreakpoints(
            owner,
            BreakpointSet.Create(wide),
            "branch-breakpoints"
        );
        using var composition = new Composition(graph, "breakpoint-compatibility");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var branch = Components.Layout(
            ComponentContent.Empty,
            style: Style.Empty.When(
                () => breakpoints.IsActive(wide),
                Style.Empty.Set(LayoutProperties.Width, 40f)
            ),
            breakpoints: breakpoints
        );
        ComponentContent content =
        [
            ContentRecipe.When("wide-branch", () => constraints.Current.Width >= 800, branch),
        ];
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.ResponsiveContainer(content, constraints)
        );

        var wideScene = SceneLayout.Project(composition, new(900, 100, 1), new EmptyShaper());
        var mounted = composition
            .Elements()
            .SingleOrDefault(element =>
                element.Name.StartsWith("layout", StringComparison.Ordinal)
            );
        Assert.IsNotNull(
            mounted,
            "Mounted elements: "
                + string.Join(", ", composition.Elements().Select(element => element.Name))
        );
        Assert.IsTrue(breakpoints.IsActive(wide));
        Assert.AreEqual(900f, breakpoints.Width);
        Assert.AreEqual(40f, mounted.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(
            40f,
            wideScene.Boxes.Single(box => box.Identity.ElementId == mounted.Id).Bounds.Width
        );

        _ = SceneLayout.Project(composition, new(700, 100, 1), new EmptyShaper());
        Assert.IsFalse(
            composition
                .Elements()
                .Any(element => element.Name.StartsWith("layout", StringComparison.Ordinal))
        );

        var remountedScene = SceneLayout.Project(composition, new(900, 100, 1), new EmptyShaper());
        var remounted = composition
            .Elements()
            .Single(element => element.Name.StartsWith("layout", StringComparison.Ordinal));
        Assert.IsTrue(breakpoints.IsActive(wide));
        Assert.AreEqual(900f, breakpoints.Width);
        Assert.AreEqual(40f, remounted.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(
            40f,
            remountedScene.Boxes.Single(box => box.Identity.ElementId == remounted.Id).Bounds.Width
        );
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }
}
