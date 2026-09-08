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
        RunCore(title, composition, theme, null, null);

    /// <summary>Runs one portable application session through startup and negotiated asynchronous shutdown.</summary>
    [STAThread]
    public static int Run(ApplicationSession session, WindowsWindowOptions? window = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        window?.Validate();
        return RunCore(session.Title, session.Composition, session.Theme, session, window);
    }

    private static int RunCore(
        string title,
        Composition composition,
        ThemeContext? theme,
        ApplicationSession? session,
        WindowsWindowOptions? windowOptions
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
        WindowsLiveResize? liveResize = null;
        WindowsPopupChain? popup = null;
        var popupInputGate = new WindowsPopupInputGate();
        ContextMenuRequest? pendingPopup = null;
        InputRouter? contextMenuRouter = null;
        Action<ContextMenuRequest>? popupRequested = null;
        var errors = new List<Exception>();
        try
        {
            window = SDL.CreateWindow(
                title,
                windowOptions?.Width ?? InitialLogicalWidth,
                windowOptions?.Height ?? InitialLogicalHeight,
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
            var caretBlink = new WindowsCaretBlink(WindowsCaretBlink.GetCaretBlinkTime());
            RetainedScene? lastScene = null;
            static long NowMilliseconds() => (long)Stopwatch.GetElapsedTime(0).TotalMilliseconds;
            input = new WindowsInputAdapter(composition, window, clipboard);
            popupRequested = request =>
            {
                pendingPopup?.Dispose();
                pendingPopup = request;
            };
            contextMenuRouter = composition.Input;
            contextMenuRouter.ContextMenuRequested += popupRequested;
            var settings = new WindowsSettings();
            var diagnostics = WindowsSettingsDiagnostic.None;
            _ = cursor.Activate();
            _ = ApplySettings(composition, settings, theme);
            diagnostics = ReportDiagnostics(settings, diagnostics, Console.Error.WriteLine);
            float configuredWindowScale = 0;
            if (windowOptions is not null)
            {
                configuredWindowScale = GetViewport(window, sdlRenderer).Scale;
                ApplyWindowDimensions(window, windowOptions, configuredWindowScale, initial: true);
            }
            var recordedPerformanceBaseline = false;
            session?.Start();
            bool ObserveHostEvent(SDL.Event @event)
            {
                if (popup?.Dispatch(@event) == true)
                {
                    if (
                        popup.IsDismissed
                        && (SDL.EventType)@event.Type == SDL.EventType.WindowFocusLost
                    )
                        popupInputGate.LostFocus();
                    input.ProcessClipboardRequests();
                    return false;
                }
                var type = (SDL.EventType)@event.Type;
                if (IsForeignWindowEvent(@event, SDL.GetWindowID(window)))
                    return false;
                if (
                    type == SDL.EventType.MouseButtonDown
                    && popup is null
                    && popupInputGate.ConsumeOwnerPointerDown(popupOpen: false)
                )
                    return false;
                if (
                    popup is not null
                    && WindowsPopupHost.EventWindowId(@event) == SDL.GetWindowID(window)
                    && type
                        is SDL.EventType.MouseButtonDown
                            or SDL.EventType.WindowResized
                            or SDL.EventType.WindowPixelSizeChanged
                            or SDL.EventType.WindowMinimized
                            or SDL.EventType.WindowCloseRequested
                )
                {
                    popup.Dismiss();
                    popup.Dispose();
                    popup = null;
                    if (
                        type == SDL.EventType.MouseButtonDown
                        && popupInputGate.ConsumeOwnerPointerDown(popupOpen: true)
                    )
                        return false;
                }
                var refresh = Observe(
                    scheduler,
                    input,
                    workDispatcher,
                    composition,
                    session,
                    @event
                );
                return refresh;
            }

            void SynchronizePopup()
            {
                popup?.ResolveFocus();
                if (popup?.IsDismissed == true)
                {
                    popup.Dispose();
                    popup = null;
                }
                if (pendingPopup is not { } request)
                    return;
                pendingPopup = null;
                popup?.Dispose();
                popup = null;
                if (!request.IsValid || request.IsDismissed)
                {
                    request.Dispose();
                    return;
                }
                if (
                    windowOptions?.MenuPresentation == WindowsMenuPresentation.PreferNative
                    && WindowsNativeMenuHost.TryShow(window, hwnd, request)
                )
                {
                    // Native tracking returns outside SDL input dispatch. Complete owner
                    // clipboard commands and refresh command/focus changes on this path too.
                    input.ProcessClipboardRequests();
                    scheduler.Request(FrameOperation.Input);
                    return;
                }
                popup = new WindowsPopupChain(
                    window,
                    hwnd,
                    request,
                    uiaDispatcher,
                    clipboard,
                    cursor
                );
                popupInputGate.Opened();
            }

            liveResize = new WindowsLiveResize(
                SDL.GetWindowID(window),
                () =>
                {
                    popup?.Dismiss();
                    if (session?.IsCompleted == true || composition.IsDisposed)
                        return;
                    _ = workDispatcher.Process();
                    if (session?.IsCompleted == true || composition.IsDisposed)
                        return;
                    _ = uiaDispatcher.Process();
                    input.ProcessClipboardRequests();
                    var liveViewport = GetViewport(window, sdlRenderer);
                    if (!liveViewport.IsRenderable)
                        return;
                    var liveScene = ProjectAndInstall(
                        composition,
                        new(
                            liveViewport.LogicalWidth,
                            liveViewport.LogicalHeight,
                            liveViewport.Scale
                        ),
                        sceneRenderer
                    );
                    lastScene = liveScene;
                    uiaProvider.Refresh(liveScene);
                    input.RefreshTextInput();
                    var hasLiveCaret = composition.Input.TryGetCaretGeometry(
                        out var liveCaretBounds
                    );
                    caretBlink.SetTarget(
                        hasLiveCaret ? composition.Input.FocusedElement : null,
                        liveCaretBounds,
                        NowMilliseconds()
                    );
                    _ = cursor.Activate(
                        input.PointerPosition is { } point
                            ? composition.Input.CursorAt(point.X, point.Y)
                            : CursorIntent.Default
                    );
                    caretBlink.BeforePresent(NowMilliseconds());
                    _ = presenter.Present(
                        liveScene,
                        liveViewport,
                        sceneRenderer,
                        caretBlink.Visible
                    );
                    caretBlink.Presented(NowMilliseconds());
                }
            );
            while (scheduler.IsOpen && session?.IsCompleted != true)
            {
                liveResize.ThrowIfFailed();
                uiaDispatcher.SetOwnerPhase("events");
                var refreshSettings = false;
                if (scheduler.ShouldWaitForEvent)
                {
                    var timeout = scheduler.IsVisible
                        ? caretBlink.WaitMilliseconds(NowMilliseconds())
                        : -1;
                    var safeIntentTimeout = popup?.SafeIntentWaitMilliseconds() ?? -1;
                    if (safeIntentTimeout >= 0 && (timeout < 0 || safeIntentTimeout < timeout))
                        timeout = safeIntentTimeout;
                    SDL.Event @event;
                    bool received;
                    liveResize.EnterPump();
                    try
                    {
                        received =
                            timeout < 0
                                ? SDL.WaitEvent(out @event)
                                : SDL.WaitEventTimeout(out @event, timeout);
                    }
                    finally
                    {
                        liveResize.ExitPump();
                    }
                    if (!received && timeout < 0)
                        throw new InvalidOperationException($"SDL_WaitEvent: {SDL.GetError()}");
                    if (received)
                        refreshSettings |= ObserveHostEvent(@event);
                }
                bool PollEvent(out SDL.Event @event)
                {
                    liveResize.EnterPump();
                    try
                    {
                        return SDL.PollEvent(out @event);
                    }
                    finally
                    {
                        liveResize.ExitPump();
                    }
                }
                while (PollEvent(out var @event))
                    refreshSettings |= ObserveHostEvent(@event);
                _ = popup?.Tick();
                liveResize.ThrowIfFailed();
                SynchronizePopup();

                uiaDispatcher.SetOwnerPhase("dispatch");
                if (workDispatcher.Process())
                {
                    scheduler.Request();
                    popup?.Refresh();
                }
                SynchronizePopup();
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
                var uiaActions = uiaDispatcher.Process();
                if (uiaActions != 0)
                {
                    input.ProcessClipboardRequests();
                    scheduler.Request();
                    popup?.Refresh();
                    SynchronizePopup();
                }
                refreshSettings |= settingsListener.TakePending();
                if (refreshSettings)
                {
                    if (
                        caretBlink.SetInterval(
                            WindowsCaretBlink.GetCaretBlinkTime(),
                            NowMilliseconds()
                        )
                    )
                        scheduler.Request();
                    if (ApplySettings(composition, settings, theme))
                        scheduler.Request();
                    diagnostics = ReportDiagnostics(settings, diagnostics, Console.Error.WriteLine);
                }

                var viewport = GetViewport(window, sdlRenderer);
                if (windowOptions is not null && configuredWindowScale != viewport.Scale)
                {
                    configuredWindowScale = viewport.Scale;
                    ApplyWindowDimensions(
                        window,
                        windowOptions,
                        configuredWindowScale,
                        initial: false
                    );
                }
                var caretOnly = scheduler.RequestCaretFrame(
                    caretBlink,
                    input.WindowFocused,
                    input.CaretActivity,
                    NowMilliseconds(),
                    lastScene is not null
                );
                if (!scheduler.TryBegin(viewport))
                    continue;
                uiaDispatcher.SetOwnerPhase("frame");
                uiaDispatcher.RecordFrame();
                var started = Stopwatch.GetTimestamp();
                var scene = caretOnly
                    ? lastScene!
                    : ProjectAndInstall(
                        composition,
                        new(viewport.LogicalWidth, viewport.LogicalHeight, viewport.Scale),
                        sceneRenderer
                    );
                if (!caretOnly)
                {
                    lastScene = scene;
                    uiaProvider.Refresh(scene);
                    input.RefreshTextInput();
                    var hasCaret = composition.Input.TryGetCaretGeometry(out var caretBounds);
                    caretBlink.SetTarget(
                        hasCaret ? composition.Input.FocusedElement : null,
                        caretBounds,
                        NowMilliseconds()
                    );
                    if (popup is null || popup.IsDismissed)
                        _ = cursor.Activate(
                            input.PointerPosition is { } point
                                ? composition.Input.CursorAt(point.X, point.Y)
                                : CursorIntent.Default
                        );
                }
                var projected = Stopwatch.GetTimestamp();
                caretBlink.BeforePresent(NowMilliseconds());
                var phase = presenter.Present(scene, viewport, sceneRenderer, caretBlink.Visible);
                caretBlink.Presented(NowMilliseconds());
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
            Capture(errors, () => liveResize?.Dispose());
            if (popupRequested is not null && contextMenuRouter is not null)
            {
                var handler = popupRequested;
                var router = contextMenuRouter;
                Capture(errors, () => router.ContextMenuRequested -= handler);
            }
            Capture(errors, () => pendingPopup?.Dispose());
            Capture(errors, () => popup?.Dispose());
            Capture(errors, () => input?.Dispose());
            Capture(errors, () => workDispatcher?.Dispose());
            Capture(errors, () => settingsListener?.Dispose());
            Capture(errors, () => clipboard?.Dispose());
            Capture(errors, () => cursor?.Dispose());
            Capture(errors, () => presenter?.Dispose());
            Capture(errors, () => sceneRenderer?.Dispose());
            if (sdlRenderer != 0)
                Capture(errors, () => SDL.DestroyRenderer(sdlRenderer));
            if (window != 0)
                Capture(errors, () => SDL.DestroyWindow(window));
            Capture(errors, () => uiaListener?.Dispose());
            Capture(errors, () => uiaProvider?.Dispose());
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
            Capture(errors, SDL.Quit);
        }

        ThrowAll(errors);
        return 0;
    }

    internal static bool IsForeignWindowEvent(SDL.Event @event, uint ownerWindowId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerWindowId);
        var eventWindowId = WindowsPopupHost.EventWindowId(@event);
        return eventWindowId != 0 && eventWindowId != ownerWindowId;
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

    internal static void ApplyWindowDimensions(
        nint window,
        WindowsWindowOptions options,
        float dpiScale,
        bool initial
    )
    {
        options.Validate();
        var density = SDL.GetWindowPixelDensity(window);
        int Units(int value) => WindowsWindowOptions.ToWindowUnits(value, dpiScale, density);
        if (
            !SDL.SetWindowMinimumSize(
                window,
                Units(options.MinimumWidth),
                Units(options.MinimumHeight)
            )
        )
            throw new InvalidOperationException($"SDL_SetWindowMinimumSize: {SDL.GetError()}");
        if (
            initial
            && (
                !SDL.SetWindowSize(window, Units(options.Width), Units(options.Height))
                || !SDL.SyncWindow(window)
            )
        )
            throw new InvalidOperationException($"SDL_SetWindowSize: {SDL.GetError()}");
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
