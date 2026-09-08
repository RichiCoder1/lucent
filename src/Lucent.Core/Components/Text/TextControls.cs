namespace Lucent.Core;

internal static partial class Controls
{
    public static void Text(
        Element element,
        ThemeContext theme,
        string text,
        Style? style = null
    ) =>
        ConfigureSemantic(
            element,
            theme,
            TextStyle.Set(ProjectionProperties.Text, Required(text, nameof(text))),
            style,
            new(SemanticRole.Text, text)
        );
}
