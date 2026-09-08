using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ErrorNoticeContracts
{
    [TestMethod]
    public void LiveMessageOptionalRetryInvocationAndDisposalUseOrdinaryComponentContracts()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "error-notice");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var message = composition.Root.Scope.Signal("Could not load issues.", "message");
        var calls = 0;
        var mounted = composition.Mount(
            composition.Root,
            theme,
            Components.ErrorNotice(() => message.Value, () => calls++)
        );
        graph.Drain();

        var initial = Flatten(composition.SemanticSnapshot()!).ToArray();
        var status = initial.Single(node => node.Role == SemanticRole.Status);
        var retry = initial.Single(node => node.Role == SemanticRole.Button);
        Assert.AreEqual("Could not load issues.", status.Name);
        Assert.AreEqual("Retry", retry.Name);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(retry.Identity, new(SemanticCommandKind.Invoke))
        );
        Assert.AreEqual(1, calls);

        message.Value = "The service is still unavailable.";
        graph.Drain();
        Assert.AreEqual(
            "The service is still unavailable.",
            Flatten(composition.SemanticSnapshot()!)
                .Single(node => node.Role == SemanticRole.Status)
                .Name
        );

        mounted.Dispose();
        Assert.AreEqual(
            SemanticCommandResult.Stale,
            composition.ExecuteSemanticCommand(retry.Identity, new(SemanticCommandKind.Invoke))
        );
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void MissingRetryOmitsTheActionWithoutChangingStatusSemantics()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "error-notice-message-only");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.HighContrast);
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.ErrorNotice(() => "Read-only failure", style: Style.Empty.Width(240))
        );
        graph.Drain();

        var semantics = Flatten(composition.SemanticSnapshot()!).ToArray();
        Assert.AreEqual(1, semantics.Count(node => node.Role == SemanticRole.Status));
        Assert.AreEqual(0, semantics.Count(node => node.Role == SemanticRole.Button));
        Assert.AreEqual(
            240f,
            composition.Root.Children.Single().Resolve(LayoutProperties.Width).Value
        );
    }

    private static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot snapshot)
    {
        yield return snapshot;
        foreach (var child in snapshot.Children)
        foreach (var descendant in Flatten(child))
            yield return descendant;
    }
}
