using System.Runtime.InteropServices;
using System.Text.Json;
using SDL3;

/// <summary>The issue browser owns state and intent only; NativeUi owns its tree, rendering, input, and semantics.</summary>
internal static class IssueBrowser
{
    private const int Width = 1200;
    private const int Height = 760;
    private const int RowHeight = 34;
    private const int ViewportHeight = 578;

    private sealed record Issue(string Id, string Title, string Status, string Priority, string Assignee);

    internal enum InputCommand
    {
        Search, OpenFilter, HighFilter, AssigneeFilter, PageDown, End, Home,
        SelectFirst, Next, RemoveSelected, EditTitle, CommitTitle, RevertTitle,
        FailSearch, Retry, ToggleTheme, ReducedMotion, Tab, ShiftTab, EscapeToList
    }

    public static string RunWalkthrough(string? output)
    {
        var directory = Path.GetFullPath(output ?? Path.Combine("..", "evidence", "issue-12", "native"));
        Directory.CreateDirectory(directory);
        using var app = new State(LoadSeed());
        var steps = app.Walkthrough();
        File.WriteAllLines(Path.Combine(directory, "walkthrough.jsonl"), steps.Select(step => JsonSerializer.Serialize(step, ProbeJsonContext.Default.IssueBrowserStep)));
        File.WriteAllText(Path.Combine(directory, "assignee-filter.json"), JsonSerializer.Serialize(app.AssigneeCheck(), ProbeJsonContext.Default.IssueBrowserAssigneeFilterCheck));
        File.WriteAllText(Path.Combine(directory, "semantics.json"), JsonSerializer.Serialize(app.Semantics(), ProbeJsonContext.Default.IssueBrowserSemanticArray));
        File.WriteAllText(Path.Combine(directory, "identity.json"), JsonSerializer.Serialize(app.Identity(), ProbeJsonContext.Default.IssueBrowserIdentity));
        File.WriteAllText(Path.Combine(directory, "tree.txt"), app.TreeDump());
        File.WriteAllText(Path.Combine(directory, "layout.txt"), app.LayoutDump());
        File.WriteAllText(Path.Combine(directory, "style.txt"), app.StyleDump());
        File.WriteAllBytes(Path.Combine(directory, "light.png"), app.Capture(false));
        File.WriteAllBytes(Path.Combine(directory, "dark.png"), app.Capture(true));
        if (steps.Any(step => !step.Pass)) throw new InvalidOperationException("Issue browser walkthrough failed.");
        return string.Join(Environment.NewLine, steps.Select(step => JsonSerializer.Serialize(step, ProbeJsonContext.Default.IssueBrowserStep)));
    }

