namespace Lucent.Core;

public static partial class Components
{
    [LucentComponent]
    internal static ComponentRecipe NumberFieldStep(
        ImageSource source,
        string label,
        Action onInvoke,
        Style style
    ) => IconButtonRecipe(() => source, () => label, onInvoke, style, focusOnPointer: false);

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
        var decreaseStyle = Style.Empty.Bind(
            InputProperties.Enabled,
            () => Editable() && session.CanStep(-1)
        );
        var increaseStyle = Style.Empty.Bind(
            InputProperties.Enabled,
            () => Editable() && session.CanStep(1)
        );
        return NumberFieldEditorView(
            field,
            session,
            enabled,
            readOnly,
            () =>
            {
                if (Editable() && session.CanStep(-1))
                    _ = session.Step(-1);
            },
            () =>
            {
                if (Editable() && session.CanStep(1))
                    _ = session.Step(1);
            },
            decreaseStyle,
            increaseStyle
        );
    }

    [LucentComponent]
    internal static ComponentRecipe NumberFieldTextField(
        FieldContext field,
        NumericEditSession session,
        Func<bool>? enabled,
        Func<bool>? readOnly
    ) =>
        TextFieldControlledCore(
            field,
            () => session.Draft,
            session.Edit,
            Style.Empty.MinWidth(0).MainGrow(1),
            null,
            enabled,
            readOnly,
            () => _ = session.Commit(),
            session.Cancel,
            preserveSelectionOnAppliedChange: true
        );

    [LucentComponent]
    internal static ComponentRecipe NumberFieldHost(
        NumericEditSession session,
        string label,
        Func<bool>? enabled,
        Func<bool>? readOnly,
        [DefaultContent] ComponentContent content
    ) =>
        Host(
            "number-field",
            content,
            (context, root) =>
            {
                root.Present(
                    context.Theme,
                    Style
                        .Empty.Set(LayoutProperties.Axis, LayoutAxis.Row)
                        .Spacing(6)
                        .CrossAlignment(LayoutAlignment.Center)
                        .Bind(InputProperties.Enabled, () => enabled?.Invoke() != false)
                );
                root.AttachBehaviors(new NumberFieldBehavior(session, label));
            }
        );
}

internal sealed class NumberFieldBehavior(NumericEditSession session, string label) : Behavior
{
    public override string Name => "number-field";
    public override BehaviorOwnership Ownership => BehaviorOwnership.Semantics;

    public override void Attach(BehaviorContext context)
    {
        SemanticDeclaration Declaration() =>
            SemanticDeclaration.Create(SemanticRole.Spinner, label).Value(session.Draft).Build();
        context.SetSemantics(Declaration());
        context.Effect(() => context.UpdateSemantics(Declaration()), label + ".spinner");
    }
}
