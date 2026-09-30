using System.Text;
using Lucent.Core;

var projectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
var reference = AppRoutes.Issue(projectId, 42, IssueView.Activity);
if (
    reference.Location.CanonicalText
    != "/projects/11111111-1111-1111-1111-111111111111/issues/42?view=Activity"
)
    throw new InvalidOperationException("Generated route formatting was not canonical.");

var table = RouteTable.Create(AppRoutes.Module.Patterns);
var parsed = RouteLocation.Parse(reference.Location.CanonicalText, table.Limits);
if (!parsed.Succeeded)
    throw new InvalidOperationException("Canonical route parsing failed.");
var matched = table.Match(parsed.Location!);
if (
    matched.Status != RouteMatchStatus.Matched
    || matched.Match is null
    || !ReferenceEquals(matched.Match.Pattern, reference.Pattern)
)
    throw new InvalidOperationException("Typed route matching did not preserve pattern identity.");

var descriptors = RouteDescriptorSet.Create(table, [AppRoutes.Module]);
var definition = descriptors.GetDefinition(matched.Match);
if (definition.Branch.Count != 2)
    throw new InvalidOperationException("Generated route ancestry was incomplete.");

var sawTypedContexts = false;
var requirements = ComponentRequirements
    .Context<RouteContext<ProjectRoute>>(Source("project"))
    .AndContext<RouteContext<IssueRoute>>(Source("issue"));
var content = ComponentRecipe.Defer(
    "generated-route-context-consumer",
    requirements,
    (_, contexts) =>
    {
        if (
            contexts.Previous.Parameters.ProjectId != projectId
            || contexts.Previous.Parameters.View != IssueView.Activity
            || contexts.Value.Parameters.ProjectId != projectId
            || contexts.Value.Parameters.IssueId != 42
            || contexts.Value.Parameters.View != IssueView.Activity
            || contexts.Previous.Definition.Id.Value != "project"
            || contexts.Value.Definition.Id.Value != "issue"
        )
            throw new InvalidOperationException("Generated typed route contexts were incorrect.");
        sawTypedContexts = true;
        return ComponentRecipe.Create("route-leaf", static (_, _) => { });
    }
);
content = definition.Branch[1].ProvideContext(matched.Match, content);
content = definition.Branch[0].ProvideContext(matched.Match, content);

var graph = new ReactiveGraph();
using var composition = new Composition(graph, "generated-navigation-aot");
using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
_ = composition.Mount(composition.Root, theme, content);
if (!sawTypedContexts)
    throw new InvalidOperationException("Generated route contexts were not resolved during mount.");

var dump = matched.Dump();
if (
    dump.Contains("11111111", StringComparison.Ordinal)
    || dump.Contains("42", StringComparison.Ordinal)
    || dump.Contains("Activity", StringComparison.Ordinal)
)
    throw new InvalidOperationException("Route diagnostics exposed parameter values.");

Console.WriteLine("generated-navigation-native-aot=pass");
VerifyRestoration(descriptors, reference, projectId);
VerifyMountedRestoration(descriptors, projectId);
Console.WriteLine("generated-navigation-restoration=pass");
VerifyJournalRestoration(descriptors, projectId);
Console.WriteLine("generated-navigation-journal=pass");

static ComponentRequirementSource Source(string member) =>
    new(member, "generated-route-context", "Program.cs", 1, 1);

