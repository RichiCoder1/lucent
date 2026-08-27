using System.Text.Json;
using System.Runtime.InteropServices;
using SkiaSharp;
using SDL3;

/// <summary>Issue #12's small deterministic application layer over the proven reactive, control, and virtual-list seams.</summary>
internal static class IssueBrowser
{
    private const int Width = 1200, Height = 760, VisibleRows = 28;
    private sealed record Issue(string Id, string Title, string Status, string Priority, string Assignee);
    internal enum InputCommand { Search, OpenFilter, HighFilter, AssigneeFilter, PageDown, End, Home, SelectFirst, Next, RemoveSelected, EditTitle, CommitTitle, RevertTitle, FailSearch, Retry, ToggleTheme, ReducedMotion, Tab, ShiftTab, EscapeToList }

    public static string RunWalkthrough(string? evidenceDirectory)
    {
        var evidence = Path.GetFullPath(evidenceDirectory ?? Path.Combine("..", "evidence", "issue-12", "native"));
        Directory.CreateDirectory(evidence);
        using var app = new State(LoadSeed());
        var steps = app.Walkthrough();
        var transcript = Path.Combine(evidence, "walkthrough.jsonl");
        File.WriteAllLines(transcript, steps.Select(step => JsonSerializer.Serialize(step, ProbeJsonContext.Default.IssueBrowserStep)));
        File.WriteAllText(Path.Combine(evidence, "assignee-filter.json"), JsonSerializer.Serialize(app.VerifyAssigneeFilter(), ProbeJsonContext.Default.IssueBrowserAssigneeFilterCheck));
        File.WriteAllText(Path.Combine(evidence, "semantics.json"), JsonSerializer.Serialize(app.Semantics(), ProbeJsonContext.Default.IssueBrowserSemanticArray));
        File.WriteAllText(Path.Combine(evidence, "identity.json"), JsonSerializer.Serialize(app.Identity(), ProbeJsonContext.Default.IssueBrowserIdentity));
        Draw(Path.Combine(evidence, "light.png"), false, app);
        Draw(Path.Combine(evidence, "dark.png"), true, app);
        if (steps.Any(step => !step.Pass)) throw new InvalidOperationException("Issue browser walkthrough failed.");
        return string.Join(Environment.NewLine, steps.Select(step => JsonSerializer.Serialize(step, ProbeJsonContext.Default.IssueBrowserStep)));
    }

