using System;
using System.Security.Cryptography;
using System.Text;

namespace Lucent.Lui.Compiler;

/// <summary>Immutable evaluated-project identity used by host adapters.</summary>
public sealed class LuiProjectIdentity : IEquatable<LuiProjectIdentity>
{
    public LuiProjectIdentity(string rootPath, string languageVersion)
    {
        RootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        LanguageVersion = languageVersion ?? throw new ArgumentNullException(nameof(languageVersion));
    }

    public string RootPath { get; }
    public string LanguageVersion { get; }
    public bool Equals(LuiProjectIdentity? other) => other != null && StringComparer.Ordinal.Equals(RootPath, other.RootPath) && StringComparer.Ordinal.Equals(LanguageVersion, other.LanguageVersion);
    public override bool Equals(object? obj) => Equals(obj as LuiProjectIdentity);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(RootPath) ^ StringComparer.Ordinal.GetHashCode(LanguageVersion);
}

/// <summary>Logical, project-relative document identity. It is never an absolute host path.</summary>
public sealed class LuiDocumentIdentity : IEquatable<LuiDocumentIdentity>
{
    public LuiDocumentIdentity(string logicalPath)
    {
        if (String.IsNullOrWhiteSpace(logicalPath)) throw new ArgumentException("A logical document path is required.", nameof(logicalPath));
        var normalized = logicalPath.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) || (normalized.Length >= 2 && Char.IsLetter(normalized[0]) && normalized[1] == ':')) throw new ArgumentException("Document paths must be project-relative.", nameof(logicalPath));
        var segments = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || Array.Exists(segments, segment => segment == "." || segment == "..")) throw new ArgumentException("Document paths must be project-relative.", nameof(logicalPath));
        LogicalPath = String.Join("/", segments);
    }

    public string LogicalPath { get; }
    public string StableId => Hash(LogicalPath);
    public bool Equals(LuiDocumentIdentity? other) => other != null && StringComparer.Ordinal.Equals(LogicalPath, other.LogicalPath);
    public override bool Equals(object? obj) => Equals(obj as LuiDocumentIdentity);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(LogicalPath);
    public static string Hash(string value)
    {
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            var builder = new StringBuilder(16);
            for (var i = 0; i != 8; i++) builder.Append(bytes[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }
}
