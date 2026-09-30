using System.Text;
using Lucent.Core;

namespace Lucent.Core.Tests;

public sealed partial class NavigationRestorationContracts
{
    private const string JournalPayload =
        """{"schema":"lucent.navigation","version":1,"scope":"workspace-v1","mode":"journal","activeKey":37,"entries":[{"key":64,"definition":"home","location":"/home"},{"key":11,"definition":"item","location":"/items/1"},{"key":37,"definition":"item","location":"/items/2"}]}""";
    private const string InteractionPayload =
        """{"codec":"lucent.interaction","version":1,"focus":"heading","viewports":[{"target":"items","x":1.25,"y":240}]}""";

    [TestMethod]
    public void JournalCaptureUsesLocalKeysBoundedWindowAndIndependentExpectedBytes()
    {
        var table = Table();
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("journal-capture");
        using var session = new NavigationSession(owner, table, maximumEntries: 3);
        _ = session.Navigate(Location("/home"));
        _ = session.Navigate(Location("/items/1"));
        _ = session.Navigate(Location("/items/2"));
        _ = session.Navigate(Location("/items/3"));
        _ = session.Back();
        var codec = JournalCodec(table, maximumEntries: 3);
        var captured = codec.Capture(session);
        const string expected =
            """{"schema":"lucent.navigation","version":1,"scope":"workspace-v1","mode":"journal","activeKey":2,"entries":[{"key":1,"definition":"item","location":"/items/1"},{"key":2,"definition":"item","location":"/items/2"},{"key":3,"definition":"item","location":"/items/3"}]}""";
        Assert.AreEqual(NavigationRestorationStatus.Ready, captured.Status);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(expected), captured.Utf8.ToArray());
        Assert.AreEqual(2L, session.Journal.Entries[0].EntryId);
        var preceding = JournalCodec(table, maximumEntries: 2)
            .Decode(JournalCodec(table, maximumEntries: 2).Capture(session).Utf8.Span)
            .Journal!;
        Assert.AreEqual(2, preceding.Entries.Count);
        Assert.AreEqual("/items/1", preceding.Entries[0].Target.Location.CanonicalText);
        Assert.AreEqual("/items/2", preceding.Entries[1].Target.Location.CanonicalText);
        Assert.AreEqual(1, preceding.ActiveIndex);
        var filtered = JournalCodec(table, match => match.GetValue(0).Signed32 != 1)
            .Capture(session);
        var kept = codec.Decode(filtered.Utf8.Span).Journal!;
        Assert.AreEqual("/items/2", kept.Entries[0].Target.Location.CanonicalText);
        Assert.AreEqual(0, kept.ActiveIndex);
        Assert.AreEqual(
            NavigationRestorationStatus.NoSnapshot,
            JournalCodec(table, _ => false).Capture(session).Status
        );
    }

    [TestMethod]
    public void JournalDecodeDropsInvalidInactiveRoutesAndRecomputesActiveIndex()
    {
        var table = Table();
        var codec = JournalCodec(table);
        var payload = JournalPayload.Replace("/home", "/missing").Replace("/items/1", "/items/%31");
        var plan = codec.Decode(Encoding.UTF8.GetBytes(payload));
        Assert.AreEqual(NavigationRestorationStatus.Ready, plan.Status);
        Assert.AreEqual(2, plan.DroppedEntries);
        Assert.AreEqual(0, plan.Journal!.ActiveIndex);
        Assert.AreEqual(37, plan.Journal.Entries.Single().Key);
        Assert.AreEqual(2, plan.Target!.Match.GetValue(0).Signed32);
        Assert.AreEqual(
            NavigationRestorationStatus.InvalidRoute,
            codec
                .Decode(Encoding.UTF8.GetBytes(JournalPayload.Replace("/items/2", "/missing")))
                .Status
        );
        Assert.AreEqual(
            NavigationRestorationStatus.PolicyRejected,
            JournalCodec(table, match => match.DefinitionId.Value == "home")
                .Decode(Encoding.UTF8.GetBytes(JournalPayload))
                .Status
        );
        Assert.AreEqual(
            NavigationRestorationStatus.Ready,
            codec.Decode(Encoding.UTF8.GetBytes(Snapshot)).Status
        );
        Assert.AreEqual(
            NavigationRestorationStatus.UnsupportedMode,
            Codec(table).Decode(Encoding.UTF8.GetBytes(JournalPayload)).Status
        );
    }

    [TestMethod]
    [DataRow("duplicate-key")]
    [DataRow("missing-active")]
    [DataRow("zero-key")]
    [DataRow("large-key")]
    [DataRow("fraction-key")]
    [DataRow("duplicate-entry-field")]
    [DataRow("unknown-entry-field")]
    [DataRow("mixed-mode")]
    [DataRow("empty")]
    public void InvalidJournalStructureRejectsTheEnvelope(string example)
    {
        var payload = example switch
        {
            "duplicate-key" => JournalPayload.Replace("\"key\":64", "\"key\":37"),
            "missing-active" => JournalPayload.Replace("\"activeKey\":37", "\"activeKey\":4"),
            "zero-key" => JournalPayload.Replace("\"key\":64", "\"key\":0"),
            "large-key" => JournalPayload.Replace("\"key\":64", "\"key\":65"),
            "fraction-key" => JournalPayload.Replace("\"key\":64", "\"key\":1.5"),
            "duplicate-entry-field" => JournalPayload.Replace(
                "\"key\":64",
                "\"key\":64,\"key\":64"
            ),
            "unknown-entry-field" => JournalPayload.Replace("\"key\":64", "\"key\":64,\"other\":0"),
            "mixed-mode" => JournalPayload.Insert(
                1,
                "\"active\":{\"definition\":\"home\",\"location\":\"/home\"},"
            ),
            "empty" => JournalPayload[..JournalPayload.IndexOf('[', StringComparison.Ordinal)]
                + "[]}",
            _ => throw new InvalidOperationException(),
        };
        Assert.AreEqual(
            NavigationRestorationStatus.InvalidPayload,
            JournalCodec(Table()).Decode(Encoding.UTF8.GetBytes(payload)).Status
        );
    }

    [TestMethod]
    public void JournalStateIsExplicitTypedAndSeparateFromRouteIdentity()
    {
        var bytes = Encoding.UTF8.GetBytes(WithState(InteractionPayload));
        var table = Table();
        var enabled = JournalCodec(table, interaction: true).Decode(bytes);
        var state = enabled.Journal!.Entries[2].State!;
        Assert.AreEqual("heading", state.FocusTargetId);
        Assert.AreEqual("items", state.Viewports.Single().TargetId);
        Assert.AreEqual(new ScrollOffset(1.25f, 240), state.Viewports.Single().Offset);
        Assert.AreEqual(0, enabled.DroppedStates);
        var disabled = JournalCodec(table).Decode(bytes);
        Assert.AreEqual(NavigationRestorationStatus.Ready, disabled.Status);
        Assert.IsNull(disabled.Journal!.Entries[2].State);
        Assert.AreEqual(1, disabled.DroppedStates);
    }

    [TestMethod]
    [DataRow("unknown-codec")]
    [DataRow("unknown-version")]
    [DataRow("negative")]
    [DataRow("overflow")]
    [DataRow("duplicate-state")]
    [DataRow("unknown-field")]
    [DataRow("empty-target")]
    [DataRow("unicode-target")]
    [DataRow("duplicate-target")]
    [DataRow("too-many-targets")]
    [DataRow("scalar")]
    public void InvalidBoundedStateIsDroppedWithoutDroppingItsRoute(string example)
    {
        var state = example switch
        {
            "unknown-codec" => InteractionPayload.Replace("lucent.interaction", "future.codec"),
            "unknown-version" => InteractionPayload.Replace("\"version\":1", "\"version\":2"),
            "negative" => InteractionPayload.Replace("1.25", "-1"),
            "overflow" => InteractionPayload.Replace("1.25", "1e100"),
            "duplicate-state" => InteractionPayload.Replace(
                "\"version\":1",
                "\"version\":1,\"version\":1"
            ),
            "unknown-field" => InteractionPayload.Insert(1, "\"extra\":0,"),
            "empty-target" => InteractionPayload.Replace("heading", ""),
            "unicode-target" => InteractionPayload.Replace("heading", "\\uD800"),
            "duplicate-target" => InteractionPayload.Replace(
                "[{",
                "[{\"target\":\"items\",\"x\":0,\"y\":1},{"
            ),
            "too-many-targets" =>
                "{\"codec\":\"lucent.interaction\",\"version\":1,\"focus\":null,\"viewports\":["
                    + string.Join(
                        ',',
                        Enumerable
                            .Range(0, 17)
                            .Select(index => $"{{\"target\":\"t{index}\",\"x\":0,\"y\":0}}")
                    )
                    + "]}",
            "scalar" => "false",
            _ => throw new InvalidOperationException(),
        };
        var plan = JournalCodec(Table(), interaction: true)
            .Decode(Encoding.UTF8.GetBytes(WithState(state)));
        Assert.AreEqual(NavigationRestorationStatus.Ready, plan.Status);
        Assert.AreEqual(1, plan.DroppedStates);
        Assert.AreEqual(3, plan.Journal!.Entries.Count);
        Assert.IsNull(plan.Journal.Entries[2].State);
    }

    [TestMethod]
    public void JournalEntryAndStateBudgetsRejectExcessWithoutPartialImport()
    {
        var table = Table();
        Assert.AreEqual(
            NavigationRestorationStatus.InvalidPayload,
            JournalCodec(table, interaction: true)
                .Decode(Encoding.UTF8.GetBytes(WithState("{\"future\":[[[[[[0]]]]]]}")))
                .Status
        );
        Assert.AreEqual(
            NavigationRestorationStatus.TooLarge,
            JournalCodec(table, maximumEntries: 2)
                .Decode(Encoding.UTF8.GetBytes(JournalPayload))
                .Status
        );
        var exactState = InteractionPayload.Insert(
            1,
            new string(
                ' ',
                NavigationRestorationOptions.MaximumStateBytes
                    - Encoding.UTF8.GetByteCount(InteractionPayload)
            )
        );
        Assert.AreEqual(
            NavigationRestorationStatus.Ready,
            JournalCodec(table, interaction: true)
                .Decode(Encoding.UTF8.GetBytes(WithState(exactState)))
                .Status
        );
        Assert.AreEqual(
            NavigationRestorationStatus.TooLarge,
            JournalCodec(table, interaction: true)
                .Decode(Encoding.UTF8.GetBytes(WithState(exactState.Insert(1, " "))))
                .Status
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new NavigationRestorationOptions(maximumEntries: 65)
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new NavigationRestorationOptions(stateCodecs: (NavigationRestorationStateCodecs)2)
        );
        var truncated = Encoding.UTF8.GetBytes(JournalPayload);
        for (var count = 1; count < truncated.Length; count++)
            Assert.AreEqual(
                NavigationRestorationStatus.InvalidPayload,
                JournalCodec(table).Decode(truncated.AsSpan(0, count)).Status
            );
    }

    private static string WithState(string state) =>
        JournalPayload.Replace(
            "\"location\":\"/items/2\"",
            "\"location\":\"/items/2\",\"state\":" + state
        );

    private static NavigationRestoration JournalCodec(
        RouteTable table,
        Func<RouteMatch, bool>? policy = null,
        int maximumEntries = 32,
        bool interaction = false
    ) =>
        new(
            table,
            "workspace-v1",
            RouteReference.Create(table.Patterns[0], []),
            policy ?? (_ => true),
            options: new(
                NavigationRestorationMode.Journal,
                maximumEntries,
                interaction
                    ? NavigationRestorationStateCodecs.Interaction
                    : NavigationRestorationStateCodecs.None
            )
        );
}
