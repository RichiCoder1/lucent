using System.Collections.ObjectModel;
using System.Globalization;

namespace Lucent.Core;

/// <summary>A stable generated identity for one terminal route definition.</summary>
public sealed record RouteDefinitionId
{
    /// <summary>Creates a route-definition identity from authored metadata.</summary>
    public RouteDefinitionId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(character => char.IsControl(character)))
            throw new ArgumentException(
                "Route definition identities cannot contain control characters.",
                nameof(value)
            );
        Value = value;
    }

    /// <summary>Gets the stable authored identity.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>The fixed built-in representation of one route value.</summary>
public enum RouteValueKind
{
    /// <summary>An uninitialized value.</summary>
    None,

    /// <summary>An unconstrained string.</summary>
    Text,

    /// <summary>A canonical signed 32-bit integer.</summary>
    Signed32,

    /// <summary>A canonical signed 64-bit integer.</summary>
    Signed64,

    /// <summary>A lowercase canonical D-format GUID.</summary>
    Uuid,

    /// <summary>A lowercase canonical Boolean.</summary>
    Boolean,

    /// <summary>An ordinal generated enum name.</summary>
    EnumName,
}

/// <summary>One typed route value without an object payload.</summary>
public readonly struct RouteValue : IEquatable<RouteValue>
{
    private readonly string? _text;
    private readonly long _integer;
    private readonly Guid _guid;

    private RouteValue(RouteValueKind kind, string? text, long integer, Guid guid)
    {
        Kind = kind;
        _text = text;
        _integer = integer;
        _guid = guid;
    }

    /// <summary>Gets the value's fixed representation.</summary>
    public RouteValueKind Kind { get; }

    /// <summary>Creates a string value.</summary>
    public static RouteValue FromText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(RouteValueKind.Text, value, 0, default);
    }

    /// <summary>Creates a signed 32-bit integer value.</summary>
    public static RouteValue FromSigned32(int value) =>
        new(RouteValueKind.Signed32, null, value, default);

    /// <summary>Creates a signed 64-bit integer value.</summary>
    public static RouteValue FromSigned64(long value) =>
        new(RouteValueKind.Signed64, null, value, default);

    /// <summary>Creates a GUID value.</summary>
    public static RouteValue FromUuid(Guid value) => new(RouteValueKind.Uuid, null, 0, value);

    /// <summary>Creates a Boolean value.</summary>
    public static RouteValue FromBoolean(bool value) =>
        new(RouteValueKind.Boolean, null, value ? 1 : 0, default);

    /// <summary>Creates an ordinal generated enum-name value.</summary>
    public static RouteValue FromEnumName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new(RouteValueKind.EnumName, value, 0, default);
    }

    /// <summary>Gets a string value.</summary>
    public string Text =>
        Kind == RouteValueKind.Text
            ? _text!
            : throw new InvalidOperationException("The route value is not a string.");

    /// <summary>Gets a signed 32-bit integer value.</summary>
    public int Signed32 =>
        Kind == RouteValueKind.Signed32
            ? checked((int)_integer)
            : throw new InvalidOperationException("The route value is not an Int32.");

    /// <summary>Gets a signed 64-bit integer value.</summary>
    public long Signed64 =>
        Kind == RouteValueKind.Signed64
            ? _integer
            : throw new InvalidOperationException("The route value is not an Int64.");

    /// <summary>Gets a GUID value.</summary>
    public Guid Uuid =>
        Kind == RouteValueKind.Uuid
            ? _guid
            : throw new InvalidOperationException("The route value is not a GUID.");

    /// <summary>Gets a Boolean value.</summary>
    public bool Boolean =>
        Kind == RouteValueKind.Boolean
            ? _integer != 0
            : throw new InvalidOperationException("The route value is not a Boolean.");

    /// <summary>Gets an ordinal generated enum name.</summary>
    public string EnumName =>
        Kind == RouteValueKind.EnumName
            ? _text!
            : throw new InvalidOperationException("The route value is not an enum name.");

    /// <inheritdoc />
    public bool Equals(RouteValue other) =>
        Kind == other.Kind
        && _integer == other._integer
        && _guid == other._guid
        && string.Equals(_text, other._text, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RouteValue other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, _text, _integer, _guid);

    /// <summary>Compares two typed route values.</summary>
    public static bool operator ==(RouteValue left, RouteValue right) => left.Equals(right);

    /// <summary>Compares two typed route values.</summary>
    public static bool operator !=(RouteValue left, RouteValue right) => !left.Equals(right);

    /// <summary>Returns a redacted representation.</summary>
    public override string ToString() => $"route-value kind={Kind}";

    internal string CanonicalText() =>
        Kind switch
        {
            RouteValueKind.Text => _text!,
            RouteValueKind.Signed32 => checked((int)_integer).ToString(
                CultureInfo.InvariantCulture
            ),
            RouteValueKind.Signed64 => _integer.ToString(CultureInfo.InvariantCulture),
            RouteValueKind.Uuid => _guid.ToString("D", CultureInfo.InvariantCulture),
            RouteValueKind.Boolean => _integer == 0 ? "false" : "true",
            RouteValueKind.EnumName => _text!,
            _ => throw new InvalidOperationException("The route value is uninitialized."),
        };
}

