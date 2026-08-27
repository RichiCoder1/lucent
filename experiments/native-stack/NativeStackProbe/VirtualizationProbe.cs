/// <summary>Fixed-height scroll placement; variable-height rows are deliberately out of scope.</summary>
internal sealed class ScrollViewport
{
    private readonly Action _changed;
    public StableElement Element { get; }
    public int Offset { get; private set; }
    public int ViewportHeight => Element.Bounds.Height;
    public int ContentHeight { get; private set; }

    public ScrollViewport(StableElement root, InputRouter input, int width, int height, Action changed)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        _changed = changed;
        Element = new(new("issues.viewport"), new(0, 0, width, height), new("#18181b", "#fafafa", 0), new("list", "Issues", Actions: ["scroll"]));
        root.Children.Add(Element);
        input.Set(Element, new("scroll", FocusScope: true));
    }

    public void SetContentHeight(int height) { ContentHeight = Math.Max(0, height); SetOffset(Offset); }
    public void Wheel(int delta) => ScrollBy(-delta);
    public void Key(string key) => ScrollBy(key switch { "ArrowDown" => 10, "ArrowUp" => -10, "PageDown" => ViewportHeight, "PageUp" => -ViewportHeight, "Home" => -Offset, "End" => ContentHeight, _ => 0 });
    public void ScrollBy(int pixels) => SetOffset(Offset + pixels);
    private void SetOffset(int offset)
    {
        var next = Math.Clamp(offset, 0, Math.Max(0, ContentHeight - ViewportHeight));
        if (next == Offset) return;
        Offset = next;
        _changed();
    }
}

/// <summary>Keyed, fixed-height list with a deliberately small realized window.</summary>
internal sealed class VirtualizedList : IDisposable
{
    private const int Overscan = 2;
    private readonly ReactiveGraph _graph;
    private readonly ReactiveSignal<int> _lifetimeTick;
    private readonly StableElement _root;
    private readonly InputRouter _input;
    private readonly FocusScopes _focus;
    private readonly SceneProjection _projection;
    private readonly StructuralRegion _region;
    private readonly StableElement _content;
    private readonly Dictionary<string, StableElement> _elements = new(StringComparer.Ordinal);
    private readonly HashSet<string> _realized = new(StringComparer.Ordinal);
    private string[] _keys;
    private string? _focusedKey;
    private int _releasedScopes;
    private int _effectRuns;

    public ScrollViewport Viewport { get; }
    public string? SelectedKey { get; private set; }
    public int RealizedCount => _realized.Count;
    public int RealizedLimit => (Viewport.ViewportHeight + RowHeight - 1) / RowHeight + Overscan * 2;
    public int ReleasedScopes => _releasedScopes;
    public int EffectRuns => _effectRuns;
    public int RowHeight { get; }
    public StableElement? Row(string key) => _elements.GetValueOrDefault(key);

    public VirtualizedList(ReactiveGraph graph, StableElement root, InputRouter input, FocusScopes focus, SceneProjection projection, IEnumerable<string> keys, int width, int height, int rowHeight = 10)
    {
        if (rowHeight <= 0) throw new ArgumentOutOfRangeException(nameof(rowHeight));
        _graph = graph; _root = root; _input = input; _focus = focus; _projection = projection; RowHeight = rowHeight;
        _keys = Validate(keys);
        _lifetimeTick = graph.Signal(0, "issues.row-lifetime");
        Viewport = new(root, input, width, height, Synchronize);
        _content = new(new("issues.content"), new(0, 0, width, height), new("#18181b", "#fafafa", 0), new("group", "Issue rows"));
        Viewport.Element.Children.Add(_content);
        _region = new(_content, graph, projection, input, focus);
        Synchronize();
    }

    public void Wheel(int delta) => Viewport.Wheel(delta);
    public void Key(string key)
    {
        if (key is "ArrowDown" or "ArrowUp") { MoveFocus(key == "ArrowDown" ? 1 : -1); return; }
        Viewport.Key(key);
    }
    public void Select(string key, bool focus = false)
    {
        var index = Array.IndexOf(_keys, key);
        if (index < 0) throw new ArgumentException("Unknown issue key.", nameof(key));
        SelectedKey = key;
        EnsureVisible(index);
        Synchronize();
        if (focus) Focus(key);
        UpdateSelectionSemantics();
    }
    public void SetItems(IEnumerable<string> keys)
    {
        var selectedIndex = SelectedKey is { } selected ? Array.IndexOf(_keys, selected) : -1;
        var focusedIndex = _focusedKey is { } focused ? Array.IndexOf(_keys, focused) : -1;
        _keys = Validate(keys);
        if (SelectedKey is not null && Array.IndexOf(_keys, SelectedKey) < 0)
            SelectedKey = _keys.Length == 0 ? null : _keys[Math.Min(selectedIndex, _keys.Length - 1)];
        if (_focusedKey is not null && Array.IndexOf(_keys, _focusedKey) < 0)
            _focusedKey = _keys.Length == 0 ? null : _keys[Math.Min(focusedIndex, _keys.Length - 1)];
        if (_focusedKey is not null) SelectedKey = _focusedKey;
        if (SelectedKey is { } selectedKey) EnsureVisible(Array.IndexOf(_keys, selectedKey));
        Synchronize();
        if (_focusedKey is { } focusedKey) Focus(focusedKey);
        UpdateSelectionSemantics();
    }
    public void TickRows() { _lifetimeTick.Value++; _graph.Drain(); }
    public void Dispose()
    {
        _region.Dispose();
        _input.Release([Viewport.Element]); _focus.Release(Flatten(Viewport.Element)); _projection.Release(Flatten(Viewport.Element));
        _root.Children.Remove(Viewport.Element); _lifetimeTick.Dispose(); _realized.Clear(); _elements.Clear();
    }

