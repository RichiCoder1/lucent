namespace Lucent.Core;

internal sealed class ToggleSelectionBinding(
    string name,
    SemanticRole role,
    Func<CheckState> read,
    Action<CheckState> request,
    CheckStateCycle cycle
)
{
    internal string Name { get; } = ControlState.Required(name, nameof(name));
    internal SemanticRole Role { get; } = role;
    internal Func<CheckState> Read { get; } = read ?? throw new ArgumentNullException(nameof(read));
    internal Action<CheckState> Request { get; } =
        request ?? throw new ArgumentNullException(nameof(request));
    internal CheckStateCycle Cycle { get; } =
        Enum.IsDefined(cycle) ? cycle : throw new ArgumentOutOfRangeException(nameof(cycle));

    internal ImageSource Artwork() =>
        Role switch
        {
            SemanticRole.CheckBox => ReadChecked() switch
            {
                CheckState.Off => SelectionArtwork.CheckOff,
                CheckState.On => SelectionArtwork.CheckOn,
                CheckState.Mixed => SelectionArtwork.CheckMixed,
                _ => throw new InvalidOperationException("Checkbox state must be finite."),
            },
            SemanticRole.Switch => ReadChecked() switch
            {
                CheckState.Off => SelectionArtwork.SwitchOff,
                CheckState.On => SelectionArtwork.SwitchOn,
                _ => throw new InvalidOperationException("Switch state must be binary."),
            },
            _ => throw new InvalidOperationException("Selection toggle role is unsupported."),
        };

    internal CheckState ReadChecked()
    {
        var value = Read();
        if (!Enum.IsDefined(value) || (Role == SemanticRole.Switch && value == CheckState.Mixed))
            throw new InvalidOperationException("The applied selection state is invalid.");
        return value;
    }

    internal CheckState Next()
    {
        var value = ReadChecked();
        return Cycle == CheckStateCycle.TriState
                ? value switch
                {
                    CheckState.Off => CheckState.On,
                    CheckState.On => CheckState.Mixed,
                    CheckState.Mixed => CheckState.Off,
                    _ => throw new InvalidOperationException("Checkbox state must be finite."),
                }
            : value == CheckState.On ? CheckState.Off
            : CheckState.On;
    }
}

internal static class SelectionArtwork
{
    internal static readonly ImageSource CheckOff = Source(
        "check-off",
        "<rect x='9' y='1' width='18' height='18' rx='3'/>"
    );
    internal static readonly ImageSource CheckOn = Source(
        "check-on",
        "<rect x='9' y='1' width='18' height='18' rx='3'/><path d='m13 10 4 4 7-8'/>"
    );
    internal static readonly ImageSource CheckMixed = Source(
        "check-mixed",
        "<rect x='9' y='1' width='18' height='18' rx='3'/><path d='M13 10h10'/>"
    );
    internal static readonly ImageSource RadioOff = Source(
        "radio-off",
        "<circle cx='18' cy='10' r='9'/>"
    );
    internal static readonly ImageSource RadioOn = Source(
        "radio-on",
        "<circle cx='18' cy='10' r='9'/><circle cx='18' cy='10' r='4' fill='black' stroke='none'/>"
    );
    internal static readonly ImageSource SwitchOff = Source(
        "switch-off",
        "<rect x='1' y='2' width='34' height='16' rx='8'/><circle cx='10' cy='10' r='6' fill='black' stroke='none'/>"
    );
    internal static readonly ImageSource SwitchOn = Source(
        "switch-on",
        "<rect x='1' y='2' width='34' height='16' rx='8'/><circle cx='26' cy='10' r='6' fill='black' stroke='none'/>"
    );

    private static ImageSource Source(string name, string geometry)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns='http://www.w3.org/2000/svg' width='36' height='20' viewBox='0 0 36 20' fill='none' stroke='black' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'>"
                + geometry
                + "</svg>"
        );
        return ImageSource.FromAsset(
            new AssetReference(
                new AssetId("Lucent.Core", "selection/" + name + ".svg"),
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
                bytes.Length,
                AssetFormat.Svg,
                () => new MemoryStream(bytes, writable: false),
                new AssetImageMetadata(36, 20)
            )
        );
    }
}

internal sealed class RadioGroupBinding(string label, RadioSelectionRequirement requirement)
{
    internal string Label { get; } = ControlState.Required(label, nameof(label));
    internal RadioSelectionRequirement Requirement { get; } =
        Enum.IsDefined(requirement)
            ? requirement
            : throw new ArgumentOutOfRangeException(nameof(requirement));
}

internal sealed class RadioOptionBinding
{
    internal required Func<string> Label { get; init; }
    internal required Func<bool> Enabled { get; init; }
    internal required Func<bool> Selected { get; init; }
    internal required Func<bool> Roving { get; init; }
    internal required Action<BehaviorContext> Register { get; init; }
    internal required Func<BehaviorContext, bool, bool> Activate { get; init; }
    internal required Func<Key, BehaviorContext, bool> Move { get; init; }
}

