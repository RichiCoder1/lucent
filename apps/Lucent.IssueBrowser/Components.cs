namespace Lucent.IssueBrowser;

public static partial class Components
{
    private static readonly Style IssueRowBodyStyle = PresentationStyles
        .Typography(TextRole.Body)
        .Set(TypographyProperties.TextWrap, TextWrap.WordWithGraphemeFallback)
        .Set(TypographyProperties.MaxLines, 1)
        .Set(TypographyProperties.Overflow, TextOverflow.Ellipsis);

    private static readonly Style IssueRowCaptionStyle = PresentationStyles
        .Typography(TextRole.Caption)
        .Set(TypographyProperties.TextWrap, TextWrap.WordWithGraphemeFallback)
        .Set(TypographyProperties.MaxLines, 1)
        .Set(TypographyProperties.Overflow, TextOverflow.Ellipsis);

    private static Style IssueRowLayout(IssueBrowserViewState? view) =>
        Style
            .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
            .Spacing(4)
            .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Stretch)
            .Bind(LayoutProperties.Height, () => view?.RowHeight ?? 58);

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