static void VerifyRestoration(
    RouteDescriptorSet descriptors,
    RouteReference reference,
    Guid projectId
)
{
    const string snapshot =
        """{"schema":"lucent.navigation","version":1,"scope":"package-restoration-v1","mode":"location","active":{"definition":"issue","location":"/projects/11111111-1111-1111-1111-111111111111/issues/42?view=Activity"}}""";
    var expectedBytes = Encoding.UTF8.GetBytes(snapshot);
    var restoration = new NavigationRestoration(
        descriptors.Table,
        "package-restoration-v1",
        AppRoutes.Project(projectId),
        static match => match.DefinitionId.Value is "project" or "issue"
    );
    var captureGraph = new ReactiveGraph();
    using (var captureOwner = captureGraph.CreateScope("capture-owner"))
    using (var source = new NavigationSession(captureOwner, descriptors.Table, reference.Location))
    {
        var captured = restoration.Capture(source);
        if (
            captured.Status != NavigationRestorationStatus.Ready
            || !captured.Utf8.Span.SequenceEqual(expectedBytes)
        )
            throw new InvalidOperationException(
                "Packaged capture did not produce the version 1 bytes."
            );
    }

    foreach (var scenario in new[] { "restore", "guard-fallback", "invalid-fallback" })
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "restoration-package");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var owner = graph.CreateScope("restoration-owner");
        using var session = new NavigationSession(owner, descriptors.Table);
        using var handle = new RouteOutletHandle();
        var prepared = new List<string>();
        var mountedProject = false;
        var mountedIssue = false;
        var fallback = scenario != "restore";
        var bundle = RouteBundle.Create(
            descriptors,
            level =>
            {
                ComponentRecipe recipe;
                if (level.Id.Value == "project")
                    recipe = ComponentRecipe.Defer(
                        "restored-project",
                        ComponentRequirements.Context<RouteContext<ProjectRoute>>(
                            Source("project")
                        ),
                        (_, context) =>
                        {
                            if (
                                context.Parameters.ProjectId != projectId
                                || context.Parameters.View
                                    != (fallback ? IssueView.Summary : IssueView.Activity)
                            )
                                throw new InvalidOperationException(
                                    "Restored project context was incorrect."
                                );
                            mountedProject = true;
                            return Components.RouterOutlet();
                        }
                    );
                else
                    recipe = ComponentRecipe.Defer(
                        "restored-issue",
                        ComponentRequirements.Context<RouteContext<IssueRoute>>(Source("issue")),
                        (_, context) =>
                        {
                            if (
                                context.Parameters.ProjectId != projectId
                                || context.Parameters.IssueId != 42
                                || context.Parameters.View != IssueView.Activity
                            )
                                throw new InvalidOperationException(
                                    "Restored issue context was incorrect."
                                );
                            mountedIssue = true;
                            return ComponentRecipe.Create("restored-leaf", static (_, _) => { });
                        }
                    );
                return new RouteDestination(typeof(AppRoutes), recipe, level.Id);
            }
        );
        var options = new RouteOutletOptions(
            prepare: (level, request, _) =>
            {
                if (
                    request.Origin != NavigationOrigin.Restoration
                    || request.Phase != NavigationPreparationPhase.Enter
                    || request.Current is not null
                    || session.Current is not null
                    || handle.Snapshot.Levels.Count != 0
                )
                    throw new InvalidOperationException(
                        "Restoration published before Enter preparation."
                    );
                prepared.Add(level.Id.Value);
                return ValueTask.FromResult(
                    scenario == "guard-fallback" && level.Id.Value == "issue"
                        ? NavigationPreparationResult.Stay
                        : NavigationPreparationResult.Allow
                );
            }
        );
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Router([Components.RouterOutlet(options, handle)], bundle, session: session)
        );
        if (session.Current is not null || handle.Snapshot.Levels.Count != 0 || prepared.Count != 0)
            throw new InvalidOperationException(
                "Empty startup mounted a route before restoration."
            );
        var plan = restoration.Decode(scenario == "invalid-fallback" ? "broken"u8 : expectedBytes);
        var operation = session.Restore(plan);
        if (
            !operation.Completion.IsCompletedSuccessfully
            || operation.Completion.Result.Kind != NavigationOutcomeKind.Committed
            || session.Current?.DefinitionId.Value != (fallback ? "project" : "issue")
            || session.Journal.Entries.Count != 1
            || !mountedProject
            || mountedIssue == fallback
            || handle.Snapshot.EntryId != session.Current.EntryId
            || handle.Snapshot.Levels.Count != (fallback ? 1 : 2)
        )
            throw new InvalidOperationException(
                "Packaged restoration did not publish the expected route."
            );
        var expectedPreparation = scenario switch
        {
            "guard-fallback" => "project,issue,project",
            "invalid-fallback" => "project",
            _ => "project,issue",
        };
        if (string.Join(',', prepared) != expectedPreparation)
            throw new InvalidOperationException(
                "Packaged restoration did not prepare fallback exactly once."
            );
    }
}

