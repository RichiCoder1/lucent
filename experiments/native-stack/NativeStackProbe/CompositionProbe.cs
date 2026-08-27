using System.Text;
using System.Runtime.InteropServices;
using SDL3;
using SkiaSharp;

/// <summary>Finite straight-C# authoring values. IDs, scopes, bounds, and scene commands remain projection details.</summary>
internal static class NativeUi
{
    public static UiNode Row(params UiNode[] children) => new(UiKind.Row, children);
    public static UiNode Column(params UiNode[] children) => new(UiKind.Column, children);
    public static UiNode Text(string value) => new(UiKind.Text, [], value);
    public static UiNode Text(Func<string> value) => new(UiKind.Text, [], TextValue: value);
    public static UiNode Control(string name) => new(UiKind.Control, [], name);
    public static UiNode Button(string name, Action press) => new(UiKind.Button, [], name, Press: press);
    public static UiNode TextField(string name, Action<string> changed) => new(UiKind.TextField, [], name, Changed: changed);
    public static UiNode TextField(string name, Func<string> value, Action<string> changed) => new(UiKind.TextField, [], name, Value: value, Changed: changed);
    public static UiNode Selectable(string name, Action selected) => new(UiKind.Selectable, [], name, Select: selected);
    public static UiNode Selectable(string name, Func<bool> isSelected, Action selected) => new(UiKind.Selectable, [], name, Select: selected, IsSelected: isSelected);
    public static UiNode Selectable(Func<string> name, Func<bool> isSelected, Action selected) => new(UiKind.Selectable, [], TextValue: name, Select: selected, IsSelected: isSelected);
    public static UiNode Region(string name, Func<string?>? value = null) => new(UiKind.Region, [], name, SemanticValue: value);
    public static UiNode Show(Func<bool> visible, Func<UiNode> content) => new(UiKind.Show, [], Show: visible, Content: content);
    public static UiNode For<T>(IEnumerable<T> items, Func<T, string> key, Func<T, UiNode> item) => new(UiKind.For, [], Items: () => items.Cast<object>(), Key: value => key((T)value), Item: value => item((T)value));
    public static UiNode For<T>(Func<IEnumerable<T>> items, Func<T, string> key, Func<T, UiNode> item) => new(UiKind.For, [], Items: () => items().Cast<object>(), Key: value => key((T)value), Item: value => item((T)value), ItemsChanged: true);
    /// <summary>Fixed-height virtual-list authoring entry point; slicing, keyed ownership, and scroll state remain in composition.</summary>
    public static UiNode VirtualList<T>(string name, Func<IEnumerable<T>> items, Func<T, string> key, Func<T, UiNode> row, int rowHeight, int viewportHeight, Func<string?>? value = null)
    {
        if (rowHeight <= 0 || viewportHeight <= 0) throw new ArgumentOutOfRangeException(nameof(rowHeight));
        return new(UiKind.VirtualList, [], name, Items: () => items().Cast<object>(), Key: item => key((T)item), Item: item => row((T)item), SemanticValue: value, ItemsChanged: true, RowHeight: rowHeight, ViewportHeight: viewportHeight);
    }
    public static RetainedComposition Mount(ReactiveGraph graph, UiNode root) => new(graph, root);
}

internal enum UiKind { Row, Column, Text, Control, Button, TextField, Selectable, Region, Show, For, VirtualList }
internal enum CompositionInputKind { Pointer, Text, Key, Wheel, Tab, Semantic }
internal sealed record CompositionInput(CompositionInputKind Kind, string? Target = null, string? Value = null, int X = 0, int Y = 0, PointerKind Pointer = PointerKind.Down, bool Shift = false, bool Control = false);

internal static class CompositionInputs
{
    public static bool TryFromSdl(SDL.Event e, out CompositionInput? input)
    {
        input = ((SDL.EventType)e.Type) switch
        {
            SDL.EventType.TextInput => new(CompositionInputKind.Text, Value: Marshal.PtrToStringUTF8(e.Text.Text) ?? ""),
            SDL.EventType.KeyDown => new(CompositionInputKind.Key, Value: e.Key.Key switch { SDL.Keycode.Pagedown => "PageDown", SDL.Keycode.Pageup => "PageUp", SDL.Keycode.End => "End", SDL.Keycode.Home => "Home", SDL.Keycode.Down => "ArrowDown", SDL.Keycode.Up => "ArrowUp", SDL.Keycode.Left => "ArrowLeft", SDL.Keycode.Right => "ArrowRight", SDL.Keycode.Backspace => "Backspace", SDL.Keycode.Delete => "Delete", SDL.Keycode.Return => "Enter", SDL.Keycode.Space => "Space", SDL.Keycode.Escape => "Escape", SDL.Keycode.Tab => "Tab", SDL.Keycode.A => "A", SDL.Keycode.C => "C", SDL.Keycode.X => "X", SDL.Keycode.V => "V", _ => "" }, Shift: (e.Key.Mod & SDL.Keymod.Shift) != 0, Control: (e.Key.Mod & SDL.Keymod.Ctrl) != 0),
            SDL.EventType.MouseWheel => new(CompositionInputKind.Wheel, Value: e.Wheel.Y < 0 ? "PageDown" : "PageUp", X: (int)e.Wheel.MouseX, Y: (int)e.Wheel.MouseY),
            SDL.EventType.MouseButtonDown => new(CompositionInputKind.Pointer, X: (int)e.Button.X, Y: (int)e.Button.Y, Pointer: PointerKind.Down),
            SDL.EventType.MouseButtonUp => new(CompositionInputKind.Pointer, X: (int)e.Button.X, Y: (int)e.Button.Y, Pointer: PointerKind.Up),
            _ => null
        };
        return input is not null;
    }
}

