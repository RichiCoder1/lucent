using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Lucent.Core;
using Lucent.IssueBrowser;
using Lucent.Renderer.Skia;
using SkiaSharp;

try
{
    FixtureIdentity();
    RecipeEvidence();
    DirectRootParity();
    VirtualizedIssueRowParity();
    ReactiveFilterBarParity();
    AsyncBrowserStates();
    DensityRestyle();
    OptimisticStatusMutations();
    GeneratedMutationSemantics();
    SourceExceptionsBecomeTransientFailures();
    VisualSurface();
    DisposedRequestCannotCommit();
    StartupStaysFrameworkOwned();
    Console.WriteLine("Lucent.IssueBrowser async/data contract: PASS");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("Lucent.IssueBrowser async/data contract: FAIL: " + exception.Message);
    return 1;
}

static void FixtureIdentity()
{
    IssueFixture.AssertIntegrity();
    Assert(
        IssueFixture.Issues.Count == IssueFixture.TotalCount
            && IssueFixture.Issues.Count(issue => issue.Status == "open") == IssueFixture.OpenCount
            && IssueFixture.Issues.Count(issue => issue.Status == "closed")
                == IssueFixture.ClosedCount,
        "Frozen issue fixture identity or counts changed."
    );
}

static void RecipeEvidence()
{
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(graph, out _);
    graph.Drain();
    using var renderer = new SkiaSceneRenderer();
    _ = SceneLayout.Project(composition, new(800, 500, 1), renderer);
    var semantics = composition.SemanticDump();
    var dump = composition.Dump();
    Assert(
        dump.Contains("name=\"issue-browser.filters\"", StringComparison.Ordinal)
            && dump.Contains("name=\"issue-browser.issue-row\"", StringComparison.Ordinal)
            && Flatten(composition.SemanticSnapshot()!)
                .Any(node => node.Role == SemanticRole.TextField && node.Name == "Search issues")
            && Flatten(composition.SemanticSnapshot()!)
                .Count(node => node.Role == SemanticRole.ListItem) <= 9,
        "C# Filter Bar or virtual Issue Row recipe structure regressed."
    );
    Assert(
        Hash(dump + semantics)
            == "307d21bd80059fc5a25690653b0f621cc09b15dd417a737ffdc037079aaf6389",
        "Issue Browser composition/semantic evidence changed: " + Hash(dump + semantics)
    );
}

static void DirectRootParity()
{
    var issue = IssueFixture.Issues[0];
    var generatedFilter = DirectRootEvidence(
        browser => Lucent.IssueBrowser.Components.FilterBar(browser),
        issue
    );
    var generatedFilterNullStyle = DirectRootEvidence(
        browser => Lucent.IssueBrowser.Components.FilterBar(browser, null),
        issue
    );
    var handwrittenFilter = DirectRootEvidence(
        browser => HandwrittenParityFixture.Create(browser),
        issue
    );
    var generatedRow = DirectRootEvidence(
        browser => Lucent.IssueBrowser.Components.IssueRow(browser, issue),
        issue
    );
    var handwrittenRow = DirectRootEvidence(
        browser => HandwrittenParityFixture.CreateRow(browser, issue),
        issue
    );
    Assert(
        generatedFilter == generatedFilterNullStyle
            && generatedFilter == handwrittenFilter
            && generatedRow == handwrittenRow,
        "Generated .lui direct roots diverged from the retained handwritten C# parity fixtures or null root style changed the default."
    );
    Assert(
        FilterBarInputEvidence(browser => Lucent.IssueBrowser.Components.FilterBar(browser))
            == FilterBarInputEvidence(browser => HandwrittenParityFixture.Create(browser)),
        "Generated and handwritten Filter Bars diverged after ordinary retained text input."
    );
    var evidence =
        generatedFilter.Dump
        + generatedFilter.Semantics
        + generatedRow.Dump
        + generatedRow.Semantics;
    Assert(
        Hash(evidence) == "d48b9c8d8865a14594924ae7da5818d8ae680ad129245b906b8c2bdf006f5d39",
        "Approved direct-root #48 parity rebaseline changed: " + Hash(evidence)
    );
}

static void VirtualizedIssueRowParity()
{
    var generated = VirtualizedIssueRowEvidence(Lucent.IssueBrowser.Components.IssueRow);
    var handwritten = VirtualizedIssueRowEvidence(HandwrittenParityFixture.CreateRow);
    Assert(
        generated == handwritten,
        $"Generated virtual Issue Rows diverged from the test-owned C# fixture: dump={generated.Dump == handwritten.Dump} semantics={generated.Semantics == handwritten.Semantics} scene={generated.Scene == handwritten.Scene} pixels={generated.Pixels == handwritten.Pixels} identity={generated.ReorderedElementId == handwritten.ReorderedElementId}."
    );
    Assert(
        generated.InitialRows <= 6
            && generated.RemainingRows <= 6
            && generated.ReorderedElementId != 0,
        "The 10,000-row generated Issue Row list exceeded its realization bound or lost keyed identity."
    );
}

