using System.Collections.Concurrent;
using Lucent.Core;
using Lucent.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lucent.Hosting.Tests;

[TestClass]
public sealed class HostingTests
{
    private static readonly string[] MissingRequirementEvents =
    [
        "service-start",
        "bound-service-create",
        "service-stop",
        "bound-service-dispose",
        "service-dispose",
    ];
    private static readonly string[] BoundServiceEvents =
    [
        "service-start",
        "bound-service-create",
        "component-setup",
        "service-stop",
        "component-dispose",
        "bound-service-dispose",
        "service-dispose",
    ];
    private static readonly string[] LifecycleBuilderEvents =
    [
        "service-start",
        "root-factory",
        "bound-service-create",
        "component-setup",
        "root-mount",
        "prepare",
        "service-stop",
        "ui-dispose",
        "bound-service-dispose",
        "async-scope-dispose",
        "service-dispose",
    ];

    [TestMethod]
    public void OfficialHostCreatesOneScopedModelAndReleasesUiBeforeServices()
    {
        var owner = Environment.CurrentManagedThreadId;
        var events = new List<string>();
        var builder = HostedApplication.CreateBuilder();
        builder.Services.AddSingleton(events);
        builder.Services.AddSingleton<IHostedService>(_ => new RecordingHostedService(events));
        builder.Services.AddScoped(_ => new ScopedModel(events));
        builder.Services.AddScoped(_ => new SyncScopedResource(events));
        builder.Services.AddScoped(_ => new AsyncScopedResource(events));
        var lifecycle = new HostedApplication(
            _ => builder.Build(),
            (services, session) =>
            {
                Assert.AreEqual(owner, Environment.CurrentManagedThreadId);
                Assert.AreSame(
                    services.GetRequiredService<ScopedModel>(),
                    services.GetRequiredService<ScopedModel>()
                );
                Assert.AreEqual(owner, services.GetRequiredService<ScopedModel>().CreatedThread);
                _ = services.GetRequiredService<SyncScopedResource>();
                _ = services.GetRequiredService<AsyncScopedResource>();
                events.Add("root-factory");
                return ComponentRecipe.Create(
                    "hosted-root",
                    (_, root) =>
                    {
                        Assert.AreEqual(owner, Environment.CurrentManagedThreadId);
                        events.Add("root-mount");
                        root.Scope.OnDispose(() =>
                        {
                            Assert.AreEqual(owner, Environment.CurrentManagedThreadId);
                            events.Add("ui-dispose");
                        });
                    }
                );
            },
            (services, cancellationToken) =>
            {
                Assert.AreEqual(owner, Environment.CurrentManagedThreadId);
                Assert.IsTrue(cancellationToken.CanBeCanceled);
                Assert.AreSame(
                    services.GetRequiredService<ScopedModel>(),
                    services.GetRequiredService<ScopedModel>()
                );
                events.Add("prepare");
                return ValueTask.FromResult(true);
            }
        );
        var app = LucentApplication
            .CreateBuilder()
            .UseHost(new PumpingHost(session => session.RequestClose(), 17))
            .Build();

        Assert.AreEqual(17, app.Run(lifecycle));
        CollectionAssert.AreEqual(
            new List<string>
            {
                "service-start",
                "model-create",
                "root-factory",
                "root-mount",
                "prepare",
                "service-stop",
                "ui-dispose",
                "async-scope-dispose",
                "sync-scope-dispose",
                "model-dispose",
                "service-dispose",
            },
            events,
            "The official host, composition, async scope, and root provider did not release in order."
        );
    }

