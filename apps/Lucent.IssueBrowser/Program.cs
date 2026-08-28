using Lucent.Core;
using Lucent.Platform.Windows;

try
{
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(graph, out var theme);
    graph.Drain();
    return WindowsBootstrap.Run("Lucent Issue Browser — M2", composition, theme, graph.Drain);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Lucent M0 startup failed: {exception.Message}");
    return 1;
}

public static class IssueBrowserStructure
{
    private static readonly Property<uint> Surface = new("surface", 0xfff8fafcU, transition: TransitionKind.Color);
    private static readonly Property<uint> Foreground = new("foreground", 0xff0f172aU, inherits: true, transition: TransitionKind.Color);
    private static readonly Property<float> Opacity = new("opacity", 1f, transition: TransitionKind.Opacity);
    private static readonly Token<uint> PageSurface = new("page-surface", 0xfff8fafcU);
    private static readonly Token<uint> PageForeground = new("page-foreground", 0xff0f172aU);
    private static readonly Token<uint> HeaderSurface = new("header-surface", 0xffe2e8f0U);
    private static readonly Token<uint> RowSurface = new("row-surface", 0xffffffffU);
    private static readonly Token<uint> FocusSurface = new("focus-surface", 0xffffff00U);
    private static readonly Token<uint> FocusForeground = new("focus-foreground", 0xff0f172aU);
    private static readonly Theme LightTheme = Palette("issue-browser-light", 0xfff8fafcU, 0xff0f172aU, 0xffe2e8f0U, 0xffffffffU, 0xffffff00U, 0xff0f172aU);
    private static readonly Theme DarkTheme = Palette("issue-browser-dark", 0xff0f172aU, 0xfff8fafcU, 0xff1e293bU, 0xff111827U, 0xfffacc15U, 0xff0f172aU);
    private static readonly Theme HighContrastTheme = Palette("issue-browser-high-contrast", 0xff000000U, 0xffffffffU, 0xff000000U, 0xff000000U, 0xffffff00U, 0xff000000U);

    public static Composition Create(ReactiveGraph graph) => Create(graph, out _);

    public static Composition Create(ReactiveGraph graph, out ThemeContext theme)
    {
        var composition = new Composition(graph, "issue-browser");
        var loading = composition.Root.Scope.Signal(true, "issue-browser.loading");
        var issues = composition.Root.Scope.Signal<Issue[]>([], "issue-browser.issues");
        var themeContext = new ThemeContext(composition.Root.Scope, LightTheme);
        theme = themeContext;
        _ = composition.Root.Scope.Effect(() => themeContext.Theme = themeContext.Appearance.Contrast == ThemeContrast.High ? HighContrastTheme : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark ? DarkTheme : LightTheme, "issue-browser-appearance");
        composition.Root.Present(themeContext, author: Style.Empty.Set(Surface, PageSurface).Set(Foreground, PageForeground)
            .Set(SceneProperties.Fill, PageSurface).Set(SceneProperties.Foreground, PageForeground)
            .Set(Arrangement.Axis, LayoutAxis.Column).Set(Arrangement.Clip, true));
        composition.Root.AttachBehaviors(new Semantics("root-semantics", new(SemanticRole.Group, "Issue Browser")));
        var header = composition.Child(composition.Root, "issue-browser.header");
        header.Present(themeContext, Style.Empty, Style.Empty.Set(Opacity, 1f).Set(Arrangement.Height, 52f).Set(Arrangement.Width, 800f)
            .Set(Arrangement.Axis, LayoutAxis.Column).Set(SceneProperties.Fill, HeaderSurface));
        header.AttachBehaviors(new Semantics("header-semantics", new(SemanticRole.Group, "Issue Browser header")));
        var title = composition.Child(header, "issue-browser.title");
        title.Present(themeContext, author: Style.Empty.Set(Arrangement.Height, 24f).Set(SceneProperties.Text, "Issues").Set(SceneProperties.FontSize, 18f));
        title.AttachBehaviors(new Semantics("title-semantics", new(SemanticRole.Text, "Issues")));
        _ = composition.When(composition.Root, "issue-browser.loading-region", () => loading.Value,
            context =>
            {
                var element = context.Element("issue-browser.loading");
                element.Present(themeContext, author: Style.Empty.Set(Opacity, 1f).Set(Arrangement.Height, 28f).Set(SceneProperties.Text, "Loading issues"));
                element.AttachBehaviors(new Semantics("loading-semantics", new(SemanticRole.Status, "Loading issues")));
                return element;
            });
        _ = composition.ForEach(composition.Root, "issue-browser.issue-list", () => issues.Value,
            issue => issue.Number, (issue, context) =>
            {
                var row = context.Element("issue-browser.issue-row");
                row.Present(themeContext,
                    Style.Empty.Set(Opacity, 1f).Set(Arrangement.Height, 30f).Set(Arrangement.Width, 800f).Set(SceneProperties.Fill, RowSurface)
                        .Set(SceneProperties.Text, "Issue " + issue.Number).When(VariantState.Selected, Style.Empty.Set(Opacity, .9f)).When(VariantState.FocusVisible, Style.Empty.Set(SceneProperties.Fill, FocusSurface).Set(SceneProperties.Foreground, FocusForeground)),
                    Style.Empty.When(VariantState.Selected, Style.Empty.Set(Opacity, .8f)));
                row.AttachBehaviors(new RowActionBehavior("issue-row-action", new(SemanticRole.ListItem, "Issue " + issue.Number, actions: SemanticAction.Select)));
                return row;
            });

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

    private static Theme Palette(string name, uint surface, uint foreground, uint header, uint row, uint focus, uint focusForeground) => new Theme(name)
        .Set(PageSurface, surface).Set(PageForeground, foreground).Set(HeaderSurface, header).Set(RowSurface, row).Set(FocusSurface, focus).Set(FocusForeground, focusForeground);

    private sealed class Semantics(string name, SemanticDeclaration declaration) : Behavior
    {
        public override string Name => name;
        public override BehaviorOwnership Ownership => declaration.Actions == SemanticAction.None ? BehaviorOwnership.Semantics : BehaviorOwnership.Action | BehaviorOwnership.Semantics;
        public override void Attach(BehaviorContext context) => context.SetSemantics(declaration);
    }
}
