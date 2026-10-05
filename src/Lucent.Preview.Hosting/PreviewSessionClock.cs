namespace Lucent.Preview.Hosting;

/// <summary>Runs real timers while anchoring fixture UTC to its authored initial instant.</summary>
internal sealed class PreviewSessionClock(DateTimeOffset initialTime) : TimeProvider
{
    private readonly long _started = System.GetTimestamp();

    public override DateTimeOffset GetUtcNow() =>
        initialTime + System.GetElapsedTime(_started, System.GetTimestamp());

    public override long GetTimestamp() => System.GetTimestamp();

    public override long TimestampFrequency => System.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period
    ) => System.CreateTimer(callback, state, dueTime, period);
}
