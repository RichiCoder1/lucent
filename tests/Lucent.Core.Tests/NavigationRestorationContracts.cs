using System.Text;
using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed partial class NavigationRestorationContracts
{
    private const string Snapshot =
        """{"schema":"lucent.navigation","version":1,"scope":"workspace-v1","mode":"location","active":{"definition":"item","location":"/items/1"}}""";

    [TestMethod]
    public void CaptureHasFixedVersionOneBytesAndDecodeMatchesCurrentDefinition()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("restoration-codec");
        var table = Table();
        var codec = Codec(table);
        using var session = new NavigationSession(owner, table, Location("/items/1"));
        var captured = codec.Capture(session);
        Assert.AreEqual(NavigationRestorationStatus.Ready, captured.Status);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(Snapshot), captured.Utf8.ToArray());
        var decoded = codec.Decode(Encoding.UTF8.GetBytes(Snapshot));
        Assert.AreEqual(NavigationRestorationStatus.Ready, decoded.Status);
        Assert.AreEqual("/items/1", decoded.Target!.Location.CanonicalText);
        Assert.AreEqual(1, decoded.Target.Match.GetValue(0).Signed32);
        Assert.AreSame(
            table.Patterns.Single(pattern => pattern.Id.Value == "item"),
            decoded.Target.Match.Pattern
        );
        Assert.AreEqual(
            1,
            session.Journal.Entries.Count,
            "Decode must not mutate the source session."
        );
    }

    [TestMethod]
    [DataRow("schema", NavigationRestorationStatus.InvalidPayload)]
    [DataRow("version", NavigationRestorationStatus.UnsupportedVersion)]
    [DataRow("scope", NavigationRestorationStatus.ScopeMismatch)]
    [DataRow("mode", NavigationRestorationStatus.UnsupportedMode)]
    [DataRow("stale", NavigationRestorationStatus.InvalidRoute)]
    [DataRow("unmatched", NavigationRestorationStatus.InvalidRoute)]
    [DataRow("noncanonical", NavigationRestorationStatus.InvalidRoute)]
    [DataRow("traversal", NavigationRestorationStatus.InvalidRoute)]
    [DataRow("duplicate", NavigationRestorationStatus.InvalidPayload)]
    [DataRow("escaped-duplicate", NavigationRestorationStatus.InvalidPayload)]
    [DataRow("duplicate-active", NavigationRestorationStatus.InvalidPayload)]
    [DataRow("unknown", NavigationRestorationStatus.InvalidPayload)]
    [DataRow("state", NavigationRestorationStatus.InvalidPayload)]
    [DataRow("missing", NavigationRestorationStatus.InvalidPayload)]
    [DataRow("active-type", NavigationRestorationStatus.InvalidPayload)]
    [DataRow("number-type", NavigationRestorationStatus.InvalidPayload)]
    [DataRow("trailing", NavigationRestorationStatus.InvalidPayload)]
    [DataRow("comment", NavigationRestorationStatus.InvalidPayload)]
    [DataRow("comma", NavigationRestorationStatus.InvalidPayload)]
    [DataRow("unicode", NavigationRestorationStatus.InvalidPayload)]
    [DataRow("deep", NavigationRestorationStatus.InvalidPayload)]
    [DataRow("key-size", NavigationRestorationStatus.TooLarge)]
    [DataRow("location-size", NavigationRestorationStatus.TooLarge)]
    public void HostileOrStaleSnapshotsFailWithFiniteReasons(
        string example,
        NavigationRestorationStatus expected
    )
    {
        var payload = example switch
        {
            "schema" => Snapshot.Replace("lucent.navigation", "other.schema"),
            "version" => Snapshot.Replace("\"version\":1", "\"version\":2"),
            "scope" => Snapshot.Replace("workspace-v1", "another-workspace"),
            "mode" => Snapshot.Replace("\"mode\":\"location\"", "\"mode\":\"journal\""),
            "stale" => Snapshot.Replace("\"definition\":\"item\"", "\"definition\":\"old-item\""),
            "unmatched" => Snapshot.Replace("/items/1", "/missing"),
            "noncanonical" => Snapshot.Replace("/items/1", "/items/%31"),
            "traversal" => Snapshot.Replace("/items/1", "/items/../1"),
            "duplicate" => Snapshot.Replace("\"version\":1", "\"version\":1,\"version\":1"),
            "escaped-duplicate" => Snapshot.Replace(
                "\"version\":1",
                "\"version\":1,\"vers\\u0069on\":1"
            ),
            "duplicate-active" => Snapshot.Replace(
                "\"definition\":\"item\"",
                "\"definition\":\"item\",\"definition\":\"item\""
            ),
            "unknown" => Snapshot.Insert(1, "\"extra\":0,"),
            "state" => Snapshot.Replace(
                "\"definition\":\"item\"",
                "\"definition\":\"item\",\"state\":{}"
            ),
            "missing" => Snapshot.Replace("\"scope\":\"workspace-v1\",", ""),
            "active-type" => Snapshot.Replace(
                "{\"definition\":\"item\",\"location\":\"/items/1\"}",
                "[]"
            ),
            "number-type" => Snapshot.Replace("\"version\":1", "\"version\":\"1\""),
            "trailing" => Snapshot + " {}",
            "comment" => Snapshot.Insert(1, "/* untrusted */"),
            "comma" => Snapshot.Insert(Snapshot.Length - 1, ","),
            "unicode" => Snapshot.Replace("workspace-v1", "\\uD800"),
            "deep" => Snapshot.Replace("\"item\"", "[[[[[[[[[[0]]]]]]]]]]"),
            "key-size" => Snapshot.Replace("workspace-v1", new string('x', 129)),
            "location-size" => Snapshot.Replace("/items/1", "/" + new string('x', 2048)),
            _ => throw new InvalidOperationException(),
        };
        var plan = Codec(Table()).Decode(Encoding.UTF8.GetBytes(payload));
        Assert.AreEqual(expected, plan.Status);
        Assert.IsNull(plan.Target);
        Assert.AreEqual("navigation-restore-plan status=" + expected, plan.ToString());
    }

    [TestMethod]
    public void EveryTruncationAndInvalidUtf8IsRejectedWithoutAPlanTarget()
    {
        var codec = Codec(Table());
        var bytes = Encoding.UTF8.GetBytes(Snapshot);
        Assert.AreEqual(NavigationRestorationStatus.NoSnapshot, codec.Decode([]).Status);
        for (var length = 1; length < bytes.Length; length++)
        {
            var result = codec.Decode(bytes.AsSpan(0, length));
            Assert.AreEqual(
                NavigationRestorationStatus.InvalidPayload,
                result.Status,
                $"Prefix {length} was accepted."
            );
            Assert.IsNull(result.Target);
        }
        bytes[bytes.AsSpan().IndexOf("workspace-v1"u8)] = 0xff;
        Assert.AreEqual(NavigationRestorationStatus.InvalidPayload, codec.Decode(bytes).Status);
    }

    [TestMethod]
    public void ByteBudgetIsAppliedBeforeReadingAndWhileWritingWithoutPartialPayloads()
    {
        var table = Table();
        var bytes = Encoding.UTF8.GetBytes(Snapshot);
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("restoration-budget");
        using var session = new NavigationSession(owner, table, Location("/items/1"));
        var exact = Codec(table, maximumBytes: bytes.Length);
        Assert.AreEqual(NavigationRestorationStatus.Ready, exact.Capture(session).Status);
        Assert.AreEqual(NavigationRestorationStatus.Ready, exact.Decode(bytes).Status);
        var small = Codec(table, maximumBytes: bytes.Length - 1);
        var captured = small.Capture(session);
        Assert.AreEqual(NavigationRestorationStatus.TooLarge, captured.Status);
        Assert.IsTrue(captured.Utf8.IsEmpty);
        Assert.AreEqual(NavigationRestorationStatus.TooLarge, small.Decode(bytes).Status);
        var oversized = new byte[NavigationRestoration.MaximumPayloadBytes + 1];
        var codec = Codec(table);
        _ = codec.Decode(oversized);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 100; index++)
            Assert.AreEqual(NavigationRestorationStatus.TooLarge, codec.Decode(oversized).Status);
        Assert.IsTrue(
            GC.GetAllocatedBytesForCurrentThread() - before
                < NavigationRestoration.MaximumPayloadBytes,
            "Rejecting oversized input must not copy or parse the payload."
        );
    }

    [TestMethod]
    public void EmptyOrNonpersistableCurrentProducesNoSnapshotAndPolicyRejectsDecode()
    {
        var table = Table();
        var codec = Codec(table, _ => false);
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("restoration-policy");
        using var session = new NavigationSession(owner, table);
        Assert.AreEqual(NavigationRestorationStatus.NoSnapshot, codec.Capture(session).Status);
        _ = session.Navigate(Location("/items/1"));
        var captured = codec.Capture(session);
        Assert.AreEqual(NavigationRestorationStatus.NoSnapshot, captured.Status);
        Assert.IsTrue(captured.Utf8.IsEmpty);
        Assert.AreEqual("navigation-capture status=NoSnapshot", captured.ToString());
        Assert.AreEqual(
            NavigationRestorationStatus.PolicyRejected,
            codec.Decode(Encoding.UTF8.GetBytes(Snapshot)).Status
        );
        var throwing = Codec(
            table,
            _ => throw new InvalidOperationException("private-policy-detail")
        );
        Assert.AreEqual(
            NavigationRestorationStatus.PolicyRejected,
            throwing.Decode(Encoding.UTF8.GetBytes(Snapshot)).Status
        );
    }

    [TestMethod]
    public void ScopeBoundsUseUtf8AndForeignFallbacksAreRejectedAtConfiguration()
    {
        var table = Table();
        var fallback = RouteReference.Create(
            table.Patterns.Single(pattern => pattern.Id.Value == "home"),
            []
        );
        var policy = new NavigationRestoration(table, new string('é', 64), fallback, _ => true);
        Assert.AreSame(fallback, policy.SafeFallback);
        Assert.AreSame(table, policy.RouteTable);
        Assert.ThrowsExactly<ArgumentException>(() =>
            new NavigationRestoration(table, new string('é', 65), fallback, _ => true)
        );
        Assert.ThrowsExactly<ArgumentException>(() =>
            new NavigationRestoration(table, "\ud800", fallback, _ => true)
        );
        Assert.ThrowsExactly<ArgumentException>(() =>
            new NavigationRestoration(Table(), "workspace-v1", fallback, _ => true)
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            Codec(table, maximumBytes: NavigationRestoration.MaximumPayloadBytes + 1)
        );
    }

    private static RouteTable Table() =>
        RouteTable.Create([
            RoutePattern.Create(new("home"), [RouteSegmentPattern.LiteralSegment("home")]),
            RoutePattern.Create(
                new("item"),
                [
                    RouteSegmentPattern.LiteralSegment("items"),
                    RouteSegmentPattern.Parameter("id", 0, RouteValueShape.Signed32),
                ]
            ),
        ]);

    private static NavigationRestoration Codec(
        RouteTable table,
        Func<RouteMatch, bool>? policy = null,
        int maximumBytes = NavigationRestoration.MaximumPayloadBytes
    ) =>
        new(
            table,
            "workspace-v1",
            RouteReference.Create(table.Patterns.Single(pattern => pattern.Id.Value == "home"), []),
            policy ?? (_ => true),
            maximumBytes
        );

    private static RouteLocation Location(string value) => RouteLocation.Parse(value).Location!;
}