    public static string RunVisible()
    {
        using var app = new State(LoadSeed());
        using var host = new WindowHost("Lucent Native Issue Browser", Width, Height, SDL.WindowFlags.Resizable);
        using var presenter = new SdlSkiaPresenter(host.Window, new SkiaSceneRenderer());
        if (!SDL.ShowWindow(host.Window)) throw new InvalidOperationException(SDL.GetError());
        app.Route(new(CompositionInputKind.Semantic, "Search issues", "focus"));
        app.Present(presenter);
        SDL.StartTextInput(host.Window);
        while (SDL.WaitEventTimeout(out var e, 20))
        {
            var type = (SDL.EventType)e.Type;
            if (type is SDL.EventType.Quit or SDL.EventType.WindowCloseRequested) break;
            var changed = type switch
            {
                SDL.EventType.TextInput => app.Route(new(CompositionInputKind.Text, Value: Marshal.PtrToStringUTF8(e.Text.Text) ?? "")),
                SDL.EventType.KeyDown => app.Route(new(CompositionInputKind.Key, Value: e.Key.Key switch { SDL.Keycode.Pagedown => "PageDown", SDL.Keycode.Pageup => "PageUp", SDL.Keycode.End => "End", SDL.Keycode.Home => "Home", SDL.Keycode.Down => "ArrowDown", SDL.Keycode.Up => "ArrowUp", SDL.Keycode.Left => "ArrowLeft", SDL.Keycode.Right => "ArrowRight", SDL.Keycode.Backspace => "Backspace", SDL.Keycode.Delete => "Delete", SDL.Keycode.Return => "Enter", SDL.Keycode.Space => "Space", SDL.Keycode.Escape => "Escape", SDL.Keycode.Tab => "Tab", SDL.Keycode.A => "A", SDL.Keycode.C => "C", SDL.Keycode.X => "X", SDL.Keycode.V => "V", _ => "" }, Shift: (e.Key.Mod & SDL.Keymod.Shift) != 0, Control: (e.Key.Mod & SDL.Keymod.Ctrl) != 0)),
                SDL.EventType.MouseWheel => app.Route(new(CompositionInputKind.Wheel, Value: e.Wheel.Y < 0 ? "PageDown" : "PageUp", X: (int)e.Wheel.MouseX, Y: (int)e.Wheel.MouseY)),
                SDL.EventType.MouseButtonDown => app.Route(new(CompositionInputKind.Pointer, X: (int)e.Button.X, Y: (int)e.Button.Y, Pointer: PointerKind.Down)),
                SDL.EventType.MouseButtonUp => app.Route(new(CompositionInputKind.Pointer, X: (int)e.Button.X, Y: (int)e.Button.Y, Pointer: PointerKind.Up)),
                _ => false
            };
            if (changed) app.Present(presenter);
            app.Advance(TimeSpan.FromMilliseconds(20));
        }
        SDL.StopTextInput(host.Window);
        return $"{{\"mode\":\"native-ui\",\"rows\":{app.Rows.Count},\"realized\":{app.Realized}}}";
    }