/// <summary>Immutable finite authoring node; modifiers only alter typed layout, paint, or control values.</summary>
internal sealed record UiNode(UiKind Kind, UiNode[] Children, string? Text = null, Func<string>? TextValue = null, Func<bool>? Show = null, Func<UiNode>? Content = null, Func<IEnumerable<object>>? Items = null, Func<object, string>? Key = null, Func<object, UiNode>? Item = null, Style? StyleSpec = null, VariantStyle? VariantsSpec = null, Func<ThemeLayer>? ThemeValue = null, Func<StyleState>? StateValue = null, UiLength? LayoutGap = null, UiLength? LayoutPadding = null, Action? Press = null, Func<string>? Value = null, Action<string>? Changed = null, Action? Cancelled = null, string? EscapeFocusTarget = null, Action? Select = null, Func<bool>? IsSelected = null, Func<string?>? SemanticValue = null, TimeSpan? TransitionDuration = null, Func<bool>? ReducedMotion = null, bool ItemsChanged = false, int RowHeight = 0, int ViewportHeight = 0)
{
    public UiNode Bg(UiColor color) => this with { StyleSpec = (StyleSpec ?? new Style()).Bg(color) };
    public UiNode Bg(SemanticToken token) => this with { StyleSpec = (StyleSpec ?? new Style()).Bg(token) };
    public UiNode Fg(UiColor color) => this with { StyleSpec = (StyleSpec ?? new Style()).Fg(color) };
    public UiNode Fg(SemanticToken token) => this with { StyleSpec = (StyleSpec ?? new Style()).Fg(token) };
    public UiNode Style(Style style) => this with { StyleSpec = style };
    public UiNode Variants(VariantStyle variants) => this with { VariantsSpec = variants };
    public UiNode Theme(Func<ThemeLayer> theme) => this with { ThemeValue = theme };
    public UiNode State(Func<StyleState> state) => this with { StateValue = state };
    public UiNode Transition(TimeSpan duration, Func<bool> reducedMotion) => duration > TimeSpan.Zero ? this with { TransitionDuration = duration, ReducedMotion = reducedMotion } : throw new ArgumentOutOfRangeException(nameof(duration));
    public UiNode OnCancel(Action cancelled, string? focusTarget = null) => Kind == UiKind.TextField ? this with { Cancelled = cancelled, EscapeFocusTarget = focusTarget } : throw new InvalidOperationException("OnCancel applies only to TextField.");
    public UiNode Gap(int value) => this with { LayoutGap = Length(value) };
    public UiNode Padding(int value) => this with { LayoutPadding = Length(value) };
    public UiNode OnPress(Action action) => Kind is UiKind.Control or UiKind.Button ? this with { Press = action } : throw new InvalidOperationException("OnPress applies only to Button.");
    private static UiLength Length(int value) => value >= 0 ? new(value) : throw new ArgumentOutOfRangeException(nameof(value));
}

/// <summary>Generic retained projection from authored nodes: it owns retained elements, structural ownership, layout, scene, semantics, and disposal.</summary>
internal sealed class RetainedComposition : IDisposable
{
    private readonly ReactiveGraph _graph;
    private readonly SceneProjection _projection;
    private readonly RetainedScene _scene = new();
    private readonly ProjectedSnapshots _snapshots = new();
    private readonly InputRouter _input = new();
    private readonly FocusScopes _focus;
    private readonly FrameScheduler _scheduler;
    private readonly MountedNode _root;
    private StableElement? _rootElement;
    private ElementId? _styledFocus;
    private int _nextId;
    private bool _disposed;

    internal RetainedComposition(ReactiveGraph graph, UiNode root)
    {
        _graph = graph;
        _projection = new(_scene, _snapshots);
        _focus = new(_input);
        _scheduler = new(() => { });
        _root = Mount(root, null);
        Render();
        _graph.Drain(); // starts explicit Show callbacks after their owner exists.
    }

