using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucent.Core;

namespace Lucent.IssueBrowser;

/// <summary>Ordinary C# recipes for the reference application's composition.</summary>
public static partial class Components
{
    [LucentComponent]
    public static ComponentRecipe IssueBrowser(IssueBrowserState browser)
    {
        ArgumentNullException.ThrowIfNull(browser);
        return ComponentRecipe.Create(
            "issue-browser",
            (context, root) =>
                context.Mount(
                    root,
                    Lucent.Core.Components.Column(
                        [
                            Header(browser).Named("issue-browser.header"),
                            ContentRecipe.When(
                                "issue-browser.loading-region",
                                () => browser.IsLoading,
                                Loading(browser).Named("issue-browser.loading")
                            ),
                            ContentRecipe.When(
                                "issue-browser.error-region",
                                () => browser.Error is not null,
                                Error(browser).Named("issue-browser.error")
                            ),
                            Lucent
                                .Core.Components.VirtualizedList(
                                    () => browser.VisibleIssues,
                                    issue => issue.Number,
                                    issue =>
                                        IssueRow(browser, issue).Named("issue-browser.issue-row"),
                                    () => browser.Density == IssueDensity.Comfortable ? 30f : 22f,
                                    "Issues",
                                    Style.Empty.Width(800f).Height(60f)
                                )
                                .Named("issue-browser.scroll-viewport"),
                            ContentRecipe.ForEach(
                                "issue-browser.details-region",
                                () =>
                                    browser.SelectedIssue is { } issue
                                        ? [issue]
                                        : Array.Empty<BrowserIssue>(),
                                issue => issue.Number,
                                issue => Details(browser, issue).Named("issue-browser.details")
                            ),
                            ContentRecipe.When(
                                "issue-browser.details-retry-region",
                                () => browser.SelectedIssue is not null && browser.CanRetrySelected,
                                Lucent
                                    .Core.Components.Button(
                                        "Retry",
                                        browser.RetrySelected,
                                        Style.Empty.Height(
                                            IssueBrowserStructure.DensityFilterHeight
                                        )
                                    )
                                    .Named("issue-browser.details-retry")
                            ),
                        ],
                        Style
                            .Empty.Width(800f)
                            .Height(500f)
                            .Background(IssueBrowserStructure.PageSurface)
                            .TextColor(IssueBrowserStructure.PageForeground)
                            .Clip(true)
                    )
                )
        );
    }

    private static readonly Style FilterBarStyle = Style
        .Empty.Width(800f)
        .Height(IssueBrowserStructure.DensityFilterHeight)
        .Spacing(IssueBrowserStructure.DensitySpacing);
    private static readonly Style TextFieldStyle = Style.Empty.Width(250f).Height(24f);

    private static Style IssueRowStyle(IssueBrowserState browser) =>
        Style
            .Empty.Width(800f)
            .Height(() => browser.Density == IssueDensity.Comfortable ? 30f : 22f)
            .Spacing(IssueBrowserStructure.DensitySpacing)
            .FontSize(IssueBrowserStructure.DensityFontSize)
            .Background(IssueBrowserStructure.RowSurface)
            .When(
                VariantState.FocusVisible,
                Style
                    .Empty.Background(IssueBrowserStructure.FocusSurface)
                    .TextColor(IssueBrowserStructure.FocusForeground)
            );

    private static ComponentRecipe Header(IssueBrowserState browser) =>
        ComponentRecipe.Create(
            "issue-browser.header",
            (context, root) =>
                context.Mount(
                    root,
                    Lucent.Core.Components.Column(
                        [
                            Lucent
                                .Core.Components.Text(
                                    "Issues",
                                    Style
                                        .Empty.Height(24f)
                                        .FontSize(IssueBrowserStructure.DensityTitleFontSize)
                                )
                                .Named("issue-browser.title"),
                            FilterBar(browser).Named("issue-browser.filters"),
                            Lucent
                                .Core.Components.Button(
                                    "Density: Comfortable/Compact",
                                    browser.ToggleDensity,
                                    Style
                                        .Empty.Width(250f)
                                        .Height(IssueBrowserStructure.DensityFilterHeight)
                                )
                                .Named("issue-browser.density"),
                        ],
                        Style
                            .Empty.Width(800f)
                            .Height(IssueBrowserStructure.DensityHeaderHeight)
                            .Background(IssueBrowserStructure.HeaderSurface)
                    )
                )
        );

    private static ComponentRecipe Loading(IssueBrowserState browser) =>
        Lucent.Core.Components.Status(
            () => browser.IsStale ? "Refreshing issues" : "Loading issues",
            Style.Empty.Height(28f)
        );

    private static ComponentRecipe Error(IssueBrowserState browser) =>
        ComponentRecipe.Create(
            "issue-browser.error",
            (context, root) =>
                context.Mount(
                    root,
                    Lucent.Core.Components.Column([
                        Lucent.Core.Components.Status(
                            () => browser.Error ?? "",
                            Style.Empty.Axis(LayoutAxis.Column)
                        ),
                        Lucent
                            .Core.Components.Button("Retry", browser.Retry, Style.Empty.Height(30f))
                            .Named("issue-browser.retry"),
                    ])
                )
        );

    private static ComponentRecipe Details(IssueBrowserState browser, BrowserIssue issue) =>
        ComponentRecipe.Create(
            "issue-browser.details",
            (context, root) =>
                context.Mount(
                    root,
                    Lucent.Core.Components.Column(
                        [
                            Lucent
                                .Core.Components.Text(() =>
                                    Detail(
                                        browser,
                                        issue,
                                        selected => "#" + selected.Number + " " + selected.Title
                                    )
                                )
                                .Named("issue-browser.details-title"),
                            Lucent
                                .Core.Components.Status(() =>
                                    Detail(
                                        browser,
                                        issue,
                                        selected =>
                                            selected.Status
                                            + (
                                                browser.SelectedMutationMessage is { } message
                                                    ? " · " + message
                                                    : ""
                                            )
                                    )
                                )
                                .Named("issue-browser.details-status"),
                            Lucent
                                .Core.Components.Text(() =>
                                    Detail(browser, issue, selected => selected.Body)
                                )
                                .Named("issue-browser.details-body"),
                            Lucent
                                .Core.Components.Button(
                                    "Open/Close",
                                    browser.ToggleSelectedStatus,
                                    Style.Empty.Height(IssueBrowserStructure.DensityFilterHeight)
                                )
                                .Named("issue-browser.status-action"),
                        ],
                        Style
                            .Empty.Width(800f)
                            .Spacing(IssueBrowserStructure.DensitySpacing)
                            .FontSize(IssueBrowserStructure.DensityFontSize)
                    )
                )
        );

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
