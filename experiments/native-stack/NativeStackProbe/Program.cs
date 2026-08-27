using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SDL3;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Windows.Win32.Foundation;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var json = args.FirstOrDefault() switch
            {
                "--automated" => JsonSerializer.Serialize(RunAutomated(), ProbeJsonContext.Default.AutomatedResult),
                "--manual" => JsonSerializer.Serialize(RunManual(args.Skip(1).FirstOrDefault() ?? "native-stack-ime.jsonl"), ProbeJsonContext.Default.ManualResult),
                "--text-self-check" => JsonSerializer.Serialize(TextState.SelfCheck(), ProbeJsonContext.Default.TextCheckResult),
                "--reactive-self-check" => JsonSerializer.Serialize(ReactiveProbe.Run(), ProbeJsonContext.Default.ReactiveCheckResult),
                "--style-self-check" => JsonSerializer.Serialize(StyleProbe.Run(), ProbeJsonContext.Default.StyleCheckResult),
                "--structural-self-check" => JsonSerializer.Serialize(StructuralProbe.Run(), ProbeJsonContext.Default.StructuralCheckResult),
                "--controls-self-check" => JsonSerializer.Serialize(ControlsProbe.Run(), ProbeJsonContext.Default.ControlsCheckResult),
                "--virtualization-self-check" => JsonSerializer.Serialize(VirtualizationProbe.Run(), ProbeJsonContext.Default.VirtualizationCheckResult),
                "--scene-self-check" when args.Length == 2 => JsonSerializer.Serialize(SceneProbe.Run(args[1]), ProbeJsonContext.Default.SceneCheckResult),
                "--layout-self-check" when args.Length == 2 => JsonSerializer.Serialize(LayoutProbe.Run(args[1]), ProbeJsonContext.Default.LayoutCheckResult),
                "--uia-host" when args.Length == 3 => JsonSerializer.Serialize(RunUiaHost(args[1], args[2], manual: false), ProbeJsonContext.Default.UiaHostResult),
                "--uia-manual" when args.Length == 3 => JsonSerializer.Serialize(RunUiaHost(args[1], args[2], manual: true), ProbeJsonContext.Default.UiaHostResult),
                "--uia-ccw-self-check" when args.Length is 1 or 2 => JsonSerializer.Serialize(RunUiaCcwSelfCheck(args.Skip(1).FirstOrDefault()), ProbeJsonContext.Default.CcwSelfCheckResult),
                "--issue-browser-walkthrough" => IssueBrowser.RunWalkthrough(args.Skip(1).FirstOrDefault()),
                "--issue-browser" => IssueBrowser.RunVisible(),
                null => JsonSerializer.Serialize(RunDependencyProbe(), ProbeJsonContext.Default.ProbeResult),
                _ => throw new ArgumentException("Use --automated, --manual [jsonl-path], --text-self-check, --reactive-self-check, --style-self-check, --structural-self-check, --controls-self-check, --virtualization-self-check, --scene-self-check|--layout-self-check <artifact-directory>, --uia-host|--uia-manual <ready-json> <close-signal>, or --uia-ccw-self-check [wrappers|create|qi|options|disconnect|release].")
            };
            Console.WriteLine(json);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new FailureResult(false, exception.Message), ProbeJsonContext.Default.FailureResult));
            return 1;
        }
    }

    private static ProbeResult RunDependencyProbe()
    {
        using var host = new WindowHost("NativeStackProbe", 1, 1, SDL.WindowFlags.Hidden);
        var png = RasterProbe();
        var shaping = ShapeProbe();
        var fallback = SKFontManager.Default.MatchCharacter('漢')
            ?? throw new InvalidOperationException("Skia font fallback lookup returned null.");
        return new(true, new(SDL.GetVersion().ToString(), host.HwndText, host.Dpi), new(png, fallback.FamilyName), shaping, LoadedModules());
    }

    private static AutomatedResult RunAutomated()
    {
        TextState.SelfCheck();
        using var host = new WindowHost("NativeStackProbe automated", 160, 100, SDL.WindowFlags.Resizable);
        using var subclass = new WindowSubclass(host.Hwnd);
        var installed = subclass.Install();
        var installedAgain = subclass.Install();
        if (!installed || !installedAgain)
            throw new InvalidOperationException("SetWindowSubclass failed.");

        if (!SDL.ShowWindow(host.Window) || !SDL.RaiseWindow(host.Window) ||
            !SDL.SetWindowSize(host.Window, 320, 240) || !SDL.SyncWindow(host.Window))
            throw new InvalidOperationException($"SDL window operation: {SDL.GetError()}");

        var events = Pump(500);
        var sizeRead = SDL.GetWindowSize(host.Window, out var width, out var height);
        var hwndAgain = host.ReadHwnd();
        var dpiAgain = host.ReadDpi();
        var scale = SDL.GetWindowDisplayScale(host.Window);
        var callbacksBefore = subclass.MessageCount;
        Native.SendMessage(host.Hwnd, Native.ProbeMessage, 0, 0);
        var callbackCount = subclass.MessageCount - callbacksBefore;
        var removed = subclass.Remove();
        callbacksBefore = subclass.MessageCount;
        Native.SendMessage(host.Hwnd, Native.ProbeMessage, 0, 0);
        var callbacksAfterRemoval = subclass.MessageCount - callbacksBefore;

        if (!SDL.HideWindow(host.Window))
            throw new InvalidOperationException($"SDL_HideWindow: {SDL.GetError()}");
        events |= Pump(500);
        Native.SendMessage(host.Hwnd, Native.WmClose, 0, 0);
        events |= Pump(500);
        host.Destroy();
        events |= Pump(200);
        var result = new AutomatedResult(true, host.HwndText, host.Hwnd != IntPtr.Zero && hwndAgain == host.Hwnd,
            host.Dpi, dpiAgain, host.Dpi == dpiAgain, scale, sizeRead && width == 320 && height == 240,
            events.Shown, events.Resized, events.FocusGained, events.FocusLost, events.DisplayScaleChanged, events.CloseRequested,
            installedAgain, callbackCount, removed, callbacksAfterRemoval, true, events.Destroyed);
        if (!result.CriticalClaimsPass)
            throw new InvalidOperationException($"Automated proof failed: {JsonSerializer.Serialize(result, ProbeJsonContext.Default.AutomatedResult)}");
        return result;
    }

    private static ManualResult RunManual(string logPath)
    {
        if (!SDL.SetHint("SDL_IME_IMPLEMENTED_UI", "composition"))
            throw new InvalidOperationException("SDL_IME_IMPLEMENTED_UI hint was not accepted.");
        using var host = new WindowHost("NativeStackProbe IME - type here; close when done", 640, 180, SDL.WindowFlags.Resizable);
        using var subclass = new WindowSubclass(host.Hwnd);
        if (!subclass.Install() || !SDL.ShowWindow(host.Window) || !SDL.RaiseWindow(host.Window))
            throw new InvalidOperationException($"Manual host setup: {SDL.GetError()}");

        var area = new SDL.Rect { X = 24, Y = 72, W = 592, H = 32 };
        Pump(100);
        var state = new TextState();
        using var renderer = new ManualImeRenderer(host.Window);
        using var log = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };
        var eventCount = 0;
        void WriteLog(string kind, string? text)
        {
            log.WriteLine(JsonSerializer.Serialize(new TextLog(kind, text, state.Committed, state.Preedit, state.Start, state.Length), ProbeJsonContext.Default.TextLog));
            eventCount++;
        }
        void Redraw()
        {
            var cursor = renderer.Draw(state.Committed, state.Preedit, state.Start);
            if (!SDL.SetTextInputArea(host.Window, in area, cursor))
                throw new InvalidOperationException($"SDL_SetTextInputArea: {SDL.GetError()}");
        }
        StartTextInput(host.Window, in area, 0);
        Redraw();
        WriteLog("started", null);
        var running = true;
        var closeLogged = false;
        while (running)
        {
            while (SDL.PollEvent(out var @event))
            {
                switch ((SDL.EventType)@event.Type)
                {
                    case SDL.EventType.TextEditing:
                        var preedit = Marshal.PtrToStringUTF8(@event.Edit.Text) ?? "";
                        state.SetPreedit(preedit, @event.Edit.Start, @event.Edit.Length);
                        Redraw();
                        WriteLog("editing", preedit);
                        break;
                    case SDL.EventType.TextInput:
                        var committed = Marshal.PtrToStringUTF8(@event.Text.Text) ?? "";
                        state.Commit(committed);
                        Redraw();
                        WriteLog("input", committed);
                        break;
                    case SDL.EventType.WindowFocusLost:
                        state.Cancel();
                        if (!SDL.StopTextInput(host.Window))
                            throw new InvalidOperationException($"SDL_StopTextInput: {SDL.GetError()}");
                        Redraw();
                        WriteLog("focus-lost", null);
                        break;
                    case SDL.EventType.WindowFocusGained:
                        StartTextInput(host.Window, in area, renderer.Draw(state.Committed, state.Preedit, state.Start));
                        WriteLog("focus-gained", null);
                        break;
                    case SDL.EventType.WindowCloseRequested:
                    case SDL.EventType.Quit:
                        if (!closeLogged) { WriteLog("close", null); closeLogged = true; }
                        running = false;
                        break;
                }
            }
            SDL.Delay(10);
        }
        SDL.StopTextInput(host.Window);
        return new(true, Path.GetFullPath(logPath), eventCount, state.Committed);
    }

    private static void StartTextInput(IntPtr window, in SDL.Rect area, int cursor)
    {
        if (!SDL.StartTextInput(window) || !SDL.SetTextInputArea(window, in area, cursor))
            throw new InvalidOperationException($"SDL text input setup: {SDL.GetError()}");
    }

    private static EventFlags Pump(int milliseconds)
    {
        var flags = new EventFlags();
        var until = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < until)
        {
            while (SDL.PollEvent(out var @event))
                flags.Observe((SDL.EventType)@event.Type);
            SDL.Delay(10);
        }
        return flags;
    }


    private static UiaHostResult RunUiaHost(string readyPath, string closePath, bool manual)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(readyPath))!);
        File.Delete(readyPath);
        File.Delete(closePath);
        using var host = new WindowHost(manual ? "Lucent Native Accessibility proof" : "NativeStackProbe UIA", 240, 120,
            manual ? SDL.WindowFlags.Resizable : SDL.WindowFlags.Hidden);
        var provider = host.CreateProvider();
        using var subclass = new WindowSubclass(host.Hwnd, provider);
        if (!subclass.Install()) throw new InvalidOperationException("SetWindowSubclass failed for UIA host.");

        var invalidFirst = Native.SendMessage(host.Hwnd, Native.WmGetObject, 0, Native.UiaRootObjectId + 1);
        var invalidSecond = Native.SendMessage(host.Hwnd, Native.WmGetObject, 0, -1);
        var invalidChained = invalidFirst == 0 && invalidSecond == 0;
        if (!invalidChained || subclass.InvalidUiaDeliveryCount != 2)
            throw new InvalidOperationException($"Invalid WM_GETOBJECT self-check failed: invalid={invalidFirst:X}/{invalidSecond:X}; deliveries={subclass.InvalidUiaDeliveryCount}.");

        if (manual && (!SDL.ShowWindow(host.Window) || !SDL.RaiseWindow(host.Window)))
            throw new InvalidOperationException($"Manual UIA host setup: {SDL.GetError()}");
        File.WriteAllText(readyPath, JsonSerializer.Serialize(new UiaReadyResult(true, host.HwndText, RuntimeFeature.IsDynamicCodeSupported), ProbeJsonContext.Default.UiaReadyResult));
        var until = Environment.TickCount64 + (manual ? 600_000 : 30_000);
        var windowClosed = false;
        while (!File.Exists(closePath) && !windowClosed && Environment.TickCount64 < until)
        {
            windowClosed = Pump(20).CloseRequested;
        }
        if (!File.Exists(closePath) && !windowClosed)
            throw new TimeoutException(manual ? "Manual UIA review did not close within 10 minutes." : "UIA helper did not signal close within 30 seconds.");

        var removed = subclass.Remove();
        provider.Disconnect();
        host.Destroy();
        provider.Dispose();
        var result = new UiaHostResult(true, host.HwndText, !RuntimeFeature.IsDynamicCodeSupported,
            invalidChained, subclass.InvalidUiaDeliveryCount, subclass.RootUiaDeliveryCount, subclass.UiaDeliveryCount,
            provider.CallCount, provider.OptionsCallCount, provider.PropertyCallCount, provider.HostCallCount, provider.InterfaceCreated, provider.Disconnected, provider.DisconnectHResult,
            removed, provider.Released, host.Destroyed);
        if (!result.CriticalClaimsPass)
            throw new InvalidOperationException($"UIA host proof failed: {JsonSerializer.Serialize(result, ProbeJsonContext.Default.UiaHostResult)}");
        return result;
    }

    private static unsafe CcwSelfCheckResult RunUiaCcwSelfCheck(string? requestedPhase)
    {
        var phase = requestedPhase?.ToLowerInvariant() switch
        {
            null => CcwPhase.Release,
            "wrappers" => CcwPhase.Wrappers,
            "create" => CcwPhase.Create,
            "qi" => CcwPhase.QueryInterface,
            "options" => CcwPhase.Options,
            "disconnect" => CcwPhase.Disconnect,
            "release" => CcwPhase.Release,
            _ => throw new ArgumentException("Use wrappers, create, qi, options, disconnect, or release.")
        };
        void Mark(string marker)
        {
            Console.WriteLine(JsonSerializer.Serialize(new CcwMarker(marker), ProbeJsonContext.Default.CcwMarker));
            Console.Out.Flush();
        }

        Mark("before-provider");
        using var provider = new UiaRootProvider(0, new("CCW self-check", "CCW.SelfCheck", Native.UiaControlTypeWindow), phase, Mark);
        Mark("after-provider");
        var options = 0;
        if (phase >= CcwPhase.Options)
        {
            Mark("before-options-vtable");
            options = ((delegate* unmanaged[MemberFunction]<nint, int*, int>)(*(nint**)provider.InterfacePointer)[3])(provider.InterfacePointer, &options);
            Mark("after-options-vtable");
        }
        if (phase >= CcwPhase.Disconnect)
        {
            Mark("before-disconnect");
            provider.Disconnect();
            Mark("after-disconnect");
        }
        if (phase >= CcwPhase.Release)
        {
            Mark("before-release");
            provider.Dispose();
            Mark("after-release");
        }
        return new(true, phase.ToString(), options, provider.InterfaceCreated, provider.Disconnected, provider.DisconnectHResult, provider.Released);
    }

    private static int RasterProbe()
    {
        using var surface = SKSurface.Create(new SKImageInfo(8, 8));
        surface.Canvas.Clear(SKColors.CornflowerBlue);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data is null || data.Size == 0 ? throw new InvalidOperationException("Skia PNG encoding failed.") : checked((int)data.Size);
    }

    private static ShapingResult ShapeProbe()
    {
        using var typeface = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
        using var font = new SKFont(typeface, 16);
        using var shaper = new SKShaper(typeface);
        var latin = shaper.Shape("office", font);
        var arabic = shaper.Shape("العَرَبِيَّة", font);
        if (latin.Codepoints.Length == 0 || latin.Width <= 0 || arabic.Codepoints.Length == 0 || arabic.Width <= 0)
            throw new InvalidOperationException("HarfBuzz shaping failed.");
        return new(latin.Codepoints.Length, latin.Width, arabic.Codepoints.Length, arabic.Width);
    }

    private static NativeModule[] LoadedModules() => Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
        .Where(module => module.FileName is not null && (module.FileName.Contains("SDL", StringComparison.OrdinalIgnoreCase) || module.FileName.Contains("Skia", StringComparison.OrdinalIgnoreCase) || module.FileName.Contains("HarfBuzz", StringComparison.OrdinalIgnoreCase)))
        .Select(module => new NativeModule(module.ModuleName, module.FileVersionInfo.FileVersion, module.FileName)).ToArray();
}

