namespace Lucent.Core;

public static partial class Component
{
    /// <summary>Defines an erased retained recipe with generated state isolated to each mount.</summary>
    public static ComponentRecipe Define<TState>(
        string name,
        Func<ComponentContext, TState, ComponentRecipe> build
    )
        where TState : IComponentState<TState>
    {
        ReactiveGraph.ValidateName(name, nameof(name));
        ArgumentNullException.ThrowIfNull(build);
        return ComponentRecipe.Defer(
            name,
            owner =>
                Build(owner, name, context => build(context, TState.CreateComponentState(context)))
        );
    }

    /// <summary>Defines a capability-bearing retained recipe with generated state isolated to each mount.</summary>
    public static AuthorRecipe<TCapability> Define<TState, TCapability>(
        string name,
        AuthorRecipeTarget<TCapability> target,
        Func<ComponentContext, TState, AuthorRecipe<TCapability>> build
    )
        where TState : IComponentState<TState>
        where TCapability : AuthorCapability
    {
        ReactiveGraph.ValidateName(name, nameof(name));
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(build);
        return AuthorRecipe.Defer(
            name,
            target,
            owner =>
                Build(owner, name, context => build(context, TState.CreateComponentState(context)))
        );
    }
}
