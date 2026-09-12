using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class NavigationContracts
{
    [TestMethod]
    public void ManualTabsRoveRetainVisitedPanelsAndPreserveControlledLag()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "tabs");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var mounts = new Dictionary<string, int>();
        var items = graph.Signal(Items(), "tabs.items");
        var selected = graph.Signal(SelectedKey.Some("a"), "tabs.selected");
        var requests = new List<string>();
        TabItem<string>[] Items() =>
            [
                new("a", "Alpha", () => Panel("Alpha panel", "a")),
                new("b", "Beta", () => Panel("Beta panel", "b"), enabled: false),
                new("c", "Charlie", () => Panel("Charlie panel", "c")),
            ];
        ComponentRecipe Panel(string label, string key)
        {
            mounts[key] = mounts.GetValueOrDefault(key) + 1;
            return Components.Text(label);
        }
        composition.Mount(
            composition.Root,
            theme,
            Components.Tabs(
                "Documents",
                () => items.Value,
                () => selected.Value,
                requests.Add,
                style: Style.Empty.Width(280).Height(150)
            )
        );
        graph.Drain();
        Assert.AreEqual(1, mounts["a"]);
        var alphaPanel = Node(composition, "Alpha panel");
        var scene = Install(composition, graph);
        var tabBounds = Tabs(composition)
            .Select(tab =>
                scene.Boxes.Single(box => box.Identity.ElementId == tab.Identity.ElementId).Bounds
            )
            .ToArray();
        Assert.IsTrue(tabBounds[0].X < tabBounds[1].X && tabBounds[1].X < tabBounds[2].X);
        Assert.AreEqual(tabBounds[0].Y, tabBounds[1].Y);
        Assert.AreEqual(tabBounds[1].Y, tabBounds[2].Y);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.AreEqual("Alpha", FocusedTab(composition).Name);
        Assert.IsTrue(composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Down)).Handled);
        graph.Drain();
        Assert.AreEqual("Charlie", FocusedTab(composition).Name);
        Assert.AreEqual(0, requests.Count, "Manual activation committed during arrow movement.");
        _ = composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Enter));
        Assert.AreEqual("c", requests.Single());
        Assert.AreEqual("Alpha", SelectedTab(composition).Name);

        selected.Value = SelectedKey.Some("c");
        graph.Drain();
        Assert.AreEqual(1, mounts["c"]);
        Assert.AreEqual(
            0,
            Nodes(composition.SemanticSnapshot()!).Count(node => node.Name == "Alpha panel")
        );
        selected.Value = SelectedKey.Some("a");
        graph.Drain();
        Assert.AreEqual(1, mounts["a"]);
        Assert.AreEqual(
            alphaPanel.Identity.ElementId,
            Node(composition, "Alpha panel").Identity.ElementId
        );

        var beforeRemoval = requests.Count;
        selected.Value = SelectedKey.Some("c");
        items.Value = [new("a", "Alpha", () => Panel("Alpha panel", "a"))];
        graph.Drain();
        Assert.AreEqual(beforeRemoval, requests.Count);
        Assert.AreEqual(0, Tabs(composition).Count(node => node.Selected));
        GC.KeepAlive(scene);
    }

    [TestMethod]
    public void DisclosureLazilyRetainsContentAndReturnsDescendantFocusBeforeCollapse()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "disclosure");
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var expanded = graph.Signal(false, "expanded");
        var mounts = 0;
        var content = ComponentRecipe.Defer(
            "lazy",
            _ =>
            {
                mounts++;
                return Components.Button("Inside", () => { }, Style.Empty.Width(80).Height(30));
            }
        );
        composition.Mount(
            composition.Root,
            theme,
            Components.Disclosure(
                [content],
                "Details",
                () => expanded.Value,
                value => expanded.Value = value,
                style: Style.Empty.Width(240)
            )
        );
        graph.Drain();
        Assert.AreEqual(0, mounts);
        var header = Node(composition, "Details");
        Assert.IsFalse(header.Expanded!.Value);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(header.Identity, new(SemanticCommandKind.Expand))
        );
        graph.Drain();
        Assert.AreEqual(1, mounts);
        var inside = Node(composition, "Inside");
        var scene = Install(composition, graph);
        Assert.IsTrue(
            composition.Input.FocusSemantic(
                new(inside.Identity.CompositionEpoch, inside.Identity.ElementId)
            )
        );
        header = Node(composition, "Details");
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(header.Identity, new(SemanticCommandKind.Collapse))
        );
        graph.Drain();
        Assert.AreEqual(header.Identity.ElementId, composition.Input.FocusedElement?.ElementId);
        Assert.AreEqual(
            0,
            Nodes(composition.SemanticSnapshot()!).Count(node => node.Name == "Inside")
        );
        header = Node(composition, "Details");
        _ = composition.ExecuteSemanticCommand(header.Identity, new(SemanticCommandKind.Expand));
        graph.Drain();
        Assert.AreEqual(1, mounts);
        Assert.AreEqual(inside.Identity.ElementId, Node(composition, "Inside").Identity.ElementId);
        GC.KeepAlive(scene);
    }

    private static RetainedScene Install(Composition composition, ReactiveGraph graph)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            graph.Drain();
            var scene = SceneLayout.Project(composition, new(320, 220, 1), new MetricShaper());
            if (composition.Input.SetScene(scene))
                return scene;
            scene.Dispose();
        }
        throw new InvalidOperationException("Navigation scene did not converge.");
    }

    private static SemanticSnapshot Node(Composition composition, string name) =>
        Nodes(composition.SemanticSnapshot()!).Single(node => node.Name == name);

    private static SemanticSnapshot FocusedTab(Composition composition) =>
        Tabs(composition).Single(node => node.Focused);

    private static SemanticSnapshot SelectedTab(Composition composition) =>
        Tabs(composition).Single(node => node.Selected);

    private static IEnumerable<SemanticSnapshot> Tabs(Composition composition) =>
        Nodes(composition.SemanticSnapshot()!).Where(node => node.Role == SemanticRole.Tab);

    private static IEnumerable<SemanticSnapshot> Nodes(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var nested in Nodes(child))
            yield return nested;
    }

    private sealed class MetricShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) =>
            request.Text.Length == 0
                ? new("empty", 0, 0, [])
                : new(
                    "nav",
                    request.Text.Length,
                    request.FontSize,
                    [
                        new ShapedRun(
                            "nav",
                            "nav",
                            400,
                            5,
                            0,
                            "nav",
                            0,
                            "nav#0",
                            request.Direction,
                            request.Language,
                            request.FontSize,
                            0,
                            request.FontSize,
                            -request.FontSize,
                            0,
                            request.Text.Length,
                            [new(1, 0, 0, 0, request.Text.Length, 0, 0)]
                        ),
                    ]
                );
    }

    private sealed class ImmediatePreparer : IImagePreparer
    {
        public ValueTask<PreparedImage> PrepareAsync(
            ImagePreparationRequest request,
            CancellationToken token
        ) =>
            ValueTask.FromResult<PreparedImage>(new RasterImage(1, 1, new byte[] { 0, 0, 0, 255 }));
    }
}