    private void MoveFocus(int delta)
    {
        if (_keys.Length == 0) return;
        var current = _focusedKey ?? SelectedKey;
        var index = current is null ? (delta > 0 ? -1 : _keys.Length) : Array.IndexOf(_keys, current);
        Focus(_keys[Math.Clamp(index + delta, 0, _keys.Length - 1)]);
    }
    private void Focus(string key)
    {
        _focusedKey = key;
        SelectedKey = key;
        EnsureVisible(Array.IndexOf(_keys, key));
        Synchronize();
        _focus.Focus(_root, _elements[key]);
        UpdateSelectionSemantics();
    }
    private void EnsureVisible(int index)
    {
        var top = index * RowHeight; var bottom = top + RowHeight;
        if (top < Viewport.Offset) Viewport.ScrollBy(top - Viewport.Offset);
        else if (bottom > Viewport.Offset + Viewport.ViewportHeight) Viewport.ScrollBy(bottom - Viewport.Offset - Viewport.ViewportHeight);
    }
    private void Synchronize()
    {
        Viewport.SetContentHeight(_keys.Length * RowHeight);
        var first = Viewport.Offset / RowHeight;
        var last = Math.Min(_keys.Length - 1, (Viewport.Offset + Viewport.ViewportHeight - 1) / RowHeight);
        var visible = _keys.Skip(Math.Max(0, first - Overscan)).Take(last < first ? 0 : last - first + 1 + Overscan + Math.Min(Overscan, first)).ToArray();
        var departing = _realized.Except(visible, StringComparer.Ordinal).ToArray();
        if (_focusedKey is not null && departing.Contains(_focusedKey, StringComparer.Ordinal)) _focusedKey = null;
        _region.For(visible, CreateRow);
        foreach (var key in departing) _elements.Remove(key);
        _realized.Clear(); foreach (var key in visible) _realized.Add(key);
        foreach (var key in visible)
        {
            var index = Array.IndexOf(_keys, key); var row = _elements[key];
            row.Bounds = new(0, index * RowHeight - Viewport.Offset, Viewport.Element.Bounds.Width, RowHeight);
            row.Mark(DirtyFacet.Layout);
        }
        _projection.Project(Flatten(_root));
        UpdateSelectionSemantics();
    }
    private StableElement CreateRow(string key, ReactiveScope scope)
    {
        var element = new StableElement(new("issues.row." + key), new(0, 0, 0, RowHeight), new("#27272a", "#fafafa", 0), new("option", key, Actions: ["select"]));
        _elements.Add(key, element);
        scope.Own(new ScopeMarker(() => _releasedScopes++));
        _ = scope.Effect(() => { _ = _lifetimeTick.Value; _effectRuns++; }, "issues.row." + key);
        _input.Set(element, new("select", true, true, Pointer: pointer => { if (pointer.Kind == PointerKind.Down) Select(key, focus: true); }));
        return element;
    }
    private void UpdateSelectionSemantics()
    {
        foreach (var (key, row) in _elements)
        {
            var value = key == SelectedKey ? "selected" : null;
            if (row.Semantics.Value == value) continue;
            row.Semantics = row.Semantics with { Value = value };
            row.Mark(DirtyFacet.Semantics);
        }
        _projection.Project(Flatten(_root));
    }
    private static string[] Validate(IEnumerable<string> keys)
    {
        var values = keys.ToArray();
        if (values.Any(string.IsNullOrWhiteSpace) || values.Distinct(StringComparer.Ordinal).Count() != values.Length) throw new ArgumentException("List keys must be nonempty and unique.", nameof(keys));
        return values;
    }
    private static IEnumerable<StableElement> Flatten(StableElement root) => [root, .. root.Children.SelectMany(Flatten)];
    private sealed class ScopeMarker(Action released) : IDisposable { public void Dispose() => released(); }
}

