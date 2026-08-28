using System.Diagnostics;
using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Lucent.Platform.Windows;

/// <summary>Owns the Windows SDL frame loop: Windows DPI supplies scale, SDL supplies backing pixels, and Core stays logical.</summary>
public static class WindowsBootstrap
{
    private const int InitialLogicalWidth = 800;
    private const int InitialLogicalHeight = 500;

    [STAThread]
    public static int Run(string title, Composition composition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(composition);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
            throw new PlatformNotSupportedException("M2 requires Windows 10 version 1607 or later.");
        EnablePerMonitorV2();
        RequireNativeAssets();
        if (!SDL.Init(SDL.InitFlags.Video)) throw new InvalidOperationException($"SDL_Init: {SDL.GetError()}");

        nint window = 0;
        nint sdlRenderer = 0;
        try
        {
            window = SDL.CreateWindow(title, InitialLogicalWidth, InitialLogicalHeight, SDL.WindowFlags.Resizable | SDL.WindowFlags.HighPixelDensity);
            if (window == 0) throw new InvalidOperationException($"SDL_CreateWindow: {SDL.GetError()}");
            sdlRenderer = SDL.CreateRenderer(window, null);
            if (sdlRenderer == 0) throw new InvalidOperationException($"SDL_CreateRenderer: {SDL.GetError()}");
            if (!SDL.SetRenderVSync(sdlRenderer, WindowsPresentationContract.VsyncInterval)) throw new InvalidOperationException($"SDL_SetRenderVSync: {SDL.GetError()}");

            using var presenter = new CpuSkiaPresenter(sdlRenderer);
            using var sceneRenderer = new SkiaSceneRenderer();
            var scheduler = new WindowsFrameScheduler();
            while (scheduler.IsOpen)
            {
                if (scheduler.ShouldWaitForEvent)
                {
                    if (!SDL.WaitEvent(out var @event)) throw new InvalidOperationException($"SDL_WaitEvent: {SDL.GetError()}");
                    Observe(scheduler, (SDL.EventType)@event.Type);
                }
                while (SDL.PollEvent(out var @event)) Observe(scheduler, (SDL.EventType)@event.Type);
                if (!scheduler.IsOpen) break;

                var viewport = GetViewport(window, sdlRenderer);
                if (!scheduler.TryBegin(viewport)) continue;
                var started = Stopwatch.GetTimestamp();
                var scene = SceneLayout.Project(composition, new(viewport.LogicalWidth, viewport.LogicalHeight, viewport.Scale), sceneRenderer);
                var projected = Stopwatch.GetTimestamp();
                var phase = presenter.Present(scene, viewport, sceneRenderer);
                scheduler.Complete(FrameTiming.FromTimestamps(started, projected, phase.Rasterized, phase.Uploaded, phase.Presented));
            }
            return 0;
        }
        finally
        {
            if (sdlRenderer != 0) SDL.DestroyRenderer(sdlRenderer);
            if (window != 0) SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    private static WindowsViewport GetViewport(nint window, nint renderer)
    {
        var hwnd = SDL.GetPointerProperty(SDL.GetWindowProperties(window), SDL.Props.WindowWin32HWNDPointer, 0);
        var dpi = hwnd == 0 ? 0U : PInvoke.GetDpiForWindow(new HWND(hwnd));
        if (dpi == 0 || !SDL.GetRenderOutputSize(renderer, out var width, out var height))
            throw new InvalidOperationException($"Windows viewport: {SDL.GetError()}");
        return new(width, height, dpi / 96F);
    }

    private static void Observe(WindowsFrameScheduler scheduler, SDL.EventType @event)
    {
        switch (@event)
        {
            case SDL.EventType.Quit:
            case SDL.EventType.WindowCloseRequested: scheduler.Observe(WindowsFrameEvent.Closed); break;
            case SDL.EventType.WindowMinimized: scheduler.Observe(WindowsFrameEvent.Minimized); break;
            case SDL.EventType.WindowRestored: scheduler.Observe(WindowsFrameEvent.Restored); break;
            case SDL.EventType.WindowExposed: scheduler.Observe(WindowsFrameEvent.Exposed); break;
            case SDL.EventType.WindowResized: scheduler.Observe(WindowsFrameEvent.Resized); break;
            case SDL.EventType.WindowPixelSizeChanged: scheduler.Observe(WindowsFrameEvent.PixelSizeChanged); break;
            case SDL.EventType.WindowDisplayChanged: scheduler.Observe(WindowsFrameEvent.DisplayChanged); break;
            case SDL.EventType.WindowDisplayScaleChanged: scheduler.Observe(WindowsFrameEvent.DisplayScaleChanged); break;
        }
    }

    private static void EnablePerMonitorV2()
    {
        if (!WindowsDpi.EnsurePerMonitorV2())
            throw new InvalidOperationException("SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2) failed.");
    }

    private static void RequireNativeAssets()
    {
        foreach (var asset in new[] { "SDL3.dll", "libSkiaSharp.dll", "libHarfBuzzSharp.dll" })
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, asset)))
                throw new FileNotFoundException($"Required native asset missing: {asset}.");
    }
}
