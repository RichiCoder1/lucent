using System.IO.Compression;
using System.Text.Json;

namespace Lucent.Tooling.Cache;

public sealed record CompleteReleaseRequest
{
    public required string ArchivePath { get; init; }
    public required string StagingRoot { get; init; }
    public required long ArtifactBytes { get; init; }
    public required string ArtifactDigest { get; init; }
    public required string DescriptorSha256 { get; init; }
    public required string ReleaseVersion { get; init; }
    public required string SourceCommit { get; init; }
    public required string SdkPackageSha256 { get; init; }
    public required long RunId { get; init; }
    public required long RunAttempt { get; init; }
    public required ApprovedServer ExpectedServer { get; init; }
}

public sealed record ExtractedRelease(string StageDirectory, string ServerArchivePath);

// Authentication of the catalog and GitHub metadata belongs to the extension.
// This class binds downloaded bytes to that approved entry before extracting one ZIP.
public static class CompleteReleaseArchive
{
    private const long MaxOuterBytes = 512L * 1024 * 1024;
    private const long MaxExpandedBytes = 1024L * 1024 * 1024;
    private const int MaxEntries = 1_000;
    private const int MaxDescriptorBytes = 2 * 1024 * 1024;

    public static ExtractedRelease Extract(
        CompleteReleaseRequest request,
        CancellationToken cancellationToken
    )
    {
        ValidateRequest(request);
        using var stream = new FileStream(
            request.ArchivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );
        if (stream.Length != request.ArtifactBytes)
            throw new CacheException(
                "outer_size",
                "Complete release archive size differs from GitHub metadata."
            );
        if ($"sha256:{ServerArchive.Sha256(stream, cancellationToken)}" != request.ArtifactDigest)
            throw new CacheException(
                "outer_digest",
                "Complete release archive digest differs from the approved artifact."
            );
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count is 0 or > MaxEntries)
            throw new CacheException(
                "outer_entries",
                "Complete release archive has an unsupported entry count."
            );
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ServerArchive.ValidateName(entry.FullName);
            var kind = (entry.ExternalAttributes >> 16) & 0xf000;
            if (
                entry.FullName.EndsWith('/')
                || kind is not (0 or 0x8000)
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0
                || !names.Add(entry.FullName)
                || entry.Length <= 0
                || entry.Length > MaxOuterBytes
            )
                throw new CacheException(
                    "outer_entry",
                    "Complete release archive contains an unsafe or duplicate entry."
                );
            expanded = checked(expanded + entry.Length);
            if (expanded > MaxExpandedBytes)
                throw new CacheException(
                    "outer_expansion",
                    "Complete release archive exceeds its expansion limit."
                );
        }
        if (!names.Contains("complete.json"))
            throw new CacheException("outer_descriptor", "Complete release descriptor is missing.");
        var descriptorEntry = archive.GetEntry("complete.json")!;
        if (descriptorEntry.Length > MaxDescriptorBytes)
            throw new CacheException(
                "outer_descriptor",
                "Complete release descriptor is too large."
            );
        byte[] descriptorBytes;
        using (var input = descriptorEntry.Open())
        using (var output = new MemoryStream())
        {
            CopyBounded(input, output, descriptorEntry.Length, cancellationToken);
            descriptorBytes = output.ToArray();
        }
        var descriptorHash = Convert
            .ToHexString(System.Security.Cryptography.SHA256.HashData(descriptorBytes))
            .ToLowerInvariant();
        if (descriptorHash != request.DescriptorSha256)
            throw new CacheException(
                "outer_descriptor",
                "Complete release descriptor differs from the shipped catalog."
            );
        using var descriptor = JsonDocument.Parse(descriptorBytes);
        var serverName = ValidateDescriptor(descriptor.RootElement, request);
        var serverEntry =
            archive.GetEntry(serverName)
            ?? throw new CacheException("outer_server", "Complete release server ZIP is missing.");

        Directory.CreateDirectory(request.StagingRoot);
        if ((File.GetAttributes(request.StagingRoot) & FileAttributes.ReparsePoint) != 0)
            throw new CacheException("cache_path", "Acquisition staging root is a link.");
        var stage = Path.Combine(request.StagingRoot, "complete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var serverPath = Path.Combine(stage, "server.zip");
            using (var input = serverEntry.Open())
            using (
                var output = new FileStream(
                    serverPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None
                )
            )
            {
                CopyBounded(
                    input,
                    output,
                    request.ExpectedServer.Artifact!.Bytes,
                    cancellationToken
                );
                output.Flush(flushToDisk: true);
            }
            using (var server = File.OpenRead(serverPath))
            {
                if (
                    ServerArchive.Sha256(server, cancellationToken)
                    != request.ExpectedServer.Artifact!.Sha256
                )
                    throw new CacheException(
                        "server_hash",
                        "Complete release server ZIP differs from the approved server."
                    );
            }
            return new ExtractedRelease(stage, serverPath);
        }
        catch
        {
            Directory.Delete(stage, recursive: true);
            throw;
        }
    }

    private static string ValidateDescriptor(JsonElement descriptor, CompleteReleaseRequest request)
    {
        var release = descriptor.GetProperty("releaseSet");
        if (
            descriptor.GetProperty("schemaVersion").GetInt32() != 1
            || descriptor.GetProperty("status").GetString() != "complete"
            || release.GetProperty("sourceState").GetString() != "clean"
            || release.GetProperty("version").GetString() != request.ReleaseVersion
            || release.GetProperty("sourceCommit").GetString() != request.SourceCommit
        )
            throw new CacheException(
                "outer_descriptor",
                "Complete release identity differs from the approved catalog."
            );
        var provenance = descriptor.GetProperty("provenance");
        if (
            provenance.GetProperty("repository").GetString() != "RichiCoder1/lucent"
            || provenance.GetProperty("workflow").GetString() != ".github/workflows/tests.yml"
            || provenance.GetProperty("sourceCommit").GetString() != request.SourceCommit
            || provenance.GetProperty("runId").GetInt64() != request.RunId
            || provenance.GetProperty("runAttempt").GetInt64() != request.RunAttempt
        )
            throw new CacheException(
                "outer_descriptor",
                "Complete release provenance differs from the approved run."
            );
        var sdk = descriptor
            .GetProperty("packages")
            .EnumerateArray()
            .Where(package => package.GetProperty("id").GetString() == "Lucent.Lui.Sdk")
            .ToArray();
        if (
            sdk.Length != 1
            || sdk[0].GetProperty("version").GetString() != request.ReleaseVersion
            || sdk[0].GetProperty("repositoryCommit").GetString() != request.SourceCommit
            || sdk[0].GetProperty("artifact").GetProperty("sha256").GetString()
                != request.SdkPackageSha256
        )
            throw new CacheException(
                "outer_descriptor",
                "Complete release SDK differs from the approved catalog."
            );
        var server = descriptor.GetProperty("server");
        var artifact = server.GetProperty("artifact");
        var name = artifact.GetProperty("fileName").GetString() ?? "";
        ServerArchive.ValidateName(name);
        if (
            artifact.GetProperty("bytes").GetInt64() != request.ExpectedServer.Artifact!.Bytes
            || artifact.GetProperty("sha256").GetString() != request.ExpectedServer.Artifact.Sha256
            || server.GetProperty("identity").GetProperty("sourceCommit").GetString()
                != request.SourceCommit
            || !ServerArchive
                .CanonicalBytes(server.GetProperty("identity"))
                .AsSpan()
                .SequenceEqual(ServerArchive.CanonicalBytes(request.ExpectedServer.Identity))
        )
            throw new CacheException(
                "outer_descriptor",
                "Complete release server differs from the approved catalog."
            );
        return name;
    }

    private static void ValidateRequest(CompleteReleaseRequest request)
    {
        if (
            !Path.IsPathFullyQualified(request.ArchivePath)
            || !Path.IsPathFullyQualified(request.StagingRoot)
            || request.ArtifactBytes is <= 0 or > MaxOuterBytes
            || request.ArtifactDigest is not { Length: 71 }
            || !request.ArtifactDigest.StartsWith("sha256:", StringComparison.Ordinal)
            || !ServerArchive.IsSha256(request.ArtifactDigest[7..])
            || !ServerArchive.IsSha256(request.DescriptorSha256)
            || !ServerArchive.IsSha256(request.SdkPackageSha256)
            || request.SourceCommit is not { Length: 40 }
            || request.SourceCommit.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || request.RunId <= 0
            || request.RunAttempt <= 0
            || request.ExpectedServer.Artifact is null
            || request.ExpectedServer.Artifact.Bytes is <= 0 or > ServerArchive.MaxArchiveBytes
            || !ServerArchive.IsSha256(request.ExpectedServer.Artifact.Sha256)
        )
            throw new CacheException("request", "Approved complete release request is invalid.");
    }

    private static void CopyBounded(
        Stream input,
        Stream output,
        long approvedBytes,
        CancellationToken cancellationToken
    )
    {
        var buffer = new byte[64 * 1024];
        long copied = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = input.Read(buffer);
            if (read == 0)
                break;
            copied = checked(copied + read);
            if (copied > approvedBytes)
                throw new CacheException(
                    "outer_expansion",
                    "Complete release entry exceeds its approved length."
                );
            output.Write(buffer, 0, read);
        }
        if (copied != approvedBytes)
            throw new CacheException(
                "outer_expansion",
                "Complete release entry length differs from its approved length."
            );
    }
}
