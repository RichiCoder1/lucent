using System.Globalization;

namespace Lucent.Core;

/// <summary>Built-in color palettes for Lucent controls. Use one as the starting theme for an application.</summary>
public static class ControlThemes
{
    internal static readonly Token<Brush> Surface = new("control-surface", Color.Parse("#ffffff"));
    internal static readonly Token<Color> SurfaceColor = new(
        "control-surface-color",
        Color.Parse("#ffffff")
    );
    internal static readonly Token<Color> Foreground = new(
        "control-foreground",
        Color.Parse("#0f172a")
    );
    internal static readonly Token<Color> SecondaryForeground = new(
        "control-secondary-foreground",
        Color.Parse("#475569")
    );
    internal static readonly Token<Color> DisabledForeground = new(
        "control-disabled-foreground",
        Color.Parse("#475569")
    );
    internal static readonly Token<Brush> Accent = new("control-accent", Color.Parse("#2563eb"));
    internal static readonly Token<Brush> AccentPressed = new(
        "control-accent-pressed",
        Color.Parse("#1d4ed8")
    );
    internal static readonly Token<Brush> Selected = new(
        "control-selected",
        Color.Parse("#dbeafe")
    );
    internal static readonly Token<Brush> Focus = new("control-focus", Color.Parse("#ffff00"));
    internal static readonly Token<global::Lucent.Core.FocusRing> FocusRing = new(
        "control-focus-ring",
        global::Lucent.Core.FocusRing.Inset(Color.Parse("#ffff00"), 2)
    );
    internal static readonly Token<Color> FocusForeground = new(
        "control-focus-foreground",
        Color.Parse("#000000")
    );
    internal static readonly Token<Brush> Disabled = new(
        "control-disabled",
        Color.Parse("#94a3b8")
    );
    internal static readonly Token<Brush> Border = new("control-border", Color.Parse("#cbd5e1"));
    internal static readonly Token<Brush> BorderHover = new(
        "control-border-hover",
        Color.Parse("#94a3b8")
    );
    internal static readonly Token<Brush> Divider = new("control-divider", Color.Parse("#cbd5e1"));
    internal static readonly Token<Brush> ScrollTrack = new(
        "control-scrollbar-track",
        Color.Parse("#00000040")
    );
    internal static readonly Token<Brush> ScrollThumb = new(
        "control-scrollbar-thumb",
        Color.Parse("#64748b")
    );
    internal static readonly Token<Brush> ScrollThumbHover = new(
        "control-scrollbar-thumb-hover",
        Color.Parse("#475569")
    );
    internal static readonly Token<Brush> ScrollThumbPressed = new(
        "control-scrollbar-thumb-pressed",
        Color.Parse("#334155")
    );

    /// <summary>Gets a light palette for controls.</summary>
    public static Theme Light { get; } =
        Palette(
            "controls-light",
            Color.Parse("#ffffff"),
            Color.Parse("#0f172a"),
            Color.Parse("#475569"),
            Color.Parse("#475569"),
            Color.Parse("#2563eb"),
            Color.Parse("#1d4ed8"),
            Color.Parse("#dbeafe"),
            Color.Parse("#ffff00"),
            Color.Parse("#000000"),
            Color.Parse("#94a3b8"),
            Color.Parse("#cbd5e1"),
            Color.Parse("#94a3b8"),
            Color.Parse("#cbd5e1")
        );

    /// <summary>Gets a dark palette for controls.</summary>
    public static Theme Dark { get; } =
        Palette(
            "controls-dark",
            Color.Parse("#111827"),
            Color.Parse("#f8fafc"),
            Color.Parse("#cbd5e1"),
            Color.Parse("#cbd5e1"),
            Color.Parse("#60a5fa"),
            Color.Parse("#3b82f6"),
            Color.Parse("#1e3a5f"),
            Color.Parse("#facc15"),
            Color.Parse("#000000"),
            Color.Parse("#64748b"),
            Color.Parse("#475569"),
            Color.Parse("#94a3b8"),
            Color.Parse("#475569")
        );

