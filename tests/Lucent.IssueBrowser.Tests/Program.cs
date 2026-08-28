using Lucent.Core;

try
{
    var first = Dump();
    if (first != Dump()) throw new InvalidOperationException("Issue Browser composition is not deterministic.");
    if (!first.Contains("issue-browser.header", StringComparison.Ordinal) || !first.Contains("issue-browser.issue-list", StringComparison.Ordinal) ||
        first.Split('\n').Count(line => line.Contains("issue-browser.issue-row", StringComparison.Ordinal)) != 3 || first.Contains("issue-browser.loading\"", StringComparison.Ordinal) ||
        first.Contains("retained composition", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Issue Browser did not produce only its active static/state-driven structure.");
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
