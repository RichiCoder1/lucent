using System.Globalization;

namespace Lucent.Core;

/// <summary>Source-owned palettes for the bounded controls. Applications may layer their own tokens over either theme.</summary>
public static class ControlThemes
{
    internal static readonly Token<uint> Surface = new("control-surface", 0xffffffffU);
    internal static readonly Token<uint> Foreground = new("control-foreground", 0xff0f172aU);
    internal static readonly Token<uint> Accent = new("control-accent", 0xff2563ebU);
    internal static readonly Token<uint> AccentPressed = new("control-accent-pressed", 0xff1d4ed8U);
    internal static readonly Token<uint> Selected = new("control-selected", 0xffdbeafeU);
    internal static readonly Token<uint> Focus = new("control-focus", 0xffffff00U);
    internal static readonly Token<uint> FocusForeground = new("control-focus-foreground", 0xff000000U);
    internal static readonly Token<uint> Disabled = new("control-disabled", 0xff94a3b8U);

    public static Theme Light { get; } = Palette("controls-light", 0xffffffffU, 0xff0f172aU, 0xff2563ebU, 0xff1d4ed8U, 0xffdbeafeU, 0xffffff00U, 0xff000000U, 0xff94a3b8U);
    public static Theme Dark { get; } = Palette("controls-dark", 0xff111827U, 0xfff8fafcU, 0xff60a5faU, 0xff3b82f6U, 0xff1e3a5fU, 0xfffacc15U, 0xff000000U, 0xff64748bU);
    public static Theme HighContrast { get; } = Palette("controls-high-contrast", 0xff000000U, 0xffffffffU, 0xffffffffU, 0xffffff00U, 0xff0000ffU, 0xffffff00U, 0xff000000U, 0xff808080U);

    private static Theme Palette(string name, uint surface, uint foreground, uint accent, uint pressed, uint selected, uint focus, uint focusForeground, uint disabled) => new Theme(name)
        .Set(Surface, surface).Set(Foreground, foreground).Set(Accent, accent).Set(AccentPressed, pressed).Set(Selected, selected).Set(Focus, focus).Set(FocusForeground, focusForeground).Set(Disabled, disabled);
}

/// <summary>Scope-owned mutable state for a single control recipe.</summary>
public sealed class ControlState
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
public sealed class ScrollViewportState
{
    private readonly ReactiveScope _scope;
    private readonly Signal<ScrollOffset> _offset;
    internal ScrollViewportState(ReactiveScope scope, string name, ScrollOffset offset) { offset.Validate(); _scope = scope; _offset = scope.Signal(offset, name + ".offset"); }
    public ScrollOffset Offset { get => _offset.Value; set { _scope.CheckMutationGuard(); if (_scope.IsDisposed) throw new ObjectDisposedException(nameof(ScrollViewportState)); value.Validate(); _offset.Value = value; } }
}

