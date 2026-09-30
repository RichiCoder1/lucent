using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Lucent.Tooling.Cache;

internal static class ServerArchive
{
    internal const long MaxArchiveBytes = 512L * 1024 * 1024;
    private const long MaxFileBytes = 512L * 1024 * 1024;
    private const long MaxExpandedBytes = 1024L * 1024 * 1024;
    private const int MaxEntries = 10_000;
    private const int MaxJsonBytes = 2 * 1024 * 1024;

    public static string Sha256(Stream stream, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(buffer)) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static void Extract(
        string archivePath,
        string serverDirectory,
        ApprovedServer expected,
        CancellationToken cancellationToken
    )
    {
        using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );
        if (
            stream.Length <= 0
            || stream.Length > MaxArchiveBytes
            || stream.Length != expected.Artifact!.Bytes
        )
            throw new CacheException(
                "archive_size",
                "Server archive size differs from the approved release."
            );
        if (
            !StringComparer.Ordinal.Equals(
                Sha256(stream, cancellationToken),
                expected.Artifact.Sha256
            )
        )
            throw new CacheException(
                "archive_hash",
                "Server archive hash differs from the approved release."
            );
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaxEntries)
            throw new CacheException(
                "archive_entries",
                "Server archive entry count is outside the supported range."
            );
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = entry.FullName;
            ValidateName(name);
            var kind = (entry.ExternalAttributes >> 16) & 0xf000;
            if (
                name.EndsWith('/')
                || kind is not (0 or 0x8000)
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0
                || !names.Add(name)
            )
                throw new CacheException(
                    "archive_entry",
                    $"Unsupported or duplicate server archive entry: {name}"
                );
            if (entry.Length <= 0 || entry.Length > MaxFileBytes || entry.CompressedLength < 0)
                throw new CacheException(
                    "archive_entry",
                    $"Invalid server archive entry length: {name}"
                );
            expanded = checked(expanded + entry.Length);
            // Actual expanded bytes are counted while copying as well. A ratio cap would
            // reject valid highly compressible notices without tightening this ceiling.
            if (expanded > MaxExpandedBytes)
                throw new CacheException(
                    "archive_expansion",
                    "Server archive exceeds the expanded size limit."
                );
        }
        foreach (var name in names)
        {
            for (string? parent = Parent(name); parent is not null; parent = Parent(parent))
                if (names.Contains(parent))
                    throw new CacheException(
                        "archive_entry",
                        $"Server archive file is also a parent directory: {parent}"
                    );
        }

        Directory.CreateDirectory(serverDirectory);
        var root =
            Path.GetFullPath(serverDirectory).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(
                Path.Combine(
                    serverDirectory,
                    entry.FullName.Replace('/', Path.DirectorySeparatorChar)
                )
            );
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new CacheException("archive_path", "Server archive entry escapes staging.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = new FileStream(
                target,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None
            );
            var buffer = new byte[64 * 1024];
            long actual = 0;
            while (true)
            {
                var read = input.Read(buffer);
                if (read == 0)
                    break;
                actual = checked(actual + read);
                if (actual > entry.Length || actual > MaxFileBytes)
                    throw new CacheException(
                        "archive_expansion",
                        "Server archive entry expanded beyond its declared length."
                    );
                output.Write(buffer, 0, read);
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (actual != entry.Length)
                throw new CacheException(
                    "archive_expansion",
                    "Server archive entry has an invalid expanded length."
                );
        }
        ValidatePayload(serverDirectory, expected, cancellationToken);
    }

    public static void ValidatePayload(
        string serverDirectory,
        ApprovedServer expected,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = ReadJson(ResolveRegularFile(serverDirectory, "lucent-server-files.json"));
        if (manifest.GetProperty("schemaVersion").GetInt32() != 1)
            throw new CacheException("inventory", "Unsupported server inventory schema.");
        var manifestHash = Convert
            .ToHexString(SHA256.HashData(CanonicalBytes(manifest)))
            .ToLowerInvariant();
        if (!StringComparer.Ordinal.Equals(manifestHash, expected.FilesSha256))
            throw new CacheException(
                "inventory_hash",
                "Server inventory differs from the approved release."
            );
        var files = manifest.GetProperty("files");
        if (
            files.ValueKind != JsonValueKind.Array
            || files.GetArrayLength() == 0
            || files.GetArrayLength() > MaxEntries
        )
            throw new CacheException("inventory", "Server inventory entry count is invalid.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var file in files.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = file.GetProperty("fileName").GetString() ?? "";
            ValidateName(name);
            if (name == "lucent-server-files.json" || !names.Add(name))
                throw new CacheException(
                    "inventory",
                    "Server inventory contains a duplicate or reserved path."
                );
            var bytes = file.GetProperty("bytes").GetInt64();
            var hash = file.GetProperty("sha256").GetString() ?? "";
            if (bytes <= 0 || bytes > MaxFileBytes || !IsSha256(hash))
                throw new CacheException("inventory", $"Invalid server inventory entry: {name}");
            total = checked(total + bytes);
            if (total > MaxExpandedBytes)
                throw new CacheException(
                    "inventory",
                    "Server inventory exceeds the expanded size limit."
                );
            var path = ResolveRegularFile(serverDirectory, name);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (
                stream.Length != bytes
                || !StringComparer.Ordinal.Equals(Sha256(stream, cancellationToken), hash)
            )
                throw new CacheException(
                    "inventory_hash",
                    $"Server file differs from inventory: {name}"
                );
            hashes.Add(name, hash);
        }
        var actual = EnumerateRegularFiles(serverDirectory, cancellationToken);
        names.Add("lucent-server-files.json");
        if (!names.SetEquals(actual))
            throw new CacheException(
                "inventory",
                "Server directory contains missing or undeclared files."
            );
        foreach (var required in RequiredDeploymentFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!names.Contains(required))
                throw new CacheException(
                    "deployment",
                    $"Missing server deployment file: {required}"
                );
        }

        var identity = ReadJson(ResolveRegularFile(serverDirectory, "lucent-server.json"));
        ValidateIdentity(identity, expected.Identity, hashes);
        var runtime = ReadJson(
            ResolveRegularFile(serverDirectory, "Lucent.Lui.LanguageServer.runtimeconfig.json")
        );
        if (
            !CanonicalBytes(runtime.GetProperty("runtimeOptions"))
                .AsSpan()
                .SequenceEqual(CanonicalBytes(identity.GetProperty("runtime")))
        )
            throw new CacheException(
                "runtime",
                "Server runtime configuration differs from its identity."
            );
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDependencies(
            serverDirectory,
            "Lucent.Lui.LanguageServer.deps.json",
            cancellationToken
        );
        ValidateDependencies(
            serverDirectory,
            "BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.deps.json",
            cancellationToken
        );
    }

    internal static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static void ValidateName(string name)
    {
        if (
            string.IsNullOrWhiteSpace(name)
            || name.StartsWith('/')
            || name.Contains('\\')
            || name.Contains(':')
            || name.Any(char.IsControl)
            || name.Split('/').Any(IsUnsafeSegment)
        )
            throw new CacheException("archive_path", $"Unsafe server archive path: {name}");
    }

    private static bool IsUnsafeSegment(string part)
    {
        if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' '))
            return true;
        var stem = part.Split('.')[0];
        if (
            stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
        )
            return true;
        return stem.Length == 4
            && (
                stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)
            )
            && stem[3] is >= '1' and <= '9';
    }

    private static string? Parent(string name)
    {
        var slash = name.LastIndexOf('/');
        return slash < 0 ? null : name[..slash];
    }

    private static JsonElement ReadJson(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0 || file.Length > MaxJsonBytes)
            throw new CacheException("metadata", "Server metadata is missing or too large.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.Clone();
    }

    internal static byte[] CanonicalBytes(JsonElement element)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (
            var writer = new Utf8JsonWriter(
                buffer,
                new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
            )
        )
            WriteCanonical(writer, element);
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (
                    var property in element
                        .EnumerateObject()
                        .OrderBy(p => p.Name, StringComparer.Ordinal)
                )
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var child in element.EnumerateArray())
                    WriteCanonical(writer, child);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteNumberValue(element.GetInt64());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new CacheException("metadata", "Unsupported JSON value in server metadata.");
        }
    }

    private static string ResolveRegularFile(string root, string name)
    {
        var current = Path.GetFullPath(root);
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new CacheException("cache_path", "Server directory is a link.");
        foreach (var part in name.Split('/'))
        {
            current = Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new CacheException("cache_path", "Server file path contains a link.");
        }
        if (!File.Exists(current))
            throw new CacheException("inventory", $"Missing server file: {name}");
        return current;
    }

    private static HashSet<string> EnumerateRegularFiles(
        string root,
        CancellationToken cancellationToken
    )
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string directory)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new CacheException("cache_path", "Server directory contains a link.");
                if ((attributes & FileAttributes.Directory) != 0)
                    Visit(path);
                else if (!result.Add(Path.GetRelativePath(root, path).Replace('\\', '/')))
                    throw new CacheException(
                        "inventory",
                        "Server directory contains case-alias files."
                    );
            }
        }
        Visit(root);
        return result;
    }

    private static string[] RequiredDeploymentFiles()
    {
        using var stream =
            typeof(ServerArchive).Assembly.GetManifestResourceStream(
                "Lucent.Tooling.Cache.ReleasePolicy.json"
            )
            ?? throw new CacheException(
                "policy",
                "Server release policy is missing from the helper."
            );
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        return root.GetProperty("serverFiles")
            .EnumerateArray()
            .Concat(root.GetProperty("serverNotices").EnumerateArray())
            .Select(value => value.GetString()!)
            .ToArray();
    }

    private static void ValidateIdentity(
        JsonElement actual,
        JsonElement expected,
        Dictionary<string, string> hashes
    )
    {
        if (!CanonicalBytes(actual).AsSpan().SequenceEqual(CanonicalBytes(expected)))
            throw new CacheException(
                "identity",
                "Server archive identity differs from the approved release."
            );
        if (
            actual.GetProperty("schemaVersion").GetInt32() != 1
            || actual.GetProperty("runtime").GetProperty("tfm").GetString() != "net10.0"
            || actual
                .GetProperty("runtime")
                .GetProperty("framework")
                .GetProperty("name")
                .GetString() != "Microsoft.NETCore.App"
        )
            throw new CacheException("identity", "Unsupported server runtime identity.");
        var policy = ReadPolicy();
        if (
            !CanonicalBytes(actual.GetProperty("language"))
                .AsSpan()
                .SequenceEqual(CanonicalBytes(policy.GetProperty("language")))
            || !CanonicalBytes(actual.GetProperty("protocol"))
                .AsSpan()
                .SequenceEqual(CanonicalBytes(policy.GetProperty("protocol")))
        )
            throw new CacheException(
                "identity",
                "Unsupported server language or protocol identity."
            );
        foreach (
            var (field, name) in new[]
            {
                ("server", "Lucent.Lui.LanguageServer.dll"),
                ("compiler", "Lucent.Lui.Compiler.dll"),
            }
        )
            if (
                !hashes.TryGetValue(name, out var hash)
                || actual.GetProperty(field).GetProperty("sha256").GetString() != hash
            )
                throw new CacheException(
                    "identity",
                    $"Server assembly identity hash differs: {field}"
                );
    }

    private static JsonElement ReadPolicy()
    {
        using var stream =
            typeof(ServerArchive).Assembly.GetManifestResourceStream(
                "Lucent.Tooling.Cache.ReleasePolicy.json"
            )
            ?? throw new CacheException(
                "policy",
                "Server release policy is missing from the helper."
            );
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    private static void ValidateDependencies(
        string root,
        string relative,
        CancellationToken cancellationToken
    )
    {
        var deps = ReadJson(ResolveRegularFile(root, relative));
        var targetName = deps.GetProperty("runtimeTarget").GetProperty("name").GetString()!;
        var target = deps.GetProperty("targets").GetProperty(targetName);
        var prefix = Parent(relative) is { } directory ? directory + "/" : "";
        foreach (var library in target.EnumerateObject())
        foreach (var sectionName in new[] { "runtime", "native", "runtimeTargets" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!library.Value.TryGetProperty(sectionName, out var section))
                continue;
            foreach (var dependency in section.EnumerateObject())
            {
                if (dependency.Name.EndsWith("/_._", StringComparison.Ordinal))
                    continue;
                var name =
                    sectionName == "runtimeTargets"
                        ? dependency.Name
                        : Path.GetFileName(dependency.Name);
                ValidateName(name);
                _ = ResolveRegularFile(root, prefix + name);
            }
        }
    }
}
