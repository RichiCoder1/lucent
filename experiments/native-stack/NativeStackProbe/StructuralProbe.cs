using System.Text.Json;

internal sealed record Interaction(string Action, bool Focusable = false, bool CaptureOnDown = false, bool FocusScope = false, Action<RoutedPointer>? Pointer = null);
internal sealed class RoutedPointer(PointerKind kind, int x, int y)
{
    public PointerKind Kind { get; } = kind;
    public int X { get; } = x;
    public int Y { get; } = y;
    public bool Handled { get; set; }
}
internal enum PointerKind { Down, Move, Up }

/// <summary>Bounded input routing: deepest hit target bubbles to its root; there is deliberately no tunnel phase.</summary>
internal sealed class InputRouter
{
    private readonly Dictionary<ElementId, Interaction> _interactions = [];
    private ElementId? _capture;
    public ElementId? Capture => _capture;
    public void Set(StableElement element, Interaction interaction) => _interactions.Add(element.Id, interaction);
    public Interaction? Get(StableElement element) => _interactions.GetValueOrDefault(element.Id);
    public StableElement? Dispatch(StableElement root, PointerKind kind, int x, int y)
    {
        var target = _capture is { } captured ? Find(root, captured) : Hit(root, x, y);
        if (target is null) return null;
        var pointer = new RoutedPointer(kind, x, y);
        foreach (var element in Path(root, target).Reverse())
        {
            var interaction = Get(element);
            interaction?.Pointer?.Invoke(pointer);
            if (pointer.Handled) break;
        }
        if (kind == PointerKind.Down && Get(target)?.CaptureOnDown == true) _capture = target.Id;
        if (kind == PointerKind.Up) _capture = null;
        return target;
    }
    public void Release(IEnumerable<StableElement> elements)
    {
        foreach (var element in elements) { _interactions.Remove(element.Id); if (_capture == element.Id) _capture = null; }
    }
    private static StableElement? Hit(StableElement element, int x, int y)
    {
        if (!Contains(element.Bounds, x, y)) return null;
        for (var index = element.Children.Count - 1; index >= 0; index--) if (Hit(element.Children[index], x, y) is { } child) return child;
        return element;
    }
    private static bool Contains(Bounds bounds, int x, int y) => x >= bounds.X && y >= bounds.Y && x < bounds.X + bounds.Width && y < bounds.Y + bounds.Height;
    private static StableElement? Find(StableElement element, ElementId id) => element.Id == id ? element : element.Children.Select(child => Find(child, id)).FirstOrDefault(child => child is not null);
    private static IEnumerable<StableElement> Path(StableElement root, StableElement target)
    {
        if (root == target) return [root];
        foreach (var child in root.Children)
            if (Path(child, target).ToArray() is { Length: > 0 } tail) return [root, .. tail];
        return [];
    }
}

internal sealed class FocusScopes(InputRouter input)
{
    private ElementId? _focused;
    public ElementId? Focused => _focused;
    public void Focus(StableElement root, StableElement element)
    {
        if (input.Get(element)?.Focusable != true || !Contains(root, element.Id)) throw new InvalidOperationException("Only attached focusable elements may receive focus.");
        if (_focused is { } previous && Find(root, previous) is { } old) { old.Semantics = old.Semantics with { Focused = false }; old.Mark(DirtyFacet.Semantics); }
        _focused = element.Id;
        element.Semantics = element.Semantics with { Focused = true }; element.Mark(DirtyFacet.Semantics);
    }
    public StableElement? Next(StableElement root, bool reverse = false)
    {
        var current = _focused is { } id ? Find(root, id) : null;
        var scope = current is null ? root : Path(root, current).LastOrDefault(element => input.Get(element)?.FocusScope == true) ?? root;
        var items = Flatten(scope).Where(element => input.Get(element)?.Focusable == true).ToArray();
        if (items.Length == 0) return null;
        var index = current is null ? -1 : Array.FindIndex(items, element => element.Id == current.Id);
        index = (index + (reverse ? items.Length - 1 : 1)) % items.Length;
        if (current is not null) { current.Semantics = current.Semantics with { Focused = false }; current.Mark(DirtyFacet.Semantics); }
        _focused = items[index].Id;
        items[index].Semantics = items[index].Semantics with { Focused = true }; items[index].Mark(DirtyFacet.Semantics);
        return items[index];
    }
    public void Release(IEnumerable<StableElement> elements) { var ids = elements.Select(element => element.Id).ToHashSet(); if (_focused is { } id && ids.Contains(id)) _focused = null; }
    private static bool Contains(StableElement root, ElementId id) => Find(root, id) is not null;
    private static StableElement? Find(StableElement root, ElementId id) => root.Id == id ? root : root.Children.Select(child => Find(child, id)).FirstOrDefault(child => child is not null);
    private static IEnumerable<StableElement> Path(StableElement root, StableElement target) => root == target ? [root] : root.Children.Select(child => Path(child, target)).FirstOrDefault(path => path.Any())?.Prepend(root) ?? [];
    private static IEnumerable<StableElement> Flatten(StableElement root) => [root, .. root.Children.SelectMany(Flatten)];
}

