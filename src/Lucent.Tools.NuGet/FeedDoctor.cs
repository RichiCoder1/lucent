using System.Text.RegularExpressions;
using NuGet.Configuration;
using NuGet.Versioning;

namespace Lucent.Tools.NuGet;

public sealed record FeedRequest(
    string Workspace,
    string PackageId,
    string Version,
    bool Online,
    string Generation
);

public sealed record SourceObservation(
    int Source,
    string Selection,
    string Kind,
    string Reachability = "notChecked",
    string Authentication = "notChecked",
    string Release = "notChecked",
    string Reason = "none"
);

public sealed record FeedReport(
    int SchemaVersion,
    string Kind,
    string Scope,
    string Generation,
    string Status,
    IReadOnlyList<SourceObservation> Sources,
    string Reason = "none",
    string AuthenticationPolicy = "anonymous-no-credentials",
    string RestoreReadiness = "notChecked",
    string PrivateAvailability = "notChecked",
    string ConfiguredAuthentication = "notChecked",
    string ConfigurationScope = "windows-local-fixed"
);

public sealed partial class FeedDoctor
{
    private readonly Func<string, CancellationToken, PinnedNuGetSettings> _load;

    public FeedDoctor()
        : this(PinnedNuGetSettings.Open) { }

    internal FeedDoctor(Func<string, CancellationToken, PinnedNuGetSettings> load) => _load = load;

    public async Task<FeedReport> ObserveAsync(
        FeedRequest request,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (
            string.IsNullOrEmpty(request.Workspace)
            || request.Workspace.Length > 2048
            || !Path.IsPathFullyQualified(request.Workspace)
            || !Directory.Exists(request.Workspace)
            || string.IsNullOrEmpty(request.PackageId)
            || !PackageIdPattern().IsMatch(request.PackageId)
            || string.IsNullOrEmpty(request.Version)
            || request.Version.Length > 128
            || !NuGetVersion.TryParse(request.Version, out var version)
            || string.IsNullOrEmpty(request.Generation)
            || !GenerationPattern().IsMatch(request.Generation)
        )
            return Report(request, "invalid-request", [], "invalid-request");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            using var owned = _load(request.Workspace, deadline.Token);
            var settings = owned.Settings;
            // Provider.LoadPackageSources materializes credentials/certificates. Project
            // only the official parser's source data for this anonymous observation.
            var sources =
                settings
                    .GetSection(ConfigurationConstants.PackageSources)
                    ?.Items.OfType<SourceItem>()
                    .Select(item => new PackageSource(
                        item.GetValueAsPath(),
                        item.Key,
                        settings
                            .GetSection(ConfigurationConstants.DisabledPackageSources)
                            ?.GetFirstItemWithAttribute<AddItem>(
                                ConfigurationConstants.KeyAttribute,
                                item.Key
                            )
                            is null
                    )
                    {
                        AllowInsecureConnections =
                            bool.TryParse(item.AllowInsecureConnections, out var allow) && allow,
                    })
                    .ToArray()
                ?? [];
            if (sources.Length > 32)
                return Report(request, "configuration-unavailable", []);
            var mapping = PackageSourceMapping.GetPackageSourceMapping(settings);
            var allowed = mapping.GetConfiguredPackageSources(request.PackageId);
            var result = new List<SourceObservation>();
            if (!owned.IsCurrent(deadline.Token))
                return Report(request, "stale", []);
            for (var i = 0; i < sources.Length; i++)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var source = sources[i];
                var selection =
                    !source.IsEnabled ? "disabled"
                    : mapping.IsEnabled
                    && !allowed.Contains(source.Name, StringComparer.OrdinalIgnoreCase)
                        ? "mapping-excluded"
                    : "eligible";
                var kind = source.IsHttp ? "http" : "local";
                var observation = new SourceObservation(i + 1, selection, kind);
                if (request.Online && selection == "eligible")
                {
                    if (source.IsHttp)
                        observation = await FeedTransport
                            .ObserveAsync(
                                source,
                                request.PackageId,
                                version,
                                observation,
                                deadline.Token
                            )
                            .ConfigureAwait(false);
                    else
                        observation = observation with
                        {
                            Reachability = "unsupported",
                            Authentication = "unknown",
                            Release = "unknown",
                            Reason = "unsupported-source",
                        };
                }
                result.Add(observation);
            }
            if (!owned.IsCurrent(deadline.Token))
                return Report(request, "stale", []);
            cancellationToken.ThrowIfCancellationRequested();
            return Report(request, "observed", result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Report(request, "timed-out", [], "deadline");
        }
        catch (UnsupportedDefaultsException)
        {
            return Report(request, "configuration-unavailable", [], "unsupported-defaults");
        }
        catch (PlatformNotSupportedException)
        {
            return Report(request, "unsupported-host", [], "unsupported-host");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return Report(request, "configuration-unavailable", [], "configuration-load");
        }
    }

    private static FeedReport Report(
        FeedRequest request,
        string status,
        IReadOnlyList<SourceObservation> sources,
        string reason = "none"
    ) =>
        new(
            1,
            "nuget-feed-doctor",
            request.Online ? "online-observation" : "effective-configuration",
            !string.IsNullOrEmpty(request.Generation)
            && GenerationPattern().IsMatch(request.Generation)
                ? request.Generation
                : "",
            status,
            sources,
            status == "stale" ? "stale-inputs" : reason
        );

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex GenerationPattern();
}
