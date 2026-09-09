namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Creates a controlled primary editor whose draft requests remain distinct from the application value.</summary>
    [LucentComponent]
    public static ComponentRecipe TextField(
        FieldContext field,
        Func<string> value,
        Action<string> onChangeRequested,
        Style? style = null,
        string? placeholder = null,
        Func<bool>? enabled = null,
        Func<bool>? readOnly = null
    )
    {
        return TextFieldControlledCore(
            field,
            value,
            onChangeRequested,
            style,
            placeholder,
            enabled,
            readOnly
        );
    }

    private static ComponentRecipe TextFieldControlledCore(
        FieldContext field,
        Func<string> value,
        Action<string> onChangeRequested,
        Style? style,
        string? placeholder,
        Func<bool>? enabled,
        Func<bool>? readOnly,
        Action? committed = null,
        Action? cancelled = null
    )
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(onChangeRequested);
        return ComponentRecipe.Create(
            "text-field",
            (context, root) =>
            {
                var applied = root.Scope.Derived(
                    () =>
                    {
                        var current = value();
                        TextFieldState.ValidateText(current);
                        return current;
                    },
                    root.Name + ".applied-value"
                );
                var state = Controls.TextField(
                    root,
                    context.Theme,
                    field.AccessibleName,
                    applied.Value,
                    style,
                    null,
                    field.FocusTarget,
                    placeholder,
                    field,
                    enabled,
                    readOnly,
                    committed,
                    cancelled
                );
                var lastApplied = applied.Value;
                var lastDraft = state.Value;
                _ = root.Scope.Effect(
                    () =>
                    {
                        var currentApplied = applied.Value;
                        var currentDraft = state.Value;
                        if (!string.Equals(currentApplied, lastApplied, StringComparison.Ordinal))
                        {
                            lastApplied = currentApplied;
                            lastDraft = currentApplied;
                            if (
                                !string.Equals(
                                    currentDraft,
                                    currentApplied,
                                    StringComparison.Ordinal
                                )
                            )
                                state.Value = currentApplied;
                        }
                        else if (!string.Equals(currentDraft, lastDraft, StringComparison.Ordinal))
                        {
                            lastDraft = currentDraft;
                            onChangeRequested(currentDraft);
                        }
                    },
                    root.Name + ".controlled-value"
                );
            }
        );
    }

    /// <summary>Creates the primary single-line editor for a typed field context.</summary>
    [LucentComponent]
    public static ComponentRecipe TextField(
        FieldContext field,
        string initialValue = "",
        Action<string>? onChange = null,
        Style? style = null,
        EditorSession? session = null,
        string? placeholder = null,
        Func<bool>? enabled = null,
        Func<bool>? readOnly = null
    )
    {
        ArgumentNullException.ThrowIfNull(field);
        return TextFieldCore(
            initialValue,
            onChange,
            style,
            field.AccessibleName,
            session,
            field.FocusTarget,
            placeholder,
            field,
            enabled,
            readOnly
        );
    }

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
    ) => TextFieldCore(initialValue, onChange, style, label, session, focusTarget, placeholder);

    private static ComponentRecipe TextFieldCore(
        string initialValue,
        Action<string>? onChange,
        Style? style,
        string label,
        EditorSession? session,
        FocusTarget? focusTarget,
        string? placeholder,
        FieldContext? field = null,
        Func<bool>? enabled = null,
        Func<bool>? readOnly = null,
        Action? committed = null,
        Action? cancelled = null
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
                    placeholder,
                    field,
                    enabled,
                    readOnly,
                    committed,
                    cancelled
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
