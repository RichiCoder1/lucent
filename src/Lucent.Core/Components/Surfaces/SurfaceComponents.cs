namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Retains an anchored, interactive, nonmodal surface while the controlled open value is true.</summary>
    /// <remarks>Outside dismissal is consumed by default. The callback requests state changes; content is mounted once per open session.</remarks>
    [LucentComponent]
    public static ComponentRecipe Popover(
        Func<bool> open,
        Action<bool> onOpenRequested,
        ComponentRecipe popup,
        [DefaultContent] ComponentContent content,
        bool consumeOutsideClick = true,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(onOpenRequested);
        ArgumentNullException.ThrowIfNull(popup);
        return ComponentRecipe.Create(
            "popover-anchor",
            (context, root) =>
            {
                root.Present(
                    context.Theme,
                    component: Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Column),
                    author: style
                );
                context.Mount(root, content);
                OwnedSurfaceRequest? session = null;
                root.Scope.OnDispose(() => session?.Dispose());
                _ = root.Scope.Effect(
                    () =>
                    {
                        if (open())
                        {
                            // A dismissed controlled session remains closed until the owner acknowledges
                            // false, preventing an unrelated reactive change from reopening it.
                            if (session is not null)
                                return;
                            session = new OwnedSurfaceRequest(
                                root,
                                context.Theme,
                                popup,
                                interactive: true,
                                consumeOutsideClick,
                                () => onOpenRequested(false)
                            );
                            root.Composition.Input.RequestSurface(session);
                        }
                        else
                        {
                            session?.Dispose();
                            session = null;
                        }
                    },
                    "popover-open"
                );
            }
        );
    }
}
