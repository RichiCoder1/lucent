using System.Text;
using SDL3;
using SkiaSharp;

/// <summary>Finite straight-C# authoring values. IDs, scopes, bounds, and scene commands remain projection details.</summary>
internal static class NativeUi
{
    public static UiNode Row(params UiNode[] children) => new(UiKind.Row, children);
    public static UiNode Column(params UiNode[] children) => new(UiKind.Column, children);
    public static UiNode Text(string value) => new(UiKind.Text, [], value);
    public static UiNode Control(string name) => new(UiKind.Control, [], name);
    public static UiNode Show(Func<bool> visible, Func<UiNode> content) => new(UiKind.Show, [], Show: visible, Content: content);
    public static UiNode For<T>(IEnumerable<T> items, Func<T, string> key, Func<T, UiNode> item) => new(UiKind.For, [], Items: () => items.Cast<object>(), Key: value => key((T)value), Item: value => item((T)value));
    public static RetainedComposition Mount(ReactiveGraph graph, UiNode root) => new(graph, root);
}

internal enum UiKind { Row, Column, Text, Control, Show, For }

/// <summary>Immutable finite authoring node; modifiers only alter typed layout, paint, or control values.</summary>
internal sealed record UiNode(UiKind Kind, UiNode[] Children, string? Text = null, Func<bool>? Show = null, Func<UiNode>? Content = null, Func<IEnumerable<object>>? Items = null, Func<object, string>? Key = null, Func<object, UiNode>? Item = null, Style? Style = null, UiLength? LayoutGap = null, UiLength? LayoutPadding = null, Action? Press = null)
{
    public UiNode Bg(UiColor color) => this with { Style = (Style ?? new Style()).Bg(color) };
    public UiNode Bg(SemanticToken token) => this with { Style = (Style ?? new Style()).Bg(token) };
    public UiNode Fg(UiColor color) => this with { Style = (Style ?? new Style()).Fg(color) };
    public UiNode Fg(SemanticToken token) => this with { Style = (Style ?? new Style()).Fg(token) };
    public UiNode Gap(int value) => this with { LayoutGap = Length(value) };
    public UiNode Padding(int value) => this with { LayoutPadding = Length(value) };
    public UiNode OnPress(Action action) => Kind == UiKind.Control ? this with { Press = action } : throw new InvalidOperationException("OnPress applies only to Control.");
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
    private readonly MountedNode _root;
    private int _nextId;
    private bool _disposed;

    internal RetainedComposition(ReactiveGraph graph, UiNode root)
    {
        _graph = graph;
        _projection = new(_scene, _snapshots);
        _focus = new(_input);
        _root = Mount(root);
        Render();
        _graph.Drain(); // starts explicit Show callbacks after their owner exists.
    }

    public void Refresh() { ThrowIfDisposed(); _root.Refresh(); Render(); }
    public string TreeDump() => Tree(_root.Element) + "\n";
    public string LayoutDump() => Dump(_snapshots.Layout.Select(pair => $"{pair.Key.Value}=[{pair.Value.X},{pair.Value.Y},{pair.Value.Width},{pair.Value.Height}]"));
    public string StyleDump() => Dump(_snapshots.Style.Select(pair => $"{pair.Key.Value}={pair.Value.Background}/{pair.Value.Foreground}/{pair.Value.CornerRadius}"));
    public string SemanticDump() => Dump(_snapshots.Semantics.Select(pair => $"{pair.Key.Value}={pair.Value.Role}/{pair.Value.Name}/{string.Join(',', pair.Value.Actions ?? [])}"));
    public StableElement? FindText(string text) => Flatten(_root.Element).FirstOrDefault(element => element.Semantics.Name == text);
    public int SceneCount => _scene.Count;
    public int SemanticCount => _snapshots.Semantics.Count;
    internal RetainedScene Scene => _scene;
    internal bool PressControl(string name)
    {
        var element = Flatten(_root.Element).SingleOrDefault(candidate => candidate.Semantics is { Role: "button", Name: var candidateName } && candidateName == name);
        if (element is null) return false;
        var x = element.Bounds.X + element.Bounds.Width / 2;
        var y = element.Bounds.Y + element.Bounds.Height / 2;
        return _input.Dispatch(_root.Element, PointerKind.Down, x, y) == element && _input.Dispatch(_root.Element, PointerKind.Up, x, y) == element;
    }

