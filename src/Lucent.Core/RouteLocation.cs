using System.Collections.ObjectModel;
using System.Text;

namespace Lucent.Core;

/// <summary>Finite limits applied to supplied and generated in-app route locations.</summary>
public sealed record RouteLocationLimits
{
    /// <summary>Creates route-location limits.</summary>
    public RouteLocationLimits(
        int maximumUtf8Bytes = 2048,
        int maximumSegments = 32,
        int maximumSegmentUtf8Bytes = 256,
        int maximumQueryPairs = 32,
        int maximumQueryKeyUtf8Bytes = 64,
        int maximumQueryValueUtf8Bytes = 1024
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumUtf8Bytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSegments);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSegmentUtf8Bytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumQueryPairs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumQueryKeyUtf8Bytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumQueryValueUtf8Bytes);
        MaximumUtf8Bytes = maximumUtf8Bytes;
        MaximumSegments = maximumSegments;
        MaximumSegmentUtf8Bytes = maximumSegmentUtf8Bytes;
        MaximumQueryPairs = maximumQueryPairs;
        MaximumQueryKeyUtf8Bytes = maximumQueryKeyUtf8Bytes;
        MaximumQueryValueUtf8Bytes = maximumQueryValueUtf8Bytes;
    }

    /// <summary>The shared default limits.</summary>
    public static RouteLocationLimits Default { get; } = new();

    /// <summary>Gets the maximum UTF-8 byte count for supplied and canonical text.</summary>
    public int MaximumUtf8Bytes { get; }

    /// <summary>Gets the maximum number of path segments.</summary>
    public int MaximumSegments { get; }

    /// <summary>Gets the maximum decoded UTF-8 byte count of one path segment.</summary>
    public int MaximumSegmentUtf8Bytes { get; }

    /// <summary>Gets the maximum number of query pairs.</summary>
    public int MaximumQueryPairs { get; }

    /// <summary>Gets the maximum decoded UTF-8 byte count of one query key.</summary>
    public int MaximumQueryKeyUtf8Bytes { get; }

    /// <summary>Gets the maximum decoded UTF-8 byte count of one query value.</summary>
    public int MaximumQueryValueUtf8Bytes { get; }
}

/// <summary>A decoded query pair whose position and value-presence are preserved.</summary>
public sealed class RouteQueryPair
{
    internal RouteQueryPair(string key, string value, bool hasValue)
    {
        Key = key;
        Value = value;
        HasValue = hasValue;
    }

    /// <summary>Gets the decoded, case-sensitive key.</summary>
    public string Key { get; }

    /// <summary>Gets the decoded value, or the empty string when no equals sign was supplied.</summary>
    public string Value { get; }

    /// <summary>Gets whether the pair included an equals sign and value.</summary>
    public bool HasValue { get; }
}

/// <summary>The finite reason an in-app location was rejected.</summary>
public enum RouteLocationErrorKind
{
    /// <summary>No error occurred.</summary>
    None,

    /// <summary>The supplied location was null or empty.</summary>
    Empty,

    /// <summary>The supplied location was not an absolute in-app path.</summary>
    NotAbsolutePath,

    /// <summary>The supplied location began with an authority marker.</summary>
    AuthorityNotAllowed,

    /// <summary>A fragment was supplied.</summary>
    FragmentNotAllowed,

    /// <summary>A raw or decoded backslash was supplied.</summary>
    BackslashNotAllowed,

    /// <summary>A raw or decoded control character was supplied.</summary>
    ControlCharacterNotAllowed,

    /// <summary>A character is outside the in-app location grammar.</summary>
    InvalidCharacter,

    /// <summary>The input contains invalid Unicode.</summary>
    InvalidUnicode,

    /// <summary>A percent escape is incomplete or malformed.</summary>
    InvalidPercentEncoding,

    /// <summary>Percent-encoded bytes are not strict UTF-8.</summary>
    InvalidUtf8,

    /// <summary>A non-root path ended in a slash or contained an empty segment.</summary>
    EmptyPathSegment,

    /// <summary>A decoded path segment was dot or dot-dot.</summary>
    DotPathSegment,

    /// <summary>The supplied location exceeded its UTF-8 byte bound.</summary>
    InputTooLong,

    /// <summary>The canonical location exceeded its UTF-8 byte bound.</summary>
    CanonicalTooLong,

    /// <summary>The path contains too many segments.</summary>
    TooManySegments,

    /// <summary>A decoded path segment is too long.</summary>
    PathSegmentTooLong,

    /// <summary>A question mark was not followed by a query pair.</summary>
    EmptyQuery,