static VirtualizedIssueRowEvidence VirtualizedIssueRowEvidence(
    Func<IssueBrowserState, BrowserIssue, ComponentRecipe> row
)
{
    var graph = new ReactiveGraph();
    using var composition = new Composition(graph, "issue-row-virtual-parity");
    using var theme = new ThemeContext(
        composition.Root.Scope,
        new Theme("issue-row-virtual-parity")
    );
    using var handler = new DeferredGitHubHandler();
    using var client = new HttpClient(handler)
    {
        BaseAddress = new Uri("https://api.github.local/"),
    };
    var browser = new IssueBrowserState(composition.Root.Scope, new GitHubIssueSource(client));
    var values = composition.Root.Scope.Signal<IReadOnlyList<BrowserIssue>>(
        Array.Empty<BrowserIssue>(),
        "issue-row-virtual-parity.values"
    );
    _ = composition.Mount(
        composition.Root,
        theme,
        Lucent.Core.Components.VirtualizedList(
            () => values.Value,
            issue => issue.Number,
            issue => row(browser, issue).Named("issue-browser.issue-row"),
            () => 30f,
            "Issues",
            Style.Empty.Width(800f).Height(60f)
        )
    );
    values.Value = browser.VisibleIssues;
    graph.Drain();
    handler.ReplyJson(0);
    graph.Drain();
    values.Value = browser.VisibleIssues;
    graph.Drain();
    Assert(
        values.Value.Count == IssueFixture.TotalCount,
        "The virtual Issue Row parity fixture did not load all 10,000 issues."
    );

    using var renderer = new SkiaSceneRenderer();
    var scene = SceneLayout.Project(composition, new(800, 60, 1), renderer);
    Assert(composition.Input.SetScene(scene), "The virtual Issue Row parity scene was rejected.");
    var initialRows = Flatten(composition.SemanticSnapshot()!)
        .Count(node => node.Role == SemanticRole.ListItem);
    var initial = IssueRow(composition, 10_000, "initial");
    var dump = composition.Dump();
    var semantics = SemanticEvidence(composition.SemanticSnapshot()!);
    var sceneEvidence = SceneEvidence(scene);
    var pixels = Pixels(renderer, scene, 800, 60);

    Assert(
        composition.ExecuteSemanticCommand(initial.Identity, new(SemanticCommandKind.Select))
            == SemanticCommandResult.Applied
            && composition.Input.FocusSemantic(
                new(initial.Identity.CompositionEpoch, initial.Identity.ElementId)
            ),
        "The generated virtual Issue Row did not accept semantic selection and focus."
    );
    graph.Drain();
    scene = SceneLayout.Project(composition, new(800, 60, 1), renderer);
    Assert(composition.Input.SetScene(scene), "The selected virtual Issue Row scene was rejected.");
    var selected = IssueRow(composition, 10_000, "selected");
    Assert(
        selected.Selected
            && composition.Input.FocusedElement?.ElementId == selected.Identity.ElementId
            && composition.IsCurrent(selected.Identity),
        "The selected virtual Issue Row did not retain current focus and selection semantics."
    );

    var reorderedValues = values.Value.ToArray();
    (reorderedValues[0], reorderedValues[1]) = (reorderedValues[1], reorderedValues[0]);
    values.Value = reorderedValues;
    graph.Drain();
    scene = SceneLayout.Project(composition, new(800, 60, 1), renderer);
    Assert(
        composition.Input.SetScene(scene),
        "The reordered virtual Issue Row scene was rejected."
    );
    var reordered = IssueRow(composition, 10_000, "reordered");
    Assert(
        reordered.Identity.ElementId == selected.Identity.ElementId
            && reordered.Selected
            && composition.Input.FocusedElement?.ElementId == selected.Identity.ElementId
            && composition.IsCurrent(selected.Identity),
        "Keyed virtual Issue Row reorder changed runtime identity, focus, selection, or current semantics."
    );

    values.Value = values.Value.Skip(2).ToArray();
    graph.Drain();
    scene = SceneLayout.Project(composition, new(800, 60, 1), renderer);
    Assert(composition.Input.SetScene(scene), "The removed virtual Issue Row scene was rejected.");
    var remainingRows = Flatten(composition.SemanticSnapshot()!)
        .Count(node => node.Role == SemanticRole.ListItem);
    Assert(
        !Flatten(composition.SemanticSnapshot()!)
            .Any(node => node.Name.StartsWith("#10000 ", StringComparison.Ordinal))
            && !composition.IsCurrent(reordered.Identity)
            && composition.ExecuteSemanticCommand(
                reordered.Identity,
                new(SemanticCommandKind.Select)
            ) == SemanticCommandResult.Stale
            && composition.Input.FocusedElement?.ElementId != reordered.Identity.ElementId,
        "Removed virtual Issue Row retained current, focus, or semantic command access."
    );
    return new(
        sceneEvidence,
        dump,
        semantics,
        pixels,
        initialRows,
        remainingRows,
        reordered.Identity.ElementId
    );
}

static SemanticSnapshot IssueRow(Composition composition, int number, string phase)
{
    var row = Flatten(composition.SemanticSnapshot()!)
        .SingleOrDefault(node =>
            node.Role == SemanticRole.ListItem
            && node.Name.StartsWith("#" + number + " ", StringComparison.Ordinal)
        );
    if (row is null)
        throw new InvalidOperationException(
            "The " + phase + " virtual Issue Row was not realized."
        );
    return row;
}

static void ReactiveFilterBarParity()
{
    var generated = ReactiveFilterBarEvidence(
        (browser, style) => Lucent.IssueBrowser.Components.FilterBar(browser, style)
    );
    var handwritten = ReactiveFilterBarEvidence(HandwrittenParityFixture.Create);
    Assert(
        generated == handwritten,
        $"Generated Filter Bar diverged from the test-owned handwritten fixture after an idle reactive style completion: beforeDump={generated.BeforeDump == handwritten.BeforeDump} afterDump={generated.AfterDump == handwritten.AfterDump} beforeSemantics={generated.BeforeSemantics == handwritten.BeforeSemantics} afterSemantics={generated.AfterSemantics == handwritten.AfterSemantics} beforeScene={generated.BeforeScene == handwritten.BeforeScene} afterScene={generated.AfterScene == handwritten.AfterScene} beforePixels={generated.BeforePixels == handwritten.BeforePixels} afterPixels={generated.AfterPixels == handwritten.AfterPixels}."
    );
    Assert(
        generated.BeforeDump == generated.AfterDump
            && generated.BeforeSemantics == generated.AfterSemantics
            && generated.RootId == generated.AfterRootId,
        "Reactive Filter Bar completion changed retained structure, semantics, or identity."
    );
    Assert(
        generated.AfterScene.Contains("brush=solid(#123456FF)", StringComparison.Ordinal)
            && generated.AfterScene.Contains("opacity=0.5", StringComparison.Ordinal)
            && generated.BeforePixels != generated.AfterPixels,
        "Reactive Filter Bar did not publish the expected Background, Padding, Opacity, and pixels."
    );
}

