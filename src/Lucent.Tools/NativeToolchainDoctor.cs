using System.Text.Json;

namespace Lucent.Tools;

public enum NativeToolchainProbeStatus
{
    Observed,
    UnsupportedHost,
    DiscoveryUnavailable,
    DiscoveryUnsupported,
    Failed,
}

public sealed record NativeToolchainProbeResult(
    NativeToolchainProbeStatus Status,
    string Instances = "",
    string CppInstances = "",
    string SdkInstances = ""
);

public interface INativeToolchainProbe
{
    Task<NativeToolchainProbeResult> RunAsync(CancellationToken cancellationToken);
}

/// <summary>Optional observations of Visual Studio registered Windows x64 prerequisites.</summary>
public static class NativeToolchainDoctor
{
    private const string Scope = "installed-windows-x64-toolchain";
    private const string Expected =
        "Visual Studio installer registration for Windows x64: Visual Studio 2022 or later with x64/x86 C++ tools and Windows SDK components";

    public static async Task<DoctorResult> RunAsync(
        INativeToolchainProbe probe,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(probe);
        cancellationToken.ThrowIfCancellationRequested();
        var observation = await probe.RunAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (observation.Status != NativeToolchainProbeStatus.Observed)
            return Unavailable(
                observation.Status switch
                {
                    NativeToolchainProbeStatus.UnsupportedHost =>
                        "Windows toolchain discovery is not applicable on this host.",
                    NativeToolchainProbeStatus.DiscoveryUnavailable =>
                        "The installed Visual Studio discovery tool is unavailable.",
                    NativeToolchainProbeStatus.DiscoveryUnsupported =>
                        "The installed discovery tool cannot perform the required component queries.",
                    _ => "Installed Visual Studio prerequisites could not be inspected.",
                }
            );
        try
        {
            var instances = Parse(observation.Instances);
            var cpp = Parse(observation.CppInstances);
            var sdk = Parse(observation.SdkInstances);
            if (!cpp.IsSubsetOf(instances) || !sdk.IsSubsetOf(instances))
                return Unavailable("Visual Studio component observations were inconsistent.");
            var combined = cpp.Intersect(sdk, StringComparer.Ordinal).Count();
            var checks = new DoctorCheck[]
            {
                Check(
                    "native-visual-studio",
                    instances.Count,
                    "eligible Visual Studio installations",
                    "Visual Studio 2022 or later"
                ),
                Check(
                    "native-cpp-x64",
                    cpp.Count,
                    "installations with registered x64/x86 C++ tools",
                    "Microsoft.VisualStudio.Component.VC.Tools.x86.x64"
                ),
                Check(
                    "native-windows-sdk",
                    sdk.Count,
                    "installations with registered Windows SDK components",
                    "Microsoft.VisualStudio.Component.Windows10SDK.* or Windows11SDK.*"
                ),
                new(
                    "native-prerequisites",
                    "native",
                    combined > 0 ? "pass" : "fail",
                    combined > 0
                        ? "Windows x64 native prerequisites were observed together in Visual Studio registration."
                        : "No eligible Visual Studio installation reports both required component families.",
                    "Matching installations: " + combined,
                    combined > 0
                        ? null
                        : "Review Visual Studio Desktop development with C++ and its default Windows SDK components.",
                    Expected,
                    Scope
                ),
                new(
                    "native-publish",
                    "native",
                    "notChecked",
                    "Actual NativeAOT publication remains unverified.",
                    "Registration observations do not verify component files, standalone SDKs, project compatibility or a successful publish.",
                    Expected: "A successful Windows x64 NativeAOT consumer publish and execution",
                    Scope: Scope
                ),
            };
            return new(
                1,
                "native-prerequisites-doctor",
                Scope,
                combined > 0 ? "available" : "blocked",
                [new("native", combined > 0 ? "observed" : "notChecked")],
                checks
            );
        }
        catch (JsonException)
        {
            return Unavailable("Visual Studio discovery returned an invalid inventory.");
        }
    }

    private static DoctorCheck Check(string code, int count, string label, string expected) =>
        new(
            code,
            "native",
            count > 0 ? "pass" : "fail",
            count > 0
                ? "Visual Studio registration reports " + label + "."
                : "Visual Studio registration reports no " + label + ".",
            "Matching installations: " + count,
            count > 0 ? null : "Review the installed Visual Studio C++ workload components.",
            expected,
            Scope
        );

    private static DoctorResult Unavailable(string summary) =>
        new(
            1,
            "native-prerequisites-doctor",
            Scope,
            "unavailable",
            [new("native", "notChecked")],
            [
                new(
                    "native-prerequisites",
                    "native",
                    "notChecked",
                    summary,
                    Remedy: "Review installed Visual Studio discovery and retry this explicit check.",
                    Expected: Expected,
                    Scope: Scope
                ),
            ]
        );

    private static HashSet<string> Parse(string text)
    {
        if (text.Length > 64 * 1024)
            throw new JsonException();
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
        if (
            document.RootElement.ValueKind != JsonValueKind.Array
            || document.RootElement.GetArrayLength() > 128
        )
            throw new JsonException();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var instance in document.RootElement.EnumerateArray())
        {
            if (
                instance.ValueKind != JsonValueKind.Object
                || !instance.TryGetProperty("instanceId", out var id)
                || id.ValueKind != JsonValueKind.String
                || id.GetString() is not { Length: > 0 and <= 128 } value
                || !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                || !instance.TryGetProperty("isComplete", out var complete)
                || complete.ValueKind != JsonValueKind.True
                || !instance.TryGetProperty("isLaunchable", out var launchable)
                || launchable.ValueKind != JsonValueKind.True
                || !instance.TryGetProperty("installationVersion", out var version)
                || version.ValueKind != JsonValueKind.String
                || !Version.TryParse(version.GetString(), out var parsed)
                || parsed.Major < 17
                || !ids.Add(value)
            )
                throw new JsonException();
        }
        return ids;
    }
}
