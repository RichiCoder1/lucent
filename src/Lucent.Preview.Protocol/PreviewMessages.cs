namespace Lucent.Preview.Protocol;

/// <summary>Correlated identity for one explicitly supervised worker operation.</summary>
public abstract record PreviewWorkerIdentity
{
    /// <summary>The bounded ProtocolVersion wire value.</summary>
    public required int ProtocolVersion { get; init; }

    /// <summary>The bounded Kind wire value.</summary>
    public required string Kind { get; init; }

    /// <summary>The bounded SessionId wire value.</summary>
    public required string SessionId { get; init; }

    /// <summary>The opaque generation string, never a number.</summary>
    public required string Generation { get; init; }

    /// <summary>The bounded RequestId wire value.</summary>
    public required string RequestId { get; init; }

    /// <summary>The bounded ProjectTargetDigest wire value.</summary>
    public required string ProjectTargetDigest { get; init; }

    /// <summary>The bounded InputDigest wire value.</summary>
    public required string InputDigest { get; init; }

    /// <summary>The bounded ArtifactDigest wire value.</summary>
    public required string ArtifactDigest { get; init; }
}

/// <summary>One fully resolved capture presentation.</summary>
public sealed record PreviewWorkerRequest : PreviewWorkerIdentity
{
    /// <summary>The bounded ScenarioId wire value.</summary>
    public required string ScenarioId { get; init; }

    /// <summary>The bounded PresentationId wire value.</summary>
    public required string PresentationId { get; init; }

    /// <summary>The bounded OutputDirectory wire value.</summary>
    public required string OutputDirectory { get; init; }

    /// <summary>The bounded LogicalWidth wire value.</summary>
    public required double LogicalWidth { get; init; }

    /// <summary>The bounded LogicalHeight wire value.</summary>
    public required double LogicalHeight { get; init; }

    /// <summary>The bounded Scale wire value.</summary>
    public required double Scale { get; init; }

    /// <summary>The bounded ColorScheme wire value.</summary>
    public required string ColorScheme { get; init; }

    /// <summary>The bounded Contrast wire value.</summary>
    public required string Contrast { get; init; }

    /// <summary>Explicit fixture density, independent of Core layout policy.</summary>
    public required double Density { get; init; }
}

/// <summary>One frame published after successful cleanup.</summary>
public sealed record PreviewWorkerResult : PreviewWorkerIdentity
{
    /// <summary>The bounded ScenarioId wire value.</summary>
    public required string ScenarioId { get; init; }

    /// <summary>The bounded PresentationId wire value.</summary>
    public required string PresentationId { get; init; }

    /// <summary>The bounded FrameSequence wire value.</summary>
    public required int FrameSequence { get; init; }

    /// <summary>The bounded FileName wire value.</summary>
    public required string FileName { get; init; }

    /// <summary>The bounded ByteLength wire value.</summary>
    public required int ByteLength { get; init; }

    /// <summary>The bounded Sha256 wire value.</summary>
    public required string Sha256 { get; init; }

    /// <summary>The bounded Width wire value.</summary>
    public required int Width { get; init; }

    /// <summary>The bounded Height wire value.</summary>
    public required int Height { get; init; }

    /// <summary>The bounded LogicalWidth wire value.</summary>
    public required double LogicalWidth { get; init; }

    /// <summary>The bounded LogicalHeight wire value.</summary>
    public required double LogicalHeight { get; init; }

    /// <summary>The bounded Scale wire value.</summary>
    public required double Scale { get; init; }

    /// <summary>The bounded ColorScheme wire value.</summary>
    public required string ColorScheme { get; init; }

    /// <summary>The bounded Contrast wire value.</summary>
    public required string Contrast { get; init; }

    /// <summary>Explicit fixture density, independent of Core layout policy.</summary>
    public required double Density { get; init; }

    /// <summary>The bounded Culture wire value.</summary>
    public required string Culture { get; init; }

    /// <summary>The bounded UICulture wire value.</summary>
    public required string UICulture { get; init; }

    /// <summary>The canonical UTC roundtrip instant, preserving seven fractional digits.</summary>
    public required string InitialTime { get; init; }
}

/// <summary>Inert catalog enumeration in the explicit development executable.</summary>
public sealed record PreviewCatalogRequest : PreviewWorkerIdentity
{
    /// <summary>The bounded OutputDirectory wire value.</summary>
    public required string OutputDirectory { get; init; }
}

/// <summary>One bounded registration-order catalog snapshot.</summary>
public sealed record PreviewCatalogResult : PreviewWorkerIdentity
{
    /// <summary>The owned bounded registration-order metadata copy.</summary>
    public required PreviewCatalogEntry[] Scenarios { get; init; }
}

/// <summary>Authored labels and effective immutable descriptor defaults; source labels are data only.</summary>
public sealed record PreviewCatalogEntry
{
    /// <summary>The bounded Id wire value.</summary>
    public required string Id { get; init; }

    /// <summary>The bounded Title wire value.</summary>
    public required string Title { get; init; }

    /// <summary>The inert authored project label; never a navigation capability.</summary>
    public required string SourceProject { get; init; }

    /// <summary>The inert authored document label; never a navigation capability.</summary>
    public required string SourceDocument { get; init; }

    /// <summary>The bounded SourceComponent wire value.</summary>
    public required string SourceComponent { get; init; }

    /// <summary>The bounded LogicalWidth wire value.</summary>
    public required double LogicalWidth { get; init; }

    /// <summary>The bounded LogicalHeight wire value.</summary>
    public required double LogicalHeight { get; init; }

    /// <summary>The bounded Scale wire value.</summary>
    public required double Scale { get; init; }

    /// <summary>The bounded ColorScheme wire value.</summary>
    public required string ColorScheme { get; init; }

    /// <summary>The bounded Contrast wire value.</summary>
    public required string Contrast { get; init; }

    /// <summary>Explicit fixture density, independent of Core layout policy.</summary>
    public required double Density { get; init; }

    /// <summary>The bounded Culture wire value.</summary>
    public required string Culture { get; init; }

    /// <summary>The bounded UICulture wire value.</summary>
    public required string UICulture { get; init; }

    /// <summary>The canonical UTC roundtrip instant, preserving seven fractional digits.</summary>
    public required string InitialTime { get; init; }
}
