namespace Lucent.IssueBrowser;

[LucentRouteModule(RouteFallbackPolicy.Reject)]
public static partial class IssueBrowserRoutes { }

[LucentRoute(typeof(IssueBrowserRoutes), "/issues", Id = "issues")]
public readonly record struct IssuesRoute();

[LucentRoute(typeof(IssueBrowserRoutes), "/issues/{number}", Id = "issue")]
public readonly record struct IssueRoute(int Number);

internal static class IssueBrowserRouting
{
    internal static RouteBundle Bundle { get; } =
        RouteBundle.Create(
            [IssueBrowserRoutes.Module],
            static level =>
                level.Id.Value switch
                {
                    "issues" => new(typeof(IssuesRoute), Components.IssuesRouteView()),
                    "issue" => new(typeof(IssueRoute), Components.IssueRouteView()),
                    _ => throw new InvalidOperationException(
                        "Unknown Issue Browser route definition."
                    ),
                }
        );

    internal static RouteTable Table => Bundle.Table;

    internal static ComponentRecipe Outlet(NavigationInteraction interaction) =>
        Lucent.Core.Components.Router(
            [
                Lucent.Core.Components.RouterOutlet(
                    options: new RouteOutletOptions(interaction: interaction)
                ),
            ],
            Bundle,
            session: interaction.Session
        );
}
