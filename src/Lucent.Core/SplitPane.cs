namespace Lucent.Core;

/// <summary>Hoistable preferred pane size, preserved when available window space changes.</summary>
public sealed class SplitPaneState : IDisposable
{
    private readonly ReactiveScope _scope;
    private readonly Signal<float> _preferred;

    /// <summary>Creates an owner-scoped pane size and finite layout constraints in logical pixels.</summary>
    public SplitPaneState(
        ReactiveScope owner,
        float initialExtent = 320,
        float minimumFirst = 160,
        float minimumSecond = 160,
        LayoutAxis axis = LayoutAxis.Row,
        float splitterThickness = 8,
        string name = "split-pane"
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        CheckExtent(initialExtent, nameof(initialExtent));
        CheckExtent(minimumFirst, nameof(minimumFirst));
        CheckExtent(minimumSecond, nameof(minimumSecond));
        if (!Enum.IsDefined(axis))
            throw new ArgumentOutOfRangeException(nameof(axis));
        if (!float.IsFinite(splitterThickness) || splitterThickness <= 0)
            throw new ArgumentOutOfRangeException(nameof(splitterThickness));
        _scope = owner.CreateChild(name);
        _preferred = _scope.Signal(initialExtent, name + ".preferred");
        Constraints = new(_scope, name + ".constraints");
        MinimumFirst = minimumFirst;
        MinimumSecond = minimumSecond;
        Axis = axis;
        SplitterThickness = splitterThickness;
    }

    /// <summary>Gets or sets the preferred first pane extent, independent of temporary layout clamping.</summary>
    public float PreferredExtent
    {
        get
        {
            CheckRead();
            return _preferred.Value;
        }
        set
        {
            _scope.CheckMutationGuard();
            CheckRead();
            CheckExtent(value, nameof(value));
            _preferred.Value = value;
        }
    }

    /// <summary>Gets the effective first pane extent within the current available space.</summary>
    public float EffectiveExtent => Geometry().Value;

    /// <summary>Gets the preferred minimum first pane extent.</summary>
    public float MinimumFirst { get; }

    /// <summary>Gets the preferred minimum second pane extent.</summary>
    public float MinimumSecond { get; }

    /// <summary>Gets whether panes are arranged in a row or column.</summary>
    public LayoutAxis Axis { get; }

    /// <summary>Gets the splitter hit area's logical thickness.</summary>
    public float SplitterThickness { get; }

    /// <summary>Gets whether this state has released its reactive resources.</summary>
    public bool IsDisposed => _scope.IsDisposed;

    /// <summary>Releases the state and its reactive resources.</summary>
    public void Dispose() => _scope.Dispose();

    internal ResponsiveConstraints Constraints { get; }

    internal (float Value, float Minimum, float Maximum, float Handle) Geometry()
    {
        CheckRead();
        var bounds = Constraints.Current;
        var extent = Axis == LayoutAxis.Row ? bounds.Width : bounds.Height;
        var handle = Math.Min(SplitterThickness, extent);
        var available = Math.Max(0, extent - handle);
        var maximum = Math.Max(0, available - MinimumSecond);
        var minimum = Math.Min(MinimumFirst, maximum);
        return (Math.Clamp(_preferred.Value, minimum, maximum), minimum, maximum, handle);
    }

    internal void Resize(double extent)
    {
        var range = Geometry();
        PreferredExtent = (float)Math.Clamp(extent, range.Minimum, range.Maximum);
    }

    private static void CheckExtent(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }

    private void CheckRead()
    {
        _scope.Graph.CheckThread();
        ObjectDisposedException.ThrowIf(_scope.IsDisposed, this);
    }
}

