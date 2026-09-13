using AuthoringGate;
using Lucent.Core;

Verify(AuthoringConsumer.Components.Consumer);
Verify(ProofLibrary.Generated);
Console.WriteLine("Packaged authoring gate: PASS");

static void Verify(Func<Func<string>, ComponentRecipe> build)
{
    var graph = new ReactiveGraph();
    using var composition = new Composition(graph, "packaged-authoring");
    using var theme = new ThemeContext(composition.Root.Scope, new Theme("authoring"));
    var label = composition.Root.Scope.Signal("Before", "label");
    var element = composition.Mount(composition.Root, theme, build(() => label.Value));
    graph.Drain();
    var before = Flatten(composition.SemanticSnapshot()!).Single(node => node.Name == "Before");
    Require(before.Description == "Explicit packaged target", "Author description was lost.");
    using (var scene = SceneLayout.Project(composition, new(320, 200, 1), new NoTextShaper()))
    {
        var box = scene.Boxes.Single(box => box.Identity.ElementId == before.Identity.ElementId);
        Require(box.Bounds.Width == 132, "Author style did not reach the first presentation.");
    }
    label.Value = "After";
    graph.Drain();
    var after = Flatten(composition.SemanticSnapshot()!).Single(node => node.Name == "After");
    Require(
        after.Identity.CompositionEpoch == before.Identity.CompositionEpoch
            && after.Identity.ElementId == before.Identity.ElementId,
        "A live metadata update replaced the retained root."
    );
    Require(
        !composition.IsCurrent(before.Identity),
        "The old semantic generation remained current."
    );
    Require(element.Id == after.Identity.ElementId, "Author conversion introduced an extra root.");
}

static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot node)
{
    yield return node;
    foreach (var child in node.Children)
    foreach (var descendant in Flatten(child))
        yield return descendant;
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class NoTextShaper : ITextShaper
{
    public ShapedText Shape(TextMeasureRequest request) =>
        throw new InvalidOperationException(
            "The authoring target unexpectedly introduced text paint."
        );
}
