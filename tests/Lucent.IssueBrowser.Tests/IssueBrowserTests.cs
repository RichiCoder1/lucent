using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Lucent.Core;
using Lucent.IssueBrowser;
using Lucent.Renderer.Skia;
using SkiaSharp;

namespace Lucent.IssueBrowser.Tests;

[TestClass]
public sealed class IssueBrowserTests
{
    [TestMethod]
    public void FixtureIdentity()
    {
        IssueFixture.AssertIntegrity();
        Assert(
            IssueFixture.Issues.Count == IssueFixture.TotalCount
                && IssueFixture.Issues.Count(issue => issue.Status == "open")
                    == IssueFixture.OpenCount
                && IssueFixture.Issues.Count(issue => issue.Status == "closed")
                    == IssueFixture.ClosedCount,
            "Frozen issue fixture identity or counts changed."
        );
    }

    [TestMethod]
    public void ApplicationLifecycleEntryPoint()
    {
        var host = new ApplicationHostProbe();
        var result = LucentApplication
            .CreateBuilder()
            .UseHost(host)
            .SetTitle("Issue Browser Probe")
            .SetTheme(_ => ControlThemes.Light)
            .Build()
            .Run(IssueBrowserStructure.Create());
        Assert(
            result == 23,
            "The public Issue Browser application recipe lost the host exit code."
        );
        Assert(
            host.Title == "Issue Browser Probe",
            "The Issue Browser application title was not hosted."
        );
        Assert(
            host.ObservedApplicationRoot,
            "The public application recipe did not mount its fill root and generated browser."
        );
        Assert(
            host.CompositionDisposed,
            "The public Issue Browser composition was not released after hosting."
        );
    }

    [TestMethod]
    public void RecipeEvidence()
    {
        var graph = new ReactiveGraph();
        using var composition = IssueBrowserStructure.Create(graph, out _);
        graph.Drain();
        using var renderer = new SkiaSceneRenderer();
        var scene = SceneLayout.Project(composition, new(800, 500, 1), renderer);
        var dump = composition.Dump();
        Assert(
            dump.Contains("name=\"issue-browser.filters\"", StringComparison.Ordinal)
                && dump.Contains("name=\"issue-browser.issue-row\"", StringComparison.Ordinal)
                && Flatten(composition.SemanticSnapshot()!)
                    .Any(node =>
                        node.Role == SemanticRole.TextField && node.Name == "Search issues"
                    )
                && Flatten(composition.SemanticSnapshot()!)
                    .Count(node => node.Role == SemanticRole.ListItem)
                    <= RealizedRowBound(scene, composition, 30f),
            "C# Filter Bar or virtual Issue Row recipe structure regressed."
        );
        var nodes = Flatten(composition.SemanticSnapshot()!).ToArray();
        Assert(
            nodes
                .Where(node => node.Role == SemanticRole.TextField)
                .Select(node => node.Name)
                .SequenceEqual([
                    "Search issues",
                    "Status: all, open, closed",
                    "Assignee: all, marta, devin, joel",
                ])
                && nodes.Any(node => node.Role == SemanticRole.ListItem)
                && nodes.Count(node => node.Role == SemanticRole.ListItem)
                    <= RealizedRowBound(scene, composition, 30f)
                && scene.ScrollBars is [{ Maximum.Y: > 0 }],
            "Issue Browser filters, bounded list semantics or default scroll affordance changed."
        );
    }

    [TestMethod]
    public void CompiledFilterBarUsesHoistedEditors()
    {
        using var composition = LoadedComposition(out var graph, out var browser);
        using var renderer = new SkiaSceneRenderer();
        Install(composition, renderer, new(700, 600, 1));
        var before = Fields(composition);
        SetValue(composition, before["Search issues"], "native");
        graph.Drain();
        Install(composition, renderer, new(1120, 760, 1));
        var after = Fields(composition);
        Assert(
            browser.Search == "native",
            "The hoisted search editor did not update browser state."
        );
        Assert(
            after["Search issues"].Value == "native",
            "Responsive remount cleared the hoisted search editor."
        );
        SetValue(composition, after["Search issues"], "no fixture can match this query");
        graph.Drain();
        var narrow = new LayoutViewport(420, 360, 1);
        var empty = Install(composition, renderer, narrow);
        var emptySemantics = Flatten(composition.SemanticSnapshot()!).ToArray();
        Assert(
            emptySemantics.Any(node => node.Name == "No matching issues")
                && emptySemantics.Any(node =>
                    node.Role == SemanticRole.Button && node.Name == "Clear filters"
                )
                && empty.Boxes.All(box =>
                    box.Bounds.X >= -.01f && box.Bounds.X + box.Bounds.Width <= narrow.Width + .01f
                ),
            "The compact empty result lost its explanation, recovery action, or horizontal bounds."
        );
        CaptureIfRequested(renderer, empty, narrow, "issue-browser-empty-narrow.png");
    }

