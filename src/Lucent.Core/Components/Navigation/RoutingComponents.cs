namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Provides one fixed route bundle and shared navigation session to a subtree.</summary>
    [LucentComponent]
    public static ComponentRecipe Router(
        [DefaultContent] ComponentContent content,
        RouteBundle routes,
        string? initial = null,
        NavigationSession? session = null,
        string? name = null
    )
    {
        content = Content(content);
        ArgumentNullException.ThrowIfNull(routes);
        var kind = name ?? "router";
        ReactiveGraph.ValidateName(kind, nameof(name));
        return ComponentRecipe.Defer(
            kind,
            owner =>
            {
                RouteLocation? initialLocation = null;
                if (initial is not null)
                {
                    var parsed = RouteLocation.Parse(initial);
                    initialLocation =
                        parsed.Location
                        ?? throw new ArgumentException(
                            $"The initial route location was rejected ({parsed.Error.Kind}).",
                            nameof(initial)
                        );
                }

                var effective =
                    session ?? new NavigationSession(owner, routes.Table, initialLocation);
                if (!ReferenceEquals(effective.RouteTable, routes.Table))
                    throw new ArgumentException(
                        "A borrowed navigation session must use the router bundle's exact RouteTable instance.",
                        nameof(session)
                    );
                if (session is not null && initial is not null)
                    throw new ArgumentException(
                        "A router cannot combine a borrowed session with a separate initial location.",
                        nameof(initial)
                    );

                var placement = new RouterPlacement(routes, effective, cursor: null);
                var frame = ComponentRecipe.Create(
                    kind,
                    (context, root) =>
                    {
                        root.Present(
                            context.Theme,
                            author: Style
                                .Empty.Axis(LayoutAxis.Column)
                                .MainGrow(1)
                                .MainBasis(0)
                                .MinWidth(0)
                                .MinHeight(0)
                        );
                        context.Mount(root, content);
                    }
                );
                return Context.Provide(effective, Context.Provide(placement, frame));
            }
        );
    }

    /// <summary>Displays the current destination from the nearest Router.</summary>
    /// <remarks>Supply options only to the root outlet; nested outlets inherit its policy.</remarks>
    [LucentComponent]
    public static ComponentRecipe RouterOutlet(
        RouteOutletOptions? options = null,
        RouteOutletHandle? handle = null,
        string? name = null
    ) => RouteOutlet.CreateFromRouter(handle, name, options);
}
