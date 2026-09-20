namespace Lucent.ComponentBrowser;

[LucentRouteModule(RouteFallbackPolicy.Reject)]
public static partial class ComponentBrowserRoutes { }

[LucentRoute(
    typeof(ComponentBrowserRoutes),
    "/examples/buttons",
    Id = "buttons",
    Component = typeof(ButtonsExample)
)]
public readonly record struct ButtonsRoute();

[LucentRoute(
    typeof(ComponentBrowserRoutes),
    "/examples/fields",
    Id = "fields",
    Component = typeof(FieldsExample)
)]
public readonly record struct FieldsRoute();

[LucentRoute(
    typeof(ComponentBrowserRoutes),
    "/examples/password",
    Id = "password",
    Component = typeof(PasswordExample)
)]
public readonly record struct PasswordRoute();

[LucentRoute(
    typeof(ComponentBrowserRoutes),
    "/examples/combo-box",
    Id = "combo-box",
    Component = typeof(ComboBoxExample)
)]
public readonly record struct ComboBoxRoute();

[LucentRoute(
    typeof(ComponentBrowserRoutes),
    "/examples/selection",
    Id = "selection",
    Component = typeof(SelectionExample)
)]
public readonly record struct SelectionRoute();

[LucentRoute(
    typeof(ComponentBrowserRoutes),
    "/examples/feedback",
    Id = "feedback",
    Component = typeof(FeedbackExample)
)]
public readonly record struct FeedbackRoute();

[LucentRoute(
    typeof(ComponentBrowserRoutes),
    "/examples/menus",
    Id = "menus",
    Component = typeof(MenusExample)
)]
public readonly record struct MenusRoute();

[LucentRoute(
    typeof(ComponentBrowserRoutes),
    "/examples/surfaces",
    Id = "surfaces",
    Component = typeof(PopoverExample)
)]
public readonly record struct SurfacesRoute();

[LucentRoute(
    typeof(ComponentBrowserRoutes),
    "/examples/numeric",
    Id = "numeric",
    Component = typeof(NumericExample)
)]
public readonly record struct NumericRoute();

[LucentRoute(
    typeof(ComponentBrowserRoutes),
    "/examples/date-time",
    Id = "date-time",
    Component = typeof(DateTimeExample)
)]
public readonly record struct DateTimeRoute();

[LucentRoute(
    typeof(ComponentBrowserRoutes),
    "/examples/navigation",
    Id = "navigation",
    Component = typeof(NavigationExample)
)]
public readonly record struct NavigationRoute();

[LucentRoute(
    typeof(ComponentBrowserRoutes),
    "/examples/tree",
    Id = "tree",
    Component = typeof(TreeExample)
)]
public readonly record struct TreeRoute();

[LucentRoute(
    typeof(ComponentBrowserRoutes),
    "/examples/storage",
    Id = "storage",
    Component = typeof(StorageExample)
)]
public readonly record struct StorageRoute();

[LucentRoute(
    typeof(ComponentBrowserRoutes),
    "/examples/table",
    Id = "table",
    Component = typeof(TableExample)
)]
public readonly record struct TableRoute();