    /// <summary>Gets a high-contrast palette for controls.</summary>
    public static Theme HighContrast { get; } =
        Palette(
            "controls-high-contrast",
            Color.Parse("#000000"),
            Color.Parse("#ffffff"),
            Color.Parse("#ffffff"),
            Color.Parse("#808080"),
            Color.Parse("#ffffff"),
            Color.Parse("#ffff00"),
            Color.Parse("#0000ff"),
            Color.Parse("#ffff00"),
            Color.Parse("#000000"),
            Color.Parse("#808080"),
            Color.Parse("#ffffff"),
            Color.Parse("#ffff00"),
            Color.Parse("#ffffff")
        );

    private static Theme Palette(
        string name,
        Color surface,
        Color foreground,
        Color secondaryForeground,
        Color disabledForeground,
        Color accent,
        Color pressed,
        Color selected,
        Color focus,
        Color focusForeground,
        Color disabled,
        Color border,
        Color borderHover,
        Color divider
    ) =>
        new Theme(name)
            .Set(Surface, (Brush)surface)
            .Set(SurfaceColor, surface)
            .Set(Foreground, foreground)
            .Set(SecondaryForeground, secondaryForeground)
            .Set(DisabledForeground, disabledForeground)
            .Set(Accent, (Brush)accent)
            .Set(AccentPressed, (Brush)pressed)
            .Set(Selected, (Brush)selected)
            .Set(Focus, (Brush)focus)
            .Set(FocusRing, global::Lucent.Core.FocusRing.Inset((Brush)focus, 2))
            .Set(FocusForeground, focusForeground)
            .Set(Disabled, (Brush)disabled)
            .Set(Border, (Brush)border)
            .Set(BorderHover, (Brush)borderHover)
            .Set(Divider, (Brush)divider)
            .Set(ScrollTrack, (Brush)Color.FromArgb(0x40, foreground.R, foreground.G, foreground.B))
            .Set(ScrollThumb, (Brush)foreground)
            .Set(ScrollThumbHover, (Brush)accent)
            .Set(ScrollThumbPressed, (Brush)pressed);
}

/// <summary>Scope-owned mutable state for a single control recipe.</summary>
internal sealed class ControlState
{
    private readonly ReactiveScope _scope;
    private readonly Signal<string> _label;
    private readonly Signal<bool> _selected;
    private readonly Signal<float?> _progress;

    internal ControlState(
        ReactiveScope scope,
        string name,
        string label,
        bool selected = false,
        float? progress = null
    )
    {
        _scope = scope;
        _label = scope.Signal(Required(label, nameof(label)), name + ".label");
        _selected = scope.Signal(selected, name + ".selected");
        if (progress is { } value)
            ValidateProgress(value);
        _progress = scope.Signal(progress, name + ".progress");
    }

    public string Label
    {
        get => _label.Value;
        set
        {
            Check();
            _label.Value = Required(value, nameof(value));
        }
    }
    public bool Selected
    {
        get => _selected.Value;
        set
        {
            Check();
            _selected.Value = value;
        }
    }
    public float Progress
    {
        get =>
            _progress.Value
            ?? throw new InvalidOperationException("This control has no progress value.");
        set
        {
            Check();
            ValidateProgress(value);
            _progress.Value = value;
        }
    }
    internal float? ProgressValue => _progress.Value;

    private void Check()
    {
        _scope.CheckMutationGuard();
        ObjectDisposedException.ThrowIf(_scope.IsDisposed, typeof(ControlState));
    }

    internal static void ValidateProgress(float value)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(nameof(value));
    }

    internal static string Required(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A control name or label is required.", parameter);
        return value;
    }
}

/// <summary>Mount adapter for a hoistable bounded viewport.</summary>
internal sealed class ScrollViewportState
{
    private readonly ReactiveScope _scope;
    private readonly ViewportState _viewport;

    internal ScrollViewportState(
        ReactiveScope scope,
        string name,
        ScrollOffset offset,
        ViewportState? viewport = null
    )
    {
        _scope = scope;
        _viewport = viewport ?? new ViewportState(scope, offset, name + ".state");
        _ = _viewport.AcquireMount(scope);
    }

    public ScrollOffset Offset
    {
        get
        {
            CheckRead();
            return _viewport.Offset;
        }
        set
        {
            CheckMutation();
            _viewport.Offset = value;
        }
    }

    private void CheckRead()
    {
        _scope.Graph.CheckThread();
        ObjectDisposedException.ThrowIf(_scope.IsDisposed, typeof(ScrollViewportState));
    }

    private void CheckMutation()
    {
        _scope.CheckMutationGuard();
        ObjectDisposedException.ThrowIf(_scope.IsDisposed, typeof(ScrollViewportState));
    }
}

