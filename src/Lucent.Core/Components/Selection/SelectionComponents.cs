namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Creates a controlled checkbox with distinct Off, On, and Mixed applied states.</summary>
    [LucentComponent]
    public static ComponentRecipe CheckBox(
        string label,
        Func<CheckState> readCheckState,
        Action<CheckState> onCheckRequested,
        CheckStateCycle cycle = CheckStateCycle.Binary,
        Style? style = null
    ) =>
        ToggleSelectionContent(
            new(label, SemanticRole.CheckBox, readCheckState, onCheckRequested, cycle),
            style
        );

    /// <summary>Creates a controlled binary switch for an immediate setting.</summary>
    [LucentComponent]
    public static ComponentRecipe Switch(
        string label,
        Func<bool> readBool,
        Action<bool> onToggleRequested,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(readBool);
        ArgumentNullException.ThrowIfNull(onToggleRequested);
        return ToggleSelectionContent(
            new(
                label,
                SemanticRole.Switch,
                () => readBool() ? CheckState.On : CheckState.Off,
                state => onToggleRequested(state == CheckState.On),
                CheckStateCycle.Binary
            ),
            style
        );
    }

    /// <summary>Creates a controlled, keyed radio group with one roving tab stop.</summary>
    [LucentComponent]
    public static ComponentRecipe RadioGroup<TKey>(
        string label,
        Func<IEnumerable<RadioOption<TKey>>> items,
        Func<TKey> readSelectedKey,
        Action<TKey> onSelectionRequested,
        RadioSelectionRequirement requirement = RadioSelectionRequirement.Required,
        Style? style = null
    )
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(readSelectedKey);
        return RadioGroup(
            label,
            items,
            () => SelectedKey.Some(readSelectedKey()),
            onSelectionRequested,
            requirement,
            style
        );
    }

    /// <summary>Creates a controlled, keyed radio group whose applied state can explicitly be empty.</summary>
    [LucentComponent]
    public static ComponentRecipe RadioGroup<TKey>(
        string label,
        Func<IEnumerable<RadioOption<TKey>>> items,
        Func<SelectedKey<TKey>> readSelectedKey,
        Action<TKey> onSelectionRequested,
        RadioSelectionRequirement requirement = RadioSelectionRequirement.Optional,
        Style? style = null
    )
        where TKey : notnull
    {
        label = Required(label, nameof(label));
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(readSelectedKey);
        ArgumentNullException.ThrowIfNull(onSelectionRequested);
        if (!Enum.IsDefined(requirement))
            throw new ArgumentOutOfRangeException(nameof(requirement));

        return ComponentRecipe.Create(
            "radio-group",
            (context, root) =>
            {
                var currentItems = root.Scope.Derived(
                    () => SnapshotOptions(items()),
                    root.Name + ".items"
                );
                var policy = new KeyedSelectionPolicy<TKey, RadioOption<TKey>>(
                    root.Scope,
                    root.Name,
                    () => currentItems.Value,
                    item => item.Key,
                    item => item.Enabled,
                    readSelectedKey,
                    onSelectionRequested
                );
                var optionContent = ContentRecipe.ForEach(
                    root.Name + ".options",
                    () => currentItems.Value,
                    item => item.Key,
                    current =>
                    {
                        var binding = new RadioOptionBinding
                        {
                            Label = () => current.Value.Label,
                            Enabled = () => current.Value.Enabled,
                            Selected = () => policy.IsApplied(current.Value.Key),
                            Roving = () => policy.IsRoving(current.Value.Key),
                            Register = behavior =>
                                policy.RegisterTarget(current.Value.Key, behavior),
                            Activate = (behavior, request) =>
                            {
                                var key = current.Value.Key;
                                return policy.Activate(key, request) && policy.Focus(key, behavior);
                            },
                            Move = policy.Move,
                        };
                        return RadioOptionContent(binding);
                    }
                );
                RadioGroupContent(new RadioGroupBinding(label, requirement), [optionContent], style)
                    .Apply(context, root);
            }
        );
    }

    [LucentComponent]
    internal static ComponentRecipe SelectionToggleHost(
        ToggleSelectionBinding binding,
        [DefaultContent] ComponentContent content,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(binding);
        content = Content(content);
        return ComponentRecipe.Create(
            "selection-toggle",
            (context, root) =>
            {
                Controls.ToggleSelection(root, context.Theme, binding, style);
                context.Mount(root, content);
            }
        );
    }

    [LucentComponent]
    internal static ComponentRecipe RadioGroupHost(
        RadioGroupBinding binding,
        [DefaultContent] ComponentContent content,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(binding);
        content = Content(content);
        return ComponentRecipe.Create(
            "radio-group",
            (context, root) =>
            {
                Controls.RadioGroup(root, context.Theme, binding, style);
                context.Mount(root, content);
            }
        );
    }

    [LucentComponent]
    internal static ComponentRecipe RadioOptionHost(
        RadioOptionBinding binding,
        [DefaultContent] ComponentContent content
    )
    {
        ArgumentNullException.ThrowIfNull(binding);
        content = Content(content);
        return ComponentRecipe.Create(
            "radio-option",
            (context, root) =>
            {
                Controls.RadioOption(root, context.Theme, binding);
                context.Mount(root, content);
            }
        );
    }

    [LucentComponent]
    internal static ComponentRecipe SelectionDecoration(Func<string> content, Style? style = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return ComponentRecipe.Create(
            "selection-decoration",
            (context, root) =>
            {
                var text = root.Scope.Derived(
                    () => Required(content(), nameof(content)),
                    root.Name + ".text"
                );
                root.Present(
                    context.Theme,
                    Style.Empty.Set(ProjectionProperties.Text, text.Value),
                    style
                );
                _ = root.Scope.Effect(
                    () => root.UpdateControl(ProjectionProperties.Text, text.Value),
                    root.Name + ".projection"
                );
            }
        );
    }

    private static RadioOption<TKey>[] SnapshotOptions<TKey>(IEnumerable<RadioOption<TKey>> source)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = source.ToArray();
        if (result.Any(static item => item is null))
            throw new ArgumentException(
                "Radio group options cannot contain null entries.",
                nameof(source)
            );
        var keys = new HashSet<TKey>();
        foreach (var item in result)
        {
            if (!keys.Add(item.Key))
                throw new ArgumentException(
                    "Radio group option keys must be unique.",
                    nameof(source)
                );
        }
        return result;
    }
}