    public string TreeDump() => Tree(_root.Element) + "\n";
    public string LayoutDump() => Dump(_snapshots.Layout.Select(pair => $"{pair.Key.Value}=[{pair.Value.X},{pair.Value.Y},{pair.Value.Width},{pair.Value.Height}]"));
    public string StyleDump() => Dump(_snapshots.Style.Select(pair => $"{pair.Key.Value}={pair.Value.Background}/{pair.Value.Foreground}/radius={pair.Value.CornerRadius};opacity={pair.Value.Opacity};transform={pair.Value.TranslateX},{pair.Value.TranslateY},{pair.Value.Scale};size={pair.Value.Width?.Value},{pair.Value.Height?.Value};padding={pair.Value.Padding?.Value};gap={pair.Value.Gap?.Value};align={pair.Value.Alignment};type={pair.Value.Typography?.Size},{pair.Value.Typography?.Weight};border={pair.Value.Border?.Value},{pair.Value.BorderWidth?.Value};shadow={pair.Value.Shadow?.Blur},{pair.Value.Shadow?.X},{pair.Value.Shadow?.Y};ring={pair.Value.FocusRing?.Value}"));
    public string SemanticDump() => Dump(_snapshots.Semantics.Select(pair => $"{pair.Key.Value}={pair.Value.Role}/{pair.Value.Name}/{string.Join(',', pair.Value.Actions ?? [])};value={pair.Value.Value};focused={pair.Value.Focused};selected={pair.Value.Selected}"));
    public StableElement? FindText(string text) => Flatten(_root.Element).FirstOrDefault(element => element.Semantics.Name == text);
    public int SceneCount => _scene.Count;
    public int SemanticCount => _snapshots.Semantics.Count;
    public int ProjectionCalls { get; private set; }
    internal RetainedScene Scene => _scene;
    internal byte[] CapturePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap)) new SkiaSceneRenderer().Render(_scene, canvas);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
    internal IEnumerable<(string Name, Semantics Value)> Semantics() => _snapshots.Semantics.Select(pair => (pair.Key.Value, pair.Value));
    internal void Present(SdlSkiaPresenter presenter) => presenter.Present(_scene);
    internal void Advance(TimeSpan elapsed) { _scheduler.Clock.Tick(elapsed); _scheduler.Pump(); }
    internal bool TransitionActive(string name) => Mounted(name)?.TransitionActive == true;
    /// <summary>The sole generic input boundary for mounted behavior, including SDL events and proof actions.</summary>
    internal bool Input(CompositionInput input)
    {
        StableElement? target;
        switch (input.Kind)
        {
            case CompositionInputKind.Pointer:
                target = input.Target is null ? null : Flatten(_root.Element).FirstOrDefault(element => element.Semantics.Name == input.Target);
                var x = target is null ? input.X : target.Bounds.X + target.Bounds.Width / 2;
                var y = target is null ? input.Y : target.Bounds.Y + target.Bounds.Height / 2;
                return _input.Dispatch(_root.Element, input.Pointer, x, y) is not null;
            case CompositionInputKind.Text:
                if (FocusedBehavior() is TextField field) { field.Input(input.Value ?? ""); Project([field.Element]); return true; }
                return false;
            case CompositionInputKind.Key:
                if (input.Value is null) return false;
                if (input.Value is "Tab") { _focus.Next(_root.Element, input.Shift); Project(Flatten(_root.Element)); return true; }
                if (FocusedBehavior() is TextField text) { text.Key(input.Value, input.Shift, input.Control); Project([text.Element]); return true; }
                if (FocusedBehavior() is Button button) { button.Key(input.Value); return true; }
                return MountedByElement(_focus.Focused)?.HandleListKey(input.Value) == true;
            case CompositionInputKind.Wheel:
                return HitVirtualList(input.X, input.Y)?.Scroll(input.Value ?? "PageDown") == true;
            case CompositionInputKind.Tab:
                _focus.Next(_root.Element, input.Shift); Project(Flatten(_root.Element)); return true;
            case CompositionInputKind.Semantic:
                if (input.Target is null || input.Value is null) return false;
                if (input.Value == "focus") return Focus(input.Target);
                target = Flatten(_root.Element).FirstOrDefault(element => element.Semantics.Name == input.Target); if (target is null) return false;
                if (input.Value.StartsWith("set-value:", StringComparison.Ordinal) && Mounted(input.Target, "edit")?.Behavior is TextField set) { set.InvokeSemanticSetValue(input.Value[10..]); Project([set.Element]); return true; }
                if (input.Value == "press") return PressControl(input.Target);
                if (input.Value == "select") return Select(input.Target);
                if (Mounted(input.Target) is { Node.Kind: UiKind.VirtualList } list) return list.Scroll(input.Value);
                return false;
            default: return false;
        }
    }
    internal bool PressControl(string name)
    {
        var element = Flatten(_root.Element).SingleOrDefault(candidate => candidate.Semantics is { Role: "button", Name: var candidateName } && candidateName == name);
        if (element is null) return false;
        var x = element.Bounds.X + element.Bounds.Width / 2;
        var y = element.Bounds.Y + element.Bounds.Height / 2;
        return _input.Dispatch(_root.Element, PointerKind.Down, x, y) == element && _input.Dispatch(_root.Element, PointerKind.Up, x, y) == element;
    }
    internal bool EditTextField(string name, string value)
    {
        var element = Find(name, "edit");
        if (element is null) return false;
        _input.Dispatch(_root.Element, PointerKind.Down, element.Bounds.X + 1, element.Bounds.Y + 1); _input.Dispatch(_root.Element, PointerKind.Up, element.Bounds.X + 1, element.Bounds.Y + 1);
        if (Mounted(name, "edit")?.Behavior is not TextField field) return false;
        field.InvokeSemanticSetValue(value); Project([element]); return true;
    }
    internal bool Select(string name)
    {
        var element = Find(name, "option");
        if (element is null) return false;
        var x = element.Bounds.X + 1; var y = element.Bounds.Y + 1;
        var selected = _input.Dispatch(_root.Element, PointerKind.Down, x, y) == element && _input.Dispatch(_root.Element, PointerKind.Up, x, y) == element;
        Project([element]); return selected;
    }
    internal bool Focus(string name)
    {
        var element = Flatten(_root.Element).SingleOrDefault(candidate => candidate.Semantics.Name == name && _input.Get(candidate)?.Focusable == true);
        if (element is null) return false;
        if (Mounted(name, element.Semantics.Role)?.Behavior is { } behavior) behavior.Focus(true); else _focus.Focus(_root.Element, element);
        Project([element]); return true;
    }
    internal bool Pointer(string name, PointerKind kind)
    {
        var element = Find(name, null); if (element is null) return false;
        return _input.Dispatch(_root.Element, kind, element.Bounds.X + 1, element.Bounds.Y + 1) == element;
    }
    internal bool Scroll(string name, string command) => Mounted(name) is { Node.Kind: UiKind.VirtualList } list && list.Scroll(command);
    internal int ScrollOffset(string name) => Mounted(name)?.ScrollOffset ?? 0;
    internal int RealizedCount(string name) => Mounted(name)?.Children.Count ?? 0;
    internal string? FocusedName() => MountedByElement(_focus.Focused)?.Element.Semantics.Name;
    internal Bounds VirtualBounds(string name) => Mounted(name)?.Element.Bounds ?? default;
    internal (string First, string Last, Bounds FirstBounds, Bounds LastBounds) RealizedWindow(string name)
    {
        var list = Mounted(name) ?? throw new ArgumentException("Unknown virtual list.", nameof(name));
        if (list.Children.Count == 0) return ("", "", default, default);
        return (list.Node.Key!(list.AllValues[list.FirstItem]), list.Node.Key!(list.AllValues[list.FirstItem + list.Children.Count - 1]), list.Children[0].Element.Bounds, list.Children[^1].Element.Bounds);
    }
    private StableElement? Find(string name, string? role) => Flatten(_root.Element).SingleOrDefault(element => element.Semantics.Name == name && (role is null || element.Semantics.Role == role));
    private MountedNode? Mounted(string name, string? role = null) => FlattenMounted(_root).SingleOrDefault(mounted => mounted.Element.Semantics.Name == name && (role is null || mounted.Element.Semantics.Role == role));
    private MountedNode? MountedByElement(ElementId? id) => id is null ? null : FlattenMounted(_root).SingleOrDefault(mounted => mounted.Element.Id == id);
    private IElementBehavior? FocusedBehavior() => MountedByElement(_focus.Focused)?.Behavior;
    private MountedNode? HitVirtualList(int x, int y) => FlattenMounted(_root).LastOrDefault(node => node.Node.Kind == UiKind.VirtualList && Contains(node.Element.Bounds, x, y));
    private static bool Contains(Bounds bounds, int x, int y) => x >= bounds.X && y >= bounds.Y && x < bounds.X + bounds.Width && y < bounds.Y + bounds.Height;

    private MountedNode Mount(UiNode node, Func<ThemeLayer>? inheritedTheme)
    {
        var theme = node.ThemeValue ?? inheritedTheme;
        var role = node.Kind switch { UiKind.Text => "text", UiKind.Control or UiKind.Button => "button", UiKind.TextField => "edit", UiKind.Selectable => "option", UiKind.VirtualList => "listbox", UiKind.Region => "region", UiKind.Show or UiKind.For => "region", _ => "group" };
        var name = node.Text ?? (node.TextValue is not null ? "" : node.Kind.ToString());
        var actions = node.Kind is UiKind.Control or UiKind.Button ? new[] { "press" } : node.Kind == UiKind.TextField ? ["set-value"] : node.Kind == UiKind.Selectable ? ["select"] : node.Kind == UiKind.VirtualList ? ["select", "scroll"] : null;
        var element = new StableElement(new($"native.{++_nextId}"), default, Resolve(node, theme), new(role, name, node.SemanticValue?.Invoke(), actions));
        _rootElement ??= element;
        if (node.Kind is UiKind.Text or UiKind.Control or UiKind.Button or UiKind.Selectable) element.Text = node.Text ?? (node.Kind == UiKind.Selectable ? node.TextValue?.Invoke() : "") ?? "";
        var mounted = new MountedNode(this, node, element, _graph.Scope(), theme);
        if (node.Kind is UiKind.Row or UiKind.Column)
            foreach (var child in node.Children) mounted.Children.Add(Mount(child, theme));
        void InvalidateBehavior() => SetBehaviorStyles(mounted);
        if (node.Kind is UiKind.Control or UiKind.Button && node.Press is not null)
            mounted.Behavior = new Button(_rootElement!, element, _input, _focus, node.Press, InvalidateBehavior);
        if (node.Kind == UiKind.TextField)
            mounted.Behavior = new TextField(_rootElement!, element, _input, _focus, new MemoryClipboard(), node.Changed, () => { node.Cancelled?.Invoke(); if (node.EscapeFocusTarget is not null) Focus(node.EscapeFocusTarget); }, InvalidateBehavior);
        if (node.Kind == UiKind.TextField && node.Value is not null)
            mounted.ValueEffect = mounted.Scope.Effect(() => mounted.SetTextFieldValue(node.Value()), "composition.text-field-value");
        if (node.SemanticValue is not null)
            mounted.SemanticEffect = mounted.Scope.Effect(mounted.SetSemanticValue, "composition.semantic-value");
        if (node.Kind == UiKind.Selectable)
            mounted.Behavior = new Selectable(_rootElement!, element, _input, _focus, () => mounted.Node.Select?.Invoke(), InvalidateBehavior);
        if (node.Kind == UiKind.Selectable && node.TextValue is not null)
            mounted.TextEffect = mounted.Scope.Effect(() => mounted.SetText(mounted.Node.TextValue!()), "composition.selectable-text");
        if (node.IsSelected is not null)
            mounted.SelectedEffect = mounted.Scope.Effect(mounted.SetSelected, "composition.selected");
        if (node.Kind == UiKind.Text && node.TextValue is not null)
            mounted.TextEffect = mounted.Scope.Effect(() => mounted.SetText(node.TextValue()), "composition.text");
        if (theme is not null || node.StateValue is not null || node.VariantsSpec is not null || node.TransitionDuration is not null)
            mounted.StyleEffect = mounted.Scope.Effect(mounted.SetStyle, "composition.style");
        if (node.Kind == UiKind.Show)
            mounted.ShowEffect = mounted.Scope.Effect(() => mounted.SetShown(node.Show!()), "composition.show");
        if (node.Kind is UiKind.For or UiKind.VirtualList)
        {
            if (node.ItemsChanged) mounted.ForEffect = mounted.Scope.Effect(mounted.RefreshFor, "composition.for");
            else mounted.RefreshFor();
        }
        if (node.Kind == UiKind.VirtualList)
            _input.Set(element, new("scroll", true, Pointer: pointer => { if (pointer.Kind == PointerKind.Down) _focus.Focus(_rootElement!, element); }));
        mounted.Attach();
        return mounted;
    }

    private void Render()
    {
        if (_disposed) return;
        Layout(_root, 0, 0, _root.Element.Style.Width?.Value ?? 120, _root.Element.Style.Height?.Value ?? 80);
        var elements = Flatten(_root.Element).ToArray();
        Project(elements);
        _scene.Order(elements.Select(element => element.Id));
    }

    private static ResolvedStyle Resolve(UiNode node, Func<ThemeLayer>? inheritedTheme)
    {
        var theme = (node.ThemeValue ?? inheritedTheme)?.Invoke() ?? Themes.Light;
        var style = Styles.Compose(node.StyleSpec ?? new Style(), node.VariantsSpec?.Resolve(StyleState.None, theme) ?? new Style()).Resolve(theme);
        return Resolved(style, node.Kind == UiKind.Text);
    }

    private static void Layout(MountedNode mounted, int x, int y, int width, int height)
    {
        var element = mounted.Element;
        var node = mounted.Node;
        width = mounted.Element.Style.Width?.Value ?? width; height = mounted.Element.Style.Height?.Value ?? height;
        var bounds = new Bounds(x, y, Math.Max(0, width), Math.Max(0, height));
        if (element.Bounds != bounds) { element.Bounds = bounds; element.Mark(DirtyFacet.Layout); }
        var children = mounted.LayoutChildren.ToArray();
        if (children.Length == 0) return;
        if (node.Kind == UiKind.VirtualList)
        {
            for (var index = 0; index < children.Length; index++)
            {
                var virtualRow = children[index];
                Layout(virtualRow, x, y + (mounted.FirstItem + index) * node.RowHeight - mounted.ScrollOffset, width, node.RowHeight);
                SetClip(virtualRow.Element, bounds);
            }
            return;
        }
        var padding = element.Style.Padding?.Value ?? node.LayoutPadding?.Value ?? 0;
        var gap = element.Style.Gap?.Value ?? node.LayoutGap?.Value ?? 0;
        var row = node.Kind == UiKind.Row;
        var main = row ? width : height;
        var fixedMain = children.Sum(child => (row ? child.Element.Style.Width : child.Element.Style.Height)?.Value ?? 0);
        var flexible = children.Count(child => (row ? child.Element.Style.Width : child.Element.Style.Height) is null);
        var available = Math.Max(0, main - padding * 2 - gap * (children.Length - 1) - fixedMain);
        var cursor = row ? x + padding : y + padding;
        for (var index = 0; index < children.Length; index++)
        {
            var child = children[index]; var childStyle = child.Element.Style;
            var size = (row ? childStyle.Width : childStyle.Height)?.Value ?? (flexible == 0 ? 0 : available / flexible + (index < available % flexible ? 1 : 0));
            var cross = row ? childStyle.Height?.Value ?? Math.Max(0, height - padding * 2) : childStyle.Width?.Value ?? Math.Max(0, width - padding * 2);
            var alignment = childStyle.Alignment ?? UiAlignment.Stretch;
            var offset = alignment switch { UiAlignment.Center => ((row ? height : width) - cross) / 2, UiAlignment.End => (row ? height : width) - padding - cross, _ => padding };
            Layout(child, row ? cursor : x + offset, row ? y + offset : cursor, row ? size : cross, row ? cross : size);
            cursor += size + gap;
        }
    }
    private static void SetClip(StableElement element, Bounds clip)
    {
        if (element.Clip != clip) { element.Clip = clip; element.Mark(DirtyFacet.Paint); }
        foreach (var child in element.Children) SetClip(child, clip);
    }

    private void Release(MountedNode mounted)
    {
        var elements = Flatten(mounted.Element).ToArray();
        _input.Release(elements); _focus.Release(elements); _projection.Release(elements);
    }
    private void SetBehaviorStyles(MountedNode source)
    {
        if (_disposed) return;
        source.SetStyle();
        var focused = _focus.Focused;
        if (_styledFocus == focused) return;
        if (_styledFocus is { } previous) FlattenMounted(_root).FirstOrDefault(node => node.Element.Id == previous)?.SetStyle();
        if (focused is { } current && current != source.Element.Id) FlattenMounted(_root).FirstOrDefault(node => node.Element.Id == current)?.SetStyle();
        _styledFocus = focused;
    }
    private void Project(IEnumerable<StableElement> elements) { ProjectionCalls++; _scheduler.Projected(_projection.Project(elements)); }
    private static IEnumerable<StableElement> Flatten(StableElement root) => [root, .. root.Children.SelectMany(Flatten)];
    private static IEnumerable<MountedNode> FlattenMounted(MountedNode root) => [root, .. root.Children.SelectMany(FlattenMounted)];
    private static string Dump(IEnumerable<string> lines) => string.Join('\n', lines.OrderBy(line => line, StringComparer.Ordinal)) + "\n";
    private static string Tree(StableElement element) => element.Id.Value + "(" + string.Join(',', element.Children.Select(Tree)) + ")";
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _root.Dispose();
        _scheduler.Clock.Dispose();
    }

    private sealed class MountedNode(RetainedComposition owner, UiNode node, StableElement element, ReactiveScope scope, Func<ThemeLayer>? inheritedTheme) : IDisposable
    {
        private readonly Dictionary<string, MountedNode> _keyed = new(StringComparer.Ordinal);
        private MountedNode? _shown;
        public UiNode Node { get; private set; } = node;
        public StableElement Element { get; } = element;
        public ReactiveScope Scope { get; } = scope;
        public List<MountedNode> Children { get; } = [];
        public IEnumerable<MountedNode> LayoutChildren => _shown is null ? Children : [_shown, .. Children];
        public ReactiveEffect? ShowEffect { get; set; }
        public ReactiveEffect? ForEffect { get; set; }
        public ReactiveEffect? TextEffect { get; set; }
        public ReactiveEffect? ValueEffect { get; set; }
        public ReactiveEffect? SemanticEffect { get; set; }
        public ReactiveEffect? SelectedEffect { get; set; }
        public ReactiveEffect? StyleEffect { get; set; }
        public IElementBehavior? Behavior { get; set; }
        private PaintTransition? _transition;
        private ResolvedStyle? _transitionFrom;
        private ResolvedStyle? _transitionTarget;
        private bool _styleApplied;
        public bool TransitionActive => _transition?.Active == true;
        public int ScrollOffset { get; private set; }
        public int FirstItem { get; private set; }
        private object[] _allValues = [];
        internal object[] AllValues => _allValues;
        private int TotalItems { get; set; }
        public void SetStyle()
        {
            var theme = (Node.ThemeValue ?? inheritedTheme)?.Invoke() ?? Themes.Light;
            var state = (Node.StateValue?.Invoke() ?? StyleState.None) | (Behavior?.State ?? StyleState.None);
            var style = Styles.Compose(Node.StyleSpec ?? new Style(), Node.VariantsSpec?.Resolve(state, theme) ?? new Style()).Resolve(theme);
            var resolved = Resolved(style, Node.Kind == UiKind.Text);
            var reduced = Node.ReducedMotion?.Invoke() == true;
            if (!_styleApplied) { _styleApplied = true; Apply(resolved); return; }
            _transition?.SetReducedMotion(reduced);
            if (Element.Style == resolved || _transitionTarget == resolved) return;
            var layout = Element.Style.Width != resolved.Width || Element.Style.Height != resolved.Height || Element.Style.Padding != resolved.Padding || Element.Style.Gap != resolved.Gap || Element.Style.Alignment != resolved.Alignment;
            if (layout) { _transitionTarget = resolved; Element.Style = resolved; Element.Mark(DirtyFacet.Layout | DirtyFacet.Paint); owner.Render(); return; }
            if (Node.TransitionDuration is null) { Apply(resolved); return; }
            _transitionFrom = Element.Style; _transitionTarget = resolved;
            _transition ??= new PaintTransition(owner._scheduler, Node.TransitionDuration.Value, ApplyTransition);
            _transition.Start(reduced);
        }
        private void Apply(ResolvedStyle style) { _transitionTarget = style; Element.Style = style; Element.Mark(DirtyFacet.Paint); owner.Project([Element]); }
        private void ApplyTransition(float progress)
        {
            var from = _transitionFrom ?? Element.Style; var target = _transitionTarget ?? Element.Style;
            Element.Style = target with { Opacity = Lerp(from.Opacity, target.Opacity, progress), TranslateX = Lerp(from.TranslateX, target.TranslateX, progress), TranslateY = Lerp(from.TranslateY, target.TranslateY, progress), Scale = Lerp(from.Scale, target.Scale, progress) };
            Element.Mark(DirtyFacet.Paint); owner.Project([Element]);
        }
        private static float Lerp(float from, float to, float progress) => from + (to - from) * progress;
        public void Update(UiNode next)
        {
            if (next.Kind != Node.Kind) throw new InvalidOperationException("A keyed row cannot change kind without changing its key.");
            Node = next;
            var label = next.TextValue?.Invoke() ?? next.Text;
            if (label is not null) SetText(label);
            if (next.IsSelected is not null) SetSelected();
            SetStyle();
        }
        public void SetTextFieldValue(string value)
        {
            if (Behavior is not TextField field) return;
            field.SetValue(value); owner.Project([Element]);
        }
        public void SetText(string text)
        {
            if (Element.Semantics.Name == text) return;
            Element.Semantics = Element.Semantics with { Name = text }; Element.Text = text;
            Element.Mark(DirtyFacet.Paint | DirtyFacet.Semantics);
            owner.Project([Element]);
        }
        public void SetSemanticValue()
        {
            var value = Node.SemanticValue?.Invoke();
            if (Element.Semantics.Value == value) return;
            Element.Semantics = Element.Semantics with { Value = value };
            Element.Mark(DirtyFacet.Semantics); owner.Project([Element]);
        }
        public void SetSelected()
        {
            var selected = Node.IsSelected?.Invoke() == true;
            if (Element.Semantics.Selected == selected) return;
            Element.Semantics = Element.Semantics with { Selected = selected };
            Element.Mark(DirtyFacet.Semantics); SetStyle(); owner.Project([Element]);
        }
        public void SetShown(bool visible)
        {
            if (visible && _shown is null) _shown = owner.Mount(Node.Content!(), inheritedTheme);
            if (!visible && _shown is not null) { _shown.Dispose(); _shown = null; }
            Attach(); owner.Render();
        }
        public void RefreshFor()
        {
            var values = Node.Items!().ToArray();
            if (Node.Kind == UiKind.VirtualList)
            {
                var allKeys = values.Select(Node.Key!).ToArray();
                if (allKeys.Distinct(StringComparer.Ordinal).Count() != allKeys.Length) throw new ArgumentException("VirtualList keys must be unique across the complete source.");
                _allValues = values;
                TotalItems = values.Length;
                var visible = (Node.ViewportHeight + Node.RowHeight - 1) / Node.RowHeight + 4;
                var maxOffset = Math.Max(0, TotalItems * Node.RowHeight - Node.ViewportHeight);
                ScrollOffset = Math.Clamp(ScrollOffset, 0, maxOffset);
                FirstItem = Math.Max(0, ScrollOffset / Node.RowHeight - 2);
                values = values.Skip(FirstItem).Take(visible).ToArray();
            }
            var keys = values.Select(Node.Key!).ToArray();
            if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length) throw new ArgumentException("For keys must be unique.");
            foreach (var key in _keyed.Keys.Except(keys, StringComparer.Ordinal).ToArray()) { _keyed[key].Dispose(); _keyed.Remove(key); }
            foreach (var value in values)
            {
                var key = Node.Key!(value);
                var next = Node.Item!(value);
                if (_keyed.TryGetValue(key, out var existing)) existing.Update(next);
                else _keyed.Add(key, owner.Mount(next, inheritedTheme));
            }
            Children.Clear(); Children.AddRange(keys.Select(key => _keyed[key])); Attach();
            if (Node.Kind == UiKind.VirtualList) owner.Render();
        }
        public bool Scroll(string command)
        {
            if (Node.Kind != UiKind.VirtualList) return false;
            var max = Math.Max(0, TotalItems * Node.RowHeight - Node.ViewportHeight);
            ScrollOffset = command switch
            {
                "ArrowDown" => Math.Min(max, ScrollOffset + Node.RowHeight),
                "ArrowUp" => Math.Max(0, ScrollOffset - Node.RowHeight),
                "PageDown" => Math.Min(max, ScrollOffset + Node.ViewportHeight),
                "PageUp" => Math.Max(0, ScrollOffset - Node.ViewportHeight),
                "Home" => 0,
                "End" => max,
                _ => ScrollOffset
            };
            RefreshFor();
            return true;
        }
        public bool HandleListKey(string key)
        {
            if (Node.Kind != UiKind.VirtualList) return false;
            if (key is "ArrowDown" or "ArrowUp")
            {
                var selected = Element.Semantics.Value;
                var index = selected is null ? (key == "ArrowDown" ? -1 : TotalItems) : Array.FindIndex(_allValues, item => Node.Key!(item) == selected);
                index = Math.Clamp(index + (key == "ArrowDown" ? 1 : -1), 0, Math.Max(0, TotalItems - 1));
                if (TotalItems == 0) return true;
                var value = _allValues[index]; Element.Semantics = Element.Semantics with { Value = Node.Key!(value) }; Element.Mark(DirtyFacet.Semantics);
                var top = index * Node.RowHeight; var bottom = top + Node.RowHeight;
                if (top < ScrollOffset) ScrollOffset = top;
                else if (bottom > ScrollOffset + Node.ViewportHeight) ScrollOffset = bottom - Node.ViewportHeight;
                RefreshFor();
                var child = _keyed[Node.Key!(value)];
                child.Node.Select?.Invoke();
                owner.Project([Element]);
                return true;
            }
            return Scroll(key);
        }
        public void Attach()
        {
            Element.Children.Clear();
            if (_shown is not null) Element.Children.Add(_shown.Element);
            Element.Children.AddRange(Children.Select(child => child.Element));
        }
        public void Dispose()
        {
            ShowEffect?.Dispose();
            ForEffect?.Dispose();
            TextEffect?.Dispose();
            ValueEffect?.Dispose();
            SemanticEffect?.Dispose();
            SelectedEffect?.Dispose();
            StyleEffect?.Dispose();
            _transition?.Dispose();
            Behavior?.Dispose();
            _shown?.Dispose();
            foreach (var child in Children.Distinct().ToArray()) child.Dispose();
            Children.Clear(); _keyed.Clear(); owner.Release(this); Scope.Dispose();
        }
    }

    private static ResolvedStyle Resolved(Style style, bool transparent = false) => new(style.Background?.Value ?? (transparent ? "#00000000" : "#000000"), style.Foreground?.Value ?? "#ffffff", style.Radius?.Value ?? 0, style.Opacity?.Value ?? 1, style.Transform?.X ?? 0, style.Transform?.Y ?? 0, style.Transform?.Scale ?? 1,
        style.Width, style.Height, style.PaddingX, style.Gap, style.Alignment, style.Typography, style.Border, style.BorderWidth, style.Shadow, style.FocusRing);
}

