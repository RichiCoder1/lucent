using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Lucent.Core;
using Lucent.Renderer.Skia;
using Lucent.IssueBrowser;
using SkiaSharp;

try
{
    FixtureIdentity();
    RecipeEvidence();
    DirectRootParity();
    AsyncBrowserStates();
    DensityRestyle();
    OptimisticStatusMutations();
    SourceExceptionsBecomeTransientFailures();
    VisualSurface();
    DisposedRequestCannotCommit();
    StartupStaysFrameworkOwned();
    Console.WriteLine("Lucent.IssueBrowser async/data contract: PASS");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("Lucent.IssueBrowser async/data contract: FAIL: " + exception.Message);
    return 1;
}

static void FixtureIdentity()
{
    IssueFixture.AssertIntegrity();
    Assert(IssueFixture.Issues.Count == IssueFixture.TotalCount && IssueFixture.Issues.Count(issue => issue.Status == "open") == IssueFixture.OpenCount && IssueFixture.Issues.Count(issue => issue.Status == "closed") == IssueFixture.ClosedCount,
        "Frozen issue fixture identity or counts changed.");
}

static void RecipeEvidence()
{
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(graph, out _);
    graph.Drain();
    using var renderer = new SkiaSceneRenderer();
    _ = SceneLayout.Project(composition, new(800, 500, 1), renderer);
    var semantics = composition.SemanticDump();
    var dump = composition.Dump();
    Assert(dump.Contains("name=\"issue-browser.filters\"", StringComparison.Ordinal) && dump.Contains("name=\"issue-browser.issue-row\"", StringComparison.Ordinal) &&
        Flatten(composition.SemanticSnapshot()!).Any(node => node.Role == SemanticRole.TextField && node.Name == "Search issues") &&
        Flatten(composition.SemanticSnapshot()!).Count(node => node.Role == SemanticRole.ListItem) <= 9,
        "C# Filter Bar or virtual Issue Row recipe structure regressed.");
    Assert(Hash(dump + semantics) == "5ab48000a6b4ef599cd1f839dab51bda3f8865d8be7ecfa19729c3aabea44fd6", "Direct-root Filter Bar/Issue Row composition/semantic evidence changed: " + Hash(dump + semantics));
}

static void DirectRootParity()
{
    var issue = IssueFixture.Issues[0];
    var generatedFilter = DirectRootEvidence(browser => Lucent.IssueBrowser.Components.FilterBar(browser), issue);
    var handwrittenFilter = DirectRootEvidence(Lucent.IssueBrowser.Components.FilterBarHandwrittenParity, issue);
    var generatedRow = DirectRootEvidence(browser => Lucent.IssueBrowser.Components.IssueRow(browser, issue), issue);
    var handwrittenRow = DirectRootEvidence(browser => Lucent.IssueBrowser.Components.IssueRowHandwrittenParity(browser, issue), issue);
    Assert(generatedFilter == handwrittenFilter && generatedRow == handwrittenRow, "Generated .lui direct roots diverged from the retained handwritten C# parity fixtures.");
    var evidence = generatedFilter.Dump + generatedFilter.Semantics + generatedRow.Dump + generatedRow.Semantics;
    Assert(Hash(evidence) == "f8a08b14e5f17e4f8d15b75ecaca943ec2b1b71495754bb8c619ca05ec8c167d", "Approved direct-root #48 parity rebaseline changed: " + Hash(evidence));
}

static (string Dump, string Semantics) DirectRootEvidence(Func<IssueBrowserState, ComponentRecipe> recipe, BrowserIssue issue)
{
    var graph = new ReactiveGraph();
    using var handler = new DeferredGitHubHandler();
    using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.local/") };
    using var composition = IssueBrowserStructure.Create(graph, new GitHubIssueSource(client), out var browser, out var theme);
    graph.Drain(); handler.ReplyJson(0); graph.Drain();
    var root = composition.Mount(composition.Root, theme, recipe(browser));
    graph.Drain();
    var dump = composition.Dump();
    var start = dump.LastIndexOf("element " + root.Id + " ", StringComparison.Ordinal);
    var semantic = Flatten(composition.SemanticSnapshot()!).Single(snapshot => snapshot.Identity.ElementId == root.Id);
    return (dump.Substring(start), SemanticEvidence(semantic));
}

