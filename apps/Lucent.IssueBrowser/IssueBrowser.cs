using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucent.Core;

namespace Lucent.IssueBrowser;

public sealed record BrowserIssue(int Number, string Title, string Status, string Assignee, string Labels, string Updated, string Body);

/// <summary>Frozen, local issue data used by the reference application and its deterministic transport contract.</summary>
public static class IssueFixture
{
    public const string Seed = "lucent-issue-browser-v1";
    public const string Sha256 = "9f4a8b3ce5df46f73a4a3c662345b4dcd479557f37cc7a599d5bb6472b8b4efa";
    public const int TotalCount = 10_000;
    public const int OpenCount = 6_667;
    public const int ClosedCount = 3_333;
    private static readonly BrowserIssue[] Seeds =
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
    public static IReadOnlyList<BrowserIssue> Issues { get; } = CreateIssues();

    public static string Json => "[" + string.Join(',', Issues.Select(issue =>
        $"{{\"number\":{issue.Number},\"title\":\"{issue.Title}\",\"state\":\"{issue.Status}\",\"assignee\":{{\"login\":\"{issue.Assignee}\"}},\"labels\":[{string.Join(',', issue.Labels.Split(", ", StringSplitOptions.None).Select(label => $"{{\"name\":\"{label}\"}}"))}],\"updated_at\":\"{issue.Updated}T00:00:00Z\",\"body\":\"{issue.Body}\"}}")) + "]";

    public static void AssertIntegrity()
    {
        var canonical = Seed + "\n" + string.Join('\n', Issues.Select(issue => string.Join('|', issue.Number, issue.Title, issue.Status, issue.Assignee, issue.Labels, issue.Updated, issue.Body)));
        var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        if (Issues.Count != TotalCount || Issues.Count(issue => issue.Status == "open") != OpenCount || Issues.Count(issue => issue.Status == "closed") != ClosedCount || actual != Sha256)
            throw new InvalidOperationException("The frozen issue fixture identity or counts changed.");
    }

    private static IReadOnlyList<BrowserIssue> CreateIssues()
    {
        var issues = new List<BrowserIssue>(TotalCount);
        var generated = 0;
        for (var number = TotalCount; number >= 37; number--)
        {
            var seed = Seeds[generated++ % Seeds.Length];
            issues.Add(seed with { Number = number, Title = seed.Title + " (issue #" + number + ")", Body = seed.Body + " Deterministic fixture issue #" + number + "." });
        }
        for (var number = 24; number >= 1; number--)
        {
            var seed = Seeds[generated++ % Seeds.Length];
            issues.Add(seed with { Number = number, Title = seed.Title + " (issue #" + number + ")", Body = seed.Body + " Deterministic fixture issue #" + number + "." });
        }
        issues.AddRange(Seeds);
        return issues.AsReadOnly();
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

public enum IssueDensity { Comfortable, Compact }

/// <summary>Application-owned save results; transport policy is deliberately not a Core concern.</summary>
public abstract record IssueStatusSaveOutcome
{
    public sealed record Saved : IssueStatusSaveOutcome;
    public sealed record Rejected(string Reason) : IssueStatusSaveOutcome;
    public sealed record TransientFailure(string Reason) : IssueStatusSaveOutcome;
}

public interface IIssueStatusSource
{
    Task<IssueStatusSaveOutcome> SaveAsync(int issueNumber, string status, CancellationToken cancellationToken);
}

/// <summary>Ordinary offline source: each deterministic branch keeps the reference app useful without test startup modes.</summary>
public sealed class FixtureIssueStatusSource : IIssueStatusSource
{
    private readonly HashSet<int> _transientFailures = [];

    public Task<IssueStatusSaveOutcome> SaveAsync(int issueNumber, string status, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IssueStatusSaveOutcome outcome = (issueNumber % 3) switch
        {
            0 => new IssueStatusSaveOutcome.Rejected("Fixture policy rejected this change."),
            1 when _transientFailures.Add(issueNumber) => new IssueStatusSaveOutcome.TransientFailure("Fixture source is temporarily unavailable."),
            _ => new IssueStatusSaveOutcome.Saved()
        };
        return Task.FromResult(outcome);
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
    private readonly Signal<IssueDensity> _density;
    private readonly Signal<IReadOnlyDictionary<int, string>> _statuses;
    private readonly Signal<IReadOnlyDictionary<int, string>> _messages;
    private readonly AsyncValue<IReadOnlyList<BrowserIssue>> _issues;
    private readonly Derived<IReadOnlyList<BrowserIssue>> _visible;
    private readonly ReactiveScope _scope;
    private readonly IIssueStatusSource _statusSource;
    private readonly Dictionary<int, IssueStatusMutation> _mutations = [];

    public IssueBrowserState(ReactiveScope scope, GitHubIssueSource source, IIssueStatusSource? statusSource = null)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(source);
        _scope = scope;
        _statusSource = statusSource ?? new FixtureIssueStatusSource();
        _retry = scope.Signal(0, "issue-browser.retry");
        _search = scope.Signal("", "issue-browser.search");
        _status = scope.Signal("all", "issue-browser.status");
        _assignee = scope.Signal("all", "issue-browser.assignee");
        _selectedNumber = scope.Signal<int?>(null, "issue-browser.selection");
        _density = scope.Signal(IssueDensity.Comfortable, "issue-browser.density");
        _statuses = scope.Signal<IReadOnlyDictionary<int, string>>(new Dictionary<int, string>(), "issue-browser.statuses");
        _messages = scope.Signal<IReadOnlyDictionary<int, string>>(new Dictionary<int, string>(), "issue-browser.mutation-messages");
        _issues = scope.Async(async token => { _ = _retry.Value; return await source.LoadAsync(token).ConfigureAwait(false); }, "issue-browser.issues");
        _visible = scope.Derived<IReadOnlyList<BrowserIssue>>(() => Issues.Where(Matches).ToArray(), "issue-browser.visible-issues");
    }

    public IReadOnlyList<BrowserIssue> Issues => (_issues.Value ?? Array.Empty<BrowserIssue>()).Select(issue => _statuses.Value.TryGetValue(issue.Number, out var status) ? issue with { Status = status } : issue).ToArray();
    public IReadOnlyList<BrowserIssue> VisibleIssues => _visible.Value;
    public bool IsLoading => _issues.IsPending;
    public bool IsStale => _issues.HasValue && _issues.IsPending;
    public string? Error => _issues.Error?.Message;
    public BrowserIssue? SelectedIssue => Issues.FirstOrDefault(issue => issue.Number == _selectedNumber.Value);
    public IssueDensity Density { get => _density.Value; set => _density.Value = value; }
    public string? SelectedMutationMessage => _selectedNumber.Value is { } number && _messages.Value.TryGetValue(number, out var message) ? message : null;
    public bool CanRetrySelected { get { _ = _messages.Value; return _selectedNumber.Value is { } number && _mutations.TryGetValue(number, out var mutation) && mutation.CanRetry; } }
    public bool IsSelected(int number) => _selectedNumber.Value == number;
    public string Search { get => _search.Value; set => _search.Value = value.Trim(); }
    public string Status { get => _status.Value; set => _status.Value = Normalize(value, "all"); }
    public string Assignee { get => _assignee.Value; set => _assignee.Value = Normalize(value, "all"); }

    public void SetSearch(string value) => Search = value;
    public void SetStatus(string value) => Status = value;
    public void SetAssignee(string value) => Assignee = value;

    public void Retry() => _retry.Value++;
    public void Select(int number) => _selectedNumber.Value = Issues.Any(issue => issue.Number == number) ? number : null;
    public void ToggleDensity() => Density = Density == IssueDensity.Comfortable ? IssueDensity.Compact : IssueDensity.Comfortable;
    public void ToggleSelectedStatus()
    {
        if (SelectedIssue is not { } issue) return;
        var next = issue.Status == "open" ? "closed" : "open";
        Set(_statuses, issue.Number, next);
        Set(_messages, issue.Number, null);
        Mutation(issue.Number).Start(issue.Status, next);
    }
    public void RetrySelected()
    {
        if (_selectedNumber.Value is { } number && _mutations.TryGetValue(number, out var mutation) && mutation.Retry()) Set(_messages, number, null);
    }

    private bool Matches(BrowserIssue issue) => (Status == "all" || issue.Status == Status) && (Assignee == "all" || issue.Assignee == Assignee) &&
        (string.IsNullOrWhiteSpace(Search) || (issue.Title + " " + issue.Labels + " " + issue.Body).Contains(Search, StringComparison.OrdinalIgnoreCase));
    private static string Normalize(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
    private IssueStatusMutation Mutation(int number) => _mutations.TryGetValue(number, out var mutation) ? mutation : _mutations[number] = new IssueStatusMutation(_scope, number, _statusSource, CompleteMutation);
    private void CompleteMutation(int number, string original, string requested, IssueStatusSaveOutcome outcome)
    {
        var status = requested;
        string? message = outcome switch
        {
            IssueStatusSaveOutcome.Saved => null,
            IssueStatusSaveOutcome.Rejected rejected => "Rejected: " + rejected.Reason,
            IssueStatusSaveOutcome.TransientFailure failure => "Not synced: " + failure.Reason,
            _ => throw new InvalidOperationException("Unknown issue status save outcome.")
        };
        if (outcome is IssueStatusSaveOutcome.Rejected) status = original;
        Set(_statuses, number, status);
        Set(_messages, number, message);
    }
    private static void Set(Signal<IReadOnlyDictionary<int, string>> values, int number, string? value)
    {
        var next = new Dictionary<int, string>(values.Value);
        if (value is null) next.Remove(number); else next[number] = value;
        values.Value = next;
    }

    private sealed class IssueStatusMutation
    {
        private readonly Signal<Request?> _request;
        private readonly AsyncValue<IssueStatusSaveOutcome> _save;
        private readonly Action<int, string, string, IssueStatusSaveOutcome> _complete;
        private long _generation;
        private long _completed;
        private Request? _lastTransient;

        public IssueStatusMutation(ReactiveScope scope, int number, IIssueStatusSource source, Action<int, string, string, IssueStatusSaveOutcome> complete)
        {
            _complete = complete;
            _request = scope.Signal<Request?>(null, "issue-browser.mutation." + number);
            _save = scope.Async(async token =>
            {
                var request = _request.Value ?? throw new InvalidOperationException("Issue mutation started without a request.");
                return await source.SaveAsync(number, request.Status, token).ConfigureAwait(false);
            }, "issue-browser.mutation-save." + number);
            _ = scope.Effect(() => Commit(number), "issue-browser.mutation-commit." + number);
        }

        public bool CanRetry => _lastTransient is not null;
        public void Start(string original, string status) { _lastTransient = null; _request.Value = new(++_generation, original, status); }
        public bool Retry()
        {
            if (_lastTransient is not { } request) return false;
            _lastTransient = null;
            _request.Value = request with { Generation = ++_generation };
            return true;
        }

        private void Commit(int number)
        {
            var request = _request.Value;
            if (request is null) return;
            var error = _save.Error;
            if (_save.IsPending || _save.IsCancelled || request.Generation == _completed) return;
            _completed = request.Generation;
            var outcome = error is null ? _save.Value ?? throw new InvalidOperationException("Issue mutation completed without an outcome.") : new IssueStatusSaveOutcome.TransientFailure(FailureReason(error));
            _lastTransient = outcome is IssueStatusSaveOutcome.TransientFailure ? request : null;
            _complete(number, request.Original, request.Status, outcome);
        }

        private static string FailureReason(Exception error)
        {
            var reason = error.GetBaseException().Message.Trim();
            if (string.IsNullOrWhiteSpace(reason)) reason = error.GetBaseException().GetType().Name;
            const int maximum = 160;
            return "Unexpected save failure: " + (reason.Length <= maximum ? reason : reason[..maximum] + "…");
        }

        private sealed record Request(long Generation, string Original, string Status);
    }
}

public static class IssueBrowserStructure
{
    internal static readonly Token<Brush> PageSurface = new("page-surface", Color.Parse("#f8fafc"));
    internal static readonly Token<Color> PageForeground = new("page-foreground", Color.Parse("#0f172a"));
    internal static readonly Token<Brush> HeaderSurface = new("header-surface", Color.Parse("#e2e8f0"));
    internal static readonly Token<Brush> RowSurface = new("row-surface", Color.Parse("#ffffff"));
    internal static readonly Token<Brush> FocusSurface = new("focus-surface", Color.Parse("#ffff00"));
    internal static readonly Token<Color> FocusForeground = new("focus-foreground", Color.Parse("#0f172a"));
    internal static readonly Token<float?> DensityHeaderHeight = new("issue-density-header-height", 84f);
    internal static readonly Token<float?> DensityFilterHeight = new("issue-density-filter-height", 28f);
    internal static readonly Token<float> DensitySpacing = new("issue-density-spacing", 8f);
    internal static readonly Token<float> DensityFontSize = new("issue-density-font-size", 14f);
    internal static readonly Token<float> DensityTitleFontSize = new("issue-density-title-font-size", 18f);

    public static Composition Create(ReactiveGraph graph) => Create(graph, out _);
    public static Composition Create(ReactiveGraph graph, out ThemeContext theme)
    {
        var handler = new FixtureHttpHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.local/") };
        return Create(graph, new GitHubIssueSource(client), new FixtureIssueStatusSource(), client, out _, out theme);
    }

    public static Composition Create(ReactiveGraph graph, GitHubIssueSource source, out IssueBrowserState state, out ThemeContext theme) => Create(graph, source, new FixtureIssueStatusSource(), null, out state, out theme);
    public static Composition Create(ReactiveGraph graph, GitHubIssueSource source, IIssueStatusSource statusSource, out IssueBrowserState state, out ThemeContext theme) => Create(graph, source, statusSource, null, out state, out theme);

    private static Composition Create(ReactiveGraph graph, GitHubIssueSource source, IIssueStatusSource statusSource, IDisposable? transport, out IssueBrowserState state, out ThemeContext theme)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(statusSource);
        IssueFixture.AssertIntegrity();
        var composition = new Composition(graph, "issue-browser");
        if (transport is not null) composition.Root.Scope.Own(transport);
        var browser = new IssueBrowserState(composition.Root.Scope, source, statusSource);
        var themeContext = new ThemeContext(composition.Root.Scope, Palette(ControlThemes.Light, Color.Parse("#f8fafc"), Color.Parse("#0f172a"), Color.Parse("#e2e8f0"), Color.Parse("#ffffff"), Color.Parse("#ffff00"), Color.Parse("#0f172a"), IssueDensity.Comfortable));
        theme = themeContext;
        _ = composition.Root.Scope.Effect(() => themeContext.Theme = Palette(themeContext.Appearance.Contrast == ThemeContrast.High ? ControlThemes.HighContrast : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark ? ControlThemes.Dark : ControlThemes.Light,
            themeContext.Appearance.Contrast == ThemeContrast.High ? Color.Parse("#000000") : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark ? Color.Parse("#0f172a") : Color.Parse("#f8fafc"),
            themeContext.Appearance.Contrast == ThemeContrast.High ? Color.Parse("#ffffff") : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark ? Color.Parse("#f8fafc") : Color.Parse("#0f172a"),
            themeContext.Appearance.Contrast == ThemeContrast.High ? Color.Parse("#000000") : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark ? Color.Parse("#1e293b") : Color.Parse("#e2e8f0"),
            themeContext.Appearance.Contrast == ThemeContrast.High ? Color.Parse("#000000") : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark ? Color.Parse("#111827") : Color.Parse("#ffffff"),
            themeContext.Appearance.Contrast == ThemeContrast.High ? Color.Parse("#ffff00") : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark ? Color.Parse("#facc15") : Color.Parse("#ffff00"),
            themeContext.Appearance.Contrast == ThemeContrast.High ? Color.Parse("#000000") : Color.Parse("#0f172a"), browser.Density), "issue-browser-appearance");
        _ = composition.Mount(composition.Root, themeContext, Components.IssueBrowser(browser).Named("Issue Browser"));
        state = browser;
        return composition;
    }

    private static Theme Palette(Theme controls, Color surface, Color foreground, Color header, Color row, Color focus, Color focusForeground, IssueDensity density) => controls
        .Set(PageSurface, (Brush)surface).Set(PageForeground, foreground).Set(HeaderSurface, (Brush)header).Set(RowSurface, (Brush)row).Set(FocusSurface, (Brush)focus).Set(FocusForeground, focusForeground)
        .Set(DensityHeaderHeight, density == IssueDensity.Comfortable ? 84f : 68f).Set(DensityFilterHeight, density == IssueDensity.Comfortable ? 28f : 22f)
        .Set(DensitySpacing, density == IssueDensity.Comfortable ? 8f : 4f).Set(DensityFontSize, density == IssueDensity.Comfortable ? 14f : 12f).Set(DensityTitleFontSize, density == IssueDensity.Comfortable ? 18f : 16f);

    private sealed class FixtureHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(IssueFixture.Json, Encoding.UTF8, "application/json") });
    }
}

