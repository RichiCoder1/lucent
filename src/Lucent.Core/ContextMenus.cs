namespace Lucent.Core;

/// <summary>The finite entry kinds supported by a platform-rendered standard context menu.</summary>
public enum StandardMenuEntryKind
{
    /// <summary>An invokable semantic command.</summary>
    Command,

    /// <summary>A noninteractive separator between command groups.</summary>
    Separator,

    /// <summary>A command that opens a nested standard menu.</summary>
    Submenu,
}

/// <summary>One immutable command or separator in a standard context menu.</summary>
public sealed class StandardMenuEntry
{
    internal StandardMenuEntry(
        StandardMenuEntryKind kind,
        string? label,
        SemanticIdentity? identity,
        bool enabled,
        StandardMenuDescriptor? submenu = null
    )
    {
        Kind = kind;
        Label = label;
        Identity = identity;
        Enabled = enabled;
        Submenu = submenu;
    }

    /// <summary>Gets whether this entry is a command or separator.</summary>
    public StandardMenuEntryKind Kind { get; }

    /// <summary>Gets the command label, or <see langword="null"/> for a separator.</summary>
    public string? Label { get; }

    /// <summary>Gets the current semantic command identity, or <see langword="null"/> for a separator.</summary>
    public SemanticIdentity? Identity { get; }

    /// <summary>Gets whether the current semantic command is enabled.</summary>
    public bool Enabled { get; }

    /// <summary>Gets the nested descriptor for a submenu entry.</summary>
    public StandardMenuDescriptor? Submenu { get; }
}

/// <summary>An immutable bounded standard-menu projection suitable for platform adapters.</summary>
public sealed class StandardMenuDescriptor
{
    internal StandardMenuDescriptor(IReadOnlyList<StandardMenuEntry> entries) =>
        Entries = Array.AsReadOnly(entries.ToArray());

    /// <summary>Gets commands and separators in authored order.</summary>
    public IReadOnlyList<StandardMenuEntry> Entries { get; }
}

/// <summary>One currently hosted level in a rendered context-menu chain.</summary>
public sealed class MenuLevelSnapshot
{
    internal MenuLevelSnapshot(
        int depth,
        Composition composition,
        ElementIdentity? parentTrigger,
        bool focusFirst
    )
    {
        Depth = depth;
        Composition = composition;
        ParentTrigger = parentTrigger;
        FocusFirst = focusFirst;
    }

    /// <summary>Gets the zero-based level depth.</summary>
    public int Depth { get; }

    /// <summary>Gets the composition the host projects in this popup level.</summary>
    public Composition Composition { get; }

    /// <summary>Gets the parent-level trigger used to place this level.</summary>
    public ElementIdentity? ParentTrigger { get; }

    /// <summary>Gets whether the host should focus the first item after installing this level's scene.</summary>
    public bool FocusFirst { get; }
}

internal enum StandardMenuPart
{
    None,
    Menu,
    Command,
    Submenu,
    Separator,
    SeparatorLine,
}

/// <summary>One owner-thread menu invocation, independent of native popup transport.</summary>
/// <remarks>The host queues the request during input routing, then owns popup creation and disposal outside that route.</remarks>
public sealed class ContextMenuRequest : PopupSurfaceRequest
{
    private readonly ComponentRecipe _content;
    private readonly ThemeContext _theme;
    private readonly ElementIdentity? _returnFocus;
    private readonly Action? _onClosed;
    private Composition? _popup;
    private Element? _menuRoot;
    private readonly List<MenuLevel> _levels = [];
    private readonly Dictionary<ElementIdentity, SubmenuRegistration> _submenus = [];
    private bool _dismissed;

    private const int MaximumDescriptorDepth = 8;
    private const int MaximumDescriptorEntries = 512;

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
    public override Composition Owner { get; }

    /// <summary>The retained invocation target; this need not be the selected document.</summary>
    public ElementIdentity Target { get; }

    /// <summary>Preferred anchor in owner-client logical coordinates.</summary>
    public override LayoutRect Anchor { get; }

