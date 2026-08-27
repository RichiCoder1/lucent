using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SDL3;

/// <summary>The issue browser owns state and intent only; NativeUi owns its tree, rendering, input, and semantics.</summary>
internal static class IssueBrowser
{
    private const int Width = 1200;
    private const int Height = 760;
    private const int RowHeight = 34;
    private const int ViewportHeight = 578;
    private const int Issue15Samples = 500;
    private const int Issue15Warmup = 50;
    private const int Issue15IdleSeconds = 10;
    private const int Issue15ScrollCycles = 20;
    private const double Issue15P95BudgetMilliseconds = 16.7;
    private const double Issue15P99BudgetMilliseconds = 33.3;
    private const long Issue15LiveGrowthBudgetBytes = 16L * 1024 * 1024;
    private const long Issue15ReturnAllowanceBytes = 8L * 1024 * 1024;
    private static readonly string[] Issue15Corpus = Enumerable.Range(0, Issue15Samples).Select(index => (index % 41) switch { 0 => "Home", 40 => "End", _ => "PageDown" }).ToArray();

    private sealed record Issue(string Id, string Title, string Status, string Priority, string Assignee);

    internal enum InputCommand
    {
        Search, OpenFilter, ClosedFilter, HighFilter, AssigneeFilter, PageDown, End, Home,
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
        File.WriteAllText(Path.Combine(directory, "closed-filter.json"), JsonSerializer.Serialize(app.ClosedFilterCheck(), ProbeJsonContext.Default.IssueBrowserClosedFilterCheck));
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

    /// <summary>Exercises the retained semantic/UIA seam without an app-owned accessibility tree.</summary>
    public static string RunIssue14Contract(string? output)
    {
        var directory = Path.GetFullPath(output ?? Path.Combine("..", "evidence", "issue-14", "native"));
        Directory.CreateDirectory(directory);
        using var app = new State(LoadSeed());
        var initial = app.Semantics();
        var completeSemantics = initial.All(item => item.Role.Length > 0 && item.Name.Length > 0 && item.SuppressionReason is null);
        var roleMappings = initial.Select(item => new Issue14RoleMapping(item.Name, item.Role, UiaControlType(item.Role), item.Actions, item.Enabled)).ToArray();

        app.Send(InputCommand.EditTitle, "UIA Value 😀");
        var valueSet = app.Semantics().Any(item => item.Name == "Title" && item.Value == "UIA Value 😀" && item.Actions.Contains("set-value"));
        app.Send(InputCommand.CommitTitle);
        var valueCommitted = app.CurrentTitle == "UIA Value 😀";
        app.Send(InputCommand.OpenFilter); app.CompleteAsync();
        var invoked = app.Rows.All(issue => issue.Status == "Open");
        app.Send(InputCommand.SelectFirst); app.Send(InputCommand.Next);
        var selectedAndFocused = app.Semantics().Any(item => item.Selected) && app.Semantics().Any(item => item.Name == "Issue list" && item.Focused);
        app.Send(InputCommand.FailSearch); app.CompleteAsync();
        var retryAvailable = app.Semantics().Any(item => item.Name == "Retry" && item.Enabled && item.Actions.Contains("press"));
        app.Send(InputCommand.Retry); app.CompleteAsync();
        var retried = app.Semantics().All(item => item.Name != "Search state" || item.Value is null);
        var result = new Issue14ProviderContract(completeSemantics, valueSet, valueCommitted, invoked, selectedAndFocused, retryAvailable, retried,
            roleMappings, app.Semantics().Count(item => item.SuppressionReason is not null));
        if (!result.Ok) throw new InvalidOperationException("Issue #14 semantic provider contract failed.");
        File.WriteAllText(Path.Combine(directory, "provider-contract.json"), JsonSerializer.Serialize(result, ProbeJsonContext.Default.Issue14ProviderContract));
        File.WriteAllText(Path.Combine(directory, "semantic-dump.json"), JsonSerializer.Serialize(app.Semantics(), ProbeJsonContext.Default.IssueBrowserSemanticArray));
        return JsonSerializer.Serialize(result, ProbeJsonContext.Default.Issue14ProviderContract);
    }

    private static int UiaControlType(string role) => role switch
    {
        "button" => 50000, "edit" => 50004, "text" => 50020, "listbox" => 50008,
        "option" => 50007, "group" => 50026, "region" => 50033, _ => throw new InvalidOperationException($"No UIA control type for {role}.")
    };

