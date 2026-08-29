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
    private const int InstallAttempts = 3;

    [STAThread]
    public static int Run(string title, Composition composition, ThemeContext? theme = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(composition);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
            throw new PlatformNotSupportedException("M2 requires Windows 10 version 1607 or later.");
        EnablePerMonitorV2();
        RequireNativeAssets();
        if (!SDL.SetHint("SDL_IME_IMPLEMENTED_UI", "composition"))
            throw new InvalidOperationException("SDL_IME_IMPLEMENTED_UI hint was not accepted.");
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
            var hwnd = SDL.GetPointerProperty(SDL.GetWindowProperties(window), SDL.Props.WindowWin32HWNDPointer, 0);
            if (hwnd == 0) throw new InvalidOperationException("SDL window did not expose an HWND.");

            using var presenter = new CpuSkiaPresenter(sdlRenderer);
            using var sceneRenderer = new SkiaSceneRenderer();
            using var cursor = new WindowsCursor();
            using var clipboard = new WindowsClipboard();
            using var settingsListener = new WindowsSettingsListener(hwnd);
            var scheduler = new WindowsFrameScheduler();
            using var input = new WindowsInputAdapter(composition, window, clipboard);
            var settings = new WindowsSettings();
            var diagnostics = WindowsSettingsDiagnostic.None;
            _ = cursor.Activate();
            _ = ApplySettings(composition, settings, theme);
            diagnostics = ReportDiagnostics(settings, diagnostics, Console.Error.WriteLine);
            while (scheduler.IsOpen)
            {
                var refreshSettings = false;
                if (scheduler.ShouldWaitForEvent)
                {
                    if (!SDL.WaitEvent(out var @event)) throw new InvalidOperationException($"SDL_WaitEvent: {SDL.GetError()}");
                    refreshSettings |= Observe(scheduler, input, @event);
                }
                while (SDL.PollEvent(out var @event)) refreshSettings |= Observe(scheduler, input, @event);
                if (!scheduler.IsOpen) break;
                refreshSettings |= settingsListener.TakePending();
                if (refreshSettings)
                {
                    if (ApplySettings(composition, settings, theme)) scheduler.Request();
                    diagnostics = ReportDiagnostics(settings, diagnostics, Console.Error.WriteLine);
                }

                var viewport = GetViewport(window, sdlRenderer);
                if (!scheduler.TryBegin(viewport)) continue;
                var started = Stopwatch.GetTimestamp();
                var scene = ProjectAndInstall(composition, new(viewport.LogicalWidth, viewport.LogicalHeight, viewport.Scale), sceneRenderer);
                input.RefreshTextInput();
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

    private static bool Observe(WindowsFrameScheduler scheduler, WindowsInputAdapter input, SDL.Event @event)
    {
        var type = (SDL.EventType)@event.Type;
        try { if (input.Dispatch(@event)) scheduler.Request(); }
        finally { if (input.ConsumeRepaintRequest()) scheduler.Request(); }
        switch (type)
        {
            case SDL.EventType.Quit:
            case SDL.EventType.WindowCloseRequested: scheduler.Observe(WindowsFrameEvent.Closed); return false;
            case SDL.EventType.WindowMinimized: scheduler.Observe(WindowsFrameEvent.Minimized); return false;
            case SDL.EventType.WindowRestored: scheduler.Observe(WindowsFrameEvent.Restored); return false;
            case SDL.EventType.WindowExposed: scheduler.Observe(WindowsFrameEvent.Exposed); return false;
            case SDL.EventType.WindowResized: scheduler.Observe(WindowsFrameEvent.Resized); return false;
            case SDL.EventType.WindowPixelSizeChanged: scheduler.Observe(WindowsFrameEvent.PixelSizeChanged); return false;
            case SDL.EventType.WindowDisplayChanged: scheduler.Observe(WindowsFrameEvent.DisplayChanged); return false;
            case SDL.EventType.WindowDisplayScaleChanged: scheduler.Observe(WindowsFrameEvent.DisplayScaleChanged); return false;
            default: return false;
        }
    }

    internal static bool ApplySettings(Composition composition, WindowsSettings settings, ThemeContext? theme)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Apply(theme)) return false;
        composition.Flush();
        return true;
    }

    /// <summary>Projects only a scene accepted by Core input; reconciliation may require a bounded reprojection.</summary>
    internal static RetainedScene ProjectAndInstall(Composition composition, LayoutViewport viewport, ITextShaper shaper)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(shaper);
        for (var attempt = 0; attempt < InstallAttempts; attempt++)
        {
            composition.Flush();
            var scene = SceneLayout.Project(composition, viewport, shaper);
            if (composition.Input.SetScene(scene)) return scene;
        }
        throw new InvalidOperationException($"Core input rejected {InstallAttempts} consecutive projected scenes.");
    }

    internal static WindowsSettingsDiagnostic ReportDiagnostics(WindowsSettings settings, WindowsSettingsDiagnostic prior, Action<string>? output)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Diagnostics == prior) return prior;
        output?.Invoke("Lucent Windows settings diagnostics: " + settings.DiagnosticStatus);
        return settings.Diagnostics;
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
