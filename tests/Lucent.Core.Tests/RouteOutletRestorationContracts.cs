using System.Text;
using Lucent.Core;

namespace Lucent.Core.Tests;

public sealed partial class RouteOutletContracts
{
    [TestMethod]
    public void JournalRestoreWiresRootOutletContextsAndRetainsParentOnForwardWithoutInteraction()
    {
        var fixture = Fixture.Create();
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "restoration-outlet");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var owner = graph.CreateScope("restoration-navigation");
        using var session = new NavigationSession(owner, fixture.Table);
        using var handle = new RouteOutletHandle();
        var prepared = new List<string>();
        var options = new RouteOutletOptions(
            prepare: (level, request, _) =>
            {
                prepared.Add(request.Phase + ":" + level.Id.Value);
                return ValueTask.FromResult(NavigationPreparationResult.Allow);
            }
        );
        _ = composition.Mount(
            composition.Root,
            theme,
            Router(
                session,
                Bundle(fixture.Descriptors, level => BuildLevel(level, fixture)),
                handle,
                options
            )
        );
        var restoration = new NavigationRestoration(
            fixture.Table,
            "outlet-v1",
            RouteReference.Create(
                fixture.Table.Patterns.Single(pattern => pattern.Id.Value == "project"),
                [RouteValue.FromSigned32(7)]
            ),
            _ => true,
            options: new(NavigationRestorationMode.Journal)
        );
        const string payload =
            """{"schema":"lucent.navigation","version":1,"scope":"outlet-v1","mode":"journal","activeKey":9,"entries":[{"key":42,"definition":"project","location":"/projects/7"},{"key":9,"definition":"issue","location":"/projects/7/issues/42"},{"key":60,"definition":"issue","location":"/projects/7/issues/43"}]}""";
        Assert.AreEqual(
            NavigationOutcomeKind.Committed,
            Completed(session.Restore(restoration.Decode(Encoding.UTF8.GetBytes(payload)))).Kind
        );
        CollectionAssert.AreEqual(EnterInitialCalls, prepared);
        Assert.AreEqual(2, handle.Snapshot.Levels.Count);
        Assert.AreEqual(2L, handle.Snapshot.EntryId);
        Assert.AreEqual(3, session.Journal.Entries.Count);
        Assert.AreEqual(7, fixture.ProjectContexts.Single().Parameters.Project);
        Assert.AreEqual(42, fixture.IssueContexts.Single().Parameters.Issue);
        Assert.AreSame(session.Current, fixture.IssueContexts.Single().ActiveEntry);
        var parent = handle.Snapshot.Levels[0].ElementId;
        var initialLeaf = handle.Snapshot.Levels[1].ElementId;
        Assert.AreEqual(NavigationOutcomeKind.Committed, Completed(session.Forward()).Kind);
        Assert.AreEqual(3L, handle.Snapshot.EntryId);
        Assert.AreEqual(parent, handle.Snapshot.Levels[0].ElementId);
        Assert.AreNotEqual(initialLeaf, handle.Snapshot.Levels[1].ElementId);
        Assert.AreEqual(43, fixture.IssueContexts[^1].Parameters.Issue);
        Assert.AreEqual(2, fixture.IssueContexts.Count);
        Assert.AreEqual(1, fixture.ProjectContexts.Count);
    }
}
