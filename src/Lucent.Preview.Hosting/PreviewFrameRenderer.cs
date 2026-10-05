using Lucent.Core;
using Lucent.Renderer.Skia;
using SkiaSharp;

namespace Lucent.Preview.Hosting;

/// <summary>Owner-thread resources for one fixed, protocol-bounded preview viewport.</summary>
internal sealed class PreviewFrameRenderer : IDisposable
{
    private readonly SKBitmap _bitmap;
    private readonly SKCanvas _canvas;
    private readonly int _surfaceBytes;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;

    internal PreviewFrameRenderer(LayoutViewport viewport)
    {
        _bitmap = new(
            checked((int)MathF.Ceiling(viewport.Width * viewport.Scale)),
            checked((int)MathF.Ceiling(viewport.Height * viewport.Scale)),
            SKColorType.Rgba8888,
            SKAlphaType.Premul
        );
        _surfaceBytes = _bitmap.ByteCount;
        SKCanvas? canvas = null;
        try
        {
            _canvas = canvas = new(_bitmap);
            Renderer = new();
        }
        catch
        {
            canvas?.Dispose();
            _bitmap.Dispose();
            throw;
        }
    }

    internal SkiaSceneRenderer Renderer { get; }
    internal int SurfaceBytes => _surfaceBytes;

    internal byte[] Capture(RetainedScene scene, bool showCaret)
    {
        CheckOwner();
        _canvas.Clear(SKColors.Transparent);
        Renderer.Render(scene, _canvas, showCaret);
        using var image = SKImage.FromBitmap(_bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray()
            ?? throw new InvalidOperationException("Preview PNG encoding failed.");
    }

    public void Dispose()
    {
        CheckOwner();
        try
        {
            Renderer.Dispose();
        }
        finally
        {
            try
            {
                _canvas.Dispose();
            }
            finally
            {
                _bitmap.Dispose();
            }
        }
    }

    private void CheckOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException(
                "Preview render resources belong to their owner thread."
            );
    }
}