    /// <summary>The query contains an empty pair.</summary>
    EmptyQueryPair,

    /// <summary>A query pair contains an empty key.</summary>
    EmptyQueryKey,

    /// <summary>The query contains too many pairs.</summary>
    TooManyQueryPairs,

    /// <summary>A decoded query key is too long.</summary>
    QueryKeyTooLong,

    /// <summary>A decoded query value is too long.</summary>
    QueryValueTooLong,
}

/// <summary>Structured rejection data that never contains supplied route text.</summary>
public readonly record struct RouteLocationError(RouteLocationErrorKind Kind, int Utf16Offset)
{
    /// <summary>Gets whether this value describes a rejection.</summary>
    public bool HasError => Kind != RouteLocationErrorKind.None;
}

/// <summary>The result of parsing an untrusted in-app route location.</summary>
public sealed class RouteLocationParseResult
{
    internal RouteLocationParseResult(RouteLocation? location, RouteLocationError error)
    {
        Location = location;
        Error = error;
    }

    /// <summary>Gets whether parsing succeeded.</summary>
    public bool Succeeded => Location is not null;

    /// <summary>Gets the parsed location, or null when rejected.</summary>
    public RouteLocation? Location { get; }

    /// <summary>Gets structured rejection data.</summary>
    public RouteLocationError Error { get; }
}

