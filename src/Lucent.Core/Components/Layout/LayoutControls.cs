namespace Lucent.Core;

internal static partial class Controls
{
    public static void Panel(
        Element element,
        ThemeContext theme,
        string name,
        Style? style = null
    ) =>
        ConfigureSemantic(
            element,
            theme,
            PanelStyle,
            style,
            new(SemanticRole.Group, Required(name, nameof(name)))
        );

    public static void Row(Element element, ThemeContext theme, string name, Style? style = null) =>
        ConfigureSemantic(
            element,
            theme,
            RowStyle,
            style,
            new(SemanticRole.Group, Required(name, nameof(name)))
        );

    public static void Column(
        Element element,
        ThemeContext theme,
        string name,
        Style? style = null
    ) => Panel(element, theme, name, style);

    public static void List(
        Element element,
        ThemeContext theme,
        string name,
        Style? style = null
    ) =>
        ConfigureSemantic(
            element,
            theme,
            PanelStyle,
            style,
            new(SemanticRole.List, Required(name, nameof(name)))
        );
}
