using System.Diagnostics;
using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;
using SkiaSharp;

namespace Lucent.Platform.Windows;

/// <summary>Owns the persistent CPU Skia surface and its matching SDL streaming texture on the window thread.</summary>
internal sealed class CpuSkiaPresenter : IDisposable
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly nint _renderer;
    private readonly CpuResourceState _resources = new();
    private SKSurface? _surface;
    private nint _texture;
    private bool _disposed;

    public CpuSkiaPresenter(nint renderer)
    {
        ArgumentOutOfRangeException.ThrowIfZero(renderer, nameof(renderer));
        _renderer = renderer;
    }

    internal int LiveSurfaceCount => _surface is null ? 0 : 1;
    internal int LiveTextureCount => _texture == 0 ? 0 : 1;

    public PresenterPhaseTimestamps Present(
        RetainedScene scene,
        WindowsViewport viewport,
        SkiaSceneRenderer renderer,
        bool showCaret = true
    )
    {
        CheckThread();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(renderer);
        if (!viewport.IsRenderable)
            throw new ArgumentOutOfRangeException(nameof(viewport));
        EnsureResources(new(viewport));
        var rasterStarted = Stopwatch.GetTimestamp();
        var canvas = _surface!.Canvas;
        canvas.Clear(SKColors.Transparent);
        renderer.Render(scene, canvas, showCaret);
        var rasterized = Stopwatch.GetTimestamp();
        using var pixels =
            _surface.PeekPixels()
            ?? throw new InvalidOperationException("Skia surface did not expose CPU pixels.");
        if (!SDL.UpdateTexture(_texture, 0, pixels.GetPixels(), pixels.RowBytes))
            throw new InvalidOperationException($"SDL_UpdateTexture: {SDL.GetError()}");
        var uploaded = Stopwatch.GetTimestamp();
        if (!SDL.RenderTexture(_renderer, _texture, 0, 0) || !SDL.RenderPresent(_renderer))
            throw new InvalidOperationException($"SDL present: {SDL.GetError()}");
        var presented = Stopwatch.GetTimestamp();
        return new(rasterized, uploaded, presented);
    }

    public void Dispose()
    {
        CheckThread();
        if (_disposed)
            return;
        _disposed = true;
        DestroyResources();
    }

    private void EnsureResources(CpuResourceDescriptor descriptor)
    {
        if (!_resources.NeedsRecreation(descriptor))
            return;
        if (descriptor.Format != WindowsPresentationContract.SurfaceFormat)
            throw new InvalidOperationException(
                "The CPU presenter supports only premultiplied RGBA8888."
            );
        DestroyResources();
        using var colorSpace = SKColorSpace.CreateSrgb();
        _surface =
            SKSurface.Create(
                new SKImageInfo(
                    descriptor.Width,
                    descriptor.Height,
                    SKColorType.Rgba8888,
                    SKAlphaType.Premul,
                    colorSpace
                )
            )
            ?? throw new InvalidOperationException("Skia could not create the CPU raster surface.");
        _texture = SDL.CreateTexture(
            _renderer,
            SDL.PixelFormat.ABGR8888,
            SDL.TextureAccess.Streaming,
            descriptor.Width,
            descriptor.Height
        );
        if (_texture == 0)
            throw new InvalidOperationException($"SDL_CreateTexture: {SDL.GetError()}");
        _resources.Commit(descriptor);
    }

    private void DestroyResources()
    {
        if (_texture != 0)
        {
            SDL.DestroyTexture(_texture);
            _texture = 0;
        }
        _surface?.Dispose();
        _surface = null;
        _resources.Release();
    }

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException(
                "CPU presenter access must remain on its owner thread."
            );
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            ObjectDisposedException.ThrowIf(true, typeof(CpuSkiaPresenter));
    }
}

internal readonly record struct PresenterPhaseTimestamps(
    long Rasterized,
    long Uploaded,
    long Presented
);
