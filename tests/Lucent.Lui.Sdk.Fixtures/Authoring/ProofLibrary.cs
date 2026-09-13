using Lucent.Core;

namespace AuthoringGate;

public static class ProofLibrary
{
    private static readonly AuthorRecipeTarget<StyledAccessibleCapability> Target =
        AuthorRecipe.Target<StyledAccessibleCapability>(
            (context, root, values) =>
            {
                root.Present(context.Theme, Style.Empty.Width(20).Height(24), values.Style);
                root.AttachBehaviors(new ProofSemantics(values));
            }
        );

    [LucentComponent]
    public static AuthorRecipe<StyledAccessibleCapability> Probe(Func<string> label) =>
        AuthorRecipe
            .Create("authoring-probe", Target)
            .Style(Style.Empty.Width(132))
            .Aria.Name(label)
            .Description("Explicit packaged target")
            .End.Named("proof");

    public static ComponentRecipe Generated(Func<string> label) => Components.GeneratedProbe(label);

    private sealed class ProofSemantics(AuthorRecipeValues values) : Behavior
    {
        public override string Name => "authoring-proof-semantics";
        public override BehaviorOwnership Ownership => BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            SemanticDeclaration Declaration() =>
                new(
                    SemanticRole.Text,
                    values.NameReader?.Invoke() ?? values.Name ?? "Default proof",
                    description: values.DescriptionReader?.Invoke() ?? values.Description
                );
            context.SetSemantics(Declaration());
            context.Effect(() => context.UpdateSemantics(Declaration()), "proof-semantics");
        }
    }
}
