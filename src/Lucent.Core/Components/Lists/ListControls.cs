namespace Lucent.Core;

internal sealed class ListBoxBinding(
    string label,
    bool required,
    Func<int> count,
    Func<int?> selectedIndex,
    Action<int> reveal
)
{
    internal string Label { get; } = ControlState.Required(label, nameof(label));
    internal bool Required { get; } = required;
    internal Func<int> Count { get; } = count ?? throw new ArgumentNullException(nameof(count));
    internal Func<int?> SelectedIndex { get; } =
        selectedIndex ?? throw new ArgumentNullException(nameof(selectedIndex));
    internal Action<int> Reveal { get; } =
        reveal ?? throw new ArgumentNullException(nameof(reveal));
}

internal sealed class ChoiceRowBinding
{
    internal SemanticRole Role { get; init; } = SemanticRole.ListItem;
    internal Func<int?>? CollectionIndex { get; init; }
    internal required Func<string> Label { get; init; }
    internal required Func<bool> Enabled { get; init; }
    internal required Func<bool> Selected { get; init; }
    internal required Func<bool> Active { get; init; }
    internal required Func<int?> Position { get; init; }
    internal required Func<int?> Size { get; init; }
    internal required Action<BehaviorContext> Register { get; init; }
    internal required Func<BehaviorContext, bool, bool> Activate { get; init; }
    internal required Func<Key, BehaviorContext, bool> Move { get; init; }
    internal required Func<bool> Confirm { get; init; }
    internal required Func<bool> RequestOnFocus { get; init; }
    internal required Func<string, BehaviorContext, bool> Search { get; init; }
    internal Action? DismissWithoutCommit { get; init; }
    internal Action? CommitAndDismiss { get; init; }
}

internal sealed class SelectBinding
{
    internal required string Label { get; init; }
    internal required Func<string> Value { get; init; }
    internal required Func<bool> Expanded { get; init; }
    internal required Action Open { get; init; }
    internal required Action Close { get; init; }
}

internal static partial class Controls
{
    internal static void ListBox(Element element, ThemeContext theme, ListBoxBinding binding) =>
        Configure(element, theme, PanelStyle, null, new ListBoxBehavior(binding));

