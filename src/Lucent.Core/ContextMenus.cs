namespace Lucent.Core;

/// <summary>One owner-thread menu invocation, independent of native popup transport.</summary>
/// <remarks>The host queues the request during input routing, then owns popup creation and disposal outside that route.</remarks>
public sealed class ContextMenuRequest : IDisposable
{
    private readonly ComponentRecipe _content;
    private readonly ThemeContext _theme;
    private readonly ElementIdentity? _returnFocus;
    private readonly Action? _onClosed;
    private Composition? _popup;
    private Element? _menuRoot;
    private bool _dismissed;

    internal ContextMenuRequest(
        Composition owner,
        ElementIdentity target,
        LayoutRect anchor,
        ComponentRecipe content,
        ThemeContext theme,
        Action? onClosed = null
    )
    {
        _onClosed = onClosed;
        Owner = owner;
        Target = target;
        Anchor = anchor;
        _content = content;
        _theme = theme;
        _returnFocus = owner.Input.FocusedElement;
    }

    /// <summary>The composition whose command context opened the menu.</summary>
    public Composition Owner { get; }

    /// <summary>The retained invocation target; this need not be the selected document.</summary>
    public ElementIdentity Target { get; }

    /// <summary>Preferred anchor in owner-client logical coordinates.</summary>
    public LayoutRect Anchor { get; }

    /// <summary>Whether the target still exists and accepts interaction.</summary>
    public bool IsValid =>
        !Owner.IsDisposed
        && Owner.Find(Target) is { IsDisposed: false } element
        && element.InputAvailable();

    /// <summary>Whether a command, dismissal, or target disposal has ended this menu.</summary>
    public bool IsDismissed => _dismissed || !IsValid;

    /// <summary>Mounts the popup once on the same reactive graph and UI owner as its caller.</summary>
    public Composition CreateComposition()
    {
        ObjectDisposedException.ThrowIf(IsDismissed, this);
        if (_popup is not null)
            return _popup;
        var popup = new Composition(Owner.Graph, "context-menu");
        try
        {
            popup.MenuSession = this;
            var theme = new ThemeContext(
                popup.Root.Scope,
                _theme.Theme,
                _theme.ReducedMotion,
                _theme.Appearance
            );
            _ = popup.Root.Scope.Effect(
                () =>
                {
                    theme.Theme = _theme.Theme;
                    theme.ReducedMotion = _theme.ReducedMotion;
                    theme.Appearance = _theme.Appearance;
                },
                "popup-theme"
            );
            _menuRoot = popup.Mount(popup.Root, theme, _content);
            _popup = popup;
            return popup;
        }
        catch
        {
            popup.Dispose();
            throw;
        }
    }

    /// <summary>Measures the authored menu within the host's available logical work area.</summary>
    public LayoutRect Measure(ITextShaper shaper, LayoutViewport available)
    {
        var popup = CreateComposition();
        var scene = SceneLayout.Project(popup, available, shaper);
        var bounds = scene.Boxes.Single(box => box.Identity.ElementId == _menuRoot!.Id).Bounds;
        return new(
            0,
            0,
            Math.Clamp(bounds.Width, 1, available.Width),
            Math.Clamp(bounds.Height, 1, available.Height)
        );
    }

    /// <summary>Requests host dismissal without running a nested native loop.</summary>
    public void Dismiss()
    {
        if (_dismissed)
            return;
        _dismissed = true;
        if (!Owner.IsDisposed && Owner.Find(Target) is { IsDisposed: false })
            _onClosed?.Invoke();
    }

    /// <summary>Restores the prior retained focus if its owner is still available.</summary>
    public bool RestoreFocus()
    {
        if (Owner.IsDisposed || _returnFocus is not { } focus)
            return false;
        var current = Owner.Input.FocusedElement;
        // Do not change modality or override focus deliberately moved by the invoked command.
        return current == focus || current is null && Owner.Input.FocusSemantic(focus);
    }

    /// <summary>Releases the temporary menu composition; accepted application commands keep their original owners.</summary>
    public void Dispose()
    {
        try
        {
            Dismiss();
        }
        finally
        {
            var popup = _popup;
            _popup = null;
            _menuRoot = null;
            popup?.Dispose();
        }
    }
}

