using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Lucent.Core;

/// <summary>The finite reason a combined route table was rejected.</summary>
public enum RouteTableConfigurationErrorKind
{
    /// <summary>Two patterns use the same stable definition identity.</summary>
    DuplicateDefinitionId,

    /// <summary>Two terminal path languages overlap without a precedence winner.</summary>
    AmbiguousPath,

    /// <summary>An authored pattern cannot fit within this table's limits.</summary>
    PatternExceedsLimits,
}

/// <summary>Structured authored route-table conflict data.</summary>
public sealed record RouteTableConfigurationError(
    RouteTableConfigurationErrorKind Kind,
    RouteDefinitionId First,
    RouteDefinitionId Second
);

/// <summary>A deterministic combined-table configuration failure.</summary>
public sealed class RouteTableConfigurationException : ArgumentException
{
    private readonly ReadOnlyCollection<RouteTableConfigurationError> _errors;

    internal RouteTableConfigurationException(RouteTableConfigurationError[] errors)
        : base(
            "The route table contains "
                + errors.Length.ToString(CultureInfo.InvariantCulture)
                + " deterministic configuration conflict(s)."
        ) => _errors = Array.AsReadOnly(errors);

    /// <summary>Gets immutable structured conflicts ordered by definition identity.</summary>
    public IReadOnlyList<RouteTableConfigurationError> Errors => _errors;
}

/// <summary>The finite result of matching a canonical location.</summary>
public enum RouteMatchStatus
{
    /// <summary>The location exceeds this table's configured limits.</summary>
    RejectedLocation,

    /// <summary>One route matched and its values were decoded.</summary>
    Matched,

    /// <summary>No terminal path shape accepted the location.</summary>
    NotFound,

    /// <summary>The selected path's query was invalid.</summary>
    RejectedQuery,
}

/// <summary>The finite reason a selected route rejected its query.</summary>
public enum RouteMatchErrorKind
{
    /// <summary>No match error occurred.</summary>
    None,

    /// <summary>A query key was not declared by the selected route.</summary>
    UnknownQueryKey,

    /// <summary>A scalar query key occurred more than once.</summary>
    DuplicateQueryValue,

    /// <summary>A non-string query parameter omitted its equals sign and value.</summary>
    QueryValueRequired,

    /// <summary>A query value did not have the declared canonical built-in shape.</summary>
    InvalidQueryValue,

    /// <summary>A required query parameter was absent.</summary>
    MissingRequiredQuery,
}

/// <summary>Structured query rejection data without supplied keys or values.</summary>
public readonly record struct RouteMatchError(
    RouteMatchErrorKind Kind,
    int QueryIndex,
    int CaptureSlot
)
{
    /// <summary>Gets whether this value describes a rejection.</summary>
    public bool HasError => Kind != RouteMatchErrorKind.None;
}

/// <summary>The safe outcome for one authored candidate.</summary>
public enum RouteCandidateOutcome
{
    /// <summary>The path shape did not match.</summary>
    PathMismatch,

    /// <summary>A higher-precedence path matched.</summary>
    LowerPrecedence,

    /// <summary>The selected path matched but its query was rejected.</summary>
    QueryRejected,

    /// <summary>The route matched.</summary>
    Matched,
}

/// <summary>Safe deterministic candidate data without application values.</summary>
public sealed record RouteCandidateDiagnostic(
    RouteDefinitionId DefinitionId,
    RouteCandidateOutcome Outcome
);

/// <summary>An immutable terminal route match with indexed typed captures.</summary>
public sealed class RouteMatch
{
    private readonly RouteValue[] _values;

    internal RouteMatch(RoutePattern pattern, RouteLocation location, RouteValue[] values)
    {
        Pattern = pattern;
        Location = location;
        _values = values.ToArray();
    }

    /// <summary>Gets the sole authoritative terminal pattern.</summary>
    public RoutePattern Pattern { get; }

    /// <summary>Gets the matched canonical location.</summary>
    public RouteLocation Location { get; }

    /// <summary>Gets the stable generated terminal definition identity.</summary>
    public RouteDefinitionId DefinitionId => Pattern.Id;

    /// <summary>Gets the number of dense generated capture slots.</summary>
    public int ValueCount => _values.Length;

