using Lucent.Core;

namespace Lucent.Core.Tests;

public sealed partial class RouteOutletContracts
{
    [TestMethod]
    public void RootDetachSupersedesPendingPreparationBeforeReplacementAttaches()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "detached-route-preparation");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var (bundle, _) = CreateAuthoringBundle();
        using var owner = graph.CreateScope("session");
        using var session = new NavigationSession(owner, bundle.Table, Location("/items/1"));
        using var handle = new RouteOutletHandle();
        var gate = new TaskCompletionSource<NavigationPreparationResult>();
        var enters = 0;
        var constructed = new List<int>();
        var options = new RouteOutletOptions(
            prepare: (_, request, _) =>
            {
                if (request.Phase == NavigationPreparationPhase.Leave)
                    return new(gate.Task);
                enters++;
                return ValueTask.FromResult(NavigationPreparationResult.Allow);
            },
            resolve: request =>
            {
                constructed.Add(request.GetContext<ItemRoute>().Parameters.Id);
                return request.Default;
            }
        );
        var root = composition.Mount(
            composition.Root,
            theme,
            Components.Router([Components.RouterOutlet(options)], bundle, session: session)
        );
        graph.Drain();
        constructed.Clear();
        var current = session.Current;
        var journal = session.Journal;
        var pending = session.Navigate(Location("/items/2"));
        Assert.AreEqual(NavigationPhase.PreparingLeave, session.Phase);

        root.Dispose();

        Assert.AreEqual(NavigationOutcomeKind.Superseded, Completed(pending).Kind);
        Assert.AreEqual(NavigationPhase.Idle, session.Phase);
        Assert.IsNull(session.Pending);
        composition.Mount(
            composition.Root,
            theme,
            Components.Router([Components.RouterOutlet(options, handle)], bundle, session: session)
        );
        graph.Drain();
        var replacement = handle.Snapshot.Levels.Single().ElementId;
        gate.SetResult(NavigationPreparationResult.Allow);
        graph.Drain();

        Assert.AreSame(current, session.Current);
        Assert.AreSame(journal, session.Journal);
        Assert.AreEqual(0, enters);
        Assert.HasCount(1, constructed);
        Assert.AreEqual(1, constructed[0]);
        Assert.AreEqual(replacement, handle.Snapshot.Levels.Single().ElementId);
        Assert.IsFalse(session.IsDisposed);
    }

    [TestMethod]
    [DataRow("back")]
    [DataRow("forward")]
    [DataRow("unmatched")]
    [DataRow("activation")]
    [DataRow("foreign-reference")]
    public void RejectedSupersedingRequestRefreshesTheCommittedDestination(string requestKind)
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "rejected-navigation-refresh");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var (bundle, _) = CreateAuthoringBundle();
        using var owner = graph.CreateScope("session");
        using var session = new NavigationSession(owner, bundle.Table, Location("/items/1"));
        using var handle = new RouteOutletHandle();
        var alternate = graph.Signal(false, "alternate");
        var gate = new TaskCompletionSource<NavigationPreparationResult>();
        var options = new RouteOutletOptions(
            prepare: (_, _, _) => new(gate.Task),
            resolve: request =>
                alternate.Value
                    ? new RouteDestination(
                        typeof(AlternatePage),
                        ComponentRecipe.Create("alternate", static (_, _) => { })
                    )
                    : request.Default
        );
        composition.Mount(
            composition.Root,
            theme,
            Components.Router([Components.RouterOutlet(options, handle)], bundle, session: session)
        );
        graph.Drain();
        var current = session.Current;
        var journal = session.Journal;
        var original = handle.Snapshot.Levels.Single().ElementId;
        var pending = session.Navigate(Location("/items/2"));
        alternate.Value = true;
        graph.Drain();
        Assert.AreEqual(original, handle.Snapshot.Levels.Single().ElementId);

        var rejected = requestKind switch
        {
            "back" => session.Back(),
            "forward" => session.Forward(),
            "unmatched" => session.Navigate(Location("/missing")),
            "activation" => session.Activate("not-an-in-app-location"),
            _ => session.Navigate(
                RouteReference.Create(
                    RoutePattern.Create(
                        new RouteDefinitionId("foreign"),
                        [
                            RouteSegmentPattern.LiteralSegment("items"),
                            RouteSegmentPattern.LiteralSegment("1"),
                        ]
                    ),
                    []
                )
            ),
        };
        Assert.AreEqual(NavigationOutcomeKind.Superseded, Completed(pending).Kind);
        Assert.AreEqual(
            requestKind is "back" or "forward"
                ? NavigationOutcomeKind.Stayed
                : NavigationOutcomeKind.RejectedActivation,
            Completed(rejected).Kind
        );
        graph.Drain();

        Assert.AreEqual(NavigationPhase.Idle, session.Phase);
        Assert.IsNull(session.Pending);
        Assert.AreSame(current, session.Current);
        Assert.AreSame(journal, session.Journal);
        Assert.AreNotEqual(original, handle.Snapshot.Levels.Single().ElementId);
        gate.SetResult(NavigationPreparationResult.Allow);
        graph.Drain();
        Assert.AreSame(current, session.Current);
    }

    [TestMethod]
    public void ValidSupersedingRequestDoesNotPublishAnIntermediateIdleReplacement()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "superseding-navigation-refresh");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var (bundle, _) = CreateAuthoringBundle();
        using var owner = graph.CreateScope("session");
        using var session = new NavigationSession(owner, bundle.Table, Location("/items/1"));
        using var handle = new RouteOutletHandle();
        var alternate = graph.Signal(false, "alternate");
        var gate = new TaskCompletionSource<NavigationPreparationResult>();
        var mounts = 0;
        var options = new RouteOutletOptions(
            prepare: (_, _, _) => new(gate.Task),
            resolve: request =>
                alternate.Value
                    ? new RouteDestination(
                        typeof(AlternatePage),
                        ComponentRecipe.Create("alternate", (_, _) => mounts++)
                    )
                    : request.Default
        );
        composition.Mount(
            composition.Root,
            theme,
            Components.Router([Components.RouterOutlet(options, handle)], bundle, session: session)
        );
        graph.Drain();
        var original = handle.Snapshot.Levels.Single().ElementId;
        var pending = session.Navigate(Location("/items/2"));
        alternate.Value = true;
        graph.Drain();
        var idleNotifications = 0;
        using var idle = session.RegisterIdle(owner, () => idleNotifications++);

        var replacement = session.Navigate(Location("/items/3"));
        graph.Drain();

        Assert.AreEqual(NavigationOutcomeKind.Superseded, Completed(pending).Kind);
        Assert.IsFalse(replacement.IsCompleted);
        Assert.AreEqual(NavigationPhase.PreparingLeave, session.Phase);
        Assert.AreEqual(0, idleNotifications);
        Assert.AreEqual(0, mounts);
        Assert.AreEqual(original, handle.Snapshot.Levels.Single().ElementId);
        replacement.Cancel();
        graph.Drain();
        Assert.AreEqual(1, idleNotifications);
        Assert.AreEqual(1, mounts);
        Assert.AreNotEqual(original, handle.Snapshot.Levels.Single().ElementId);
        gate.SetResult(NavigationPreparationResult.Allow);
        graph.Drain();
        Assert.AreEqual(1, session.Current!.Match.GetValue(0).Signed32);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RejectedDeferredIntentPublishesIdleAfterPreparationOrReplacementReturns(
        bool fromPreparation
    )
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "rejected-deferred-navigation");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var (bundle, _) = CreateAuthoringBundle();
        using var owner = graph.CreateScope("session");
        using var session = new NavigationSession(owner, bundle.Table, Location("/items/1"));
        using var handle = new RouteOutletHandle();
        var alternate = graph.Signal(false, "alternate");
        var gate = new TaskCompletionSource<NavigationPreparationResult>();
        var idleNotifications = 0;
        var inCallback = false;
        var mounts = 0;
        NavigationOperation? deferred = null;
        NavigationOperation? rejected = null;
        void SupersedeAndReject()
        {
            inCallback = true;
            deferred = session.Navigate(Location("/items/3"));
            alternate.Value = true;
            rejected = session.Back();
            Assert.AreEqual(0, idleNotifications);
            Assert.AreEqual(0, mounts);
            Assert.AreEqual(NavigationPhase.PreparingLeave, session.Phase);
            inCallback = false;
        }
        var options = new RouteOutletOptions(
            prepare: (_, _, _) =>
            {
                if (fromPreparation && deferred is null)
                    SupersedeAndReject();
                return new(gate.Task);
            },
            resolve: request =>
            {
                if (!alternate.Value)
                    return request.Default;
                if (!fromPreparation && deferred is null)
                    SupersedeAndReject();
                return new RouteDestination(
                    typeof(AlternatePage),
                    ComponentRecipe.Create("alternate", (_, _) => mounts++)
                );
            }
        );
        composition.Mount(
            composition.Root,
            theme,
            Components.Router([Components.RouterOutlet(options, handle)], bundle, session: session)
        );
        graph.Drain();
        var current = session.Current;
        var journal = session.Journal;
        var original = handle.Snapshot.Levels.Single().ElementId;
        using var idle = session.RegisterIdle(
            owner,
            () =>
            {
                Assert.IsFalse(inCallback);
                Assert.IsTrue(rejected!.IsCompleted);
                idleNotifications++;
            }
        );

        if (fromPreparation)
            Assert.AreEqual(
                NavigationOutcomeKind.Superseded,
                Completed(session.Navigate(Location("/items/2"))).Kind
            );
        else
            alternate.Value = true;
        graph.Drain();

        Assert.IsNotNull(deferred);
        Assert.IsNotNull(rejected);
        Assert.AreEqual(NavigationOutcomeKind.Superseded, Completed(deferred).Kind);
        Assert.AreEqual(NavigationFailureKind.HistoryBoundary, Completed(rejected).FailureKind);
        Assert.AreEqual(1, idleNotifications);
        Assert.AreEqual(1, mounts);
        Assert.AreEqual(NavigationPhase.Idle, session.Phase);
        Assert.IsNull(session.Pending);
        Assert.AreSame(current, session.Current);
        Assert.AreSame(journal, session.Journal);
        Assert.AreNotEqual(original, handle.Snapshot.Levels.Single().ElementId);
        gate.SetResult(NavigationPreparationResult.Allow);
        graph.Drain();
        Assert.AreSame(current, session.Current);
    }

    [TestMethod]
    [DataRow("resolver")]
    [DataRow("setup")]
    [DataRow("cleanup")]
    [DataRow("setup-and-cleanup")]
    public void FailedReplacementDoesNotStartDeferredNavigation(string failure)
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "replacement-abort");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var (bundle, _) = CreateAuthoringBundle();
        using var owner = graph.CreateScope("session");
        using var session = new NavigationSession(owner, bundle.Table, Location("/items/1"));
        using var handle = new RouteOutletHandle();
        var trigger = graph.Signal(false, "trigger");
        var prepares = 0;
        NavigationOperation? operation = null;
        var options = new RouteOutletOptions(
            prepare: (_, _, _) =>
            {
                prepares++;
                return ValueTask.FromResult(NavigationPreparationResult.Allow);
            },
            resolve: request =>
            {
                if (!trigger.Value || operation is not null)
                    return request.Default;
                if (failure == "resolver")
                {
                    operation = session.Navigate(Location("/items/2"));
                    throw new InvalidOperationException("resolver failure");
                }
                return new RouteDestination(
                    typeof(AlternatePage),
                    ComponentRecipe.Create(
                        "candidate",
                        (_, root) =>
                        {
                            if (failure is "cleanup" or "setup-and-cleanup")
                                root.Scope.OnDispose(() =>
                                    throw new InvalidOperationException("cleanup failure")
                                );
                            operation = session.Navigate(Location("/items/2"));
                            if (failure is "setup" or "setup-and-cleanup")
                                throw new InvalidOperationException("setup failure");
                        }
                    )
                );
            }
        );
        composition.Mount(
            composition.Root,
            theme,
            Components.Router([Components.RouterOutlet(options, handle)], bundle, session: session)
        );
        graph.Drain();
        var original = handle.Snapshot.Levels[0].ElementId;
        trigger.Value = true;
        var error = Assert.Throws<AggregateException>(() => graph.Drain());
        StringAssert.Contains(
            error.ToString(),
            failure == "setup-and-cleanup" ? "setup failure" : failure + " failure"
        );
        if (failure == "setup-and-cleanup")
            StringAssert.Contains(error.ToString(), "cleanup failure");
        Assert.AreEqual(
            0,
            prepares,
            "Failed replacement must abort deferred navigation before releasing its reservation."
        );
        Assert.AreEqual(NavigationPhase.Terminal, session.Phase);
        Assert.IsNotNull(operation);
        Assert.AreEqual(NavigationFailureKind.Terminal, Completed(operation).FailureKind);
        Assert.AreEqual(1, session.Current!.Match.GetValue(0).Signed32);
        Assert.AreEqual(original, handle.Snapshot.Levels[0].ElementId);
        Assert.IsNotNull(composition.Find(new ElementIdentity(composition.Epoch, original)));
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, false)]
    [DataRow(false, true)]
    public void ReplacementTracksConsumedDerivedReadsAcrossResolvers(
        bool mutateAfterRead,
        bool warmDerived
    )
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "mixed-resolver-selection");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var bundle = CreateNestedAuthoringBundle(static () => { });
        using var owner = graph.CreateScope("session");
        using var session = new NavigationSession(
            owner,
            bundle.Table,
            Location("/projects/1/issues/2")
        );
        var trigger = graph.Signal(false, "trigger");
        var mode = graph.Signal(0, "mode");
        using var derived = graph.Derived(() => mode.Value, "derived-mode");
        var mounted = new List<int>();
        var options = new RouteOutletOptions(resolve: request =>
        {
            if (!trigger.Value)
                return request.Default;
            if (request.Level.Id.Value == "issue")
            {
                if (mutateAfterRead)
                    mode.Value = 1;
                _ = derived.Value;
                return request.Default;
            }
            var choice = derived.Value;
            return new RouteDestination(
                typeof(AlternatePage),
                ComponentRecipe.Create(
                    "parent",
                    (context, root) =>
                    {
                        mounted.Add(choice);
                        context.Mount(root, Components.RouterOutlet());
                    }
                ),
                choice
            );
        });
        composition.Mount(
            composition.Root,
            theme,
            Components.Router([Components.RouterOutlet(options)], bundle, session: session)
        );
        graph.Drain();
        if (warmDerived)
        {
            _ = derived.Value;
            mode.Value = 1;
        }
        trigger.Value = true;
        graph.Drain();
        Assert.HasCount(1, mounted);
        Assert.AreEqual(
            mutateAfterRead || warmDerived ? 1 : 0,
            mounted[0],
            "Never mount a parent chosen from the stale half of a mixed selection."
        );
        Assert.AreEqual(NavigationPhase.Idle, session.Phase);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void InitialMountDefersNavigationUntilCandidateRollback(bool fromMount)
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "initial-navigation-reentrancy");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var (bundle, _) = CreateAuthoringBundle();
        using var owner = graph.CreateScope("session");
        using var session = new NavigationSession(owner, bundle.Table, Location("/items/1"));
        using var handle = new RouteOutletHandle();
        var gate = new TaskCompletionSource<NavigationPreparationResult>();
        NavigationOperation? operation = null;
        var disposed = 0;
        var options = new RouteOutletOptions(
            prepare: (_, _, _) => new(gate.Task),
            resolve: _ =>
            {
                if (!fromMount && operation is null)
                    operation = session.Navigate(Location("/items/2"));
                return new RouteDestination(
                    typeof(DefaultPage),
                    ComponentRecipe.Create(
                        "initial",
                        (_, root) =>
                        {
                            root.Scope.OnDispose(() => disposed++);
                            if (fromMount && operation is null)
                                operation = session.Navigate(Location("/items/2"));
                        }
                    )
                );
            }
        );
        composition.Mount(
            composition.Root,
            theme,
            Components.Router([Components.RouterOutlet(options, handle)], bundle, session: session)
        );
        graph.Drain();
        Assert.IsNotNull(operation);
        Assert.IsFalse(operation.IsCompleted);
        Assert.AreEqual(0, handle.Snapshot.Levels.Count);
        Assert.AreEqual(fromMount ? 1 : 0, disposed);
        operation.Cancel();
        graph.Drain();
        Assert.AreEqual(NavigationPhase.Idle, session.Phase);
        Assert.AreEqual(1, session.Current!.Match.GetValue(0).Signed32);
        Assert.AreEqual(1, handle.Snapshot.Levels.Count);
        gate.SetResult(NavigationPreparationResult.Allow);
        graph.Drain();
        Assert.AreEqual(1, session.Current.Match.GetValue(0).Signed32);
    }

    [TestMethod]
    public void ReplacementRetirementEnforcesNavigationReentrancyBoundary()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "replacement-retirement");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var (bundle, _) = CreateAuthoringBundle();
        using var owner = graph.CreateScope("session");
        using var session = new NavigationSession(owner, bundle.Table, Location("/items/1"));
        var choice = graph.Signal(0, "choice");
        NavigationPhase? phase = null;
        var options = new RouteOutletOptions(resolve: _ =>
        {
            var key = choice.Value;
            return new RouteDestination(
                typeof(DefaultPage),
                ComponentRecipe.Create(
                    "page",
                    (_, root) =>
                    {
                        if (key == 0)
                            root.Scope.OnDispose(() =>
                            {
                                phase = session.Phase;
                                session.Navigate(Location("/items/2"));
                            });
                    }
                ),
                key
            );
        });
        composition.Mount(
            composition.Root,
            theme,
            Components.Router([Components.RouterOutlet(options)], bundle, session: session)
        );
        graph.Drain();
        choice.Value = 1;
        var error = Assert.Throws<AggregateException>(() => graph.Drain());
        Assert.AreEqual(NavigationPhase.Retiring, phase);
        Assert.AreEqual(NavigationPhase.Terminal, session.Phase);
        StringAssert.Contains(error.ToString(), nameof(NavigationReentrancyException));
        Assert.AreEqual(1, session.Current!.Match.GetValue(0).Signed32);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void ReplacementDefersToNavigationStartedByResolverOrMount(
        bool navigateFromMount,
        bool veto
    )
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "replacement-navigation-reentrancy");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var candidateDisposed = 0;
        var bundle = CreateNestedAuthoringBundle(static () => { });
        using var sessionOwner = graph.CreateScope("session");
        using var session = new NavigationSession(
            sessionOwner,
            bundle.Table,
            Location("/projects/1/issues/2")
        );
        using var handle = new RouteOutletHandle();
        var alternate = graph.Signal(false, "alternate");
        var gate = new TaskCompletionSource<NavigationPreparationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        NavigationOperation? operation = null;
        var options = new RouteOutletOptions(
            prepare: (_, _, _) => new(gate.Task),
            resolve: request =>
            {
                if (!alternate.Value || request.Level.Id.Value != "project")
                    return request.Default;
                if (!navigateFromMount && operation is null)
                    operation = session.Navigate(Location("/projects/2/issues/3"));
                return new RouteDestination(
                    typeof(AlternatePage),
                    ComponentRecipe.Create(
                        "candidate",
                        (context, root) =>
                        {
                            root.Scope.OnDispose(() => candidateDisposed++);
                            if (navigateFromMount && operation is null)
                                operation = session.Navigate(Location("/projects/2/issues/3"));
                            context.Mount(root, Components.RouterOutlet());
                        }
                    )
                );
            }
        );
        composition.Mount(
            composition.Root,
            theme,
            Components.Router([Components.RouterOutlet(options, handle)], bundle, session: session)
        );
        graph.Drain();
        var original = handle.Snapshot.Levels.Select(level => level.ElementId).ToArray();
        var originalRoots = original
            .Select(id => composition.Find(new ElementIdentity(composition.Epoch, id))!)
            .ToArray();

        alternate.Value = true;
        graph.Drain();

        Assert.IsNotNull(operation);
        Assert.IsFalse(operation.IsCompleted);
        Assert.AreEqual(NavigationPhase.PreparingLeave, session.Phase);
        CollectionAssert.AreEqual(
            original,
            handle.Snapshot.Levels.Select(level => level.ElementId).ToArray()
        );
        Assert.IsTrue(
            originalRoots.All(root => !root.IsDisposed),
            "Pending navigation must retain the committed branch."
        );
        Assert.AreEqual(navigateFromMount ? 1 : 0, candidateDisposed);

        // Returning to Idle after cancellation must evaluate the latest selection.
        if (veto)
        {
            gate.SetResult(NavigationPreparationResult.Stay);
            Assert.AreEqual(
                NavigationOutcomeKind.Stayed,
                DrainUntilCompleted(graph, operation).Kind
            );
        }
        else
            operation.Cancel();
        graph.Drain();
        Assert.AreEqual(NavigationPhase.Idle, session.Phase);
        Assert.AreEqual(1, session.Current!.Match.GetValue(0).Signed32);
        Assert.AreNotEqual(original[0], handle.Snapshot.Levels[0].ElementId);
        Assert.IsTrue(originalRoots.All(root => root.IsDisposed));
        if (!veto)
            gate.SetResult(NavigationPreparationResult.Allow);
        graph.Drain();
        Assert.AreEqual(1, session.Current.Match.GetValue(0).Signed32);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SameUpdateParentReplacementAndChildFailureDisposesEveryCandidate(bool cleanupThrows)
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "nested-candidate-rollback");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var retired = 0;
        var parentDisposed = 0;
        var childDisposed = 0;
        var candidateEffects = 0;
        var bundle = CreateNestedAuthoringBundle(() => retired++);
        using var sessionOwner = graph.CreateScope("session");
        using var session = new NavigationSession(
            sessionOwner,
            bundle.Table,
            Location("/projects/1/issues/2")
        );
        using var handle = new RouteOutletHandle();
        var fail = graph.Signal(false, "replace-and-fail");
        var tick = graph.Signal(0, "candidate-tick");
        var options = new RouteOutletOptions(resolve: request =>
        {
            if (!fail.Value)
                return request.Default;
            return new RouteDestination(
                typeof(AlternatePage),
                ComponentRecipe.Create(
                    "candidate-" + request.Level.Id.Value,
                    (context, root) =>
                    {
                        if (request.Level.Id.Value == "project")
                        {
                            root.Scope.OnDispose(() =>
                            {
                                parentDisposed++;
                                if (cleanupThrows)
                                    throw new InvalidOperationException("parent cleanup failed");
                            });
                            root.Scope.Effect(
                                () =>
                                {
                                    _ = tick.Value;
                                    candidateEffects++;
                                },
                                "candidate-effect"
                            );
                            context.Mount(root, Components.RouterOutlet());
                        }
                        else
                        {
                            root.Scope.OnDispose(() => childDisposed++);
                            throw new InvalidOperationException("child staging failed");
                        }
                    }
                )
            );
        });
        composition.Mount(
            composition.Root,
            theme,
            Components.Router([Components.RouterOutlet(options, handle)], bundle, session: session)
        );
        graph.Drain();
        var original = handle.Snapshot.Levels.Select(level => level.ElementId).ToArray();

        fail.Value = true;
        var error = Assert.ThrowsExactly<AggregateException>(graph.Drain);
        StringAssert.Contains(error.ToString(), "child staging failed");
        if (cleanupThrows)
            StringAssert.Contains(error.ToString(), "parent cleanup failed");
        Assert.AreEqual(1, parentDisposed);
        Assert.AreEqual(1, childDisposed);
        Assert.AreEqual(0, retired);
        CollectionAssert.AreEqual(
            original,
            handle.Snapshot.Levels.Select(level => level.ElementId).ToArray()
        );
        var before = candidateEffects;
        tick.Value++;
        graph.Drain();
        Assert.AreEqual(before, candidateEffects, "Rolled-back subscriptions must not run.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReplacementDoesNotPublishSelectionInvalidatedByMount(bool throughDerived)
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "replacement-selection-freshness");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var (bundle, _) = CreateAuthoringBundle();
        using var owner = graph.CreateScope("session");
        using var session = new NavigationSession(owner, bundle.Table, Location("/items/1"));
        using var handle = new RouteOutletHandle();
        var selected = graph.Signal(0, "selection");
        using var derived = graph.Derived(() => selected.Value, "derived-selection");
        var roots = new Dictionary<int, Element>();
        var options = new RouteOutletOptions(resolve: _ =>
        {
            var choice = throughDerived ? derived.Value : selected.Value;
            return new RouteDestination(
                typeof(DefaultPage),
                ComponentRecipe.Create(
                    "choice-" + choice,
                    (_, root) =>
                    {
                        roots[choice] = root;
                        if (choice == 1)
                            selected.Value = 2;
                        if (choice == 2)
                        {
                            Assert.IsFalse(roots[0].IsDisposed);
                            Assert.AreEqual(
                                roots[0].Id,
                                handle.Snapshot.Levels[0].ElementId,
                                "The stale candidate must never have been published."
                            );
                        }
                    }
                ),
                choice
            );
        });
        composition.Mount(
            composition.Root,
            theme,
            Components.Router([Components.RouterOutlet(options, handle)], bundle, session: session)
        );
        graph.Drain();
        selected.Value = 1;
        graph.Drain();
        Assert.IsTrue(roots[0].IsDisposed);
        Assert.IsTrue(roots[1].IsDisposed);
        Assert.IsFalse(roots[2].IsDisposed);
        Assert.AreEqual(roots[2].Id, handle.Snapshot.Levels[0].ElementId);
    }
}
