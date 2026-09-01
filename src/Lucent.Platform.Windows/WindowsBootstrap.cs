using System.Diagnostics;
using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Lucent.Platform.Windows;

/// <summary>Runs a Lucent composition in the Windows SDL host.</summary>
/// <remarks>
/// This is the Windows adapter boundary: Windows Per-Monitor V2 supplies the authoritative DPI,
/// SDL supplies the window and backing pixels, Skia paints the retained scene, and Core continues
/// to use logical coordinates. The host is intended for a Windows application entry point and
/// executes its event, input, accessibility, rendering, and disposal work on one STA owner thread.
///
/// Startup validates the supported Windows version and the SDL, Skia, HarfBuzz, and Visual C++
/// native assets needed by a NativeAOT deployment before creating the window. The method owns
/// host-side adapters and native window resources for the duration of the loop, tears them down
/// before SDL shutdown, and returns only after a close event. The supplied composition remains
/// caller-owned; Core itself has no Windows or renderer dependency.
/// </remarks>
public static class WindowsBootstrap
{
    private const int InitialLogicalWidth = 800;
    private const int InitialLogicalHeight = 500;
    private const int InstallAttempts = 3;

    /// <summary>Creates the Windows host, runs frames until the window closes, and performs ordered teardown.</summary>
    /// <param name="title">Nonblank title shown in the SDL-created top-level window.</param>
    /// <param name="composition">Composition to project, present, route input to, and expose through UI Automation.</param>
    /// <param name="theme">Optional theme context whose settings are initialized and refreshed from Windows.</param>
    /// <returns>Zero after the host observes a normal close or quit event.</returns>
    /// <exception cref="ArgumentException"><paramref name="title"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="composition"/> is <see langword="null"/>.</exception>
    /// <exception cref="PlatformNotSupportedException">The process is not running on the supported Windows version.</exception>
    /// <exception cref="FileNotFoundException">A NativeAOT-published SDL, Skia, HarfBuzz, or Visual C++ runtime asset is missing beside the application.</exception>
    /// <exception cref="InvalidOperationException">Windows DPI setup, SDL initialization, window creation, rendering, input projection, or native presentation fails.</exception>
    [STAThread]
    public static int Run(string title, Composition composition, ThemeContext? theme = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(composition);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
            throw new PlatformNotSupportedException(
                "M2 requires Windows 10 version 1607 or later."
            );
        EnablePerMonitorV2();
        RequireNativeAssets();
        if (!SDL.SetHint("SDL_IME_IMPLEMENTED_UI", "composition"))
            throw new InvalidOperationException("SDL_IME_IMPLEMENTED_UI hint was not accepted.");
        if (!SDL.Init(SDL.InitFlags.Video))
            throw new InvalidOperationException($"SDL_Init: {SDL.GetError()}");

        nint window = 0;
        nint sdlRenderer = 0;
        try
        {
            window = SDL.CreateWindow(
                title,
                InitialLogicalWidth,
                InitialLogicalHeight,
                SDL.WindowFlags.Resizable | SDL.WindowFlags.HighPixelDensity
            );
            if (window == 0)
                throw new InvalidOperationException($"SDL_CreateWindow: {SDL.GetError()}");
            sdlRenderer = SDL.CreateRenderer(window, null);
            if (sdlRenderer == 0)
                throw new InvalidOperationException($"SDL_CreateRenderer: {SDL.GetError()}");
            if (!SDL.SetRenderVSync(sdlRenderer, WindowsPresentationContract.VsyncInterval))
                throw new InvalidOperationException($"SDL_SetRenderVSync: {SDL.GetError()}");
            var hwnd = SDL.GetPointerProperty(
                SDL.GetWindowProperties(window),
                SDL.Props.WindowWin32HWNDPointer,
                0
            );
            if (hwnd == 0)
                throw new InvalidOperationException("SDL window did not expose an HWND.");

            using var uiaDispatcher = new WindowsUiaDispatcher();
            using var uiaProvider = new WindowsUiaProvider(hwnd, composition, uiaDispatcher);
            using var uiaListener = new WindowsUiaListener(hwnd, uiaProvider);
            using var presenter = new CpuSkiaPresenter(sdlRenderer);
            using var sceneRenderer = new SkiaSceneRenderer();
            using var cursor = new WindowsCursor();
            using var clipboard = new WindowsClipboard();
            using var settingsListener = new WindowsSettingsListener(hwnd);
            using var workDispatcher = new WindowsWorkDispatcher(composition);
            using var m6Diagnostics = new M6Diagnostics();
            var scheduler = new WindowsFrameScheduler();
            using var input = new WindowsInputAdapter(composition, window, clipboard);
            var settings = new WindowsSettings();
            var diagnostics = WindowsSettingsDiagnostic.None;
            _ = cursor.Activate();
            _ = ApplySettings(composition, settings, theme);
            diagnostics = ReportDiagnostics(settings, diagnostics, Console.Error.WriteLine);
            var recordedM6Baseline = false;
            while (scheduler.IsOpen)
            {
                uiaDispatcher.SetOwnerPhase("events");
                var refreshSettings = false;
                if (scheduler.ShouldWaitForEvent)
                {
                    if (!SDL.WaitEvent(out var @event))
                        throw new InvalidOperationException($"SDL_WaitEvent: {SDL.GetError()}");
                    refreshSettings |= Observe(scheduler, input, workDispatcher, @event);
                }
                while (SDL.PollEvent(out var @event))
                    refreshSettings |= Observe(scheduler, input, workDispatcher, @event);
                uiaDispatcher.SetOwnerPhase("dispatch");
                if (uiaDispatcher.Process() != 0)
                    scheduler.Request();
                if (!scheduler.IsOpen)
                    break;
                refreshSettings |= settingsListener.TakePending();
                if (refreshSettings)
                {
                    if (ApplySettings(composition, settings, theme))
                        scheduler.Request();
                    diagnostics = ReportDiagnostics(settings, diagnostics, Console.Error.WriteLine);
                }

                var viewport = GetViewport(window, sdlRenderer);
                if (!scheduler.TryBegin(viewport))
                    continue;
                uiaDispatcher.SetOwnerPhase("frame");
                uiaDispatcher.RecordFrame();
                var started = Stopwatch.GetTimestamp();
                var scene = ProjectAndInstall(
                    composition,
                    new(viewport.LogicalWidth, viewport.LogicalHeight, viewport.Scale),
                    sceneRenderer
                );
                uiaProvider.Refresh(scene);
                input.RefreshTextInput();
                var projected = Stopwatch.GetTimestamp();
                var phase = presenter.Present(scene, viewport, sceneRenderer);
                var timing = FrameTiming.FromTimestamps(
                    started,
                    projected,
                    phase.Rasterized,
                    phase.Uploaded,
                    phase.Presented
                );
                scheduler.Complete(timing);
                m6Diagnostics.Record(
                    scheduler.CurrentRequest.Complete(phase.Presented),
                    timing,
                    presenter,
                    sceneRenderer,
                    uiaProvider
                );
                if (!recordedM6Baseline)
                {
                    m6Diagnostics.RecordResources(
                        "pre",
                        presenter.LiveSurfaceCount,
                        presenter.LiveTextureCount,
                        sceneRenderer.LiveTextBlobCount,
                        uiaProvider.CacheCount
                    );
                    recordedM6Baseline = true;
                }
            }
            uiaDispatcher.SetOwnerPhase("shutdown");
            uiaListener.Dispose();
            uiaProvider.Dispose();
            presenter.Dispose();
            sceneRenderer.Dispose();
            m6Diagnostics.RecordPostGcResources(
                presenter.LiveSurfaceCount,
                presenter.LiveTextureCount,
                sceneRenderer.LiveTextBlobCount,
                uiaProvider.CacheCount
            );
            return 0;
        }
        finally
        {
            if (sdlRenderer != 0)
                SDL.DestroyRenderer(sdlRenderer);
            if (window != 0)
                SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    private static WindowsViewport GetViewport(nint window, nint renderer)
    {
        var hwnd = SDL.GetPointerProperty(
            SDL.GetWindowProperties(window),
            SDL.Props.WindowWin32HWNDPointer,
            0
        );
        var dpi = hwnd == 0 ? 0U : PInvoke.GetDpiForWindow(new HWND(hwnd));
        if (dpi == 0 || !SDL.GetRenderOutputSize(renderer, out var width, out var height))
            throw new InvalidOperationException($"Windows viewport: {SDL.GetError()}");
        return new(width, height, dpi / 96F);
    }

    private static bool Observe(
        WindowsFrameScheduler scheduler,
        WindowsInputAdapter input,
        WindowsWorkDispatcher workDispatcher,
        SDL.Event @event
    )
    {
        if (workDispatcher.IsWakeEvent(@event))
        {
            if (workDispatcher.Process())
                scheduler.Request();
            return false;
        }
        var timestamp = Stopwatch.GetTimestamp();
        var type = (SDL.EventType)@event.Type;
        try
        {
            if (input.Dispatch(@event))
                scheduler.Request(FrameOperation.Input, timestamp);
        }
        finally
        {
            if (input.ConsumeRepaintRequest())
                scheduler.Request(FrameOperation.Input, timestamp);
        }
        switch (type)
        {
            case SDL.EventType.Quit:
            case SDL.EventType.WindowCloseRequested:
                scheduler.Observe(WindowsFrameEvent.Closed);
                return false;
            case SDL.EventType.WindowMinimized:
                scheduler.Observe(WindowsFrameEvent.Minimized);
                return false;
            case SDL.EventType.WindowRestored:
                scheduler.Observe(WindowsFrameEvent.Restored);
                return false;
            case SDL.EventType.WindowExposed:
                scheduler.Observe(WindowsFrameEvent.Exposed);
                return false;
            case SDL.EventType.WindowResized:
                scheduler.Observe(WindowsFrameEvent.Resized, timestamp);
                return false;
            case SDL.EventType.WindowPixelSizeChanged:
                scheduler.Observe(WindowsFrameEvent.PixelSizeChanged, timestamp);
                return false;
            case SDL.EventType.WindowDisplayChanged:
                scheduler.Observe(WindowsFrameEvent.DisplayChanged, timestamp);
                return false;
            case SDL.EventType.WindowDisplayScaleChanged:
                scheduler.Observe(WindowsFrameEvent.DisplayScaleChanged, timestamp);
                return false;
            default:
                return false;
        }
    }

    internal static bool ApplySettings(
        Composition composition,
        WindowsSettings settings,
        ThemeContext? theme
    )
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Apply(theme))
            return false;
        composition.Flush();
        return true;
    }

