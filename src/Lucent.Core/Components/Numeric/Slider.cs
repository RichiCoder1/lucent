using System.Globalization;

namespace Lucent.Core;

/// <summary>Controls the direction in which increasing slider values are presented.</summary>
public enum SliderDirection
{
    /// <summary>Uses left-to-right for horizontal sliders and bottom-to-top for vertical sliders.</summary>
    Forward,

    /// <summary>Reverses the stock direction.</summary>
    Reverse,
}

/// <summary>Validated finite range and interaction policy for a slider.</summary>
public sealed class SliderOptions
{
    /// <summary>Creates a finite slider range.</summary>
    public SliderOptions(
        double minimum,
        double maximum,
        double increment,
        double? pageIncrement = null,
        LayoutAxis orientation = LayoutAxis.Row,
        SliderDirection direction = SliderDirection.Forward,
        bool wheelEnabled = false
    )
    {
        var page = pageIncrement ?? increment * 10;
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum >= maximum)
            throw new ArgumentException("Slider bounds must be finite and increasing.");
        if (!double.IsFinite(increment) || increment <= 0)
            throw new ArgumentOutOfRangeException(nameof(increment));
        if (!double.IsFinite(page) || page <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageIncrement));
        if (!Enum.IsDefined(orientation))
            throw new ArgumentOutOfRangeException(nameof(orientation));
        if (!Enum.IsDefined(direction))
            throw new ArgumentOutOfRangeException(nameof(direction));
        Minimum = minimum;
        Maximum = maximum;
        Increment = increment;
        PageIncrement = page;
        Orientation = orientation;
        Direction = direction;
        WheelEnabled = wheelEnabled;
    }

    /// <summary>Gets the inclusive minimum.</summary>
    public double Minimum { get; }

    /// <summary>Gets the inclusive maximum.</summary>
    public double Maximum { get; }

    /// <summary>Gets the arrow and automation increment.</summary>
    public double Increment { get; }

    /// <summary>Gets the Page Up and Page Down increment.</summary>
    public double PageIncrement { get; }

    /// <summary>Gets the slider orientation.</summary>
    public LayoutAxis Orientation { get; }

    /// <summary>Gets the increasing visual direction.</summary>
    public SliderDirection Direction { get; }

    /// <summary>Gets whether focused wheel changes are enabled.</summary>
    public bool WheelEnabled { get; }
}

internal sealed class SliderState
{
    private readonly Func<double> _readValue;
    private readonly Action<double> _request;
    private readonly Action<double>? _commit;
    private readonly Signal<double> _draft;
    private double _applied;
    private double? _requested;

    internal SliderState(
        ReactiveScope owner,
        Func<double> readValue,
        Action<double> request,
        Action<double>? commit,
        SliderOptions options,
        string name
    )
    {
        _readValue = readValue;
        _request = request;
        _commit = commit;
        Options = options;
        _applied = Validate(readValue());
        _draft = owner.Signal(_applied, name + ".draft");
        _ = owner.Effect(Reconcile, name + ".applied");
    }

    internal SliderOptions Options { get; }
    internal double Draft => _draft.Value;
    internal float Fraction =>
        (float)((Draft - Options.Minimum) / (Options.Maximum - Options.Minimum));
    internal float VisualFraction =>
        Options.Orientation == LayoutAxis.Row
            ? Options.Direction == SliderDirection.Forward
                ? Fraction
                : 1 - Fraction
            : Options.Direction == SliderDirection.Forward
                ? 1 - Fraction
                : Fraction;

    internal double Begin() => _applied;

    internal void Preview(double value)
    {
        var next = Snap(value);
        if (next == _draft.Value && _requested == next)
            return;
        _draft.Value = next;
        _requested = next;
        _request(next);
    }

    internal void Complete() => _commit?.Invoke(_draft.Value);

    internal void Cancel(double initial)
    {
        Preview(initial);
        _commit?.Invoke(initial);
    }

    private void Reconcile()
    {
        var value = Validate(_readValue());
        if (value == _applied)
            return;
        _applied = value;
        if (_requested == value)
            _requested = null;
        _draft.Value = value;
    }

    private double Validate(double value)
    {
        if (!double.IsFinite(value) || value < Options.Minimum || value > Options.Maximum)
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "The controlled slider value must be finite and inside its range."
            );
        return value;
    }

    private double Snap(double value)
    {
        var bounded = Math.Clamp(value, Options.Minimum, Options.Maximum);
        var steps = Math.Round(
            (bounded - Options.Minimum) / Options.Increment,
            MidpointRounding.AwayFromZero
        );
        return Math.Clamp(
            Options.Minimum + steps * Options.Increment,
            Options.Minimum,
            Options.Maximum
        );
    }
}

