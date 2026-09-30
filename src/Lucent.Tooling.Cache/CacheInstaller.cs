using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Lucent.Tooling.Cache;

public sealed class CacheInstaller(IServerIdentityRunner identityRunner)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(15);

    public async Task<CacheResult> ExecuteAsync(
        CacheRequest request,
        CancellationToken cancellationToken
    )
    {
        ValidateRequest(request);
        var expected = request.Expected!;
        var sha = expected.Artifact!.Sha256!;
        var root = Path.GetFullPath(request.CacheRoot!);
        var generations = Path.Combine(root, "generations");
        var staging = Path.Combine(root, ".staging");
        var locks = Path.Combine(root, "locks");
        var generation = Path.Combine(generations, sha);
        if (request.Operation == "verify")
        {
            // Inspection must never create cache state, including on a cold host.
            foreach (var existing in new[] { root, generations })
                if (
                    !Directory.Exists(existing)
                    || (File.GetAttributes(existing) & FileAttributes.ReparsePoint) != 0
                )
                    throw new CacheException(
                        "cache_missing",
                        "The approved server generation is not installed."
                    );
            return await VerifyGenerationAsync(generation, request, cancellationToken);
        }
        EnsureDirectory(root);
        EnsureDirectory(generations);
        EnsureDirectory(staging);
        EnsureDirectory(locks);
        if (request.Operation == "install-release")
            return await InstallReleaseAsync(request, staging, cancellationToken);
        await using var held = await AcquireLockAsync(
            Path.Combine(locks, sha + ".lock"),
            cancellationToken
        );
        if (Directory.Exists(generation))
            return await VerifyGenerationAsync(generation, request, cancellationToken);
        var ownStage = Path.Combine(staging, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ownStage);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = Path.Combine(ownStage, "server");
            ServerArchive.Extract(request.ArchivePath!, payload, expected, cancellationToken);
            await CheckIdentityAsync(payload, request, cancellationToken);
            var receipt = new CacheReceipt(1, expected, request.Anchor!);
            var receiptPath = Path.Combine(ownStage, "receipt.json");
            await using (
                var receiptFile = new FileStream(
                    receiptPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None
                )
            )
            {
                await receiptFile.WriteAsync(
                    JsonSerializer.SerializeToUtf8Bytes(receipt, JsonOptions),
                    cancellationToken
                );
                receiptFile.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(generation))
                return await VerifyGenerationAsync(generation, request, cancellationToken);
            Directory.Move(ownStage, generation);
            return Result(generation, request);
        }
        finally
        {
            // Only this operation's random staging directory may be removed.
            var prefix =
                Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (
                Path.GetFullPath(ownStage).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(ownStage)
                && (File.GetAttributes(ownStage) & FileAttributes.ReparsePoint) == 0
            )
                Directory.Delete(ownStage, recursive: true);
        }
    }

    private async Task<CacheResult> InstallReleaseAsync(
        CacheRequest request,
        string staging,
        CancellationToken cancellationToken
    )
    {
        var release = request.CompleteRelease!;
        if (
            !Path.GetFullPath(release.StagingRoot)
                .Equals(Path.GetFullPath(staging), StringComparison.OrdinalIgnoreCase)
            || release.SourceCommit != request.Anchor!.SourceCommit
            || release.DescriptorSha256 != request.Anchor.DescriptorSha256
            || release.RunId != request.Anchor.RunId
            || release.RunAttempt != request.Anchor.RunAttempt
            || release.ArtifactDigest != request.Anchor.ArtifactDigest
            || release.ExpectedServer.Artifact?.Sha256 != request.Expected!.Artifact?.Sha256
            || release.ExpectedServer.Artifact?.Bytes != request.Expected.Artifact?.Bytes
            || release.ExpectedServer.FilesSha256 != request.Expected.FilesSha256
            || !ServerArchive
                .CanonicalBytes(release.ExpectedServer.Identity)
                .AsSpan()
                .SequenceEqual(ServerArchive.CanonicalBytes(request.Expected.Identity))
        )
            throw new CacheException(
                "request",
                "Complete release differs from the approved server request."
            );
        var extracted = CompleteReleaseArchive.Extract(release, cancellationToken);
        Exception? primaryError = null;
        try
        {
            return await ExecuteAsync(
                request with
                {
                    Operation = "install",
                    ArchivePath = extracted.ServerArchivePath,
                    CompleteRelease = null,
                },
                cancellationToken
            );
        }
        catch (Exception error)
        {
            primaryError = error;
            throw;
        }
        finally
        {
            try
            {
                var prefix =
                    Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (
                    Path.GetFullPath(extracted.StageDirectory)
                        .StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(extracted.StageDirectory)
                    && (File.GetAttributes(extracted.StageDirectory) & FileAttributes.ReparsePoint)
                        == 0
                )
                    Directory.Delete(extracted.StageDirectory, recursive: true);
            }
            catch when (primaryError is not null) { }
        }
    }

    private async Task<CacheResult> VerifyGenerationAsync(
        string generation,
        CacheRequest request,
        CancellationToken cancellationToken
    )
    {
        if (
            !Directory.Exists(generation)
            || (File.GetAttributes(generation) & FileAttributes.ReparsePoint) != 0
        )
            throw new CacheException(
                "cache_missing",
                "The approved server generation is not installed."
            );
        var receiptPath = Path.Combine(generation, "receipt.json");
        var receiptInfo = new FileInfo(receiptPath);
        if (
            !receiptInfo.Exists
            || receiptInfo.Length <= 0
            || receiptInfo.Length > 64 * 1024
            || (receiptInfo.Attributes & FileAttributes.ReparsePoint) != 0
        )
            throw new CacheException(
                "cache_receipt",
                "The installed server receipt is missing or invalid."
            );
        var receipt = JsonSerializer.Deserialize<CacheReceipt>(
            await File.ReadAllBytesAsync(receiptPath, cancellationToken),
            JsonOptions
        );
        if (
            receipt is null
            || receipt.SchemaVersion != 1
            || receipt.Expected?.Artifact is null
            || receipt.Anchor is null
            || receipt.Expected.Artifact.Bytes != request.Expected!.Artifact!.Bytes
            || receipt.Expected.Artifact.Sha256 != request.Expected.Artifact.Sha256
            || receipt.Expected.FilesSha256 != request.Expected.FilesSha256
            || !ServerArchive
                .CanonicalBytes(receipt.Expected.Identity)
                .AsSpan()
                .SequenceEqual(ServerArchive.CanonicalBytes(request.Expected.Identity))
            || receipt.Anchor.SourceCommit != request.Anchor!.SourceCommit
            || !ServerArchive.IsSha256(receipt.Anchor.DescriptorSha256)
        )
            throw new CacheException(
                "cache_receipt",
                "Installed server receipt differs from the approved release."
            );
        var payload = Path.Combine(generation, "server");
        ServerArchive.ValidatePayload(payload, request.Expected, cancellationToken);
        await CheckIdentityAsync(payload, request, cancellationToken);
        return Result(generation, request);
    }

    private async Task CheckIdentityAsync(
        string payload,
        CacheRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actual = await identityRunner.IdentifyAsync(
            request.DotnetPath!,
            Path.Combine(payload, "Lucent.Lui.LanguageServer.dll"),
            cancellationToken
        );
        if (
            !ServerArchive
                .CanonicalBytes(actual)
                .AsSpan()
                .SequenceEqual(ServerArchive.CanonicalBytes(request.Expected!.Identity))
        )
            throw new CacheException(
                "identity",
                "Project-free server identity differs from the approved release."
            );
    }

    private static CacheResult Result(string generation, CacheRequest request) =>
        new(
            1,
            "verified",
            Path.Combine(generation, "server", "Lucent.Lui.LanguageServer.dll"),
            generation,
            request.Expected!.Artifact!.Sha256!,
            request.Expected.FilesSha256!,
            request.Expected.Identity
        );

    private static async Task<FileStream> AcquireLockAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        var start = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
            }
            catch (IOException) when (start.Elapsed < LockTimeout)
            {
                await Task.Delay(100, cancellationToken);
            }
            catch (IOException error)
            {
                throw new CacheException(
                    "cache_lock",
                    $"Timed out waiting for the server installation lock: {error.Message}"
                );
            }
        }
    }

    private static void EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new CacheException("cache_path", "Cache directory is a link.");
    }

    private static void ValidateRequest(CacheRequest request)
    {
        if (
            request.SchemaVersion != 1
            || request.Operation is not ("install" or "verify" or "install-release")
            || string.IsNullOrWhiteSpace(request.CacheRoot)
            || !Path.IsPathFullyQualified(request.CacheRoot)
            || string.IsNullOrWhiteSpace(request.DotnetPath)
            || !Path.IsPathFullyQualified(request.DotnetPath)
            || !File.Exists(request.DotnetPath)
            || request.Operation == "install"
                && (
                    string.IsNullOrWhiteSpace(request.ArchivePath)
                    || !Path.IsPathFullyQualified(request.ArchivePath)
                    || !File.Exists(request.ArchivePath)
                )
            || request.Operation == "install-release" && request.CompleteRelease is null
        )
            throw new CacheException("request", "Cache request paths or operation are invalid.");
        var expected = request.Expected;
        var anchor = request.Anchor;
        if (
            expected?.Artifact is null
            || expected.Artifact.Bytes <= 0
            || expected.Artifact.Bytes > ServerArchive.MaxArchiveBytes
            || !ServerArchive.IsSha256(expected.Artifact.Sha256)
            || !ServerArchive.IsSha256(expected.FilesSha256)
            || expected.Identity.ValueKind != JsonValueKind.Object
            || anchor is null
            || anchor.Kind is not ("bundled-catalog" or "github-actions-receipt")
            || !ServerArchive.IsSha256(anchor.DescriptorSha256)
            || anchor.SourceCommit is not { Length: 40 }
            || anchor.SourceCommit.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || expected.Identity.GetProperty("sourceCommit").GetString() != anchor.SourceCommit
        )
            throw new CacheException(
                "request",
                "Approved server identity or caller anchor is invalid."
            );
    }

    private sealed record CacheReceipt(
        int SchemaVersion,
        ApprovedServer Expected,
        CallerAnchor Anchor
    );
}

public sealed class DotnetIdentityRunner : IServerIdentityRunner
{
    public async Task<JsonElement> IdentifyAsync(
        string dotnetPath,
        string serverPath,
        CancellationToken cancellationToken
    )
    {
        var start = new ProcessStartInfo(dotnetPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(serverPath);
        start.ArgumentList.Add("--identity");
        using var process =
            Process.Start(start)
            ?? throw new CacheException("identity", "The server identity process did not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var stdout = ReadBoundedAsync(process.StandardOutput, 64 * 1024, timeout.Token);
            var stderr = ReadBoundedAsync(process.StandardError, 64 * 1024, timeout.Token);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(timeout.Token));
            var output = await stdout;
            if (process.ExitCode != 0)
                throw new CacheException(
                    "identity",
                    "The project-free server identity check failed."
                );
            using var document = JsonDocument.Parse(output);
            return document.RootElement.Clone();
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        int maxChars,
        CancellationToken cancellationToken
    )
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
                return output.ToString();
            if (output.Length + read > maxChars)
                throw new CacheException(
                    "identity_output",
                    "Server identity output exceeds its size limit."
                );
            output.Append(buffer, 0, read);
        }
    }
}