/// <summary>A fixed built-in lexical shape used by generated route descriptors.</summary>
public sealed class RouteValueShape
{
    private readonly ReadOnlyCollection<string> _enumNames;

    private RouteValueShape(RouteValueKind kind, string[]? enumNames = null)
    {
        Kind = kind;
        _enumNames = Array.AsReadOnly(enumNames ?? []);
    }

    /// <summary>The unconstrained string shape.</summary>
    public static RouteValueShape Text { get; } = new(RouteValueKind.Text);

    /// <summary>The canonical signed 32-bit integer shape.</summary>
    public static RouteValueShape Signed32 { get; } = new(RouteValueKind.Signed32);

    /// <summary>The canonical signed 64-bit integer shape.</summary>
    public static RouteValueShape Signed64 { get; } = new(RouteValueKind.Signed64);

    /// <summary>The lowercase canonical D-format GUID shape.</summary>
    public static RouteValueShape Uuid { get; } = new(RouteValueKind.Uuid);

    /// <summary>The lowercase canonical Boolean shape.</summary>
    public static RouteValueShape Boolean { get; } = new(RouteValueKind.Boolean);

    /// <summary>Gets the fixed representation.</summary>
    public RouteValueKind Kind { get; }

    /// <summary>Gets immutable ordinal names for an enum shape.</summary>
    public IReadOnlyList<string> EnumNames => _enumNames;