internal sealed class ManualImeRenderer : IDisposable
{
    private const int Width = 640, Height = 180;
    private readonly nint _renderer;
    private readonly nint _texture;
    private readonly SKBitmap _bitmap = new(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
    private readonly SKTypeface _typeface = SKTypeface.FromFamilyName("Yu Gothic UI") ?? SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;

    public ManualImeRenderer(nint window)
    {
        _renderer = SDL.CreateRenderer(window, null);
        if (_renderer == 0) throw new InvalidOperationException($"SDL_CreateRenderer: {SDL.GetError()}");
        _texture = SDL.CreateTexture(_renderer, SDL.PixelFormat.ABGR8888, SDL.TextureAccess.Streaming, Width, Height);
        if (_texture == 0) throw new InvalidOperationException($"SDL_CreateTexture: {SDL.GetError()}");
    }

    public int Draw(string committed, string preedit, int preeditStart)
    {
        using var canvas = new SKCanvas(_bitmap);
        canvas.Clear(new SKColor(24, 24, 27));
        using var instruction = new SKFont(_typeface, 14);
        using var text = new SKFont(_typeface, 24);
        using var muted = new SKPaint { Color = new SKColor(161, 161, 170), IsAntialias = true };
        using var normal = new SKPaint { Color = new SKColor(244, 244, 245), IsAntialias = true };
        using var accent = new SKPaint { Color = new SKColor(96, 165, 250), IsAntialias = true, StrokeWidth = 2 };
        canvas.DrawText("Japanese IME: select Japanese あ mode, type, then close", 24, 38, SKTextAlign.Left, instruction, muted);
        const float x = 24, baseline = 108;
        canvas.DrawText(committed, x, baseline, SKTextAlign.Left, text, normal);
        var committedWidth = text.MeasureText(committed);
        canvas.DrawText(preedit, x + committedWidth, baseline, SKTextAlign.Left, text, accent);
        var preeditWidth = text.MeasureText(preedit);
        if (preeditWidth > 0) canvas.DrawLine(x + committedWidth, baseline + 5, x + committedWidth + preeditWidth, baseline + 5, accent);
        var caretX = x + committedWidth + text.MeasureText(TextState.RunePrefix(preedit, preeditStart));
        canvas.DrawLine(caretX, baseline - 26, caretX, baseline + 6, normal);
        // ponytail: one fixed short line; add scrolling/selection layout only when editing needs it.
        if (!SDL.UpdateTexture(_texture, IntPtr.Zero, _bitmap.GetPixels(), _bitmap.RowBytes) || !SDL.RenderTexture(_renderer, _texture, IntPtr.Zero, IntPtr.Zero))
            throw new InvalidOperationException($"SDL manual render upload: {SDL.GetError()}");
        SDL.RenderPresent(_renderer);
        return Math.Clamp((int)MathF.Round(caretX - x), 0, 592);
    }

    public void Dispose()
    {
        SDL.DestroyTexture(_texture);
        SDL.DestroyRenderer(_renderer);
        _bitmap.Dispose();
        if (!ReferenceEquals(_typeface, SKTypeface.Default)) _typeface.Dispose();
    }
}

internal sealed class WindowHost : IDisposable
{
    private bool _destroyed;
    private UiaRootProvider? _provider;
    public WindowHost(string title, int width, int height, SDL.WindowFlags flags)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
            throw new PlatformNotSupportedException("GetDpiForWindow requires Windows 10 version 1607 or newer.");
        if (!SDL.Init(SDL.InitFlags.Video)) throw new InvalidOperationException($"SDL_Init: {SDL.GetError()}");
        Window = SDL.CreateWindow(title, width, height, flags);
        if (Window == IntPtr.Zero) throw new InvalidOperationException($"SDL_CreateWindow: {SDL.GetError()}");
        Hwnd = ReadHwnd();
        if (Hwnd == IntPtr.Zero) throw new InvalidOperationException("SDL did not expose a Win32 HWND.");
        Dpi = ReadDpi();
        if (Dpi == 0) throw new InvalidOperationException("GetDpiForWindow returned zero.");
    }
    public IntPtr Window { get; private set; }
    public IntPtr Hwnd { get; }
    public uint Dpi { get; }
    public UiaRootProvider CreateProvider() => _provider ??= new(Hwnd, new("NativeStackProbe UIA root", "NativeStackProbe.Root", Native.UiaControlTypeWindow));
    public bool Destroyed => _destroyed;
    public string HwndText => $"0x{Hwnd.ToInt64():X}";
    public IntPtr ReadHwnd() => SDL.GetPointerProperty(SDL.GetWindowProperties(Window), SDL.Props.WindowWin32HWNDPointer, IntPtr.Zero);
#pragma warning disable CA1416 // The probe explicitly rejects pre-1607 Windows before creating Host.
    public uint ReadDpi() => Windows.Win32.PInvoke.GetDpiForWindow(new HWND(Hwnd));
#pragma warning restore CA1416
    public void Destroy() { if (!_destroyed) { SDL.DestroyWindow(Window); _destroyed = true; } }
    public void Dispose() { Destroy(); _provider?.Dispose(); SDL.Quit(); }
}

