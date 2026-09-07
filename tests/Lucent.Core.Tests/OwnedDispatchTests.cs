using System.Runtime.CompilerServices;
using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class OwnedDispatchTests
{
    [TestMethod]
    public void WorkerPostsRunOnlyOnOwnerAndCanBeCancelled()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("dispatch");
        var ownerThread = Environment.CurrentManagedThreadId;
        var calls = new List<int>();
        IDisposable? cancelled = null;
        var worker = new Thread(() =>
        {
            cancelled = scope.Post(() => calls.Add(-1));
            scope.Post(() => calls.Add(Environment.CurrentManagedThreadId));
        });
        worker.Start();
        worker.Join();
        cancelled!.Dispose();
        Assert.IsEmpty(calls);
        graph.Drain();
        CollectionAssert.AreEqual(new[] { ownerThread }, calls);
    }

    [TestMethod]
    public void ScopeDisposalReleasesQueuedAndFutureClosuresBeforeDrain()
    {
        var graph = new ReactiveGraph();
        var scope = graph.CreateScope("release");
        var queued = QueuePayload(scope);
        scope.Dispose();
        var future = QueuePayload(scope);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.IsFalse(queued.IsAlive, "Disposed scope retained an undrained callback payload.");
        Assert.IsFalse(future.IsAlive, "Post to a disposed scope retained its callback payload.");
        graph.Drain();
        GC.KeepAlive(scope);
        GC.KeepAlive(graph);
    }

    [TestMethod]
    public void ThrowingCallbackDoesNotPreventLaterPostedWork()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("errors");
        var completed = false;
        scope.Post(() => throw new IOException("callback failed"));
        scope.Post(() => completed = true);
        Assert.Throws<AggregateException>(() => graph.Drain());
        Assert.IsTrue(completed);
        graph.Drain();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference QueuePayload(ReactiveScope scope)
    {
        var payload = new object();
        scope.Post(() => GC.KeepAlive(payload));
        return new WeakReference(payload);
    }
}
