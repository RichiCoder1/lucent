namespace Lucent.IssueBrowser;

/// <summary>View-owned browsing state, separate from issue loading and mutation.</summary>
public sealed class IssueBrowserViewState
{
    private readonly IssueBrowserState _browser;
    private readonly IssueBrowserNavigationPersistence? _persistence;

    public IssueBrowserViewState(
        ReactiveScope owner,
        IssueBrowserState browser,
        IssueBrowserNavigationPersistence? persistence = null
    )
    {
        _browser = browser;
        _persistence = persistence;
        Navigation = new(
            owner,
            IssueBrowserRouting.Table,
            persistence is null ? IssueBrowserRoutes.Issues().Location : null
        );
        Interaction = new(owner, Navigation);
        Breakpoints = new(owner, IssueBrowserBreakpoints.Set, "browser-view.breakpoints");
        Split = new(owner, 380, 280, 320, name: "browser-view.split");
        ListViewport = new(owner, name: "browser-view.list");
        DetailViewport = new(owner, name: "browser-view.detail");
        SearchEditor = new(owner, "issue-search", browser.Search);
        _ = Navigation.RegisterCommitted(
            owner,
            commit =>
            {
                if (commit.Current.DefinitionId.Value == "issue")
                    browser.Select(commit.Current.Match.GetValue(0).Signed32);
            }
        );
        persistence?.Bind(owner, this, browser);
    }

    public WindowBreakpoints Breakpoints { get; }
    public SplitPaneState Split { get; }
    public ViewportState ListViewport { get; }
    public ViewportState DetailViewport { get; }
    public EditorSession SearchEditor { get; }
    public NavigationSession Navigation { get; }
    public NavigationInteraction Interaction { get; }

    /// <summary>Gets the route signal used by responsive pane styles without starting issue loading.</summary>
    public bool DetailsRoute => Navigation.Current?.DefinitionId.Value == "issue";
    public bool ShowDetails => DetailsRoute;
    public int? OpenedNumber =>
        Navigation.Current is { DefinitionId.Value: "issue" } current
            ? current.Match.GetValue(0).Signed32
            : null;
    public DensityPreset Density =>
        _browser.Density == IssueDensity.Comfortable
            ? DensityPreset.Comfortable
            : DensityPreset.Compact;

    /// <summary>Gets the shared extent used by the two-line row and its fixed-row viewport.</summary>
    public float IssueRowHeight
    {
        get
        {
            var metrics = DensityMetrics.For(Density);
            return metrics.RowHeight + metrics.FontSize + metrics.Spacing;
        }
    }
    public string ResultSummary =>
        $"{_browser.VisibleIssues.Count:N0} issues · {(_browser.Status == "all" ? "all states" : _browser.Status)}";

    public void OpenIssue(int number, NavigationOrigin origin = NavigationOrigin.Application)
    {
        if (_browser.Issues.Any(issue => issue.Number == number))
            Navigation.Navigate(IssueBrowserRoutes.Issue(number), origin: origin);
    }

    public void BackToList() => Navigation.Navigate(IssueBrowserRoutes.Issues());

    public void Back() => Navigation.Back();

    internal NavigationPreparationResult PrepareNavigation(RouteOutletPreparationRequest request)
    {
        if (_persistence is { AllowsNavigation: false })
            return NavigationPreparationResult.Stay;
        return
            request.Phase == NavigationPreparationPhase.Enter
            && request.Target.DefinitionId.Value == "issue"
            && !_browser.Issues.Any(issue =>
                issue.Number == request.Target.Match.GetValue(0).Signed32
            )
            ? NavigationPreparationResult.Stay
            : NavigationPreparationResult.Allow;
    }

    public void ClearFilters()
    {
        SearchEditor.Text = "";
        _browser.Search = "";
        _browser.Status = "all";
        _browser.Assignee = "all";
    }
}
