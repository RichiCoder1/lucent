using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class StyleValueContracts
{
    private static readonly Property<int> Value = new("value", 0);
    private static readonly Token<int> First = new("first", 10);
    private static readonly Token<int> Second = new("second", 10);

    [TestMethod]
    public void SelectionTracksOnlyTheCurrentTokenAndPreservesProvenance()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "token-selection");
        var root = composition.Root;
        var theme = new ThemeContext(root.Scope, new Theme("test"));
        var choice = root.Scope.Signal(0, "choice");
        var reads = 0;
        root.Present(
            theme,
            author: Style.Empty.BindValue<int>(
                Value,
                () =>
                {
                    reads++;
                    return choice.Value switch
                    {
                        0 => First,
                        1 => Second,
                        _ => 42,
                    };
                }
            )
        );
        graph.Drain();
        Assert.AreEqual(10, root.Resolve(Value).Value);
        Assert.AreEqual("author:token:first:fallback", root.Resolve(Value).Winner.Source);
        Assert.AreEqual(1, reads);

        theme.Theme = theme.Theme.Set(Second, 20);
        graph.Drain();
        Assert.AreEqual(
            1,
            reads,
            "A different token must not invalidate the selected token, even on its first read."
        );
        theme.Theme = theme.Theme.Set(First, 10);
        graph.Drain();
        Assert.AreEqual("author:token:first:theme", root.Resolve(Value).Winner.Source);
        theme.Theme = theme.Theme.Set(Second, 10);
        graph.Drain();
        choice.Value = 1;
        graph.Drain();
        Assert.AreEqual(10, root.Resolve(Value).Value);
        Assert.AreEqual("author:token:second:theme", root.Resolve(Value).Winner.Source);
        var before = reads;
        theme.Theme = theme.Theme.Set(First, 99);
        graph.Drain();
        Assert.AreEqual(
            before,
            reads,
            "The previous token must be detached after selection changes."
        );
        theme.Theme = theme.Theme.Set(Second, 21);
        graph.Drain();
        Assert.AreEqual(21, root.Resolve(Value).Value);

        choice.Value = 2;
        graph.Drain();
        Assert.AreEqual(42, root.Resolve(Value).Value);
        Assert.AreEqual("author", root.Resolve(Value).Winner.Source);
        before = reads;
        theme.Theme = theme.Theme.Set(Second, 22);
        graph.Drain();
        Assert.AreEqual(before, reads, "Concrete values must not retain a token dependency.");
    }

    [TestMethod]
    public void InactiveAndDisposedBindingsReleaseTheirDependencies()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "token-lifetime");
        var root = composition.Root;
        var theme = new ThemeContext(root.Scope, new Theme("test"));
        var choice = root.Scope.Signal(false, "choice");
        var reads = 0;
        var child = composition.Child(root, "child");
        child.Present(
            theme,
            author: Style
                .Empty.Set(Value, 5)
                .When(
                    VariantState.Hover,
                    Style.Empty.BindValue<int>(
                        Value,
                        () =>
                        {
                            reads++;
                            return choice.Value ? First : Second;
                        }
                    )
                )
        );
        graph.Drain();
        Assert.AreEqual(0, reads);
        Assert.AreEqual(5, child.Resolve(Value).Value);
        child.SetVariants(VariantState.Hover);
        graph.Drain();
        Assert.AreEqual(1, reads);
        child.SetVariants(VariantState.None);
        graph.Drain();
        choice.Value = true;
        theme.Theme = theme.Theme.Set(Second, 40).Set(First, 30);
        graph.Drain();
        Assert.AreEqual(1, reads);
        Assert.AreEqual(5, child.Resolve(Value).Value);
        child.SetVariants(VariantState.Hover);
        graph.Drain();
        Assert.AreEqual(30, child.Resolve(Value).Value);
        Assert.AreEqual(VariantState.Hover, child.Resolve(Value).Winner.Condition);
        Assert.AreEqual(2, reads);
        child.Dispose();
        choice.Value = false;
        theme.Theme = theme.Theme.Set(First, 31);
        graph.Drain();
        Assert.AreEqual(2, reads);
    }

    [TestMethod]
    public void ReusedStylesResolveAgainstEachMountTheme()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "token-mounts");
        var root = composition.Root;
        var leftTheme = new ThemeContext(root.Scope, new Theme("left").Set(First, 11));
        var rightTheme = new ThemeContext(root.Scope, new Theme("right").Set(First, 12));
        var style = Style.Empty.BindValue<int>(Value, () => First);
        var left = composition.Child(root, "left");
        var right = composition.Child(root, "right");
        left.Present(leftTheme, author: style);
        right.Present(rightTheme, author: style);
        graph.Drain();
        Assert.AreEqual(11, left.Resolve(Value).Value);
        Assert.AreEqual(12, right.Resolve(Value).Value);
        leftTheme.Theme = leftTheme.Theme.Set(First, 21);
        graph.Drain();
        Assert.AreEqual(21, left.Resolve(Value).Value);
        Assert.AreEqual(12, right.Resolve(Value).Value);
        left.Dispose();
        rightTheme.Theme = rightTheme.Theme.Set(First, 22);
        graph.Drain();
        Assert.AreEqual(22, right.Resolve(Value).Value);
    }

    [TestMethod]
    public void FailedSelectionsKeepTheLastSuccessfulValueAndTokenIdentity()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "token-failure");
        var root = composition.Root;
        var fail = root.Scope.Signal(true, "fail");
        var theme = new ThemeContext(root.Scope, new Theme("test"));
        root.Present(
            theme,
            author: Style
                .Empty.Set(Value, 5)
                .BindValue<int>(
                    Value,
                    () => fail.Value ? throw new InvalidOperationException("selection") : First
                )
        );
        Assert.ThrowsExactly<AggregateException>(graph.Drain);
        Assert.AreEqual(5, root.Resolve(Value).Value);
        fail.Value = false;
        graph.Drain();
        Assert.AreEqual(10, root.Resolve(Value).Value);
        Assert.AreEqual("author:token:first:fallback", root.Resolve(Value).Winner.Source);
        fail.Value = true;
        Assert.ThrowsExactly<AggregateException>(graph.Drain);
        Assert.AreEqual(10, root.Resolve(Value).Value);
        Assert.AreEqual("author:token:first:fallback", root.Resolve(Value).Winner.Source);
    }

    [TestMethod]
    public void ConstructionSelectionKeepsTheTokenLiveAndSupportsConcreteNull()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "static-selection");
        var root = composition.Root;
        var theme = new ThemeContext(root.Scope, new Theme("test"));
        var optional = new Property<string?>("optional", "default");
        root.Present(
            theme,
            author: Style
                .Empty.SetValue<int>(Value, First)
                .SetValue(optional, StyleValue.FromValue<string?>(null))
        );
        Assert.AreEqual(10, root.Resolve(Value).Value);
        Assert.IsNull(root.Resolve(optional).Value);
        theme.Theme = theme.Theme.Set(First, 22);
        graph.Drain();
        Assert.AreEqual(22, root.Resolve(Value).Value);
        Assert.AreEqual("author:token:first:theme", root.Resolve(Value).Winner.Source);
        Assert.ThrowsExactly<ArgumentNullException>(() => StyleValue.FromToken<int>(null!));
    }
}
