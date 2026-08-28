using System.Diagnostics;

namespace Lucent.Platform.Windows;

/// <summary>Windows owns physical pixels; Core receives device-independent logical coordinates and their one raster scale.</summary>
internal readonly record struct WindowsViewport
{
    public WindowsViewport(int backingWidth, int backingHeight, float scale)
    {
        if (backingWidth < 0 || backingHeight < 0 || !float.IsFinite(scale) || scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale), "Backing dimensions must be non-negative and scale must be finite and positive.");
        BackingWidth = backingWidth;
        BackingHeight = backingHeight;
        Scale = scale;
    }

    public int BackingWidth { get; }
    public int BackingHeight { get; }
    public float Scale { get; }

    public float LogicalWidth => BackingWidth / Scale;
    public float LogicalHeight => BackingHeight / Scale;
    public CpuSurfaceFormat Format => WindowsPresentationContract.SurfaceFormat;
    public bool IsRenderable => BackingWidth > 0 && BackingHeight > 0;

}

internal enum CpuColorFormat { Rgba8888, Bgra8888 }
internal enum CpuAlphaFormat { Premultiplied, Opaque }
internal readonly record struct CpuSurfaceFormat(CpuColorFormat Color, CpuAlphaFormat Alpha);

/// <summary>The sole M2 pixel and pacing policy: premultiplied RGBA CPU pixels upload as SDL ABGR bytes with vsync enabled.</summary>
internal static class WindowsPresentationContract
{
    public static readonly CpuSurfaceFormat SurfaceFormat = new(CpuColorFormat.Rgba8888, CpuAlphaFormat.Premultiplied);
    public const int VsyncInterval = 1;
}

internal readonly record struct CpuResourceDescriptor(int Width, int Height, CpuSurfaceFormat Format)
{
    public CpuResourceDescriptor(WindowsViewport viewport) : this(viewport.BackingWidth, viewport.BackingHeight, viewport.Format) { }
}

/// <summary>Tracks the one CPU surface and streaming texture pair. A scale change alone repaints into the existing backing allocation.</summary>
internal sealed class CpuResourceState
{
    private CpuResourceDescriptor? _current;

    public int CreationCount { get; private set; }
    public CpuResourceDescriptor? Current => _current;
    public bool NeedsRecreation(CpuResourceDescriptor descriptor) => _current != descriptor;
    public void Commit(CpuResourceDescriptor descriptor) { _current = descriptor; CreationCount++; }
    public void Release() => _current = null;
}

internal enum WindowsFrameEvent
{
    Moved,
    Exposed,
    Resized,
    PixelSizeChanged,
    DisplayChanged,
    DisplayScaleChanged,
    Minimized,
    Restored,
    Closed
}

/// <summary>Coalesces SDL window notifications into at most one frame; idle has no timer and therefore no frames.</summary>
internal sealed class WindowsFrameScheduler
{
    private bool _requested = true;
    private bool _minimized;
    private bool _awaitingRenderable;

    public bool IsOpen { get; private set; } = true;
    public bool IsFrameRequested => _requested;
    public bool ShouldWaitForEvent => !_requested || _awaitingRenderable;
    public int PresentedFrames { get; private set; }
    public FrameTiming LastTiming { get; private set; }

    /// <summary>Requests one event-caused frame without adding a timer or idle work.</summary>
    public void Request()
    {
        if (!IsOpen || _minimized) return;
        _requested = true;
        _awaitingRenderable = false;
    }

    public void Observe(WindowsFrameEvent @event)
    {
        switch (@event)
        {
            case WindowsFrameEvent.Closed: IsOpen = false; _requested = false; _awaitingRenderable = false; break;
            case WindowsFrameEvent.Minimized: _minimized = true; _requested = false; _awaitingRenderable = false; break;
            case WindowsFrameEvent.Restored: _minimized = false; _requested = true; _awaitingRenderable = false; break;
            case WindowsFrameEvent.Moved: break;
            default: if (!_minimized) { _requested = true; _awaitingRenderable = false; } break;
        }
    }

    public bool TryBegin(WindowsViewport viewport)
    {
        if (!_requested || _minimized) return false;
        if (!viewport.IsRenderable) { _awaitingRenderable = true; return false; }
        _requested = false;
        _awaitingRenderable = false;
        return true;
    }

    public void Complete(FrameTiming timing) { LastTiming = timing; PresentedFrames++; }
}

/// <summary>Measured in phase order so projection, CPU raster, upload, and present cannot be collapsed into one opaque duration.</summary>
internal readonly record struct FrameTiming(TimeSpan Projection, TimeSpan Raster, TimeSpan Upload, TimeSpan Present)
{
    public static FrameTiming FromTimestamps(long started, long projected, long rasterized, long uploaded, long presented)
    {
        if (started > projected || projected > rasterized || rasterized > uploaded || uploaded > presented)
            throw new ArgumentOutOfRangeException(nameof(presented), "Frame phase timestamps must be ordered.");
        return new(Stopwatch.GetElapsedTime(started, projected), Stopwatch.GetElapsedTime(projected, rasterized), Stopwatch.GetElapsedTime(rasterized, uploaded), Stopwatch.GetElapsedTime(uploaded, presented));
    }
}
