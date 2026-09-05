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

    /// <summary>Creates a single-line text editor. Use <paramref name="initialValue"/> for its starting text and <paramref name="onChange"/> to observe committed edits.</summary>
    [LucentComponent]
    public static ComponentRecipe TextField(
        string initialValue = "",
        Action<string>? onChange = null,
        Style? style = null,
        string label = "Text field"
    )
    {
        TextFieldState.ValidateText(initialValue);
        label = Required(label, nameof(label));
        return ComponentRecipe.Create(
            "text-field",
            (context, root) =>
            {
                var state = Controls.TextField(root, context.Theme, label, initialValue, style);
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

    /// <summary>Creates a scrollable viewport that clips its content. Use it when content can be larger than the available space.</summary>
    [LucentComponent]
    public static ComponentRecipe ScrollViewport(
        [DefaultContent] ComponentContent content,
        string label = "Scroll viewport",
        Style? style = null
    )
    {
        content = Content(content);
        label = Required(label, nameof(label));
        return ComponentRecipe.Create(
            "scroll-viewport",
            (context, root) =>
            {
                Controls.ScrollViewport(root, context.Theme, label, style: style);
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
        Style? style = null
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
                var scroll = Controls.ScrollViewport(root, context.Theme, label, style: style);
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
