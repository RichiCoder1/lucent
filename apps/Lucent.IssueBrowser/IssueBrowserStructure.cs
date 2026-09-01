using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucent.Core;

namespace Lucent.IssueBrowser;

public static class IssueBrowserStructure
{
    internal static readonly Token<Brush> PageSurface = new("page-surface", Color.Parse("#f8fafc"));
    internal static readonly Token<Color> PageForeground = new("page-foreground", Color.Parse("#0f172a"));
    internal static readonly Token<Brush> HeaderSurface = new("header-surface", Color.Parse("#e2e8f0"));
    internal static readonly Token<Brush> RowSurface = new("row-surface", Color.Parse("#ffffff"));
    internal static readonly Token<Brush> FocusSurface = new("focus-surface", Color.Parse("#ffff00"));
    internal static readonly Token<Color> FocusForeground = new("focus-foreground", Color.Parse("#0f172a"));
    internal static readonly Token<float?> DensityHeaderHeight = new("issue-density-header-height", 84f);
    internal static readonly Token<float?> DensityFilterHeight = new("issue-density-filter-height", 28f);
    internal static readonly Token<float> DensitySpacing = new("issue-density-spacing", 8f);
    internal static readonly Token<float> DensityFontSize = new("issue-density-font-size", 14f);
    internal static readonly Token<float> DensityTitleFontSize = new("issue-density-title-font-size", 18f);

    public static Composition Create(ReactiveGraph graph) => Create(graph, out _);
    public static Composition Create(ReactiveGraph graph, out ThemeContext theme)
    {
        var handler = new FixtureHttpHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.local/") };
        return Create(graph, new GitHubIssueSource(client), new FixtureIssueStatusSource(), client, out _, out theme);
    }

    public static Composition Create(ReactiveGraph graph, GitHubIssueSource source, out IssueBrowserState state, out ThemeContext theme) => Create(graph, source, new FixtureIssueStatusSource(), null, out state, out theme);
    public static Composition Create(ReactiveGraph graph, GitHubIssueSource source, IIssueStatusSource statusSource, out IssueBrowserState state, out ThemeContext theme) => Create(graph, source, statusSource, null, out state, out theme);

    private static Composition Create(ReactiveGraph graph, GitHubIssueSource source, IIssueStatusSource statusSource, IDisposable? transport, out IssueBrowserState state, out ThemeContext theme)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(statusSource);
        IssueFixture.AssertIntegrity();
        var composition = new Composition(graph, "issue-browser");
        if (transport is not null) composition.Root.Scope.Own(transport);
        var browser = new IssueBrowserState(composition.Root.Scope, source, statusSource);
        var themeContext = new ThemeContext(composition.Root.Scope, Palette(ControlThemes.Light, Color.Parse("#f8fafc"), Color.Parse("#0f172a"), Color.Parse("#e2e8f0"), Color.Parse("#ffffff"), Color.Parse("#ffff00"), Color.Parse("#0f172a"), IssueDensity.Comfortable));
        theme = themeContext;
        _ = composition.Root.Scope.Effect(() => themeContext.Theme = Palette(themeContext.Appearance.Contrast == ThemeContrast.High ? ControlThemes.HighContrast : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark ? ControlThemes.Dark : ControlThemes.Light,
            themeContext.Appearance.Contrast == ThemeContrast.High ? Color.Parse("#000000") : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark ? Color.Parse("#0f172a") : Color.Parse("#f8fafc"),
            themeContext.Appearance.Contrast == ThemeContrast.High ? Color.Parse("#ffffff") : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark ? Color.Parse("#f8fafc") : Color.Parse("#0f172a"),
            themeContext.Appearance.Contrast == ThemeContrast.High ? Color.Parse("#000000") : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark ? Color.Parse("#1e293b") : Color.Parse("#e2e8f0"),
            themeContext.Appearance.Contrast == ThemeContrast.High ? Color.Parse("#000000") : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark ? Color.Parse("#111827") : Color.Parse("#ffffff"),
            themeContext.Appearance.Contrast == ThemeContrast.High ? Color.Parse("#ffff00") : themeContext.Appearance.ColorScheme == ThemeColorScheme.Dark ? Color.Parse("#facc15") : Color.Parse("#ffff00"),
            themeContext.Appearance.Contrast == ThemeContrast.High ? Color.Parse("#000000") : Color.Parse("#0f172a"), browser.Density), "issue-browser-appearance");
        _ = composition.Mount(composition.Root, themeContext, Components.IssueBrowser(browser).Named("Issue Browser"));
        state = browser;
        return composition;
    }

    private static Theme Palette(Theme controls, Color surface, Color foreground, Color header, Color row, Color focus, Color focusForeground, IssueDensity density) => controls
        .Set(PageSurface, (Brush)surface).Set(PageForeground, foreground).Set(HeaderSurface, (Brush)header).Set(RowSurface, (Brush)row).Set(FocusSurface, (Brush)focus).Set(FocusForeground, focusForeground)
        .Set(DensityHeaderHeight, density == IssueDensity.Comfortable ? 84f : 68f).Set(DensityFilterHeight, density == IssueDensity.Comfortable ? 28f : 22f)
        .Set(DensitySpacing, density == IssueDensity.Comfortable ? 8f : 4f).Set(DensityFontSize, density == IssueDensity.Comfortable ? 14f : 12f).Set(DensityTitleFontSize, density == IssueDensity.Comfortable ? 18f : 16f);

    private sealed class FixtureHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(IssueFixture.Json, Encoding.UTF8, "application/json") });
    }
}
