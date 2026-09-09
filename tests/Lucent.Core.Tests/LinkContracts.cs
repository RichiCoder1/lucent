using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class LinkContracts
{
    [TestMethod]
    public void RenderingLinkHasNoSideEffectAndExposesOneInvocation()
    {
        using var composition = new Composition(new ReactiveGraph(), "link-contract");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var invocations = 0;
        composition.Mount(
            composition.Root,
            theme,
            Components.Link("Read the guide", () => invocations++)
        );
        composition.Flush();
        Assert.AreEqual(0, invocations);
        var nodes = Nodes(composition.SemanticSnapshot()!).ToArray();
        var link = nodes.Single(node => node.Actions.HasFlag(SemanticAction.Invoke));
        Assert.AreEqual(SemanticRole.Hyperlink, link.Role);
        Assert.AreEqual("Read the guide", link.Name);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(link.Identity, new(SemanticCommandKind.Invoke))
        );
        Assert.AreEqual(1, invocations);
    }

    [TestMethod]
    public void UriPolicyCopiesSchemesAndChecksApplicationDecision()
    {
        var schemes = new List<string> { "https" };
        var policy = new UriLaunchPolicy(schemes, uri => uri.Host == "example.test");
        schemes.Add("file");
        Assert.IsTrue(policy.Allows(new("https://example.test/guide")));
        Assert.IsFalse(policy.Allows(new("https://other.test/guide")));
        Assert.IsFalse(policy.Allows(new("file:///C:/notes.txt")));
        Assert.IsFalse(policy.Allows(new("guide", UriKind.Relative)));
    }

    private static IEnumerable<SemanticSnapshot> Nodes(SemanticSnapshot root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var node in Nodes(child))
            yield return node;
    }
}