internal sealed record VirtualizationCheckResult(bool Ok, bool TenThousandRows, bool WheelAndKeyboardScroll, bool StableRealizedIdentity, bool BoundedRealizedCount, bool SelectionAndFocusVisible, bool KeyedSelectionPreserved, bool DeterministicReorderRemoval, bool ScopeInputFocusCaptureSceneSemanticsReleased, int RealizedCount, int RealizedLimit, int ReleasedScopes, int ManagedGrowthBytes, int ManagedBeforeBytes, int ManagedAfterBytes);

internal static class VirtualizationProbe
{
    public static VirtualizationCheckResult Run()
    {
        var graph = new ReactiveGraph(); var scene = new RetainedScene(); var snapshots = new ProjectedSnapshots(); var projection = new SceneProjection(scene, snapshots);
        var root = new StableElement(new("issues.root"), new(0, 0, 100, 100), new("#09090b", "#fafafa", 0), new("window", "Issues")); var input = new InputRouter(); var focus = new FocusScopes(input);
        var keys = Enumerable.Range(0, 10_000).Select(index => $"issue-{index:D5}").ToArray();
        var list = new VirtualizedList(graph, root, input, focus, projection, keys, 100, 100);
        graph.Drain();
        var tenThousandRows = keys.Length == 10_000;
        var stable = list.Row("issue-00005")!; list.Wheel(-20); var stableIdentity = list.Row("issue-00005") == stable; var wheel = list.Viewport.Offset == 20 && stableIdentity;
        list.Key("PageDown"); var keyboard = list.Viewport.Offset == 120;
        list.Select("issue-05000", focus: true);
        var selectedVisible = list.SelectedKey == "issue-05000" && focus.Focused == list.Row("issue-05000")!.Id && list.Row("issue-05000")!.Bounds.Y >= 0 && list.Row("issue-05000")!.Bounds.Y < 100;
        list.Key("ArrowDown"); selectedVisible &= list.SelectedKey == "issue-05001" && focus.Focused == list.Row("issue-05001")!.Id;
        var selected = list.Row("issue-05001")!;
        list.SetItems(keys.OrderByDescending(key => key, StringComparer.Ordinal));
        var reordered = list.SelectedKey == "issue-05001" && list.Row("issue-05001") == selected;
        var withoutSelected = keys.Where(key => key != "issue-05001").ToArray(); list.SetItems(withoutSelected);
        var deterministic = list.SelectedKey == "issue-04998" && focus.Focused == list.Row("issue-04998")!.Id;
        var captured = list.Row("issue-05000")!; input.Dispatch(root, PointerKind.Down, 1, captured.Bounds.Y + 1); var captureEstablished = input.Capture == captured.Id;
        var sceneHadCaptured = scene.Commands.Any(command => command.Id == captured.Id) && snapshots.Semantics.ContainsKey(captured.Id);
        var effectsBeforeRelease = list.EffectRuns; list.Select("issue-09000"); list.TickRows();
        var released = captureEstablished && input.Capture is null && input.Get(captured) is null && focus.Focused != captured.Id && list.Row("issue-05000") is null && !scene.Commands.Any(command => command.Id == captured.Id) && !snapshots.Semantics.ContainsKey(captured.Id) && list.ReleasedScopes > 0 && sceneHadCaptured && list.EffectRuns - effectsBeforeRelease <= list.RealizedCount;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); var before = GC.GetTotalMemory(true);
        for (var cycle = 0; cycle < 20; cycle++) { list.Select(keys[(cycle * 499) % keys.Length]); }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); var after = GC.GetTotalMemory(true);
        var realizedCount = list.RealizedCount; var realizedLimit = list.RealizedLimit; var bounded = realizedCount <= realizedLimit && realizedLimit == 14;
        list.Dispose();
        static bool IsListOwned(ElementId id) => id.Value is "issues.viewport" or "issues.content" || id.Value.StartsWith("issues.row.", StringComparison.Ordinal);
        released &= !root.Children.Any(element => element.Id.Value == "issues.viewport") && input.Get(list.Viewport.Element) is null && !scene.Commands.Any(command => IsListOwned(command.Id)) && !snapshots.Semantics.Keys.Any(IsListOwned);
        var result = new VirtualizationCheckResult(tenThousandRows && wheel && keyboard && stableIdentity && bounded && selectedVisible && reordered && deterministic && released, tenThousandRows, wheel && keyboard, stableIdentity, bounded, selectedVisible, reordered, deterministic, released, realizedCount, realizedLimit, list.ReleasedScopes, (int)Math.Clamp(after - before, int.MinValue, int.MaxValue), (int)Math.Min(before, int.MaxValue), (int)Math.Min(after, int.MaxValue));
        if (!result.Ok) throw new InvalidOperationException($"Virtualization self-check failed: {result}.");
        return result;
    }
}
