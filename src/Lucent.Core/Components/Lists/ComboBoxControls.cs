namespace Lucent.Core;

internal sealed class ComboBoxBinding
{
    internal required string Label { get; init; }
    internal required Func<string> Value { get; init; }
    internal required Func<bool> Expanded { get; init; }
    internal required Action Open { get; init; }
    internal required Action Close { get; init; }
    internal required Func<Key, bool> Navigate { get; init; }
    internal required Func<bool> Commit { get; init; }
    internal required FocusTarget FocusTarget { get; init; }
}

internal static partial class Controls
{
    internal static void ComboBox(
        Element element,
        ThemeContext theme,
        ComboBoxBinding binding,
        Style? style
    ) =>
        Configure(
            element,
            theme,
            RowStyle
                .Set(LayoutProperties.Spacing, 4f)
                .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Center),
            style,
            new ComboBoxBehavior(binding)
        );

    private sealed class ComboBoxBehavior(ComboBoxBinding binding) : Behavior
    {
        public override string Name => "combo-box";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            context.SetSemantics(Declaration());
            context.OnSemanticCommand(command =>
                command.Kind switch
                {
                    SemanticCommandKind.Focus => Focus(),
                    SemanticCommandKind.Expand when !binding.Expanded() => Open(),
                    SemanticCommandKind.Collapse when binding.Expanded() => Close(),
                    _ => false,
                }
            );
            context.Effect(() => context.UpdateSemantics(Declaration()), "combo-box-state");
            context.OnKey(route =>
            {
                if (route.Command.Kind != KeyCommandKind.Down || route.Command.IsRepeat)
                    return;
                if (route.Command.Key is Key.Down or Key.Up)
                    route.Handled = binding.Expanded()
                        ? binding.Navigate(route.Command.Key)
                        : Open();
                else if (route.Command.Key == Key.Enter && binding.Expanded())
                    route.Handled = binding.Commit();
                else if (route.Command.Key == Key.Escape && binding.Expanded())
                    route.Handled = Close();
                else if (route.Command.Key == Key.Tab && binding.Expanded())
                    binding.Close();
            });
            AttachClick(context);
            bool Focus()
            {
                binding.FocusTarget.Request();
                return true;
            }
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
            void AttachClick(BehaviorContext behaviorContext)
            {
                int? armedPointer = null;
                bool? openOnRelease = null;
                behaviorContext.OnPointer(route =>
                {
                    if (
                        route.Command is
                        { Kind: PointerCommandKind.Down, Button: PointerButton.Primary }
                    )
                    {
                        var captured = route.Capture();
                        armedPointer = captured ? route.Command.PointerId : null;
                        openOnRelease = captured ? !binding.Expanded() : null;
                        behaviorContext.SetState(BehaviorState.Pressed, captured);
                        route.Handled = captured;
                        return;
                    }
                    if (
                        route.Command.Kind != PointerCommandKind.Cancel
                        && !route.Command.Releases(PointerButton.Primary)
                    )
                        return;
                    if (armedPointer != route.Command.PointerId)
                        return;
                    var active = behaviorContext.State.GetValueOrDefault(BehaviorState.Pressed);
                    armedPointer = null;
                    behaviorContext.SetState(BehaviorState.Pressed, false);
                    if (
                        active
                        && route.Command.Kind == PointerCommandKind.Up
                        && route.IsInsideCurrentTarget
                    )
                    {
                        if (openOnRelease == true)
                        {
                            binding.FocusTarget.Request();
                            _ = Open();
                        }
                        else
                            _ = Close();
                    }
                    openOnRelease = null;
                    route.Handled = active;
                });
                behaviorContext.OnCaptureLost(loss =>
                {
                    if (armedPointer != loss.PointerId)
                        return;
                    armedPointer = null;
                    openOnRelease = null;
                    behaviorContext.SetState(BehaviorState.Pressed, false);
                });
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