/// <summary>Ordinary C# recipes for the reference application's composition.</summary>
public static partial class Components
{
    [LucentComponent]
    public static ComponentRecipe IssueBrowser(IssueBrowserState browser)
    {
        ArgumentNullException.ThrowIfNull(browser);
        return ComponentRecipe.Create("issue-browser", (context, root) => context.Mount(root, Lucent.Core.Components.Column([
            Header(browser).Named("issue-browser.header"),
            ContentRecipe.When("issue-browser.loading-region", () => browser.IsLoading, Loading(browser).Named("issue-browser.loading")),
            ContentRecipe.When("issue-browser.error-region", () => browser.Error is not null, Error(browser).Named("issue-browser.error")),
            Lucent.Core.Components.VirtualizedList(() => browser.VisibleIssues, issue => issue.Number, issue => IssueRow(browser, issue).Named("issue-browser.issue-row"),
                () => browser.Density == IssueDensity.Comfortable ? 30f : 22f, "Issues", Style.Empty.Width(800f).Height(60f)).Named("issue-browser.scroll-viewport"),
            ContentRecipe.ForEach("issue-browser.details-region", () => browser.SelectedIssue is { } issue ? [issue] : Array.Empty<BrowserIssue>(), issue => issue.Number,
                issue => Details(browser, issue).Named("issue-browser.details")),
            ContentRecipe.When("issue-browser.details-retry-region", () => browser.SelectedIssue is not null && browser.CanRetrySelected,
                Lucent.Core.Components.Button("Retry", browser.RetrySelected, Style.Empty.Height(IssueBrowserStructure.DensityFilterHeight)).Named("issue-browser.details-retry"))
        ], Style.Empty.Width(800f).Height(500f).Background(IssueBrowserStructure.PageSurface).TextColor(IssueBrowserStructure.PageForeground).Clip(true))));
    }

