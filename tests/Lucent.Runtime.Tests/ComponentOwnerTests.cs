using Lucent.Runtime;

namespace Lucent.Runtime.Tests;

[TestClass]
public sealed class ComponentOwnerTests
{
    [TestMethod]
    public void Cleanup_is_lifo_and_children_are_owned()
    {
        var order = new List<string>();
        using var owner = new ComponentOwner(new TestUiDispatcher());
        owner.OnDispose(() => order.Add("parent-first"));
        var child = owner.CreateChild();
        child.OnDispose(() => order.Add("child"));
        owner.OnDispose(() => order.Add("parent-last"));

        owner.Dispose();

        CollectionAssert.AreEqual(
            new[] { "parent-last", "child", "parent-first" },
            order);
        Assert.IsTrue(child.IsDisposed);
    }

    [TestMethod]
    public void Explicit_child_disposal_and_parent_disposal_are_idempotent()
    {
        var calls = 0;
        using var owner = new ComponentOwner(new TestUiDispatcher());
        var child = owner.CreateChild();
        child.OnDispose(() => calls++);

        child.Dispose();
        owner.Dispose();
        owner.Dispose();

        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void Registration_after_disposal_is_rejected()
    {
        var owner = new ComponentOwner(new TestUiDispatcher());
        owner.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.OnDispose(() => { }));
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.CreateChild());
    }

    [TestMethod]
    public void Queued_and_late_dispatches_are_ignored_after_disposal()
    {
        var dispatcher = new TestUiDispatcher();
        var owner = new ComponentOwner(dispatcher);
        var calls = 0;
        owner.Dispatch(() => calls++);

        owner.Dispose();
        owner.Dispatch(() => calls++);
        dispatcher.DrainAll();

        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public void Disposal_is_exhaustive_and_aggregates_cancellation_and_cleanup_failures()
    {
        var owner = new ComponentOwner(new TestUiDispatcher());
        var cleanupRan = false;
        using var registration = owner.CancellationToken.Register(
            () => throw new InvalidOperationException("cancellation"));
        owner.OnDispose(() => throw new ArgumentException("cleanup"));
        owner.OnDispose(() => cleanupRan = true);

        var failure = Assert.ThrowsExactly<AggregateException>(owner.Dispose);

        Assert.IsTrue(cleanupRan);
        Assert.HasCount(2, failure.InnerExceptions);
        Assert.IsTrue(failure.InnerExceptions.Any(exception => exception.Message == "cancellation"));
        Assert.IsTrue(failure.InnerExceptions.Any(exception => exception.Message == "cleanup"));
        Assert.IsTrue(owner.IsDisposed);
        Assert.IsTrue(owner.CancellationToken.IsCancellationRequested);
    }

    [TestMethod]
    public void Concurrent_queue_and_dispose_never_runs_a_drained_callback()
    {
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var dispatcher = new TestUiDispatcher();
            var owner = new ComponentOwner(dispatcher);
            var calls = 0;

            Parallel.Invoke(
                () => owner.Dispatch(() => Interlocked.Increment(ref calls)),
                owner.Dispose);
            owner.Dispose();
            dispatcher.DrainAll();

            Assert.AreEqual(0, calls);
        }
    }

    [TestMethod]
    public void Unhandled_reporting_is_inherited_and_suppressed_after_disposal()
    {
        var errors = new List<Exception>();
        var owner = new ComponentOwner(new TestUiDispatcher(), errors.Add);
        var child = owner.CreateChild();
        var error = new InvalidOperationException("boom");

        child.ReportUnhandled(error);
        owner.Dispose();
        child.ReportUnhandled(new InvalidOperationException("late"));

        Assert.AreSame(error, errors.Single());
    }

    [TestMethod]
    public void Missing_reporter_rethrows_and_null_is_rejected()
    {
        using var owner = new ComponentOwner(new TestUiDispatcher());
        Assert.ThrowsExactly<ArgumentNullException>(() => owner.ReportUnhandled(null!));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            owner.ReportUnhandled(new InvalidOperationException("boom")));
    }
}
