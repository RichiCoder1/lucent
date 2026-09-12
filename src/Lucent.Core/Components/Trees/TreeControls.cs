namespace Lucent.Core;

internal sealed class TreeBinding(
    string label,
    Func<int> count,
    Func<int?> selectedIndex,
    Action<int> reveal
)
{
    internal string Label { get; } = ControlState.Required(label, nameof(label));
    internal Func<int> Count { get; } = count ?? throw new ArgumentNullException(nameof(count));
    internal Func<int?> SelectedIndex { get; } =
        selectedIndex ?? throw new ArgumentNullException(nameof(selectedIndex));
    internal Action<int> Reveal { get; } =
        reveal ?? throw new ArgumentNullException(nameof(reveal));
}

internal sealed class TreeRowBinding
{
    internal required Func<string> Label { get; init; }
    internal required Func<bool> Selected { get; init; }
    internal required Func<bool> Active { get; init; }
    internal required Func<bool> Enabled { get; init; }
    internal required Func<bool> HasChildren { get; init; }
    internal required Func<bool> Expanded { get; init; }
    internal required Func<int> Level { get; init; }
    internal required Func<int> Position { get; init; }
    internal required Func<int> Size { get; init; }
    internal required Func<int> CollectionIndex { get; init; }
    internal required Action<BehaviorContext, FocusTarget> Register { get; init; }
    internal required Func<BehaviorContext, bool, bool> Activate { get; init; }
    internal required Func<Key, BehaviorContext, bool> Navigate { get; init; }
    internal required Func<bool, bool> RequestExpanded { get; init; }
    internal required Func<bool> Confirm { get; init; }
    internal required Func<bool> RequestOnFocus { get; init; }
    internal FocusTarget? FocusTarget { get; set; }
    internal ElementIdentity? Chevron { get; set; }
}

internal static partial class Controls
{
    internal static void Tree(
        Element element,
        ThemeContext theme,
        TreeBinding binding,
        Style? style
    )
    {
        Configure(element, theme, PanelStyle, style, new TreeBehavior(binding));
    }

    internal static void TreeRow(
        Element element,
        ThemeContext theme,
        TreeRowBinding binding,
        Style? style
    ) =>
        Configure(
            element,
            theme,
            RowStyle
                .Bind(
                    LayoutProperties.Padding,
                    () => new Insets(8 + (binding.Level() - 1) * 16, 6, 8, 6)
                )
                .Set(LayoutProperties.Spacing, 6f)
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
            new TreeRowBehavior(binding)
        );

    internal static void TreeChevron(
        Element element,
        ThemeContext theme,
        TreeRowBinding binding,
        Style? style
    )
    {
        var initialSource = binding.Expanded()
            ? NavigationArtwork.Expanded
            : NavigationArtwork.Collapsed;
        Controls.Image(element, theme, initialSource, null, decorative: true, icon: true, style);
        var image = element.Scope.Own(new ImageBinding(element, initialSource));
        element.Image = image;
        var identity = new ElementIdentity(element.Composition.Epoch, element.Id);
        binding.Chevron = identity;
        element.Scope.OnDispose(() =>
        {
            if (binding.Chevron == identity)
                binding.Chevron = null;
        });
        _ = element.Scope.Effect(
            () =>
            {
                var source = binding.Expanded()
                    ? NavigationArtwork.Expanded
                    : NavigationArtwork.Collapsed;
                element.UpdateControl(ImageProperties.Source, source);
                image.SetSource(source);
                element.UpdateControl(
                    VisualProperties.Participation,
                    binding.HasChildren()
                        ? ElementParticipation.Visible
                        : ElementParticipation.Hidden
                );
            },
            element.Name + ".tree-chevron-state"
        );
    }

    private sealed class TreeBehavior(TreeBinding binding) : Behavior
    {
        public override string Name => "tree";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            SemanticDeclaration Declaration() =>
                new(
                    SemanticRole.Tree,
                    binding.Label,
                    actions: SemanticAction.RealizeItem,
                    selection: new(false, false),
                    collection: new(binding.Count(), binding.SelectedIndex())
                );
            context.SetSemantics(Declaration());
            context.Effect(() => context.UpdateSemantics(Declaration()), "tree-collection");
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

