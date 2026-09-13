namespace Lucent.Core;

/// <summary>Concise C# component construction over retained deferred recipes.</summary>
public static partial class Component
{
    /// <summary>Defines an erased retained recipe whose setup runs once for each mount.</summary>
    public static ComponentRecipe Define(string name, Func<ComponentContext, ComponentRecipe> build)
    {
        ReactiveGraph.ValidateName(name, nameof(name));
        ArgumentNullException.ThrowIfNull(build);
        return ComponentRecipe.Defer(name, owner => Build(owner, name, build));
    }

    /// <summary>Defines a capability-bearing retained recipe whose setup runs once for each mount.</summary>
    public static AuthorRecipe<TCapability> Define<TCapability>(
        string name,
        AuthorRecipeTarget<TCapability> target,
        Func<ComponentContext, AuthorRecipe<TCapability>> build
    )
        where TCapability : AuthorCapability
    {
        ReactiveGraph.ValidateName(name, nameof(name));
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(build);
        return AuthorRecipe.Defer(name, target, owner => Build(owner, name, build));
    }

    // Keep all Define entry points on this single per-owner construction seam. Generated state
    // creates and attaches its typed cells here before invoking authored build code.
    internal static TResult Build<TResult>(
        ReactiveScope owner,
        string name,
        Func<ComponentContext, TResult> build
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(build);
        return build(new ComponentContext(owner, name));
    }
}
