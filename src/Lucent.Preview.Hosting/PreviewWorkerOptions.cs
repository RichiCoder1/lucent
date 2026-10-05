using Lucent.Testing;

namespace Lucent.Preview.Hosting;

/// <summary>Explicit development worker ownership and cooperative bounds.</summary>
public sealed class PreviewWorkerOptions
{
    /// <summary>The bounded-operation or live-startup deadline; the supervisor owns forced termination.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>The parent channel; stop or EOF cancels the launch.</summary>
    public TextReader ParentInput { get; init; } = Console.In;

    /// <summary>Explicit authored image preparation before capture, using the cancellation token.</summary>
    /// <remarks>Bounded capture only. Completion establishes author-declared readiness, not global application idleness.</remarks>
    public Func<
        HeadlessApplication,
        CancellationToken,
        ValueTask
    >? PrepareImagesAsync { get; init; }
}