    private static readonly Style FilterBarStyle = Style.Empty.Width(800f).Height(IssueBrowserStructure.DensityFilterHeight).Spacing(IssueBrowserStructure.DensitySpacing);
    private static readonly Style TextFieldStyle = Style.Empty.Width(250f).Height(24f);
    private static Style IssueRowStyle(IssueBrowserState browser) => Style.Empty.Width(800f).Height(() => browser.Density == IssueDensity.Comfortable ? 30f : 22f).Spacing(IssueBrowserStructure.DensitySpacing).FontSize(IssueBrowserStructure.DensityFontSize).Background(IssueBrowserStructure.RowSurface)
        .When(VariantState.FocusVisible, Style.Empty.Background(IssueBrowserStructure.FocusSurface).TextColor(IssueBrowserStructure.FocusForeground));

    private static ComponentRecipe Header(IssueBrowserState browser) => ComponentRecipe.Create("issue-browser.header", (context, root) => context.Mount(root, Lucent.Core.Components.Column([
        Lucent.Core.Components.Text("Issues", Style.Empty.Height(24f).FontSize(IssueBrowserStructure.DensityTitleFontSize)).Named("issue-browser.title"),
        FilterBar(browser).Named("issue-browser.filters"),
        Lucent.Core.Components.Button("Density: Comfortable/Compact", browser.ToggleDensity, Style.Empty.Width(250f).Height(IssueBrowserStructure.DensityFilterHeight)).Named("issue-browser.density")
    ], Style.Empty.Width(800f).Height(IssueBrowserStructure.DensityHeaderHeight).Background(IssueBrowserStructure.HeaderSurface))));

