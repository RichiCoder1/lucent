using Lucent.Preview.Protocol;

namespace Lucent.Preview.Hosting;

internal sealed record PreviewSessionIdentity(
    string SessionId,
    string Generation,
    string RequestId,
    string ProjectTargetDigest,
    string InputDigest,
    string ArtifactDigest,
    string ScenarioId,
    string PresentationId
)
{
    internal static PreviewSessionIdentity From(PreviewWorkerRequest request) =>
        new(
            request.SessionId,
            request.Generation,
            request.RequestId,
            request.ProjectTargetDigest,
            request.InputDigest,
            request.ArtifactDigest,
            request.ScenarioId,
            request.PresentationId
        );
}

internal sealed record PreviewFrameToken(PreviewSessionIdentity Session, long Sequence);

internal sealed record PreviewRenderedFrame(
    PreviewFrameToken Token,
    long SceneGeneration,
    byte[] Png,
    int Width,
    int Height
);

internal readonly record struct PreviewFrameAcknowledgment(
    bool Accepted,
    bool PresentationAcknowledged
);

internal sealed record PreviewRenderDiagnostics(
    long Frames,
    long LoopTurns,
    int OwnerThread,
    int ActivePointers,
    int PressedKeys,
    int SurfaceBytes,
    long TextBlobCreations,
    int LiveTextBlobs,
    bool ResourcesDisposed
);
