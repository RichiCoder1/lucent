using System.Globalization;
using Lucent.Core;
using Lucent.Testing;
using Lucent.Testing.Skia;
using Microsoft.Extensions.Time.Testing;

namespace Lucent.Preview.Tests;

[TestClass]
public sealed class PreviewCatalogTests
{
    private static readonly DateTimeOffset InitialTime = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly ThemeAppearance DarkAppearance = new(
        ThemeColorScheme.Dark,
        ThemeContrast.Normal
    );
    private static readonly string[] ScenarioIds = ["card", "Card"];
    private static readonly string[] CancelledLifecycle = ["stop", "dispose"];
    private static readonly string[] ServiceLifecycle =
    [
        "mount",
        "stop",
        "component-dispose",
        "dispose",
    ];

    [TestMethod]
    public void CatalogIsInertAndUsesOrdinalUniqueIds()
    {
        var setups = 0;
        var roots = 0;
        var builder = new PreviewCatalogBuilder();
        builder.Add(
            Descriptor("card"),
            (_, _) => ValueTask.FromResult(++setups),
            (_, _) =>
            {
                roots++;
                return Components.Text("fixture");
            }
        );
        Assert.Throws<ArgumentException>(() =>
            builder.Add(
                Descriptor("card"),
                (_, _) => ValueTask.FromResult(0),
                (_, _) => Components.Text("duplicate")
            )
        );
        builder.Add(
            Descriptor("Card"),
            (_, _) => ValueTask.FromResult(0),
            (_, _) => Components.Text("distinct")
        );
        var catalog = builder.Build();
        CollectionAssert.AreEqual(
            ScenarioIds,
            catalog.Scenarios.Select(scenario => scenario.Descriptor.Id).ToArray()
        );
        Assert.AreNotSame(catalog.Get("card"), catalog.Get("Card"));
        Assert.AreEqual(0, setups);
        Assert.AreEqual(0, roots);
    }

    [TestMethod]
    public async Task EachLaunchOwnsFreshFixtureAndPreservesExplicitData()
    {
        var setups = 0;
        var builder = new PreviewCatalogBuilder();
        builder.Add(
            Descriptor("fresh"),
            (_, _) => ValueTask.FromResult(Interlocked.Increment(ref setups)),
            (sequence, _) => Components.Text("Authored value " + sequence)
        );
        var scenario = builder.Build().Get("fresh");
        await using var first = await Start(scenario);
        await using var second = await Start(scenario);
        using var firstFrame = await first.SnapshotAsync();
        using var secondFrame = await second.SnapshotAsync();
        firstFrame.Require(SemanticRole.Text, "Authored value 1");
        secondFrame.Require(SemanticRole.Text, "Authored value 2");
        Assert.AreEqual(2, setups);
        Assert.IsTrue(await first.InvokeAsync(context => context.Composition.Design.IsDesignMode));
    }

    [TestMethod]
    public async Task CancelledBeforeSetupDoesNotExecuteUserCode()
    {
        var setups = 0;
        var roots = 0;
        var builder = new PreviewCatalogBuilder();
        builder.Add(
            Descriptor("cancelled"),
            (_, _) => ValueTask.FromResult(++setups),
            (_, _) =>
            {
                roots++;
                return Components.Text("must not mount");
            }
        );
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var failure = await Assert.ThrowsAsync<Exception>(() =>
            Start(builder.Build().Get("cancelled"), cancellation.Token)
        );
        Assert.IsTrue(ContainsCancellation(failure), failure.ToString());
        Assert.AreEqual(0, setups);
        Assert.AreEqual(0, roots);
    }

    [TestMethod]
    public async Task ObsoleteSetupCleansItsResourcesWithoutPublishingRoot()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();
        var roots = 0;
        PreviewSetupContext? retained = null;
        var builder = new PreviewCatalogBuilder();
        builder.Add(
            Descriptor("obsolete"),
            async (context, _) =>
            {
                retained = context;
                context.OnStop(() =>
                {
                    events.Add("stop");
                    return ValueTask.CompletedTask;
                });
                context.OnDispose(() =>
                {
                    events.Add("dispose");
                    return ValueTask.CompletedTask;
                });
                entered.SetResult();
                await complete.Task;
                return 1;
            },
            (_, _) =>
            {
                roots++;
                return Components.Text("obsolete root");
            }
        );
        using var cancellation = new CancellationTokenSource();
        var start = Start(builder.Build().Get("obsolete"), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        cancellation.Cancel();
        complete.SetResult();
        var failure = await Assert.ThrowsAsync<Exception>(() => start);
        Assert.IsTrue(ContainsCancellation(failure), failure.ToString());
        Assert.AreEqual(0, roots);
        CollectionAssert.AreEqual(CancelledLifecycle, events);
        Assert.Throws<InvalidOperationException>(() =>
            retained!.OnDispose(() => ValueTask.CompletedTask)
        );
    }

