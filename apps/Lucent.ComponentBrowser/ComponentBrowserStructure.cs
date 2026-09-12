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
                var browser = new ComponentBrowserState(root.Scope, filePicker, uriLauncher);
                _ = context.Mount(
                    root,
                    Components.ComponentBrowser(browser, context.Theme).Named("Component Browser")
                );
            }
        );

    internal static Composition Create(ReactiveGraph graph, out ThemeContext theme)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var composition = new Composition(graph, "component-browser");
        var themeContext = new ThemeContext(
            composition.Root.Scope,
            StockTheme(ThemeAppearance.Light)
        );
        theme = themeContext;
        InstallAppearanceTheme(composition, themeContext);
        _ = composition.Mount(
            composition.Root,
            themeContext,
            ComponentRecipe.Create(
                "component-browser-application",
                (context, root) =>
                {
                    root.Present(
                        context.Theme,
                        author: PresentationStyles.Surface.MainGrow(1).MainBasis(0)
                    );
                    var browser = new ComponentBrowserState(root.Scope);
                    _ = context.Mount(
                        root,
                        Components
                            .ComponentBrowser(browser, context.Theme)
                            .Named("Component Browser")
                    );
                }
            )
        );
        return composition;
    }

    private static void InstallAppearanceTheme(Composition composition, ThemeContext theme)
    {
        var appliedAppearance = theme.Appearance;
        _ = composition.Root.Scope.Effect(
            () =>
            {
                var appearance = theme.Appearance;
                if (appearance == appliedAppearance)
                    return;
                appliedAppearance = appearance;
                theme.Theme = StockTheme(appearance);
            },
            "component-browser-appearance"
        );
    }

    private static Theme StockTheme(ThemeAppearance appearance) =>
        appearance.Contrast == ThemeContrast.High ? ControlThemes.HighContrast
        : appearance.ColorScheme == ThemeColorScheme.Dark ? ControlThemes.Dark
        : ControlThemes.Light;
}
