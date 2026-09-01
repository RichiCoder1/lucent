using System.Globalization;

namespace Lucent.Core;

/// <summary>Source-owned palettes for the bounded controls. Applications may layer their own tokens over either theme.</summary>
public static class ControlThemes
{
    internal static readonly Token<Brush> Surface = new("control-surface", Color.Parse("#ffffff"));
    internal static readonly Token<Color> SurfaceColor = new("control-surface-color", Color.Parse("#ffffff"));
    internal static readonly Token<Color> Foreground = new("control-foreground", Color.Parse("#0f172a"));
    internal static readonly Token<Brush> Accent = new("control-accent", Color.Parse("#2563eb"));
    internal static readonly Token<Brush> AccentPressed = new("control-accent-pressed", Color.Parse("#1d4ed8"));
    internal static readonly Token<Brush> Selected = new("control-selected", Color.Parse("#dbeafe"));
    internal static readonly Token<Brush> Focus = new("control-focus", Color.Parse("#ffff00"));
    internal static readonly Token<Color> FocusForeground = new("control-focus-foreground", Color.Parse("#000000"));
    internal static readonly Token<Brush> Disabled = new("control-disabled", Color.Parse("#94a3b8"));

    public static Theme Light { get; } = Palette("controls-light", Color.Parse("#ffffff"), Color.Parse("#0f172a"), Color.Parse("#2563eb"), Color.Parse("#1d4ed8"), Color.Parse("#dbeafe"), Color.Parse("#ffff00"), Color.Parse("#000000"), Color.Parse("#94a3b8"));
    public static Theme Dark { get; } = Palette("controls-dark", Color.Parse("#111827"), Color.Parse("#f8fafc"), Color.Parse("#60a5fa"), Color.Parse("#3b82f6"), Color.Parse("#1e3a5f"), Color.Parse("#facc15"), Color.Parse("#000000"), Color.Parse("#64748b"));
    public static Theme HighContrast { get; } = Palette("controls-high-contrast", Color.Parse("#000000"), Color.Parse("#ffffff"), Color.Parse("#ffffff"), Color.Parse("#ffff00"), Color.Parse("#0000ff"), Color.Parse("#ffff00"), Color.Parse("#000000"), Color.Parse("#808080"));

    private static Theme Palette(string name, Color surface, Color foreground, Color accent, Color pressed, Color selected, Color focus, Color focusForeground, Color disabled) => new Theme(name)
        .Set(Surface, (Brush)surface).Set(SurfaceColor, surface).Set(Foreground, foreground).Set(Accent, (Brush)accent).Set(AccentPressed, (Brush)pressed).Set(Selected, (Brush)selected).Set(Focus, (Brush)focus).Set(FocusForeground, focusForeground).Set(Disabled, (Brush)disabled);
}

/// <summary>Scope-owned mutable state for a single control recipe.</summary>
internal sealed class ControlState
{
    private readonly ReactiveScope _scope;
    private readonly Signal<string> _label;
    private readonly Signal<bool> _selected;
    private readonly Signal<float?> _progress;

    internal ControlState(ReactiveScope scope, string name, string label, bool selected = false, float? progress = null)
    {
        _scope = scope; _label = scope.Signal(Required(label, nameof(label)), name + ".label"); _selected = scope.Signal(selected, name + ".selected");
        if (progress is { } value) ValidateProgress(value);
        _progress = scope.Signal(progress, name + ".progress");
    }

    public string Label { get => _label.Value; set { Check(); _label.Value = Required(value, nameof(value)); } }
    public bool Selected { get => _selected.Value; set { Check(); _selected.Value = value; } }
    public float Progress { get => _progress.Value ?? throw new InvalidOperationException("This control has no progress value."); set { Check(); ValidateProgress(value); _progress.Value = value; } }
    internal float? ProgressValue => _progress.Value;
    private void Check() { _scope.CheckMutationGuard(); if (_scope.IsDisposed) throw new ObjectDisposedException(nameof(ControlState)); }
    internal static void ValidateProgress(float value) { if (!float.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value)); }
    internal static string Required(string value, string parameter) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A control name or label is required.", parameter); return value; }
}

