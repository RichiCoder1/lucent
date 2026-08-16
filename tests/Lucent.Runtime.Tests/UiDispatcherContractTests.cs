using Lucent.Runtime;

namespace Lucent.Runtime.Tests;

[TestClass]
public sealed class UiDispatcherContractTests
{
    [TestMethod]
    public void Runtime_exports_only_the_planned_types()
    {
        CollectionAssert.AreEquivalent(
            new[] { typeof(IUiDispatcher), typeof(AvaloniaUiDispatcher), typeof(ComponentOwner) },
            typeof(ComponentOwner).Assembly.GetExportedTypes());
    }

    [TestMethod]
    public void Test_dispatcher_preserves_fifo_order()
    {
        var dispatcher = new TestUiDispatcher();
        var calls = new List<int>();
        dispatcher.Dispatch(() => calls.Add(1));
        dispatcher.Dispatch(() => calls.Add(2));

        dispatcher.DrainOne();
        CollectionAssert.AreEqual(new[] { 1 }, calls);
        dispatcher.DrainAll();
        CollectionAssert.AreEqual(new[] { 1, 2 }, calls);
    }

    [TestMethod]
    public void Dispatchers_reject_null_actions()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new TestUiDispatcher().Dispatch(null!));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => AvaloniaUiDispatcher.Instance.Dispatch(null!));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new ComponentOwner(null!));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new ComponentOwner(new TestUiDispatcher()).Dispatch(null!));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new ComponentOwner(new TestUiDispatcher()).OnDispose(null!));
    }
}

internal sealed class TestUiDispatcher : IUiDispatcher
{
    private readonly Queue<Action> _actions = new();

    public void Dispatch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_actions)
        {
            _actions.Enqueue(action);
        }
    }

    public void DrainOne()
    {
        Action action;
        lock (_actions)
        {
            action = _actions.Dequeue();
        }

        action();
    }

    public void DrainAll()
    {
        while (true)
        {
            lock (_actions)
            {
                if (_actions.Count == 0)
                {
                    return;
                }
            }

            DrainOne();
        }
    }
}
