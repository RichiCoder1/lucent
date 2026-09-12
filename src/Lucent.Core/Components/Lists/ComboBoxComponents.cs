namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Creates an editable ComboBox with generation-safe asynchronous suggestions.</summary>
    [LucentComponent]
    public static ComponentRecipe ComboBox<TKey>(
        FieldContext field,
        Func<ComboBoxSelectedItem<TKey>?> readSelectedItem,
        Action<TKey> onSelectionRequested,
        Func<string, CancellationToken, ValueTask<ComboBoxSuggestionResult<TKey>>> suggestions,
        ComboBoxOptions? options = null,
        Action<string>? onFreeTextRequested = null,
        Style? style = null
    )
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(readSelectedItem);
        ArgumentNullException.ThrowIfNull(onSelectionRequested);
        ArgumentNullException.ThrowIfNull(suggestions);
        options ??= new();
        if (
            options.SelectionPolicy == ComboBoxSelectionPolicy.AllowFreeText
            && onFreeTextRequested is null
        )
            throw new ArgumentException(
                "Free-text ComboBox policy requires an explicit callback.",
                nameof(onFreeTextRequested)
            );

        return ComponentRecipe.Create(
            "combo-box",
            (context, root) =>
            {
                var initial = ReadApplied(readSelectedItem);
                var query = root.Scope.Signal(initial?.Label ?? string.Empty, root.Name + ".query");
                var open = root.Scope.Signal(false, root.Name + ".open");
                var applied = root.Scope.Derived(
                    () => ReadApplied(readSelectedItem),
                    root.Name + ".applied"
                );
                var results = root.Scope.Async(
                    () => (Open: open.Value, Query: query.Value),
                    async (input, token) =>
                    {
                        if (!input.Open)
                            return ComboBoxSuggestionResult.Success<TKey>([]);
                        if (options.Debounce != TimeSpan.Zero)
                            await Task.Delay(options.Debounce, options.TimeProvider, token)
                                .ConfigureAwait(false);
                        var result = await suggestions(input.Query, token).ConfigureAwait(false);
                        return result
                            ?? throw new InvalidOperationException(
                                "A ComboBox suggestion provider returned null."
                            );
                    },
                    root.Name + ".suggestions"
                );
                var current = root.Scope.Derived(
                    () => CurrentSuggestions(results),
                    root.Name + ".current-suggestions"
                );
                var policy = CreatePolicy(
                    root.Scope,
                    root.Name,
                    current,
                    () =>
                        applied.Value is { } value
                            ? SelectedKey.Some(value.Key)
                            : SelectedKey.None<TKey>(),
                    onSelectionRequested
                );
                var typeAhead = root.Scope.Own(
                    new TypeAheadController<TKey>(() => current.Value, policy, options.TimeProvider)
                );
                OwnedSurfaceRequest? surface = null;
                var editorFocused = false;
                ComboBoxSelectedItem<TKey>? priorApplied = initial;

                void RestoreAppliedQuery() => query.Value = applied.Value?.Label ?? string.Empty;
                void Close(bool restore)
                {
                    policy.CancelRoving();
                    if (restore)
                        RestoreAppliedQuery();
                    open.Value = false;
                }
                void Commit()
                {
                    if (!editorFocused)
                        return;
                    if (policy.TryGetRoving(out var key) && IsEnabled(current.Value, key))
                    {
                        onSelectionRequested(key);
                        RestoreAppliedQuery();
                        Close(restore: false);
                        return;
                    }
                    if (
                        options.SelectionPolicy == ComboBoxSelectionPolicy.AllowFreeText
                        && query.Value.Length != 0
                    )
                    {
                        onFreeTextRequested!(query.Value);
                        Close(restore: false);
                    }
                }

                var binding = new ComboBoxBinding
                {
                    Label = field.AccessibleName,
                    Value = () => applied.Value?.Label ?? string.Empty,
                    Expanded = () => open.Value,
                    Open = () => open.Value = true,
                    Close = () => Close(restore: true),
                    FocusTarget = field.FocusTarget,
                };
                var editor = TextFieldControlledCore(
                    field,
                    () => query.Value,
                    value =>
                    {
                        query.Value = value;
                        policy.CancelRoving();
                        open.Value = true;
                    },
                    style: Style.Empty,
                    placeholder: field.AccessibleName,
                    enabled: null,
                    readOnly: null,
                    committed: Commit,
                    cancelled: () => Close(restore: true),
                    focusChanged: focused =>
                    {
                        editorFocused = focused;
                        if (focused)
                            open.Value = true;
                    }
                );
                ComboBoxContent(binding, editor, style).Apply(context, root);

                root.Scope.OnDispose(() => surface?.Dispose());
                _ = root.Scope.Effect(
                    () =>
                    {
                        if (results.Error is { } error)
                            throw new InvalidOperationException(
                                "Unexpected ComboBox suggestion-provider failure.",
                                error
                            );
                    },
                    root.Name + ".suggestion-failure"
                );
                _ = root.Scope.Effect(
                    () =>
                    {
                        var next = applied.Value;
                        if (Equals(next, priorApplied))
                            return;
                        priorApplied = next;
                        if (!open.Value)
                            query.Value = next?.Label ?? string.Empty;
                    },
                    root.Name + ".applied-query"
                );
                _ = root.Scope.Effect(
                    () =>
                    {
                        if (open.Value)
                        {
                            _ = results.IsPending;
                            if (surface is not null)
                                return;
                            var anchorWidth = root
                                .Composition.Input.SurfaceAnchor(
                                    new(root.Composition.Epoch, root.Id)
                                )
                                ?.Width;
                            var popup = ComboBoxPopup(
                                field.AccessibleName,
                                results,
                                current,
                                policy,
                                typeAhead,
                                onSelectionRequested,
                                () => Close(restore: true),
                                options.SelectionPolicy
                                    == ComboBoxSelectionPolicy.SelectionRequired,
                                Math.Max(160, anchorWidth ?? 0)
                            );
                            surface = new OwnedSurfaceRequest(
                                root,
                                context.Theme,
                                popup,
                                interactive: true,
                                consumeOutsideClick: true,
                                closed: () => Close(restore: true)
                            );
                            root.Composition.Input.RequestSurface(surface);
                        }
                        else
                        {
                            surface?.Dispose();
                            surface = null;
                        }
                    },
                    root.Name + ".surface"
                );
            }
        );
    }

    [LucentComponent]
    internal static ComponentRecipe ComboBoxHost(
        ComboBoxBinding binding,
        [DefaultContent] ComponentContent content,
        Style? style = null
    ) =>
        Host(
            "combo-box-frame",
            content,
            (context, root) => Controls.ComboBox(root, context.Theme, binding, style)
        );

    private static ComponentRecipe ComboBoxPopup<TKey>(
        string label,
        AsyncValue<ComboBoxSuggestionResult<TKey>> results,
        Derived<ChoiceItem<TKey>[]> current,
        KeyedSelectionPolicy<TKey, ChoiceItem<TKey>> policy,
        TypeAheadController<TKey> typeAhead,
        Action<TKey> request,
        Action dismiss,
        bool required,
        float minimumWidth
    )
        where TKey : notnull
    {
        var list = ComponentRecipe.Create(
            "combo-box-list",
            (context, root) =>
                MountListBox(
                    context,
                    root,
                    label,
                    current,
                    policy,
                    typeAhead,
                    request,
                    ListBoxSelectionMode.ExplicitConfirmation,
                    DefaultChoiceRowHeight,
                    Style
                        .Empty.MinWidth(minimumWidth)
                        .Bind(
                            LayoutProperties.Height,
                            () => Math.Min(240, current.Value.Length * DefaultChoiceRowHeight)
                        )
                        .MaxHeight(240),
                    null,
                    required,
                    dismiss
                )
        );
        var loading = ContentRecipe.When(
            "combo-box.loading",
            () => SuggestionState(results) == ComboBoxSuggestionState.Loading,
            Components.Status("Loading suggestions")
        );
        var empty = ContentRecipe.When(
            "combo-box.empty",
            () => SuggestionState(results) == ComboBoxSuggestionState.Empty,
            Components.Status("No results")
        );
        var failed = ContentRecipe.When(
            "combo-box.failed",
            () => SuggestionState(results) == ComboBoxSuggestionState.Failed,
            Components.Status(() => results.Value!.Failure!)
        );
        return Components.Column([loading, empty, failed, list]);
    }

    private static ComboBoxSelectedItem<TKey>? ReadApplied<TKey>(
        Func<ComboBoxSelectedItem<TKey>?> read
    )
        where TKey : notnull => read();

    private static ChoiceItem<TKey>[] CurrentSuggestions<TKey>(
        AsyncValue<ComboBoxSuggestionResult<TKey>> results
    )
        where TKey : notnull
    {
        var state = SuggestionState(results);
        return state is ComboBoxSuggestionState.Ready ? results.Value!.Items.ToArray() : [];
    }

    private static ComboBoxSuggestionState SuggestionState<TKey>(
        AsyncValue<ComboBoxSuggestionResult<TKey>> results
    )
        where TKey : notnull
    {
        if (results.IsPending || !results.HasValue)
            return ComboBoxSuggestionState.Loading;
        var value = results.Value!;
        if (value.IsFailure)
            return ComboBoxSuggestionState.Failed;
        return value.Items.Count == 0
            ? ComboBoxSuggestionState.Empty
            : ComboBoxSuggestionState.Ready;
    }

    private static bool IsEnabled<TKey>(ChoiceItem<TKey>[] items, TKey key)
        where TKey : notnull
    {
        foreach (var item in items)
            if (EqualityComparer<TKey>.Default.Equals(item.Key, key))
                return item.Enabled;
        return false;
    }
}