    [TestMethod]
    public void BuilderLifecycleCreatesRootAfterHostReadinessAndReleasesUiBeforeAsyncScope()
    {
        var owner = Environment.CurrentManagedThreadId;
        var events = new List<string>();
        var hostBuilder = HostedApplication.CreateBuilder();
        hostBuilder.Services.AddSingleton(events);
        hostBuilder.Services.AddSingleton<IHostedService>(_ => new RecordingHostedService(events));
        hostBuilder.Services.AddScoped(_ => new AsyncScopedResource(events));
        hostBuilder.Services.AddScoped(services =>
        {
            _ = services.GetRequiredService<AsyncScopedResource>();
            return new BoundScopedService(events);
        });

        BoundScopedService? borrowed = null;
        var rootFactoryCalls = 0;
        var root = ComponentRecipe.Defer(
            "builder-bound-service-consumer",
            ComponentRequirements.Service<BoundScopedService>(
                new("service", typeof(BoundScopedService).FullName!, "HostingBuilder.lui", 1, 1)
            ),
            (_, service) =>
            {
                borrowed = service;
                Assert.AreEqual(owner, Environment.CurrentManagedThreadId);
                Assert.IsFalse(service.IsDisposed);
                events.Add("component-setup");
                return ComponentRecipe.Create(
                    "builder-mounted-root",
                    (_, mounted) =>
                    {
                        events.Add("root-mount");
                        mounted.Scope.OnDispose(() => events.Add("ui-dispose"));
                    }
                );
            }
        );
        var app = LucentApplication
            .CreateBuilder()
            .UseHost(new PumpingHost(session => session.RequestClose()))
            .UseHosting(
                () => hostBuilder.Build(),
                (services, cancellationToken) =>
                {
                    Assert.AreEqual(owner, Environment.CurrentManagedThreadId);
                    Assert.IsTrue(cancellationToken.CanBeCanceled);
                    Assert.IsNotNull(borrowed);
                    Assert.IsFalse(borrowed.IsDisposed);
                    Assert.AreSame(borrowed, services.GetRequiredService<BoundScopedService>());
                    events.Add("prepare");
                    return ValueTask.FromResult(true);
                }
            )
            .Build(() =>
            {
                rootFactoryCalls++;
                Assert.AreEqual(owner, Environment.CurrentManagedThreadId);
                Assert.AreEqual("service-start", events[^1]);
                events.Add("root-factory");
                return root;
            });

        Assert.AreEqual(0, app.Run());
        Assert.AreEqual(1, rootFactoryCalls);
        Assert.IsNotNull(borrowed);
        Assert.IsTrue(borrowed.IsDisposed);
        CollectionAssert.AreEqual(LifecycleBuilderEvents, events);
    }

    [TestMethod]
    public void BuilderHostingStartupFailureStillStopsAndDisposesTheAcquiredHost()
    {
        var events = new List<string>();
        var hostBuilder = HostedApplication.CreateBuilder();
        hostBuilder.Services.AddSingleton(events);
        hostBuilder.Services.AddSingleton<IHostedService>(_ => new FailingStartService(events));
        var rootFactoryCalls = 0;
        var app = LucentApplication
            .CreateBuilder()
            .UseHost(new PumpingHost())
            .UseHosting(() => hostBuilder.Build())
            .Build(() =>
            {
                rootFactoryCalls++;
                return EmptyRecipe();
            });

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => app.Run());

