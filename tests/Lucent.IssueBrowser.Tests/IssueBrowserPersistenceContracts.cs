using System.Text;
using System.Text.Json;
using Lucent.Core;
using Lucent.IssueBrowser;
using TestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Lucent.IssueBrowser.Tests;

public sealed partial class IssueBrowserTests
{
    [TestMethod]
    [DataRow("", false, false, true)]
    [DataRow("--native-menus", true, false, true)]
    [DataRow("--restore-navigation", false, true, true)]
    [DataRow("--restore-navigation --native-menus", true, true, true)]
    [DataRow("--restore-navigation --restore-navigation", false, true, false)]
    [DataRow("--unknown", false, false, false)]
    public void NavigationPersistenceRequiresExplicitStartupOption(
        string arguments,
        bool nativeMenus,
        bool restore,
        bool accepted
    )
    {
        TestAssert.AreEqual(
            accepted,
            Program.TryOptions(
                arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                out var actualNative,
                out var actualRestore
            )
        );
        TestAssert.AreEqual(nativeMenus, actualNative);
        TestAssert.AreEqual(restore, actualRestore);
    }

    [TestMethod]
    public void NavigationPersistenceRestoresRealHostedJournalAndCapturesMountedStateOnClose()
    {
        using var directory = new NavigationDirectory();
        var issue = IssueFixture.Issues[0];
        directory.Seed(JournalInput(issue.Number));
        var notices = new List<NavigationPersistenceNotice>();
        // Reopen from the first close's output with a fresh controller and hosted root.
        for (var launch = 0; launch < 2; launch++)
        {
            var persistence = new IssueBrowserNavigationPersistence(
                new IssueBrowserNavigationStorage(directory.Snapshot),
                notices.Add
            );
            var host = new PersistenceHost(session =>
            {
                var title = Elements(session.Composition.Root)
                    .Single(element => element.Name == "issue-browser.details-title");
                TestAssert.AreEqual(
                    issue.Title,
                    Flatten(session.Composition.SemanticSnapshot()!)
                        .Single(node => node.Identity.ElementId == title.Id)
                        .Name
                );
            });
            var builder = LucentApplication.CreateBuilder().UseHost(host);
            persistence.Configure(builder);
            TestAssert.AreEqual(
                0,
                builder.Build().Run(IssueBrowserStructure.CreateHosted(persistence))
            );
        }
        using var saved = JsonDocument.Parse(File.ReadAllText(directory.Snapshot));
        var entries = saved.RootElement.GetProperty("entries");
        TestAssert.AreEqual(2, entries.GetArrayLength());
        TestAssert.AreEqual(2, saved.RootElement.GetProperty("activeKey").GetInt32());
        TestAssert.AreEqual("/issues", entries[0].GetProperty("location").GetString());
        TestAssert.AreEqual(
            $"/issues/{issue.Number}",
            entries[1].GetProperty("location").GetString()
        );
        var state = entries[1].GetProperty("state");
        TestAssert.AreEqual("lucent.interaction", state.GetProperty("codec").GetString());
        TestAssert.AreEqual(
            "issue-actions",
            state.GetProperty("viewports")[0].GetProperty("target").GetString()
        );
        TestAssert.HasCount(0, notices);
    }

    [TestMethod]
    [DataRow("unavailable-route")]
    [DataRow("invalid-json")]
    [DataRow("missing")]
    [DataRow("read-failure")]
    public void NavigationPersistenceUsesGuardedFallbackForMissingOrRejectedStartup(string scenario)
    {
        using var directory = new NavigationDirectory();
        if (scenario == "unavailable-route")
            directory.Seed(JournalInput(int.MaxValue));
        else if (scenario == "invalid-json")
            directory.Seed("{broken-json");
        var file = new ControlledNavigationFile(directory.Snapshot)
        {
            ReadUnavailable = scenario == "read-failure",
        };
        var notices = new List<NavigationPersistenceNotice>();
        var persistence = new IssueBrowserNavigationPersistence(new(file), notices.Add);
        var builder = LucentApplication
            .CreateBuilder()
            .UseHost(
                new PersistenceHost(session =>
                {
                    TestAssert.IsTrue(
                        Flatten(session.Composition.SemanticSnapshot()!)
                            .Any(node => node.Name == "Open an issue")
                    );
                    TestAssert.IsFalse(
                        session
                            .Composition.Dump()
                            .Contains("issue-browser.details-title", StringComparison.Ordinal)
                    );
                })
            );
        persistence.Configure(builder);
        TestAssert.AreEqual(
            0,
            builder.Build().Run(IssueBrowserStructure.CreateHosted(persistence))
        );
        using var saved = JsonDocument.Parse(File.ReadAllText(directory.Snapshot));
        var entries = saved.RootElement.GetProperty("entries");
        TestAssert.AreEqual(1, entries.GetArrayLength());
        TestAssert.AreEqual("/issues", entries[0].GetProperty("location").GetString());
        if (scenario == "read-failure")
            CollectionAssert.Contains(notices, NavigationPersistenceNotice.ReadUnavailable);
        else if (scenario == "invalid-json")
            CollectionAssert.Contains(notices, NavigationPersistenceNotice.SnapshotRejected);
        else
            TestAssert.HasCount(0, notices);
    }

