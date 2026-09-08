namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Installs application key bindings for the supplied component subtree.</summary>
    /// <remarks>A matching nearest binding consumes its chord even while its command is disabled or busy.</remarks>
    [LucentComponent]
    public static ComponentRecipe CommandScope(
        [DefaultContent] ComponentContent content,
        CommandBindings bindings
    )
    {
        content = Content(content);
        ArgumentNullException.ThrowIfNull(bindings);
        return ComponentRecipe.Create(
            "command-scope",
            (context, root) =>
            {
                root.Present(
                    context.Theme,
                    author: Style
                        .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                        .Set(LayoutProperties.MainGrow, 1f)
                );
                root.AttachBehaviors(new CommandScopeBehavior(bindings));
                context.Mount(root, content);
            }
        );
    }
}