static void VerifyMountedRestoration(RouteDescriptorSet descriptors, Guid projectId)
{
    NavigationSession? navigation = null;
    using var outlet = new RouteOutletHandle();
    var mountedCalls = 0;
    var restoration = new NavigationRestoration(
        descriptors.Table,
        "mounted-v1",
        AppRoutes.Project(projectId),
        static _ => true
    );
    var bundle = RouteBundle.Create(
        descriptors,
        level => new RouteDestination(
            typeof(AppRoutes),
            ComponentRecipe.Create("mounted-project", static (_, _) => { }),
            level.Id
        )
    );
    var app = LucentApplication
        .CreateBuilder()
        .UseHost(new ConsoleLifecycleHost())
        .ConfigureRoot(
            (session, _) =>
            {
                navigation = new NavigationSession(session.Scope, descriptors.Table);
                return Components.Router(
                    [Components.RouterOutlet(handle: outlet)],
                    bundle,
                    session: navigation
                );
            }
        )
        .OnMounted(_ =>
        {
            mountedCalls++;
            if (
                navigation is null
                || navigation.Current is not null
                || outlet.Snapshot.Levels.Count != 0
            )
                throw new InvalidOperationException(
                    "The mounted callback did not start from an empty root outlet."
                );
            var replay = navigation.Restore(restoration.Decode([]));
            if (
                !replay.Completion.IsCompletedSuccessfully
                || !replay.Completion.Result.IsCommitted
                || navigation.Current?.DefinitionId.Value != "project"
                || outlet.Snapshot.TerminalDefinition?.Value != "project"
            )
                throw new InvalidOperationException(
                    "The public mounted hook could not replay the startup route."
                );
            return ValueTask.CompletedTask;
        })
        .Build(ComponentRecipe.Create("startup-placeholder", static (_, _) => { }));
    if (app.Run() != 0 || mountedCalls != 1 || navigation is not { IsDisposed: true })
        throw new InvalidOperationException(
            "Mounted restoration did not finish the application lifecycle cleanly."
        );
}

