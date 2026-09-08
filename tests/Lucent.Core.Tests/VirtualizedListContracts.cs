using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class VirtualizedListContracts
{
    [TestMethod]
    public void LargeUnstyledListRealizesFromAssignedViewport()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "virtualized-default-layout");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        Controls.Column(composition.Root, theme, "root");
        var factoryCalls = 0;
        var list = composition.Mount(
            composition.Root,
            theme,
            Components.VirtualizedList(
                () => Enumerable.Range(0, 10_000),
                value => value,
                value =>
                {
                    factoryCalls++;
                    return Components.Text("Row " + value.Value);
                },
                () => 24f,
                label: "Rows"
            )
        );
        graph.Drain();

        var scene = SceneLayout.Project(composition, new(320, 200, 1), new EmptyShaper());
        var bounds = scene.Boxes.Single(box => box.Identity.ElementId == list.Id).Bounds;
        Assert.IsTrue(
            bounds.Height == 200 && factoryCalls > 0 && factoryCalls < 100,
            "An unstyled 10,000-row list measured its source as its viewport: bounds="
                + bounds
                + " factories="
                + factoryCalls
        );
    }

    [TestMethod]
    public void ExplicitListHeightOverridesStockFillDefault()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "virtualized-explicit-layout");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        Controls.Column(composition.Root, theme, "root");
        var list = composition.Mount(
            composition.Root,
            theme,
            Components.VirtualizedList(
                () => Enumerable.Range(0, 10_000),
                value => value,
                value => Components.Text("Row " + value.Value),
                () => 24f,
                label: "Rows",
                style: Style.Empty.Height(48)
            )
        );
        graph.Drain();

        var scene = SceneLayout.Project(composition, new(320, 200, 1), new EmptyShaper());
        var bounds = scene.Boxes.Single(box => box.Identity.ElementId == list.Id).Bounds;
        Assert.IsTrue(
            bounds.Height == 48,
            "An explicit list height was overridden by the stock fill default: " + bounds
        );
    }

    [TestMethod]
    public void WidthOnlyListStyleStaysBoundedInAColumn()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "virtualized-width-column");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        Controls.Column(composition.Root, theme, "root");
        var factoryCalls = 0;
        var list = composition.Mount(
            composition.Root,
            theme,
            Components.VirtualizedList(
                () => Enumerable.Range(0, 10_000),
                value => value,
                value =>
                {
                    factoryCalls++;
                    return Components.Text("Row " + value.Value);
                },
                () => 24f,
                label: "Rows",
                style: Style.Empty.Width(180)
            )
        );
        graph.Drain();

        var scene = SceneLayout.Project(composition, new(320, 200, 1), new EmptyShaper());
        var bounds = scene.Boxes.Single(box => box.Identity.ElementId == list.Id).Bounds;
        Assert.IsTrue(
            bounds.Width == 180 && bounds.Height == 200 && factoryCalls > 0 && factoryCalls < 100,
            "A width-only column list lost bounded fill: bounds="
                + bounds
                + " factories="
                + factoryCalls
        );
    }

    [TestMethod]
    public void WidthWinsAsTheMainGeometryInARow()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "virtualized-width-row");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        Controls.Row(composition.Root, theme, "root");
        var factoryCalls = 0;
        var list = composition.Mount(
            composition.Root,
            theme,
            Components.VirtualizedList(
                () => Enumerable.Range(0, 10_000),
                value => value,
                value =>
                {
                    factoryCalls++;
                    return Components.Text("Row " + value.Value);
                },
                () => 24f,
                label: "Rows",
                style: Style.Empty.Width(96)
            )
        );
        graph.Drain();

        var scene = SceneLayout.Project(composition, new(320, 200, 1), new EmptyShaper());
        var bounds = scene.Boxes.Single(box => box.Identity.ElementId == list.Id).Bounds;
        Assert.IsTrue(
            bounds.Width == 96 && bounds.Height == 200 && factoryCalls > 0 && factoryCalls < 100,
            "A width-only row list did not preserve author geometry: bounds="
                + bounds
                + " factories="
                + factoryCalls
        );
    }

    [TestMethod]
    public void NestedListInResponsiveSplitPaneStaysBoundedWithoutAnAuthorHeight()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "virtualized-nested-layout");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var constraints = new ResponsiveConstraints(composition.Root.Scope);
        using var split = new SplitPaneState(
            composition.Root.Scope,
            initialExtent: 160,
            minimumFirst: 80,
            minimumSecond: 80
        );
        var factoryCalls = 0;
        var root = composition.Mount(
            composition.Root,
            theme,
            Components.ResponsiveContainer(
                [
                    Components.Column([
                        Components.SplitPane(
                            [
                                Components.Column(
                                    [
                                        Components.VirtualizedList(
                                            () => Enumerable.Range(0, 10_000),
                                            value => value,
                                            value =>
                                            {
                                                if (++factoryCalls > 100)
                                                    throw new InvalidOperationException(
                                                        "Nested virtualized content exceeded its viewport realization guard."
                                                    );
                                                return Components.Text("Row " + value.Value);
                                            },
                                            () => 24f,
                                            label: "Rows"
                                        ),
                                    ],
                                    style: Style
                                        .Empty.Set(LayoutProperties.MainGrow, 1f)
                                        .Set(LayoutProperties.MinWidth, 0f)
                                        .Set(LayoutProperties.MinHeight, 0f)
                                ),
                            ],
                            [Components.Text("Details")],
                            split,
                            style: Style
                                .Empty.Set(LayoutProperties.MainGrow, 1f)
                                .Set(LayoutProperties.MinWidth, 0f)
                                .Set(LayoutProperties.MinHeight, 0f)
                        ),
                    ]),
                ],
                constraints,
                style: Style
                    .Empty.Set(LayoutProperties.MainGrow, 1f)
                    .Set(LayoutProperties.MinWidth, 0f)
                    .Set(LayoutProperties.MinHeight, 0f)
            )
        );
        graph.Drain();

        var scene = SceneLayout.Project(composition, new(700, 400, 1), new EmptyShaper());
        var bounds = scene.Boxes.Single(box => box.Identity.ElementId == root.Id).Bounds;
        Assert.IsTrue(
            bounds.Width == 700 && bounds.Height == 400 && factoryCalls > 0 && factoryCalls < 100,
            "A nested 10,000-row split-pane list escaped its finite viewport: bounds="
                + bounds
                + " factories="
                + factoryCalls
        );
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.Text.Length == 0)
                return new("empty", 0, 0, []);
            var glyph = new ShapedGlyph(1, 0, 0, 0, request.Text.Length, 0, 0);
            var run = new ShapedRun(
                "virtualized-list-test",
                "virtualized-list-test",
                400,
                5,
                0,
                "virtualized-list-test",
                0,
                "virtualized-list-test#0",
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
            return new("virtualized-list-test", request.Text.Length, request.FontSize, [run]);
        }
    }
}