        Assert.AreEqual("start failed", failure.Message);
        Assert.AreEqual(0, rootFactoryCalls);
        CollectionAssert.AreEqual(
            new List<string> { "service-start", "service-stop", "service-dispose" },
            events
        );
    }

    [TestMethod]
    public void BuilderHostingPreservesStopUiScopeAndHostFailures()
    {
        var events = new List<string>();
        var hostBuilder = HostedApplication.CreateBuilder();
        hostBuilder.Services.AddSingleton(events);
        hostBuilder.Services.AddSingleton<IHostedService>(_ => new FailingStopService(events));
        hostBuilder.Services.AddScoped(_ => new FailingScopedResource(events));
        var root = ComponentRecipe.Defer(
            "builder-failing-scope",
            ComponentRequirements.Service<FailingScopedResource>(
                new("service", typeof(FailingScopedResource).FullName!, "HostingBuilder.lui", 1, 1)
            ),
            (_, _) =>
                ComponentRecipe.Create(
                    "builder-failing-root",
                    (_, mounted) =>
                        mounted.Scope.OnDispose(() =>
                        {
                            events.Add("ui-dispose");
                            throw new InvalidOperationException("ui dispose failed");
                        })
                )
        );
        var app = LucentApplication
            .CreateBuilder()
            .UseHost(new PumpingHost(session => session.RequestClose()))
            .UseHosting(() => new FailingDisposeHost(hostBuilder.Build(), events))
            .Build(root);

        var failure = Assert.ThrowsExactly<AggregateException>(() => app.Run());
        var messages = Flatten(failure).Select(error => error.Message).ToArray();
        foreach (
            var expected in new List<string>
            {
                "stop failed",
                "ui dispose failed",
                "scope dispose failed",
                "host dispose failed",
            }
        )
            CollectionAssert.Contains(messages, expected, $"Cleanup lost '{expected}'.");
        CollectionAssert.AreEqual(
            new List<string>
            {
                "service-start",
                "service-stop",
                "ui-dispose",
                "scope-dispose",
                "host-dispose",
                "service-dispose",
            },
            events,
            "A failed cleanup boundary prevented later cleanup from running."
        );
    }

    [TestMethod]
    public void HostingBuilderSnapshotsKeepTheirServiceProvidersIsolated()
    {
        using var firstMounted = new ManualResetEventSlim();
        using var secondMounted = new ManualResetEventSlim();
        var providerByOwner = new ConcurrentDictionary<int, string>();
        var builder = LucentApplication
            .CreateBuilder()
            .UseHost(new InterleavingPumpingHost(firstMounted, secondMounted))
            .UseHosting(
                session =>
                {
                    var hostBuilder = HostedApplication.CreateBuilder();
                    hostBuilder.Services.AddSingleton(new HostingMarker(session.Title));
                    return hostBuilder.Build();
                },
                (services, _) =>
                {
                    var marker = services.GetRequiredService<HostingMarker>();
                    providerByOwner[Environment.CurrentManagedThreadId] = marker.Title;
                    return ValueTask.FromResult(true);
                }
            )
            .OnPrepareClose(
                (context, _) =>
                {
                    Assert.IsTrue(
                        providerByOwner.TryGetValue(
                            Environment.CurrentManagedThreadId,
                            out var providerTitle
                        )
                    );
                    Assert.AreEqual(
                        context.Session.Title,
                        providerTitle,
                        "Close preparation used another built application's service provider."
                    );
                    return ValueTask.FromResult(true);
                }
            );

        var first = builder.SetTitle("first").Build(() => HostMarkerRoot("first"));
        var second = builder.SetTitle("second").Build(() => HostMarkerRoot("second"));
        var firstRun = Task.Factory.StartNew(
            first.Run,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        );
        var secondRun = Task.Factory.StartNew(
            second.Run,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        );

        Task.WaitAll(firstRun, secondRun);

        Assert.AreEqual(0, firstRun.Result);
        Assert.AreEqual(0, secondRun.Result);
    }

    [TestMethod]
    public void FailedCloseCanRetryWithoutDuplicatingAcceptedPendingWork()
    {
        var first = NewGate();
        var accepted = NewGate();
        var attempts = 0;
        var acceptedWork = 0;
        string? closeError = null;
        var builder = HostedApplication.CreateBuilder();
        var lifecycle = new HostedApplication(
            _ => builder.Build(),
            (_, _) => EmptyRecipe(),
            async (_, cancellationToken) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    await first.Task;
                    closeError = "save failed";
                    return false;
                }

                Interlocked.Increment(ref acceptedWork);
                await accepted.Task;
                return true;
            }
        );
        var host = new PumpingHost(session =>
        {
            session.RequestClose();
            session.RequestClose();
            PumpingHost.WaitFor(
                session,
                () => session.Status.Phase == ApplicationPhase.PreparingClose
            );
            first.SetResult();
            PumpingHost.WaitFor(
                session,
                () =>
                    session.Status.Phase == ApplicationPhase.Running && session.Status.Error is null
            );
            Assert.AreEqual("save failed", closeError);

            session.RequestClose();
            PumpingHost.WaitFor(
                session,
                () => session.Status.Phase == ApplicationPhase.PreparingClose
            );
            session.RequestClose();
            session.RequestClose();
            accepted.SetResult();
        });
        var app = LucentApplication.CreateBuilder().UseHost(host).Build();

        Assert.AreEqual(0, app.Run(lifecycle));
        Assert.AreEqual(2, attempts, "Close retry did not run exactly one new preparation.");
        Assert.AreEqual(
            1,
            acceptedWork,
            "Repeated close requests duplicated work already accepted by preparation."
        );
    }

    [TestMethod]
    public void WorkerStopApplicationUsesCloseNegotiationAndPreservesAcceptedWork()
    {
        var first = NewGate();
        var accepted = NewGate();
        var owner = Environment.CurrentManagedThreadId;
        var prepareThreads = new List<int>();
        var attempts = 0;
        var acceptedWork = 0;
        var worker = 0;
        IHostApplicationLifetime? lifetime = null;
        var builder = HostedApplication.CreateBuilder();
        var lifecycle = new HostedApplication(
            _ =>
            {
                var host = builder.Build();
                lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
                return host;
            },
            (_, _) => EmptyRecipe(),
            async (_, cancellationToken) =>
            {
                prepareThreads.Add(Environment.CurrentManagedThreadId);
                attempts++;
                if (attempts == 1)
                {
                    await first.Task;
                    return false;
                }

                Interlocked.Increment(ref acceptedWork);
                await accepted.Task;
                return true;
            }
        );
        var hostAdapter = new PumpingHost(session =>
        {
            // A pool task may inline when this pool-owned test waits on it. Require a real worker.
            Task.Factory.StartNew(
                    () =>
                    {
                        worker = Environment.CurrentManagedThreadId;
                        lifetime!.StopApplication();
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                )
                .GetAwaiter()
                .GetResult();
            PumpingHost.WaitFor(
                session,
                () => session.Status.Phase == ApplicationPhase.PreparingClose
            );
            first.SetResult();
            PumpingHost.WaitFor(session, () => session.Status.Phase == ApplicationPhase.Running);
            session.RequestClose();
            PumpingHost.WaitFor(
                session,
                () => session.Status.Phase == ApplicationPhase.PreparingClose
            );
            session.RequestClose();
            accepted.SetResult();
        });
        var app = LucentApplication.CreateBuilder().UseHost(hostAdapter).Build();

        Assert.AreEqual(0, app.Run(lifecycle));
        Assert.AreNotEqual(
            owner,
            worker,
            "StopApplication was not raised from the worker fixture."
        );
        CollectionAssert.AreEqual(
            new List<int> { owner, owner },
            prepareThreads,
            "The host lifetime callback bypassed owner-thread close negotiation."
        );
        Assert.AreEqual(2, attempts, "The declined host-lifetime close could not retry.");
        Assert.AreEqual(
            1,
            acceptedWork,
            "Host-lifetime and repeated close requests duplicated accepted work."
        );
    }

    [TestMethod]
    public void StartupFailureStillStopsAndDisposesTheOfficialHost()
    {
        var events = new List<string>();
        var builder = HostedApplication.CreateBuilder();
        builder.Services.AddSingleton(events);
        builder.Services.AddSingleton<IHostedService>(_ => new FailingStartService(events));
        var lifecycle = new HostedApplication(
            _ => builder.Build(),
            (_, _) => throw new AssertFailedException("Root factory ran after startup failed.")
        );
        var app = LucentApplication.CreateBuilder().UseHost(new PumpingHost()).Build();

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => app.Run(lifecycle));
        Assert.AreEqual("start failed", failure.Message);
        CollectionAssert.AreEqual(
            new List<string> { "service-start", "service-stop", "service-dispose" },
            events,
            "Startup failure did not cross the stop and host-disposal boundaries exactly once."
        );
    }

    [TestMethod]
    public void StopCompositionScopeAndHostFailuresAreAllPreserved()
    {
        var events = new List<string>();
        var builder = HostedApplication.CreateBuilder();
        builder.Services.AddSingleton(events);
        builder.Services.AddSingleton<IHostedService>(_ => new FailingStopService(events));
        builder.Services.AddScoped(_ => new FailingScopedResource(events));
        var lifecycle = new HostedApplication(
            _ => new FailingDisposeHost(builder.Build(), events),
            (services, _) =>
            {
                var resource = services.GetRequiredService<FailingScopedResource>();
                Assert.IsNotNull(resource);
                return ComponentRecipe.Create(
                    "failing-root",
                    (_, root) =>
                        root.Scope.OnDispose(() =>
                        {
                            events.Add("ui-dispose");
                            throw new InvalidOperationException("ui dispose failed");
                        })
                );
            }
        );
        var app = LucentApplication
            .CreateBuilder()
            .UseHost(new PumpingHost(session => session.RequestClose()))
            .Build();

        var failure = Assert.ThrowsExactly<AggregateException>(() => app.Run(lifecycle));
        var messages = Flatten(failure).Select(error => error.Message).ToArray();
        foreach (
            var expected in new List<string>
            {
                "stop failed",
                "ui dispose failed",
                "scope dispose failed",
                "host dispose failed",
            }
        )
            CollectionAssert.Contains(messages, expected, $"Cleanup lost '{expected}'.");
        CollectionAssert.AreEqual(
            new List<string>
            {
                "service-start",
                "service-stop",
                "ui-dispose",
                "scope-dispose",
                "host-dispose",
                "service-dispose",
            },
            events,
            "A failed cleanup boundary prevented a later independent boundary from being attempted."
        );
    }

    [TestMethod]
    public void LifecycleBindingBorrowsFromTheExistingApplicationScopeUntilUiCleanup()
    {
        var events = new List<string>();
        var builder = HostedApplication.CreateBuilder();
        builder.Services.AddSingleton(events);
        builder.Services.AddSingleton<IHostedService>(_ => new RecordingHostedService(events));
        builder.Services.AddScoped(_ => new BoundScopedService(events));
        var resolutions = 0;
        var lifecycle = new HostedApplication(
            _ => builder.Build(),
            (services, _) =>
            {
                var expected = services.GetRequiredService<BoundScopedService>();
                return ComponentRecipe.Defer(
                    "bound-service-consumer",
                    ComponentRequirements.Service<BoundScopedService>(
                        new(
                            "service",
                            typeof(BoundScopedService).FullName!,
                            "HostingTests.lui",
                            1,
                            1
                        )
                    ),
                    (owner, service) =>
                    {
                        resolutions++;
                        Assert.AreSame(expected, service);
                        events.Add("component-setup");
                        owner.OnDispose(() =>
                        {
                            Assert.IsFalse(service.IsDisposed);
                            events.Add("component-dispose");
                        });
                        return EmptyRecipe();
                    }
                );
            }
        );
        var app = LucentApplication
            .CreateBuilder()
            .UseHost(new PumpingHost(session => session.RequestClose()))
            .Build();

        Assert.AreEqual(0, app.Run(lifecycle));
        Assert.AreEqual(1, resolutions);
        CollectionAssert.AreEqual(BoundServiceEvents, events);
    }

    private static TaskCompletionSource NewGate() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    [TestMethod]
    public void MissingRequirementDoesNotDisposePreviouslyResolvedServicesDuringRollback()
    {
        var events = new List<string>();
        var builder = HostedApplication.CreateBuilder();
        builder.Services.AddSingleton<IHostedService>(_ => new RecordingHostedService(events));
        builder.Services.AddScoped(_ => new BoundScopedService(events));
        var lifecycle = new HostedApplication(
            _ => builder.Build(),
            (_, _) =>
                ComponentRecipe.Defer(
                    "requirements-fail",
                    ComponentRequirements
                        .Service<BoundScopedService>(
                            new("first", "BoundScopedService", "Failure.lui", 2, 1)
                        )
                        .AndService<UnregisteredService>(
                            new("missing", "UnregisteredService", "Failure.lui", 3, 1)
                        ),
                    (_, _) =>
                        throw new AssertFailedException(
                            "State initialized before requirements succeeded."
                        )
                )
        );
        var app = LucentApplication.CreateBuilder().UseHost(new PumpingHost()).Build();
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => app.Run(lifecycle));
        StringAssert.Contains(error.Message, "missing");
        StringAssert.Contains(error.Message, "Failure.lui:3:1");
        CollectionAssert.AreEqual(MissingRequirementEvents, events);
    }

    [TestMethod]
    public void TerminalStopRejectsNewMountsBeforeTheHostStopsAndLeavesCachedBorrowersAlive()
    {
        var builder = HostedApplication.CreateBuilder();
        var events = new List<string>();
        ConditionalRegion? late = null;
        BoundScopedService? borrowed = null;
        var rejected = false;
        builder.Services.AddScoped(_ => new BoundScopedService(events));
        builder.Services.AddSingleton<IHostedService>(_ => new StopCallbackService(() =>
        {
            Assert.IsNotNull(late);
            Assert.IsNotNull(borrowed);
            Assert.IsFalse(borrowed.IsDisposed);
            var error = Assert.ThrowsExactly<InvalidOperationException>(() => late.Update(true));
            StringAssert.Contains(error.Message, "no longer accepting mounts");
            rejected = true;
        }));
        var lifecycle = new HostedApplication(
            _ => builder.Build(),
            (_, _) =>
                ComponentRecipe.Defer(
                    "stop-consumer",
                    ComponentRequirements.Service<BoundScopedService>(
                        new("service", "BoundScopedService", "Stop.lui", 1, 1)
                    ),
                    (owner, service) =>
                    {
                        borrowed = service;
                        owner.OnDispose(() => Assert.IsFalse(service.IsDisposed));
                        return ComponentRecipe.Create(
                            "stop-root",
                            (context, element) =>
                            {
                                late = context.When(
                                    element,
                                    "late",
                                    static () => false,
                                    mounted => mounted.Element("late-child")
                                );
                            }
                        );
                    }
                )
        );
        var app = LucentApplication
            .CreateBuilder()
            .UseHost(new PumpingHost(session => session.RequestClose()))
            .Build();
        Assert.AreEqual(0, app.Run(lifecycle));
        Assert.IsTrue(rejected);
        Assert.IsNotNull(borrowed);
        Assert.IsTrue(borrowed.IsDisposed);
    }

    private sealed class UnregisteredService;

    private sealed class StopCallbackService(Action stop) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            stop();
            return Task.CompletedTask;
        }
    }

    private static ComponentRecipe EmptyRecipe() => ComponentRecipe.Create("empty", (_, _) => { });

    private static ComponentRecipe HostMarkerRoot(string expectedTitle) =>
        ComponentRecipe.Defer(
            "hosting-snapshot-root",
            ComponentRequirements.Service<HostingMarker>(
                new("service", typeof(HostingMarker).FullName!, "HostingTests.lui", 1, 1)
            ),
            (_, marker) =>
            {
                Assert.AreEqual(expectedTitle, marker.Title);
                return EmptyRecipe();
            }
        );

    private static IEnumerable<Exception> Flatten(Exception error) =>
        error is AggregateException aggregate
            ? aggregate.InnerExceptions.SelectMany(Flatten)
            : [error];

    private sealed class PumpingHost(Action<ApplicationSession>? running = null, int exitCode = 0)
        : IApplicationHost
    {
        public int Run(ApplicationSession session)
        {
            session.Start();
            WaitFor(
                session,
                () => session.IsCompleted || session.Status.Phase == ApplicationPhase.Running
            );
            if (!session.IsCompleted)
                running?.Invoke(session);
            WaitFor(session, () => session.IsCompleted);
            return exitCode;
        }

        internal static void WaitFor(ApplicationSession session, Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 5000;
            while (!condition())
            {
                session.ProcessEvents();
                if (!session.Composition.IsDisposed)
                    session.Composition.Flush();
                if (Environment.TickCount64 >= deadline)
                    Assert.Fail("Application lifecycle did not reach the expected state.");
                Thread.Sleep(1);
            }
            session.ProcessEvents();
            if (!session.Composition.IsDisposed)
                session.Composition.Flush();
        }
    }

    private sealed class InterleavingPumpingHost(
        ManualResetEventSlim firstMounted,
        ManualResetEventSlim secondMounted
    ) : IApplicationHost
    {
        public int Run(ApplicationSession session)
        {
            session.Start();
            PumpingHost.WaitFor(
                session,
                () => session.IsCompleted || session.Status.Phase == ApplicationPhase.Running
            );
            if (session.IsCompleted)
                return 0;

            if (session.Title == "first")
            {
                firstMounted.Set();
                if (!secondMounted.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("The second application did not mount.");
            }
            else if (session.Title == "second")
            {
                if (!firstMounted.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("The first application did not mount.");
                secondMounted.Set();
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unexpected test application '{session.Title}'."
                );
            }

            session.RequestClose();
            PumpingHost.WaitFor(session, () => session.IsCompleted);
            return 0;
        }
    }

    private sealed record HostingMarker(string Title);

    private sealed class RecordingHostedService(List<string> events) : IHostedService, IDisposable
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            events.Add("service-start");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            events.Add("service-stop");
            return Task.CompletedTask;
        }

        public void Dispose() => events.Add("service-dispose");
    }

    private sealed class ScopedModel(List<string> events) : IDisposable
    {
        internal int CreatedThread { get; } = RecordCreate(events);

        public void Dispose() => events.Add("model-dispose");

        private static int RecordCreate(List<string> events)
        {
            events.Add("model-create");
            return Environment.CurrentManagedThreadId;
        }
    }

    private sealed class SyncScopedResource(List<string> events) : IDisposable
    {
        public void Dispose() => events.Add("sync-scope-dispose");
    }

    private sealed class AsyncScopedResource(List<string> events) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            events.Add("async-scope-dispose");
        }
    }

    private sealed class FailingStartService(List<string> events) : IHostedService, IDisposable
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            events.Add("service-start");
            throw new InvalidOperationException("start failed");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            events.Add("service-stop");
            return Task.CompletedTask;
        }

        public void Dispose() => events.Add("service-dispose");
    }

    private sealed class FailingStopService(List<string> events) : IHostedService, IDisposable
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            events.Add("service-start");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            events.Add("service-stop");
            throw new InvalidOperationException("stop failed");
        }

        public void Dispose() => events.Add("service-dispose");
    }

    private sealed class FailingScopedResource(List<string> events) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            events.Add("scope-dispose");
            return ValueTask.FromException(new InvalidOperationException("scope dispose failed"));
        }
    }

    private sealed class FailingDisposeHost(IHost inner, List<string> events)
        : IHost,
            IAsyncDisposable
    {
        public IServiceProvider Services => inner.Services;

        public Task StartAsync(CancellationToken cancellationToken = default) =>
            inner.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken = default) =>
            inner.StopAsync(cancellationToken);

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        public async ValueTask DisposeAsync()
        {
            events.Add("host-dispose");
            if (inner is IAsyncDisposable asyncInner)
                await asyncInner.DisposeAsync();
            else
                inner.Dispose();
            throw new InvalidOperationException("host dispose failed");
        }
    }

    private sealed class BoundScopedService : IDisposable
    {
        private readonly List<string> _events;

        public BoundScopedService(List<string> events)
        {
            _events = events;
            events.Add("bound-service-create");
        }

        internal bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
            _events.Add("bound-service-dispose");
        }
    }
}
