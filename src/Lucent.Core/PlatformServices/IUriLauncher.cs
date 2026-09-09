namespace Lucent.Core;

/// <summary>Outcome of an explicit application request to invoke an external URI handler.</summary>
public enum UriLaunchStatus
{
    /// <summary>The operating system accepted the handler launch; completion is not observed.</summary>
    Launched,

    /// <summary>The request was canceled before a handler was started.</summary>
    Canceled,

    /// <summary>This host does not provide URI launching.</summary>
    Unsupported,

    /// <summary>The application's explicit URI policy rejected the request.</summary>
    Denied,

    /// <summary>The operating system could not start the requested handler.</summary>
    Failed,
}

/// <summary>Explicit URI-launch outcome. Errors contain no requested URI or credentials.</summary>
public sealed record UriLaunchResult(UriLaunchStatus Status, string? Error = null);

/// <summary>Application-injected external handler capability; it does not perform internal navigation.</summary>
public interface IUriLauncher
{
    /// <summary>Applies application policy and requests a handler launch. Cancellation cannot undo an accepted launch.</summary>
    ValueTask<UriLaunchResult> LaunchAsync(Uri uri, CancellationToken cancellationToken = default);
}

/// <summary>An immutable, explicit allowlist of external URI schemes and optional application decision.</summary>
public sealed class UriLaunchPolicy
{
    private readonly HashSet<string> _schemes;
    private readonly Func<Uri, bool>? _allow;

    /// <summary>Creates a policy that accepts only listed absolute URI schemes and the optional predicate.</summary>
    public UriLaunchPolicy(IEnumerable<string> allowedSchemes, Func<Uri, bool>? allow = null)
    {
        ArgumentNullException.ThrowIfNull(allowedSchemes);
        _schemes = new(StringComparer.OrdinalIgnoreCase);
        foreach (var scheme in allowedSchemes)
        {
            if (string.IsNullOrWhiteSpace(scheme) || !Uri.CheckSchemeName(scheme))
                throw new ArgumentException(
                    "Allowed schemes must be valid URI scheme names.",
                    nameof(allowedSchemes)
                );
            _schemes.Add(scheme);
        }
        _allow = allow;
    }

    /// <summary>Returns whether this absolute URI is allowed. It does not launch or fetch the URI.</summary>
    public bool Allows(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsAbsoluteUri && _schemes.Contains(uri.Scheme) && (_allow?.Invoke(uri) ?? true);
    }
}
