namespace Lucent.IssueBrowser;

public static partial class Components
{
    private static void OpenIssue(
        IssueBrowserState browser,
        int number,
        IssueBrowserViewState? view
    )
    {
        if (view is null)
            browser.OpenIssue(number);
        else
            view.OpenIssue(number);
    }

    private static string Label(BrowserIssue issue) =>
        $"#{issue.Number} {issue.Title} — {issue.Status} · {issue.Assignee}";

    private static string FormatUpdated(BrowserIssue issue) =>
        DateTimeOffset.TryParse(
            issue.Updated,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out var updated
        )
            ? updated.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : issue.Updated;
}
