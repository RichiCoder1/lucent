namespace Lucent.Core;

internal sealed class TabListBinding(string label)
{
    internal string Label { get; } = ControlState.Required(label, nameof(label));
}

internal sealed class TabHeaderBinding
{
    internal required Func<string> Label { get; init; }
    internal required Func<bool> Enabled { get; init; }
    internal required Func<bool> Selected { get; init; }
    internal required Func<bool> Roving { get; init; }
    internal required Action<BehaviorContext> Register { get; init; }
    internal required Func<BehaviorContext, bool, bool> Activate { get; init; }
    internal required Func<Key, BehaviorContext, bool> Move { get; init; }
    internal required Func<bool> Confirm { get; init; }
}

internal sealed class TabPanelBinding(Func<bool> active)
{
    internal Func<bool> Active { get; } = active ?? throw new ArgumentNullException(nameof(active));
}

internal sealed class DisclosureBinding(string heading, Func<bool> expanded, Action<bool> request)
{
    internal string Heading { get; } = ControlState.Required(heading, nameof(heading));
    internal Func<bool> Expanded { get; } =
        expanded ?? throw new ArgumentNullException(nameof(expanded));
    internal Action<bool> Request { get; } =
        request ?? throw new ArgumentNullException(nameof(request));
    internal Element? Panel { get; set; }
}

internal static partial class Controls
{
    internal static void TabList(
        Element element,
        ThemeContext theme,
        TabListBinding binding,
        Style? style
    )
    {
        var component = RowStyle
            .With(ScrollBarStyle)
            .Set(LayoutProperties.Spacing, 2f)
            .Set(LayoutProperties.Clip, true)
            .Set(LayoutProperties.Scroll, default(ScrollOffset));
        Preflight(element, theme, component, style, new TabListBehavior(binding, null!));
        var state = new ScrollViewportState(element.Scope, element.Name + ".scroll", default);
        Configure(element, theme, component, style, new TabListBehavior(binding, state));
        Bind(element, state, value => element.UpdateControl(LayoutProperties.Scroll, value.Offset));
    }

    internal static void TabHeader(Element element, ThemeContext theme, TabHeaderBinding binding) =>
        Configure(
            element,
            theme,
            PanelStyle
                .Set(LayoutProperties.Padding, Insets.Symmetric(10, 6))
                .Set(LayoutProperties.MinHeight, 32f)
                .Set(VisualProperties.CornerRadius, 4f)
                .Set(InputProperties.Cursor, CursorIntent.Pointer)
                .Bind(InputProperties.Enabled, binding.Enabled)
                .Bind(TypographyProperties.TextColor, () => theme.Token(ControlThemes.Foreground))
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
            null,
            new TabHeaderBehavior(binding)
        );

    internal static void DisclosureHeader(
        Element element,
        ThemeContext theme,
        DisclosureBinding binding,
        Style? style
    ) =>
        Configure(
            element,
            theme,
            RowStyle
                .Set(LayoutProperties.Padding, Insets.Symmetric(8, 6))
                .Set(LayoutProperties.MinHeight, 32f)
                .Set(LayoutProperties.Spacing, 8f)
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
            new DisclosureBehavior(binding)
        );

