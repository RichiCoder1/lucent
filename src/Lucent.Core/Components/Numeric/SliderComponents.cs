using System.Globalization;

namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Creates a controlled finite range input with preview requests and explicit gesture completion.</summary>
    [LucentComponent]
    public static ComponentRecipe Slider(
        string label,
        Func<double> value,
        Action<double> onValueRequested,
        SliderOptions options,
        Action<double>? onCommit = null,
        Func<bool>? enabled = null,
        Func<bool>? readOnly = null,
        Func<double, string>? formatValue = null,
        Style? style = null
    )
    {
        label = Required(label, nameof(label));
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(onValueRequested);
        ArgumentNullException.ThrowIfNull(options);
        formatValue ??= current => current.ToString("G", CultureInfo.CurrentCulture);
        var format = formatValue;
        return ComponentRecipe.Create(
            "slider",
            (context, root) =>
            {
                var state = new SliderState(
                    root.Scope,
                    value,
                    onValueRequested,
                    onCommit,
                    options,
                    root.Name
                );
                root.Present(
                    context.Theme,
                    Style
                        .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                        .Set(LayoutProperties.Spacing, 6f),
                    style
                );

                var labelElement = context.Child(root, "label");
                Controls.Text(labelElement, context.Theme, label);

                var control = context.Child(root, "control");
                var controlStyle = Style
                    .Empty.Set(LayoutProperties.Mode, LayoutMode.Grid)
                    .Set(
                        LayoutProperties.Height,
                        options.Orientation == LayoutAxis.Row ? 36f : 160f
                    )
                    .Set(InputProperties.Cursor, CursorIntent.Pointer);
                if (options.Orientation == LayoutAxis.Column)
                    controlStyle = controlStyle.Set(LayoutProperties.Width, 36f);
                if (options.Orientation == LayoutAxis.Row)
                    controlStyle = controlStyle
                        .Bind(LayoutProperties.Columns, () => SliderTracks(state.VisualFraction))
                        .Set(LayoutProperties.Rows, GridTracks.Create(GridTrack.Fixed(36)));
                else
                    controlStyle = controlStyle
                        .Set(LayoutProperties.Columns, GridTracks.Create(GridTrack.Fixed(36)))
                        .Bind(LayoutProperties.Rows, () => SliderTracks(state.VisualFraction));
                if (enabled is not null)
                    controlStyle = controlStyle.Bind(InputProperties.Enabled, enabled);
                control.Present(context.Theme, controlStyle);
                control.AttachBehaviors(new SliderBehavior(state, label, readOnly));

                var track = context.Child(control, "track");
                var horizontal = options.Orientation == LayoutAxis.Row;
                var trackStyle = Style
                    .Empty.Set(
                        LayoutProperties.GridPlacement,
                        horizontal ? new GridPlacement(0, 0, 1, 3) : new GridPlacement(0, 0, 3, 1)
                    )
                    .Set(LayoutProperties.Axis, horizontal ? LayoutAxis.Column : LayoutAxis.Row)
                    .Set(LayoutProperties.MainAlignment, LayoutAlignment.Center)
                    .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Stretch);
                trackStyle = horizontal
                    ? trackStyle.Set(LayoutProperties.Height, 36f)
                    : trackStyle.Set(LayoutProperties.Width, 36f);
                track.Present(context.Theme, trackStyle);
                var trackLine = context.Child(track, "line");
                var trackLineStyle = Style
                    .Empty.Set(VisualProperties.Background, ControlThemes.Border)
                    .Set(VisualProperties.CornerRadius, 2f);
                trackLineStyle = horizontal
                    ? trackLineStyle.Set(LayoutProperties.Height, 4f)
                    : trackLineStyle.Set(LayoutProperties.Width, 4f);
                trackLine.Present(context.Theme, trackLineStyle);

                var fill = context.Child(control, "fill");
                var fillStartsLeading = options.Direction == SliderDirection.Forward == horizontal;
                var fillStyle = Style
                    .Empty.Set(
                        LayoutProperties.GridPlacement,
                        horizontal
                            ? new GridPlacement(0, fillStartsLeading ? 0 : 1, 1, 2)
                            : new GridPlacement(fillStartsLeading ? 0 : 1, 0, 2, 1)
                    )
                    .Set(LayoutProperties.Axis, horizontal ? LayoutAxis.Column : LayoutAxis.Row)
                    .Set(LayoutProperties.MainAlignment, LayoutAlignment.Center)
                    .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Stretch);
                fillStyle = horizontal
                    ? fillStyle.Set(LayoutProperties.Height, 36f)
                    : fillStyle.Set(LayoutProperties.Width, 36f);
                fill.Present(context.Theme, fillStyle);
                var fillLine = context.Child(fill, "line");
                var fillLineStyle = Style
                    .Empty.Set(VisualProperties.Background, ControlThemes.Accent)
                    .Set(VisualProperties.CornerRadius, 2f);
                fillLineStyle = horizontal
                    ? fillLineStyle.Set(LayoutProperties.Height, 4f)
                    : fillLineStyle.Set(LayoutProperties.Width, 4f);
                fillLine.Present(context.Theme, fillLineStyle);

                var thumb = context.Child(control, "thumb");
                thumb.Present(
                    context.Theme,
                    Style
                        .Empty.Set(
                            LayoutProperties.GridPlacement,
                            horizontal ? new GridPlacement(0, 1) : new GridPlacement(1, 0)
                        )
                        .Set(LayoutProperties.Axis, horizontal ? LayoutAxis.Column : LayoutAxis.Row)
                        .Set(LayoutProperties.Width, horizontal ? 16f : 36f)
                        .Set(LayoutProperties.Height, horizontal ? 36f : 16f)
                        .Set(LayoutProperties.MainAlignment, LayoutAlignment.Center)
                        .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Center)
                );
                var thumbKnob = context.Child(thumb, "knob");
                thumbKnob.Present(
                    context.Theme,
                    Style
                        .Empty.Set(LayoutProperties.Width, 16f)
                        .Set(LayoutProperties.Height, 16f)
                        .Set(VisualProperties.Background, ControlThemes.Accent)
                        .Set(VisualProperties.CornerRadius, 8f)
                );

                var valueElement = context.Child(root, "value");
                var initialText = Required(format(state.Draft), nameof(formatValue));
                Controls.Text(valueElement, context.Theme, initialText);
                _ = root.Scope.Effect(
                    () =>
                    {
                        var current = Required(format(state.Draft), nameof(formatValue));
                        valueElement.UpdateControl(ProjectionProperties.Text, current);
                        valueElement.UpdateControlSemantics(new(SemanticRole.Text, current));
                    },
                    root.Name + ".value-text"
                );
            }
        );
    }

    private static GridTracks SliderTracks(float fraction) =>
        GridTracks.Create(
            fraction == 0 ? GridTrack.Fixed(0) : GridTrack.Fraction(fraction),
            GridTrack.Fixed(16),
            fraction == 1 ? GridTrack.Fixed(0) : GridTrack.Fraction(1 - fraction)
        );
}
