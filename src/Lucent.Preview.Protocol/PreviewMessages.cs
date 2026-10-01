namespace Lucent.Preview.Protocol;

/// <summary>One explicit worker launch; digests correlate independently verified build inputs.</summary>
public sealed record PreviewWorkerRequest
{
    /// <summary>The exact supported wire version.</summary>
    public required int ProtocolVersion { get; init; }

    /// <summary>The owning editor session.</summary>
    public required string SessionId { get; init; }

    /// <summary>The opaque generation string, never a JSON number.</summary>
    public required string Generation { get; init; }

    /// <summary>The launch nonce; correlation does not authenticate author code.</summary>
    public required string RequestId { get; init; }

    /// <summary>The canonical selected project and effective global-properties digest.</summary>
    public required string ProjectTargetDigest { get; init; }

    /// <summary>The evaluated build input digest.</summary>
    public required string InputDigest { get; init; }

    /// <summary>The verified executable artifact closure digest.</summary>
    public required string ArtifactDigest { get; init; }

    /// <summary>The exact catalog scenario identifier.</summary>
    public required string ScenarioId { get; init; }

    /// <summary>The coordinator's opaque presentation selection identity.</summary>
    public required string PresentationId { get; init; }

    /// <summary>An absolute caller-owned local output directory.</summary>
    public required string OutputDirectory { get; init; }
}

/// <summary>One completed frame, published only after successful scenario and host cleanup.</summary>
public sealed record PreviewWorkerResult
{
    /// <summary>The exact supported wire version.</summary>
    public required int ProtocolVersion { get; init; }

    /// <summary>The owning editor session.</summary>
    public required string SessionId { get; init; }

    /// <summary>The opaque generation string.</summary>
    public required string Generation { get; init; }

    /// <summary>The launch nonce.</summary>
    public required string RequestId { get; init; }

    /// <summary>The selected project and target digest.</summary>
    public required string ProjectTargetDigest { get; init; }

    /// <summary>The evaluated build input digest.</summary>
    public required string InputDigest { get; init; }

    /// <summary>The executable artifact closure digest.</summary>
    public required string ArtifactDigest { get; init; }

    /// <summary>The captured catalog scenario.</summary>
    public required string ScenarioId { get; init; }

    /// <summary>The opaque presentation selection identity.</summary>
    public required string PresentationId { get; init; }

    /// <summary>The one-shot sequence, exactly one.</summary>
    public required int FrameSequence { get; init; }

    /// <summary>The exact relative filename, frame.png.</summary>
    public required string FileName { get; init; }

    /// <summary>The encoded PNG byte count.</summary>
    public required int ByteLength { get; init; }

    /// <summary>The encoded PNG SHA256 digest.</summary>
    public required string Sha256 { get; init; }

    /// <summary>The physical frame width.</summary>
    public required int Width { get; init; }

    /// <summary>The physical frame height.</summary>
    public required int Height { get; init; }

    /// <summary>The descriptor's logical viewport width.</summary>
    public required double LogicalWidth { get; init; }

    /// <summary>The descriptor's logical viewport height.</summary>
    public required double LogicalHeight { get; init; }

    /// <summary>The descriptor's physical scale.</summary>
    public required double Scale { get; init; }
}