static ReactiveFilterEvidence ReactiveFilterBarEvidence(
    Func<IssueBrowserState, Style?, ComponentRecipe> recipe
)
{
    var graph = new ReactiveGraph();
    using var composition = new Composition(graph, "filter-bar-reactive-parity");
    using var theme = new ThemeContext(
        composition.Root.Scope,
        new Theme("filter-bar-reactive-parity")
    );
    using var client = new HttpClient(new DeferredGitHubHandler())
    {
        BaseAddress = new Uri("https://api.github.local/"),
    };
    var browser = new IssueBrowserState(composition.Root.Scope, new GitHubIssueSource(client));
    var completion = new TaskCompletionSource<FilterBarAppearance>(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    var initial = new FilterBarAppearance(Color.Parse("#000000"), Insets.Zero, 1f);
    AsyncValue<FilterBarAppearance>? appearance = null;
    var host = ComponentRecipe.Create(
        "filter-bar-reactive-host",
        (context, root) =>
        {
            appearance = root.Scope.Async(
                _ => completion.Task,
                initial,
                "filter-bar-reactive-appearance"
            );
            context.Mount(
                root,
                recipe(
                    browser,
                    Style
                        .Empty.Background(() => appearance!.Value.Background)
                        .Padding(() => appearance!.Value.Padding)
                        .Opacity(() => appearance!.Value.Opacity)
                )
            );
        }
    );
    var hostRoot = composition.Mount(composition.Root, theme, host);
    var filterRoot = hostRoot.Children.Single();
    graph.Drain();
    Assert(
        graph.Dump().Contains("name=\"filter-bar-reactive-appearance\"", StringComparison.Ordinal)
            && graph
                .Dump()
                .Contains(
                    "name=\"filter-bar-reactive-appearance\" scope=2 deps=[] dirty=false generation=1 pending=true",
                    StringComparison.Ordinal
                ),
        "Filter Bar style bindings did not start their live async source."
    );
    var beforeDump = composition.Dump();
    var beforeSemantics = SemanticEvidence(composition.SemanticSnapshot()!);
    using var renderer = new SkiaSceneRenderer();
    var beforeScene = SceneLayout.Project(composition, new(800, 80, 1), renderer);
    var beforePixels = Pixels(renderer, beforeScene, 800, 80);
    var wakes = 0;
    Action wake = () => Interlocked.Increment(ref wakes);
    graph.WorkAvailable += wake;
    try
    {
        Task.Run(() => completion.SetResult(new(Color.Parse("#123456"), Insets.Uniform(2), .5f)))
            .GetAwaiter()
            .GetResult();
        Assert(
            SpinWait.SpinUntil(() => Volatile.Read(ref wakes) == 1, 2_000),
            "Idle reactive completion did not raise its work notification."
        );
        var frame = composition.Flush();
        var idle = composition.Flush();
        Assert(
            wakes == 1 && frame && !idle,
            $"Idle reactive completion did not coalesce to exactly one wake and frame: wakes={wakes} frame={frame} idle={idle}."
        );
    }
    finally
    {
        graph.WorkAvailable -= wake;
    }
    var afterDump = composition.Dump();
    var afterSemantics = SemanticEvidence(composition.SemanticSnapshot()!);
    var afterScene = SceneLayout.Project(composition, new(800, 80, 1), renderer);
    var afterPixels = Pixels(renderer, afterScene, 800, 80);
    Assert(
        filterRoot.Resolve(VisualProperties.Background).Value.Equals((Brush)Color.Parse("#123456"))
            && filterRoot.Resolve(LayoutProperties.Padding).Value == Insets.Uniform(2)
            && filterRoot.Resolve(VisualProperties.Opacity).Value == .5f,
        $"Reactive Filter Bar bindings did not resolve through the public style seam: background={filterRoot.Resolve(VisualProperties.Background).Value} padding={filterRoot.Resolve(LayoutProperties.Padding).Value} opacity={filterRoot.Resolve(VisualProperties.Opacity).Value}."
    );
    Assert(
        filterRoot.Children.Count == 3,
        "Reactive Filter Bar completion changed its retained child structure."
    );

    AssertDisposedReactiveFilterBar(recipe);

    return new(
        filterRoot.Id,
        filterRoot.Id,
        beforeDump,
        afterDump,
        beforeSemantics,
        afterSemantics,
        SceneEvidence(beforeScene),
        SceneEvidence(afterScene),
        beforePixels,
        afterPixels
    );
}

static void AssertDisposedReactiveFilterBar(Func<IssueBrowserState, Style?, ComponentRecipe> recipe)
{
    var graph = new ReactiveGraph();
    using var composition = new Composition(graph, "filter-bar-disposal-parity");
    using var theme = new ThemeContext(
        composition.Root.Scope,
        new Theme("filter-bar-disposal-parity")
    );
    using var client = new HttpClient(new DeferredGitHubHandler())
    {
        BaseAddress = new Uri("https://api.github.local/"),
    };
    var browser = new IssueBrowserState(composition.Root.Scope, new GitHubIssueSource(client));
    var completion = new TaskCompletionSource<FilterBarAppearance>();
    var initial = new FilterBarAppearance(Color.Parse("#000000"), Insets.Zero, 1f);
    var host = ComponentRecipe.Create(
        "filter-bar-disposed-host",
        (context, root) =>
        {
            var source = root.Scope.Async(
                _ => completion.Task,
                initial,
                "filter-bar-disposed-appearance"
            );
            context.Mount(
                root,
                recipe(
                    browser,
                    Style
                        .Empty.Background(() => source.Value.Background)
                        .Padding(() => source.Value.Padding)
                        .Opacity(() => source.Value.Opacity)
                )
            );
        }
    );
    var hostRoot = composition.Mount(composition.Root, theme, host);
    graph.Drain();
    var wakes = 0;
    Action wake = () => Interlocked.Increment(ref wakes);
    graph.WorkAvailable += wake;
    try
    {
        hostRoot.Dispose();
        var producer = new Thread(() =>
            completion.SetResult(new(Color.Parse("#abcdef"), Insets.Uniform(3), .25f))
        );
        producer.Start();
        Assert(
            producer.Join(2_000),
            "Disposed Filter Bar completion did not reach its bounded producer barrier."
        );
        Assert(
            wakes == 0 && !composition.Flush() && composition.Root.Children.Count == 0,
            "Disposed Filter Bar scope accepted a late async style completion."
        );
    }
    finally
    {
        graph.WorkAvailable -= wake;
    }
}

static (string Dump, string Semantics) DirectRootEvidence(
    Func<IssueBrowserState, ComponentRecipe> recipe,
    BrowserIssue issue
)
{
    var graph = new ReactiveGraph();
    using var handler = new DeferredGitHubHandler();
    using var client = new HttpClient(handler)
    {
        BaseAddress = new Uri("https://api.github.local/"),
    };
    using var composition = IssueBrowserStructure.Create(
        graph,
        new GitHubIssueSource(client),
        out var browser,
        out var theme
    );
    graph.Drain();
    handler.ReplyJson(0);
    graph.Drain();
    var root = composition.Mount(composition.Root, theme, recipe(browser));
    graph.Drain();
    var dump = composition.Dump();
    var start = dump.LastIndexOf("element " + root.Id + " ", StringComparison.Ordinal);
    var semantic = Flatten(composition.SemanticSnapshot()!)
        .Single(snapshot => snapshot.Identity.ElementId == root.Id);
    return (dump.Substring(start), SemanticEvidence(semantic));
}

static (string State, string Dump, string Semantics) FilterBarInputEvidence(
    Func<IssueBrowserState, ComponentRecipe> recipe
)
{
    var graph = new ReactiveGraph();
    using var composition = new Composition(graph, "filter-bar-input-parity");
    using var theme = new ThemeContext(
        composition.Root.Scope,
        new Theme("filter-bar-input-parity")
    );
    using var client = new HttpClient(new DeferredGitHubHandler())
    {
        BaseAddress = new Uri("https://api.github.local/"),
    };
    var browser = new IssueBrowserState(composition.Root.Scope, new GitHubIssueSource(client));
    var root = composition.Mount(composition.Root, theme, recipe(browser));
    graph.Drain();
    using var renderer = new SkiaSceneRenderer();
    Assert(
        composition.Input.SetScene(SceneLayout.Project(composition, new(800, 80, 1), renderer)),
        "Filter Bar input scene was rejected."
    );
    var rootSemantic = Flatten(composition.SemanticSnapshot()!)
        .Single(snapshot => snapshot.Identity.ElementId == root.Id);
    var fields = Flatten(rootSemantic)
        .Where(snapshot => snapshot.Role == SemanticRole.TextField)
        .ToDictionary(snapshot => snapshot.Name, StringComparer.Ordinal);
    foreach (
        var (label, value) in new[]
        {
            ("Search issues", "native"),
            ("Status: all, open, closed", "closed"),
            ("Assignee: all, marta, devin, joel", "marta"),
        }
    )
    {
        Assert(
            fields.TryGetValue(label, out var field)
                && composition.ExecuteSemanticCommand(
                    field.Identity,
                    new(SemanticCommandKind.SetValue, value)
                ) == SemanticCommandResult.Applied,
            "Filter Bar rejected ordinary retained text input for " + label + "."
        );
        graph.Drain();
    }
    Assert(
        browser.Search == "native" && browser.Status == "closed" && browser.Assignee == "marta",
        "Filter Bar onChange handlers did not update all browser filters."
    );
    rootSemantic = Flatten(composition.SemanticSnapshot()!)
        .Single(snapshot => snapshot.Identity.ElementId == root.Id);
    return (
        browser.Search + "|" + browser.Status + "|" + browser.Assignee,
        composition.Dump(),
        SemanticEvidence(rootSemantic)
    );
}

static string SemanticEvidence(SemanticSnapshot snapshot) =>
    snapshot.Role
    + "|"
    + snapshot.Name
    + "|"
    + snapshot.Value
    + "|"
    + snapshot.Enabled
    + "|"
    + snapshot.Focused
    + "|"
    + snapshot.Selected
    + "|"
    + snapshot.Actions
    + "["
    + string.Join(",", snapshot.Children.Select(SemanticEvidence))
    + "]";

static void AsyncBrowserStates()
{
    using var transport = new DeferredGitHubHandler();
    using var client = new HttpClient(transport)
    {
        BaseAddress = new Uri("https://api.github.local/"),
    };
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(
        graph,
        new GitHubIssueSource(client),
        out var browser,
        out _
    );
    graph.Drain();

    Assert(
        transport.Requests.Count == 1
            && transport.Requests[0]
                == "GET /repos/RichiCoder1/lucent/issues?state=all&per_page=100|application/vnd.github+json|Lucent-IssueBrowser/0.1",
        "GitHub adapter request contract changed or used a real transport."
    );
    Assert(
        browser.IsLoading
            && !browser.IsStale
            && browser.Issues.Count == 0
            && browser.Error is null
            && Flatten(composition.SemanticSnapshot()!)
                .Any(node => node.Role == SemanticRole.Status && node.Name == "Loading issues"),
        "Initial loading state was not externally visible."
    );
    using var initialRenderer = new SkiaSceneRenderer();
    var initial = SceneLayout.Project(composition, new(800, 500, 1), initialRenderer);
    Assert(composition.Input.SetScene(initial), "Initial issue-browser scene did not install.");
    var search = Flatten(composition.SemanticSnapshot()!)
        .Single(node => node.Role == SemanticRole.TextField && node.Name == "Search issues");
    Assert(
        composition.Input.FocusSemantic(
            new(search.Identity.CompositionEpoch, search.Identity.ElementId)
        ),
        "Initial loading scene rejected Search focus."
    );

    transport.ReplyJson(0);
    graph.Drain();
    using var renderer = new SkiaSceneRenderer();
    var scene = SceneLayout.Project(composition, new(800, 500, 1), renderer);
    Assert(
        composition.Input.SetScene(scene)
            && !browser.IsLoading
            && !browser.IsStale
            && browser.Error is null
            && browser.Issues.Count == 10_000
            && browser.VisibleIssues.Count == 10_000
            && Flatten(composition.SemanticSnapshot()!)
                .Count(node => node.Role == SemanticRole.ListItem) <= 7,
        "Successful response did not build a bounded virtual issue browser list."
    );

    browser.Search = "native";
    graph.Drain();
    Assert(
        browser.VisibleIssues.Count > 1
            && browser.VisibleIssues.All(issue =>
                (issue.Title + " " + issue.Labels + " " + issue.Body).Contains(
                    "native",
                    StringComparison.OrdinalIgnoreCase
                )
            ),
        "Search result content was not deterministic."
    );
    browser.Status = "closed";
    graph.Drain();
    Assert(
        browser.VisibleIssues.All(issue => issue.Status == "closed"),
        "Status filter did not compose with search."
    );
    browser.Search = "";
    browser.Status = "open";
    browser.Assignee = "marta";
    graph.Drain();
    Assert(
        browser.VisibleIssues.Count > 1
            && browser.VisibleIssues.All(issue =>
                issue.Status == "open" && issue.Assignee == "marta"
            ),
        "Status and assignee filters did not return deterministic results."
    );
    browser.Assignee = "all";
    browser.Status = "all";
    graph.Drain();

    scene = SceneLayout.Project(composition, new(800, 500, 1), renderer);
    Assert(composition.Input.SetScene(scene), "Filtered virtual list did not install.");
    var viewport = Flatten(composition.SemanticSnapshot()!)
        .Single(node => node.Name == "Issues" && node.Actions.HasFlag(SemanticAction.Scroll));
    Assert(
        composition.Input.ScrollSemantic(
            new(viewport.Identity.CompositionEpoch, viewport.Identity.ElementId),
            new(SemanticCommandKind.Scroll, Endpoint: SemanticScrollEndpoint.End)
        ),
        "Virtual list could not scroll to its end."
    );
    graph.Drain();
    scene = SceneLayout.Project(composition, new(800, 500, 1), renderer);
    Assert(composition.Input.SetScene(scene), "End virtual list scene did not install.");
    var row = Flatten(composition.SemanticSnapshot()!)
        .Single(node =>
            node.Role == SemanticRole.ListItem
            && node.Name.StartsWith("#1 ", StringComparison.Ordinal)
        );
    Assert(
        composition.ExecuteSemanticCommand(row.Identity, new(SemanticCommandKind.Select))
            == SemanticCommandResult.Applied,
        "Issue row semantic selection was rejected."
    );
    graph.Drain();
    Assert(
        browser.SelectedIssue?.Number == 1
            && composition.Dump().Contains("issue-browser.details", StringComparison.Ordinal),
        "Selection did not expose issue details."
    );

    browser.Retry();
    graph.Drain();
    Assert(
        transport.Requests.Count == 2
            && browser.IsLoading
            && browser.IsStale
            && browser.Issues.Count == 10_000,
        "Retry did not retain stale data while loading."
    );
    transport.ReplyStatus(1, HttpStatusCode.ServiceUnavailable);
    graph.Drain();
    Assert(
        !browser.IsLoading
            && browser.IsStale == false
            && browser.Error is not null
            && browser.Issues.Count == 10_000
            && composition.Dump().Contains("issue-browser.error", StringComparison.Ordinal),
        "Failed retry did not retain stale data and expose error."
    );

    browser.Retry();
    graph.Drain();
    Assert(
        transport.Requests.Count == 3
            && browser.IsLoading
            && browser.IsStale
            && browser.Error is null,
        "Error retry did not return to stale loading state."
    );
    browser.Retry();
    graph.Drain();
    Assert(
        transport.Requests.Count == 4
            && transport.Cancellations >= 1
            && browser.IsLoading
            && browser.Issues.Count == 10_000,
        "Replacement request did not cancel prior work."
    );
    transport.ReplyJson(2);
    graph.Drain();
    Assert(
        browser.IsLoading && browser.Issues.Count == 10_000,
        "Ignored-cancellation response committed out of order."
    );
    transport.ReplyJson(3);
    graph.Drain();
    Assert(
        !browser.IsLoading
            && browser.Error is null
            && browser.Issues.Count == 10_000
            && browser.SelectedIssue?.Number == 1,
        "Latest retry response did not commit deterministically."
    );
}

static void DisposedRequestCannotCommit()
{
    using var transport = new DeferredGitHubHandler();
    using var client = new HttpClient(transport)
    {
        BaseAddress = new Uri("https://api.github.local/"),
    };
    var graph = new ReactiveGraph();
    var composition = IssueBrowserStructure.Create(
        graph,
        new GitHubIssueSource(client),
        out _,
        out _
    );
    graph.Drain();
    Assert(
        transport.Requests.Count == 1,
        "Disposed-request proof did not start its local request."
    );
    composition.Dispose();
    transport.ReplyJson(0);
    graph.Drain();
    Assert(
        transport.Cancellations == 1
            && !graph.Dump().Contains("issue-browser", StringComparison.Ordinal),
        "Disposed request retained or committed browser state."
    );
}

static void DensityRestyle()
{
    using var transport = new DeferredGitHubHandler();
    using var client = new HttpClient(transport)
    {
        BaseAddress = new Uri("https://api.github.local/"),
    };
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(
        graph,
        new GitHubIssueSource(client),
        out var browser,
        out var theme
    );
    graph.Drain();
    transport.ReplyJson(0);
    graph.Drain();
    using var renderer = new SkiaSceneRenderer();
    var viewport = new LayoutViewport(800, 500, 1);
    var scene = SceneLayout.Project(composition, viewport, renderer);
    Assert(composition.Input.SetScene(scene), "Density baseline scene did not install.");
    var scroll = Flatten(composition.SemanticSnapshot()!)
        .Single(node => node.Name == "Issues" && node.Actions.HasFlag(SemanticAction.Scroll));
    Assert(
        composition.Input.ScrollSemantic(
            new(scroll.Identity.CompositionEpoch, scroll.Identity.ElementId),
            new(SemanticCommandKind.Scroll, Vertical: 1507)
        ),
        "Density proof could not reach a mid-list offset."
    );
    graph.Drain();
    scene = SceneLayout.Project(composition, viewport, renderer);
    Assert(composition.Input.SetScene(scene), "Density mid-list scene did not install.");
    var comfortable = TopRow(scene, composition, "density comfortable");
    Assert(
        composition.ExecuteSemanticCommand(
            comfortable.Semantic.Identity,
            new(SemanticCommandKind.Select)
        ) == SemanticCommandResult.Applied
            && composition.Input.FocusSemantic(
                new(
                    comfortable.Semantic.Identity.CompositionEpoch,
                    comfortable.Semantic.Identity.ElementId
                )
            ),
        "Density proof could not select and focus its top row."
    );
    graph.Drain();
    scene = SceneLayout.Project(composition, viewport, renderer);
    Assert(composition.Input.SetScene(scene), "Density focused scene did not install.");
    comfortable = TopRow(scene, composition, "density focused comfortable");
    var focused = composition.Input.FocusedElement;
    var density = Flatten(composition.SemanticSnapshot()!)
        .Single(node =>
            node.Role == SemanticRole.Button && node.Name == "Density: Comfortable/Compact"
        );
    Assert(
        composition.ExecuteSemanticCommand(density.Identity, new(SemanticCommandKind.Invoke))
            == SemanticCommandResult.Applied,
        "Keyboard/UIA density button rejected Invoke."
    );
    graph.Drain();
    scene = SceneLayout.Project(composition, viewport, renderer);
    Assert(composition.Input.SetScene(scene), "Compact density scene did not install.");
    var compact = TopRow(scene, composition, "density compact");
    Assert(
        compact.Number == comfortable.Number
            && MathF.Abs(compact.Relative - comfortable.Relative) < .001f
            && compact.Height == 22f
            && composition.Input.FocusedElement == focused
            && compact.Semantic.Identity == comfortable.Semantic.Identity
            && composition.IsCurrent(comfortable.Semantic.Identity),
        "Compact density lost its scroll anchor, focus, UIA identity, or typed row restyle: comfortable="
            + comfortable
            + " compact="
            + compact
            + " focused="
            + focused
            + "/"
            + composition.Input.FocusedElement
            + "."
    );
    Assert(
        Flatten(composition.SemanticSnapshot()!).Count(node => node.Role == SemanticRole.ListItem)
            <= 9
            && scene
                .Boxes.Single(box => box.Identity.ElementId == compact.Semantic.Identity.ElementId)
                .Text!.Runs.Single()
                .FontSize == 12f,
        "Compact density exceeded its bounded realization or retained comfortable typography."
    );
    Assert(
        composition.ExecuteSemanticCommand(density.Identity, new(SemanticCommandKind.Invoke))
            == SemanticCommandResult.Applied,
        "Compact density button identity was stale."
    );
    graph.Drain();
    scene = SceneLayout.Project(composition, viewport, renderer);
    Assert(
        composition.Input.SetScene(scene),
        "Restored comfortable density scene did not install."
    );
    var restored = TopRow(scene, composition, "density restored comfortable");
    Assert(
        restored.Number == comfortable.Number
            && MathF.Abs(restored.Relative - comfortable.Relative) < .001f
            && restored.Height == 30f
            && restored.Semantic.Identity == comfortable.Semantic.Identity
            && composition.Input.FocusedElement == focused,
        "Comfortable restoration changed the retained row, offset, focus, or UIA identity."
    );
    foreach (
        var appearance in new[]
        {
            new ThemeAppearance(ThemeColorScheme.Light, ThemeContrast.Normal),
            new ThemeAppearance(ThemeColorScheme.Dark, ThemeContrast.Normal),
            new ThemeAppearance(ThemeColorScheme.Light, ThemeContrast.High),
        }
    )
    {
        theme.Appearance = appearance;
        graph.Drain();
        scene = SceneLayout.Project(composition, viewport, renderer);
        Assert(
            composition.Input.SetScene(scene),
            "Density style did not remain installable for " + appearance + "."
        );
    }
}

static void OptimisticStatusMutations()
{
    using var transport = new DeferredGitHubHandler();
    using var client = new HttpClient(transport)
    {
        BaseAddress = new Uri("https://api.github.local/"),
    };
    var saves = new DeferredStatusSource();
    var graph = new ReactiveGraph();
    var composition = IssueBrowserStructure.Create(
        graph,
        new GitHubIssueSource(client),
        saves,
        out var browser,
        out _
    );
    graph.Drain();
    transport.ReplyJson(0);
    graph.Drain();
    Assert(
        new FixtureIssueStatusSource().SaveAsync(3, "closed", default).Result
            is IssueStatusSaveOutcome.Rejected
            && new FixtureIssueStatusSource().SaveAsync(4, "closed", default).Result
                is IssueStatusSaveOutcome.TransientFailure
            && new FixtureIssueStatusSource().SaveAsync(5, "closed", default).Result
                is IssueStatusSaveOutcome.Saved,
        "The ordinary local status source no longer exposes deterministic rejection, transient, and success paths."
    );
    browser.Select(10_000);
    browser.ToggleSelectedStatus();
    graph.Drain();
    Assert(
        browser.SelectedIssue?.Status == "closed"
            && saves.Requests.Count == 1
            && !browser.CanRetrySelected
            && !HasRetry(composition),
        "Status save was not optimistic or exposed Retry before a transient result."
    );
    browser.Search = "native";
    browser.Select(9_999);
    browser.ToggleSelectedStatus();
    graph.Drain();
    Assert(
        saves.Requests.Count == 2 && saves.Cancellations == 0,
        "Filtering, selection, or an unrelated issue cancelled an application save."
    );
    browser.Search = "";
    browser.Select(10_000);
    browser.ToggleSelectedStatus();
    graph.Drain();
    Assert(
        saves.Requests.Count == 3
            && saves.Cancellations >= 1
            && browser.SelectedIssue?.Status == "open",
        "A newer per-issue generation did not replace the optimistic status."
    );
    saves.Reply(0, new IssueStatusSaveOutcome.Saved());
    graph.Drain();
    Assert(
        browser.SelectedIssue?.Status == "open",
        "Stale save completion committed over the newest issue generation."
    );
    saves.Reply(2, new IssueStatusSaveOutcome.Rejected("policy"));
    graph.Drain();
    Assert(
        browser.SelectedIssue?.Status == "closed"
            && browser.SelectedMutationMessage == "Rejected: policy"
            && !browser.CanRetrySelected
            && !HasRetry(composition),
        "Rejected save did not roll back with its reason or hid Retry."
    );
    browser.Select(9_999);
    saves.Reply(1, new IssueStatusSaveOutcome.TransientFailure("offline"));
    graph.Drain();
    Assert(
        browser.SelectedMutationMessage == "Not synced: offline"
            && browser.SelectedIssue is { } optimistic
            && optimistic.Status
                != IssueFixture.Issues.First(issue => issue.Number == optimistic.Number).Status
            && browser.CanRetrySelected
            && HasRetry(composition),
        "Transient save did not retain the optimistic status and expose Retry."
    );
    browser.RetrySelected();
    graph.Drain();
    Assert(
        saves.Requests.Count == 4 && !browser.CanRetrySelected && !HasRetry(composition),
        "Manual retry did not start exactly one save and hide Retry while pending."
    );
    saves.Reply(3, new IssueStatusSaveOutcome.Saved());
    graph.Drain();
    Assert(
        browser.SelectedMutationMessage is null
            && !browser.CanRetrySelected
            && !HasRetry(composition),
        "Successful retry retained a transient failure message or Retry action."
    );
    browser.ToggleSelectedStatus();
    graph.Drain();
    composition.Dispose();
    Assert(
        saves.Cancellations >= 2
            && !graph.Dump().Contains("issue-browser.mutation", StringComparison.Ordinal),
        "Application disposal did not cancel and release outstanding issue saves."
    );
}

static void GeneratedMutationSemantics()
{
    using var transport = new DeferredGitHubHandler();
    using var client = new HttpClient(transport)
    {
        BaseAddress = new Uri("https://api.github.local/"),
    };
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(
        graph,
        new GitHubIssueSource(client),
        new FixtureIssueStatusSource(),
        out var browser,
        out _
    );
    graph.Drain();
    transport.ReplyJson(0);
    graph.Drain();
    browser.Select(10_000);
    graph.Drain();
    var action = Flatten(composition.SemanticSnapshot()!)
        .Single(node => node.Role == SemanticRole.Button && node.Name == "Open/Close");
    Assert(
        composition.ExecuteSemanticCommand(action.Identity, new(SemanticCommandKind.Invoke))
            == SemanticCommandResult.Applied,
        "Generated Details rejected its ordinary semantic status action."
    );
    graph.Drain();
    var semantics = Flatten(composition.SemanticSnapshot()!);
    Assert(
        semantics.Any(node =>
            node.Role == SemanticRole.Status
            && node.Name == "closed · Not synced: Fixture source is temporarily unavailable."
        ) && semantics.Any(node => node.Role == SemanticRole.Button && node.Name == "Retry"),
        "Generated Details did not publish transient status and Retry semantics."
    );
}

static void SourceExceptionsBecomeTransientFailures()
{
    using var transport = new DeferredGitHubHandler();
    using var client = new HttpClient(transport)
    {
        BaseAddress = new Uri("https://api.github.local/"),
    };
    var saves = new DeferredStatusSource();
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(
        graph,
        new GitHubIssueSource(client),
        saves,
        out var browser,
        out _
    );
    graph.Drain();
    transport.ReplyJson(0);
    graph.Drain();
    browser.Select(10_000);
    browser.ToggleSelectedStatus();
    graph.Drain();
    saves.Fail(0, new InvalidOperationException("first fault"));
    graph.Drain();
    Assert(
        browser.SelectedIssue?.Status == "closed"
            && browser.SelectedMutationMessage == "Not synced: Unexpected save failure: first fault"
            && browser.CanRetrySelected,
        "A first source exception did not become a retryable transient failure while retaining optimism."
    );
    browser.RetrySelected();
    graph.Drain();
    saves.Reply(1, new IssueStatusSaveOutcome.Saved());
    graph.Drain();
    Assert(
        browser.SelectedIssue?.Status == "closed"
            && browser.SelectedMutationMessage is null
            && !browser.CanRetrySelected,
        "A successful retry did not clear its transient state."
    );
    browser.ToggleSelectedStatus();
    graph.Drain();
    saves.Fail(2, new InvalidOperationException(new string('x', 200)));
    graph.Drain();
    Assert(
        browser.SelectedIssue?.Status == "open"
            && browser.SelectedMutationMessage is { } message
            && message.StartsWith("Not synced: Unexpected save failure: ", StringComparison.Ordinal)
            && message.Length <= 200
            && browser.CanRetrySelected,
        "A fault after a saved value reused the old value or exposed an unbounded reason."
    );
}

static void VisualSurface()
{
    using var transport = new DeferredGitHubHandler();
    using var client = new HttpClient(transport)
    {
        BaseAddress = new Uri("https://api.github.local/"),
    };
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(
        graph,
        new GitHubIssueSource(client),
        out _,
        out var theme
    );
    graph.Drain();
    transport.ReplyJson(0);
    graph.Drain();
    using var renderer = new SkiaSceneRenderer();
    var light = SceneLayout.Project(composition, new(800, 500, 1.25f), renderer);
    var router = composition.Input;
    Assert(
        router.SetScene(light)
            && router.MoveFocus(FocusTraversalDirection.Next)
            && router.FocusedElement is not null,
        "First keyboard focus target was unavailable."
    );
    var focused = router.FocusedElement!.Value;
    var bounds = light.Boxes.Single(box => box.Identity == focused).Bounds;
    Assert(
        bounds.X <= 20
            && bounds.Y <= 35
            && bounds.X + bounds.Width > 20
            && bounds.Y + bounds.Height > 35,
        "First keyboard focus target no longer covers the published smoke coordinate."
    );
    Assert(
        SceneLayout
            .Project(composition, new(800, 500, 1.25f), renderer)
            .Dump()
            .Contains("brush=solid(#FFFF00FF)", StringComparison.Ordinal),
        "Keyboard focus did not produce a visible focus paint."
    );
    theme.Appearance = new(ThemeColorScheme.Dark, ThemeContrast.Normal);
    graph.Drain();
    var dark = SceneLayout.Project(composition, new(800, 500, 1.25f), renderer);
    theme.Appearance = new(ThemeColorScheme.Light, ThemeContrast.High);
    graph.Drain();
    var high = SceneLayout.Project(composition, new(800, 500, 1.25f), renderer);
    Assert(
        light.Dump().Contains("brush=solid(#F8FAFCFF)", StringComparison.Ordinal)
            && dark.Dump().Contains("brush=solid(#0F172AFF)", StringComparison.Ordinal)
            && dark.Dump().Contains("brush=solid(#FACC15FF)", StringComparison.Ordinal)
            && high.Dump().Contains("brush=solid(#000000FF)", StringComparison.Ordinal)
            && !light.Dump().Contains("Native IME", StringComparison.Ordinal),
        "Issue Browser appearance or diagnostic-safe retained scene regressed."
    );
    using var bitmap = new SKBitmap(1000, 625, SKColorType.Rgba8888, SKAlphaType.Premul);
    using (var canvas = new SKCanvas(bitmap))
    {
        canvas.Clear(SKColors.Transparent);
        renderer.Render(light, canvas);
    }
    Assert(
        Enumerable.Range(0, bitmap.Width).Any(x => bitmap.GetPixel(x, 0).Alpha != 0),
        "Issue Browser retained scene did not paint."
    );
}

static void StartupStaysFrameworkOwned()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        var app = Path.Combine(directory.FullName, "apps", "Lucent.IssueBrowser");
        if (Directory.Exists(app))
        {
            var source = string.Join(
                '\n',
                Directory.EnumerateFiles(app, "*.cs").Select(File.ReadAllText)
            );
            var issueRow = File.ReadAllText(Path.Combine(app, "IssueRow.lui"));
            var issueBrowser = File.ReadAllText(Path.Combine(app, "IssueBrowser.lui"));
            Assert(
                !source.Contains(".Drain(", StringComparison.Ordinal)
                    && !source.Contains("test mode", StringComparison.OrdinalIgnoreCase)
                    && !source.Contains("IssueRowHandwritten", StringComparison.Ordinal)
                    && !Regex.IsMatch(source, @"\bComponentRecipe\s+IssueRow\s*\(")
                    && issueRow.Contains("public component IssueRow", StringComparison.Ordinal)
                    && !issueRow.Contains("VirtualizedList", StringComparison.Ordinal)
                    && issueBrowser.Contains(
                        "public component IssueBrowser",
                        StringComparison.Ordinal
                    )
                    && issueBrowser.Contains(
                        "<VirtualizedList source={() => browser.VisibleIssues} key={issue => issue.Number} row={issue => IssueRow(browser, issue).Named(\"issue-browser.issue-row\")}",
                        StringComparison.Ordinal
                    )
                    && issueBrowser.Contains(
                        "foreach (var issue in browser.SelectedIssue is { } selected ? [selected] : Array.Empty<BrowserIssue>()) keyed by issue.Number",
                        StringComparison.Ordinal
                    ),
                "Production Issue Browser no longer owns its generated virtual row factory and keyed detail region in .lui."
            );
            return;
        }
        directory = directory.Parent;
    }
    throw new InvalidOperationException("Could not locate Issue Browser startup source.");
}

