namespace Lucent.ComponentBrowser;

[LucentRouteModule(RouteFallbackPolicy.Reject)]
public static partial class ComponentBrowserRoutes { }

[LucentRoute(typeof(ComponentBrowserRoutes), "/examples/{id}", Id = "example")]
public readonly record struct ExampleRoute(string Id);

internal static class ComponentBrowserRouting
{
    internal static RouteTable Table { get; } =
        RouteTable.Create(ComponentBrowserRoutes.Module.Patterns);
    internal static RouteDescriptorSet Descriptors { get; } =
        RouteDescriptorSet.Create(Table, [ComponentBrowserRoutes.Module]);

    internal static ComponentRecipe Outlet(NavigationInteraction interaction) =>
        RouteOutlet.Create(
            Descriptors,
            static _ => Components.ComponentExample(),
            options: new RouteOutletOptions(
                prepare: static (_, request, _) =>
                    ValueTask.FromResult(
                        ComponentCatalog.Items.Any(item =>
                            item.Id == request.Target.Match.GetValue(0).Text
                        )
                            ? NavigationPreparationResult.Allow
                            : NavigationPreparationResult.Fail()
                    ),
                interaction: interaction
            )
        );
}
