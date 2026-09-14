using System.Globalization;

namespace Lucent.Core;

/// <summary>Identifies one generated context provider without exposing its supplied value.</summary>
public sealed record ContextProviderSource
{
    /// <summary>Creates immutable source metadata for one exact typed provider.</summary>
    public ContextProviderSource(
        Type exactType,
        string typeName,
        string projectRelativePath,
        int line,
        int column
    )
        : this(exactType, typeName, projectRelativePath, line, column, framework: false) { }

    private ContextProviderSource(
        Type exactType,
        string typeName,
        string projectRelativePath,
        int line,
        int column,
        bool framework
    )
    {
        ArgumentNullException.ThrowIfNull(exactType);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRelativePath);
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(column, 1);
        ExactType = exactType;
        TypeName = typeName;
        ProjectRelativePath = framework
            ? projectRelativePath
            : ComponentSourcePath.Normalize(projectRelativePath, nameof(projectRelativePath));
        Line = line;
        Column = column;
    }

    /// <summary>Gets the exact closed provider type.</summary>
    public Type ExactType { get; }

    /// <summary>Gets the compiler-normalized exact type display.</summary>
    public string TypeName { get; }

    /// <summary>Gets the normalized project-relative source path.</summary>
    public string ProjectRelativePath { get; }

    /// <summary>Gets the one-based source line.</summary>
    public int Line { get; }

    /// <summary>Gets the one-based source column.</summary>
    public int Column { get; }

    internal string Location =>
        ProjectRelativePath
        + ":"
        + Line.ToString(CultureInfo.InvariantCulture)
        + ":"
        + Column.ToString(CultureInfo.InvariantCulture);

    internal static ContextProviderSource Manual<T>() =>
        new(typeof(T), "<csharp-exact-type>", "<csharp>", 1, 1, framework: true);
}

internal readonly record struct ContextProviderDiagnostic(
    long OwnerId,
    string TypeName,
    string SourceLocation,
    long? ShadowedOwnerId
);

internal readonly record struct ContextRequirementDiagnostic(
    ComponentRequirementKind Kind,
    string Member,
    string TypeName,
    string SourceLocation
);
