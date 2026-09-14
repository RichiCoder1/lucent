namespace Lucent.ComponentBrowser;

public static class ComponentBrowserStructure
{
    public static ComponentRecipe Create(
        IFilePicker? filePicker = null,
        IUriLauncher? uriLauncher = null
    ) =>
        ComponentRecipe.Create(
            "component-browser-application",
            (context, root) =>
            {
                root.Present(
                    context.Theme,
                    author: PresentationStyles.Surface.MainGrow(1).MainBasis(0)
                );
                _ = context.Mount(
                    root,
                    Components.ComponentBrowserApplication(context.Theme, filePicker, uriLauncher)
                );
            }
        );
}
