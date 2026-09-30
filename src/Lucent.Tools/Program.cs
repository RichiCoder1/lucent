using System.Text.Json;

namespace Lucent.Tools;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            return await RunAsync(
                    args,
                    Console.Out,
                    Console.Error,
                    cancellationToken: cancellation.Token
                )
                .ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    internal static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter errorOutput,
        IDotnetProbe? staticProbe = null,
        ITrustedProjectProbe? trustedProbe = null,
        INativeToolchainProbe? nativeProbe = null,
        IFeedDoctorProbe? feedProbe = null,
        CancellationToken cancellationToken = default
    )
    {
        var json = args.Contains("--json", StringComparer.Ordinal);
        var native = args.Contains("--native-prerequisites", StringComparer.Ordinal);
        var feed =
            args.Contains("--feed", StringComparer.Ordinal)
            || args.Contains("--version", StringComparer.Ordinal)
            || args.Contains("--online", StringComparer.Ordinal);
        var feedGeneration = Guid.NewGuid().ToString("N");
        var online = false;
        var trusted =
            args.Contains("--trusted-project", StringComparer.Ordinal)
            || args.Contains("--server", StringComparer.Ordinal);
        try
        {
            if (args.Length == 0 || args[0] != "doctor")
                throw new ArgumentException(
                    "Usage: lucent doctor [--json] [--workspace <absolute-directory>]"
                );
            var workspace = Directory.GetCurrentDirectory();
            string? project = null;
            string? server = null;
            string? packageId = null;
            string? packageVersion = null;
            var hasWorkspace = false;
            for (var index = 1; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--json":
                        break;
                    case "--native-prerequisites":
                        break;
                    case "--feed" when index + 1 < args.Length && packageId is null:
                        packageId = args[++index];
                        break;
                    case "--version" when index + 1 < args.Length && packageVersion is null:
                        packageVersion = args[++index];
                        break;
                    case "--online" when !online:
                        online = true;
                        break;
                    case "--workspace" when index + 1 < args.Length:
                        workspace = args[++index];
                        hasWorkspace = true;
                        break;
                    case "--trusted-project" when index + 1 < args.Length && project is null:
                        project = args[++index];
                        break;
                    case "--server" when index + 1 < args.Length && server is null:
                        server = args[++index];
                        break;
                    default:
                        throw new ArgumentException("Unsupported doctor option.");
                }
            }
            if (feed)
            {
                if (native || trusted || packageId is null || packageVersion is null)
                    throw new ArgumentException(
                        "Feed inspection requires a package ID and exact version and is exclusive of native and trusted project modes."
                    );
                var feedResult = await FeedDoctorClient
                    .RunAsync(
                        new(workspace, packageId, packageVersion, online, feedGeneration),
                        feedProbe,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                if (json)
                    output.WriteLine(JsonSerializer.Serialize(feedResult, JsonOptions));
                else
                    WriteFeed(feedResult, output);
                return feedResult.Status == "observed" ? 0 : 1;
            }
            if (native)
            {
                if (
                    trusted
                    || hasWorkspace
                    || args.Count(value => value == "--native-prerequisites") != 1
                )
                    throw new ArgumentException(
                        "Native prerequisite inspection is exclusive and accepts only --json."
                    );
                var nativeResult = await NativeToolchainDoctor
                    .RunAsync(nativeProbe ?? new NativeToolchainProcessProbe(), cancellationToken)
                    .ConfigureAwait(false);
                if (json)
                    output.WriteLine(JsonSerializer.Serialize(nativeResult, JsonOptions));
                else
                    WriteHuman(nativeResult, output);
                return nativeResult.Status switch
                {
                    "available" => 0,
                    "blocked" => 1,
                    _ => 2,
                };
            }
            if (trusted)
            {
                if (project is null || server is null || hasWorkspace)
                    throw new ArgumentException(
                        "Trusted project and server flags must be paired without --workspace."
                    );
                var report = await TrustedProjectDoctor
                    .RunAsync(
                        project,
                        server,
                        trustedProbe ?? new TrustedProjectProcessProbe(),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                if (json)
                    output.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                else
                {
                    output.WriteLine("Lucent trusted project: available (trusted-project)");
                    output.WriteLine("Requirements: passed (fresh evaluation)");
                    output.WriteLine(
                        "Delivery: explicit override; unauthenticated as a release artifact."
                    );
                    output.WriteLine(
                        report.Target.Status == "observed"
                            ? $"Target: {report.Target.Framework}; runtime identifier: {report.Target.RuntimeIdentifier ?? "none"}"
                            : "Target: notChecked"
                    );
                    output.WriteLine(
                        $"Server: {report.Server.Sha256}; compiler: {report.Compiler.Sha256}"
                    );
                    output.WriteLine(
                        "Semantic readiness, managed build, native publication and feed access: notChecked."
                    );
                }
                return 0;
            }
            var result = await Doctor
                .RunAsync(workspace, staticProbe ?? new DotnetProcessProbe(), cancellationToken)
                .ConfigureAwait(false);
            if (json)
                output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            else
                WriteHuman(result, output);
            return result.Status == "blocked" ? 1 : 0;
        }
        catch (Exception error)
            when (error
                    is ArgumentException
                        or IOException
                        or UnauthorizedAccessException
                        or OperationCanceledException
            )
        {
            if (feed)
            {
                var failure = FeedDoctorClient.Failure(
                    new("", "", "", online, feedGeneration),
                    error is ArgumentException ? "invalid-request" : "unavailable",
                    error is ArgumentException ? "invalid-request"
                        : error is OperationCanceledException ? "cancelled"
                        : "invocation-failed"
                );
                if (json)
                    output.WriteLine(JsonSerializer.Serialize(failure, JsonOptions));
                else
                    WriteFeed(failure, output);
                return 1;
            }
            if (json)
                output.WriteLine(
                    JsonSerializer.Serialize(
                        new
                        {
                            schemaVersion = 1,
                            kind = native ? "native-prerequisites-doctor"
                            : trusted ? "trusted-project-doctor"
                            : "environment-doctor",
                            scope = native ? "installed-windows-x64-toolchain"
                            : trusted ? "trusted-project"
                            : "static-offline",
                            status = "unavailable",
                            error = new
                            {
                                code = trusted && error is TrustedProjectFailure failure
                                    ? failure.Code
                                    : "doctor-invocation",
                                message = native
                                    ? "The native prerequisite check could not establish an installed Windows x64 toolchain observation."
                                : trusted
                                    ? "The trusted project check could not establish current compatible requirements."
                                : "The doctor could not inspect the requested workspace.",
                            },
                        },
                        JsonOptions
                    )
                );
            else
                errorOutput.WriteLine(
                    native
                        ? "The native prerequisite check is unavailable. Usage: lucent doctor --native-prerequisites [--json]"
                    : trusted
                        ? "The trusted project check could not establish current compatible requirements. Usage: lucent doctor --trusted-project <absolute.csproj> --server <absolute.server.dll> [--json]"
                    : "The doctor could not inspect the requested workspace. Usage: lucent doctor [--json] [--workspace <absolute-directory>]"
                );
            return trusted && error is TrustedProjectFailure ? 1 : 2;
        }
    }

    private static void WriteHuman(DoctorResult result, TextWriter writer)
    {
        writer.WriteLine(
            $"Lucent {(result.Kind == "native-prerequisites-doctor" ? "Windows x64 native prerequisites" : "environment")}: {result.Status} ({result.Scope})"
        );
        foreach (var capability in result.Capabilities)
            writer.WriteLine($"{capability.Name}: {capability.Status}");
        foreach (var check in result.Checks)
        {
            writer.WriteLine($"[{check.Status}] {check.Code}: {check.Summary}");
            if (check.Evidence is not null)
                writer.WriteLine("  " + check.Evidence);
            if (check.Remedy is not null)
                writer.WriteLine("  Next: " + check.Remedy);
        }
    }

    private static void WriteFeed(FeedDoctorReport result, TextWriter writer)
    {
        writer.WriteLine($"Lucent NuGet feed: {result.Status} ({result.Scope}); {result.Reason}");
        writer.WriteLine(
            "Anonymous observation only; configured authentication, private availability and restore readiness: notChecked."
        );
        foreach (var source in result.Sources)
            writer.WriteLine(
                $"Source {source.Source}: {source.Selection}; {source.Kind}; reachability={source.Reachability}; authentication={source.Authentication}; release={source.Release}; reason={source.Reason}"
            );
    }
}