static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot snapshot)
{
    yield return snapshot;
    foreach (var child in snapshot.Children)
    foreach (var node in Flatten(child))
        yield return node;
}

static TopVisibleRow TopRow(RetainedScene scene, Composition composition, string phase)
{
    var scroll = Flatten(composition.SemanticSnapshot()!)
        .Single(node => node.Name == "Issues" && node.Actions.HasFlag(SemanticAction.Scroll));
    var viewport = scene
        .Boxes.Single(box => box.Identity.ElementId == scroll.Identity.ElementId)
        .Bounds;
    var candidates = Flatten(composition.SemanticSnapshot()!)
        .Where(node => node.Role == SemanticRole.ListItem)
        .Select(node => new
        {
            Semantic = node,
            Box = scene.Boxes.Single(box => box.Identity.ElementId == node.Identity.ElementId),
        })
        .Where(candidate =>
            candidate.Box.Bounds.Y <= viewport.Y
            && candidate.Box.Bounds.Y + candidate.Box.Bounds.Height > viewport.Y
        )
        .ToArray();
    var number =
        candidates.Length == 1
        && int.TryParse(candidates[0].Semantic.Name.Split(' ')[0].TrimStart('#'), out var parsed)
            ? parsed
            : 0;
    Assert(number != 0, phase + " did not retain one top visible issue row.");
    return new(
        number,
        candidates[0].Box.Bounds.Y - viewport.Y,
        candidates[0].Box.Bounds.Height,
        candidates[0].Semantic
    );
}