static string SemanticEvidence(SemanticSnapshot snapshot) => snapshot.Role + "|" + snapshot.Name + "|" + snapshot.Value + "|" + snapshot.Enabled + "|" + snapshot.Focused + "|" + snapshot.Selected + "|" + snapshot.Actions + "[" + string.Join(",", snapshot.Children.Select(SemanticEvidence)) + "]";

static void AsyncBrowserStates()
{
    using var transport = new DeferredGitHubHandler();
    using var client = new HttpClient(transport) { BaseAddress = new Uri("https://api.github.local/") };
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(graph, new GitHubIssueSource(client), out var browser, out _);
    graph.Drain();

    Assert(transport.Requests.Count == 1 && transport.Requests[0] == "GET /repos/RichiCoder1/lucent/issues?state=all&per_page=100|application/vnd.github+json|Lucent-IssueBrowser/0.1", "GitHub adapter request contract changed or used a real transport.");
    Assert(browser.IsLoading && !browser.IsStale && browser.Issues.Count == 0 && browser.Error is null && Flatten(composition.SemanticSnapshot()!).Any(node => node.Role == SemanticRole.Status && node.Name == "Loading issues"), "Initial loading state was not externally visible.");
    using var initialRenderer = new SkiaSceneRenderer();
    var initial = SceneLayout.Project(composition, new(800, 500, 1), initialRenderer);
    Assert(composition.Input.SetScene(initial), "Initial issue-browser scene did not install.");
    var search = Flatten(composition.SemanticSnapshot()!).Single(node => node.Role == SemanticRole.TextField && node.Name == "Search issues");
    Assert(composition.Input.FocusSemantic(new(search.Identity.CompositionEpoch, search.Identity.ElementId)), "Initial loading scene rejected Search focus.");

    transport.ReplyJson(0); graph.Drain();
    using var renderer = new SkiaSceneRenderer();
    var scene = SceneLayout.Project(composition, new(800, 500, 1), renderer);
    Assert(composition.Input.SetScene(scene) && !browser.IsLoading && !browser.IsStale && browser.Error is null && browser.Issues.Count == 10_000 && browser.VisibleIssues.Count == 10_000 &&
        Flatten(composition.SemanticSnapshot()!).Count(node => node.Role == SemanticRole.ListItem) <= 7, "Successful response did not build a bounded virtual issue browser list.");

    browser.Search = "native"; graph.Drain();
    Assert(browser.VisibleIssues.Count > 1 && browser.VisibleIssues.All(issue => (issue.Title + " " + issue.Labels + " " + issue.Body).Contains("native", StringComparison.OrdinalIgnoreCase)), "Search result content was not deterministic.");
    browser.Status = "closed"; graph.Drain();
    Assert(browser.VisibleIssues.All(issue => issue.Status == "closed"), "Status filter did not compose with search.");
    browser.Search = ""; browser.Status = "open"; browser.Assignee = "marta"; graph.Drain();
    Assert(browser.VisibleIssues.Count > 1 && browser.VisibleIssues.All(issue => issue.Status == "open" && issue.Assignee == "marta"), "Status and assignee filters did not return deterministic results.");
    browser.Assignee = "all"; browser.Status = "all"; graph.Drain();

    scene = SceneLayout.Project(composition, new(800, 500, 1), renderer);
    Assert(composition.Input.SetScene(scene), "Filtered virtual list did not install.");
    var viewport = Flatten(composition.SemanticSnapshot()!).Single(node => node.Name == "Issues" && node.Actions.HasFlag(SemanticAction.Scroll));
    Assert(composition.Input.ScrollSemantic(new(viewport.Identity.CompositionEpoch, viewport.Identity.ElementId), new(SemanticCommandKind.Scroll, Endpoint: SemanticScrollEndpoint.End)), "Virtual list could not scroll to its end.");
    graph.Drain();
    scene = SceneLayout.Project(composition, new(800, 500, 1), renderer);
    Assert(composition.Input.SetScene(scene), "End virtual list scene did not install.");
    var row = Flatten(composition.SemanticSnapshot()!).Single(node => node.Role == SemanticRole.ListItem && node.Name.StartsWith("#1 ", StringComparison.Ordinal));
    Assert(composition.ExecuteSemanticCommand(row.Identity, new(SemanticCommandKind.Select)) == SemanticCommandResult.Applied, "Issue row semantic selection was rejected.");
    graph.Drain();
    Assert(browser.SelectedIssue?.Number == 1 && composition.Dump().Contains("issue-browser.details", StringComparison.Ordinal), "Selection did not expose issue details.");

    browser.Retry(); graph.Drain();
    Assert(transport.Requests.Count == 2 && browser.IsLoading && browser.IsStale && browser.Issues.Count == 10_000, "Retry did not retain stale data while loading.");
    transport.ReplyStatus(1, HttpStatusCode.ServiceUnavailable); graph.Drain();
    Assert(!browser.IsLoading && browser.IsStale == false && browser.Error is not null && browser.Issues.Count == 10_000 && composition.Dump().Contains("issue-browser.error", StringComparison.Ordinal), "Failed retry did not retain stale data and expose error.");

    browser.Retry(); graph.Drain();
    Assert(transport.Requests.Count == 3 && browser.IsLoading && browser.IsStale && browser.Error is null, "Error retry did not return to stale loading state.");
    browser.Retry(); graph.Drain();
    Assert(transport.Requests.Count == 4 && transport.Cancellations >= 1 && browser.IsLoading && browser.Issues.Count == 10_000, "Replacement request did not cancel prior work.");
    transport.ReplyJson(2); graph.Drain();
    Assert(browser.IsLoading && browser.Issues.Count == 10_000, "Ignored-cancellation response committed out of order.");
    transport.ReplyJson(3); graph.Drain();
    Assert(!browser.IsLoading && browser.Error is null && browser.Issues.Count == 10_000 && browser.SelectedIssue?.Number == 1, "Latest retry response did not commit deterministically.");
}

