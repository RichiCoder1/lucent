namespace Lucent.Core;

/// <summary>Classifies generated component dependency metadata without changing runtime resolution.</summary>
public enum ComponentRequirementKind
{
    /// <summary>An exact contextual value supplied by authored placement.</summary>
    Context,

    /// <summary>An exact borrowed application service supplied by the lifecycle binding.</summary>
    Inject,
}

/// <summary>Describes one generated component requirement for compiler and editor discovery.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ComponentRequirementAttribute : Attribute
{
    /// <summary>Creates immutable metadata for one generated component method.</summary>
    public ComponentRequirementAttribute(
        Type exactType,
        ComponentRequirementKind kind,
        string member,
        string projectRelativePath,
        int line,
        int column
    )
        : this(exactType, kind, member, projectRelativePath, line, column, optional: false) { }

    /// <summary>Creates immutable metadata for one declared requirement with explicit optionality.</summary>
    public ComponentRequirementAttribute(
        Type exactType,
        ComponentRequirementKind kind,
        string member,
        string projectRelativePath,
        int line,
        int column,
        bool optional
    )
    {
        ArgumentNullException.ThrowIfNull(exactType);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        ExactType = exactType;
        Kind = kind;
        Member = member;
        ProjectRelativePath = ComponentSourcePath.Normalize(
            projectRelativePath,
            nameof(projectRelativePath)
        );
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(column, 1);
        Line = line;
        Column = column;
        Optional = optional;
    }

    /// <summary>Gets the exact closed required type.</summary>
    public Type ExactType { get; }

    /// <summary>Gets the authored dependency domain.</summary>
    public ComponentRequirementKind Kind { get; }

    /// <summary>Gets the declared component member.</summary>
    public string Member { get; }

    /// <summary>Gets the normalized project-relative source path.</summary>
    public string ProjectRelativePath { get; }

    /// <summary>Gets the one-based source line.</summary>
    public int Line { get; }

    /// <summary>Gets the one-based source column.</summary>
    public int Column { get; }

    /// <summary>Gets whether a missing application service is accepted as <see langword="null"/>.</summary>
    public bool Optional { get; }
}

/// <summary>Identifies one generated component requirement at its authored source location.</summary>
public sealed record ComponentRequirementSource
{
    /// <summary>Creates immutable diagnostic metadata for one declared requirement.</summary>
    public ComponentRequirementSource(
        string member,
        string typeName,
        string filePath,
        int line,
        int column
    )
        : this(member, typeName, filePath, line, column, framework: false) { }

    private ComponentRequirementSource(
        string member,
        string typeName,
        string filePath,
        int line,
        int column,
        bool framework
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(column, 1);
        var normalizedPath = framework
            ? filePath.Replace('\\', '/')
            : ComponentSourcePath.Normalize(filePath, nameof(filePath));
        Member = member;
        TypeName = typeName;
        FilePath = normalizedPath;
        Line = line;
        Column = column;
    }

    /// <summary>Gets the declared component member.</summary>
    public string Member { get; }

    /// <summary>Gets the compiler-normalized exact type display.</summary>
    public string TypeName { get; }

    /// <summary>Gets the compiler-normalized source path.</summary>
    public string FilePath { get; }

    /// <summary>Gets the one-based source line.</summary>
    public int Line { get; }

    /// <summary>Gets the one-based source column.</summary>
    public int Column { get; }

    internal InvalidOperationException Missing(string source) =>
        new(
            $"Requirement '{Member}' for exact type '{TypeName}' at {FilePath}:{Line}:{Column} has no {source}."
        );

    internal InvalidOperationException NullResult(string source) =>
        new(
            $"Requirement '{Member}' for exact type '{TypeName}' at {FilePath}:{Line}:{Column} received null from its {source}."
        );

    internal string Location =>
        FilePath
        + ":"
        + Line.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + ":"
        + Column.ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal static ComponentRequirementSource FrameworkTheme { get; } =
        new("Theme", "Lucent.Core.ThemeContext", "<framework>", 1, 1, framework: true);
}

/// <summary>Builds closed typed requirement plans for generated component recipes.</summary>
public static class ComponentRequirements
{
    /// <summary>Starts a plan with one exact contextual value.</summary>
    public static ComponentRequirementPlan<T> Context<T>(ComponentRequirementSource source) =>
        ComponentRequirementPlan<T>.StartContext(source);

    /// <summary>Starts a plan with one exact borrowed application service.</summary>
    public static ComponentRequirementPlan<T> Service<T>(ComponentRequirementSource source)
        where T : class => ComponentRequirementPlan<T>.StartService<T>(source);

    /// <summary>Starts a plan with one optional exact borrowed application service.</summary>
    public static ComponentRequirementPlan<T?> OptionalService<T>(ComponentRequirementSource source)
        where T : class => ComponentRequirementPlan<T?>.StartOptionalService<T>(source);
}

/// <summary>An opaque closed typed plan resolved once at a component's mount position.</summary>
public sealed class ComponentRequirementPlan<TValues>
{
    private readonly Func<MountEnvironment, Element, TValues> _resolve;
    private readonly RequirementIdentity[] _requirements;
    private readonly bool _hasServices;
    private readonly bool _selected;

    private ComponentRequirementPlan(
        Func<MountEnvironment, Element, TValues> resolve,
        RequirementIdentity[] requirements,
        bool hasServices,
        bool selected = false
    )
    {
        _resolve = resolve;
        _requirements = requirements;
        _hasServices = hasServices;
        _selected = selected;
    }

