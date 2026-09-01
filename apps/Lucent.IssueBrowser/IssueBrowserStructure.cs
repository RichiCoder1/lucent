using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lucent.IssueBrowser;

public static class IssueBrowserStructure
{
    public static Composition Create(ReactiveGraph graph) => Create(graph, out _);

    public static Composition Create(ReactiveGraph graph, out ThemeContext theme)
    {
        var handler = new FixtureHttpHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.local/") };
        return Create(
            graph,
            new GitHubIssueSource(client),
            new FixtureIssueStatusSource(),
            client,
            out _,
            out theme
        );
    }

    public static Composition Create(
        ReactiveGraph graph,
        GitHubIssueSource source,
        out IssueBrowserState state,
        out ThemeContext theme
    ) => Create(graph, source, new FixtureIssueStatusSource(), null, out state, out theme);

    public static Composition Create(
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
        var composition = new Composition(graph, "issue-browser");
        if (transport is not null)
            composition.Root.Scope.Own(transport);
        var browser = new IssueBrowserState(composition.Root.Scope, source, statusSource);
        var themeContext = new ThemeContext(
            composition.Root.Scope,
            AppTheme.Create(
                ControlThemes.Light,
                Color.Parse("#f8fafc"),
                Color.Parse("#0f172a"),
                Color.Parse("#e2e8f0"),
                Color.Parse("#ffffff"),
                Color.Parse("#ffff00"),
                Color.Parse("#0f172a"),
                IssueDensity.Comfortable
            )
        );
        theme = themeContext;
        _ = composition.Root.Scope.Effect(
            () =>
                themeContext.Theme = AppTheme.Create(
                    themeContext.Appearance.Contrast == ThemeContrast.High
                            ? ControlThemes.HighContrast
                        : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark
                            ? ControlThemes.Dark
                        : ControlThemes.Light,
                    themeContext.Appearance.Contrast == ThemeContrast.High ? Color.Parse("#000000")
                        : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark
                            ? Color.Parse("#0f172a")
                        : Color.Parse("#f8fafc"),
                    themeContext.Appearance.Contrast == ThemeContrast.High ? Color.Parse("#ffffff")
                        : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark
                            ? Color.Parse("#f8fafc")
                        : Color.Parse("#0f172a"),
                    themeContext.Appearance.Contrast == ThemeContrast.High ? Color.Parse("#000000")
                        : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark
                            ? Color.Parse("#1e293b")
                        : Color.Parse("#e2e8f0"),
                    themeContext.Appearance.Contrast == ThemeContrast.High ? Color.Parse("#000000")
                        : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark
                            ? Color.Parse("#111827")
                        : Color.Parse("#ffffff"),
                    themeContext.Appearance.Contrast == ThemeContrast.High ? Color.Parse("#ffff00")
                        : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark
                            ? Color.Parse("#facc15")
                        : Color.Parse("#ffff00"),
                    themeContext.Appearance.Contrast == ThemeContrast.High
                        ? Color.Parse("#000000")
                        : Color.Parse("#0f172a"),
                    browser.Density
                ),
            "issue-browser-appearance"
        );
        _ = composition.Mount(
            composition.Root,
            themeContext,
            Components.IssueBrowser(browser).Named("Issue Browser")
        );
        state = browser;
        return composition;
    }

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
