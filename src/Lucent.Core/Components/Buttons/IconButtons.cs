namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Creates a button with a decorative leading icon and visible text.</summary>
    [LucentComponent]
    public static ComponentRecipe Button(
        [DefaultContent] string content,
        ImageSource leadingIcon,
        Action? onInvoke = null,
        Style? style = null
    )
    {
        content = Required(content, nameof(content));
        ArgumentNullException.ThrowIfNull(leadingIcon);
        return ComposedButton(() => content, () => leadingIcon, onInvoke, style);
    }

    /// <summary>Creates a button whose visible text and decorative leading icon follow typed readers.</summary>
    [LucentComponent]
    public static ComponentRecipe Button(
        [DefaultContent] Func<string> content,
        Func<ImageSource> leadingIcon,
        Action? onInvoke = null,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(leadingIcon);
        return ComposedButton(content, leadingIcon, onInvoke, style);
    }

    /// <summary>Creates a button whose visible text follows a reader and whose decorative leading icon is fixed.</summary>
    [LucentComponent]
    public static ComponentRecipe Button(
        [DefaultContent] Func<string> content,
        ImageSource leadingIcon,
        Action? onInvoke = null,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(leadingIcon);
        return ComposedButton(content, () => leadingIcon, onInvoke, style);
    }

    /// <summary>Creates a button with fixed visible text whose decorative leading icon follows a reader.</summary>
    [LucentComponent]
    public static ComponentRecipe Button(
        [DefaultContent] string content,
        Func<ImageSource> leadingIcon,
        Action? onInvoke = null,
        Style? style = null
    )
    {
        content = Required(content, nameof(content));
        ArgumentNullException.ThrowIfNull(leadingIcon);
        return ComposedButton(() => content, leadingIcon, onInvoke, style);
    }

    /// <summary>Creates an icon-only button with a required accessible label.</summary>
    [LucentComponent]
    public static ComponentRecipe IconButton(
        ImageSource source,
        string label,
        Action? onInvoke = null,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        label = Required(label, nameof(label));
        return IconButtonRecipe(() => source, () => label, onInvoke, style);
    }

    /// <summary>Creates an icon-only button whose source and accessible label follow typed readers.</summary>
    [LucentComponent]
    public static ComponentRecipe IconButton(
        Func<ImageSource> source,
        Func<string> label,
        Action? onInvoke = null,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(label);
        return IconButtonRecipe(source, label, onInvoke, style);
    }

    /// <summary>Creates an icon-only button with a fixed source and a label that follows a reader.</summary>
    [LucentComponent]
    public static ComponentRecipe IconButton(
        ImageSource source,
        Func<string> label,
        Action? onInvoke = null,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(label);
        return IconButtonRecipe(() => source, label, onInvoke, style);
    }

    /// <summary>Creates an icon-only button whose source follows a reader and whose label is fixed.</summary>
    [LucentComponent]
    public static ComponentRecipe IconButton(
        Func<ImageSource> source,
        string label,
        Action? onInvoke = null,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        label = Required(label, nameof(label));
        return IconButtonRecipe(source, () => label, onInvoke, style);
    }

    private static ComponentRecipe ComposedButton(
        Func<string> content,
        Func<ImageSource> leadingIcon,
        Action? onInvoke,
        Style? style
    ) =>
        ComponentRecipe.Create(
            "button",
            (context, root) =>
            {
                var label = root.Scope.Derived(
                    () => Required(content(), nameof(content)),
                    root.Name + ".button-label"
                );
                Controls.ComposedButton(root, context.Theme, label.Value, onInvoke, style);
                MountDecorativeIcon(context, root, leadingIcon);
                MountButtonText(context, root, label);
                _ = root.Scope.Effect(
                    () =>
                        root.UpdateControlSemantics(
                            new(SemanticRole.Button, label.Value, actions: SemanticAction.Invoke)
                        ),
                    root.Name + ".button"
                );
            }
        );

    private static ComponentRecipe IconButtonRecipe(
        Func<ImageSource> source,
        Func<string> label,
        Action? onInvoke,
        Style? style
    ) =>
        ComponentRecipe.Create(
            "icon-button",
            (context, root) =>
            {
                var initialSource =
                    source()
                    ?? throw new ArgumentException(
                        "An icon button source reader returned null.",
                        nameof(source)
                    );
                var accessibleLabel = root.Scope.Derived(
                    () => Required(label(), nameof(label)),
                    root.Name + ".icon-button-label"
                );
                Controls.IconButton(
                    root,
                    context.Theme,
                    initialSource,
                    accessibleLabel.Value,
                    onInvoke,
                    style
                );
                var binding = root.Scope.Own(new ImageBinding(root, initialSource));
                root.Image = binding;
                _ = root.Scope.Effect(
                    () =>
                    {
                        var nextSource =
                            source()
                            ?? throw new ArgumentException(
                                "An icon button source reader returned null.",
                                nameof(source)
                            );
                        root.UpdateControl(ImageProperties.Source, nextSource);
                        binding.SetSource(nextSource);
                        root.UpdateControlSemantics(
                            new(
                                SemanticRole.Button,
                                accessibleLabel.Value,
                                actions: SemanticAction.Invoke
                            )
                        );
                    },
                    root.Name + ".icon-button"
                );
            }
        );

    private static void MountDecorativeIcon(
        CompositionContext context,
        Element root,
        Func<ImageSource> source
    ) => _ = context.Mount(root, Icon(source));

    private static void MountButtonText(
        CompositionContext context,
        Element root,
        Derived<string> label
    ) =>
        _ = context.Mount(
            root,
            ComponentRecipe.Create(
                "button-text",
                (childContext, text) =>
                {
                    Controls.DecorativeText(text, context.Theme, label.Value);
                    _ = text.Scope.Effect(
                        () => text.UpdateControl(ProjectionProperties.Text, label.Value),
                        text.Name + ".content"
                    );
                }
            )
        );
}