    /// <summary>Gets one generated typed capture by its stable slot.</summary>
    public RouteValue GetValue(int captureSlot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(captureSlot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(captureSlot, _values.Length);
        return _values[captureSlot];
    }

    /// <summary>Returns a redacted match description.</summary>
    public override string ToString() =>
        $"route-match id={DefinitionId} values={ValueCount.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>A structured match result with deterministic redacted candidate evidence.</summary>
public sealed class RouteMatchResult
{
    private readonly ReadOnlyCollection<RouteCandidateDiagnostic> _candidates;

    internal RouteMatchResult(
        RouteMatchStatus status,
        RouteMatch? match,
        RouteLocationError locationError,
        RouteMatchError error,
        RouteCandidateDiagnostic[] candidates
    )
    {
        Status = status;
        Match = match;
        LocationError = locationError;
        Error = error;
        _candidates = Array.AsReadOnly(candidates);
    }

    /// <summary>Gets the finite outcome.</summary>
    public RouteMatchStatus Status { get; }

    /// <summary>Gets the match, or null for a rejected or unmatched location.</summary>
    public RouteMatch? Match { get; }

    /// <summary>Gets a structured table-limit rejection.</summary>
    public RouteLocationError LocationError { get; }

    /// <summary>Gets structured query rejection data.</summary>
    public RouteMatchError Error { get; }

    /// <summary>Gets safe candidate evidence ordered by definition identity.</summary>
    public IReadOnlyList<RouteCandidateDiagnostic> Candidates => _candidates;

    /// <summary>Returns a deterministic redacted result dump.</summary>
    public string Dump()
    {
        var output = new StringBuilder();
        output
            .Append("route-match status=")
            .Append(Status)
            .Append(" location-error=")
            .Append(LocationError.Kind)
            .Append(" location-offset=")
            .Append(LocationError.Utf16Offset.ToString(CultureInfo.InvariantCulture))
            .Append(" error=")
            .Append(Error.Kind)
            .Append(" query-index=")
            .Append(Error.QueryIndex.ToString(CultureInfo.InvariantCulture))
            .Append(" capture-slot=")
            .Append(Error.CaptureSlot.ToString(CultureInfo.InvariantCulture));
        foreach (var candidate in _candidates)
        {
            output
                .Append('\n')
                .Append("candidate id=")
                .Append(candidate.DefinitionId.Value)
                .Append(" outcome=")
                .Append(candidate.Outcome);
        }
        return output.Append('\n').ToString();
    }

    /// <summary>Returns the same redacted representation as <see cref="Dump"/>.</summary>
    public override string ToString() => Dump();
}

/// <summary>An immutable authoritative table of terminal route patterns.</summary>
public sealed class RouteTable
{
    private readonly ReadOnlyCollection<RoutePattern> _patterns;

    private RouteTable(RoutePattern[] patterns, RouteLocationLimits limits)
    {
        _patterns = Array.AsReadOnly(patterns);
        Limits = limits;
    }

    /// <summary>Gets terminal patterns in stable definition-identity order.</summary>
    public IReadOnlyList<RoutePattern> Patterns => _patterns;

    /// <summary>Gets the limits that raw activation parsing must use with this table.</summary>
    public RouteLocationLimits Limits { get; }

    /// <summary>Creates the one authoritative table and rejects combined-module conflicts.</summary>
    public static RouteTable Create(
        IReadOnlyList<RoutePattern> patterns,
        RouteLocationLimits? limits = null
    )
    {
        ArgumentNullException.ThrowIfNull(patterns);
        var effectiveLimits = limits ?? RouteLocationLimits.Default;
        var copy = patterns.ToArray();
        if (copy.Any(pattern => pattern is null))
            throw new ArgumentException("Route table patterns cannot be null.", nameof(patterns));
        Array.Sort(
            copy,
            static (left, right) => StringComparer.Ordinal.Compare(left.Id.Value, right.Id.Value)
        );
        var errors = new List<RouteTableConfigurationError>();
        for (var left = 0; left < copy.Length; left++)
        {
            if (!PatternFitsLimits(copy[left], effectiveLimits))
                errors.Add(
                    new(
                        RouteTableConfigurationErrorKind.PatternExceedsLimits,
                        copy[left].Id,
                        copy[left].Id
                    )
                );
            for (var right = left + 1; right < copy.Length; right++)
            {
                if (copy[left].Id == copy[right].Id)
                    errors.Add(
                        new(
                            RouteTableConfigurationErrorKind.DuplicateDefinitionId,
                            copy[left].Id,
                            copy[right].Id
                        )
                    );
                else if (PathsAreAmbiguous(copy[left], copy[right]))
                    errors.Add(
                        new(
                            RouteTableConfigurationErrorKind.AmbiguousPath,
                            copy[left].Id,
                            copy[right].Id
                        )
                    );
            }
        }
        if (errors.Count != 0)
            throw new RouteTableConfigurationException(errors.ToArray());
        return new(copy, effectiveLimits);
    }

    /// <summary>Matches a canonical location without side effects.</summary>
    public RouteMatchResult Match(RouteLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        var locationError = location.ValidateLimits(Limits);
        if (locationError.HasError)
            return new(RouteMatchStatus.RejectedLocation, null, locationError, default, []);
        var pathMatches = new List<PathMatch>();
        var outcomes = new Dictionary<RouteDefinitionId, RouteCandidateOutcome>();
        foreach (var pattern in _patterns)
        {
            if (TryMatchPath(pattern, location, out var values))
            {
                pathMatches.Add(new(pattern, values));
                outcomes.Add(pattern.Id, RouteCandidateOutcome.LowerPrecedence);
            }
            else
                outcomes.Add(pattern.Id, RouteCandidateOutcome.PathMismatch);
        }
        if (pathMatches.Count == 0)
            return Result(RouteMatchStatus.NotFound, null, default, outcomes);

        var winner = pathMatches[0];
        for (var index = 1; index < pathMatches.Count; index++)
        {
            if (ComparePrecedence(pathMatches[index].Pattern, winner.Pattern) > 0)
                winner = pathMatches[index];
        }
        var queryError = MatchQuery(winner.Pattern, location, winner.Values);
        if (queryError.HasError)
        {
            outcomes[winner.Pattern.Id] = RouteCandidateOutcome.QueryRejected;
            return Result(RouteMatchStatus.RejectedQuery, null, queryError, outcomes);
        }
        outcomes[winner.Pattern.Id] = RouteCandidateOutcome.Matched;
        return Result(
            RouteMatchStatus.Matched,
            new(winner.Pattern, location, winner.Values),
            default,
            outcomes
        );
    }

    /// <summary>Returns the stable authored table without route values.</summary>
    public string Dump()
    {
        var output = new StringBuilder("route-table patterns=")
            .Append(_patterns.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" limits=")
            .Append(Limits.MaximumUtf8Bytes.ToString(CultureInfo.InvariantCulture))
            .Append('/')
            .Append(Limits.MaximumSegments.ToString(CultureInfo.InvariantCulture))
            .Append('/')
            .Append(Limits.MaximumSegmentUtf8Bytes.ToString(CultureInfo.InvariantCulture))
            .Append('/')
            .Append(Limits.MaximumQueryPairs.ToString(CultureInfo.InvariantCulture))
            .Append('/')
            .Append(Limits.MaximumQueryKeyUtf8Bytes.ToString(CultureInfo.InvariantCulture))
            .Append('/')
            .Append(Limits.MaximumQueryValueUtf8Bytes.ToString(CultureInfo.InvariantCulture));
        foreach (var pattern in _patterns)
        {
            output
                .Append('\n')
                .Append("pattern id=")
                .Append(pattern.Id.Value)
                .Append(" shape=")
                .Append(pattern.DiagnosticPattern);
        }
        return output.Append('\n').ToString();
    }

    /// <summary>Returns the same redacted representation as <see cref="Dump"/>.</summary>
    public override string ToString() => Dump();

    private static RouteMatchResult Result(
        RouteMatchStatus status,
        RouteMatch? match,
        RouteMatchError error,
        IReadOnlyDictionary<RouteDefinitionId, RouteCandidateOutcome> outcomes
    ) =>
        new(
            status,
            match,
            default,
            error,
            outcomes
                .OrderBy(item => item.Key.Value, StringComparer.Ordinal)
                .Select(item => new RouteCandidateDiagnostic(item.Key, item.Value))
                .ToArray()
        );

    private static bool TryMatchPath(
        RoutePattern pattern,
        RouteLocation location,
        out RouteValue[] values
    )
    {
        values = new RouteValue[pattern.CaptureCount];
        if (pattern.Segments.Count != location.Segments.Count)
            return false;
        for (var index = 0; index < pattern.Segments.Count; index++)
        {
            var segment = pattern.Segments[index];
            if (segment.IsLiteral)
            {
                if (
                    !string.Equals(
                        segment.Literal,
                        location.Segments[index],
                        StringComparison.Ordinal
                    )
                )
                    return false;
            }
            else if (
                !segment.Shape!.TryParse(location.Segments[index], out values[segment.CaptureSlot])
            )
                return false;
        }
        foreach (var query in pattern.Query)
        {
            if (query.Default.HasValue)
                values[query.CaptureSlot] = query.Default.Value;
        }
        return true;
    }

    private static RouteMatchError MatchQuery(
        RoutePattern pattern,
        RouteLocation location,
        RouteValue[] values
    )
    {
        var declarations = pattern.Query.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var supplied = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < location.Query.Count; index++)
        {
            var pair = location.Query[index];
            if (!declarations.TryGetValue(pair.Key, out var declaration))
                return new(RouteMatchErrorKind.UnknownQueryKey, index, -1);
            if (!supplied.Add(pair.Key))
                return new(RouteMatchErrorKind.DuplicateQueryValue, index, declaration.CaptureSlot);
            if (!pair.HasValue && declaration.Shape.Kind != RouteValueKind.Text)
                return new(RouteMatchErrorKind.QueryValueRequired, index, declaration.CaptureSlot);
            if (!declaration.Shape.TryParse(pair.Value, out var value))
                return new(RouteMatchErrorKind.InvalidQueryValue, index, declaration.CaptureSlot);
            values[declaration.CaptureSlot] = value;
        }
        foreach (var declaration in pattern.Query)
        {
            if (!declaration.Default.HasValue && !supplied.Contains(declaration.Key))
                return new(RouteMatchErrorKind.MissingRequiredQuery, -1, declaration.CaptureSlot);
        }
        return default;
    }

    private static bool PathsAreAmbiguous(RoutePattern left, RoutePattern right)
    {
        if (left.Segments.Count != right.Segments.Count)
            return false;
        var precedence = 0;
        for (var index = 0; index < left.Segments.Count; index++)
        {
            var leftSegment = left.Segments[index];
            var rightSegment = right.Segments[index];
            if (!SegmentsOverlap(leftSegment, rightSegment))
                return false;
            if (precedence == 0)
                precedence = Category(leftSegment).CompareTo(Category(rightSegment));
        }
        return precedence == 0;
    }

    private static bool PatternFitsLimits(RoutePattern pattern, RouteLocationLimits limits)
    {
        try
        {
            var path = pattern
                .Segments.Select(segment =>
                    segment.Literal ?? MinimumText(segment.Shape!, path: true)
                )
                .ToArray();
            var query = pattern
                .Query.Where(item => !item.Default.HasValue)
                .Select(item => new RouteQueryPair(
                    item.Key,
                    MinimumText(item.Shape, path: false),
                    true
                ))
                .ToArray();
            _ = RouteLocation.CreateCanonical(path, query, limits);
            return true;
        }
        catch (RoutePatternConfigurationException error)
            when (error.Kind == RoutePatternConfigurationErrorKind.FormattedLocationRejected)
        {
            return false;
        }
    }

    private static string MinimumText(RouteValueShape shape, bool path) =>
        shape.Kind switch
        {
            RouteValueKind.Text => path ? "x" : string.Empty,
            RouteValueKind.Signed32 or RouteValueKind.Signed64 => "0",
            RouteValueKind.Uuid => "00000000-0000-0000-0000-000000000000",
            RouteValueKind.Boolean => "true",
            RouteValueKind.EnumName => shape
                .EnumNames.OrderBy(value =>
                    path
                        ? RouteLocation.CanonicalizePathSegment(value).Length
                        : RouteLocation.CanonicalizeQueryComponent(value).Length
                )
                .ThenBy(value => value, StringComparer.Ordinal)
                .First(),
            _ => throw new InvalidOperationException("A route shape is uninitialized."),
        };

    private static bool SegmentsOverlap(RouteSegmentPattern left, RouteSegmentPattern right)
    {
        if (left.IsLiteral && right.IsLiteral)
            return string.Equals(left.Literal, right.Literal, StringComparison.Ordinal);
        if (left.IsLiteral)
            return right.Shape!.TryParse(left.Literal!, out _);
        if (right.IsLiteral)
            return left.Shape!.TryParse(right.Literal!, out _);
        return left.Shape!.Overlaps(right.Shape!);
    }

    private static int ComparePrecedence(RoutePattern left, RoutePattern right)
    {
        for (var index = 0; index < left.Segments.Count; index++)
        {
            var comparison = Category(left.Segments[index])
                .CompareTo(Category(right.Segments[index]));
            if (comparison != 0)
                return comparison;
        }
        return 0;
    }

    private static int Category(RouteSegmentPattern segment) =>
        segment.IsLiteral ? 2
        : segment.Shape!.Kind == RouteValueKind.Text ? 0
        : 1;

    private sealed record PathMatch(RoutePattern Pattern, RouteValue[] Values);
}