public static partial class Components
{
    /// <summary>Creates two stable content panes separated by a pointer-, keyboard- and automation-resizable divider.</summary>
    [LucentComponent]
    public static ComponentRecipe SplitPane(
        ComponentContent first,
        ComponentContent second,
        SplitPaneState state,
        string label = "Resize panes",
        Style? style = null,
        Style? splitterStyle = null,
        Style? firstStyle = null,
        Style? secondStyle = null
    )
    {
        first = Content(first);
        second = Content(second);
        ArgumentNullException.ThrowIfNull(state);
        label = Required(label, nameof(label));
        return ComponentRecipe.Create(
            "split-pane",
            (context, root) =>
            {
                state.Constraints.AcquireMount(root.Scope);
                var horizontal = state.Axis == LayoutAxis.Row;
                var extentProperty = horizontal ? LayoutProperties.Width : LayoutProperties.Height;
                root.Present(
                    context.Theme,
                    component: Style
                        .Empty.Set(LayoutProperties.Axis, state.Axis)
                        .Set(LayoutProperties.MainGrow, 1f)
                        .Set(LayoutProperties.Clip, true),
                    author: style
                );
                root.UpdateControl(ProjectionProperties.ResponsiveConstraints, state.Constraints);
                var leading = context.Child(root, "first-pane");
                leading.Present(
                    context.Theme,
                    component: Style
                        .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                        .Set(LayoutProperties.MainShrink, 0f)
                        .Set(LayoutProperties.Clip, true)
                        .Bind(extentProperty, () => state.EffectiveExtent),
                    author: firstStyle
                );
                context.Mount(leading, first);

                var divider = context.Child(root, "splitter");
                divider.Present(
                    context.Theme,
                    component: Style
                        .Empty.Set(
                            LayoutProperties.Axis,
                            horizontal ? LayoutAxis.Column : LayoutAxis.Row
                        )
                        .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Center)
                        .Set(LayoutProperties.MainShrink, 0f)
                        .Bind(extentProperty, () => state.Geometry().Handle)
                        .Set(
                            InputProperties.Cursor,
                            horizontal ? CursorIntent.ResizeHorizontal : CursorIntent.ResizeVertical
                        )
                        .Set(VisualProperties.Background, ControlThemes.Surface)
                        .When(
                            VariantState.Hover,
                            Style.Empty.Set(VisualProperties.Background, ControlThemes.Selected)
                        )
                        .When(
                            VariantState.Pressed,
                            Style.Empty.Set(VisualProperties.Background, ControlThemes.Selected)
                        )
                        .When(
                            VariantState.FocusVisible,
                            Style.Empty.Bind(
                                VisualProperties.FocusRing,
                                () => FocusRing.Inset(context.Theme.Token(ControlThemes.Focus), 2)
                            )
                        ),
                    author: splitterStyle
                );
                divider.AttachBehaviors(new SplitterBehavior(state, label));
                var line = context.Child(divider, "splitter-line");
                line.Present(
                    context.Theme,
                    component: Style
                        .Empty.Set(extentProperty, 1f)
                        .Set(LayoutProperties.MainGrow, 1f)
                        .Set(VisualProperties.Background, ControlThemes.Disabled)
                );

                var trailing = context.Child(root, "second-pane");
                trailing.Present(
                    context.Theme,
                    component: Style
                        .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                        .Set(LayoutProperties.MainGrow, 1f)
                        .Set(LayoutProperties.Clip, true),
                    author: secondStyle
                );
                context.Mount(trailing, second);
            }
        );
    }
}

internal sealed class SplitterBehavior(SplitPaneState state, string label) : Behavior
{
    private int? _pointer;
    private float _startPosition;
    private float _startExtent;

    public override string Name => "splitter";
    public override BehaviorOwnership Ownership =>
        BehaviorOwnership.Focus | BehaviorOwnership.Action | BehaviorOwnership.Semantics;

    public override void Attach(BehaviorContext context)
    {
        SemanticDeclaration Declaration()
        {
            var range = state.Geometry();
            return new(
                SemanticRole.Splitter,
                label,
                actions: SemanticAction.SetRangeValue,
                range: new SemanticRangeSnapshot(range.Value, range.Minimum, range.Maximum, 8, 40)
            );
        }
        context.MakeFocusable();
        context.SetSemantics(Declaration());
        context.Effect(() => context.UpdateSemantics(Declaration()), label + ".range");
        context.OnSemanticCommand(command =>
        {
            if (command.Kind == SemanticCommandKind.Focus)
                return context.Composition.Input.FocusSemantic(context.Identity);
            if (
                command.Kind != SemanticCommandKind.SetRangeValue
                || command.NumericValue is not { } value
            )
                return false;
            var range = state.Geometry();
            if (value < range.Minimum || value > range.Maximum)
                return false;
            state.Resize(value);
            return true;
        });
        context.OnCaptureLost(_ =>
        {
            _pointer = null;
            context.SetState(BehaviorState.Pressed, false);
        });
        context.OnPointer(route =>
        {
            var command = route.Command;
            if (command is { Kind: PointerCommandKind.Down, Button: PointerButton.Primary })
            {
                route.Focus();
                if (route.Capture())
                {
                    _pointer = command.PointerId;
                    _startPosition = Coordinate(command);
                    _startExtent = state.EffectiveExtent;
                    context.SetState(BehaviorState.Pressed, true);
                }
                route.Handled = true;
            }
            else if (_pointer == command.PointerId)
            {
                if (command.Kind is PointerCommandKind.Move or PointerCommandKind.Up)
                    state.Resize((double)_startExtent + Coordinate(command) - _startPosition);
                if (command.Kind is PointerCommandKind.Up or PointerCommandKind.Cancel)
                {
                    _pointer = null;
                    context.SetState(BehaviorState.Pressed, false);
                }
                route.Handled = true;
            }
        });
        context.OnKey(route =>
        {
            var command = route.Command;
            if (
                command.Kind != KeyCommandKind.Down
                || (command.Modifiers & ~KeyModifiers.Shift) != 0
            )
                return;
            var range = state.Geometry();
            var step = command.Modifiers.HasFlag(KeyModifiers.Shift) ? 40 : 8;
            var backward = state.Axis == LayoutAxis.Row ? Key.Left : Key.Up;
            var forward = state.Axis == LayoutAxis.Row ? Key.Right : Key.Down;
            if (command.Key == backward)
                state.Resize(range.Value - step);
            else if (command.Key == forward)
                state.Resize(range.Value + step);
            else if (command.Key == Key.Home)
                state.Resize(range.Minimum);
            else if (command.Key == Key.End)
                state.Resize(range.Maximum);
            else
                return;
            route.Handled = true;
        });
    }

    private float Coordinate(PointerCommand command) =>
        state.Axis == LayoutAxis.Row ? command.X : command.Y;
}