internal static class SemanticContracts
{
    public static void Validate(IEnumerable<StableElement> elements, InputRouter input, FocusScopes focus)
    {
        foreach (var element in elements)
        {
            var semantic = element.Semantics;
            if (semantic.SuppressionReason is { Length: 0 } || semantic.SuppressionReason?.Trim().Length == 0) throw new InvalidOperationException($"Semantic suppression for {element.Id.Value} requires a reason.");
            var interaction = input.Get(element);
            if (interaction is null || semantic.SuppressionReason is not null) continue;
            if (string.IsNullOrWhiteSpace(semantic.Role) || string.IsNullOrWhiteSpace(semantic.Name) || semantic.Actions is null || !semantic.Actions.Contains(interaction.Action, StringComparer.Ordinal))
                throw new InvalidOperationException($"Interactive element {element.Id.Value} lacks required semantics.");
            if (interaction.Focusable && semantic.Focused != (focus.Focused == element.Id)) throw new InvalidOperationException($"Focused state is not projected for {element.Id.Value}.");
        }
    }
    public static string Dump(IEnumerable<StableElement> elements) => JsonSerializer.Serialize(elements.OrderBy(element => element.Id.Value, StringComparer.Ordinal).Select(element => new SemanticDumpItem(element.Id.Value, element.Semantics.Role, element.Semantics.Name, element.Semantics.Value, element.Semantics.Actions ?? [], element.Semantics.Enabled, element.Semantics.Focused, element.Semantics.SuppressionReason, element.Semantics.Selected)).ToArray(), ProbeJsonContext.Default.SemanticDumpItemArray);
    public static string Dump(IReadOnlyDictionary<ElementId, Semantics> semantics) => JsonSerializer.Serialize(semantics.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal).Select(pair => new SemanticDumpItem(pair.Key.Value, pair.Value.Role, pair.Value.Name, pair.Value.Value, pair.Value.Actions ?? [], pair.Value.Enabled, pair.Value.Focused, pair.Value.SuppressionReason, pair.Value.Selected)).ToArray(), ProbeJsonContext.Default.SemanticDumpItemArray);
}

internal sealed record SemanticDumpItem(string Id, string Role, string Name, string? Value, string[] Actions, bool Enabled, bool Focused, string? SuppressionReason, bool Selected);

