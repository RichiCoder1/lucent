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
    private readonly Func<CpuResourceDescriptor, nint> _createTexture;
    private readonly CpuResourceState _resources = new();
    private SKSurface? _surface;
    private nint _texture;
    private bool _disposed;

    public CpuSkiaPresenter(nint renderer, Func<CpuResourceDescriptor, nint>? createTexture = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(renderer, nameof(renderer));
        _renderer = renderer;
        _createTexture =
            createTexture
            ?? (
                descriptor =>
                    SDL.CreateTexture(
                        _renderer,
                        SDL.PixelFormat.ABGR8888,
                        SDL.TextureAccess.Streaming,
                        descriptor.Width,
                        descriptor.Height
                    )
            );
    }

    internal int LiveSurfaceCount => _surface is null ? 0 : 1;
    internal int LiveTextureCount => _texture == 0 ? 0 : 1;
    internal int ResourceCreationCount => _resources.CreationCount;

    /// <summary>Invalidates environmental presentation resources; application/render callbacks are never retried.</summary>
    internal bool HandleRendererEvent(SDL.EventType type)
    {
        CheckThread();
        ThrowIfDisposed();
        switch (type)
        {
            case SDL.EventType.RenderTargetsReset:
                return true;
            case SDL.EventType.RenderDeviceReset:
                DestroyResources();
                return true;
            case SDL.EventType.RenderDeviceLost:
                throw new InvalidOperationException(
                    "SDL render device was lost and cannot be recovered."
                );
            default:
                return false;
        }
    }

    public PresenterPhaseTimestamps Present(
        RetainedScene scene,
        WindowsViewport viewport,
        SkiaSceneRenderer renderer,
        bool showCaret = true,
        Action<SKCanvas>? drawUnderlay = null
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
        drawUnderlay?.Invoke(canvas);
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
        try
        {
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
                ?? throw new InvalidOperationException(
                    "Skia could not create the CPU raster surface."
                );
            _texture = _createTexture(descriptor);
            if (_texture == 0)
                throw new InvalidOperationException($"SDL_CreateTexture: {SDL.GetError()}");
            // Copy the complete premultiplied frame, including transparent popup margins.
            // Blending here would multiply alpha twice and accumulate old shadow pixels.
            if (!SDL.SetTextureBlendMode(_texture, SDL.BlendMode.None))
                throw new InvalidOperationException($"SDL_SetTextureBlendMode: {SDL.GetError()}");
            _resources.Commit(descriptor);
        }
        catch
        {
            DestroyResources();
            throw;
        }
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
