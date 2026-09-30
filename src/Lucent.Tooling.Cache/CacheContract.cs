using System.Text.Json;

namespace Lucent.Tooling.Cache;

public sealed record CacheRequest
{
    public int SchemaVersion { get; init; }
    public string? Operation { get; init; }
    public string? CacheRoot { get; init; }
    public string? ArchivePath { get; init; }
    public string? DotnetPath { get; init; }
    public ApprovedServer? Expected { get; init; }
    public CallerAnchor? Anchor { get; init; }
    public CompleteReleaseRequest? CompleteRelease { get; init; }
}

public sealed record ApprovedServer
{
    public ArtifactBytes? Artifact { get; init; }
    public string? FilesSha256 { get; init; }
    public JsonElement Identity { get; init; }
}

public sealed record ArtifactBytes
{
    public long Bytes { get; init; }
    public string? Sha256 { get; init; }
}

// The caller authenticated this anchor against the shipped catalog or GitHub.
// The helper checks its consistency with bytes; it does not authenticate GitHub.
public sealed record CallerAnchor
{
    public string? Kind { get; init; }
    public string? SourceCommit { get; init; }
    public string? DescriptorSha256 { get; init; }
    public long? RunId { get; init; }
    public long? RunAttempt { get; init; }
    public long? ArtifactId { get; init; }
    public string? ArtifactDigest { get; init; }
}

public sealed record CacheResult(
    int SchemaVersion,
    string Status,
    string ServerPath,
    string GenerationPath,
    string ArchiveSha256,
    string FilesSha256,
    JsonElement Identity
);

public sealed class CacheException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IServerIdentityRunner
{
    Task<JsonElement> IdentifyAsync(
        string dotnetPath,
        string serverPath,
        CancellationToken cancellationToken
    );
}
