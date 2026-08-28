using Lucent.Core;

try
{
    var first = Dump();
    if (first != Dump()) throw new InvalidOperationException("Issue Browser composition is not deterministic.");
    if (!first.Contains("issue-browser.header", StringComparison.Ordinal) || !first.Contains("issue-browser.issue-list", StringComparison.Ordinal) ||
        first.Split('\n').Count(line => line.Contains("issue-browser.issue-row", StringComparison.Ordinal)) != 3 || first.Contains("issue-browser.loading\"", StringComparison.Ordinal) ||
        first.Contains("retained composition", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Issue Browser did not produce only its active static/state-driven structure.");
    var snapshot = Snapshot();
    if (!first.Contains("property name=\"surface\" winner=\"author:token:page-surface:theme\"#0", StringComparison.Ordinal) ||
        !first.Contains("property name=\"opacity\" winner=\"author\"#0[Selected]", StringComparison.Ordinal) ||
        !first.Contains("behavior name=\"issue-row-semantics\" ownership=Action, Semantics", StringComparison.Ordinal) ||
        first.Split('\n').Count(line => line.Contains("semantic element=", StringComparison.Ordinal)) != 6 ||
        first.Contains("Implement retained", StringComparison.Ordinal) ||
        !Flatten(snapshot).Any(node => node.Actions == SemanticAction.Select))
        throw new InvalidOperationException("Issue Browser did not consume typed presentation, behavior, and semantic contracts without leaking issue values.");
    Console.WriteLine("Lucent.IssueBrowser composition contract: PASS");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("Lucent.IssueBrowser composition contract: FAIL: " + exception.Message);
    return 1;
}

static string Dump()
{
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(graph);
    graph.Drain();
    return composition.Dump();
}

static SemanticSnapshot Snapshot()
{
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(graph);
    graph.Drain();
    return composition.SemanticSnapshot()!;
}

static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot snapshot)
{
    yield return snapshot;
    foreach (var child in snapshot.Children)
        foreach (var node in Flatten(child)) yield return node;
}
