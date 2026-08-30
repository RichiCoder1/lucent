using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucent.Core;

public sealed record BrowserIssue(int Number, string Title, string Status, string Assignee, string Labels, string Updated, string Body);

/// <summary>Frozen, local issue data used by the reference application and its deterministic transport contract.</summary>
public static class IssueFixture
{
    public const string Seed = "lucent-issue-browser-v1";
    public const string Sha256 = "5021c89ddf0911e3f00445a816be039bd3626c10111f1184f70796803a0f76fe";
    public const int TotalCount = 12;
    public const int OpenCount = 8;
    public const int ClosedCount = 4;
    public static IReadOnlyList<BrowserIssue> Issues { get; } =
    [
        new(36, "Native IME composition must cancel cleanly", "open", "marta", "input, accessibility", "2025-01-16", "Keep preedit ownership inside the input adapter."),
        new(35, "Wire retained scene focus invalidation", "closed", "devin", "render", "2025-01-15", "Refresh focus paint without rebuilding input ownership."),
        new(34, "Add high contrast status tokens", "open", "marta", "accessibility", "2025-01-14", "Make loading and error status visible in all appearance modes."),
        new(33, "Validate NativeAOT publishing surface", "open", "joel", "build", "2025-01-13", "Keep the reference application trim-safe."),
        new(32, "Route pointer capture across scroll bounds", "closed", "devin", "input", "2025-01-12", "Release capture when a row departs the retained scene."),
        new(31, "Expose semantic snapshots for diagnostics", "open", "marta", "accessibility", "2025-01-11", "Diagnostics must explain the current semantic tree."),
        new(30, "Make async issue source stale-safe", "open", "joel", "reactive", "2025-01-10", "Only the latest response may update browser state."),
        new(29, "Implement deterministic issue fixture", "closed", "marta", "test", "2025-01-09", "Freeze realistic issue data for offline verification."),
        new(28, "Handle scale transition resource lifetime", "open", "devin", "render", "2025-01-08", "Dispose superseded backing resources after presentation."),
        new(27, "Clarify control composition ownership", "closed", "joel", "docs", "2025-01-07", "Document content and behavior ownership boundaries."),
        new(26, "Stabilize keyboard list selection", "open", "marta", "input", "2025-01-06", "Keep selection coherent through keyed list updates."),
        new(25, "Add CI artifact verification", "open", "joel", "build", "2025-01-05", "Verify the published native application contents.")
    ];

    public static string Json => "[" + string.Join(',', Issues.Select(issue =>
        $"{{\"number\":{issue.Number},\"title\":\"{issue.Title}\",\"state\":\"{issue.Status}\",\"assignee\":{{\"login\":\"{issue.Assignee}\"}},\"labels\":[{string.Join(',', issue.Labels.Split(", ", StringSplitOptions.None).Select(label => $"{{\"name\":\"{label}\"}}"))}],\"updated_at\":\"{issue.Updated}T00:00:00Z\",\"body\":\"{issue.Body}\"}}")) + "]";

    public static void AssertIntegrity()
    {
        var canonical = Seed + "\n" + string.Join('\n', Issues.Select(issue => string.Join('|', issue.Number, issue.Title, issue.Status, issue.Assignee, issue.Labels, issue.Updated, issue.Body)));
        var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        if (Issues.Count != TotalCount || Issues.Count(issue => issue.Status == "open") != OpenCount || Issues.Count(issue => issue.Status == "closed") != ClosedCount || actual != Sha256)
            throw new InvalidOperationException("The frozen issue fixture identity or counts changed.");
    }
}

/// <summary>Small GitHub REST shape adapter. Its caller supplies the transport, so production remains offline by default.</summary>
public sealed class GitHubIssueSource(HttpClient client)
{
    public async Task<IReadOnlyList<BrowserIssue>> LoadAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/repos/RichiCoder1/lucent/issues?state=all&per_page=100");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("Lucent-IssueBrowser/0.1");
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("GitHub issues response must be an array.");
        return document.RootElement.EnumerateArray().Where(issue => !issue.TryGetProperty("pull_request", out _)).Select(Read).OrderByDescending(issue => issue.Number).ToArray();
    }

    private static BrowserIssue Read(JsonElement issue)
    {
        var assignee = issue.TryGetProperty("assignee", out var owner) && owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty("login", out var login) ? login.GetString() ?? "unassigned" : "unassigned";
        var labels = issue.TryGetProperty("labels", out var labelsValue) && labelsValue.ValueKind == JsonValueKind.Array
            ? string.Join(", ", labelsValue.EnumerateArray().Select(label => label.TryGetProperty("name", out var name) ? name.GetString() : null).Where(name => !string.IsNullOrWhiteSpace(name))) : "none";
        return new(issue.GetProperty("number").GetInt32(), issue.GetProperty("title").GetString() ?? "Untitled", issue.GetProperty("state").GetString() ?? "open", assignee, labels, issue.GetProperty("updated_at").GetString() ?? "", issue.GetProperty("body").GetString() ?? "");
    }
}