    public static string RunVisible()
    {
        using var app = new State(LoadSeed());
        using var host = new WindowHost("Lucent Native Issue Browser", Width, Height, SDL.WindowFlags.Resizable);
        using var presenter = new SdlSkiaPresenter(host.Window, new SkiaSceneRenderer());
        var dispatcher = new UiaSourceDispatcher(app);
        using var provider = new UiaIssueProvider(host.Hwnd, dispatcher); using var subclass = new WindowSubclass(host.Hwnd, provider);
        dispatcher.Attach(provider.Reconcile, provider.ActionCompleted);
        if (!subclass.Install()) throw new InvalidOperationException("SetWindowSubclass failed.");
        if (!SDL.ShowWindow(host.Window)) throw new InvalidOperationException(SDL.GetError());
        app.Route(new(CompositionInputKind.Semantic, "Search issues", "focus"));
        app.Present(presenter);
        SDL.StartTextInput(host.Window);
        var running = true;
        while (running)
        {
            if (SDL.WaitEventTimeout(out var e, 20))
            {
                var type = (SDL.EventType)e.Type;
                if (type is SDL.EventType.Quit or SDL.EventType.WindowCloseRequested) running = false;
                else if (CompositionInputs.TryFromSdl(e, out var input) && app.Route(input!)) app.Present(presenter);
            }
            dispatcher.Pump();
            app.Advance(TimeSpan.FromMilliseconds(20));
        }
        SDL.StopTextInput(host.Window);
        return $"{{\"mode\":\"native-ui\",\"rows\":{app.Rows.Count},\"realized\":{app.Realized}}}";
    }

    /// <summary>Fail-closed serial NativeAOT benchmark over the issue browser's mounted input and SDL present path.</summary>
    public static string RunIssue15Benchmark()
    {
        using var app = new State(LoadSeed());
        using var host = new WindowHost("Lucent Native Issue Browser issue-15", Width, Height, SDL.WindowFlags.Hidden);
        using var presenter = new SdlSkiaPresenter(host.Window, new SkiaSceneRenderer(), captureReadback: false);
        app.Present(presenter);

        for (var index = 0; index < Issue15Warmup; index++) PresentInput(app, presenter, Issue15Corpus[index]);
        var staleRetained = app.Issue15StaleRetained();
        app.CompleteAsync();
        app.Send(InputCommand.Search, "");
        app.CompleteAsync();
        app.Present(presenter);
        var unrelatedInvalidation = app.Issue15UnrelatedWriteIdle();

        ForceCollection();
        var postWarmBaseline = GC.GetTotalMemory(false);
        var scrollDispatches = 0;
        for (var cycle = 0; cycle < Issue15ScrollCycles; cycle++)
        {
            scrollDispatches += app.Issue15FullScrollCycle();
            app.Present(presenter);
        }
        // GetTotalMemory(false) includes dead allocation churn; compact first so this is live managed state.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        var liveAfterScroll = GC.GetTotalMemory(false);
        ForceCollection();
        var postCollection = GC.GetTotalMemory(false);

        var gcBefore = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var samples = new double[Issue15Samples];
        for (var index = 0; index < samples.Length; index++)
        {
            var started = Stopwatch.GetTimestamp();
            PresentInput(app, presenter, Issue15Corpus[index]);
            samples[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
        var gcAfter = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };

        var framesBeforeIdle = app.PresentedFrames;
        var requestsBeforeIdle = app.RequestedFrames;
        var idleStarted = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(idleStarted) < TimeSpan.FromSeconds(Issue15IdleSeconds))
        {
            app.Advance(TimeSpan.FromMilliseconds(20));
            SDL.Delay(20);
        }
        var idleElapsedMilliseconds = Stopwatch.GetElapsedTime(idleStarted).TotalMilliseconds;
        var idleFrames = app.PresentedFrames - framesBeforeIdle;
        var idleRequests = app.RequestedFrames - requestsBeforeIdle;
        var ordered = samples.OrderBy(value => value).ToArray();
        var p95 = Percentile(ordered, .95);
        var p99 = Percentile(ordered, .99);
        var visibleRows = (ViewportHeight + RowHeight - 1) / RowHeight;
        var returnLimit = checked(postWarmBaseline + postWarmBaseline / 10 + Issue15ReturnAllowanceBytes);
        var result = new Issue15BenchmarkResult(
            p95 <= Issue15P95BudgetMilliseconds && p99 <= Issue15P99BudgetMilliseconds &&
            idleElapsedMilliseconds >= Issue15IdleSeconds * 1000 && idleFrames == 0 && idleRequests == 0 && app.Realized <= visibleRows * 3 &&
            liveAfterScroll - postWarmBaseline <= Issue15LiveGrowthBudgetBytes && postCollection <= returnLimit &&
            staleRetained && unrelatedInvalidation,
            Issue15Samples, Issue15Warmup, Issue15IdleSeconds, Issue15ScrollCycles, Issue15Corpus,
            p95, p99, Issue15P95BudgetMilliseconds, Issue15P99BudgetMilliseconds,
            idleElapsedMilliseconds, idleFrames, idleRequests, app.Realized, visibleRows, visibleRows * 3,
            postWarmBaseline, liveAfterScroll, postCollection, liveAfterScroll - postWarmBaseline, Issue15LiveGrowthBudgetBytes, returnLimit,
            allocatedAfter - allocatedBefore, new(gcAfter[0] - gcBefore[0], gcAfter[1] - gcBefore[1], gcAfter[2] - gcBefore[2]),
            presenter.PresentCalls, app.PresentedFrames, app.RequestedFrames, app.ProjectionCalls, scrollDispatches,
            staleRetained, unrelatedInvalidation, RuntimeInformation.FrameworkDescription, RuntimeInformation.ProcessArchitecture.ToString(), Environment.Version.ToString(), host.ReadDpi());
        if (!result.Ok) throw new InvalidOperationException($"Issue #15 budget failed: {JsonSerializer.Serialize(result, ProbeJsonContext.Default.Issue15BenchmarkResult)}");
        return JsonSerializer.Serialize(result, ProbeJsonContext.Default.Issue15BenchmarkResult);
    }

