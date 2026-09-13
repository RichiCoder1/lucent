using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class StockAuthoringContracts
{
    [TestMethod]
    public void StockStyleChainsReachTheOriginalRootBeforePresentation()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "stock-authoring");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var button = Components
            .Button("Save", style: Style.Empty.Width(40))
            .Style(Style.Empty.Width(120))
            .Named("save");
        ComponentContent content = [button, Components.Text("Details")];
        var row = composition.Mount(
            composition.Root,
            theme,
            Components.Row(content).Style(Style.Empty.Height(52)).Named("toolbar")
        );
        graph.Drain();

        Assert.AreEqual(1, composition.Root.Children.Count);
        Assert.AreEqual(2, row.Children.Count);
        Assert.AreEqual(52f, row.Resolve(LayoutProperties.Height).Value);
        var mounted = row.Children[0];
        Assert.AreEqual("save", mounted.Name);
        Assert.AreEqual(0, mounted.Children.Count);
        Assert.AreEqual(120f, mounted.Resolve(LayoutProperties.Width).Value);
        var semantic = Descendants(composition.SemanticSnapshot()!)
            .Single(node => node.Identity.ElementId == mounted.Id);
        Assert.AreEqual(SemanticRole.Button, semantic.Role);
        Assert.AreEqual(SemanticAction.Invoke, semantic.Actions);
    }

    [TestMethod]
    public void DeferredStockRecipeKeepsStyleLiveAndOwnsOneRootPerMount()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "stock-deferred");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var width = composition.Root.Scope.Signal(80f, "width");
        var setups = 0;
        var recipe = Component.Define(
            "deferred-button",
            _ =>
            {
                setups++;
                return Components
                    .Button("Go")
                    .Style(Style.Empty.Bind(LayoutProperties.Width, () => width.Value));
            }
        );
        var first = composition.Mount(composition.Root, theme, recipe);
        var second = composition.Mount(composition.Root, theme, recipe);
        graph.Drain();
        Assert.AreEqual(2, setups);
        Assert.AreEqual(2, composition.Root.Children.Count);
        Assert.AreEqual(0, first.Children.Count);
        Assert.AreEqual(80f, first.Resolve(LayoutProperties.Width).Value);
        width.Value = 96;
        graph.Drain();
        Assert.AreEqual(96f, first.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(96f, second.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(2, setups);
    }

    [TestMethod]
    public void SameRootStockCompositionRetainsOuterStylePrecedence()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "stock-composed");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var progress = composition.Mount(
            composition.Root,
            theme,
            Components.ProgressBar("Loading", () => 0.5).Style(Style.Empty.Width(177))
        );
        graph.Drain();
        Assert.AreEqual(177f, progress.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(
            SemanticRole.ProgressBar,
            Descendants(composition.SemanticSnapshot()!)
                .Single(node => node.Identity.ElementId == progress.Id)
                .Role
        );
        Assert.AreEqual(1, composition.Root.Children.Count);
    }

    private static IEnumerable<SemanticSnapshot> Descendants(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Descendants(child))
            yield return descendant;
    }
}
