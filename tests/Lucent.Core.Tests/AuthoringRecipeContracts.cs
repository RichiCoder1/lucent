using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class AuthoringRecipeContracts
{
    [TestMethod]
    public void ExplicitTargetAppliesStyleAndSemanticsToOneRetainedRoot()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "authoring-target");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var recipe = AuthoringRecipeProof.Create(
            "Authored root",
            "Authored description",
            Style.Empty.Width(124).Height(24)
        );

        var mounted = composition.Mount(composition.Root, theme, recipe.Recipe);
        graph.Drain();

        var semantic = Descendants(composition.SemanticSnapshot()!)
            .Single(node => node.Identity.ElementId == mounted.Id);
        Assert.AreEqual(SemanticRole.Group, semantic.Role);
        Assert.AreEqual("Authored root", semantic.Name);
        Assert.AreEqual("Authored description", semantic.Description);
        Assert.AreEqual(124f, mounted.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(24f, mounted.Resolve(LayoutProperties.Height).Value);
        Assert.AreEqual(1, composition.Root.Children.Count);
        Assert.AreEqual(0, mounted.Children.Count);
    }

    [TestMethod]
    public void SameAssemblyCoreAuthoringFactoryPresentsItsTargetedRoot()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "authoring-core-proof");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var label = composition.Root.Scope.Signal("Core proof", "authoring-core-proof-label");

        var mounted = composition.Mount(
            composition.Root,
            theme,
            Components.AuthoringRecipeLuiProof(() => label.Value)
        );
        graph.Drain();

        var semantic = Descendants(composition.SemanticSnapshot()!)
            .Single(node => node.Identity.ElementId == mounted.Id);
        Assert.AreEqual("Core proof", semantic.Name);
        Assert.AreEqual("Core authoring proof", semantic.Description);
        Assert.AreEqual(88f, mounted.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(24f, mounted.Resolve(LayoutProperties.Height).Value);
        Assert.AreEqual(0, mounted.Children.Count);

        label.Value = "Updated proof";
        graph.Drain();
        semantic = Descendants(composition.SemanticSnapshot()!)
            .Single(node => node.Identity.ElementId == mounted.Id);
        Assert.AreEqual("Updated proof", semantic.Name);
    }

    [TestMethod]
    public void TargetRunsBeforeFirstPresentationWithoutAddingAWrapper()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "authoring-order");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var order = new List<string>();
        var target = AuthorRecipe.Target<StyledCapability>(
            (context, root, values) =>
            {
                order.Add("target");
                root.Present(context.Theme, author: values.Style);
                order.Add("present");
            }
        );
        var recipe = AuthorRecipe
            .Create("authoring-order-root", target)
            .Style(Style.Empty.Width(48));

        var mounted = composition.Mount(composition.Root, theme, recipe.Recipe);

        var expectedOrder = new[] { "target", "present" };
        CollectionAssert.AreEqual(expectedOrder, order);
        Assert.AreEqual(48f, mounted.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(1, composition.Root.Children.Count);
    }

    [TestMethod]
    public void DeferredAuthorRecipeRetainsTargetAndOneRootPerMount()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "authoring-deferred");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var setups = 0;
        var applications = 0;
        var target = AuthorRecipe.Target<StyledCapability>(
            (context, root, values) =>
            {
                applications++;
                root.Present(context.Theme, author: values.Style);
            }
        );
        var recipe = AuthorRecipe
            .Defer(
                "authoring-deferred-root",
                target,
                _ =>
                {
                    setups++;
                    return AuthorRecipe
                        .Create("authored-root", target)
                        .Style(Style.Empty.Width(36));
                }
            )
            .Named("named-deferred-root");

        var first = composition.Mount(composition.Root, theme, recipe.Recipe);
        var second = composition.Mount(composition.Root, theme, recipe.Recipe);

        Assert.AreEqual(2, setups);
        Assert.AreEqual(2, applications);
        Assert.AreNotSame(first, second);
        Assert.AreEqual("named-deferred-root", first.Name);
        Assert.AreEqual("named-deferred-root", second.Name);
        Assert.AreEqual(36f, first.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(36f, second.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(2, composition.Root.Children.Count);
    }

    [TestMethod]
    public void DeferredLayersRetainIntermediateAuthoringBeforeTheOuterLayer()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "authoring-deferred-layers");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var applications = 0;
        AuthorRecipeValues applied = default;
        var target = AuthorRecipe.Target<StyledAccessibleCapability>(
            (context, root, values) =>
            {
                applications++;
                applied = values;
                root.Present(context.Theme, author: values.Style);
            }
        );
        var leaf = AuthorRecipe
            .Create("authoring-leaf", target)
            .Style(Style.Empty.Width(17))
            .Aria.Name("leaf")
            .End;
        var middle = AuthorRecipe
            .Defer("authoring-middle", target, _ => leaf)
            .Style(Style.Empty.Height(29))
            .Aria.Description("middle description")
            .End;
        var outer = AuthorRecipe
            .Defer("authoring-outer", target, _ => middle)
            .Style(Style.Empty.Width(43))
            .Aria.Name("outer")
            .End;

        var mounted = composition.Mount(composition.Root, theme, outer.Recipe);

        Assert.AreEqual(1, applications);
        Assert.AreEqual("outer", applied.Name);
        Assert.AreEqual("middle description", applied.Description);
        Assert.AreEqual(43f, mounted.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(29f, mounted.Resolve(LayoutProperties.Height).Value);
        Assert.AreEqual(1, composition.Root.Children.Count);
    }

    [TestMethod]
    public void LiveAriaReaderUpdatesTheSameRetainedSemanticRoot()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "authoring-live-reader");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var name = composition.Root.Scope.Signal("first name", "authoring-live-reader-name");
        var recipe = AuthoringRecipeProof.Create(() => name.Value, "live description");

        var mounted = composition.Mount(composition.Root, theme, recipe.Recipe);
        graph.Drain();
        var first = Descendants(composition.SemanticSnapshot()!)
            .Single(node => node.Identity.ElementId == mounted.Id);
        Assert.AreEqual("first name", first.Name);

        name.Value = "second name";
        graph.Drain();

        var second = Descendants(composition.SemanticSnapshot()!)
            .Single(node => node.Identity.ElementId == mounted.Id);
        Assert.AreEqual("second name", second.Name);
        Assert.AreSame(mounted, composition.Root.Children.Single());
    }

    [TestMethod]
    public void ContentConversionMountsTheAuthoredRootWithoutSyntheticStructure()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "authoring-content");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var target = AuthorRecipe.Target<StyledCapability>(
            (context, root, values) => root.Present(context.Theme, author: values.Style)
        );
        var authored = AuthorRecipe
            .Create("content-authored-root", target)
            .Style(Style.Empty.Width(52))
            .Named("named-content-root");
        var content = (ContentRecipe)authored;
        var host = ComponentRecipe.Create(
            "content-host",
            (context, root) => context.Mount(root, ComponentContent.Create(new[] { content }))
        );

        var mountedHost = composition.Mount(composition.Root, theme, host);

        Assert.AreEqual(1, mountedHost.Children.Count);
        Assert.AreEqual("named-content-root", mountedHost.Children[0].Name);
        Assert.AreEqual(52f, mountedHost.Children[0].Resolve(LayoutProperties.Width).Value);
    }

    [TestMethod]
    public void KeyedFactoryAcceptsTypedAuthorRecipeThroughAnExplicitLambda()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "authoring-keyed");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var target = AuthorRecipe.Target<StyledCapability>(
            (context, root, values) => root.Present(context.Theme, author: values.Style)
        );
        var initialItems = new[] { 2, 4 };
        var items = composition.Root.Scope.Signal(initialItems, "authoring-keyed-items");

        AuthorRecipe<StyledCapability> BuildItem(CurrentItem<int> item) =>
            AuthorRecipe
                .Create("authoring-keyed-item", target)
                .Style(Style.Empty.Width(item.Value * 10))
                .Named("item-" + item.Value);

        var content = ContentRecipe.ForEach(
            "authoring-keyed-items",
            () => items.Value,
            static value => value,
            item => BuildItem(item)
        );
        var host = ComponentRecipe.Create(
            "authoring-keyed-host",
            (context, root) => context.Mount(root, ComponentContent.Create([content]))
        );

        var mountedHost = composition.Mount(composition.Root, theme, host);
        graph.Drain();
        var keyed = mountedHost.Children.Single();

        Assert.AreEqual(1, mountedHost.Children.Count);
        Assert.AreEqual("authoring-keyed-items", keyed.Name);
        Assert.AreEqual(2, keyed.Children.Count);
        Assert.AreEqual("item-2", keyed.Children[0].Name);
        Assert.AreEqual("item-4", keyed.Children[1].Name);
        Assert.AreEqual(20f, keyed.Children[0].Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(40f, keyed.Children[1].Resolve(LayoutProperties.Width).Value);
        var second = keyed.Children[1];
        items.Value = [4, 2, 6];
        graph.Drain();
        Assert.AreSame(second, keyed.Children[0]);
        Assert.AreEqual(3, keyed.Children.Count);
        Assert.AreEqual(60f, keyed.Children[2].Resolve(LayoutProperties.Width).Value);
    }

    [TestMethod]
    public void AriaFixedAndReaderReplacementNeverResurrectsThePreviousForm()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "authoring-aria-replacement");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var liveName = "live name";
        var liveDescription = "live description";
        Func<string> nameReader = () => liveName;
        Func<string> descriptionReader = () => liveDescription;
        var values = new List<AuthorRecipeValues>();
        var target = AuthorRecipe.Target<AccessibleCapability>(
            (context, root, authored) =>
            {
                values.Add(authored);
                root.Present(context.Theme);
            }
        );
        var reader = AuthorRecipe
            .Create("aria-replacement-root", target)
            .Aria.Name(nameReader)
            .Description(descriptionReader)
            .End;
        var fixedValues = reader.Aria.Name("fixed name").Description("fixed description").End;

        _ = composition.Mount(composition.Root, theme, fixedValues.Recipe);

        var fixedSnapshot = values[0];
        Assert.AreEqual("fixed name", fixedSnapshot.Name);
        Assert.IsNull(fixedSnapshot.NameReader);
        Assert.AreEqual("fixed description", fixedSnapshot.Description);
        Assert.IsNull(fixedSnapshot.DescriptionReader);

        var liveValues = fixedValues.Aria.Name(nameReader).Description(descriptionReader).End;
        _ = composition.Mount(composition.Root, theme, liveValues.Recipe);

        var live = values[1];
        Assert.IsNull(live.Name);
        Assert.AreSame(nameReader, live.NameReader);
        Assert.AreEqual("live name", live.NameReader!());
        Assert.IsNull(live.Description);
        Assert.AreSame(descriptionReader, live.DescriptionReader);
        Assert.AreEqual("live description", live.DescriptionReader!());
    }

    [TestMethod]
    public void DefaultAndConflictingAuthorTargetsFailClosed()
    {
        var invalid = default(AuthorRecipe<StyledCapability>);
        Assert.IsFalse(invalid.IsValid);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = invalid.Recipe);
        Assert.ThrowsExactly<ArgumentException>(() =>
            AuthorRecipe.Target<AuthorCapability>((_, _, _) => { })
        );

        var firstTarget = AuthorRecipe.Target<StyledCapability>(
            (context, root, _) => root.Present(context.Theme)
        );
        var secondTarget = AuthorRecipe.Target<StyledCapability>(
            (context, root, _) => root.Present(context.Theme)
        );
        var recipe = AuthorRecipe.Create("target-conflict-root", firstTarget).Recipe;

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            recipe.WithAuthoringTarget(secondTarget)
        );
    }

    private static IEnumerable<SemanticSnapshot> Descendants(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Descendants(child))
            yield return descendant;
    }
}