internal sealed unsafe class WindowSubclass : IDisposable
{
    private const nuint Id = 3;
    private readonly GCHandle _self;
    private readonly IntPtr _hwnd;
    private readonly UiaRootProvider? _provider;
    private bool _installed;
    public WindowSubclass(IntPtr hwnd, UiaRootProvider? provider = null) { _hwnd = hwnd; _provider = provider; _self = GCHandle.Alloc(this); }
    public int MessageCount { get; private set; }
    public int UiaDeliveryCount { get; private set; }
    public int RootUiaDeliveryCount { get; private set; }
    public int InvalidUiaDeliveryCount { get; private set; }
    public bool Install()
    {
        var result = Native.SetWindowSubclass(_hwnd, &Callback, Id, (nuint)GCHandle.ToIntPtr(_self));
        _installed |= result != 0;
        return result != 0;
    }
    public bool Remove()
    {
        var result = !_installed || Native.RemoveWindowSubclass(_hwnd, &Callback, Id) != 0;
        _installed = !result;
        return result;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint Callback(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (GCHandle.FromIntPtr((IntPtr)data).Target is WindowSubclass owner)
        {
            if (message == Native.ProbeMessage) owner.MessageCount++;
            if (message == Native.WmGetObject)
            {
                owner.UiaDeliveryCount++;
                if (lParam == Native.UiaRootObjectId && owner._provider is not null)
                {
                    owner.RootUiaDeliveryCount++;
                    Console.Error.WriteLine("{\"Marker\":\"uia-root-before-return\"}");
                    Console.Error.Flush();
                    var result = Native.UiaReturnRawElementProvider(hwnd, wParam, lParam, owner._provider.InterfacePointer);
                    Console.Error.WriteLine("{\"Marker\":\"uia-root-after-return\"}");
                    Console.Error.Flush();
                    return result;
                }
                owner.InvalidUiaDeliveryCount++;
            }
        }
        return Native.DefSubclassProc(hwnd, message, wParam, lParam);
    }
    public void Dispose()
    {
        if (!Remove()) return; // ponytail: leak callback refdata until process exit if unhooking fails; retrying risks a dangling callback.
        if (_self.IsAllocated) _self.Free();
    }
}

internal sealed class TextState
{
    public string Committed { get; private set; } = "";
    public string Preedit { get; private set; } = "";
    public int Start { get; private set; }
    public int Length { get; private set; }
    public void SetPreedit(string text, int start, int length)
    {
        Preedit = Normalize(text);
        if (Preedit.Length == 0)
        {
            Start = start < 0 ? -1 : 0;
            Length = length < 0 ? -1 : 0;
            return;
        }
        var runeCount = Preedit.EnumerateRunes().Count();
        Start = start < 0 ? -1 : Math.Clamp(start, 0, runeCount);
        Length = length < 0 ? -1 : Math.Clamp(length, 0, runeCount - (Start < 0 ? 0 : Start));
    }
    public void Commit(string text) { Committed += Normalize(text); Cancel(); }
    public void ReplaceCommitted(string text) { Committed = Normalize(text); Cancel(); }
    public void Cancel() { Preedit = ""; Start = Length = 0; }
    public static string RunePrefix(string text, int runeIndex)
    {
        if (runeIndex < 0) return text;
        var utf16Length = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (runeIndex-- == 0) break;
            utf16Length += rune.Utf16SequenceLength;
        }
        return text[..utf16Length];
    }
    private static string Normalize(string text) => string.Concat(text.EnumerateRunes());
    public static TextCheckResult SelfCheck()
    {
        var state = new TextState();
        state.SetPreedit("中", 0, 1); state.SetPreedit("", 0, 0);
        if (state.Preedit.Length != 0) throw new InvalidOperationException("Empty preedit did not cancel.");
        state.SetPreedit("", -1, -1);
        if (state.Start != -1 || state.Length != -1) throw new InvalidOperationException("Unset empty-preedit indices were not preserved.");
        state.SetPreedit("😀中", 1, 2);
        if (state.Start != 1 || state.Length != 1) throw new InvalidOperationException("Preedit indices were not interpreted as Unicode scalars.");
        if (RunePrefix(state.Preedit, state.Start) != "😀") throw new InvalidOperationException("Preedit cursor split a surrogate-pair rune.");
        state.SetPreedit("x", -1, -1);
        if (state.Start != -1 || state.Length != -1) throw new InvalidOperationException("Unset preedit indices were not preserved.");
        state.Commit("😀"); state.SetPreedit("x", 0, 1); state.Cancel();
        if (state.Committed != "😀" || state.Preedit.Length != 0) throw new InvalidOperationException("Text state check failed.");
        return new(true, state.Committed);
    }
}

