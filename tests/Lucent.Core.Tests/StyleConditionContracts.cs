using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class StyleConditionContracts
{
    [TestMethod]
    public void NestedPredicateShortCircuitsAndReleasesInactiveDependencies()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "short-circuit");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var detail = graph.Signal<string?>(null, "detail");
        var enabled = graph.Signal(true, "enabled");
        var reads = 0;
        composition.Root.Present(
            theme,
            author: Style.Empty.When(
                () => detail.Value is not null,
                Style.Empty.When(
                    () =>
                    {
                        reads++;
                        Assert.IsNotNull(
                            detail.Value,
                            "Inactive parent evaluated its child predicate."
                        );
                        return detail.Value.Length > 0 && enabled.Value;
                    },
                    Style.Empty.Width(42)
                )
            )
        );
        graph.Drain();
        Assert.AreEqual(0, reads);
        detail.Value = "ready";
        graph.Drain();
        Assert.AreEqual(42f, composition.Root.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(1, reads);
        detail.Value = null;
        graph.Drain();
        Assert.IsNull(composition.Root.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(1, reads);
        enabled.Value = false;
        graph.Drain();
        Assert.AreEqual(1, reads, "Inactive predicate retained its leaf dependency.");
        detail.Value = "ready";
        graph.Drain();
        Assert.IsNull(composition.Root.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(2, reads);
    }

    [TestMethod]
    public void ReusedConditionalStyleKeepsIndependentParentAndVariantGates()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "reused-gates");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var first = graph.Signal(false, "first");
        var second = graph.Signal(false, "second");
        var leaf = graph.Signal(true, "leaf");
        var reads = 0;
        var shared = Style.Empty.When(
            () =>
            {
                reads++;
                return leaf.Value;
            },
            Style.Empty.Width(42)
        );
        composition.Root.Present(
            theme,
            author: Style
                .Empty.When(() => first.Value, shared)
                .When(VariantState.Hover, Style.Empty.When(() => second.Value, shared))
        );
        graph.Drain();
        Assert.AreEqual(0, reads);
        second.Value = true;
        graph.Drain();
        Assert.AreEqual(0, reads, "Inactive variant evaluated a nested predicate.");
        composition.Root.SetVariants(VariantState.Hover);
        graph.Drain();
        Assert.AreEqual(1, reads);
        Assert.AreEqual(42f, composition.Root.Resolve(LayoutProperties.Width).Value);
        first.Value = true;
        graph.Drain();
        Assert.AreEqual(2, reads);
        composition.Root.SetVariants(VariantState.None);
        graph.Drain();
        Assert.AreEqual(42f, composition.Root.Resolve(LayoutProperties.Width).Value);
        first.Value = false;
        graph.Drain();
        leaf.Value = false;
        graph.Drain();
        Assert.AreEqual(2, reads);
        Assert.IsNull(composition.Root.Resolve(LayoutProperties.Width).Value);
    }

    [TestMethod]
    public void ReactiveConditionIsSharedAndSuppressesSameBucketBindings()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "conditional-style");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var width = graph.Signal(110f, "width");
        var conditionReads = 0;
        var bindingReads = 0;
        composition.Root.Present(
            theme,
            author: Style.Empty.When(
                () =>
                {
                    conditionReads++;
                    return width.Value >= 100;
                },
                Style
                    .Empty.Bind(
                        LayoutProperties.Width,
                        () =>
                        {
                            bindingReads++;
                            return 20f;
                        }
                    )
                    .Set(LayoutProperties.Height, 30f)
            )
        );
        graph.Drain();

        Assert.AreEqual(20f, composition.Root.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(30f, composition.Root.Resolve(LayoutProperties.Height).Value);
        Assert.AreEqual(1, conditionReads);
        Assert.AreEqual(1, bindingReads);

        width.Value = 120;
        graph.Drain();
        Assert.AreEqual(2, conditionReads);
        Assert.AreEqual(1, bindingReads, "Same-bucket width change republished conditional work.");

        width.Value = 90;
        graph.Drain();
        Assert.IsNull(composition.Root.Resolve(LayoutProperties.Width).Value);
        Assert.IsNull(composition.Root.Resolve(LayoutProperties.Height).Value);
        Assert.AreEqual(1, bindingReads);

        width.Value = 110;
        graph.Drain();
        Assert.AreEqual(20f, composition.Root.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(2, bindingReads);
    }

    [TestMethod]
    public void NestedConditionsAndVariantsComposeByAnd()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "nested-conditional-style");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var outer = graph.Signal(false, "outer");
        var inner = graph.Signal(false, "inner");
        composition.Root.Present(
            theme,
            author: Style.Empty.When(
                () => outer.Value,
                Style.Empty.When(
                    () => inner.Value,
                    Style.Empty.When(
                        VariantState.Hover,
                        Style.Empty.Set(LayoutProperties.Width, 42f)
                    )
                )
            )
        );
        composition.Root.SetVariants(VariantState.Hover);

        Assert.IsNull(composition.Root.Resolve(LayoutProperties.Width).Value);
        outer.Value = true;
        graph.Drain();
        Assert.IsNull(composition.Root.Resolve(LayoutProperties.Width).Value);
        inner.Value = true;
        graph.Drain();
        Assert.AreEqual(42f, composition.Root.Resolve(LayoutProperties.Width).Value);
        composition.Root.SetVariants(VariantState.None);
        Assert.IsNull(composition.Root.Resolve(LayoutProperties.Width).Value);
    }

    [TestMethod]
    public void ConditionCannotMutateReactiveState()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "mutating-condition");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var value = graph.Signal(0, "value");
        composition.Root.Present(
            theme,
            author: Style.Empty.When(
                () =>
                {
                    value.Value++;
                    return true;
                },
                Style.Empty.Width(10)
            )
        );

        var error = Assert.ThrowsExactly<AggregateException>(graph.Drain);
        Assert.IsInstanceOfType<InvalidOperationException>(error.InnerException);
        Assert.AreEqual(0, value.Value);
    }

    [TestMethod]
    public void ConditionCanReadDirtyLazyDerivedState()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "derived-condition");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var width = graph.Signal(90f, "width");
        var wide = graph.Derived(() => width.Value >= 100, "wide");
        composition.Root.Present(
            theme,
            author: Style.Empty.When(() => wide.Value, Style.Empty.Width(10))
        );
        graph.Drain();
        Assert.IsNull(composition.Root.Resolve(LayoutProperties.Width).Value);

        width.Value = 100;
        graph.Drain();
        Assert.AreEqual(10f, composition.Root.Resolve(LayoutProperties.Width).Value);
    }
}
