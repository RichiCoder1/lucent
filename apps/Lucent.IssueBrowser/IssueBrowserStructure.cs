using System.Net;
using System.Text;

namespace Lucent.IssueBrowser;

public static class IssueBrowserStructure
{
    public static ComponentRecipe Create() =>
        Create(scope =>
        {
            IssueFixture.AssertIntegrity();
            var client = scope.Own(
                new HttpClient(new FixtureHttpHandler())
                {
                    BaseAddress = new Uri("https://api.github.local/"),
                }
            );
            return new(new GitHubIssueSource(client), new FixtureIssueStatusSource());
        });

    internal static Composition Create(ReactiveGraph graph) => Create(graph, out _);

    internal static Composition Create(ReactiveGraph graph, out ThemeContext theme)
    {
        var client = new HttpClient(new FixtureHttpHandler())
        {
            BaseAddress = new Uri("https://api.github.local/"),
        };
        return Create(
            graph,
            new GitHubIssueSource(client),
            new FixtureIssueStatusSource(),
            client,
            out _,
            out theme
        );
    }

    internal static Composition Create(
        ReactiveGraph graph,
        GitHubIssueSource source,
        out IssueBrowserState state,
        out ThemeContext theme
    ) => Create(graph, source, new FixtureIssueStatusSource(), null, out state, out theme);

    internal static Composition Create(
        ReactiveGraph graph,
        GitHubIssueSource source,
        IIssueStatusSource statusSource,
        out IssueBrowserState state,
        out ThemeContext theme
    ) => Create(graph, source, statusSource, null, out state, out theme);

    private static Composition Create(
        ReactiveGraph graph,
        GitHubIssueSource source,
        IIssueStatusSource statusSource,
        IDisposable? transport,
        out IssueBrowserState state,
        out ThemeContext theme
    )
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(statusSource);
        IssueFixture.AssertIntegrity();
        IssueBrowserState? capturedState = null;
        var composition = new Composition(graph, "issue-browser");
        composition.ConfigureImages(
            new ImageCache(new Lucent.Renderer.Skia.SkiaImagePreparer())
        );
        var themeContext = new ThemeContext(
            composition.Root.Scope,
            StockTheme(ThemeAppearance.Light)
        );
        theme = themeContext;
        InstallAppearanceTheme(composition, themeContext);
        _ = composition.Mount(
            composition.Root,
            themeContext,
            Create(
                scope =>
                {
                    if (transport is not null)
                        scope.Own(transport);
                    return new(source, statusSource);
                },
                browser => capturedState = browser
            )
        );
        state =
            capturedState
            ?? throw new InvalidOperationException(
                "Issue Browser state was not created during mount."
            );
        return composition;
    }

    private static ComponentRecipe Create(
        Func<ReactiveScope, BrowserDependencies> dependencies,
        Action<IssueBrowserState>? created = null
    ) =>
        ComponentRecipe.Create(
            "issue-browser-application",
            (context, root) =>
            {
                root.Present(context.Theme, author: Style.Empty.MainGrow(1).MainBasis(0));
                var configured = dependencies(root.Scope);
                var browser = new IssueBrowserState(
                    root.Scope,
                    configured.Issues,
                    configured.Statuses
                );
                created?.Invoke(browser);
                _ = context.Mount(root, Components.IssueBrowser(browser).Named("Issue Browser"));
            }
        );

    private static void InstallAppearanceTheme(Composition composition, ThemeContext theme)
    {
        var appliedAppearance = theme.Appearance;
        _ = composition.Root.Scope.Effect(
            () =>
            {
                var appearance = theme.Appearance;
                if (appearance == appliedAppearance)
                    return;
                var next = StockTheme(appearance);
                appliedAppearance = appearance;
                theme.Theme = next;
            },
            "issue-browser-appearance"
        );
    }

    private static Theme StockTheme(ThemeAppearance appearance) =>
        appearance.Contrast == ThemeContrast.High ? ControlThemes.HighContrast
        : appearance.ColorScheme == ThemeColorScheme.Dark ? ControlThemes.Dark
        : ControlThemes.Light;

    private readonly record struct BrowserDependencies(
        GitHubIssueSource Issues,
        IIssueStatusSource Statuses
    );

    private sealed class FixtureHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        IssueFixture.Json,
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
    }
}