/// <summary>Owns only its created branches. Show and keyed For retain stable elements; they never reconcile arbitrary trees.</summary>
internal sealed class StructuralRegion(StableElement parent, ReactiveGraph graph, SceneProjection projection, InputRouter input, FocusScopes focus) : IDisposable
{
    private readonly List<OwnedBranch> _branches = [];
    private readonly Dictionary<string, OwnedBranch> _keyed = new(StringComparer.Ordinal);
    private OwnedBranch? _shown;
    public void Show(bool visible, Func<ReactiveScope, StableElement> create)
    {
        if (visible && _shown is null) _shown = Create(create);
        if (!visible && _shown is not null) { Remove([_shown]); _shown = null; }
        Attach();
    }
    public void For(IEnumerable<string> keys, Func<string, ReactiveScope, StableElement> create)
    {
        var ordered = keys.ToArray();
        if (ordered.Distinct(StringComparer.Ordinal).Count() != ordered.Length) throw new ArgumentException("Region keys must be unique.");
        Remove(_keyed.Where(pair => !ordered.Contains(pair.Key, StringComparer.Ordinal)).Select(pair => pair.Value).ToArray());
        foreach (var key in ordered) if (!_keyed.ContainsKey(key)) _keyed.Add(key, Create(scope => create(key, scope)));
        _branches.Clear(); _branches.AddRange(ordered.Select(key => _keyed[key]));
        Attach();
    }
    public void Dispose() => Remove(_branches.Concat(_keyed.Values).Append(_shown).Where(branch => branch is not null).Cast<OwnedBranch>().Distinct().ToArray());
    private OwnedBranch Create(Func<ReactiveScope, StableElement> create) { var scope = graph.Scope(); return new(scope, create(scope)); }
    private void Attach() { parent.Children.Clear(); if (_shown is not null) parent.Children.Add(_shown.Element); parent.Children.AddRange(_branches.Select(branch => branch.Element)); projection.Project(Flatten(parent)); }
    private void Remove(IEnumerable<OwnedBranch> branches)
    {
        foreach (var branch in branches.Distinct())
        {
            var elements = Flatten(branch.Element).ToArray();
            input.Release(elements); focus.Release(elements); projection.Release(elements); branch.Scope.Dispose();
            _branches.Remove(branch); if (_shown == branch) _shown = null; foreach (var key in _keyed.Where(pair => pair.Value == branch).Select(pair => pair.Key).ToArray()) _keyed.Remove(key);
        }
        parent.Children.Clear(); if (_shown is not null) parent.Children.Add(_shown.Element); parent.Children.AddRange(_branches.Select(branch => branch.Element));
    }
    private static IEnumerable<StableElement> Flatten(StableElement root) => [root, .. root.Children.SelectMany(Flatten)];
    private sealed record OwnedBranch(ReactiveScope Scope, StableElement Element);
}

internal sealed record StructuralCheckResult(bool Ok, bool ShowStable, bool KeyedStable, bool DuplicateIdsRejected, bool HitTesting, bool Bubbling, bool PointerCapture, bool FocusScopes, bool KeyboardTraversal, bool SemanticProjection, bool AccessibilityFailure, bool SuppressionDumped, bool ChurnCleanup, bool EffectUnregistered, bool AsyncCancelled, bool SceneReleased, bool SemanticReleased, string SemanticDump);