    /// <summary>Creates a generated enum-name shape.</summary>
    public static RouteValueShape Enum(params ReadOnlySpan<string> names)
    {
        if (names.IsEmpty)
            throw new RoutePatternConfigurationException(
                RoutePatternConfigurationErrorKind.EmptyEnum,
                "A route enum shape requires at least one canonical name."
            );
        var copy = names.ToArray();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in copy)
        {
            if (
                string.IsNullOrWhiteSpace(name)
                || name.Any(character => char.IsControl(character))
                || !RouteLocation.IsValidDecodedValue(name)
                || !unique.Add(name)
            )
                throw new RoutePatternConfigurationException(
                    RoutePatternConfigurationErrorKind.InvalidEnumName,
                    "Route enum names must be nonempty, unique, ordinal values without controls."
                );
        }
        Array.Sort(copy, StringComparer.Ordinal);
        return new(RouteValueKind.EnumName, copy);
    }

    internal bool Accepts(RouteValue value) =>
        value.Kind == Kind
        && (
            Kind != RouteValueKind.EnumName
            || _enumNames.Contains(value.EnumName, StringComparer.Ordinal)
        );

    internal bool TryParse(string text, out RouteValue value)
    {
        switch (Kind)
        {
            case RouteValueKind.Text:
                value = RouteValue.FromText(text);
                return true;
            case RouteValueKind.Signed32:
                if (
                    int.TryParse(
                        text,
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out var int32
                    )
                    && text == int32.ToString(CultureInfo.InvariantCulture)
                )
                {
                    value = RouteValue.FromSigned32(int32);
                    return true;
                }
                break;
            case RouteValueKind.Signed64:
                if (
                    long.TryParse(
                        text,
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out var int64
                    )
                    && text == int64.ToString(CultureInfo.InvariantCulture)
                )
                {
                    value = RouteValue.FromSigned64(int64);
                    return true;
                }
                break;
            case RouteValueKind.Uuid:
                if (
                    System.Guid.TryParseExact(text, "D", out var guid)
                    && text == guid.ToString("D", CultureInfo.InvariantCulture)
                )
                {
                    value = RouteValue.FromUuid(guid);
                    return true;
                }
                break;
            case RouteValueKind.Boolean:
                if (text == "true" || text == "false")
                {
                    value = RouteValue.FromBoolean(text == "true");
                    return true;
                }
                break;
            case RouteValueKind.EnumName:
                if (_enumNames.Contains(text, StringComparer.Ordinal))
                {
                    value = RouteValue.FromEnumName(text);
                    return true;
                }
                break;
        }
        value = default;
        return false;
    }

    internal bool Overlaps(RouteValueShape other)
    {
        if (Kind == RouteValueKind.Text || other.Kind == RouteValueKind.Text)
            return true;
        if (
            Kind is RouteValueKind.Signed32 or RouteValueKind.Signed64
            && other.Kind is RouteValueKind.Signed32 or RouteValueKind.Signed64
        )
            return true;
        if (Kind == other.Kind && Kind != RouteValueKind.EnumName)
            return true;
        if (Kind == RouteValueKind.EnumName)
            return _enumNames.Any(name => other.TryParse(name, out _));
        if (other.Kind == RouteValueKind.EnumName)
            return other._enumNames.Any(name => TryParse(name, out _));
        return false;
    }
}

/// <summary>One literal or required typed path segment.</summary>
public sealed class RouteSegmentPattern
{
    private RouteSegmentPattern(
        string? literal,
        string? parameterName,
        int captureSlot,
        RouteValueShape? shape
    )
    {
        Literal = literal;
        ParameterName = parameterName;
        CaptureSlot = captureSlot;
        Shape = shape;
    }

    /// <summary>Gets the decoded literal, or null for a parameter.</summary>
    public string? Literal { get; }

    /// <summary>Gets the generated parameter name, or null for a literal.</summary>
    public string? ParameterName { get; }

    /// <summary>Gets the generated capture slot, or -1 for a literal.</summary>
    public int CaptureSlot { get; }

    /// <summary>Gets the parameter shape, or null for a literal.</summary>
    public RouteValueShape? Shape { get; }

    /// <summary>Gets whether this is a literal segment.</summary>
    public bool IsLiteral => Literal is not null;

    /// <summary>Creates a decoded literal segment.</summary>
    public static RouteSegmentPattern LiteralSegment(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!RouteLocation.IsValidDecodedPathSegment(value))
            throw new RoutePatternConfigurationException(
                RoutePatternConfigurationErrorKind.InvalidLiteral,
                "A route literal must be one valid decoded path segment."
            );
        return new(value, null, -1, null);
    }

    /// <summary>Creates one required typed path parameter.</summary>
    public static RouteSegmentPattern Parameter(string name, int captureSlot, RouteValueShape shape)
    {
        ValidateParameter(name, captureSlot, shape);
        return new(null, name, captureSlot, shape);
    }

    internal static void ValidateParameter(string name, int captureSlot, RouteValueShape shape)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentOutOfRangeException.ThrowIfNegative(captureSlot);
        if (
            !char.IsLetter(name[0])
            || name.Any(character => !char.IsLetterOrDigit(character) && character != '_')
        )
            throw new RoutePatternConfigurationException(
                RoutePatternConfigurationErrorKind.InvalidParameterName,
                "Route parameter names must be identifiers."
            );
    }
}

