using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lucent.IssueBrowser;

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

    public IssueBrowserState(
        ReactiveScope scope,
        GitHubIssueSource source,
        IIssueStatusSource? statusSource = null
    )
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(source);
        _scope = scope;
        _statusSource = statusSource ?? new FixtureIssueStatusSource();
        _retry = scope.Signal(0, "issue-browser.retry");
        _search = scope.Signal("", "issue-browser.search");
        _status = scope.Signal("all", "issue-browser.status");
        _assignee = scope.Signal("all", "issue-browser.assignee");
        _selectedNumber = scope.Signal<int?>(null, "issue-browser.selection");
        _density = scope.Signal(IssueDensity.Comfortable, "issue-browser.density");
        _statuses = scope.Signal<IReadOnlyDictionary<int, string>>(
            new Dictionary<int, string>(),
            "issue-browser.statuses"
        );
        _messages = scope.Signal<IReadOnlyDictionary<int, string>>(
            new Dictionary<int, string>(),
            "issue-browser.mutation-messages"
        );
        _issues = scope.Async(
            async token =>
            {
                _ = _retry.Value;
                return await source.LoadAsync(token).ConfigureAwait(false);
            },
            "issue-browser.issues"
        );
        _visible = scope.Derived<IReadOnlyList<BrowserIssue>>(
            () => Issues.Where(Matches).ToArray(),
            "issue-browser.visible-issues"
        );
    }

    public IReadOnlyList<BrowserIssue> Issues =>
        (_issues.Value ?? Array.Empty<BrowserIssue>())
            .Select(issue =>
                _statuses.Value.TryGetValue(issue.Number, out var status)
                    ? issue with
                    {
                        Status = status,
                    }
                    : issue
            )
            .ToArray();
    public IReadOnlyList<BrowserIssue> VisibleIssues => _visible.Value;
    public bool IsLoading => _issues.IsPending;
    public bool IsStale => _issues.HasValue && _issues.IsPending;
    public string? Error => _issues.Error?.Message;
    public BrowserIssue? SelectedIssue =>
        Issues.FirstOrDefault(issue => issue.Number == _selectedNumber.Value);
    public IssueDensity Density
    {
        get => _density.Value;
        set => _density.Value = value;
    }
    public string? SelectedMutationMessage =>
        _selectedNumber.Value is { } number && _messages.Value.TryGetValue(number, out var message)
            ? message
            : null;
    public bool CanRetrySelected
    {
        get
        {
            _ = _messages.Value;
            return _selectedNumber.Value is { } number
                && _mutations.TryGetValue(number, out var mutation)
                && mutation.CanRetry;
        }
    }

    public bool IsSelected(int number) => _selectedNumber.Value == number;

    public string Search
    {
        get => _search.Value;
        set => _search.Value = value.Trim();
    }
    public string Status
    {
        get => _status.Value;
        set => _status.Value = Normalize(value, "all");
    }
    public string Assignee
    {
        get => _assignee.Value;
        set => _assignee.Value = Normalize(value, "all");
    }

    public void SetSearch(string value) => Search = value;

    public void SetStatus(string value) => Status = value;

    public void SetAssignee(string value) => Assignee = value;

    public void Retry() => _retry.Value++;

    public void Select(int number) =>
        _selectedNumber.Value = Issues.Any(issue => issue.Number == number) ? number : null;

    public void OpenIssue(int number) => Select(number);

    public void ToggleIssueStatus(int number)
    {
        var issue = Issues.FirstOrDefault(candidate => candidate.Number == number);
        if (issue is not null)
            ToggleStatus(issue);
    }

    public void ToggleDensity() =>
        Density =
            Density == IssueDensity.Comfortable ? IssueDensity.Compact : IssueDensity.Comfortable;

    public void ToggleSelectedStatus()
    {
        if (SelectedIssue is { } issue)
            ToggleStatus(issue);
    }

    private void ToggleStatus(BrowserIssue issue)
    {
        var next = issue.Status == "open" ? "closed" : "open";
        Set(_statuses, issue.Number, next);
        Set(_messages, issue.Number, null);
        Mutation(issue.Number).Start(issue.Status, next);
    }

    public void RetrySelected()
    {
        if (
            _selectedNumber.Value is { } number
            && _mutations.TryGetValue(number, out var mutation)
            && mutation.Retry()
        )
            Set(_messages, number, null);
    }

    private bool Matches(BrowserIssue issue) =>
        (Status == "all" || issue.Status == Status)
        && (Assignee == "all" || issue.Assignee == Assignee)
        && (
            string.IsNullOrWhiteSpace(Search)
            || (issue.Title + " " + issue.Labels + " " + issue.Body).Contains(
                Search,
                StringComparison.OrdinalIgnoreCase
            )
        );

    private static string Normalize(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();

    private IssueStatusMutation Mutation(int number) =>
        _mutations.TryGetValue(number, out var mutation)
            ? mutation
            : _mutations[number] = new IssueStatusMutation(
                _scope,
                number,
                _statusSource,
                CompleteMutation
            );

    private void CompleteMutation(
        int number,
        string original,
        string requested,
        IssueStatusSaveOutcome outcome
    )
    {
        var status = requested;
        string? message = outcome switch
        {
            IssueStatusSaveOutcome.Saved => null,
            IssueStatusSaveOutcome.Rejected rejected => "Rejected: " + rejected.Reason,
            IssueStatusSaveOutcome.TransientFailure failure => "Not synced: " + failure.Reason,
            _ => throw new InvalidOperationException("Unknown issue status save outcome."),
        };
        if (outcome is IssueStatusSaveOutcome.Rejected)
            status = original;
        Set(_statuses, number, status);
        Set(_messages, number, message);
    }

    private static void Set(
        Signal<IReadOnlyDictionary<int, string>> values,
        int number,
        string? value
    )
    {
        var next = new Dictionary<int, string>(values.Value);
        if (value is null)
            next.Remove(number);
        else
            next[number] = value;
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

        public IssueStatusMutation(
            ReactiveScope scope,
            int number,
            IIssueStatusSource source,
            Action<int, string, string, IssueStatusSaveOutcome> complete
        )
        {
            _complete = complete;
            _request = scope.Signal<Request?>(null, "issue-browser.mutation." + number);
            _save = scope.Async(
                async token =>
                {
                    var request =
                        _request.Value
                        ?? throw new InvalidOperationException(
                            "Issue mutation started without a request."
                        );
                    return await source
                        .SaveAsync(number, request.Status, token)
                        .ConfigureAwait(false);
                },
                "issue-browser.mutation-save." + number
            );
            _ = scope.Effect(() => Commit(number), "issue-browser.mutation-commit." + number);
        }

        public bool CanRetry => _lastTransient is not null;

        public void Start(string original, string status)
        {
            _lastTransient = null;
            _request.Value = new(++_generation, original, status);
        }

        public bool Retry()
        {
            if (_lastTransient is not { } request)
                return false;
            _lastTransient = null;
            _request.Value = request with { Generation = ++_generation };
            return true;
        }

        private void Commit(int number)
        {
            var request = _request.Value;
            if (request is null)
                return;
            var error = _save.Error;
            if (_save.IsPending || _save.IsCancelled || request.Generation == _completed)
                return;
            _completed = request.Generation;
            var outcome = error is null
                ? _save.Value
                    ?? throw new InvalidOperationException(
                        "Issue mutation completed without an outcome."
                    )
                : new IssueStatusSaveOutcome.TransientFailure(FailureReason(error));
            _lastTransient = outcome is IssueStatusSaveOutcome.TransientFailure ? request : null;
            _complete(number, request.Original, request.Status, outcome);
        }

        private static string FailureReason(Exception error)
        {
            var reason = error.GetBaseException().Message.Trim();
            if (string.IsNullOrWhiteSpace(reason))
                reason = error.GetBaseException().GetType().Name;
            const int maximum = 160;
            return "Unexpected save failure: "
                + (reason.Length <= maximum ? reason : reason[..maximum] + "…");
        }

        private sealed record Request(long Generation, string Original, string Status);
    }
}