/// <summary>Internal implementations used by the built-in components to configure their elements.</summary>
internal static class Controls
{
    // Text and structural containers deliberately inherit their paint from the
    // surrounding presentation. This keeps composed controls (for example a
    // Selectable containing a Row and Text) from painting over the owner's
    // state background or text color. Applications opt into an explicit stock
    // surface at their shell/container boundary with PresentationStyles.Surface.
    private static readonly Style TextStyle = Style.Empty;
    private static readonly Style PanelStyle = Style.Empty.Set(
        LayoutProperties.Axis,
        LayoutAxis.Column
    );
    private static readonly Style RowStyle = Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Row);
    private static readonly Style ScrollBarStyle = Style
        .Empty.Set(ScrollBarProperties.Visibility, ScrollBarVisibility.Auto)
        .Set(ScrollBarProperties.Thickness, 12f)
        .Set(ScrollBarProperties.MinimumThumbLength, 24f)
        .Set(ScrollBarProperties.TrackBrush, ControlThemes.ScrollTrack)
        .Set(ScrollBarProperties.ThumbBrush, ControlThemes.ScrollThumb)
        .Set(ScrollBarProperties.HoverThumbBrush, ControlThemes.ScrollThumbHover)
        .Set(ScrollBarProperties.PressedThumbBrush, ControlThemes.ScrollThumbPressed)
        .Set(ScrollBarProperties.ThumbCornerRadius, 6f);

    private static Style ButtonStyle(ThemeContext theme) =>
        RowStyle
            .Set(LayoutProperties.Clip, true)
            .Set(LayoutProperties.Padding, Insets.Symmetric(12, 8))
            .Set(LayoutProperties.MinHeight, 36f)
            .Set(LayoutProperties.MainAlignment, LayoutAlignment.Center)
            .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Center)
            .Set(VisualProperties.CornerRadius, 6f)
            .Bind(
                VisualProperties.Background,
                () => ResolveBrush(theme, ControlThemes.Accent, PresentationStyles.TransparentBrush)
            )
            .Bind(
                TypographyProperties.TextColor,
                () => ResolveButtonText(theme, ControlThemes.SurfaceColor)
            )
            .Bind(VisualProperties.Border, () => ResolveBorder(theme, ControlThemes.Border))
            .When(
                VariantState.Hover,
                Style
                    .Empty.Set(VisualProperties.Background, ControlThemes.AccentPressed)
                    .Bind(
                        VisualProperties.Border,
                        () => ResolveBorder(theme, ControlThemes.BorderHover)
                    )
            )
            .When(
                VariantState.Pressed,
                Style
                    .Empty.Set(VisualProperties.Background, ControlThemes.AccentPressed)
                    .Set(TypographyProperties.TextColor, ControlThemes.SurfaceColor)
            )
            .When(VariantState.FocusVisible, FocusStyle(theme))
            .When(
                VariantState.Disabled,
                Style
                    .Empty.Set(VisualProperties.Background, ControlThemes.Disabled)
                    .Set(TypographyProperties.TextColor, ControlThemes.DisabledForeground)
                    .Bind(
                        VisualProperties.Border,
                        () => ResolveBorder(theme, ControlThemes.Disabled)
                    )
            );

    private static Style TextFieldStyle(ThemeContext theme) =>
        RowStyle
            .Set(LayoutProperties.Clip, true)
            .Set(LayoutProperties.Padding, Insets.Symmetric(12, 8))
            .Set(LayoutProperties.MainAlignment, LayoutAlignment.Start)
            .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Center)
            .Set(VisualProperties.CornerRadius, 6f)
            .Bind(
                VisualProperties.Background,
                () =>
                    ResolveBrush(theme, ControlThemes.Surface, PresentationStyles.TransparentBrush)
            )
            .Bind(TypographyProperties.TextColor, () => theme.Token(ControlThemes.Foreground))
            .Bind(VisualProperties.Border, () => ResolveBorder(theme, ControlThemes.Border))
            .When(
                VariantState.Hover,
                Style
                    .Empty.Set(VisualProperties.Background, ControlThemes.Selected)
                    .Bind(
                        VisualProperties.Border,
                        () => ResolveBorder(theme, ControlThemes.BorderHover)
                    )
            )
            .When(VariantState.FocusVisible, FocusStyle(theme))
            .When(
                VariantState.Disabled,
                Style
                    .Empty.Set(VisualProperties.Background, ControlThemes.Disabled)
                    .Set(TypographyProperties.TextColor, ControlThemes.DisabledForeground)
                    .Bind(
                        VisualProperties.Border,
                        () => ResolveBorder(theme, ControlThemes.Disabled)
                    )
            );

    private static Style SelectableStyle(ThemeContext theme) =>
        RowStyle
            .Set(LayoutProperties.Clip, true)
            .Set(LayoutProperties.Padding, Insets.Symmetric(10, 6))
            .Set(LayoutProperties.MinHeight, 32f)
            .Set(LayoutProperties.MainAlignment, LayoutAlignment.Start)
            .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Center)
            .Set(VisualProperties.CornerRadius, 4f)
            .Bind(
                VisualProperties.Background,
                () =>
                    ResolveBrush(theme, ControlThemes.Surface, PresentationStyles.TransparentBrush)
            )
            .Bind(TypographyProperties.TextColor, () => theme.Token(ControlThemes.Foreground))
            .When(
                VariantState.Hover,
                Style.Empty.Set(VisualProperties.Background, ControlThemes.Selected)
            )
            .When(
                VariantState.Selected,
                Style.Empty.Set(VisualProperties.Background, ControlThemes.Selected)
            )
            .When(
                VariantState.Pressed,
                Style
                    .Empty.Set(VisualProperties.Background, ControlThemes.AccentPressed)
                    .Set(TypographyProperties.TextColor, ControlThemes.SurfaceColor)
            )
            .When(VariantState.FocusVisible, FocusStyle(theme))
            .When(VariantState.Selected | VariantState.FocusVisible, FocusStyle(theme))
            .When(
                VariantState.Disabled,
                Style
                    .Empty.Set(VisualProperties.Background, ControlThemes.Disabled)
                    .Set(TypographyProperties.TextColor, ControlThemes.DisabledForeground)
                    .Bind(
                        VisualProperties.Border,
                        () => ResolveBorder(theme, ControlThemes.Disabled)
                    )
            );

    private static Brush ResolveBrush(ThemeContext theme, Token<Brush> standard, Brush minimal) =>
        theme.PresentationMode == ControlPresentationMode.Minimal ? minimal : theme.Token(standard);

    private static Border ResolveBorder(ThemeContext theme, Token<Brush> standard) =>
        theme.PresentationMode == ControlPresentationMode.Minimal
            ? Border.None
            : Border.Hairline(theme.Token(standard));

    private static Color ResolveButtonText(ThemeContext theme, Token<Color> standard) =>
        theme.PresentationMode == ControlPresentationMode.Minimal
            ? theme.Token(ControlThemes.Foreground)
            : theme.Token(standard);

    private static Style FocusStyle(ThemeContext theme) =>
        theme.PresentationMode != ControlPresentationMode.Minimal && IsHighContrast(theme)
            ? Style
                .Empty.Set(VisualProperties.Background, ControlThemes.Focus)
                .Set(TypographyProperties.TextColor, ControlThemes.FocusForeground)
                .Set(VisualProperties.FocusRing, ControlThemes.FocusRing)
            : Style.Empty.Set(VisualProperties.FocusRing, ControlThemes.FocusRing);

    private static bool IsHighContrast(ThemeContext theme) =>
        theme.Appearance.Contrast == ThemeContrast.High
        || string.Equals(
            theme.CurrentTheme.Name,
            ControlThemes.HighContrast.Name,
            StringComparison.Ordinal
        );

    public static void Text(
        Element element,
        ThemeContext theme,
        string text,
        Style? style = null
    ) =>
        ConfigureSemantic(
            element,
            theme,
            TextStyle.Set(ProjectionProperties.Text, Required(text, nameof(text))),
            style,
            new(SemanticRole.Text, text)
        );

    public static void Panel(
        Element element,
        ThemeContext theme,
        string name,
        Style? style = null
    ) =>
        ConfigureSemantic(
            element,
            theme,
            PanelStyle,
            style,
            new(SemanticRole.Group, Required(name, nameof(name)))
        );

    public static void Row(Element element, ThemeContext theme, string name, Style? style = null) =>
        ConfigureSemantic(
            element,
            theme,
            RowStyle,
            style,
            new(SemanticRole.Group, Required(name, nameof(name)))
        );

    public static void Column(
        Element element,
        ThemeContext theme,
        string name,
        Style? style = null
    ) => Panel(element, theme, name, style);

    public static void List(
        Element element,
        ThemeContext theme,
        string name,
        Style? style = null
    ) =>
        ConfigureSemantic(
            element,
            theme,
            PanelStyle,
            style,
            new(SemanticRole.List, Required(name, nameof(name)))
        );

    public static ScrollViewportState ScrollViewport(
        Element element,
        ThemeContext theme,
        string name,
        ScrollOffset offset = default,
        Style? style = null,
        ViewportState? viewport = null
    )
    {
        name = Required(name, nameof(name));
        offset.Validate();
        var initialOffset = viewport?.Offset ?? offset;
        var component = PanelStyle
            .With(ScrollBarStyle)
            .Set(LayoutProperties.Clip, true)
            .Set(LayoutProperties.Scroll, initialOffset);
        Preflight(element, theme, component, style, new ScrollViewportBehavior(name, null!));
        var state = new ScrollViewportState(
            element.Scope,
            element.Name + ".scroll",
            offset,
            viewport
        );
        Configure(element, theme, component, style, new ScrollViewportBehavior(name, state));
        Bind(element, state, value => element.UpdateControl(LayoutProperties.Scroll, value.Offset));
        return state;
    }

    /// <summary>Creates the fixed-height list used by <see cref="Components.VirtualizedList{TKey,TItem}"/>.</summary>
    public static VirtualizedRegion<TKey, TItem> VirtualizedList<TKey, TItem>(
        Element viewport,
        ThemeContext theme,
        string name,
        string label,
        Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key,
        Func<CurrentItem<TItem>, CompositionContext, Element> row,
        float rowHeight
    )
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(row);
        var region = viewport.Composition.Virtualize(
            viewport,
            Required(name, nameof(name)),
            source,
            key,
            row,
            rowHeight,
            theme
        );
        try
        {
            List(region.Region, theme, Required(label, nameof(label)));
            region.Configure();
            return region;
        }
        catch
        {
            region.Dispose();
            throw;
        }
    }

    public static void Button(
        Element element,
        ThemeContext theme,
        string label,
        Action? activate = null,
        Style? style = null
    )
    {
        label = Required(label, nameof(label));
        Configure(
            element,
            theme,
            ButtonStyle(theme).Set(ProjectionProperties.Text, label),
            style,
            new ButtonBehavior(
                "button",
                new(SemanticRole.Button, label, actions: SemanticAction.Invoke),
                activate
            )
        );
    }

    public static TextFieldState TextField(
        Element element,
        ThemeContext theme,
        string name,
        string value = "",
        Style? style = null,
        EditorSession? session = null,
        FocusTarget? focusTarget = null
    )
    {
        name = Required(name, nameof(name));
        TextFieldState.ValidateText(value);
        if (session?.IsMultiline == true)
            throw new ArgumentException(
                "TextField requires a single-line editor session.",
                nameof(session)
            );
        var initialText = session?.Text ?? value;
        var component = TextFieldStyle(theme)
            .Set(ProjectionProperties.Text, initialText)
            .Set(ProjectionProperties.TextMeasure, name);
        Preflight(element, theme, component, style, new TextFieldBehavior(null!, name));
        var editor =
            session
            ?? new EditorSession(element.Scope, element.Name, value, element.Name + ".editor");
        var state = new TextFieldState(element.Scope, element.Name + ".text", editor);
        Configure(element, theme, component, style, new TextFieldBehavior(state, name));
        if (focusTarget is not null)
            element.Composition.Input.RegisterFocusTarget(
                element.Id,
                element.Scope,
                state,
                focusTarget
            );
        _ = element.Scope.Effect(
            () =>
            {
                element.UpdateControl(
                    ProjectionProperties.Text,
                    state.DisplayText.Length == 0 && !state.Focused ? name : state.DisplayText
                );
                element.UpdateControl(
                    ProjectionProperties.TextSelectionStart,
                    state.Focused ? state.DisplaySelectionStart : null
                );
                element.UpdateControl(
                    ProjectionProperties.TextSelectionEnd,
                    state.Focused ? state.DisplaySelectionEnd : null
                );
                element.UpdateControl(
                    ProjectionProperties.TextCaret,
                    state.Focused ? state.DisplayCaret : null
                );
            },
            element.Name + ".text-value"
        );
        return state;
    }

    public static TextAreaState TextArea(
        Element element,
        ThemeContext theme,
        string name,
        string value = "",
        Style? style = null,
        EditorSession? session = null,
        FocusTarget? focusTarget = null
    )
    {
        name = Required(name, nameof(name));
        TextFieldState.ValidateMultilineText(value);
        if (session is not null && !session.IsMultiline)
            throw new ArgumentException(
                "TextArea requires a multiline editor session.",
                nameof(session)
            );
        var editor =
            session
            ?? new EditorSession(
                element.Scope,
                element.Name,
                value,
                element.Name + ".editor",
                multiline: true
            );
        var initialText = editor.Text;
        var component = TextFieldStyle(theme)
            .With(ScrollBarStyle)
            .Set(ProjectionProperties.Text, initialText)
            .Set(ProjectionProperties.TextMeasure, name)
            .Set(ProjectionProperties.TextMultiline, true)
            .Set(ProjectionProperties.TextCaretAffinity, editor.CaretAffinity)
            .Set(TypographyProperties.TextWrap, TextWrap.WordWithGraphemeFallback)
            .Set(LayoutProperties.Scroll, editor.Viewport.Offset);
        Preflight(element, theme, component, style, new TextFieldBehavior(null!, name));
        var state = new TextAreaState(element.Scope, element.Name + ".text", editor);
        Configure(element, theme, component, style, new TextFieldBehavior(state, name));
        if (focusTarget is not null)
            element.Composition.Input.RegisterFocusTarget(
                element.Id,
                element.Scope,
                state,
                focusTarget
            );
        _ = element.Scope.Effect(
            () =>
            {
                element.UpdateControl(
                    ProjectionProperties.Text,
                    state.DisplayText.Length == 0 && !state.Focused ? name : state.DisplayText
                );
                element.UpdateControl(
                    ProjectionProperties.TextSelectionStart,
                    state.Focused ? state.DisplaySelectionStart : null
                );
                element.UpdateControl(
                    ProjectionProperties.TextSelectionEnd,
                    state.Focused ? state.DisplaySelectionEnd : null
                );
                element.UpdateControl(
                    ProjectionProperties.TextCaret,
                    state.Focused ? state.DisplayCaret : null
                );
                element.UpdateControl(ProjectionProperties.TextMultiline, state.IsMultiline);
                element.UpdateControl(
                    ProjectionProperties.TextCaretAffinity,
                    state.Focused ? state.Session.CaretAffinity : TextAffinity.Downstream
                );
                element.UpdateControl(
                    LayoutProperties.Scroll,
                    state.ScrollState?.Offset ?? default
                );
            },
            element.Name + ".text-area-value"
        );
        return state;
    }

    public static ControlState Selectable(
        Element element,
        ThemeContext theme,
        string label,
        Action? activate = null,
        Style? style = null,
        bool controlled = false
    ) =>
        ConfigureSelectable(element, theme, label, activate, style, projectLabel: true, controlled);

    internal static ControlState ComposedSelectable(
        Element element,
        ThemeContext theme,
        string label,
        Action? activate = null,
        Style? style = null,
        bool controlled = false
    ) =>
        ConfigureSelectable(
            element,
            theme,
            label,
            activate,
            style,
            projectLabel: false,
            controlled
        );

    private static ControlState ConfigureSelectable(
        Element element,
        ThemeContext theme,
        string label,
        Action? activate,
        Style? style,
        bool projectLabel,
        bool controlled
    )
    {
        label = Required(label, nameof(label));
        var styleBase = SelectableStyle(theme);
        var component = projectLabel ? styleBase.Set(ProjectionProperties.Text, label) : styleBase;
        var behavior = new RowActionBehavior(
            "selectable",
            new(SemanticRole.ListItem, label, actions: SemanticAction.Select)
        );
        Preflight(element, theme, component, style, behavior);
        var state = new ControlState(element.Scope, element.Name + ".selectable", label);
        Configure(
            element,
            theme,
            component,
            style,
            new RowActionBehavior(
                "selectable",
                new(SemanticRole.ListItem, label, actions: SemanticAction.Select),
                activate,
                state,
                controlled
            )
        );
        Bind(
            element,
            state,
            value =>
            {
                if (projectLabel)
                    element.UpdateControl(ProjectionProperties.Text, value.Label);
            },
            value => new(SemanticRole.ListItem, value.Label, actions: SemanticAction.Select)
        );
        return state;
    }

    public static ControlState Loading(
        Element element,
        ThemeContext theme,
        string label = "Loading",
        Style? style = null
    )
    {
        label = Required(label, nameof(label));
        var component = TextStyle.Set(ProjectionProperties.Text, label);
        Preflight(
            element,
            theme,
            component,
            style,
            new SemanticBehavior(new(SemanticRole.Status, label))
        );
        var state = new ControlState(element.Scope, element.Name + ".loading", label);
        ConfigureSemantic(element, theme, component, style, new(SemanticRole.Status, label));
        Bind(
            element,
            state,
            value => element.UpdateControl(ProjectionProperties.Text, value.Label),
            value => new(SemanticRole.Status, value.Label)
        );
        return state;
    }

    public static ControlState Progress(
        Element element,
        ThemeContext theme,
        string label,
        float value,
        Style? style = null
    )
    {
        ControlState.ValidateProgress(value);
        label = Required(label, nameof(label));
        var text = ProgressText(label, value);
        var component = TextStyle.Set(ProjectionProperties.Text, text);
        Preflight(
            element,
            theme,
            component,
            style,
            new SemanticBehavior(new(SemanticRole.Status, label))
        );
        var state = new ControlState(
            element.Scope,
            element.Name + ".progress",
            label,
            progress: value
        );
        ConfigureSemantic(
            element,
            theme,
            component,
            style,
            new(SemanticRole.Status, label, value: Percent(value))
        );
        Bind(
            element,
            state,
            current =>
                element.UpdateControl(
                    ProjectionProperties.Text,
                    ProgressText(current.Label, current.Progress)
                ),
            current => new(SemanticRole.Status, current.Label, value: Percent(current.Progress))
        );
        return state;
    }

    public static ControlState Error(
        Element element,
        ThemeContext theme,
        string message,
        Style? style = null
    )
    {
        message = Required(message, nameof(message));
        var component = TextStyle.Set(ProjectionProperties.Text, message);
        Preflight(
            element,
            theme,
            component,
            style,
            new SemanticBehavior(new(SemanticRole.Status, message))
        );
        var state = new ControlState(element.Scope, element.Name + ".error", message);
        ConfigureSemantic(element, theme, component, style, new(SemanticRole.Status, message));
        Bind(
            element,
            state,
            value => element.UpdateControl(ProjectionProperties.Text, value.Label),
            value => new(SemanticRole.Status, value.Label)
        );
        return state;
    }

    private static void Bind(
        Element element,
        ControlState state,
        Action<ControlState> visual,
        Func<ControlState, SemanticDeclaration> semantics
    ) =>
        _ = element.Scope.Effect(
            () =>
            {
                visual(state);
                element.UpdateControlSemantics(semantics(state));
            },
            element.Name + ".control-state"
        );

    private static void Bind(
        Element element,
        ScrollViewportState state,
        Action<ScrollViewportState> visual
    ) => _ = element.Scope.Effect(() => visual(state), element.Name + ".scroll-state");

    private static void ConfigureSemantic(
        Element element,
        ThemeContext theme,
        Style component,
        Style? author,
        SemanticDeclaration semantics
    ) => Configure(element, theme, component, author, new SemanticBehavior(semantics));

    private static void Configure(
        Element element,
        ThemeContext theme,
        Style component,
        Style? author,
        Behavior behavior
    )
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(behavior);
        element.ValidateBehaviorAttachment(behavior); // Preflight keeps a failed ownership claim from installing presentation.
        element.Present(theme, component, author);
        element.AttachBehaviors(behavior);
    }

    private static void Preflight(
        Element element,
        ThemeContext theme,
        Style component,
        Style? author,
        Behavior behavior
    )
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(behavior);
        element.ValidatePresentation(theme, component, author);
        element.ValidateBehaviorAttachment(behavior);
    }

    private static string Required(string value, string parameter) =>
        ControlState.Required(value, parameter);

    private static string Percent(float value) =>
        MathF.Round(value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string ProgressText(string label, float value) => label + " " + Percent(value);

    private sealed class SemanticBehavior(SemanticDeclaration semantics) : Behavior
    {
        public override string Name => "semantics";
        public override BehaviorOwnership Ownership => BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context) => context.SetSemantics(semantics);
    }
}
