using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class AriaAuthoringContracts
{
    private static readonly ChoiceItem<int>[] SingleChoice = [new(1, "One")];
    private static readonly string[] SingleText = ["One"];

    [TestMethod]
    public void OptionalMetadataRevealsCurrentBaseAndPreservesBehaviorState()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "author-aria-base");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var baseName = composition.Root.Scope.Signal("Base one", "author-aria-base-name");
        var metadata = composition.Root.Scope.Signal<AriaMetadata?>(
            new("Author name", "Author description"),
            "author-aria-metadata"
        );
        var relationships = new SemanticRelationships(helpText: "Behavior help");
        var reads = 0;
        var target = AuthorRecipe.Target<StyledAccessibleCapability>(
            (context, root, values) =>
            {
                root.RegisterRecipeAuthoring(values);
                root.AttachBehaviors(
                    new BoundSemanticBehavior(() =>
                        SemanticDeclaration
                            .Create(SemanticRole.TextField, baseName.Value)
                            .Enabled(false)
                            .Focused(true)
                            .Relationships(relationships)
                            .Description("Behavior description")
                            .ConfidentialEditing(false)
                            .Range(
                                new SemanticRangeSnapshot(2, 0, 10, 1, 2, isReadOnly: true),
                                false
                            )
                            .SelectionContainer(new SemanticSelectionSnapshot(false, true))
                            .SelectionItem(true, false)
                            .Build()
                    )
                );
                root.Present(context.Theme, author: values.Style);
            }
        );
        var recipe = AuthorRecipe
            .Create("author-aria-base-target", target)
            .Aria.Metadata(() =>
            {
                reads++;
                return metadata.Value;
            })
            .End;

        var mounted = composition.Mount(composition.Root, theme, recipe.Recipe);
        graph.Drain();

        var authored = Find(composition, mounted.Id);
        Assert.AreEqual("Author name", authored.Name);
        Assert.AreEqual("Author description", authored.Description);
        Assert.AreEqual(SemanticRole.TextField, authored.Role);
        Assert.IsFalse(authored.Enabled);
        Assert.IsTrue(authored.Focused);
        Assert.IsTrue(authored.Selected);
        Assert.IsTrue(authored.IsPassword);
        Assert.AreSame(relationships, authored.Relationships);
        Assert.AreEqual(2d, authored.Range!.Value);
        Assert.IsTrue(authored.Range.IsReadOnly);
        Assert.IsTrue(authored.Selection!.IsSelectionRequired);

        baseName.Value = "Base two";
        graph.Drain();
        Assert.AreEqual("Author name", Find(composition, mounted.Id).Name);

        metadata.Value = null;
        graph.Drain();
        var restored = Find(composition, mounted.Id);
        Assert.AreEqual("Base two", restored.Name);
        Assert.AreEqual("Behavior description", restored.Description);
        Assert.AreSame(relationships, restored.Relationships);
        Assert.IsTrue(restored.IsPassword);

        var readsBeforeDispose = reads;
        mounted.Dispose();
        metadata.Value = new("After disposal");
        graph.Drain();
        Assert.AreEqual(readsBeforeDispose, reads);
    }

    [TestMethod]
    public void OrderedMetadataUsesTheLastActiveWriterForEachField()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "author-aria-order");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var firstName = composition.Root.Scope.Signal("First", "author-aria-first-name");
        var grouped = composition.Root.Scope.Signal<AriaMetadata?>(
            new("Grouped", "Grouped description"),
            "author-aria-grouped"
        );
        var recipe = Components
            .Button("Behavior")
            .Aria.Name(() => firstName.Value)
            .Metadata(() => grouped.Value)
            .Description("Last description")
            .End;

        var mounted = composition.Mount(composition.Root, theme, recipe.Recipe);
        graph.Drain();
        var semantic = Find(composition, mounted.Id);
        Assert.AreEqual("Grouped", semantic.Name);
        Assert.AreEqual("Last description", semantic.Description);

        firstName.Value = "Latest first";
        grouped.Value = null;
        graph.Drain();
        semantic = Find(composition, mounted.Id);
        Assert.AreEqual("Latest first", semantic.Name);
        Assert.AreEqual("Last description", semantic.Description);
        Assert.AreEqual(SemanticAction.Invoke, semantic.Actions);
    }

    [TestMethod]
    public void ButtonAriaParameterUsesThePersistentMetadataPath()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "button-aria-parameter");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var metadata = composition.Root.Scope.Signal<AriaMetadata?>(
            new("Explicit button", "Explicit description"),
            "button-aria-parameter-value"
        );

        var mounted = composition.Mount(
            composition.Root,
            theme,
            Components.Button("Behavior button", aria: () => metadata.Value)
        );
        graph.Drain();
        Assert.AreEqual("Explicit button", Find(composition, mounted.Id).Name);

        metadata.Value = null;
        graph.Drain();
        var semantic = Find(composition, mounted.Id);
        Assert.AreEqual("Behavior button", semantic.Name);
        Assert.AreEqual(SemanticRole.Button, semantic.Role);
        Assert.AreEqual(SemanticAction.Invoke, semantic.Actions);
    }

    [TestMethod]
    public void LiveWhitespaceFailsAtUpdate()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "author-aria-guards");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var name = composition.Root.Scope.Signal("Valid", "author-aria-guard-name");
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Button("Behavior").Aria.Name(() => name.Value).End.Recipe
        );
        graph.Drain();

        name.Value = "  ";
        var updateError = Assert.ThrowsExactly<AggregateException>(graph.Drain);
        Assert.IsInstanceOfType<ArgumentException>(updateError.InnerExceptions.Single());
    }

    [TestMethod]
    public void StyleOnlyRegistrationRejectsAria()
    {
        using var composition = new Composition(new ReactiveGraph(), "author-aria-style-guard");
        Assert.ThrowsExactly<ArgumentException>(() => new AriaMetadata(" "));
        Assert.ThrowsExactly<ArgumentException>(() => new AriaMetadata(description: "\t"));
        var styled = composition.Child(composition.Root, "style-only-aria");
        var values = new AuthorRecipeValues(
            null,
            null,
            null,
            null,
            null,
            () => new AriaMetadata("Unsupported")
        );
        Assert.ThrowsExactly<InvalidOperationException>(() => styled.RegisterRecipeStyle(values));
    }

    [TestMethod]
    public void DescendantSemanticControlsExposeOnlyStyleAuthoringAtTheirRoots()
    {
        AuthorRecipe<StyledCapability> slider = Components.Slider(
            "Volume",
            () => 0.5,
            _ => { },
            new SliderOptions(0, 1, 0.1)
        );
        AuthorRecipe<StyledCapability> listBox = Components.ListBox(
            "Choices",
            () => SingleChoice,
            () => 1,
            _ => { }
        );
        AuthorRecipe<StyledCapability> virtualizedList = Components.VirtualizedList(
            () => SingleText,
            value => value,
            value => Components.Text(value.Value),
            () => 36f
        );

        using var composition = new Composition(new ReactiveGraph(), "author-aria-descendants");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var sliderRoot = composition.Mount(composition.Root, theme, slider.Recipe);
        var listRoot = composition.Mount(composition.Root, theme, listBox.Recipe);
        var virtualizedRoot = composition.Mount(composition.Root, theme, virtualizedList.Recipe);
        var semantics = Descendants(composition.SemanticSnapshot()!).ToArray();

        Assert.AreNotEqual(
            sliderRoot.Id,
            semantics.Single(node => node.Role == SemanticRole.Slider).Identity.ElementId
        );
        Assert.AreNotEqual(
            listRoot.Id,
            semantics
                .Single(node => node.Role == SemanticRole.List && node.Name == "Choices")
                .Identity.ElementId
        );
        Assert.AreNotEqual(
            virtualizedRoot.Id,
            semantics
                .Single(node => node.Role == SemanticRole.List && node.Name == "Items")
                .Identity.ElementId
        );
    }

    private static SemanticSnapshot Find(Composition composition, long elementId) =>
        Descendants(composition.SemanticSnapshot()!)
            .Single(node => node.Identity.ElementId == elementId);

    private static IEnumerable<SemanticSnapshot> Descendants(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Descendants(child))
            yield return descendant;
    }

    private sealed class BoundSemanticBehavior(Func<SemanticDeclaration> read) : Behavior
    {
        public override string Name => "author-aria-bound-semantics";

        public override BehaviorOwnership Ownership => BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context) => context.BindSemantics(read);
    }
}
