namespace Lucent.Core;

/// <summary>Bounds retained secret-editing history for PasswordField.</summary>
public sealed class PasswordFieldOptions
{
    /// <summary>Default maximum number of password edit snapshots retained for undo.</summary>
    public const int DefaultHistoryLimit = 8;

    /// <summary>Creates password editing options with a history limit from zero through 64.</summary>
    public PasswordFieldOptions(int historyLimit = DefaultHistoryLimit)
    {
        if (historyLimit is < 0 or > 64)
            throw new ArgumentOutOfRangeException(nameof(historyLimit));
        HistoryLimit = historyLimit;
    }

    /// <summary>Gets the maximum number of password edit snapshots retained for undo.</summary>
    public int HistoryLimit { get; }
}

public static partial class Components
{
    [LucentComponent]
    internal static ComponentRecipe PasswordFieldEditor(
        FieldContext field,
        Func<string> value,
        Action<string> onChangeRequested,
        PasswordFieldOptions? options,
        Func<bool>? enabled,
        Func<bool>? readOnly
    )
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(onChangeRequested);
        options ??= new PasswordFieldOptions();
        return ComponentRecipe.Create(
            "password-field-editor",
            (context, root) =>
            {
                var revealed = root.Scope.Signal(false, root.Name + ".revealed");
                void Toggle()
                {
                    revealed.Value = !revealed.Value;
                    field.FocusTarget.Request();
                }

                var content = ComponentContent.Create([
                    TextFieldControlledCore(
                        field,
                        value,
                        onChangeRequested,
                        null,
                        "",
                        enabled,
                        readOnly,
                        confidential: true,
                        reveal: () => revealed.Value,
                        historyLimit: options.HistoryLimit,
                        remask: () => revealed.Value = false
                    ),
                    ButtonCore(
                        () => revealed.Value ? "Hide password" : "Show password",
                        Toggle,
                        Style.Empty.Bind(
                            InputProperties.Enabled,
                            () => enabled?.Invoke() != false && readOnly?.Invoke() != true
                        ),
                        // Keep the editor focused so blur does not remask before a Hide click.
                        // Keyboard and automation focus remain available on the button.
                        focusOnPointer: false
                    ),
                ]);
                root.Present(context.Theme, Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Row));
                context.Mount(root, content);
            }
        );
    }
}
