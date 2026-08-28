using Lucent.Platform.Windows;

if (args is ["--listener-proof"]) return ListenerProof.Run();

try
{
    DpiAndResourceMatrix();
    DpiAwarenessContract();
    FrameSchedulingMatrix();
    WindowsHostContracts.InputAdapterAndRoutingContract();
    WindowsHostContracts.InputInstallConvergenceContract();
    WindowsHostContracts.InputReconciliationPaintContract();
    WindowsHostContracts.SettingsAndAppearanceContract();
    WindowsHostContracts.ClipboardAndCursorContract();
    WindowsHostContracts.ThrowingCleanupContract();
    Console.WriteLine("Lucent.Platform.Windows presenter contract/AOT seam: PASS");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("Lucent.Platform.Windows presenter contract/AOT seam: FAIL: " + error.Message);
    return 1;
}

static void DpiAndResourceMatrix()
{
    foreach (var (scale, width, height) in new[] { (1f, 800, 500), (1.25f, 1000, 625), (1.5f, 1200, 750), (2f, 1600, 1000) })
    {
        var viewport = new WindowsViewport(width, height, scale);
        Assert(viewport.LogicalWidth == 800 && viewport.LogicalHeight == 500 && viewport.Format == WindowsPresentationContract.SurfaceFormat,
            $"DPI {scale:R} did not retain one logical 800x500 viewport.");
    }
    var large = new WindowsViewport(3840, 2160, 1.25f);
    Assert(large.LogicalWidth == 3072 && large.LogicalHeight == 1728, "4K backing dimensions overflowed or double-scaled.");
    var fractional = new WindowsViewport(1001, 751, 1.25f);
    Assert(MathF.Abs(fractional.LogicalWidth - 800.8f) < .001f && MathF.Abs(fractional.LogicalHeight - 600.8f) < .001f, "Noninteger scale discarded logical precision.");

    var resources = new CpuResourceState();
    Assert(WindowsPresentationContract.SurfaceFormat is { Color: CpuColorFormat.Rgba8888, Alpha: CpuAlphaFormat.Premultiplied } && WindowsPresentationContract.VsyncInterval == 1,
        "The CPU pixel, alpha, and vsync contract was not explicit.");
    var first = new CpuResourceDescriptor(1000, 625, WindowsPresentationContract.SurfaceFormat);
    Assert(resources.NeedsRecreation(first), "First backing allocation was not requested.");
    resources.Commit(first);
    Assert(!resources.NeedsRecreation(first), "Unchanged backing allocation was recreated.");
    Assert(!resources.NeedsRecreation(new CpuResourceDescriptor(1000, 625, WindowsPresentationContract.SurfaceFormat)), "Scale-only repaint would recreate resources.");
    var resized = new CpuResourceDescriptor(1200, 750, WindowsPresentationContract.SurfaceFormat);
    Assert(resources.NeedsRecreation(resized), "Backing resize did not recreate resources.");
    resources.Commit(resized);
    var reformatted = new CpuResourceDescriptor(1200, 750, new(CpuColorFormat.Bgra8888, CpuAlphaFormat.Premultiplied));
    Assert(resources.NeedsRecreation(reformatted), "Format change did not recreate resources.");
    resources.Commit(reformatted);
    Assert(resources.CreationCount == 3, "Resource recreation was not bounded to backing size or format changes.");
}

static void DpiAwarenessContract()
{
    Assert(WindowsDpi.EnsurePerMonitorV2() && WindowsDpi.IsPerMonitorV2(), "Per-Monitor V2 was not established.");
    Assert(WindowsDpi.EnsurePerMonitorV2() && WindowsDpi.IsPerMonitorV2(), "Repeated Per-Monitor V2 initialization did not tolerate the already-effective context.");
}

static void FrameSchedulingMatrix()
{
    var scheduler = new WindowsFrameScheduler();
    var normal = new WindowsViewport(1000, 625, 1.25f);
    Assert(scheduler.IsFrameRequested && !scheduler.TryBegin(new WindowsViewport(0, 0, 1.25f)) && scheduler.IsFrameRequested && scheduler.ShouldWaitForEvent,
        "Zero-sized startup did not retain its frame request while returning to event-driven idle.");
    scheduler.Observe(WindowsFrameEvent.PixelSizeChanged);
    Assert(scheduler.TryBegin(normal), "Initial frame was not requested.");
    scheduler.Complete(FrameTiming.FromTimestamps(10, 20, 30, 40, 50));
    Assert(!scheduler.IsFrameRequested && !scheduler.TryBegin(normal) && scheduler.PresentedFrames == 1,
        "Idle produced an extra frame.");
    var timing = scheduler.LastTiming;
    Assert(timing.Projection > TimeSpan.Zero && timing.Raster > TimeSpan.Zero && timing.Upload > TimeSpan.Zero && timing.Present > TimeSpan.Zero,
        "Frame timings did not retain distinct phase durations.");

    scheduler.Observe(WindowsFrameEvent.Moved); // Negative virtual-desktop coordinates are intentionally not part of frame sizing.
    Assert(!scheduler.IsFrameRequested, "A virtual-desktop move requested a resize frame.");
    scheduler.Observe(WindowsFrameEvent.Minimized);
    scheduler.Observe(WindowsFrameEvent.PixelSizeChanged);
    Assert(!scheduler.IsFrameRequested && !scheduler.TryBegin(new WindowsViewport(0, 0, 1.5f)), "Minimized zero-size window presented.");
    scheduler.Observe(WindowsFrameEvent.Restored);
    scheduler.Observe(WindowsFrameEvent.DisplayScaleChanged);
    Assert(!scheduler.TryBegin(new WindowsViewport(0, 0, 1.5f)) && scheduler.IsFrameRequested && scheduler.ShouldWaitForEvent,
        "Restore/scale intent was lost while SDL temporarily reported zero backing output.");
    scheduler.Observe(WindowsFrameEvent.PixelSizeChanged);
    Assert(scheduler.TryBegin(new WindowsViewport(1200, 750, 1.5f)), "Restore/DPI/resize event reordering lost its coalesced frame.");
    scheduler.Complete(FrameTiming.FromTimestamps(100, 110, 120, 130, 140));
    Assert(scheduler.PresentedFrames == 2 && !scheduler.IsFrameRequested, "Restore emitted more than one frame.");
    scheduler.Observe(WindowsFrameEvent.Closed);
    Assert(!scheduler.IsOpen && !scheduler.IsFrameRequested, "Close left a pending frame.");

    var requested = new WindowsFrameScheduler();
    Assert(requested.TryBegin(normal), "Requested-frame scheduler did not begin initially.");
    requested.Complete(FrameTiming.FromTimestamps(200, 210, 220, 230, 240));
    requested.Request();
    Assert(requested.TryBegin(normal), "Event-caused invalidation did not request exactly one frame.");
    requested.Complete(FrameTiming.FromTimestamps(250, 260, 270, 280, 290));
    Assert(!requested.IsFrameRequested && requested.PresentedFrames == 2, "Event-caused invalidation left idle frame work behind.");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
