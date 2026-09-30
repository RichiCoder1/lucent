using System.Text;
using Lucent.Core;

namespace Lucent.Core.Tests;

public sealed partial class NavigationSessionContracts
{
    private static readonly string[] RestorationFallbackDefinitions = ["item", "home"];
    private static readonly string[] RestorationRedirectDefinitions = ["item", "item", "home"];

    [TestMethod]
    public void RestorationPreparesEnterBeforePublishingOneFreshLocation()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("restore-session");
        var table = Table("home", "item");
        using var session = new NavigationSession(owner, table);
        var gate = new TaskCompletionSource<NavigationPreparationResult>();
        var participant = new RestorationParticipant { Prepare = _ => new(gate.Task) };
        using var registration = session.AttachParticipant(participant);
        var commits = new List<NavigationCommit>();
        using var observer = session.RegisterCommitted(owner, commits.Add);
        var operation = session.Restore(RestorePlan(table));
        Assert.IsNull(session.Current);
        Assert.AreEqual(0, session.Journal.Entries.Count);
        Assert.IsFalse(operation.Completion.IsCompleted);
        Assert.AreEqual(NavigationPreparationPhase.Enter, participant.Requests.Single().Phase);
        Assert.AreEqual(NavigationOrigin.Restoration, participant.Requests.Single().Origin);
        gate.SetResult(NavigationPreparationResult.Allow);
        Assert.AreEqual(
            NavigationOutcomeKind.Committed,
            DrainUntilCompleted(graph, operation).Kind
        );
        Assert.AreEqual("item", session.Current!.DefinitionId.Value);
        Assert.AreEqual(1L, session.Current.EntryId);
        Assert.AreEqual(1, session.Journal.Entries.Count);
        Assert.IsFalse(session.CanGoBack || session.CanGoForward);
        Assert.AreSame(session.Current, commits.Single().Current);
        Assert.AreEqual(NavigationOrigin.Restoration, commits.Single().Origin);
        Assert.AreEqual(
            NavigationFailureKind.InvalidRestorationState,
            Completed(session.Restore(RestorePlan(table))).FailureKind
        );
    }

    [TestMethod]
    [DataRow("stay")]
    [DataRow("fail")]
    [DataRow("reject")]
    public void ExpectedRestorationRejectionPreparesSafeFallbackOnce(string rejection)
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("restore-fallback");
        var table = Table("home", "item");
        using var session = new NavigationSession(owner, table);
        var participant = new RestorationParticipant
        {
            Prepare = request =>
                ValueTask.FromResult(
                    request.Target.DefinitionId.Value == "home" ? NavigationPreparationResult.Allow
                    : rejection == "stay" ? NavigationPreparationResult.Stay
                    : rejection == "fail" ? NavigationPreparationResult.Fail()
                    : NavigationPreparationResult.RejectedActivation()
                ),
        };
        using var registration = session.AttachParticipant(participant);
        var operation = session.Restore(RestorePlan(table));
        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(operation).Kind);
        Assert.AreEqual("home", session.Current!.DefinitionId.Value);
        CollectionAssert.AreEqual(
            RestorationFallbackDefinitions,
            participant.Requests.Select(request => request.Target.DefinitionId.Value).ToArray()
        );
        Assert.IsTrue(
            participant.Requests.All(request =>
                request.OperationId == operation.Id
                && request.Current is null
                && request.Phase == NavigationPreparationPhase.Enter
            )
        );
        Assert.AreEqual(1, participant.Publications.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MissingOrMalformedSnapshotUsesOnlyOneGuardedFallback(bool malformed)
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("restore-invalid");
        var table = Table("home", "item");
        using var session = new NavigationSession(owner, table);
        var participant = new RestorationParticipant
        {
            Prepare = _ => ValueTask.FromResult(NavigationPreparationResult.Stay),
        };
        using var registration = session.AttachParticipant(participant);
        var plan = Restoration(table).Decode(malformed ? "broken"u8 : []);
        Assert.AreEqual(NavigationOutcomeKind.Stayed, Completed(session.Restore(plan)).Kind);
        Assert.AreEqual("home", participant.Requests.Single().Target.DefinitionId.Value);
        Assert.IsNull(session.Current);
        Assert.AreEqual(0, session.Journal.Entries.Count);
        Assert.AreEqual(
            NavigationFailureKind.InvalidRestorationState,
            Completed(session.Restore(plan)).FailureKind
        );
    }

    [TestMethod]
    public void RejectedFallbackDoesNotLoopOrPublishAnIntermediateRoute()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("restore-fallback-stay");
        var table = Table("home", "item");
        using var session = new NavigationSession(owner, table);
        var participant = new RestorationParticipant
        {
            Prepare = _ => ValueTask.FromResult(NavigationPreparationResult.Stay),
        };
        using var registration = session.AttachParticipant(participant);
        Assert.AreEqual(
            NavigationOutcomeKind.Stayed,
            Completed(session.Restore(RestorePlan(table))).Kind
        );
        Assert.AreEqual(2, participant.Requests.Count);
        Assert.AreEqual(0, participant.Publications.Count);
        Assert.IsNull(session.Current);
        Assert.AreEqual(NavigationPhase.Idle, session.Phase);
    }

    [TestMethod]
    [DataRow("activation")]
    [DataRow("cancel")]
    [DataRow("detach")]
    [DataRow("dispose")]
    public void ObsoleteRestorationCannotStartFallbackOrOverwriteNewerIntent(string supersession)
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("restore-obsolete");
        var table = Table("home", "item", "newer");
        using var session = new NavigationSession(owner, table);
        var gate = new TaskCompletionSource<NavigationPreparationResult>();
        var participant = new RestorationParticipant
        {
            Prepare = request =>
                request.Target.DefinitionId.Value == "item"
                    ? new(gate.Task)
                    : ValueTask.FromResult(NavigationPreparationResult.Allow),
        };
        using var registration = session.AttachParticipant(participant);
        var restore = session.Restore(RestorePlan(table));
        if (supersession == "activation")
            Assert.AreEqual(
                NavigationOutcomeKind.Committed,
                Completed(session.Activate("/newer")).Kind
            );
        else if (supersession == "cancel")
            restore.Cancel();
        else if (supersession == "detach")
            registration.Dispose();
        else
            session.Dispose();
        Assert.AreEqual(
            supersession == "dispose"
                ? NavigationOutcomeKind.Disposed
                : NavigationOutcomeKind.Superseded,
            Completed(restore).Kind
        );
        gate.SetResult(NavigationPreparationResult.Stay);
        graph.Drain();
        Assert.IsFalse(
            participant.Requests.Any(request => request.Target.DefinitionId.Value == "home")
        );
        Assert.AreEqual(
            supersession == "activation" ? "newer" : null,
            session.Current?.DefinitionId.Value
        );
    }

    [TestMethod]
    public void RestorationRedirectUsesExistingBoundedPreparationAndPublishesOneTarget()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("restore-redirect");
        var table = Table("home", "item", "redirected");
        using var session = new NavigationSession(owner, table);
        var participant = new RestorationParticipant
        {
            Prepare = request =>
                ValueTask.FromResult(
                    request.Target.DefinitionId.Value == "item"
                        ? NavigationPreparationResult.Redirect(Location("/redirected"))
                        : NavigationPreparationResult.Allow
                ),
        };
        using var registration = session.AttachParticipant(participant);
        Assert.AreEqual(
            NavigationOutcomeKind.Committed,
            Completed(session.Restore(RestorePlan(table))).Kind
        );
        Assert.AreEqual("redirected", session.Current!.DefinitionId.Value);
        Assert.AreEqual(2, participant.Requests.Count);
        Assert.AreEqual(1, session.Journal.Entries.Count);
        Assert.AreEqual(1, participant.Publications.Count);
    }

    [TestMethod]
    [DataRow("stage")]
    [DataRow("publish")]
    [DataRow("retire")]
    public void RestorationTerminalFailureNeverAttemptsFallback(string failure)
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("restore-terminal");
        var table = Table("home", "item");
        using var session = new NavigationSession(owner, table);
        var participant = new RestorationParticipant
        {
            OnStage = () =>
            {
                if (failure == "stage")
                    throw new InvalidOperationException("stage");
            },
            OnPublish = () =>
            {
                if (failure == "publish")
                    throw new InvalidOperationException("publish");
            },
            OnRetire = () =>
            {
                if (failure == "retire")
                    throw new InvalidOperationException("retire");
            },
        };
        using var registration = session.AttachParticipant(participant);
        Assert.AreEqual(
            NavigationFailureKind.Terminal,
            Completed(session.Restore(RestorePlan(table))).FailureKind
        );
        Assert.IsTrue(session.IsTerminated);
        Assert.AreEqual(1, participant.Requests.Count);
    }

    [TestMethod]
    public void RestorationRequiresAttachedEmptyIdleSessionAndExactPolicyTable()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("restore-state");
        var table = Table("home", "item");
        var plan = RestorePlan(table);
        using var session = new NavigationSession(owner, table);
        Assert.AreEqual(
            NavigationFailureKind.InvalidRestorationState,
            Completed(session.Restore(plan)).FailureKind
        );
        using var registration = session.AttachParticipant(new RestorationParticipant());
        Assert.AreEqual(
            NavigationFailureKind.InvalidRestorationState,
            Completed(session.Restore(RestorePlan(Table("home", "item")))).FailureKind
        );
        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(session.Restore(plan)).Kind);
        using var populated = new NavigationSession(owner, table, Location("/home"));
        using var populatedRoot = populated.AttachParticipant(new RestorationParticipant());
        Assert.AreEqual(
            NavigationFailureKind.InvalidRestorationState,
            Completed(populated.Restore(plan)).FailureKind
        );
        Assert.AreEqual("home", populated.Current!.DefinitionId.Value);
        using var busy = new NavigationSession(owner, table);
        var gate = new TaskCompletionSource<NavigationPreparationResult>();
        using var busyRoot = busy.AttachParticipant(
            new RestorationParticipant { Prepare = _ => new(gate.Task) }
        );
        var pending = busy.Activate("/item");
        Assert.AreEqual(
            NavigationFailureKind.InvalidRestorationState,
            Completed(busy.Restore(plan)).FailureKind
        );
        Assert.IsFalse(pending.Completion.IsCompleted);
        gate.SetResult(NavigationPreparationResult.Allow);
        Assert.AreEqual(NavigationOutcomeKind.Committed, DrainUntilCompleted(graph, pending).Kind);
    }

    [TestMethod]
    public void CaptureReadsCommittedRouteDuringPreparationButRejectsPublicationAndReplacement()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("restore-capture");
        var table = Table("home", "item");
        using var session = new NavigationSession(owner, table, Location("/home"));
        var codec = Restoration(table);
        var gate = new TaskCompletionSource<NavigationPreparationResult>();
        var publicationStates = new List<NavigationRestorationStatus>();
        var participant = new RestorationParticipant
        {
            Prepare = _ => new(gate.Task),
            OnStage = () => publicationStates.Add(codec.Capture(session).Status),
            OnPublish = () => publicationStates.Add(codec.Capture(session).Status),
            OnRetire = () => publicationStates.Add(codec.Capture(session).Status),
        };
        using var registration = session.AttachParticipant(participant);
        using (var replacement = session.TryReserveReplacement(participant, session.Current!))
            Assert.AreEqual(
                NavigationRestorationStatus.InvalidState,
                codec.Capture(session).Status
            );
        var navigation = session.Navigate(Location("/item"));
        var captured = codec.Capture(session);
        Assert.AreEqual(NavigationRestorationStatus.Ready, captured.Status);
        Assert.AreEqual("/home", codec.Decode(captured.Utf8.Span).Target!.Location.CanonicalText);
        Assert.IsFalse(navigation.Completion.IsCompleted);
        gate.SetResult(NavigationPreparationResult.Allow);
        Assert.AreEqual(
            NavigationOutcomeKind.Committed,
            DrainUntilCompleted(graph, navigation).Kind
        );
        CollectionAssert.AreEqual(
            Enumerable.Repeat(NavigationRestorationStatus.InvalidState, 3).ToArray(),
            publicationStates
        );
        Assert.AreEqual(
            "/item",
            codec.Decode(codec.Capture(session).Utf8.Span).Target!.Location.CanonicalText
        );
        Assert.AreEqual(
            NavigationRestorationStatus.InvalidState,
            Restoration(Table("home", "item")).Capture(session).Status
        );
        session.Dispose();
        Assert.AreEqual(NavigationRestorationStatus.InvalidState, codec.Capture(session).Status);
    }

    [TestMethod]
    public void RestoreRechecksPolicyAndDoesNotOverwriteNavigationStartedByIt()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("restore-policy");
        var table = Table("home", "item", "newer");
        using var session = new NavigationSession(owner, table);
        using var root = session.AttachParticipant(new RestorationParticipant());
        var admitted = true;
        var codec = Restoration(table, _ => admitted);
        var plan = codec.Decode(RestoreBytes());
        admitted = false;
        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(session.Restore(plan)).Kind);
        Assert.AreEqual("home", session.Current!.DefinitionId.Value);
        using var reentrant = new NavigationSession(owner, table);
        using var reentrantRoot = reentrant.AttachParticipant(new RestorationParticipant());
        var navigateFromPolicy = false;
        var reentrantCodec = Restoration(
            table,
            _ =>
            {
                if (navigateFromPolicy)
                    reentrant.Activate("/newer");
                return true;
            }
        );
        var reentrantPlan = reentrantCodec.Decode(RestoreBytes());
        navigateFromPolicy = true;
        Assert.AreEqual(
            NavigationOutcomeKind.Superseded,
            Completed(reentrant.Restore(reentrantPlan)).Kind
        );
        Assert.AreEqual("newer", reentrant.Current!.DefinitionId.Value);
    }

    [TestMethod]
    public void EvenRejectedReentrantActivationTakesPrecedenceOverRestorationAdmission()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("restore-policy-rejected-activation");
        var table = Table("home", "item");
        using var session = new NavigationSession(owner, table);
        using var root = session.AttachParticipant(new RestorationParticipant());
        var activate = false;
        var codec = Restoration(
            table,
            _ =>
            {
                if (activate)
                    session.Activate("/missing");
                return true;
            }
        );
        var plan = codec.Decode(RestoreBytes());
        activate = true;
        Assert.AreEqual(NavigationOutcomeKind.Superseded, Completed(session.Restore(plan)).Kind);
        Assert.IsNull(session.Current);
    }

    [TestMethod]
    public void FallbackDoesNotResetTheOperationsRedirectBudget()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("restore-redirect-bound");
        var table = Table("home", "item", "newer");
        using var session = new NavigationSession(owner, table, maximumRedirects: 1);
        var participant = new RestorationParticipant
        {
            Prepare = request =>
                ValueTask.FromResult(
                    NavigationPreparationResult.Redirect(
                        Location(request.Target.DefinitionId.Value == "item" ? "/item" : "/newer")
                    )
                ),
        };
        using var root = session.AttachParticipant(participant);
        Assert.AreEqual(
            NavigationFailureKind.PreparationRejected,
            Completed(session.Restore(RestorePlan(table))).FailureKind
        );
        CollectionAssert.AreEqual(
            RestorationRedirectDefinitions,
            participant.Requests.Select(request => request.Target.DefinitionId.Value).ToArray()
        );
        Assert.IsNull(session.Current);
        Assert.AreEqual(0, participant.Publications.Count);
    }

    private static NavigationRestorePlan RestorePlan(RouteTable table) =>
        Restoration(table).Decode(RestoreBytes());

    private static byte[] RestoreBytes() =>
        Encoding.UTF8.GetBytes(
            """{"schema":"lucent.navigation","version":1,"scope":"session-v1","mode":"location","active":{"definition":"item","location":"/item"}}"""
        );

    private static NavigationRestoration Restoration(
        RouteTable table,
        Func<RouteMatch, bool>? policy = null
    ) =>
        new(
            table,
            "session-v1",
            RouteReference.Create(table.Patterns.Single(pattern => pattern.Id.Value == "home"), []),
            policy ?? (_ => true)
        );

    private sealed class RestorationParticipant : INavigationTransactionParticipant
    {
        internal Func<
            NavigationPrepareRequest,
            ValueTask<NavigationPreparationResult>
        > Prepare { get; init; } = _ => ValueTask.FromResult(NavigationPreparationResult.Allow);
        internal Action? OnStage { get; init; }
        internal Action? OnPublish { get; init; }
        internal Action? OnRetire { get; init; }
        internal List<NavigationPrepareRequest> Requests { get; } = [];
        internal List<NavigationPublication> Publications { get; } = [];
        internal NavigationJournalSnapshot? CapturedJournal { get; private set; }
        internal IReadOnlyDictionary<
            long,
            NavigationEntryInteractionState
        >? CaptureStates { get; init; }

        public bool TryCaptureRestorationStates(
            NavigationJournalSnapshot journal,
            out IReadOnlyDictionary<long, NavigationEntryInteractionState>? states
        )
        {
            CapturedJournal = journal;
            states = CaptureStates;
            return true;
        }

        public ValueTask<NavigationPreparationResult> PrepareAsync(
            NavigationPrepareRequest request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return Prepare(request);
        }

        public NavigationStage Stage(NavigationStageRequest request)
        {
            OnStage?.Invoke();
            return new TestStage();
        }

        public void Publish(NavigationStage stage, NavigationPublication publication)
        {
            OnPublish?.Invoke();
            Publications.Add(publication);
        }

        public void Retire(NavigationRetirement retirement) => OnRetire?.Invoke();
    }
}