    private static ComponentRecipe Loading(IssueBrowserState browser) => Lucent.Core.Components.Status(() => browser.IsStale ? "Refreshing issues" : "Loading issues", Style.Empty.Height(28f));
    private static ComponentRecipe Error(IssueBrowserState browser) => ComponentRecipe.Create("issue-browser.error", (context, root) => context.Mount(root, Lucent.Core.Components.Column([
        Lucent.Core.Components.Status(() => browser.Error ?? "", Style.Empty.Axis(LayoutAxis.Column)),
        Lucent.Core.Components.Button("Retry", browser.Retry, Style.Empty.Height(30f)).Named("issue-browser.retry")
    ])));
    private static ComponentRecipe Details(IssueBrowserState browser, BrowserIssue issue) => ComponentRecipe.Create("issue-browser.details", (context, root) => context.Mount(root, Lucent.Core.Components.Column([
        Lucent.Core.Components.Text(() => Detail(browser, issue, selected => "#" + selected.Number + " " + selected.Title)).Named("issue-browser.details-title"),
        Lucent.Core.Components.Status(() => Detail(browser, issue, selected => selected.Status + (browser.SelectedMutationMessage is { } message ? " · " + message : ""))).Named("issue-browser.details-status"),
        Lucent.Core.Components.Text(() => Detail(browser, issue, selected => selected.Body)).Named("issue-browser.details-body"),
        Lucent.Core.Components.Button("Open/Close", browser.ToggleSelectedStatus, Style.Empty.Height(IssueBrowserStructure.DensityFilterHeight)).Named("issue-browser.status-action")
    ], Style.Empty.Width(800f).Spacing(IssueBrowserStructure.DensitySpacing).FontSize(IssueBrowserStructure.DensityFontSize))));
    private static string Label(IssueBrowserState browser, BrowserIssue issue)
    {
        var current = browser.Issues.First(candidate => candidate.Number == issue.Number);
        return $"#{current.Number} {current.Title} — {current.Status} · {current.Assignee}";
    }
    private static string Detail(IssueBrowserState browser, BrowserIssue issue, Func<BrowserIssue, string> read) => browser.SelectedIssue is { } selected && selected.Number == issue.Number ? read(selected) : read(issue);
}
