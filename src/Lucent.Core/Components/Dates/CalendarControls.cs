namespace Lucent.Core;

internal static partial class Controls
{
    internal static void CalendarDay(
        Element element,
        ThemeContext theme,
        CalendarState calendar,
        DateEditSession session,
        int slot,
        Action close
    )
    {
        CalendarDay Current() => calendar.Day(slot, session.Applied);
        var style = Style
            .Empty.Set(LayoutProperties.Width, 32f)
            .Set(LayoutProperties.Height, 32f)
            .Set(LayoutProperties.GridPlacement, new GridPlacement(slot / 7 + 1, slot % 7))
            .Set(LayoutProperties.MainAlignment, LayoutAlignment.Center)
            .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Center)
            .Set(VisualProperties.CornerRadius, 4f)
            .Bind(
                ProjectionProperties.Text,
                () => Current().Date?.Day.ToString(session.Options.Culture) ?? ""
            )
            .Bind(InputProperties.Enabled, () => Current().IsEnabled)
            .Bind(
                InputProperties.Cursor,
                () => Current().IsEnabled ? CursorIntent.Pointer : CursorIntent.Default
            )
            .Bind(
                TypographyProperties.TextColor,
                () =>
                    Current().InDisplayedMonth
                        ? theme.Token(ControlThemes.Foreground)
                        : theme.Token(ControlThemes.SecondaryForeground)
            )
            .Bind(
                VisualProperties.Background,
                () =>
                    Current().IsSelected
                        ? theme.Token(ControlThemes.Selected)
                        : PresentationStyles.TransparentBrush
            )
            .Bind(
                VisualProperties.Border,
                () =>
                    Current().IsToday
                        ? Border.Hairline(theme.Token(ControlThemes.Accent))
                        : Border.None
            )
            .Bind(
                VisualProperties.FocusRing,
                () =>
                    Current().IsFocused && calendar.FocusVisible
                        ? theme.Token(ControlThemes.FocusRing)
                        : FocusRing.None
            )
            .When(
                VariantState.Hover,
                Style.Empty.Set(VisualProperties.Background, ControlThemes.Selected)
            )
            .When(
                VariantState.Pressed,
                Style.Empty.Set(VisualProperties.Background, ControlThemes.AccentPressed)
            )
            .When(
                VariantState.Disabled,
                Style.Empty.Set(TypographyProperties.TextColor, ControlThemes.DisabledForeground)
            );
        Configure(
            element,
            theme,
            style,
            null,
            new CalendarDayBehavior(calendar, session, slot, close)
        );
    }
}

internal sealed class CalendarBehavior(
    CalendarState calendar,
    DateEditSession session,
    Action close
) : Behavior
{
    public override string Name => "calendar";
    public override BehaviorOwnership Ownership =>
        BehaviorOwnership.Focus | BehaviorOwnership.Semantics;

    public override void Attach(BehaviorContext context)
    {
        SemanticDeclaration Declaration() =>
            new(
                SemanticRole.Calendar,
                "Calendar",
                description: calendar.Focused.ToString("D", session.Options.Culture),
                selection: new(false, !session.Options.AllowNull)
            );
        context.SetSemantics(Declaration());
        context.MakeFocusable();
        context.OnSemanticCommand(command =>
            command.Kind == SemanticCommandKind.Focus
            && context.CompositionInput().FocusSemantic(context.Identity)
        );
        context.OnFocus(route =>
            calendar.FocusVisible =
                route.Command.Kind == FocusCommandKind.Gained
                && route.Command.Modality == InputModality.Keyboard
        );
        context.Effect(() => context.UpdateSemantics(Declaration()), "calendar.focused-date");
        context.OnKey(route =>
        {
            if (route.Command.Kind != KeyCommandKind.Down)
                return;
            var moved = route.Command.Key switch
            {
                Key.Left => calendar.MoveDays(-1),
                Key.Right => calendar.MoveDays(1),
                Key.Up => calendar.MoveDays(-7),
                Key.Down => calendar.MoveDays(7),
                Key.Home => calendar.MoveWeekEdge(false),
                Key.End => calendar.MoveWeekEdge(true),
                Key.PageUp => calendar.MoveMonth(-1),
                Key.PageDown => calendar.MoveMonth(1),
                Key.Enter or Key.Space => Select(),
                Key.Escape => Cancel(),
                _ => false,
            };
            if (moved)
                route.Handled = true;
        });
        bool Select()
        {
            session.Select(calendar.Focused);
            close();
            return true;
        }
        bool Cancel()
        {
            close();
            return true;
        }
    }
}

internal sealed class CalendarDayBehavior(
    CalendarState calendar,
    DateEditSession session,
    int slot,
    Action close
) : Behavior
{
    public override string Name => "calendar-day";
    public override BehaviorOwnership Ownership =>
        BehaviorOwnership.Action | BehaviorOwnership.Semantics;

    public override void Attach(BehaviorContext context)
    {
        CalendarDay Current() => calendar.Day(slot, session.Applied);
        SemanticDeclaration Declaration()
        {
            var day = Current();
            return new(
                SemanticRole.ListItem,
                day.AccessibleName,
                enabled: day.IsEnabled,
                selected: day.IsSelected,
                actions: day.IsEnabled ? SemanticAction.Select : SemanticAction.None,
                positionInSet: slot + 1,
                sizeOfSet: 42
            );
        }
        context.SetSemantics(Declaration());
        context.Effect(
            () =>
            {
                _ = Current();
                context.UpdateSemantics(Declaration());
            },
            "calendar-day." + slot
        );
        bool Select()
        {
            var day = Current();
            if (!day.IsEnabled || day.Date is not { } date)
                return false;
            session.Select(date);
            close();
            return true;
        }
        context.OnSemanticCommand(command =>
            command.Kind == SemanticCommandKind.Select && Select()
        );
        int? armedPointer = null;
        context.OnPointer(route =>
        {
            if (route.Command is { Kind: PointerCommandKind.Down, Button: PointerButton.Primary })
            {
                var armed = route.Capture();
                armedPointer = armed ? route.Command.PointerId : null;
                context.SetState(BehaviorState.Pressed, armed);
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
                _ = Select();
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
}
