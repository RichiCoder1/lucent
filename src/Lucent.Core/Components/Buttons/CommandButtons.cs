namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Creates a button that observes command availability and guards every invocation.</summary>
    /// <remarks>The button borrows the command; its caller retains ownership and disposal responsibility.</remarks>
    [LucentComponent]
    public static AuthorRecipe<StyledAccessibleCapability> Button(
        [DefaultContent] string content,
        ApplicationCommand command,
        Style? style = null,
        Func<ImageSource>? leadingIcon = null,
        FocusTarget? focusTarget = null,
        Func<AriaMetadata?>? aria = null
    )
    {
        content = Required(content, nameof(content));
        return Button(() => content, command, style, leadingIcon, focusTarget, aria);
    }

    /// <summary>Creates a reader-labeled button that observes command availability and guards every invocation.</summary>
    /// <remarks>The button borrows the command; its caller retains ownership and disposal responsibility.</remarks>
    [LucentComponent]
    public static AuthorRecipe<StyledAccessibleCapability> Button(
        [DefaultContent] Func<string> content,
        ApplicationCommand command,
        Style? style = null,
        Func<ImageSource>? leadingIcon = null,
        FocusTarget? focusTarget = null,
        Func<AriaMetadata?>? aria = null
    )
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(command);
        var available = (style ?? Style.Empty).When(
            () => !command.IsEnabled,
            Style.Empty.Enabled(false)
        );
        var recipe = leadingIcon is null
            ? ReaderButton(content, focusTarget, () => command.TryExecute(), available, null)
            : StockRecipe.Accessible(
                ComposedButton(
                    content,
                    leadingIcon,
                    () => command.TryExecute(),
                    available,
                    focusTarget
                )
            );
        return aria is null ? recipe : recipe.Aria.Metadata(aria).End;
    }
}
