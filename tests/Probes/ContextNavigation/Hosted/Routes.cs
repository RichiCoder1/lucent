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
    internal static RouteTable Table { get; } = RouteTable.Create(ProbeRoutes.Module.Patterns);

    internal static RouteDescriptorSet Descriptors { get; } =
        RouteDescriptorSet.Create(Table, [ProbeRoutes.Module]);

    internal static ComponentRecipe Root(ProbeWorkspace workspace) =>
        Context.Provide(
            workspace.Navigation,
            RouteOutlet.Create(
                Descriptors,
                static level =>
                    level.Id.Value switch
                    {
                        "shell" => Components.HostedShell(),
                        _ => throw new InvalidOperationException("Unknown root route."),
                    },
                workspace.Outlet,
                options: new RouteOutletOptions(workspace.PrepareRoute)
            )
        );

    internal static ComponentRecipe Child() =>
        RouteOutlet.CreateChild(
            Descriptors,
            static level =>
                level.Id.Value switch
                {
                    "item" => Components.HostedLeaf(),
                    _ => throw new InvalidOperationException("Unknown child route."),
                }
        );
}