    [TestMethod]
    public void VirtualizedRowsRemainBoundedAndAccessible()
    {
        using var composition = LoadedComposition(out _, out _);
        using var renderer = new SkiaSceneRenderer();
        var scene = Install(composition, renderer, new(1120, 760, 1));
        var rows = Flatten(composition.SemanticSnapshot()!)
            .Where(node => node.Role == SemanticRole.ListItem)
            .ToArray();
        Assert(
            rows.Length <= RealizedRowBound(scene, composition, 58)
                && rows.Length > 0
                && rows.All(row =>
                    row.Name.StartsWith('#') && row.Name.Contains(" — ") && row.Name.Contains(" · ")
                ),
            "The 10,000-row list exceeded its realization bound or lost the accessible row label."
        );
    }

    [TestMethod]
    public void CompiledIssueRowContextMenuTargetsRightClickedIssue()
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
        transport.ReplyJson(0);
        graph.Drain();
        using var renderer = new SkiaSceneRenderer();

        RetainedScene Install()
        {
            var next = SceneLayout.Project(composition, new(1120, 760, 1), renderer);
            Assert(
                composition.Input.SetScene(next),
                "Issue Browser context-menu scene was rejected."
            );
            return next;
        }

        var scene = Install();
        var rows = Flatten(composition.SemanticSnapshot()!)
            .Where(node => node.Role == SemanticRole.ListItem)
            .Take(3)
            .ToArray();
        Assert(rows.Length == 3, "The Issue Browser did not realize three context-menu rows.");
        var firstNumber = IssueNumber(rows[0]);
        var secondNumber = IssueNumber(rows[2]);
        Assert(
            composition.ExecuteSemanticCommand(rows[0].Identity, new(SemanticCommandKind.Select))
                == SemanticCommandResult.Applied,
            "The first Issue Browser row could not be selected before opening its sibling menu."
        );
        graph.Drain();
        scene = Install();
        rows = Flatten(composition.SemanticSnapshot()!)
            .Where(node => node.Role == SemanticRole.ListItem)
            .Take(3)
            .ToArray();

        ContextMenuRequest? request = null;
        composition.Input.ContextMenuRequested += value => request = value;
        var bounds = scene
            .Boxes.Single(box => box.Identity.ElementId == rows[2].Identity.ElementId)
            .Bounds;
        var x = bounds.X + MathF.Max(1, bounds.Width / 2);
        var y = bounds.Y + MathF.Max(1, bounds.Height / 2);
        Assert(
            composition
                .Input.DispatchPointer(
                    new(PointerCommandKind.Down, 91, x, y, PointerButton.Secondary)
                )
                .Handled
                && composition.Input.DispatchPointer(new(PointerCommandKind.Up, 91, x, y)).Handled,
            "The Issue Browser did not route a secondary click to the generated row menu target."
        );
        Assert(request is not null, "The generated row menu did not publish a request.");
        using var menuRequest = request!;
        Assert(
            scene.Boxes.Any(box => box.Identity == request!.Target),
            "The context-menu request did not retain the right-clicked row target."
        );
        Assert(
            browser.SelectedIssue?.Number == firstNumber,
            "Opening a row context menu changed the existing selection before invocation."
        );
        using var popup = request!.CreateComposition();
        popup.Flush();
        var descriptor = request.StandardMenu;
        Assert(descriptor is not null, "The row menu did not qualify for standard presentation.");
        Assert(
            descriptor!
                .Entries.Select(entry => entry.Label)
                .SequenceEqual(new string?[] { "Open issue", null, "Toggle status", "Set status" })
                && descriptor.Entries[^1].Kind == StandardMenuEntryKind.Submenu
                && descriptor.Entries[^1].Submenu?.Entries.Count == 2,
            "The generated row menu did not project its standard commands in authored order."
        );
        var beforeSecondStatus = browser
            .Issues.Single(issue => issue.Number == secondNumber)
            .Status;
        var toggle = descriptor.Entries.Single(entry => entry.Label == "Toggle status");
        Assert(
            request.InvokeStandardCommand(toggle.Identity!.Value) == SemanticCommandResult.Applied,
            "The native standard Toggle status command was rejected before popup projection."
        );
        graph.Drain();
        var afterSecondStatus = browser.Issues.Single(issue => issue.Number == secondNumber).Status;
        Assert(
            browser.SelectedIssue?.Number == firstNumber && afterSecondStatus != beforeSecondStatus,
            "Toggling a right-clicked issue changed its status but also stole the existing selection: selected="
                + browser.SelectedIssue?.Number
                + " target="
                + secondNumber
                + " before="
                + beforeSecondStatus
                + " after="
                + afterSecondStatus
                + "."
        );

