using System.Runtime.InteropServices;
using Lucent.Platform.Windows;
using SDL3;

internal static partial class ListenerProof
{
    private const uint Red = 0x000000ff;
    private const uint Green = 0x0000ff00;

    internal static int Run()
    {
        if (!SDL.Init(SDL.InitFlags.Video)) return Fail("SDL_Init: " + SDL.GetError());
        nint window = 0;
        WindowsSettingsListener? listener = null;
        try
        {
            window = SDL.CreateWindow("Lucent Windows listener proof", 160, 120, SDL.WindowFlags.Resizable);
            if (window == 0) return Fail("SDL_CreateWindow: " + SDL.GetError());
            var hwnd = SDL.GetPointerProperty(SDL.GetWindowProperties(window), SDL.Props.WindowWin32HWNDPointer, 0);
            if (hwnd == 0) return Fail("SDL window did not expose an HWND.");
            listener = new WindowsSettingsListener(hwnd);
            Paint(hwnd, Red);
            Console.WriteLine("LISTENER-PROOF READY");
            var refreshed = false;
            while (SDL.WaitEvent(out var @event))
            {
                if (listener.TakePending()) { refreshed = true; Paint(hwnd, Green); Console.WriteLine("LISTENER-PROOF REFRESHED"); }
                if ((SDL.EventType)@event.Type == SDL.EventType.WindowExposed && !refreshed) Paint(hwnd, Red);
                if ((SDL.EventType)@event.Type is SDL.EventType.Quit or SDL.EventType.WindowCloseRequested) return 0;
            }
            return Fail("SDL_WaitEvent: " + SDL.GetError());
        }
        catch (Exception error) { return Fail(error.Message); }
        finally
        {
            listener?.Dispose();
            if (window != 0) SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    private static void Paint(nint window, uint color)
    {
        if (!GetClientRect(window, out var rectangle) || rectangle.Right <= 0 || rectangle.Bottom <= 0)
            throw new InvalidOperationException("Listener proof window had no client area.");
        var dc = GetDC(window);
        if (dc == 0) throw new InvalidOperationException("GetDC(listener proof) failed.");
        var brush = CreateSolidBrush(color);
        if (brush == 0) { _ = ReleaseDC(window, dc); throw new InvalidOperationException("CreateSolidBrush(listener proof) failed."); }
        try
        {
            if (FillRect(dc, ref rectangle, brush) == 0) throw new InvalidOperationException("FillRect(listener proof) failed.");
        }
        finally { _ = DeleteObject(brush); _ = ReleaseDC(window, dc); }
    }

    private static int Fail(string message) { Console.Error.WriteLine("Lucent Windows listener proof: " + message); return 1; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { internal int Left; internal int Top; internal int Right; internal int Bottom; }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint window, out Rect rectangle);
    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint window);
    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint window, nint dc);
    [LibraryImport("user32.dll")]
    private static partial int FillRect(nint dc, ref Rect rectangle, nint brush);
    [LibraryImport("gdi32.dll")]
    private static partial nint CreateSolidBrush(uint color);
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint value);
}
