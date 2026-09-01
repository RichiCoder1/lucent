using System;
using System.Security.Cryptography;
using System.Text;

namespace Lucent.Lui.Compiler;

/// <summary>Immutable evaluated-project identity supplied by build or editor hosts.</summary>
/// <remarks>This value distinguishes project contexts for tooling and has no runtime role.</remarks>
public sealed class LuiProjectIdentity : IEquatable<LuiProjectIdentity>
{
    /// <summary>Creates an identity from the evaluated project root and selected language version.</summary>
    public LuiProjectIdentity(string rootPath, string languageVersion)
    {
        RootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        LanguageVersion =
            languageVersion ?? throw new ArgumentNullException(nameof(languageVersion));
    }

    /// <summary>Root path supplied by the host; equality remains ordinal and case-sensitive.</summary>
    public string RootPath { get; }

    /// <summary>Language version that participates in project identity.</summary>
    public string LanguageVersion { get; }

    /// <summary>Compares the root path and language version using ordinal equality.</summary>
    public bool Equals(LuiProjectIdentity? other) =>
        other != null
        && StringComparer.Ordinal.Equals(RootPath, other.RootPath)
        && StringComparer.Ordinal.Equals(LanguageVersion, other.LanguageVersion);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as LuiProjectIdentity);

    /// <inheritdoc />
    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(RootPath)
        ^ StringComparer.Ordinal.GetHashCode(LanguageVersion);
}

/// <summary>Logical, project-relative document identity for build and editor tooling.</summary>
/// <remarks>It is never an absolute host path and has no runtime role.</remarks>
public sealed class LuiDocumentIdentity : IEquatable<LuiDocumentIdentity>
{
    private static readonly char[] PathSeparators = new[] { '/' };

    /// <summary>Validates and normalizes a project-relative path to slash-separated form.</summary>
    /// <exception cref="ArgumentException"><paramref name="logicalPath"/> is blank, absolute, or contains dot segments.</exception>
    public LuiDocumentIdentity(string logicalPath)
    {
        if (String.IsNullOrWhiteSpace(logicalPath))
            throw new ArgumentException(
                "A logical document path is required.",
                nameof(logicalPath)
            );
        var normalized = logicalPath.Replace('\\', '/');
        if (
            normalized.StartsWith("/", StringComparison.Ordinal)
            || (normalized.Length >= 2 && Char.IsLetter(normalized[0]) && normalized[1] == ':')
        )
            throw new ArgumentException(
                "Document paths must be project-relative.",
                nameof(logicalPath)
            );
        var segments = normalized.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (
            segments.Length == 0
            || Array.Exists(segments, segment => segment == "." || segment == "..")
        )
            throw new ArgumentException(
                "Document paths must be project-relative.",
                nameof(logicalPath)
            );
        LogicalPath = String.Join("/", segments);
    }

    /// <summary>Normalized slash-separated project-relative path.</summary>
    public string LogicalPath { get; }

    /// <summary>Stable truncated SHA-256 identifier derived from <see cref="LogicalPath"/>.</summary>
    public string StableId => Hash(LogicalPath);

    /// <summary>Compares normalized logical paths using ordinal equality.</summary>
    public bool Equals(LuiDocumentIdentity? other) =>
        other != null && StringComparer.Ordinal.Equals(LogicalPath, other.LogicalPath);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as LuiDocumentIdentity);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(LogicalPath);

    /// <summary>Returns the deterministic 16-hex-character SHA-256 prefix for a non-null value.</summary>
    public static string Hash(string value)
    {
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            var builder = new StringBuilder(16);
            for (var i = 0; i != 8; i++)
                builder.Append(
                    bytes[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture)
                );
            return builder.ToString();
        }
    }
}