internal sealed record CompositionCheckResult(bool Ok, bool TypedAuthoring, bool ReactiveTextStable, bool ReactiveStyleFacets, bool MountedBehaviors, bool UnrelatedWriteIdle, bool DisposedAsyncCannotUpdate, bool ControlPressed, bool DumpsMatch, bool ShowStable, bool KeyedStable, bool Released, bool NativePresented, int RasterDifferencePixels, uint NativeDpi, string Tree, string Layout, string Style, string ReactiveStyle, string BehaviorSemantics, string Semantics);

internal static class CompositionProbe
{
    public static CompositionCheckResult Run()
    {
        var first = Scenario(presentNative: true);
        var second = Scenario();
        var dumpsMatch = first.Dumps == second.Dumps;
        var bindings = ReactiveBindings();
        var behaviors = MountedBehaviorCheck();
        var virtualList = VirtualListBinding();
        var result = new CompositionCheckResult(first.TypedAuthoring && virtualList && bindings.TextStable && bindings.StyleFacets && behaviors.Ok && bindings.UnrelatedIdle && bindings.DisposedAsyncCannotUpdate && first.ControlPressed && dumpsMatch && first.ShowStable && first.KeyedStable && first.Released && first.Native.Presented && first.Native.RasterDifferencePixels == 0,
            first.TypedAuthoring && virtualList, bindings.TextStable, bindings.StyleFacets, behaviors.Ok, bindings.UnrelatedIdle, bindings.DisposedAsyncCannotUpdate, first.ControlPressed, dumpsMatch, first.ShowStable, first.KeyedStable, first.Released, first.Native.Presented, first.Native.RasterDifferencePixels, first.Native.Dpi, first.Dumps.Tree, first.Dumps.Layout, first.Dumps.Style, bindings.StyleDump, behaviors.Semantics, first.Dumps.Semantics);
        if (!result.Ok) throw new InvalidOperationException($"Composition self-check failed: text={bindings.TextStable}; style={bindings.StyleFacets}; behavior={behaviors.Ok}({behaviors.Semantics}); idle={bindings.UnrelatedIdle}; async={bindings.DisposedAsyncCannotUpdate}; press={first.ControlPressed}; stable={dumpsMatch}; show={first.ShowStable}; keyed={first.KeyedStable}; released={first.Released}; native={first.Native.Presented}/{first.Native.RasterDifferencePixels}.");
        return result;
    }