/// <summary>Application state backed by the framework-owned latest-generation async value.</summary>
public sealed class IssueBrowserState
{
    private readonly Signal<int> _retry;
    private readonly Signal<string> _search;
    private readonly Signal<string> _status;
    private readonly Signal<string> _assignee;
    private readonly Signal<int?> _selectedNumber;
    private readonly AsyncValue<IReadOnlyList<BrowserIssue>> _issues;
    private readonly Derived<IReadOnlyList<BrowserIssue>> _visible;

    public IssueBrowserState(ReactiveScope scope, GitHubIssueSource source)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(source);
        _retry = scope.Signal(0, "issue-browser.retry");
        _search = scope.Signal("", "issue-browser.search");
        _status = scope.Signal("all", "issue-browser.status");
        _assignee = scope.Signal("all", "issue-browser.assignee");
        _selectedNumber = scope.Signal<int?>(null, "issue-browser.selection");
        _issues = scope.Async(async token => { _ = _retry.Value; return await source.LoadAsync(token).ConfigureAwait(false); }, "issue-browser.issues");
        _visible = scope.Derived<IReadOnlyList<BrowserIssue>>(() => Issues.Where(Matches).ToArray(), "issue-browser.visible-issues");
    }

    public IReadOnlyList<BrowserIssue> Issues => _issues.Value ?? Array.Empty<BrowserIssue>();
    public IReadOnlyList<BrowserIssue> VisibleIssues => _visible.Value;
    public bool IsLoading => _issues.IsPending;
    public bool IsStale => _issues.HasValue && _issues.IsPending;
    public string? Error => _issues.Error?.Message;
    public BrowserIssue? SelectedIssue => Issues.FirstOrDefault(issue => issue.Number == _selectedNumber.Value);
    public string Search { get => _search.Value; set => _search.Value = value.Trim(); }
    public string Status { get => _status.Value; set => _status.Value = Normalize(value, "all"); }
    public string Assignee { get => _assignee.Value; set => _assignee.Value = Normalize(value, "all"); }

    public void Retry() => _retry.Value++;
    public void Select(int number) => _selectedNumber.Value = Issues.Any(issue => issue.Number == number) ? number : null;

    private bool Matches(BrowserIssue issue) => (Status == "all" || issue.Status == Status) && (Assignee == "all" || issue.Assignee == Assignee) &&
        (string.IsNullOrWhiteSpace(Search) || (issue.Title + " " + issue.Labels + " " + issue.Body).Contains(Search, StringComparison.OrdinalIgnoreCase));
    private static string Normalize(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
}

public static class IssueBrowserStructure
{
    private static readonly Token<uint> PageSurface = new("page-surface", 0xfff8fafcU);
    private static readonly Token<uint> PageForeground = new("page-foreground", 0xff0f172aU);
    private static readonly Token<uint> HeaderSurface = new("header-surface", 0xffe2e8f0U);
    private static readonly Token<uint> RowSurface = new("row-surface", 0xffffffffU);
    private static readonly Token<uint> FocusSurface = new("focus-surface", 0xffffff00U);
    private static readonly Token<uint> FocusForeground = new("focus-foreground", 0xff0f172aU);
    private static readonly Theme LightTheme = Palette(ControlThemes.Light, 0xfff8fafcU, 0xff0f172aU, 0xffe2e8f0U, 0xffffffffU, 0xffffff00U, 0xff0f172aU);
    private static readonly Theme DarkTheme = Palette(ControlThemes.Dark, 0xff0f172aU, 0xfff8fafcU, 0xff1e293bU, 0xff111827U, 0xfffacc15U, 0xff0f172aU);
    private static readonly Theme HighContrastTheme = Palette(ControlThemes.HighContrast, 0xff000000U, 0xffffffffU, 0xff000000U, 0xff000000U, 0xffffff00U, 0xff000000U);

    public static Composition Create(ReactiveGraph graph) => Create(graph, out _);
    public static Composition Create(ReactiveGraph graph, out ThemeContext theme)
    {
        var handler = new FixtureHttpHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.local/") };
        return Create(graph, new GitHubIssueSource(client), client, out _, out theme);
    }

    public static Composition Create(ReactiveGraph graph, GitHubIssueSource source, out IssueBrowserState state, out ThemeContext theme) => Create(graph, source, null, out state, out theme);

    private static Composition Create(ReactiveGraph graph, GitHubIssueSource source, IDisposable? transport, out IssueBrowserState state, out ThemeContext theme)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(source);
        IssueFixture.AssertIntegrity();
        var composition = new Composition(graph, "issue-browser");
        if (transport is not null) composition.Root.Scope.Own(transport);
        var browser = new IssueBrowserState(composition.Root.Scope, source);
        var themeContext = new ThemeContext(composition.Root.Scope, LightTheme);
        theme = themeContext;
        _ = composition.Root.Scope.Effect(() => themeContext.Theme = themeContext.Appearance.Contrast == ThemeContrast.High ? HighContrastTheme : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark ? DarkTheme : LightTheme, "issue-browser-appearance");
        Controls.Column(composition.Root, themeContext, "Issue Browser", Style.Empty.Set(SceneProperties.Fill, PageSurface).Set(SceneProperties.Foreground, PageForeground).Set(Arrangement.Clip, true));

