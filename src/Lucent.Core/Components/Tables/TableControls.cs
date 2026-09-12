namespace Lucent.Core;

internal static partial class Controls
{
    internal static void Table(
        Element element,
        ThemeContext theme,
        TableBinding binding,
        Style? style
    ) => Configure(element, theme, PanelStyle, style, new TableBehavior(binding));

    internal static void TableHeading(
        Element element,
        ThemeContext theme,
        TableColumnBinding binding,
        Style? style
    )
    {
        Configure(
            element,
            theme,
            TextStyle
                .Bind(ProjectionProperties.Text, binding.Caption)
                .Set(
                    InputProperties.Cursor,
                    binding.CanSort ? CursorIntent.Pointer : CursorIntent.Default
                )
                .When(
                    VariantState.Hover,
                    Style.Empty.Set(VisualProperties.Background, ControlThemes.Selected)
                )
                .When(VariantState.FocusVisible, FocusStyle(theme)),
            style,
            new TableHeadingBehavior(binding)
        );
        binding.RegisterHeader(new(element.Composition.Epoch, element.Id));
    }

    internal static void TableCell(
        Element element,
        ThemeContext theme,
        TableCellBinding binding,
        Style? style
    ) =>
        Configure(
            element,
            theme,
            TextStyle
                .Bind(ProjectionProperties.Text, binding.Text)
                .Bind(LayoutProperties.Width, () => (float?)binding.Width())
                .Set(LayoutProperties.MainShrink, 0f)
                .Set(LayoutProperties.Padding, Insets.Symmetric(8, 6)),
            style,
            new TableCellBehavior(binding)
        );

    internal static void TableColumnResize(
        Element element,
        ThemeContext theme,
        TableColumnBinding binding
    ) =>
        Configure(
            element,
            theme,
            PanelStyle
                .Set(LayoutProperties.Width, 8f)
                .Set(LayoutProperties.MainShrink, 0f)
                .Bind(LayoutProperties.Height, () => (float?)binding.HeaderHeight())
                .Set(InputProperties.Cursor, CursorIntent.ResizeHorizontal)
                .Bind(
                    VisualProperties.Border,
                    () =>
                        theme.PresentationMode == ControlPresentationMode.Minimal
                            ? Border.None
                            : Border.Hairline(theme.Token(ControlThemes.Divider), BorderSides.Left)
                )
                .When(
                    VariantState.Hover,
                    Style.Empty.Set(VisualProperties.Background, ControlThemes.Selected)
                )
                .When(
                    VariantState.Pressed,
                    Style.Empty.Set(VisualProperties.Background, ControlThemes.Selected)
                )
                .When(VariantState.FocusVisible, FocusStyle(theme)),
            null,
            new TableColumnResizeBehavior(binding)
        );

    private sealed class TableBehavior(TableBinding binding) : Behavior
    {
        public override string Name => "table";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            SemanticDeclaration Declaration() =>
                new(
                    SemanticRole.Table,
                    binding.Label,
                    actions: SemanticAction.RealizeItem,
                    selection: new(false, false),
                    collection: new(binding.Count(), binding.SelectedIndex()),
                    grid: new(binding.Count(), binding.ColumnCount, binding.Headers())
                );
            context.SetSemantics(Declaration());
            context.Effect(() => context.UpdateSemantics(Declaration()), "table-metadata");
            context.OnSemanticCommand(command =>
            {
                if (
                    command.Kind != SemanticCommandKind.RealizeItem
                    || command.ItemIndex is not { } index
                    || index < 0
                    || index >= binding.Count()
                    || binding.Reveal is null
                )
                    return false;
                binding.Reveal(index);
                return true;
            });
        }
    }

    private sealed class TableCellBehavior(TableCellBinding binding) : Behavior
    {
        public override string Name => "table-cell";
        public override BehaviorOwnership Ownership => BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            SemanticDeclaration Declaration() =>
                new(
                    SemanticRole.Text,
                    string.IsNullOrWhiteSpace(binding.Text()) ? "Empty" : binding.Text(),
                    gridItem: binding.Table() is { } table && binding.Row() is { } row
                        ? new(table, row, binding.Column)
                        : null
                );
            context.SetSemantics(Declaration());
            context.Effect(() => context.UpdateSemantics(Declaration()), "cell-value");
        }
    }

    private sealed class TableHeadingBehavior(TableColumnBinding binding) : Behavior
    {
        public override string Name => "table-heading";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            SemanticDeclaration Declaration() =>
                new(
                    SemanticRole.HeaderItem,
                    binding.Header,
                    value: binding.SortLabel(),
                    actions: binding.CanSort ? SemanticAction.Invoke : SemanticAction.None
                );
            if (binding.CanSort)
                new ButtonBehavior(Name, Declaration(), binding.Sort).Attach(context);
            else
                context.SetSemantics(Declaration());
            context.Effect(() => context.UpdateSemantics(Declaration()), "header-order");
        }
    }

    private sealed class TableColumnResizeBehavior(TableColumnBinding binding) : Behavior
    {
        private int? _pointer;
        private float _startX;
        private float _startWidth;
        public override string Name => "table-column-resize";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            SemanticDeclaration Declaration() =>
                new(
                    SemanticRole.Splitter,
                    binding.Header + " column width",
                    actions: SemanticAction.SetRangeValue,
                    range: new(binding.Width(), binding.Minimum, binding.Maximum, 8, 40)
                );
            context.MakeFocusable();
            context.SetSemantics(Declaration());
            context.Effect(() => context.UpdateSemantics(Declaration()), "column-width");
            context.OnSemanticCommand(command =>
            {
                if (command.Kind == SemanticCommandKind.Focus)
                    return context.Composition.Input.FocusSemantic(context.Identity);
                if (
                    command.Kind != SemanticCommandKind.SetRangeValue
                    || command.NumericValue is not { } width
                    || width < binding.Minimum
                    || width > binding.Maximum
                )
                    return false;
                binding.Resize(width);
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
                        _startX = command.X;
                        _startWidth = binding.Width();
                        context.SetState(BehaviorState.Pressed, true);
                    }
                    route.Handled = true;
                }
                else if (_pointer == command.PointerId)
                {
                    if (
                        command.Kind == PointerCommandKind.Move
                        || command.Releases(PointerButton.Primary)
                    )
                        binding.Resize((double)_startWidth + command.X - _startX);
                    if (
                        command.Kind == PointerCommandKind.Cancel
                        || command.Releases(PointerButton.Primary)
                    )
                    {
                        _pointer = null;
                        context.SetState(BehaviorState.Pressed, false);
                    }
                    route.Handled =
                        command.Kind is PointerCommandKind.Move or PointerCommandKind.Cancel
                        || command.Releases(PointerButton.Primary);
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
                var step = command.Modifiers.HasFlag(KeyModifiers.Shift) ? 40 : 8;
                switch (command.Key)
                {
                    case Key.Left:
                        binding.Resize(binding.Width() - step);
                        break;
                    case Key.Right:
                        binding.Resize(binding.Width() + step);
                        break;
                    case Key.Home:
                        binding.Resize(binding.Minimum);
                        break;
                    case Key.End:
                        binding.Resize(binding.Maximum);
                        break;
                    default:
                        return;
                }
                route.Handled = true;
            });
        }
    }
}