static void DisposedRequestCannotCommit()
{
    using var transport = new DeferredGitHubHandler();
    using var client = new HttpClient(transport) { BaseAddress = new Uri("https://api.github.local/") };
    var graph = new ReactiveGraph();
    var composition = IssueBrowserStructure.Create(graph, new GitHubIssueSource(client), out _, out _);
    graph.Drain();
    Assert(transport.Requests.Count == 1, "Disposed-request proof did not start its local request.");
    composition.Dispose();
    transport.ReplyJson(0); graph.Drain();
    Assert(transport.Cancellations == 1 && !graph.Dump().Contains("issue-browser", StringComparison.Ordinal), "Disposed request retained or committed browser state.");
}

static void DensityRestyle()
{
    using var transport = new DeferredGitHubHandler();
    using var client = new HttpClient(transport) { BaseAddress = new Uri("https://api.github.local/") };
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(graph, new GitHubIssueSource(client), out var browser, out var theme);
    graph.Drain(); transport.ReplyJson(0); graph.Drain();
    using var renderer = new SkiaSceneRenderer();
    var viewport = new LayoutViewport(800, 500, 1);
    var scene = SceneLayout.Project(composition, viewport, renderer);
    Assert(composition.Input.SetScene(scene), "Density baseline scene did not install.");
    var scroll = Flatten(composition.SemanticSnapshot()!).Single(node => node.Name == "Issues" && node.Actions.HasFlag(SemanticAction.Scroll));
    Assert(composition.Input.ScrollSemantic(new(scroll.Identity.CompositionEpoch, scroll.Identity.ElementId), new(SemanticCommandKind.Scroll, Vertical: 1507)), "Density proof could not reach a mid-list offset.");
    graph.Drain(); scene = SceneLayout.Project(composition, viewport, renderer);
    Assert(composition.Input.SetScene(scene), "Density mid-list scene did not install.");
    var comfortable = TopRow(scene, composition, "density comfortable");
    Assert(composition.ExecuteSemanticCommand(comfortable.Semantic.Identity, new(SemanticCommandKind.Select)) == SemanticCommandResult.Applied && composition.Input.FocusSemantic(new(comfortable.Semantic.Identity.CompositionEpoch, comfortable.Semantic.Identity.ElementId)), "Density proof could not select and focus its top row.");
    graph.Drain(); scene = SceneLayout.Project(composition, viewport, renderer);
    Assert(composition.Input.SetScene(scene), "Density focused scene did not install.");
    comfortable = TopRow(scene, composition, "density focused comfortable");
    var focused = composition.Input.FocusedElement;
    var density = Flatten(composition.SemanticSnapshot()!).Single(node => node.Role == SemanticRole.Button && node.Name == "Density: Comfortable/Compact");
    Assert(composition.ExecuteSemanticCommand(density.Identity, new(SemanticCommandKind.Invoke)) == SemanticCommandResult.Applied, "Keyboard/UIA density button rejected Invoke.");
    graph.Drain(); scene = SceneLayout.Project(composition, viewport, renderer);
    Assert(composition.Input.SetScene(scene), "Compact density scene did not install.");
    var compact = TopRow(scene, composition, "density compact");
    Assert(compact.Number == comfortable.Number && MathF.Abs(compact.Relative - comfortable.Relative) < .001f && compact.Height == 22f &&
        composition.Input.FocusedElement == focused && compact.Semantic.Identity == comfortable.Semantic.Identity && composition.IsCurrent(comfortable.Semantic.Identity), "Compact density lost its scroll anchor, focus, UIA identity, or typed row restyle: comfortable=" + comfortable + " compact=" + compact + " focused=" + focused + "/" + composition.Input.FocusedElement + ".");
    Assert(Flatten(composition.SemanticSnapshot()!).Count(node => node.Role == SemanticRole.ListItem) <= 9 && scene.Boxes.Single(box => box.Identity.ElementId == compact.Semantic.Identity.ElementId).Text!.Runs.Single().FontSize == 12f, "Compact density exceeded its bounded realization or retained comfortable typography.");
    Assert(composition.ExecuteSemanticCommand(density.Identity, new(SemanticCommandKind.Invoke)) == SemanticCommandResult.Applied, "Compact density button identity was stale.");
    graph.Drain(); scene = SceneLayout.Project(composition, viewport, renderer);
    Assert(composition.Input.SetScene(scene), "Restored comfortable density scene did not install.");
    var restored = TopRow(scene, composition, "density restored comfortable");
    Assert(restored.Number == comfortable.Number && MathF.Abs(restored.Relative - comfortable.Relative) < .001f && restored.Height == 30f && restored.Semantic.Identity == comfortable.Semantic.Identity && composition.Input.FocusedElement == focused,
        "Comfortable restoration changed the retained row, offset, focus, or UIA identity.");
    foreach (var appearance in new[] { new ThemeAppearance(ThemeColorScheme.Light, ThemeContrast.Normal), new ThemeAppearance(ThemeColorScheme.Dark, ThemeContrast.Normal), new ThemeAppearance(ThemeColorScheme.Light, ThemeContrast.High) })
    {
        theme.Appearance = appearance; graph.Drain();
        scene = SceneLayout.Project(composition, viewport, renderer);
        Assert(composition.Input.SetScene(scene), "Density style did not remain installable for " + appearance + ".");
    }
}