/// <summary>An explicit typed default for an optional query parameter.</summary>
public sealed class RouteQueryDefault
{
    private RouteQueryDefault(bool hasValue, RouteValue value)
    {
        HasValue = hasValue;
        Value = value;
    }

    /// <summary>A required query parameter has no default.</summary>
    public static RouteQueryDefault Required { get; } = new(false, default);

    /// <summary>Gets whether this descriptor supplies a value when the key is absent.</summary>
    public bool HasValue { get; }

    /// <summary>Gets the typed default. Read this only when <see cref="HasValue"/> is true.</summary>
    public RouteValue Value { get; }

    /// <summary>Creates an explicit typed default.</summary>
    public static RouteQueryDefault FromValue(RouteValue value)
    {
        if (value.Kind == RouteValueKind.None)
            throw new ArgumentException(
                "A route query default must be initialized.",
                nameof(value)
            );
        return new(true, value);
    }
}

/// <summary>One known scalar query parameter.</summary>
public sealed class RouteQueryPattern
{
    private RouteQueryPattern(
        string key,
        string parameterName,
        int captureSlot,
        RouteValueShape shape,
        RouteQueryDefault defaultValue
    )
    {
        Key = key;
        ParameterName = parameterName;
        CaptureSlot = captureSlot;
        Shape = shape;
        Default = defaultValue;
    }

    /// <summary>Gets the decoded case-sensitive key.</summary>
    public string Key { get; }

    /// <summary>Gets the generated parameter name.</summary>
    public string ParameterName { get; }

    /// <summary>Gets the generated capture slot.</summary>
    public int CaptureSlot { get; }

    /// <summary>Gets the fixed built-in value shape.</summary>
    public RouteValueShape Shape { get; }

    /// <summary>Gets the explicit required/default descriptor.</summary>
    public RouteQueryDefault Default { get; }

    /// <summary>Creates a required scalar query parameter.</summary>
    public static RouteQueryPattern Required(
        string key,
        string parameterName,
        int captureSlot,
        RouteValueShape shape
    ) => Create(key, parameterName, captureSlot, shape, RouteQueryDefault.Required);

    /// <summary>Creates an optional scalar query parameter with a typed default.</summary>
    public static RouteQueryPattern Optional(
        string key,
        string parameterName,
        int captureSlot,
        RouteValueShape shape,
        RouteValue defaultValue
    ) => Create(key, parameterName, captureSlot, shape, RouteQueryDefault.FromValue(defaultValue));

    private static RouteQueryPattern Create(
        string key,
        string parameterName,
        int captureSlot,
        RouteValueShape shape,
        RouteQueryDefault defaultValue
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!RouteLocation.IsValidDecodedQueryKey(key))
            throw new RoutePatternConfigurationException(
                RoutePatternConfigurationErrorKind.InvalidQueryKey,
                "A route query key must be one nonempty decoded query component."
            );
        RouteSegmentPattern.ValidateParameter(parameterName, captureSlot, shape);
        if (defaultValue.HasValue && !shape.Accepts(defaultValue.Value))
            throw new RoutePatternConfigurationException(
                RoutePatternConfigurationErrorKind.DefaultShapeMismatch,
                "A route query default must have its parameter's fixed shape."
            );
        return new(key, parameterName, captureSlot, shape, defaultValue);
    }
}

/// <summary>The finite reason an authored route pattern was rejected.</summary>
public enum RoutePatternConfigurationErrorKind
{
    /// <summary>A literal segment is invalid.</summary>
    InvalidLiteral,

    /// <summary>A generated parameter name is invalid.</summary>
    InvalidParameterName,

