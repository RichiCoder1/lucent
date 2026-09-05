namespace Lucent.IssueBrowser;

public static partial class Components
{
    private static string Label(BrowserIssue issue) =>
        $"#{issue.Number} {issue.Title} — {issue.Status} · {issue.Assignee}";
}