    private MountedNode Mount(UiNode node)
    {
        var role = node.Kind switch { UiKind.Text => "text", UiKind.Control => "button", UiKind.Show or UiKind.For => "region", _ => "group" };
        var name = node.Text ?? node.Kind.ToString();
        var actions = node.Kind == UiKind.Control ? new[] { "press" } : null;
        var element = new StableElement(new($"native.{++_nextId}"), default, Resolve(node), new(role, name, Actions: actions));
        var mounted = new MountedNode(this, node, element, _graph.Scope());
        if (node.Kind is UiKind.Row or UiKind.Column)
            foreach (var child in node.Children) mounted.Children.Add(Mount(child));
        if (node.Kind == UiKind.Control && node.Press is not null)
            _input.Set(element, new("press", true, true, Pointer: pointer => { if (pointer.Kind == PointerKind.Up) node.Press(); }));
        if (node.Kind == UiKind.Show)
            mounted.ShowEffect = mounted.Scope.Effect(() => mounted.SetShown(node.Show!()), "composition.show");
        if (node.Kind == UiKind.For)
            mounted.RefreshFor();
        mounted.Attach();
        return mounted;
    }

    private void Render()
    {
        if (_disposed) return;
        Layout(_root, 0, 0, 120, 80);
        _projection.Project(Flatten(_root.Element));
    }

    private static ResolvedStyle Resolve(UiNode node)
    {
        var style = (node.Style ?? new Style()).Resolve(Themes.Light);
        return new(style.Background?.Value ?? "#000000", style.Foreground?.Value ?? "#ffffff", style.Radius?.Value ?? 0);
    }

    private static void Layout(MountedNode mounted, int x, int y, int width, int height)
    {
        var element = mounted.Element;
        var node = mounted.Node;
        var bounds = new Bounds(x, y, Math.Max(0, width), Math.Max(0, height));
        if (element.Bounds != bounds) { element.Bounds = bounds; element.Mark(DirtyFacet.Layout); }
        var children = mounted.LayoutChildren.ToArray();
        if (children.Length == 0) return;
        var padding = node.LayoutPadding?.Value ?? 0;
        var gap = node.LayoutGap?.Value ?? 0;
        var row = node.Kind == UiKind.Row;
        var available = Math.Max(0, (row ? width : height) - padding * 2 - gap * (children.Length - 1));
        var cursor = row ? x + padding : y + padding;
        for (var index = 0; index < children.Length; index++)
        {
            var size = available / children.Length + (index < available % children.Length ? 1 : 0);
            Layout(children[index], row ? cursor : x + padding, row ? y + padding : cursor, row ? size : Math.Max(0, width - padding * 2), row ? Math.Max(0, height - padding * 2) : size);
            cursor += size + gap;
        }
    }

