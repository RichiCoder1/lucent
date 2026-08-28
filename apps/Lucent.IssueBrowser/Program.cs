using Lucent.Core;
using Lucent.Platform.Windows;

try
{
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(graph);
    graph.Drain();
    return WindowsBootstrap.Run("Lucent Issue Browser — M0 (1.25x test presentation)");
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

    public static Composition Create(ReactiveGraph graph)
    {
        var composition = new Composition(graph, "issue-browser");
        var loading = composition.Root.Scope.Signal(true, "issue-browser.loading");
        var issues = composition.Root.Scope.Signal<Issue[]>([], "issue-browser.issues");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("issue-browser-light").Set(PageSurface, 0xfff8fafcU).Set(PageForeground, 0xff0f172aU));
        composition.Root.Present(theme, author: Style.Empty.Set(Surface, PageSurface).Set(Foreground, PageForeground));
        composition.Root.AttachBehaviors(new Semantics("root-semantics", new(SemanticRole.Group, "Issue Browser")));
        var header = composition.Child(composition.Root, "issue-browser.header");
        header.Present(theme, Style.Empty, Style.Empty.Set(Opacity, 1f));
        header.AttachBehaviors(new Semantics("header-semantics", new(SemanticRole.Group, "Issue Browser header")));
        var title = composition.Child(header, "issue-browser.title");
        title.Present(theme);
        title.AttachBehaviors(new Semantics("title-semantics", new(SemanticRole.Text, "Issues")));
        _ = composition.When(composition.Root, "issue-browser.loading-region", () => loading.Value,
            context =>
            {
                var element = context.Element("issue-browser.loading");
                element.Present(theme, author: Style.Empty.Set(Opacity, 1f));
                element.AttachBehaviors(new Semantics("loading-semantics", new(SemanticRole.Status, "Loading issues")));
                return element;
            });
        _ = composition.ForEach(composition.Root, "issue-browser.issue-list", () => issues.Value,
            issue => issue.Number, (issue, context) =>
            {
                var row = context.Element("issue-browser.issue-row");
                row.Present(theme,
                    Style.Empty.Set(Opacity, 1f).When(VariantState.Selected, Style.Empty.Set(Opacity, .9f)),
                    Style.Empty.When(VariantState.Selected, Style.Empty.Set(Opacity, .8f)));
                if (issue.Number == 29) row.SetVariants(VariantState.Selected);
                row.AttachBehaviors(new Semantics("issue-row-semantics", new(SemanticRole.ListItem, "Issue " + issue.Number, actions: SemanticAction.Select)));
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

    private sealed class Semantics(string name, SemanticDeclaration declaration) : Behavior
    {
        public override string Name => name;
        public override BehaviorOwnership Ownership => declaration.Actions == SemanticAction.None ? BehaviorOwnership.Semantics : BehaviorOwnership.Action | BehaviorOwnership.Semantics;
        public override void Attach(BehaviorContext context) => context.SetSemantics(declaration);
    }
}