    /// <summary>Projects only a scene accepted by Core input; reconciliation may require a bounded reprojection.</summary>
    internal static RetainedScene ProjectAndInstall(
        Composition composition,
        LayoutViewport viewport,
        ITextShaper shaper
    )
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(shaper);
        for (var attempt = 0; attempt < InstallAttempts; attempt++)
        {
            composition.Flush();
            var scene = SceneLayout.Project(composition, viewport, shaper);
            if (composition.Input.SetScene(scene))
                return scene;
        }
        throw new InvalidOperationException(
            $"Core input rejected {InstallAttempts} consecutive projected scenes."
        );
    }

    internal static WindowsSettingsDiagnostic ReportDiagnostics(
        WindowsSettings settings,
        WindowsSettingsDiagnostic prior,
        Action<string>? output
    )
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Diagnostics == prior)
            return prior;
        output?.Invoke("Lucent Windows settings diagnostics: " + settings.DiagnosticStatus);
        return settings.Diagnostics;
    }

    private static void EnablePerMonitorV2()
    {
        if (!WindowsDpi.EnsurePerMonitorV2())
            throw new InvalidOperationException(
                "SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2) failed."
            );
    }

    private static void RequireNativeAssets()
    {
        foreach (
            var asset in new[]
            {
                "SDL3.dll",
                "libSkiaSharp.dll",
                "libHarfBuzzSharp.dll",
                "vcruntime140.dll",
            }
        )
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, asset)))
                throw new FileNotFoundException($"Required native asset missing: {asset}.");
    }
}
