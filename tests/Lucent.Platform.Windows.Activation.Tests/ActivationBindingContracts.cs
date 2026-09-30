using Lucent.Core;

namespace Lucent.Platform.Windows.Activation.Tests;

[TestClass]
public sealed class ActivationBindingContracts
{
    private static readonly WindowsActivationOptions Options = new(
        "Lucent.Activation.Tests",
        "lucent-test",
        "navigation"
    );

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReentrantColdPolicyCannotReplaceNewerNavigationWithFallback(bool allow)
    {
        using var inbox = new ActivationInbox();
        inbox.Offer(Protocol("/first", ActivationDelivery.Cold));
        NavigationSession? navigation = null;
        var observed = new List<ActivationBindingResult>();
        RunApplication(
            inbox,
            () => navigation!,
            value => navigation = value,
            new(
                _ =>
                {
                    Assert.AreEqual(
                        NavigationOutcomeKind.Committed,
                        navigation!.Activate("/newer").Completion.Result.Kind
                    );
                    return allow;
                },
                _ => false
            ),
            () => ValueTask.FromResult(ReadOnlyMemory<byte>.Empty),
            observed.Add,
            session =>
            {
                Pump(session, () => session.Status.Phase == ApplicationPhase.Running);
                Assert.AreEqual("newer", navigation!.Current?.DefinitionId.Value);
                if (allow)
                    Assert.IsEmpty(observed);
                else
                    Assert.AreEqual(
                        ActivationBindingStatus.PolicyRejected,
                        observed.Single().Status
                    );
            }
        );
    }

    [TestMethod]
    public void WarmArrivalDuringStorageReadSupersedesStartupRestore()
    {
        using var inbox = new ActivationInbox();
        inbox.Offer(WindowsActivationEnvelope.Launch(ActivationDelivery.Cold));
        NavigationSession? navigation = null;
        var readStarted = false;
        var storage = new TaskCompletionSource<ReadOnlyMemory<byte>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        RunApplication(
            inbox,
            () => navigation!,
            value => navigation = value,
            new(_ => true, _ => false),
            () =>
            {
                readStarted = true;
                return new(storage.Task);
            },
            null,
            session =>
            {
                Pump(session, () => readStarted);
                inbox.Offer(Protocol("/newer", ActivationDelivery.Redirected));
                storage.SetResult(ReadOnlyMemory<byte>.Empty);
                Pump(session, () => session.Status.Phase == ApplicationPhase.Running);
                Pump(session, () => navigation!.Current?.DefinitionId.Value == "newer");
                Assert.AreEqual(1, navigation!.Journal.Entries.Count);
            }
        );
    }

    [TestMethod]
    [DataRow("/first", true)]
    [DataRow("/missing", false)]
    public void RejectedWarmProtocolDuringStorageReadStillRestoresStartup(
        string route,
        bool rejectByPolicy
    )
    {
        using var inbox = new ActivationInbox();
        inbox.Offer(WindowsActivationEnvelope.Launch(ActivationDelivery.Cold));
        NavigationSession? navigation = null;
        var storage = new TaskCompletionSource<ReadOnlyMemory<byte>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var readStarted = false;
        var observed = new List<ActivationBindingResult>();
        RunApplication(
            inbox,
            () => navigation!,
            value => navigation = value,
            new(_ => !rejectByPolicy, _ => false),
            () =>
            {
                readStarted = true;
                return new(storage.Task);
            },
            observed.Add,
            session =>
            {
                Pump(session, () => readStarted);
                inbox.Offer(Protocol(route, ActivationDelivery.Redirected));
                Pump(session, () => observed.Count == 1);
                storage.SetResult(ReadOnlyMemory<byte>.Empty);
                Pump(session, () => navigation!.Current?.DefinitionId.Value == "home");
                Assert.AreEqual(1, navigation!.Journal.Entries.Count);
                Assert.AreEqual(
                    rejectByPolicy
                        ? ActivationBindingStatus.PolicyRejected
                        : ActivationBindingStatus.NavigationStayed,
                    observed.Single().Status
                );
            }
        );
    }