    /// <summary>The current portable appearance used by the popup theme.</summary>
    public override ThemeAppearance Appearance => _theme.Appearance;

    /// <summary>Whether the target still exists and accepts interaction.</summary>
    public override bool IsValid =>
        !Owner.IsDisposed
        && Owner.Find(Target) is { IsDisposed: false } element
        && element.InputAvailable();

    /// <summary>Whether a command, dismissal, or target disposal has ended this menu.</summary>
    public override bool IsDismissed => _dismissed || !IsValid;

    /// <summary>Gets the root-to-leaf popup levels the rendered host currently owns.</summary>
    public IReadOnlyList<MenuLevelSnapshot> ActiveLevels =>
        Array.AsReadOnly(
            _levels.Where(level => level.Active).Select(level => level.Snapshot).ToArray()
        );

    /// <summary>Raised after the active rendered popup chain changes.</summary>
    public event Action? ActiveLevelsChanged;

    /// <summary>Gets a bounded platform-menu tree when the content uses only unstyled standard menu components.</summary>
    /// <remarks>Custom content, layout, explicit styles, or a tree beyond the fixed budgets return <see langword="null"/> so the host can use the Lucent-rendered popup.</remarks>
    public StandardMenuDescriptor? StandardMenu
    {
        get
        {
            if (IsDismissed)
                return null;
            var popup = CreateComposition();
            var remaining = MaximumDescriptorEntries;
            return BuildStandardMenu(popup, _menuRoot!, 1, ref remaining);
        }
    }

    /// <summary>Invokes a descriptor command through the popup's current semantic command surface.</summary>
    public SemanticCommandResult InvokeStandardCommand(SemanticIdentity identity)
    {
        if (IsDismissed)
            return SemanticCommandResult.Stale;
        var level = _levels.FirstOrDefault(item =>
            item.Composition.Epoch == identity.CompositionEpoch
        );
        return level is null
            ? SemanticCommandResult.Stale
            : level.Composition.ExecuteSemanticCommand(identity, new(SemanticCommandKind.Invoke));
    }

    /// <summary>Mounts the popup once on the same reactive graph and UI owner as its caller.</summary>
    public override Composition CreateComposition()
    {
        ObjectDisposedException.ThrowIf(IsDismissed, this);
        if (_popup is not null)
            return _popup;
        var popup = new Composition(Owner.Graph, "context-menu");
        try
        {
            popup.ShareImagesFrom(Owner);
            popup.MenuSession = this;
            var theme = new ThemeContext(
                popup.Root.Scope,
                _theme.Theme,
                _theme.ReducedMotion,
                _theme.Appearance,
                _theme.PresentationMode
            );
            _ = popup.Root.Scope.Effect(
                () =>
                {
                    theme.Theme = _theme.Theme;
                    theme.ReducedMotion = _theme.ReducedMotion;
                    theme.Appearance = _theme.Appearance;
                    theme.PresentationMode = _theme.PresentationMode;
                },
                "popup-theme"
            );
            _menuRoot = popup.Mount(popup.Root, theme, _content);
            _popup = popup;
            _levels.Add(new MenuLevel(popup, _menuRoot, 0, null, active: true, focusFirst: false));
            return popup;
        }
        catch
        {
            popup.Dispose();
            throw;
        }
    }

    /// <summary>Measures the authored menu within the host's available logical work area.</summary>
    public override LayoutRect Measure(ITextShaper shaper, LayoutViewport available)
    {
        var popup = CreateComposition();
        ConstrainMenuHeight(_menuRoot!, available.Height);
        using var scene = SceneLayout.Project(popup, available, shaper);
        var bounds = scene.Boxes.Single(box => box.Identity.ElementId == _menuRoot!.Id).Bounds;
        return new(
            0,
            0,
            Math.Clamp(bounds.Width, 1, available.Width),
            Math.Clamp(bounds.Height, 1, available.Height)
        );
    }

