using System.Diagnostics;
using System.Globalization;
using Lucent.Core;
using Lucent.Preview.Protocol;
using SkiaSharp;

namespace Lucent.Preview.Hosting.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LiveSessionContracts
{
    private readonly List<OwnedStartup> _startups = [];
    public TestContext TestContext { get; set; } = null!;

    private Task<PreviewRenderSession> StartAsync(
        PreviewScenario scenario,
        PreviewWorkerRequest request,
        CancellationToken cancellationToken = default,
        params TaskCompletionSource[] barriers
    )
    {
        var owned = new OwnedStartup(
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken),
            barriers
        );
        _startups.Add(owned);
        owned.Startup = StartCoreAsync();
        return owned.Startup;

        async Task<PreviewRenderSession> StartCoreAsync()
        {
            try
            {
                owned.Session = await PreviewRenderSession.StartAsync(
                    scenario,
                    request,
                    owned.Lifetime.Token
                );
                return owned.Session;
            }
            catch
            {
                owned.StartupFailed = true;
                throw;
            }
        }
    }

    private void ExpectFailedCompletion(PreviewRenderSession session) =>
        _startups.Single(owned => ReferenceEquals(owned.Session, session)).ExpectedFailure = true;

    [TestCleanup]
    public async Task StopAllStartedOwnersWithoutReplacingAnAssertionFailure()
    {
        List<Exception> failures = [];
        foreach (var owned in _startups)
        {
            foreach (var barrier in owned.Barriers)
                barrier.TrySetResult();
            try
            {
                owned.Lifetime.Cancel();
                var session =
                    owned.Session ?? await owned.Startup.WaitAsync(TimeSpan.FromSeconds(5));
                await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception error)
            {
                if (
                    owned.StartupFailed
                    || owned.ExpectedFailure
                    || TestContext.CurrentTestOutcome != UnitTestOutcome.Passed
                )
                    TestContext.WriteLine("Owned cleanup observation: " + error);
                else
                    failures.Add(error);
            }
            finally
            {
                owned.Lifetime.Dispose();
            }
        }
        if (failures.Count != 0)
            throw new AggregateException("Unexpected live-test cleanup failure.", failures);
    }

    [TestMethod]
    public async Task AsyncStartupAndWorkerInvalidationProduceOwnedPixelsWithoutInput()
    {
        var entered = NewCompletion();
        var release = NewCompletion();
        var fixture = new Fixture();
        var scenario = Scenario(
            fixture,
            async (context, token) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
                context.OnDispose(() =>
                {
                    fixture.DisposalThread = Environment.CurrentManagedThreadId;
                    return ValueTask.CompletedTask;
                });
            }
        );
        var startup = StartAsync(scenario, Request(), barriers: [release]);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(startup.IsCompleted);
        release.SetResult();
        var live = await startup.WaitAsync(TimeSpan.FromSeconds(5));
        var first = await Frame(live);
        AssertPixel(first, SKColors.Blue);
        Assert.IsTrue((await live.AcknowledgeAsync(first.Token)).Accepted);
        await fixture.SetAsync(1);
        var changed = await Frame(live);
        AssertPixel(changed, SKColors.Red);
        Assert.AreEqual(first.Token.Session, changed.Token.Session);
        Assert.AreEqual(first.Token.Sequence + 1, changed.Token.Sequence);
        Assert.AreEqual(fixture.OwnerThread, live.Diagnostics.OwnerThread);
        await live.StopAsync();
        Assert.AreEqual(fixture.OwnerThread, fixture.DisposalThread);
        Assert.IsTrue(live.Diagnostics.ResourcesDisposed);
        Assert.AreEqual(0, live.Diagnostics.LiveTextBlobs);
    }

    [TestMethod]
    public async Task RealAnchoredTimerChangesPixelsAndOwnedTimerStopsWithSession()
    {
        var fixture = new Fixture();
        var timerFired = NewCompletion();
        var scenario = Scenario(
            fixture,
            (context, _) =>
            {
                fixture.Clock = context.Clock;
                fixture.InitialUtc = context.Clock.GetUtcNow();
                return ValueTask.CompletedTask;
            },
            rootReady: session =>
            {
                fixture.Timer = session.Scope.Own(
                    fixture.Clock!.CreateTimer(
                        _ =>
                        {
                            session.Scope.Post(() =>
                            {
                                fixture.Value!.Value = 1;
                                fixture.TimerOwnerThread = Environment.CurrentManagedThreadId;
                                timerFired.TrySetResult();
                            });
                        },
                        null,
                        Timeout.InfiniteTimeSpan,
                        Timeout.InfiniteTimeSpan
                    )
                );
            }
        );
        var live = await StartAsync(scenario, Request());
        var first = await Frame(live);
        AssertPixel(first, SKColors.Blue);
        await live.AcknowledgeAsync(first.Token);
        Assert.IsTrue(
            fixture.Timer!.Change(TimeSpan.FromMilliseconds(50), Timeout.InfiniteTimeSpan)
        );
        await timerFired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var timed = await Frame(live);
        AssertPixel(timed, SKColors.Red);
        Assert.AreEqual(fixture.OwnerThread, fixture.TimerOwnerThread);
        Assert.IsTrue(fixture.InitialUtc >= DateTimeOffset.UnixEpoch);
        Assert.IsTrue(fixture.InitialUtc < DateTimeOffset.UnixEpoch.AddSeconds(5));
        Assert.IsTrue(fixture.Clock!.GetUtcNow() > fixture.InitialUtc);
        await live.StopAsync();
        Assert.IsFalse(fixture.Timer!.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan));
        Assert.IsTrue(live.Diagnostics.ResourcesDisposed);
    }

    [TestMethod]
    public async Task FrameBackpressureCoalescesChangesAndRejectsForeignOrRepeatedAcknowledgments()
    {
        var fixture = new Fixture();
        var live = await StartAsync(Scenario(fixture), Request());
        var first = await Frame(live);
        AssertPixel(first, SKColors.Blue);
        await fixture.SetAsync(1);
        await fixture.SetAsync(0);
        await fixture.SetAsync(1);
        Assert.AreEqual(1L, live.Diagnostics.Frames);
        var foreign = first.Token with
        {
            Session = first.Token.Session with { Generation = "other" },
        };
        Assert.IsFalse((await live.AcknowledgeAsync(foreign)).Accepted);
        Assert.IsFalse((await live.AcknowledgeAsync(first.Token with { Sequence = 99 })).Accepted);
        Assert.IsTrue((await live.AcknowledgeAsync(first.Token)).Accepted);
        var latest = await Frame(live);
        AssertPixel(latest, SKColors.Red);
        Assert.AreEqual(2L, latest.Token.Sequence);
        Assert.IsFalse((await live.AcknowledgeAsync(first.Token)).Accepted);
        Assert.IsTrue((await live.AcknowledgeAsync(latest.Token)).Accepted);
        var counters = live.Diagnostics;
        await Task.Delay(50);
        var beforeIdle = live.Diagnostics;
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var idle = Stopwatch.StartNew();
        await Task.Delay(150);
        process.Refresh();
        var afterIdle = live.Diagnostics;
        TestContext.WriteLine(
            $"Live-host idle diagnostic: wall={idle.Elapsed.TotalMilliseconds:F2}ms processCpu={(process.TotalProcessorTime - cpuBefore).TotalMilliseconds:F2}ms."
        );
        Assert.AreEqual(
            beforeIdle.LoopTurns,
            afterIdle.LoopTurns,
            "An idle live owner must wait for an event."
        );
        Assert.AreEqual(2L, afterIdle.Frames);
        Assert.IsGreaterThan(0L, counters.TextBlobCreations);
        Assert.AreEqual(counters.TextBlobCreations, afterIdle.TextBlobCreations);
        Assert.AreEqual(160 * 120 * 4, afterIdle.SurfaceBytes);
    }

    [TestMethod]
    public async Task MotionResumesAtCurrentDeadlineAfterFrameAcknowledgmentWithoutBusyLoop()
    {
        var fixture = new Fixture();
        var live = await StartAsync(Scenario(fixture, motion: true), Request());
        var initial = await Frame(live);
        await live.AcknowledgeAsync(initial.Token);
        await fixture.SetAsync(1);
        var started = await Frame(live);
        AssertPixel(started, SKColors.Blue);
        await Task.Delay(30);
        var blocked = live.Diagnostics;
        await Task.Delay(120);
        Assert.AreEqual(blocked.LoopTurns, live.Diagnostics.LoopTurns);
        Assert.AreEqual(2L, live.Diagnostics.Frames);
        await live.AcknowledgeAsync(started.Token);
        var completed = await Frame(live);
        AssertPixel(completed, SKColors.Red);
        Assert.IsTrue((await live.AcknowledgeAsync(completed.Token)).Accepted);
    }

    [TestMethod]
    public async Task FocusLossCancelsAppliedCaptureAndKeysEvenWhenFrameSceneWasReplaced()
    {
        var fixture = new Fixture();
        var behavior = new InputProbe();
        var live = await StartAsync(Scenario(fixture, behavior: behavior), Request());
        var first = await Frame(live);
        var down = await live.PointerAsync(
            first.Token,
            1,
            new(PointerCommandKind.Down, 7, 20, 20, PointerButton.Primary)
        );
        Assert.AreEqual(InputDispatchStatus.Delivered, down!.Status);
        await live.AcknowledgeAsync(first.Token);
        var focused = await Frame(live);
        var key = await live.KeyAsync(focused.Token, 2, new(KeyCommandKind.Down, Key.A));
        Assert.AreEqual(InputDispatchStatus.Delivered, key!.Status);
        Assert.IsNull(
            await live.KeyAsync(focused.Token, 2, new(KeyCommandKind.Down, Key.A)),
            "Repeated input sequence must be rejected before author callbacks."
        );
        Assert.IsNull(
            await live.PointerAsync(first.Token, 3, new(PointerCommandKind.Move, 7, 25, 25)),
            "Superseded frame must be display-only."
        );
        Assert.IsTrue(await live.SetFocusAsync(live.Identity, false));
        Assert.AreEqual(1, behavior.Cancels);
        Assert.AreEqual(1, behavior.KeyUps);
        Assert.AreEqual(fixture.OwnerThread, behavior.InputThread);
        var acknowledged = await live.AcknowledgeAsync(focused.Token);
        Assert.IsTrue(acknowledged.Accepted);
        Assert.IsFalse(
            acknowledged.PresentationAcknowledged,
            "Reprojection must retain only the old acknowledgment token, not its disposed scene."
        );
        var cleaned = await Frame(live);
        Assert.IsNull(await live.KeyAsync(cleaned.Token, 4, new(KeyCommandKind.Down, Key.A)));
        await live.StopAsync();
        Assert.AreEqual(0, live.Diagnostics.ActivePointers);
        Assert.AreEqual(0, live.Diagnostics.PressedKeys);
        Assert.IsTrue(live.Diagnostics.ResourcesDisposed);
    }

    [TestMethod]
    public async Task StaleOwnedReleaseRoutesOnlySurvivingCaptureAndOriginalKeyOwner()
    {
        var fixture = new Fixture();
        var behavior = new InputProbe();
        var live = await StartAsync(Scenario(fixture, behavior: behavior), Request());
        var first = await Frame(live);
        Assert.IsTrue(await live.SetFocusAsync(live.Identity, true));
        Assert.AreEqual(
            InputDispatchStatus.Delivered,
            (
                await live.PointerAsync(
                    first.Token,
                    1,
                    new(PointerCommandKind.Down, 7, 20, 20, PointerButton.Primary)
                )
            )!.Status
        );
        await live.AcknowledgeAsync(first.Token);
        var second = await Frame(live);
        Assert.AreEqual(
            InputDispatchStatus.Delivered,
            (await live.KeyAsync(second.Token, 2, new(KeyCommandKind.Down, Key.A)))!.Status
        );
        Assert.AreEqual(
            InputDispatchStatus.Delivered,
            (await live.KeyAsync(second.Token, 3, new(KeyCommandKind.Down, Key.C)))!.Status
        );
        Assert.AreEqual(
            InputDispatchStatus.Delivered,
            (
                await live.PointerAsync(
                    second.Token,
                    4,
                    new(PointerCommandKind.Down, 9, 30, 30, PointerButton.Primary)
                )
            )!.Status
        );
        await live.AcknowledgeAsync(second.Token);
        var held = await Frame(live);
        Assert.IsNull(
            await live.PointerAsync(
                first.Token with
                {
                    Session = first.Token.Session with { Generation = "foreign" },
                },
                5,
                new(PointerCommandKind.Up, 7, 20, 20, PointerButton.Primary)
            )
        );
        Assert.IsNull(
            await live.PointerAsync(
                held.Token with
                {
                    Sequence = held.Token.Sequence + 1,
                },
                5,
                new(PointerCommandKind.Up, 7, 20, 20, PointerButton.Primary)
            )
        );
        Assert.IsNull(
            await live.PointerAsync(
                first.Token,
                5,
                new(PointerCommandKind.Up, 8, 20, 20, PointerButton.Primary)
            )
        );
        Assert.AreEqual(
            0,
            behavior.Cancels,
            "Foreign, future and unowned releases cannot cancel the held pointer."
        );
        // A producer can publish a new frame before the consumer displays it. Release
        // the already owned sequence through its surviving capture, never a new hit target.
        Assert.IsNull(
            await live.PointerAsync(
                first.Token,
                6,
                new(PointerCommandKind.Up, 7, 20, 20, PointerButton.Primary)
            )
        );
        Assert.IsNull(await live.KeyAsync(second.Token, 7, new(KeyCommandKind.Up, Key.A)));
        Assert.AreEqual(0, behavior.Cancels);
        Assert.AreEqual(1, behavior.KeyUps);
        Assert.AreEqual(
            1,
            behavior.PointerUps,
            "Compatible captured release must reach the original owner."
        );
        CollectionAssert.AreEquivalent(new[] { Key.C }, behavior.HeldKeys.ToArray());
        await live.AcknowledgeAsync(held.Token);
        _ = await Frame(live);
        Assert.AreEqual(
            1,
            live.Diagnostics.ActivePointers,
            "Another held pointer must survive scoped release."
        );
        Assert.AreEqual(
            1,
            live.Diagnostics.PressedKeys,
            "Another held key must survive scoped release."
        );
        await live.StopAsync();
        Assert.AreEqual(1, behavior.Cancels);
        Assert.AreEqual(2, behavior.KeyUps);
    }

    [TestMethod]
    public async Task StaleKeyReleaseCannotInvokeReplacementFocusOwner()
    {
        var fixture = new Fixture();
        var original = new InputProbe();
        var replacement = new InputProbe();
        var live = await StartAsync(Scenario(fixture, behavior: original), Request());
        var first = await Frame(live);
        await live.PointerAsync(
            first.Token,
            1,
            new(PointerCommandKind.Down, 7, 20, 20, PointerButton.Primary)
        );
        await live.AcknowledgeAsync(first.Token);
        var focused = await Frame(live);
        await live.KeyAsync(focused.Token, 2, new(KeyCommandKind.Down, Key.A));
        var added = NewCompletion();
        fixture.Scope!.Post(() =>
        {
            var child = fixture.Composition!.Child(fixture.Root!, "replacement-focus");
            child.Present(fixture.Theme!, author: Style.Empty.Width(160).Height(20));
            child.AttachBehaviors(replacement);
            added.SetResult();
        });
        await added.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await live.AcknowledgeAsync(focused.Token);
        var both = await Frame(live);
        await live.KeyAsync(both.Token, 3, new(KeyCommandKind.Down, Key.Tab));
        await live.AcknowledgeAsync(both.Token);
        var changed = await Frame(live);
        Assert.AreEqual(
            1,
            replacement.FocusGains,
            "The fixture must actually replace the original focus owner."
        );
        Assert.IsNull(await live.KeyAsync(focused.Token, 4, new(KeyCommandKind.Up, Key.A)));
        Assert.AreEqual(
            0,
            replacement.KeyUps,
            "Stale release must not invoke the new focus owner's author callbacks."
        );
        await live.AcknowledgeAsync(changed.Token);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task KeyReleaseCannotInvokeFocusOwnerChangedSynchronouslyByDown(bool blur)
    {
        var fixture = new Fixture();
        var original = new InputProbe();
        var replacement = new InputProbe();
        var live = await StartAsync(Scenario(fixture, behavior: original), Request());
        var first = await Frame(live);
        await live.PointerAsync(
            first.Token,
            1,
            new(PointerCommandKind.Down, 7, 20, 20, PointerButton.Primary)
        );
        await live.AcknowledgeAsync(first.Token);
        var focused = await Frame(live);
        var added = NewCompletion();
        fixture.Scope!.Post(() =>
        {
            var child = fixture.Composition!.Child(fixture.Root!, "replacement-focus");
            child.Present(fixture.Theme!, author: Style.Empty.Width(160).Height(20));
            child.AttachBehaviors(replacement);
            added.SetResult();
        });
        await added.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await live.AcknowledgeAsync(focused.Token);
        var both = await Frame(live);
        var down = await live.KeyAsync(both.Token, 2, new(KeyCommandKind.Down, Key.Tab));
        Assert.AreEqual(InputDispatchStatus.Delivered, down!.Status);
        Assert.IsTrue(original.HeldKeys.Contains(Key.Tab));
        Assert.AreEqual(
            1,
            replacement.FocusGains,
            "Tab down must synchronously move focus before this frame is acknowledged."
        );
        if (blur)
            await live.SetFocusAsync(live.Identity, false);
        else
            Assert.IsNull(await live.KeyAsync(both.Token, 3, new(KeyCommandKind.Up, Key.Tab)));
        Assert.AreEqual(0, replacement.KeyUps, "Release must not invoke the new focus owner.");
        Assert.AreEqual(0, original.KeyUps, "Release cannot route through a lost focus owner.");
        await live.StopAsync();
        Assert.AreEqual(0, replacement.KeyUps, "Stop must not replay the abandoned ownership.");
        Assert.AreEqual(0, live.Diagnostics.PressedKeys);
    }

    [TestMethod]
    public async Task DisplayAcknowledgedPointerDownSurvivesPendingHoverPaintWithoutLosingClick()
    {
        var fixture = new Fixture();
        var live = await StartAsync(Scenario(fixture, button: true), Request());
        var displayed = await Frame(live);
        await live.AcknowledgeAsync(displayed.Token);
        var hover = await live.PointerAsync(
            displayed.Token,
            1,
            new(PointerCommandKind.Move, 0, 20, 20)
        );
        Assert.AreEqual(InputDispatchStatus.Delivered, hover!.Status);
        var pending = await Frame(live);
        Assert.AreEqual(displayed.Token.Sequence + 1, pending.Token.Sequence);
        await live.SetFocusAsync(live.Identity, true);
        var down = await live.PointerAsync(
            displayed.Token,
            2,
            new(PointerCommandKind.Down, 0, 20, 20, PointerButton.Primary)
        );
        Assert.AreEqual(InputDispatchStatus.Delivered, down!.Status);
        Assert.IsNull(
            await live.PointerAsync(
                displayed.Token,
                3,
                new(PointerCommandKind.Down, 0, 20, 20, PointerButton.Secondary)
            ),
            "Compatible old display metadata cannot start another down on an owned pointer."
        );
        Assert.IsNull(
            await live.PointerAsync(
                displayed.Token,
                4,
                new(PointerCommandKind.Up, 0, 20, 20, PointerButton.Primary)
            )
        );
        Assert.AreEqual(
            1,
            fixture.TargetInvocations,
            "The original stock button must invoke once through hover-frame backpressure."
        );
        await live.AcknowledgeAsync(pending.Token);
        _ = await Frame(live);
    }

    [TestMethod]
    public async Task CompatibleDisplayedAdmissionIsLimitedToPointerDownAndWheelAndExpiresOnNewDisplayAck()
    {
        var fixture = new Fixture();
        var live = await StartAsync(Scenario(fixture, scroll: true), Request());
        var displayed = await Frame(live);
        await live.AcknowledgeAsync(displayed.Token);
        await live.PointerAsync(displayed.Token, 1, new(PointerCommandKind.Move, 0, 20, 20));
        var pending = await Frame(live);
        Assert.IsNull(
            await live.PointerAsync(displayed.Token, 2, new(PointerCommandKind.Move, 0, 20, 20))
        );
        Assert.IsNull(await live.KeyAsync(displayed.Token, 3, new(KeyCommandKind.Down, Key.Tab)));
        Assert.IsNull(await live.TextAsync(displayed.Token, 4, new(TextInputKind.Commit, "stale")));
        Assert.AreEqual(
            InputDispatchStatus.Delivered,
            (await live.WheelAsync(displayed.Token, 5, new(20, 20, 0, 30)))!.Status
        );
        await live.AcknowledgeAsync(pending.Token);
        var scrolled = await Frame(live);
        Assert.IsNull(
            await live.WheelAsync(displayed.Token, 6, new(20, 20, 0, 30)),
            "A superseded display acknowledgment cannot authorize a later wheel."
        );
        await live.AcknowledgeAsync(scrolled.Token);
        Assert.IsNull(
            await live.PointerAsync(
                displayed.Token,
                7,
                new(PointerCommandKind.Down, 0, 20, 20, PointerButton.Primary)
            )
        );
    }

    [TestMethod]
    [DataRow("geometry")]
    [DataRow("target")]
    [DataRow("scroll")]
    public async Task DisplayedFrameCannotAuthorizePointerDownOrWheelAfterInputProjectionChanged(
        string change
    )
    {
        var fixture = new Fixture();
        var live = await StartAsync(
            Scenario(fixture, movingTarget: change == "geometry", scroll: change == "scroll"),
            Request()
        );
        var displayed = await Frame(live);
        await live.AcknowledgeAsync(displayed.Token);
        if (change == "geometry")
            await fixture.SetAsync(1);
        else
        {
            var done = NewCompletion();
            fixture.Scope!.Post(() =>
            {
                if (change == "scroll")
                    fixture.ScrollViewport!.Offset = new(0, 40);
                else
                {
                    var replacement = fixture.Composition!.Child(fixture.Root!, "new-hit-owner");
                    replacement.Present(fixture.Theme!, author: Style.Empty.Width(40).Height(20));
                    replacement.AttachBehaviors(new TargetProbe(fixture));
                }
                done.SetResult();
            });
            await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        var pending = await Frame(live);
        Assert.IsNull(
            await live.PointerAsync(
                displayed.Token,
                1,
                new(PointerCommandKind.Down, 0, 20, 10, PointerButton.Primary)
            )
        );
        Assert.IsNull(await live.WheelAsync(displayed.Token, 2, new(20, 10, 0, 30)));
        Assert.AreEqual(0, fixture.TargetInvocations);
        await live.AcknowledgeAsync(pending.Token);
    }

    [TestMethod]
    public async Task FocusGainPreservesFreshBlurFrameForItsInitiatingPointerDown()
    {
        var fixture = new Fixture();
        var live = await StartAsync(Scenario(fixture, behavior: new InputProbe()), Request());
        var first = await Frame(live);
        await live.SetFocusAsync(live.Identity, false);
        await live.AcknowledgeAsync(first.Token);
        var blurred = await Frame(live);
        await live.AcknowledgeAsync(blurred.Token);
        Assert.IsTrue(await live.SetFocusAsync(live.Identity, true));
        var delivered = await live.PointerAsync(
            blurred.Token,
            1,
            new(PointerCommandKind.Down, 7, 20, 20, PointerButton.Primary)
        );
        Assert.AreEqual(InputDispatchStatus.Delivered, delivered!.Status);
    }

    [TestMethod]
    public async Task ExplicitButtonReleaseClearsOnlyItsOwnPointerBookkeeping()
    {
        var fixture = new Fixture();
        var behavior = new InputProbe();
        var live = await StartAsync(Scenario(fixture, behavior: behavior), Request());
        var first = await Frame(live);
        await live.PointerAsync(
            first.Token,
            1,
            new(PointerCommandKind.Down, 7, 20, 20, PointerButton.Primary)
        );
        await live.AcknowledgeAsync(first.Token);
        var focused = await Frame(live);
        await live.PointerAsync(
            focused.Token,
            2,
            new(PointerCommandKind.Down, 7, 20, 20, PointerButton.Secondary)
        );
        await live.PointerAsync(
            focused.Token,
            3,
            new(PointerCommandKind.Up, 7, 20, 20, PointerButton.Secondary)
        );
        await live.SetFocusAsync(live.Identity, false);
        Assert.AreEqual(
            1,
            behavior.Cancels,
            "Primary capture must remain for focus-loss cancellation."
        );
    }

    [TestMethod]
    public async Task CommittedTextChangesEditorPixelsAndStoppedSessionRejectsInputAndFrames()
    {
        var fixture = new Fixture();
        var live = await StartAsync(Scenario(fixture, editor: true), Request());
        var first = await Frame(live);
        await live.KeyAsync(first.Token, 1, new(KeyCommandKind.Down, Key.Tab));
        await live.AcknowledgeAsync(first.Token);
        var focused = await Frame(live);
        await live.KeyAsync(focused.Token, 2, new(KeyCommandKind.Down, Key.End));
        await live.AcknowledgeAsync(focused.Token);
        var before = await Frame(live);
        var text = await live.TextAsync(before.Token, 3, new(TextInputKind.Commit, "-proof"));
        Assert.AreEqual(InputDispatchStatus.Delivered, text!.Status);
        Assert.AreEqual("seed-proof", fixture.Text);
        await live.AcknowledgeAsync(before.Token);
        var after = await Frame(live);
        Assert.IsGreaterThan(0, ChangedEditorPixels(before.Png, after.Png));
        await live.StopAsync();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
            live.KeyAsync(after.Token, 4, new(KeyCommandKind.Up, Key.Tab))
        );
        await Assert.ThrowsExactlyAsync<System.Threading.Channels.ChannelClosedException>(
            async () =>
                await Frame(live)
        );
        Assert.AreEqual(0, live.Diagnostics.PressedKeys);
        Assert.AreEqual(0, live.Diagnostics.LiveTextBlobs);
    }

    [TestMethod]
    public async Task PreCancelledStartupInvokesNeitherSetupNorRoot()
    {
        var setups = 0;
        var roots = 0;
        var fixture = new Fixture();
        var scenario = Scenario(
            fixture,
            (_, _) =>
            {
                setups++;
                return ValueTask.CompletedTask;
            },
            rootReady: _ => roots++
        );
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await StartAsync(scenario, Request(), cancellation.Token)
        );
        Assert.AreEqual(0, setups);
        Assert.AreEqual(0, roots);
    }

    [TestMethod]
    public async Task RequestedDarkHighContrastThemeIsVisibleDuringSynchronousSetup()
    {
        var appearance = new ThemeAppearance(ThemeColorScheme.Dark, ThemeContrast.High);
        var token = new Token<string>("setup-theme-token", "missing");
        var property = new Property<string>("setup-theme-property", "unset");
        var observed = "not-run";
        var fixture = new Fixture();
        var scenario = Scenario(
            fixture,
            (context, _) =>
            {
                Assert.AreEqual(appearance, context.Session.Theme.Appearance);
                var root = context.Session.Composition.Child(
                    context.Session.Composition.Root,
                    "setup-theme-observation"
                );
                root.Present(context.Session.Theme, author: Style.Empty.Set(property, token));
                observed = root.Resolve(property).Value;
                return ValueTask.CompletedTask;
            },
            themeFactory: requested =>
            {
                Assert.AreEqual(
                    appearance,
                    requested,
                    "The factory must never receive default Light for this request."
                );
                return new Theme("dark-high-contrast").Set(token, "requested-dark-token");
            }
        );
        var live = await StartAsync(
            scenario,
            Request() with
            {
                ColorScheme = "dark",
                Contrast = "high",
            }
        );
        await Frame(live);
        Assert.AreEqual("requested-dark-token", observed);
    }

    [TestMethod]
    public async Task CleanupReprojectionRevokesOldInputWithoutLosingItsFrameAcknowledgment()
    {
        var fixture = new Fixture();
        var live = await StartAsync(Scenario(fixture, movingTarget: true), Request());
        var first = await Frame(live);
        using (var bitmap = SKBitmap.Decode(first.Png))
            Assert.AreEqual(SKColors.Blue, bitmap.GetPixel(100, 10));
        await fixture.SetAsync(1);
        await live.SetFocusAsync(live.Identity, false);
        await live.SetFocusAsync(live.Identity, true);
        var stale = await live.PointerAsync(
            first.Token,
            1,
            new(PointerCommandKind.Down, 1, 100, 10, PointerButton.Primary)
        );
        Assert.IsNull(stale);
        Assert.AreEqual(
            0,
            fixture.TargetInvocations,
            "Old pixels must not authorize a moved, unseen target."
        );
        Assert.IsTrue((await live.AcknowledgeAsync(first.Token)).Accepted);
        var moved = await Frame(live);
        using (var bitmap = SKBitmap.Decode(moved.Png))
            Assert.AreEqual(SKColors.Green, bitmap.GetPixel(100, 10));
        var current = await live.PointerAsync(
            moved.Token,
            2,
            new(PointerCommandKind.Down, 1, 100, 10, PointerButton.Primary)
        );
        Assert.AreEqual(InputDispatchStatus.Delivered, current!.Status);
        Assert.AreEqual(1, fixture.TargetInvocations);
    }

    [TestMethod]
    public async Task SyntheticKeyReleasesReachAuthorWhenFirstReleaseChangesLayout()
    {
        var fixture = new Fixture();
        var releases = 0;
        var behavior = new InputProbe
        {
            OnKeyUp = _ =>
            {
                if (++releases == 1)
                    fixture
                        .Composition!.Child(fixture.Root!, "release-layout")
                        .Present(fixture.Theme!, author: Style.Empty.Width(140).Height(120));
            },
        };
        var live = await StartAsync(Scenario(fixture, behavior: behavior), Request());
        var first = await Frame(live);
        await live.PointerAsync(
            first.Token,
            1,
            new(PointerCommandKind.Down, 7, 20, 20, PointerButton.Primary)
        );
        await live.AcknowledgeAsync(first.Token);
        var focused = await Frame(live);
        await live.KeyAsync(focused.Token, 2, new(KeyCommandKind.Down, Key.A));
        await live.KeyAsync(focused.Token, 3, new(KeyCommandKind.Down, Key.C));
        CollectionAssert.AreEquivalent(new[] { Key.A, Key.C }, behavior.HeldKeys.ToArray());
        await live.SetFocusAsync(live.Identity, false);
        Assert.AreEqual(2, behavior.KeyUps);
        Assert.AreEqual(
            0,
            behavior.HeldKeys.Count,
            "Both independent author-held keys must be released."
        );
        CollectionAssert.AreEquivalent(new[] { Key.A, Key.C }, behavior.ReleasedKeys.ToArray());
    }

    [TestMethod]
    public async Task ParentCancellationDuringAsyncStartupWaitsForOwnedCleanup()
    {
        var fixture = new Fixture();
        var entered = NewCompletion();
        var scenario = Scenario(
            fixture,
            async (context, token) =>
            {
                context.OnDispose(() =>
                {
                    fixture.DisposalThread = Environment.CurrentManagedThreadId;
                    return ValueTask.CompletedTask;
                });
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
        );
        using var cancellation = new CancellationTokenSource();
        var startup = StartAsync(scenario, Request(), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await startup.WaitAsync(TimeSpan.FromSeconds(5))
        );
        Assert.AreEqual(0, fixture.OwnerThread);
        Assert.IsGreaterThan(0, fixture.DisposalThread);
    }

    [TestMethod]
    public async Task FailedAsyncStartupCompletesOwnedCleanupBeforeReportingFailure()
    {
        var fixture = new Fixture();
        var scenario = Scenario(
            fixture,
            async (context, _) =>
            {
                context.OnDispose(() =>
                {
                    fixture.DisposalThread = Environment.CurrentManagedThreadId;
                    return ValueTask.CompletedTask;
                });
                await Task.Yield();
                throw new InvalidOperationException("async setup failed");
            }
        );
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await StartAsync(scenario, Request())
        );
        StringAssert.Contains(error.Message, "async setup failed");
        Assert.AreEqual(0, fixture.OwnerThread, "No root factory runs after failed startup.");
        Assert.IsGreaterThan(0, fixture.DisposalThread);
    }

    [TestMethod]
    public async Task CommandQueueOverflowStopsRatherThanLosingAnOrderedRelease()
    {
        var entered = NewCompletion();
        var release = NewCompletion();
        var behavior = new InputProbe { EnterDown = entered, ReleaseDown = release };
        var fixture = new Fixture();
        var live = await StartAsync(
            Scenario(fixture, behavior: behavior),
            Request(),
            barriers: [release]
        );
        ExpectFailedCompletion(live);
        var first = await Frame(live);
        var down = live.PointerAsync(
            first.Token,
            1,
            new(PointerCommandKind.Down, 7, 20, 20, PointerButton.Primary)
        );
        var queued = new List<Task<PreviewFrameAcknowledgment>>();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var index = 0; index < 257; index++)
                queued.Add(live.AcknowledgeAsync(first.Token));
            Assert.IsTrue(queued[^1].IsFaulted, "Overflow must reject immediately.");
        }
        finally
        {
            release.TrySetResult();
        }
        await down.WaitAsync(TimeSpan.FromSeconds(5));
        var error = await Assert.ThrowsAsync<Exception>(async () =>
            await live.Completion.WaitAsync(TimeSpan.FromSeconds(5))
        );
        StringAssert.Contains(error.ToString(), "queue is full");
        foreach (var command in queued)
            await Assert.ThrowsAsync<Exception>(async () => await command);
        Assert.IsTrue(live.Diagnostics.ResourcesDisposed);
        Assert.AreEqual(0, live.Diagnostics.ActivePointers);
        Assert.AreEqual(1, behavior.Cancels);
    }

    [TestMethod]
    public async Task FailedInputCleanupAndFixtureCleanupKeepCompletionFailed()
    {
        var fixture = new Fixture();
        var behavior = new InputProbe { FailCancel = true };
        var scenario = Scenario(
            fixture,
            (context, _) =>
            {
                context.OnDispose(() =>
                    throw new InvalidOperationException("fixture cleanup failed")
                );
                return ValueTask.CompletedTask;
            },
            behavior: behavior
        );
        var live = await StartAsync(scenario, Request());
        ExpectFailedCompletion(live);
        var first = await Frame(live);
        await live.PointerAsync(
            first.Token,
            1,
            new(PointerCommandKind.Down, 7, 20, 20, PointerButton.Primary)
        );
        await live.AcknowledgeAsync(first.Token);
        var focused = await Frame(live);
        await live.KeyAsync(focused.Token, 2, new(KeyCommandKind.Down, Key.A));
        var failure = await Assert.ThrowsAsync<Exception>(async () => await live.StopAsync());
        StringAssert.Contains(failure.ToString(), "pointer cleanup failed");
        StringAssert.Contains(failure.ToString(), "fixture cleanup failed");
        Assert.AreEqual(1, behavior.Cancels);
        Assert.AreEqual(1, behavior.KeyUps);
        Assert.IsNotNull(behavior.AttachedContext);
        Assert.AreSame(behavior.AttachedContext, behavior.CancelContext);
        Assert.AreSame(behavior.AttachedContext, behavior.KeyUpContext);
        Assert.AreEqual(fixture.OwnerThread, behavior.InputThread);
        Assert.AreEqual(0, live.Diagnostics.ActivePointers);
        Assert.IsTrue(live.Diagnostics.ResourcesDisposed);
        Assert.AreEqual(0, live.Diagnostics.LiveTextBlobs);
        await Assert.ThrowsAsync<Exception>(async () => await live.Completion);
    }

    private static TaskCompletionSource NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<PreviewRenderedFrame> Frame(PreviewRenderSession live)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await live.ReadFrameAsync(cancellation.Token);
    }

    private static void AssertPixel(PreviewRenderedFrame frame, SKColor expected)
    {
        using var bitmap = SKBitmap.Decode(frame.Png);
        Assert.AreEqual(160, bitmap.Width);
        Assert.AreEqual(120, bitmap.Height);
        Assert.AreEqual(expected, bitmap.GetPixel(150, 110));
    }

    private static int ChangedEditorPixels(byte[] before, byte[] after)
    {
        using var a = SKBitmap.Decode(before);
        using var b = SKBitmap.Decode(after);
        var changed = 0;
        for (var y = 0; y < 40; y++)
        for (var x = 0; x < 160; x++)
            if (a.GetPixel(x, y) != b.GetPixel(x, y))
                changed++;
        return changed;
    }

    private static PreviewScenario Scenario(
        Fixture fixture,
        Func<PreviewSetupContext, CancellationToken, ValueTask>? setup = null,
        Action<ApplicationSession>? rootReady = null,
        bool motion = false,
        Behavior? behavior = null,
        bool editor = false,
        bool movingTarget = false,
        bool button = false,
        bool scroll = false,
        Func<ThemeAppearance, Theme>? themeFactory = null
    ) =>
        new PreviewCatalogBuilder()
            .Add(
                new PreviewScenarioDescriptor(
                    "live",
                    "Live host contracts",
                    new("fixture.csproj", "Fixture.cs", "Fixture"),
                    new(
                        new(160, 120, 1),
                        ThemeAppearance.Light,
                        themeFactory ?? (_ => ControlThemes.Light),
                        1,
                        CultureInfo.InvariantCulture,
                        CultureInfo.InvariantCulture,
                        DateTimeOffset.UnixEpoch
                    )
                ),
                async (context, token) =>
                {
                    if (setup is not null)
                        await setup(context, token);
                    return fixture;
                },
                (data, session) =>
                {
                    data.OwnerThread = Environment.CurrentManagedThreadId;
                    data.Scope = session.Scope;
                    data.Composition = session.Composition;
                    data.Value = session.Scope.Signal(0, "live-paint");
                    rootReady?.Invoke(session);
                    if (button)
                        return Components.Button(
                            "Increment",
                            () => data.TargetInvocations++,
                            Style.Empty.Width(160).Height(40)
                        );
                    if (scroll)
                    {
                        data.ScrollViewport = new(session.Scope);
                        return Components.ScrollViewport(
                            [
                                Components.Text(
                                    "Scroll content",
                                    Style.Empty.Height(300).MainShrink(0)
                                ),
                            ],
                            style: Style.Empty.Width(160).Height(80),
                            viewport: data.ScrollViewport
                        );
                    }
                    if (editor)
                        return Components.TextField(
                            "seed",
                            text => data.Text = text,
                            Style.Empty.Width(160).Height(40)
                        );
                    return ComponentRecipe.Create(
                        "live-root",
                        (context, root) =>
                        {
                            data.Root = root;
                            data.Theme = context.Theme;
                            var style = Style
                                .Empty.Width(160)
                                .Height(120)
                                .Bind(
                                    VisualProperties.Background,
                                    () =>
                                        Brush.Solid(
                                            Color.Parse(
                                                data.Value.Value == 0 ? "#0000FF" : "#FF0000"
                                            )
                                        )
                                );
                            if (motion)
                                style = style.Transition(
                                    VisualProperties.Background,
                                    Motion.Duration(80, Easing.Linear)
                                );
                            if (movingTarget)
                                style = style.Bind(
                                    LayoutProperties.Padding,
                                    () => new Insets(data.Value.Value == 0 ? 0 : 80, 0, 0, 0)
                                );
                            root.Present(context.Theme, author: style);
                            if (behavior is not null)
                                root.AttachBehaviors(behavior);
                            context.Mount(
                                root,
                                movingTarget
                                    ?
                                    [
                                        ComponentRecipe.Create(
                                            "moving-target",
                                            (targetContext, target) =>
                                            {
                                                target.Present(
                                                    targetContext.Theme,
                                                    author: Style
                                                        .Empty.Width(40)
                                                        .Height(20)
                                                        .Set(
                                                            VisualProperties.Background,
                                                            Brush.Solid(Color.Parse("#008000"))
                                                        )
                                                );
                                                target.AttachBehaviors(new TargetProbe(data));
                                            }
                                        ),
                                    ]
                                    : [Components.Text("Retained pixels")]
                            );
                        }
                    );
                }
            )
            .Build()
            .Get("live");

    private static PreviewWorkerRequest Request() =>
        new()
        {
            ProtocolVersion = 2,
            Kind = PreviewProtocol.CaptureRequestKind,
            SessionId = "session",
            Generation = "generation",
            RequestId = "request",
            ProjectTargetDigest = new('a', 64),
            InputDigest = new('b', 64),
            ArtifactDigest = new('c', 64),
            ScenarioId = "live",
            PresentationId = "default",
            OutputDirectory = Path.GetTempPath(),
            LogicalWidth = 160,
            LogicalHeight = 120,
            Scale = 1,
            ColorScheme = "light",
            Contrast = "normal",
            Density = 1,
        };

    private sealed class Fixture
    {
        internal Signal<int>? Value;
        internal ReactiveScope? Scope;
        internal int OwnerThread;
        internal int DisposalThread;
        internal int TimerOwnerThread;
        internal string Text = "seed";
        internal int TargetInvocations;
        internal Element? Root;
        internal Composition? Composition;
        internal ThemeContext? Theme;
        internal TimeProvider? Clock;
        internal DateTimeOffset InitialUtc;
        internal ITimer? Timer;
        internal ViewportState? ScrollViewport;

        internal Task SetAsync(int value)
        {
            var done = NewCompletion();
            Scope!.Post(() =>
            {
                Value!.Value = value;
                done.SetResult();
            });
            return done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class OwnedStartup(
        CancellationTokenSource lifetime,
        TaskCompletionSource[] barriers
    )
    {
        internal CancellationTokenSource Lifetime { get; } = lifetime;
        internal TaskCompletionSource[] Barriers { get; } = barriers;
        internal Task<PreviewRenderSession> Startup { get; set; } = null!;
        internal PreviewRenderSession? Session;
        internal bool StartupFailed;
        internal bool ExpectedFailure;
    }

    private sealed class InputProbe : Behavior
    {
        internal bool FailCancel;
        internal int Cancels;
        internal int KeyUps;
        internal int PointerUps;
        internal int FocusGains;
        internal int InputThread;
        internal SynchronizationContext? AttachedContext;
        internal SynchronizationContext? CancelContext;
        internal SynchronizationContext? KeyUpContext;
        internal TaskCompletionSource? EnterDown;
        internal TaskCompletionSource? ReleaseDown;
        internal Action<Key>? OnKeyUp;
        internal HashSet<Key> HeldKeys { get; } = [];
        internal List<Key> ReleasedKeys { get; } = [];
        public override string Name => "live-input";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            AttachedContext = SynchronizationContext.Current;
            context.SetSemantics(
                SemanticDeclaration.Create(SemanticRole.Group, "Input owner").Build()
            );
            context.MakeFocusable();
            context.OnFocus(route =>
            {
                if (route.Command.Kind == FocusCommandKind.Gained)
                    FocusGains++;
            });
            context.OnPointer(route =>
            {
                InputThread = Environment.CurrentManagedThreadId;
                if (route.Command.Kind == PointerCommandKind.Down)
                {
                    EnterDown?.TrySetResult();
                    ReleaseDown?.Task.GetAwaiter().GetResult();
                    route.Focus();
                    route.Capture();
                }
                if (route.Command.Kind == PointerCommandKind.Cancel)
                {
                    CancelContext = SynchronizationContext.Current;
                    Cancels++;
                    if (FailCancel)
                        throw new InvalidOperationException("pointer cleanup failed");
                }
                if (route.Command.Kind == PointerCommandKind.Up)
                    PointerUps++;
            });
            context.OnKey(route =>
            {
                InputThread = Environment.CurrentManagedThreadId;
                if (route.Command.Kind == KeyCommandKind.Down)
                    HeldKeys.Add(route.Command.Key);
                if (route.Command.Kind == KeyCommandKind.Up)
                {
                    KeyUpContext = SynchronizationContext.Current;
                    KeyUps++;
                    HeldKeys.Remove(route.Command.Key);
                    ReleasedKeys.Add(route.Command.Key);
                    OnKeyUp?.Invoke(route.Command.Key);
                }
            });
        }
    }

    private sealed class TargetProbe(Fixture fixture) : Behavior
    {
        public override string Name => "moving-target-input";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            context.SetSemantics(
                SemanticDeclaration.Create(SemanticRole.Group, "Moving target").Build()
            );
            context.OnPointer(route =>
            {
                if (route.Command.Kind == PointerCommandKind.Down)
                    fixture.TargetInvocations++;
            });
        }
    }
}
