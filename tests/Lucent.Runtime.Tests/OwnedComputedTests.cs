using Lucent.Runtime;

namespace Lucent.Runtime.Tests;

[TestClass]
public sealed class OwnedComputedTests
{
    [TestMethod]
    public async Task Replacement_suppresses_old_generation_and_keeps_new_work_running()
    {
        var dispatcher = new TestUiDispatcher();
        using var owner = new ComponentOwner(dispatcher);
        var first = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var computed = new OwnedComputed<int>(owner, 0, _ => ++calls == 1 ? first.Task : second.Task, () => { });

        computed.Refresh();
        computed.Refresh();
        first.SetResult(1);
        second.SetResult(2);
        await Task.Yield();
        await Task.Yield();
        dispatcher.DrainAll();

        Assert.AreEqual(2, computed.Value);
        Assert.IsFalse(computed.IsPending);
    }

    [TestMethod]
    public async Task Synchronous_throw_null_factory_and_foreign_cancellation_are_failures()
    {
        var dispatcher = new TestUiDispatcher();
        using var owner = new ComponentOwner(dispatcher);
        var errors = new List<Exception>();
        using var sync = new OwnedComputed<int>(owner, 0, _ => throw new InvalidOperationException("sync"), () => { }, errors.Add);
        using var nil = new OwnedComputed<int>(owner, 0, _ => null!, () => { }, errors.Add);
        using var foreign = new OwnedComputed<int>(owner, 0, _ => Task.FromException<int>(new OperationCanceledException("foreign")), () => { }, errors.Add);

        sync.Refresh(); nil.Refresh(); foreign.Refresh();
        await Task.Yield();
        dispatcher.DrainAll();

        Assert.AreEqual(3, errors.Count);
        Assert.IsFalse(sync.IsPending);
        Assert.IsFalse(nil.IsPending);
        Assert.IsFalse(foreign.IsPending);
        Assert.AreEqual("foreign", foreign.ErrorMessage);
    }

    [TestMethod]
    public void Refresh_after_dispose_is_idempotent_and_suppresses_late_work()
    {
        var dispatcher = new TestUiDispatcher();
        using var owner = new ComponentOwner(dispatcher);
        var starts = 0;
        var computed = new OwnedComputed<int>(owner, 0, _ => { starts++; return Task.FromResult(1); }, () => { });
        computed.Dispose();
        computed.Refresh();
        computed.Dispose();

        Assert.AreEqual(0, starts);
        Assert.IsFalse(computed.IsPending);
    }

    [TestMethod]
    public void Invalidation_failure_uses_root_reporter_once()
    {
        var dispatcher = new TestUiDispatcher();
        var errors = new List<Exception>();
        var invalidations = 0;
        using var owner = new ComponentOwner(dispatcher, errors.Add);
        using var computed = new OwnedComputed<int>(owner, 0, _ => Task.FromResult(1), () =>
        {
            if (Interlocked.Increment(ref invalidations) == 1)
                throw new InvalidOperationException("update");
        });

        computed.Refresh();
        dispatcher.DrainAll();

        Assert.AreEqual(1, errors.Count);
        Assert.AreEqual("update", errors[0].Message);
    }
    [TestMethod]
    public async Task Refresh_commits_and_keeps_stale_value_during_replacement()
    {
        var dispatcher = new TestUiDispatcher();
        using var owner = new ComponentOwner(dispatcher);
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invalidations = 0;
        using var computed = new OwnedComputed<int>(owner, 0, _ => pending.Task, () => invalidations++);

        computed.Refresh();
        Assert.IsTrue(computed.IsPending);
        pending.SetResult(42);
        await Task.Yield();
        dispatcher.DrainAll();

        Assert.AreEqual(42, computed.Value);
        Assert.IsTrue(computed.HasCommittedValue);
        Assert.IsFalse(computed.IsPending);
        Assert.AreEqual(2, invalidations);
    }

    [TestMethod]
    public async Task Failure_is_inspectable_and_reported_once()
    {
        var dispatcher = new TestUiDispatcher();
        var errors = new List<Exception>();
        using var owner = new ComponentOwner(dispatcher);
        using var computed = new OwnedComputed<int>(owner, 7,
            _ => Task.FromException<int>(new InvalidOperationException("failed")),
            () => { }, errors.Add);

        computed.Refresh();
        await Task.Yield();
        dispatcher.DrainAll();

        Assert.IsFalse(computed.IsPending);
        Assert.AreEqual("failed", computed.ErrorMessage);
        Assert.AreEqual(1, errors.Count);
        Assert.AreEqual(7, computed.Value);
    }

    [TestMethod]
    public async Task Failure_without_reporter_propagates_from_ui_commit()
    {
        var dispatcher = new TestUiDispatcher();
        using var owner = new ComponentOwner(dispatcher);
        using var computed = new OwnedComputed<int>(owner, 0,
            _ => Task.FromException<int>(new InvalidOperationException("unhandled")),
            () => { });

        computed.Refresh();
        await Task.Yield();

        var error = Assert.ThrowsExactly<InvalidOperationException>(dispatcher.DrainAll);
        Assert.AreEqual("unhandled", error.Message);
        Assert.AreEqual("unhandled", computed.ErrorMessage);
        Assert.IsFalse(computed.IsPending);
    }

    [TestMethod]
    public async Task Disposal_suppresses_late_completion()
    {
        var dispatcher = new TestUiDispatcher();
        using var owner = new ComponentOwner(dispatcher);
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invalidations = 0;
        using var computed = new OwnedComputed<int>(owner, 0, _ => pending.Task, () => invalidations++);

        computed.Refresh();
        computed.Dispose();
        pending.SetResult(9);
        await Task.Yield();
        dispatcher.DrainAll();

        Assert.AreEqual(0, computed.Value);
        Assert.AreEqual(1, invalidations);
    }
}