    /// <summary>Measures one active popup level within its host-provided logical work area.</summary>
    public LayoutRect Measure(MenuLevelSnapshot level, ITextShaper shaper, LayoutViewport available)
    {
        var retained = RequireLevel(level, active: true);
        ConstrainMenuHeight(retained.Root, available.Height);
        using var scene = SceneLayout.Project(retained.Composition, available, shaper);
        var bounds = scene.Boxes.Single(box => box.Identity.ElementId == retained.Root.Id).Bounds;
        return new(
            0,
            0,
            Math.Clamp(bounds.Width, 1, available.Width),
            Math.Clamp(bounds.Height, 1, available.Height)
        );
    }

    private static void ConstrainMenuHeight(Element root, float availableHeight)
    {
        if (float.IsFinite(availableHeight) && availableHeight > 0)
            root.UpdateControl(LayoutProperties.MaxHeight, availableHeight);
    }

    /// <summary>Focuses the first eligible item after a host installs the level's projected scene.</summary>
    public bool FocusFirst(MenuLevelSnapshot level)
    {
        var retained = RequireLevel(level, active: true);
        return retained.Composition.MenuSession == this
            && retained.Composition.Input.FocusMenuBoundary(first: true);
    }

    /// <summary>Closes this rendered level and descendants, restoring focus to its parent trigger.</summary>
    public bool CloseLevel(MenuLevelSnapshot level)
    {
        var retained = RequireLevel(level, active: true);
        return CloseLevel(retained, restoreFocus: true);
    }

    internal void RegisterSubmenu(
        ElementIdentity trigger,
        BehaviorContext context,
        Func<ComponentRecipe> content,
        ThemeContext theme,
        Func<bool>? enabled
    )
    {
        if (_submenus.ContainsKey(trigger))
            throw new InvalidOperationException("A menu trigger may own only one submenu.");
        _submenus.Add(trigger, new(trigger, context, content, theme, enabled));
    }

    internal bool OpenSubmenu(ElementIdentity trigger, bool focusFirst)
    {
        if (IsDismissed || !_submenus.TryGetValue(trigger, out var registration))
            return false;
        var parent = _levels.FirstOrDefault(level =>
            level.Active && ReferenceEquals(level.Composition, registration.ContextComposition)
        );
        if (parent is null || parent.Depth + 1 >= MaximumDescriptorDepth)
            return false;
        if (registration.Level is { Active: true } open && registration.Available)
        {
            // Ordinary motion within a trigger must not close/reopen its branch,
            // invalidate accessibility identities, or restart pointer-intent time.
            if (focusFirst)
                _ = open.Composition.Input.FocusMenuBoundary(first: true);
            return true;
        }
        CloseDescendants(parent.Depth);
        var child = GetOrCreateSubmenu(registration, parent.Depth + 1);
        if (child is null || !registration.Available)
            return false;
        child.Active = true;
        child.Snapshot = new(child.Depth, child.Composition, trigger, focusFirst);
        registration.SetExpanded(true);
        ActiveLevelsChanged?.Invoke();
        return true;
    }

    internal bool CloseCurrentLevel(Composition composition)
    {
        var level = _levels.FirstOrDefault(item =>
            item.Active && ReferenceEquals(item.Composition, composition)
        );
        if (level is null)
            return false;
        if (level.Depth == 0)
            return false;
        return CloseLevel(level, restoreFocus: true);
    }

    internal bool CollapseSubmenu(ElementIdentity trigger)
    {
        if (
            !_submenus.TryGetValue(trigger, out var registration)
            || registration.Level is not { Active: true } level
        )
            return false;
        return CloseLevel(level, restoreFocus: true);
    }

    internal void RefreshSubmenuAvailability(ElementIdentity trigger, bool enabled)
    {
        if (!_submenus.TryGetValue(trigger, out var registration))
            return;
        registration.SetEnabled(enabled);
        if (!registration.Available && registration.Level is { Active: true } level)
            _ = CloseLevel(level, restoreFocus: true);
    }