    public static string RunVisible()
    {
        using var state = new State(LoadSeed());
        using var host = new WindowHost("Lucent Native Issue Browser", Width, Height, SDL.WindowFlags.Resizable);
        using var presenter = new SdlSkiaPresenter(host.Window, new BrowserRenderer(state));
        if (!SDL.ShowWindow(host.Window)) throw new InvalidOperationException(SDL.GetError());
        var scene = state.Scene;
        scene.Upsert(new("issue-browser.background"), new(0, 0, Width, Height), new("#fafafa", "#09090b", 0));
        scene.Upsert(new("issue-browser.rail"), new(0, 0, 216, Height), new("#f4f4f5", "#09090b", 0));
        scene.Upsert(new("issue-browser.list"), new(216, 0, 420, Height), new("#ffffff", "#09090b", 0));
        scene.Upsert(new("issue-browser.detail"), new(636, 0, Width - 636, Height), new("#fafafa", "#09090b", 0));
        presenter.Present(scene); SDL.StartTextInput(host.Window);
        while (SDL.WaitEventTimeout(out var e, 20))
        {
            var type = (SDL.EventType)e.Type; if (type is SDL.EventType.Quit or SDL.EventType.WindowCloseRequested) break;
            var changed = true;
            switch (type)
            {
                case SDL.EventType.TextInput: state.Dispatch(InputCommand.Search, state.Query + (Marshal.PtrToStringUTF8(e.Text.Text) ?? "")); break;
                case SDL.EventType.KeyDown:
                    state.Dispatch(e.Key.Key switch { SDL.Keycode.Pagedown => InputCommand.PageDown, SDL.Keycode.End => InputCommand.End, SDL.Keycode.Home => InputCommand.Home, SDL.Keycode.Down => InputCommand.Next, SDL.Keycode.Escape => InputCommand.EscapeToList, SDL.Keycode.Tab => (e.Key.Mod & SDL.Keymod.Shift) != 0 ? InputCommand.ShiftTab : InputCommand.Tab, SDL.Keycode.T => InputCommand.ToggleTheme, _ => InputCommand.Search }, e.Key.Key == SDL.Keycode.Backspace && state.Query.Length > 0 ? state.Query[..^1] : state.Query); break;
                case SDL.EventType.MouseButtonDown:
                    if (e.Button.X < 72 && e.Button.Y is >= 180 and <= 224) state.Dispatch(InputCommand.AssigneeFilter);
                    else if (e.Button.X < 148 && e.Button.Y is >= 180 and <= 224) state.Dispatch(InputCommand.OpenFilter);
                    else if (e.Button.X < 220 && e.Button.Y is >= 180 and <= 224) state.Dispatch(InputCommand.HighFilter);
                    else if (e.Button.X is >= 224 and < 632) state.Dispatch(InputCommand.SelectFirst);
                    else if (e.Button.X > 760 && e.Button.Y is >= 380 and <= 440) state.Dispatch(InputCommand.Retry);
                    else changed = false;
                    break;
                case SDL.EventType.MouseWheel: state.Dispatch(e.Wheel.Y < 0 ? InputCommand.PageDown : InputCommand.Home); break;
                default: changed = false; break;
            }
            if (changed) presenter.Present(scene);
        }
        SDL.StopTextInput(host.Window);
        return $"{{\"mode\":\"sdl-skia\",\"rows\":{state.Rows.Count},\"realized\":{state.Realized}}}";
    }