public static partial class Components
{
    /// <summary>Adds a lazily constructed context menu to the supplied subtree without changing primary activation.</summary>
    [LucentComponent]
    public static ComponentRecipe ContextMenu(
        [DefaultContent] ComponentContent content,
        Func<ComponentRecipe> menu,
        Style? style = null,
        Action<bool>? onOpenChanged = null
    )
    {
        ArgumentNullException.ThrowIfNull(menu);
        content = Content(content);
        return ComponentRecipe.Create(
            "context-menu-target",
            (context, root) =>
            {
                root.Present(
                    context.Theme,
                    author: Style.Compose(
                        Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Column),
                        style ?? Style.Empty
                    )
                );
                root.AttachBehaviors(new ContextMenuBehavior(menu, context.Theme, onOpenChanged));
                context.Mount(root, content);
            }
        );
    }

    /// <summary>Creates a themed menu whose popup hosting is supplied by the platform.</summary>
    [LucentComponent]
    public static ComponentRecipe Menu(
        [DefaultContent] ComponentContent content,
        Style? style = null
    )
    {
        content = Content(content);
        return ComponentRecipe.Create(
            "menu",
            (context, root) =>
            {
                root.Present(
                    context.Theme,
                    component: Style
                        .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                        .Set(LayoutProperties.Width, 260f)
                        .Set(LayoutProperties.Padding, Insets.Uniform(4))
                        .Set(VisualProperties.Background, ControlThemes.Surface)
                        .Set(TypographyProperties.TextColor, ControlThemes.Foreground),
                    author: style
                );
                root.AttachBehaviors(new MenuBehavior());
                context.Mount(root, content);
            }
        );
    }

    /// <summary>Creates an invokable menu item. Availability is checked again when invoked.</summary>
    [LucentComponent]
    public static ComponentRecipe MenuItem(
        [DefaultContent] string content,
        Action onInvoke,
        Func<bool>? enabled = null,
        Style? style = null
    )
    {
        content = Required(content, nameof(content));
        ArgumentNullException.ThrowIfNull(onInvoke);
        return ComponentRecipe.Create(
            "menu-item",
            (context, root) =>
            {
                root.Present(
                    context.Theme,
                    component: Style
                        .Empty.Set(LayoutProperties.Height, 32f)
                        .Set(LayoutProperties.MainShrink, 0f)
                        .Set(LayoutProperties.Padding, Insets.Symmetric(10, 6))
                        .Set(LayoutProperties.Clip, true)
                        .Set(ProjectionProperties.Text, content)
                        .Set(TypographyProperties.FontSize, 14f)
                        .Set(TypographyProperties.TextColor, ControlThemes.Foreground)
                        .Set(VisualProperties.Background, ControlThemes.Surface)
                        .When(
                            VariantState.Hover,
                            Style.Empty.Set(VisualProperties.Background, ControlThemes.Selected)
                        )
                        .When(
                            VariantState.FocusVisible,
                            Style.Empty.Set(VisualProperties.Background, ControlThemes.Selected)
                        )
                        .When(
                            VariantState.Disabled,
                            Style.Empty.Set(TypographyProperties.TextColor, Color.Parse("#808080"))
                        ),
                    author: style
                );
                root.AttachBehaviors(
                    new ButtonBehavior(
                        "menu-item",
                        new(SemanticRole.MenuItem, content, actions: SemanticAction.Invoke),
                        () =>
                        {
                            if (!(enabled?.Invoke() ?? true))
                                return;
                            root.Composition.MenuSession?.Dismiss();
                            onInvoke();
                        }
                    )
                );
                if (enabled is not null)
                    _ = root.Scope.Effect(
                        () => root.UpdateControl(InputProperties.Enabled, enabled()),
                        "menu-item-enabled"
                    );
            }
        );
    }

    /// <summary>Creates an item backed by an application-owned asynchronous command.</summary>
    [LucentComponent]
    public static ComponentRecipe MenuItem(
        [DefaultContent] string content,
        ApplicationCommand command,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(command);
        return MenuItem(content, () => command.TryExecute(), () => command.IsEnabled, style);
    }

    /// <summary>Creates a noninteractive visual separator between command groups.</summary>
    [LucentComponent]
    public static ComponentRecipe MenuSeparator(Style? style = null) =>
        Column(
            [],
            Style.Compose(
                Style
                    .Empty.Height(1)
                    .Set(LayoutProperties.MainShrink, 0f)
                    .Set(VisualProperties.Background, (Brush)Color.Parse("#d4d4d4")),
                style ?? Style.Empty
            )
        );
}

internal sealed class ContextMenuBehavior(
    Func<ComponentRecipe> menu,
    ThemeContext theme,
    Action<bool>? onOpenChanged
) : Behavior
{
    public override string Name => "context-menu";
    public override BehaviorOwnership Ownership => BehaviorOwnership.None;

    public override void Attach(BehaviorContext context) =>
        context.RegisterContextMenu(menu, theme, onOpenChanged);
}

internal sealed class MenuBehavior : Behavior
{
    public override string Name => "menu";
    public override BehaviorOwnership Ownership =>
        BehaviorOwnership.Action | BehaviorOwnership.Semantics;

    public override void Attach(BehaviorContext context)
    {
        context.SetSemantics(new(SemanticRole.Menu, "Context menu"));
        context.OnKey(route =>
        {
            if (route.Command.Kind != KeyCommandKind.Down)
                return;
            var router = context.CompositionInput();
            switch (route.Command.Key)
            {
                case Key.Escape:
                case Key.Tab:
                    router.DismissMenu();
                    break;
                case Key.Down:
                    router.MoveMenuFocus(FocusTraversalDirection.Next);
                    break;
                case Key.Up:
                    router.MoveMenuFocus(FocusTraversalDirection.Previous);
                    break;
                case Key.Home:
                    router.FocusMenuBoundary(first: true);
                    break;
                case Key.End:
                    router.FocusMenuBoundary(first: false);
                    break;
                default:
                    return;
            }
            route.Handled = true;
        });
    }
}
