using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Lucent.Tools;

public sealed record DoctorCheck(
    string Code,
    string Capability,
    string Status,
    string Summary,
    string? Evidence = null,
    string? Remedy = null,
    string Expected = "Observation only",
    string Scope = "static-offline"
)
{
    public string Severity =>
        Status switch
        {
            "fail" => "error",
            "notChecked" => "warning",
            _ => "info",
        };
}

public sealed record DoctorCapability(string Name, string Status);

public sealed record DoctorResult(
    int SchemaVersion,
    string Kind,
    string Scope,
    string Status,
    IReadOnlyList<DoctorCapability> Capabilities,
    IReadOnlyList<DoctorCheck> Checks
);

public sealed record DotnetProbeResult(bool Success, string Output);

public interface IDotnetProbe
{
    Task<DotnetProbeResult> RunAsync(
        string workingDirectory,
        string argument,
        CancellationToken cancellationToken
    );
}

public static class Doctor
{
    private const int MaximumConfigBytes = 1024 * 1024;
    private const string NotChecked = "notChecked";
    private static readonly string[] CapabilityNames = ["build", "editor", "restore", "native"];

    public static async Task<DoctorResult> RunAsync(
        string workspace,
        IDotnetProbe probe,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(probe);
        if (!Path.IsPathFullyQualified(workspace) || !Directory.Exists(workspace))
            throw new ArgumentException(
                "The workspace must be an existing absolute directory.",
                nameof(workspace)
            );
        workspace = Path.GetFullPath(workspace);
        var checks = new List<DoctorCheck>();

        var globalJson = NearestFile(workspace, "global.json");
        if (globalJson is null)
            checks.Add(new("global-json", "build", "pass", "No workspace SDK pin is present."));
        else
        {
            try
            {
                using var stream = File.OpenRead(globalJson);
                if (stream.Length > MaximumConfigBytes)
                    throw new JsonException("Configuration is too large.");
                if (stream.Length >= 3)
                {
                    var prefix = new byte[3];
                    stream.ReadExactly(prefix);
                    if (prefix[0] != 0xef || prefix[1] != 0xbb || prefix[2] != 0xbf)
                        stream.Position = 0;
                }
                using var document = JsonDocument.Parse(
                    stream,
                    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }
                );
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new JsonException("Configuration must be an object.");
                var hasSdk = root.TryGetProperty("sdk", out var sdk);
                if (hasSdk && sdk.ValueKind != JsonValueKind.Object)
                    throw new JsonException("SDK policy must be an object.");
                var version =
                    hasSdk && sdk.TryGetProperty("version", out var selected)
                        ? selected.GetString()
                        : null;
                var hasPaths = hasSdk && sdk.TryGetProperty("paths", out _);
                checks.Add(
                    new(
                        "global-json",
                        "build",
                        "pass",
                        "A global.json file is present.",
                        version is null
                            ? "No SDK version pin."
                            : $"Requested SDK {SafeVersion(version)}; SDK search paths {(hasPaths ? "declared" : "not declared")}.",
                        Expected: "Readable global.json configuration"
                    )
                );
            }
            catch (Exception error)
                when (error
                        is IOException
                            or UnauthorizedAccessException
                            or JsonException
                            or InvalidOperationException
                            or KeyNotFoundException
                )
            {
                checks.Add(
                    new(
                        "global-json",
                        "build",
                        "fail",
                        "The workspace SDK pin cannot be read or understood.",
                        "global.json",
                        "Correct global.json before building or starting semantic tooling.",
                        "Readable global.json configuration"
                    )
                );
            }
        }