    [TestMethod]
    public void PendingWarmGuardRejectionResumesStartupRestore()
    {
        using var inbox = new ActivationInbox();
        inbox.Offer(WindowsActivationEnvelope.Launch(ActivationDelivery.Cold));
        NavigationSession? navigation = null;
        var storage = new TaskCompletionSource<ReadOnlyMemory<byte>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var guard = new TaskCompletionSource<NavigationPreparationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var readStarted = false;
        var observed = new List<ActivationBindingResult>();
        RunApplication(
            inbox,
            () => navigation!,
            value => navigation = value,
            new(_ => true, _ => false),
            () =>
            {
                readStarted = true;
                return new(storage.Task);
            },
            observed.Add,
            session =>
            {
                Pump(session, () => readStarted);
                inbox.Offer(Protocol("/first", ActivationDelivery.Redirected));
                Pump(session, () => navigation!.Phase == NavigationPhase.PreparingEnter);
                storage.SetResult(ReadOnlyMemory<byte>.Empty);
                session.ProcessEvents();
                Assert.IsNull(navigation!.Current);
                guard.SetResult(NavigationPreparationResult.Stay);
                Pump(session, () => navigation.Current?.DefinitionId.Value == "home");
                Assert.AreEqual(1, navigation.Journal.Entries.Count);
                Assert.AreEqual(ActivationBindingStatus.NavigationStayed, observed.Single().Status);
            },
            prepare: (_, request, _) =>
                request.Target.DefinitionId.Value == "first"
                    ? new(guard.Task)
                    : ValueTask.FromResult(NavigationPreparationResult.Allow)
        );
    }

    [TestMethod]
    public void RejectedWarmRouteAfterSupersedingInitialRestoreGetsSafeFallback()
    {
        using var inbox = new ActivationInbox();
        inbox.Offer(WindowsActivationEnvelope.Launch(ActivationDelivery.Cold));
        NavigationSession? navigation = null;
        var firstEnter = new TaskCompletionSource<NavigationPreparationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var homePrepares = 0;
        RunApplication(
            inbox,
            () => navigation!,
            value => navigation = value,
            new(_ => true, _ => false),
            () => ValueTask.FromResult(ReadOnlyMemory<byte>.Empty),
            null,
            session =>
            {
                Pump(session, () => navigation!.Phase == NavigationPhase.PreparingEnter);
                inbox.Offer(Protocol("/missing", ActivationDelivery.Redirected));
                Pump(session, () => navigation!.Current?.DefinitionId.Value == "home");
                Assert.AreEqual(2, homePrepares);
                Assert.AreEqual(1, navigation!.Journal.Entries.Count);
            },
            prepare: (_, request, _) =>
                request.Target.DefinitionId.Value == "home" && ++homePrepares == 1
                    ? new(firstEnter.Task)
                    : ValueTask.FromResult(NavigationPreparationResult.Allow)
        );
    }

    [TestMethod]
    public void RejectedWarmRouteAfterSupersedingColdProtocolUsesSafeFallback()
    {
        using var inbox = new ActivationInbox();
        inbox.Offer(Protocol("/first", ActivationDelivery.Cold));
        NavigationSession? navigation = null;
        var firstEnter = new TaskCompletionSource<NavigationPreparationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        RunApplication(
            inbox,
            () => navigation!,
            value => navigation = value,
            new(_ => true, _ => false),
            () => ValueTask.FromResult(ReadOnlyMemory<byte>.Empty),
            null,
            session =>
            {
                Pump(session, () => navigation!.Pending?.Target.DefinitionId.Value == "first");
                inbox.Offer(Protocol("/missing", ActivationDelivery.Redirected));
                Pump(session, () => navigation!.Current?.DefinitionId.Value == "home");
                Assert.AreEqual(1, navigation!.Journal.Entries.Count);
            },
            prepare: (_, request, _) =>
                request.Target.DefinitionId.Value == "first"
                    ? new(firstEnter.Task)
                    : ValueTask.FromResult(NavigationPreparationResult.Allow)
        );
    }

