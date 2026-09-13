namespace Lucent.Core;

public static partial class Components
{
    [LucentComponent]
    internal static ComponentRecipe FieldRoot(
        FieldContext field,
        FieldParticipation participation,
        [DefaultContent] ComponentContent content,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(field);
        return ComponentRecipe.Create(
            "field",
            (context, root) =>
            {
                field.AttachFieldRoot(root, participation);
                Controls.FieldRoot(root, context.Theme, style);
                context.Mount(root, content);
            }
        );
    }

    [LucentComponent]
    internal static ComponentRecipe FieldLabel(FieldContext field, string label, bool required)
    {
        ArgumentNullException.ThrowIfNull(field);
        label = Required(label, nameof(label));
        var content = required ? label + " *" : label;
        return ComponentRecipe.Create(
            "field-label",
            (context, root) =>
            {
                field.AttachLabel(root);
                Controls.FieldLabel(root, context.Theme, content, field.FocusTarget);
            }
        );
    }

    [LucentComponent]
    internal static ComponentRecipe FieldHelp(FieldContext field, Func<string> content)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(content);
        return ComponentRecipe.Create(
            "field-help",
            (context, root) =>
            {
                field.AttachHelp(root);
                var value = root.Scope.Derived(
                    () => Required(content(), nameof(content)),
                    root.Name + ".content"
                );
                Controls.FieldHelp(root, context.Theme, value.Value);
                _ = root.Scope.Effect(
                    () =>
                    {
                        var current = value.Value;
                        field.SetHelpText(current);
                        root.UpdateControl(ProjectionProperties.Text, current);
                        root.UpdateControlSemantics(new(SemanticRole.Text, current));
                    },
                    root.Name + ".update"
                );
            }
        );
    }

    [LucentComponent]
    internal static ComponentRecipe FieldError(FieldContext field, Func<string> content)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(content);
        return ComponentRecipe.Create(
            "field-error",
            (context, root) =>
            {
                field.AttachError(root);
                var value = root.Scope.Derived(
                    () => Required(content(), nameof(content)),
                    root.Name + ".content"
                );
                Controls.FieldError(root, context.Theme, value.Value);
                _ = root.Scope.Effect(
                    () =>
                    {
                        var current = value.Value;
                        root.UpdateControl(ProjectionProperties.Text, current);
                        root.UpdateControlSemantics(new(SemanticRole.Status, current));
                    },
                    root.Name + ".update"
                );
            }
        );
    }
}
