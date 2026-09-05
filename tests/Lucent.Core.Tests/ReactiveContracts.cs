using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ReactiveContracts
{
    [TestMethod]
    public void BranchesBatchesAndReentrancy()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("branches");
        var selectLeft = scope.Signal(true, "select-left");
        var left = scope.Signal(1, "left");
        var right = scope.Signal(10, "right");
        var unrelated = scope.Signal(0, "unrelated");
        var selected = scope.Derived(() => selectLeft.Value ? left.Value : right.Value, "selected");
        var runs = 0;
        _ = scope.Effect(
            () =>
            {
                _ = selected.Value;
                runs++;
            },
            "selected-effect"
        );
        graph.Drain();
        unrelated.Value++;
        right.Value++;
        graph.Drain();
        Assert(runs == 1, "Unrelated write scheduled work.");
        selectLeft.Value = false;
        graph.Drain();
        left.Value++;
        graph.Drain();
        Assert(runs == 2, "Branch replacement retained old dependency.");
        right.Value++;
        graph.Drain();
        Assert(runs == 3, "Branch replacement lost active dependency.");

        var order = new List<string>();
        var first = scope.Signal(0, "first");
        var second = scope.Signal(0, "second");
        _ = scope.Effect(() => order.Add("first:" + first.Value), "first-effect");
        _ = scope.Effect(() => order.Add("second:" + second.Value), "second-effect");
        graph.Drain();
        order.Clear();
        graph.Batch(() =>
        {
            second.Value = 1;
            graph.Batch(() =>
            {
                first.Value = 1;
                graph.Drain();
                first.Value = 2;
            });
            second.Value = 2;
        });
        Assert(
            order.SequenceEqual(["second:2", "first:2"]),
            "Nested batch did not flush once deterministically."
        );

        var loop = scope.Signal(0, "loop");
        var loopRuns = 0;
        _ = scope.Effect(
            () =>
            {
                var value = loop.Value;
                loopRuns++;
                if (value < 2)
                    loop.Value = value + 1;
            },
            "convergent"
        );
        graph.Drain();
        Assert(loopRuns == 3 && loop.Value == 2, "First-run reentrant write was not rescheduled.");
        loop.Value = 0;
        graph.Drain();
        Assert(loopRuns == 6 && loop.Value == 2, "Later reentrant write was not rescheduled.");

        var batch = scope.Signal(0, "batch");
        _ = scope.Effect(
            () =>
            {
                if (batch.Value == 1)
                    throw new InvalidOperationException("drain");
            },
            "batch-effect"
        );
        graph.Drain();
        try
        {
            graph.Batch(() =>
            {
                batch.Value = 1;
                throw new ArgumentException("body");
            });
            throw new InvalidOperationException("Expected batch failure.");
        }
        catch (AggregateException exception)
        {
            Assert(
                exception.InnerExceptions.Any(error => error is ArgumentException)
                    && exception.InnerExceptions.Any(error => error is AggregateException),
                "Batch masked a body or drain failure."
            );
        }
        batch.Value = 2;
        graph.Drain();
    }

    [TestMethod]
    public void FailureRecoveryAndOwnership()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("failures");
        var guard = scope.Signal(true, "self-guard");
        Derived<int>? self = null;
        self = scope.Derived(() => guard.Value ? self!.Value : 7, "self");
        var recovered = 0;
        _ = scope.Effect(
            () =>
            {
                _ = self.Value;
                recovered++;
            },
            "self-consumer"
        );
        ExpectCycle(graph.Drain, "self", "self");
        guard.Value = false;
        graph.Drain();
        Assert(recovered == 1, "Self-cycle consumer did not recover without a manual pull.");

        var mutualGuard = scope.Signal(true, "mutual-guard");
        Derived<int>? first = null;
        Derived<int>? second = null;
        first = scope.Derived(() => mutualGuard.Value ? second!.Value : 3, "first");
        second = scope.Derived(() => first!.Value, "second");
        var mutualRecovered = 0;
        _ = scope.Effect(
            () =>
            {
                _ = first.Value;
                mutualRecovered++;
            },
            "mutual-consumer"
        );
        ExpectCycle(graph.Drain, "first", "second", "first");
        mutualGuard.Value = false;
        graph.Drain();
        Assert(
            mutualRecovered == 1,
            "Mutual-cycle consumer did not recover without a manual pull."
        );

        var prior = scope.Signal(1, "prior");
        var observed = scope.Signal(1, "observed");
        var fail = scope.Signal(false, "fail");
        var flaky = scope.Derived(
            () =>
            {
                if (fail.Value)
                {
                    _ = observed.Value;
                    throw new InvalidOperationException("expected");
                }
                return prior.Value;
            },
            "flaky"
        );
        var flakyRuns = 0;
        _ = scope.Effect(
            () =>
            {
                _ = flaky.Value;
                flakyRuns++;
            },
            "flaky-consumer"
        );
        graph.Drain();
        fail.Value = true;
        ExpectAggregate(graph.Drain);
        prior.Value++;
        ExpectAggregate(graph.Drain);
        observed.Value++;
        ExpectAggregate(graph.Drain);
        fail.Value = false;
        graph.Drain();
        var afterSuccess = flakyRuns;
        observed.Value++;
        graph.Drain();
        Assert(
            flakyRuns == afterSuccess,
            "Successful callback did not replace failed dependencies exactly."
        );

        var effectPrior = scope.Signal(1, "effect-prior");
        var effectObserved = scope.Signal(1, "effect-observed");
        var effectFail = scope.Signal(false, "effect-fail");
        var effectRuns = 0;
        _ = scope.Effect(
            () =>
            {
                effectRuns++;
                _ = effectPrior.Value;
                if (effectFail.Value)
                {
                    _ = effectObserved.Value;
                    throw new InvalidOperationException("expected effect failure");
                }
            },
            "flaky-effect"
        );
        graph.Drain();
        effectFail.Value = true;
        ExpectAggregate(graph.Drain);
        effectPrior.Value++;
        ExpectAggregate(graph.Drain);
        effectObserved.Value++;
        ExpectAggregate(graph.Drain);
        effectFail.Value = false;
        graph.Drain();
        var effectAfterSuccess = effectRuns;
        effectObserved.Value++;
        graph.Drain();
        Assert(
            effectRuns == effectAfterSuccess,
            "Successful effect did not replace failed dependencies exactly."
        );

        ReactiveEffect? victim = null;
        var dispose = scope.Signal(0, "dispose");
        var victimSource = scope.Signal(0, "victim-source");
        var victimRuns = 0;
        _ = scope.Effect(
            () =>
            {
                if (dispose.Value != 0)
                    victim!.Dispose();
            },
            "disposer"
        );
        victim = scope.Effect(
            () =>
            {
                _ = victimSource.Value;
                victimRuns++;
            },
            "victim"
        );
        graph.Drain();
        graph.Batch(() =>
        {
            dispose.Value = 1;
            victimSource.Value = 1;
        });
        Assert(
            victimRuns == 1 && !graph.Dump().Contains("victim\"", StringComparison.Ordinal),
            "Disposed effect remained queued or dumped."
        );

        var child = scope.CreateChild("manual-child");
        var manual = child.Signal(1, "manual-node");
        manual.Dispose();
        child.Dispose();
        Assert(
            !graph.Dump().Contains("manual-", StringComparison.Ordinal),
            "Manual child/node disposal remained active."
        );
        var before = graph.Dump();
        ExpectArgument(() => graph.Derived<int>(null!, "ghost-derived"));
        ExpectArgument(() => scope.Effect(null!, "ghost-effect"));
        ExpectArgument(() => graph.Async<int>(null!, "ghost-async"));
        Assert(graph.Dump() == before, "Invalid callback registered a ghost node.");
    }

    [TestMethod]
    public void AsyncOwnershipAndThreading()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("async");
        var source = scope.Signal(1, "source");
        var work = new List<TaskCompletionSource<int>>();
        var cancelled = 0;
        var value = scope.Async(
            token =>
            {
                _ = source.Value;
                token.Register(() => cancelled++);
                var next = new TaskCompletionSource<int>();
                work.Add(next);
                return next.Task;
            },
            10,
            "latest"
        );
        Assert(value.Value == 10 && value.IsPending, "Async stale/pending state failed.");
        source.Value = 2;
        Assert(value.IsPending && cancelled == 1, "Async dependency cancellation failed.");
        CompleteOnWorker(work[0], 1);
        graph.Drain();
        Assert(
            value.Value == 10 && value.IsPending,
            "Stale cancellation-ignoring result committed."
        );
        CompleteOnWorker(work[1], 20);
        graph.Drain();
        Assert(value.Value == 20 && !value.IsPending, "Latest result did not commit.");
        source.Value = 3;
        _ = value.IsPending;
        source.Value = 4;
        _ = value.IsPending;
        CompleteOnWorker(work[2], 30);
        graph.Drain();
        Assert(value.Value == 20 && value.IsPending, "Late cancelled generation committed.");
        CompleteOnWorker(work[3], 40);
        graph.Drain();
        Assert(value.Value == 40 && !value.IsPending, "Replacement generation failed.");

        var sync = scope.Async<int>(_ => throw new InvalidOperationException("secret"), "sync");
        _ = sync.Value;
        Assert(
            sync.Error is InvalidOperationException && !sync.IsPending,
            "Sync loader failure remained pending."
        );
        var nullTask = scope.Async<int>(_ => null!, "null-task");
        _ = nullTask.Value;
        Assert(
            nullTask.Error is InvalidOperationException && !nullTask.IsPending,
            "Null task remained pending."
        );
        var canceledWork = new TaskCompletionSource<int>();
        var canceled = scope.Async(_ => canceledWork.Task, 6, "cancelled");
        _ = canceled.Value;
        Task.Run(canceledWork.SetCanceled).GetAwaiter().GetResult();
        graph.Drain();
        Assert(
            canceled.IsCancelled && !canceled.IsPending && canceled.Value == 6,
            "Current cancellation lost stale state."
        );

        var wrongGraph = new ReactiveGraph();
        var wrongWork = new TaskCompletionSource<int>();
        var wrong = wrongGraph.Async(_ => wrongWork.Task, 0, "wrong-thread");
        _ = wrong.Value;
        var rejected = false;
        Exception? workerFailure = null;
        var worker = new Thread(() =>
        {
            try
            {
                wrongWork.SetResult(9);
                try
                {
                    wrongGraph.Drain();
                }
                catch (InvalidOperationException)
                {
                    rejected = true;
                }
            }
            catch (Exception error)
            {
                workerFailure = error;
            }
        });
        worker.Start();
        worker.Join();
        if (workerFailure is not null)
            throw new InvalidOperationException("Wrong-thread proof worker failed.", workerFailure);
        Assert(
            rejected && wrong.Value == 0 && wrong.IsPending,
            "Wrong-thread commit was accepted."
        );
        wrongGraph.Drain();
        Assert(
            wrong.Value == 9 && !wrong.IsPending,
            "UI-thread recovery after rejected commit failed."
        );

        var throwing = scope.CreateChild("throwing-scope");
        var never = new TaskCompletionSource<int>();
        _ = throwing
            .Async(
                token =>
                {
                    token.Register(() => throw new InvalidOperationException("cancel"));
                    return never.Task;
                },
                "throwing-load"
            )
            .Value;
        var cleanupRuns = 0;
        throwing.OnDispose(() =>
        {
            cleanupRuns++;
            throw new InvalidOperationException("cleanup");
        });
        var cleanupErrors = CaptureAggregate(throwing.Dispose);
        Assert(
            Flatten(cleanupErrors).Any(error => error.Message == "cancel")
                && Flatten(cleanupErrors).Any(error => error.Message == "cleanup"),
            "Throwing cancellation/cleanup did not preserve both failures."
        );
        Assert(
            cleanupRuns == 1 && !graph.Dump().Contains("throwing-", StringComparison.Ordinal),
            "Throwing cancellation/cleanup broke active graph cleanup."
        );

        var fanoutSource = scope.Signal(0, "fanout-source");
        var fanoutWork = new TaskCompletionSource<int>();
        var fanoutAsync = scope.Async(
            token =>
            {
                _ = fanoutSource.Value;
                token.Register(() => throw new InvalidOperationException("fanout-cancel"));
                return fanoutWork.Task;
            },
            "fanout-async"
        );
        _ = fanoutAsync.Value;
        var fanoutRuns = 0;
        _ = scope.Effect(
            () =>
            {
                _ = fanoutSource.Value;
                fanoutRuns++;
            },
            "fanout-sibling"
        );
        graph.Drain();
        var fanoutErrors = CaptureAggregate(() => fanoutSource.Value = 1);
        Assert(
            Flatten(fanoutErrors).Any(error => error.Message == "fanout-cancel") && fanoutRuns == 1,
            "Throwing cancellation did not report while preserving sibling scheduling."
        );
        graph.Drain();
        Assert(fanoutRuns == 2, "Throwing cancellation aborted invalidation fan-out.");

        var reentrantSource = scope.Signal(0, "reentrant-async-source");
        var reentrant = scope.Async(
            token =>
            {
                var current = reentrantSource.Value;
                token.Register(() => throw new InvalidOperationException("reentrant-cancel"));
                if (current == 0)
                    reentrantSource.Value = 1;
                return Task.FromResult(9);
            },
            0,
            "reentrant-async"
        );
        var reentrantErrors = CaptureAggregate(() => _ = reentrant.Value);
        Assert(
            Flatten(reentrantErrors).Any(error => error.Message == "reentrant-cancel")
                && reentrantSource.Value == 1,
            "Reentrant async cancellation failure was swallowed."
        );
        Assert(
            reentrant.Value == 0 && reentrant.IsPending,
            "Reentrant async generation did not restart deterministically."
        );
        graph.Drain();
        Assert(
            reentrant.Value == 9 && !reentrant.IsPending,
            "Reentrant async replacement did not commit."
        );

        var laterSource = scope.Signal(0, "later-reentrant-source");
        var mutateLater = false;
        var later = scope.Async(
            token =>
            {
                var current = laterSource.Value;
                token.Register(() => throw new InvalidOperationException("later-reentrant-cancel"));
                if (mutateLater)
                    laterSource.Value = current + 1;
                return Task.FromResult(current);
            },
            -1,
            "later-reentrant"
        );
        _ = later.Value;
        graph.Drain();
        Assert(
            later.Value == 0 && !later.IsPending,
            "Initial later-reentrant generation did not complete."
        );
        mutateLater = true;
        laterSource.Value = 1;
        var laterErrors = CaptureAggregate(() => _ = later.Value);
        Assert(
            Flatten(laterErrors).Any(error => error.Message == "later-reentrant-cancel")
                && laterSource.Value == 2,
            "Replacement-generation cancellation failure was swallowed."
        );
        mutateLater = false;
        Assert(
            later.Value == 0 && later.IsPending,
            "Invalidated replacement generation did not restart."
        );
        graph.Drain();
        Assert(
            later.Value == 2 && !later.IsPending,
            "Later replacement generation did not commit."
        );

        var faultedWork = new TaskCompletionSource<int>();
        var faulted = scope.Async(_ => faultedWork.Task, 4, "faulted-task");
        _ = faulted.Value;
        Task.Run(() => faultedWork.SetException(new InvalidOperationException("async-fault")))
            .GetAwaiter()
            .GetResult();
        graph.Drain();
        Assert(
            faulted.Error?.Message == "async-fault" && faulted.Value == 4 && !faulted.IsPending,
            "Faulted async task did not retain stale state and expose error."
        );
        var mutationRejected = false;
        workerFailure = null;
        worker = new Thread(() =>
        {
            try
            {
                source.Value = 99;
            }
            catch (InvalidOperationException)
            {
                mutationRejected = true;
            }
            catch (Exception error)
            {
                workerFailure = error;
            }
        });
        worker.Start();
        worker.Join();
        if (workerFailure is not null)
            throw new InvalidOperationException(
                "Wrong-thread mutation worker failed.",
                workerFailure
            );
        Assert(mutationRejected, "Wrong-thread mutation was accepted.");
    }

    [TestMethod]
    public void LifetimeRelease()
    {
        var disposed = DisposedScopePayload();
        var queued = QueuedEffectPayload();
        var posted = PostedPayload();
        var never = NeverLoadPayload();
        ForceGc();
        Assert(
            !disposed.Payload.IsAlive
                && !queued.Payload.IsAlive
                && !posted.Payload.IsAlive
                && !never.Payload.IsAlive,
            "Disposed graph ownership retained a payload."
        );
        GC.KeepAlive(disposed.Root);
        GC.KeepAlive(queued.Root);
        GC.KeepAlive(posted.Root);
        GC.KeepAlive(never.Root);
        GC.KeepAlive(never.Producer);
    }

    [TestMethod]
    public void WorkAvailableIsEdgeTriggered()
    {
        var graph = new ReactiveGraph();
        var first = new TaskCompletionSource<int>();
        var second = new TaskCompletionSource<int>();
        var one = graph.Async(_ => first.Task, 0, "wake-one");
        var two = graph.Async(_ => second.Task, 0, "wake-two");
        _ = one.Value;
        _ = two.Value;
        var wakes = 0;
        graph.WorkAvailable += () => Interlocked.Increment(ref wakes);
        Task.WhenAll(Task.Run(() => first.SetResult(1)), Task.Run(() => second.SetResult(2)))
            .GetAwaiter()
            .GetResult();
        SpinWait.SpinUntil(() => Volatile.Read(ref wakes) != 0, 2_000);
        graph.Drain();
        Assert(
            wakes == 1 && one.Value == 1 && two.Value == 2,
            "Worker burst did not produce one drainable wake."
        );
        var third = new TaskCompletionSource<int>();
        var three = graph.Async(_ => third.Task, 0, "wake-three");
        _ = three.Value;
        Task.Run(() => third.SetResult(3)).GetAwaiter().GetResult();
        SpinWait.SpinUntil(() => Volatile.Read(ref wakes) == 2, 2_000);
        graph.Drain();
        Assert(wakes == 2 && three.Value == 3, "Empty-to-nonempty reset lost a later worker wake.");
    }

    [TestMethod]
    public void WorkAvailableObserverFailurePreservesDeliveryAndCoalescing()
    {
        var graph = new ReactiveGraph();
        var first = new TaskCompletionSource<int>();
        var second = new TaskCompletionSource<int>();
        using var one = graph.Async(_ => first.Task, 0, "wake-one");
        using var two = graph.Async(_ => second.Task, 0, "wake-two");
        _ = one.Value;
        _ = two.Value;
        var failure = new InvalidOperationException("wake-observer");
        Action failing = () => throw failure;
        var wakes = 0;
        graph.WorkAvailable += failing;
        graph.WorkAvailable += () => Interlocked.Increment(ref wakes);
        Task.Run(() =>
            {
                first.SetResult(1);
                second.SetResult(2);
            })
            .GetAwaiter()
            .GetResult();

        var errors = Flatten(CaptureAggregate(graph.Drain)).ToArray();
        Assert(
            wakes == 1 && one.Value == 1 && two.Value == 2 && errors.SequenceEqual([failure]),
            "A failing observer suppressed delivery, burst coalescing, commit, or error reporting."
        );
        graph.Drain();
        graph.WorkAvailable -= failing;
        var third = new TaskCompletionSource<int>();
        using var three = graph.Async(_ => third.Task, 0, "wake-three");
        _ = three.Value;
        Task.Run(() => third.SetResult(3)).GetAwaiter().GetResult();
        graph.Drain();
        Assert(wakes == 2 && three.Value == 3, "An observer failure broke the next wake edge.");
    }

    [TestMethod]
    public void LateWakeObserverFailureRearmsAfterConcurrentDrain()
    {
        var graph = new ReactiveGraph();
        var completion = new TaskCompletionSource<int>();
        using var value = graph.Async(_ => completion.Task, 0, "late-wake-error");
        _ = value.Value;
        using var observerEntered = new ManualResetEventSlim();
        using var releaseObserver = new ManualResetEventSlim();
        var wakes = 0;
        var failures = 0;
        var failure = new InvalidOperationException("late-observer");
        var unsubscribedCalls = 0;
        Action? unsubscribe = null;
        unsubscribe = () =>
        {
            Interlocked.Increment(ref unsubscribedCalls);
            graph.WorkAvailable -= unsubscribe;
        };
        graph.WorkAvailable += unsubscribe;
        graph.WorkAvailable += () => Interlocked.Increment(ref wakes);
        graph.WorkAvailable += () =>
        {
            Interlocked.Increment(ref failures);
            observerEntered.Set();
            if (!releaseObserver.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Owner did not release observer.");
            throw failure;
        };
        var producer = Task.Run(() => completion.SetResult(7));
        try
        {
            Assert(observerEntered.Wait(TimeSpan.FromSeconds(5)), "Observer never ran.");
            graph.Drain();
            Assert(
                wakes == 1 && value.Value == 7,
                "Original work was not drainable during notification."
            );
        }
        finally
        {
            releaseObserver.Set();
            producer.GetAwaiter().GetResult();
        }
        Assert(
            wakes == 2 && failures == 1 && unsubscribedCalls == 1,
            "Late failure was stranded or retried a failed or unsubscribed observer."
        );
        Assert(
            Flatten(CaptureAggregate(graph.Drain)).Single() == failure,
            "Late observer failure was not reported on the owner."
        );
        graph.Drain();
    }

    [TestMethod]
    public void AllWakeObserversMayFailWithoutRecursiveNotification()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("failed-wake-scope");
        var completion = new TaskCompletionSource<int>();
        var value = scope.Async(_ => completion.Task, 0, "disposed-wake-value");
        _ = value.Value;
        var one = new InvalidOperationException("first-observer");
        var two = new InvalidOperationException("second-observer");
        var calls = 0;
        graph.WorkAvailable += () =>
        {
            Interlocked.Increment(ref calls);
            throw one;
        };
        graph.WorkAvailable += () =>
        {
            Interlocked.Increment(ref calls);
            throw two;
        };
        Task.Run(() => completion.SetResult(1)).GetAwaiter().GetResult();
        scope.Dispose();
        Assert(
            calls == 2 && Flatten(CaptureAggregate(graph.Drain)).SequenceEqual([one, two]),
            "Observer failures escaped posting, recursed, or were lost during scope disposal."
        );
        graph.Drain();
        Assert(
            !graph.Dump().Contains("disposed-wake-value", StringComparison.Ordinal),
            "Disposed async work remained active."
        );
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    private static LifetimeProbe DisposedScopePayload()
    {
        var graph = new ReactiveGraph();
        var scope = graph.CreateScope("released-scope");
        var payload = new Payload();
        var weak = new WeakReference(payload);
        _ = scope.Derived(() => payload, "released-node");
        scope.Dispose();
        Assert(
            !graph.Dump().Contains("released-", StringComparison.Ordinal),
            "Disposed scope/node remained in dump."
        );
        return new LifetimeProbe(weak, graph);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    private static LifetimeProbe QueuedEffectPayload()
    {
        var graph = new ReactiveGraph();
        var payload = new Payload();
        var weak = new WeakReference(payload);
        var effect = graph.Effect(() => GC.KeepAlive(payload), "queued-release");
        effect.Dispose();
        return new LifetimeProbe(weak, graph);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    private static LifetimeProbe PostedPayload()
    {
        var graph = new ReactiveGraph();
        var source = new TaskCompletionSource<Payload>();
        var async = graph.Async(_ => source.Task, "posted-release");
        _ = async.Value;
        var payload = new Payload();
        var weak = new WeakReference(payload);
        Task.Run(() => source.SetResult(payload)).GetAwaiter().GetResult();
        async.Dispose();
        Assert(
            !graph.Dump().Contains("posted-release", StringComparison.Ordinal),
            "Disposed posted async remained active."
        );
        return new LifetimeProbe(weak, graph);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    private static LifetimeProbe NeverLoadPayload()
    {
        var graph = new ReactiveGraph();
        var source = new TaskCompletionSource<int>();
        var payload = new Payload();
        var weak = new WeakReference(payload);
        var async = graph.Async(
            _ =>
            {
                GC.KeepAlive(payload);
                return source.Task;
            },
            "never-release"
        );
        _ = async.Value;
        async.Dispose();
        return new LifetimeProbe(weak, graph, source);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static string EquivalentDump()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("dump");
        var source = scope.Signal(1, "source");
        var derived = scope.Derived(() => source.Value + 1, "derived");
        _ = scope.Effect(() => _ = derived.Value, "consumer");
        graph.Drain();
        return graph.Dump();
    }

    private static void CompleteOnWorker(TaskCompletionSource<int> completion, int value) =>
        Task.Run(() => completion.SetResult(value)).GetAwaiter().GetResult();

    private static void ExpectAggregate(Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected aggregate failure.");
        }
        catch (AggregateException) { }
    }

    private static AggregateException CaptureAggregate(Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected aggregate failure.");
        }
        catch (AggregateException exception)
        {
            return exception;
        }
    }

    private static IEnumerable<Exception> Flatten(AggregateException exception) =>
        exception.Flatten().InnerExceptions;

    private static void ExpectCycle(Action action, params string[] path)
    {
        var errors = Flatten(CaptureAggregate(action));
        var cycle =
            errors.OfType<ReactiveCycleException>().SingleOrDefault()
            ?? throw new InvalidOperationException("Expected a reactive cycle.");
        Assert(
            cycle.Message == "Reactive cycle: " + string.Join(" -> ", path),
            "Reactive cycle path was not exact."
        );
    }

    private static void ExpectArgument(Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected argument failure.");
        }
        catch (ArgumentNullException) { }
    }

    private static void Assert(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }

    private sealed record LifetimeProbe(
        WeakReference Payload,
        object Root,
        object? Producer = null
    );

    private sealed class Payload;

    [TestMethod]
    public void DiagnosticDumpIsDeterministicAndRedacted()
    {
        var first = EquivalentDump();
        Assert(first == EquivalentDump(), "Reactive dumps differ for equivalent live graphs.");
        Assert(
            !first.Contains("value=", StringComparison.OrdinalIgnoreCase)
                && !first.Contains("secret", StringComparison.OrdinalIgnoreCase),
            "Dump exposed values or errors."
        );
    }
}
