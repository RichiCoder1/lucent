using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ReactiveDrainBoundaryContracts
{
    [TestMethod]
    public void DefaultApplicationEventDrainIncludesSelfPostedCallbacksInItsBudget()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "bounded-events");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var lifecycle = new CapturingContextLifecycle();
        var session = new ApplicationSession(
            "Bounded events",
            composition,
            theme,
            lifecycle,
            static _ => { },
            static report => report()
        );
        session.Start();
        session.ProcessEvents();
        var context = lifecycle.Context!;
        var callbacks = 0;
        SendOrPostCallback? callback = null;
        callback = _ =>
        {
            callbacks++;
            if (callbacks < 1_030)
                context.Post(callback!, null);
        };
        context.Post(callback, null);

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
            session.ProcessEvents()
        );

        StringAssert.Contains(failure.Message, "1024");
        Assert.AreEqual(1_024, callbacks);
        Assert.IsTrue(session.ProcessEvents(16));
        Assert.AreEqual(1_030, callbacks);
        session.Abort(null);
        while (!session.IsCompleted)
            session.ProcessEvents(16);
    }

    [TestMethod]
    public void DefaultDrainBoundsWriteThenThrowWithoutDiscardingOriginalFailure()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("bounded-write-then-throw");
        var value = scope.Signal(0, "value");
        _ = scope.Effect(
            () =>
            {
                var current = value.Value;
                if (current < 10_000)
                    value.Value = current + 1;
                throw new InvalidOperationException("write-then-throw");
            },
            "write-then-throw"
        );

        var error = Assert.ThrowsExactly<AggregateException>(graph.Drain);
        var failures = Flatten(error).ToArray();

        Assert.IsTrue(failures.Any(failure => failure.Message == "write-then-throw"));
        Assert.IsTrue(
            failures.Any(failure => failure.Message.Contains("10000", StringComparison.Ordinal))
        );
        Assert.IsTrue(failures.Length <= 66, "The bounded drain retained an unbounded error list.");
    }

    [TestMethod]
    public void HealthyWorkloadImmediatelyBelowDefaultDrainLimitSettles()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("healthy-near-limit");
        var value = scope.Signal(0, "value");
        var runs = 0;
        _ = scope.Effect(
            () =>
            {
                var current = value.Value;
                runs++;
                if (current < 9_998)
                    value.Value = current + 1;
            },
            "healthy-near-limit"
        );

        graph.Drain();

        Assert.AreEqual(9_999, runs);
        Assert.AreEqual(9_998, value.Value);
    }

    [TestMethod]
    public void ActiveDrainDefersNestedBatchAndCompositionFlush()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "nested-drain-order");
        var trigger = composition.Root.Scope.Signal(false, "trigger");
        var dependent = composition.Root.Scope.Signal(0, "dependent");
        var order = new List<string>();
        _ = composition.Root.Scope.Effect(
            () =>
            {
                if (dependent.Value != 0)
                    order.Add("dependent");
            },
            "dependent"
        );
        _ = composition.Root.Scope.Effect(
            () =>
            {
                if (!trigger.Value)
                    return;
                order.Add("outer-start");
                graph.Batch(() => dependent.Value = 1);
                Assert.IsFalse(composition.Flush());
                order.Add("outer-end");
            },
            "outer"
        );
        graph.Drain();
        order.Clear();

        trigger.Value = true;
        graph.Drain();

        Assert.AreSequenceEqual(["outer-start", "outer-end", "dependent"], order);
    }

    private static IEnumerable<Exception> Flatten(Exception error)
    {
        if (error is not AggregateException aggregate)
        {
            yield return error;
            yield break;
        }
        foreach (var inner in aggregate.InnerExceptions)
        foreach (var nested in Flatten(inner))
            yield return nested;
    }

    private sealed class CapturingContextLifecycle : IApplicationLifecycle
    {
        public SynchronizationContext? Context { get; private set; }

        public ValueTask<ComponentRecipe> StartAsync(ApplicationSession session)
        {
            Context = SynchronizationContext.Current;
            return ValueTask.FromResult(
                ComponentRecipe.Create("bounded-event-root", static (_, _) => { })
            );
        }

        public ValueTask<bool> PrepareCloseAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask StopAsync() => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
