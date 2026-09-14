namespace Lucent.IssueBrowser;

[LucentRouteModule(RouteFallbackPolicy.Reject)]
public static partial class IssueBrowserRoutes { }

[LucentRoute(typeof(IssueBrowserRoutes), "/issues", Id = "issues")]
public readonly record struct IssuesRoute();

[LucentRoute(typeof(IssueBrowserRoutes), "/issues/{number}", Id = "issue")]
public readonly record struct IssueRoute(int Number);

internal static class IssueBrowserRouting
{
    internal static RouteTable Table { get; } =
        RouteTable.Create(IssueBrowserRoutes.Module.Patterns);
    internal static RouteDescriptorSet Descriptors { get; } =
        RouteDescriptorSet.Create(Table, [IssueBrowserRoutes.Module]);

    internal static ComponentRecipe Outlet(NavigationInteraction interaction) =>
        RouteOutlet.Create(
            Descriptors,
            static level =>
                level.Id.Value switch
                {
                    "issues" => Components.IssuesRouteView(),
                    "issue" => Components.IssueRouteView(),
                    _ => throw new InvalidOperationException(
                        "Unknown Issue Browser route definition."
                    ),
                },
            options: new RouteOutletOptions(interaction: interaction)
        );
}
