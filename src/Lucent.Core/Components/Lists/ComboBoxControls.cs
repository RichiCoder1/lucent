namespace Lucent.Core;

internal sealed class ComboBoxBinding
{
    internal required string Label { get; init; }
    internal required Func<string> Value { get; init; }
    internal required Func<bool> Expanded { get; init; }
    internal required Action Open { get; init; }
    internal required Action Close { get; init; }
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
                if (route.Command.Key == Key.Escape && binding.Expanded())
                    route.Handled = Close();
                else if (route.Command.Key == Key.Tab && binding.Expanded())
                    binding.Close();
                else if (route.Command.Key is Key.Down or Key.Up && !binding.Expanded())
                    route.Handled = Open();
            });
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
