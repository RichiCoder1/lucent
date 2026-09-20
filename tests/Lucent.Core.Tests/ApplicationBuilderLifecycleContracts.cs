using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ApplicationBuilderLifecycleContracts
{
    private static readonly string[] SuccessfulLifecycleEvents =
    [
        "start-one",
        "start-two",
        "factory",
        "configure-root",
        "root-setup",
        "root-mount",
        "mounted",
        "prepare-one",
        "prepare-two",
        "restore-two",
        "restore-one",
        "prepare-one",
        "prepare-two",
        "resource-stop-two",
        "resource-stop-one",
        "builder-stop",
        "ui-dispose",
        "resource-dispose-two",
        "resource-dispose-one",
        "builder-dispose",
    ];

    private static readonly string[] FailedStartupEvents =
    [
        "start-one",
        "start-two",
        "resource-stop-two",
        "resource-stop-one",
        "builder-stop",
        "ui-dispose",
        "resource-dispose-two",
        "resource-dispose-one",
        "builder-dispose",
    ];

    [TestMethod]
    public void DeferredRootRunsAfterStartupDecoratesAfterCreationAndRestoresDeclinedClose()
    {
        var events = new List<string>();
        var rootContext = new RootCapability("root-context");
        var prepareCount = 0;
        var mountedInRecipe = false;
        var rootFactoryCalls = 0;
        var factoryThread = 0;
        var expectedTheme = (ThemeContext?)null;
        ApplicationStartupOutcome? stopOutcome = null;
        ApplicationStartupOutcome? disposeOutcome = null;
        var root = ComponentRecipe.Defer(
            "deferred-lifecycle-root",
            ComponentRequirements.Context<RootCapability>(Source("root-context")),
            (owner, values) =>
            {
                Assert.AreSame(rootContext, values);
                events.Add("root-setup");
                owner.OnDispose(() => events.Add("ui-dispose"));
                return ComponentRecipe.Create(
                    "mounted-root",
                    (mount, _) =>
                    {
                        Assert.AreSame(expectedTheme, mount.Theme);
                        events.Add("root-mount");
                        mountedInRecipe = true;
                    }
                );
            }
        );
        var builder = LucentApplication
            .CreateBuilder()
            .UseHost(
                new PumpingHost(session =>
                {
                    expectedTheme = session.Theme;
                    session.RequestClose();
                    PumpUntil(
                        session,
                        () => prepareCount == 1 && session.Status.Phase == ApplicationPhase.Running
                    );
                    session.RequestClose();
                })
            )
            .OnStart(start =>
            {
                events.Add("start-one");
                start
                    .ProvideRootContext(rootContext)
                    .OnStop(() =>
                    {
                        events.Add("resource-stop-one");
                        return ValueTask.CompletedTask;
                    })
                    .OnDispose(() =>
                    {
                        events.Add("resource-dispose-one");
                        return ValueTask.CompletedTask;
                    });
                return ValueTask.CompletedTask;
            })
            .OnStart(async start =>
            {
                events.Add("start-two");
                await Task.Yield();
                start.OnStop(() =>
                {
                    events.Add("resource-stop-two");
                    return ValueTask.CompletedTask;
                });
                start.OnDispose(() =>
                {
                    events.Add("resource-dispose-two");
                    return ValueTask.CompletedTask;
                });
            })
            .ConfigureRoot(
                (session, recipe) =>
                {
                    events.Add("configure-root");
                    expectedTheme = session.Theme;
                    Assert.AreSame(expectedTheme, session.Theme);
                    return recipe;
                }
            )
            .OnMounted(session =>
            {
                Assert.IsTrue(
                    mountedInRecipe,
                    "Mounted notification ran before root mount completed."
                );
                Assert.IsTrue(session.Composition.Root.HasPresentation);
                events.Add("mounted");
                return ValueTask.CompletedTask;
            })
            .OnPrepareClose(
                (attempt, _) =>
                {
                    events.Add("prepare-one");
                    attempt.OnDeclined(() => events.Add("restore-one"));
                    return ValueTask.FromResult(true);
                }
            )
            .OnPrepareClose(
                (attempt, _) =>
                {
                    events.Add("prepare-two");
                    attempt.OnDeclined(() => events.Add("restore-two"));
                    prepareCount++;
                    return ValueTask.FromResult(prepareCount != 1);
                }
            )
            .OnStop(context =>
            {
                stopOutcome = context.Startup;
                events.Add("builder-stop");
                return ValueTask.CompletedTask;
            })
            .OnDispose(context =>
            {
                disposeOutcome = context.Startup;
                events.Add("builder-dispose");
                return ValueTask.CompletedTask;
            });

        Assert.AreEqual(
            0,
            rootFactoryCalls,
            "The builder invoked a deferred factory during Build."
        );
        var app = builder.Build(() =>
        {
            rootFactoryCalls++;
            factoryThread = Environment.CurrentManagedThreadId;
            Assert.AreEqual("start-two", events[^1]);
            events.Add("factory");
            return root;
        });

        Assert.AreEqual(0, rootFactoryCalls, "Build invoked the root factory.");
        Assert.AreEqual(0, app.Run());
        Assert.AreEqual(1, rootFactoryCalls);
        Assert.AreEqual(Environment.CurrentManagedThreadId, factoryThread);
        Assert.IsNotNull(stopOutcome);
        Assert.IsNotNull(disposeOutcome);
        Assert.IsTrue(stopOutcome.ServicesReady);
        Assert.IsTrue(stopOutcome.RootFactoryInvoked);
        Assert.IsTrue(stopOutcome.RootCreated);
        Assert.IsTrue(stopOutcome.RootMounted);
        Assert.IsNull(stopOutcome.Error);
        Assert.IsTrue(disposeOutcome.RootMounted);

        CollectionAssert.AreEqual(SuccessfulLifecycleEvents, events);
    }

    [TestMethod]
    public void FailedStartupRunsOnlyAcquiredCleanupAndPreservesAllFailures()
    {
        var events = new List<string>();
        ApplicationStartupOutcome? outcome = null;
        var rootFactoryCalls = 0;
        var app = LucentApplication
            .CreateBuilder()
            .UseHost(new PumpingHost())
            .OnStart(start =>
            {
                events.Add("start-one");
                start.Session.Scope.OnDispose(() => events.Add("ui-dispose"));
                start.OnStop(() =>
                {
                    events.Add("resource-stop-one");
                    return ValueTask.CompletedTask;
                });
                start.OnDispose(() =>
                {
                    events.Add("resource-dispose-one");
                    return ValueTask.CompletedTask;
                });
                return ValueTask.CompletedTask;
            })
            .OnStart(start =>
            {
                events.Add("start-two");
                start.OnStop(() =>
                {
                    events.Add("resource-stop-two");
                    throw new InvalidOperationException("stop-two");
                });
                start.OnDispose(() =>
                {
                    events.Add("resource-dispose-two");
                    throw new InvalidOperationException("dispose-two");
                });
                throw new InvalidOperationException("startup failed");
            })
            .OnStart(_ =>
            {
                events.Add("unentered-start");
                return ValueTask.CompletedTask;
            })
            .OnStop(context =>
            {
                outcome = context.Startup;
                events.Add("builder-stop");
                throw new InvalidOperationException("builder-stop failed");
            })
            .OnDispose(context =>
            {
                Assert.AreSame(outcome, context.Startup);
                events.Add("builder-dispose");
                return ValueTask.CompletedTask;
            })
            .Build(() =>
            {
                rootFactoryCalls++;
                return EmptyRecipe();
            });

        var failure = Assert.ThrowsExactly<AggregateException>(() => app.Run());
        var messages = Flatten(failure).Select(error => error.Message).ToArray();
        CollectionAssert.Contains(messages, "startup failed");
        CollectionAssert.Contains(messages, "stop-two");
        CollectionAssert.Contains(messages, "builder-stop failed");
        CollectionAssert.Contains(messages, "dispose-two");
        Assert.AreEqual(0, rootFactoryCalls);
        Assert.IsNotNull(outcome);
        Assert.IsFalse(outcome.ServicesReady);
        Assert.IsFalse(outcome.RootFactoryInvoked);
        Assert.IsFalse(outcome.RootCreated);
        Assert.IsFalse(outcome.RootMounted);
        Assert.AreEqual("startup failed", outcome.Error?.Message);
        CollectionAssert.AreEqual(FailedStartupEvents, events);
    }

    [TestMethod]
    public void BuildFixedRootSupportsParameterlessRunAndMissingRootIsReported()
    {
        var recipe = EmptyRecipe();
        var builder = LucentApplication.CreateBuilder().UseHost(new PumpingHost());
        Assert.AreEqual(0, builder.Build(recipe).Run());

        var appWithoutRoot = builder.Build();
        Assert.ThrowsExactly<InvalidOperationException>(() => appWithoutRoot.Run());
        Assert.AreEqual(0, appWithoutRoot.Run(() => recipe));
    }

    private static ComponentRequirementSource Source(string member) =>
        new(member, typeof(RootCapability).FullName!, "Application.lui", 1, 1);

    private static ComponentRecipe EmptyRecipe() =>
        ComponentRecipe.Create("empty-lifecycle-root", static (_, _) => { });

    private static IEnumerable<Exception> Flatten(Exception error) =>
        error is AggregateException aggregate
            ? aggregate.InnerExceptions.SelectMany(Flatten)
            : [error];

    private static void PumpUntil(ApplicationSession session, Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 5_000;
        while (!condition())
        {
            session.ProcessEvents();
            if (!session.Composition.IsDisposed)
                session.Composition.Flush();
            if (Environment.TickCount64 >= deadline)
                Assert.Fail("The application lifecycle did not reach the expected state.");
            Thread.Sleep(1);
        }
        session.ProcessEvents();
        if (!session.Composition.IsDisposed)
            session.Composition.Flush();
    }

    private sealed record RootCapability(string Name);

    private sealed class PumpingHost(Action<ApplicationSession>? whenRunning = null)
        : IApplicationHost
    {
        public int Run(ApplicationSession session)
        {
            session.Start();
            PumpUntil(
                session,
                () => session.IsCompleted || session.Status.Phase == ApplicationPhase.Running
            );
            if (!session.IsCompleted)
            {
                if (whenRunning is null)
                    session.RequestClose();
                else
                    whenRunning(session);
            }
            PumpUntil(session, () => session.IsCompleted);
            return 0;
        }
    }
}
