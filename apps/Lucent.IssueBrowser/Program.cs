using Lucent.Core;
using Lucent.Platform.Windows;

try
{
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(graph, out var theme);
    return WindowsBootstrap.Run("Lucent Issue Browser — M3", composition, theme);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Lucent M0 startup failed: {exception.Message}");
    return 1;
}

public static class IssueBrowserStructure
{
    private static readonly Token<uint> PageSurface = new("page-surface", 0xfff8fafcU);
    private static readonly Token<uint> PageForeground = new("page-foreground", 0xff0f172aU);
    private static readonly Token<uint> HeaderSurface = new("header-surface", 0xffe2e8f0U);
    private static readonly Token<uint> RowSurface = new("row-surface", 0xffffffffU);
    private static readonly Token<uint> FocusSurface = new("focus-surface", 0xffffff00U);
    private static readonly Token<uint> FocusForeground = new("focus-foreground", 0xff0f172aU);
    private static readonly Theme LightTheme = Palette(ControlThemes.Light, 0xfff8fafcU, 0xff0f172aU, 0xffe2e8f0U, 0xffffffffU, 0xffffff00U, 0xff0f172aU);
    private static readonly Theme DarkTheme = Palette(ControlThemes.Dark, 0xff0f172aU, 0xfff8fafcU, 0xff1e293bU, 0xff111827U, 0xfffacc15U, 0xff0f172aU);
    private static readonly Theme HighContrastTheme = Palette(ControlThemes.HighContrast, 0xff000000U, 0xffffffffU, 0xff000000U, 0xff000000U, 0xffffff00U, 0xff000000U);

    public static Composition Create(ReactiveGraph graph) => Create(graph, out _);

    public static Composition Create(ReactiveGraph graph, out ThemeContext theme)
    {
        var composition = new Composition(graph, "issue-browser");
        var loading = composition.Root.Scope.Signal(true, "issue-browser.loading");
        var loadError = composition.Root.Scope.Signal<string?>(null, "issue-browser.error");
        var issues = composition.Root.Scope.Signal<Issue[]>([], "issue-browser.issues");
        var themeContext = new ThemeContext(composition.Root.Scope, LightTheme);
        theme = themeContext;
        _ = composition.Root.Scope.Effect(() => themeContext.Theme = themeContext.Appearance.Contrast == ThemeContrast.High ? HighContrastTheme : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark ? DarkTheme : LightTheme, "issue-browser-appearance");
        Controls.Column(composition.Root, themeContext, "Issue Browser", Style.Empty.Set(SceneProperties.Fill, PageSurface).Set(SceneProperties.Foreground, PageForeground).Set(Arrangement.Clip, true));

        var header = composition.Child(composition.Root, "issue-browser.header");
        Controls.Panel(header, themeContext, "Issue Browser header", Style.Empty.Set(Arrangement.Height, 52f).Set(Arrangement.Width, 800f).Set(SceneProperties.Fill, HeaderSurface));
        var title = composition.Child(header, "issue-browser.title");
        Controls.Text(title, themeContext, "Issues", Style.Empty.Set(Arrangement.Height, 24f).Set(SceneProperties.FontSize, 18f));

        _ = composition.When(composition.Root, "issue-browser.loading-region", () => loading.Value,
            Controls.Recipe("issue-browser.loading", (context, element) => Controls.Loading(element, themeContext, "Loading issues", Style.Empty.Set(Arrangement.Height, 28f))));
        _ = composition.When(composition.Root, "issue-browser.error-region", () => loadError.Value is not null,
            Controls.Recipe("issue-browser.error", (context, element) =>
            {
                Controls.Error(element, themeContext, loadError.Value!, Style.Empty.Set(Arrangement.Axis, LayoutAxis.Column));
                var retry = context.Child(element, "issue-browser.retry");
                Controls.Button(retry, themeContext, "Retry", () => { loadError.Value = null; loading.Value = true; }, Style.Empty.Set(Arrangement.Height, 30f));
            }));

        var viewport = composition.Child(composition.Root, "issue-browser.scroll-viewport");
        Controls.ScrollViewport(viewport, themeContext, "Issues", style: Style.Empty.Set(Arrangement.Width, 800f));
        var list = composition.ForEach(viewport, "issue-browser.issue-list", () => issues.Value,
            issue => issue.Number, (issue, context) =>
            {
                var row = context.Element("issue-browser.issue-row");
                Controls.Selectable(row, themeContext, "Issue " + issue.Number, style: Style.Empty.Set(Arrangement.Height, 30f).Set(Arrangement.Width, 800f).Set(SceneProperties.Fill, RowSurface)
                    .When(VariantState.FocusVisible, Style.Empty.Set(SceneProperties.Fill, FocusSurface).Set(SceneProperties.Foreground, FocusForeground)));
                return row;
            });
        Controls.List(list.Region, themeContext, "Issues");

        graph.Batch(() =>
        {
            issues.Value =
            [
                new(29, "Implement retained composition and structural ownership", "open"),
                new(28, "Implement reactive graph and scopes", "closed"),
                new(26, "Native 0.1 Milestone 1", "open")
            ];
            loading.Value = false;
        });
        return composition;
    }

    private sealed record Issue(int Number, string Title, string State);

    private static Theme Palette(Theme controls, uint surface, uint foreground, uint header, uint row, uint focus, uint focusForeground) => controls
        .Set(PageSurface, surface).Set(PageForeground, foreground).Set(HeaderSurface, header).Set(RowSurface, row).Set(FocusSurface, focus).Set(FocusForeground, focusForeground);
}
