using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ComponentContextContracts
{
    private static readonly string[] ExpectedRollbackOrder = ["inner-last", "inner-first", "outer"];

    [TestMethod]
    public void DefineAllocatesIndependentStateForEveryMountAndPreservesTypedCapabilities()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "component-define");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var states = new List<Signal<int>>();
        var erased = Component.Define(
            "counter",
            ui =>
            {
                states.Add(ui.State(0));
                return Presented("counter-root");
            }
        );

        var first = composition.Mount(composition.Root, theme, erased);
        var second = composition.Mount(composition.Root, theme, erased);

        Assert.AreEqual(2, states.Count);
        Assert.AreNotSame(states[0], states[1]);
        Assert.AreNotSame(first, second);
        states[0].Value = 3;
        Assert.AreEqual(0, states[1].Value);

        var target = AuthorRecipe.Target<StyledCapability>(
            (context, root, values) => root.Present(context.Theme, author: values.Style)
        );
        AuthorRecipe<StyledCapability> typed = Component.Define(
            "typed-counter",
            target,
            ui =>
                AuthorRecipe
                    .Create("typed-root", target)
                    .Style(Style.Empty.Width(ui.State(41).Value))
        );
        var typedRoot = composition.Mount(composition.Root, theme, typed.Recipe);
        Assert.AreEqual(41f, typedRoot.Resolve(LayoutProperties.Width).Value);
    }

    [TestMethod]
    public void DiagnosticFallbacksUseOneOrdinalAndExplicitNamesStillConsumeIt()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "component-diagnostics");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        Signal<int>? state = null;
        Derived<int>? computed = null;
        ReactiveEffect? observer = null;
        AsyncValue<string>? resource = null;
        var recipe = Component.Define(
            "diagnostics",
            ui =>
            {
                state = ui.State(1, "explicit-state");
                computed = ui.Computed(() => state.Value + 1);
                observer = ui.Observe(() => _ = computed.Value, "explicit-observer");
                resource = ui.Resource(_ => Task.FromResult("ready"));
                return Presented("diagnostic-root");
            }
        );

        _ = composition.Mount(composition.Root, theme, recipe);
        graph.Drain();

        Assert.AreEqual("explicit-state", state!.Name);
        Assert.AreEqual("diagnostics.computed-2", computed!.Name);
        Assert.AreEqual("explicit-observer", observer!.Name);
        Assert.AreEqual("diagnostics.resource-4", resource!.Name);
    }

    [TestMethod]
    public void ResourceForwardsEveryAsyncShapeWithoutStringStaleValueAmbiguity()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "component-resources");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        AsyncValue<string>? plain = null;
        AsyncValue<string>? stale = null;
        AsyncValue<string>? sourced = null;
        AsyncValue<string>? sourcedStale = null;
        var source = "input";
        var recipe = Component.Define(
            "resources",
            ui =>
            {
                plain = ui.Resource(_ => Task.FromResult("plain"), "plain-name");
                stale = ui.Resource(_ => Task.FromResult("loaded"), "stale", name: null);
                sourced = ui.Resource(
                    () => source,
                    (value, _) => Task.FromResult(value + "-loaded"),
                    "source-name"
                );
                sourcedStale = ui.Resource(
                    () => source,
                    (value, _) => Task.FromResult(value + "-loaded"),
                    "source-stale",
                    name: null
                );
                return Presented("resource-root");
            }
        );

        _ = composition.Mount(composition.Root, theme, recipe);

        Assert.AreEqual("plain-name", plain!.Name);
        Assert.AreEqual("stale", stale!.Value);
        Assert.AreEqual("source-name", sourced!.Name);
        Assert.AreEqual("source-stale", sourcedStale!.Value);
    }

    [TestMethod]
    public void NestedDefineSharesTheRetainedOwnerAndRollsBackSetupInReverseOrder()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "component-nested-rollback");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var cleanup = new List<string>();
        var nested = Component.Define(
            "outer",
            outer =>
            {
                outer.OnDispose(() => cleanup.Add("outer"));
                return Component.Define(
                    "inner",
                    inner =>
                    {
                        inner.Own(new CallbackDisposable(() => cleanup.Add("inner-first")));
                        inner.OnDispose(() => cleanup.Add("inner-last"));
                        throw new InvalidOperationException("setup failed");
                    }
                );
            }
        );

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            composition.Mount(composition.Root, theme, nested)
        );
        CollectionAssert.AreEqual(ExpectedRollbackOrder, cleanup);
        Assert.AreEqual(0, composition.Root.Children.Count);
    }

    [TestMethod]
    public void PostDispatchesOnTheOwnerAndComponentDisposalCancelsPendingCallbacks()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "component-post");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        ComponentContext? captured = null;
        Signal<int>? state = null;
        var recipe = Component.Define(
            "posted",
            ui =>
            {
                captured = ui;
                state = ui.State(0);
                return Presented("posted-root");
            }
        );
        var root = composition.Mount(composition.Root, theme, recipe);

        Task.Run(() => captured!.Post(() => state!.Value = 1)).GetAwaiter().GetResult();
        graph.Drain();
        Assert.AreEqual(1, state!.Value);

        Task.Run(() => captured!.Post(() => Assert.Fail("disposed callback ran")))
            .GetAwaiter()
            .GetResult();
        root.Dispose();
        graph.Drain();
    }

    [TestMethod]
    public void ContextAllocationsKeepExistingOwnerThreadChecks()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "component-thread");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        ComponentContext? captured = null;
        var recipe = Component.Define(
            "threaded",
            ui =>
            {
                captured = ui;
                return Presented("threaded-root");
            }
        );
        _ = composition.Mount(composition.Root, theme, recipe);

        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                _ = captured!.State(0);
            }
            catch (Exception error)
            {
                failure = error;
            }
        });
        worker.Start();
        worker.Join();
        Assert.IsInstanceOfType<InvalidOperationException>(failure);
        Assert.AreEqual("threaded.state-1", captured!.State(1).Name);
    }

    [TestMethod]
    public void KeyReplacementAndCollapseRetainStateWhileRemovalDisposesIt()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "component-retention");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var rows = composition.Root.Scope.Signal(new[] { new Row(1, "first") }, "component-rows");
        var participation = composition.Root.Scope.Signal(
            ElementParticipation.Visible,
            "component-participation"
        );
        var setups = 0;
        var disposals = 0;
        var content = ContentRecipe.ForEach(
            "component-items",
            () => rows.Value,
            row => row.Key,
            _current =>
                Component.Define(
                    "component-item",
                    ui =>
                    {
                        setups++;
                        _ = ui.State(7);
                        ui.OnDispose(() => disposals++);
                        return ComponentRecipe.Create(
                            "component-item-root",
                            (context, root) =>
                                root.Present(
                                    context.Theme,
                                    component: Style.Empty.Participation(() => participation.Value)
                                )
                        );
                    }
                )
        );
        var host = ComponentRecipe.Create(
            "component-host",
            (context, root) => context.Mount(root, ComponentContent.Create([content]))
        );
        var mounted = composition.Mount(composition.Root, theme, host);
        graph.Drain();
        var retained = mounted.Children.Single().Children.Single();

        rows.Value = [new Row(1, "replacement")];
        graph.Drain();
        participation.Value = ElementParticipation.Collapsed;
        graph.Drain();
        Assert.AreSame(retained, mounted.Children.Single().Children.Single());
        Assert.AreEqual(1, setups);
        Assert.AreEqual(0, disposals);

        rows.Value = [];
        graph.Drain();
        Assert.IsTrue(retained.IsDisposed);
        Assert.AreEqual(1, disposals);
    }

    [TestMethod]
    public void LateResourceCompletionIsSuppressedButApplicationOwnedWorkContinues()
    {
        var graph = new ReactiveGraph();
        using var application = graph.CreateScope("application-work");
        using var composition = new Composition(graph, "component-async-lifetime");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var componentCompletion = new TaskCompletionSource<int>();
        var acceptedApplicationWork = new TaskCompletionSource<int>();
        var applicationResource = application.Async(
            _ => acceptedApplicationWork.Task,
            "accepted-application-work"
        );
        _ = applicationResource.Value;
        AsyncValue<int>? resource = null;
        var recipe = Component.Define(
            "async-owner",
            ui =>
            {
                resource = ui.Resource(_ => componentCompletion.Task);
                ui.Observe(() => _ = resource.Value);
                return Presented("async-root");
            }
        );
        var root = composition.Mount(composition.Root, theme, recipe);
        graph.Drain();
        Assert.IsTrue(resource!.IsPending);

        root.Dispose();
        Task.Run(() =>
            {
                componentCompletion.SetResult(3);
                acceptedApplicationWork.SetResult(9);
            })
            .GetAwaiter()
            .GetResult();
        graph.Drain();

        Assert.IsTrue(resource.IsDisposed);
        Assert.AreEqual(9, applicationResource.Value);
    }

    [TestMethod]
    public void CleanupFailureStillAttemptsEarlierOwnedCleanup()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "component-cleanup-failure");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var cleaned = false;
        var recipe = Component.Define(
            "cleanup",
            ui =>
            {
                ui.OnDispose(() => cleaned = true);
                ui.OnDispose(() => throw new InvalidOperationException("cleanup failed"));
                return Presented("cleanup-root");
            }
        );
        var root = composition.Mount(composition.Root, theme, recipe);

        Assert.ThrowsExactly<AggregateException>(root.Dispose);
        Assert.IsTrue(cleaned);
    }

    private static ComponentRecipe Presented(string name) =>
        ComponentRecipe.Create(name, (context, root) => root.Present(context.Theme));

    private sealed class CallbackDisposable(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private sealed record Row(int Key, string Text);
}
