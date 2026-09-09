using System.Runtime.InteropServices;
using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows;

/// <summary>Owns the SDL surfaces used by one window icon.</summary>
/// <remarks>
/// Artwork preparation is deliberately performed on a worker. SDL surface creation, alternate
/// registration and <see cref="SDL.SetWindowIcon(nint, nint)"/> are performed by the owner thread,
/// as required by SDL. The base surface remains alive until the window has been destroyed.
/// </remarks>
internal sealed class WindowsWindowIcon : IDisposable
{
    private nint _baseSurface;
    private int _disposed;

    private WindowsWindowIcon(nint baseSurface)
    {
        _baseSurface = baseSurface;
    }

    /// <summary>Runs the shared renderer preparation on a bounded worker task.</summary>
    internal static Task<IReadOnlyList<ArtworkRendition>> PrepareAsync(
        ImageSource source,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        return Task.Run(
            () =>
                SkiaArtwork.PrepareRenditions(
                    source,
                    SkiaArtwork.ApplicationIconSizes,
                    new SkiaImagePreparer(),
                    ImageLoadLimits.Default,
                    cancellationToken
                ),
            cancellationToken
        );
    }

    /// <summary>Prepares packaged PNG renditions for a generated application default.</summary>
    internal static Task<IReadOnlyList<ArtworkRendition>> PrepareAsync(
        IReadOnlyList<ApplicationIconRendition> renditions,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(renditions);
        var copied = renditions.ToArray();
        if (copied.Length == 0)
            throw new ArgumentException(
                "At least one packaged icon rendition is required.",
                nameof(renditions)
            );
        return Task.Run(
            () =>
            {
                var output = new ArtworkRendition[copied.Length];
                var preparer = new SkiaImagePreparer();
                for (var index = 0; index < copied.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var rendition = copied[index];
                    output[index] = SkiaArtwork
                        .PrepareRenditions(
                            rendition.Source,
                            [rendition.PixelSize],
                            preparer,
                            ImageLoadLimits.Default,
                            cancellationToken
                        )
                        .Single();
                }
                return (IReadOnlyList<ArtworkRendition>)output;
            },
            cancellationToken
        );
    }

    /// <summary>Installs prepared surfaces on the SDL owner thread.</summary>
    internal static WindowsWindowIcon Install(
        nint window,
        IReadOnlyList<ArtworkRendition> renditions
    )
    {
        ArgumentOutOfRangeException.ThrowIfZero(window);
        ArgumentNullException.ThrowIfNull(renditions);
        if (renditions.Count == 0)
            throw new ArgumentException(
                "Windows icons require at least one finite rendition.",
                nameof(renditions)
            );

        var surfaces = new List<(int Size, nint Surface)>(renditions.Count);
        nint baseSurface = 0;
        try
        {
            foreach (var rendition in renditions)
            {
                ArgumentNullException.ThrowIfNull(rendition);
                if (rendition.Width is <= 0 or > 256 || rendition.Width != rendition.Height)
                    throw new ArgumentException(
                        "Windows icon renditions must be unique square sizes no larger than 256 pixels.",
                        nameof(renditions)
                    );
                if (surfaces.Any(pair => pair.Size == rendition.Width))
                    throw new ArgumentException(
                        "Windows icon rendition sizes must be unique.",
                        nameof(renditions)
                    );
                var surface = CreateSurface(rendition);
                surfaces.Add((rendition.Width, surface));
            }

            baseSurface = surfaces
                .OrderBy(pair => Math.Abs(pair.Size - 32))
                .ThenBy(pair => pair.Size)
                .First()
                .Surface;
            for (var index = 0; index < surfaces.Count; index++)
            {
                var alternate = surfaces[index];
                if (alternate.Surface == baseSurface)
                    continue;
                if (!SDL.AddSurfaceAlternateImage(baseSurface, alternate.Surface))
                    throw new InvalidOperationException(
                        $"SDL_AddSurfaceAlternateImage: {SDL.GetError()}"
                    );
                // SDL takes an alternate reference. The caller releases its construction
                // reference immediately; the base surface owns the alternate until removal.
                SDL.DestroySurface(alternate.Surface);
                surfaces[index] = (alternate.Size, 0);
            }

            if (!SDL.SetWindowIcon(window, baseSurface))
                throw new InvalidOperationException($"SDL_SetWindowIcon: {SDL.GetError()}");
            var owner = new WindowsWindowIcon(baseSurface);
            surfaces.Clear();
            return owner;
        }
        catch
        {
            if (baseSurface != 0)
                SDL.RemoveSurfaceAlternateImages(baseSurface);
            foreach (var pair in surfaces)
                if (pair.Surface != 0)
                    SDL.DestroySurface(pair.Surface);
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        var surface = Interlocked.Exchange(ref _baseSurface, 0);
        if (surface == 0)
            return;
        SDL.RemoveSurfaceAlternateImages(surface);
        SDL.DestroySurface(surface);
    }

    private static nint CreateSurface(ArtworkRendition rendition)
    {
        var surface = SDL.CreateSurface(
            rendition.Width,
            rendition.Height,
            // SDL's Windows backend requires ARGB8888 here before it copies the
            // surface into a 32-bit DIB for CreateIconFromResource.
            SDL.PixelFormat.ARGB8888
        );
        if (surface == 0)
            throw new InvalidOperationException($"SDL_CreateSurface: {SDL.GetError()}");

        var locked = false;
        try
        {
            locked = SDL.LockSurface(surface);
            if (!locked)
                throw new InvalidOperationException($"SDL_LockSurface: {SDL.GetError()}");
            unsafe
            {
                var description = *(SDL.Surface*)surface;
                if (description.Pixels == 0 || description.Pitch < rendition.Width * 4)
                    throw new InvalidOperationException(
                        "SDL returned an invalid icon surface layout."
                    );
                var pixels = ToSdlArgb8888(rendition.PremultipliedSrgbRgba.Span);
                for (var row = 0; row < rendition.Height; row++)
                {
                    var destination = description.Pixels + row * description.Pitch;
                    Marshal.Copy(
                        pixels,
                        row * rendition.Width * 4,
                        destination,
                        rendition.Width * 4
                    );
                }
            }
            return surface;
        }
        catch
        {
            if (locked)
            {
                SDL.UnlockSurface(surface);
                locked = false;
            }
            SDL.DestroySurface(surface);
            throw;
        }
        finally
        {
            if (locked)
                SDL.UnlockSurface(surface);
        }
    }

    /// <summary>Converts Skia's premultiplied RGBA bytes to Windows SDL's straight BGRA DIB bytes.</summary>
    internal static byte[] ToSdlArgb8888(ReadOnlySpan<byte> premultipliedRgba)
    {
        if (premultipliedRgba.Length % 4 != 0)
            throw new ArgumentException(
                "RGBA pixels must contain four bytes per pixel.",
                nameof(premultipliedRgba)
            );
        var output = new byte[premultipliedRgba.Length];
        for (var index = 0; index < premultipliedRgba.Length; index += 4)
        {
            var alpha = premultipliedRgba[index + 3];
            output[index] = Unpremultiply(premultipliedRgba[index + 2], alpha);
            output[index + 1] = Unpremultiply(premultipliedRgba[index + 1], alpha);
            output[index + 2] = Unpremultiply(premultipliedRgba[index], alpha);
            output[index + 3] = alpha;
        }
        return output;
    }

    private static byte Unpremultiply(byte channel, byte alpha) =>
        alpha == 0 ? (byte)0 : (byte)Math.Min(255, (channel * 255 + alpha / 2) / alpha);
}
