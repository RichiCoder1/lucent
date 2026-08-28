using Lucent.Core;
using Lucent.Platform.Windows;

try
{
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(graph);
    graph.Drain();
    return WindowsBootstrap.Run("Lucent Issue Browser — M0 (1.25x test presentation)");
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Lucent M0 startup failed: {exception.Message}");
    return 1;
}

public static class IssueBrowserStructure
{
    public static Composition Create(ReactiveGraph graph)
    {
        var loading = graph.Signal(true, "issue-browser.loading");
        var issues = graph.Signal<Issue[]>([], "issue-browser.issues");
        var composition = new Composition(graph, "issue-browser");
        var header = composition.Child(composition.Root, "issue-browser.header");
        _ = composition.Child(header, "issue-browser.title");
        _ = composition.When(composition.Root, "issue-browser.loading-region", () => loading.Value,
            context => context.Element("issue-browser.loading"));
        _ = composition.ForEach(composition.Root, "issue-browser.issue-list", () => issues.Value,
            issue => issue.Number, (issue, context) => context.Element("issue-browser.issue-row"));

        graph.Batch(() =>
        {
            issues.Value =
            [
                new(29, "Implement retained composition and structural ownership", "open"),
                new(28, "Implement reactive graph and scopes", "closed"),
                new(26, "Native 0.1 Milestone 1", "open")
            ];
            loading.Value = false;
        });
        return composition;
    }

    private sealed record Issue(int Number, string Title, string State);
}