    [TestMethod]
    public void NewerRejectedRouteQueuedDuringColdPolicyDoesNotResurrectColdUri()
    {
        using var inbox = new ActivationInbox();
        inbox.Offer(Protocol("/first", ActivationDelivery.Cold));
        NavigationSession? navigation = null;
        RunApplication(
            inbox,
            () => navigation!,
            value => navigation = value,
            new(
                envelope =>
                {
                    if (envelope.Delivery == ActivationDelivery.Cold)
                        inbox.Offer(Protocol("/missing", ActivationDelivery.Redirected));
                    return true;
                },
                _ => false
            ),
            () => ValueTask.FromResult(ReadOnlyMemory<byte>.Empty),
            null,
            session =>
            {
                Pump(session, () => navigation!.Current?.DefinitionId.Value == "home");
                Assert.AreEqual(1, navigation!.Journal.Entries.Count);
            }
        );
    }

    [TestMethod]
    public void DeclinedCloseDuringHeldSnapshotReadResumesStartupRestore()
    {
        using var inbox = new ActivationInbox();
        inbox.Offer(WindowsActivationEnvelope.Launch(ActivationDelivery.Cold));
        NavigationSession? navigation = null;
        var storage = new TaskCompletionSource<ReadOnlyMemory<byte>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var closeDecision = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var readStarted = false;
        RunApplication(
            inbox,
            () => navigation!,
            value => navigation = value,
            new(_ => true, _ => false),
            () =>
            {
                readStarted = true;
                return new(storage.Task);
            },
            null,
            session =>
            {
                Pump(session, () => readStarted);
                session.RequestClose();
                storage.SetResult(ReadOnlyMemory<byte>.Empty);
                Pump(session, () => session.Status.Phase == ApplicationPhase.PreparingClose);
                closeDecision.SetResult(false);
                Pump(
                    session,
                    () =>
                        session.Status.Phase == ApplicationPhase.Running
                        && !session.IsCloseRequested
                );
                Pump(session, () => navigation!.Current?.DefinitionId.Value == "home");
            },
            firstCloseDecision: closeDecision
        );
    }

    [TestMethod]
    public void ReentrantPolicyCannotReplaceNewerPendingEnterWithOlderExternalRoute()
    {
        using var inbox = new ActivationInbox();
        inbox.Offer(WindowsActivationEnvelope.Launch(ActivationDelivery.Cold));
        NavigationSession? navigation = null;
        var firstEnter = new TaskCompletionSource<NavigationPreparationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var newerEnter = new TaskCompletionSource<NavigationPreparationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        RunApplication(
            inbox,
            () => navigation!,
            value => navigation = value,
            new(
                envelope =>
                {
                    if (envelope.EscapedPathAndQuery == "/first")
                        navigation!.Activate("/newer");
                    return true;
                },
                _ => false
            ),
            () => ValueTask.FromResult(ReadOnlyMemory<byte>.Empty),
            null,
            session =>
            {
                Pump(session, () => navigation!.Phase == NavigationPhase.PreparingEnter);
                inbox.Offer(Protocol("/first", ActivationDelivery.Redirected));
                Pump(session, () => navigation!.Pending?.Target.DefinitionId.Value == "newer");
                newerEnter.SetResult(NavigationPreparationResult.Allow);
                Pump(session, () => navigation!.Current?.DefinitionId.Value == "newer");
                Assert.AreEqual(1, navigation!.Journal.Entries.Count);
            },
            prepare: (_, request, _) =>
                request.Target.DefinitionId.Value switch
                {
                    "home" => new(firstEnter.Task),
                    "newer" => new(newerEnter.Task),
                    _ => ValueTask.FromResult(NavigationPreparationResult.Allow),
                }
        );
    }

