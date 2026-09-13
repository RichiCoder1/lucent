namespace Lucent.Core;

public static partial class Components
{
    private static readonly AuthorRecipeTarget<StyledAccessibleCapability> AuthoringProofTarget =
        AuthorRecipe.Target<StyledAccessibleCapability>(
            static (context, root, values) =>
            {
                root.AttachBehaviors(new AuthoringProofSemantics(values));
                root.Present(context.Theme, author: values.Style);
            }
        );

    /// <summary>Provides the same-assembly runtime proof for an explicitly targeted author recipe.</summary>
    [LucentComponent]
    internal static AuthorRecipe<StyledAccessibleCapability> AuthoringRecipeCoreProof(
        Func<string> label
    ) =>
        AuthorRecipe
            .Create("authoring-core-proof", AuthoringProofTarget)
            .Style(Style.Empty.Width(88).Height(24))
            .Aria.Name(label)
            .Description("Core authoring proof")
            .End;

    private sealed class AuthoringProofSemantics(AuthorRecipeValues values) : Behavior
    {
        public override string Name => "authoring-core-proof-semantics";

        public override BehaviorOwnership Ownership => BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            SemanticDeclaration Declaration() =>
                new(
                    SemanticRole.Group,
                    values.NameReader?.Invoke() ?? values.Name ?? "Unnamed authoring proof",
                    description: values.DescriptionReader?.Invoke() ?? values.Description
                );
            context.SetSemantics(Declaration());
            context.Effect(
                () => context.UpdateSemantics(Declaration()),
                "authoring-core-proof-semantics"
            );
        }
    }
}