    private static bool VirtualListBinding()
    {
        var graph = new ReactiveGraph();
        var items = graph.Signal(Enumerable.Range(0, 10_000).Select(index => (Id: $"item-{index:D5}", Title: $"Item {index}")).ToArray(), "composition.virtual-items");
        var selected = graph.Signal("", "composition.virtual-selected");
        using var app = NativeUi.Mount(graph, NativeUi.VirtualList("Virtual", () => items.Value, item => item.Id,
            item => NativeUi.Selectable(item.Title, () => selected.Value == item.Id, () => selected.Value = item.Id).Style(new Style().Size(100, 10)), 10, 100, () => selected.Value).Style(new Style().Size(100, 100)));
        graph.Drain();
        var bounded = app.RealizedCount("Virtual") == 14;
        app.Input(new(CompositionInputKind.Semantic, "Virtual", "focus"));
        app.Input(new(CompositionInputKind.Key, Value: "ArrowDown")); graph.Drain();
        var keyboard = selected.Value == "item-00000" && app.FocusedName() == "Virtual";
        var before = app.FindText("Item 0");
        items.Value = items.Value.Select(item => item.Id == "item-00000" ? (item.Id, "Updated") : item).ToArray(); graph.Drain();
        var updated = before is not null && ReferenceEquals(before, app.FindText("Updated"));
        app.Scroll("Virtual", "End"); var end = app.RealizedWindow("Virtual"); var viewport = app.VirtualBounds("Virtual");
        var rowCommands = app.Scene.Commands.Where(command => command.Label?.StartsWith("Item ", StringComparison.Ordinal) == true || command.Label == "Updated").ToArray();
        var clipped = end.Last == "item-09999" && end.LastBounds.Y < viewport.Y + viewport.Height
            && rowCommands.Length == app.RealizedCount("Virtual") && rowCommands.All(command => command.Clip == viewport)
            && rowCommands.Any(command => command.Bounds.Y < viewport.Y || command.Bounds.Y + command.Bounds.Height > viewport.Y + viewport.Height);
        return bounded && keyboard && updated && clipped;
    }