    /// <summary>A query key is invalid.</summary>
    InvalidQueryKey,

    /// <summary>An enum has no names.</summary>
    EmptyEnum,

    /// <summary>An enum name is invalid or duplicated.</summary>
    InvalidEnumName,

    /// <summary>A query default does not have the declared shape.</summary>
    DefaultShapeMismatch,

    /// <summary>A capture slot is missing or duplicated.</summary>
    InvalidCaptureSlots,

    /// <summary>A query key is duplicated.</summary>
    DuplicateQueryKey,

    /// <summary>Formatted values do not satisfy location bounds or grammar.</summary>
    FormattedLocationRejected,
}

/// <summary>A redacted authored-pattern configuration failure.</summary>
public sealed class RoutePatternConfigurationException : ArgumentException
{
    internal RoutePatternConfigurationException(
        RoutePatternConfigurationErrorKind kind,
        string message
    )
        : base(message) => Kind = kind;

    /// <summary>Gets the finite failure reason.</summary>
    public RoutePatternConfigurationErrorKind Kind { get; }
}

/// <summary>The finite reason typed route formatting failed.</summary>
public enum RouteFormatErrorKind
{
    /// <summary>The number of generated values did not match the pattern.</summary>
    ValueCountMismatch,

    /// <summary>A generated value did not have the capture slot's shape.</summary>
    ValueShapeMismatch,

    /// <summary>The formatted location did not satisfy grammar or bounds.</summary>
    LocationRejected,
}

/// <summary>A structured redacted typed-route formatting failure.</summary>
public sealed class RouteFormatException : ArgumentException
{
    internal RouteFormatException(RouteFormatErrorKind kind, int captureSlot, string message)
        : base(message)
    {
        Kind = kind;
        CaptureSlot = captureSlot;
    }

    /// <summary>Gets the finite failure reason.</summary>
    public RouteFormatErrorKind Kind { get; }

    /// <summary>Gets the generated capture slot, or -1 when no one slot caused the failure.</summary>
    public int CaptureSlot { get; }
}

/// <summary>An immutable terminal path/query shape shared with generated route code.</summary>
public sealed class RoutePattern
{
    private readonly ReadOnlyCollection<RouteSegmentPattern> _segments;
    private readonly ReadOnlyCollection<RouteQueryPattern> _query;
    private readonly RouteValueShape[] _captureShapes;

    private RoutePattern(
        RouteDefinitionId id,
        RouteSegmentPattern[] segments,
        RouteQueryPattern[] query,
        RouteValueShape[] captureShapes,
        string diagnosticPattern
    )
    {
        Id = id;
        _segments = Array.AsReadOnly(segments);
        _query = Array.AsReadOnly(query);
        _captureShapes = captureShapes;
        DiagnosticPattern = diagnosticPattern;
    }

    /// <summary>Gets the stable generated terminal definition identity.</summary>
    public RouteDefinitionId Id { get; }

    /// <summary>Gets immutable path segments.</summary>
    public IReadOnlyList<RouteSegmentPattern> Segments => _segments;

    /// <summary>Gets immutable query declarations in generated formatter order.</summary>
    public IReadOnlyList<RouteQueryPattern> Query => _query;

    /// <summary>Gets the number of dense generated capture slots.</summary>
    public int CaptureCount => _captureShapes.Length;

    /// <summary>Gets a safe authored shape without parameter values or defaults.</summary>
    public string DiagnosticPattern { get; }