internal static class StructuralProbe
{
    public static StructuralCheckResult Run()
    {
        var graph = new ReactiveGraph(); var scene = new RetainedScene(); var snapshots = new ProjectedSnapshots(); var projection = new SceneProjection(scene, snapshots);
        var root = Element("root", new(0, 0, 100, 100), new("window", "Root")); var input = new InputRouter(); var focus = new FocusScopes(input);
        using var region = new StructuralRegion(root, graph, projection, input, focus);
        var created = 0; StableElement? shown = null;
        region.Show(true, scope => shown = Element("show", new(5, 5, 20, 20), new("button", "Show", "shown", ["press"])));
        var showStable = shown is not null; region.Show(true, _ => throw new InvalidOperationException("Show rebuilt a stable branch.")); showStable &= root.Children.Single() == shown;
        var bubble = new List<string>(); var child = shown!;
        var scope = Element("scope", new(5, 15, 15, 8), new("group", "Scope", Actions: ["group"])); var first = Element("first", new(5, 15, 7, 8), new("button", "First", Actions: ["press"])); var second = Element("second", new(13, 15, 7, 8), new("button", "Second", Actions: ["press"])); scope.Children.AddRange([first, second]); child.Children.Add(scope);
        input.Set(root, new("root", Pointer: _ => bubble.Add("root"))); input.Set(child, new("press", true, true, true, _ => bubble.Add("child"))); input.Set(scope, new("group", FocusScope: true)); input.Set(first, new("press", true)); input.Set(second, new("press", true));
        var hit = input.Dispatch(root, PointerKind.Down, 10, 10); input.Dispatch(root, PointerKind.Move, 90, 90); var captured = input.Capture == child.Id && bubble.SequenceEqual(["child", "root", "child", "root"]);
        input.Dispatch(root, PointerKind.Up, 90, 90); var hitTesting = hit == child && input.Dispatch(root, PointerKind.Down, 99, 99) == root; var bubbling = bubble.Take(2).SequenceEqual(["child", "root"]);
        root.Semantics = root.Semantics with { Actions = ["root"] }; root.Mark(DirtyFacet.Semantics);
        focus.Focus(root, first); var focusScoped = focus.Next(root) == second && focus.Next(root) == first;
        SemanticContracts.Validate(Flatten(root), input, focus);
        projection.Project(Flatten(root));
        var semanticDump = SemanticContracts.Dump(Flatten(root)); var semanticProjection = snapshots.Semantics[child.Id] is { Role: "button", Name: "Show", Value: "shown", Enabled: true } shownSemantic && shownSemantic.Actions!.SequenceEqual(["press"]) && snapshots.Semantics[first.Id].Focused && semanticDump.Contains("\"Enabled\":true", StringComparison.Ordinal);
        var inaccessible = false; var bad = Element("bad", new(0, 0, 1, 1), new("", "")); input.Set(bad, new("press"));
        try { SemanticContracts.Validate([bad], input, focus); } catch (InvalidOperationException) { inaccessible = true; }
        var duplicateIdsRejected = false;
        try { projection.Project([Element("duplicate", new(0, 0, 1, 1), new("group", "One")), Element("duplicate", new(1, 0, 1, 1), new("group", "Two"))]); }
        catch (InvalidOperationException) { duplicateIdsRejected = true; }
        var suppressed = Element("suppressed", new(0, 0, 1, 1), new("", "", SuppressionReason: "temporary native bridge"));
        root.Children.Add(suppressed); input.Set(suppressed, new("press")); SemanticContracts.Validate(Flatten(root), input, focus); projection.Project(Flatten(root));
        semanticDump = SemanticContracts.Dump(snapshots.Semantics); var suppressionDumped = semanticDump.Contains("temporary native bridge", StringComparison.Ordinal);
        root.Children.Remove(suppressed); input.Release([suppressed]); projection.Release([suppressed]);
        region.Show(false, _ => throw new InvalidOperationException());

        StableElement? keyedB = null;
        region.For(["a", "b"], (key, scope) => { created++; var element = Element("key." + key, new(30, 5, 10, 10), new("button", key, Actions: ["press"])); if (key == "b") keyedB = element; return element; });
        region.For(["b", "c"], (key, scope) => { created++; return Element("key." + key, new(30, 5, 10, 10), new("button", key, Actions: ["press"])); });
        var keyedStable = created == 3 && root.Children[0] == keyedB;

        var source = graph.Signal(0, "churn.source"); var effectRuns = 0; var completion = new TaskCompletionSource<int>(); var cancelled = false; var sceneBeforeChurn = scene.Count; var semanticsBeforeChurn = snapshots.Semantics.Count;
        region.Show(true, scope => { _ = scope.Effect(() => { _ = source.Value; effectRuns++; }, "churn.effect"); _ = scope.AsyncComputed(token => { token.Register(() => cancelled = true); return completion.Task; }, "churn.async").Value; return Element("churn", new(50, 5, 10, 10), new("button", "Churn", Actions: ["press"])); });
        graph.Drain(); var churn = root.Children.Single(element => element.Id.Value == "churn"); input.Set(churn, new("press", true, true)); focus.Focus(root, churn); input.Dispatch(root, PointerKind.Down, 51, 6);
        region.Show(false, _ => throw new InvalidOperationException()); source.Value++; completion.SetResult(1); graph.Drain();
        var sceneReleased = scene.Count == sceneBeforeChurn && scene.Commands.All(command => command.Id != churn.Id); var semanticReleased = snapshots.Semantics.Count == semanticsBeforeChurn && !snapshots.Semantics.ContainsKey(churn.Id);
        var cleanup = root.Children.Count == 2 && input.Capture is null && focus.Focused is null && effectRuns == 1 && cancelled && sceneReleased && semanticReleased;
        var result = new StructuralCheckResult(showStable && keyedStable && duplicateIdsRejected && hitTesting && bubbling && captured && focusScoped && semanticProjection && inaccessible && suppressionDumped && cleanup, showStable, keyedStable, duplicateIdsRejected, hitTesting, bubbling, captured, focusScoped, focusScoped, semanticProjection, inaccessible, suppressionDumped, cleanup, effectRuns == 1, cancelled, sceneReleased, semanticReleased, semanticDump);
        if (!result.Ok) throw new InvalidOperationException($"Structural self-check failed: show={showStable}; keyed={keyedStable}; ids={duplicateIdsRejected}; hit={hitTesting}; bubble={bubbling}; capture={captured}; focus={focusScoped}; semantic={semanticProjection}; inaccessible={inaccessible}; suppression={suppressionDumped}; cleanup={cleanup}.");
        return result;
    }
    private static StableElement Element(string id, Bounds bounds, Semantics semantics) => new(new(id), bounds, new("#000000", "#ffffff", 0), semantics);
    private static IEnumerable<StableElement> Flatten(StableElement root) => [root, .. root.Children.SelectMany(Flatten)];
}