    private static BindingResult ReactiveBindings()
    {
        var graph = new ReactiveGraph();
        var text = graph.Signal("one", "composition.text");
        var unrelated = graph.Signal(0, "composition.unrelated");
        var evaluations = 0;
        using var app = NativeUi.Mount(graph, NativeUi.Column(NativeUi.Text(() => { evaluations++; return text.Value; }), NativeUi.Text("fixed")));
        var bound = app.FindText("one")!;
        var projections = app.ProjectionCalls;
        unrelated.Value++; graph.Drain();
        var unrelatedIdle = app.ProjectionCalls == projections;
        text.Value = "two"; graph.Drain();
        var textStable = ReferenceEquals(bound, app.FindText("two")) && app.Scene.Commands.Single(command => command.Id == bound.Id).Label == "two" && app.ProjectionCalls == projections + 1 && evaluations == 2;

        var theme = graph.Signal(Themes.Light, "composition.theme");
        var state = graph.Signal(StyleState.Selected, "composition.style-state");
        using var styled = NativeUi.Mount(graph, NativeUi.Control("Theme-aware")
            .Style(new Style().Size(80, 20).Padding(2).Gap(1).Align(UiAlignment.Center).Type(14, 600).Bg(SemanticToken.Card).Fg(SemanticToken.CardForeground).Border(new UiColor("#333333")).Rounded(4).Shadow(2).Opacity(.8f).Transform(1, 2).Ring(2))
            .Variants(new VariantStyle(new Style().Bg(SemanticToken.Card).Fg(SemanticToken.CardForeground), Selected: new Style().Bg(SemanticToken.Primary), FocusVisible: new Style().Ring(3)))
            .Theme(() => theme.Value).State(() => state.Value));
        var lightDump = styled.StyleDump();
        theme.Value = Themes.Dark; state.Value |= StyleState.FocusVisible; graph.Drain();
        var darkDump = styled.StyleDump();
        using var inherited = NativeUi.Mount(graph, NativeUi.Column(
            NativeUi.Column(
                NativeUi.Text("Inherited").Fg(SemanticToken.MutedForeground),
                NativeUi.Text("Override").Fg(SemanticToken.MutedForeground).Theme(() => Themes.Light))
                .Theme(() => Themes.Dark)));
        var inheritedElement = inherited.FindText("Inherited")!;
        var overrideElement = inherited.FindText("Override")!;
        var nestedTheme = inherited.Scene.Commands.Single(command => command.Id == inheritedElement.Id).Style?.Foreground == Themes.Dark.Get(SemanticToken.MutedForeground).Value
            && inherited.Scene.Commands.Single(command => command.Id == overrideElement.Id).Style?.Foreground == Themes.Light.Get(SemanticToken.MutedForeground).Value;
        var styleFacets = lightDump.Contains("#18181b/#09090b/radius=4;opacity=0.8;transform=1,2,1;size=80,20;padding=2;gap=1;align=Center;type=14,600;border=#333333,1;shadow=2,0,0;ring=2", StringComparison.Ordinal) && darkDump.Contains("#fafafa/#fafafa/radius=4;opacity=0.8;transform=1,2,1;size=80,20;padding=2;gap=1;align=Center;type=14,600;border=#333333,1;shadow=2,0,0;ring=3", StringComparison.Ordinal) && nestedTheme && state.Value.HasFlag(StyleState.Selected);

        var pending = new TaskCompletionSource<string>();
        var scope = graph.Scope();
        var async = scope.AsyncComputed(_ => pending.Task, "composition.async");
        var stale = NativeUi.Mount(graph, NativeUi.Text(() => async.Value ?? "pending"));
        graph.Drain(); stale.Dispose(); scope.Dispose(); pending.SetResult("late"); graph.Drain();
        var disposedAsyncCannotUpdate = stale.SceneCount == 0 && stale.SemanticCount == 0;
        return new(textStable, styleFacets, darkDump, unrelatedIdle, disposedAsyncCannotUpdate);
    }