    private static IssueBrowserSeed LoadSeed()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "experiments", "native-stack", "gauntlet", "issues.seed.json"))) root = root.Parent;
        var path = root is null ? Path.GetFullPath(Path.Combine("gauntlet", "issues.seed.json")) : Path.Combine(root.FullName, "experiments", "native-stack", "gauntlet", "issues.seed.json");
        return JsonSerializer.Deserialize(File.ReadAllText(path), ProbeJsonContext.Default.IssueBrowserSeed) ?? throw new InvalidOperationException("Invalid issue seed.");
    }
    private static void Draw(string path, bool dark, State state)
    {
        using var bitmap = new SKBitmap(Width, Height); using var canvas = new SKCanvas(bitmap); new BrowserRenderer(state, dark).Render(new RetainedScene(), canvas);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes(path, data.ToArray());
    }

    private sealed class BrowserRenderer(State state, bool? forcedDark = null) : ISkiaSceneRenderer
    {
        public void Render(RetainedScene scene, SKCanvas canvas)
        {
            var dark = forcedDark ?? state.Theme == "dark"; var background = dark ? "#09090b" : "#f8fafc"; var panel = dark ? "#111113" : "#ffffff"; var raised = dark ? "#18181b" : "#f8fafc";
            var border = dark ? "#27272a" : "#e2e8f0"; var foreground = dark ? "#fafafa" : "#0f172a"; var subtle = dark ? "#a1a1aa" : "#64748b"; var accent = dark ? "#fafafa" : "#18181b";
            canvas.Clear(SKColor.Parse(background));
            using var paint = new SKPaint { IsAntialias = true }; using var title = new SKFont(SKTypeface.Default, 20); using var heading = new SKFont(SKTypeface.Default, 15); using var body = new SKFont(SKTypeface.Default, 13); using var small = new SKFont(SKTypeface.Default, 11);
            void Rect(float x, float y, float width, float height, string color, float radius = 0) { paint.Color = SKColor.Parse(color); canvas.DrawRoundRect(new SKRect(x, y, x + width, y + height), radius, radius, paint); }
            void Text(string text, float x, float y, SKFont font, string color) { paint.Color = SKColor.Parse(color); canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint); }
            Rect(0, 0, 208, Height, panel); Rect(207, 0, 1, Height, border); Rect(224, 16, 408, Height - 32, panel, 10); Rect(648, 16, 536, Height - 32, panel, 10);
            Text("LUCENT", 20, 28, small, subtle); Text("Issue tracker", 20, 54, title, foreground); Text("Workspace", 20, 86, small, subtle);
            Rect(16, 104, 176, 38, raised, 7); Text("⌕  Search issues", 28, 128, body, subtle);
            Text("FILTERS", 20, 174, small, subtle); Rect(16, 188, 52, 30, state.Assignee == "Ada" ? accent : raised, 15); Text("Ada", 29, 208, body, state.Assignee == "Ada" ? (dark ? "#18181b" : "#fafafa") : foreground); Rect(76, 188, 68, 30, accent, 15); Text("Open", 92, 208, body, dark ? "#18181b" : "#fafafa"); Rect(152, 188, 56, 30, raised, 15); Text("High", 162, 208, body, foreground);
            Text("Views", 20, 258, small, subtle); Text("All issues", 28, 286, body, foreground); Text("Assigned to me", 28, 316, body, subtle); Text("Recently updated", 28, 346, body, subtle);
            Text("Open issues", 248, 48, heading, foreground); Text($"{state.Rows.Count} matching", 248, 68, small, subtle); Rect(544, 34, 64, 24, raised, 12); Text("Newest", 557, 50, small, subtle);
            var rows = state.Rows.Take(16).ToArray();
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index]; var y = 82 + index * 39; var selected = row.Id == state.Selected;
                if (selected) Rect(236, y - 17, 384, 36, dark ? "#27272a" : "#f1f5f9", 6);
                Rect(248, y - 2, 7, 7, row.Status == "Open" ? "#22c55e" : subtle, 4); Text(row.Title, 266, y + 3, body, foreground); Text(row.Id.Replace("issue-", "#"), 560, y + 3, small, subtle);
            }
            var selectedIssue = state.SelectedIssue;
            Text("ISSUE DETAILS", 676, 48, small, subtle); Text(selectedIssue?.Title ?? "Select an issue", 676, 82, title, foreground); Text(state.Selected.Replace("issue-", "#"), 676, 106, body, subtle);
            Rect(676, 128, 68, 26, dark ? "#052e16" : "#dcfce7", 13); Text(selectedIssue?.Status ?? "Open", 694, 146, small, dark ? "#86efac" : "#166534"); Rect(752, 128, 68, 26, raised, 13); Text(selectedIssue?.Priority ?? "High", 771, 146, small, foreground);
            Text("Title", 676, 194, small, subtle); Rect(676, 206, 476, 42, raised, 7); Text(state.Draft.Length == 0 ? selectedIssue?.Title ?? "Issue title" : state.Draft, 690, 232, body, foreground);
            Text("Description", 676, 286, small, subtle); Text("Investigate the reported behavior and document the", 676, 316, body, foreground); Text("smallest safe fix. Preserve current issue context.", 676, 338, body, foreground);
            Rect(676, 392, 92, 34, accent, 7); Text("Save changes", 690, 414, small, dark ? "#18181b" : "#fafafa"); Rect(780, 392, 70, 34, raised, 7); Text("Retry", 801, 414, small, foreground);
            Text("Activity", 676, 482, heading, foreground); Rect(676, 504, 476, 1, border); Rect(680, 526, 8, 8, "#3b82f6", 4); Text("Query completed and retained the latest result.", 704, 535, body, subtle);
        }
    }

    private sealed class State : IDisposable
    {
        private readonly IssueBrowserSeed _seed; private readonly Issue[] _all; private readonly Dictionary<string, Issue> _byId;
        private readonly ReactiveGraph _graph = new(); private readonly ReactiveSignal<string> _query; private readonly ReactiveSignal<string?> _status; private readonly ReactiveSignal<string?> _priority; private readonly ReactiveSignal<string?> _assignee; private readonly ReactiveSignal<ThemeLayer> _theme;
        private readonly ReactiveComputed<Issue[]> _filtered; private readonly ReactiveAsyncComputed<Issue[]> _async;
        private readonly InputRouter _input = new(); private readonly FocusScopes _focus; private readonly StableElement _root; private readonly ProjectedSnapshots _snapshots = new(); private readonly SceneProjection _projection; private readonly VirtualizedList _list; private readonly TextField _title; private readonly Button _retry;
        private readonly StableElement _searchSemantic, _statusSemantic, _prioritySemantic, _assigneeSemantic; private string _editOriginal = "", _focusName = "search"; private readonly List<string> _focusTrace = ["search"]; private bool _reducedMotion;
        public RetainedScene Scene { get; } = new(); public List<Issue> Rows => _filtered.Value.ToList(); public string Selected => _list.SelectedKey ?? ""; public Issue? SelectedIssue => _byId.GetValueOrDefault(Selected); public string Theme => ReferenceEquals(_theme.Value, Themes.Dark) ? "dark" : "light"; public int Realized => _list.RealizedCount; public string Query => _query.Value; public string Draft => _title.Text; public string? Assignee => _assignee.Value;
        public State(IssueBrowserSeed seed)
        {
            _seed = seed; _all = Enumerable.Range(0, seed.Count).Select(index => new Issue($"issue-{index:D5}", index % 5 == 0 ? $"Authentication failure {index}" : $"Issue {index}", index % 3 == 0 ? "Closed" : "Open", index % 4 == 0 ? "High" : index % 4 == 1 ? "Medium" : "Low", index % 2 == 0 ? "Ada" : "Lin")).ToArray(); _byId = _all.ToDictionary(x => x.Id);
            _query = _graph.Signal("", "issues.query"); _status = _graph.Signal<string?>(null, "issues.status"); _priority = _graph.Signal<string?>(null, "issues.priority"); _assignee = _graph.Signal<string?>(null, "issues.assignee"); _theme = _graph.Signal(Themes.Light, "issues.theme");
            _filtered = _graph.Computed(() => _all.Where(issue => issue.Title.Contains(_query.Value, StringComparison.OrdinalIgnoreCase) && (_status.Value is null || issue.Status == _status.Value) && (_priority.Value is null || issue.Priority == _priority.Value) && (_assignee.Value is null || issue.Assignee == _assignee.Value)).ToArray(), "issues.filtered");
            _async = _graph.AsyncComputed(async cancellation => { var query = _query.Value; var status = _status.Value; var priority = _priority.Value; var assignee = _assignee.Value; await Task.Delay(seed.AsyncDelayMilliseconds, cancellation); if (query == seed.FailureQuery) throw new InvalidOperationException("Search failed"); return _all.Where(issue => issue.Title.Contains(query, StringComparison.OrdinalIgnoreCase) && (status is null || issue.Status == status) && (priority is null || issue.Priority == priority) && (assignee is null || issue.Assignee == assignee)).ToArray(); }, "issues.search");
            _root = new(new("issue-browser.root"), new(0, 0, Width, Height), new("#fafafa", "#09090b", 0), new("window", "Issue browser")); _focus = new(_input); _projection = new(Scene, _snapshots);
            _searchSemantic = new(new("issue-browser.search"), new(16, 104, 176, 38), new("#f4f4f5", "#09090b", 7), new("edit", "Search issues", "", ["set-value"]));
            _statusSemantic = new(new("issue-browser.status"), new(76, 188, 68, 30), new("#18181b", "#fafafa", 15), new("checkbox", "Open", null, ["toggle"]));
            _prioritySemantic = new(new("issue-browser.priority"), new(152, 188, 56, 30), new("#f4f4f5", "#09090b", 15), new("checkbox", "High", null, ["toggle"])); _assigneeSemantic = new(new("issue-browser.assignee"), new(16, 188, 52, 30), new("#f4f4f5", "#09090b", 15), new("checkbox", "Ada", null, ["toggle"])); _root.Children.AddRange([_searchSemantic, _statusSemantic, _prioritySemantic, _assigneeSemantic]);
            _list = new(_graph, _root, _input, _focus, _projection, _all.Select(x => x.Id), 404, 280, 10);
            _title = new(_root, _input, _focus, "issue-browser.title", "Issue title", new MemoryClipboard()); _retry = new(_root, _input, _focus, "issue-browser.retry", "Retry", Retry); _graph.Drain();
        }
        public List<IssueBrowserStep> Walkthrough()
        {
            Sync(); var output = new List<IssueBrowserStep> { Step("W1 cold-start", Rows.Count == _seed.Count && Realized <= _list.RealizedLimit, "10000 rows; realized <= three visible windows", $"{Rows.Count} rows; realized {Realized}") };
            var stale = _list.RealizedCount; Dispatch(InputCommand.Search, "auth"); var pending = _async.Pending && _list.RealizedCount == stale; CompleteAsync(); output.Add(Step("W2 search", pending && Rows.Count == 2000, "stale rows then 2000 auth rows", $"{Rows.Count} rows"));
            foreach (var query in new[] { "a", "au", "aut", "auth", "authentication" }) Dispatch(InputCommand.Search, query); CompleteAsync(); output.Add(Step("W3 latest-query", Rows.Count == 2000 && !_async.Pending, "only final query commits", $"{Rows.Count} final rows"));
            Dispatch(InputCommand.Search, "auth"); Dispatch(InputCommand.OpenFilter); Dispatch(InputCommand.HighFilter); Sync(); output.Add(Step("W4 filters", Rows.Count == 333, "333 open high auth rows", $"{Rows.Count} rows"));
            Dispatch(InputCommand.PageDown); Dispatch(InputCommand.End); var end = _list.Viewport.Offset > 0; Dispatch(InputCommand.Home); output.Add(Step("W5 scroll", end && _list.Viewport.Offset == 0 && Realized <= _list.RealizedLimit, "PageDown End Home retain ceiling", $"realized {Realized}"));
            Dispatch(InputCommand.SelectFirst); Dispatch(InputCommand.Next); output.Add(Step("W6 selection", Selected == Rows[1].Id && _focus.Focused == _list.Row(Selected)?.Id, "pointer and keyboard select details", Selected));
            var prior = Array.IndexOf(Rows.Select(x => x.Id).ToArray(), Selected); var expectedAfterRemoval = Rows.Where(x => x.Id != Selected).ElementAt(prior).Id; Dispatch(InputCommand.RemoveSelected); output.Add(Step("W7 filtered-selection", Selected == expectedAfterRemoval, "removed selection clamps to prior index", Selected));
            var original = _byId[Selected].Title; Dispatch(InputCommand.EditTitle, "uncommitted"); Dispatch(InputCommand.RevertTitle); var reverted = _title.Text == original; Dispatch(InputCommand.EditTitle, "Scalar 😀 title"); Dispatch(InputCommand.CommitTitle); var committed = _byId[Selected].Title == "Scalar 😀 title"; Dispatch(InputCommand.EditTitle, original); Dispatch(InputCommand.CommitTitle); output.Add(Step("W8 title-edit", reverted && committed && _byId[Selected].Title == original, "scalar edit, Escape revert, then commit", _byId[Selected].Title));
            Dispatch(InputCommand.FailSearch); var staleOnFailure = _list.RealizedCount > 0; CompleteAsync(); var failed = _async.Error is not null; Dispatch(InputCommand.Retry); CompleteAsync(); output.Add(Step("W9 failure-retry", staleOnFailure && failed && _async.Error is null, "error retains stale rows then retry clears", $"stale={staleOnFailure}; failed={failed}; cleared={_async.Error is null}"));
            Dispatch(InputCommand.EditTitle, "draft"); var selected = Selected; var queryState = Query; var status = _status.Value; var priority = _priority.Value; var scroll = _list.Viewport.Offset; Dispatch(InputCommand.ReducedMotion); Dispatch(InputCommand.ToggleTheme); output.Add(Step("W10 theme", Theme == "dark" && Selected == selected && Query == queryState && _status.Value == status && _priority.Value == priority && _list.Viewport.Offset == scroll && Draft == "draft" && _reducedMotion, "theme preserves query filters selection scroll draft and reduced motion", "dark state preserved"));
            Dispatch(InputCommand.Tab); Dispatch(InputCommand.Tab); Dispatch(InputCommand.Tab); Dispatch(InputCommand.Tab); Dispatch(InputCommand.ShiftTab); Dispatch(InputCommand.EscapeToList); output.Add(Step("W11 keyboard", _focusName == "list" && _focusTrace.SequenceEqual(["search", "filters", "list", "details", "title", "details", "list"]), "Tab/ShiftTab traversal and Escape list focus", string.Join('>', _focusTrace)));
            var semantics = Semantics(); output.Add(Step("W12 semantics", semantics.All(x => x.Role.Length > 0 && x.Name.Length > 0) && semantics.Single(x => x.Id == "title").Actions.Contains("set-value") && semantics.Single(x => x.Id == "issues").Value == Selected, "projected roles names selection set-value and zero suppressions", "projected semantic tree")); return output;
        }
        public IssueBrowserAssigneeFilterCheck VerifyAssigneeFilter()
        {
            Dispatch(InputCommand.OpenFilter); Dispatch(InputCommand.HighFilter); Dispatch(InputCommand.AssigneeFilter); Sync();
            return new(Assignee == "Ada" && Rows.Count == 1000 && Rows.All(row => row.Assignee == "Ada"), 1000, Rows.Count, Assignee ?? "none");
        }
        private void SetQuery(string query) { _query.Value = query; _ = _async.Value; _graph.Drain(); }
        public void Dispatch(InputCommand command, string? text = null)
        {
            switch (command)
            {
                case InputCommand.Search: SetQuery(text ?? ""); break; case InputCommand.OpenFilter: _status.Value = _status.Value is null ? "Open" : null; break; case InputCommand.HighFilter: _priority.Value = _priority.Value is null ? "High" : null; break; case InputCommand.AssigneeFilter: _assignee.Value = _assignee.Value is null ? "Ada" : null; break;
                case InputCommand.PageDown: _list.Key("PageDown"); break; case InputCommand.End: _list.Key("End"); break; case InputCommand.Home: _list.Key("Home"); break; case InputCommand.SelectFirst: _list.Select(Rows[0].Id, true); break; case InputCommand.Next: _list.Key("ArrowDown"); break;
                case InputCommand.RemoveSelected: var keys = Rows.Where(x => x.Id != Selected).Select(x => x.Id); _list.SetItems(keys); break;
                case InputCommand.EditTitle: _title.Focus(true); if (_editOriginal.Length == 0) _editOriginal = _byId.GetValueOrDefault(Selected)?.Title ?? ""; _title.InvokeSemanticSetValue(text ?? ""); break;
                case InputCommand.CommitTitle: if (_byId.TryGetValue(Selected, out var issue)) { var updated = issue with { Title = _title.Text }; _byId[Selected] = updated; _all[Array.FindIndex(_all, item => item.Id == Selected)] = updated; } _editOriginal = ""; break;
                case InputCommand.RevertTitle: _title.InvokeSemanticSetValue(_editOriginal); _editOriginal = ""; break;
                case InputCommand.FailSearch: SetQuery(_seed.FailureQuery); break; case InputCommand.Retry: Retry(); break; case InputCommand.ToggleTheme: _theme.Value = ReferenceEquals(_theme.Value, Themes.Light) ? Themes.Dark : Themes.Light; break; case InputCommand.ReducedMotion: _reducedMotion = true; break;
                case InputCommand.Tab: MoveFocus(1); break; case InputCommand.ShiftTab: MoveFocus(-1); break; case InputCommand.EscapeToList: _focusName = "list"; _focusTrace.Add(_focusName); _list.Select(Selected, true); break;
            }
            _graph.Drain();
        }
        private void Retry() { _query.Value = "auth"; _ = _async.Value; _graph.Drain(); }
        private void CompleteAsync() { for (var attempt = 0; attempt != 10 && _async.Pending; attempt++) { Thread.Sleep(_seed.AsyncDelayMilliseconds + 10); _graph.Drain(); } Sync(); }
        private void Sync() { _list.SetItems(_filtered.Value.Select(x => x.Id)); _graph.Drain(); }
        private void MoveFocus(int delta) { var order = new[] { "search", "filters", "list", "details", "title" }; var index = Array.IndexOf(order, _focusName); _focusName = order[Math.Clamp(index + delta, 0, order.Length - 1)]; _focusTrace.Add(_focusName); }
        public IssueBrowserSemantic[] Semantics()
        {
            _searchSemantic.Semantics = _searchSemantic.Semantics with { Value = Query }; _statusSemantic.Semantics = _statusSemantic.Semantics with { Value = _status.Value }; _prioritySemantic.Semantics = _prioritySemantic.Semantics with { Value = _priority.Value }; _assigneeSemantic.Semantics = _assigneeSemantic.Semantics with { Value = _assignee.Value };
            _list.Viewport.Element.Semantics = _list.Viewport.Element.Semantics with { Value = Selected, Actions = ["select", "scroll"] }; foreach (var element in new[] { _searchSemantic, _statusSemantic, _prioritySemantic, _assigneeSemantic, _list.Viewport.Element }) element.Mark(DirtyFacet.Semantics); _projection.Project(Flatten(_root));
            return _snapshots.Semantics.Where(pair => pair.Key.Value is "issue-browser.search" or "issue-browser.status" or "issue-browser.priority" or "issue-browser.assignee" or "issues.viewport" or "issue-browser.title" or "issue-browser.retry").Select(pair => new IssueBrowserSemantic(pair.Key.Value switch { "issue-browser.search" => "search", "issue-browser.status" => "status", "issue-browser.priority" => "priority", "issue-browser.assignee" => "assignee", "issues.viewport" => "issues", "issue-browser.title" => "title", _ => "retry" }, pair.Value.Role, pair.Value.Name, pair.Value.Value, pair.Value.Actions ?? [])).ToArray();
        }
        public IssueBrowserIdentity Identity() => new(Rows.Count, Realized, Selected, Theme, Query, _status.Value, _priority.Value, _assignee.Value, _list.Viewport.Offset, Draft, _reducedMotion, Width, Height);
        public void Dispose() { _retry.Dispose(); _title.Dispose(); _list.Dispose(); _async.Dispose(); _filtered.Dispose(); _query.Dispose(); _status.Dispose(); _priority.Dispose(); _assignee.Dispose(); _theme.Dispose(); }
        private static IssueBrowserStep Step(string step, bool pass, string expected, string observed) => new(step, pass, expected, observed);
        private static IEnumerable<StableElement> Flatten(StableElement root) => [root, .. root.Children.SelectMany(Flatten)];
    }
}

internal sealed record IssueBrowserStep([property: System.Text.Json.Serialization.JsonPropertyName("step")] string Step, [property: System.Text.Json.Serialization.JsonPropertyName("pass")] bool Pass, [property: System.Text.Json.Serialization.JsonPropertyName("expected")] string Expected, [property: System.Text.Json.Serialization.JsonPropertyName("observed")] string Observed);
internal sealed record IssueBrowserIdentity(int Rows, int Realized, string Selected, string Theme, string Query, string? Status, string? Priority, string? Assignee, int Scroll, string Draft, bool ReducedMotion, int Width, int Height);
internal sealed record IssueBrowserSemantic(string Id, string Role, string Name, string? Value, string[] Actions);
internal sealed record IssueBrowserAssigneeFilterCheck(bool Pass, int Expected, int Observed, string Assignee);
internal sealed record IssueBrowserSeed([property: System.Text.Json.Serialization.JsonPropertyName("count")] int Count, [property: System.Text.Json.Serialization.JsonPropertyName("asyncDelayMilliseconds")] int AsyncDelayMilliseconds, [property: System.Text.Json.Serialization.JsonPropertyName("failureQuery")] string FailureQuery);
