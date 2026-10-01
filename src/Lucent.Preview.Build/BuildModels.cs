using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lucent.Preview.Build;

public sealed record PreviewBuildRequest(
    int ProtocolVersion,
    string SessionId,
    string Generation,
    string RequestId,
    string ProjectPath,
    string Configuration,
    string TargetFramework,
    string RuntimeIdentifier,
    string OutputDirectory,
    string[] ExtraInputs
);

public sealed record FileInput(string Path, string? Sha256);

public sealed record EvaluatedItem(
    string Kind,
    string Identity,
    SortedDictionary<string, string> Metadata
);

public sealed record EvaluatedGlob(
    string Kind,
    string Pattern,
    string WatchRoot,
    string[] Excludes,
    string[] Removes
);

public sealed record ProjectSnapshot(
    string ProjectPath,
    string NodeDigest,
    SortedDictionary<string, string> GlobalProperties,
    SortedDictionary<string, string> Properties,
    EvaluatedItem[] Items,
    EvaluatedGlob[] Globs,
    FileInput[] Inputs
);

public sealed record ConsumedSnapshot(
    string ProjectPath,
    string Stage,
    EvaluatedItem[] Items,
    FileInput[] Inputs
);

public sealed record ArtifactFile(string FileName, long ByteLength, string Sha256);

public sealed record PreviewBuildReport(
    int ProtocolVersion,
    string Kind,
    string Status,
    PreviewBuildRequest Request,
    string SdkPath,
    string DotnetPath,
    string ProjectTargetDigest,
    string InputDigest,
    string ArtifactDigest,
    string EntryPoint,
    string[] WatchDirectories,
    string[] GlobWatchRoots,
    ProjectSnapshot[] Projects,
    ConsumedSnapshot[] Consumption,
    ArtifactFile[] Artifacts
);

internal static class BuildData
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        MaxDepth = 32,
    };
    internal const int MaximumInputs = 8192;
    internal const long MaximumFileBytes = 256 * 1024 * 1024;

    internal static string CanonicalPath(string path)
    {
        var value = System.IO.Path.GetFullPath(path);
        return OperatingSystem.IsWindows() ? value.ToUpperInvariant() : value;
    }

    internal static string Digest<T>(T value) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Json)));

    internal static FileInput Snapshot(string path)
    {
        path = CanonicalPath(path);
        if (Directory.Exists(path))
            throw new InvalidOperationException(
                "Directory-valued preview inputs are unsupported; declare individual files."
            );
        if (!File.Exists(path))
            return new(path, null);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Reparse-point preview inputs are unsupported.");
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumFileBytes)
            throw new InvalidOperationException("A preview input exceeds the supported size.");
        return new(path, Convert.ToHexStringLower(SHA256.HashData(stream)));
    }

    internal static void RequireInside(string path, string directory)
    {
        var full = CanonicalPath(path);
        var root =
            CanonicalPath(directory).TrimEnd(System.IO.Path.DirectorySeparatorChar)
            + System.IO.Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "A preview output escaped its generation directory."
            );
        for (
            var current = System.IO.Path.GetDirectoryName(full);
            current is not null;
            current = System.IO.Path.GetDirectoryName(current)
        )
            if (
                Directory.Exists(current)
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0
            )
                throw new InvalidOperationException(
                    "Reparse-point preview output directories are unsupported."
                );
    }

    internal static string[] EnumerateOwnedFiles(string directory, int maximum)
    {
        var files = new List<string>();
        var pending = new Queue<string>();
        pending.Enqueue(directory);
        var directories = 0;
        while (pending.TryDequeue(out var current))
        {
            if (++directories > maximum)
                throw new InvalidOperationException(
                    "Preview output directories exceed their supported bound."
                );
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                RequireInside(entry, directory);
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException(
                        "Reparse-point preview outputs are unsupported."
                    );
                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Enqueue(entry);
                else
                {
                    if (files.Count >= maximum)
                        throw new InvalidOperationException(
                            "Preview output files exceed their supported bound."
                        );
                    files.Add(entry);
                }
            }
        }
        return files.ToArray();
    }

    internal static async Task WriteAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken
    )
    {
        var temporary = path + ".next";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(value, Json),
            cancellationToken
        );
        File.Move(temporary, path, overwrite: true);
    }
}
