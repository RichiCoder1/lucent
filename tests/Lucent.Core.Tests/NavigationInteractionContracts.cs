using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class NavigationInteractionContracts
{
    [TestMethod]
    public void ComponentOwnedInteractionDefersNestedOutletInitialReconciliation()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "component-owned-navigation");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var table = Table("first");
        var descriptors = Descriptors(table);
        NavigationSession? session = null;
        NavigationInteraction? interaction = null;
        var component = ComponentRecipe.Defer(
            "component-owned-navigation",
            owner =>
            {
                var ownedSession = new NavigationSession(owner, table, Location("/first"));
                var ownedInteraction = new NavigationInteraction(owner, ownedSession);
                session = ownedSession;
                interaction = ownedInteraction;
                var focus = new FocusTarget(owner, "component-owned-focus");
                var outlet = RouteOutlet.Create(
                    descriptors,
                    _ =>
                        Components.NavigationTarget(
                            [Components.Button("First", focusTarget: focus)],
                            ownedInteraction,
                            "first",
                            "First",
                            focus,
                            NavigationTargetKind.Heading
                        ),
                    options: new RouteOutletOptions(interaction: ownedInteraction)
                );
                return Components.NavigationBoundary(
                    [Context.Provide(ownedSession, outlet)],
                    ownedInteraction
                );
            }
        );

        composition.Mount(composition.Root, theme, component);
        graph.Drain();

        Assert.IsNotNull(session);
        Assert.IsNotNull(interaction);
        Assert.AreEqual("first", session.Current!.DefinitionId.Value);
    }

    [TestMethod]
    public void RetainedOutletPublishesFocusAndViewportStateThroughInteractionHook()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("integrated-navigation-owner");
        var table = Table("first", "second");
        var descriptors = Descriptors(table);
        using var session = new NavigationSession(owner, table, Location("/first"));
        using var interaction = new NavigationInteraction(owner, session);
        using var composition = new Composition(graph, "integrated-navigation");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var firstFocus = new FocusTarget(composition.Root.Scope, "first-button-focus");
        using var secondFocus = new FocusTarget(composition.Root.Scope, "second-button-focus");
        using var firstViewport = new ViewportState(
            composition.Root.Scope,
            name: "first-button-viewport"
        );
        using var secondViewport = new ViewportState(
            composition.Root.Scope,
            name: "second-button-viewport"
        );
        using var outletHandle = new RouteOutletHandle();
        ComponentRecipe Level(RouteLevelDescriptor level)
        {
            var first = level.Id.Value == "first";
            var label = first ? "First action" : "Second action";
            var focus = first ? firstFocus : secondFocus;
            var viewport = first ? firstViewport : secondViewport;
            return Components.NavigationTarget(
                [Components.Button(label, focusTarget: focus)],
                interaction,
                "action",
                label,
                focus,
                NavigationTargetKind.Heading,
                viewport
            );
        }
        var outlet = RouteOutlet.Create(
            descriptors,
            Level,
            handle: outletHandle,
            options: new RouteOutletOptions(interaction: interaction)
        );
        composition.Mount(
            composition.Root,
            theme,
            Components.NavigationBoundary(
                [Context.Provide(session, outlet)],
                interaction,
                "Workspace"
            )
        );
        graph.Drain();
        Assert.AreEqual(1, outletHandle.Snapshot.Levels.Count);
        Assert.IsNotNull(composition.Input.FocusTargetIdentity(firstFocus));
        Assert.IsTrue(
            firstFocus.IsPending,
            "Initial outlet publication did not request its heading."
        );
        using var initial = Install(composition, graph);
        Assert.AreEqual("First action", FocusedName(composition));

        firstViewport.Offset = new(9, 27);
        Completed(
            interaction.Navigate(
                Reference(table, "second"),
                NavigationHistoryAction.Replace,
                NavigationOrigin.Link
            )
        );
        using var replaced = Install(composition, graph);

        Assert.AreEqual("Second action", FocusedName(composition));
        Assert.AreEqual(firstViewport.Offset, secondViewport.Offset);

        secondViewport.Offset = new(4, 48);
        Completed(interaction.Navigate(Reference(table, "second"), NavigationHistoryAction.Push));
        using var sameRoutePush = Install(composition, graph);
        Assert.AreEqual(
            default,
            secondViewport.Offset,
            "A same-route Push must start a fresh entry instead of inheriting its predecessor."
        );
        Assert.AreEqual("Second action", FocusedName(composition));

        var boundary = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Name == "Workspace");
        Assert.AreEqual(SemanticAnnouncement.Polite, boundary.Announcement);
        Assert.AreEqual("Second action", boundary.Description);
    }

    [TestMethod]
    public void ReplaceTransfersTargetStateAndPushProtectsRetainedParentFocus()
    {
        using var fixture = new InteractionFixture();
        var first = fixture.Entry(1, "first");
        var replacement = fixture.Entry(2, "second");
        var pushed = fixture.Entry(3, "third");
        var oldOutlet = InteractionFixture.Outlet(first, fixture.ParentId, fixture.FirstId);
        var replacementOutlet = InteractionFixture.Outlet(
            replacement,
            fixture.ParentId,
            fixture.SecondId
        );

        fixture.Interaction.AfterPublish(
            InteractionFixture.Publication(null, first, NavigationHistoryAction.Replace, [first]),
            oldOutlet
        );
        fixture.Install();
        Assert.AreEqual(fixture.FirstId, fixture.Composition.Input.FocusedElement?.ElementId);

        fixture.FirstViewport.Offset = new(12, 34);
        var replace = InteractionFixture.Publication(
            first,
            replacement,
            NavigationHistoryAction.Replace,
            [replacement]
        );
        fixture.Interaction.BeforePublish(replace, oldOutlet);
        fixture.Interaction.AfterPublish(replace, replacementOutlet);
        fixture.Install();

        Assert.AreEqual(fixture.SecondId, fixture.Composition.Input.FocusedElement?.ElementId);
        Assert.AreEqual(new ScrollOffset(12, 34), fixture.SecondViewport.Offset);
        Assert.IsTrue(
            fixture.Interaction.TryGetEntryState(replacement.EntryId, out var replacementState)
        );
        Assert.AreEqual("editor", replacementState!.FocusTargetId);

        Assert.IsTrue(
            fixture.Composition.Input.FocusSemantic(
                new ElementIdentity(fixture.Composition.Epoch, fixture.ParentId)
            )
        );
        var push = InteractionFixture.Publication(
            replacement,
            pushed,
            NavigationHistoryAction.Push,
            [replacement, pushed]
        );
        fixture.Interaction.BeforePublish(push, replacementOutlet);
        fixture.Interaction.AfterPublish(
            push,
            InteractionFixture.Outlet(pushed, fixture.ParentId, fixture.ThirdId)
        );
        fixture.Install();

        Assert.AreEqual(
            fixture.ParentId,
            fixture.Composition.Input.FocusedElement?.ElementId,
            "A retained parent focus target must not be replaced by a fresh child target."
        );
        Assert.AreEqual(
            default,
            fixture.ThirdViewport.Offset,
            "Push must use fresh viewport state."
        );
        Assert.IsFalse(
            fixture.Interaction.TryGetEntryState(first.EntryId, out _),
            "Interaction state must be pruned with the bounded journal."
        );
        var boundary = fixture.Semantics("Navigation");
        Assert.AreEqual(SemanticRole.Group, boundary.Role);
        Assert.AreEqual(SemanticAnnouncement.Polite, boundary.Announcement);
        Assert.AreEqual("Third editor", boundary.Description);
    }

    [TestMethod]
    public void TraversalFallsBackWhenStoredTargetIsMissingAndOverlapFailsClosed()
    {
        using var fixture = new InteractionFixture();
        var first = fixture.Entry(1, "first");
        var second = fixture.Entry(2, "second");
        var oldOutlet = InteractionFixture.Outlet(first, fixture.FirstId);
        var secondOutlet = InteractionFixture.Outlet(second, fixture.SecondId);

        fixture.Interaction.AfterPublish(
            InteractionFixture.Publication(null, first, NavigationHistoryAction.Replace, [first]),
            oldOutlet
        );
        fixture.Install();
        var push = InteractionFixture.Publication(
            first,
            second,
            NavigationHistoryAction.Push,
            [first, second]
        );
        fixture.Interaction.BeforePublish(push, oldOutlet);
        fixture.Interaction.AfterPublish(push, secondOutlet);
        fixture.Install();

        var back = InteractionFixture.Publication(
            second,
            first,
            NavigationHistoryAction.Back,
            [first, second],
            currentIndex: 0
        );
        fixture.Interaction.BeforePublish(back, secondOutlet);
        fixture.Interaction.AfterPublish(
            back,
            InteractionFixture.Outlet(first, fixture.FallbackId)
        );
        fixture.Install();
        Assert.AreEqual(fixture.FallbackId, fixture.Composition.Input.FocusedElement?.ElementId);

        var duplicate = InteractionFixture.Outlet(first, fixture.FirstId, fixture.SecondId);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            fixture.Interaction.AfterPublish(back, duplicate)
        );
    }

    [TestMethod]
    public void InitialFocusRetryIsSupersededByImmediatePublication()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("initial-retry-owner");
        var table = Table("first", "second");
        var descriptors = Descriptors(table);
        using var session = new NavigationSession(owner, table, Location("/first"));
        using var interaction = new NavigationInteraction(owner, session);
        using var composition = new Composition(graph, "initial-retry");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var firstFocus = new FocusTarget(composition.Root.Scope, "initial-first-focus");
        using var secondFocus = new FocusTarget(composition.Root.Scope, "initial-second-focus");
        using var firstViewport = new ViewportState(
            composition.Root.Scope,
            name: "initial-first-viewport"
        );
        using var secondViewport = new ViewportState(
            composition.Root.Scope,
            name: "initial-second-viewport"
        );
        ComponentRecipe Level(RouteLevelDescriptor level)
        {
            var first = level.Id.Value == "first";
            var focus = first ? firstFocus : secondFocus;
            var viewport = first ? firstViewport : secondViewport;
            return Components.NavigationTarget(
                [Components.Button(level.Id.Value, focusTarget: focus)],
                interaction,
                "action",
                level.Id.Value,
                focus,
                NavigationTargetKind.Heading,
                viewport
            );
        }
        var outlet = RouteOutlet.Create(
            descriptors,
            Level,
            options: new RouteOutletOptions(interaction: interaction)
        );
        composition.Mount(
            composition.Root,
            theme,
            Components.NavigationBoundary([Context.Provide(session, outlet)], interaction)
        );

        firstViewport.Offset = new(13, 21);
        var outcome = Completed(
            interaction.Navigate(Reference(table, "second"), NavigationHistoryAction.Replace)
        );
        Assert.AreEqual(NavigationOutcomeKind.Committed, outcome.Kind);
        graph.Drain();

        Assert.AreEqual("second", session.Current!.DefinitionId.Value);
        Assert.IsTrue(
            interaction.TryGetEntryState(session.Current.EntryId, out var retained),
            "The superseded operation-zero retry pruned the replacement entry state."
        );
        Assert.AreEqual(new ScrollOffset(13, 21), retained!.Viewports.Single().Offset);
    }

    [TestMethod]
    public void DuplicateCandidateFailsBeforeSwapAndDisposesItsRegistrations()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("duplicate-candidate-owner");
        var table = Table("first", "second");
        var descriptors = Descriptors(table);
        using var session = new NavigationSession(owner, table, Location("/first"));
        using var interaction = new NavigationInteraction(owner, session);
        using var composition = new Composition(graph, "duplicate-candidate");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var firstFocus = new FocusTarget(composition.Root.Scope, "duplicate-first-focus");
        using var secondFocus = new FocusTarget(composition.Root.Scope, "duplicate-second-focus");
        using var duplicateFocus = new FocusTarget(
            composition.Root.Scope,
            "duplicate-second-focus-copy"
        );
        using var handle = new RouteOutletHandle();
        ComponentRecipe Target(string label, FocusTarget focus) =>
            Components.NavigationTarget(
                [Components.Button(label, focusTarget: focus)],
                interaction,
                "action",
                label,
                focus,
                NavigationTargetKind.Heading
            );
        ComponentRecipe Level(RouteLevelDescriptor level) =>
            level.Id.Value == "first"
                ? Target("First", firstFocus)
                : Components.Column([
                    Target("Second", secondFocus),
                    Target("Duplicate", duplicateFocus),
                ]);
        var outlet = RouteOutlet.Create(
            descriptors,
            Level,
            handle,
            options: new RouteOutletOptions(interaction: interaction)
        );
        composition.Mount(
            composition.Root,
            theme,
            Components.NavigationBoundary([Context.Provide(session, outlet)], interaction)
        );
        graph.Drain();
        var committedRoot = handle.Snapshot.Levels.Single().ElementId;

        var outcome = Completed(interaction.Navigate(Reference(table, "second")));

        Assert.AreEqual(NavigationOutcomeKind.Failed, outcome.Kind);
        Assert.AreEqual(NavigationFailureKind.Terminal, outcome.FailureKind);
        Assert.IsTrue(session.IsTerminated);
        Assert.AreEqual("first", session.Current!.DefinitionId.Value);
        Assert.AreEqual(committedRoot, handle.Snapshot.Levels.Single().ElementId);
        Assert.IsNotNull(composition.Input.FocusTargetIdentity(firstFocus));
        Assert.IsNull(composition.Input.FocusTargetIdentity(secondFocus));
        Assert.IsNull(composition.Input.FocusTargetIdentity(duplicateFocus));
    }

    [TestMethod]
    public void CommandFactoriesRejectAnotherReactiveGraph()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("command-owner-graph");
        using var session = new NavigationSession(owner, Table("first"));
        using var interaction = new NavigationInteraction(owner, session);
        var otherGraph = new ReactiveGraph();
        using var otherOwner = otherGraph.CreateScope("other-command-owner");

        Assert.ThrowsExactly<ArgumentException>(() =>
            interaction.CreateBackCommand(otherOwner, NavigationOrigin.Menu)
        );
        Assert.ThrowsExactly<ArgumentException>(() =>
            interaction.CreateNavigateCommand(
                otherOwner,
                () => Reference(session.RouteTable, "first")
            )
        );
    }

    [TestMethod]
    public void RetainedLiveReplacementRestoresTargetWithoutStealingPersistentShellFocus()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("retained-live-focus-owner");
        var firstPattern = RoutePattern.Create(
            new RouteDefinitionId("first"),
            [
                RouteSegmentPattern.LiteralSegment("shell"),
                RouteSegmentPattern.LiteralSegment("first"),
            ]
        );
        var secondPattern = RoutePattern.Create(
            new RouteDefinitionId("second"),
            [
                RouteSegmentPattern.LiteralSegment("shell"),
                RouteSegmentPattern.LiteralSegment("second"),
            ]
        );
        var table = RouteTable.Create([firstPattern, secondPattern]);
        var source = new RouteDeclarationSource("tests/focus-routes.lui", 1, 1);
        RouteContext<string>? parentContext = null;
        var parent = new RouteLevelDescriptor(
            new RouteDefinitionId("shell"),
            [],
            source,
            (definition, _, content, live) =>
            {
                parentContext = new RouteContext<string>(definition, "shell", live);
                return Context.Provide(parentContext, content);
            }
        );
        var first = new RouteLevelDescriptor(
            firstPattern.Id,
            [],
            source,
            static (_, _, content, _) => content
        );
        var second = new RouteLevelDescriptor(
            secondPattern.Id,
            [],
            source,
            static (_, _, content, _) => content
        );
        var descriptors = RouteDescriptorSet.Create(
            table,
            [
                new RouteModuleDescriptor(
                    "focus-routes",
                    RouteFallbackPolicy.Reject,
                    source,
                    [
                        new RouteDefinitionDescriptor(firstPattern, [parent, first]),
                        new RouteDefinitionDescriptor(secondPattern, [parent, second]),
                    ]
                ),
            ]
        );
        using var session = new NavigationSession(owner, table, Location("/shell/first"));
        using var interaction = new NavigationInteraction(owner, session);
        using var composition = new Composition(graph, "retained-live-focus");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var shellFocus = new FocusTarget(composition.Root.Scope, "persistent-shell-focus");
        using var firstFocus = new FocusTarget(composition.Root.Scope, "retained-first-focus");
        using var secondFocus = new FocusTarget(composition.Root.Scope, "retained-second-focus");
        var focusPersistentShell = false;
        ElementIdentity? persistentShellIdentity = null;
        ComponentRecipe Target(string label, string id, FocusTarget focus) =>
            Components.NavigationTarget(
                [Components.Button(label, focusTarget: focus)],
                interaction,
                id,
                label,
                focus,
                NavigationTargetKind.Heading
            );
        ComponentRecipe Level(RouteLevelDescriptor level) =>
            level.Id.Value == "shell"
                ? ComponentRecipe.Create(
                    "retained-focus-shell",
                    (context, root) =>
                    {
                        root.Present(
                            context.Theme,
                            author: Style.Empty.Axis(LayoutAxis.Column).MainGrow(1)
                        );
                        _ = context.Switch(
                            root,
                            "retained-focus-target",
                            () =>
                                parentContext!.ActiveEntry!.DefinitionId.Value == "first"
                                    ? new ConditionalChoice(
                                        0,
                                        Target("First target", "target", firstFocus)
                                    )
                                    : new ConditionalChoice(
                                        1,
                                        Target("Second target", "target", secondFocus)
                                    )
                        );
                        _ = root.Scope.Effect(
                            () =>
                            {
                                _ = parentContext!.ActiveEntry;
                                if (focusPersistentShell && persistentShellIdentity is { } identity)
                                    _ = composition.Input.FocusSemantic(identity);
                            },
                            "retained-persistent-shell-focus"
                        );
                        context.Mount(root, RouteOutlet.CreateChild(descriptors, Level));
                    }
                )
                : ComponentRecipe.Create(
                    "retained-focus-leaf",
                    (context, root) =>
                        root.Present(context.Theme, author: Style.Empty.Width(20).Height(20))
                );
        var outlet = RouteOutlet.Create(
            descriptors,
            Level,
            options: new RouteOutletOptions(interaction: interaction)
        );
        composition.Mount(
            composition.Root,
            theme,
            Components.NavigationBoundary(
                [
                    Components.Button("Persistent shell", focusTarget: shellFocus),
                    Context.Provide(session, outlet),
                ],
                interaction
            )
        );
        graph.Drain();
        using var initial = Install(composition, graph);
        firstFocus.Request();
        using var firstFocused = Install(composition, graph);
        Assert.AreEqual("First target", FocusedName(composition));

        var secondOutcome = Completed(
            interaction.Navigate(
                RouteReference.Create(secondPattern, []),
                NavigationHistoryAction.Replace
            )
        );
        Assert.AreEqual(NavigationOutcomeKind.Committed, secondOutcome.Kind);
        graph.Drain();
        Assert.IsTrue(
            secondFocus.IsPending,
            "Post-publication reconciliation did not request the replacement target."
        );
        using var secondScene = Install(composition, graph);
        Assert.AreEqual("Second target", FocusedName(composition));

        persistentShellIdentity = composition.Input.FocusTargetIdentity(shellFocus);
        Assert.IsNotNull(persistentShellIdentity);
        focusPersistentShell = true;
        var retainedOutcome = Completed(
            interaction.Navigate(
                RouteReference.Create(secondPattern, []),
                NavigationHistoryAction.Push
            )
        );
        Assert.AreEqual(NavigationOutcomeKind.Committed, retainedOutcome.Kind);
        graph.Drain();
        using var shellScene = Install(composition, graph);
        Assert.AreEqual("Persistent shell", FocusedName(composition));
    }

    [TestMethod]
    public void UnappliedStageRetiresRootsRecordedByPartialPublication()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("partial-publication-owner");
        var table = Table("first");
        var descriptors = Descriptors(table);
        using var session = new NavigationSession(owner, table);
        using var composition = new Composition(graph, "partial-publication");
        var mount = new RouteOutletMount(
            session,
            descriptors,
            static _ => Components.Text("unused"),
            composition.Root,
            cursor: null,
            handle: null,
            options: null
        );
        using var stage = new RouteOutletStage(mount);
        var retired = composition.Create(
            composition.Root,
            "partially-detached-root",
            attach: false
        );
        var disposed = false;
        retired.Scope.OnDispose(() => disposed = true);
        stage.AddRetired(retired);

        stage.Dispose();

        Assert.IsTrue(disposed);
        Assert.IsTrue(retired.IsDisposed);
        Assert.AreEqual(0, stage.Retired.Count);
        mount.Dispose();
    }

    [TestMethod]
    public void BackCommandsReadLiveSessionAndNearestScopeKeepsPrecedence()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("navigation-command-owner");
        var table = Table("first", "second");
        using var session = new NavigationSession(owner, table, Location("/first"));
        using var interaction = new NavigationInteraction(owner, session);
        using var menuBack = interaction.CreateBackCommand(
            owner,
            NavigationOrigin.Menu,
            "menu-back"
        );

        Assert.IsFalse(menuBack.IsEnabled);
        Completed(interaction.Navigate(Reference(table, "second")));
        Assert.IsTrue(menuBack.IsEnabled);
        Assert.IsTrue(menuBack.TryExecute());
        graph.Drain();
        Assert.AreEqual("first", session.Current!.DefinitionId.Value);

        Completed(interaction.Navigate(Reference(table, "second")));
        Assert.IsTrue(
            menuBack.IsEnabled,
            "A persistent popup command must read the new journal state."
        );

        using var composition = new Composition(graph, "navigation-command-scope");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var editorFocus = new FocusTarget(composition.Root.Scope, "editor-focus");
        var intercepted = 0;
        using var editorBack = new ApplicationCommand(
            composition.Root.Scope,
            _ =>
            {
                intercepted++;
                return Task.CompletedTask;
            },
            name: "editor-back"
        );
        composition.Mount(
            composition.Root,
            theme,
            Components.NavigationBoundary(
                [
                    Components.CommandScope(
                        [
                            Components.TextField(
                                label: "Editor",
                                focusTarget: editorFocus,
                                placeholder: ""
                            ),
                        ],
                        new CommandBindings([
                            new(editorBack, new KeyChord(Key.Left, KeyModifiers.Alt)),
                        ])
                    ),
                ],
                interaction
            )
        );
        graph.Drain();
        using var scene = Install(composition, graph);
        editorFocus.Request();
        using var focused = Install(composition, graph);
        var routed = composition.Input.DispatchKey(
            new KeyCommand(KeyCommandKind.Down, Key.Left, KeyModifiers.Alt)
        );
        graph.Drain();

        Assert.IsTrue(routed.Handled);
        Assert.AreEqual(
            0,
            intercepted,
            "The focused editor must consume its modified caret chord before command scopes."
        );
        Assert.AreEqual("second", session.Current!.DefinitionId.Value);
    }

    private static RouteTable Table(params string[] definitions) =>
        RouteTable.Create(
            definitions
                .Select(definition =>
                    RoutePattern.Create(
                        new RouteDefinitionId(definition),
                        [RouteSegmentPattern.LiteralSegment(definition)]
                    )
                )
                .ToArray()
        );

    private static RouteReference Reference(RouteTable table, string definition) =>
        RouteReference.Create(table.Patterns.Single(pattern => pattern.Id.Value == definition), []);

    private static RouteLocation Location(string text) => RouteLocation.Parse(text).Location!;

    private static RouteDescriptorSet Descriptors(RouteTable table)
    {
        var source = new RouteDeclarationSource("Routes.cs", 1, 1);
        var definitions = table
            .Patterns.Select(pattern =>
            {
                var level = new RouteLevelDescriptor(
                    pattern.Id,
                    [],
                    source,
                    static (_, _, content) => content
                );
                return new RouteDefinitionDescriptor(pattern, [level]);
            })
            .ToArray();
        var module = new RouteModuleDescriptor(
            "Routes",
            RouteFallbackPolicy.Reject,
            source,
            definitions
        );
        return RouteDescriptorSet.Create(table, [module]);
    }

    private static string? FocusedName(Composition composition)
    {
        var focused = composition.Input.FocusedElement;
        return focused is null
            ? null
            : Nodes(composition.SemanticSnapshot()!)
                .Single(node => node.Identity.ElementId == focused.Value.ElementId)
                .Name;
    }

    private static NavigationOutcome Completed(NavigationOperation operation)
    {
        Assert.IsTrue(operation.Completion.IsCompleted);
        return operation.Completion.Result;
    }

    private static RetainedScene Install(Composition composition, ReactiveGraph graph)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            graph.Drain();
            var scene = SceneLayout.Project(composition, new(500, 320, 1), new MetricShaper());
            if (composition.Input.SetScene(scene))
                return scene;
            scene.Dispose();
        }
        throw new InvalidOperationException("Navigation interaction scene did not converge.");
    }

    private sealed class InteractionFixture : IDisposable
    {
        private readonly ReactiveGraph _graph = new();
        private readonly ReactiveScope _owner;
        private readonly NavigationSession _session;
        private readonly ThemeContext _theme;
        private readonly FocusTarget _parentFocus;
        private readonly FocusTarget _firstFocus;
        private readonly FocusTarget _secondFocus;
        private readonly FocusTarget _thirdFocus;
        private readonly FocusTarget _fallbackFocus;
        private RetainedScene? _scene;

        internal InteractionFixture()
        {
            _owner = _graph.CreateScope("interaction-fixture");
            _session = new NavigationSession(_owner, Table("first", "second", "third"));
            Interaction = new NavigationInteraction(_owner, _session);
            Composition = new Composition(_graph, "interaction-composition");
            _theme = new ThemeContext(Composition.Root.Scope, ControlThemes.Light);
            _parentFocus = new FocusTarget(Composition.Root.Scope, "parent-focus");
            _firstFocus = new FocusTarget(Composition.Root.Scope, "first-focus");
            _secondFocus = new FocusTarget(Composition.Root.Scope, "second-focus");
            _thirdFocus = new FocusTarget(Composition.Root.Scope, "third-focus");
            _fallbackFocus = new FocusTarget(Composition.Root.Scope, "fallback-focus");
            FirstViewport = new ViewportState(Composition.Root.Scope, name: "first-viewport");
            SecondViewport = new ViewportState(Composition.Root.Scope, name: "second-viewport");
            ThirdViewport = new ViewportState(Composition.Root.Scope, name: "third-viewport");
            Composition.Mount(
                Composition.Root,
                _theme,
                Components.NavigationBoundary(
                    [
                        Target("Parent", "parent", _parentFocus, NavigationTargetKind.Useful),
                        Target(
                            "First editor",
                            "editor",
                            _firstFocus,
                            NavigationTargetKind.Heading,
                            FirstViewport
                        ),
                        Target(
                            "Second editor",
                            "editor",
                            _secondFocus,
                            NavigationTargetKind.Heading,
                            SecondViewport
                        ),
                        Target(
                            "Third editor",
                            "third",
                            _thirdFocus,
                            NavigationTargetKind.Heading,
                            ThirdViewport
                        ),
                        Target(
                            "Fallback heading",
                            "fallback",
                            _fallbackFocus,
                            NavigationTargetKind.Heading
                        ),
                    ],
                    Interaction
                )
            );
            _graph.Drain();
            Install();
            ParentId = Node("Parent").Identity.ElementId;
            FirstId = Node("First editor").Identity.ElementId;
            SecondId = Node("Second editor").Identity.ElementId;
            ThirdId = Node("Third editor").Identity.ElementId;
            FallbackId = Node("Fallback heading").Identity.ElementId;
        }

        internal Composition Composition { get; }
        internal NavigationInteraction Interaction { get; }
        internal ViewportState FirstViewport { get; }
        internal ViewportState SecondViewport { get; }
        internal ViewportState ThirdViewport { get; }
        internal long ParentId { get; }
        internal long FirstId { get; }
        internal long SecondId { get; }
        internal long ThirdId { get; }
        internal long FallbackId { get; }

        internal NavigationSnapshot Entry(long id, string definition)
        {
            var location = Location("/" + definition);
            var result = _session.RouteTable.Match(location);
            return new NavigationSnapshot(id, location, result.Match!);
        }

        internal static NavigationPublication Publication(
            NavigationSnapshot? previous,
            NavigationSnapshot current,
            NavigationHistoryAction history,
            IReadOnlyList<NavigationSnapshot> entries,
            int? currentIndex = null
        ) =>
            new(
                1,
                current.EntryId,
                previous,
                current,
                new NavigationJournalSnapshot(
                    entries,
                    currentIndex ?? entries.Count - 1,
                    capacity: 4
                ),
                history,
                NavigationOrigin.Application
            );

        internal static RouteOutletSnapshot Outlet(
            NavigationSnapshot current,
            params long[] roots
        ) =>
            new(
                current.EntryId,
                current.EntryId,
                current.DefinitionId,
                roots
                    .Select(
                        (root, level) =>
                            new RouteOutletLevelSnapshot(level, current.DefinitionId, root)
                    )
                    .ToArray()
            );

        internal void Install()
        {
            _scene?.Dispose();
            _scene = NavigationInteractionContracts.Install(Composition, _graph);
        }

        internal SemanticSnapshot Semantics(string name) =>
            Nodes(Composition.SemanticSnapshot()!).Single(node => node.Name == name);

        public void Dispose()
        {
            _scene?.Dispose();
            ThirdViewport.Dispose();
            SecondViewport.Dispose();
            FirstViewport.Dispose();
            _fallbackFocus.Dispose();
            _thirdFocus.Dispose();
            _secondFocus.Dispose();
            _firstFocus.Dispose();
            _parentFocus.Dispose();
            _theme.Dispose();
            Composition.Dispose();
            Interaction.Dispose();
            _session.Dispose();
            _owner.Dispose();
        }

        private ComponentRecipe Target(
            string label,
            string id,
            FocusTarget focus,
            NavigationTargetKind kind,
            ViewportState? viewport = null
        ) =>
            Components.NavigationTarget(
                [Components.TextField(label: label, focusTarget: focus, placeholder: "")],
                Interaction,
                id,
                label,
                focus,
                kind,
                viewport
            );

        private SemanticSnapshot Node(string name) =>
            Nodes(Composition.SemanticSnapshot()!).Single(node => node.Name == name);
    }

    private static IEnumerable<SemanticSnapshot> Nodes(SemanticSnapshot root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var nested in Nodes(child))
            yield return nested;
    }

    private sealed class MetricShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) =>
            request.Text.Length == 0
                ? new("empty", 0, 0, [])
                : new(
                    "navigation-interaction",
                    request.Text.Length,
                    request.FontSize,
                    [
                        new ShapedRun(
                            "navigation-interaction",
                            "navigation-interaction",
                            400,
                            5,
                            0,
                            "navigation-interaction",
                            0,
                            "navigation-interaction#0",
                            request.Direction,
                            request.Language,
                            request.FontSize,
                            0,
                            request.FontSize,
                            -request.FontSize,
                            0,
                            request.Text.Length,
                            [new(1, 0, 0, 0, request.Text.Length, 0, 0)]
                        ),
                    ]
                );
    }
}