    [TestMethod]
    public async Task SetupFailureDisposesPartialAcquisitionsAndExpiresContext()
    {
        var disposed = 0;
        var roots = 0;
        PreviewSetupContext? retained = null;
        var builder = new PreviewCatalogBuilder();
        builder.Add<int>(
            Descriptor("failure"),
            (context, _) =>
            {
                retained = context;
                context.OnDispose(() =>
                {
                    disposed++;
                    return ValueTask.CompletedTask;
                });
                throw new InvalidOperationException("controlled fixture failure");
            },
            (_, _) =>
            {
                roots++;
                return Components.Text("unreachable");
            }
        );
        var failure = await Assert.ThrowsAsync<Exception>(() =>
            Start(builder.Build().Get("failure"))
        );
        StringAssert.Contains(failure.ToString(), "controlled fixture failure");
        Assert.AreEqual(1, disposed);
        Assert.AreEqual(0, roots);
        Assert.Throws<InvalidOperationException>(() => retained!.ProvideRootContext("late"));
    }

    [TestMethod]
    public async Task CancellationDuringRootConstructionRejectsTheReturnedRecipe()
    {
        using var cancellation = new CancellationTokenSource();
        var roots = 0;
        var mounts = 0;
        var disposed = 0;
        var scenario = new PreviewCatalogBuilder()
            .Add(
                Descriptor("cancel-root"),
                (context, _) =>
                {
                    context.OnDispose(() =>
                    {
                        disposed++;
                        return ValueTask.CompletedTask;
                    });
                    return ValueTask.FromResult(0);
                },
                (_, _) =>
                {
                    roots++;
                    cancellation.Cancel();
                    return Component.Define(
                        "rejected-root",
                        _ =>
                        {
                            mounts++;
                            return Components.Text("must not mount");
                        }
                    );
                }
            )
            .Build()
            .Get("cancel-root");
        var failure = await Assert.ThrowsAsync<Exception>(() =>
            Start(scenario, cancellation.Token)
        );
        Assert.IsTrue(ContainsCancellation(failure), failure.ToString());
        Assert.AreEqual(1, roots);
        Assert.AreEqual(0, mounts);
        Assert.AreEqual(1, disposed);
    }

    [TestMethod]
    public async Task SetupCapabilitiesExpireAndBindingCannotCrossSessionsOrCreateAnotherRoot()
    {
        PreviewSetupContext? retained = null;
        PreviewScenarioBinding? binding = null;
        var catalog = new PreviewCatalogBuilder()
            .Add(
                Descriptor("single-root"),
                (context, _) =>
                {
                    retained = context;
                    return ValueTask.FromResult("ready");
                },
                (value, _) => Components.Text(value)
            )
            .Build();
        var clock = new FakeTimeProvider(InitialTime);
        await using var first = await HeadlessApplication.StartAsync(
            context => binding!.CreateRoot(context.Session),
            builder => binding = catalog.Get("single-root").Bind(builder, clock),
            new HeadlessApplicationOptions
            {
                Purpose = CompositionPurpose.Preview,
                TimeProviderFactory = () => clock,
            }
        );
        await first.InvokeAsync(context =>
        {
            Assert.Throws<InvalidOperationException>(() => retained!.ProvideRootContext("late"));
            Assert.Throws<InvalidOperationException>(() =>
                retained!.OnDispose(() => ValueTask.CompletedTask)
            );
            Assert.Throws<InvalidOperationException>(() => binding!.CreateRoot(context.Session));
            return 0;
        });
        await using var other = await HeadlessApplication.StartAsync(
            Components.Text("another session")
        );
        await other.InvokeAsync(context =>
        {
            var failure = Assert.Throws<InvalidOperationException>(() =>
                binding!.CreateRoot(context.Session)
            );
            StringAssert.Contains(failure.Message, "session");
            return 0;
        });
    }