    private void Release(MountedNode mounted)
    {
        var elements = Flatten(mounted.Element).ToArray();
        _input.Release(elements); _focus.Release(elements); _projection.Release(elements);
    }
    private static IEnumerable<StableElement> Flatten(StableElement root) => [root, .. root.Children.SelectMany(Flatten)];
    private static string Dump(IEnumerable<string> lines) => string.Join('\n', lines.OrderBy(line => line, StringComparer.Ordinal)) + "\n";
    private static string Tree(StableElement element) => element.Id.Value + "(" + string.Join(',', element.Children.Select(Tree)) + ")";
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _root.Dispose();
    }

    private sealed class MountedNode(RetainedComposition owner, UiNode node, StableElement element, ReactiveScope scope) : IDisposable
    {
        private readonly Dictionary<string, MountedNode> _keyed = new(StringComparer.Ordinal);
        private MountedNode? _shown;
        public UiNode Node { get; } = node;
        public StableElement Element { get; } = element;
        public ReactiveScope Scope { get; } = scope;
        public List<MountedNode> Children { get; } = [];
        public IEnumerable<MountedNode> LayoutChildren => _shown is null ? Children : [_shown, .. Children];
        public ReactiveEffect? ShowEffect { get; set; }
        public void SetShown(bool visible)
        {
            if (visible && _shown is null) _shown = owner.Mount(Node.Content!());
            if (!visible && _shown is not null) { _shown.Dispose(); _shown = null; }
            Attach(); owner.Render();
        }
        public void Refresh()
        {
            if (Node.Kind == UiKind.For) RefreshFor();
            foreach (var child in Children.ToArray()) child.Refresh();
            _shown?.Refresh();
            Attach();
        }
        public void RefreshFor()
        {
            var values = Node.Items!().ToArray();
            var keys = values.Select(Node.Key!).ToArray();
            if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length) throw new ArgumentException("For keys must be unique.");
            foreach (var key in _keyed.Keys.Except(keys, StringComparer.Ordinal).ToArray()) { _keyed[key].Dispose(); _keyed.Remove(key); }
            foreach (var value in values) { var key = Node.Key!(value); if (!_keyed.ContainsKey(key)) _keyed.Add(key, owner.Mount(Node.Item!(value))); }
            Children.Clear(); Children.AddRange(keys.Select(key => _keyed[key])); Attach();
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
            _shown?.Dispose();
            foreach (var child in Children.Distinct().ToArray()) child.Dispose();
            Children.Clear(); _keyed.Clear(); owner.Release(this); Scope.Dispose();
        }
    }
}

internal sealed record CompositionCheckResult(bool Ok, bool TypedAuthoring, bool ControlPressed, bool DumpsMatch, bool ShowStable, bool KeyedStable, bool Released, bool NativePresented, int RasterDifferencePixels, uint NativeDpi, string Tree, string Layout, string Style, string Semantics);

internal static class CompositionProbe
{
    public static CompositionCheckResult Run()
    {
        var first = Scenario(presentNative: true);
        var second = Scenario();
        var dumpsMatch = first.Dumps == second.Dumps;
        var result = new CompositionCheckResult(first.TypedAuthoring && first.ControlPressed && dumpsMatch && first.ShowStable && first.KeyedStable && first.Released && first.Native.Presented && first.Native.RasterDifferencePixels == 0,
            first.TypedAuthoring, first.ControlPressed, dumpsMatch, first.ShowStable, first.KeyedStable, first.Released, first.Native.Presented, first.Native.RasterDifferencePixels, first.Native.Dpi, first.Dumps.Tree, first.Dumps.Layout, first.Dumps.Style, first.Dumps.Semantics);
        if (!result.Ok) throw new InvalidOperationException("Composition self-check failed.");
        return result;
    }

    private static ScenarioResult Scenario(bool presentNative = false)
    {
        var graph = new ReactiveGraph();
        var details = graph.Signal(true, "composition.details");
        var rows = new List<string> { "a", "b" };
        var presses = 0;
        using var app = NativeUi.Mount(graph,
            NativeUi.Column(
                NativeUi.Text("Issues").Fg(SemanticToken.Primary),
                NativeUi.Row(
                    NativeUi.Control("Save").OnPress(() => presses++),
                    NativeUi.Show(() => details.Value, () => NativeUi.Text("Details"))),
                NativeUi.For(rows, issue => issue, issue => NativeUi.Text(issue)))
            .Gap(3).Padding(2).Bg(SemanticToken.Background).Fg(SemanticToken.Foreground));
        var detailsBefore = app.FindText("Details");
        graph.Drain();
        var showStable = detailsBefore is not null && ReferenceEquals(detailsBefore, app.FindText("Details"));
        var controlPressed = app.PressControl("Save") && presses == 1;
        var b = app.FindText("b");
        rows.Remove("a"); rows.Add("c"); app.Refresh();
        var keyedStable = b is not null && ReferenceEquals(b, app.FindText("b")) && app.FindText("a") is null && app.FindText("c") is not null;
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
    private sealed record ScenarioResult(bool TypedAuthoring, bool ControlPressed, bool ShowStable, bool KeyedStable, bool Released, Dumps Dumps, NativePresentation Native);
}