    [TestMethod]
    public void ReentrantAttentionPolicyCannotFocusAfterPendingRouteReplacement()
    {
        using var inbox = new ActivationInbox();
        inbox.Offer(WindowsActivationEnvelope.Launch(ActivationDelivery.Cold));
        NavigationSession? navigation = null;
        var firstEnter = new TaskCompletionSource<NavigationPreparationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var newerEnter = new TaskCompletionSource<NavigationPreparationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var attentionCalls = 0;
        RunApplication(
            inbox,
            () => navigation!,
            value => navigation = value,
            new(
                _ => true,
                envelope =>
                {
                    if (envelope.Kind == ActivationKind.Launch)
                        navigation!.Activate("/newer");
                    return true;
                }
            ),
            () => ValueTask.FromResult(ReadOnlyMemory<byte>.Empty),
            null,
            session =>
            {
                Pump(session, () => navigation!.Current?.DefinitionId.Value == "home");
                navigation!.Activate("/first");
                Assert.AreEqual("first", navigation.Pending?.Target.DefinitionId.Value);
                inbox.Offer(WindowsActivationEnvelope.Launch(ActivationDelivery.Redirected));
                Pump(session, () => navigation.Pending?.Target.DefinitionId.Value == "newer");
                newerEnter.SetResult(NavigationPreparationResult.Allow);
                Pump(session, () => navigation.Current?.DefinitionId.Value == "newer");
                Assert.AreEqual(0, attentionCalls);
            },
            () =>
            {
                attentionCalls++;
                return new(true, true, false);
            },
            (_, request, _) =>
                request.Target.DefinitionId.Value switch
                {
                    "first" => new(firstEnter.Task),
                    "newer" => new(newerEnter.Task),
                    _ => ValueTask.FromResult(NavigationPreparationResult.Allow),
                }
        );
    }

    [TestMethod]
    public void PlainLaunchDuringStorageReadAllowsStartupFallback()
    {
        using var inbox = new ActivationInbox();
        inbox.Offer(WindowsActivationEnvelope.Launch(ActivationDelivery.Cold));
        NavigationSession? navigation = null;
        var readStarted = false;
        var attention = 0;
        var storage = new TaskCompletionSource<ReadOnlyMemory<byte>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        RunApplication(
            inbox,
            () => navigation!,
            value => navigation = value,
            new(_ => true, _ => true),
            () =>
            {
                readStarted = true;
                return new(storage.Task);
            },
            null,
            session =>
            {
                Pump(session, () => readStarted);
                inbox.Offer(WindowsActivationEnvelope.Launch(ActivationDelivery.Redirected));
                storage.SetResult(ReadOnlyMemory<byte>.Empty);
                Pump(session, () => session.Status.Phase == ApplicationPhase.Running);
                Assert.AreEqual("home", navigation!.Current?.DefinitionId.Value);
                Assert.AreEqual(1, navigation.Journal.Entries.Count);
                Assert.AreEqual(1, attention);
            },
            () =>
            {
                attention++;
                return new(true, true, false);
            }
        );
    }

    [TestMethod]
    public void PlainLaunchDuringStartupEnterAllowsFallbackToCommit()
    {
        using var inbox = new ActivationInbox();
        inbox.Offer(WindowsActivationEnvelope.Launch(ActivationDelivery.Cold));
        NavigationSession? navigation = null;
        var attention = 0;
        var gate = new TaskCompletionSource<NavigationPreparationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        RunApplication(
            inbox,
            () => navigation!,
            value => navigation = value,
            new(_ => true, _ => true),
            () => ValueTask.FromResult(ReadOnlyMemory<byte>.Empty),
            null,
            session =>
            {
                Pump(session, () => navigation!.Phase == NavigationPhase.PreparingEnter);
                Assert.IsNull(navigation!.Current);
                inbox.Offer(WindowsActivationEnvelope.Launch(ActivationDelivery.Redirected));
                gate.SetResult(NavigationPreparationResult.Allow);
                Pump(session, () => navigation!.Current?.DefinitionId.Value == "home");
                Assert.AreEqual(1, navigation.Journal.Entries.Count);
                Assert.AreEqual(1, attention);
            },
            () =>
            {
                attention++;
                return new(true, true, false);
            },
            (_, _, _) => new(gate.Task)
        );
    }

