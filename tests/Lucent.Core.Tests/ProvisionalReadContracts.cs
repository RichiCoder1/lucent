using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ProvisionalReadContracts
{
    private static readonly int[] OneItem = [1];

    [TestMethod]
    public void AmbiguousRegionsWithoutAParentThemeFailBeforeAllocation()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "themeless-regions");
        var before = composition.Root.Children.Count;

        var when = Assert.ThrowsExactly<InvalidOperationException>(() =>
            composition.When(
                composition.Root,
                "when",
                static () => false,
                static context => context.Element("child")
            )
        );
        var @switch = Assert.ThrowsExactly<InvalidOperationException>(() =>
            composition.Switch(composition.Root, "switch", static () => default)
        );
        var each = Assert.ThrowsExactly<InvalidOperationException>(() =>
            composition.ForEach(
                composition.Root,
                "each",
                Array.Empty<int>,
                static item => item,
                static (_, context) => context.Element("child")
            )
        );

        foreach (var failure in new[] { when, @switch, each })
        {
            StringAssert.Contains(failure.Message, "ThemeContext");
            StringAssert.Contains(failure.Message, "Structure");
        }
        Assert.AreEqual(before, composition.Root.Children.Count);
    }

    [TestMethod]
    public void ExplicitInferredAndStructuralRegionsExposeIntentionalThemeContexts()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "region-themes");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var explicitActive = graph.Signal(true, "explicit-active");
        var explicitRegion = composition.When(
            composition.Root,
            theme,
            "explicit",
            () => explicitActive.Value,
            context =>
            {
                Assert.AreSame(theme, context.Theme);
                var child = context.Element("explicit-child");
                Controls.Text(child, context.Theme, "Explicit");
                return child;
            }
        );
        var presentedParent = composition.Child(composition.Root, "presented-parent");
        presentedParent.Present(theme);
        var inferredRegion = composition.ForEach(
            presentedParent,
            "inferred",
            static () => OneItem,
            static item => item,
            (_, context) =>
            {
                Assert.AreSame(theme, context.Theme);
                var child = context.Element("inferred-child");
                Controls.Text(child, context.Theme, "Inferred");
                return child;
            }
        );
        var structuralRegion = composition.WhenStructure(
            composition.Root,
            "structural",
            static () => true,
            context =>
            {
                _ = Assert.ThrowsExactly<InvalidOperationException>(() =>
                {
                    _ = context.Theme;
                });
                return context.Element("structural-child");
            }
        );

        graph.Drain();

        Assert.IsNotNull(explicitRegion.Active);
        Assert.AreEqual(1, inferredRegion.Items.Count);
        Assert.IsNotNull(structuralRegion.Active);
    }

    [TestMethod]
    public void ParentDerivedReadsAreIndependentOfDirtyTimingDuringChildMount()
    {
        AssertParentDerivedMount(dirty: false);
        AssertParentDerivedMount(dirty: true);
    }

    [TestMethod]
    public void ParentAsyncReadsAreIndependentOfDirtyTimingDuringChildMount()
    {
        AssertParentAsyncMount(dirty: false);
        AssertParentAsyncMount(dirty: true);
    }

    [TestMethod]
    public void ParentDerivedEvaluationStillCannotWriteOutsideTheProvisionalTree()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "derived-write-guard");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var parent = composition.Child(composition.Root, "parent");
        var source = parent.Scope.Signal(1, "source");
        var derived = parent.Scope.Derived(
            () =>
            {
                source.Value++;
                return source.Value;
            },
            "writing-derived"
        );

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
            composition.Mount(
                parent,
                theme,
                ComponentRecipe.Create("child", (context, root) => _ = derived.Value)
            )
        );

        Assert.IsTrue(
            Flatten(failure)
                .Any(error => error.Message.Contains("provisional root", StringComparison.Ordinal))
        );
        Assert.AreEqual(1, source.Value);
        Assert.AreEqual(0, parent.Children.Count);
    }

    [TestMethod]
    public void FailedMountCancelsOnlyItsOwnStartedAsyncWork()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "async-rollback");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var parent = composition.Child(composition.Root, "parent");
        var parentCompletion = new TaskCompletionSource<int>();
        CancellationToken parentToken = default;
        var parentAsync = parent.Scope.Async(
            token =>
            {
                parentToken = token;
                return parentCompletion.Task;
            },
            "parent-async"
        );
        AsyncValue<int>? childAsync = null;
        CancellationToken childToken = default;

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
            composition.Mount(
                parent,
                theme,
                ComponentRecipe.Create(
                    "failing-child",
                    (context, root) =>
                    {
                        Assert.IsTrue(parentAsync.IsPending);
                        childAsync = root.Scope.Async(
                            token =>
                            {
                                childToken = token;
                                return Task.FromResult(1);
                            },
                            "child-async"
                        );
                        Assert.IsTrue(childAsync.IsPending);
                        throw new InvalidOperationException("mount-failed");
                    }
                )
            )
        );

        Assert.IsTrue(Flatten(failure).Any(error => error.Message == "mount-failed"));
        Assert.IsNotNull(childAsync);
        Assert.IsTrue(childAsync.IsDisposed);
        _ = Assert.ThrowsExactly<ObjectDisposedException>(() => _ = childAsync.Value);
        Assert.IsTrue(childToken.IsCancellationRequested);
        Assert.IsFalse(parentToken.IsCancellationRequested);
        Assert.IsFalse(parentAsync.IsDisposed);
        Assert.AreEqual(0, parent.Children.Count);
        parentCompletion.SetResult(2);
        graph.Drain();
        Assert.AreEqual(2, parentAsync.Value);
    }

    private static void AssertParentDerivedMount(bool dirty)
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, dirty ? "dirty" : "clean");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var parent = composition.Child(composition.Root, "parent");
        var source = parent.Scope.Signal(1, "source");
        var derived = parent.Scope.Derived(() => source.Value * 2, "derived");
        Assert.AreEqual(2, derived.Value);
        if (dirty)
            source.Value = 2;
        var observed = 0;

        _ = composition.Mount(
            parent,
            theme,
            ComponentRecipe.Create("child", (context, root) => observed = derived.Value)
        );

        Assert.AreEqual(dirty ? 4 : 2, observed);
    }

    private static void AssertParentAsyncMount(bool dirty)
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, dirty ? "async-dirty" : "async-clean");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var parent = composition.Child(composition.Root, "parent");
        var loads = 0;
        var resource = parent.Scope.Async(_ => Task.FromResult(++loads), "parent-async");
        Assert.IsTrue(resource.IsPending);
        graph.Drain();
        Assert.AreEqual(1, resource.Value);
        if (dirty)
            resource.Refresh();
        var observed = 0;

        _ = composition.Mount(
            parent,
            theme,
            ComponentRecipe.Create("child", (context, root) => observed = resource.Value)
        );
        graph.Drain();

        Assert.AreEqual(dirty ? 2 : 1, resource.Value);
        Assert.AreEqual(1, observed);
    }

    private static IEnumerable<Exception> Flatten(Exception error) =>
        error is AggregateException aggregate
            ? aggregate.InnerExceptions.SelectMany(Flatten)
            : [error];
}