    private static BehaviorResult MountedBehaviorCheck()
    {
        var graph = new ReactiveGraph(); var presses = 0; var edits = ""; var selections = 0;
        var app = NativeUi.Mount(graph, NativeUi.Column(
            NativeUi.Button("Save", () => presses++).Variants(new VariantStyle(new Style().Bg(new UiColor("#101010")), Pressed: new Style().Bg(new UiColor("#202020")), FocusVisible: new Style().Bg(new UiColor("#303030")))),
            NativeUi.TextField("Title", value => edits = value).Variants(new VariantStyle(new Style().Bg(new UiColor("#404040")), FocusVisible: new Style().Bg(new UiColor("#505050")))),
            NativeUi.Selectable("Issue 22", () => selections++).Variants(new VariantStyle(new Style().Bg(new UiColor("#606060")), Selected: new Style().Bg(new UiColor("#707070")), FocusVisible: new Style().Bg(new UiColor("#808080"))))));
        var down = app.Pointer("Save", PointerKind.Down) && app.StyleDump().Contains("#202020", StringComparison.Ordinal);
        var up = app.Pointer("Save", PointerKind.Up) && app.StyleDump().Contains("#101010", StringComparison.Ordinal);
        var buttonFocus = app.Focus("Save") && app.StyleDump().Contains("#303030", StringComparison.Ordinal);
        var press = down && up && buttonFocus && presses == 1; var edit = app.EditTextField("Title", "Typed") && edits == "Typed"; var focus = app.Focus("Title") && app.StyleDump().Contains("#505050", StringComparison.Ordinal); var selected = app.Select("Issue 22") && selections == 1 && app.StyleDump().Contains("#707070", StringComparison.Ordinal); selected &= app.Focus("Issue 22") && app.StyleDump().Contains("#808080", StringComparison.Ordinal);
        var semantics = app.SemanticDump(); var portable = semantics.Contains("button/Save/press", StringComparison.Ordinal) && semantics.Contains("edit/Title/set-value;value=Typed;focused=False", StringComparison.Ordinal) && semantics.Contains("option/Issue 22/select;value=;focused=True;selected=True", StringComparison.Ordinal);
        app.Dispose(); return new(press && edit && focus && selected && portable && app.SceneCount == 0 && app.SemanticCount == 0, semantics);
    }

