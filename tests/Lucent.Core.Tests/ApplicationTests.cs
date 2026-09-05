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
            (composition, theme) =>
            {
                Assert.AreEqual(
                    "Light-Normal",
                    theme.Theme.Name,
                    "The initial theme was not created once."
                );
                theme.Appearance = new ThemeAppearance(ThemeColorScheme.Dark, ThemeContrast.Normal);
                composition.Graph.Drain();
                Assert.AreEqual(
                    "Dark-Normal",
                    theme.Theme.Name,
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
        Assert.AreEqual(0, host.RunCount, "The host ran after the root recipe failed to mount.");

        var hostCleanup = 0;
        var hostFailure = LucentApplication
            .CreateBuilder()
            .UseHost(new RecordingHost(0, (_, _) => throw new InvalidOperationException("host")))
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
            .UseHost(new RecordingHost(0, (_, _) => throw new InvalidOperationException("primary")))
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
        Assert.ThrowsExactly<ArgumentNullException>(() => first.Run(null!));
        Assert.AreEqual(0, first.Run(EmptyRecipe()), "The first application run failed.");
        Assert.ThrowsExactly<InvalidOperationException>(() => first.Run(EmptyRecipe()));
        Assert.AreEqual(
            0,
            builder.Build().Run(EmptyRecipe()),
            "A fresh build was not independently runnable."
        );
    }

    private static ComponentRecipe EmptyRecipe() => ComponentRecipe.Create("empty", (_, _) => { });

    private static IEnumerable<Exception> Flatten(Exception exception) =>
        exception is AggregateException aggregate
            ? aggregate.InnerExceptions.SelectMany(Flatten)
            : [exception];

    private sealed class RecordingHost(int exitCode, Action<Composition, ThemeContext>? run = null)
        : IApplicationHost
    {
        private Composition? _composition;

        internal string? Title { get; private set; }
        internal Theme? Theme { get; private set; }
        internal int RunCount { get; private set; }
        internal bool CompositionDisposed => _composition?.IsDisposed == true;

        public int Run(string title, Composition composition, ThemeContext theme)
        {
            Title = title;
            Theme = theme.Theme;
            RunCount++;
            _composition = composition;
            run?.Invoke(composition, theme);
            return exitCode;
        }
    }
}
