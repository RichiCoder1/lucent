namespace Lucent.Core;

/// <summary>An ordinal, case-sensitive identity within a declaring asset domain.</summary>
public sealed record AssetId
{
    /// <summary>Creates an identity, normalizing logical separators without resolving filesystem paths.</summary>
    public AssetId(string domain, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (
            domain is "." or ".."
            || domain.Any(character =>
                !char.IsLetterOrDigit(character) && character is not '.' and not '-' and not '_'
            )
        )
            throw new ArgumentException(
                "Asset domains contain letters, digits, '.', '-' or '_'.",
                nameof(domain)
            );

        var normalized = path.Replace('\\', '/');
        if (
            normalized.Any(character =>
                char.IsControl(character) || ":*?\"<>|#".Contains(character)
            )
            || normalized
                .Split('/')
                .Any(segment =>
                    string.IsNullOrWhiteSpace(segment)
                    || segment is "." or ".."
                    || segment != segment.Trim()
                )
        )
            throw new ArgumentException(
                "Asset paths must be relative, nonempty logical segments without traversal or reserved characters.",
                nameof(path)
            );

        Domain = domain;
        Path = normalized;
    }

    /// <summary>The declaring library's stable domain, independent of its installed file location.</summary>
    public string Domain { get; }

    /// <summary>The normalized, case-sensitive logical path within the domain.</summary>
    public string Path { get; }

    /// <summary>Returns diagnostic identity only; this representation does not enable URI loading.</summary>
    public override string ToString() => $"asset://{Domain}/{Path}";
}