static void OptimisticStatusMutations()
{
    using var transport = new DeferredGitHubHandler();
    using var client = new HttpClient(transport) { BaseAddress = new Uri("https://api.github.local/") };
    var saves = new DeferredStatusSource();
    var graph = new ReactiveGraph();
    var composition = IssueBrowserStructure.Create(graph, new GitHubIssueSource(client), saves, out var browser, out _);
    graph.Drain(); transport.ReplyJson(0); graph.Drain();
    Assert(new FixtureIssueStatusSource().SaveAsync(3, "closed", default).Result is IssueStatusSaveOutcome.Rejected && new FixtureIssueStatusSource().SaveAsync(4, "closed", default).Result is IssueStatusSaveOutcome.TransientFailure && new FixtureIssueStatusSource().SaveAsync(5, "closed", default).Result is IssueStatusSaveOutcome.Saved,
        "The ordinary local status source no longer exposes deterministic rejection, transient, and success paths.");
    browser.Select(10_000); browser.ToggleSelectedStatus(); graph.Drain();
    Assert(browser.SelectedIssue?.Status == "closed" && saves.Requests.Count == 1 && !browser.CanRetrySelected && !HasRetry(composition), "Status save was not optimistic or exposed Retry before a transient result.");
    browser.Search = "native"; browser.Select(9_999); browser.ToggleSelectedStatus(); graph.Drain();
    Assert(saves.Requests.Count == 2 && saves.Cancellations == 0, "Filtering, selection, or an unrelated issue cancelled an application save.");
    browser.Search = ""; browser.Select(10_000); browser.ToggleSelectedStatus(); graph.Drain();
    Assert(saves.Requests.Count == 3 && saves.Cancellations >= 1 && browser.SelectedIssue?.Status == "open", "A newer per-issue generation did not replace the optimistic status.");
    saves.Reply(0, new IssueStatusSaveOutcome.Saved()); graph.Drain();
    Assert(browser.SelectedIssue?.Status == "open", "Stale save completion committed over the newest issue generation.");
    saves.Reply(2, new IssueStatusSaveOutcome.Rejected("policy")); graph.Drain();
    Assert(browser.SelectedIssue?.Status == "closed" && browser.SelectedMutationMessage == "Rejected: policy" && !browser.CanRetrySelected && !HasRetry(composition), "Rejected save did not roll back with its reason or hid Retry.");
    browser.Select(9_999); saves.Reply(1, new IssueStatusSaveOutcome.TransientFailure("offline")); graph.Drain();
    Assert(browser.SelectedMutationMessage == "Not synced: offline" && browser.SelectedIssue is { } optimistic && optimistic.Status != IssueFixture.Issues.First(issue => issue.Number == optimistic.Number).Status && browser.CanRetrySelected && HasRetry(composition), "Transient save did not retain the optimistic status and expose Retry.");
    browser.RetrySelected(); graph.Drain();
    Assert(saves.Requests.Count == 4 && !browser.CanRetrySelected && !HasRetry(composition), "Manual retry did not start exactly one save and hide Retry while pending.");
    saves.Reply(3, new IssueStatusSaveOutcome.Saved()); graph.Drain();
    Assert(browser.SelectedMutationMessage is null && !browser.CanRetrySelected && !HasRetry(composition), "Successful retry retained a transient failure message or Retry action.");
    browser.ToggleSelectedStatus(); graph.Drain(); composition.Dispose();
    Assert(saves.Cancellations >= 2 && !graph.Dump().Contains("issue-browser.mutation", StringComparison.Ordinal), "Application disposal did not cancel and release outstanding issue saves.");
}

