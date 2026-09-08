namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Creates a single-line text editor. <paramref name="label"/> is the accessible name; <paramref name="placeholder"/> is the muted empty-field hint, defaults to that label, and may be empty to disable the hint. Supply <paramref name="session"/> to retain its document state across mounts; otherwise <paramref name="initialValue"/> seeds mount-owned state.</summary>
    [LucentComponent]
    public static ComponentRecipe TextField(
        string initialValue = "",
        Action<string>? onChange = null,
        Style? style = null,
        string label = "Text field",
        EditorSession? session = null,
        FocusTarget? focusTarget = null,
        string? placeholder = null
    )
    {
        TextFieldState.ValidateText(initialValue);
        if (session is not null && initialValue.Length != 0)
            throw new ArgumentException(
                "Initial text is owned by the supplied editor session.",
                nameof(initialValue)
            );
        label = Required(label, nameof(label));
        return ComponentRecipe.Create(
            "text-field",
            (context, root) =>
            {
                var state = Controls.TextField(
                    root,
                    context.Theme,
                    label,
                    initialValue,
                    style,
                    session,
                    focusTarget,
                    placeholder
                );
                if (onChange is not null)
                {
                    var prior = state.Value;
                    _ = root.Scope.Effect(
                        () =>
                        {
                            var value = state.Value;
                            if (value != prior)
                            {
                                prior = value;
                                onChange(value);
                            }
                        },
                        root.Name + ".on-change"
                    );
                }
            }
        );
    }

    /// <summary>Creates a multiline text editor. <paramref name="label"/> is the accessible name; <paramref name="placeholder"/> is the muted empty-field hint, defaults to that label, and may be empty to disable the hint. Supply <paramref name="session"/> to retain its document state across mounts.</summary>
    [LucentComponent]
    public static ComponentRecipe TextArea(
        string initialValue = "",
        Action<string>? onChange = null,
        Style? style = null,
        string label = "Text area",
        EditorSession? session = null,
        FocusTarget? focusTarget = null,
        string? placeholder = null
    )
    {
        TextFieldState.ValidateMultilineText(initialValue);
        if (session is not null && initialValue.Length != 0)
            throw new ArgumentException(
                "Initial text is owned by the supplied editor session.",
                nameof(initialValue)
            );
        label = Required(label, nameof(label));
        return ComponentRecipe.Create(
            "text-area",
            (context, root) =>
            {
                var state = Controls.TextArea(
                    root,
                    context.Theme,
                    label,
                    initialValue,
                    style,
                    session,
                    focusTarget,
                    placeholder
                );
                if (onChange is not null)
                {
                    var prior = state.Value;
                    _ = root.Scope.Effect(
                        () =>
                        {
                            var value = state.Value;
                            if (value != prior)
                            {
                                prior = value;
                                onChange(value);
                            }
                        },
                        root.Name + ".on-change"
                    );
                }
            }
        );
    }
}