    private static ScenarioResult Scenario(bool presentNative = false)
    {
        var graph = new ReactiveGraph();
        var details = graph.Signal(true, "composition.details");
        var rows = graph.Signal(new[] { "a", "b" }, "composition.rows");
        var rowEvaluations = 0;
        var presses = 0;
        using var app = NativeUi.Mount(graph,
            NativeUi.Column(
                NativeUi.Text("Issues").Fg(SemanticToken.Primary),
                NativeUi.Row(
                    NativeUi.Control("Save").OnPress(() => presses++),
                    NativeUi.Show(() => details.Value, () => NativeUi.Text("Details"))),
                NativeUi.For(() => { rowEvaluations++; return rows.Value; }, issue => issue, issue => NativeUi.Text(issue)))
            .Gap(3).Padding(2).Bg(SemanticToken.Background).Fg(SemanticToken.Foreground));
        var detailsBefore = app.FindText("Details");
        graph.Drain();
        var showStable = detailsBefore is not null && ReferenceEquals(detailsBefore, app.FindText("Details"));
        var controlPressed = app.PressControl("Save") && presses == 1;
        var b = app.FindText("b");
        rows.Value = ["b", "c"]; graph.Drain();
        var keyedStable = b is not null && ReferenceEquals(b, app.FindText("b")) && app.FindText("a") is null && app.FindText("c") is not null && rowEvaluations == 2;
        details.Value = false; graph.Drain();
        var hidden = app.FindText("Details") is null;
        details.Value = true; graph.Drain();
        var typedAuthoring = hidden && app.FindText("Details") is not null;
        var dumps = new Dumps(app.TreeDump(), app.LayoutDump(), app.StyleDump(), app.SemanticDump());
        var native = presentNative ? Present(app) : new NativePresentation(false, 0, 0);
        app.Dispose();
        return new(typedAuthoring, controlPressed, showStable, keyedStable, app.SceneCount == 0 && app.SemanticCount == 0, dumps, native);
    }

    private static NativePresentation Present(RetainedComposition app)
    {
        const int width = 120;
        const int height = 80;
        var renderer = new SkiaSceneRenderer();
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap)) renderer.Render(app.Scene, canvas);
        using var host = new WindowHost("NativeStackProbe composition", width, height, SDL.WindowFlags.Hidden);
        using var presenter = new SdlSkiaPresenter(host.Window, renderer);
        presenter.Present(app.Scene);
        var capture = presenter.LastCapture ?? throw new InvalidOperationException("Composition native present did not capture the renderer output.");
        return new(presenter.PresentCalls == 1 && capture.Width == width && capture.Height == height && host.ReadDpi() > 0, Difference(bitmap.Bytes, capture.Pixels), host.ReadDpi());
    }

    private static int Difference(byte[] left, byte[] right)
    {
        if (left.Length != right.Length || left.Length % 4 != 0) return int.MaxValue;
        var different = 0;
        for (var pixel = 0; pixel < left.Length; pixel += 4)
            if (left[pixel] != right[pixel] || left[pixel + 1] != right[pixel + 1] || left[pixel + 2] != right[pixel + 2] || left[pixel + 3] != right[pixel + 3]) different++;
        return different;
    }

    private sealed record Dumps(string Tree, string Layout, string Style, string Semantics);
    private sealed record NativePresentation(bool Presented, int RasterDifferencePixels, uint Dpi);
    private sealed record BindingResult(bool TextStable, bool StyleFacets, string StyleDump, bool UnrelatedIdle, bool DisposedAsyncCannotUpdate);
    private sealed record BehaviorResult(bool Ok, string Semantics);
    private sealed record ScenarioResult(bool TypedAuthoring, bool ControlPressed, bool ShowStable, bool KeyedStable, bool Released, Dumps Dumps, NativePresentation Native);
}