    [TestMethod]
    public async Task BorrowedServiceSurvivesComponentTeardownAndThenDisposesOnce()
    {
        var events = new List<string>();
        var service = new OwnedProbe(events);
        Element? mounted = null;
        var lateResolutions = 0;
        var catalog = new PreviewCatalogBuilder()
            .Add(
                Descriptor("services"),
                (context, _) =>
                {
                    var session = context.Session;
                    context.CreateServiceBinding(new ProbeSource(service));
                    context.OnDispose(() =>
                    {
                        service.Dispose();
                        return ValueTask.CompletedTask;
                    });
                    context.OnStop(() =>
                    {
                        service.Read("stop");
                        Assert.Throws<InvalidOperationException>(() =>
                            session.Composition.Mount(
                                mounted!,
                                session.Theme,
                                ComponentRecipe.Defer(
                                    "late-service",
                                    ComponentRequirements.Service<OwnedProbe>(
                                        new ComponentRequirementSource(
                                            "probe",
                                            "OwnedProbe",
                                            "ScenarioCard.lui",
                                            1,
                                            1
                                        )
                                    ),
                                    (_, _) =>
                                    {
                                        lateResolutions++;
                                        return Components.Text("must not mount");
                                    }
                                )
                            )
                        );
                        return ValueTask.CompletedTask;
                    });
                    return ValueTask.FromResult(0);
                },
                (_, _) =>
                    ComponentRecipe.Defer(
                        "service-fixture",
                        ComponentRequirements.Service<OwnedProbe>(
                            new ComponentRequirementSource(
                                "probe",
                                "OwnedProbe",
                                "ScenarioCard.lui",
                                1,
                                1
                            )
                        ),
                        (owner, probe) =>
                        {
                            probe.Read("mount");
                            owner.OnDispose(() => probe.Read("component-dispose"));
                            return Components.Text("borrowed service");
                        }
                    )
            )
            .Build();
        var application = await Start(catalog.Get("services"));
        mounted = await application.InvokeAsync(context =>
            context.Composition.Root.Children.Single()
        );
        using (var frame = await application.SnapshotAsync())
            frame.Require(SemanticRole.Text, "borrowed service");
        await application.DisposeAsync();
        CollectionAssert.AreEqual(ServiceLifecycle, events);
        Assert.AreEqual(1, service.DisposeCount);
        Assert.AreEqual(0, lateResolutions);
    }

    [TestMethod]
    public async Task LaterStartupFailureCannotInvokeFixtureFromCleanup()
    {
        PreviewScenarioBinding? binding = null;
        var roots = 0;
        var cleanupChecked = false;
        var scenario = new PreviewCatalogBuilder()
            .Add(
                Descriptor("late-failure"),
                (context, _) =>
                {
                    var session = context.Session;
                    context.OnDispose(() =>
                    {
                        Assert.Throws<InvalidOperationException>(() =>
                            binding!.CreateRoot(session)
                        );
                        cleanupChecked = true;
                        return ValueTask.CompletedTask;
                    });
                    return ValueTask.FromResult(1);
                },
                (_, _) =>
                {
                    roots++;
                    return Components.Text("released fixture");
                }
            )
            .Build()
            .Get("late-failure");
        var failure = await Assert.ThrowsAsync<Exception>(() =>
            HeadlessApplication.StartAsync(
                context => binding!.CreateRoot(context.Session),
                builder =>
                {
                    binding = scenario.Bind(builder, new FakeTimeProvider(InitialTime));
                    builder.OnStart(_ =>
                        throw new InvalidOperationException("later startup failure")
                    );
                },
                new HeadlessApplicationOptions { Purpose = CompositionPurpose.Preview }
            )
        );
        Assert.IsTrue(ContainsFailure(failure, "later startup failure"));
        Assert.IsTrue(cleanupChecked);
        Assert.AreEqual(0, roots);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SessionCloseRejectsUnpublishedRootWithoutExternalCancellation(
        bool duringFactory
    )
    {
        var roots = 0;
        var mounts = 0;
        var disposed = 0;
        var scenario = new PreviewCatalogBuilder()
            .Add(
                Descriptor("closing"),
                (context, _) =>
                {
                    context.OnDispose(() =>
                    {
                        disposed++;
                        return ValueTask.CompletedTask;
                    });
                    if (!duringFactory)
                        context.Session.RequestClose();
                    return ValueTask.FromResult(0);
                },
                (_, session) =>
                {
                    roots++;
                    session.RequestClose();
                    return Component.Define(
                        "unpublished",
                        _ =>
                        {
                            mounts++;
                            return Components.Text("must not mount");
                        }
                    );
                }
            )
            .Build()
            .Get("closing");
        var failure = await Assert.ThrowsAsync<Exception>(() => Start(scenario));
        Assert.IsTrue(ContainsFailure(failure, "active starting session"));
        Assert.AreEqual(duringFactory ? 1 : 0, roots);
        Assert.AreEqual(0, mounts);
        Assert.AreEqual(1, disposed);
    }

    [TestMethod]
    public async Task PresentationSnapshotsCultureAndSharesClockAndDensityWithFixture()
    {
        var culture = new CultureInfo("en-US");
        culture.NumberFormat.NumberDecimalSeparator = "!";
        var presentation = new PreviewPresentation(
            new LayoutViewport(320, 240, 1.5f),
            DarkAppearance,
            static _ => ControlThemes.Dark,
            1.25f,
            culture,
            CultureInfo.GetCultureInfo("fr-FR"),
            InitialTime
        );
        culture.NumberFormat.NumberDecimalSeparator = ":";
        Assert.IsTrue(presentation.Culture.IsReadOnly);
        var catalog = new PreviewCatalogBuilder()
            .Add(
                new PreviewScenarioDescriptor(
                    "presentation",
                    "Presentation fixture",
                    new PreviewSource("fixture.csproj", "ScenarioCard.lui", "ScenarioCard"),
                    presentation
                ),
                (context, _) =>
                {
                    Assert.AreEqual(InitialTime, context.Clock.GetUtcNow());
                    Assert.AreEqual(1.25f, context.Descriptor.Presentation.Density);
                    Assert.AreEqual("fr-FR", CultureInfo.CurrentUICulture.Name);
                    return ValueTask.FromResult(12.5m.ToString(CultureInfo.CurrentCulture));
                },
                (value, _) => Components.Text(value)
            )
            .Build();
        await using var application = await Start(catalog.Get("presentation"));
        using var frame = await application.SnapshotAsync();
        frame.Require(SemanticRole.Text, "12!5");
        Assert.AreEqual(new LayoutViewport(320, 240, 1.5f), frame.Scene.Viewport);
        Assert.AreEqual(
            DarkAppearance,
            await application.InvokeAsync(context => context.Session.Theme.Appearance)
        );
    }

    [TestMethod]
    public async Task ApplicationPurposeCannotSilentlyRunPreviewSetup()
    {
        var setups = 0;
        var scenario = new PreviewCatalogBuilder()
            .Add(
                Descriptor("purpose"),
                (_, _) => ValueTask.FromResult(++setups),
                (_, _) => Components.Text("must not mount")
            )
            .Build()
            .Get("purpose");
        PreviewScenarioBinding? binding = null;
        var failure = await Assert.ThrowsAsync<Exception>(() =>
            HeadlessApplication.StartAsync(
                context => binding!.CreateRoot(context.Session),
                builder => binding = scenario.Bind(builder, new FakeTimeProvider(InitialTime)),
                options: null
            )
        );
        Assert.IsTrue(ContainsFailure(failure, "explicit Preview composition purpose"));
        Assert.AreEqual(0, setups);
    }

    private static bool ContainsCancellation(Exception error) =>
        error is OperationCanceledException
        || error is AggregateException aggregate
            && aggregate.InnerExceptions.Any(ContainsCancellation)
        || error.InnerException is { } inner && ContainsCancellation(inner);

    private static bool ContainsFailure(Exception error, string message) =>
        error is InvalidOperationException
            && error.Message.Contains(message, StringComparison.Ordinal)
        || error is AggregateException aggregate
            && aggregate.InnerExceptions.Any(inner => ContainsFailure(inner, message))
        || error.InnerException is { } inner && ContainsFailure(inner, message);

    private sealed class OwnedProbe(List<string> events) : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Read(string stage)
        {
            ObjectDisposedException.ThrowIf(DisposeCount != 0, this);
            events.Add(stage);
        }

        public void Dispose()
        {
            DisposeCount++;
            events.Add("dispose");
        }
    }

