using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class RouteOutletContracts
{
    private static readonly string[] EnterInitialCalls = ["Enter:project", "Enter:issue"];
    private static readonly string[] LeaveAndBlockedEnterCalls =
    [
        "Leave:issue",
        "Leave:project",
        "Enter:project",
        "Enter:issue",
    ];

    [TestMethod]
    public void NestedOutletRetainsPrefixPublishesLiveContextAndReceivesLayoutBounds()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "route-outlet-layout");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var fixture = Fixture.Create();
        using var sessionOwner = graph.CreateScope("route-session");
        using var session = new NavigationSession(sessionOwner, fixture.Table);
        using var handle = new RouteOutletHandle();

        composition.Root.Present(
            theme,
            author: Style.Empty.Axis(LayoutAxis.Column).Width(160).Height(80)
        );
        var outlet = RouteOutlet.Create(
            fixture.Descriptors,
            level => BuildLevel(level, fixture),
            handle
        );
        _ = composition.Mount(composition.Root, theme, Context.Provide(session, outlet));

        var first = session.Navigate(Location("/projects/7"));
        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(first).Kind);
        Assert.AreEqual(1, handle.Snapshot.Levels.Count);
        var retainedParent = handle.Snapshot.Levels[0].ElementId;
        Assert.IsNotNull(fixture.ProjectContexts[^1].ActiveEntry);
        Assert.AreEqual(session.Current!.EntryId, fixture.ProjectContexts[^1].ActiveEntry!.EntryId);
        Assert.IsNotNull(fixture.ProjectContexts[^1].Child);
        Assert.AreEqual(0, fixture.ProjectContexts[^1].Child!.Levels.Count);

        var child = session.Navigate(Location("/projects/7/issues/42"));
        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(child).Kind);
        Assert.AreEqual(2, handle.Snapshot.Levels.Count);
        Assert.AreEqual(retainedParent, handle.Snapshot.Levels[0].ElementId);
        Assert.AreEqual("issue", handle.Snapshot.Levels[1].Definition.Value);
        var retiredChild = handle.Snapshot.Levels[1].ElementId;
        Assert.AreEqual(1, fixture.ProjectContexts[^1].Child!.Levels.Count);
        Assert.AreEqual(session.Current!.EntryId, fixture.ProjectContexts[^1].ActiveEntry!.EntryId);

        using var scene = SceneLayout.Project(composition, new(160, 80, 1), new EmptyShaper());
        var parentBox = scene.Boxes.Single(box => box.Identity.ElementId == retainedParent).Bounds;
        var childBox = scene
            .Boxes.Single(box => box.Identity.ElementId == handle.Snapshot.Levels[1].ElementId)
            .Bounds;
        Assert.IsTrue(parentBox.Width > 0 && parentBox.Height > 0);
        Assert.IsTrue(childBox.Width > 0 && childBox.Height > 0);

        var backToParent = session.Navigate(Location("/projects/7"));
        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(backToParent).Kind);
        Assert.AreEqual(1, handle.Snapshot.Levels.Count);
        Assert.AreEqual(retainedParent, handle.Snapshot.Levels[0].ElementId);
        Assert.IsNull(
            composition.Find(new ElementIdentity(composition.Epoch, retiredChild)),
            "Retired child roots must be released after publication."
        );
        Assert.AreEqual(0, fixture.ProjectContexts[^1].Child!.Levels.Count);
        Assert.AreEqual(session.Current!.EntryId, fixture.ProjectContexts[^1].ActiveEntry!.EntryId);
        Assert.IsNull(
            fixture.IssueContexts[^1].ActiveEntry,
            "A retired route context must stop exposing its committed entry."
        );
        Assert.IsNull(fixture.IssueContexts[^1].Child);
    }

    [TestMethod]
    public void RetainedRouteContextReadsRerunWhenChildOutletPublishes()
    {
        var fixture = Fixture.Create(observeLive: true);
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "route-outlet-live-context");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var sessionOwner = graph.CreateScope("route-session");
        using var session = new NavigationSession(sessionOwner, fixture.Table);
        using var handle = new RouteOutletHandle();
        composition.Root.Present(theme, author: Style.Empty.Width(160).Height(80));
        var outlet = RouteOutlet.Create(
            fixture.Descriptors,
            level => BuildLevel(level, fixture),
            handle
        );
        _ = composition.Mount(composition.Root, theme, Context.Provide(session, outlet));

        Assert.AreEqual(
            NavigationOutcomeKind.Committed,
            Completed(session.Navigate(Location("/projects/1"))).Kind
        );
        Assert.AreEqual(1, fixture.LiveReads.Count);
        Assert.AreEqual(((long?)session.Current!.EntryId, 0), fixture.LiveReads[0]);
        Assert.AreEqual(1, fixture.LiveDerivedReads.Count);
        Assert.AreEqual(((long?)session.Current!.EntryId, 0), fixture.LiveDerivedReads[0]);
        Assert.AreEqual(1, fixture.LiveDerivedMountReads.Count);
        Assert.AreEqual(((long?)session.Current!.EntryId, -1), fixture.LiveDerivedMountReads[0]);

        Assert.AreEqual(
            NavigationOutcomeKind.Committed,
            Completed(session.Navigate(Location("/projects/1/issues/2"))).Kind
        );
        Assert.AreEqual(2, fixture.LiveReads.Count);
        Assert.AreEqual(((long?)session.Current!.EntryId, 1), fixture.LiveReads[1]);
        Assert.AreEqual(2, fixture.LiveDerivedReads.Count);
        Assert.AreEqual(((long?)session.Current!.EntryId, 1), fixture.LiveDerivedReads[1]);

        Assert.AreEqual(
            NavigationOutcomeKind.Committed,
            Completed(session.Navigate(Location("/projects/1"))).Kind
        );
        Assert.AreEqual(3, fixture.LiveReads.Count);
        Assert.AreEqual(((long?)session.Current!.EntryId, 0), fixture.LiveReads[2]);
        Assert.AreEqual(3, fixture.LiveDerivedReads.Count);
        Assert.AreEqual(((long?)session.Current!.EntryId, 0), fixture.LiveDerivedReads[2]);
    }

    [TestMethod]
    public void PreparationWalksLeaveLeafToRootAndEnterRootToLeafAndCanStay()
    {
        var fixture = Fixture.Create();
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "route-outlet-preparation");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var sessionOwner = graph.CreateScope("route-session");
        using var session = new NavigationSession(sessionOwner, fixture.Table);
        using var handle = new RouteOutletHandle();
        var calls = new List<string>();
        var blockIssue = false;
        var options = new RouteOutletOptions(
            (level, request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                calls.Add(request.Phase + ":" + level.Id.Value);
                return ValueTask.FromResult(
                    blockIssue
                    && request.Phase == NavigationPreparationPhase.Enter
                    && level.Id.Value == "issue"
                        ? NavigationPreparationResult.Stay
                        : NavigationPreparationResult.Allow
                );
            }
        );
        var outlet = RouteOutlet.Create(
            fixture.Descriptors,
            level => BuildLevel(level, fixture),
            handle,
            options: options
        );
        _ = composition.Mount(composition.Root, theme, Context.Provide(session, outlet));

        Assert.AreEqual(
            NavigationOutcomeKind.Committed,
            Completed(session.Navigate(Location("/projects/7/issues/1"))).Kind
        );
        CollectionAssert.AreEqual(EnterInitialCalls, calls);

        calls.Clear();
        blockIssue = true;
        var stayed = session.Navigate(Location("/projects/7/issues/2"));
        Assert.AreEqual(NavigationOutcomeKind.Stayed, Completed(stayed).Kind);
        CollectionAssert.AreEqual(LeaveAndBlockedEnterCalls, calls);
        Assert.AreEqual("issue", session.Current!.DefinitionId.Value);
        Assert.AreEqual(2, handle.Snapshot.Levels.Count);
    }

    [TestMethod]
    public void CommittedObserverRunsAfterSessionCurrentAndJournalAreAssigned()
    {
        var fixture = Fixture.Create();
        var graph = new ReactiveGraph();
        using var sessionOwner = graph.CreateScope("route-session");
        using var observerOwner = graph.CreateScope("route-observer");
        using var session = new NavigationSession(sessionOwner, fixture.Table);
        var observed = new List<long>();
        using var registration = session.RegisterCommitted(
            observerOwner,
            commit =>
            {
                Assert.AreSame(commit.Current, session.Current);
                Assert.AreSame(commit.Journal, session.Journal);
                observed.Add(commit.Current.EntryId);
            }
        );

        Assert.AreEqual(
            NavigationOutcomeKind.Committed,
            Completed(session.Navigate(Location("/projects/1"))).Kind
        );
        Assert.AreEqual(
            NavigationOutcomeKind.Committed,
            Completed(session.Navigate(Location("/projects/2"))).Kind
        );
        CollectionAssert.AreEqual(
            session.Journal.Entries.Select(entry => entry.EntryId).ToArray(),
            observed.ToArray()
        );
    }

    [TestMethod]
    public void SupersededAsyncPreparationCannotPublishAStaleBranch()
    {
        var fixture = Fixture.Create();
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "route-outlet-async");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var sessionOwner = graph.CreateScope("route-session");
        using var session = new NavigationSession(sessionOwner, fixture.Table);
        using var handle = new RouteOutletHandle();
        var gates = new Queue<TaskCompletionSource<NavigationPreparationResult>>();
        var options = new RouteOutletOptions(
            (_, _, _) =>
            {
                var gate = new TaskCompletionSource<NavigationPreparationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                gates.Enqueue(gate);
                return new ValueTask<NavigationPreparationResult>(gate.Task);
            }
        );
        var outlet = RouteOutlet.Create(
            fixture.Descriptors,
            level => BuildLevel(level, fixture),
            handle,
            options: options
        );
        _ = composition.Mount(composition.Root, theme, Context.Provide(session, outlet));

        var first = session.Navigate(Location("/projects/1"));
        var second = session.Navigate(Location("/projects/2"));
        Assert.AreEqual(NavigationOutcomeKind.Superseded, Completed(first).Kind);
        Assert.AreEqual(2, gates.Count);
        Assert.AreEqual(0, handle.Snapshot.Levels.Count);

        gates.Dequeue().SetResult(NavigationPreparationResult.Allow);
        gates.Dequeue().SetResult(NavigationPreparationResult.Allow);
        var secondOutcome = DrainUntilCompleted(graph, second);

        Assert.AreEqual(NavigationOutcomeKind.Committed, secondOutcome.Kind);
        Assert.AreEqual("project", session.Current!.DefinitionId.Value);
        Assert.AreEqual(1, handle.Snapshot.Levels.Count);
        Assert.AreEqual(2, fixture.ProjectContexts[^1].Parameters.Project);
    }

    [TestMethod]
    public void MissingChildOutletFailsClosedWithoutChangingCommittedBranch()
    {
        var fixture = Fixture.Create();
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "route-outlet-missing-child");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var sessionOwner = graph.CreateScope("route-session");
        using var session = new NavigationSession(sessionOwner, fixture.Table);
        using var handle = new RouteOutletHandle();
        var outlet = RouteOutlet.Create(fixture.Descriptors, BuildLevelWithoutChild, handle);
        _ = composition.Mount(composition.Root, theme, Context.Provide(session, outlet));

        Assert.AreEqual(
            NavigationOutcomeKind.Committed,
            Completed(session.Navigate(Location("/projects/1"))).Kind
        );
        var committedId = handle.Snapshot.Levels[0].ElementId;
        var failed = session.Navigate(Location("/projects/1/issues/2"));

        var outcome = Completed(failed);
        Assert.AreEqual(NavigationOutcomeKind.Failed, outcome.Kind);
        Assert.AreEqual(NavigationFailureKind.Terminal, outcome.FailureKind);
        Assert.IsTrue(session.IsTerminated);
        Assert.AreEqual("project", session.Current!.DefinitionId.Value);
        Assert.AreEqual(committedId, handle.Snapshot.Levels[0].ElementId);
        Assert.AreEqual(1, handle.Snapshot.Levels.Count);
    }

    [TestMethod]
    public void SessionAcceptsOnlyOneRootOutletParticipant()
    {
        var fixture = Fixture.Create();
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "route-outlet-single-root");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var sessionOwner = graph.CreateScope("route-session");
        using var session = new NavigationSession(sessionOwner, fixture.Table);
        var first = RouteOutlet.Create(fixture.Descriptors, level => BuildLevel(level, fixture));
        _ = composition.Mount(composition.Root, theme, Context.Provide(session, first));

        var second = RouteOutlet.Create(fixture.Descriptors, level => BuildLevel(level, fixture));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            composition.Mount(composition.Root, theme, Context.Provide(session, second))
        );
    }

    private static ComponentRecipe BuildLevel(RouteLevelDescriptor level, Fixture fixture) =>
        level.Id.Value switch
        {
            "project" => ComponentRecipe.Create(
                "project-route",
                (context, root) =>
                {
                    root.Present(
                        context.Theme,
                        author: Style.Empty.Axis(LayoutAxis.Column).MainGrow(1)
                    );
                    if (fixture.ObserveLive)
                    {
                        var route = fixture.ProjectContexts[^1];
                        var derived = root.Scope.Derived(
                            () => (route.ActiveEntry?.EntryId, route.Child?.Levels.Count ?? -1),
                            "route-live-context-derived"
                        );
                        fixture.LiveDerivedMountReads.Add(derived.Value);
                        _ = root.Scope.Effect(
                            () =>
                            {
                                var entry = route.ActiveEntry;
                                var child = route.Child;
                                fixture.LiveReads.Add((entry?.EntryId, child?.Levels.Count ?? -1));
                            },
                            "route-live-context"
                        );
                        _ = root.Scope.Effect(
                            () => fixture.LiveDerivedReads.Add(derived.Value),
                            "route-live-context-derived-observer"
                        );
                    }
                    context.Mount(
                        root,
                        RouteOutlet.CreateChild(
                            fixture.Descriptors,
                            child => BuildLevel(child, fixture)
                        )
                    );
                }
            ),
            "issue" => ComponentRecipe.Create(
                "issue-route",
                (context, root) =>
                    root.Present(context.Theme, author: Style.Empty.Width(80).Height(20))
            ),
            _ => throw new InvalidOperationException("Unknown route-level fixture."),
        };

    private static ComponentRecipe BuildLevelWithoutChild(RouteLevelDescriptor level) =>
        level.Id.Value == "project"
            ? ComponentRecipe.Create(
                "project-route-without-child",
                (context, root) =>
                    root.Present(
                        context.Theme,
                        author: Style.Empty.Axis(LayoutAxis.Column).MainGrow(1)
                    )
            )
            : throw new InvalidOperationException("Unexpected child route mount.");

    private static RouteLocation Location(string value) => RouteLocation.Parse(value).Location!;

    private static NavigationOutcome Completed(NavigationOperation operation)
    {
        Assert.IsTrue(
            operation.Completion.IsCompleted,
            "Expected a synchronous navigation outcome."
        );
        return operation.Completion.Result;
    }

    private static NavigationOutcome DrainUntilCompleted(
        ReactiveGraph graph,
        NavigationOperation operation
    )
    {
        var deadline = Environment.TickCount64 + 5_000;
        while (!operation.Completion.IsCompleted && Environment.TickCount64 < deadline)
        {
            graph.Drain();
            if (!operation.Completion.IsCompleted)
                Thread.Sleep(1);
        }
        Assert.IsTrue(
            operation.Completion.IsCompleted,
            "The owner graph did not settle navigation."
        );
        return operation.Completion.Result;
    }

    private sealed class Fixture
    {
        private Fixture(
            RouteTable table,
            RouteDescriptorSet descriptors,
            List<RouteContext<ProjectRoute>> projectContexts,
            List<RouteContext<IssueRoute>> issueContexts,
            bool observeLive
        )
        {
            Table = table;
            Descriptors = descriptors;
            ProjectContexts = projectContexts;
            IssueContexts = issueContexts;
            ObserveLive = observeLive;
        }

        internal RouteTable Table { get; }
        internal RouteDescriptorSet Descriptors { get; }
        internal List<RouteContext<ProjectRoute>> ProjectContexts { get; }
        internal List<RouteContext<IssueRoute>> IssueContexts { get; }
        internal bool ObserveLive { get; }
        internal List<(long? EntryId, int ChildLevels)> LiveReads { get; } = [];
        internal List<(long? EntryId, int ChildLevels)> LiveDerivedMountReads { get; } = [];
        internal List<(long? EntryId, int ChildLevels)> LiveDerivedReads { get; } = [];

        internal static Fixture Create(bool observeLive = false)
        {
            var projectPattern = RoutePattern.Create(
                new RouteDefinitionId("project"),
                [
                    RouteSegmentPattern.LiteralSegment("projects"),
                    RouteSegmentPattern.Parameter("project", 0, RouteValueShape.Signed32),
                ]
            );
            var issuePattern = RoutePattern.Create(
                new RouteDefinitionId("issue"),
                [
                    RouteSegmentPattern.LiteralSegment("projects"),
                    RouteSegmentPattern.Parameter("project", 0, RouteValueShape.Signed32),
                    RouteSegmentPattern.LiteralSegment("issues"),
                    RouteSegmentPattern.Parameter("issue", 1, RouteValueShape.Signed32),
                ]
            );
            var source = new RouteDeclarationSource("tests/routes.lui", 1, 1);
            var contexts = new List<RouteContext<ProjectRoute>>();
            var issueContexts = new List<RouteContext<IssueRoute>>();
            var projectLevel = new RouteLevelDescriptor(
                new RouteDefinitionId("project"),
                [0],
                source,
                (definition, match, content, live) =>
                {
                    var context = new RouteContext<ProjectRoute>(
                        definition,
                        new ProjectRoute(match.GetValue(0).Signed32),
                        live
                    );
                    contexts.Add(context);
                    return Context.Provide(context, content);
                }
            );
            var issueLevel = new RouteLevelDescriptor(
                new RouteDefinitionId("issue"),
                [1],
                source,
                (definition, match, content, live) =>
                {
                    var context = new RouteContext<IssueRoute>(
                        definition,
                        new IssueRoute(match.GetValue(0).Signed32, match.GetValue(1).Signed32),
                        live
                    );
                    issueContexts.Add(context);
                    return Context.Provide(context, content);
                }
            );
            var project = new RouteDefinitionDescriptor(projectPattern, [projectLevel]);
            var issue = new RouteDefinitionDescriptor(issuePattern, [projectLevel, issueLevel]);
            var module = new RouteModuleDescriptor(
                "tests",
                RouteFallbackPolicy.Reject,
                source,
                [project, issue]
            );
            var table = RouteTable.Create([projectPattern, issuePattern]);
            return new(
                table,
                RouteDescriptorSet.Create(table, [module]),
                contexts,
                issueContexts,
                observeLive
            );
        }
    }

    private sealed record ProjectRoute(int Project);

    private sealed record IssueRoute(int Project, int Issue);

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }
}