    /// <summary>Adds an exact contextual value before service requirements begin.</summary>
    public ComponentRequirementPlan<(TValues Previous, TContext Value)> AndContext<TContext>(
        ComponentRequirementSource source
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ThrowIfSelected();
        if (_hasServices)
            throw new InvalidOperationException(
                "Context requirements must be declared before application service requirements."
            );
        var identity = new RequirementIdentity(
            ContextIdentity<TContext>.Value,
            ComponentRequirementKind.Context,
            source
        );
        ValidateDistinct(identity);
        return new ComponentRequirementPlan<(TValues, TContext)>(
            (environment, parent) =>
                (
                    _resolve(environment, parent),
                    environment.RequireContext<TContext>(source, parent)
                ),
            Append(identity),
            false
        );
    }

    /// <summary>Adds one exact borrowed application service after all contextual values.</summary>
    public ComponentRequirementPlan<(TValues Previous, TService Value)> AndService<TService>(
        ComponentRequirementSource source
    )
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ThrowIfSelected();
        var identity = new RequirementIdentity(
            ContextIdentity<TService>.Value,
            ComponentRequirementKind.Inject,
            source
        );
        ValidateDistinct(identity);
        return new ComponentRequirementPlan<(TValues, TService)>(
            (environment, parent) =>
                (
                    _resolve(environment, parent),
                    environment.RequireService<TService>(source, parent)
                ),
            Append(identity),
            true
        );
    }

    /// <summary>Adds one optional exact borrowed application service after all contextual values.</summary>
    public ComponentRequirementPlan<(
        TValues Previous,
        TService? Value
    )> AndOptionalService<TService>(ComponentRequirementSource source)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ThrowIfSelected();
        var identity = new RequirementIdentity(
            ContextIdentity<TService>.Value,
            ComponentRequirementKind.Inject,
            source
        );
        ValidateDistinct(identity);
        return new ComponentRequirementPlan<(TValues, TService?)>(
            (environment, parent) =>
                (
                    _resolve(environment, parent),
                    environment.OptionalService<TService>(source, parent)
                ),
            Append(identity),
            true
        );
    }

    /// <summary>Maps the fully resolved values to a generated component-specific bundle.</summary>
    public ComponentRequirementPlan<TResult> Select<TResult>(Func<TValues, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ThrowIfSelected();
        return new ComponentRequirementPlan<TResult>(
            (environment, parent) => selector(_resolve(environment, parent)),
            _requirements,
            _hasServices,
            selected: true
        );
    }

    internal TValues Resolve(MountEnvironment environment, Element parent) =>
        _resolve(environment, parent);

    internal ContextRequirementDiagnostic[] Describe() =>
        [
            .. _requirements.Select(requirement => new ContextRequirementDiagnostic(
                requirement.Kind,
                requirement.Source.Member,
                requirement.Source.TypeName,
                requirement.Source.Location
            )),
        ];

    internal static ComponentRequirementPlan<TValues> StartContext(
        ComponentRequirementSource source
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        var identity = new RequirementIdentity(
            ContextIdentity<TValues>.Value,
            ComponentRequirementKind.Context,
            source
        );
        return new ComponentRequirementPlan<TValues>(
            (environment, parent) => environment.RequireContext<TValues>(source, parent),
            [identity],
            false
        );
    }

    internal static ComponentRequirementPlan<TService> StartService<TService>(
        ComponentRequirementSource source
    )
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(source);
        var identity = new RequirementIdentity(
            ContextIdentity<TService>.Value,
            ComponentRequirementKind.Inject,
            source
        );
        return new ComponentRequirementPlan<TService>(
            (environment, parent) => environment.RequireService<TService>(source, parent),
            [identity],
            true
        );
    }

    internal static ComponentRequirementPlan<TService?> StartOptionalService<TService>(
        ComponentRequirementSource source
    )
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(source);
        var identity = new RequirementIdentity(
            ContextIdentity<TService>.Value,
            ComponentRequirementKind.Inject,
            source
        );
        return new ComponentRequirementPlan<TService?>(
            (environment, parent) => environment.OptionalService<TService>(source, parent),
            [identity],
            true
        );
    }

    private RequirementIdentity[] Append(RequirementIdentity identity)
    {
        var next = new RequirementIdentity[_requirements.Length + 1];
        Array.Copy(_requirements, next, _requirements.Length);
        next[^1] = identity;
        return next;
    }

    private void ValidateDistinct(RequirementIdentity next)
    {
        foreach (var existing in _requirements)
            if (ReferenceEquals(existing.Identity, next.Identity))
                throw new InvalidOperationException(
                    $"Exact type '{next.Source.TypeName}' is already required by member '{existing.Source.Member}'; member '{next.Source.Member}' cannot require it again."
                );
    }

    private void ThrowIfSelected()
    {
        if (_selected)
            throw new InvalidOperationException(
                "Requirement value mapping must be the final operation in a requirement plan."
            );
    }
}

internal sealed record RequirementIdentity(
    object Identity,
    ComponentRequirementKind Kind,
    ComponentRequirementSource Source
);

internal static class ComponentSourcePath
{
    internal static string Normalize(string path) => Normalize(path, nameof(path));

    internal static string Normalize(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/');
        if (
            Path.IsPathRooted(normalized)
            || normalized.Contains(':')
            || segments.Any(segment => String.IsNullOrWhiteSpace(segment) || segment is "." or "..")
        )
            throw new ArgumentException(
                "Source paths must be normalized project-relative paths.",
                parameterName
            );
        return normalized;
    }
}
