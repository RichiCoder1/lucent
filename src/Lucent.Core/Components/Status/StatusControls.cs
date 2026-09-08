using System.Globalization;

namespace Lucent.Core;

internal static partial class Controls
{
    public static ControlState Loading(
        Element element,
        ThemeContext theme,
        string label = "Loading",
        Style? style = null
    )
    {
        label = Required(label, nameof(label));
        var component = TextStyle.Set(ProjectionProperties.Text, label);
        Preflight(
            element,
            theme,
            component,
            style,
            new SemanticBehavior(new(SemanticRole.Status, label))
        );
        var state = new ControlState(element.Scope, element.Name + ".loading", label);
        ConfigureSemantic(element, theme, component, style, new(SemanticRole.Status, label));
        Bind(
            element,
            state,
            value => element.UpdateControl(ProjectionProperties.Text, value.Label),
            value => new(SemanticRole.Status, value.Label)
        );
        return state;
    }

    public static ControlState Progress(
        Element element,
        ThemeContext theme,
        string label,
        float value,
        Style? style = null
    )
    {
        ControlState.ValidateProgress(value);
        label = Required(label, nameof(label));
        var text = ProgressText(label, value);
        var component = TextStyle.Set(ProjectionProperties.Text, text);
        Preflight(
            element,
            theme,
            component,
            style,
            new SemanticBehavior(new(SemanticRole.Status, label))
        );
        var state = new ControlState(
            element.Scope,
            element.Name + ".progress",
            label,
            progress: value
        );
        ConfigureSemantic(
            element,
            theme,
            component,
            style,
            new(SemanticRole.Status, label, value: Percent(value))
        );
        Bind(
            element,
            state,
            current =>
                element.UpdateControl(
                    ProjectionProperties.Text,
                    ProgressText(current.Label, current.Progress)
                ),
            current => new(SemanticRole.Status, current.Label, value: Percent(current.Progress))
        );
        return state;
    }

    public static ControlState Error(
        Element element,
        ThemeContext theme,
        string message,
        Style? style = null
    )
    {
        message = Required(message, nameof(message));
        var component = TextStyle.Set(ProjectionProperties.Text, message);
        Preflight(
            element,
            theme,
            component,
            style,
            new SemanticBehavior(new(SemanticRole.Status, message))
        );
        var state = new ControlState(element.Scope, element.Name + ".error", message);
        ConfigureSemantic(element, theme, component, style, new(SemanticRole.Status, message));
        Bind(
            element,
            state,
            value => element.UpdateControl(ProjectionProperties.Text, value.Label),
            value => new(SemanticRole.Status, value.Label)
        );
        return state;
    }

    private static string Percent(float value) =>
        MathF.Round(value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string ProgressText(string label, float value) => label + " " + Percent(value);
}
