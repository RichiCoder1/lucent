using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Lucent.Tools.NuGet;

internal static class FeedTransport
{
    private const int MaximumBody = 1024 * 1024;

    internal static async Task<SourceObservation> ObserveAsync(
        PackageSource source,
        string package,
        NuGetVersion version,
        SourceObservation observation,
        CancellationToken cancellationToken
    )
    {
        if (!Uri.TryCreate(source.Source, UriKind.Absolute, out var origin) || !Safe(origin))
            return observation with
            {
                Reachability = "unsupported",
                Authentication = "unknown",
                Release = "unknown",
                Reason = "unsupported-source",
            };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        using var clientHandler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseDefaultCredentials = false,
            Credentials = null,
            DefaultProxyCredentials = null,
            UseProxy = false,
        };
        using var handler = new OriginHandler(origin, clientHandler);
        using var http = new HttpSource(
            source,
            () =>
                Task.FromResult<HttpHandlerResource>(
                    new HttpHandlerResourceV3(clientHandler, handler)
                ),
            NullThrottle.Instance
        );
        try
        {
            var index = await ReadAsync(http, origin, deadline.Token).ConfigureAwait(false);
            if (index.Code is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return AuthenticationFailure(observation, index.Code);
            if (index.Code != HttpStatusCode.OK)
                return observation with
                {
                    Reachability = "reachable",
                    Authentication = "notExercised",
                    Release = "unknown",
                    Reason = "http-status",
                };
            var document = JObject.Parse(index.Body);
            if (
                document["version"]?.Value<string>() != "3.0.0"
                || document["resources"] is not JArray resources
                || resources.Count > 128
            )
                throw new IOException("Invalid service index.");
            var resource = new ServiceIndexResourceV3(document, DateTime.UtcNow);
            var baseUri = resource.GetServiceEntryUri("PackageBaseAddress/3.0.0");
            // Service resource origins require the same scrutiny as HTTP redirects.
            if (baseUri is null || !Safe(baseUri) || !SameOrigin(origin, baseUri))
                return observation with
                {
                    Reachability = "reachable",
                    Authentication = "notExercised",
                    Release = "unknown",
                    Reason = "unsupported-resource",
                };
            var uri = new Uri(
                baseUri.AbsoluteUri.TrimEnd('/') + "/" + package.ToLowerInvariant() + "/index.json"
            );
            var release = await ReadAsync(http, uri, deadline.Token).ConfigureAwait(false);
            if (release.Code is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return AuthenticationFailure(observation, release.Code);
            var status =
                release.Code == HttpStatusCode.NotFound ? "notFoundInAnonymousView" : "unknown";
            if (release.Code == HttpStatusCode.OK)
            {
                var versions =
                    JObject.Parse(release.Body)["versions"] as JArray
                    ?? throw new IOException("Invalid version response.");
                if (
                    versions.Count > 4096
                    || versions.Any(value =>
                        value.Type != JTokenType.String
                        || !NuGetVersion.TryParse(value.Value<string>(), out _)
                    )
                )
                    throw new IOException("Invalid version response.");
                status = versions.Any(value =>
                    NuGetVersion.TryParse(value.Value<string>(), out var candidate)
                    && VersionComparer.VersionRelease.Equals(candidate, version)
                )
                    ? "available"
                    : "notFoundInAnonymousView";
            }
            return observation with
            {
                Reachability = "reachable",
                Authentication = "notExercised",
                Release = status,
                Reason = status == "unknown" ? "http-status" : "none",
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return observation with
            {
                Reachability = "timed-out",
                Authentication = "unknown",
                Release = "unknown",
                Reason = "deadline",
            };
        }
        catch (TimeoutException)
        {
            return observation with
            {
                Reachability = "timed-out",
                Authentication = "unknown",
                Release = "unknown",
                Reason = "deadline",
            };
        }
        catch (RedirectRejectedException)
        {
            return observation with
            {
                Reachability = "unsupported",
                Authentication = "unknown",
                Release = "unknown",
                Reason = "unsupported-redirect",
            };
        }
        catch (HttpRequestException)
        {
            return observation with
            {
                Reachability = "unreachable",
                Authentication = "unknown",
                Release = "unknown",
                Reason = "unreachable",
            };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return observation with
            {
                Reachability = "inconclusive",
                Authentication = "unknown",
                Release = "unknown",
                Reason = "invalid-response",
            };
        }
    }

    private static SourceObservation AuthenticationFailure(
        SourceObservation source,
        HttpStatusCode code
    ) =>
        source with
        {
            Reachability = "reachable",
            Authentication = code == HttpStatusCode.Unauthorized ? "authRequired" : "forbidden",
            Release = "unknown",
        };

    private static bool Safe(Uri uri) =>
        string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Fragment)
        && string.IsNullOrEmpty(uri.Query)
        && (uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback);

    private static bool SameOrigin(Uri origin, Uri uri) =>
        origin.Scheme == uri.Scheme && origin.IdnHost == uri.IdnHost && origin.Port == uri.Port;

    private static Task<(HttpStatusCode Code, string Body)> ReadAsync(
        HttpSource source,
        Uri uri,
        CancellationToken cancellationToken
    ) =>
        source.ProcessResponseAsync(
            new HttpSourceRequest(uri, NullLogger.Instance)
            {
                MaxTries = 1,
                RequestTimeout = TimeSpan.FromSeconds(8),
                DownloadTimeout = TimeSpan.FromSeconds(8),
            },
            async response =>
            {
                if (response.StatusCode != HttpStatusCode.OK)
                    return (response.StatusCode, "");
                if (response.Content.Headers.ContentLength > MaximumBody)
                    throw new IOException("Response bound exceeded.");
                await using var stream = await response
                    .Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                using var memory = new MemoryStream();
                var buffer = new byte[8192];
                int count;
                while (
                    (
                        count = await stream
                            .ReadAsync(buffer, cancellationToken)
                            .ConfigureAwait(false)
                    ) > 0
                )
                {
                    if (memory.Length + count > MaximumBody)
                        throw new IOException("Response bound exceeded.");
                    memory.Write(buffer, 0, count);
                }
                return (
                    response.StatusCode,
                    Encoding.UTF8.GetString(memory.GetBuffer(), 0, (int)memory.Length)
                );
            },
            NullLogger.Instance,
            cancellationToken
        );

    private sealed class OriginHandler(Uri origin, HttpMessageHandler inner)
        : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            for (var redirects = 0; ; redirects++)
            {
                var uri = request.RequestUri!;
                if (!Safe(uri) || !SameOrigin(origin, uri))
                    throw new RedirectRejectedException();
                var response = await base.SendAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308))
                    return response;
                var location = response.Headers.Location;
                response.Dispose();
                if (redirects >= 3 || location is null)
                    throw new RedirectRejectedException();
                var destination = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (!Safe(destination) || !SameOrigin(origin, destination))
                    throw new RedirectRejectedException();
                request = new HttpRequestMessage(HttpMethod.Get, destination);
            }
        }
    }

    private sealed class RedirectRejectedException : IOException;
}
