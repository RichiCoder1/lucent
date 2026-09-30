using System.Text;
using Lucent.Core;

namespace Lucent.Core.Tests;

public sealed partial class NavigationSessionContracts
{
    private const string SessionJournal =
        """{"schema":"lucent.navigation","version":1,"scope":"session-v1","mode":"journal","activeKey":9,"entries":[{"key":42,"definition":"home","location":"/home"},{"key":9,"definition":"item","location":"/item","state":{"codec":"lucent.interaction","version":1,"focus":"heading","viewports":[{"target":"list","x":0,"y":20}]}},{"key":60,"definition":"newer","location":"/newer"}]}""";

    [TestMethod]
    public void JournalImportPublishesAtomicallyWithFreshIdsAndDormantRoutesPrepareOnlyWhenVisited()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("journal-import");
        var table = Table("home", "item", "newer", "fresh");
        using var session = new NavigationSession(owner, table);
        var gate = new TaskCompletionSource<NavigationPreparationResult>();
        var preparingInitial = true;
        var participant = new RestorationParticipant
        {
            Prepare = _ =>
                preparingInitial
                    ? new(gate.Task)
                    : ValueTask.FromResult(NavigationPreparationResult.Allow),
        };
        using var root = session.AttachParticipant(participant);
        var commits = new List<NavigationCommit>();
        using var observed = session.RegisterCommitted(owner, commits.Add);
        var operation = session.Restore(
            JournalRestoration(table).Decode(Encoding.UTF8.GetBytes(SessionJournal))
        );
        Assert.AreEqual(0, session.Journal.Entries.Count);
        Assert.IsNull(session.Current);
        Assert.AreEqual("item", participant.Requests.Single().Target.DefinitionId.Value);
        Assert.AreEqual(2L, participant.Requests.Single().Target.EntryId);
        preparingInitial = false;
        gate.SetResult(NavigationPreparationResult.Allow);
        Assert.AreEqual(
            NavigationOutcomeKind.Committed,
            DrainUntilCompleted(graph, operation).Kind
        );
        var publication = participant.Publications.Single();
        Assert.AreEqual(3, publication.Journal.Entries.Count);
        Assert.AreEqual(1, publication.Journal.CurrentIndex);
        Assert.AreSame(participant.Requests[0].Target, publication.Current);
        Assert.AreSame(publication.Current, commits.Single().Current);
        Assert.AreEqual(1L, publication.Journal.Entries[0].EntryId);
        Assert.AreEqual(2L, publication.Journal.Entries[1].EntryId);
        Assert.AreEqual(3L, publication.Journal.Entries[2].EntryId);
        Assert.AreEqual("heading", publication.RestoredInteractionStates![2].FocusTargetId);
        Assert.IsFalse(publication.RestoredInteractionStates.ContainsKey(9));
        Assert.AreEqual(
            new ScrollOffset(0, 20),
            publication.RestoredInteractionStates[2].Viewports.Single().Offset
        );
        Assert.IsTrue(session.CanGoBack && session.CanGoForward);
        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(session.Back()).Kind);
        Assert.AreEqual("home", session.Current!.DefinitionId.Value);
        Assert.AreEqual(NavigationPreparationPhase.Leave, participant.Requests[1].Phase);
        Assert.AreEqual(NavigationPreparationPhase.Enter, participant.Requests[2].Phase);
        Assert.AreEqual("home", participant.Requests[2].Target.DefinitionId.Value);
        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(session.Forward()).Kind);
        Assert.AreEqual(2L, session.Current.EntryId);
        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(session.Forward()).Kind);
        Assert.AreEqual(3L, session.Current.EntryId);
        Assert.AreEqual(
            NavigationOutcomeKind.Committed,
            Completed(session.Navigate(Location("/fresh"))).Kind
        );
        Assert.AreEqual(4L, session.Current.EntryId);
        Assert.IsTrue(
            participant.Publications.Skip(1).All(value => value.RestoredInteractionStates is null)
        );
    }

    [TestMethod]
    [DataRow("stay")]
    [DataRow("redirect")]
    [DataRow("capacity")]
    public void JournalFallbackOrRedirectDropsDormantEntriesAndAllImportedState(string scenario)
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("journal-discard");
        var table = Table("home", "item", "newer");
        using var session = new NavigationSession(
            owner,
            table,
            maximumEntries: scenario == "capacity" ? 2 : 32
        );
        var participant = new RestorationParticipant
        {
            Prepare = request =>
                ValueTask.FromResult(
                    request.Target.DefinitionId.Value == "item"
                        ? scenario == "redirect"
                            ? NavigationPreparationResult.Redirect(Location("/newer"))
                            : NavigationPreparationResult.Stay
                        : NavigationPreparationResult.Allow
                ),
        };
        using var root = session.AttachParticipant(participant);
        var operation = session.Restore(
            JournalRestoration(table).Decode(Encoding.UTF8.GetBytes(SessionJournal))
        );
        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(operation).Kind);
        Assert.AreEqual(
            scenario == "redirect" ? "newer" : "home",
            session.Current!.DefinitionId.Value
        );
        Assert.AreEqual(1L, session.Current.EntryId);
        Assert.AreEqual(1, session.Journal.Entries.Count);
        Assert.IsNull(participant.Publications.Single().RestoredInteractionStates);
        Assert.AreEqual(scenario == "capacity" ? 1 : 2, participant.Requests.Count);
    }

    [TestMethod]
    public void JournalReplaySupersessionCannotPublishImportedHistoryOrState()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("journal-obsolete");
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
        using var root = session.AttachParticipant(participant);
        var restore = session.Restore(
            JournalRestoration(table).Decode(Encoding.UTF8.GetBytes(SessionJournal))
        );
        Assert.AreEqual(
            NavigationOutcomeKind.Committed,
            Completed(session.Activate("/newer")).Kind
        );
        Assert.AreEqual(NavigationOutcomeKind.Superseded, Completed(restore).Kind);
        gate.SetResult(NavigationPreparationResult.Allow);
        graph.Drain();
        Assert.AreEqual("newer", session.Current!.DefinitionId.Value);
        Assert.AreEqual(1, session.Journal.Entries.Count);
        Assert.IsNull(participant.Publications.Single().RestoredInteractionStates);
    }

    [TestMethod]
    public void JournalAdmissionRechecksInactiveRoutesWithoutPublishingStaleEntries()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("journal-readmission");
        var table = Table("home", "item", "newer");
        using var session = new NavigationSession(owner, table);
        var participant = new RestorationParticipant();
        using var root = session.AttachParticipant(participant);
        var allowHome = true;
        var codec = JournalRestoration(
            table,
            match => allowHome || match.DefinitionId.Value != "home"
        );
        var plan = codec.Decode(Encoding.UTF8.GetBytes(SessionJournal));
        allowHome = false;
        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(session.Restore(plan)).Kind);
        Assert.AreEqual(0, session.Journal.CurrentIndex);
        Assert.AreEqual(2, session.Journal.Entries.Count);
        Assert.AreEqual("item", session.Journal.Entries[0].DefinitionId.Value);
        Assert.AreEqual("newer", session.Journal.Entries[1].DefinitionId.Value);
        Assert.AreEqual(
            "heading",
            participant.Publications.Single().RestoredInteractionStates![1].FocusTargetId
        );
    }

    [TestMethod]
    public void JournalCaptureUsesParticipantStateOnlyWhenTheCodecIsExplicitlyRegistered()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("journal-state-capture");
        var table = Table("home", "item", "newer");
        using var session = new NavigationSession(owner, table, Location("/item"));
        var state = new NavigationEntryInteractionState("heading", [new("list", new(0, 20))]);
        using var root = session.AttachParticipant(
            new RestorationParticipant
            {
                CaptureStates = new Dictionary<long, NavigationEntryInteractionState>
                {
                    [session.Current!.EntryId] = state,
                },
            }
        );
        var codec = JournalRestoration(table);
        var captured = codec.Capture(session);
        const string expected =
            """{"schema":"lucent.navigation","version":1,"scope":"session-v1","mode":"journal","activeKey":1,"entries":[{"key":1,"definition":"item","location":"/item","state":{"codec":"lucent.interaction","version":1,"focus":"heading","viewports":[{"target":"list","x":0,"y":20}]}}]}""";
        Assert.AreEqual(NavigationRestorationStatus.Ready, captured.Status);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(expected), captured.Utf8.ToArray());
        var disabled = new NavigationRestoration(
            table,
            "session-v1",
            RouteReference.Create(table.Patterns[0], []),
            _ => true,
            options: new(NavigationRestorationMode.Journal)
        );
        Assert.IsNull(
            codec.Decode(disabled.Capture(session).Utf8.Span).Journal!.Entries.Single().State
        );
    }

    [TestMethod]
    public void JournalStateCaptureVisitsOnlyTheSelectedBoundedWindow()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("journal-state-window");
        var table = Table("home", "item", "newer");
        using var session = new NavigationSession(owner, table);
        var participant = new RestorationParticipant();
        using var root = session.AttachParticipant(participant);
        Completed(session.Navigate(Location("/home")));
        Completed(session.Navigate(Location("/item")));
        Completed(session.Navigate(Location("/newer")));
        var codec = new NavigationRestoration(
            table,
            "session-v1",
            RouteReference.Create(table.Patterns[0], []),
            _ => true,
            options: new(
                NavigationRestorationMode.Journal,
                2,
                NavigationRestorationStateCodecs.Interaction
            )
        );
        Assert.AreEqual(NavigationRestorationStatus.Ready, codec.Capture(session).Status);
        Assert.AreEqual(2, participant.CapturedJournal!.Entries.Count);
        Assert.AreEqual("item", participant.CapturedJournal.Entries[0].DefinitionId.Value);
        Assert.AreEqual("newer", participant.CapturedJournal.Current!.DefinitionId.Value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OversizedCapturedInteractionStateProducesNoPartialSnapshot(bool encodedBytes)
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("journal-state-limit");
        var table = Table("home", "item");
        using var session = new NavigationSession(owner, table, Location("/item"));
        var positions = Enumerable
            .Range(0, encodedBytes ? 16 : 17)
            .Select(index => new NavigationViewportPosition(
                encodedBytes ? new string('é', 62) + index : "target" + index,
                new(0, 1)
            ))
            .ToArray();
        using var root = session.AttachParticipant(
            new RestorationParticipant
            {
                CaptureStates = new Dictionary<long, NavigationEntryInteractionState>
                {
                    [session.Current!.EntryId] = new("heading", positions),
                },
            }
        );
        var result = JournalRestoration(table).Capture(session);
        Assert.AreEqual(NavigationRestorationStatus.TooLarge, result.Status);
        Assert.IsTrue(result.Utf8.IsEmpty);
    }

    private static NavigationRestoration JournalRestoration(
        RouteTable table,
        Func<RouteMatch, bool>? policy = null
    ) =>
        new(
            table,
            "session-v1",
            RouteReference.Create(table.Patterns[0], []),
            policy ?? (_ => true),
            options: new(
                NavigationRestorationMode.Journal,
                stateCodecs: NavigationRestorationStateCodecs.Interaction
            )
        );
}
