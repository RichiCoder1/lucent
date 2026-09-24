using System.Security.Cryptography;
using System.Text.Json;

internal sealed record PublishedIdentity(
    string Status,
    string? Configuration,
    string? Execution,
    string? TargetFramework,
    string? RuntimeIdentifier,
    string? Sdk,
    string? SourceRevision,
    bool? SourceDirty,
    string? PublishedUtc,
    string? Error
)
{
    internal static PublishedIdentity Read(string app)
    {
        var path = app + ".benchmark.json";
        if (!File.Exists(path))
            return new("missing", null, null, null, null, null, null, null, null, null);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var version) || version.GetInt32() != 1)
                return Invalid("Unsupported publishing identity schema version.");
            var executable = RequiredString(root, "executable");
            if (
                !String.Equals(
                    executable,
                    Path.GetFileName(app),
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return Invalid("Publishing identity executable name does not match.");
            var expected = RequiredString(root, "appSha256");
            if (!String.Equals(expected, Hash(app), StringComparison.OrdinalIgnoreCase))
                return Invalid("Publishing identity app hash does not match.");
            if (
                !root.TryGetProperty("binaries", out var binaries)
                || binaries.ValueKind != JsonValueKind.Array
            )
                return Invalid("Publishing identity binary inventory is missing.");
            var foundApp = false;
            foreach (var binary in binaries.EnumerateArray())
            {
                var name = RequiredString(binary, "name");
                if (
                    name != Path.GetFileName(name)
                    || name.Contains(Path.DirectorySeparatorChar)
                    || name.Contains(Path.AltDirectorySeparatorChar)
                )
                    return Invalid("Publishing identity contains an invalid binary path.");
                var file = Path.Combine(Path.GetDirectoryName(app)!, name);
                if (
                    !String.Equals(
                        RequiredString(binary, "sha256"),
                        Hash(file),
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    return Invalid(
                        "Publishing identity binary inventory hash does not match: " + name
                    );
                if (String.Equals(name, executable, StringComparison.OrdinalIgnoreCase))
                    foundApp = true;
            }
            if (!foundApp)
                return Invalid("Publishing identity binary inventory omits the app executable.");
            return new(
                "verified",
                RequiredString(root, "configuration"),
                RequiredString(root, "execution"),
                RequiredString(root, "targetFramework"),
                RequiredString(root, "runtimeIdentifier"),
                RequiredString(root, "sdk"),
                RequiredString(root, "sourceRevision"),
                root.GetProperty("sourceDirty").GetBoolean(),
                RequiredString(root, "publishedUtc"),
                null
            );
        }
        catch (Exception error)
            when (error
                    is IOException
                        or UnauthorizedAccessException
                        or JsonException
                        or InvalidOperationException
                        or FormatException
                        or KeyNotFoundException
            )
        {
            return Invalid("Publishing identity could not be validated: " + error.Message);
        }
    }

    private static string RequiredString(JsonElement parent, string name) =>
        parent.GetProperty(name).GetString()
        ?? throw new FormatException("Missing publishing identity " + name + ".");

    private static string Hash(string path) =>
        File.Exists(path)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            : "missing";

    private static PublishedIdentity Invalid(string error) =>
        new("invalid", null, null, null, null, null, null, null, null, error);
}
