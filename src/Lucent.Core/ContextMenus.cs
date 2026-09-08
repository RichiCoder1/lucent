namespace Lucent.Core;

/// <summary>The finite entry kinds supported by a platform-rendered standard context menu.</summary>
public enum StandardMenuEntryKind
{
    /// <summary>An invokable semantic command.</summary>
    Command,

    /// <summary>A noninteractive separator between command groups.</summary>
    Separator,
}

/// <summary>One immutable command or separator in a standard context menu.</summary>
public sealed class StandardMenuEntry
{
    internal StandardMenuEntry(
        StandardMenuEntryKind kind,
        string? label,
        SemanticIdentity? identity,
        bool enabled
    )
    {
        Kind = kind;
        Label = label;
        Identity = identity;
        Enabled = enabled;
    }

    /// <summary>Gets whether this entry is a command or separator.</summary>
    public StandardMenuEntryKind Kind { get; }

    /// <summary>Gets the command label, or <see langword="null"/> for a separator.</summary>
    public string? Label { get; }

    /// <summary>Gets the current semantic command identity, or <see langword="null"/> for a separator.</summary>
    public SemanticIdentity? Identity { get; }

    /// <summary>Gets whether the current semantic command is enabled.</summary>
    public bool Enabled { get; }
}

/// <summary>An immutable flat standard-menu projection suitable for platform adapters.</summary>
public sealed class StandardMenuDescriptor
{
    internal StandardMenuDescriptor(IReadOnlyList<StandardMenuEntry> entries) =>
        Entries = Array.AsReadOnly(entries.ToArray());

    /// <summary>Gets commands and separators in authored order.</summary>
    public IReadOnlyList<StandardMenuEntry> Entries { get; }
}

internal enum StandardMenuPart
{
    None,
    Menu,
    Command,
    Separator,
    SeparatorLine,
}

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

    /// <summary>The current portable appearance used by the popup theme.</summary>
    public ThemeAppearance Appearance => _theme.Appearance;

    /// <summary>Whether the target still exists and accepts interaction.</summary>
    public bool IsValid =>
        !Owner.IsDisposed
        && Owner.Find(Target) is { IsDisposed: false } element
        && element.InputAvailable();

    /// <summary>Whether a command, dismissal, or target disposal has ended this menu.</summary>
    public bool IsDismissed => _dismissed || !IsValid;

    /// <summary>Gets a flat platform-menu descriptor when the content uses only unstyled standard menu components.</summary>
    /// <remarks>Custom content, layout, nesting, or explicit styles return <see langword="null"/> so the host can use the Lucent-rendered popup.</remarks>
    public StandardMenuDescriptor? StandardMenu
    {
        get
        {
            if (IsDismissed)
                return null;
            var popup = CreateComposition();
            popup.Flush();
            if (
                _menuRoot is not { StandardMenuPart: StandardMenuPart.Menu } root
                || root.Children.Count == 0
                || root.Children.Any(child =>
                    child.StandardMenuPart is StandardMenuPart.None or StandardMenuPart.Menu
                    || child.StandardMenuPart == StandardMenuPart.Command
                        && child.Children.Count != 0
                    || child.StandardMenuPart == StandardMenuPart.Separator
                        && (
                            child.Children.Count != 1
                            || child.Children[0].StandardMenuPart != StandardMenuPart.SeparatorLine
                            || child.Children[0].Children.Count != 0
                        )
                )
            )
                return null;
            var semantics = popup.SemanticSnapshot();
            if (semantics is null)
                return null;
            var commands = SemanticNodes(semantics)
                .Where(node => node.Role == SemanticRole.MenuItem)
                .ToDictionary(node => node.Identity.ElementId);
            var entries = new List<StandardMenuEntry>(root.Children.Count);
            foreach (var child in root.Children)
            {
                if (child.StandardMenuPart == StandardMenuPart.Separator)
                {
                    entries.Add(new(StandardMenuEntryKind.Separator, null, null, enabled: false));
                    continue;
                }
                if (
                    child.StandardMenuPart != StandardMenuPart.Command
                    || !commands.TryGetValue(child.Id, out var command)
                    || (command.Actions & SemanticAction.Invoke) == 0
                )
                    return null;
                entries.Add(
                    new(
                        StandardMenuEntryKind.Command,
                        command.Name,
                        command.Identity,
                        command.Enabled
                    )
                );
            }
            return new(entries);
        }
    }

    /// <summary>Invokes a descriptor command through the popup's current semantic command surface.</summary>
    public SemanticCommandResult InvokeStandardCommand(SemanticIdentity identity)
    {
        if (IsDismissed || _popup is null)
            return SemanticCommandResult.Stale;
        return _popup.ExecuteSemanticCommand(identity, new(SemanticCommandKind.Invoke));
    }

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

    private static IEnumerable<SemanticSnapshot> SemanticNodes(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in SemanticNodes(child))
            yield return descendant;
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
                if (style is null)
                    root.StandardMenuPart = StandardMenuPart.Menu;
                root.Present(
                    context.Theme,
                    component: Style
                        .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                        .Set(LayoutProperties.MaxWidth, 260f)
                        .Set(LayoutProperties.Padding, Insets.Uniform(6))
                        .Set(LayoutProperties.Clip, true)
                        .Set(VisualProperties.Background, ControlThemes.Surface)
                        .Bind(
                            VisualProperties.Border,
                            () => Border.Hairline(context.Theme.Token(ControlThemes.Disabled))
                        )
                        .Set(VisualProperties.CornerRadius, 8f)
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
                if (style is null)
                    root.StandardMenuPart = StandardMenuPart.Command;
                root.Present(
                    context.Theme,
                    component: Style
                        .Empty.Set(LayoutProperties.Height, 32f)
                        .Set(LayoutProperties.MainShrink, 0f)
                        .Set(LayoutProperties.Padding, Insets.Symmetric(12, 7))
                        .Set(LayoutProperties.Clip, true)
                        .Set(VisualProperties.CornerRadius, 4f)
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
                            Style
                                .Empty.Set(VisualProperties.Background, ControlThemes.Selected)
                                .Set(TypographyProperties.TextColor, ControlThemes.Foreground)
                        )
                        .When(
                            VariantState.Disabled,
                            Style.Empty.Set(VisualProperties.Opacity, 0.55f)
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
        ComponentRecipe.Create(
            "menu-separator",
            (context, root) =>
            {
                if (style is null)
                    root.StandardMenuPart = StandardMenuPart.Separator;
                root.Present(
                    context.Theme,
                    component: Style
                        .Empty.Height(9)
                        .Set(LayoutProperties.MainShrink, 0f)
                        .Set(LayoutProperties.Padding, Insets.Symmetric(0, 4))
                        .Set(VisualProperties.Background, ControlThemes.Surface),
                    author: style
                );
                var line = context.Child(root, "menu-separator-line");
                line.StandardMenuPart = StandardMenuPart.SeparatorLine;
                line.Present(
                    context.Theme,
                    component: Style
                        .Empty.Height(1)
                        .Set(LayoutProperties.MainShrink, 0f)
                        .Set(VisualProperties.Background, ControlThemes.Disabled)
                );
            }
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
        context.OnPointer(route =>
        {
            if (route.Command.Kind == PointerCommandKind.Move)
                route.FocusTarget();
        });
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
