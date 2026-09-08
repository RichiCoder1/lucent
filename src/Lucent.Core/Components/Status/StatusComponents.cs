namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Creates a noninteractive component that displays status text.</summary>
    [LucentComponent]
    public static ComponentRecipe Status([DefaultContent] string content, Style? style = null)
    {
        content = Required(content, nameof(content));
        return ComponentRecipe.Create(
            "status",
            (context, root) => Controls.Loading(root, context.Theme, content, style)
        );
    }

    /// <summary>Creates a noninteractive status component whose text follows the supplied reader.</summary>
    [LucentComponent]
    public static ComponentRecipe Status([DefaultContent] Func<string> content, Style? style = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return ComponentRecipe.Create(
            "status",
            (context, root) =>
            {
                var value = root.Scope.Derived(
                    () => Required(content(), nameof(content)),
                    root.Name + ".status-read"
                );
                var state = Controls.Loading(root, context.Theme, value.Value, style);
                _ = root.Scope.Effect(() => state.Label = value.Value, root.Name + ".status");
            }
        );
    }

    /// <summary>Creates a progress component with a fixed value from 0 to 1, where 1 means complete.</summary>
    [LucentComponent]
    public static ComponentRecipe Progress(
        [DefaultContent] string label,
        float value,
        Style? style = null
    )
    {
        label = Required(label, nameof(label));
        ControlState.ValidateProgress(value);
        return ComponentRecipe.Create(
            "progress",
            (context, root) => Controls.Progress(root, context.Theme, label, value, style)
        );
    }

    /// <summary>Creates a progress component whose value follows the supplied reader; values range from 0 to 1.</summary>
    [LucentComponent]
    public static ComponentRecipe Progress(
        [DefaultContent] string label,
        Func<float> value,
        Style? style = null
    )
    {
        label = Required(label, nameof(label));
        ArgumentNullException.ThrowIfNull(value);
        return ComponentRecipe.Create(
            "progress",
            (context, root) =>
            {
                var progress = root.Scope.Derived(
                    () => ProgressValue(value),
                    root.Name + ".progress-read"
                );
                var state = Controls.Progress(root, context.Theme, label, progress.Value, style);
                _ = root.Scope.Effect(
                    () => state.Progress = progress.Value,
                    root.Name + ".progress"
                );
            }
        );
    }

    private static float ProgressValue(Func<float> read)
    {
        var result = read();
        ControlState.ValidateProgress(result);
        return result;
    }
}