internal sealed class SliderBehavior(SliderState state, string label, Func<bool>? readOnly)
    : Behavior
{
    private int? _pointer;
    private double _initial;
    private bool _focused;

    public override string Name => "slider";
    public override BehaviorOwnership Ownership =>
        BehaviorOwnership.Focus | BehaviorOwnership.Action | BehaviorOwnership.Semantics;

    public override void Attach(BehaviorContext context)
    {
        bool IsReadOnly() => readOnly?.Invoke() == true;
        SemanticDeclaration Declaration() =>
            new(
                SemanticRole.Slider,
                label,
                actions: IsReadOnly() ? SemanticAction.None : SemanticAction.SetRangeValue,
                value: state.Draft.ToString(CultureInfo.CurrentCulture),
                range: new(
                    state.Draft,
                    state.Options.Minimum,
                    state.Options.Maximum,
                    state.Options.Increment,
                    state.Options.PageIncrement,
                    IsReadOnly()
                )
            );

        context.MakeFocusable();
        context.SetSemantics(Declaration());
        context.Effect(() => context.UpdateSemantics(Declaration()), label + ".range");
        context.OnSemanticCommand(command =>
        {
            if (command.Kind == SemanticCommandKind.Focus)
                return context.CompositionInput().FocusSemantic(context.Identity);
            if (
                command.Kind != SemanticCommandKind.SetRangeValue
                || IsReadOnly()
                || command.NumericValue is not { } value
            )
                return false;
            state.Preview(value);
            state.Complete();
            return true;
        });
        context.OnFocus(route => _focused = route.Command.Kind == FocusCommandKind.Gained);
        context.OnWheel(route =>
        {
            if (
                !state.Options.WheelEnabled
                || !_focused
                || IsReadOnly()
                || route.Command.DeltaY == 0
            )
                return;
            var direction = route.Command.DeltaY < 0 ? 1 : -1;
            if (state.Options.Direction == SliderDirection.Reverse)
                direction = -direction;
            state.Preview(state.Draft + direction * state.Options.Increment);
            state.Complete();
            route.Handled = true;
        });
        context.OnCaptureLost(loss =>
        {
            if (_pointer != loss.PointerId)
                return;
            _pointer = null;
            context.SetState(BehaviorState.Pressed, false);
            if (loss.Reason == PointerCaptureLossReason.Released)
                state.Complete();
            else
                state.Cancel(_initial);
        });
        context.OnPointer(route =>
        {
            if (IsReadOnly())
                return;
            var command = route.Command;
            if (command is { Kind: PointerCommandKind.Down, Button: PointerButton.Primary })
            {
                route.Focus();
                _initial = state.Begin();
                if (route.Capture())
                {
                    _pointer = command.PointerId;
                    context.SetState(BehaviorState.Pressed, true);
                }
                PreviewPointer(context, command);
                route.Handled = true;
            }
            else if (
                _pointer == command.PointerId
                && command.Kind
                    is PointerCommandKind.Move
                        or PointerCommandKind.Up
                        or PointerCommandKind.Cancel
            )
            {
                if (command.Kind == PointerCommandKind.Cancel)
                    state.Cancel(_initial);
                else
                    PreviewPointer(context, command);
                if (command.Kind is PointerCommandKind.Up or PointerCommandKind.Cancel)
                {
                    _pointer = null;
                    context.SetState(BehaviorState.Pressed, false);
                    if (command.Kind == PointerCommandKind.Up)
                        state.Complete();
                }
                route.Handled = true;
            }
        });
        context.OnKey(route =>
        {
            if (
                route.Command.Kind != KeyCommandKind.Down
                || IsReadOnly()
                || route.Command.Modifiers != KeyModifiers.None
            )
                return;
            var initial = state.Begin();
            var delta = route.Command.Key switch
            {
                Key.Left or Key.Down => -state.Options.Increment,
                Key.Right or Key.Up => state.Options.Increment,
                Key.PageDown => -state.Options.PageIncrement,
                Key.PageUp => state.Options.PageIncrement,
                _ => 0,
            };
            if (state.Options.Direction == SliderDirection.Reverse)
                delta = -delta;
            if (route.Command.Key == Key.Home)
                state.Preview(state.Options.Minimum);
            else if (route.Command.Key == Key.End)
                state.Preview(state.Options.Maximum);
            else if (route.Command.Key == Key.Escape)
                state.Cancel(initial);
            else if (delta != 0)
                state.Preview(state.Draft + delta);
            else
                return;
            if (route.Command.Key != Key.Escape)
                state.Complete();
            route.Handled = true;
        });
    }

    private void PreviewPointer(BehaviorContext context, PointerCommand command)
    {
        if (context.CompositionInput().Bounds(context.Identity) is not { } bounds)
            return;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        var fraction =
            state.Options.Orientation == LayoutAxis.Row
                ? (command.X - bounds.X) / bounds.Width
                : 1 - (command.Y - bounds.Y) / bounds.Height;
        if (state.Options.Direction == SliderDirection.Reverse)
            fraction = 1 - fraction;
        state.Preview(
            state.Options.Minimum
                + Math.Clamp(fraction, 0, 1) * (state.Options.Maximum - state.Options.Minimum)
        );
    }
}
