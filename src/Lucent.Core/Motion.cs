namespace Lucent.Core;

/// <summary>Finite easing curves supported by implicit presentation transitions.</summary>
public enum Easing
{
    /// <summary>Changes at a constant rate.</summary>
    Linear,

    /// <summary>Decelerates using <c>1 - (1 - p)^3</c>.</summary>
    EaseOut,
}

/// <summary>Immutable finite motion policy for one eligible presentation property.</summary>
public readonly record struct Motion
{
    private const int MaximumDurationMilliseconds = 60_000;

    private Motion(int durationMilliseconds, Easing easing)
    {
        DurationMilliseconds = durationMilliseconds;
        Easing = easing;
    }

    /// <summary>Disables motion and presents a changed target immediately.</summary>
    public static Motion None => default;

    /// <summary>A short 120 millisecond cubic ease-out transition.</summary>
    public static Motion Quick => new(120, Easing.EaseOut);

    /// <summary>Creates a finite transition lasting zero through 60,000 milliseconds.</summary>
    public static Motion Duration(int milliseconds, Easing easing = Easing.Linear)
    {
        if (milliseconds is < 0 or > MaximumDurationMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        if (!Enum.IsDefined(easing))
            throw new ArgumentOutOfRangeException(nameof(easing));
        return new(milliseconds, easing);
    }

    /// <summary>Gets the finite duration. Zero has the same behavior as <see cref="None"/>.</summary>
    public int DurationMilliseconds { get; }

    /// <summary>Gets the easing curve.</summary>
    public Easing Easing { get; }

    internal double Apply(double progress) =>
        Easing == Easing.EaseOut ? 1d - Math.Pow(1d - progress, 3d) : progress;
}

/// <summary>Portable frame demand produced by the composition-owned presentation timeline.</summary>
public readonly record struct PresentationFrameDemand(
    bool IsActive,
    TimeSpan? NextDeadline,
    long Revision
);

/// <summary>Result of sampling existing presentation tracks at one absolute monotonic time.</summary>
public readonly record struct PresentationSampleResult(
    bool PixelsChanged,
    PresentationFrameDemand Demand
);

/// <summary>Bounded aggregate counters for the composition presentation timeline.</summary>
public readonly record struct PresentationDiagnostics(
    int ActiveTracks,
    long Starts,
    long Retargets,
    long SampledTracks,
    long Cancellations,
    long WakeRequests
);