    private sealed class TreeRowBehavior(TreeRowBinding binding) : Behavior
    {
        public override string Name => "tree-item";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            context.SetSemantics(Declaration());
            context.MakeFocusable(tabStop: false);
            var focusTarget =
                binding.FocusTarget
                ?? throw new InvalidOperationException("Tree row focus target is unavailable.");
            context.RegisterFocusTarget(focusTarget);
            binding.Register(context, focusTarget);
            context.OnSemanticCommand(command =>
                command.Kind switch
                {
                    SemanticCommandKind.Focus => binding.Activate(context, false),
                    SemanticCommandKind.Select => binding.Activate(context, true),
                    SemanticCommandKind.Expand
                        when binding.Enabled() && binding.HasChildren() && !binding.Expanded() =>
                        binding.RequestExpanded(true),
                    SemanticCommandKind.Collapse
                        when binding.Enabled() && binding.HasChildren() && binding.Expanded() =>
                        binding.RequestExpanded(false),
                    _ => false,
                }
            );
            context.OnFocus(route =>
            {
                if (route.Command.Kind == FocusCommandKind.Gained)
                {
                    if (binding.RequestOnFocus())
                        _ = binding.Activate(context, true);
                }
            });
            context.Effect(
                () =>
                {
                    var selected = binding.Selected();
                    context.SetTabStop(binding.Enabled() && binding.Active());
                    context.SetState(BehaviorState.Selected, selected);
                    context.UpdateSemantics(Declaration(selected));
                },
                "tree-item-state"
            );
            AttachPointer(context);
            context.OnKey(route =>
            {
                if (route.Command.Kind != KeyCommandKind.Down)
                    return;
                if (route.Command is { IsRepeat: false, Key: Key.Enter or Key.Space })
                    route.Handled = binding.Confirm();
                else if (
                    route.Command.Key
                    is Key.Left
                        or Key.Right
                        or Key.Up
                        or Key.Down
                        or Key.Home
                        or Key.End
                )
                    route.Handled = binding.Navigate(route.Command.Key, context);
            });
        }

        private SemanticDeclaration Declaration(bool? selected = null) =>
            new(
                SemanticRole.TreeItem,
                ControlState.Required(binding.Label(), nameof(binding.Label)),
                enabled: binding.Enabled(),
                selected: selected ?? binding.Selected(),
                actions: SemanticAction.Select
                    | (
                        binding.Enabled() && binding.HasChildren()
                            ? SemanticAction.ExpandCollapse
                            : SemanticAction.None
                    ),
                expanded: binding.Enabled() && binding.HasChildren() ? binding.Expanded() : null,
                positionInSet: binding.Position(),
                sizeOfSet: binding.Size(),
                level: binding.Level(),
                collectionIndex: binding.CollectionIndex()
            );

        private void AttachPointer(BehaviorContext context)
        {
            int? armedPointer = null;
            var armedChevron = false;
            context.OnPointer(route =>
            {
                if (
                    route.Command is
                    { Kind: PointerCommandKind.Down, Button: PointerButton.Primary }
                )
                {
                    var armed = route.Capture();
                    armedPointer = armed ? route.Command.PointerId : null;
                    armedChevron =
                        armed
                        && binding.Chevron is { } chevron
                        && route.Target == chevron
                        && binding.Enabled()
                        && binding.HasChildren();
                    context.SetState(BehaviorState.Pressed, armed && !armedChevron);
                    if (armed && !armedChevron)
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
                var expand = armedChevron;
                armedPointer = null;
                armedChevron = false;
                context.SetState(BehaviorState.Pressed, false);
                if (expand)
                {
                    route.Handled = true;
                    if (
                        route.Command.Kind == PointerCommandKind.Up
                        && binding.Chevron is { } chevron
                        && context
                            .CompositionInput()
                            .Contains(chevron, route.Command.X, route.Command.Y)
                    )
                        _ = binding.RequestExpanded(!binding.Expanded());
                    return;
                }
                if (
                    active
                    && route.Command.Kind == PointerCommandKind.Up
                    && route.IsInsideCurrentTarget
                )
                    _ = binding.Activate(context, !binding.RequestOnFocus());
                route.Handled = active;
            });
            context.OnCaptureLost(loss =>
            {
                if (armedPointer != loss.PointerId)
                    return;
                armedPointer = null;
                armedChevron = false;
                context.SetState(BehaviorState.Pressed, false);
            });
        }
    }
}
