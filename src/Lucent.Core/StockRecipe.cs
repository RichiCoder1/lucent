namespace Lucent.Core;

// Only explicitly opted-in stock factories use these mappings. No public operation decorates an
// arbitrary erased recipe or searches its descendants for a presentation/semantic target.
internal static class StockRecipe
{
    internal static AuthorRecipe<StyledCapability> Styled(
        string kind,
        Action<CompositionContext, Element> content
    ) => Styled(ComponentRecipe.Create(kind, content));

    internal static AuthorRecipe<StyledAccessibleCapability> Accessible(
        string kind,
        Action<CompositionContext, Element> content
    ) => Accessible(ComponentRecipe.Create(kind, content));

    internal static readonly AuthorRecipeTarget<StyledCapability> StyledTarget =
        AuthorRecipe.Target<StyledCapability>(
            static (_, root, values) => root.RegisterRecipeStyle(values)
        );

    internal static readonly AuthorRecipeTarget<StyledAccessibleCapability> AccessibleTarget =
        AuthorRecipe.Target<StyledAccessibleCapability>(
            static (_, root, values) => root.RegisterRecipeAuthoring(values)
        );

    internal static AuthorRecipe<StyledCapability> Styled(ComponentRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return new(recipe.WithAuthoringTarget(StyledTarget), StyledTarget, recipe.Authoring);
    }

    internal static AuthorRecipe<StyledAccessibleCapability> Accessible(ComponentRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return new(
            recipe.WithAuthoringTarget(AccessibleTarget),
            AccessibleTarget,
            recipe.Authoring
        );
    }
}
