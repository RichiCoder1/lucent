using System.Diagnostics;
using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class RetainedRegionChurnContracts
{
    [TestMethod]
    public void SameKeysRetainMountsWhilePublishingInPlaceMutablePayloads()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "retained-churn");
        var data = Enumerable.Range(0, 100).Select(index => new MutableRow(index)).ToArray();
        var source = graph.Signal(data, "rows");
        var mounted = 0;
        var disposed = 0;
        var reads = 0;
        var observed = "";
        var region = composition.ForEachStructure(
            composition.Root,
            "rows",
            () => source.Value,
            row => row.Id,
            (current, context) =>
            {
                mounted++;
                var root = context.Element("row");
                root.Scope.OnDispose(() => disposed++);
                _ = root.Scope.Effect(
                    () =>
                    {
                        reads++;
                        if (current.Value.Id == 0)
                            observed = current.Value.Text;
                    },
                    "payload"
                );
                return root;
            }
        );
        graph.Drain();
        var first = region.Items[0];
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (var pass = 0; pass < 100; pass++)
        {
            source.Value = data.ToArray();
            graph.Drain();
        }
        stopwatch.Stop();
        Console.WriteLine(
            $"100 retained keys x100 publications: {stopwatch.Elapsed.TotalMilliseconds:F3} ms; {GC.GetAllocatedBytesForCurrentThread() - allocated} bytes; mounts={mounted}; disposals={disposed}; reads={reads}"
        );
        Assert.AreEqual(100, mounted);
        Assert.AreEqual(0, disposed);
        Assert.AreSame(first, region.Items[0]);
        // A source publication deliberately notifies CurrentItem even for the same object.
        // Equality cutoff here would silently lose in-place mutations in ordinary app models.
        data[0].Text = "changed in place";
        source.Value = data.ToArray();
        graph.Drain();
        Assert.AreEqual("changed in place", observed);
        Assert.AreEqual(100, mounted);
        source.Value = [];
        graph.Drain();
        Assert.AreEqual(100, disposed);
    }

    private sealed class MutableRow(int id)
    {
        internal int Id { get; } = id;
        internal string Text { get; set; } = "initial";
    }
}
