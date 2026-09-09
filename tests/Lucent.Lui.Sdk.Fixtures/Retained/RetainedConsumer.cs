using Lucent.Core;

namespace Consumer;

internal sealed record Item(int Id, string Title);

internal static class RetainedConsumer
{
    [LucentComponent]
    internal static ComponentRecipe SnapshotLabel(string label) =>
        Lucent.Core.Components.Text(() => "static:" + label);

    public static void Run()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "sdk-retained");
        using var theme = new ThemeContext(composition.Root.Scope, new Theme("sdk-retained"));
        var items = composition.Root.Scope.Signal<Item[]>([new(1, "before")], "items");
        var selected = composition.Root.Scope.Signal<Item?>(new(1, "before"), "selected");
        composition.Mount(composition.Root, theme, Components.Retained(items, selected));
        graph.Drain();
        var before = Flatten(composition.SemanticSnapshot()!).ToArray();
        var rowId = before.Single(node => node.Name == "live:before").Identity.ElementId;
        var implicitLiveId = before
            .Single(node => node.Name == "implicit-live:before")
            .Identity.ElementId;
        var snapshotId = before.Single(node => node.Name == "static:before").Identity.ElementId;
        var detailId = before.Single(node => node.Name == "detail:before").Identity.ElementId;
        items.Value = [new(1, "after")];
        selected.Value = new(1, "after");
        graph.Drain();
        var after = Flatten(composition.SemanticSnapshot()!).ToArray();
        Require(
            after.Single(node => node.Name == "live:after").Identity.ElementId == rowId,
            "Same-key replacement lost row identity or retained stale text."
        );
        Require(
            after.Single(node => node.Name == "implicit-live:after").Identity.ElementId
                == implicitLiveId,
            "A plain expression supplied to a live input did not update in place."
        );
        Require(
            after.Single(node => node.Name == "detail:after").Identity.ElementId == detailId,
            "Same-branch replacement lost identity or retained stale pattern data."
        );
        Require(
            after.Single(node => node.Name == "static:before").Identity.ElementId == snapshotId,
            "A plain component parameter did not retain its construction-time value."
        );
        items.Value = [];
        selected.Value = null;
        graph.Drain();
        Require(
            !Flatten(composition.SemanticSnapshot()!)
                .Any(node =>
                    node.Identity.ElementId == rowId
                    || node.Identity.ElementId == implicitLiveId
                    || node.Identity.ElementId == snapshotId
                    || node.Identity.ElementId == detailId
                ),
            "Removing retained content left its semantics mounted."
        );
        items.Value = [new(1, "reentered")];
        selected.Value = new(1, "reentered");
        graph.Drain();
        var reentered = Flatten(composition.SemanticSnapshot()!).ToArray();
        Require(
            reentered.Single(node => node.Name == "live:reentered").Identity.ElementId != rowId,
            "Reentry reused a disposed row."
        );
        Require(
            reentered.Single(node => node.Name == "detail:reentered").Identity.ElementId
                != detailId,
            "Reentry reused a disposed conditional branch."
        );
        Console.WriteLine("retained payload SDK proof: PASS");
    }

    private static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot? node)
    {
        if (node is null)
            yield break;
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Flatten(child))
            yield return descendant;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