static void VerifyJournalRestoration(RouteDescriptorSet descriptors, Guid projectId)
{
    // Fixed wire input exercises the shipped static codec under trimming/AOT. The
    // behavior matrix and native focus/scroll transport remain in their owner suites.
    const string snapshot =
        """{"schema":"lucent.navigation","version":1,"scope":"package-journal-v1","mode":"journal","activeKey":7,"entries":[{"key":7,"definition":"project","location":"/projects/11111111-1111-1111-1111-111111111111?view=Activity"},{"key":42,"definition":"issue","location":"/projects/11111111-1111-1111-1111-111111111111/issues/42?view=Activity","state":{"codec":"lucent.interaction","version":1,"focus":"issue-heading","viewports":[{"target":"issue-body","x":0,"y":42}]}}]}""";
    var restoration = new NavigationRestoration(
        descriptors.Table,
        "package-journal-v1",
        AppRoutes.Project(projectId),
        static _ => true,
        options: new(
            NavigationRestorationMode.Journal,
            stateCodecs: NavigationRestorationStateCodecs.Interaction
        )
    );
    NavigationSession? navigation = null;
    NavigationInteraction? interaction = null;
    using var outlet = new RouteOutletHandle();
    var mountedCalls = 0;
    var bundle = RouteBundle.Create(
        descriptors,
        level => new RouteDestination(
            typeof(AppRoutes),
            level.Id.Value == "project"
                ? Components.RouterOutlet()
                : ComponentRecipe.Defer(
                    "journal-issue",
                    ComponentRequirements.Context<RouteContext<IssueRoute>>(Source("journal")),
                    (_, context) =>
                    {
                        if (
                            context.Parameters.IssueId != 42
                            || context.Parameters.ProjectId != projectId
                        )
                            throw new InvalidOperationException(
                                "Journal lost generated route parameters."
                            );
                        return ComponentRecipe.Create("journal-leaf", static (_, _) => { });
                    }
                ),
            level.Id
        )
    );
    var application = LucentApplication
        .CreateBuilder()
        .UseHost(new ConsoleLifecycleHost())
        .ConfigureRoot(
            (session, _) =>
            {
                navigation = new NavigationSession(session.Scope, descriptors.Table);
                interaction = new NavigationInteraction(session.Scope, navigation);
                return Components.NavigationBoundary(
                    [
                        Components.Router(
                            [
                                Components.RouterOutlet(
                                    new RouteOutletOptions(interaction: interaction),
                                    outlet
                                ),
                            ],
                            bundle,
                            session: navigation
                        ),
                    ],
                    interaction
                );
            }
        )
        .OnMounted(_ =>
        {
            mountedCalls++;
            if (navigation is null || interaction is null || navigation.Current is not null)
                throw new InvalidOperationException("Journal startup was not empty.");
            var plan = restoration.Decode(Encoding.UTF8.GetBytes(snapshot));
            if (
                plan.Status != NavigationRestorationStatus.Ready
                || plan.DroppedEntries != 0
                || plan.DroppedStates != 0
            )
                throw new InvalidOperationException(
                    $"Packaged journal/state codec rejected fixed input: {plan.Status}, dropped entries={plan.DroppedEntries}, states={plan.DroppedStates}."
                );
            RequireCommitted(navigation.Restore(plan));
            var journal = navigation.Journal;
            if (journal.Entries.Count != 2 || journal.CurrentIndex != 0 || !navigation.CanGoForward)
                throw new InvalidOperationException(
                    "Journal did not restore the active cursor and forward history."
                );
            var dormantId = journal.Entries[1].EntryId;
            if (
                dormantId == 42
                || !interaction.TryGetEntryState(dormantId, out var state)
                || state is null
                || state.FocusTargetId != "issue-heading"
                || state.Viewports.Count != 1
                || state.Viewports[0].TargetId != "issue-body"
                || state.Viewports[0].Offset != new ScrollOffset(0, 42)
            )
                throw new InvalidOperationException(
                    "Journal did not map decoded state to a fresh runtime entry."
                );
            var rootId = outlet.Snapshot.Levels[0].ElementId;
            RequireCommitted(navigation.Forward());
            if (
                navigation.Current?.EntryId != dormantId
                || outlet.Snapshot.Levels.Count != 2
                || outlet.Snapshot.Levels[0].ElementId != rootId
            )
                throw new InvalidOperationException(
                    "Forward did not mount the imported typed suffix on the retained root."
                );
            RequireCommitted(navigation.Back());
            if (navigation.Journal.CurrentIndex != 0 || outlet.Snapshot.Levels.Count != 1)
                throw new InvalidOperationException("Back did not restore the imported cursor.");
            if (restoration.Capture(navigation).Status != NavigationRestorationStatus.Ready)
                throw new InvalidOperationException(
                    "The imported journal could not be captured again."
                );
            return ValueTask.CompletedTask;
        })
        .Build(ComponentRecipe.Create("journal-placeholder", static (_, _) => { }));
    if (application.Run() != 0 || mountedCalls != 1 || navigation is not { IsDisposed: true })
        throw new InvalidOperationException(
            "Journal lifecycle did not complete and dispose its owner."
        );

    static void RequireCommitted(NavigationOperation operation)
    {
        if (
            !operation.Completion.IsCompletedSuccessfully
            || !operation.Completion.Result.IsCommitted
        )
            throw new InvalidOperationException("Packaged journal navigation did not commit.");
    }
}

internal sealed class ConsoleLifecycleHost : IApplicationHost
{
    public int Run(ApplicationSession session)
    {
        session.Start();
        Pump(
            session,
            () => session.Status.Phase == ApplicationPhase.Running || session.IsCompleted
        );
        if (session.IsCompleted)
            throw new InvalidOperationException(
                "Console application failed startup.",
                session.Status.Error
            );
        session.RequestClose();
        Pump(session, () => session.IsCompleted);
        if (session.Status.Error is { } error)
            throw new InvalidOperationException("Console application failed cleanup.", error);
        return 0;
    }

    private static void Pump(ApplicationSession session, Func<bool> complete)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!complete())
        {
            session.ProcessEvents();
            if (!session.Composition.IsDisposed)
                session.Composition.Flush();
            if (Environment.TickCount64 >= deadline)
                throw new TimeoutException("Console restoration lifecycle did not settle.");
            Thread.Sleep(1);
        }
    }
}
