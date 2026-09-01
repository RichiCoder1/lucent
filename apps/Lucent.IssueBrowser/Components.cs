namespace Lucent.IssueBrowser;

public static partial class Components
{
    private static string Label(IssueBrowserState browser, BrowserIssue issue)
    {
        var current = browser.Issues.First(candidate => candidate.Number == issue.Number);
        return $"#{current.Number} {current.Title} — {current.Status} · {current.Assignee}";
    }

    private static string Detail(
        IssueBrowserState browser,
        BrowserIssue issue,
        Func<BrowserIssue, string> read
    ) =>
        browser.SelectedIssue is { } selected && selected.Number == issue.Number
            ? read(selected)
            : read(issue);
}