static bool HasRetry(Composition composition) =>
    Flatten(composition.SemanticSnapshot()!)
        .Any(node => node.Role == SemanticRole.Button && node.Name == "Retry");

static string Hash(string value) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

static string Pixels(SkiaSceneRenderer renderer, RetainedScene scene, int width, int height)
{
    using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Transparent);
    renderer.Render(scene, canvas);
    using var pixels = bitmap.PeekPixels();
    return Convert.ToHexString(SHA256.HashData(pixels.GetPixelSpan()));
}

static string SceneEvidence(RetainedScene scene) =>
    Regex.Replace(
        Regex.Replace(scene.Dump(), @"epoch=\d+", "epoch=*"),
        @"inputSignature=[^ ]+",
        "inputSignature=*"
    );

static void Assert(bool value, string message)
{
    if (!value)
        throw new InvalidOperationException(message);
}

sealed class DeferredGitHubHandler : HttpMessageHandler
{
    private readonly List<TaskCompletionSource<HttpResponseMessage>> _responses = [];
    public List<string> Requests { get; } = [];
    public int Cancellations { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Requests.Add(
            request.Method
                + " "
                + request.RequestUri!.PathAndQuery
                + "|"
                + string.Join(',', request.Headers.Accept.Select(header => header.MediaType))
                + "|"
                + string.Join(
                    ' ',
                    request.Headers.UserAgent.Select(header =>
                        header.Product?.Name + "/" + header.Product?.Version
                    )
                )
        );
        cancellationToken.Register(() => Cancellations++);
        var response = new TaskCompletionSource<HttpResponseMessage>();
        _responses.Add(response);
        return response.Task;
    }