    [TestMethod]
    public void CoalescedLaunchAndInvalidDeliveryDoNotDisplaceColdProtocol()
    {
        using var inbox = new ActivationInbox();
        inbox.Offer(Protocol("/first", ActivationDelivery.Cold));
        inbox.Offer(WindowsActivationEnvelope.Launch(ActivationDelivery.Redirected));
        NavigationSession? navigation = null;
        var observed = new List<ActivationBindingResult>();
        RunApplication(
            inbox,
            () => navigation!,
            value => navigation = value,
            new(
                envelope =>
                {
                    if (envelope.Kind == ActivationKind.Protocol)
                        inbox.Offer(
                            WindowsActivationEnvelope.Rejected(
                                ActivationDelivery.Redirected,
                                ActivationRejection.InvalidProtocol
                            )
                        );
                    return true;
                },
                _ => false
            ),
            () => ValueTask.FromResult(ReadOnlyMemory<byte>.Empty),
            observed.Add,
            session =>
            {
                Pump(session, () => session.Status.Phase == ApplicationPhase.Running);
                Assert.AreEqual("first", navigation!.Current?.DefinitionId.Value);
                Assert.AreEqual(1, navigation.Journal.Entries.Count);
                Assert.IsTrue(
                    observed.Any(result => result.Status == ActivationBindingStatus.Committed)
                );
                Assert.IsTrue(
                    observed.Any(result => result.Status == ActivationBindingStatus.Rejected)
                );
            }
        );
    }

    [TestMethod]
    public void InvalidWarmDeliveryRetainsCommittedRouteAndSkipsAttention()
    {
        using var inbox = new ActivationInbox();
        inbox.Offer(WindowsActivationEnvelope.Launch(ActivationDelivery.Cold));
        NavigationSession? navigation = null;
        var attention = 0;
        var observed = new List<ActivationBindingResult>();
        RunApplication(
            inbox,
            () => navigation!,
            value => navigation = value,
            new(_ => true, _ => true),
            () => ValueTask.FromResult(ReadOnlyMemory<byte>.Empty),
            observed.Add,
            session =>
            {
                Pump(session, () => session.Status.Phase == ApplicationPhase.Running);
                Assert.AreEqual("home", navigation!.Current?.DefinitionId.Value);
                inbox.Offer(
                    WindowsActivationEnvelope.Rejected(
                        ActivationDelivery.Redirected,
                        ActivationRejection.InvalidProtocol
                    )
                );
                Pump(session, () => observed.Count == 1);
                Assert.AreEqual(ActivationBindingStatus.Rejected, observed.Single().Status);
                Assert.AreEqual("home", navigation.Current?.DefinitionId.Value);
                Assert.AreEqual(1, navigation.Journal.Entries.Count);
                Assert.AreEqual(0, attention);
            },
            () =>
            {
                attention++;
                return new(true, true, false);
            }
        );
    }

    [TestMethod]
    public void ReentrantAttentionPolicyCannotCallNativeWindowAfterClosing()
    {
        using var inbox = new ActivationInbox();
        inbox.Offer(WindowsActivationEnvelope.Launch(ActivationDelivery.Cold));
        NavigationSession? navigation = null;
        var attentionCalls = 0;
        var observed = new List<ActivationBindingResult>();
        RunApplication(
            inbox,
            () => navigation!,
            value => navigation = value,
            new(
                _ => true,
                _ =>
                {
                    inbox.BeginClose();
                    return true;
                }
            ),
            () => ValueTask.FromResult(ReadOnlyMemory<byte>.Empty),
            observed.Add,
            session =>
            {
                Pump(session, () => session.Status.Phase == ApplicationPhase.Running);
                inbox.Offer(Protocol("/newer", ActivationDelivery.Redirected));
                Pump(session, () => inbox.IsClosing);
                Assert.AreEqual("newer", navigation!.Current?.DefinitionId.Value);
                Assert.AreEqual(ActivationBindingStatus.Committed, observed.Single().Status);
                Assert.IsNull(observed.Single().Attention);
                Assert.AreEqual(0, attentionCalls);
            },
            () =>
            {
                attentionCalls++;
                return new(true, true, false);
            }
        );
    }