        scene = Install();
        var refreshedRow = Flatten(composition.SemanticSnapshot()!)
            .Single(node =>
                node.Role == SemanticRole.ListItem && IssueNumber(node) == secondNumber
            );
        Assert(
            scene.Boxes.Any(box => box.Identity.ElementId == refreshedRow.Identity.ElementId)
                && refreshedRow.Name.Contains(
                    "— " + afterSecondStatus + " ·",
                    StringComparison.Ordinal
                ),
            "The refreshed Issue Browser scene did not reflect the native status mutation for the right-clicked issue: status="
                + afterSecondStatus
                + " row="
                + refreshedRow.Name
                + " selected="
                + browser.SelectedIssue?.Number
                + "."
        );
    }

    static VirtualizedIssueRowEvidence CaptureVirtualizedIssueRowEvidence(
        Func<IssueBrowserState, Func<BrowserIssue>, ComponentRecipe> row
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
                issue => row(browser, () => issue.Value).Named("issue-browser.issue-row"),
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
        Assert(
            composition.Input.SetScene(scene),
            "The virtual Issue Row parity scene was rejected."
        );
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
        Assert(
            composition.Input.SetScene(scene),
            "The selected virtual Issue Row scene was rejected."
        );
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

        var beforeReplacementPixels = Pixels(renderer, scene, 800, 60);
        // Replace only the list source: the browser's original records deliberately stay unchanged.
        // A row must read its retained payload rather than looking the key up in browser.Issues.
        values.Value = values
            .Value.Select(issue =>
                issue.Number == 10_000 ? issue with { Title = "Updated retained record" } : issue
            )
            .ToArray();
        graph.Drain();
        scene = SceneLayout.Project(composition, new(800, 60, 1), renderer);
        Assert(composition.Input.SetScene(scene), "The replaced-record scene was rejected.");
        var refreshed = IssueRow(composition, 10_000, "replaced");
        Assert(
            refreshed.Name.Contains("Updated retained record", StringComparison.Ordinal)
                && refreshed.Identity.ElementId == reordered.Identity.ElementId
                && refreshed.Selected
                && composition.Input.FocusedElement?.ElementId == refreshed.Identity.ElementId
                && Pixels(renderer, scene, 800, 60) != beforeReplacementPixels,
            "Same-key replacement failed to update row text/semantics while retaining identity, focus and selection."
        );

        values.Value = values.Value.Skip(2).ToArray();
        graph.Drain();
        scene = SceneLayout.Project(composition, new(800, 60, 1), renderer);
        Assert(
            composition.Input.SetScene(scene),
            "The removed virtual Issue Row scene was rejected."
        );
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

    [TestMethod]
    public void CompactSelectionOpensDetailsAndBackRestoresList()
    {
        using var composition = LoadedComposition(out var graph, out var browser);
        using var renderer = new SkiaSceneRenderer();
        Install(composition, renderer, new(700, 600, 1));
        var row = Flatten(composition.SemanticSnapshot()!)
            .First(node => node.Role == SemanticRole.ListItem);
        Assert(
            composition.ExecuteSemanticCommand(row.Identity, new(SemanticCommandKind.Select))
                == SemanticCommandResult.Applied,
            "The compact issue row rejected selection."
        );
        graph.Drain();
        Install(composition, renderer, new(700, 600, 1));
        var details = Flatten(composition.SemanticSnapshot()!).ToArray();
        Assert(
            browser.SelectedIssue is not null
                && details.Any(node =>
                    node.Role == SemanticRole.Button && node.Name == "Back to issues"
                )
                && details.Any(node => node.Name == "Close issue" || node.Name == "Reopen issue"),
            "Compact selection did not replace the list with issue details."
        );
        var back = details.Single(node =>
            node.Role == SemanticRole.Button && node.Name == "Back to issues"
        );
        Assert(
            composition.ExecuteSemanticCommand(back.Identity, new(SemanticCommandKind.Invoke))
                == SemanticCommandResult.Applied,
            "Back to issues was not invokable."
        );
        graph.Drain();
        Install(composition, renderer, new(700, 600, 1));
        Assert(
            Flatten(composition.SemanticSnapshot()!).Any(node => node.Role == SemanticRole.List),
            "Back navigation did not restore the issue list."
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
            graph
                .Dump()
                .Contains("name=\"filter-bar-reactive-appearance\"", StringComparison.Ordinal)
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
            Task.Run(() =>
                    completion.SetResult(new(Color.Parse("#123456"), Insets.Uniform(2), .5f))
                )
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
            filterRoot
                .Resolve(VisualProperties.Background)
                .Value.Equals((Brush)Color.Parse("#123456"))
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

    static void AssertDisposedReactiveFilterBar(
        Func<IssueBrowserState, Style?, ComponentRecipe> recipe
    )
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
        var semantic =
            Flatten(composition.SemanticSnapshot()!)
                .SingleOrDefault(snapshot => snapshot.Identity.ElementId == root.Id)
            ?? Flatten(composition.SemanticSnapshot()!)
                .Single(snapshot => snapshot.Role == SemanticRole.ListItem);
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

    static Composition LoadedComposition(out ReactiveGraph graph, out IssueBrowserState browser)
    {
        graph = new ReactiveGraph();
        var handler = new DeferredGitHubHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.local/") };
        var composition = IssueBrowserStructure.Create(
            graph,
            new GitHubIssueSource(client),
            out browser,
            out _
        );
        composition.Root.Scope.Own(client);
        graph.Drain(100);
        handler.ReplyJson(0);
        graph.Drain(100);
        return composition;
    }

    static RetainedScene Install(
        Composition composition,
        SkiaSceneRenderer renderer,
        LayoutViewport viewport
    )
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var scene = SceneLayout.Project(composition, viewport, renderer, 10_000);
            if (composition.Input.SetScene(scene))
                return scene;
        }
        throw new InvalidOperationException("The Issue Browser scene did not settle.");
    }

    static Dictionary<string, SemanticSnapshot> Fields(Composition composition) =>
        Flatten(composition.SemanticSnapshot()!)
            .Where(node => node.Role == SemanticRole.TextField)
            .ToDictionary(node => node.Name, StringComparer.Ordinal);

    static void SetValue(Composition composition, SemanticSnapshot field, string value) =>
        Assert(
            composition.ExecuteSemanticCommand(
                field.Identity,
                new(SemanticCommandKind.SetValue, value)
            ) == SemanticCommandResult.Applied,
            "The field rejected a semantic value update."
        );

    [TestMethod]
    public void AsyncBrowserStates()
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
                    .Count(node => node.Role == SemanticRole.ListItem)
                    <= RealizedRowBound(scene, composition, 30f),
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
        var errorViewport = new LayoutViewport(420, 600, 1);
        scene = Install(composition, renderer, errorViewport);
        Assert(
            Flatten(composition.SemanticSnapshot()!)
                .Any(node => node.Role == SemanticRole.Button && node.Name == "Retry"),
            "Compact error presentation lost its recovery command."
        );
        CaptureIfRequested(renderer, scene, errorViewport, "issue-browser-error-narrow.png");

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

    [TestMethod]
    public void DisposedRequestCannotCommit()
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

    [TestMethod]
    public void DensityRestyle()
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
        var viewport = new LayoutViewport(1120, 760, 1);
        var scene = Install(composition, renderer, viewport);
        var comfortable = Flatten(composition.SemanticSnapshot()!)
            .First(node => node.Role == SemanticRole.ListItem);
        Assert(
            composition.ExecuteSemanticCommand(
                comfortable.Identity,
                new(SemanticCommandKind.Select)
            ) == SemanticCommandResult.Applied
                && composition.Input.FocusSemantic(
                    new(comfortable.Identity.CompositionEpoch, comfortable.Identity.ElementId)
                ),
            "Density proof could not select and focus its first row."
        );
        graph.Drain();
        scene = Install(composition, renderer, viewport);
        var comfortableHeight = scene
            .Boxes.Single(box => box.Identity.ElementId == comfortable.Identity.ElementId)
            .Bounds.Height;
        var focused = composition.Input.FocusedElement;
        var density = Flatten(composition.SemanticSnapshot()!)
            .Single(node =>
                node.Role == SemanticRole.Button && node.Name == "Density: comfortable"
            );
        Assert(
            composition.ExecuteSemanticCommand(density.Identity, new(SemanticCommandKind.Invoke))
                == SemanticCommandResult.Applied,
            "Keyboard/UIA density button rejected Invoke."
        );
        graph.Drain();
        scene = Install(composition, renderer, viewport);
        var compact = Flatten(composition.SemanticSnapshot()!)
            .Single(node => node.Identity.ElementId == comfortable.Identity.ElementId);
        var compactBox = scene.Boxes.Single(box =>
            box.Identity.ElementId == compact.Identity.ElementId
        );
        Assert(
            compactBox.Bounds.Height < comfortableHeight
                && composition.Input.FocusedElement == focused
                && compact.Identity.ElementId == comfortable.Identity.ElementId
                && composition.IsCurrent(compact.Identity),
            "Compact density lost focus, UIA identity, or its typed row height: comfortable="
                + comfortableHeight
                + " compact="
                + compactBox.Bounds.Height
                + " identity="
                + (compact.Identity.ElementId == comfortable.Identity.ElementId)
                + " current="
                + composition.IsCurrent(compact.Identity)
                + " focus="
                + focused
                + "/"
                + composition.Input.FocusedElement
        );
        var compactRows = Flatten(composition.SemanticSnapshot()!)
            .Count(node => node.Role == SemanticRole.ListItem);
        var compactBound = RealizedRowBound(scene, composition, 49f, retainedTransitionRows: 4);
        Assert(
            compactRows <= compactBound,
            "Compact density exceeded its bounded realization: rows="
                + compactRows
                + " bound="
                + compactBound
                + "."
        );
        density = Flatten(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.Button && node.Name == "Density: compact");
        Assert(
            composition.ExecuteSemanticCommand(density.Identity, new(SemanticCommandKind.Invoke))
                == SemanticCommandResult.Applied,
            "Compact density button identity was stale."
        );
        graph.Drain();
        scene = Install(composition, renderer, viewport);
        var restored = Flatten(composition.SemanticSnapshot()!)
            .Single(node => node.Identity.ElementId == comfortable.Identity.ElementId);
        var restoredBox = scene.Boxes.Single(box =>
            box.Identity.ElementId == restored.Identity.ElementId
        );
        Assert(
            restoredBox.Bounds.Height == comfortableHeight
                && restored.Identity.ElementId == comfortable.Identity.ElementId
                && composition.Input.FocusedElement == focused,
            "Comfortable restoration changed the retained row, focus, UIA identity, or height."
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
            _ = Install(composition, renderer, viewport);
        }
    }

    [TestMethod]
    public void OptimisticStatusMutations()
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
                && !browser.CanRetrySelected,
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
                && !browser.CanRetrySelected,
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
                && browser.CanRetrySelected,
            "Transient save did not retain the optimistic status and expose Retry."
        );
        browser.RetrySelected();
        graph.Drain();
        Assert(
            saves.Requests.Count == 4 && !browser.CanRetrySelected,
            "Manual retry did not start exactly one save and hide Retry while pending."
        );
        saves.Reply(3, new IssueStatusSaveOutcome.Saved());
        graph.Drain();
        Assert(
            browser.SelectedMutationMessage is null && !browser.CanRetrySelected,
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

    [TestMethod]
    public void GeneratedMutationSemantics()
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
        using var renderer = new SkiaSceneRenderer();
        var viewport = new LayoutViewport(1120, 760, 1);
        _ = Install(composition, renderer, viewport);
        browser.Select(10_000);
        graph.Drain();
        _ = Install(composition, renderer, viewport);
        var action = Flatten(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.Button && node.Name == "Close issue");
        Assert(
            composition.ExecuteSemanticCommand(action.Identity, new(SemanticCommandKind.Invoke))
                == SemanticCommandResult.Applied,
            "Generated Details rejected its ordinary semantic status action."
        );
        graph.Drain();
        _ = Install(composition, renderer, viewport);
        var semantics = Flatten(composition.SemanticSnapshot()!);
        Assert(
            semantics.Any(node =>
                node.Role == SemanticRole.Status
                && node.Name.Contains("closed ·", StringComparison.Ordinal)
                && node.Name.EndsWith(
                    " · Not synced: Fixture source is temporarily unavailable.",
                    StringComparison.Ordinal
                )
            )
                && semantics.Any(node =>
                    node.Role == SemanticRole.Button && node.Name == "Retry status change"
                ),
            "Generated Details did not publish transient status and Retry semantics."
        );
    }

    [TestMethod]
    public void SourceExceptionsBecomeTransientFailures()
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
                && browser.SelectedMutationMessage
                    == "Not synced: Unexpected save failure: first fault"
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
                && message.StartsWith(
                    "Not synced: Unexpected save failure: ",
                    StringComparison.Ordinal
                )
                && message.Length <= 200
                && browser.CanRetrySelected,
            "A fault after a saved value reused the old value or exposed an unbounded reason."
        );
    }

    [TestMethod]
    public void VisualSurface()
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
        var viewport = new LayoutViewport(1120, 760, 1.25f);
        var light = Install(composition, renderer, viewport);
        var router = composition.Input;
        Assert(
            router.SetScene(light)
                && router.MoveFocus(FocusTraversalDirection.Next)
                && router.FocusedElement is not null,
            "First keyboard focus target was unavailable."
        );
        Assert(
            Install(composition, renderer, viewport)
                .Dump()
                .Contains("brush=solid(#FFFF00FF)", StringComparison.Ordinal),
            "Keyboard focus did not produce a visible focus paint."
        );
        var row = Flatten(composition.SemanticSnapshot()!)
            .First(node => node.Role == SemanticRole.ListItem);
        Assert(
            composition.ExecuteSemanticCommand(row.Identity, new(SemanticCommandKind.Select))
                == SemanticCommandResult.Applied,
            "Appearance captures could not select a detail issue."
        );
        graph.Drain();
        light = Install(composition, renderer, viewport);
        CaptureIfRequested(renderer, light, viewport, "issue-browser-light-wide.png");
        var narrowViewport = new LayoutViewport(420, 360, 1.25f);
        CaptureIfRequested(
            renderer,
            Install(composition, renderer, narrowViewport),
            narrowViewport,
            "issue-browser-light-narrow.png"
        );
        theme.Appearance = new(ThemeColorScheme.Dark, ThemeContrast.Normal);
        graph.Drain();
        var dark = Install(composition, renderer, viewport);
        CaptureIfRequested(renderer, dark, viewport, "issue-browser-dark-wide.png");
        CaptureIfRequested(
            renderer,
            Install(composition, renderer, narrowViewport),
            narrowViewport,
            "issue-browser-dark-narrow.png"
        );
        theme.Appearance = new(ThemeColorScheme.Light, ThemeContrast.High);
        graph.Drain();
        var high = Install(composition, renderer, viewport);
        CaptureIfRequested(renderer, high, viewport, "issue-browser-high-contrast-wide.png");
        CaptureIfRequested(
            renderer,
            Install(composition, renderer, narrowViewport),
            narrowViewport,
            "issue-browser-high-contrast-narrow.png"
        );
        Assert(
            light.Dump() != dark.Dump()
                && dark.Dump() != high.Dump()
                && !light.Dump().Contains("Native IME", StringComparison.Ordinal),
            "Stock light, dark, and high-contrast appearances did not produce distinct diagnostic-safe scenes."
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

    [TestMethod]
    public void ResponsiveVisualSurface()
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
        transport.ReplyJson(0);
        graph.Drain();
        using var renderer = new SkiaSceneRenderer();

        var wideViewport = new LayoutViewport(1120, 760, 1.25f);
        var wide = Install(composition, renderer, wideViewport);
        var wideSemantics = Flatten(composition.SemanticSnapshot()!).ToArray();
        var splitter = wideSemantics.Single(node => node.Role == SemanticRole.Splitter);
        var listViewport = wideSemantics.Single(node =>
            node.Name == "Issues" && node.Actions.HasFlag(SemanticAction.Scroll)
        );
        var splitterBounds = wide
            .Boxes.Single(box => box.Identity.ElementId == splitter.Identity.ElementId)
            .Bounds;
        var listBounds = wide
            .Boxes.Single(box => box.Identity.ElementId == listViewport.Identity.ElementId)
            .Bounds;
        var row = wideSemantics.First(node => node.Role == SemanticRole.ListItem);
        Assert(
            splitterBounds.Y + splitterBounds.Height >= 740
                && listBounds.Y + listBounds.Height >= 740
                && composition.ExecuteSemanticCommand(row.Identity, new(SemanticCommandKind.Select))
                    == SemanticCommandResult.Applied,
            "Wide Issue Browser did not fill its workspace: splitter="
                + splitterBounds
                + " list="
                + listBounds
        );
        graph.Drain();
        wide = Install(composition, renderer, wideViewport);
        Assert(
            browser.SelectedIssue is { } selected
                && Flatten(composition.SemanticSnapshot()!)
                    .Any(node => node.Name == selected.Title),
            "Wide issue selection did not retain and reveal the detail pane."
        );

        var narrowViewport = new LayoutViewport(420, 360, 1.5f);
        var narrow = Install(composition, renderer, narrowViewport);
        var narrowSemantics = Flatten(composition.SemanticSnapshot()!).ToArray();
        Assert(
            narrowSemantics.Any(node =>
                node.Role == SemanticRole.Button && node.Name == "Back to issues"
            )
                && narrowSemantics.All(node => node.Role != SemanticRole.Splitter)
                && narrow.Boxes.All(box =>
                    box.Bounds.X >= -.01f
                    && box.Bounds.X + box.Bounds.Width <= narrowViewport.Width + .01f
                ),
            "The 420-pixel detail layout lost Back navigation, retained a splitter, or overflowed horizontally."
        );
        var back = narrowSemantics.Single(node =>
            node.Role == SemanticRole.Button && node.Name == "Back to issues"
        );
        Assert(
            composition.ExecuteSemanticCommand(back.Identity, new(SemanticCommandKind.Invoke))
                == SemanticCommandResult.Applied,
            "Compact Back command was not invokable."
        );
        graph.Drain();
        narrow = Install(composition, renderer, narrowViewport);
        Assert(
            browser.SelectedIssue is not null
                && Flatten(composition.SemanticSnapshot()!)
                    .Any(node =>
                        node.Name == "Issues" && node.Actions.HasFlag(SemanticAction.Scroll)
                    )
                && !Flatten(composition.SemanticSnapshot()!)
                    .Any(node => node.Role == SemanticRole.Button && node.Name == "Back to issues"),
            "Compact Back navigation did not restore the list while preserving selection."
        );

        wide = Install(composition, renderer, wideViewport);
        Assert(
            Flatten(composition.SemanticSnapshot()!).Any(node => node.Role == SemanticRole.Splitter)
                && browser.SelectedIssue is not null
                && wide.Boxes.Max(box => box.Bounds.Y + box.Bounds.Height)
                    >= wideViewport.Height - .01f,
            "Returning wide lost split/detail state or failed to fill the viewport."
        );
    }

    [TestMethod]
    public void StartupStaysFrameworkOwned()
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
                var program = File.ReadAllText(Path.Combine(app, "Program.cs"));
                var structure = File.ReadAllText(Path.Combine(app, "IssueBrowserStructure.cs"));
                var issueRow = File.ReadAllText(Path.Combine(app, "IssueRow.lui"));
                var issueBrowser = File.ReadAllText(Path.Combine(app, "IssueBrowser.lui"));
                var authoredLui = string.Join(
                    '\n',
                    Directory.EnumerateFiles(app, "*.lui").Select(File.ReadAllText)
                );
                Assert(
                    !source.Contains(".Drain(", StringComparison.Ordinal)
                        && program.Contains("[STAThread]", StringComparison.Ordinal)
                        && program.Contains(".UseWindows(", StringComparison.Ordinal)
                        && program.Contains("--native-menus", StringComparison.Ordinal)
                        && program.Contains(
                            "WindowsMenuPresentation.PreferNative",
                            StringComparison.Ordinal
                        )
                        && program.Contains(
                            ".Run(IssueBrowserStructure.Create())",
                            StringComparison.Ordinal
                        )
                        && !program.Contains("ReactiveGraph", StringComparison.Ordinal)
                        && !program.Contains("Composition", StringComparison.Ordinal)
                        && !program.Contains("WindowsBootstrap", StringComparison.Ordinal)
                        && structure.Contains(
                            "public static ComponentRecipe Create()",
                            StringComparison.Ordinal
                        )
                        && !source.Contains("test mode", StringComparison.OrdinalIgnoreCase)
                        && !source.Contains("IssueRowHandwritten", StringComparison.Ordinal)
                        && !Regex.IsMatch(source, @"\bComponentRecipe\s+IssueRow\s*\(")
                        && issueRow.Contains("public component IssueRow", StringComparison.Ordinal)
                        && !issueRow.Contains("VirtualizedList", StringComparison.Ordinal)
                        && issueBrowser.Contains(
                            "public component IssueBrowser",
                            StringComparison.Ordinal
                        )
                        && authoredLui.Contains(
                            "<VirtualizedList source={() => browser.VisibleIssues} key={issue => issue.Number} row={issue => IssueRow(browser, () => issue.Value, view).Named(\"issue-browser.issue-row\")}",
                            StringComparison.Ordinal
                        )
                        && authoredLui.Contains(
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

    static int IssueNumber(SemanticSnapshot row) =>
        int.Parse(
            row.Name.Split(' ')[0].TrimStart('#'),
            System.Globalization.CultureInfo.InvariantCulture
        );

    static int RealizedRowBound(
        RetainedScene scene,
        Composition composition,
        float rowHeight,
        int retainedTransitionRows = 0
    )
    {
        var scroll = Flatten(composition.SemanticSnapshot()!)
            .Single(node => node.Name == "Issues" && node.Actions.HasFlag(SemanticAction.Scroll));
        var viewportHeight = scene
            .Boxes.Single(box => box.Identity.ElementId == scroll.Identity.ElementId)
            .Bounds.Height;
        return (int)MathF.Ceiling(viewportHeight / rowHeight) + 2 + retainedTransitionRows;
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
                candidate.Box.Bounds.Y < viewport.Y + viewport.Height
                && candidate.Box.Bounds.Y + candidate.Box.Bounds.Height > viewport.Y
            )
            .OrderBy(candidate => candidate.Box.Bounds.Y)
            .ToArray();
        var number =
            candidates.Length == 1
            && int.TryParse(
                candidates[0].Semantic.Name.Split(' ')[0].TrimStart('#'),
                out var parsed
            )
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

    static void CaptureIfRequested(
        SkiaSceneRenderer renderer,
        RetainedScene scene,
        LayoutViewport viewport,
        string fileName
    )
    {
        var directory = Environment.GetEnvironmentVariable("LUCENT_HEADLESS_CAPTURES");
        if (string.IsNullOrWhiteSpace(directory))
            return;
        Directory.CreateDirectory(directory);
        var width = checked((int)MathF.Round(viewport.Width * viewport.Scale));
        var height = checked((int)MathF.Round(viewport.Height * viewport.Scale));
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            renderer.Render(scene, canvas);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(Path.Combine(directory, fileName));
        data.SaveTo(stream);
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
            Task.Run(() =>
                    _responses[index].SetResult(Response(HttpStatusCode.OK, IssueFixture.Json))
                )
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

    internal sealed class ApplicationHostProbe : IApplicationHost
    {
        private Composition? _composition;

        internal string? Title { get; private set; }
        internal bool ObservedApplicationRoot { get; private set; }
        internal bool CompositionDisposed => _composition?.IsDisposed == true;

        public int Run(ApplicationSession session)
        {
            session.Start();
            var composition = session.Composition;
            var theme = session.Theme;
            Title = session.Title;
            _composition = composition;

            var applicationRoot = composition.Root.Children.Single();

            ObservedApplicationRoot =
                applicationRoot.Name.StartsWith(
                    "issue-browser-application",
                    StringComparison.Ordinal
                )
                && applicationRoot.Resolve(LayoutProperties.MainGrow).Value == 1f
                && applicationRoot.Children.Single().Name == "Issue Browser"
                && theme.Theme.Name == ControlThemes.Light.Name;
            session.RequestClose();
            session.ProcessEvents();
            Assert(session.IsCompleted, "The application lifecycle did not complete.");
            return 23;
        }
    }

    internal static class HandwrittenParityFixture
    {
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
                Style
                    .Empty.Height(() => browser.Density == IssueDensity.Comfortable ? 28f : 22f)
                    .Spacing(() => browser.Density == IssueDensity.Comfortable ? 8f : 4f)
                    .With(style)
            );
        }

        internal static ComponentRecipe CreateRow(
            IssueBrowserState browser,
            Func<BrowserIssue> issue
        )
        {
            ArgumentNullException.ThrowIfNull(browser);
            ArgumentNullException.ThrowIfNull(issue);
            var style = Style
                .Empty.Spacing(() => browser.Density == IssueDensity.Comfortable ? 8f : 4f)
                .FontSize(() => browser.Density == IssueDensity.Comfortable ? 14f : 12f)
                .Height(() => browser.Density == IssueDensity.Comfortable ? 30f : 22f);
            return Lucent
                .Core.Components.ContextMenu(
                    [
                        Lucent.Core.Components.Selectable(
                            () => Label(issue()),
                            () => browser.IsSelected(issue().Number),
                            () => browser.Select(issue().Number),
                            style
                        ),
                    ],
                    () =>
                        Lucent.Core.Components.Menu([
                            Lucent.Core.Components.MenuItem(
                                "Open issue",
                                () => browser.OpenIssue(issue().Number)
                            ),
                            Lucent.Core.Components.MenuSeparator(),
                            Lucent.Core.Components.MenuItem(
                                "Toggle status",
                                () => browser.ToggleIssueStatus(issue().Number)
                            ),
                        ])
                )
                .Named("issue-browser.issue-row-menu");
        }

        private static string Label(BrowserIssue issue) =>
            $"#{issue.Number} {issue.Title} — {issue.Status} · {issue.Assignee}";
    }
}