    public void ReplyJson(int index) =>
        Task.Run(() => _responses[index].SetResult(Response(HttpStatusCode.OK, IssueFixture.Json)))
            .GetAwaiter()
            .GetResult();

    public void ReplyStatus(int index, HttpStatusCode status) =>
        Task.Run(() => _responses[index].SetResult(Response(status, "{}")))
            .GetAwaiter()
            .GetResult();

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

sealed class DeferredStatusSource : IIssueStatusSource
{
    private readonly List<TaskCompletionSource<IssueStatusSaveOutcome>> _responses = [];
    public List<(int Number, string Status)> Requests { get; } = [];
    public int Cancellations { get; private set; }

    public Task<IssueStatusSaveOutcome> SaveAsync(
        int issueNumber,
        string status,
        CancellationToken cancellationToken
    )
    {
        Requests.Add((issueNumber, status));
        cancellationToken.Register(() => Cancellations++);
        var response = new TaskCompletionSource<IssueStatusSaveOutcome>();
        _responses.Add(response);
        return response.Task;
    }

    public void Reply(int index, IssueStatusSaveOutcome outcome) =>
        Task.Run(() => _responses[index].SetResult(outcome)).GetAwaiter().GetResult();

    public void Fail(int index, Exception error) =>
        Task.Run(() => _responses[index].SetException(error)).GetAwaiter().GetResult();
}

readonly record struct TopVisibleRow(
    int Number,
    float Relative,
    float Height,
    SemanticSnapshot Semantic
);

readonly record struct FilterBarAppearance(Brush Background, Insets Padding, float Opacity)
{
    public FilterBarAppearance(Color background, Insets padding, float opacity)
        : this((Brush)background, padding, opacity) { }
}

readonly record struct ReactiveFilterEvidence(
    long RootId,
    long AfterRootId,
    string BeforeDump,
    string AfterDump,
    string BeforeSemantics,
    string AfterSemantics,
    string BeforeScene,
    string AfterScene,
    string BeforePixels,
    string AfterPixels
);

readonly record struct VirtualizedIssueRowEvidence(
    string Scene,
    string Dump,
    string Semantics,
    string Pixels,
    int InitialRows,
    int RemainingRows,
    long ReorderedElementId
);

internal static class HandwrittenParityFixture
{
    private static readonly Style FilterBarStyle = Style
        .Empty.Width(800f)
        .Height(Tokens.DensityFilterHeight)
        .Spacing(Tokens.DensitySpacing);
    private static readonly Style TextFieldStyle = Style.Empty.Width(250f).Height(24f);