    private static ActivationEnvelope Protocol(string route, ActivationDelivery delivery)
    {
        Assert.IsTrue(
            WindowsActivationEnvelope.TryProtocol(
                "lucent-test://navigation" + route,
                Options,
                delivery,
                out var envelope
            )
        );
        return envelope!;
    }

    private static void RunApplication(
        ActivationInbox inbox,
        Func<NavigationSession> navigation,
        Action<NavigationSession> setNavigation,
        WindowsActivationPolicy policy,
        Func<ValueTask<ReadOnlyMemory<byte>>> readSnapshot,
        Action<ActivationBindingResult>? observe,
        Action<ApplicationSession> inspect,
        Func<Lucent.Platform.Windows.WindowsAttentionResult>? attention = null,
        RouteOutletPreparationHandler? prepare = null,
        TaskCompletionSource<bool>? firstCloseDecision = null
    )
    {
        var bundle = Bundle();
        var fallback = RouteReference.Create(
            bundle.Table.Patterns.Single(pattern => pattern.Id.Value == "home"),
            []
        );
        var restoration = new NavigationRestoration(
            bundle.Table,
            "activation-tests",
            fallback,
            _ => true
        );
        var builder = LucentApplication
            .CreateBuilder()
            .UseHost(new ConsoleHost(inspect))
            .UseWindowsActivation(
                inbox,
                navigation,
                restoration,
                readSnapshot,
                policy,
                attention,
                observe
            )
            .ConfigureRoot(
                (session, _) =>
                {
                    var mountedNavigation = new NavigationSession(session.Scope, bundle.Table);
                    setNavigation(mountedNavigation);
                    return Components.Router(
                        [Components.RouterOutlet(new RouteOutletOptions(prepare))],
                        bundle,
                        session: mountedNavigation
                    );
                }
            );
        if (firstCloseDecision is not null)
        {
            var closeAttempts = 0;
            builder.OnPrepareClose(
                (_, _) =>
                    ++closeAttempts == 1 ? new(firstCloseDecision.Task) : ValueTask.FromResult(true)
            );
        }
        var app = builder.Build(ComponentRecipe.Create("root", static (_, _) => { }));
        Assert.AreEqual(0, app.Run());
    }

    private static RouteBundle Bundle()
    {
        var source = new RouteDeclarationSource("activation-tests", 1, 1);
        var definitions = new List<RouteDefinitionDescriptor>();
        foreach (var name in new[] { "home", "first", "newer" })
        {
            var pattern = RoutePattern.Create(
                new(name),
                [RouteSegmentPattern.LiteralSegment(name)]
            );
            var level = new RouteLevelDescriptor(
                pattern.Id,
                [],
                source,
                static (definition, _, live) => new RouteContext<string>(definition, "", live),
                static (context, content) => Context.Provide((RouteContext<string>)context, content)
            );
            definitions.Add(new(pattern, [level]));
        }
        return RouteBundle.Create(
            [
                new RouteModuleDescriptor(
                    "activation-tests",
                    RouteFallbackPolicy.Reject,
                    source,
                    definitions
                ),
            ],
            level => new RouteDestination(
                typeof(ActivationBindingContracts),
                ComponentRecipe.Create("page", static (_, _) => { }),
                level.Id
            )
        );
    }

    private static void Pump(ApplicationSession session, Func<bool> done)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!done())
        {
            session.ProcessEvents();
            if (!session.Composition.IsDisposed)
                session.Composition.Flush();
            if (Environment.TickCount64 >= deadline)
                throw new TimeoutException("Activation model did not settle.");
            Thread.Sleep(1);
        }
    }

    private sealed class ConsoleHost(Action<ApplicationSession> inspect) : IApplicationHost
    {
        public int Run(ApplicationSession session)
        {
            session.Start();
            inspect(session);
            session.RequestClose();
            Pump(session, () => session.IsCompleted);
            if (session.Status.Error is { } error)
                throw new InvalidOperationException("Activation app failed.", error);
            return 0;
        }
    }
}
