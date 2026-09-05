using System.Diagnostics;
using System.Runtime.ExceptionServices;
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
/// before SDL shutdown, and returns only after direct close or completed session shutdown. A low-level
/// supplied composition remains caller-owned; an application session owns its composition and lifecycle.
/// </remarks>
public static class WindowsBootstrap
{
    private const int InitialLogicalWidth = 800;
    private const int InitialLogicalHeight = 500;
    private const int InstallAttempts = 3;

    /// <summary>Creates the Windows host, runs frames until the window closes, and performs ordered teardown.</summary>
    /// <param name="title">Nonblank title shown in the SDL-created top-level window and exposed as its accessible name.</param>
    /// <param name="composition">Composition to project, present, route input to, and expose through UI Automation.</param>
    /// <param name="theme">Optional theme context whose settings are initialized and refreshed from Windows.</param>
    /// <returns>Zero after the host observes a normal close or quit event.</returns>
    /// <exception cref="ArgumentException"><paramref name="title"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="composition"/> is <see langword="null"/>.</exception>
    /// <exception cref="PlatformNotSupportedException">The process is not running on the supported Windows version.</exception>
    /// <exception cref="FileNotFoundException">A NativeAOT-published SDL, Skia, HarfBuzz, or Visual C++ runtime asset is missing beside the application.</exception>
    /// <exception cref="InvalidOperationException">Windows DPI setup, SDL initialization, window creation, rendering, input projection, or native presentation fails.</exception>
    [STAThread]
    public static int Run(string title, Composition composition, ThemeContext? theme = null) =>
        RunCore(title, composition, theme, null);

    /// <summary>Runs one portable application session through startup and negotiated asynchronous shutdown.</summary>
    [STAThread]
    public static int Run(ApplicationSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return RunCore(session.Title, session.Composition, session.Theme, session);
    }