internal sealed class EventFlags
{
    public bool Shown, Resized, FocusGained, FocusLost, DisplayScaleChanged, CloseRequested, Destroyed;
    public void Observe(SDL.EventType type) => (Shown, Resized, FocusGained, FocusLost, DisplayScaleChanged, CloseRequested, Destroyed) =
        (Shown || type == SDL.EventType.WindowShown, Resized || type == SDL.EventType.WindowResized, FocusGained || type == SDL.EventType.WindowFocusGained, FocusLost || type == SDL.EventType.WindowFocusLost, DisplayScaleChanged || type == SDL.EventType.WindowDisplayScaleChanged, CloseRequested || type == SDL.EventType.WindowCloseRequested, Destroyed || type == SDL.EventType.WindowDestroyed);
    public static EventFlags operator |(EventFlags left, EventFlags right) => new() { Shown = left.Shown || right.Shown, Resized = left.Resized || right.Resized, FocusGained = left.FocusGained || right.FocusGained, FocusLost = left.FocusLost || right.FocusLost, DisplayScaleChanged = left.DisplayScaleChanged || right.DisplayScaleChanged, CloseRequested = left.CloseRequested || right.CloseRequested, Destroyed = left.Destroyed || right.Destroyed };
}

internal static unsafe partial class Native
{
    internal const uint ProbeMessage = 0x8000 + 0x31;
    internal const uint WmGetObject = 0x003D;
    internal const uint WmClose = 0x0010;
    internal const nint UiaRootObjectId = -25;
    internal const int UiaControlTypeWindow = 50032;
    [LibraryImport("comctl32.dll", SetLastError = true)] internal static partial int SetWindowSubclass(nint hwnd, delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> callback, nuint id, nuint data);
    [LibraryImport("comctl32.dll", SetLastError = true)] internal static partial int RemoveWindowSubclass(nint hwnd, delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> callback, nuint id);
    [LibraryImport("comctl32.dll")] internal static partial nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")] internal static partial nint SendMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [LibraryImport("UIAutomationCore.dll")] internal static partial nint UiaReturnRawElementProvider(nint hwnd, nuint wParam, nint lParam, nint provider);
    [LibraryImport("UIAutomationCore.dll")] internal static partial int UiaDisconnectProvider(nint provider);
    [LibraryImport("UIAutomationCore.dll")] internal static partial int UiaHostProviderFromHwnd(nint hwnd, out nint provider);
    [LibraryImport("oleaut32.dll", EntryPoint = "SysAllocString", StringMarshalling = StringMarshalling.Utf16)] internal static partial nint SysAllocString(string value);
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct RawVariant
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(8)] public nint Value;
}

