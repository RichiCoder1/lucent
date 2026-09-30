using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Lucent.Tools;

public sealed record FeedDoctorRequest(
    string Workspace,
    string PackageId,
    string Version,
    bool Online,
    string Generation
);

public sealed record FeedSourceObservation(
    int Source,
    string Selection,
    string Kind,
    string Reachability,
    string Authentication,
    string Release,
    string Reason
);

public sealed record FeedDoctorReport(
    int SchemaVersion,
    string Kind,
    string Scope,
    string Generation,
    string Status,
    IReadOnlyList<FeedSourceObservation> Sources,
    string Reason,
    string AuthenticationPolicy = "anonymous-no-credentials",
    string RestoreReadiness = "notChecked",
    string PrivateAvailability = "notChecked",
    string ConfiguredAuthentication = "notChecked",
    string ConfigurationScope = "windows-local-fixed"
);

public sealed record FeedDoctorProcessResult(
    int ExitCode,
    string Output,
    bool TerminationConfirmed = true
);

public interface IFeedDoctorProbe
{
    Task<FeedDoctorProcessResult> RunAsync(
        FeedDoctorRequest request,
        CancellationToken cancellationToken
    );
}

/// <summary>Explicit BCL-only client for the separately packaged NuGet observation payload.</summary>
public static class FeedDoctorClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] ReportKeys =
    [
        "schemaVersion",
        "kind",
        "scope",
        "generation",
        "status",
        "sources",
        "reason",
        "authenticationPolicy",
        "restoreReadiness",
        "privateAvailability",
        "configuredAuthentication",
        "configurationScope",
    ];
    private static readonly string[] SourceKeys =
    [
        "source",
        "selection",
        "kind",
        "reachability",
        "authentication",
        "release",
        "reason",
    ];
    internal static string PayloadPath =>
        Path.Combine(AppContext.BaseDirectory, "nuget", "Lucent.Tools.NuGet.dll");

    public static async Task<FeedDoctorReport> RunAsync(
        FeedDoctorRequest request,
        IFeedDoctorProbe? probe = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidRequest(request))
            return Failure(request, "invalid-request", "invalid-request");
        try
        {
            var result = await (probe ?? new ProcessProbe())
                .RunAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!result.TerminationConfirmed)
                return Failure(request);
            cancellationToken.ThrowIfCancellationRequested();
            return Decode(result, request) ?? Failure(request);
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or Win32Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure(request);
        }
    }

    internal static bool ValidPackageId(string? value) =>
        value is { Length: > 0 and <= 100 }
        && char.IsAsciiLetterOrDigit(value[0])
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');

    internal static bool ValidVersion(string? value) =>
        value is { Length: > 0 and <= 128 }
        && char.IsAsciiDigit(value[0])
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+');

    private static bool ValidGeneration(string? value) =>
        value is { Length: > 0 and <= 128 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private static bool ValidRequest(FeedDoctorRequest request) =>
        request is not null
        && request.Workspace is { Length: > 0 and <= 2048 }
        && Path.IsPathFullyQualified(request.Workspace)
        && Directory.Exists(request.Workspace)
        && ValidPackageId(request.PackageId)
        && ValidVersion(request.Version)
        && ValidGeneration(request.Generation);

    internal static FeedDoctorReport Failure(
        FeedDoctorRequest request,
        string status = "unavailable",
        string reason = "invocation-failed"
    ) =>
        new(
            1,
            "nuget-feed-doctor",
            request.Online ? "online-observation" : "effective-configuration",
            ValidGeneration(request.Generation) ? request.Generation : "",
            status,
            [],
            reason
        );

    internal static FeedDoctorReport? Decode(
        FeedDoctorProcessResult result,
        FeedDoctorRequest request
    )
    {
        if (result.Output.Length > 64 * 1024)
            return null;
        try
        {
            using var document = JsonDocument.Parse(
                result.Output,
                new JsonDocumentOptions { MaxDepth = 8 }
            );
            var value = document.RootElement;
            if (
                !Keys(value, ReportKeys)
                || value.GetProperty("schemaVersion").ValueKind != JsonValueKind.Number
                || !value.GetProperty("schemaVersion").TryGetInt32(out var schema)
                || schema != 1
                || Text(value, "kind") != "nuget-feed-doctor"
                || Text(value, "scope")
                    != (request.Online ? "online-observation" : "effective-configuration")
                || Text(value, "generation") != request.Generation
                || Text(value, "authenticationPolicy") != "anonymous-no-credentials"
                || Text(value, "restoreReadiness") != "notChecked"
                || Text(value, "privateAvailability") != "notChecked"
                || Text(value, "configuredAuthentication") != "notChecked"
                || Text(value, "configurationScope") != "windows-local-fixed"
            )
                return null;
            var status = Text(value, "status");
            var reason = Text(value, "reason");
            if (
                !OneOf(
                    status,
                    "observed",
                    "invalid-request",
                    "configuration-unavailable",
                    "stale",
                    "timed-out",
                    "unsupported-host",
                    "unavailable"
                )
                || !OneOf(
                    reason,
                    "none",
                    "invalid-request",
                    "configuration-load",
                    "unsupported-defaults",
                    "unsupported-host",
                    "stale-inputs",
                    "deadline",
                    "cancelled",
                    "invocation-failed"
                )
                || result.ExitCode != (status == "observed" ? 0 : 1)
            )
                return null;
            var sources = value.GetProperty("sources");
            if (
                sources.ValueKind != JsonValueKind.Array
                || sources.GetArrayLength() > 32
                || (status != "observed" && sources.GetArrayLength() != 0)
                || (status == "observed" && reason != "none")
            )
                return null;
            var observations = new List<FeedSourceObservation>();
            foreach (var source in sources.EnumerateArray())
            {
                if (
                    !Keys(source, SourceKeys)
                    || source.GetProperty("source").ValueKind != JsonValueKind.Number
                    || !source.GetProperty("source").TryGetInt32(out var ordinal)
                    || ordinal != observations.Count + 1
                )
                    return null;
                var selection = Text(source, "selection");
                var kind = Text(source, "kind");
                var reachability = Text(source, "reachability");
                var authentication = Text(source, "authentication");
                var release = Text(source, "release");
                var sourceReason = Text(source, "reason");
                if (
                    !OneOf(selection, "eligible", "disabled", "mapping-excluded")
                    || !OneOf(kind, "http", "local")
                    || !OneOf(
                        reachability,
                        "notChecked",
                        "reachable",
                        "unreachable",
                        "inconclusive",
                        "timed-out",
                        "unsupported"
                    )
                    || !OneOf(
                        authentication,
                        "notChecked",
                        "notExercised",
                        "unknown",
                        "authRequired",
                        "forbidden"
                    )
                    || !OneOf(
                        release,
                        "notChecked",
                        "available",
                        "notFoundInAnonymousView",
                        "unknown"
                    )
                    || !OneOf(
                        sourceReason,
                        "none",
                        "unsupported-source",
                        "unsupported-resource",
                        "unsupported-redirect",
                        "unreachable",
                        "invalid-response",
                        "deadline",
                        "http-status"
                    )
                    || (
                        (!request.Online || selection != "eligible")
                        && (
                            reachability != "notChecked"
                            || authentication != "notChecked"
                            || release != "notChecked"
                            || sourceReason != "none"
                        )
                    )
                )
                    return null;
                observations.Add(
                    new(
                        ordinal,
                        selection!,
                        kind!,
                        reachability!,
                        authentication!,
                        release!,
                        sourceReason!
                    )
                );
            }
            return new(
                1,
                "nuget-feed-doctor",
                Text(value, "scope")!,
                request.Generation,
                status!,
                observations,
                reason!
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Keys(JsonElement value, string[] keys) =>
        value.ValueKind == JsonValueKind.Object
        && value.EnumerateObject().Count() == keys.Length
        && keys.All(key => value.TryGetProperty(key, out _));

    private static string? Text(JsonElement value, string key) =>
        value.GetProperty(key).ValueKind == JsonValueKind.String
            ? value.GetProperty(key).GetString()
            : null;

    private static bool OneOf(string? value, params string[] choices) =>
        value is not null && choices.Contains(value, StringComparer.Ordinal);

    private sealed class ProcessProbe : IFeedDoctorProbe
    {
        public async Task<FeedDoctorProcessResult> RunAsync(
            FeedDoctorRequest request,
            CancellationToken cancellationToken
        )
        {
            if (!File.Exists(PayloadPath) || !File.Exists(DotnetProcessProbe.HostPath))
                return new(1, "");
            var input = JsonSerializer.Serialize(request, JsonOptions);
            if (input.Length > 16 * 1024)
                return new(1, "");
            var start = new ProcessStartInfo(DotnetProcessProbe.HostPath)
            {
                WorkingDirectory = Path.GetTempPath(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            start.ArgumentList.Add(PayloadPath);
            start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(65));
            using var process = Process.Start(start) ?? throw new IOException();
            var output = ReadAsync(process.StandardOutput, deadline, 64 * 1024);
            var errors = ReadAsync(process.StandardError, deadline, 16 * 1024);
            FeedDoctorProcessResult? result = null;
            var cleanupConfirmed = true;
            try
            {
                await process
                    .StandardInput.WriteAsync(input.AsMemory(), deadline.Token)
                    .ConfigureAwait(false);
                process.StandardInput.Close();
                await Task.WhenAll(output, errors, process.WaitForExitAsync(deadline.Token))
                    .WaitAsync(deadline.Token)
                    .ConfigureAwait(false);
                result = new(process.ExitCode, await output.ConfigureAwait(false));
            }
            catch (Exception error)
                when (error
                        is OperationCanceledException
                            or IOException
                            or Win32Exception
                            or InvalidOperationException
                ) { }
            finally
            {
                deadline.Cancel();
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited) { }
                catch (Win32Exception)
                {
                    cleanupConfirmed = false;
                }
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
                    await Task.WhenAll(output, errors)
                        .WaitAsync(cleanup.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited || !output.IsCompleted || !errors.IsCompleted)
                        cleanupConfirmed = false;
                }
                catch (IOException) when (process.HasExited) { }
            }
            if (!cleanupConfirmed)
                return new(1, "", false);
            cancellationToken.ThrowIfCancellationRequested();
            return result ?? new(1, "");
        }

        private static async Task<string> ReadAsync(
            StreamReader reader,
            CancellationTokenSource deadline,
            int limit
        )
        {
            try
            {
                return await DotnetProcessProbe
                    .ReadBoundedAsync(reader, deadline.Token, limit)
                    .ConfigureAwait(false);
            }
            catch (IOException)
            {
                await deadline.CancelAsync().ConfigureAwait(false);
                throw;
            }
        }
    }
}
