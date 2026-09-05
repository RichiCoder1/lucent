using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ApplicationTests
{
    [TestMethod]
    public void BuilderSnapshotsConfigurationAndProvidesDefaults()
    {
        var firstHost = new RecordingHost(3);
        var secondHost = new RecordingHost(4);
        var builder = LucentApplication.CreateBuilder().UseHost(firstHost);
        var first = builder.Build();
        builder.SetTitle("Second").SetTheme(_ => new Theme("second")).UseHost(secondHost);
        var second = builder.Build();

        Assert.AreEqual(3, first.Run(EmptyRecipe()), "The first host exit code was not preserved.");
        Assert.AreEqual(
            "Lucent",
            firstHost.Title,
            "The default title or builder snapshot changed."
        );
        Assert.AreSame(
            ControlThemes.Light,
            firstHost.Theme,
            "The default light appearance did not select the standard light control theme."
        );
        Assert.AreEqual(
            4,
            second.Run(EmptyRecipe()),
            "The second host exit code was not preserved."
        );
        Assert.AreEqual(
            "Second",
            secondHost.Title,
            "The updated builder title was not snapshotted."
        );
        Assert.AreEqual(
            "second",
            secondHost.Theme?.Name,
            "The updated theme factory was not snapshotted."
        );

        var factoryCalls = 0;
        var missingHost = LucentApplication
            .CreateBuilder()
            .SetTheme(_ =>
            {
                factoryCalls++;
                return ControlThemes.Light;
            });
        Assert.ThrowsExactly<InvalidOperationException>(() => missingHost.Build());
        Assert.AreEqual(
            0,
            factoryCalls,
            "Build allocated application theme state before a host was selected."
        );
    }

    [TestMethod]
    public void RunReturnsHostResultAndTracksAppearanceWithoutRedundantRefresh()
    {
        var cleanup = 0;
        var appearances = new List<ThemeAppearance>();
        var host = new RecordingHost(
            7,
            session =>
            {
                Assert.AreEqual(
                    "Light-Normal",
                    session.Theme.Theme.Name,
                    "The initial theme was not created once."
                );
                session.Theme.Appearance = new ThemeAppearance(
                    ThemeColorScheme.Dark,
                    ThemeContrast.Normal
                );
                session.Composition.Graph.Drain();
                Assert.AreEqual(
                    "Dark-Normal",
                    session.Theme.Theme.Name,
                    "Appearance did not refresh the theme."
                );
            }
        );
        var app = LucentApplication
            .CreateBuilder()
            .UseHost(host)
            .SetTheme(appearance =>
            {
                appearances.Add(appearance);
                return new Theme($"{appearance.ColorScheme}-{appearance.Contrast}");
            })
            .Build();

        var result = app.Run(
            ComponentRecipe.Create("root", (_, root) => root.Scope.OnDispose(() => cleanup++))
        );

        Assert.AreEqual(7, result, "The host result was not returned.");
        Assert.AreEqual(1, cleanup, "Normal application cleanup did not run exactly once.");
        CollectionAssert.AreEqual(
            new[]
            {
                ThemeAppearance.Light,
                new ThemeAppearance(ThemeColorScheme.Dark, ThemeContrast.Normal),
            },
            appearances,
            "The theme factory ran redundantly or missed an appearance transition."
        );
        Assert.IsTrue(
            host.CompositionDisposed,
            "The host composition was not released after return."
        );
    }

    [TestMethod]
    public void RunCleansUpAfterMountAndHostFailures()
    {
        var mountCleanup = 0;
        var host = new RecordingHost(0);
        var mountFailure = LucentApplication.CreateBuilder().UseHost(host).Build();
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            mountFailure.Run(
                ComponentRecipe.Create(
                    "mount-failure",
                    (_, root) =>
                    {
                        root.Scope.OnDispose(() => mountCleanup++);
                        throw new InvalidOperationException("mount");
                    }
                )
            )
        );
        Assert.AreEqual(1, mountCleanup, "A failed mount did not release its recipe resources.");
        Assert.AreEqual(1, host.RunCount, "The host did not own the failed startup attempt.");

        var hostCleanup = 0;
        var hostFailure = LucentApplication
            .CreateBuilder()
            .UseHost(new RecordingHost(0, _ => throw new InvalidOperationException("host")))
            .Build();
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            hostFailure.Run(
                ComponentRecipe.Create(
                    "host-failure",
                    (_, root) => root.Scope.OnDispose(() => hostCleanup++)
                )
            )
        );
        Assert.AreEqual(1, hostCleanup, "A host failure did not release the mounted recipe.");
    }

    [TestMethod]
    public void RunPreservesPrimaryAndCleanupFailures()
    {
        var app = LucentApplication
            .CreateBuilder()
            .UseHost(new RecordingHost(0, _ => throw new InvalidOperationException("primary")))
            .Build();
        var failure = Assert.ThrowsExactly<AggregateException>(() =>
            app.Run(
                ComponentRecipe.Create(
                    "aggregate",
                    (_, root) =>
                        root.Scope.OnDispose(() => throw new InvalidOperationException("cleanup"))
                )
            )
        );
        var messages = Flatten(failure).Select(item => item.Message).ToArray();
        CollectionAssert.Contains(messages, "primary", "The primary host failure was lost.");
        CollectionAssert.Contains(messages, "cleanup", "The cleanup failure was lost.");
    }

    [TestMethod]
    public void BuiltApplicationsAreOneShotAndFreshBuildsRunIndependently()
    {
        var builder = LucentApplication.CreateBuilder().UseHost(new RecordingHost(0));
        var first = builder.Build();
        Assert.ThrowsExactly<ArgumentNullException>(() => first.Run((ComponentRecipe)null!));
        Assert.AreEqual(0, first.Run(EmptyRecipe()), "The first application run failed.");
        Assert.ThrowsExactly<InvalidOperationException>(() => first.Run(EmptyRecipe()));
        Assert.AreEqual(
            0,
            builder.Build().Run(EmptyRecipe()),
            "A fresh build was not independently runnable."
        );
    }

    [TestMethod]
    public void SessionCloseNegotiationIsRetryableCoalescedAndOwnerThreadBound()
    {
        var lifecycle = new RetryLifecycle();
        var priorContext = SynchronizationContext.Current;
        var host = new DelegateHost(session =>
        {
            Exception? foreignStart = null;
            var foreignThread = new Thread(() =>
            {
                try
                {
                    session.Start();
                }
                catch (Exception error)
                {
                    foreignStart = error;
                }
            });
            foreignThread.Start();
            foreignThread.Join();
            Assert.IsInstanceOfType<InvalidOperationException>(foreignStart);
            session.Start();
            Assert.AreSame(priorContext, SynchronizationContext.Current);
            PumpUntil(session, () => session.Status.Phase == ApplicationPhase.Running);
            Assert.AreSame(priorContext, SynchronizationContext.Current);

            Task.Run(() =>
                {
                    session.RequestClose();
                    session.RequestClose();
                })
                .GetAwaiter()
                .GetResult();
            PumpUntil(
                session,
                () =>
                    session.Status.Phase == ApplicationPhase.Running
                    && session.Status.Error?.Message == "prepare"
            );
            Assert.AreEqual(1, lifecycle.PrepareCalls);

            session.RequestClose();
            session.RequestClose();
            PumpUntil(
                session,
                () =>
                    lifecycle.PrepareCalls == 2
                    && session.Status.Phase == ApplicationPhase.Running
                    && session.Status.Error is null
            );

            session.RequestClose();
            session.RequestClose();
            PumpUntil(session, () => session.IsCompleted);
            Assert.AreEqual(ApplicationPhase.Completed, session.Status.Phase);
            Assert.AreSame(priorContext, SynchronizationContext.Current);
            return 12;
        });
        var app = LucentApplication.CreateBuilder().UseHost(host).Build();

        Assert.AreEqual(12, app.Run(lifecycle));
        Assert.AreEqual(3, lifecycle.PrepareCalls);
        Assert.AreEqual(1, lifecycle.StopCalls);
        Assert.AreEqual(1, lifecycle.DisposeCalls);
        Assert.IsTrue(lifecycle.OwnerThreads.All(id => id == Environment.CurrentManagedThreadId));
        Assert.IsTrue(lifecycle.HadSessionContext);
    }

    [TestMethod]
    public void CloseRequestedDuringStartupIsLatched()
    {
        var lifecycle = new AcceptingLifecycle();
        var host = new DelegateHost(session =>
        {
            session.Start();
            Task.Run(session.RequestClose).GetAwaiter().GetResult();
            PumpUntil(session, () => session.IsCompleted);
            return 4;
        });

        Assert.AreEqual(4, LucentApplication.CreateBuilder().UseHost(host).Build().Run(lifecycle));
        Assert.AreEqual(1, lifecycle.PrepareCalls);
        Assert.AreEqual(1, lifecycle.StopCalls);
        Assert.AreEqual(1, lifecycle.DisposeCalls);
    }

    [TestMethod]
    public void StartupStopCompositionAndDisposeFailuresRemainObservable()
    {
        var lifecycle = new FailingLifecycle();
        var scopeCleanup = 0;
        var host = new DelegateHost(session =>
        {
            session.Scope.OnDispose(() =>
            {
                scopeCleanup++;
                throw new InvalidOperationException("composition");
            });
            session.Start();
            PumpUntil(session, () => session.IsCompleted);
            return 0;
        });
        var failure = Assert.ThrowsExactly<AggregateException>(() =>
            LucentApplication.CreateBuilder().UseHost(host).Build().Run(lifecycle)
        );
        var messages = Flatten(failure).Select(error => error.Message).ToArray();

        CollectionAssert.Contains(messages, "start");
        CollectionAssert.Contains(messages, "stop");
        CollectionAssert.Contains(messages, "composition");
        CollectionAssert.Contains(messages, "dispose");
        Assert.AreEqual(1, scopeCleanup);
        Assert.AreEqual(1, lifecycle.StopCalls);
        Assert.AreEqual(1, lifecycle.DisposeCalls);
    }

    [TestMethod]
    public void UnexpectedHostFailureWaitsForPendingStartupBeforeCleanup()
    {
        var lifecycle = new AcceptingLifecycle();
        var mounted = 0;
        lifecycle.Recipe = ComponentRecipe.Create("pending", (_, _) => mounted++);
        var host = new DelegateHost(session =>
        {
            session.Start();
            throw new InvalidOperationException("host");
        });
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
            LucentApplication.CreateBuilder().UseHost(host).Build().Run(lifecycle)
        );

        Assert.AreEqual("host", failure.Message);
        Assert.AreEqual(0, mounted, "An aborted pending startup mounted after host failure.");
        Assert.AreEqual(1, lifecycle.StopCalls);
        Assert.AreEqual(1, lifecycle.DisposeCalls);
    }

    [TestMethod]
    public void SessionContextMarshalsCallbacksBoundsDrainsAndDropsLatePosts()
    {
        var priorContext = SynchronizationContext.Current;
        SynchronizationContext? captured = null;
        var wakeCount = 0;
        var lifecycle = new AcceptingLifecycle();
        var host = new DelegateHost(session =>
        {
            session.WorkAvailable += () => wakeCount++;
            session.Start();
            PumpUntil(session, () => session.Status.Phase == ApplicationPhase.Running);

            using (session.EnterContext())
            {
                captured = SynchronizationContext.Current;
                Assert.IsNotNull(captured);
                var continuationThread = 0;
                async Task ResumeAsync()
                {
                    await Task.Yield();
                    continuationThread = Environment.CurrentManagedThreadId;
                }

                var resume = ResumeAsync();
                Assert.IsFalse(resume.IsCompleted);
                Assert.IsTrue(session.ProcessEvents());
                Assert.IsTrue(resume.IsCompletedSuccessfully);
                Assert.AreEqual(Environment.CurrentManagedThreadId, continuationThread);

                Exception? sendFailure = null;
                var foreignThread = new Thread(() =>
                {
                    try
                    {
                        captured.Send(static _ => { }, null);
                    }
                    catch (Exception error)
                    {
                        sendFailure = error;
                    }
                });
                foreignThread.Start();
                foreignThread.Join();
                Assert.IsInstanceOfType<NotSupportedException>(sendFailure);

                var callbackCount = 0;
                SendOrPostCallback callback = null!;
                callback = _ =>
                {
                    callbackCount++;
                    if (callbackCount < 3)
                        captured.Post(callback, null);
                };
                captured.Post(callback, null);
                Assert.IsTrue(session.ProcessEvents());
                Assert.AreEqual(1, callbackCount);
                Assert.IsTrue(session.ProcessEvents());
                Assert.AreEqual(2, callbackCount);
                Assert.IsTrue(session.ProcessEvents());
                Assert.AreEqual(3, callbackCount);
            }
            Assert.AreSame(priorContext, SynchronizationContext.Current);

            session.RequestClose();
            PumpUntil(session, () => session.IsCompleted);
            var completedWakeCount = wakeCount;
            captured!.Post(static _ => throw new InvalidOperationException("late"), null);
            Assert.AreEqual(completedWakeCount, wakeCount);
            Assert.IsFalse(session.ProcessEvents());
            return 0;
        });

        Assert.AreEqual(0, LucentApplication.CreateBuilder().UseHost(host).Build().Run(lifecycle));
        Assert.AreSame(priorContext, SynchronizationContext.Current);
    }

    [TestMethod]
    public void ThrowingStatusEffectStillCompletesAndReleasesEveryStage()
    {
        var lifecycle = new PausingStopLifecycle();
        var host = new DelegateHost(session =>
        {
            session.Start();
            PumpUntil(session, () => session.Status.Phase == ApplicationPhase.Running);
            _ = session.Scope.Effect(
                () =>
                {
                    if (session.Status.Phase == ApplicationPhase.Stopping)
                        throw new InvalidOperationException("status-observer");
                },
                "throwing-status-observer"
            );

            session.RequestClose();
            for (
                var attempt = 0;
                attempt < 10 && session.Status.Phase != ApplicationPhase.Stopping;
                attempt++
            )
                Assert.IsTrue(session.ProcessEvents());
            Assert.AreEqual(ApplicationPhase.Stopping, session.Status.Phase);
            Exception statusFailure;
            try
            {
                statusFailure = Assert.ThrowsExactly<AggregateException>(() =>
                    _ = session.Composition.Flush()
                );
            }
            finally
            {
                lifecycle.ReleaseStop();
            }
            throw statusFailure;
        });

        var failure = Assert.ThrowsExactly<AggregateException>(() =>
            LucentApplication.CreateBuilder().UseHost(host).Build().Run(lifecycle)
        );
        CollectionAssert.Contains(
            Flatten(failure).Select(error => error.Message).ToArray(),
            "status-observer"
        );
        Assert.AreEqual(1, lifecycle.StopCalls);
        Assert.AreEqual(1, lifecycle.DisposeCalls);
    }

    [TestMethod]
    public void LateWorkObserverFailureRearmsAfterConcurrentOwnerDrain()
    {
        var lifecycle = new AcceptingLifecycle();
        var host = new DelegateHost(session =>
        {
            session.Start();
            PumpUntil(session, () => session.Status.Phase == ApplicationPhase.Running);
            SynchronizationContext captured;
            using (session.EnterContext())
                captured = SynchronizationContext.Current!;

            using var observerEntered = new ManualResetEventSlim();
            using var releaseObserver = new ManualResetEventSlim();
            var wakes = 0;
            Action successful = () => Interlocked.Increment(ref wakes);
            Action failing = () =>
            {
                observerEntered.Set();
                if (!releaseObserver.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Owner did not release the observer.");
                throw new InvalidOperationException("late-session-observer");
            };
            session.WorkAvailable += successful;
            session.WorkAvailable += failing;

            var producer = Task.Run(() => captured.Post(static _ => { }, null));
            try
            {
                Assert.IsTrue(observerEntered.Wait(TimeSpan.FromSeconds(5)));
                Assert.IsTrue(session.ProcessEvents());
                Assert.AreEqual(1, wakes);
            }
            finally
            {
                releaseObserver.Set();
                producer.GetAwaiter().GetResult();
            }

            Assert.AreEqual(
                2,
                wakes,
                "The successful observer was not rearmed for the late failure."
            );
            var failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
                session.ProcessEvents()
            );
            Assert.AreEqual("late-session-observer", failure.Message);
            session.WorkAvailable -= failing;
            session.WorkAvailable -= successful;
            session.RequestClose();
            PumpUntil(session, () => session.IsCompleted);
            return 0;
        });

        Assert.AreEqual(0, LucentApplication.CreateBuilder().UseHost(host).Build().Run(lifecycle));
    }

    [TestMethod]
    public void ThrowingWorkObserverIsReportedAndDoesNotStrandCleanup()
    {
        var lifecycle = new AcceptingLifecycle();
        var host = new DelegateHost(session =>
        {
            session.WorkAvailable += () => throw new InvalidOperationException("wake-observer");
            session.Start();
            session.ProcessEvents();
            return 0;
        });

        var failure = Assert.ThrowsExactly<AggregateException>(() =>
            LucentApplication.CreateBuilder().UseHost(host).Build().Run(lifecycle)
        );
        Assert.IsTrue(
            Flatten(failure).Any(error => error.Message == "wake-observer"),
            "The work-observer failure was not retained."
        );
        Assert.AreEqual(1, lifecycle.StopCalls);
        Assert.AreEqual(1, lifecycle.DisposeCalls);
    }

    private static void PumpUntil(ApplicationSession session, Func<bool> complete)
    {
        for (var attempt = 0; attempt < 100 && !complete(); attempt++)
        {
            session.ProcessEvents();
            if (!session.Composition.IsDisposed)
                session.Composition.Flush();
        }
        Assert.IsTrue(complete(), "The application session did not reach the expected state.");
    }

    private static ComponentRecipe EmptyRecipe() => ComponentRecipe.Create("empty", (_, _) => { });

    private static IEnumerable<Exception> Flatten(Exception exception) =>
        exception is AggregateException aggregate
            ? aggregate.InnerExceptions.SelectMany(Flatten)
            : [exception];

    private sealed class DelegateHost(Func<ApplicationSession, int> run) : IApplicationHost
    {
        public int Run(ApplicationSession session) => run(session);
    }

    private class AcceptingLifecycle : IApplicationLifecycle
    {
        internal ComponentRecipe Recipe { get; set; } = EmptyRecipe();
        internal int PrepareCalls { get; private protected set; }
        internal int StopCalls { get; private set; }
        internal int DisposeCalls { get; private set; }

        public virtual async ValueTask<ComponentRecipe> StartAsync(ApplicationSession session)
        {
            await Task.Yield();
            return Recipe;
        }

        public virtual async ValueTask<bool> PrepareCloseAsync()
        {
            PrepareCalls++;
            await Task.Yield();
            return true;
        }

        public virtual async ValueTask StopAsync()
        {
            StopCalls++;
            await Task.Yield();
        }

        public virtual async ValueTask DisposeAsync()
        {
            DisposeCalls++;
            await Task.Yield();
        }
    }

    private sealed class RetryLifecycle : AcceptingLifecycle
    {
        internal List<int> OwnerThreads { get; } = [];
        internal bool HadSessionContext { get; private set; }

        public override async ValueTask<ComponentRecipe> StartAsync(ApplicationSession session)
        {
            HadSessionContext = SynchronizationContext.Current is not null;
            await Task.Yield();
            OwnerThreads.Add(Environment.CurrentManagedThreadId);
            return Recipe;
        }

        public override async ValueTask<bool> PrepareCloseAsync()
        {
            var call = ++PrepareCalls;
            await Task.Yield();
            OwnerThreads.Add(Environment.CurrentManagedThreadId);
            if (call == 1)
                throw new InvalidOperationException("prepare");
            return call == 3;
        }

        public override async ValueTask StopAsync()
        {
            await base.StopAsync();
            OwnerThreads.Add(Environment.CurrentManagedThreadId);
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            OwnerThreads.Add(Environment.CurrentManagedThreadId);
        }
    }

    private sealed class PausingStopLifecycle : AcceptingLifecycle
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal void ReleaseStop() => _release.SetResult();

        public override async ValueTask StopAsync()
        {
            await base.StopAsync();
            await _release.Task;
        }
    }

    private sealed class FailingLifecycle : IApplicationLifecycle
    {
        internal int StopCalls { get; private set; }
        internal int DisposeCalls { get; private set; }

        public async ValueTask<ComponentRecipe> StartAsync(ApplicationSession session)
        {
            await Task.Yield();
            throw new InvalidOperationException("start");
        }

        public async ValueTask<bool> PrepareCloseAsync()
        {
            await Task.Yield();
            return true;
        }

        public async ValueTask StopAsync()
        {
            StopCalls++;
            await Task.Yield();
            throw new InvalidOperationException("stop");
        }

        public async ValueTask DisposeAsync()
        {
            DisposeCalls++;
            await Task.Yield();
            throw new InvalidOperationException("dispose");
        }
    }

    private sealed class RecordingHost(int exitCode, Action<ApplicationSession>? run = null)
        : IApplicationHost
    {
        private ApplicationSession? _session;

        internal string? Title { get; private set; }
        internal Theme? Theme { get; private set; }
        internal int RunCount { get; private set; }
        internal bool CompositionDisposed => _session?.Composition.IsDisposed == true;

        public int Run(ApplicationSession session)
        {
            Title = session.Title;
            Theme = session.Theme.Theme;
            RunCount++;
            _session = session;
            session.Start();
            run?.Invoke(session);
            return exitCode;
        }
    }
}
