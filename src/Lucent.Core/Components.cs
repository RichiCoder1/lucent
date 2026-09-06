namespace Lucent.Core;

/// <summary>Built-in component recipes for ordinary typed composition.</summary>
public static class Components
{
    /// <summary>Creates a horizontal container for the supplied content. Use it to place child components in a row.</summary>
    [LucentComponent]
    public static ComponentRecipe Row(
        [DefaultContent] ComponentContent content,
        Style? style = null
    )
    {
        content = Content(content);
        return ComponentRecipe.Create(
            "row",
            (context, root) =>
            {
                Controls.Row(root, context.Theme, "Row", style);
                context.Mount(root, content);
            }
        );
    }

    /// <summary>Creates a vertical container for the supplied content. Use it to stack child components in a column.</summary>
    [LucentComponent]
    public static ComponentRecipe Column(
        [DefaultContent] ComponentContent content,
        Style? style = null
    )
    {
        content = Content(content);
        return ComponentRecipe.Create(
            "column",
            (context, root) =>
            {
                Controls.Column(root, context.Theme, "Column", style);
                context.Mount(root, content);
            }
        );
    }

    /// <summary>Creates a container that publishes its assigned logical content constraints to a hoistable reader.</summary>
    [LucentComponent]
    public static ComponentRecipe ResponsiveContainer(
        [DefaultContent] ComponentContent content,
        ResponsiveConstraints constraints,
        Style? style = null
    )
    {
        content = Content(content);
        ArgumentNullException.ThrowIfNull(constraints);
        return ComponentRecipe.Create(
            "responsive-container",
            (context, root) =>
            {
                constraints.AcquireMount(root.Scope);
                root.Present(
                    context.Theme,
                    author: Style.Compose(
                        Style
                            .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                            .Set(LayoutProperties.MainGrow, 1f),
                        style ?? Style.Empty
                    )
                );
                root.UpdateControl(ProjectionProperties.ResponsiveConstraints, constraints);
                context.Mount(root, content);
            }
        );
    }

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

    /// <summary>Creates a single-line text editor. Supply <paramref name="session"/> to retain its document state across mounts; otherwise <paramref name="initialValue"/> seeds mount-owned state.</summary>
    [LucentComponent]
    public static ComponentRecipe TextField(
        string initialValue = "",
        Action<string>? onChange = null,
        Style? style = null,
        string label = "Text field",
        EditorSession? session = null,
        FocusTarget? focusTarget = null
    )
    {
        TextFieldState.ValidateText(initialValue);
        if (session is not null && initialValue.Length != 0)
            throw new ArgumentException(
                "Initial text is owned by the supplied editor session.",
                nameof(initialValue)
            );
        label = Required(label, nameof(label));
        return ComponentRecipe.Create(
            "text-field",
            (context, root) =>
            {
                var state = Controls.TextField(
                    root,
                    context.Theme,
                    label,
                    initialValue,
                    style,
                    session,
                    focusTarget
                );
                if (onChange is not null)
                {
                    var prior = state.Value;
                    _ = root.Scope.Effect(
                        () =>
                        {
                            var value = state.Value;
                            if (value != prior)
                            {
                                prior = value;
                                onChange(value);
                            }
                        },
                        root.Name + ".on-change"
                    );
                }
            }
        );
    }

    /// <summary>Creates a multiline text editor. Supply <paramref name="session"/> to retain its document state across mounts.</summary>
    [LucentComponent]
    public static ComponentRecipe TextArea(
        string initialValue = "",
        Action<string>? onChange = null,
        Style? style = null,
        string label = "Text area",
        EditorSession? session = null,
        FocusTarget? focusTarget = null
    )
    {
        TextFieldState.ValidateMultilineText(initialValue);
        if (session is not null && initialValue.Length != 0)
            throw new ArgumentException(
                "Initial text is owned by the supplied editor session.",
                nameof(initialValue)
            );
        label = Required(label, nameof(label));
        return ComponentRecipe.Create(
            "text-area",
            (context, root) =>
            {
                var state = Controls.TextArea(
                    root,
                    context.Theme,
                    label,
                    initialValue,
                    style,
                    session,
                    focusTarget
                );
                if (onChange is not null)
                {
                    var prior = state.Value;
                    _ = root.Scope.Effect(
                        () =>
                        {
                            var value = state.Value;
                            if (value != prior)
                            {
                                prior = value;
                                onChange(value);
                            }
                        },
                        root.Name + ".on-change"
                    );
                }
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
                var state = Controls.Selectable(root, context.Theme, label.Value, onSelect, style);
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

    /// <summary>Creates a scrollable viewport that clips its content. Supply <paramref name="viewport"/> to retain its offset across mounts.</summary>
    [LucentComponent]
    public static ComponentRecipe ScrollViewport(
        [DefaultContent] ComponentContent content,
        string label = "Scroll viewport",
        Style? style = null,
        ViewportState? viewport = null
    )
    {
        content = Content(content);
        label = Required(label, nameof(label));
        return ComponentRecipe.Create(
            "scroll-viewport",
            (context, root) =>
            {
                Controls.ScrollViewport(
                    root,
                    context.Theme,
                    label,
                    style: style,
                    viewport: viewport
                );
                context.Mount(root, content);
            }
        );
    }

    /// <summary>Creates a scrollable list with a fixed row height. Use it for large collections so only rows near the viewport are kept active.</summary>
    [LucentComponent]
    public static ComponentRecipe VirtualizedList<TKey, TItem>(
        Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key,
        Func<CurrentItem<TItem>, ComponentRecipe> row,
        Func<float> rowHeight,
        string label = "Items",
        Style? style = null,
        ViewportState? viewport = null
    )
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(rowHeight);
        label = Required(label, nameof(label));
        return ComponentRecipe.Create(
            "virtualized-list",
            (context, root) =>
            {
                var rowHeightValue = root.Scope.Derived(
                    () => RowHeight(rowHeight),
                    root.Name + ".row-height-read"
                );
                var height = rowHeightValue.Value;
                var scroll = Controls.ScrollViewport(
                    root,
                    context.Theme,
                    label,
                    style: style,
                    viewport: viewport
                );
                var region = context.Virtualize(
                    root,
                    "rows",
                    source,
                    key,
                    (item, child) =>
                    {
                        var recipe = row(item);
                        ArgumentNullException.ThrowIfNull(recipe);
                        return recipe.Mount(child);
                    },
                    height
                );
                try
                {
                    Controls.List(region.Region, context.Theme, label);
                    region.Configure();
                }
                catch
                {
                    region.Dispose();
                    throw;
                }
                _ = root.Scope.Effect(
                    () =>
                    {
                        var next = rowHeightValue.Value;
                        if (next == height)
                            return;
                        var index = MathF.Floor(scroll.Offset.Y / height);
                        var relative = scroll.Offset.Y - index * height;
                        height = next;
                        region.SetRowHeight(next);
                        scroll.Offset = new(scroll.Offset.X, index * next + relative);
                    },
                    root.Name + ".row-height"
                );
            }
        );
    }

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

    private static ComponentContent Content(ComponentContent content) =>
        content ?? throw new ArgumentNullException(nameof(content));

    private static string Required(string value, string parameter) =>
        ControlState.Required(value, parameter);

    private static float RowHeight(Func<float> read)
    {
        var value = read();
        if (!float.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(nameof(read));
        return value;
    }

    private static float ProgressValue(Func<float> read)
    {
        var result = read();
        ControlState.ValidateProgress(result);
        return result;
    }
}