    private static IssueBrowserSeed LoadSeed()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "experiments", "native-stack", "gauntlet", "issues.seed.json"))) root = root.Parent;
        var path = root is null ? Path.Combine("gauntlet", "issues.seed.json") : Path.Combine(root.FullName, "experiments", "native-stack", "gauntlet", "issues.seed.json");
        return JsonSerializer.Deserialize(File.ReadAllText(path), ProbeJsonContext.Default.IssueBrowserSeed) ?? throw new InvalidOperationException("Invalid issue seed.");
    }

    private sealed class State : IDisposable
    {
        private readonly IssueBrowserSeed _seed;
        private readonly ReactiveGraph _graph = new();
        private readonly ReactiveSignal<Issue[]> _source;
        private readonly ReactiveSignal<string> _query;
        private readonly ReactiveSignal<string> _selected;
        private readonly ReactiveSignal<string> _draft;
        private readonly ReactiveSignal<string?> _removed;
        private readonly ReactiveSignal<string?> _status;
        private readonly ReactiveSignal<string?> _priority;
        private readonly ReactiveSignal<string?> _assignee;
        private readonly ReactiveSignal<ThemeLayer> _theme;
        private readonly ReactiveAsyncComputed<Issue[]> _latest;
        private readonly RetainedComposition _ui;
        private readonly ReactiveSignal<bool> _reducedMotion;

        public List<Issue> Rows => (_latest.Value ?? []).ToList();
        public int Realized => _ui.RealizedCount("Issue list");
        public string Query => _query.Value;
        private Issue? Current => _source.Value.FirstOrDefault(issue => issue.Id == _selected.Value);
        private ThemeLayer Theme => _theme.Value;

        public State(IssueBrowserSeed seed)
        {
            _seed = seed;
            var issues = Enumerable.Range(0, seed.Count).Select(index => new Issue(
                $"issue-{index:D5}",
                index % 5 == 0 ? $"Authentication failure {index}" : $"Issue {index}",
                index % 3 == 0 ? "Closed" : "Open",
                index % 4 == 0 ? "High" : index % 4 == 1 ? "Medium" : "Low",
                index % 2 == 0 ? "Ada" : "Lin")).ToArray();
            _source = _graph.Signal(issues, "issues.source");
            _query = _graph.Signal("", "issues.query");
            _selected = _graph.Signal("issue-00000", "issues.selected");
            _draft = _graph.Signal("", "issues.draft");
            _removed = _graph.Signal<string?>(null, "issues.removed");
            _status = _graph.Signal<string?>(null, "issues.status");
            _priority = _graph.Signal<string?>(null, "issues.priority");
            _assignee = _graph.Signal<string?>(null, "issues.assignee");
            _theme = _graph.Signal(Themes.Light, "issues.theme");
            _reducedMotion = _graph.Signal(false, "issues.reduced-motion");
            _latest = _graph.AsyncComputed(async cancellation =>
            {
                var query = _query.Value;
                var source = _source.Value;
                var status = _status.Value;
                var priority = _priority.Value;
                var assignee = _assignee.Value;
                var removed = _removed.Value;
                await Task.Delay(seed.AsyncDelayMilliseconds, cancellation);
                if (query == seed.FailureQuery) throw new InvalidOperationException("Search failed");
                return Filter(source, query, status, priority, assignee, removed);
            }, issues, "issues.latest");
            _ui = NativeUi.Mount(_graph, Author());
            _graph.Drain();
        }

        private static Issue[] Filter(Issue[] source, string query, string? status, string? priority, string? assignee, string? removed = null) => source.Where(issue =>
            issue.Id != removed &&
            issue.Title.Contains(query, StringComparison.OrdinalIgnoreCase) &&
            (status is null || issue.Status == status) &&
            (priority is null || issue.Priority == priority) &&
            (assignee is null || issue.Assignee == assignee)).ToArray();

        private UiNode Author()
        {
            Style Panel(int width) => new Style().Size(width, Height).Bg(SemanticToken.Card).Fg(SemanticToken.CardForeground).Padding(16).Gap(10);
            Style Label(int width, int height, int size = 12, int weight = 400) => new Style().Size(width, height).Fg(SemanticToken.Foreground).Type(size, weight);
            UiNode Pill(string name, ReactiveSignal<string?> signal, string value, int width) => (NativeUi.Button(name, () => ToggleFilter(signal, value))
                .Style(new Style().Size(width, 30).Bg(SemanticToken.Muted).Fg(SemanticToken.Foreground).Rounded(15).Padding(8).Type(12, 500))
                .Variants(new VariantStyle(new Style(), Selected: new Style().Bg(SemanticToken.Primary).Fg(SemanticToken.PrimaryForeground)))
                .State(() => signal.Value == value ? StyleState.Selected : StyleState.None));
            Issue? IssueFor(string id) => _source.Value.FirstOrDefault(issue => issue.Id == id);
            UiNode RowFor(Issue issue) => (NativeUi.Selectable(() => RowLabel(IssueFor(issue.Id) ?? issue), () => _selected.Value == issue.Id, () => _selected.Value = issue.Id)
                .Style(new Style().Size(392, RowHeight).Bg(SemanticToken.Card).Fg(SemanticToken.Foreground).Rounded(6).Padding(10).Type(12).Opacity(.92f).Transform(0, 2))
                .Variants(new VariantStyle(new Style(), Selected: new Style().Bg(SemanticToken.Muted).Opacity(1).Transform(0, 0), FocusVisible: new Style().Ring(2))).Transition(TimeSpan.FromMilliseconds(120), () => _reducedMotion.Value));

            var transparent = new UiColor("#00000000");
            return (NativeUi.Row(
                (NativeUi.Column(
                    (NativeUi.Text("LUCENT").Style(Label(176, 20, 11, 700).Fg(SemanticToken.MutedForeground))),
                    (NativeUi.Text("Issue tracker").Style(Label(176, 30, 20, 600))),
                    (NativeUi.TextField("Search issues", () => _query.Value, SetQuery).Style(new Style().Size(176, 38).Bg(SemanticToken.Muted).Fg(SemanticToken.Foreground).Rounded(7).Padding(10).Type(13))),
                    (NativeUi.Text("FILTERS").Style(Label(176, 20, 11, 700).Fg(SemanticToken.MutedForeground))),
                    NativeUi.Row(Pill("Ada", _assignee, "Ada", 52), Pill("Open", _status, "Open", 68), Pill("High", _priority, "High", 56)).Style(new Style().Size(176, 30).Bg(transparent).Gap(8)),
                    (NativeUi.Text("Views").Style(Label(176, 24, 11, 600).Fg(SemanticToken.MutedForeground))),
                    (NativeUi.Text("All issues").Style(Label(176, 28, 13, 500))),
                    (NativeUi.Text("Assigned to me").Style(Label(176, 28, 13).Fg(SemanticToken.MutedForeground))),
                    (NativeUi.Text("Recently updated").Style(Label(176, 28, 13).Fg(SemanticToken.MutedForeground))))
                    .Style(Panel(208).Border(SemanticToken.Border))),
                (NativeUi.Column(
                    (NativeUi.Text(() => $"Open issues · {Rows.Count} matching").Style(Label(392, 38, 15, 600))),
                    (NativeUi.VirtualList("Issue list", () => _latest.Value ?? [], issue => issue.Id, RowFor, RowHeight, ViewportHeight, () => _selected.Value)
                        .Style(new Style().Size(392, ViewportHeight).Bg(SemanticToken.Card).Fg(SemanticToken.Foreground).Gap(2))))
                    .Style(Panel(424).Border(SemanticToken.Border))),
                (NativeUi.Column(
                    (NativeUi.Text("ISSUE DETAILS").Style(Label(520, 22, 11, 700).Fg(SemanticToken.MutedForeground))),
                    (NativeUi.Text(() => Current?.Title ?? "Select an issue").Style(Label(520, 38, 20, 600))),
                    (NativeUi.Text(() => _selected.Value.Replace("issue-", "#")).Style(Label(520, 22, 12).Fg(SemanticToken.MutedForeground))),
                    NativeUi.Row(
                        (NativeUi.Text(() => Current?.Status ?? "Open").Style(new Style().Size(76, 26).Bg(SemanticToken.Muted).Fg(SemanticToken.Foreground).Rounded(13).Padding(10).Type(11, 600))),
                        (NativeUi.Text(() => Current?.Priority ?? "High").Style(new Style().Size(76, 26).Bg(SemanticToken.Muted).Fg(SemanticToken.Foreground).Rounded(13).Padding(10).Type(11, 600))))
                        .Style(new Style().Size(520, 26).Bg(transparent).Gap(8)),
                    (NativeUi.Text("Title").Style(Label(520, 22, 11).Fg(SemanticToken.MutedForeground))),
                    (NativeUi.TextField("Title", () => _draft.Value.Length == 0 ? Current?.Title ?? "" : _draft.Value, value => _draft.Value = value)
                        .Style(new Style().Size(476, 42).Bg(SemanticToken.Muted).Fg(SemanticToken.Foreground).Rounded(7).Padding(10).Type(13))
                        .Variants(new VariantStyle(new Style(), FocusVisible: new Style().Ring(2)))),
                    (NativeUi.Text("Description").Style(Label(520, 22, 11).Fg(SemanticToken.MutedForeground))),
                    (NativeUi.Text("Investigate the reported behavior and document the smallest safe fix.").Style(Label(520, 48, 13))),
                    NativeUi.Row(
                        (NativeUi.Button("Save changes", Commit).Style(new Style().Size(104, 34).Bg(SemanticToken.Primary).Fg(SemanticToken.PrimaryForeground).Rounded(7).Padding(10).Type(12, 600))),
                        (NativeUi.Button("Retry", Retry).Style(new Style().Size(72, 34).Bg(SemanticToken.Muted).Fg(SemanticToken.Foreground).Rounded(7).Padding(10).Type(12, 500))))
                        .Style(new Style().Size(520, 34).Bg(transparent).Gap(10)),
                    (NativeUi.Text("Activity").Style(Label(520, 26, 15, 600))),
                    NativeUi.Region("Search state", () => _latest.Error?.Message).Style(new Style().Size(520, 1).Bg(transparent)),
                    (NativeUi.Text(() => _latest.Error is null ? "●  Query completed and retained the latest result." : "●  Search failed — retry to keep working with stale results.").Style(Label(520, 28, 12).Fg(SemanticToken.MutedForeground))))
                    .Style(Panel(568).Border(SemanticToken.Border))))
                .Style(new Style().Size(Width, Height).Bg(SemanticToken.Background).Fg(SemanticToken.Foreground)).Theme(() => Theme));
        }

        private static string RowLabel(Issue issue) => $"{(issue.Status == "Open" ? "●" : "○")}  {issue.Title}   {issue.Id.Replace("issue-", "#")}";

        public bool Send(InputCommand command, string? text = null)
        {
            switch (command)
            {
                case InputCommand.Search: _ui.Input(new(CompositionInputKind.Semantic, "Search issues", "set-value:" + (text ?? ""))); break;
                case InputCommand.OpenFilter: _ui.Input(new(CompositionInputKind.Semantic, "Open", "press")); break;
                case InputCommand.HighFilter: _ui.Input(new(CompositionInputKind.Semantic, "High", "press")); break;
                case InputCommand.AssigneeFilter: _ui.Input(new(CompositionInputKind.Semantic, "Ada", "press")); break;
                case InputCommand.PageDown: _ui.Input(new(CompositionInputKind.Semantic, "Issue list", "PageDown")); break;
                case InputCommand.End: _ui.Input(new(CompositionInputKind.Semantic, "Issue list", "End")); break;
                case InputCommand.Home: _ui.Input(new(CompositionInputKind.Semantic, "Issue list", "Home")); break;
                case InputCommand.SelectFirst: SelectIndex(0); break;
                case InputCommand.Next: _ui.Input(new(CompositionInputKind.Semantic, "Issue list", "focus")); _ui.Input(new(CompositionInputKind.Key, Value: "ArrowDown")); break;
                case InputCommand.RemoveSelected: RemoveSelected(); break;
                case InputCommand.EditTitle:
                    _ui.Input(new(CompositionInputKind.Semantic, "Title", "focus"));
                    _ui.Input(new(CompositionInputKind.Key, Value: "A", Control: true));
                    _ui.Input(new(CompositionInputKind.Text, Value: text ?? ""));
                    break;
                case InputCommand.CommitTitle: _ui.Input(new(CompositionInputKind.Semantic, "Save changes", "press")); break;
                case InputCommand.RevertTitle: _draft.Value = Current?.Title ?? ""; break;
                case InputCommand.FailSearch: _ui.Input(new(CompositionInputKind.Semantic, "Search issues", "set-value:" + _seed.FailureQuery)); break;
                case InputCommand.Retry: _ui.Input(new(CompositionInputKind.Semantic, "Retry", "press")); break;
                case InputCommand.ToggleTheme: _theme.Value = ReferenceEquals(Theme, Themes.Light) ? Themes.Dark : Themes.Light; break;
                case InputCommand.ReducedMotion: _reducedMotion.Value = true; break;
                case InputCommand.Tab: _ui.Input(new(CompositionInputKind.Tab)); break;
                case InputCommand.ShiftTab: _ui.Input(new(CompositionInputKind.Tab, Shift: true)); break;
                case InputCommand.EscapeToList: _ui.Input(new(CompositionInputKind.Semantic, "Issue list", "focus")); break;
            }
            _graph.Drain();
            return true;
        }

        public bool Route(CompositionInput input)
        {
            if (input is { Kind: CompositionInputKind.Key, Value: "Escape" }) { Send(InputCommand.EscapeToList); return true; }
            var handled = _ui.Input(input); _graph.Drain(); return handled;
        }
        public void Advance(TimeSpan elapsed) => _ui.Advance(elapsed);

        private void ToggleFilter(ReactiveSignal<string?> signal, string value)
        {
            signal.Value = signal.Value == value ? null : value;
            _ = _latest.Value;
        }

        private void SetQuery(string value)
        {
            _query.Value = value;
            _ = _latest.Value;
        }

        private void Retry()
        {
            _query.Value = "auth";
            _ = _latest.Value;
        }

        private void SelectIndex(int index)
        {
            if (Rows.Count == 0) return;
            var issue = Rows[Math.Clamp(index, 0, Rows.Count - 1)];
            var name = RowLabel(issue);
            _ui.Input(new(CompositionInputKind.Pointer, name, Pointer: PointerKind.Down));
            _ui.Input(new(CompositionInputKind.Pointer, name, Pointer: PointerKind.Up));
        }

        private void RemoveSelected()
        {
            var index = Rows.FindIndex(issue => issue.Id == _selected.Value);
            _removed.Value = _selected.Value;
            _ = _latest.Value;
            var remaining = Filter(_source.Value, _query.Value, _status.Value, _priority.Value, _assignee.Value, _selected.Value);
            _selected.Value = remaining.Length == 0 ? "" : remaining[Math.Min(index, remaining.Length - 1)].Id;
        }

        private void Commit()
        {
            if (Current is not { } current) return;
            if (_draft.Value.Length == 0 || _draft.Value == current.Title) { _draft.Value = ""; return; }
            _source.Value = _source.Value.Select(issue => issue.Id == current.Id ? issue with { Title = _draft.Value } : issue).ToArray();
            _ = _latest.Value;
            _draft.Value = "";
        }

        private void CompleteAsync()
        {
            for (var attempt = 0; attempt != 10 && _latest.Pending; attempt++)
            {
                Thread.Sleep(_seed.AsyncDelayMilliseconds + 10);
                _graph.Drain();
            }
        }

        public List<IssueBrowserStep> Walkthrough()
        {
            var output = new List<IssueBrowserStep>
            {
                Step("W1 cold-start", Rows.Count == _seed.Count && Realized <= 21, "10000 rows; realized <= three visible windows", $"{Rows.Count} rows; realized {Realized}")
            };
            var stale = Rows.Select(issue => issue.Id).ToArray();
            Send(InputCommand.Search, "auth");
            var pending = _latest.Pending && stale.SequenceEqual(Rows.Select(issue => issue.Id));
            CompleteAsync();
            output.Add(Step("W2 search", pending && Rows.Count == 2000, "stale rows then 2000 auth rows", $"{Rows.Count} rows"));
            foreach (var query in new[] { "a", "au", "aut", "auth", "authentication" }) Send(InputCommand.Search, query);
            CompleteAsync();
            output.Add(Step("W3 latest-query", Rows.Count == 2000 && !_latest.Pending, "only final query commits", $"{Rows.Count} final rows"));
            Send(InputCommand.Search, "auth"); CompleteAsync(); Send(InputCommand.OpenFilter); CompleteAsync(); Send(InputCommand.HighFilter); CompleteAsync();
            output.Add(Step("W4 filters", Rows.Count == 333, "333 open high auth rows", $"{Rows.Count} rows"));
            Send(InputCommand.PageDown); var page = _ui.RealizedWindow("Issue list"); Send(InputCommand.End); var ended = _ui.RealizedWindow("Issue list"); var viewport = _ui.VirtualBounds("Issue list"); var end = _ui.ScrollOffset("Issue list") > 0 && ended.Last == Rows[^1].Id && ended.LastBounds.Y >= viewport.Y && ended.LastBounds.Y < viewport.Y + viewport.Height; Send(InputCommand.Home); var home = _ui.RealizedWindow("Issue list");
            output.Add(Step("W5 scroll", end && page.First != home.First && home.First == Rows[0].Id && home.FirstBounds.Y == viewport.Y && _ui.ScrollOffset("Issue list") == 0 && Realized <= 21, "PageDown End Home retain ceiling", $"page={page.First}-{page.Last}; end={ended.First}-{ended.Last}@{ended.FirstBounds.Y},{ended.LastBounds.Y}; home={home.First}-{home.Last}@{home.FirstBounds.Y},{home.LastBounds.Y}"));
            Send(InputCommand.SelectFirst); Send(InputCommand.Next);
            output.Add(Step("W6 selection", _selected.Value == Rows[1].Id, "pointer and keyboard select details", _selected.Value));
            var prior = Rows.FindIndex(issue => issue.Id == _selected.Value);
            var expected = Rows.Where(issue => issue.Id != _selected.Value).ElementAt(prior).Id;
            Send(InputCommand.RemoveSelected);
            output.Add(Step("W7 filtered-selection", _selected.Value == expected, "removed selection clamps to prior index", _selected.Value));
            var original = Current!.Title;
            Send(InputCommand.EditTitle, "uncommitted"); Send(InputCommand.RevertTitle); var reverted = _draft.Value == original;
            Send(InputCommand.EditTitle, "Scalar 😀 title"); Send(InputCommand.CommitTitle); var committed = Current!.Title == "Scalar 😀 title" && _ui.FindText(RowLabel(Current!)) is not null;
            Send(InputCommand.EditTitle, original); Send(InputCommand.CommitTitle);
            output.Add(Step("W8 title-edit", reverted && committed && Current!.Title == original, "scalar edit, Escape revert, then commit", Current!.Title));
            var staleKeys = Rows.Select(issue => issue.Id).ToArray(); Send(InputCommand.FailSearch); var staleWhilePending = staleKeys.SequenceEqual(Rows.Select(issue => issue.Id)); CompleteAsync(); var staleAfterFailure = staleKeys.SequenceEqual(Rows.Select(issue => issue.Id)); var failed = _latest.Error is not null && Semantics().Any(item => item.Name == "Search state" && item.Value == "Search failed") && Semantics().Any(item => item.Name == "Retry" && item.Actions.Contains("press"));
            Send(InputCommand.Retry); CompleteAsync();
            output.Add(Step("W9 failure-retry", staleWhilePending && staleAfterFailure && failed && _latest.Error is null, "error retains exact stale rows then retry clears", $"pending={staleWhilePending}; failed-stale={staleAfterFailure}; error={failed}; cleared={_latest.Error is null}"));
            Send(InputCommand.EditTitle, "draft"); var selected = _selected.Value; var queryState = Query; var status = _status.Value; var priority = _priority.Value; var scroll = _ui.ScrollOffset("Issue list");
            var selectedRow = RowLabel(Current!); var motionActive = _ui.TransitionActive(selectedRow); Send(InputCommand.ReducedMotion); var motionSnapped = !_ui.TransitionActive(selectedRow); Send(InputCommand.ToggleTheme); var stayedSnapped = !_ui.TransitionActive(selectedRow);
            output.Add(Step("W10 theme", ReferenceEquals(Theme, Themes.Dark) && _selected.Value == selected && Query == queryState && _status.Value == status && _priority.Value == priority && _ui.ScrollOffset("Issue list") == scroll && _draft.Value == "draft" && _reducedMotion.Value && motionActive && motionSnapped && stayedSnapped, "theme preserves state and reduced motion snaps an active mounted transition", $"active={motionActive}; snapped={motionSnapped}; stayed={stayedSnapped}"));
            Send(InputCommand.EscapeToList);
            var focus = new List<string?> { _ui.FocusedName() };
            Send(InputCommand.Tab); focus.Add(_ui.FocusedName()); Send(InputCommand.Tab); focus.Add(_ui.FocusedName()); Send(InputCommand.Tab); focus.Add(_ui.FocusedName()); Send(InputCommand.Tab); focus.Add(_ui.FocusedName()); Send(InputCommand.ShiftTab); focus.Add(_ui.FocusedName()); Send(InputCommand.EscapeToList); focus.Add(_ui.FocusedName());
            var expectedFocus = new string?[] { "Issue list", "Title", "Save changes", "Retry", "Search issues", "Retry", "Issue list" };
            output.Add(Step("W11 keyboard", focus.SequenceEqual(expectedFocus), "real Tab/ShiftTab traversal and Escape list focus", string.Join('>', focus)));
            var semantics = Semantics();
            output.Add(Step("W12 semantics", semantics.All(item => item.Role.Length > 0 && item.Name.Length > 0 && item.SuppressionReason is null) && semantics.Any(item => item.Name == "Title" && item.Actions.Contains("set-value")) && semantics.Any(item => item.Name == "Issue list" && item.Value == _selected.Value) && semantics.Any(item => item.Selected) && semantics.Any(item => item.Focused), "complete mounted semantics with zero suppressions", "projected semantic tree"));
            return output;
        }

        public IssueBrowserAssigneeFilterCheck AssigneeCheck()
        {
            _query.Value = "auth"; _status.Value = null; _priority.Value = null; _assignee.Value = "Ada"; _removed.Value = null; _ = _latest.Value; CompleteAsync();
            return new(Rows.Count == 1000, 1000, Rows.Count, _assignee.Value ?? "none");
        }

        public IssueBrowserSemantic[] Semantics() => _ui.Semantics().Select(pair => new IssueBrowserSemantic(pair.Name, pair.Value.Role, pair.Value.Name, pair.Value.Value, pair.Value.Actions ?? [], pair.Value.Enabled, pair.Value.Focused, pair.Value.Selected, pair.Value.SuppressionReason)).ToArray();

        public IssueBrowserIdentity Identity() => new(Rows.Count, Realized, _selected.Value, ReferenceEquals(Theme, Themes.Dark) ? "dark" : "light", Query, _status.Value, _priority.Value, _assignee.Value, _ui.ScrollOffset("Issue list"), _draft.Value, _reducedMotion.Value, Width, Height);
        public byte[] Capture(bool dark) { _theme.Value = dark ? Themes.Dark : Themes.Light; _graph.Drain(); return _ui.CapturePng(Width, Height); }
        public void Present(SdlSkiaPresenter presenter) => _ui.Present(presenter);
        public string TreeDump() => _ui.TreeDump();
        public string LayoutDump() => _ui.LayoutDump();
        public string StyleDump() => _ui.StyleDump();

        public void Dispose()
        {
            _ui.Dispose(); _latest.Dispose();
            _source.Dispose(); _query.Dispose(); _selected.Dispose(); _draft.Dispose(); _removed.Dispose();
            _status.Dispose(); _priority.Dispose(); _assignee.Dispose(); _theme.Dispose(); _reducedMotion.Dispose();
        }

        private static IssueBrowserStep Step(string step, bool pass, string expected, string observed) => new(step, pass, expected, observed);
    }
}

internal sealed record IssueBrowserStep([property: System.Text.Json.Serialization.JsonPropertyName("step")] string Step, [property: System.Text.Json.Serialization.JsonPropertyName("pass")] bool Pass, [property: System.Text.Json.Serialization.JsonPropertyName("expected")] string Expected, [property: System.Text.Json.Serialization.JsonPropertyName("observed")] string Observed);
internal sealed record IssueBrowserIdentity(int Rows, int Realized, string Selected, string Theme, string Query, string? Status, string? Priority, string? Assignee, int Scroll, string Draft, bool ReducedMotion, int Width, int Height);
internal sealed record IssueBrowserSemantic(string Id, string Role, string Name, string? Value, string[] Actions, bool Enabled, bool Focused, bool Selected, string? SuppressionReason);
internal sealed record IssueBrowserAssigneeFilterCheck(bool Pass, int Expected, int Observed, string Assignee);
internal sealed record IssueBrowserSeed([property: System.Text.Json.Serialization.JsonPropertyName("count")] int Count, [property: System.Text.Json.Serialization.JsonPropertyName("asyncDelayMilliseconds")] int AsyncDelayMilliseconds, [property: System.Text.Json.Serialization.JsonPropertyName("failureQuery")] string FailureQuery);