    [TestMethod]
    public void NavigationPersistenceWaitsForLoadedResourcesAndNewerNavigationWinsPendingRead()
    {
        using var directory = new NavigationDirectory();
        directory.Seed(JournalInput(IssueFixture.Issues[0].Number));
        var file = new ControlledNavigationFile(directory.Snapshot) { HoldRead = true };
        var persistence = new IssueBrowserNavigationPersistence(new(file), _ => { });
        using var handler = new DeferredGitHubHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.github.local/"),
        };
        IssueBrowserState browser = null!;
        IssueBrowserViewState view = null!;
        var host = new PersistenceHost(
            _ => TestAssert.AreEqual(IssueFixture.Issues[1].Number, view.OpenedNumber),
            session =>
            {
                TestAssert.IsTrue(browser.IsLoading);
                TestAssert.IsNull(view.Navigation.Current);
                TestAssert.AreEqual(ApplicationPhase.Starting, session.Status.Phase);
                handler.ReplyJson(0);
                PumpPersistence(session, () => !browser.IsLoading);
                TestAssert.IsNull(view.Navigation.Current);
                view.OpenIssue(IssueFixture.Issues[1].Number);
                TestAssert.AreEqual(IssueFixture.Issues[1].Number, view.OpenedNumber);
                file.ReadRelease.TrySetResult();
            }
        );
        var builder = LucentApplication.CreateBuilder().UseHost(host);
        persistence.Configure(builder);
        TestAssert.AreEqual(
            0,
            builder
                .Build()
                .Run(
                    PersistenceRoot(
                        client,
                        new FixtureIssueStatusSource(),
                        persistence,
                        (state, stateView) => (browser, view) = (state, stateView)
                    )
                )
        );
        using var saved = JsonDocument.Parse(File.ReadAllText(directory.Snapshot));
        var entries = saved.RootElement.GetProperty("entries");
        TestAssert.AreEqual(1, entries.GetArrayLength());
        TestAssert.AreEqual(
            $"/issues/{IssueFixture.Issues[1].Number}",
            entries[0].GetProperty("location").GetString()
        );
    }

    [TestMethod]
    public void NavigationPersistenceWaitsForFixtureReadinessAfterSynchronousSnapshotRead()
    {
        using var directory = new NavigationDirectory();
        var file = new ControlledNavigationFile(directory.Snapshot)
        {
            ImmediateRead = new(
                NavigationStorageStatus.Ready,
                Encoding.UTF8.GetBytes(JournalInput(IssueFixture.Issues[0].Number))
            ),
        };
        var persistence = new IssueBrowserNavigationPersistence(new(file), _ => { });
        using var handler = new DeferredGitHubHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.github.local/"),
        };
        IssueBrowserState browser = null!;
        IssueBrowserViewState view = null!;
        var host = new PersistenceHost(
            _ => TestAssert.AreEqual(IssueFixture.Issues[0].Number, view.OpenedNumber),
            session =>
            {
                TestAssert.IsTrue(browser.IsLoading);
                TestAssert.IsNull(view.Navigation.Current);
                TestAssert.AreEqual(ApplicationPhase.Starting, session.Status.Phase);
                handler.ReplyJson(0);
            }
        );
        var builder = LucentApplication.CreateBuilder().UseHost(host);
        persistence.Configure(builder);
        TestAssert.AreEqual(
            0,
            builder
                .Build()
                .Run(
                    PersistenceRoot(
                        client,
                        new FixtureIssueStatusSource(),
                        persistence,
                        (state, stateView) => (browser, view) = (state, stateView)
                    )
                )
        );
    }

    [TestMethod]
    public void NavigationPersistenceFailureCannotCancelAcceptedIssueStatusWrite()
    {
        using var directory = new NavigationDirectory();
        var prior = JournalInput(IssueFixture.Issues[0].Number);
        directory.Seed(prior);
        var file = new ControlledNavigationFile(directory.Snapshot) { FailPrepare = true };
        var notices = new List<NavigationPersistenceNotice>();
        var persistence = new IssueBrowserNavigationPersistence(new(file), notices.Add);
        using var handler = new DeferredGitHubHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.github.local/"),
        };
        var statuses = new DeferredStatusSource();
        IssueBrowserState browser = null!;
        IssueBrowserViewState view = null!;
        var host = new PersistenceHost(
            session =>
            {
                var issue = browser.Issues[0];
                browser.ToggleIssueStatus(issue.Number);
                view.BackToList();
                PumpPersistence(
                    session,
                    () =>
                        statuses.Requests.Count == 1
                        && notices.Contains(NavigationPersistenceNotice.WriteUnavailable)
                );
                TestAssert.AreEqual(0, statuses.Cancellations);
                statuses.Reply(0, new IssueStatusSaveOutcome.Rejected("independent-result"));
                PumpPersistence(
                    session,
                    () => browser.MutationMessage(issue.Number) == "Rejected: independent-result"
                );
                TestAssert.AreEqual(
                    issue.Status,
                    browser.Issues.Single(current => current.Number == issue.Number).Status
                );
                TestAssert.AreEqual(0, statuses.Cancellations);
                TestAssert.AreEqual("/issues", view.Navigation.Current!.Location.CanonicalText);
            },
            _ => handler.ReplyJson(0)
        );
        var builder = LucentApplication.CreateBuilder().UseHost(host);
        persistence.Configure(builder);
        TestAssert.AreEqual(
            0,
            builder
                .Build()
                .Run(
                    PersistenceRoot(
                        client,
                        statuses,
                        persistence,
                        (state, stateView) => (browser, view) = (state, stateView)
                    )
                )
        );
        TestAssert.AreEqual(prior, File.ReadAllText(directory.Snapshot));
    }

    [TestMethod]
    public void NavigationPersistenceCloseBeforeFirstLayoutPreservesImportedInteraction()
    {
        using var directory = new NavigationDirectory();
        var number = IssueFixture.Issues[0].Number;
        directory.Seed(
            $$$"""
            {"schema":"lucent.navigation","version":1,"scope":"issue-browser-fixture-routes-v1","mode":"journal","activeKey":1,"entries":[{"key":1,"definition":"issue","location":"/issues/{{{number}}}","state":{"codec":"lucent.interaction","version":1,"focus":"issue-actions","viewports":[{"target":"issue-actions","x":0,"y":90}]}}]}
            """
        );
        var file = new ControlledNavigationFile(directory.Snapshot) { HoldRead = true };
        var notices = new List<NavigationPersistenceNotice>();
        var persistence = new IssueBrowserNavigationPersistence(new(file), notices.Add);
        var host = new PersistenceHost(
            _ => TestAssert.Fail("Closing during startup must not install a scene."),
            session =>
            {
                session.RequestClose();
                file.ReadRelease.TrySetResult();
            },
            closeBeforeLayout: true
        );
        var builder = LucentApplication.CreateBuilder().UseHost(host);
        persistence.Configure(builder);
        TestAssert.AreEqual(
            0,
            builder.Build().Run(IssueBrowserStructure.CreateHosted(persistence))
        );
        using var saved = JsonDocument.Parse(File.ReadAllText(directory.Snapshot));
        var entry = saved.RootElement.GetProperty("entries")[0];
        TestAssert.AreEqual($"/issues/{number}", entry.GetProperty("location").GetString());
        var interaction = entry.GetProperty("state");
        TestAssert.AreEqual(
            90f,
            interaction.GetProperty("viewports")[0].GetProperty("y").GetSingle()
        );
        TestAssert.AreEqual("issue-actions", interaction.GetProperty("focus").GetString());
        TestAssert.HasCount(0, notices);
    }

    [TestMethod]
    public void NavigationPersistenceResumesAfterDeclinedCloseAndCapturesFreshActiveViewport()
    {
        using var directory = new NavigationDirectory();
        directory.Seed(JournalInput(IssueFixture.Issues[0].Number));
        IssueBrowserViewState view = null!;
        var notices = new List<string>();
        var persistence = new IssueBrowserNavigationPersistence(
            new(directory.Snapshot),
            notice => notices.Add($"{notice}: phase={view?.Navigation.Phase}")
        );
        using var handler = new DeferredGitHubHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.github.local/"),
        };
        var attempts = 0;
        var host = new PersistenceHost(
            session =>
            {
                view.DetailViewport.Offset = new(0, 17);
                session.RequestClose();
                PumpPersistence(
                    session,
                    () => attempts == 1 && session.Status.Phase == ApplicationPhase.Running
                );
                TestAssert.IsTrue(persistence.AllowsNavigation);
                view.OpenIssue(IssueFixture.Issues[1].Number);
                TestAssert.AreEqual(IssueFixture.Issues[1].Number, view.OpenedNumber);
                view.DetailViewport.Offset = new(0, 41);
            },
            _ => handler.ReplyJson(0)
        );
        var builder = LucentApplication.CreateBuilder().UseHost(host);
        persistence.Configure(builder);
        builder.OnPrepareClose(
            (_, _) =>
            {
                TestAssert.IsFalse(persistence.AllowsNavigation);
                var current = view.Navigation.Current;
                view.BackToList();
                TestAssert.AreSame(current, view.Navigation.Current);
                return ValueTask.FromResult(++attempts > 1);
            }
        );
        TestAssert.AreEqual(
            0,
            builder
                .Build()
                .Run(
                    PersistenceRoot(
                        client,
                        new FixtureIssueStatusSource(),
                        persistence,
                        (_, stateView) => view = stateView
                    )
                )
        );
        TestAssert.AreEqual(2, attempts);
        using var saved = JsonDocument.Parse(File.ReadAllText(directory.Snapshot));
        var entries = saved.RootElement.GetProperty("entries");
        var active = entries[entries.GetArrayLength() - 1];
        TestAssert.AreEqual(
            $"/issues/{IssueFixture.Issues[1].Number}",
            active.GetProperty("location").GetString()
        );
        TestAssert.AreEqual(
            41f,
            active.GetProperty("state").GetProperty("viewports")[0].GetProperty("y").GetSingle()
        );
        TestAssert.HasCount(0, notices, string.Join(", ", notices));
    }

    [TestMethod]
    public async Task NavigationStorageReadsAreBoundedAndMissingReadCreatesNothing()
    {
        using var directory = new NavigationDirectory();
        var store = new IssueBrowserNavigationStorage(directory.Snapshot);
        TestAssert.AreEqual(NavigationStorageStatus.Missing, (await store.ReadAsync()).Status);
        TestAssert.IsFalse(Directory.Exists(directory.Path));
        TestAssert.AreEqual(NavigationStorageStatus.Cleared, await store.Submit(null));
        TestAssert.IsFalse(Directory.Exists(directory.Path));

        Directory.CreateDirectory(directory.Path);
        var maximum = new byte[NavigationRestoration.MaximumPayloadBytes];
        maximum[0] = 19;
        maximum[^1] = 71;
        await File.WriteAllBytesAsync(directory.Snapshot, maximum);
        var exact = await store.ReadAsync();
        TestAssert.AreEqual(NavigationStorageStatus.Ready, exact.Status);
        CollectionAssert.AreEqual(maximum, exact.Utf8.ToArray());

        await using (var stream = File.OpenWrite(directory.Snapshot))
            stream.SetLength(NavigationRestoration.MaximumPayloadBytes + 1L);
        var oversized = await store.ReadAsync();
        TestAssert.AreEqual(NavigationStorageStatus.TooLarge, oversized.Status);
        TestAssert.IsTrue(oversized.Utf8.IsEmpty);
        TestAssert.AreEqual(
            NavigationStorageStatus.TooLarge,
            await store.Submit(new byte[NavigationRestoration.MaximumPayloadBytes + 1])
        );
        TestAssert.AreEqual(
            NavigationRestoration.MaximumPayloadBytes + 1L,
            new FileInfo(directory.Snapshot).Length
        );
    }

    [TestMethod]
    public async Task NavigationStoragePublishesOnlyLatestAcceptedImmutableSnapshot()
    {
        using var directory = new NavigationDirectory();
        var file = new ControlledNavigationFile(directory.Snapshot) { HoldFirst = true };
        var store = new IssueBrowserNavigationStorage(file);
        var oldest = store.Submit("oldest"u8.ToArray());
        await file.Staged.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var middle = store.Submit("middle"u8.ToArray());
        var bytes = "newest"u8.ToArray();
        var newest = store.Submit(bytes);
        bytes[0] = (byte)'X';
        TestAssert.AreEqual(NavigationStorageStatus.Superseded, await middle);
        TestAssert.IsFalse(File.Exists(directory.Snapshot));

        file.Release.TrySetResult();
        TestAssert.AreEqual(NavigationStorageStatus.Superseded, await oldest);
        TestAssert.AreEqual(NavigationStorageStatus.Saved, await newest);
        await store.DrainAsync();
        TestAssert.AreEqual("newest", await File.ReadAllTextAsync(directory.Snapshot));
        TestAssert.AreEqual(1, file.CommitCount);
        TestAssert.AreEqual(2, file.PrepareCount);
        TestAssert.HasCount(1, Directory.GetFiles(directory.Path));
    }

    [TestMethod]
    public async Task NavigationStorageTombstoneFencesPendingWriteAndStopDrainsAcceptedWork()
    {
        using var directory = new NavigationDirectory();
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(directory.Snapshot, "previous");
        var file = new ControlledNavigationFile(directory.Snapshot) { HoldFirst = true };
        var store = new IssueBrowserNavigationStorage(file);
        var pending = store.Submit("stale"u8.ToArray());
        await file.Staged.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var clear = store.Submit(null);
        var stopped = store.StopAsync();
        TestAssert.IsFalse(stopped.IsCompleted);
        TestAssert.AreEqual(
            NavigationStorageStatus.Stopped,
            await store.Submit("after-stop"u8.ToArray())
        );

        file.Release.TrySetResult();
        await stopped.WaitAsync(TimeSpan.FromSeconds(10));
        TestAssert.AreEqual(NavigationStorageStatus.Superseded, await pending);
        TestAssert.AreEqual(NavigationStorageStatus.Cleared, await clear);
        TestAssert.IsFalse(File.Exists(directory.Snapshot));
        TestAssert.HasCount(0, Directory.GetFiles(directory.Path));
        TestAssert.AreEqual(0, file.CommitCount);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task NavigationStorageFailurePreservesPreviousSnapshotAndLaterWritesRecover(
        bool failDuringPrepare
    )
    {
        using var directory = new NavigationDirectory();
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(directory.Snapshot, "previous");
        var file = new ControlledNavigationFile(directory.Snapshot)
        {
            FailPrepare = failDuringPrepare,
            FailCommit = !failDuringPrepare,
        };
        var store = new IssueBrowserNavigationStorage(file);
        TestAssert.AreEqual(
            NavigationStorageStatus.Unavailable,
            await store.Submit("failed"u8.ToArray())
        );
        TestAssert.AreEqual("previous", await File.ReadAllTextAsync(directory.Snapshot));
        TestAssert.HasCount(1, Directory.GetFiles(directory.Path));
        file.FailPrepare = false;
        file.FailCommit = false;
        TestAssert.AreEqual(
            NavigationStorageStatus.Saved,
            await store.Submit("recovered"u8.ToArray())
        );
        await store.StopAsync();
        TestAssert.AreEqual("recovered", await File.ReadAllTextAsync(directory.Snapshot));
        TestAssert.HasCount(1, Directory.GetFiles(directory.Path));
    }

    private sealed class NavigationDirectory : IDisposable
    {
        internal string Path { get; } =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "lucent-issue-navigation-" + Guid.NewGuid().ToString("N")
            );
        internal string Snapshot => System.IO.Path.Combine(Path, "navigation.json");

        internal void Seed(string json)
        {
            Directory.CreateDirectory(Path);
            File.WriteAllText(Snapshot, json);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class ControlledNavigationFile(string path) : IIssueBrowserNavigationFile
    {
        private readonly IssueBrowserNavigationFile _file = new(path);
        internal bool HoldFirst { get; init; }
        internal bool FailPrepare { get; set; }
        internal bool FailCommit { get; set; }
        internal bool HoldRead { get; init; }
        internal bool ReadUnavailable { get; init; }
        internal NavigationStorageRead? ImmediateRead { get; init; }
        internal TaskCompletionSource ReadRelease { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int PrepareCount { get; private set; }
        internal int CommitCount { get; private set; }
        internal TaskCompletionSource Staged { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<NavigationStorageRead> ReadAsync(CancellationToken cancellationToken)
        {
            if (ImmediateRead is { } immediate)
                return immediate;
            if (HoldRead)
                await ReadRelease.Task;
            return ReadUnavailable
                ? new(NavigationStorageStatus.Unavailable)
                : await _file.ReadAsync(cancellationToken);
        }

        public async Task<IPreparedNavigationFile> PrepareAsync(ReadOnlyMemory<byte> utf8)
        {
            PrepareCount++;
            if (FailPrepare)
                throw new IOException("Controlled navigation staging failure.");
            var prepared = await _file.PrepareAsync(utf8);
            if (HoldFirst && PrepareCount == 1)
            {
                Staged.TrySetResult();
                await Release.Task;
            }
            return new ControlledPreparedFile(this, prepared);
        }

        public void Clear() => _file.Clear();

        private sealed class ControlledPreparedFile(
            ControlledNavigationFile owner,
            IPreparedNavigationFile inner
        ) : IPreparedNavigationFile
        {
            public void Commit()
            {
                if (owner.FailCommit)
                    throw new IOException("Controlled navigation replacement failure.");
                owner.CommitCount++;
                inner.Commit();
            }

            public void Dispose() => inner.Dispose();
        }
    }

    private static string JournalInput(int number) =>
        $$"""
            {"schema":"lucent.navigation","version":1,"scope":"issue-browser-fixture-routes-v1","mode":"journal","activeKey":2,"entries":[{"key":1,"definition":"issues","location":"/issues"},{"key":2,"definition":"issue","location":"/issues/{{number}}"}]}
            """;

    private static ComponentRecipe PersistenceRoot(
        HttpClient client,
        IIssueStatusSource statuses,
        IssueBrowserNavigationPersistence persistence,
        Action<IssueBrowserState, IssueBrowserViewState> created
    ) =>
        ComponentRecipe.Defer(
            "persistence-contract",
            owner =>
            {
                var browser = new IssueBrowserState(owner, new GitHubIssueSource(client), statuses);
                var view = new IssueBrowserViewState(owner, browser, persistence);
                created(browser, view);
                return Context.Provide(
                    view.Navigation,
                    Context.Provide(
                        view,
                        Context.Provide(
                            browser,
                            Lucent.Core.Components.NavigationBoundary(
                                [IssueBrowserRouting.Outlet(view)],
                                view.Interaction
                            )
                        )
                    )
                );
            }
        );

    private sealed class PersistenceHost(
        Action<ApplicationSession> running,
        Action<ApplicationSession>? starting = null,
        bool closeBeforeLayout = false
    ) : IApplicationHost
    {
        public int Run(ApplicationSession session)
        {
            session.Composition.ConfigureImages(
                new ImageCache(new Lucent.Renderer.Skia.SkiaImagePreparer())
            );
            using var renderer = new Lucent.Renderer.Skia.SkiaSceneRenderer();
            session.Start();
            starting?.Invoke(session);
            if (closeBeforeLayout)
            {
                TestAssert.IsTrue(session.IsCloseRequested);
                PumpPersistence(session, () => session.IsCompleted);
                TestAssert.IsNull(session.Status.Error);
                return 0;
            }
            PumpPersistence(
                session,
                () => session.Status.Phase == ApplicationPhase.Running || session.IsCompleted
            );
            TestAssert.IsFalse(session.IsCompleted, session.Status.Error?.ToString());
            Install(session.Composition, renderer, new(1120, 760, 1));
            running(session);
            session.RequestClose();
            PumpPersistence(session, () => session.IsCompleted);
            return 0;
        }
    }

    private static void PumpPersistence(ApplicationSession session, Func<bool> done)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!done())
        {
            session.ProcessEvents();
            if (!session.Composition.IsDisposed)
                session.Composition.Flush();
            if (Environment.TickCount64 >= deadline)
                throw new TimeoutException("Issue Browser persistence lifecycle did not complete.");
            Thread.Sleep(1);
        }
    }
}
