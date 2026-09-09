using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ApplicationCompletionWakeContracts
{
    [TestMethod]
    public void FinalCompletionWakesObserverSubscribedAfterCapturedObserverFails()
    {
        using var observerEntered = new ManualResetEventSlim();
        using var ownerDrained = new ManualResetEventSlim();
        using var releaseObserver = new ManualResetEventSlim();
        ApplicationSession? capturedSession = null;
        var lifecycle = new ProbeLifecycle();
        var host = new ProbeHost(
            session => capturedSession = session,
            observerEntered,
            ownerDrained,
            releaseObserver
        );
        var run = Task.Run(() =>
            LucentApplication
                .CreateBuilder()
                .SetFailureReporter(_ => { })
                .UseHost(host)
                .Build()
                .Run(lifecycle)
        );
        using var completion = new ManualResetEventSlim();
        _ = run.ContinueWith(
            static (_, state) => ((ManualResetEventSlim)state!).Set(),
            completion,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );

        var naturallyCompleted = false;
        try
        {
            Assert.IsTrue(
                observerEntered.Wait(TimeSpan.FromSeconds(5)),
                "The captured observer did not enter its controlled failure gate."
            );
            Assert.IsTrue(
                ownerDrained.Wait(TimeSpan.FromSeconds(5)),
                "The application owner did not drain the original posted callback."
            );
            releaseObserver.Set();

            // Observe task completion without Task.Wait propagating the expected aggregated
            // failure before the test can inspect it.
            naturallyCompleted = completion.Wait(TimeSpan.FromSeconds(5));
            var completedAfterRecovery = naturallyCompleted;
            if (!naturallyCompleted)
            {
                // Keep a pre-fix failure bounded and release the worker instead of leaving the
                // test process with an owner pump waiting forever on the lost finalization edge.
                using var fallbackWake = new ManualResetEventSlim();
                Action fallback = fallbackWake.Set;
                capturedSession!.WorkAvailable += fallback;
                try
                {
                    capturedSession.RequestClose();
                    fallbackWake.Wait(TimeSpan.FromSeconds(5));
                    completedAfterRecovery = completion.Wait(TimeSpan.FromSeconds(5));
                }
                finally
                {
                    capturedSession.WorkAvailable -= fallback;
                }
            }

            Exception? failure = null;
            if (completedAfterRecovery)
            {
                try
                {
                    _ = run.GetAwaiter().GetResult();
                }
                catch (Exception error)
                {
                    failure = error;
                }
            }

            Assert.IsTrue(
                naturallyCompleted,
                "Final completion required a rescue wake; the finalization edge was lost."
            );
            Assert.IsTrue(
                completedAfterRecovery,
                "Final completion remained stranded after observer failure."
            );
            Assert.IsNotNull(failure);
            var messages = Flatten(failure!).Select(error => error.Message).ToArray();
            CollectionAssert.Contains(messages, "RC04 host abort");
            CollectionAssert.Contains(messages, "RC04 observer failure");
        }
        finally
        {
            releaseObserver.Set();
            if (!naturallyCompleted)
            {
                capturedSession?.RequestClose();
                completion.Wait(TimeSpan.FromSeconds(5));
            }
        }
    }

    private static IEnumerable<Exception> Flatten(Exception exception) =>
        exception is AggregateException aggregate
            ? aggregate.InnerExceptions.SelectMany(Flatten)
            : [exception];

    private sealed class ProbeHost(
        Action<ApplicationSession> capture,
        ManualResetEventSlim observerEntered,
        ManualResetEventSlim ownerDrained,
        ManualResetEventSlim releaseObserver
    ) : IApplicationHost
    {
        public int Run(ApplicationSession session)
        {
            capture(session);
            session.Start();
            PumpUntilRunning(session);
            SynchronizationContext captured;
            using (session.EnterContext())
                captured = SynchronizationContext.Current!;

            session.WorkAvailable += ThrowingObserver;
            _ = Task.Run(() =>
                captured.Post(static state => ((ManualResetEventSlim)state!).Set(), ownerDrained)
            );
            if (!observerEntered.Wait(TimeSpan.FromSeconds(5)))
                throw new InvalidOperationException("RC04 observer did not enter.");
            throw new InvalidOperationException("RC04 host abort");
        }

        private void ThrowingObserver()
        {
            observerEntered.Set();
            if (!releaseObserver.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("RC04 release timed out.");
            throw new InvalidOperationException("RC04 observer failure");
        }

        private static void PumpUntilRunning(ApplicationSession session)
        {
            for (
                var attempt = 0;
                attempt < 100 && session.Status.Phase == ApplicationPhase.Starting;
                attempt++
            )
            {
                session.ProcessEvents();
                if (!session.Composition.IsDisposed)
                    session.Composition.Flush();
            }
            if (session.Status.Phase != ApplicationPhase.Running)
                throw new InvalidOperationException("RC04 probe did not reach Running.");
        }
    }

    private sealed class ProbeLifecycle : IApplicationLifecycle
    {
        public ValueTask<ComponentRecipe> StartAsync(ApplicationSession session) =>
            ValueTask.FromResult(ComponentRecipe.Create("rc04-probe", static (_, _) => { }));

        public ValueTask<bool> PrepareCloseAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask StopAsync() => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
