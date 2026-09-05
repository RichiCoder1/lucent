using Lucent.Core;

namespace Consumer;

internal static class ContentConsumer
{
    public static void Run()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "sdk-content");
        using var theme = new ThemeContext(composition.Root.Scope, new Theme("sdk-content"));
        var component = Components.Marker("component");
        ContentRecipe contribution = Components.Marker("contribution");
        ComponentContent children =
        [
            Components.Marker("children-first"),
            Components.Marker("children-second"),
        ];

        var mounted = composition.Mount(
            composition.Root,
            theme,
            Components.ContentProof(component, contribution, children)
        );
        graph.Drain();

        var names = Flatten(composition.SemanticSnapshot()!)
            .Where(node => node.Role == SemanticRole.Text)
            .Select(node => node.Name)
            .ToArray();
        string[] expected =
        [
            "header",
            "component",
            "contribution",
            "children-first",
            "children-second",
            "footer",
            "header",
            "footer",
            "header",
            "nested-before",
            "header",
            "nested-inner",
            "footer",
            "nested-after",
            "footer",
        ];
        Require(
            names.SequenceEqual(expected),
            "Forwarded content lost declaration order, empty content, or nested content: "
                + string.Join(", ", names)
        );

        Require(mounted.Children.Count == 8, "Forwarded content changed the parent element count.");
        var emptyWrapper = mounted.Children[6];
        var nestedWrapper = mounted.Children[7];
        Require(
            nestedWrapper.Children.Count == 5,
            "Nested forwarded content changed the wrapper element count."
        );
        var innerWrapper = nestedWrapper.Children[2];
        Require(
            mounted.Children.Take(6).All(child => child.Children.Count == 0)
                && emptyWrapper.Children.Count == 2
                && emptyWrapper.Children.All(child => child.Children.Count == 0)
                && innerWrapper.Children.Count == 3
                && innerWrapper.Children.All(child => child.Children.Count == 0)
                && CountElements(mounted) == 19,
            "Forwarded content introduced a synthetic element or changed wrapper structure."
        );
        Console.WriteLine("content forwarding SDK proof: PASS");
    }

    private static int CountElements(Element element) => 1 + element.Children.Sum(CountElements);

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