        // Host inventory commands do not run an SDK selected by untrusted global.json.
        var neutralDirectory = Path.GetTempPath();
        var sdkResult = await probe
            .RunAsync(neutralDirectory, "--list-sdks", cancellationToken)
            .ConfigureAwait(false);
        var installedSdks = sdkResult.Success
            ? sdkResult
                .Output.Split('\n')
                .Select(line => line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(fields => fields.Length >= 2 && IsSafeVersion(fields[0]))
                .Select(fields => fields[0])
                .ToArray()
            : [];
        checks.Add(
            !sdkResult.Success
                ? new(
                    "dotnet-sdk",
                    "build",
                    "fail",
                    "The .NET SDK inventory could not be inspected.",
                    Remedy: "Retry the host inspection and check the .NET installation.",
                    Expected: "A successful local SDK inventory"
                )
            : installedSdks.Length > 0
                ? new(
                    "dotnet-sdk",
                    "build",
                    "pass",
                    "The .NET host reports installed SDKs.",
                    $"Installed SDK entries: {installedSdks.Length}",
                    Expected: "At least one installed .NET SDK"
                )
            : new(
                "dotnet-sdk",
                "build",
                "fail",
                "The .NET host reported no installed SDK.",
                Remedy: "Install an SDK compatible with the project's pin.",
                Expected: "At least one installed .NET SDK"
            )
        );
        checks.Add(
            new(
                "sdk-selection",
                "build",
                NotChecked,
                "The workspace-selected SDK was not executed or resolved.",
                Expected: "An SDK compatible with the workspace global.json policy"
            )
        );

        var runtimeResult = await probe
            .RunAsync(neutralDirectory, "--list-runtimes", cancellationToken)
            .ConfigureAwait(false);
        var hasRuntime =
            runtimeResult.Success
            && runtimeResult
                .Output.Split('\n')
                .Any(line =>
                {
                    var fields = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    return fields.Length >= 2
                        && fields[0] == "Microsoft.NETCore.App"
                        && Version.TryParse(fields[1], out var version)
                        && version.Major == 10;
                });
        checks.Add(
            !runtimeResult.Success
                ? new(
                    "dotnet-runtime",
                    "editor",
                    "fail",
                    "The .NET runtime inventory could not be inspected.",
                    Remedy: "Retry the host inspection and check the .NET installation.",
                    Expected: "A successful local runtime inventory"
                )
            : hasRuntime
                ? new(
                    "dotnet-runtime",
                    "editor",
                    "pass",
                    "A .NET 10 runtime is installed.",
                    Expected: "Microsoft.NETCore.App 10"
                )
            : new(
                "dotnet-runtime",
                "editor",
                "fail",
                "A .NET 10 runtime was not found.",
                Remedy: "Install the .NET 10 runtime on this host.",
                Expected: "Microsoft.NETCore.App 10"
            )
        );

        var nativePlatform =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && RuntimeInformation.ProcessArchitecture == Architecture.X64;
        checks.Add(
            nativePlatform
                ? new(
                    "native-platform",
                    "native",
                    "pass",
                    "This is a Windows x64 host.",
                    Expected: "Windows x64"
                )
                : new(
                    "native-platform",
                    "native",
                    "fail",
                    "Windows x64 native publication is unsupported on this host.",
                    Expected: "Windows x64"
                )
        );
        checks.Add(
            new(
                "native-prerequisites",
                "native",
                NotChecked,
                "The C++ linker and Windows SDK prerequisites were not checked.",
                Remedy: "Run an explicit NativeAOT publish when native publication is required.",
                Expected: "C++ linker and Windows SDK compatible with .NET 10 NativeAOT"
            )
        );

        checks.Add(InspectFeedNames(workspace));
        checks.Add(
            new(
                "feed-reachability",
                "restore",
                NotChecked,
                "Package feeds were not contacted.",
                Expected: "Reachable configured package sources"
            )
        );
        checks.Add(
            new(
                "project-requirements",
                "editor",
                NotChecked,
                "No project was evaluated or restored.",
                Expected: "Trusted evaluated project requirements"
            )
        );

        var capabilities = CapabilityNames
            .Select(name => new DoctorCapability(name, CapabilityStatus(checks, name)))
            .ToArray();
        var blocked = checks.Any(check =>
            check.Status == "fail" && check.Capability is "build" or "editor"
        );
        return new DoctorResult(
            1,
            "environment-doctor",
            "static-offline",
            blocked ? "blocked" : "available",
            capabilities,
            checks
        );
    }

    private static string CapabilityStatus(IEnumerable<DoctorCheck> checks, string capability)
    {
        var relevant = checks.Where(check => check.Capability == capability).ToArray();
        if (relevant.Any(check => check.Status == "fail"))
            return "blocked";
        return relevant.Any(check => check.Status == NotChecked) ? NotChecked : "available";
    }

    private static DoctorCheck InspectFeedNames(string workspace)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var configs = Ancestors(workspace)
            .SelectMany(directory =>
                new[]
                {
                    Path.Combine(directory, "NuGet.Config"),
                    Path.Combine(directory, "nuget.config"),
                }
            )
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToArray();
        try
        {
            foreach (var file in configs)
            {
                using var stream = File.OpenRead(file);
                if (stream.Length > MaximumConfigBytes)
                    throw new XmlException("Configuration is too large.");
                using var reader = XmlReader.Create(
                    stream,
                    new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null,
                        MaxCharactersInDocument = MaximumConfigBytes,
                    }
                );
                var document = XDocument.Load(reader);
                foreach (var source in document.Descendants("packageSources").Elements("add"))
                {
                    var name = source.Attribute("key")?.Value;
                    if (name is not null)
                        names.Add(SafeValue(name));
                }
            }
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or XmlException)
        {
            return new(
                "feed-config",
                "restore",
                NotChecked,
                "Workspace-ancestor NuGet source declarations could not be inspected safely."
            );
        }
        return configs.Length == 0
            ? new(
                "feed-config",
                "restore",
                NotChecked,
                "No workspace-ancestor NuGet configuration was found."
            )
            : new(
                "feed-config",
                "restore",
                "pass",
                "Observed workspace-ancestor package source declarations.",
                names.Count == 0 ? "No named declarations." : String.Join(", ", names.Take(64))
            );
    }

    private static string? NearestFile(string start, string name) =>
        Ancestors(start)
            .Select(directory => Path.Combine(directory, name))
            .FirstOrDefault(File.Exists);

    private static IEnumerable<string> Ancestors(string start)
    {
        for (var directory = start; ; directory = Path.GetDirectoryName(directory)!)
        {
            yield return directory;
            var parent = Path.GetDirectoryName(directory);
            if (parent is null || parent == directory)
                yield break;
        }
    }

    private static bool IsSafeVersion(string value) =>
        Regex.IsMatch(value, @"^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$");

    private static string SafeVersion(string value) =>
        Regex.IsMatch(
            value,
            @"^\d+\.\d+\.\d+(?:-(?:preview|rc)\.\d+(?:\.\d+)?)?$",
            RegexOptions.IgnoreCase
        )
            ? value
            : "[redacted]";

    private static string SafeValue(string? value)
    {
        if (
            String.IsNullOrWhiteSpace(value)
            || value.Length > 40
            || !Regex.IsMatch(value, @"^[A-Za-z][A-Za-z0-9._-]*$")
            || Regex.IsMatch(
                value,
                "secret|token|password|credential|apikey|bearer|auth|key",
                RegexOptions.IgnoreCase
            )
            || Regex.IsMatch(value, @"[A-Za-z0-9]{20,}|[0-9a-fA-F]{16,}")
        )
            return "[redacted]";
        return value;
    }
}