        var header = composition.Child(composition.Root, "issue-browser.header");
        Controls.Panel(header, themeContext, "Issue Browser header", Style.Empty.Set(Arrangement.Height, 56f).Set(Arrangement.Width, 800f).Set(SceneProperties.Fill, HeaderSurface));
        var title = composition.Child(header, "issue-browser.title");
        Controls.Text(title, themeContext, "Issues", Style.Empty.Set(Arrangement.Height, 24f).Set(SceneProperties.FontSize, 18f));
        var filters = composition.Child(header, "issue-browser.filters");
        Controls.Row(filters, themeContext, "Issue filters", Style.Empty.Set(Arrangement.Width, 800f).Set(Arrangement.Height, 28f).Set(Arrangement.Spacing, 8f));
        BindFilter(composition.Child(filters, "issue-browser.search"), themeContext, "Search issues", value => browser.Search = value);
        BindFilter(composition.Child(filters, "issue-browser.status"), themeContext, "Status: all, open, closed", value => browser.Status = value);
        BindFilter(composition.Child(filters, "issue-browser.assignee"), themeContext, "Assignee: all, marta, devin, joel", value => browser.Assignee = value);

        _ = composition.When(composition.Root, "issue-browser.loading-region", () => browser.IsLoading,
            Controls.Recipe("issue-browser.loading", (context, element) => Controls.Loading(element, themeContext, browser.IsStale ? "Refreshing issues" : "Loading issues", Style.Empty.Set(Arrangement.Height, 28f))));
        _ = composition.When(composition.Root, "issue-browser.error-region", () => browser.Error is not null,
            Controls.Recipe("issue-browser.error", (context, element) =>
            {
                Controls.Error(element, themeContext, browser.Error!, Style.Empty.Set(Arrangement.Axis, LayoutAxis.Column));
                Controls.Button(context.Child(element, "issue-browser.retry"), themeContext, "Retry", browser.Retry, Style.Empty.Set(Arrangement.Height, 30f));
            }));

        var viewport = composition.Child(composition.Root, "issue-browser.scroll-viewport");
        Controls.ScrollViewport(viewport, themeContext, "Issues", style: Style.Empty.Set(Arrangement.Width, 800f).Set(Arrangement.Height, 90f));
        var list = composition.ForEach(viewport, "issue-browser.issue-list", () => browser.VisibleIssues, issue => issue.Number, (issue, context) =>
        {
            var row = context.Element("issue-browser.issue-row");
            Controls.Selectable(row, themeContext, $"#{issue.Number} {issue.Title} — {issue.Status} · {issue.Assignee}", () => browser.Select(issue.Number), Style.Empty.Set(Arrangement.Height, 30f).Set(Arrangement.Width, 800f).Set(SceneProperties.Fill, RowSurface)
                .When(VariantState.FocusVisible, Style.Empty.Set(SceneProperties.Fill, FocusSurface).Set(SceneProperties.Foreground, FocusForeground)));
            return row;
        });
        Controls.List(list.Region, themeContext, "Issues");
        _ = composition.When(composition.Root, "issue-browser.details-region", () => browser.SelectedIssue is not null,
            Controls.Recipe("issue-browser.details", (context, element) =>
            {
                var issue = browser.SelectedIssue!;
                Controls.Panel(element, themeContext, "Issue details", Style.Empty.Set(Arrangement.Width, 800f));
                Controls.Text(context.Child(element, "issue-browser.details-title"), themeContext, $"#{issue.Number} {issue.Title}");
                Controls.Text(context.Child(element, "issue-browser.details-meta"), themeContext, $"{issue.Status} · {issue.Assignee} · {issue.Labels} · {issue.Updated}");
                Controls.Text(context.Child(element, "issue-browser.details-body"), themeContext, issue.Body);
            }));
        state = browser;
        return composition;
    }

    private static void BindFilter(Element element, ThemeContext theme, string name, Action<string> set)
    {
        var input = Controls.TextField(element, theme, name, style: Style.Empty.Set(Arrangement.Width, 250f).Set(Arrangement.Height, 24f));
        _ = element.Scope.Effect(() => set(input.Value), element.Name + ".binding");
    }

    private static Theme Palette(Theme controls, uint surface, uint foreground, uint header, uint row, uint focus, uint focusForeground) => controls
        .Set(PageSurface, surface).Set(PageForeground, foreground).Set(HeaderSurface, header).Set(RowSurface, row).Set(FocusSurface, focus).Set(FocusForeground, focusForeground);

    private sealed class FixtureHttpHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(IssueFixture.Json, Encoding.UTF8, "application/json") };
        }
    }
}
