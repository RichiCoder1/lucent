using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lucent.Tools;

public sealed record TrustedProjectTarget(
    string Status,
    string? Framework = null,
    string? RuntimeIdentifier = null
);

public sealed record TrustedProjectProtocol(string Id, int Major, int Minor);

public sealed record TrustedProjectLanguage(string Id, string Version, string FeatureLevel);

public sealed record TrustedProjectServer(
    string Sha256,
    string SourceCommit,
    string CompilerSha256,
    TrustedProjectProtocol Protocol,
    TrustedProjectLanguage Language
);

public sealed record TrustedProjectCompiler(string Sha256, string? SourceCommit);

public sealed record TrustedProjectResult(
    int SchemaVersion,
    string Kind,
    string Scope,
    string Status,
    string Evaluation,
    string ProjectState,
    string Requirements,
    string SemanticReadiness,
    string ManagedBuildReadiness,
    string NativeReadiness,
    string FeedAccess,
    TrustedProjectTarget Target,
    string Delivery,
    string ReleaseAuthentication,
    TrustedProjectServer Server,
    TrustedProjectCompiler Compiler
);

internal sealed class TrustedProjectFailure(string code)
    : IOException("The trusted project check could not establish current compatible requirements.")
{
    public string Code { get; } = code;
}

public static partial class TrustedProjectDoctor
{
    private const long MaximumFileBytes = 256 * 1024 * 1024;
    private const long MaximumTotalBytes = 512 * 1024 * 1024;
    private const int MaximumJsonCharacters = 2 * 1024 * 1024;

