using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class NavigationSessionContracts
{
    [TestMethod]
    public void SessionStartsWithoutCommittedRouteAndCommitsPushesWithDistinctEntryIds()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("navigation-test");
        using var session = new NavigationSession(owner, Table("home", "details"));

        Assert.IsNull(session.Current);
        var first = session.Navigate(Location("/home"));
        var second = session.Navigate(Location("/home"));

        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(first).Kind);
        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(second).Kind);
        Assert.AreEqual("home", session.Current!.DefinitionId.Value);
        Assert.AreEqual(2, session.Journal.Entries.Count);
        Assert.AreEqual(
            session.Journal.Entries[0].Location.CanonicalText,
            session.Journal.Entries[1].Location.CanonicalText
        );
        Assert.AreNotEqual(session.Journal.Entries[0].EntryId, session.Journal.Entries[1].EntryId);
    }

    [TestMethod]
    public void TypedReferenceMustMatchTheSessionTablePatternByReference()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("navigation-test");
        var table = Table("home");
        using var session = new NavigationSession(owner, table);

        var valid = RouteReference.Create(table.Patterns[0], []);
        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(session.Navigate(valid)).Kind);

        var equalButForeign = RoutePattern.Create(
            new RouteDefinitionId("home"),
            [RouteSegmentPattern.LiteralSegment("home")]
        );
        var rejected = RouteReference.Create(equalButForeign, []);
        var outcome = Completed(session.Navigate(rejected));

        Assert.AreEqual(NavigationOutcomeKind.RejectedActivation, outcome.Kind);
        Assert.AreEqual(NavigationFailureKind.InvalidActivation, outcome.FailureKind);
        Assert.AreEqual(valid.Location.CanonicalText, session.Current!.Location.CanonicalText);
    }

    [TestMethod]
    public void ReplaceRetainsIndexAndTraversalUsesTheSamePreparationPath()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("navigation-test");
        using var session = new NavigationSession(owner, Table("home", "one", "two"));

        Completed(session.Navigate(Location("/home")));
        Completed(session.Navigate(Location("/one")));
        var replaced = session.Navigate(Location("/two"), NavigationHistoryAction.Replace);

        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(replaced).Kind);
        Assert.AreEqual(1, session.Journal.CurrentIndex);
        Assert.AreEqual(2, session.Journal.Entries.Count);
        Assert.AreEqual("two", session.Current!.DefinitionId.Value);

        var back = session.Back();
        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(back).Kind);
        Assert.AreEqual("home", session.Current!.DefinitionId.Value);
        Assert.IsFalse(session.CanGoBack);
        Assert.IsTrue(session.CanGoForward);
        var blocked = session.Back();
        var blockedOutcome = Completed(blocked);
        Assert.AreEqual(NavigationOutcomeKind.Stayed, blockedOutcome.Kind);
        Assert.AreEqual(NavigationFailureKind.HistoryBoundary, blockedOutcome.FailureKind);
        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(session.Forward()).Kind);
        Assert.AreEqual("two", session.Current!.DefinitionId.Value);
    }

    [TestMethod]
    public void PushAfterBackTruncatesForwardEntriesAndBoundedRetentionEvictsOldest()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("navigation-test");
        using var session = new NavigationSession(
            owner,
            Table("a", "b", "c", "d"),
            maximumEntries: 2
        );

        Completed(session.Navigate(Location("/a")));
        Completed(session.Navigate(Location("/b")));
        Completed(session.Navigate(Location("/c")));
        Assert.AreEqual("b", session.Journal.Entries[0].DefinitionId.Value);
        Assert.AreEqual("c", session.Journal.Entries[1].DefinitionId.Value);

        Completed(session.Back());
        Completed(session.Navigate(Location("/d")));

        Assert.AreEqual(2, session.Journal.Entries.Count);
        Assert.AreEqual("b", session.Journal.Entries[0].DefinitionId.Value);
        Assert.AreEqual("d", session.Journal.Entries[1].DefinitionId.Value);
        Assert.IsFalse(session.CanGoForward);
    }

    [TestMethod]
    public void ParticipantReceivesLeafThenRootPreparationAndPublicationSeesOldCurrent()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("navigation-test");
        using var session = new NavigationSession(owner, Table("home", "details"));
        Completed(session.Navigate(Location("/home")));
        var participant = new RecordingParticipant();
        using var registration = session.AttachParticipant(participant);

        var operation = session.Navigate(Location("/details"));

        CollectionAssert.AreEqual(
            new[] { NavigationPreparationPhase.Leave, NavigationPreparationPhase.Enter },
            participant.Phases
        );
        Assert.AreEqual("home", participant.CurrentDuringPublish!.DefinitionId.Value);
        Assert.AreEqual("details", participant.LastPublication!.Current.DefinitionId.Value);
        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(operation).Kind);
        Assert.AreEqual(NavigationPhase.Idle, session.Phase);
    }

    [TestMethod]
    public void ExpectedPreparationFailureLeavesCurrentAndJournalUntouched()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("navigation-test");
        using var session = new NavigationSession(owner, Table("home", "details"));
        Completed(session.Navigate(Location("/home")));
        var participant = new RecordingParticipant
        {
            PreparationResult = NavigationPreparationResult.Fail(
                NavigationFailureKind.PreparationRejected
            ),
        };
        using var registration = session.AttachParticipant(participant);
        var before = session.Journal.Entries.Select(entry => entry.EntryId).ToArray();

        var operation = session.Navigate(Location("/details"));

        var outcome = Completed(operation);
        Assert.AreEqual(NavigationOutcomeKind.Failed, outcome.Kind);
        Assert.AreEqual(NavigationFailureKind.PreparationRejected, outcome.FailureKind);
        Assert.AreEqual("home", session.Current!.DefinitionId.Value);
        CollectionAssert.AreEqual(
            before,
            session.Journal.Entries.Select(entry => entry.EntryId).ToArray()
        );
        Assert.AreEqual(0, participant.StageCalls);
    }

    [TestMethod]
    public void RedirectReentersPreparationWithoutPublishingAnIntermediateEntry()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("navigation-test");
        using var session = new NavigationSession(owner, Table("a", "b"));
        var participant = new RedirectParticipant(Location("/b"));
        using var registration = session.AttachParticipant(participant);

        var operation = session.Navigate(Location("/a"));

        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(operation).Kind);
        Assert.AreEqual("b", session.Current!.DefinitionId.Value);
        Assert.AreEqual(1, session.Journal.Entries.Count);
        Assert.AreEqual(2, participant.PreparationCalls);
    }

    [TestMethod]
    public void RawActivationAndUnmatchedCanonicalLocationsAreRejectedBeforeGuards()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("navigation-test");
        using var session = new NavigationSession(owner, Table("home"));

        var malformed = session.Activate("not-an-in-app-location");
        var unmatched = session.Navigate(Location("/missing"));

        var malformedOutcome = Completed(malformed);
        var unmatchedOutcome = Completed(unmatched);
        Assert.AreEqual(NavigationOutcomeKind.RejectedActivation, malformedOutcome.Kind);
        Assert.AreEqual(NavigationFailureKind.InvalidActivation, malformedOutcome.FailureKind);
        Assert.AreEqual(NavigationOutcomeKind.RejectedActivation, unmatchedOutcome.Kind);
        Assert.AreEqual(NavigationFailureKind.RouteNotFound, unmatchedOutcome.FailureKind);
        Assert.IsNull(session.Current);
    }

    [TestMethod]
    public void SupersededAsyncPreparationIgnoresLateCompletion()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("navigation-test");
        using var session = new NavigationSession(owner, Table("a", "b", "c"));
        var participant = new DeferredParticipant();
        using var registration = session.AttachParticipant(participant);

        var first = session.Navigate(Location("/a"));
        var second = session.Navigate(Location("/b"));

        Assert.AreEqual(NavigationOutcomeKind.Superseded, Completed(first).Kind);
        participant.CompleteNext(NavigationPreparationResult.Allow);
        participant.CompleteNext(NavigationPreparationResult.Allow);
        var secondOutcome = DrainUntilCompleted(graph, second);

        Assert.AreEqual(NavigationOutcomeKind.Committed, secondOutcome.Kind);
        Assert.AreEqual("b", session.Current!.DefinitionId.Value);
        Assert.AreEqual(1, session.Journal.Entries.Count);
    }

    [TestMethod]
    public void DisposalCompletesPendingOperationAndIgnoresLatePreparation()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("navigation-test");
        var session = new NavigationSession(owner, Table("home"));
        var participant = new DeferredParticipant();
        using var registration = session.AttachParticipant(participant);
        var operation = session.Navigate(Location("/home"));

        session.Dispose();
        participant.CompleteNext(NavigationPreparationResult.Allow);
        graph.Drain();

        Assert.AreEqual(NavigationOutcomeKind.Disposed, Completed(operation).Kind);
        Assert.AreEqual(NavigationPhase.Disposed, session.Phase);
        Assert.IsNull(session.Pending);
    }

    [TestMethod]
    public void OwnerScopeDisposalCompletesPendingNavigation()
    {
        var graph = new ReactiveGraph();
        var owner = graph.CreateScope("navigation-test");
        var session = new NavigationSession(owner, Table("home"));
        using var registration = session.AttachParticipant(new DeferredParticipant());

        var operation = session.Navigate(Location("/home"));
        owner.Dispose();

        var outcome = Completed(operation);
        Assert.AreEqual(NavigationOutcomeKind.Disposed, outcome.Kind);
        Assert.IsTrue(session.IsDisposed);
        Assert.AreEqual(NavigationPhase.Disposed, session.Phase);
    }

    [TestMethod]
    public void DumpContainsIdentitiesAndExcludesRouteValues()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("navigation-test");
        using var session = new NavigationSession(owner, Table("item"));
        Completed(session.Navigate(Location("/item")));

        var dump = session.Dump();

        StringAssert.Contains(dump, "definition=item");
        Assert.IsFalse(dump.Contains("/item", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ReactiveFailureDuringBatchDoesNotReportCommittedOutcome()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("navigation-test");
        using var session = new NavigationSession(owner, Table("home", "details"));
        var fail = false;
        graph.Effect(
            () =>
            {
                _ = session.Current;
                if (fail)
                    throw new InvalidOperationException("observer failed");
            },
            "navigation-observer"
        );

        Completed(session.Navigate(Location("/home")));
        fail = true;
        var operation = session.Navigate(Location("/details"));
        var outcome = Completed(operation);

        Assert.AreEqual(NavigationOutcomeKind.Failed, outcome.Kind);
        Assert.AreEqual(NavigationFailureKind.Terminal, outcome.FailureKind);
        Assert.IsTrue(session.IsTerminated);
        Assert.AreEqual("details", session.Current!.DefinitionId.Value);
    }

    [TestMethod]
    public void StageFailureCompletesOperationAndRetainsTerminalDiagnostic()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("navigation-test");
        using var session = new NavigationSession(owner, Table("home"));
        using var registration = session.AttachParticipant(new FailingStageParticipant());

        var operation = session.Navigate(Location("/home"));
        var outcome = Completed(operation);

        Assert.AreEqual(NavigationOutcomeKind.Failed, outcome.Kind);
        Assert.AreEqual(NavigationFailureKind.Terminal, outcome.FailureKind);
        Assert.IsTrue(session.IsTerminated);
        StringAssert.Contains(session.Dump(), "terminal-error=present");
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

    private static RouteLocation Location(string text) => RouteLocation.Parse(text).Location!;

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
        for (var attempt = 0; attempt < 1_000 && !operation.Completion.IsCompleted; attempt++)
        {
            graph.Drain();
            if (!operation.Completion.IsCompleted)
                Thread.Yield();
        }
        Assert.IsTrue(
            operation.Completion.IsCompleted,
            "The owner graph did not settle navigation."
        );
        return operation.Completion.Result;
    }

    private sealed class RecordingParticipant : INavigationTransactionParticipant
    {
        internal readonly List<NavigationPreparationPhase> Phases = [];
        internal NavigationPreparationResult PreparationResult { get; init; } =
            NavigationPreparationResult.Allow;
        internal int StageCalls { get; private set; }
        internal NavigationPublication? LastPublication { get; private set; }
        internal NavigationSnapshot? CurrentDuringPublish { get; private set; }

        public ValueTask<NavigationPreparationResult> PrepareAsync(
            NavigationPrepareRequest request,
            CancellationToken cancellationToken
        )
        {
            Phases.Add(request.Phase);
            return ValueTask.FromResult(PreparationResult);
        }

        public NavigationStage Stage(NavigationStageRequest request)
        {
            StageCalls++;
            return new TestStage();
        }

        public void Publish(NavigationStage stage, NavigationPublication publication)
        {
            CurrentDuringPublish = publication.Previous;
            LastPublication = publication;
        }

        public void Retire(NavigationRetirement retirement) { }
    }

    private sealed class DeferredParticipant : INavigationTransactionParticipant
    {
        private readonly Queue<TaskCompletionSource<NavigationPreparationResult>> _preparations =
        [];

        public ValueTask<NavigationPreparationResult> PrepareAsync(
            NavigationPrepareRequest request,
            CancellationToken cancellationToken
        )
        {
            var completion = new TaskCompletionSource<NavigationPreparationResult>();
            _preparations.Enqueue(completion);
            return new(completion.Task);
        }

        public NavigationStage Stage(NavigationStageRequest request) => new TestStage();

        public void Publish(NavigationStage stage, NavigationPublication publication) { }

        public void Retire(NavigationRetirement retirement) { }

        internal void CompleteNext(NavigationPreparationResult result) =>
            _preparations.Dequeue().SetResult(result);
    }

    private sealed class RedirectParticipant(RouteLocation target)
        : INavigationTransactionParticipant
    {
        private bool _redirected;

        internal int PreparationCalls { get; private set; }

        public ValueTask<NavigationPreparationResult> PrepareAsync(
            NavigationPrepareRequest request,
            CancellationToken cancellationToken
        )
        {
            PreparationCalls++;
            if (!_redirected)
            {
                _redirected = true;
                return ValueTask.FromResult(NavigationPreparationResult.Redirect(target));
            }
            return ValueTask.FromResult(NavigationPreparationResult.Allow);
        }

        public NavigationStage Stage(NavigationStageRequest request) => new TestStage();

        public void Publish(NavigationStage stage, NavigationPublication publication) { }

        public void Retire(NavigationRetirement retirement) { }
    }

    private sealed class FailingStageParticipant : INavigationTransactionParticipant
    {
        public ValueTask<NavigationPreparationResult> PrepareAsync(
            NavigationPrepareRequest request,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(NavigationPreparationResult.Allow);

        public NavigationStage Stage(NavigationStageRequest request) => new FailingStage();

        public void Publish(NavigationStage stage, NavigationPublication publication) =>
            throw new InvalidOperationException("publication failed");

        public void Retire(NavigationRetirement retirement) { }
    }

    private sealed class FailingStage : NavigationStage
    {
        protected override void DisposeCore() =>
            throw new InvalidOperationException("cleanup failed");
    }

    private sealed class TestStage : NavigationStage
    {
        protected override void DisposeCore() { }
    }
}
