using System.Text;

namespace Lucent.Platform.Windows.Activation;

/// <summary>The Windows activation kind accepted by the optional adapter.</summary>
public enum ActivationKind
{
    /// <summary>A plain process launch.</summary>
    Launch,

    /// <summary>A protocol URI delivered by Windows.</summary>
    Protocol,

    /// <summary>A rejected external delivery retained for guarded fallback or diagnostics.</summary>
    Rejected,
}

/// <summary>Finite reason for rejecting an external activation.</summary>
public enum ActivationRejection
{
    /// <summary>No rejection occurred.</summary>
    None,

    /// <summary>The raw protocol URI did not meet the configured envelope contract.</summary>
    InvalidProtocol,

    /// <summary>The operating system supplied an unsupported activation kind.</summary>
    UnsupportedKind,

    /// <summary>The operating system arguments could not be read.</summary>
    UnreadableArguments,
}

/// <summary>How an activation reached this process.</summary>
public enum ActivationDelivery
{
    /// <summary>Arguments captured during process startup.</summary>
    Cold,

    /// <summary>Arguments redirected from another process.</summary>
    Redirected,
}

/// <summary>Transport provenance; it does not authenticate the sender.</summary>
public enum ActivationProvenance
{
    /// <summary>External operating-system delivery, including redirection.</summary>
    UntrustedExternal,
}

/// <summary>A copied activation request with escaped route text preserved for Core validation.</summary>
public sealed class ActivationEnvelope
{
    internal ActivationEnvelope(
        ActivationKind kind,
        ActivationDelivery delivery,
        string? rawUri,
        string? escapedPathAndQuery,
        ActivationRejection rejection = ActivationRejection.None,
        long sequence = 0
    )
    {
        Kind = kind;
        Delivery = delivery;
        RawUri = rawUri;
        EscapedPathAndQuery = escapedPathAndQuery;
        Rejection = rejection;
        Sequence = sequence;
    }

    /// <summary>The accepted or rejected activation kind.</summary>
    public ActivationKind Kind { get; }

    /// <summary>Whether the request was captured during startup or redirected.</summary>
    public ActivationDelivery Delivery { get; }

    /// <summary>All Windows deliveries are untrusted external input.</summary>
    public ActivationProvenance Provenance { get; } = ActivationProvenance.UntrustedExternal;

    /// <summary>Original bounded protocol URI, present only for an accepted protocol.</summary>
    public string? RawUri { get; }

    /// <summary>Escaped path and query for Core parsing, without normalization.</summary>
    public string? EscapedPathAndQuery { get; }

    /// <summary>Finite rejection reason; no raw text is retained for rejected input.</summary>
    public ActivationRejection Rejection { get; }

    internal long Sequence { get; }

    internal ActivationEnvelope WithSequence(long sequence) =>
        new(Kind, Delivery, RawUri, EscapedPathAndQuery, Rejection, sequence);
}

/// <summary>Explicit application identity and the one protocol authority accepted by this process.</summary>
public sealed record WindowsActivationOptions(string InstanceKey, string Scheme, string Host)
{
    /// <summary>Validates this application's fixed instance and protocol identity.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(InstanceKey) || InstanceKey.Length > 128)
            throw new ArgumentException(
                "Provide a bounded application/channel instance key.",
                nameof(InstanceKey)
            );
        if (string.IsNullOrEmpty(Scheme) || Scheme.Length > 64 || !IsAsciiLower(Scheme[0], true))
            throw new ArgumentException(
                "The protocol scheme must be lower-case ASCII.",
                nameof(Scheme)
            );
        foreach (var c in Scheme.AsSpan(1))
            if (!IsAsciiLower(c, false))
                throw new ArgumentException(
                    "The protocol scheme must be lower-case ASCII.",
                    nameof(Scheme)
                );
        if (string.IsNullOrEmpty(Host) || Host.Length > 253 || Host[0] == '.' || Host[^1] == '.')
            throw new ArgumentException(
                "The protocol host must be a lower-case ASCII DNS name.",
                nameof(Host)
            );
        foreach (var c in Host)
            if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c is '-' or '.'))
                throw new ArgumentException(
                    "The protocol host must be a lower-case ASCII DNS name.",
                    nameof(Host)
                );
        if (Host.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("The protocol host has an empty label.", nameof(Host));
    }

    private static bool IsAsciiLower(char c, bool first) =>
        c is >= 'a' and <= 'z' || !first && (c is >= '0' and <= '9' or '+' or '-' or '.');
}

/// <summary>Parses the raw OS representation without URI projection or dot-segment normalization.</summary>
public static class WindowsActivationEnvelope
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Maximum accepted raw protocol URI length in UTF-8 bytes.</summary>
    public const int MaxRawUriBytes = 4096;

    /// <summary>Copies a plain launch from Windows.</summary>
    public static ActivationEnvelope Launch(ActivationDelivery delivery) =>
        new(ActivationKind.Launch, delivery, null, null);

    /// <summary>Creates a finite rejection without retaining untrusted raw text.</summary>
    public static ActivationEnvelope Rejected(
        ActivationDelivery delivery,
        ActivationRejection reason
    ) => new(ActivationKind.Rejected, delivery, null, null, reason);

    /// <summary>Attempts to extract a protocol route while retaining its original escaped text.</summary>
    public static bool TryProtocol(
        string? rawUri,
        WindowsActivationOptions options,
        ActivationDelivery delivery,
        out ActivationEnvelope? envelope
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        envelope = null;
        if (string.IsNullOrEmpty(rawUri) || rawUri.Length > MaxRawUriBytes)
            return false;
        try
        {
            if (StrictUtf8.GetByteCount(rawUri) > MaxRawUriBytes)
                return false;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
        var prefix = options.Scheme + "://" + options.Host;
        if (!rawUri.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        if (rawUri.Length <= prefix.Length || rawUri[prefix.Length] != '/')
            return false;
        // Checking the authority boundary before extracting the path rejects user info,
        // ports, alternate hosts and opaque forms without normalizing the route itself.
        var escaped = rawUri[prefix.Length..];
        if (escaped.Contains('#', StringComparison.Ordinal) || escaped.Contains('\\'))
            return false;
        envelope = new(ActivationKind.Protocol, delivery, rawUri, escaped);
        return true;
    }
}