    /// <summary>Creates and validates one terminal route pattern.</summary>
    public static RoutePattern Create(
        RouteDefinitionId id,
        IReadOnlyList<RouteSegmentPattern> segments,
        IReadOnlyList<RouteQueryPattern>? query = null
    )
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(segments);
        var segmentCopy = segments.ToArray();
        var queryCopy = query?.ToArray() ?? [];
        if (segmentCopy.Any(segment => segment is null) || queryCopy.Any(item => item is null))
            throw new ArgumentException("Route pattern entries cannot be null.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in queryCopy)
        {
            if (!keys.Add(item.Key))
                throw new RoutePatternConfigurationException(
                    RoutePatternConfigurationErrorKind.DuplicateQueryKey,
                    "A route pattern cannot declare a query key more than once."
                );
        }
        var captures = segmentCopy
            .Where(segment => !segment.IsLiteral)
            .Select(segment => (segment.CaptureSlot, segment.Shape!))
            .Concat(queryCopy.Select(item => (item.CaptureSlot, item.Shape)))
            .OrderBy(item => item.CaptureSlot)
            .ToArray();
        if (
            captures.Length != 0
            && captures.Where((item, index) => item.CaptureSlot != index).Any()
        )
            throw new RoutePatternConfigurationException(
                RoutePatternConfigurationErrorKind.InvalidCaptureSlots,
                "Route capture slots must be unique and dense from zero."
            );
        var shapes = captures.Select(item => item.Item2).ToArray();
        return new(
            id,
            segmentCopy,
            queryCopy,
            shapes,
            BuildDiagnosticPattern(segmentCopy, queryCopy)
        );
    }

    /// <summary>Formats generated typed values into one canonical bounded location.</summary>
    public RouteLocation Format(ReadOnlySpan<RouteValue> values, RouteLocationLimits? limits = null)
    {
        if (values.Length != CaptureCount)
            throw new RouteFormatException(
                RouteFormatErrorKind.ValueCountMismatch,
                -1,
                "The route value count does not match the generated pattern."
            );
        for (var index = 0; index < values.Length; index++)
        {
            if (!_captureShapes[index].Accepts(values[index]))
                throw new RouteFormatException(
                    RouteFormatErrorKind.ValueShapeMismatch,
                    index,
                    "A route value does not have its generated capture shape."
                );
        }
        var path = new string[_segments.Count];
        for (var index = 0; index < path.Length; index++)
            path[index] =
                _segments[index].Literal ?? values[_segments[index].CaptureSlot].CanonicalText();
        var query = new List<RouteQueryPair>(_query.Count);
        foreach (var item in _query)
        {
            var value = values[item.CaptureSlot];
            if (item.Default.HasValue && value.Equals(item.Default.Value))
                continue;
            query.Add(new(item.Key, value.CanonicalText(), hasValue: true));
        }
        try
        {
            return RouteLocation.CreateCanonical(
                path,
                query,
                limits ?? RouteLocationLimits.Default
            );
        }
        catch (RoutePatternConfigurationException error)
            when (error.Kind == RoutePatternConfigurationErrorKind.FormattedLocationRejected)
        {
            throw new RouteFormatException(
                RouteFormatErrorKind.LocationRejected,
                -1,
                "Generated route values do not satisfy the configured location grammar and limits."
            );
        }
    }

    /// <summary>Returns the authored diagnostic pattern without application values.</summary>
    public override string ToString() => $"route-pattern id={Id} shape={DiagnosticPattern}";

    internal RouteValueShape CaptureShape(int slot) => _captureShapes[slot];

    private static string BuildDiagnosticPattern(
        RouteSegmentPattern[] segments,
        RouteQueryPattern[] query
    )
    {
        var path =
            "/"
            + string.Join(
                '/',
                segments.Select(segment =>
                    segment.IsLiteral
                        ? RouteLocation.CanonicalizePathSegment(segment.Literal!)
                        : $"{{{segment.ParameterName}:{segment.Shape!.Kind}}}"
                )
            );
        if (query.Length == 0)
            return path;
        return path
            + "?"
            + string.Join(
                '&',
                query.Select(item =>
                    RouteLocation.CanonicalizeQueryComponent(item.Key)
                    + "={"
                    + item.ParameterName
                    + ":"
                    + item.Shape.Kind
                    + (item.Default.HasValue ? "?}" : "}")
                )
            );
    }
}