    private sealed class ProbeSource(OwnedProbe probe) : IComponentServiceSource
    {
        public T Resolve<T>()
            where T : class =>
            probe as T ?? throw new InvalidOperationException("Unknown fixture service.");
    }

    internal static PreviewScenarioDescriptor Descriptor(string id) =>
        new(
            id,
            "Scenario " + id,
            new PreviewSource("fixture.csproj", "ScenarioCard.lui", "ScenarioCard"),
            new PreviewPresentation(
                new LayoutViewport(360, 440, 1),
                ThemeAppearance.Light,
                static _ => ControlThemes.Light,
                1,
                CultureInfo.GetCultureInfo("en-US"),
                CultureInfo.GetCultureInfo("en-US"),
                InitialTime
            )
        );

    internal static Task<HeadlessApplication> Start(
        PreviewScenario scenario,
        CancellationToken cancellationToken = default
    )
    {
        var presentation = scenario.Descriptor.Presentation;
        var clock = new FakeTimeProvider(presentation.InitialTime);
        PreviewScenarioBinding? binding = null;
        return SkiaHeadlessApplication.StartAsync(
            context => binding!.CreateRoot(context.Session),
            builder => binding = scenario.Bind(builder, clock, cancellationToken),
            new HeadlessApplicationOptions
            {
                Title = scenario.Descriptor.Title,
                Purpose = CompositionPurpose.Preview,
                Viewport = presentation.Viewport,
                Appearance = presentation.Appearance,
                ThemeFactory = presentation.ThemeFactory,
                Culture = presentation.Culture,
                UICulture = presentation.UICulture,
                TimeProviderFactory = () => clock,
            }
        );
    }
}
