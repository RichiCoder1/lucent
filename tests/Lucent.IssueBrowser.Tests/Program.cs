using System.Net;
using System.Net.Http;
using System.Text;
using Lucent.Core;
using Lucent.Renderer.Skia;
using SkiaSharp;

try
{
    FixtureIdentity();
    AsyncBrowserStates();
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

static void AsyncBrowserStates()
{
    using var transport = new DeferredGitHubHandler();
    using var client = new HttpClient(transport) { BaseAddress = new Uri("https://api.github.local/") };
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(graph, new GitHubIssueSource(client), out var browser, out _);
    graph.Drain();

    Assert(transport.Requests.Count == 1 && transport.Requests[0] == "GET /repos/RichiCoder1/lucent/issues?state=all&per_page=100|application/vnd.github+json|Lucent-IssueBrowser/0.1", "GitHub adapter request contract changed or used a real transport.");
    Assert(browser.IsLoading && !browser.IsStale && browser.Issues.Count == 0 && browser.Error is null && Flatten(composition.SemanticSnapshot()!).Any(node => node.Role == SemanticRole.Status && node.Name == "Loading issues"), "Initial loading state was not externally visible.");

    transport.ReplyJson(0); graph.Drain();
    Assert(!browser.IsLoading && !browser.IsStale && browser.Error is null && browser.Issues.Count == 12 && browser.VisibleIssues.Count == 12 &&
        Flatten(composition.SemanticSnapshot()!).Count(node => node.Role == SemanticRole.ListItem) == 12, "Successful response did not build the complete issue browser list.");

    browser.Search = "native"; graph.Drain();
    Assert(browser.VisibleIssues.Select(issue => issue.Number).SequenceEqual([36, 33, 25]), "Search result count/order was not deterministic.");
    browser.Status = "closed"; graph.Drain();
    Assert(browser.VisibleIssues.Count == 0, "Status filter did not compose with search.");
    browser.Search = ""; browser.Status = "open"; browser.Assignee = "marta"; graph.Drain();
    Assert(browser.VisibleIssues.Select(issue => issue.Number).SequenceEqual([36, 34, 31, 26]), "Status and assignee filters did not return the frozen result count.");
    browser.Assignee = "all"; graph.Drain();

    var row = Flatten(composition.SemanticSnapshot()!).Single(node => node.Role == SemanticRole.ListItem && node.Name.StartsWith("#30 ", StringComparison.Ordinal));
    Assert(composition.ExecuteSemanticCommand(row.Identity, new(SemanticCommandKind.Select)) == SemanticCommandResult.Applied, "Issue row semantic selection was rejected.");
    graph.Drain();
    Assert(browser.SelectedIssue?.Number == 30 && composition.Dump().Contains("issue-browser.details", StringComparison.Ordinal), "Selection did not expose issue details.");

    browser.Retry(); graph.Drain();
    Assert(transport.Requests.Count == 2 && browser.IsLoading && browser.IsStale && browser.Issues.Count == 12, "Retry did not retain stale data while loading.");
    transport.ReplyStatus(1, HttpStatusCode.ServiceUnavailable); graph.Drain();
    Assert(!browser.IsLoading && browser.IsStale == false && browser.Error is not null && browser.Issues.Count == 12 && composition.Dump().Contains("issue-browser.error", StringComparison.Ordinal), "Failed retry did not retain stale data and expose error.");

    browser.Retry(); graph.Drain();
    Assert(transport.Requests.Count == 3 && browser.IsLoading && browser.IsStale && browser.Error is null, "Error retry did not return to stale loading state.");
    browser.Retry(); graph.Drain();
    Assert(transport.Requests.Count == 4 && transport.Cancellations >= 1 && browser.IsLoading && browser.Issues.Count == 12, "Replacement request did not cancel prior work.");
    transport.ReplyJson(2); graph.Drain();
    Assert(browser.IsLoading && browser.Issues.Count == 12, "Ignored-cancellation response committed out of order.");
    transport.ReplyJson(3); graph.Drain();
    Assert(!browser.IsLoading && browser.Error is null && browser.Issues.Count == 12 && browser.SelectedIssue?.Number == 30, "Latest retry response did not commit deterministically.");
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
    Assert(SceneLayout.Project(composition, new(800, 500, 1.25f), renderer).Dump().Contains("color=0xffffff00", StringComparison.Ordinal), "Keyboard focus did not produce a visible focus paint.");
    theme.Appearance = new(ThemeColorScheme.Dark, ThemeContrast.Normal); graph.Drain();
    var dark = SceneLayout.Project(composition, new(800, 500, 1.25f), renderer);
    theme.Appearance = new(ThemeColorScheme.Light, ThemeContrast.High); graph.Drain();
    var high = SceneLayout.Project(composition, new(800, 500, 1.25f), renderer);
    Assert(light.Dump().Contains("color=0xfff8fafc", StringComparison.Ordinal) && dark.Dump().Contains("color=0xff0f172a", StringComparison.Ordinal) && dark.Dump().Contains("color=0xfffacc15", StringComparison.Ordinal) && high.Dump().Contains("color=0xff000000", StringComparison.Ordinal) && !light.Dump().Contains("Native IME", StringComparison.Ordinal), "Issue Browser appearance or diagnostic-safe retained scene regressed.");
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
