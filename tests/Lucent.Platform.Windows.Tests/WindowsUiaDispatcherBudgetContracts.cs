using SDL3;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed class WindowsUiaDispatcherBudgetContracts
{
    [TestMethod]
    public void OwnerShutdownCancelsQueuedRequestBeforeNativeDestruction()
    {
        if (!SDL.Init(SDL.InitFlags.Video))
            throw new InvalidOperationException("SDL_Init(UIA shutdown): " + SDL.GetError());

        try
        {
            var dispatcher = new WindowsUiaDispatcher(
                TimeSpan.FromSeconds(5),
                (ref SDL.Event _) => true
            );
            var actionRan = false;
            var request = Task.Factory.StartNew(
                () => dispatcher.TryInvoke("shutdown", () => actionRan = true, out _),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            );
            Assert.IsTrue(
                SpinWait.SpinUntil(() => dispatcher.PendingCount == 1, 1_000),
                "The queued UIA request did not reach the shutdown boundary."
            );

            var errors = new List<Exception>();
            WindowsBootstrap.ShutdownUia(dispatcher, listener: null, provider: null, errors);

            Assert.IsTrue(
                request.Wait(250),
                "Native destruction began while a queued UIA caller was still blocked."
            );
            Assert.AreEqual(0, errors.Count, "UIA shutdown unexpectedly reported an error.");
            Assert.IsFalse(request.Result, "Shutdown executed queued UIA work.");
            Assert.IsFalse(actionRan, "Shutdown ran a queued provider action after cancellation.");
        }
        finally
        {
            SDL.Quit();
        }
    }

    [TestMethod]
    public void RefillableProducerYieldsAndRewakesTheOwnerLoop()
    {
        if (!SDL.Init(SDL.InitFlags.Video))
            throw new InvalidOperationException("SDL_Init(UIA budget): " + SDL.GetError());

        try
        {
            var wakeCount = 0;
            using var dispatcher = new WindowsUiaDispatcher(
                TimeSpan.FromSeconds(5),
                (ref SDL.Event _) =>
                {
                    Interlocked.Increment(ref wakeCount);
                    return true;
                }
            );
            const int requestCount = 80;
            var requests = Enumerable
                .Range(0, requestCount)
                .Select(_ =>
                    Task.Factory.StartNew(
                        () => dispatcher.TryInvoke("refill", static () => 1, out _),
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default
                    )
                )
                .ToArray();

            var deadline = Environment.TickCount64 + 5_000;
            while (dispatcher.PendingCount < requestCount && Environment.TickCount64 < deadline)
                Thread.SpinWait(1000);

            Assert.AreEqual(requestCount, dispatcher.PendingCount);
            var firstPass = dispatcher.Process();

            Assert.IsTrue(
                firstPass > 0 && firstPass < requestCount,
                $"The owner pass drained an unbounded refillable queue: processed={firstPass}."
            );
            Assert.IsTrue(
                dispatcher.PendingCount > 0,
                "The bounded owner pass did not leave work for a later pass."
            );
            Assert.IsTrue(
                Volatile.Read(ref wakeCount) > requestCount,
                "Pending work did not schedule a follow-up owner wake after the budget was spent."
            );

            var processed = firstPass;
            while (dispatcher.PendingCount > 0)
                processed += dispatcher.Process();

            Assert.AreEqual(requestCount, processed);
            Assert.IsTrue(
                Task.WaitAll(requests, 5_000),
                "A bounded drain left an accepted UIA request waiting."
            );
            Assert.IsTrue(requests.All(task => task.Result));
        }
        finally
        {
            SDL.Quit();
        }
    }

    [TestMethod]
    public void CanceledRefillableProducerIsAlsoBounded()
    {
        if (!SDL.Init(SDL.InitFlags.Video))
            throw new InvalidOperationException("SDL_Init(UIA canceled budget): " + SDL.GetError());

        try
        {
            var wakeCount = 0;
            using var dispatcher = new WindowsUiaDispatcher(
                TimeSpan.FromMilliseconds(10),
                (ref SDL.Event _) =>
                {
                    Interlocked.Increment(ref wakeCount);
                    return true;
                }
            );
            const int requestCount = 80;
            var requests = Enumerable
                .Range(0, requestCount)
                .Select(_ =>
                    Task.Factory.StartNew(
                        () => dispatcher.TryInvoke("canceled-refill", static () => 1, out _),
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default
                    )
                )
                .ToArray();

            Assert.IsTrue(
                Task.WaitAll(requests, 5_000),
                "Timed-out UIA requests did not complete while the owner was idle."
            );
            Assert.IsTrue(requests.All(task => !task.Result));
            Assert.AreEqual(requestCount, dispatcher.PendingCount);

            var firstPass = dispatcher.Process();

            Assert.AreEqual(0, firstPass, "Canceled requests must not report completed actions.");
            Assert.IsTrue(
                dispatcher.PendingCount > 0,
                "Canceled refillable work bypassed the bounded owner pass."
            );
            Assert.IsTrue(
                Volatile.Read(ref wakeCount) > requestCount,
                "Canceled work did not schedule a follow-up owner wake after the budget was spent."
            );
            while (dispatcher.PendingCount > 0)
                _ = dispatcher.Process();
        }
        finally
        {
            SDL.Quit();
        }
    }
}