/// <summary>Bounded component recipes. Structure stays authored by callers; recipes configure one retained element at a time.</summary>
public static class Controls
{
    private static readonly Style TextStyle = Style.Empty.Set(SceneProperties.Foreground, ControlThemes.Foreground);
    private static readonly Style PanelStyle = Style.Empty.Set(Arrangement.Axis, LayoutAxis.Column).Set(SceneProperties.Fill, ControlThemes.Surface);
    private static readonly Style RowStyle = Style.Empty.Set(Arrangement.Axis, LayoutAxis.Row).Set(SceneProperties.Fill, ControlThemes.Surface);
    private static readonly Style ButtonStyle = RowStyle.Set(Arrangement.Clip, true).Set(SceneProperties.Fill, ControlThemes.Accent).Set(SceneProperties.Foreground, ControlThemes.Surface)
        .When(VariantState.Pressed, Style.Empty.Set(SceneProperties.Fill, ControlThemes.AccentPressed))
        .When(VariantState.FocusVisible, Style.Empty.Set(SceneProperties.Fill, ControlThemes.Focus).Set(SceneProperties.Foreground, ControlThemes.FocusForeground))
        .When(VariantState.Disabled, Style.Empty.Set(SceneProperties.Fill, ControlThemes.Disabled));
    private static readonly Style TextFieldStyle = RowStyle.Set(Arrangement.Clip, true).Set(SceneProperties.Fill, ControlThemes.Surface)
        .When(VariantState.FocusVisible, Style.Empty.Set(SceneProperties.Fill, ControlThemes.Focus).Set(SceneProperties.Foreground, ControlThemes.FocusForeground))
        .When(VariantState.Disabled, Style.Empty.Set(SceneProperties.Fill, ControlThemes.Disabled));
    private static readonly Style SelectableStyle = RowStyle.Set(Arrangement.Clip, true)
        .When(VariantState.Selected, Style.Empty.Set(SceneProperties.Fill, ControlThemes.Selected))
        .When(VariantState.Pressed, Style.Empty.Set(SceneProperties.Fill, ControlThemes.AccentPressed).Set(SceneProperties.Foreground, ControlThemes.Surface))
        .When(VariantState.FocusVisible, Style.Empty.Set(SceneProperties.Fill, ControlThemes.Focus).Set(SceneProperties.Foreground, ControlThemes.FocusForeground))
        .When(VariantState.Selected | VariantState.FocusVisible, Style.Empty.Set(SceneProperties.Fill, ControlThemes.Focus).Set(SceneProperties.Foreground, ControlThemes.FocusForeground))
        .When(VariantState.Disabled, Style.Empty.Set(SceneProperties.Fill, ControlThemes.Disabled));

    /// <summary>Creates an explicit content factory; the callback may add only children below its supplied root.</summary>
    public static Func<CompositionContext, Element> Recipe(string name, Action<CompositionContext, Element> content)
    {
        ReactiveGraph.ValidateName(name, nameof(name)); ArgumentNullException.ThrowIfNull(content);
        return context => { ArgumentNullException.ThrowIfNull(context); var element = context.Element(name); content(context, element); return element; };
    }