internal static partial class Controls
{
    private static Style SelectionRowStyle(ThemeContext theme) =>
        RowStyle
            .Set(LayoutProperties.Padding, Insets.Symmetric(8, 6))
            .Set(LayoutProperties.MinHeight, 32f)
            .Set(LayoutProperties.Spacing, 8f)
            .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Start)
            .Set(VisualProperties.CornerRadius, 4f)
            .Set(InputProperties.Cursor, CursorIntent.Pointer)
            .Bind(TypographyProperties.TextColor, () => theme.Token(ControlThemes.Foreground))
            .When(
                VariantState.Hover,
                Style.Empty.Set(VisualProperties.Background, ControlThemes.Selected)
            )
            .When(VariantState.FocusVisible, FocusStyle(theme))
            .When(
                VariantState.Disabled,
                Style.Empty.Set(TypographyProperties.TextColor, ControlThemes.DisabledForeground)
            );

    internal static void ToggleSelection(
        Element element,
        ThemeContext theme,
        ToggleSelectionBinding binding,
        Style? style
    ) =>
        Configure(
            element,
            theme,
            SelectionRowStyle(theme),
            style,
            new ToggleSelectionBehavior(binding)
        );

    internal static void RadioGroup(
        Element element,
        ThemeContext theme,
        RadioGroupBinding binding,
        Style? style
    ) =>
        Configure(
            element,
            theme,
            PanelStyle.Set(LayoutProperties.Spacing, 6f),
            style,
            new RadioGroupBehavior(binding)
        );

    internal static void RadioOption(
        Element element,
        ThemeContext theme,
        RadioOptionBinding binding
    ) =>
        Configure(
            element,
            theme,
            SelectionRowStyle(theme).Bind(InputProperties.Enabled, binding.Enabled),
            null,
            new RadioOptionBehavior(binding)
        );

    private sealed class ToggleSelectionBehavior(ToggleSelectionBinding binding) : Behavior
    {
        public override string Name => "selection-toggle";
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
                    SemanticCommandKind.Toggle => Request(),
                    _ => false,
                }
            );
            context.Effect(
                () =>
                {
                    var state = binding.ReadChecked();
                    context.SetState(BehaviorState.Selected, state == CheckState.On);
                    context.UpdateSemantics(Declaration(state));
                },
                "applied-state"
            );

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
                    _ = Request();
                route.Handled = active;
            });
            context.OnKey(route =>
            {
                if (route.Command is { Kind: KeyCommandKind.Down, IsRepeat: false, Key: Key.Space })
                    route.Handled = Request();
            });
            context.OnCaptureLost(loss =>
            {
                if (armedPointer != loss.PointerId)
                    return;
                armedPointer = null;
                context.SetState(BehaviorState.Pressed, false);
            });

            bool Request()
            {
                binding.Request(binding.Next());
                return true;
            }
        }

        private SemanticDeclaration Declaration(CheckState? state = null)
        {
            var applied = state ?? binding.ReadChecked();
            return new(
                binding.Role,
                binding.Name,
                actions: SemanticAction.Toggle,
                toggleState: applied switch
                {
                    CheckState.Off => SemanticToggleState.Off,
                    CheckState.On => SemanticToggleState.On,
                    CheckState.Mixed => SemanticToggleState.Indeterminate,
                    _ => throw new InvalidOperationException("Toggle state must be finite."),
                }
            );
        }
    }

    private sealed class RadioGroupBehavior(RadioGroupBinding binding) : Behavior
    {
        public override string Name => "radio-group";
        public override BehaviorOwnership Ownership => BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context) =>
            context.SetSemantics(
                new(
                    SemanticRole.RadioGroup,
                    binding.Label,
                    selection: new(
                        CanSelectMultiple: false,
                        IsSelectionRequired: binding.Requirement
                            == RadioSelectionRequirement.Required
                    )
                )
            );
    }

    private sealed class RadioOptionBehavior(RadioOptionBinding binding) : Behavior
    {
        public override string Name => "radio-option";
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
                    SemanticCommandKind.Select => binding.Activate(context, true),
                    _ => false,
                }
            );
            context.Effect(
                () =>
                {
                    var enabled = binding.Enabled();
                    var selected = binding.Selected();
                    var roving = enabled && binding.Roving();
                    context.SetTabStop(roving);
                    context.SetState(BehaviorState.Selected, selected);
                    context.UpdateSemantics(Declaration(selected));
                },
                "radio-state"
            );

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
                    _ = binding.Activate(context, true);
                route.Handled = active;
            });
            context.OnKey(route =>
            {
                if (route.Command.Kind != KeyCommandKind.Down)
                    return;
                if (route.Command is { IsRepeat: false, Key: Key.Space })
                {
                    route.Handled = binding.Activate(context, true);
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
                SemanticRole.RadioButton,
                ControlState.Required(binding.Label(), nameof(binding.Label)),
                enabled: binding.Enabled(),
                selected: selected ?? binding.Selected(),
                actions: SemanticAction.Select
            );
    }
}