internal sealed unsafe class UiaComWrappers : ComWrappers
{
    internal static readonly Guid Iid = new("d6dd68d1-86fd-4332-8666-9abedea2d24c");
    private static readonly ComInterfaceEntry* s_entries = CreateEntries();

    private static ComInterfaceEntry* CreateEntries()
    {
        GetIUnknownImpl(out var queryInterface, out var addRef, out var release);
        var vtable = (nint*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(UiaComWrappers), IntPtr.Size * 7);
        vtable[0] = queryInterface;
        vtable[1] = addRef;
        vtable[2] = release;
        vtable[3] = (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)&ProviderOptions;
        vtable[4] = (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int, nint*, int>)&GetPatternProvider;
        vtable[5] = (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int, RawVariant*, int>)&GetPropertyValue;
        vtable[6] = (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&GetHostRawElementProvider;
        var entries = (ComInterfaceEntry*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(UiaComWrappers), sizeof(ComInterfaceEntry));
        entries[0] = new() { IID = Iid, Vtable = (nint)vtable };
        return entries;
    }

    protected override ComInterfaceEntry* ComputeVtables(object obj, CreateComInterfaceFlags flags, out int count)
    {
        if (obj is UiaRootProvider) { count = 1; return s_entries; }
        count = 0;
        return null;
    }

    protected override object CreateObject(nint externalComObject, CreateObjectFlags flags) => throw new NotSupportedException("UIA provider RCWs are not supported.");
    protected override void ReleaseObjects(System.Collections.IEnumerable objects) { }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int ProviderOptions(ComInterfaceDispatch* dispatch, int* options)
    {
        return ComInterfaceDispatch.GetInstance<UiaRootProvider>(dispatch).GetProviderOptions(out *options);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int GetPatternProvider(ComInterfaceDispatch* dispatch, int patternId, nint* provider)
    {
        return ComInterfaceDispatch.GetInstance<UiaRootProvider>(dispatch).GetPatternProvider(patternId, out *provider);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int GetPropertyValue(ComInterfaceDispatch* dispatch, int propertyId, RawVariant* value)
    {
        return ComInterfaceDispatch.GetInstance<UiaRootProvider>(dispatch).GetPropertyValue(propertyId, value);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int GetHostRawElementProvider(ComInterfaceDispatch* dispatch, nint* provider)
    {
        return ComInterfaceDispatch.GetInstance<UiaRootProvider>(dispatch).GetHostRawElementProvider(out *provider);
    }
}

internal sealed unsafe class UiaRootProvider : IDisposable
{
    private const int S_OK = 0;
    private const int VtI4 = 3;
    private const int VtBstr = 8;
    private const int ProviderOptionsServerSideProvider = 0x0002;
    private const int UiaControlTypePropertyId = 30003;
    private const int UiaNamePropertyId = 30005;
    private const int UiaAutomationIdPropertyId = 30011;
    private readonly nint _hwnd;
    private readonly UiaSnapshot _snapshot;
    private static readonly UiaComWrappers s_wrappers = new();
    private nint _unknown;
    private nint _interface;
    private int _calls;
    private int _optionsCalls;
    private int _patternCalls;
    private int _propertyCalls;
    private int _hostCalls;
    private int _disconnected;
    private int _disconnectHResult;
    private int _released;
    private bool _interfaceCreated;

    public UiaRootProvider(nint hwnd, UiaSnapshot snapshot, CcwPhase phase = CcwPhase.Release, Action<string>? mark = null)
    {
        _hwnd = hwnd;
        _snapshot = snapshot;
        mark?.Invoke("before-wrappers");
        mark?.Invoke("after-wrappers");
        if (phase == CcwPhase.Wrappers) return;
        mark?.Invoke("before-create");
        _unknown = s_wrappers.GetOrCreateComInterfaceForObject(this, CreateComInterfaceFlags.None);
        mark?.Invoke("after-create");
        if (phase == CcwPhase.Create) return;
        mark?.Invoke("before-qi");
        var iid = UiaComWrappers.Iid;
        if (QueryInterface(_unknown, &iid, out _interface) < 0 || _interface == 0)
            throw new InvalidOperationException("Could not create IRawElementProviderSimple CCW.");
        _interfaceCreated = true;
        mark?.Invoke("after-qi");
    }

    public nint InterfacePointer => _interface;
    public int CallCount => Volatile.Read(ref _calls);
    public int OptionsCallCount => Volatile.Read(ref _optionsCalls);
    public int PatternCallCount => Volatile.Read(ref _patternCalls);
    public int PropertyCallCount => Volatile.Read(ref _propertyCalls);
    public int HostCallCount => Volatile.Read(ref _hostCalls);
    public bool InterfaceCreated => _interfaceCreated;
    public bool Disconnected => Volatile.Read(ref _disconnected) != 0;
    public int DisconnectHResult => Volatile.Read(ref _disconnectHResult);
    public bool Released => Volatile.Read(ref _released) != 0;

    public int GetProviderOptions(out int options)
    {
        Interlocked.Increment(ref _calls);
        Interlocked.Increment(ref _optionsCalls);
        options = ProviderOptionsServerSideProvider;
        return S_OK;
    }

    public int GetPatternProvider(int patternId, out nint provider)
    {
        Interlocked.Increment(ref _calls);
        Interlocked.Increment(ref _patternCalls);
        provider = 0;
        return S_OK;
    }

    public int GetPropertyValue(int propertyId, RawVariant* value)
    {
        Interlocked.Increment(ref _calls);
        Interlocked.Increment(ref _propertyCalls);
        *value = default;
        switch (propertyId)
        {
            case UiaNamePropertyId:
                value->Type = VtBstr;
                value->Value = Native.SysAllocString(_snapshot.Name);
                break;
            case UiaAutomationIdPropertyId:
                value->Type = VtBstr;
                value->Value = Native.SysAllocString(_snapshot.AutomationId);
                break;
            case UiaControlTypePropertyId:
                value->Type = VtI4;
                value->Value = _snapshot.ControlType;
                break;
        }
        return S_OK;
    }

    public int GetHostRawElementProvider(out nint provider)
    {
        Interlocked.Increment(ref _calls);
        Interlocked.Increment(ref _hostCalls);
        if (_hwnd == 0)
        {
            provider = 0;
            return S_OK;
        }
        return Native.UiaHostProviderFromHwnd(_hwnd, out provider);
    }

    public void Disconnect()
    {
        if (Interlocked.Exchange(ref _disconnected, 1) == 0 && _interface != 0)
            Volatile.Write(ref _disconnectHResult, Native.UiaDisconnectProvider(_interface));
    }

    public void Dispose()
    {
        Disconnect();
        if (Interlocked.Exchange(ref _released, 1) != 0) return;
        Release(ref _interface);
        Release(ref _unknown);
    }

    private static int QueryInterface(nint unknown, Guid* iid, out nint result)
    {
        fixed (nint* output = &result)
            return ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)unknown)[0])(unknown, iid, output);
    }

    private static void Release(ref nint pointer)
    {
        if (pointer == 0) return;
        ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)pointer)[2])(pointer);
        pointer = 0;
    }

}

