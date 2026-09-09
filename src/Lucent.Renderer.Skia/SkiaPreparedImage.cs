using Lucent.Core;
using SkiaSharp;

namespace Lucent.Renderer.Skia;

/// <summary>
/// Adapter boundary for a prepared image representation that is rendered by Skia.
/// </summary>
/// <remarks>
/// Raster preparation uses Core's portable <see cref="RasterImage"/> contract. A future static
/// vector adapter may implement this interface on its own <see cref="PreparedImage"/> subclass,
/// retaining its native or vector representation without forcing Core to reference Skia types.
/// <see cref="Draw"/> is called only on the owning <see cref="SkiaSceneRenderer"/> thread while
/// its canvas is active; implementations must preserve the canvas state they receive.
/// </remarks>
public interface ISkiaPreparedImage
{
    /// <summary>Draws the prepared representation into the supplied logical-pixel destination.</summary>
    void Draw(
        SKCanvas canvas,
        LayoutRect sourceBounds,
        LayoutRect destinationBounds,
        ImageColorMode colorMode,
        Color tint
    );
}
