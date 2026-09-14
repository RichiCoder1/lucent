using Lucent.Core;

internal enum IssueView
{
    Summary,
    Activity,
}

[LucentRouteModule(RouteFallbackPolicy.Reject)]
internal static partial class AppRoutes { }

[LucentRoute(typeof(AppRoutes), "/projects/{projectId}?view={view}", Id = "project")]
internal readonly record struct ProjectRoute(Guid ProjectId, IssueView View = IssueView.Summary);

[LucentRoute(
    typeof(AppRoutes),
    "/projects/{projectId}/issues/{issueId}?view={view}",
    Id = "issue",
    Parent = typeof(ProjectRoute)
)]
internal readonly record struct IssueRoute(
    Guid ProjectId,
    int IssueId,
    IssueView View = IssueView.Summary
);
