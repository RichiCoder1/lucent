using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ControlledSelectionTests
{
    [TestMethod]
    public void RejectedAndDelayedRequestsDoNotCommitSelection()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "controlled");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var selected = graph.Signal(false, "selected");
        var requests = 0;
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Column([
                Components.Selectable(() => "Inbox", () => !selected.Value),
                Components.Selectable(() => "Archive", () => selected.Value, () => requests++),
            ])
        );
        graph.Drain();
        var archive = Nodes(composition.SemanticSnapshot()!).Single(node => node.Name == "Archive");
        Assert.AreEqual(
            SemanticCommandResult.Requested,
            composition.ExecuteSemanticCommand(archive.Identity, new(SemanticCommandKind.Select))
        );
        graph.Drain();
        Assert.AreEqual(1, requests);
        Assert.IsFalse(
            Nodes(composition.SemanticSnapshot()!).Single(node => node.Name == "Archive").Selected
        );
        Assert.IsTrue(
            Nodes(composition.SemanticSnapshot()!).Single(node => node.Name == "Inbox").Selected
        );
        selected.Value = true;
        graph.Drain();
        Assert.IsTrue(
            Nodes(composition.SemanticSnapshot()!).Single(node => node.Name == "Archive").Selected
        );
        Assert.IsFalse(
            Nodes(composition.SemanticSnapshot()!).Single(node => node.Name == "Inbox").Selected
        );
        archive = Nodes(composition.SemanticSnapshot()!).Single(node => node.Name == "Archive");
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(archive.Identity, new(SemanticCommandKind.Select))
        );
    }

    [TestMethod]
    public void SynchronouslyAcceptedSelectionReportsApplied()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "accepted-selection");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var selected = graph.Signal(false, "selected");
        composition.Mount(
            composition.Root,
            theme,
            Components.Selectable(() => "Item", () => selected.Value, () => selected.Value = true)
        );
        graph.Drain();
        var item = Nodes(composition.SemanticSnapshot()!).Single(node => node.Name == "Item");
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(item.Identity, new(SemanticCommandKind.Select))
        );
        Assert.IsTrue(
            Nodes(composition.SemanticSnapshot()!).Single(node => node.Name == "Item").Selected
        );
    }

    private static IEnumerable<SemanticSnapshot> Nodes(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var nested in Nodes(child))
            yield return nested;
    }
}