/// <summary>Scope-owned offset for a bounded scroll viewport.</summary>
internal sealed class ScrollViewportState
{
    private readonly ReactiveScope _scope;
    private readonly Signal<ScrollOffset> _offset;
    internal ScrollViewportState(ReactiveScope scope, string name, ScrollOffset offset) { offset.Validate(); _scope = scope; _offset = scope.Signal(offset, name + ".offset"); }
    public ScrollOffset Offset { get => _offset.Value; set { _scope.CheckMutationGuard(); if (_scope.IsDisposed) throw new ObjectDisposedException(nameof(ScrollViewportState)); value.Validate(); _offset.Value = value; } }
}

/// <summary>Bounded component recipes. Structure stays authored by callers; recipes configure one retained element at a time.</summary>
internal static class Controls
{
    private static readonly Style TextStyle = Style.Empty.Set(TypographyProperties.TextColor, ControlThemes.Foreground);
    private static readonly Style PanelStyle = Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Column).Set(VisualProperties.Background, ControlThemes.Surface);
    private static readonly Style RowStyle = Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Row).Set(VisualProperties.Background, ControlThemes.Surface);
    private static readonly Style ButtonStyle = RowStyle.Set(LayoutProperties.Clip, true).Set(VisualProperties.Background, ControlThemes.Accent).Set(TypographyProperties.TextColor, ControlThemes.SurfaceColor)
        .When(VariantState.Pressed, Style.Empty.Set(VisualProperties.Background, ControlThemes.AccentPressed))
        .When(VariantState.FocusVisible, Style.Empty.Set(VisualProperties.Background, ControlThemes.Focus).Set(TypographyProperties.TextColor, ControlThemes.FocusForeground))
        .When(VariantState.Disabled, Style.Empty.Set(VisualProperties.Background, ControlThemes.Disabled));
    private static readonly Style TextFieldStyle = RowStyle.Set(LayoutProperties.Clip, true).Set(VisualProperties.Background, ControlThemes.Surface)
        .When(VariantState.FocusVisible, Style.Empty.Set(VisualProperties.Background, ControlThemes.Focus).Set(TypographyProperties.TextColor, ControlThemes.FocusForeground))
        .When(VariantState.Disabled, Style.Empty.Set(VisualProperties.Background, ControlThemes.Disabled));
    private static readonly Style SelectableStyle = RowStyle.Set(LayoutProperties.Clip, true)
        .When(VariantState.Selected, Style.Empty.Set(VisualProperties.Background, ControlThemes.Selected))
        .When(VariantState.Pressed, Style.Empty.Set(VisualProperties.Background, ControlThemes.AccentPressed).Set(TypographyProperties.TextColor, ControlThemes.SurfaceColor))
        .When(VariantState.FocusVisible, Style.Empty.Set(VisualProperties.Background, ControlThemes.Focus).Set(TypographyProperties.TextColor, ControlThemes.FocusForeground))
        .When(VariantState.Selected | VariantState.FocusVisible, Style.Empty.Set(VisualProperties.Background, ControlThemes.Focus).Set(TypographyProperties.TextColor, ControlThemes.FocusForeground))
        .When(VariantState.Disabled, Style.Empty.Set(VisualProperties.Background, ControlThemes.Disabled));

    public static void Text(Element element, ThemeContext theme, string text, Style? style = null) => ConfigureSemantic(element, theme, TextStyle.Set(ProjectionProperties.Text, Required(text, nameof(text))), style, new(SemanticRole.Text, text));
    public static void Panel(Element element, ThemeContext theme, string name, Style? style = null) => ConfigureSemantic(element, theme, PanelStyle, style, new(SemanticRole.Group, Required(name, nameof(name))));
    public static void Row(Element element, ThemeContext theme, string name, Style? style = null) => ConfigureSemantic(element, theme, RowStyle, style, new(SemanticRole.Group, Required(name, nameof(name))));
    public static void Column(Element element, ThemeContext theme, string name, Style? style = null) => Panel(element, theme, name, style);
    public static void List(Element element, ThemeContext theme, string name, Style? style = null) => ConfigureSemantic(element, theme, PanelStyle, style, new(SemanticRole.List, Required(name, nameof(name))));
    public static ScrollViewportState ScrollViewport(Element element, ThemeContext theme, string name, ScrollOffset offset = default, Style? style = null)
    {
        name = Required(name, nameof(name)); offset.Validate();
        var component = PanelStyle.Set(LayoutProperties.Clip, true).Set(LayoutProperties.Scroll, offset);
        Preflight(element, theme, component, style, new ScrollViewportBehavior(name, null!));
        var state = new ScrollViewportState(element.Scope, element.Name + ".scroll", offset);
        Configure(element, theme, component, style, new ScrollViewportBehavior(name, state));
        Bind(element, state, value => element.UpdateControl(LayoutProperties.Scroll, value.Offset));
        return state;
    }
    /// <summary>Creates a fixed-height keyed list owned by its scroll viewport. Rows outside the bounded viewport window do not remain mounted.</summary>
    public static VirtualizedRegion<TKey, TItem> VirtualizedList<TKey, TItem>(Element viewport, ThemeContext theme, string name, string label,
        Func<IEnumerable<TItem>> source, Func<TItem, TKey> key, Func<TItem, CompositionContext, Element> row, float rowHeight) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(viewport); ArgumentNullException.ThrowIfNull(theme); ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(key); ArgumentNullException.ThrowIfNull(row);
        var region = viewport.Composition.Virtualize(viewport, Required(name, nameof(name)), source, key, row, rowHeight, theme);
        try { List(region.Region, theme, Required(label, nameof(label))); region.Configure(); return region; }
        catch { region.Dispose(); throw; }
    }
    public static void Button(Element element, ThemeContext theme, string label, Action? activate = null, Style? style = null)
    {
        label = Required(label, nameof(label)); Configure(element, theme, ButtonStyle.Set(ProjectionProperties.Text, label), style, new ButtonBehavior("button", new(SemanticRole.Button, label, actions: SemanticAction.Invoke), activate));
    }
    public static TextFieldState TextField(Element element, ThemeContext theme, string name, string value = "", Style? style = null)
    {
        name = Required(name, nameof(name)); TextFieldState.ValidateText(value);
        var component = TextFieldStyle.Set(ProjectionProperties.Text, value);
        Preflight(element, theme, component, style, new TextFieldBehavior(null!, name));
        var state = new TextFieldState(element.Scope, element.Name + ".text", value);
        Configure(element, theme, component, style, new TextFieldBehavior(state, name));
        _ = element.Scope.Effect(() =>
        {
            element.UpdateControl(ProjectionProperties.Text, state.DisplayText.Length == 0 && !state.Focused ? name : state.DisplayText);
            element.UpdateControl(ProjectionProperties.TextSelectionStart, state.Focused ? state.DisplaySelectionStart : null);
            element.UpdateControl(ProjectionProperties.TextSelectionEnd, state.Focused ? state.DisplaySelectionEnd : null);
            element.UpdateControl(ProjectionProperties.TextCaret, state.Focused ? state.DisplayCaret : null);
        }, element.Name + ".text-value");
        return state;
    }
    public static ControlState Selectable(Element element, ThemeContext theme, string label, Action? activate = null, Style? style = null)
    {
        label = Required(label, nameof(label)); var component = SelectableStyle.Set(ProjectionProperties.Text, label); var behavior = new RowActionBehavior("selectable", new(SemanticRole.ListItem, label, actions: SemanticAction.Select)); Preflight(element, theme, component, style, behavior);
        var state = new ControlState(element.Scope, element.Name + ".selectable", label);
        Configure(element, theme, component, style, new RowActionBehavior("selectable", new(SemanticRole.ListItem, label, actions: SemanticAction.Select), activate, state));
        Bind(element, state, value => element.UpdateControl(ProjectionProperties.Text, value.Label), value => new(SemanticRole.ListItem, value.Label, actions: SemanticAction.Select));
        return state;
    }
    public static ControlState Loading(Element element, ThemeContext theme, string label = "Loading", Style? style = null)
    {
        label = Required(label, nameof(label)); var component = TextStyle.Set(ProjectionProperties.Text, label); Preflight(element, theme, component, style, new SemanticBehavior(new(SemanticRole.Status, label))); var state = new ControlState(element.Scope, element.Name + ".loading", label);
        ConfigureSemantic(element, theme, component, style, new(SemanticRole.Status, label)); Bind(element, state, value => element.UpdateControl(ProjectionProperties.Text, value.Label), value => new(SemanticRole.Status, value.Label)); return state;
    }
    public static ControlState Progress(Element element, ThemeContext theme, string label, float value, Style? style = null)
    {
        ControlState.ValidateProgress(value); label = Required(label, nameof(label)); var text = ProgressText(label, value); var component = TextStyle.Set(ProjectionProperties.Text, text); Preflight(element, theme, component, style, new SemanticBehavior(new(SemanticRole.Status, label))); var state = new ControlState(element.Scope, element.Name + ".progress", label, progress: value);
        ConfigureSemantic(element, theme, component, style, new(SemanticRole.Status, label, value: Percent(value)));
        Bind(element, state, current => element.UpdateControl(ProjectionProperties.Text, ProgressText(current.Label, current.Progress)), current => new(SemanticRole.Status, current.Label, value: Percent(current.Progress))); return state;
    }
    public static ControlState Error(Element element, ThemeContext theme, string message, Style? style = null)
    {
        message = Required(message, nameof(message)); var component = TextStyle.Set(ProjectionProperties.Text, message); Preflight(element, theme, component, style, new SemanticBehavior(new(SemanticRole.Status, message))); var state = new ControlState(element.Scope, element.Name + ".error", message);
        ConfigureSemantic(element, theme, component, style, new(SemanticRole.Status, message)); Bind(element, state, value => element.UpdateControl(ProjectionProperties.Text, value.Label), value => new(SemanticRole.Status, value.Label)); return state;
    }

    private static void Bind(Element element, ControlState state, Action<ControlState> visual, Func<ControlState, SemanticDeclaration> semantics)
        => _ = element.Scope.Effect(() => { visual(state); element.UpdateControlSemantics(semantics(state)); }, element.Name + ".control-state");
    private static void Bind(Element element, ScrollViewportState state, Action<ScrollViewportState> visual)
        => _ = element.Scope.Effect(() => visual(state), element.Name + ".scroll-state");

    private static void ConfigureSemantic(Element element, ThemeContext theme, Style component, Style? author, SemanticDeclaration semantics)
        => Configure(element, theme, component, author, new SemanticBehavior(semantics));
    private static void Configure(Element element, ThemeContext theme, Style component, Style? author, Behavior behavior)
    {
        ArgumentNullException.ThrowIfNull(element); ArgumentNullException.ThrowIfNull(theme); ArgumentNullException.ThrowIfNull(behavior);
        element.ValidateBehaviorAttachment(behavior); // Preflight keeps a failed ownership claim from installing presentation.
        element.Present(theme, component, author); element.AttachBehaviors(behavior);
    }
    private static void Preflight(Element element, ThemeContext theme, Style component, Style? author, Behavior behavior)
    {
        ArgumentNullException.ThrowIfNull(element); ArgumentNullException.ThrowIfNull(theme); ArgumentNullException.ThrowIfNull(behavior);
        element.ValidatePresentation(theme, component, author); element.ValidateBehaviorAttachment(behavior);
    }

    private static string Required(string value, string parameter) => ControlState.Required(value, parameter);
    private static string Percent(float value) => MathF.Round(value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    private static string ProgressText(string label, float value) => label + " " + Percent(value);

    private sealed class SemanticBehavior(SemanticDeclaration semantics) : Behavior
    {
        public override string Name => "semantics"; public override BehaviorOwnership Ownership => BehaviorOwnership.Semantics;
        public override void Attach(BehaviorContext context) => context.SetSemantics(semantics);
    }
}