internal sealed record ProbeResult(bool Ok, SdlResult Sdl, SkiaResult Skia, ShapingResult Shaping, NativeModule[] Modules);
internal sealed record SdlResult(string Version, string Hwnd, uint Dpi);
internal sealed record SkiaResult(int PngBytes, string? FallbackFamily);
internal sealed record ShapingResult(int LatinGlyphs, float LatinWidth, int ArabicGlyphs, float ArabicWidth);
internal sealed record NativeModule(string? Name, string? Version, string? Path);
internal sealed record AutomatedResult(bool Ok, string Hwnd, bool HwndStable, uint Dpi, uint DpiReread, bool DpiStable, float Scale, bool ResizeSynced, bool ShownObserved, bool ResizeObserved, bool FocusGainedObserved, bool FocusLostObserved, bool ScaleObserved, bool CloseRequestedObserved, bool DoubleInstallIdempotent, int ProbeCallbackCount, bool SubclassRemoved, int CallbackCountAfterRemoval, bool DestroyCalled, bool DestroyObserved)
{
    public bool ScalePositive => Scale > 0;
    public bool CriticalClaimsPass => HwndStable && Dpi > 0 && DpiStable && ScalePositive && ResizeSynced && ShownObserved && ResizeObserved && FocusLostObserved && CloseRequestedObserved && DoubleInstallIdempotent && ProbeCallbackCount == 1 && SubclassRemoved && CallbackCountAfterRemoval == 0 && DestroyCalled && DestroyObserved;
}
internal sealed record ManualResult(bool Ok, string LogPath, int EventCount, string Committed);
internal sealed record TextCheckResult(bool Ok, string Committed);
internal sealed record ControlsCheckResult(bool Ok, bool ButtonActivation, bool ButtonSemantics, bool TextEditing, bool UnicodeSafe, bool ImeComposition, bool Clipboard, bool FocusVisible, bool ValueSemantics, bool NegativeCases, bool Disposal);
internal sealed record TextLog(string Kind, string? Text, string Committed, string Preedit, int Start, int Length);
internal sealed record UiaSnapshot(string Name, string AutomationId, int ControlType);
internal enum CcwPhase { Wrappers = 1, Create, QueryInterface, Options, Disconnect, Release }
internal sealed record CcwMarker(string Marker);
internal sealed record CcwSelfCheckResult(bool Ok, string Phase, int OptionsHResult, bool InterfaceCreated, bool Disconnected, int DisconnectHResult, bool Released);
internal sealed record UiaReadyResult(bool Ok, string Hwnd, bool DynamicCodeSupported);
internal sealed record UiaHostResult(bool Ok, string Hwnd, bool DynamicCodeUnsupported, bool InvalidIdsChained, int InvalidDeliveryCount, int ExternalRootDeliveryCount, int TotalDeliveryCount, int ProviderCallCount, int ProviderOptionsCallCount, int ProviderPropertyCallCount, int ProviderHostCallCount, bool ProviderInterfaceCreated, bool ProviderDisconnected, int DisconnectHResult, bool SubclassRemoved, bool ProviderReleased, bool HwndDestroyed)
{
    public bool CriticalClaimsPass => DynamicCodeUnsupported && InvalidIdsChained && InvalidDeliveryCount >= 2 && ExternalRootDeliveryCount >= 3 && ProviderPropertyCallCount > 0 && ProviderInterfaceCreated && ProviderDisconnected && DisconnectHResult >= 0 && SubclassRemoved && ProviderReleased && HwndDestroyed;
}
internal sealed record FailureResult(bool Ok, string Error);