static void SourceExceptionsBecomeTransientFailures()
{
    using var transport = new DeferredGitHubHandler();
    using var client = new HttpClient(transport) { BaseAddress = new Uri("https://api.github.local/") };
    var saves = new DeferredStatusSource();
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(graph, new GitHubIssueSource(client), saves, out var browser, out _);
    graph.Drain(); transport.ReplyJson(0); graph.Drain();
    browser.Select(10_000); browser.ToggleSelectedStatus(); graph.Drain(); saves.Fail(0, new InvalidOperationException("first fault")); graph.Drain();
    Assert(browser.SelectedIssue?.Status == "closed" && browser.SelectedMutationMessage == "Not synced: Unexpected save failure: first fault" && browser.CanRetrySelected,
        "A first source exception did not become a retryable transient failure while retaining optimism.");
    browser.RetrySelected(); graph.Drain(); saves.Reply(1, new IssueStatusSaveOutcome.Saved()); graph.Drain();
    Assert(browser.SelectedIssue?.Status == "closed" && browser.SelectedMutationMessage is null && !browser.CanRetrySelected,
        "A successful retry did not clear its transient state.");
    browser.ToggleSelectedStatus(); graph.Drain(); saves.Fail(2, new InvalidOperationException(new string('x', 200))); graph.Drain();
    Assert(browser.SelectedIssue?.Status == "open" && browser.SelectedMutationMessage is { } message && message.StartsWith("Not synced: Unexpected save failure: ", StringComparison.Ordinal) && message.Length <= 200 && browser.CanRetrySelected,
        "A fault after a saved value reused the old value or exposed an unbounded reason.");
}

