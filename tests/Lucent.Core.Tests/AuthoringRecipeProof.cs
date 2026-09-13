namespace Lucent.Core;

/// <summary>Small same-assembly proof target for the bounded author recipe gate.</summary>
/// <remarks>
/// This fixture demonstrates the required target mapping: the target owns the root, applies the
/// authored semantic declaration and style before the root is presented, and leaves content and
/// lifetime on the ordinary <see cref="ComponentRecipe"/> path.
/// </remarks>
internal static class AuthoringRecipeProof
{
    private static readonly AuthorRecipeTarget<StyledAccessibleCapability> Target =
        AuthorRecipe.Target<StyledAccessibleCapability>(Apply);

    internal static AuthorRecipe<StyledAccessibleCapability> Create(
        string name,
        string? description,
        Style? style = null
    )
    {
        var aria = AuthorRecipe
            .Create("authoring-proof", Target)
            .Style(style ?? Style.Empty)
            .Aria.Name(name);
        return (description is null ? aria : aria.Description(description)).End;
    }

    internal static AuthorRecipe<StyledAccessibleCapability> Create(
        Func<string> name,
        string? description,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        var aria = AuthorRecipe
            .Create("authoring-proof", Target)
            .Style(style ?? Style.Empty)
            .Aria.Name(name);
        return (description is null ? aria : aria.Description(description)).End;
    }

    private static void Apply(CompositionContext context, Element root, AuthorRecipeValues values)
    {
        var name = values.NameReader?.Invoke() ?? values.Name;
        if (name is null)
            throw new InvalidOperationException("The proof target requires an authored name.");
        root.AttachBehaviors(new ProofSemantics(values));
        root.Present(context.Theme, author: values.Style);
    }

    private sealed class ProofSemantics(AuthorRecipeValues values) : Behavior
    {
        public override string Name => "authoring-proof-semantics";

        public override BehaviorOwnership Ownership => BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            SemanticDeclaration Declaration() =>
                new(
                    SemanticRole.Group,
                    values.NameReader?.Invoke() ?? values.Name ?? "Unnamed proof",
                    description: values.DescriptionReader?.Invoke() ?? values.Description
                );
            context.BindSemantics(Declaration);
        }
    }
}