[JsonSerializable(typeof(ProbeResult))]
[JsonSerializable(typeof(AutomatedResult))]
[JsonSerializable(typeof(ManualResult))]
[JsonSerializable(typeof(TextCheckResult))]
[JsonSerializable(typeof(ReactiveCheckResult))]
[JsonSerializable(typeof(StyleCheckResult))]
[JsonSerializable(typeof(StructuralCheckResult))]
[JsonSerializable(typeof(ControlsCheckResult))]
[JsonSerializable(typeof(VirtualizationCheckResult))]
[JsonSerializable(typeof(SemanticDumpItem[]))]
[JsonSerializable(typeof(SceneCheckResult))]
[JsonSerializable(typeof(LayoutCheckResult))]
[JsonSerializable(typeof(TextLog))]
[JsonSerializable(typeof(CcwMarker))]
[JsonSerializable(typeof(CcwSelfCheckResult))]
[JsonSerializable(typeof(UiaReadyResult))]
[JsonSerializable(typeof(UiaHostResult))]
[JsonSerializable(typeof(FailureResult))]
[JsonSerializable(typeof(IssueBrowserStep))]
[JsonSerializable(typeof(IssueBrowserIdentity))]
[JsonSerializable(typeof(IssueBrowserSemantic[]))]
[JsonSerializable(typeof(IssueBrowserSeed))]
internal sealed partial class ProbeJsonContext : JsonSerializerContext;