    public static async Task<TrustedProjectResult> RunAsync(
        string projectPath,
        string serverPath,
        ITrustedProjectProbe probe,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(probe);
        RequireFile(projectPath, ".csproj");
        RequireFile(serverPath, ".dll");
        projectPath = Path.GetFullPath(projectPath);
        serverPath = Path.GetFullPath(serverPath);
        var compilerPath = Path.Combine(
            Path.GetDirectoryName(serverPath)!,
            "Lucent.Lui.Compiler.dll"
        );
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(75));
        var token = deadline.Token;
        try
        {
            var serverHash = await HashFileAsync(serverPath, token).ConfigureAwait(false);
            var compilerHash = await HashFileAsync(compilerPath, token).ConfigureAwait(false);
            using var identityDocument = Parse(
                await probe.RunAsync(serverPath, null, token).ConfigureAwait(false)
            );
            var identity = identityDocument.RootElement;
            ValidateIdentity(identity, serverHash, compilerHash);
            if (
                await HashFileAsync(serverPath, token).ConfigureAwait(false) != serverHash
                || await HashFileAsync(compilerPath, token).ConfigureAwait(false) != compilerHash
            )
                throw new TrustedProjectFailure("tool-changed");
            using var requirementsDocument = Parse(
                await probe.RunAsync(serverPath, projectPath, token).ConfigureAwait(false)
            );
            var requirements = requirementsDocument.RootElement;
            var selected = ValidateRequirements(requirements, projectPath, identity);
            await VerifyInputsAsync(requirements.GetProperty("inputs"), projectPath, token)
                .ConfigureAwait(false);
            if (
                await HashFileAsync(serverPath, token).ConfigureAwait(false) != serverHash
                || await HashFileAsync(compilerPath, token).ConfigureAwait(false) != compilerHash
            )
                throw new TrustedProjectFailure("tool-changed");
            token.ThrowIfCancellationRequested();
            var target = selected.TryGetProperty("target", out var observed)
                ? new TrustedProjectTarget(
                    "observed",
                    Text(observed, "framework"),
                    NullableText(observed, "runtimeIdentifier")
                )
                : new TrustedProjectTarget("notChecked");
            return new(
                1,
                "trusted-project-doctor",
                "trusted-project",
                "available",
                "fresh",
                Text(requirements, "state"),
                "passed",
                "notChecked",
                "notChecked",
                "notChecked",
                "notChecked",
                target,
                "explicit-override",
                "notChecked",
                new(
                    serverHash,
                    Text(identity, "sourceCommit"),
                    compilerHash,
                    new(
                        "lucent-lui",
                        1,
                        identity.GetProperty("protocol").GetProperty("minor").GetInt32()
                    ),
                    new("lui", "preview", "preview-1")
                ),
                new(
                    compilerHash,
                    NullableText(requirements.GetProperty("compiler"), "sourceCommit")
                )
            );
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TrustedProjectFailure("project-timeout");
        }
        catch (Exception error)
            when (error
                    is JsonException
                        or InvalidOperationException
                        or KeyNotFoundException
                        or FormatException
            )
        {
            throw new TrustedProjectFailure("unsupported-result");
        }
        catch (Exception error)
            when (error is (IOException or UnauthorizedAccessException)
                && error is not TrustedProjectFailure
            )
        {
            throw new TrustedProjectFailure("project-unavailable");
        }
    }

    private static void RequireFile(string path, string extension)
    {
        if (
            !Path.IsPathFullyQualified(path)
            || !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(path)
        )
            throw new ArgumentException("An existing absolute file path is required.");
    }

    private static JsonDocument Parse(DotnetProbeResult result)
    {
        if (!result.Success)
            throw new TrustedProjectFailure("project-unavailable");
        if (result.Output.Length > MaximumJsonCharacters)
            throw new TrustedProjectFailure("unsupported-result");
        var document = JsonDocument.Parse(result.Output);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new TrustedProjectFailure("unsupported-result");
        }
        return document;
    }

    private static void ValidateIdentity(
        JsonElement identity,
        string serverHash,
        string compilerHash
    )
    {
        var protocol = identity.GetProperty("protocol");
        var language = identity.GetProperty("language");
        if (
            identity.GetProperty("schemaVersion").GetInt32() != 1
            || !CommitPattern().IsMatch(Text(identity, "sourceCommit"))
            || Text(identity.GetProperty("server"), "sha256") != serverHash
            || string.IsNullOrEmpty(Text(identity.GetProperty("server"), "informationalVersion"))
            || Text(identity.GetProperty("compiler"), "sha256") != compilerHash
            || string.IsNullOrEmpty(Text(identity.GetProperty("compiler"), "informationalVersion"))
            || Text(protocol, "id") != "lucent-lui"
            || protocol.GetProperty("major").GetInt32() != 1
            || protocol.GetProperty("minor").GetInt32() != 0
            || Text(language, "id") != "lui"
            || Text(language, "version") != "preview"
            || Text(language, "featureLevel") != "preview-1"
        )
            throw new TrustedProjectFailure("server-identity");
    }

    private static JsonElement ValidateRequirements(
        JsonElement value,
        string projectPath,
        JsonElement identity
    )
    {
        var state = Text(value, "state");
        var compiler = value.GetProperty("compiler");
        var projects = value.GetProperty("projects");
        var inputs = value.GetProperty("inputs");
        if (
            value.GetProperty("schemaVersion").GetInt32() != 1
            || Text(value, "kind") != "project-requirements"
            || state is not ("package" or "development-source")
            || value.GetProperty("semanticReady").ValueKind != JsonValueKind.False
            || !SamePath(Text(value, "projectPath"), projectPath)
            || !HashPattern().IsMatch(Text(compiler, "sha256"))
            || string.IsNullOrEmpty(Text(compiler, "informationalVersion"))
            || NullableText(compiler, "sourceCommit") is { } commit
                && !CommitPattern().IsMatch(commit)
            || value.GetProperty("packages").ValueKind != JsonValueKind.Array
            || projects.ValueKind != JsonValueKind.Array
            || projects.GetArrayLength() is < 1 or > 128
            || inputs.ValueKind != JsonValueKind.Array
            || inputs.GetArrayLength() is < 1 or > 8192
        )
            throw new TrustedProjectFailure("unsupported-result");
        if (
            Text(compiler, "sha256") != Text(identity.GetProperty("compiler"), "sha256")
            || Text(compiler, "informationalVersion")
                != Text(identity.GetProperty("compiler"), "informationalVersion")
            || NullableText(compiler, "sourceCommit") is { } sourceCommit
                && sourceCommit != Text(identity, "sourceCommit")
        )
            throw new TrustedProjectFailure("compiler-mismatch");
        JsonElement selected = default;
        var paths = new HashSet<string>(PathComparer);
        foreach (var project in projects.EnumerateArray())
        {
            var path = Text(project, "projectPath");
            if (
                !Path.IsPathFullyQualified(path)
                || !paths.Add(Path.GetFullPath(path))
                || Text(project, "state") != state
            )
                throw new TrustedProjectFailure("unsupported-result");
            if (
                project.TryGetProperty("target", out var target)
                && (
                    !TargetPattern().IsMatch(Text(target, "framework"))
                    || NullableText(target, "runtimeIdentifier") is { } rid
                        && !TargetPattern().IsMatch(rid)
                )
            )
                throw new TrustedProjectFailure("unsupported-target");
            if (state == "package")
            {
                var sdk = project.GetProperty("sdk");
                if (
                    Text(sdk, "id") != "Lucent.Lui.Sdk"
                    || string.IsNullOrEmpty(Text(sdk, "version"))
                    || !CommitPattern().IsMatch(Text(sdk, "repositoryCommit"))
                    || Text(sdk, "repositoryCommit") != NullableText(compiler, "sourceCommit")
                    || !HashPattern().IsMatch(Text(sdk, "packageSha256"))
                )
                    throw new TrustedProjectFailure("unsupported-result");
            }
            if (SamePath(path, projectPath))
                selected = project;
        }
        if (selected.ValueKind != JsonValueKind.Object)
            throw new TrustedProjectFailure("unsupported-result");
        return selected;
    }

    private static async Task VerifyInputsAsync(
        JsonElement inputs,
        string projectPath,
        CancellationToken token
    )
    {
        long total = 0;
        var names = new HashSet<string>(PathComparer);
        var selected = false;
        foreach (var input in inputs.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            var path = Text(input, "path");
            var expected = NullableText(input, "sha256");
            if (
                !Path.IsPathFullyQualified(path)
                || !names.Add(Path.GetFullPath(path))
                || expected is not null && !HashPattern().IsMatch(expected)
            )
                throw new TrustedProjectFailure("unsupported-result");
            if (expected is null)
            {
                try
                {
                    using var unexpected = File.OpenRead(path);
                    throw new TrustedProjectFailure("project-changed");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                continue;
            }
            using var stream = File.OpenRead(path);
            if (stream.Length > MaximumFileBytes || total + stream.Length > MaximumTotalBytes)
                throw new TrustedProjectFailure("project-too-large");
            var (actual, bytes) = await HashStreamAsync(
                    stream,
                    Math.Min(MaximumFileBytes, MaximumTotalBytes - total),
                    token
                )
                .ConfigureAwait(false);
            total += bytes;
            if (actual != expected)
                throw new TrustedProjectFailure("project-changed");
            if (SamePath(path, projectPath))
                selected = true;
        }
        if (!selected)
            throw new TrustedProjectFailure("unsupported-result");
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken token)
    {
        using var stream = File.OpenRead(path);
        return (await HashStreamAsync(stream, MaximumFileBytes, token).ConfigureAwait(false)).Hash;
    }

    private static async Task<(string Hash, long Bytes)> HashStreamAsync(
        Stream stream,
        long maximumBytes,
        CancellationToken token
    )
    {
        if (stream.Length > maximumBytes)
            throw new TrustedProjectFailure("project-too-large");
        var length = stream.Length;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long bytes = 0;
        while (true)
        {
            var count = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (count == 0)
                break;
            bytes += count;
            if (bytes > maximumBytes)
                throw new TrustedProjectFailure("project-too-large");
            hash.AppendData(buffer, 0, count);
        }
        if (bytes != length || stream.Length != length)
            throw new TrustedProjectFailure("project-changed");
        return (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), bytes);
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool SamePath(string left, string right) =>
        Path.IsPathFullyQualified(left)
        && PathComparer.Equals(Path.GetFullPath(left), Path.GetFullPath(right));

    private static string Text(JsonElement value, string name) =>
        value.GetProperty(name).GetString() ?? throw new JsonException();

    private static string? NullableText(JsonElement value, string name) =>
        value.GetProperty(name).GetString();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex HashPattern();

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex CommitPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")]
    private static partial Regex TargetPattern();
}