/// <summary>An immutable canonical in-app path and optional ordered query.</summary>
public sealed class RouteLocation : IEquatable<RouteLocation>
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ReadOnlyCollection<string> _segments;
    private readonly ReadOnlyCollection<RouteQueryPair> _query;

    private RouteLocation(string canonicalText, string[] segments, RouteQueryPair[] query)
    {
        CanonicalText = canonicalText;
        _segments = Array.AsReadOnly(segments);
        _query = Array.AsReadOnly(query);
    }

    /// <summary>Gets the canonical location text. This may contain application values.</summary>
    public string CanonicalText { get; }

    /// <summary>Gets decoded path segments.</summary>
    public IReadOnlyList<string> Segments => _segments;

    /// <summary>Gets decoded query pairs in supplied order.</summary>
    public IReadOnlyList<RouteQueryPair> Query => _query;

    /// <summary>Parses and canonicalizes an untrusted in-app location.</summary>
    public static RouteLocationParseResult Parse(string? text, RouteLocationLimits? limits = null)
    {
        limits ??= RouteLocationLimits.Default;
        if (string.IsNullOrEmpty(text))
            return Failure(RouteLocationErrorKind.Empty, 0);
        if (text.Length > limits.MaximumUtf8Bytes)
            return Failure(RouteLocationErrorKind.InputTooLong, limits.MaximumUtf8Bytes);
        if (!TryGetUtf8ByteCount(text, out var suppliedBytes))
            return Failure(RouteLocationErrorKind.InvalidUnicode, FirstInvalidUnicode(text));
        if (suppliedBytes > limits.MaximumUtf8Bytes)
            return Failure(RouteLocationErrorKind.InputTooLong, text.Length);
        if (text[0] != '/')
            return Failure(RouteLocationErrorKind.NotAbsolutePath, 0);
        if (text.Length > 1 && text[1] == '/')
            return Failure(RouteLocationErrorKind.AuthorityNotAllowed, 1);

        var fragment = text.IndexOf('#');
        if (fragment >= 0)
            return Failure(RouteLocationErrorKind.FragmentNotAllowed, fragment);
        var rawBackslash = text.IndexOf('\\');
        if (rawBackslash >= 0)
            return Failure(RouteLocationErrorKind.BackslashNotAllowed, rawBackslash);
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsControl(text[index]))
                return Failure(RouteLocationErrorKind.ControlCharacterNotAllowed, index);
        }

        var queryMarker = text.IndexOf('?');
        var pathEnd = queryMarker < 0 ? text.Length : queryMarker;
        if (pathEnd > 1 && text[pathEnd - 1] == '/')
            return Failure(RouteLocationErrorKind.EmptyPathSegment, pathEnd - 1);

        var segments = new List<string>();
        var canonical = new StringBuilder(text.Length);
        canonical.Append('/');
        if (pathEnd > 1)
        {
            var start = 1;
            while (start < pathEnd)
            {
                var slash = text.IndexOf('/', start, pathEnd - start);
                var end = slash < 0 ? pathEnd : slash;
                if (end == start)
                    return Failure(RouteLocationErrorKind.EmptyPathSegment, start);
                if (segments.Count == limits.MaximumSegments)
                    return Failure(RouteLocationErrorKind.TooManySegments, start);
                var decoded = DecodeComponent(text, start, end, ComponentKind.Path, out var error);
                if (decoded is null)
                    return new(null, error);
                if (decoded is "." or "..")
                    return Failure(RouteLocationErrorKind.DotPathSegment, start);
                if (StrictUtf8.GetByteCount(decoded) > limits.MaximumSegmentUtf8Bytes)
                    return Failure(RouteLocationErrorKind.PathSegmentTooLong, start);
                if (segments.Count != 0)
                    canonical.Append('/');
                AppendCanonical(canonical, decoded, ComponentKind.Path);
                segments.Add(decoded);
                start = end + 1;
            }
        }

        var query = new List<RouteQueryPair>();
        if (queryMarker >= 0)
        {
            if (queryMarker == text.Length - 1)
                return Failure(RouteLocationErrorKind.EmptyQuery, queryMarker);
            canonical.Append('?');
            var start = queryMarker + 1;
            while (start <= text.Length)
            {
                var ampersand = text.IndexOf('&', start);
                var end = ampersand < 0 ? text.Length : ampersand;
                if (end == start)
                    return Failure(RouteLocationErrorKind.EmptyQueryPair, start);
                if (query.Count == limits.MaximumQueryPairs)
                    return Failure(RouteLocationErrorKind.TooManyQueryPairs, start);
                var equals = text.IndexOf('=', start, end - start);
                var keyEnd = equals < 0 ? end : equals;
                if (keyEnd == start)
                    return Failure(RouteLocationErrorKind.EmptyQueryKey, start);
                var key = DecodeComponent(text, start, keyEnd, ComponentKind.Query, out var error);
                if (key is null)
                    return new(null, error);
                var hasValue = equals >= 0;
                var value = hasValue
                    ? DecodeComponent(text, equals + 1, end, ComponentKind.Query, out error)
                    : string.Empty;
                if (value is null)
                    return new(null, error);
                if (StrictUtf8.GetByteCount(key) > limits.MaximumQueryKeyUtf8Bytes)
                    return Failure(RouteLocationErrorKind.QueryKeyTooLong, start);
                if (StrictUtf8.GetByteCount(value) > limits.MaximumQueryValueUtf8Bytes)
                    return Failure(
                        RouteLocationErrorKind.QueryValueTooLong,
                        hasValue ? equals + 1 : keyEnd
                    );
                if (query.Count != 0)
                    canonical.Append('&');
                AppendCanonical(canonical, key, ComponentKind.Query);
                if (hasValue)
                {
                    canonical.Append('=');
                    AppendCanonical(canonical, value, ComponentKind.Query);
                }
                query.Add(new(key, value, hasValue));
                if (ampersand < 0)
                    break;
                start = end + 1;
            }
        }

        var canonicalText = canonical.ToString();
        if (StrictUtf8.GetByteCount(canonicalText) > limits.MaximumUtf8Bytes)
            return Failure(RouteLocationErrorKind.CanonicalTooLong, text.Length);
        return new(new(canonicalText, segments.ToArray(), query.ToArray()), default);
    }

    /// <inheritdoc />
    public bool Equals(RouteLocation? other) =>
        other is not null
        && string.Equals(CanonicalText, other.CanonicalText, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RouteLocation other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(CanonicalText);

    /// <summary>Returns a redacted description rather than application route values.</summary>
    public override string ToString() =>
        $"route-location segments={Segments.Count} query={Query.Count}";

    internal static RouteLocation CreateCanonical(
        IReadOnlyList<string> segments,
        IReadOnlyList<RouteQueryPair> query,
        RouteLocationLimits limits
    )
    {
        if (segments.Count > limits.MaximumSegments || query.Count > limits.MaximumQueryPairs)
            throw FormattedLocationRejected();
        long canonicalLength = 1;
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (
                !IsValidDecodedPathSegment(segment)
                || StrictUtf8.GetByteCount(segment) > limits.MaximumSegmentUtf8Bytes
            )
                throw FormattedLocationRejected();
            canonicalLength += (index == 0 ? 0 : 1) + CanonicalLength(segment, ComponentKind.Path);
            if (canonicalLength > limits.MaximumUtf8Bytes)
                throw FormattedLocationRejected();
        }
        if (query.Count != 0)
        {
            canonicalLength++;
            for (var index = 0; index < query.Count; index++)
            {
                var pair = query[index];
                if (
                    !IsValidDecodedQueryKey(pair.Key)
                    || !IsValidDecodedComponent(pair.Value)
                    || StrictUtf8.GetByteCount(pair.Key) > limits.MaximumQueryKeyUtf8Bytes
                    || StrictUtf8.GetByteCount(pair.Value) > limits.MaximumQueryValueUtf8Bytes
                )
                    throw FormattedLocationRejected();
                canonicalLength +=
                    (index == 0 ? 0 : 1) + CanonicalLength(pair.Key, ComponentKind.Query);
                if (pair.HasValue)
                    canonicalLength += 1 + CanonicalLength(pair.Value, ComponentKind.Query);
                if (canonicalLength > limits.MaximumUtf8Bytes)
                    throw FormattedLocationRejected();
            }
        }

        var canonical = new StringBuilder((int)canonicalLength);
        canonical.Append('/');
        for (var index = 0; index < segments.Count; index++)
        {
            if (index != 0)
                canonical.Append('/');
            AppendCanonical(canonical, segments[index], ComponentKind.Path);
        }
        if (query.Count != 0)
        {
            canonical.Append('?');
            for (var index = 0; index < query.Count; index++)
            {
                if (index != 0)
                    canonical.Append('&');
                AppendCanonical(canonical, query[index].Key, ComponentKind.Query);
                if (query[index].HasValue)
                {
                    canonical.Append('=');
                    AppendCanonical(canonical, query[index].Value, ComponentKind.Query);
                }
            }
        }
        return new(canonical.ToString(), segments.ToArray(), query.ToArray());
    }

    internal static bool IsValidDecodedPathSegment(string value) =>
        !string.IsNullOrEmpty(value)
        && value is not "." and not ".."
        && IsValidDecodedComponent(value);

    internal static bool IsValidDecodedQueryKey(string value) =>
        !string.IsNullOrEmpty(value) && IsValidDecodedComponent(value);

    internal static bool IsValidDecodedValue(string value) => IsValidDecodedComponent(value);

    internal static string CanonicalizePathSegment(string value)
    {
        var output = new StringBuilder(value.Length);
        AppendCanonical(output, value, ComponentKind.Path);
        return output.ToString();
    }

    internal static string CanonicalizeQueryComponent(string value)
    {
        var output = new StringBuilder(value.Length);
        AppendCanonical(output, value, ComponentKind.Query);
        return output.ToString();
    }

    internal RouteLocationError ValidateLimits(RouteLocationLimits limits)
    {
        if (StrictUtf8.GetByteCount(CanonicalText) > limits.MaximumUtf8Bytes)
            return new(RouteLocationErrorKind.InputTooLong, 0);
        if (_segments.Count > limits.MaximumSegments)
            return new(RouteLocationErrorKind.TooManySegments, 0);
        foreach (var segment in _segments)
        {
            if (StrictUtf8.GetByteCount(segment) > limits.MaximumSegmentUtf8Bytes)
                return new(RouteLocationErrorKind.PathSegmentTooLong, 0);
        }
        if (_query.Count > limits.MaximumQueryPairs)
            return new(RouteLocationErrorKind.TooManyQueryPairs, 0);
        foreach (var pair in _query)
        {
            if (StrictUtf8.GetByteCount(pair.Key) > limits.MaximumQueryKeyUtf8Bytes)
                return new(RouteLocationErrorKind.QueryKeyTooLong, 0);
            if (StrictUtf8.GetByteCount(pair.Value) > limits.MaximumQueryValueUtf8Bytes)
                return new(RouteLocationErrorKind.QueryValueTooLong, 0);
        }
        return default;
    }

    private static RouteLocationParseResult Failure(RouteLocationErrorKind kind, int offset) =>
        new(null, new(kind, offset));

    private static string? DecodeComponent(
        string text,
        int start,
        int end,
        ComponentKind kind,
        out RouteLocationError error
    )
    {
        var decoded = new StringBuilder(end - start);
        var index = start;
        while (index < end)
        {
            if (text[index] == '%')
            {
                var bytes = new List<byte>();
                var encodedStart = index;
                while (index < end && text[index] == '%')
                {
                    if (
                        index + 2 >= end
                        || !TryHex(text[index + 1], out var high)
                        || !TryHex(text[index + 2], out var low)
                    )
                    {
                        error = new(RouteLocationErrorKind.InvalidPercentEncoding, index);
                        return null;
                    }
                    bytes.Add((byte)((high << 4) | low));
                    index += 3;
                }
                string part;
                try
                {
                    part = StrictUtf8.GetString(bytes.ToArray());
                }
                catch (DecoderFallbackException)
                {
                    error = new(RouteLocationErrorKind.InvalidUtf8, encodedStart);
                    return null;
                }
                if (!IsValidDecodedComponent(part, out var invalidKind, out _))
                {
                    error = new(invalidKind, encodedStart);
                    return null;
                }
                decoded.Append(part);
                continue;
            }

            if (!Rune.TryGetRuneAt(text, index, out var rune))
            {
                error = new(RouteLocationErrorKind.InvalidUnicode, index);
                return null;
            }
            if (!IsAllowedRawRune(rune, kind))
            {
                error = new(RouteLocationErrorKind.InvalidCharacter, index);
                return null;
            }
            decoded.Append(rune.ToString());
            index += rune.Utf16SequenceLength;
        }
        error = default;
        return decoded.ToString();
    }

    private static bool IsValidDecodedComponent(string value) =>
        IsValidDecodedComponent(value, out _, out _);

    private static bool IsValidDecodedComponent(
        string value,
        out RouteLocationErrorKind error,
        out int offset
    )
    {
        for (var index = 0; index < value.Length; )
        {
            if (!Rune.TryGetRuneAt(value, index, out var rune))
            {
                error = RouteLocationErrorKind.InvalidUnicode;
                offset = index;
                return false;
            }
            if (rune.Value == '\\')
            {
                error = RouteLocationErrorKind.BackslashNotAllowed;
                offset = index;
                return false;
            }
            if (Rune.IsControl(rune) || rune.Value == 0)
            {
                error = RouteLocationErrorKind.ControlCharacterNotAllowed;
                offset = index;
                return false;
            }
            index += rune.Utf16SequenceLength;
        }
        error = RouteLocationErrorKind.None;
        offset = 0;
        return true;
    }

    private static bool IsAllowedRawRune(Rune rune, ComponentKind kind) =>
        !rune.IsAscii || IsAllowedRawAscii((char)rune.Value, kind);

    private static bool IsAllowedRawAscii(char value, ComponentKind kind) =>
        IsUnreserved(value)
        || IsSubDelimiter(value) && (kind == ComponentKind.Path || value is not '&' and not '=')
        || kind == ComponentKind.Query && value is '/' or '?';

    private static void AppendCanonical(StringBuilder output, string value, ComponentKind kind)
    {
        Span<byte> bytes = stackalloc byte[4];
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.IsAscii && IsAllowedRawAscii((char)rune.Value, kind))
            {
                output.Append((char)rune.Value);
                continue;
            }
            var count = rune.EncodeToUtf8(bytes);
            for (var index = 0; index < count; index++)
            {
                output.Append('%');
                output.Append(ToHex(bytes[index] >> 4));
                output.Append(ToHex(bytes[index] & 0x0f));
            }
        }
    }

    private static long CanonicalLength(string value, ComponentKind kind)
    {
        long length = 0;
        foreach (var rune in value.EnumerateRunes())
            length +=
                rune.IsAscii && IsAllowedRawAscii((char)rune.Value, kind)
                    ? 1
                    : rune.Utf8SequenceLength * 3;
        return length;
    }

    private static RoutePatternConfigurationException FormattedLocationRejected() =>
        new(
            RoutePatternConfigurationErrorKind.FormattedLocationRejected,
            "Generated route values do not satisfy the configured location limits."
        );

    private static bool IsUnreserved(char value) =>
        value is >= 'a' and <= 'z'
        || value is >= 'A' and <= 'Z'
        || value is >= '0' and <= '9'
        || value is '-' or '.' or '_' or '~';

    private static bool IsSubDelimiter(char value) =>
        value is '!' or '$' or '&' or '\'' or '(' or ')' or '*' or '+' or ',' or ';' or '=';

    private static bool TryHex(char value, out int result)
    {
        if (value is >= '0' and <= '9')
            result = value - '0';
        else if (value is >= 'a' and <= 'f')
            result = value - 'a' + 10;
        else if (value is >= 'A' and <= 'F')
            result = value - 'A' + 10;
        else
        {
            result = 0;
            return false;
        }
        return true;
    }

    private static char ToHex(int value) => (char)(value < 10 ? '0' + value : 'A' + value - 10);

    private static bool TryGetUtf8ByteCount(string value, out int count)
    {
        try
        {
            count = StrictUtf8.GetByteCount(value);
            return true;
        }
        catch (EncoderFallbackException)
        {
            count = 0;
            return false;
        }
    }

    private static int FirstInvalidUnicode(string value)
    {
        for (var index = 0; index < value.Length; )
        {
            if (!Rune.TryGetRuneAt(value, index, out var rune))
                return index;
            index += rune.Utf16SequenceLength;
        }
        return value.Length;
    }

    private enum ComponentKind
    {
        Path,
        Query,
    }
}