    internal static ComponentRecipe Create(IssueBrowserState browser, Style? style = null)
    {
        ArgumentNullException.ThrowIfNull(browser);
        return Lucent.Core.Components.Row(
            [
                Lucent
                    .Core.Components.TextField(
                        onChange: browser.SetSearch,
                        style: TextFieldStyle,
                        label: "Search issues"
                    )
                    .Named("issue-browser.search"),
                Lucent
                    .Core.Components.TextField(
                        onChange: browser.SetStatus,
                        style: TextFieldStyle,
                        label: "Status: all, open, closed"
                    )
                    .Named("issue-browser.status"),
                Lucent
                    .Core.Components.TextField(
                        onChange: browser.SetAssignee,
                        style: TextFieldStyle,
                        label: "Assignee: all, marta, devin, joel"
                    )
                    .Named("issue-browser.assignee"),
            ],
            FilterBarStyle.With(style)
        );
    }

    internal static ComponentRecipe CreateRow(IssueBrowserState browser, BrowserIssue issue)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(issue);
        var style = Style
            .Empty.Width(800f)
            .Spacing(Tokens.DensitySpacing)
            .FontSize(Tokens.DensityFontSize)
            .Background(Tokens.RowSurface)
            .When(
                VariantState.FocusVisible,
                Style.Empty.Background(Tokens.FocusSurface).TextColor(Tokens.FocusForeground)
            )
            .With(
                Style.Empty.Height(() => browser.Density == IssueDensity.Comfortable ? 30f : 22f)
            );
        return Lucent.Core.Components.Selectable(
            () => Label(browser, issue),
            () => browser.IsSelected(issue.Number),
            () => browser.Select(issue.Number),
            style
        );
    }

    private static string Label(IssueBrowserState browser, BrowserIssue issue)
    {
        var current = browser.Issues.First(candidate => candidate.Number == issue.Number);
        return $"#{current.Number} {current.Title} — {current.Status} · {current.Assignee}";
    }
}
