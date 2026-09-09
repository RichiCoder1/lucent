namespace Lucent.Core;

public static partial class Components
{
    [LucentComponent]
    internal static ComponentRecipe NumberFieldEditor(
        FieldContext field,
        NumericEditSession session,
        Func<bool>? enabled,
        Func<bool>? readOnly
    )
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(session);
        bool Editable() => enabled?.Invoke() != false && readOnly?.Invoke() != true;
        var stepStyle = Style.Empty.Bind(InputProperties.Enabled, Editable);
        var content = ComponentContent.Create([
            TextFieldControlledCore(
                field,
                () => session.Draft,
                session.Edit,
                null,
                null,
                enabled,
                readOnly,
                () => _ = session.Commit(),
                session.Cancel
            ),
            Button(
                "Decrease " + field.AccessibleName,
                () =>
                {
                    if (Editable())
                        _ = session.Step(-1);
                },
                stepStyle
            ),
            Button(
                "Increase " + field.AccessibleName,
                () =>
                {
                    if (Editable())
                        _ = session.Step(1);
                },
                stepStyle
            ),
        ]);
        return ComponentRecipe.Create(
            "number-field-editor",
            (context, root) =>
            {
                var presentation = Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Row);
                if (enabled is not null)
                    presentation = presentation.Bind(InputProperties.Enabled, enabled);
                root.Present(context.Theme, presentation);
                root.AttachBehaviors(new NumberFieldBehavior(session, field.AccessibleName));
                context.Mount(root, content);
            }
        );
    }
}

internal sealed class NumberFieldBehavior(NumericEditSession session, string label) : Behavior
{
    public override string Name => "number-field";
    public override BehaviorOwnership Ownership => BehaviorOwnership.Semantics;

    public override void Attach(BehaviorContext context)
    {
        SemanticDeclaration Declaration() => new(SemanticRole.Spinner, label, value: session.Draft);
        context.SetSemantics(Declaration());
        context.Effect(() => context.UpdateSemantics(Declaration()), label + ".spinner");
    }
}
