using System.ComponentModel;

namespace Lucent.Core;

/// <summary>One generated application icon and its finite packaged pixel renditions.</summary>
public sealed class ApplicationIconDefault
{
    /// <summary>Initializes a generated application icon.</summary>
    /// <param name="source">The declared source artwork.</param>
    /// <param name="renditions">Optional precomputed pixel sources keyed by physical size.</param>
    public ApplicationIconDefault(
        ImageSource source,
        IReadOnlyList<ApplicationIconRendition>? renditions = null
    )
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        var copied = renditions?.ToArray() ?? [];
        var sizes = new HashSet<int>();
        foreach (var rendition in copied)
        {
            ArgumentNullException.ThrowIfNull(rendition);
            if (!sizes.Add(rendition.PixelSize))
                throw new ArgumentException(
                    "Application icon rendition sizes must be unique.",
                    nameof(renditions)
                );
        }
        Renditions = Array.AsReadOnly(copied);
    }

    /// <summary>Gets the declared source artwork.</summary>
    public ImageSource Source { get; }

    /// <summary>Gets immutable precomputed pixel sources for platform hosts.</summary>
    public IReadOnlyList<ApplicationIconRendition> Renditions { get; }
}

/// <summary>A packaged pixel source for one physical application-icon size.</summary>
public sealed class ApplicationIconRendition
{
    /// <summary>Initializes an application-icon rendition.</summary>
    public ApplicationIconRendition(int pixelSize, ImageSource source)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize);
        PixelSize = pixelSize;
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>Gets the square physical pixel size represented by <see cref="Source"/>.</summary>
    public int PixelSize { get; }

    /// <summary>Gets the packaged PNG source for this rendition.</summary>
    public ImageSource Source { get; }
}

/// <summary>Provides the statically generated application artwork used by platform hosts.</summary>
/// <remarks>
/// The Lucent SDK emits one module initializer for the executable's
/// <c>ApplicationIcon="true"</c> asset. This registry is the small portable seam between that
/// generated source and an optional platform host; it performs no resource discovery or
/// reflection. Referenced libraries are rejected by the SDK before they can register a default.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ApplicationIconDefaults
{
    private static ApplicationIconDefault? _current;

    /// <summary>Gets the generated application icon, if the current executable declared one.</summary>
    public static ApplicationIconDefault? Current => Volatile.Read(ref _current);

    /// <summary>Registers the one generated application icon for this process.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="icon"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A generated application icon was already registered.</exception>
    public static void RegisterGenerated(ApplicationIconDefault icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        if (Interlocked.CompareExchange(ref _current, icon, null) is not null)
            throw new InvalidOperationException(
                "Only one generated application icon may be registered in an executable."
            );
    }
}
