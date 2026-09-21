using Lucent.Core;

namespace HostedContextNavigation;

[LucentRouteModule(RouteFallbackPolicy.Reject)]
public static partial class ProbeRoutes { }

[LucentRoute(typeof(ProbeRoutes), "/", Id = "shell")]
public readonly record struct ShellRoute();

[LucentRoute(typeof(ProbeRoutes), "/items/{id}", Id = "item", Parent = typeof(ShellRoute))]
public readonly record struct ItemRoute(int Id);

internal static class ProbeRouting
{
    internal static RouteBundle Bundle { get; } =
        RouteBundle.Create(
            [ProbeRoutes.Module],
            static level =>
                level.Id.Value switch
                {
                    "shell" => new(typeof(ShellRoute), Components.HostedShell()),
                    "item" => new(typeof(ItemRoute), Components.HostedLeaf()),
                    _ => throw new InvalidOperationException("Unknown probe route."),
                }
        );

    internal static RouteTable Table => Bundle.Table;

    internal static ComponentRecipe Root(ProbeWorkspace workspace) =>
        Lucent.Core.Components.Router(
            [
                Lucent.Core.Components.RouterOutlet(
                    new RouteOutletOptions(workspace.PrepareRoute),
                    workspace.Outlet
                ),
            ],
            Bundle,
            session: workspace.Navigation
        );

    internal static ComponentRecipe Child() => Lucent.Core.Components.RouterOutlet();
}