    private static void PresentInput(State app, SdlSkiaPresenter presenter, string command)
    {
        if (!app.Route(new(CompositionInputKind.Semantic, "Issue list", command))) throw new InvalidOperationException($"Issue #15 input was not handled: {command}.");
        app.Present(presenter);
    }

    private static double Percentile(double[] ordered, double percentile) => ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * percentile) - 1, 0, ordered.Length - 1)];
    private static void ForceCollection() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }

    public static string RunUiaHost(string readyPath, string closePath, bool visible = false)
    {
        using var app = new State(LoadSeed()); using var host = new WindowHost("Lucent Native Issue Browser", Width, Height, visible ? SDL.WindowFlags.Resizable : SDL.WindowFlags.Hidden);
        var dispatcher = new UiaSourceDispatcher(app);
        using var provider = new UiaIssueProvider(host.Hwnd, dispatcher); using var subclass = new WindowSubclass(host.Hwnd, provider);
        dispatcher.Attach(provider.Reconcile, provider.ActionCompleted);
        if (!subclass.Install()) throw new InvalidOperationException("SetWindowSubclass failed.");
        if (!provider.ValidateRootAbi()) throw new InvalidOperationException("FragmentRoot SDK ABI check failed.");
        if (visible && !SDL.ShowWindow(host.Window)) throw new InvalidOperationException(SDL.GetError());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(readyPath))!);
        File.WriteAllText(readyPath, JsonSerializer.Serialize(new UiaReadyResult(true, host.HwndText, RuntimeFeature.IsDynamicCodeSupported), ProbeJsonContext.Default.UiaReadyResult));
        var until = Environment.TickCount64 + 30_000;
        while (!File.Exists(closePath) && Environment.TickCount64 < until)
        {
            while (SDL.PollEvent(out var e)) if ((SDL.EventType)e.Type is SDL.EventType.Quit or SDL.EventType.WindowCloseRequested) break;
            dispatcher.Pump();
            SDL.Delay(10);
        }
        if (!File.Exists(closePath)) throw new TimeoutException("UIA helper timed out.");
        if (subclass.RootUiaDeliveryCount == 0) throw new InvalidOperationException("External UIA did not reach the issue-browser provider.");
        var result = $"{{\"ok\":true,\"visible\":{visible.ToString().ToLowerInvariant()},\"rootAbi\":true,\"rootPointCalls\":{provider.RootPointCalls},\"rootFocusCalls\":{provider.RootFocusCalls},\"rootUiaDeliveryCount\":{subclass.RootUiaDeliveryCount},\"cacheCount\":{provider.CacheCount},\"maxCacheCount\":{provider.MaxCacheCount},\"staleDisconnected\":{provider.StaleDisconnected},\"focusEvents\":{provider.FocusEvents},\"propertyEvents\":{provider.PropertyEvents},\"structureEvents\":{provider.StructureEvents}}}";
        File.WriteAllText(readyPath + ".host.json", result);
        return result;
    }

    private sealed class UiaSourceDispatcher(IUiaSemanticSource source) : IUiaSemanticSource
    {
        private readonly int _ownerThread = Environment.CurrentManagedThreadId;
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _pending = new();
        private UiaSemanticNode[] _snapshot = source.UiaNodes;
        private Action<UiaSemanticNode[], UiaSemanticNode[]>? _changed;
        private Action<string, string>? _actionCompleted;
        public UiaSemanticNode[] UiaNodes => Invoke(Read);
        public bool UiaAction(string id, string action, string? value) => Invoke(() => { var result = source.UiaAction(id, action, value); Read(); if (result) _actionCompleted?.Invoke(id, action); return result; });
        public void Attach(Action<UiaSemanticNode[], UiaSemanticNode[]> changed, Action<string, string> actionCompleted) { _changed = changed; _actionCompleted = actionCompleted; }
        public void Pump() { while (_pending.TryDequeue(out var action)) action(); Read(); }
        private UiaSemanticNode[] Read()
        {
            var next = source.UiaNodes; var previous = _snapshot; _snapshot = next;
            if (!previous.SequenceEqual(next)) _changed?.Invoke(previous, next);
            return next;
        }
        private T Invoke<T>(Func<T> action)
        {
            if (Environment.CurrentManagedThreadId == _ownerThread) return action();
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Enqueue(() => { try { completion.SetResult(action()); } catch (Exception exception) { completion.SetException(exception); } });
            return completion.Task.GetAwaiter().GetResult();
        }
    }

    private static IssueBrowserSeed LoadSeed()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "experiments", "native-stack", "gauntlet", "issues.seed.json"))) root = root.Parent;
        var path = root is null ? Path.Combine("gauntlet", "issues.seed.json") : Path.Combine(root.FullName, "experiments", "native-stack", "gauntlet", "issues.seed.json");
        return JsonSerializer.Deserialize(File.ReadAllText(path), ProbeJsonContext.Default.IssueBrowserSeed) ?? throw new InvalidOperationException("Invalid issue seed.");
    }

    private sealed class State : IDisposable, IUiaSemanticSource
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
        private readonly ReactiveSignal<int> _issue15Unrelated;

        public List<Issue> Rows => (_latest.Value ?? []).ToList();
        public int Realized => _ui.RealizedCount("Issue list");
        public int RequestedFrames => _ui.RequestedFrames;
        public int PresentedFrames => _ui.PresentedFrames;
        public int ProjectionCalls => _ui.ProjectionCalls;
        public string Query => _query.Value;
        public string? CurrentTitle => Current?.Title;
        public UiaSemanticNode[] UiaNodes { get { _graph.Drain(); return _ui.UiaNodes(); } }
        public bool UiaAction(string id, string action, string? value) { var result = _ui.UiaAction(id, action, value); _graph.Drain(); return result; }
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
            _issue15Unrelated = _graph.Signal(0, "issues.issue-15-unrelated");
            _latest = _graph.AsyncComputed(async cancellation =>
            {
                var query = _query.Value;
                var source = _source.Value;
                var status = _status.Value;
                var priority = _priority.Value;
                var assignee = _assignee.Value;
                var removed = _removed.Value;
                var result = Filter(source, query, status, priority, assignee, removed);
                await Task.Delay(seed.AsyncDelayMilliseconds, cancellation);
                if (query == seed.FailureQuery) throw new InvalidOperationException("Search failed");
                return result;
            }, issues, "issues.latest");
            _graph.RegisterDependencies(_latest, _source, _query, _status, _priority, _assignee, _removed);
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
            UiNode RowFor(Issue issue) => (NativeUi.Selectable(() => RowLabel(_source.Value.First(current => current.Id == issue.Id)), () => _selected.Value == issue.Id, () => _selected.Value = issue.Id)
                .Style(new Style().Size(392, RowHeight).Bg(SemanticToken.Card).Fg(SemanticToken.Foreground).Rounded(6).Padding(10).Type(12).Opacity(.92f).Transform(0, 2))
                .Variants(new VariantStyle(new Style(), Selected: new Style().Bg(SemanticToken.Muted).Opacity(1).Transform(0, 0), FocusVisible: new Style().Ring(2))).Transition(TimeSpan.FromMilliseconds(120), () => _reducedMotion.Value));

            var transparent = new UiColor("#00000000");
            return (NativeUi.Row(
                (NativeUi.Column(
                    (NativeUi.Text("LUCENT").Style(Label(176, 20, 11, 700).Fg(SemanticToken.MutedForeground))),
                    (NativeUi.Text("Issue tracker").Style(Label(176, 30, 20, 600))),
                    (NativeUi.TextField("Search issues", () => _query.Value, SetQuery).Style(new Style().Size(176, 38).Bg(SemanticToken.Muted).Fg(SemanticToken.Foreground).Rounded(7).Padding(10).Type(13))),
                    (NativeUi.Text("FILTERS").Style(Label(176, 20, 11, 700).Fg(SemanticToken.MutedForeground))),
                    NativeUi.Row(Pill("Ada", _assignee, "Ada", 52), Pill("Open", _status, "Open", 68)).Style(new Style().Size(176, 30).Bg(transparent).Gap(8)),
                    NativeUi.Row(Pill("Closed", _status, "Closed", 76), Pill("High", _priority, "High", 56)).Style(new Style().Size(176, 30).Bg(transparent).Gap(8)),
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
                        .Variants(new VariantStyle(new Style(), FocusVisible: new Style().Ring(2)))
                        .OnCancel(() => _draft.Value = Current?.Title ?? "", "Issue list")),
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
                case InputCommand.ClosedFilter: _ui.Input(new(CompositionInputKind.Semantic, "Closed", "press")); break;
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
                case InputCommand.RevertTitle: _ui.Input(new(CompositionInputKind.Key, Value: "Escape")); break;
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

        public bool Route(CompositionInput input) { var handled = _ui.Input(input); _graph.Drain(); return handled; }
        public void Advance(TimeSpan elapsed) => _ui.Advance(elapsed);

        internal bool Issue15StaleRetained()
        {
            var stale = Rows.Select(issue => issue.Id).ToArray();
            Send(InputCommand.Search, "auth");
            return _latest.Pending && stale.SequenceEqual(Rows.Select(issue => issue.Id));
        }

        internal bool Issue15UnrelatedWriteIdle()
        {
            var projections = _ui.ProjectionCalls;
            var requested = _ui.RequestedFrames;
            _issue15Unrelated.Value++;
            _graph.Drain();
            return _ui.ProjectionCalls == projections && _ui.RequestedFrames == requested;
        }

        internal int Issue15FullScrollCycle()
        {
            var count = 0;
            Route(new(CompositionInputKind.Semantic, "Issue list", "Home")); count++;
            var lastOffset = -1;
            while (_ui.ScrollOffset("Issue list") != lastOffset)
            {
                lastOffset = _ui.ScrollOffset("Issue list");
                Route(new(CompositionInputKind.Semantic, "Issue list", "PageDown")); count++;
            }
            Route(new(CompositionInputKind.Semantic, "Issue list", "Home"));
            return count + 1;
        }

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
            _draft.Value = "";
        }

        internal void CompleteAsync()
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
            var selectedSemantic = Semantics().Single(item => item.Name == RowLabel(Rows[1]));
            output.Add(Step("W6 selection", _selected.Value == Rows[1].Id && Current?.Id == Rows[1].Id && selectedSemantic.Selected && Semantics().Any(item => item.Name == "Issue list" && item.Focused), "pointer and keyboard select details, semantics, and list focus", _selected.Value));
            var prior = Rows.FindIndex(issue => issue.Id == _selected.Value);
            var expected = Rows.Where(issue => issue.Id != _selected.Value).ElementAt(prior).Id;
            Send(InputCommand.RemoveSelected);
            output.Add(Step("W7 filtered-selection", _selected.Value == expected, "removed selection clamps to prior index", _selected.Value));
            var original = Current!.Title;
            Send(InputCommand.EditTitle, "uncommitted"); Send(InputCommand.RevertTitle); var reverted = _draft.Value == original;
            Send(InputCommand.EditTitle, "Scalar 😀 title"); Send(InputCommand.CommitTitle); var modelCommitted = Current!.Title == "Scalar 😀 title"; var rowCommitted = _ui.FindText(RowLabel(Current!)) is not null; var committed = modelCommitted && rowCommitted;
            Send(InputCommand.EditTitle, original); Send(InputCommand.CommitTitle);
            output.Add(Step("W8 title-edit", reverted && committed && Current!.Title == original, "scalar edit, Escape revert, then commit", $"reverted={reverted}; model={modelCommitted}; row={rowCommitted}; final={Current!.Title}"));
            var staleKeys = Rows.Select(issue => issue.Id).ToArray(); Send(InputCommand.FailSearch); var staleWhilePending = staleKeys.SequenceEqual(Rows.Select(issue => issue.Id)); CompleteAsync(); var staleAfterFailure = staleKeys.SequenceEqual(Rows.Select(issue => issue.Id)); var failed = _latest.Error is not null && Semantics().Any(item => item.Name == "Search state" && item.Value == "Search failed") && Semantics().Any(item => item.Name == "Retry" && item.Actions.Contains("press"));
            Send(InputCommand.Retry); CompleteAsync();
            output.Add(Step("W9 failure-retry", staleWhilePending && staleAfterFailure && failed && _latest.Error is null, "error retains exact stale rows then retry clears", $"pending={staleWhilePending}; failed-stale={staleAfterFailure}; error={failed}; cleared={_latest.Error is null}"));
            Send(InputCommand.EditTitle, "draft"); var selected = _selected.Value; var queryState = Query; var status = _status.Value; var priority = _priority.Value; var scroll = _ui.ScrollOffset("Issue list");
            var selectedRow = RowLabel(Current!); Send(InputCommand.Next); _ui.Input(new(CompositionInputKind.Semantic, selectedRow, "select")); _graph.Drain();
            var motionActive = _ui.TransitionActive(selectedRow); Send(InputCommand.ReducedMotion); var motionSnapped = !_ui.TransitionActive(selectedRow); Send(InputCommand.ToggleTheme); var stayedSnapped = !_ui.TransitionActive(selectedRow);
            output.Add(Step("W10 theme", ReferenceEquals(Theme, Themes.Dark) && _selected.Value == selected && Query == queryState && _status.Value == status && _priority.Value == priority && _ui.ScrollOffset("Issue list") == scroll && _draft.Value == "draft" && _reducedMotion.Value && motionActive && motionSnapped && stayedSnapped, "theme preserves state and reduced motion snaps an active mounted transition", $"active={motionActive}; snapped={motionSnapped}; stayed={stayedSnapped}"));
            _ui.Input(new(CompositionInputKind.Semantic, "Search issues", "focus"));
            var focus = new List<string?> { _ui.FocusedName() };
            for (var index = 0; index != 8; index++) { Send(InputCommand.Tab); focus.Add(_ui.FocusedName()); }
            Send(InputCommand.ShiftTab); focus.Add(_ui.FocusedName());
            _ui.Input(new(CompositionInputKind.Semantic, "Title", "focus")); Send(InputCommand.RevertTitle); focus.Add(_ui.FocusedName());
            var expectedFocus = new string?[] { "Search issues", "Ada", "Open", "Closed", "High", "Issue list", "Title", "Save changes", "Retry", "Save changes", "Issue list" };
            output.Add(Step("W11 keyboard", focus.SequenceEqual(expectedFocus), "real filter/list/detail Tab traversal and authored Escape focus", string.Join('>', focus)));
            var semantics = Semantics();
            output.Add(Step("W12 semantics", semantics.All(item => item.Role.Length > 0 && item.Name.Length > 0 && item.SuppressionReason is null) && semantics.Any(item => item.Name == "Title" && item.Actions.Contains("set-value")) && semantics.Any(item => item.Name == "Issue list" && item.Value == _selected.Value) && semantics.Any(item => item.Selected) && semantics.Any(item => item.Focused), "complete mounted semantics with zero suppressions", "projected semantic tree"));
            return output;
        }

        public IssueBrowserAssigneeFilterCheck AssigneeCheck()
        {
            _query.Value = "auth"; _status.Value = null; _priority.Value = null; _assignee.Value = "Ada"; _removed.Value = null; _ = _latest.Value; CompleteAsync();
            return new(Rows.Count == 1000, 1000, Rows.Count, _assignee.Value ?? "none");
        }

        public IssueBrowserClosedFilterCheck ClosedFilterCheck()
        {
            _selected.Value = "issue-00000";
            var selected = _selected.Value; var theme = Theme; var draft = _draft.Value; var reduced = _reducedMotion.Value;
            _query.Value = "auth"; _status.Value = null; _priority.Value = null; _assignee.Value = null; _removed.Value = null; _ = _latest.Value; CompleteAsync();
            _ui.Input(new(CompositionInputKind.Semantic, "Closed", "press")); _graph.Drain(); CompleteAsync(); var active = Rows.Count == 667;
            _ui.Input(new(CompositionInputKind.Semantic, "Closed", "press")); _graph.Drain(); CompleteAsync(); var cleared = Rows.Count == 2000;
            _ui.Input(new(CompositionInputKind.Semantic, "Closed", "press")); _graph.Drain(); CompleteAsync();
            return new(active && cleared && _status.Value == "Closed" && Rows.Count == 667 && _selected.Value == selected && ReferenceEquals(Theme, theme) && _draft.Value == draft && _reducedMotion.Value == reduced, 667, Rows.Count, "Closed");
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
            _issue15Unrelated.Dispose();
        }

        private static IssueBrowserStep Step(string step, bool pass, string expected, string observed) => new(step, pass, expected, observed);
    }
}

