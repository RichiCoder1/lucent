using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ComponentStateContracts
{
    [TestMethod]
    public void GeneratedStateContractCreatesIndependentMountStateAndPreservesTarget()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "component-state");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var states = new List<ManualState>();
        var recipe = Component.Define<ManualState>(
            "stateful",
            (_, state) =>
            {
                states.Add(state);
                return Presented("stateful-root");
            }
        );

        var first = composition.Mount(composition.Root, theme, recipe);
        var second = composition.Mount(composition.Root, theme, recipe);
        states[0].Count = 8;

        Assert.AreEqual(2, states.Count);
        Assert.AreEqual(8, states[0].Count);
        Assert.AreEqual(1, states[1].Count);
        first.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = states[0].Count);
        Assert.AreEqual(1, states[1].Count);
        second.Dispose();

        var target = AuthorRecipe.Target<StyledCapability>(
            (context, root, values) => root.Present(context.Theme, author: values.Style)
        );
        AuthorRecipe<StyledCapability> typed = Component.Define<ManualState, StyledCapability>(
            "typed-stateful",
            target,
            (_, state) =>
                AuthorRecipe
                    .Create("typed-stateful-root", target)
                    .Style(Style.Empty.Width(state.Count + 40))
        );
        var typedRoot = composition.Mount(composition.Root, theme, typed.Recipe);
        Assert.AreEqual(41f, typedRoot.Resolve(LayoutProperties.Width).Value);
    }

    [TestMethod]
    public void GeneratedStateContractRetainsContextThreadAndRollbackGuards()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "component-state-guards");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        ManualState? captured = null;
        var recipe = Component.Define<ManualState>(
            "guarded-state",
            (_, state) =>
            {
                captured = state;
                return Presented("guarded-state-root");
            }
        );
        var root = composition.Mount(composition.Root, theme, recipe);

        Exception? offThreadError = null;
        var offThread = new Thread(() =>
        {
            try
            {
                _ = captured!.Count;
            }
            catch (Exception error)
            {
                offThreadError = error;
            }
        });
        offThread.Start();
        offThread.Join();
        Assert.IsInstanceOfType<InvalidOperationException>(offThreadError);
        root.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => captured!.Count = 2);

        ManualState? failed = null;
        var failure = Component.Define<ManualState>(
            "failed-state",
            (_, state) =>
            {
                failed = state;
                throw new InvalidOperationException("build failed");
            }
        );
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            composition.Mount(composition.Root, theme, failure)
        );
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = failed!.Count);
    }

    private static ComponentRecipe Presented(string name) =>
        ComponentRecipe.Create(name, (context, root) => root.Present(context.Theme));

    private sealed class ManualState : IComponentState<ManualState>
    {
        private readonly Signal<int> _count;

        private ManualState(ComponentContext context) =>
            _count = context.State(1, nameof(ManualState) + "." + nameof(Count));

        public int Count
        {
            get => _count.Value;
            set => _count.Value = value;
        }

        static ManualState IComponentState<ManualState>.CreateComponentState(
            ComponentContext context
        ) => new(context);
    }
}
