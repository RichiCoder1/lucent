using Lucent.Core;

internal static class ApplicationContracts
{
    internal static int Run()
    {
        try
        {
            ConfigurationSnapshotsAndDefaults();
            NormalRunAndAppearance();
            FailureCleanup();
            PrimaryAndCleanupFailures();
            OneShotApplications();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Application contract failed: {exception.Message}");
            return 1;
        }
    }

    private static void ConfigurationSnapshotsAndDefaults()
    {
        var firstHost = new RecordingHost(3);
        var secondHost = new RecordingHost(4);
        var builder = LucentApplication.CreateBuilder().UseHost(firstHost);
        var first = builder.Build();
        builder.SetTitle("Second").SetTheme(_ => new Theme("second")).UseHost(secondHost);
        var second = builder.Build();

        Assert(first.Run(EmptyRecipe()) == 3, "The first host exit code was not preserved.");
        Assert(firstHost.Title == "Lucent", "The default title or builder snapshot changed.");
        Assert(
            ReferenceEquals(firstHost.Theme, ControlThemes.Light),
            "The default light appearance did not select the standard light control theme."
        );
        Assert(second.Run(EmptyRecipe()) == 4, "The second host exit code was not preserved.");
        Assert(secondHost.Title == "Second", "The updated builder title was not snapshotted.");
        Assert(
            secondHost.Theme?.Name == "second",
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
        Expect<InvalidOperationException>(() => missingHost.Build());
        Assert(
            factoryCalls == 0,
            "Build allocated application theme state before a host was selected."
        );
    }

    private static void NormalRunAndAppearance()
    {
        var cleanup = 0;
        var appearances = new List<ThemeAppearance>();
        var host = new RecordingHost(
            7,
            (composition, theme) =>
            {
                Assert(
                    theme.Theme.Name == "Light-Normal",
                    "The initial theme was not created once."
                );
                theme.Appearance = new ThemeAppearance(ThemeColorScheme.Dark, ThemeContrast.Normal);
                composition.Graph.Drain();
                Assert(theme.Theme.Name == "Dark-Normal", "Appearance did not refresh the theme.");
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
        Assert(result == 7, "The host result was not returned.");
        Assert(cleanup == 1, "Normal application cleanup did not run exactly once.");
        Assert(
            appearances.SequenceEqual([
                ThemeAppearance.Light,
                new ThemeAppearance(ThemeColorScheme.Dark, ThemeContrast.Normal),
            ]),
            "The theme factory ran redundantly or missed an appearance transition."
        );
        Assert(host.CompositionDisposed, "The host composition was not released after return.");
    }

    private static void FailureCleanup()
    {
        var mountCleanup = 0;
        var host = new RecordingHost(0);
        var mountFailure = LucentApplication.CreateBuilder().UseHost(host).Build();
        Expect<InvalidOperationException>(() =>
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
        Assert(mountCleanup == 1, "A failed mount did not release its recipe resources.");
        Assert(host.RunCount == 0, "The host ran after the root recipe failed to mount.");

        var hostCleanup = 0;
        var hostFailure = LucentApplication
            .CreateBuilder()
            .UseHost(new RecordingHost(0, (_, _) => throw new InvalidOperationException("host")))
            .Build();
        Expect<InvalidOperationException>(() =>
            hostFailure.Run(
                ComponentRecipe.Create(
                    "host-failure",
                    (_, root) => root.Scope.OnDispose(() => hostCleanup++)
                )
            )
        );
        Assert(hostCleanup == 1, "A host failure did not release the mounted recipe.");
    }

    private static void PrimaryAndCleanupFailures()
    {
        var app = LucentApplication
            .CreateBuilder()
            .UseHost(new RecordingHost(0, (_, _) => throw new InvalidOperationException("primary")))
            .Build();
        var failure = Expect<AggregateException>(() =>
            app.Run(
                ComponentRecipe.Create(
                    "aggregate",
                    (_, root) =>
                        root.Scope.OnDispose(() => throw new InvalidOperationException("cleanup"))
                )
            )
        );
        var messages = Flatten(failure).Select(item => item.Message).ToArray();
        Assert(messages.Contains("primary"), "The primary host failure was lost.");
        Assert(messages.Contains("cleanup"), "The cleanup failure was lost.");
    }

    private static void OneShotApplications()
    {
        var builder = LucentApplication.CreateBuilder().UseHost(new RecordingHost(0));
        var first = builder.Build();
        Expect<ArgumentNullException>(() => first.Run(null!));
        Assert(first.Run(EmptyRecipe()) == 0, "The first application run failed.");
        Expect<InvalidOperationException>(() => first.Run(EmptyRecipe()));
        Assert(
            builder.Build().Run(EmptyRecipe()) == 0,
            "A fresh build was not independently runnable."
        );
    }

    private static ComponentRecipe EmptyRecipe() => ComponentRecipe.Create("empty", (_, _) => { });

    private static IEnumerable<Exception> Flatten(Exception exception) =>
        exception is AggregateException aggregate
            ? aggregate.InnerExceptions.SelectMany(Flatten)
            : [exception];

    private static T Expect<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

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