    internal static void ChoiceRow(
        Element element,
        ThemeContext theme,
        ChoiceRowBinding binding,
        Style? style = null
    ) =>
        Configure(
            element,
            theme,
            RowStyle
                .Set(LayoutProperties.Padding, Insets.Symmetric(8, 6))
                .Set(LayoutProperties.Spacing, 8f)
                .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Center)
                .Set(VisualProperties.CornerRadius, 4f)
                .Set(InputProperties.Cursor, CursorIntent.Pointer)
                .Bind(InputProperties.Enabled, binding.Enabled)
                .When(
                    VariantState.Hover,
                    Style.Empty.Set(VisualProperties.Background, ControlThemes.Selected)
                )
                .When(
                    VariantState.Selected,
                    Style.Empty.Set(VisualProperties.Background, ControlThemes.Selected)
                )
                .When(VariantState.FocusVisible, FocusStyle(theme))
                .When(
                    VariantState.Disabled,
                    Style.Empty.Set(
                        TypographyProperties.TextColor,
                        ControlThemes.DisabledForeground
                    )
                ),
            style,
            new ChoiceRowBehavior(binding)
        );

    internal static void SelectAnchor(
        Element element,
        ThemeContext theme,
        SelectBinding binding,
        Style? style
    ) =>
        Configure(
            element,
            theme,
            RowStyle
                .Set(LayoutProperties.Padding, Insets.Symmetric(10, 7))
                .Set(LayoutProperties.MinHeight, 36f)
                .Set(LayoutProperties.Spacing, 8f)
                .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Center)
                .Set(VisualProperties.CornerRadius, 4f)
                .Bind(
                    VisualProperties.Border,
                    () => Border.Hairline(theme.Token(ControlThemes.Border))
                )
                .Set(InputProperties.Cursor, CursorIntent.Pointer)
                .When(
                    VariantState.Hover,
                    Style.Empty.Set(VisualProperties.Background, ControlThemes.Selected)
                )
                .When(VariantState.FocusVisible, FocusStyle(theme))
                .When(
                    VariantState.Disabled,
                    Style.Empty.Set(
                        TypographyProperties.TextColor,
                        ControlThemes.DisabledForeground
                    )
                ),
            style,
            new SelectBehavior(binding)
        );

    private sealed class ListBoxBehavior(ListBoxBinding binding) : Behavior
    {
        public override string Name => "list-box";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            SemanticDeclaration Declaration() =>
                new(
                    SemanticRole.List,
                    binding.Label,
                    actions: SemanticAction.RealizeItem,
                    selection: new(false, binding.Required),
                    collection: new(binding.Count(), binding.SelectedIndex())
                );
            context.SetSemantics(Declaration());
            context.Effect(() => context.UpdateSemantics(Declaration()), "list-collection");
            context.OnSemanticCommand(command =>
            {
                if (
                    command.Kind != SemanticCommandKind.RealizeItem
                    || command.ItemIndex is not { } index
                    || index >= binding.Count()
                )
                    return false;
                binding.Reveal(index);
                return true;
            });
        }
    }

    private sealed class ChoiceRowBehavior(ChoiceRowBinding binding) : Behavior
    {
        public override string Name => "choice-row";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            context.SetSemantics(Declaration());
            context.MakeFocusable(tabStop: false);
            binding.Register(context);
            context.OnSemanticCommand(command =>
                command.Kind switch
                {
                    SemanticCommandKind.Focus => binding.Activate(context, false),
                    SemanticCommandKind.Select => Select(context),
                    _ => false,
                }
            );
            context.OnFocus(route =>
            {
                if (route.Command.Kind == FocusCommandKind.Gained && binding.RequestOnFocus())
                    _ = binding.Activate(context, true);
            });
            context.Effect(
                () =>
                {
                    var enabled = binding.Enabled();
                    var selected = binding.Selected();
                    context.SetTabStop(enabled && binding.Active());
                    context.SetState(BehaviorState.Selected, selected);
                    context.UpdateSemantics(Declaration(selected));
                },
                "choice-state"
            );
            AttachPointer(context);
            context.OnText(route =>
            {
                if (route.Command.Kind != TextInputKind.Commit)
                    return;
                route.Handled = binding.Search(route.Command.Text, context);
            });
            context.OnKey(route =>
            {
                if (route.Command.Kind != KeyCommandKind.Down)
                    return;
                if (route.Command is { IsRepeat: false, Key: Key.Enter or Key.Space })
                {
                    route.Handled = binding.CommitAndDismiss is { } commit
                        ? Commit(commit)
                        : binding.Confirm();
                    return;
                }
                if (
                    route.Command is { IsRepeat: false, Key: Key.Escape }
                    && binding.DismissWithoutCommit is { } dismiss
                )
                {
                    dismiss();
                    route.Handled = true;
                    return;
                }
                if (
                    route.Command.Key
                    is Key.Left
                        or Key.Right
                        or Key.Up
                        or Key.Down
                        or Key.Home
                        or Key.End
                )
                    route.Handled = binding.Move(route.Command.Key, context);
                else if (route.Command.Key == Key.Tab && binding.DismissWithoutCommit is { } close)
                    close();
            });

            static bool Commit(Action commit)
            {
                commit();
                return true;
            }
            bool Select(BehaviorContext behavior)
            {
                if (!binding.Activate(behavior, true))
                    return false;
                if (binding.Selected())
                    behavior.AcknowledgeSemanticSelectionApplied();
                binding.DismissWithoutCommit?.Invoke();
                return true;
            }
        }

        private void AttachPointer(BehaviorContext context)
        {
            int? armedPointer = null;
            context.OnPointer(route =>
            {
                if (
                    route.Command is
                    { Kind: PointerCommandKind.Down, Button: PointerButton.Primary }
                )
                {
                    var armed = route.Capture();
                    armedPointer = armed ? route.Command.PointerId : null;
                    context.SetState(BehaviorState.Pressed, armed);
                    if (armed)
                        route.Focus();
                    route.Handled = armed;
                    return;
                }
                if (
                    route.Command.Kind != PointerCommandKind.Cancel
                    && !route.Command.Releases(PointerButton.Primary)
                )
                    return;
                if (armedPointer != route.Command.PointerId)
                    return;
                var active = context.State.GetValueOrDefault(BehaviorState.Pressed);
                armedPointer = null;
                context.SetState(BehaviorState.Pressed, false);
                if (
                    active
                    && route.Command.Kind == PointerCommandKind.Up
                    && route.IsInsideCurrentTarget
                )
                {
                    _ = binding.Activate(context, !binding.RequestOnFocus());
                    binding.DismissWithoutCommit?.Invoke();
                }
                route.Handled = active;
            });
            context.OnCaptureLost(loss =>
            {
                if (armedPointer != loss.PointerId)
                    return;
                armedPointer = null;
                context.SetState(BehaviorState.Pressed, false);
            });
        }

        private SemanticDeclaration Declaration(bool? selected = null) =>
            new(
                binding.Role,
                ControlState.Required(binding.Label(), nameof(binding.Label)),
                enabled: binding.Enabled(),
                selected: selected ?? binding.Selected(),
                actions: SemanticAction.Select,
                positionInSet: binding.Position(),
                sizeOfSet: binding.Size(),
                collectionIndex: binding.CollectionIndex?.Invoke()
            );
    }

    private sealed class SelectBehavior(SelectBinding binding) : Behavior
    {
        public override string Name => "select";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            context.SetSemantics(Declaration());
            context.MakeFocusable();
            context.OnSemanticCommand(command =>
                command.Kind switch
                {
                    SemanticCommandKind.Focus => context
                        .CompositionInput()
                        .FocusSemantic(context.Identity),
                    SemanticCommandKind.Expand when !binding.Expanded() => Open(),
                    SemanticCommandKind.Collapse when binding.Expanded() => Close(),
                    _ => false,
                }
            );
            context.Effect(() => context.UpdateSemantics(Declaration()), "select-state");
            AttachClick(context, () => binding.Expanded() ? Close() : Open());
            context.OnKey(route =>
            {
                if (route.Command.Kind != KeyCommandKind.Down || route.Command.IsRepeat)
                    return;
                if (route.Command.Key is Key.Enter or Key.Space or Key.Down or Key.Up)
                {
                    route.Handled = binding.Expanded() ? true : Open();
                    return;
                }
                if (route.Command.Key == Key.Escape && binding.Expanded())
                    route.Handled = Close();
            });
            bool Open()
            {
                binding.Open();
                return true;
            }
            bool Close()
            {
                binding.Close();
                return true;
            }
        }

        private SemanticDeclaration Declaration() =>
            new(
                SemanticRole.ComboBox,
                binding.Label,
                actions: SemanticAction.ExpandCollapse,
                value: binding.Value(),
                expanded: binding.Expanded()
            );
    }
}
