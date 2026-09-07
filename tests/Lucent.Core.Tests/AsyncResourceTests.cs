using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class AsyncResourceTests
{
    [TestMethod]
    public void ExplicitSourceTracksOnlySourceAndCommitsLatestOnOwner()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("resource");
        var key = scope.Signal(1, "key");
        var incidental = scope.Signal(10, "incidental");
        var loads =
            new List<(int Key, TaskCompletionSource<string> Work, CancellationToken Token)>();
        var value = scope.Async(
            () => key.Value,
            (input, token) =>
            {
                _ = incidental.Value;
                var work = new TaskCompletionSource<string>();
                loads.Add((input, work, token));
                return work.Task;
            },
            "previous",
            "result"
        );
        Assert.AreEqual(0, loads.Count, "Recipe setup must not start a load.");
        Assert.AreEqual("previous", value.Value);
        incidental.Value++;
        Assert.IsTrue(value.IsPending);
        Assert.AreEqual(1, loads.Count, "Fetcher reads must not become dependencies.");
        key.Value = 2;
        Assert.IsTrue(loads[0].Token.IsCancellationRequested);
        Assert.IsTrue(value.IsPending);
        Assert.AreEqual(2, loads[1].Key);
        Task.Run(() => loads[0].Work.SetResult("obsolete")).GetAwaiter().GetResult();
        graph.Drain();
        Assert.AreEqual("previous", value.Value);
        Task.Run(() => loads[1].Work.SetResult("current")).GetAwaiter().GetResult();
        Assert.AreEqual("previous", value.Value, "Worker completion must wait for owner drain.");
        graph.Drain();
        Assert.AreEqual("current", value.Value);
        Assert.IsFalse(value.IsPending);
    }

    [TestMethod]
    public void RefreshRetriesSameSourceAndDisposalRejectsLateResults()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("retry");
        var loads = new List<TaskCompletionSource<int>>();
        var tokens = new List<CancellationToken>();
        var value = scope.Async(
            () => "same-key",
            (key, token) =>
            {
                Assert.AreEqual("same-key", key);
                var work = new TaskCompletionSource<int>();
                loads.Add(work);
                tokens.Add(token);
                return work.Task;
            },
            "result"
        );
        Assert.IsTrue(value.IsPending);
        Assert.IsFalse(value.HasValue);
        loads[0].SetException(new IOException("try again"));
        graph.Drain();
        Assert.IsInstanceOfType<IOException>(value.Error);
        value.Refresh();
        Assert.AreEqual(1, loads.Count, "Refresh must remain lazy.");
        Assert.IsTrue(value.IsPending);
        Assert.IsNull(value.Error);
        loads[1].SetResult(42);
        graph.Drain();
        Assert.AreEqual(42, value.Value);
        value.Refresh();
        Assert.IsTrue(value.IsPending);
        Assert.AreEqual(42, value.Value, "Refresh must retain successful data.");
        scope.Dispose();
        Assert.IsTrue(tokens[2].IsCancellationRequested);
        loads[2].SetResult(99);
        graph.Drain();
        Assert.Throws<ObjectDisposedException>(value.Refresh);
    }

    [TestMethod]
    public void RefreshFromWorkerIsRejected()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("thread");
        var value = scope.Async(() => 1, (key, _) => Task.FromResult(key), "result");
        Exception? error = null;
        var worker = new Thread(() =>
        {
            try
            {
                value.Refresh();
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        worker.Start();
        worker.Join();
        Assert.IsInstanceOfType<InvalidOperationException>(error);
    }
}
