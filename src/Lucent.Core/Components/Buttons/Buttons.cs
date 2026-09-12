namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Creates a button labeled with the supplied content. Use <paramref name="onInvoke"/> to respond when the user activates it.</summary>
    [LucentComponent]
    public static ComponentRecipe Button(
        [DefaultContent] string content,
        Action? onInvoke = null,
        Style? style = null
    )
    {
        content = Required(content, nameof(content));
        return ComponentRecipe.Create(
            "button",
            (context, root) => Controls.Button(root, context.Theme, content, onInvoke, style)
        );
    }

    /// <summary>Creates a button whose label follows the supplied reader. Use <paramref name="onInvoke"/> to respond when the user activates it.</summary>
    [LucentComponent]
    public static ComponentRecipe Button(
        [DefaultContent] Func<string> content,
        Action? onInvoke = null,
        Style? style = null
    ) => ButtonCore(content, onInvoke, style, focusOnPointer: true);

    private static ComponentRecipe ButtonCore(
        Func<string> content,
        Action? onInvoke,
        Style? style,
        bool focusOnPointer
    )
    {
        ArgumentNullException.ThrowIfNull(content);
        return ComponentRecipe.Create(
            "button",
            (context, root) =>
            {
                var label = root.Scope.Derived(
                    () => Required(content(), nameof(content)),
                    root.Name + ".button-label"
                );
                Controls.Button(root, context.Theme, label.Value, onInvoke, style, focusOnPointer);
                _ = root.Scope.Effect(
                    () =>
                    {
                        var nextLabel = label.Value;
                        root.UpdateControl(ProjectionProperties.Text, nextLabel);
                        root.UpdateControlSemantics(
                            new(SemanticRole.Button, nextLabel, actions: SemanticAction.Invoke)
                        );
                    },
                    root.Name + ".button"
                );
            }
        );
    }

    /// <summary>Creates a selectable list item with fixed text. Use <paramref name="onSelect"/> to respond when it is selected.</summary>
    [LucentComponent]
    public static ComponentRecipe Selectable(
        [DefaultContent] string content,
        Action? onSelect = null,
        Style? style = null
    )
    {
        content = Required(content, nameof(content));
        return ComponentRecipe.Create(
            "selectable",
            (context, root) => Controls.Selectable(root, context.Theme, content, onSelect, style)
        );
    }

    /// <summary>Creates a selectable list item whose text and selected state follow the supplied readers.</summary>
    [LucentComponent]
    public static ComponentRecipe Selectable(
        [DefaultContent] Func<string> content,
        Func<bool> selected,
        Action? onSelect = null,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(selected);
        return ComponentRecipe.Create(
            "selectable",
            (context, root) =>
            {
                var label = root.Scope.Derived(
                    () => Required(content(), nameof(content)),
                    root.Name + ".selectable-label"
                );
                var isSelected = root.Scope.Derived(selected, root.Name + ".selectable-selected");
                var state = Controls.Selectable(
                    root,
                    context.Theme,
                    label.Value,
                    onSelect,
                    style,
                    controlled: true
                );
                _ = root.Scope.Effect(
                    () =>
                    {
                        var nextLabel = label.Value;
                        var nextSelected = isSelected.Value;
                        state.Label = nextLabel;
                        state.Selected = nextSelected;
                    },
                    root.Name + ".selectable"
                );
            }
        );
    }

    /// <summary>Creates a selectable list item with composed visual content and a live accessible label and selected state.</summary>
    [LucentComponent]
    public static ComponentRecipe Selectable(
        [DefaultContent] ComponentContent content,
        Func<string> label,
        Func<bool> selected,
        Action? onSelect = null,
        Style? style = null
    )
    {
        content = Content(content);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(selected);
        return ComponentRecipe.Create(
            "selectable",
            (context, root) =>
            {
                var accessibleLabel = root.Scope.Derived(
                    () => Required(label(), nameof(label)),
                    root.Name + ".selectable-label"
                );
                var isSelected = root.Scope.Derived(selected, root.Name + ".selectable-selected");
                var state = Controls.ComposedSelectable(
                    root,
                    context.Theme,
                    accessibleLabel.Value,
                    onSelect,
                    style,
                    controlled: true
                );
                _ = root.Scope.Effect(
                    () =>
                    {
                        state.Label = accessibleLabel.Value;
                        state.Selected = isSelected.Value;
                    },
                    root.Name + ".selectable"
                );
                context.Mount(root, content);
            }
        );
    }
}