static void VisualSurface()
{
    using var transport = new DeferredGitHubHandler();
    using var client = new HttpClient(transport) { BaseAddress = new Uri("https://api.github.local/") };
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(graph, new GitHubIssueSource(client), out _, out var theme);
    graph.Drain(); transport.ReplyJson(0); graph.Drain();
    using var renderer = new SkiaSceneRenderer();
    var light = SceneLayout.Project(composition, new(800, 500, 1.25f), renderer);
    var router = composition.Input;
    Assert(router.SetScene(light) && router.MoveFocus(FocusTraversalDirection.Next) && router.FocusedElement is not null, "First keyboard focus target was unavailable.");
    var focused = router.FocusedElement!.Value;
    var bounds = light.Boxes.Single(box => box.Identity == focused).Bounds;
    Assert(bounds.X <= 20 && bounds.Y <= 35 && bounds.X + bounds.Width > 20 && bounds.Y + bounds.Height > 35, "First keyboard focus target no longer covers the published smoke coordinate.");
        Assert(SceneLayout.Project(composition, new(800, 500, 1.25f), renderer).Dump().Contains("brush=solid(#FFFF00FF)", StringComparison.Ordinal), "Keyboard focus did not produce a visible focus paint.");
    theme.Appearance = new(ThemeColorScheme.Dark, ThemeContrast.Normal); graph.Drain();
    var dark = SceneLayout.Project(composition, new(800, 500, 1.25f), renderer);
    theme.Appearance = new(ThemeColorScheme.Light, ThemeContrast.High); graph.Drain();
    var high = SceneLayout.Project(composition, new(800, 500, 1.25f), renderer);
        Assert(light.Dump().Contains("brush=solid(#F8FAFCFF)", StringComparison.Ordinal) && dark.Dump().Contains("brush=solid(#0F172AFF)", StringComparison.Ordinal) && dark.Dump().Contains("brush=solid(#FACC15FF)", StringComparison.Ordinal) && high.Dump().Contains("brush=solid(#000000FF)", StringComparison.Ordinal) && !light.Dump().Contains("Native IME", StringComparison.Ordinal), "Issue Browser appearance or diagnostic-safe retained scene regressed.");
    using var bitmap = new SKBitmap(1000, 625, SKColorType.Rgba8888, SKAlphaType.Premul);
    using (var canvas = new SKCanvas(bitmap)) { canvas.Clear(SKColors.Transparent); renderer.Render(light, canvas); }
    Assert(Enumerable.Range(0, bitmap.Width).Any(x => bitmap.GetPixel(x, 0).Alpha != 0), "Issue Browser retained scene did not paint.");
}

static void StartupStaysFrameworkOwned()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        var app = Path.Combine(directory.FullName, "apps", "Lucent.IssueBrowser");
        if (Directory.Exists(app))
        {
            var source = string.Join('\n', Directory.EnumerateFiles(app, "*.cs").Select(File.ReadAllText));
            Assert(!source.Contains(".Drain(", StringComparison.Ordinal) && !source.Contains("test mode", StringComparison.OrdinalIgnoreCase), "Application startup owns framework synchronization or a test mode.");
            return;
        }
        directory = directory.Parent;
    }
    throw new InvalidOperationException("Could not locate Issue Browser startup source.");
}

