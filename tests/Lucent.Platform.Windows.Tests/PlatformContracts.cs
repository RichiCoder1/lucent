using Lucent.Core;
using Lucent.Platform.Windows;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed class PlatformContracts
{
    [TestMethod]
    public void WindowsBuilderContract()
    {
        var builder = LucentApplication.CreateBuilder();
        Assert(
            ReferenceEquals(builder.UseWindows(), builder),
            "UseWindows did not retain the builder."
        );
        _ = builder.Build();
        LucentApplicationBuilder? missing = null;
        try
        {
            _ = missing!.UseWindows();
            throw new InvalidOperationException("UseWindows accepted a null builder.");
        }
        catch (ArgumentNullException) { }
    }

    [TestMethod]
    public void DpiAndResourceMatrix()
    {
        foreach (
            var (scale, width, height) in new[]
            {
                (1f, 800, 500),
                (1.25f, 1000, 625),
                (1.5f, 1200, 750),
                (2f, 1600, 1000),
            }
        )
        {
            var viewport = new WindowsViewport(width, height, scale);
            Assert(
                viewport.LogicalWidth == 800
                    && viewport.LogicalHeight == 500
                    && viewport.Format == WindowsPresentationContract.SurfaceFormat,
                $"DPI {scale:R} did not retain one logical 800x500 viewport."
            );
        }
        var large = new WindowsViewport(3840, 2160, 1.25f);
        Assert(
            large.LogicalWidth == 3072 && large.LogicalHeight == 1728,
            "4K backing dimensions overflowed or double-scaled."
        );
        var fractional = new WindowsViewport(1001, 751, 1.25f);
        Assert(
            MathF.Abs(fractional.LogicalWidth - 800.8f) < .001f
                && MathF.Abs(fractional.LogicalHeight - 600.8f) < .001f,
            "Noninteger scale discarded logical precision."
        );

        var resources = new CpuResourceState();
        Assert(
            WindowsPresentationContract.SurfaceFormat
                is { Color: CpuColorFormat.Rgba8888, Alpha: CpuAlphaFormat.Premultiplied }
                && WindowsPresentationContract.VsyncInterval == 1,
            "The CPU pixel, alpha, and vsync contract was not explicit."
        );
        var first = new CpuResourceDescriptor(1000, 625, WindowsPresentationContract.SurfaceFormat);
        Assert(resources.NeedsRecreation(first), "First backing allocation was not requested.");
        resources.Commit(first);
        Assert(!resources.NeedsRecreation(first), "Unchanged backing allocation was recreated.");
        Assert(
            !resources.NeedsRecreation(
                new CpuResourceDescriptor(1000, 625, WindowsPresentationContract.SurfaceFormat)
            ),
            "Scale-only repaint would recreate resources."
        );
        var resized = new CpuResourceDescriptor(
            1200,
            750,
            WindowsPresentationContract.SurfaceFormat
        );
        Assert(resources.NeedsRecreation(resized), "Backing resize did not recreate resources.");
        resources.Commit(resized);
        var reformatted = new CpuResourceDescriptor(
            1200,
            750,
            new(CpuColorFormat.Bgra8888, CpuAlphaFormat.Premultiplied)
        );
        Assert(resources.NeedsRecreation(reformatted), "Format change did not recreate resources.");
        resources.Commit(reformatted);
        Assert(
            resources.CreationCount == 3,
            "Resource recreation was not bounded to backing size or format changes."
        );
    }

    [TestMethod]
    public void DpiAwarenessContract()
    {
        Assert(
            WindowsDpi.EnsurePerMonitorV2() && WindowsDpi.IsPerMonitorV2(),
            "Per-Monitor V2 was not established."
        );
        Assert(
            WindowsDpi.EnsurePerMonitorV2() && WindowsDpi.IsPerMonitorV2(),
            "Repeated Per-Monitor V2 initialization did not tolerate the already-effective context."
        );
    }

    [TestMethod]
    public void FrameSchedulingMatrix()
    {
        var scheduler = new WindowsFrameScheduler();
        var normal = new WindowsViewport(1000, 625, 1.25f);
        Assert(
            scheduler.IsFrameRequested
                && !scheduler.TryBegin(new WindowsViewport(0, 0, 1.25f))
                && scheduler.IsFrameRequested
                && scheduler.ShouldWaitForEvent,
            "Zero-sized startup did not retain its frame request while returning to event-driven idle."
        );
        scheduler.Observe(WindowsFrameEvent.PixelSizeChanged);
        Assert(scheduler.TryBegin(normal), "Initial frame was not requested.");
        scheduler.Complete(FrameTiming.FromTimestamps(10, 20, 30, 40, 50));
        Assert(
            !scheduler.IsFrameRequested
                && !scheduler.TryBegin(normal)
                && scheduler.PresentedFrames == 1,
            "Idle produced an extra frame."
        );
        var timing = scheduler.LastTiming;
        Assert(
            timing.Projection > TimeSpan.Zero
                && timing.Raster > TimeSpan.Zero
                && timing.Upload > TimeSpan.Zero
                && timing.Present > TimeSpan.Zero,
            "Frame timings did not retain distinct phase durations."
        );

        scheduler.Observe(WindowsFrameEvent.Moved); // Negative virtual-desktop coordinates are intentionally not part of frame sizing.
        Assert(!scheduler.IsFrameRequested, "A virtual-desktop move requested a resize frame.");
        scheduler.Observe(WindowsFrameEvent.Minimized);
        scheduler.Observe(WindowsFrameEvent.PixelSizeChanged);
        Assert(
            !scheduler.IsFrameRequested && !scheduler.TryBegin(new WindowsViewport(0, 0, 1.5f)),
            "Minimized zero-size window presented."
        );
        scheduler.Observe(WindowsFrameEvent.Restored);
        scheduler.Observe(WindowsFrameEvent.DisplayScaleChanged);
        Assert(
            !scheduler.TryBegin(new WindowsViewport(0, 0, 1.5f))
                && scheduler.IsFrameRequested
                && scheduler.ShouldWaitForEvent,
            "Restore/scale intent was lost while SDL temporarily reported zero backing output."
        );
        scheduler.Observe(WindowsFrameEvent.PixelSizeChanged);
        Assert(
            scheduler.TryBegin(new WindowsViewport(1200, 750, 1.5f)),
            "Restore/DPI/resize event reordering lost its coalesced frame."
        );
        scheduler.Complete(FrameTiming.FromTimestamps(100, 110, 120, 130, 140));
        Assert(
            scheduler.PresentedFrames == 2 && !scheduler.IsFrameRequested,
            "Restore emitted more than one frame."
        );
        scheduler.Observe(WindowsFrameEvent.Closed);
        Assert(!scheduler.IsOpen && !scheduler.IsFrameRequested, "Close left a pending frame.");

        var requested = new WindowsFrameScheduler();
        Assert(requested.TryBegin(normal), "Requested-frame scheduler did not begin initially.");
        requested.Complete(FrameTiming.FromTimestamps(200, 210, 220, 230, 240));
        requested.Request();
        Assert(
            requested.TryBegin(normal),
            "Event-caused invalidation did not request exactly one frame."
        );
        requested.Complete(FrameTiming.FromTimestamps(250, 260, 270, 280, 290));
        Assert(
            !requested.IsFrameRequested && requested.PresentedFrames == 2,
            "Event-caused invalidation left idle frame work behind."
        );

        var causal = new WindowsFrameScheduler();
        Assert(causal.TryBegin(normal), "Causality scheduler did not start.");
        causal.Complete(FrameTiming.FromTimestamps(1, 2, 3, 4, 5));
        causal.Request(FrameOperation.Input, 200);
        causal.Observe(WindowsFrameEvent.Exposed, 210);
        Assert(
            causal.TryBegin(normal)
                && causal.CurrentRequest is { Operation: FrameOperation.Input, Timestamp: 200 },
            "Expose changed an input frame's causality or timestamp."
        );
        causal.Complete(FrameTiming.FromTimestamps(210, 220, 230, 240, 250));
        causal.Request(FrameOperation.Input, 300);
        causal.Observe(WindowsFrameEvent.Resized, 310);
        Assert(
            causal.TryBegin(normal)
                && causal.CurrentRequest is { Operation: FrameOperation.Mixed, Timestamp: 300 },
            "Mixed input/resize frame was not explicitly classified from its earliest cause."
        );
        causal.Complete(FrameTiming.FromTimestamps(310, 320, 330, 340, 350));
        causal.Request();
        Assert(
            causal.TryBegin(normal) && causal.CurrentRequest.Operation == FrameOperation.Unpaired,
            "Unpaired frame work was not explicitly classified."
        );
    }

    [TestMethod]
    public void TelemetryContract()
    {
        using var activities = new System.Diagnostics.ActivityListener();
        string? activityOperation = null,
            metricOperation = null;
        activities.ShouldListenTo = static _ => true;
        activities.Sample = static (
            ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _
        ) => System.Diagnostics.ActivitySamplingResult.AllData;
        activities.SampleUsingParentId = static (
            ref System.Diagnostics.ActivityCreationOptions<string> _
        ) => System.Diagnostics.ActivitySamplingResult.AllData;
        activities.ActivityStopped = activity =>
        {
            foreach (var tag in activity.Tags)
                if (tag.Key == "lucent.operation")
                    activityOperation = tag.Value;
        };
        System.Diagnostics.ActivitySource.AddActivityListener(activities);
        using var metrics = new System.Diagnostics.Metrics.MeterListener();
        metrics.InstrumentPublished = (instrument, listener) =>
        {
            if (
                instrument.Meter.Name == "Lucent.Windows"
                && instrument.Name == "lucent.frame.present.ms"
            )
                listener.EnableMeasurementEvents(instrument);
        };
        metrics.SetMeasurementEventCallback<double>(
            (_, _, tags, _) =>
            {
                foreach (var tag in tags)
                    if (tag.Key == "lucent.operation")
                        metricOperation = tag.Value?.ToString();
            }
        );
        metrics.Start();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        PerformanceDiagnostics.EmitTelemetry(
            new FrameRequest(FrameOperation.Input, started).Complete(started + 40),
            FrameTiming.FromTimestamps(10, 20, 30, 40, 50)
        );
        Assert(
            activityOperation == "input"
                && metricOperation == "input"
                && PerformanceDiagnostics.AllowedTags.SequenceEqual([
                    "lucent.operation=input",
                    "lucent.operation=resize",
                    "lucent.operation=startup",
                    "lucent.operation=other",
                ]),
            $"Frame telemetry did not emit the fixed real-operation tag allowlist through local opt-in listeners: activity={activityOperation ?? "-"}, metric={metricOperation ?? "-"}."
        );
        PerformanceDiagnostics.EmitTelemetry(
            new FrameRequest(FrameOperation.Mixed, started).Complete(started + 40),
            FrameTiming.FromTimestamps(10, 20, 30, 40, 50)
        );
        Assert(
            activityOperation == "other" && metricOperation == "other",
            "Mixed frame telemetry escaped the fixed tag allowlist."
        );
        PerformanceDiagnostics.EmitTelemetry(
            new FrameRequest(FrameOperation.Unpaired, started).Complete(started + 40),
            FrameTiming.FromTimestamps(10, 20, 30, 40, 50)
        );
        Assert(
            activityOperation == "other" && metricOperation == "other",
            "Unpaired frame telemetry escaped the fixed tag allowlist."
        );
    }

    private static void UiaDispatcherContractCore()
    {
        if (!SDL3.SDL.Init(SDL3.SDL.InitFlags.Video))
            throw new InvalidOperationException("SDL_Init(UIA dispatcher): " + SDL3.SDL.GetError());
        try
        {
            using var dispatcher = new WindowsUiaDispatcher();
            var worker = Task.Run(() =>
                dispatcher.TryInvoke(
                    "test",
                    () => Environment.CurrentManagedThreadId,
                    out var owner
                )
                    ? owner
                    : -1
            );
            var until = Environment.TickCount64 + 2_000;
            while (dispatcher.PendingCount == 0 && Environment.TickCount64 < until)
                Thread.Sleep(1);
            var woke = false;
            var types = new List<uint>();
            while (!woke && Environment.TickCount64 < until)
            {
                while (SDL3.SDL.PollEvent(out var @event))
                {
                    types.Add(@event.Type);
                    woke |= dispatcher.IsWakeEvent(@event);
                }
                if (!woke)
                    Thread.Sleep(1);
            }
            var pending = dispatcher.PendingCount;
            var processed = dispatcher.Process();
            var completed = worker.Wait(2_000);
            var result = completed ? worker.Result : -2;
            if (
                pending != 1
                || !woke
                || processed != 1
                || !completed
                || result != Environment.CurrentManagedThreadId
            )
                throw new InvalidOperationException(
                    $"UIA dispatcher failed: registered={dispatcher.EventType} pending={pending} woke={woke} events=[{string.Join(',', types)}] processed={processed} completed={completed} result={result} owner={Environment.CurrentManagedThreadId}."
                );
            var rejected = Task.Run(() => dispatcher.TryInvoke("test", () => 1, out _));
            while (dispatcher.PendingCount == 0 && Environment.TickCount64 < until)
                Thread.Sleep(1);
            dispatcher.Dispose();
            if (rejected.GetAwaiter().GetResult())
                throw new InvalidOperationException(
                    "Disposed UIA dispatcher ran a pending external request."
                );

            using var raced = new WindowsUiaDispatcher(TimeSpan.FromMilliseconds(10));
            using var started = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var completedAfterClaim = Task.Run(() =>
                raced.TryInvoke(
                    "race",
                    () =>
                    {
                        started.Set();
                        release.Wait();
                        return 7;
                    },
                    out var value
                )
                    ? value
                    : -1
            );
            var raceUntil = Environment.TickCount64 + 2_000;
            while (raced.PendingCount == 0 && Environment.TickCount64 < raceUntil)
                Thread.Sleep(1);
            _ = Task.Run(() =>
            {
                started.Wait(2_000);
                Thread.Sleep(30);
                release.Set();
            });
            if (
                raced.Process() != 1
                || !completedAfterClaim.Wait(2_000)
                || completedAfterClaim.Result != 7
                || raced.PendingCount != 0
            )
                throw new InvalidOperationException(
                    "UIA dispatcher returned timeout after the owner claimed a request or leaked its queue slot."
                );

            const int capacity = 128; // WindowsUiaDispatcher's fixed slot capacity.
            using var saturated = new WindowsUiaDispatcher(TimeSpan.FromMilliseconds(20));
            var executed = 0;
            for (var round = 0; round < 3; round++)
            {
                var attempts = Enumerable
                    .Range(0, capacity * 2)
                    .Select(_ =>
                        Task.Run(() =>
                            saturated.TryInvoke(
                                "timeout",
                                () =>
                                {
                                    Interlocked.Increment(ref executed);
                                    return 1;
                                },
                                out _
                            )
                        )
                    )
                    .ToArray();
                if (
                    !Task.WaitAll(attempts, 5_000)
                    || saturated.PendingCount != capacity
                    || saturated.PendingCount > capacity
                    || executed != 0
                )
                    throw new InvalidOperationException(
                        $"Repeated queued UIA timeouts exceeded the bounded capacity: round={round} pending={saturated.PendingCount} executed={executed}."
                    );
            }
            if (saturated.Process() != 0 || saturated.PendingCount != 0)
                throw new InvalidOperationException(
                    $"Physical UIA dispatch did not drain cancelled requests: pending={saturated.PendingCount}."
                );
            var recovered = Task.Run(() =>
                saturated.TryInvoke("recovered", () => 9, out var value) ? value : -1
            );
            var recoveredUntil = Environment.TickCount64 + 2_000;
            while (saturated.PendingCount == 0 && Environment.TickCount64 < recoveredUntil)
                Thread.Sleep(1);
            if (
                saturated.PendingCount != 1
                || saturated.Process() != 1
                || !recovered.Wait(2_000)
                || recovered.Result != 9
                || saturated.PendingCount != 0
            )
                throw new InvalidOperationException(
                    "UIA dispatcher did not recover a slot after physical queue drainage."
                );
            var disposed = Task.Run(() => saturated.TryInvoke("disposed", () => 10, out _));
            var disposedUntil = Environment.TickCount64 + 2_000;
            while (saturated.PendingCount == 0 && Environment.TickCount64 < disposedUntil)
                Thread.Sleep(1);
            if (!disposed.Wait(2_000) || disposed.Result || saturated.PendingCount != 1)
                throw new InvalidOperationException(
                    "UIA dispatcher did not retain a queued timeout for disposal drainage."
                );
            saturated.Dispose();
            if (saturated.PendingCount != 0)
                throw new InvalidOperationException(
                    "UIA dispatcher Dispose did not release its queued slot."
                );

            using var failedPush = new WindowsUiaDispatcher(
                TimeSpan.FromMilliseconds(10),
                static (ref SDL3.SDL.Event _) => false
            );
            var rejectedPushes = Enumerable
                .Range(0, capacity * 2)
                .Select(_ => Task.Run(() => failedPush.TryInvoke("push-failure", () => 1, out _)))
                .ToArray();
            Task.WaitAll(rejectedPushes);
            if (
                rejectedPushes.Any(task => task.Result)
                || failedPush.PendingCount != capacity
                || failedPush.Process() != 0
                || failedPush.PendingCount != 0
            )
                throw new InvalidOperationException(
                    "Failed SDL event pushes escaped the dispatcher's physical queue bound or did not release on dequeue: accepted="
                        + rejectedPushes.Count(task => task.Result)
                        + " pending="
                        + failedPush.PendingCount
                        + "."
                );
        }
        finally
        {
            SDL3.SDL.Quit();
        }
    }

    [TestMethod]
    public void UiaSnapshotReadContract()
    {
        if (!SDL3.SDL.Init(SDL3.SDL.InitFlags.Video))
            throw new InvalidOperationException("SDL_Init(UIA snapshot): " + SDL3.SDL.GetError());
        nint window = 0;
        try
        {
            window = SDL3.SDL.CreateWindow(
                "Lucent UIA snapshot",
                1,
                1,
                SDL3.SDL.WindowFlags.Hidden
            );
            if (window == 0)
                throw new InvalidOperationException(
                    "SDL_CreateWindow(UIA snapshot): " + SDL3.SDL.GetError()
                );
            var hwnd = SDL3.SDL.GetPointerProperty(
                SDL3.SDL.GetWindowProperties(window),
                SDL3.SDL.Props.WindowWin32HWNDPointer,
                0
            );
            if (hwnd == 0)
                throw new InvalidOperationException("SDL UIA snapshot did not expose an HWND.");
            using var composition = new Lucent.Core.Composition(
                new Lucent.Core.ReactiveGraph(),
                "uia-snapshot"
            );
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(hwnd, composition, dispatcher);
            if (provider.ScrollPercent(-1, 101) != unchecked((int)0x80070057))
                throw new InvalidOperationException(
                    "Invalid UIA scroll percent did not return E_INVALIDARG at the provider boundary."
                );
            var read = Task.Run(() => provider.ProviderActions);
            if (!read.Wait(2_000) || dispatcher.PendingCount != 0)
                throw new InvalidOperationException(
                    "A read-only UIA callback waited for the SDL owner thread instead of the published snapshot."
                );
        }
        finally
        {
            if (window != 0)
                SDL3.SDL.DestroyWindow(window);
            SDL3.SDL.Quit();
        }
    }

    [TestMethod]
    public void ReactiveWakeContract()
    {
        if (!SDL3.SDL.Init(SDL3.SDL.InitFlags.Video))
            throw new InvalidOperationException("SDL_Init(reactive wake): " + SDL3.SDL.GetError());
        try
        {
            var graph = new Lucent.Core.ReactiveGraph();
            using var composition = new Lucent.Core.Composition(graph, "reactive-wake");
            WindowsWorkDispatcher.ValidateEventType(1);
            WindowsUiaDispatcher.ValidateEventType(1);
            ExpectRegisterFailure(() => WindowsWorkDispatcher.ValidateEventType(0));
            ExpectRegisterFailure(() => WindowsUiaDispatcher.ValidateEventType(0));
            var first = new TaskCompletionSource<int>();
            var second = new TaskCompletionSource<int>();
            var one = graph.Async(_ => first.Task, 0, "reactive-wake-one");
            var two = graph.Async(_ => second.Task, 0, "reactive-wake-two");
            _ = one.Value;
            _ = two.Value;
            using var dispatcher = new WindowsWorkDispatcher(composition);
            Task.WhenAll(Task.Run(() => first.SetResult(7)), Task.Run(() => second.SetResult(8)))
                .GetAwaiter()
                .GetResult();
            var until = Environment.TickCount64 + 2_000;
            var wakes = 0;
            while (wakes == 0 && Environment.TickCount64 < until)
            {
                while (SDL3.SDL.PollEvent(out var @event))
                    if (dispatcher.IsWakeEvent(@event))
                        wakes++;
                if (wakes == 0)
                    Thread.Sleep(1);
            }
            if (
                wakes != 1
                || !dispatcher.Process()
                || one.Value != 7
                || two.Value != 8
                || dispatcher.Process()
            )
                throw new InvalidOperationException(
                    "Worker-posted idle work did not wake once, drain on the UI thread, and leave zero idle work."
                );

            var noFrame = new TaskCompletionSource<int>();
            var discarded = graph.Async(_ => noFrame.Task, 0, "reactive-wake-disposed");
            _ = discarded.Value;
            Task.Run(() => noFrame.SetResult(9)).GetAwaiter().GetResult();
            discarded.Dispose();
            while (SDL3.SDL.PollEvent(out var @event))
                if (dispatcher.IsWakeEvent(@event)) { }
            if (dispatcher.Process())
                throw new InvalidOperationException("Disposed posted work requested a frame.");

            var fatalGraph = new Lucent.Core.ReactiveGraph();
            using var fatalComposition = new Lucent.Core.Composition(
                fatalGraph,
                "reactive-wake-fatal"
            );
            var fatalWork = new TaskCompletionSource<int>();
            var fatalValue = fatalGraph.Async(_ => fatalWork.Task, 0, "reactive-wake-fatal-value");
            _ = fatalValue.Value;
            var fatals = 0;
            using var failedPush = new WindowsWorkDispatcher(
                fatalComposition,
                static (ref SDL3.SDL.Event _) => false,
                _ => Interlocked.Increment(ref fatals)
            );
            Task.Run(() => fatalWork.SetResult(10)).GetAwaiter().GetResult();
            if (fatals != 1)
                throw new InvalidOperationException(
                    "Reactive SDL push failure did not invoke the fatal path exactly once."
                );

            var disposedGraph = new Lucent.Core.ReactiveGraph();
            using var disposedComposition = new Lucent.Core.Composition(
                disposedGraph,
                "reactive-wake-dispatcher-disposed"
            );
            var disposedWork = new TaskCompletionSource<int>();
            var disposedValue = disposedGraph.Async(
                _ => disposedWork.Task,
                0,
                "reactive-wake-dispatcher-disposed-value"
            );
            _ = disposedValue.Value;
            var pushes = 0;
            using var disposedDispatcher = new WindowsWorkDispatcher(
                disposedComposition,
                (ref SDL3.SDL.Event _) =>
                {
                    Interlocked.Increment(ref pushes);
                    return true;
                }
            );
            disposedDispatcher.Dispose();
            Task.Run(() => disposedWork.SetResult(11)).GetAwaiter().GetResult();
            if (pushes != 0)
                throw new InvalidOperationException(
                    "Disposed reactive dispatcher accepted a late worker wake."
                );

            var raceGraph = new Lucent.Core.ReactiveGraph();
            using var raceComposition = new Lucent.Core.Composition(
                raceGraph,
                "reactive-wake-dispose-race"
            );
            var raceWork = new TaskCompletionSource<int>();
            var raceValue = raceGraph.Async(
                _ => raceWork.Task,
                0,
                "reactive-wake-dispose-race-value"
            );
            _ = raceValue.Value;
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var pushed = new ManualResetEventSlim();
            using var disposeStarted = new ManualResetEventSlim();
            using var raceDispatcher = new WindowsWorkDispatcher(
                raceComposition,
                (ref SDL3.SDL.Event _) =>
                {
                    entered.Set();
                    release.Wait();
                    pushed.Set();
                    return true;
                }
            );
            var producer = Task.Run(() => raceWork.SetResult(12));
            if (!entered.Wait(2_000))
                throw new InvalidOperationException(
                    "Blocking reactive wake did not enter fake SDL push."
                );
            var releaser = Task.Run(() =>
            {
                disposeStarted.Wait();
                release.Set();
            });
            disposeStarted.Set();
            raceDispatcher.Dispose();
            if (!pushed.IsSet || !producer.Wait(2_000) || !releaser.Wait(2_000))
                throw new InvalidOperationException(
                    "Reactive dispatcher disposal returned before its in-flight wake completed."
                );
        }
        finally
        {
            SDL3.SDL.Quit();
        }
    }

    static void ExpectRegisterFailure(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException("Expected SDL event registration failure.");
    }

    static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    [TestMethod]
    public void UiaDispatcherContract()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                UiaDispatcherContractCore();
            }
            catch (Exception error)
            {
                failure = error;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30)))
            throw new TimeoutException(
                "Dedicated UIA dispatcher contract did not finish in 30 seconds."
            );
        if (failure is not null)
            throw new InvalidOperationException(
                "Dedicated UIA dispatcher contract failed.",
                failure
            );
    }
}