    private static int RunCore(
        string title,
        Composition composition,
        ThemeContext? theme,
        ApplicationSession? session
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(composition);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
            throw new PlatformNotSupportedException(
                "Lucent requires Windows 10 version 1607 or later."
            );
        EnablePerMonitorV2();
        RequireNativeAssets();
        if (!SDL.SetHint("SDL_IME_IMPLEMENTED_UI", "composition"))
            throw new InvalidOperationException("SDL_IME_IMPLEMENTED_UI hint was not accepted.");
        if (!SDL.Init(SDL.InitFlags.Video))
            throw new InvalidOperationException($"SDL_Init: {SDL.GetError()}");

        nint window = 0;
        nint sdlRenderer = 0;
        WindowsUiaDispatcher? uiaDispatcher = null;
        WindowsUiaProvider? uiaProvider = null;
        WindowsUiaListener? uiaListener = null;
        CpuSkiaPresenter? presenter = null;
        SkiaSceneRenderer? sceneRenderer = null;
        WindowsCursor? cursor = null;
        WindowsClipboard? clipboard = null;
        WindowsSettingsListener? settingsListener = null;
        WindowsWorkDispatcher? workDispatcher = null;
        PerformanceDiagnostics? performanceDiagnostics = null;
        WindowsInputAdapter? input = null;
        var errors = new List<Exception>();
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

            uiaDispatcher = new WindowsUiaDispatcher();
            uiaProvider = new WindowsUiaProvider(hwnd, composition, uiaDispatcher, title);
            uiaListener = new WindowsUiaListener(hwnd, uiaProvider);
            presenter = new CpuSkiaPresenter(sdlRenderer);
            sceneRenderer = new SkiaSceneRenderer();
            cursor = new WindowsCursor();
            clipboard = new WindowsClipboard();
            settingsListener = new WindowsSettingsListener(hwnd);
            workDispatcher = session is null
                ? new WindowsWorkDispatcher(composition)
                : new WindowsWorkDispatcher(session);
            using var sessionContext = session?.EnterContext();
            performanceDiagnostics = new PerformanceDiagnostics();
            var scheduler = new WindowsFrameScheduler();
            input = new WindowsInputAdapter(composition, window, clipboard);
            var settings = new WindowsSettings();
            var diagnostics = WindowsSettingsDiagnostic.None;
            _ = cursor.Activate();
            _ = ApplySettings(composition, settings, theme);
            diagnostics = ReportDiagnostics(settings, diagnostics, Console.Error.WriteLine);
            var recordedPerformanceBaseline = false;
            session?.Start();
            while (scheduler.IsOpen && session?.IsCompleted != true)
            {
                uiaDispatcher.SetOwnerPhase("events");
                var refreshSettings = false;
                if (scheduler.ShouldWaitForEvent)
                {
                    if (!SDL.WaitEvent(out var @event))
                        throw new InvalidOperationException($"SDL_WaitEvent: {SDL.GetError()}");
                    refreshSettings |= Observe(
                        scheduler,
                        input,
                        workDispatcher,
                        composition,
                        session,
                        @event
                    );
                }
                while (SDL.PollEvent(out var @event))
                    refreshSettings |= Observe(
                        scheduler,
                        input,
                        workDispatcher,
                        composition,
                        session,
                        @event
                    );

                uiaDispatcher.SetOwnerPhase("dispatch");
                if (workDispatcher.Process())
                    scheduler.Request();
                if (
                    session?.IsCompleted == true
                    || !scheduler.IsOpen
                    || (session is null && composition.IsDisposed)
                )
                    break;
                if (composition.IsDisposed)
                {
                    scheduler.Observe(WindowsFrameEvent.Minimized);
                    continue;
                }
                if (uiaDispatcher.Process() != 0)
                    scheduler.Request();
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
                performanceDiagnostics.Record(
                    scheduler.CurrentRequest.Complete(phase.Presented),
                    timing,
                    presenter,
                    sceneRenderer,
                    uiaProvider
                );
                if (!recordedPerformanceBaseline)
                {
                    performanceDiagnostics.RecordResources(
                        "pre",
                        presenter.LiveSurfaceCount,
                        presenter.LiveTextureCount,
                        sceneRenderer.LiveTextBlobCount,
                        uiaProvider.CacheCount
                    );
                    recordedPerformanceBaseline = true;
                }
            }
            uiaDispatcher.SetOwnerPhase("shutdown");
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        finally
        {
            Capture(errors, () => input?.Dispose());
            Capture(errors, () => workDispatcher?.Dispose());
            Capture(errors, () => settingsListener?.Dispose());
            Capture(errors, () => clipboard?.Dispose());
            Capture(errors, () => cursor?.Dispose());
            Capture(errors, () => uiaListener?.Dispose());
            Capture(errors, () => uiaProvider?.Dispose());
            Capture(errors, () => presenter?.Dispose());
            Capture(errors, () => sceneRenderer?.Dispose());
            if (performanceDiagnostics is not null)
                Capture(
                    errors,
                    () =>
                        performanceDiagnostics.RecordPostGcResources(
                            presenter?.LiveSurfaceCount ?? 0,
                            presenter?.LiveTextureCount ?? 0,
                            sceneRenderer?.LiveTextBlobCount ?? 0,
                            uiaProvider?.CacheCount ?? 0
                        )
                );
            Capture(errors, () => performanceDiagnostics?.Dispose());
            Capture(errors, () => uiaDispatcher?.Dispose());
            if (sdlRenderer != 0)
                Capture(errors, () => SDL.DestroyRenderer(sdlRenderer));
            if (window != 0)
                Capture(errors, () => SDL.DestroyWindow(window));
            Capture(errors, SDL.Quit);
        }

        ThrowAll(errors);
        return 0;
    }

    private static bool Observe(
        WindowsFrameScheduler scheduler,
        WindowsInputAdapter input,
        WindowsWorkDispatcher workDispatcher,
        Composition composition,
        ApplicationSession? session,
        SDL.Event @event
    )
    {
        if (workDispatcher.IsWakeEvent(@event))
            return false;
        var timestamp = Stopwatch.GetTimestamp();
        var type = (SDL.EventType)@event.Type;
        if (!composition.IsDisposed)
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
                if (session is null)
                    scheduler.Observe(WindowsFrameEvent.Closed);
                else
                    session.RequestClose();
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

    private static void Capture(List<Exception> errors, Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
    }

    private static void ThrowAll(List<Exception> errors)
    {
        if (errors.Count == 0)
            return;
        if (errors.Count == 1)
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        throw new AggregateException("Windows host run and cleanup failed.", errors);
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
