namespace Lucent.IssueBrowser;

/// <summary>View-owned browsing state, separate from issue loading and mutation.</summary>
public sealed class IssueBrowserViewState
{
    private readonly IssueBrowserState _browser;
    private readonly Signal<bool> _showDetails;

    public IssueBrowserViewState(ReactiveScope owner, IssueBrowserState browser)
    {
        _browser = browser;
        _showDetails = owner.Signal(false, "browser-view.details");
        Breakpoints = new(owner, IssueBrowserBreakpoints.Set, "browser-view.breakpoints");
        Split = new(owner, 380, 280, 320, name: "browser-view.split");
        ListViewport = new(owner, name: "browser-view.list");
        DetailViewport = new(owner, name: "browser-view.detail");
        SearchEditor = new(owner, "issue-search", browser.Search);
        int? previous = null;
        _ = owner.Effect(
            () =>
            {
                var selected = browser.SelectedIssue?.Number;
                if (selected != previous)
                {
                    previous = selected;
                    DetailViewport.Offset = default;
                }
            },
            "browser-view.selection"
        );
    }

    public WindowBreakpoints Breakpoints { get; }
    public SplitPaneState Split { get; }
    public ViewportState ListViewport { get; }
    public ViewportState DetailViewport { get; }
    public EditorSession SearchEditor { get; }

    /// <summary>Gets the route signal used by responsive pane styles without starting issue loading.</summary>
    public bool DetailsRoute => _showDetails.Value;
    public bool ShowDetails => _showDetails.Value && _browser.SelectedIssue is not null;
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

    public void OpenIssue(int number)
    {
        _browser.Select(number);
        _showDetails.Value = true;
    }

    public void BackToList() => _showDetails.Value = false;

    public void ClearFilters()
    {
        SearchEditor.Text = "";
        _browser.Search = "";
        _browser.Status = "all";
        _browser.Assignee = "all";
    }
}