    private sealed class TabListBehavior(TabListBinding binding, ScrollViewportState state)
        : Behavior
    {
        public override string Name => "tab-list";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            context.SetSemantics(
                new(
                    SemanticRole.TabList,
                    binding.Label,
                    actions: SemanticAction.Scroll,
                    selection: new(false, true)
                )
            );
            context.MakeFocusable();
            context.RegisterScrollable(state);
            context.OnSemanticCommand(command =>
                command.Kind switch
                {
                    SemanticCommandKind.Focus => context
                        .CompositionInput()
                        .FocusSemantic(context.Identity),
                    SemanticCommandKind.Scroll => context
                        .CompositionInput()
                        .ScrollSemantic(context.Identity, command),
                    _ => false,
                }
            );
        }
    }

    private sealed class TabHeaderBehavior(TabHeaderBinding binding) : Behavior
    {
        public override string Name => "tab";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            context.SetSemantics(Declaration());
            context.MakeFocusable(false);
            binding.Register(context);
            context.OnSemanticCommand(command =>
                command.Kind switch
                {
                    SemanticCommandKind.Focus => binding.Activate(context, false),
                    SemanticCommandKind.Select => binding.Activate(context, true),
                    _ => false,
                }
            );
            context.Effect(
                () =>
                {
                    var selected = binding.Selected();
                    context.SetTabStop(binding.Enabled() && binding.Roving());
                    context.SetState(BehaviorState.Selected, selected);
                    context.UpdateSemantics(Declaration(selected));
                },
                "tab-state"
            );
            AttachClick(context, request: () => binding.Activate(context, true));
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
                    route.Handled = binding.Move(route.Command.Key, context);
            });
        }

        private SemanticDeclaration Declaration(bool? selected = null) =>
            new(
                SemanticRole.Tab,
                ControlState.Required(binding.Label(), nameof(binding.Label)),
                enabled: binding.Enabled(),
                selected: selected ?? binding.Selected(),
                actions: SemanticAction.Select
            );
    }

    private sealed class DisclosureBehavior(DisclosureBinding binding) : Behavior
    {
        public override string Name => "disclosure";
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
                    SemanticCommandKind.Expand when !binding.Expanded() => Request(true),
                    SemanticCommandKind.Collapse when binding.Expanded() => Request(false),
                    _ => false,
                }
            );
            var wasExpanded = binding.Expanded();
            context.Effect(
                () =>
                {
                    var expanded = binding.Expanded();
                    if (
                        wasExpanded
                        && !expanded
                        && binding.Panel is { } panel
                        && context.CompositionInput().FocusedElement is { } focused
                    )
                    {
                        var element = context.Composition.Find(focused);
                        if (element is not null && panel.IsAncestorOf(element))
                            _ = context.CompositionInput().FocusSemantic(context.Identity);
                    }
                    wasExpanded = expanded;
                    context.UpdateSemantics(Declaration(expanded));
                },
                "expanded-state"
            );
            AttachClick(context, () => Request(!binding.Expanded()));
            context.OnKey(route =>
            {
                if (
                    route.Command is
                    { Kind: KeyCommandKind.Down, IsRepeat: false, Key: Key.Enter or Key.Space }
                )
                    route.Handled = Request(!binding.Expanded());
            });
            bool Request(bool expanded)
            {
                binding.Request(expanded);
                return true;
            }
        }

        private SemanticDeclaration Declaration(bool? expanded = null) =>
            new(
                SemanticRole.Button,
                binding.Heading,
                actions: SemanticAction.ExpandCollapse,
                expanded: expanded ?? binding.Expanded()
            );
    }

    private static void AttachClick(BehaviorContext context, Func<bool> request)
    {
        int? armed = null;
        context.OnPointer(route =>
        {
            if (route.Command is { Kind: PointerCommandKind.Down, Button: PointerButton.Primary })
            {
                var captured = route.Capture();
                armed = captured ? route.Command.PointerId : null;
                context.SetState(BehaviorState.Pressed, captured);
                if (captured)
                    route.Focus();
                route.Handled = captured;
                return;
            }
            if (
                route.Command.Kind != PointerCommandKind.Cancel
                && !route.Command.Releases(PointerButton.Primary)
            )
                return;
            if (armed != route.Command.PointerId)
                return;
            var active = context.State.GetValueOrDefault(BehaviorState.Pressed);
            armed = null;
            context.SetState(BehaviorState.Pressed, false);
            if (
                active
                && route.Command.Kind == PointerCommandKind.Up
                && route.IsInsideCurrentTarget
            )
                _ = request();
            route.Handled = active;
        });
        context.OnCaptureLost(loss =>
        {
            if (armed == loss.PointerId)
            {
                armed = null;
                context.SetState(BehaviorState.Pressed, false);
            }
        });
    }
}
