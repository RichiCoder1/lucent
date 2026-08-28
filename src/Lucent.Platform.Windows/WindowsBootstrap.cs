using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;
using SkiaSharp;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Lucent.Platform.Windows;

/// <summary>Owns the temporary SDL/Skia M0 static-window path.</summary>
public static class WindowsBootstrap
{
    public const float BackingScale = 1.25F;
    private const int TestPresentationWidth = 800;
    private const int TestPresentationHeight = 500;

    [STAThread]
    public static int Run(string title, Composition composition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(composition);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
            throw new PlatformNotSupportedException("M0 requires Windows 10 version 1607 or later.");
        RequireNativeAssets();

        if (!SDL.Init(SDL.InitFlags.Video))
            throw new InvalidOperationException($"SDL_Init: {SDL.GetError()}");

        nint window = 0;
        nint renderer = 0;
        nint texture = 0;
        try
        {
            window = SDL.CreateWindow(title, TestPresentationWidth, TestPresentationHeight,
                SDL.WindowFlags.Resizable | SDL.WindowFlags.HighPixelDensity);
            if (window == 0)
                throw new InvalidOperationException($"SDL_CreateWindow: {SDL.GetError()}");

            var hwnd = SDL.GetPointerProperty(SDL.GetWindowProperties(window), SDL.Props.WindowWin32HWNDPointer, 0);
            if (hwnd == 0 || PInvoke.GetDpiForWindow(new HWND(hwnd)) == 0)
                throw new InvalidOperationException("SDL did not expose a usable HWND.");

            renderer = SDL.CreateRenderer(window, null);
            if (renderer == 0 || !SDL.GetRenderOutputSize(renderer, out var backingWidth, out var backingHeight) ||
                backingWidth <= 0 || backingHeight <= 0)
                throw new InvalidOperationException($"SDL renderer: {SDL.GetError()}");

            var logicalWidth = checked((int)(backingWidth / BackingScale));
            var logicalHeight = checked((int)(backingHeight / BackingScale));
            if (logicalWidth * BackingScale != backingWidth || logicalHeight * BackingScale != backingHeight ||
                !SDL.SetRenderLogicalPresentation(renderer, logicalWidth, logicalHeight, SDL.RendererLogicalPresentation.Stretch) ||
                !SDL.GetRenderLogicalPresentation(renderer, out var confirmedWidth, out var confirmedHeight, out _ ) ||
                confirmedWidth != logicalWidth || confirmedHeight != logicalHeight)
                throw new InvalidOperationException("SDL could not establish the declared 1.25x test presentation scale.");

            texture = SDL.CreateTexture(renderer, SDL.PixelFormat.ABGR8888, SDL.TextureAccess.Streaming, backingWidth, backingHeight);
            if (texture == 0)
                throw new InvalidOperationException($"SDL texture: {SDL.GetError()}");

            using var rendererScene = new SkiaSceneRenderer();
            var scene = SceneLayout.Project(composition, new(logicalWidth, logicalHeight, BackingScale), rendererScene);
            using var bitmap = new SKBitmap(backingWidth, backingHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(new SKColor(15, 23, 42));
                rendererScene.Render(scene, canvas);
            }
            var destination = new SDL.FRect { X = 0, Y = 0, W = logicalWidth, H = logicalHeight };
            if (!SDL.UpdateTexture(texture, 0, bitmap.GetPixels(), bitmap.RowBytes) ||
                !SDL.RenderTexture(renderer, texture, 0, in destination) ||
                !SDL.RenderPresent(renderer))
                throw new InvalidOperationException($"SDL present: {SDL.GetError()}");

            var open = true;
            while (open)
            {
                while (SDL.PollEvent(out var @event))
                    open &= (SDL.EventType)@event.Type is not (SDL.EventType.Quit or SDL.EventType.WindowCloseRequested);
                SDL.Delay(10);
            }
            return 0;
        }
        finally
        {
            if (texture != 0) SDL.DestroyTexture(texture);
            if (renderer != 0) SDL.DestroyRenderer(renderer);
            if (window != 0) SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    private static void RequireNativeAssets()
    {
        foreach (var asset in new[] { "SDL3.dll", "libSkiaSharp.dll", "libHarfBuzzSharp.dll" })
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, asset)))
                throw new FileNotFoundException($"Required native asset missing: {asset}.");
    }
}