    internal void SelectPointerTarget(Composition composition, ElementIdentity target)
    {
        var parent = _levels.FirstOrDefault(level =>
            level.Active && ReferenceEquals(level.Composition, composition)
        );
        if (parent is null)
            return;
        var child = _levels.FirstOrDefault(level =>
            level.Active && level.Depth == parent.Depth + 1
        );
        if (child is null || child.ParentTrigger == target)
            return;
        CloseDescendants(parent.Depth);
        ActiveLevelsChanged?.Invoke();
    }

    private MenuLevel RequireLevel(MenuLevelSnapshot snapshot, bool active)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var level = _levels.FirstOrDefault(item => ReferenceEquals(item.Snapshot, snapshot));
        if (level is null || active && !level.Active || IsDismissed)
            throw new InvalidOperationException("The menu level is no longer active.");
        return level;
    }

    private bool CloseLevel(MenuLevel level, bool restoreFocus)
    {
        if (!level.Active)
            return false;
        if (level.Depth == 0)
        {
            Dismiss();
            return true;
        }
        var trigger = level.ParentTrigger;
        if (restoreFocus && trigger is { } identity)
        {
            var parent = _levels.FirstOrDefault(item =>
                item.Active && item.Composition.Epoch == identity.CompositionEpoch
            );
            if (parent is not null && !parent.Composition.Input.FocusSemantic(identity))
                _ = parent.Composition.Input.FocusMenuAfter(identity);
        }
        CloseDescendants(level.Depth - 1);
        ActiveLevelsChanged?.Invoke();
        return true;
    }

    private void CloseDescendants(int parentDepth)
    {
        foreach (var level in _levels.Where(item => item.Active && item.Depth > parentDepth))
        {
            level.Active = false;
            if (
                level.ParentTrigger is { } trigger
                && _submenus.TryGetValue(trigger, out var registration)
            )
                registration.SetExpanded(false);
        }
    }

    private MenuLevel? GetOrCreateSubmenu(SubmenuRegistration registration, int depth)
    {
        if (!(registration.Enabled?.Invoke() ?? true))
        {
            registration.SetEnabled(false);
            return null;
        }
        if (registration.Level is { } existing)
        {
            registration.SetContentAvailable(existing.Root.Children.Count != 0);
            return existing;
        }
        var content =
            registration.Content()
            ?? throw new InvalidOperationException("A submenu factory returned no recipe.");
        var popup = new Composition(Owner.Graph, "context-submenu");
        try
        {
            popup.ShareImagesFrom(Owner);
            popup.MenuSession = this;
            var theme = new ThemeContext(
                popup.Root.Scope,
                registration.Theme.Theme,
                registration.Theme.ReducedMotion,
                registration.Theme.Appearance,
                registration.Theme.PresentationMode
            );
            _ = popup.Root.Scope.Effect(
                () =>
                {
                    theme.Theme = registration.Theme.Theme;
                    theme.ReducedMotion = registration.Theme.ReducedMotion;
                    theme.Appearance = registration.Theme.Appearance;
                    theme.PresentationMode = registration.Theme.PresentationMode;
                },
                "submenu-theme"
            );
            var root = popup.Mount(popup.Root, theme, content);
            popup.Flush();
            if (root.StandardMenuPart != StandardMenuPart.Menu)
                throw new InvalidOperationException(
                    "A submenu factory must return Components.Menu."
                );
            var level = new MenuLevel(
                popup,
                root,
                depth,
                registration.Trigger,
                active: false,
                focusFirst: false
            );
            registration.Level = level;
            _levels.Add(level);
            registration.SetContentAvailable(root.Children.Count != 0);
            return level;
        }
        catch
        {
            popup.Dispose();
            throw;
        }
    }

    private StandardMenuDescriptor? BuildStandardMenu(
        Composition composition,
        Element root,
        int depth,
        ref int remaining
    )
    {
        composition.Flush();
        if (
            depth > MaximumDescriptorDepth
            || root.StandardMenuPart != StandardMenuPart.Menu
            || root.Children.Count == 0
        )
            return null;
        var nested = new Dictionary<long, StandardMenuDescriptor?>();
        foreach (var child in root.Children)
        {
            if (--remaining < 0)
                return null;
            switch (child.StandardMenuPart)
            {
                case StandardMenuPart.Command when child.Children.Count == 0:
                    break;
                case StandardMenuPart.Submenu when child.Children.Count == 0:
                    var trigger = new ElementIdentity(composition.Epoch, child.Id);
                    if (!_submenus.TryGetValue(trigger, out var registration))
                        return null;
                    var childLevel = GetOrCreateSubmenu(registration, depth);
                    if (childLevel is null)
                    {
                        nested[child.Id] = new StandardMenuDescriptor([]);
                        break;
                    }
                    nested[child.Id] =
                        childLevel.Root.Children.Count == 0
                            ? new StandardMenuDescriptor([])
                            : BuildStandardMenu(
                                childLevel.Composition,
                                childLevel.Root,
                                depth + 1,
                                ref remaining
                            );
                    if (nested[child.Id] is null)
                        return null;
                    break;
                case StandardMenuPart.Separator
                    when child.Children.Count == 1
                        && child.Children[0].StandardMenuPart == StandardMenuPart.SeparatorLine
                        && child.Children[0].Children.Count == 0:
                    break;
                default:
                    return null;
            }
        }
        var semantics = composition.SemanticSnapshot();
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
            if (!commands.TryGetValue(child.Id, out var command))
                return null;
            if (
                child.StandardMenuPart == StandardMenuPart.Command
                && (command.Actions & SemanticAction.Invoke) != 0
            )
                entries.Add(
                    new(
                        StandardMenuEntryKind.Command,
                        command.Name,
                        command.Identity,
                        command.Enabled
                    )
                );
            else if (
                child.StandardMenuPart == StandardMenuPart.Submenu
                && (command.Actions & SemanticAction.ExpandCollapse) != 0
            )
                entries.Add(
                    new(
                        StandardMenuEntryKind.Submenu,
                        command.Name,
                        command.Identity,
                        command.Enabled,
                        nested[child.Id]
                    )
                );
            else
                return null;
        }
        return new(entries);
    }

    /// <summary>Requests host dismissal without running a nested native loop.</summary>
    public override void Dismiss()
    {
        if (_dismissed)
            return;
        _dismissed = true;
        foreach (var level in _levels)
            level.Composition.Input.ClearPendingMenuFocus();
        CloseDescendants(-1);
        ActiveLevelsChanged?.Invoke();
        if (!Owner.IsDisposed && Owner.Find(Target) is { IsDisposed: false })
            _onClosed?.Invoke();
    }

    /// <summary>Restores the prior retained focus if its owner is still available.</summary>
    public override bool RestoreFocus()
    {
        if (Owner.IsDisposed || _returnFocus is not { } focus)
            return false;
        var current = Owner.Input.FocusedElement;
        // Do not change modality or override focus deliberately moved by the invoked command.
        return current == focus || current is null && Owner.Input.FocusSemantic(focus);
    }

    /// <summary>Releases the temporary menu composition; accepted application commands keep their original owners.</summary>
    public override void Dispose()
    {
        try
        {
            Dismiss();
        }
        finally
        {
            var levels = _levels.ToArray();
            _popup = null;
            _menuRoot = null;
            _levels.Clear();
            _submenus.Clear();
            foreach (var level in levels.Reverse())
                level.Composition.Dispose();
        }
    }

    private static IEnumerable<SemanticSnapshot> SemanticNodes(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in SemanticNodes(child))
            yield return descendant;
    }

    private sealed class MenuLevel
    {
        internal MenuLevel(
            Composition composition,
            Element root,
            int depth,
            ElementIdentity? parentTrigger,
            bool active,
            bool focusFirst
        )
        {
            Composition = composition;
            Root = root;
            Depth = depth;
            ParentTrigger = parentTrigger;
            Active = active;
            Snapshot = new(depth, composition, parentTrigger, focusFirst);
        }

        internal Composition Composition { get; }
        internal Element Root { get; }
        internal int Depth { get; }
        internal ElementIdentity? ParentTrigger { get; }
        internal bool Active { get; set; }
        internal MenuLevelSnapshot Snapshot { get; set; }
    }

    private sealed class SubmenuRegistration(
        ElementIdentity trigger,
        BehaviorContext context,
        Func<ComponentRecipe> content,
        ThemeContext theme,
        Func<bool>? enabled
    )
    {
        internal ElementIdentity Trigger { get; } = trigger;
        internal BehaviorContext Context { get; } = context;
        internal Composition ContextComposition => Context.Composition;
        internal Func<ComponentRecipe> Content { get; } = content;
        internal ThemeContext Theme { get; } = theme;
        internal Func<bool>? Enabled { get; } = enabled;
        internal MenuLevel? Level { get; set; }
        internal bool Available { get; private set; } = enabled?.Invoke() ?? true;
        private bool? ContentAvailable { get; set; }

        internal void SetContentAvailable(bool available)
        {
            ContentAvailable = available;
            SetEnabled(Enabled?.Invoke() ?? true);
        }

        internal void SetEnabled(bool enabled)
        {
            Available = enabled && ContentAvailable != false;
            Context.UpdateControl(InputProperties.Enabled, Available);
            SetExpanded(Level?.Active == true && Available);
        }

        internal void SetExpanded(bool expanded) =>
            Context.UpdateSemantics(
                new(
                    SemanticRole.MenuItem,
                    Context.SemanticName,
                    enabled: Available,
                    actions: SemanticAction.ExpandCollapse,
                    expanded: expanded
                )
            );
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
                        .Set(ScrollBarProperties.Visibility, ScrollBarVisibility.Auto)
                        .Set(ScrollBarProperties.Thickness, 12f)
                        .Set(ScrollBarProperties.MinimumThumbLength, 24f)
                        .Set(ScrollBarProperties.TrackBrush, ControlThemes.ScrollTrack)
                        .Set(ScrollBarProperties.ThumbBrush, ControlThemes.ScrollThumb)
                        .Set(ScrollBarProperties.HoverThumbBrush, ControlThemes.ScrollThumbHover)
                        .Set(
                            ScrollBarProperties.PressedThumbBrush,
                            ControlThemes.ScrollThumbPressed
                        )
                        .Set(ScrollBarProperties.ThumbCornerRadius, 6f)
                        .Set(VisualProperties.Background, ControlThemes.Surface)
                        .Bind(
                            VisualProperties.Border,
                            () => Border.Hairline(context.Theme.Token(ControlThemes.Disabled))
                        )
                        .Set(VisualProperties.CornerRadius, 8f)
                        .Set(TypographyProperties.TextColor, ControlThemes.Foreground),
                    author: style
                );
                var scroll = new ScrollViewportState(root.Scope, root.Name + ".scroll", default);
                _ = root.Scope.Effect(
                    () => root.UpdateControl(LayoutProperties.Scroll, scroll.Offset),
                    root.Name + ".scroll-state"
                );
                root.AttachBehaviors(new MenuBehavior(scroll));
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

    /// <summary>Creates a menu item whose nested menu is constructed only when inspected or opened.</summary>
    [LucentComponent]
    public static ComponentRecipe MenuSubmenu(
        [DefaultContent] string content,
        Func<ComponentRecipe> menu,
        Func<bool>? enabled = null,
        Style? style = null
    )
    {
        content = Required(content, nameof(content));
        ArgumentNullException.ThrowIfNull(menu);
        return ComponentRecipe.Create(
            "menu-submenu",
            (context, root) =>
            {
                if (style is null)
                    root.StandardMenuPart = StandardMenuPart.Submenu;
                root.Present(
                    context.Theme,
                    component: Style
                        .Empty.Set(LayoutProperties.Height, 32f)
                        .Set(LayoutProperties.MainShrink, 0f)
                        .Set(LayoutProperties.Padding, Insets.Symmetric(12, 7))
                        .Set(LayoutProperties.Clip, true)
                        .Set(VisualProperties.CornerRadius, 4f)
                        .Set(ProjectionProperties.Text, content + "  ›")
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
                            Style.Empty.Set(VisualProperties.Opacity, 0.55f)
                        ),
                    author: style
                );
                root.AttachBehaviors(
                    new MenuSubmenuBehavior(content, menu, context.Theme, enabled)
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

internal sealed class MenuBehavior(ScrollViewportState scroll) : Behavior
{
    public override string Name => "menu";
    public override BehaviorOwnership Ownership =>
        BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics;

    public override void Attach(BehaviorContext context)
    {
        context.SetSemantics(
            new(SemanticRole.Menu, "Context menu", actions: SemanticAction.Scroll)
        );
        context.MakeFocusable(tabStop: false);
        context.RegisterScrollable(scroll);
        context.OnSemanticCommand(command =>
            command.Kind == SemanticCommandKind.Focus
                ? context.CompositionInput().FocusSemantic(context.Identity)
                : command.Kind == SemanticCommandKind.Scroll
                    && context.CompositionInput().ScrollSemantic(context.Identity, command)
        );
        context.OnPointer(route =>
        {
            if (route.Command.Kind == PointerCommandKind.Move)
            {
                context.Composition.MenuSession?.SelectPointerTarget(
                    context.Composition,
                    route.Target
                );
                route.FocusTarget();
            }
        });
        context.OnKey(route =>
        {
            if (route.Command.Kind != KeyCommandKind.Down)
                return;
            var router = context.CompositionInput();
            switch (route.Command.Key)
            {
                case Key.Escape:
                    if (
                        !(
                            context.Composition.MenuSession?.CloseCurrentLevel(context.Composition)
                            ?? false
                        )
                    )
                        router.DismissMenu();
                    break;
                case Key.Tab:
                    router.DismissMenu();
                    break;
                case Key.Left:
                    if (
                        !(
                            context.Composition.MenuSession?.CloseCurrentLevel(context.Composition)
                            ?? false
                        )
                    )
                        return;
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

internal sealed class MenuSubmenuBehavior(
    string label,
    Func<ComponentRecipe> menu,
    ThemeContext theme,
    Func<bool>? enabled
) : Behavior
{
    public override string Name => "menu-submenu";
    public override BehaviorOwnership Ownership =>
        BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics;

    public override void Attach(BehaviorContext context)
    {
        var available = enabled?.Invoke() ?? true;
        context.SetSemantics(
            new(
                SemanticRole.MenuItem,
                label,
                enabled: available,
                actions: SemanticAction.ExpandCollapse,
                expanded: false
            )
        );
        context.MakeFocusable();
        context.RegisterMenuSubmenu(menu, theme, enabled);
        context.OnSemanticCommand(command =>
            command.Kind switch
            {
                SemanticCommandKind.Focus => context
                    .CompositionInput()
                    .FocusSemantic(context.Identity),
                SemanticCommandKind.Expand => context.Composition.MenuSession?.OpenSubmenu(
                    context.Identity,
                    focusFirst: true
                ) == true,
                SemanticCommandKind.Collapse => context.Composition.MenuSession?.CollapseSubmenu(
                    context.Identity
                ) == true,
                _ => false,
            }
        );
        context.OnPointer(route =>
        {
            if (route.Command.Kind != PointerCommandKind.Move)
                return;
            route.Focus();
            _ = context.Composition.MenuSession?.OpenSubmenu(context.Identity, focusFirst: false);
        });
        context.OnKey(route =>
        {
            if (
                route.Command
                is not {
                    Kind: KeyCommandKind.Down,
                    IsRepeat: false,
                    Key: Key.Right or Key.Enter or Key.Space
                }
            )
                return;
            route.Handled =
                context.Composition.MenuSession?.OpenSubmenu(context.Identity, focusFirst: true)
                == true;
        });
        if (enabled is not null)
            _ = context.Effect(
                () =>
                    context.Composition.MenuSession?.RefreshSubmenuAvailability(
                        context.Identity,
                        enabled()
                    ),
                "menu-submenu-enabled"
            );
    }
}