internal sealed record IssueBrowserStep([property: System.Text.Json.Serialization.JsonPropertyName("step")] string Step, [property: System.Text.Json.Serialization.JsonPropertyName("pass")] bool Pass, [property: System.Text.Json.Serialization.JsonPropertyName("expected")] string Expected, [property: System.Text.Json.Serialization.JsonPropertyName("observed")] string Observed);
internal sealed record IssueBrowserIdentity(int Rows, int Realized, string Selected, string Theme, string Query, string? Status, string? Priority, string? Assignee, int Scroll, string Draft, bool ReducedMotion, int Width, int Height);
internal sealed record IssueBrowserSemantic(string Id, string Role, string Name, string? Value, string[] Actions, bool Enabled, bool Focused, bool Selected, string? SuppressionReason);
internal sealed record IssueBrowserAssigneeFilterCheck(bool Pass, int Expected, int Observed, string Assignee);
internal sealed record IssueBrowserClosedFilterCheck(bool Pass, int Expected, int Observed, string Status);
internal sealed record Issue14RoleMapping(string Name, string Role, int ControlType, string[] Actions, bool Enabled);
internal sealed record Issue14ProviderContract(bool CompleteSemantics, bool ValueSet, bool ValueCommitted, bool Invoked, bool SelectedAndFocused, bool RetryAvailable, bool Retried, Issue14RoleMapping[] Roles, int EmergencySuppressions)
{
    public bool Ok => CompleteSemantics && ValueSet && ValueCommitted && Invoked && SelectedAndFocused && RetryAvailable && Retried && EmergencySuppressions == 0;
}
internal sealed record IssueBrowserSeed([property: System.Text.Json.Serialization.JsonPropertyName("count")] int Count, [property: System.Text.Json.Serialization.JsonPropertyName("asyncDelayMilliseconds")] int AsyncDelayMilliseconds, [property: System.Text.Json.Serialization.JsonPropertyName("failureQuery")] string FailureQuery);
internal sealed record Issue15GcCounts(int Gen0, int Gen1, int Gen2);
internal sealed record Issue15BenchmarkResult(bool Ok, int SampleCount, int WarmupCount, int IdleSeconds, int ScrollCycles, string[] Corpus, double P95Milliseconds, double P99Milliseconds, double P95BudgetMilliseconds, double P99BudgetMilliseconds, double IdleElapsedMilliseconds, int IdleFrames, int IdleFrameRequests, int RealizedRows, int VisibleRows, int RealizedRowLimit, long PostWarmBaselineBytes, long LiveAfterScrollBytes, long PostCollectionBytes, long LiveGrowthBytes, long LiveGrowthBudgetBytes, long ReturnLimitBytes, long ProcessAllocatedBytes, Issue15GcCounts GcCollections, int NativePresentCalls, int ScheduledPresentCalls, int FrameRequests, int ProjectionCalls, int ScrollDispatches, bool StaleRetention, bool UnrelatedWriteWithoutInvalidation, string FrameworkDescription, string ProcessArchitecture, string EnvironmentVersion, uint Dpi);