    public static void Text(Element element, ThemeContext theme, string text, Style? style = null) => ConfigureSemantic(element, theme, TextStyle.Set(SceneProperties.Text, Required(text, nameof(text))), style, new(SemanticRole.Text, text));
    public static void Panel(Element element, ThemeContext theme, string name, Style? style = null) => ConfigureSemantic(element, theme, PanelStyle, style, new(SemanticRole.Group, Required(name, nameof(name))));
    public static void Row(Element element, ThemeContext theme, string name, Style? style = null) => ConfigureSemantic(element, theme, RowStyle, style, new(SemanticRole.Group, Required(name, nameof(name))));
    public static void Column(Element element, ThemeContext theme, string name, Style? style = null) => Panel(element, theme, name, style);
    public static void List(Element element, ThemeContext theme, string name, Style? style = null) => ConfigureSemantic(element, theme, PanelStyle, style, new(SemanticRole.List, Required(name, nameof(name))));
    public static ScrollViewportState ScrollViewport(Element element, ThemeContext theme, string name, ScrollOffset offset = default, Style? style = null)
    {
        name = Required(name, nameof(name)); offset.Validate();
        var component = PanelStyle.Set(Arrangement.Clip, true).Set(Arrangement.Scroll, offset);
        Preflight(element, theme, component, style, new ScrollViewportBehavior(name, null!));
        var state = new ScrollViewportState(element.Scope, element.Name + ".scroll", offset);
        Configure(element, theme, component, style, new ScrollViewportBehavior(name, state));
        Bind(element, state, value => element.UpdateControl(Arrangement.Scroll, value.Offset));
        return state;
    }
    public static void Button(Element element, ThemeContext theme, string label, Action? activate = null, Style? style = null)
    {
        label = Required(label, nameof(label)); Configure(element, theme, ButtonStyle.Set(SceneProperties.Text, label), style, new ButtonBehavior("button", new(SemanticRole.Button, label, actions: SemanticAction.Invoke), activate));
    }
    public static TextFieldState TextField(Element element, ThemeContext theme, string name, string value = "", Style? style = null)
    {
        name = Required(name, nameof(name)); TextFieldState.ValidateText(value);
        var component = TextFieldStyle.Set(SceneProperties.Text, value);
        Preflight(element, theme, component, style, new TextFieldBehavior(null!, name));
        var state = new TextFieldState(element.Scope, element.Name + ".text", value);
        Configure(element, theme, component, style, new TextFieldBehavior(state, name));
        _ = element.Scope.Effect(() =>
        {
            element.UpdateControl(SceneProperties.Text, state.DisplayText.Length == 0 && !state.Focused ? name : state.DisplayText);
            element.UpdateControl(SceneProperties.TextSelectionStart, state.Focused ? state.DisplaySelectionStart : null);
            element.UpdateControl(SceneProperties.TextSelectionEnd, state.Focused ? state.DisplaySelectionEnd : null);
            element.UpdateControl(SceneProperties.TextCaret, state.Focused ? state.DisplayCaret : null);
        }, element.Name + ".text-value");
        return state;
    }
    public static ControlState Selectable(Element element, ThemeContext theme, string label, Action? activate = null, Style? style = null)
    {
        label = Required(label, nameof(label)); var component = SelectableStyle.Set(SceneProperties.Text, label); var behavior = new RowActionBehavior("selectable", new(SemanticRole.ListItem, label, actions: SemanticAction.Select)); Preflight(element, theme, component, style, behavior);
        var state = new ControlState(element.Scope, element.Name + ".selectable", label);
        Configure(element, theme, component, style, new RowActionBehavior("selectable", new(SemanticRole.ListItem, label, actions: SemanticAction.Select), activate, state));
        Bind(element, state, value => element.UpdateControl(SceneProperties.Text, value.Label), value => new(SemanticRole.ListItem, value.Label, actions: SemanticAction.Select));
        return state;
    }
    public static ControlState Loading(Element element, ThemeContext theme, string label = "Loading", Style? style = null)
    {
        label = Required(label, nameof(label)); var component = TextStyle.Set(SceneProperties.Text, label); Preflight(element, theme, component, style, new SemanticBehavior(new(SemanticRole.Status, label))); var state = new ControlState(element.Scope, element.Name + ".loading", label);
        ConfigureSemantic(element, theme, component, style, new(SemanticRole.Status, label)); Bind(element, state, value => element.UpdateControl(SceneProperties.Text, value.Label), value => new(SemanticRole.Status, value.Label)); return state;
    }
    public static ControlState Progress(Element element, ThemeContext theme, string label, float value, Style? style = null)
    {
        ControlState.ValidateProgress(value); label = Required(label, nameof(label)); var text = ProgressText(label, value); var component = TextStyle.Set(SceneProperties.Text, text); Preflight(element, theme, component, style, new SemanticBehavior(new(SemanticRole.Status, label))); var state = new ControlState(element.Scope, element.Name + ".progress", label, progress: value);
        ConfigureSemantic(element, theme, component, style, new(SemanticRole.Status, label, value: Percent(value)));
        Bind(element, state, current => element.UpdateControl(SceneProperties.Text, ProgressText(current.Label, current.Progress)), current => new(SemanticRole.Status, current.Label, value: Percent(current.Progress))); return state;
    }
    public static ControlState Error(Element element, ThemeContext theme, string message, Style? style = null)
    {
        message = Required(message, nameof(message)); var component = TextStyle.Set(SceneProperties.Text, message); Preflight(element, theme, component, style, new SemanticBehavior(new(SemanticRole.Status, message))); var state = new ControlState(element.Scope, element.Name + ".error", message);
        ConfigureSemantic(element, theme, component, style, new(SemanticRole.Status, message)); Bind(element, state, value => element.UpdateControl(SceneProperties.Text, value.Label), value => new(SemanticRole.Status, value.Label)); return state;
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
