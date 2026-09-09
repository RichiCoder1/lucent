namespace Lucent.Core;

/// <summary>Classifies expected image-loading failures that applications may present or retry.</summary>
public enum ImageLoadFailureKind
{
    /// <summary>The source contents or prepared output are malformed.</summary>
    InvalidData,

    /// <summary>The source uses a format the preparer does not support.</summary>
    UnsupportedFormat,

    /// <summary>The source could not be opened or read.</summary>
    SourceUnavailable,

    /// <summary>A configured resource limit prevented the operation.</summary>
    BudgetDeclined,
}

/// <summary>An expected image-loading failure, distinct from a preparer or provider bug.</summary>
public sealed class ImageLoadException : Exception
{
    /// <summary>Initializes an expected image-loading failure.</summary>
    public ImageLoadException(ImageLoadFailureKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    /// <summary>Initializes an expected image-loading failure with its cause.</summary>
    public ImageLoadException(ImageLoadFailureKind kind, string message, Exception innerException)
        : base(message, innerException)
    {
        Kind = kind;
    }

    /// <summary>Gets the category of the failure.</summary>
    public ImageLoadFailureKind Kind { get; }
}

/// <summary>A bucketed output size requested from an image preparer.</summary>
public sealed record ImageRendition
{
    /// <summary>Initializes a rendition with the requested physical pixel size.</summary>
    public ImageRendition(int pixelWidth, int pixelHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
    }

    /// <summary>Gets the requested physical pixel width.</summary>
    public int PixelWidth { get; }

    /// <summary>Gets the requested physical pixel height.</summary>
    public int PixelHeight { get; }
}

/// <summary>Finite per-application limits for image preparation and retention.</summary>
public sealed record ImageLoadLimits
{
    /// <summary>Gets the default application limits.</summary>
    public static ImageLoadLimits Default { get; } = new();

    /// <summary>Initializes finite image preparation and retention limits.</summary>
    public ImageLoadLimits(
        long maximumEncodedBytes = 64L * 1024 * 1024,
        long maximumSourcePixels = 64L * 1024 * 1024,
        long maximumOutputBytes = 64L * 1024 * 1024,
        long maximumTemporaryBytes = 128L * 1024 * 1024,
        long maximumCachedBytes = 128L * 1024 * 1024,
        long maximumLeasedBytes = 128L * 1024 * 1024,
        int maximumQueuedRequests = 128,
        int maximumConcurrentPreparations = 4
    )
    {
        MaximumEncodedBytes = Positive(maximumEncodedBytes, nameof(maximumEncodedBytes));
        MaximumSourcePixels = Positive(maximumSourcePixels, nameof(maximumSourcePixels));
        MaximumOutputBytes = Positive(maximumOutputBytes, nameof(maximumOutputBytes));
        MaximumTemporaryBytes = Positive(maximumTemporaryBytes, nameof(maximumTemporaryBytes));
        MaximumCachedBytes = Positive(maximumCachedBytes, nameof(maximumCachedBytes));
        MaximumLeasedBytes = Positive(maximumLeasedBytes, nameof(maximumLeasedBytes));
        MaximumQueuedRequests = Positive(maximumQueuedRequests, nameof(maximumQueuedRequests));
        MaximumConcurrentPreparations = Positive(
            maximumConcurrentPreparations,
            nameof(maximumConcurrentPreparations)
        );
    }

    /// <summary>Gets the largest accepted encoded source.</summary>
    public long MaximumEncodedBytes { get; }

    /// <summary>Gets the largest accepted decoded source area.</summary>
    public long MaximumSourcePixels { get; }

    /// <summary>Gets the largest prepared output.</summary>
    public long MaximumOutputBytes { get; }

    /// <summary>Gets the shared temporary preparation-memory budget.</summary>
    public long MaximumTemporaryBytes { get; }

    /// <summary>Gets the unleased cache-memory budget.</summary>
    public long MaximumCachedBytes { get; }

    /// <summary>Gets the retained lease-memory budget.</summary>
    public long MaximumLeasedBytes { get; }

    /// <summary>Gets the maximum number of waiting preparations.</summary>
    public int MaximumQueuedRequests { get; }

    /// <summary>Gets the maximum number of concurrent preparations.</summary>
    public int MaximumConcurrentPreparations { get; }

    private static long Positive(long value, string parameterName) =>
        value > 0 ? value : throw new ArgumentOutOfRangeException(parameterName);

    private static int Positive(int value, string parameterName) =>
        value > 0 ? value : throw new ArgumentOutOfRangeException(parameterName);
}

/// <summary>The immutable input and memory policy for one image preparation.</summary>
public sealed class ImagePreparationRequest
{
    private readonly Func<long, IDisposable>? _reserveTemporary;

    /// <summary>Initializes a request for an image preparer.</summary>
    public ImagePreparationRequest(
        ImageSource source,
        ImageRendition rendition,
        ImageLoadLimits limits
    )
        : this(source, rendition, limits, null) { }

    internal ImagePreparationRequest(
        ImageSource source,
        ImageRendition rendition,
        ImageLoadLimits limits,
        Func<long, IDisposable>? reserveTemporary
    )
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Rendition = rendition ?? throw new ArgumentNullException(nameof(rendition));
        Limits = limits ?? throw new ArgumentNullException(nameof(limits));
        _reserveTemporary = reserveTemporary;
    }

    /// <summary>Gets the source to prepare.</summary>
    public ImageSource Source { get; }

    /// <summary>Gets the requested output rendition.</summary>
    public ImageRendition Rendition { get; }

    /// <summary>Gets the limits that apply to this preparation.</summary>
    public ImageLoadLimits Limits { get; }

    /// <summary>Reserves temporary codec or native memory until the returned lease is disposed.</summary>
    public IDisposable ReserveTemporaryBytes(long byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteCount);
        if (byteCount > Limits.MaximumTemporaryBytes)
            throw new ImageLoadException(
                ImageLoadFailureKind.BudgetDeclined,
                "The requested temporary image memory exceeds the configured budget."
            );
        return _reserveTemporary?.Invoke(byteCount) ?? TemporaryReservation.Empty;
    }

    private sealed class TemporaryReservation : IDisposable
    {
        public static TemporaryReservation Empty { get; } = new();

        public void Dispose() { }
    }
}

/// <summary>Prepares portable image resources outside layout and rendering.</summary>
public interface IImagePreparer
{
    /// <summary>Prepares an image resource for the requested rendition.</summary>
    ValueTask<PreparedImage> PrepareAsync(
        ImagePreparationRequest request,
        CancellationToken cancellationToken
    );
}
