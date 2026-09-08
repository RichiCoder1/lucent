namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Creates a text component that displays the supplied string.</summary>
    [LucentComponent]
    public static ComponentRecipe Text([DefaultContent] string content, Style? style = null)
    {
        content = Required(content, nameof(content));
        return ComponentRecipe.Create(
            "text",
            (context, root) => Controls.Text(root, context.Theme, content, style)
        );
    }

    /// <summary>Creates a text component whose displayed string is read again when its value changes.</summary>
    [LucentComponent]
    public static ComponentRecipe Text([DefaultContent] Func<string> content, Style? style = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return ComponentRecipe.Create(
            "text",
            (context, root) =>
            {
                var value = root.Scope.Derived(
                    () => Required(content(), nameof(content)),
                    root.Name + ".text-read"
                );
                Controls.Text(root, context.Theme, value.Value, style);
                _ = root.Scope.Effect(
                    () =>
                    {
                        root.UpdateControl(ProjectionProperties.Text, value.Value);
                        root.UpdateControlSemantics(new(SemanticRole.Text, value.Value));
                    },
                    root.Name + ".text"
                );
            }
        );
    }
}
