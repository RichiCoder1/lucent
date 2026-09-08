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
        Constraints = new(owner, "browser-view.bounds");
        Split = new(owner, 380, 280, 320, name: "browser-view.split");
        ListViewport = new(owner, name: "browser-view.list");
        DetailViewport = new(owner, name: "browser-view.detail");
        SearchEditor = new(owner, "issue-search", browser.Search);
        StatusEditor = new(owner, "issue-status", browser.Status);
        AssigneeEditor = new(owner, "issue-assignee", browser.Assignee);
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

    public ResponsiveConstraints Constraints { get; }
    public SplitPaneState Split { get; }
    public ViewportState ListViewport { get; }
    public ViewportState DetailViewport { get; }
    public EditorSession SearchEditor { get; }
    public EditorSession StatusEditor { get; }
    public EditorSession AssigneeEditor { get; }
    public bool IsWide => Constraints.Current.Width >= 820;
    public bool ShowDetails => _showDetails.Value && _browser.SelectedIssue is not null;
    public DensityPreset Density =>
        _browser.Density == IssueDensity.Comfortable
            ? DensityPreset.Comfortable
            : DensityPreset.Compact;
    public DensityMetrics Metrics => DensityMetrics.For(Density);
    public float RowHeight => Metrics.RowHeight + Metrics.FontSize + Metrics.Spacing;
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
        StatusEditor.Text = "all";
        AssigneeEditor.Text = "all";
        _browser.Search = "";
        _browser.Status = "all";
        _browser.Assignee = "all";
    }
}
