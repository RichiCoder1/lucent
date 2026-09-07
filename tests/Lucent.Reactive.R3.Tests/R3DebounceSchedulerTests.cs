using System.Runtime.CompilerServices;
using Lucent.Core;
using Lucent.Reactive.R3;
using Microsoft.Extensions.Time.Testing;

namespace Lucent.Reactive.R3.Tests;

[TestClass]
public sealed class OwnedDebouncedActionTests
{
    [TestMethod]
    public void EmitsLatestCallbackAfterQuietPeriodOnOwnerGraph()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("debounce");
        var clock = new FakeTimeProvider();
        using var scheduler = new OwnedDebouncedAction(scope, clock);
        var calls = new List<int>();

        scheduler.Restart(TimeSpan.FromSeconds(1), () => calls.Add(1));
        clock.Advance(TimeSpan.FromMilliseconds(500));
        scheduler.Restart(TimeSpan.FromSeconds(1), () => calls.Add(2));
        clock.Advance(TimeSpan.FromMilliseconds(999));
        graph.Drain();
        Assert.IsEmpty(calls);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsEmpty(calls);
        graph.Drain();
        Assert.AreEqual(1, calls.Count);
        Assert.AreEqual(2, calls[0]);
    }

    [TestMethod]
    public void CancelSuppressesAlreadyQueuedCallback()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("debounce");
        var clock = new FakeTimeProvider();
        using var scheduler = new OwnedDebouncedAction(scope, clock);
        var calls = 0;

        scheduler.Restart(TimeSpan.FromSeconds(1), () => calls++);
        clock.Advance(TimeSpan.FromSeconds(1));
        scheduler.Cancel();
        graph.Drain();

        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public void RestartSuppressesCallbackAlreadyQueuedByPreviousGeneration()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("debounce");
        var clock = new FakeTimeProvider();
        using var scheduler = new OwnedDebouncedAction(scope, clock);
        var calls = new List<int>();

        scheduler.Restart(TimeSpan.FromSeconds(1), () => calls.Add(1));
        clock.Advance(TimeSpan.FromSeconds(1));
        scheduler.Restart(TimeSpan.FromSeconds(1), () => calls.Add(2));
        graph.Drain();
        Assert.IsEmpty(calls);

        clock.Advance(TimeSpan.FromSeconds(1));
        graph.Drain();
        Assert.AreEqual(1, calls.Count);
        Assert.AreEqual(2, calls[0]);
    }

    [TestMethod]
    public void ScopeDisposalSuppressesPendingCallback()
    {
        var graph = new ReactiveGraph();
        var scope = graph.CreateScope("debounce");
        var clock = new FakeTimeProvider();
        var scheduler = new OwnedDebouncedAction(scope, clock);
        var calls = 0;

        scheduler.Restart(TimeSpan.FromSeconds(1), () => calls++);
        clock.Advance(TimeSpan.FromSeconds(1));
        scope.Dispose();
        graph.Drain();

        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public void WorkerRestartDeliversCallbackOnOwnerThread()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("debounce");
        var clock = new FakeTimeProvider();
        using var scheduler = new OwnedDebouncedAction(scope, clock);
        var ownerThread = Environment.CurrentManagedThreadId;
        var callbackThread = 0;

        Task.Run(() =>
            {
                scheduler.Restart(
                    TimeSpan.FromSeconds(1),
                    () => callbackThread = Environment.CurrentManagedThreadId
                );
                clock.Advance(TimeSpan.FromSeconds(1));
            })
            .GetAwaiter()
            .GetResult();

        graph.Drain();
        Assert.AreEqual(ownerThread, callbackThread);
    }

    [TestMethod]
    public void CancelReleasesQueuedCallbackClosure()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("debounce");
        var clock = new FakeTimeProvider();
        using var scheduler = new OwnedDebouncedAction(scope, clock);
        var weak = QueuePayload(scheduler, clock);

        scheduler.Cancel();
        ForceCollection();

        Assert.IsFalse(weak.IsAlive);
    }

    [TestMethod]
    public void CallbackErrorsSurfaceDuringGraphDrain()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("debounce");
        var clock = new FakeTimeProvider();
        using var scheduler = new OwnedDebouncedAction(scope, clock);

        scheduler.Restart(
            TimeSpan.FromSeconds(1),
            static () => throw new InvalidOperationException("debounced failure")
        );
        clock.Advance(TimeSpan.FromSeconds(1));

        var error = Assert.Throws<AggregateException>(() => graph.Drain());
        Assert.IsTrue(
            error
                .Flatten()
                .InnerExceptions.Any(exception => exception.Message == "debounced failure")
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference QueuePayload(
        OwnedDebouncedAction scheduler,
        FakeTimeProvider clock
    )
    {
        var payload = new CallbackPayload();
        var weak = new WeakReference(payload);
        scheduler.Restart(TimeSpan.FromSeconds(1), () => GC.KeepAlive(payload.Value));
        clock.Advance(TimeSpan.FromSeconds(1));
        return weak;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class CallbackPayload
    {
        private readonly int _value = 1;

        public int Value => _value;
    }
}