static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot snapshot)
{
    yield return snapshot;
    foreach (var child in snapshot.Children)
        foreach (var node in Flatten(child)) yield return node;
}

static TopVisibleRow TopRow(RetainedScene scene, Composition composition, string phase)
{
    var scroll = Flatten(composition.SemanticSnapshot()!).Single(node => node.Name == "Issues" && node.Actions.HasFlag(SemanticAction.Scroll));
    var viewport = scene.Boxes.Single(box => box.Identity.ElementId == scroll.Identity.ElementId).Bounds;
    var candidates = Flatten(composition.SemanticSnapshot()!).Where(node => node.Role == SemanticRole.ListItem).Select(node => new { Semantic = node, Box = scene.Boxes.Single(box => box.Identity.ElementId == node.Identity.ElementId) })
        .Where(candidate => candidate.Box.Bounds.Y <= viewport.Y && candidate.Box.Bounds.Y + candidate.Box.Bounds.Height > viewport.Y).ToArray();
    var number = candidates.Length == 1 && int.TryParse(candidates[0].Semantic.Name.Split(' ')[0].TrimStart('#'), out var parsed) ? parsed : 0;
    Assert(number != 0, phase + " did not retain one top visible issue row.");
    return new(number, candidates[0].Box.Bounds.Y - viewport.Y, candidates[0].Box.Bounds.Height, candidates[0].Semantic);
}

static bool HasRetry(Composition composition) => Flatten(composition.SemanticSnapshot()!).Any(node => node.Role == SemanticRole.Button && node.Name == "Retry");

static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

static void Assert(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

sealed class DeferredGitHubHandler : HttpMessageHandler
{
    private readonly List<TaskCompletionSource<HttpResponseMessage>> _responses = [];
    public List<string> Requests { get; } = [];
    public int Cancellations { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.Method + " " + request.RequestUri!.PathAndQuery + "|" + string.Join(',', request.Headers.Accept.Select(header => header.MediaType)) + "|" + string.Join(' ', request.Headers.UserAgent.Select(header => header.Product?.Name + "/" + header.Product?.Version)));
        cancellationToken.Register(() => Cancellations++);
        var response = new TaskCompletionSource<HttpResponseMessage>();
        _responses.Add(response);
        return response.Task;
    }

    public void ReplyJson(int index) => Task.Run(() => _responses[index].SetResult(Response(HttpStatusCode.OK, IssueFixture.Json))).GetAwaiter().GetResult();
    public void ReplyStatus(int index, HttpStatusCode status) => Task.Run(() => _responses[index].SetResult(Response(status, "{}"))).GetAwaiter().GetResult();

    private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

sealed class DeferredStatusSource : IIssueStatusSource
{
    private readonly List<TaskCompletionSource<IssueStatusSaveOutcome>> _responses = [];
    public List<(int Number, string Status)> Requests { get; } = [];
    public int Cancellations { get; private set; }

    public Task<IssueStatusSaveOutcome> SaveAsync(int issueNumber, string status, CancellationToken cancellationToken)
    {
        Requests.Add((issueNumber, status));
        cancellationToken.Register(() => Cancellations++);
        var response = new TaskCompletionSource<IssueStatusSaveOutcome>();
        _responses.Add(response);
        return response.Task;
    }

    public void Reply(int index, IssueStatusSaveOutcome outcome) => Task.Run(() => _responses[index].SetResult(outcome)).GetAwaiter().GetResult();
    public void Fail(int index, Exception error) => Task.Run(() => _responses[index].SetException(error)).GetAwaiter().GetResult();
}

readonly record struct TopVisibleRow(int Number, float Relative, float Height, SemanticSnapshot Semantic);
