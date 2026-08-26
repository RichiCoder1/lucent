using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using SDL3;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Windows.Win32.Foundation;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            Console.WriteLine(JsonSerializer.Serialize(Run(), ProbeJsonContext.Default.ProbeResult));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new FailureResult(false, exception.Message), ProbeJsonContext.Default.FailureResult));
            return 1;
        }
    }

    private static ProbeResult Run()
    {
        if (!SDL.Init(SDL.InitFlags.Video))
            throw new InvalidOperationException($"SDL_Init: {SDL.GetError()}");

        IntPtr window = IntPtr.Zero;
        try
        {
            window = SDL.CreateWindow("NativeStackProbe", 1, 1, SDL.WindowFlags.Hidden);
            if (window == IntPtr.Zero)
                throw new InvalidOperationException($"SDL_CreateWindow: {SDL.GetError()}");

            var properties = SDL.GetWindowProperties(window);
            var hwnd = SDL.GetPointerProperty(properties, SDL.Props.WindowWin32HWNDPointer, IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException("SDL did not expose a Win32 HWND.");

            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
                throw new PlatformNotSupportedException("GetDpiForWindow requires Windows 10 version 1607 or newer.");
            var dpi = Windows.Win32.PInvoke.GetDpiForWindow(new HWND(hwnd));
            if (dpi == 0)
                throw new InvalidOperationException("GetDpiForWindow returned zero.");

            var png = RasterProbe();
            var shaping = ShapeProbe();
            var fallback = SKFontManager.Default.MatchCharacter('漢');
            if (fallback is null)
                throw new InvalidOperationException("Skia font fallback lookup returned null.");

            return new(true, new(SDL.GetVersion().ToString(), $"0x{hwnd.ToInt64():X}", dpi), new(png, fallback.FamilyName), shaping, LoadedModules());
        }
        finally
        {
            if (window != IntPtr.Zero)
                SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    private static int RasterProbe()
    {
        using var surface = SKSurface.Create(new SKImageInfo(8, 8));
        surface.Canvas.Clear(SKColors.CornflowerBlue);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null || data.Size == 0)
            throw new InvalidOperationException("Skia PNG encoding failed.");
        return checked((int)data.Size);
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

    private static NativeModule[] LoadedModules() => Process.GetCurrentProcess().Modules
        .Cast<ProcessModule>()
        .Where(module => module.FileName is not null &&
            (module.FileName.Contains("SDL", StringComparison.OrdinalIgnoreCase) ||
             module.FileName.Contains("Skia", StringComparison.OrdinalIgnoreCase) ||
             module.FileName.Contains("HarfBuzz", StringComparison.OrdinalIgnoreCase)))
        .Select(module => new NativeModule(module.ModuleName, module.FileVersionInfo.FileVersion, module.FileName))
        .ToArray();
}

internal sealed record ProbeResult(bool Ok, SdlResult Sdl, SkiaResult Skia, ShapingResult Shaping, NativeModule[] Modules);
internal sealed record SdlResult(string Version, string Hwnd, uint Dpi);
internal sealed record SkiaResult(int PngBytes, string? FallbackFamily);
internal sealed record ShapingResult(int LatinGlyphs, float LatinWidth, int ArabicGlyphs, float ArabicWidth);
internal sealed record NativeModule(string? Name, string? Version, string? Path);
internal sealed record FailureResult(bool Ok, string Error);

[JsonSerializable(typeof(ProbeResult))]
[JsonSerializable(typeof(FailureResult))]
internal sealed partial class ProbeJsonContext : JsonSerializerContext;
