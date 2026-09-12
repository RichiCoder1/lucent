namespace Lucent.Core;

public static partial class Components
{
    internal static DatePickerOptions DatePolicy(DatePickerOptions? value) => value ?? new();

    internal static TimePickerOptions TimePolicy(TimePickerOptions? value) => value ?? new();

    internal static DateTimeFieldOptions DateTimeFieldPolicy(DateTimeFieldOptions? value) =>
        value ?? new();

    [LucentComponent]
    internal static ComponentRecipe TimePickerEditor(
        FieldContext field,
        TimeEditSession session,
        Func<bool>? enabled,
        Func<bool>? readOnly
    )
    {
        bool Editable() => enabled?.Invoke() != false && readOnly?.Invoke() != true;
        var actionStyle = Style.Empty.Bind(InputProperties.Enabled, Editable);
        return TimePickerEditorView(
            field,
            session,
            enabled,
            readOnly,
            () =>
            {
                if (Editable())
                    _ = session.Step(-1);
            },
            () =>
            {
                if (Editable())
                    _ = session.Step(1);
            },
            actionStyle
        );
    }

    [LucentComponent]
    internal static ComponentRecipe DatePickerEditor(
        FieldContext field,
        DateEditSession session,
        Func<bool>? enabled,
        Func<bool>? readOnly
    )
    {
        return ComponentRecipe.Create(
            "date-picker-editor",
            (context, root) =>
            {
                OwnedSurfaceRequest? surface = null;
                void Close()
                {
                    surface?.Dispose();
                    surface = null;
                }
                void Open()
                {
                    if (
                        enabled?.Invoke() == false
                        || readOnly?.Invoke() == true
                        || surface is not null
                    )
                        return;
                    surface = new OwnedSurfaceRequest(
                        root,
                        context.Theme,
                        CalendarPopup(session, Close),
                        true,
                        true,
                        Close
                    );
                    root.Composition.Input.RequestSurface(surface);
                }
                root.Scope.OnDispose(Close);
                var actionStyle = Style.Empty.Bind(
                    InputProperties.Enabled,
                    () => enabled?.Invoke() != false && readOnly?.Invoke() != true
                );
                context.Mount(
                    root,
                    ComponentContent.Create([
                        DatePickerEditorView(field, session, enabled, readOnly, Open, actionStyle),
                    ])
                );
            }
        );
    }

    private static ComponentRecipe CalendarPopup(DateEditSession session, Action close) =>
        ComponentRecipe.Create(
            "calendar",
            (context, root) =>
            {
                var calendar = new CalendarState(
                    root.Scope,
                    session.Options,
                    session.Applied,
                    root.Name
                );
                var days = new List<ContentRecipe>();
                var firstDay = session.Options.Culture.DateTimeFormat.FirstDayOfWeek;
                for (var index = 0; index < 7; index++)
                {
                    var day = (DayOfWeek)(((int)firstDay + index) % 7);
                    days.Add(
                        Text(
                            session.Options.Culture.DateTimeFormat.AbbreviatedDayNames[(int)day],
                            Style.Empty.GridPlacement(new GridPlacement(0, index))
                        )
                    );
                }
                for (var index = 0; index < 42; index++)
                {
                    days.Add(CalendarDaySlot(calendar, session, index, close));
                }
                context.Mount(
                    root,
                    ComponentContent.Create([
                        CalendarSurfaceView(
                            calendar,
                            session,
                            close,
                            ComponentContent.Create(days.ToArray())
                        ),
                    ])
                );
            }
        );

    [LucentComponent]
    internal static ComponentRecipe DateTimeTextField(
        FieldContext field,
        DateEditSession session,
        Func<bool>? enabled,
        Func<bool>? readOnly
    ) =>
        TextFieldControlledCore(
            field,
            () => session.Draft,
            session.Edit,
            Style.Empty.MinWidth(144),
            null,
            enabled,
            readOnly,
            () => _ = session.Commit(),
            session.Cancel
        );

    [LucentComponent]
    internal static ComponentRecipe DateTimeTextField(
        FieldContext field,
        TimeEditSession session,
        Func<bool>? enabled,
        Func<bool>? readOnly
    ) =>
        TextFieldControlledCore(
            field,
            () => session.Draft,
            session.Edit,
            Style.Empty.MinWidth(112),
            null,
            enabled,
            readOnly,
            () => _ = session.Commit(),
            session.Cancel
        );

    [LucentComponent]
    internal static ComponentRecipe TimePickerHost(
        TimeEditSession session,
        string label,
        Func<bool>? enabled,
        Func<bool>? readOnly,
        [DefaultContent] ComponentContent content
    ) =>
        Host(
            "time-picker",
            content,
            (context, root) =>
            {
                bool Editable() => enabled?.Invoke() != false && readOnly?.Invoke() != true;
                root.Present(
                    context.Theme,
                    Style
                        .Empty.Set(LayoutProperties.Axis, LayoutAxis.Row)
                        .Spacing(6)
                        .CrossAlignment(LayoutAlignment.Center)
                );
                root.AttachBehaviors(new TimePickerBehavior(session, label, Editable));
            }
        );

    [LucentComponent]
    internal static ComponentRecipe CalendarHost(
        CalendarState calendar,
        DateEditSession session,
        Action close,
        [DefaultContent] ComponentContent content
    ) =>
        Host(
            "calendar",
            content,
            (context, root) =>
            {
                root.Present(
                    context.Theme,
                    Style
                        .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                        .Set(LayoutProperties.Width, 224f)
                        .Set(LayoutProperties.Height, 244f)
                );
                root.AttachBehaviors(new CalendarBehavior(calendar, session, close));
            }
        );

    private static ComponentRecipe CalendarDaySlot(
        CalendarState calendar,
        DateEditSession session,
        int slot,
        Action close
    ) =>
        ComponentRecipe.Create(
            "calendar-day",
            (context, root) =>
                Controls.CalendarDay(root, context.Theme, calendar, session, slot, close)
        );

    private static ValidationState CombinedValidation(
        ValidationState draft,
        Func<ValidationState>? application
    ) => draft.Status == ValidationStatus.Invalid ? draft : application?.Invoke() ?? draft;
}

internal sealed class TimePickerBehavior(TimeEditSession session, string label, Func<bool> editable)
    : Behavior
{
    public override string Name => "time-picker";
    public override BehaviorOwnership Ownership =>
        BehaviorOwnership.Action | BehaviorOwnership.Semantics;

    public override void Attach(BehaviorContext context)
    {
        SemanticDeclaration Declaration() => new(SemanticRole.Spinner, label, value: session.Draft);
        context.SetSemantics(Declaration());
        context.Effect(() => context.UpdateSemantics(Declaration()), label + ".time-spinner");
        context.OnKey(route =>
        {
            if (route.Command.Kind != KeyCommandKind.Down || !editable())
                return;
            if (route.Command.Key == Key.Up)
                _ = session.Step(1);
            else if (route.Command.Key == Key.Down)
                _ = session.Step(-1);
            else
                return;
            route.Handled = true;
        });
    }
}
