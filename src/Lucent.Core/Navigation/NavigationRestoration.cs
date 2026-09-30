using System.Text;
using System.Text.Json;

namespace Lucent.Core;

/// <summary>Finite, redacted results for opt-in navigation persistence.</summary>
public enum NavigationRestorationStatus
{
    /// <summary>A bounded navigation snapshot is available.</summary>
    Ready,

    /// <summary>No committed, persistable location was supplied.</summary>
    NoSnapshot,

    /// <summary>The session cannot be captured at this ownership boundary.</summary>
    InvalidState,

    /// <summary>The payload or a bounded field exceeded its limit.</summary>
    TooLarge,

    /// <summary>The envelope is malformed or contains unsupported fields.</summary>
    InvalidPayload,

    /// <summary>The envelope version is not supported.</summary>
    UnsupportedVersion,

    /// <summary>The snapshot belongs to another application or workspace scope.</summary>
    ScopeMismatch,

    /// <summary>The snapshot mode is not supported.</summary>
    UnsupportedMode,

    /// <summary>The canonical location or its stored definition is no longer valid.</summary>
    InvalidRoute,

    /// <summary>The application's persistence policy rejected the route or failed.</summary>
    PolicyRejected,
}

/// <summary>A bounded UTF-8 snapshot or a finite reason why no snapshot was produced.</summary>
public sealed class NavigationCaptureResult
{
    internal NavigationCaptureResult(NavigationRestorationStatus status, byte[]? utf8 = null)
    {
        Status = status;
        Utf8 = utf8 ?? [];
    }

    /// <summary>Gets the finite capture result.</summary>
    public NavigationRestorationStatus Status { get; }

    /// <summary>Gets the snapshot bytes, empty unless capture succeeded.</summary>
    public ReadOnlyMemory<byte> Utf8 { get; }

    /// <summary>Returns a diagnostic that excludes route, scope and payload values.</summary>
    public override string ToString() => "navigation-capture status=" + Status;
}

/// <summary>An immutable startup replay decision bound to one restoration policy and route table.</summary>
/// <remarks>A rejected or absent snapshot still carries the configured safe fallback.</remarks>
public sealed class NavigationRestorePlan
{
    internal NavigationRestorePlan(
        NavigationRestoration restoration,
        NavigationRestorationStatus status,
        NavigationRestorationTarget? target = null,
        NavigationRestorationJournal? journal = null,
        int droppedEntries = 0,
        int droppedStates = 0
    )
    {
        Restoration = restoration;
        Status = status;
        Target = target;
        Journal = journal;
        DroppedEntries = droppedEntries;
        DroppedStates = droppedStates;
    }

    /// <summary>Gets the result of decoding and validating the persisted input.</summary>
    public NavigationRestorationStatus Status { get; }

    /// <summary>Gets the number of invalid inactive routes omitted from a valid journal.</summary>
    public int DroppedEntries { get; }

    /// <summary>Gets the number of unregistered or invalid bounded states omitted from valid input.</summary>
    public int DroppedStates { get; }

    internal NavigationRestoration Restoration { get; }
    internal NavigationRestorationTarget? Target { get; }
    internal NavigationRestorationJournal? Journal { get; }

    /// <summary>Returns a diagnostic that excludes route, scope and payload values.</summary>
    public override string ToString() => "navigation-restore-plan status=" + Status;
}

/// <summary>Explicit, bounded version 1 navigation persistence with application-owned storage.</summary>
/// <remarks>
/// No page objects, services or editor data are serialized. The application supplies
/// a pure persistence predicate and a safe fallback; ordinary route guards remain authoritative.
/// </remarks>
public sealed partial class NavigationRestoration
{
    /// <summary>The hard bound for a persisted UTF-8 payload.</summary>
    public const int MaximumPayloadBytes = 256 * 1024;

    /// <summary>The maximum UTF-8 size of a scope or route-definition key.</summary>
    public const int MaximumKeyBytes = 128;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _scope;
    private readonly Func<RouteMatch, bool> _canPersist;
    private readonly int _maximumPayloadBytes;
    private readonly NavigationRestorationOptions _options;

    /// <summary>Creates an opt-in policy bound to an exact table, scope and typed fallback.</summary>
    public NavigationRestoration(
        RouteTable routeTable,
        string scope,
        RouteReference fallback,
        Func<RouteMatch, bool> canPersist,
        int maximumPayloadBytes = MaximumPayloadBytes,
        NavigationRestorationOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(routeTable);
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(canPersist);
        if (!IsKey(scope))
            throw new ArgumentException(
                "A restoration scope must be a bounded nonblank UTF-8 key.",
                nameof(scope)
            );
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumPayloadBytes, MaximumPayloadBytes);
        var match = routeTable.Match(fallback.Location);
        if (match.Match is null || !ReferenceEquals(match.Match.Pattern, fallback.Pattern))
            throw new ArgumentException(
                "The safe fallback must belong to the restoration route table.",
                nameof(fallback)
            );
        RouteTable = routeTable;
        _scope = scope;
        _canPersist = canPersist;
        _maximumPayloadBytes = maximumPayloadBytes;
        _options = options ?? new();
        SafeFallback = fallback;
        Fallback = new(fallback.Location, match.Match);
    }

    /// <summary>Gets the exact matching authority to which decoded plans are bound.</summary>
    public RouteTable RouteTable { get; }

    /// <summary>Gets the validated typed fallback supplied by the application.</summary>
    /// <remarks>Hosts may use ordinary guarded navigation to this route when newer activation supersedes startup replay.</remarks>
    public RouteReference SafeFallback { get; }

    internal NavigationRestorationTarget Fallback { get; }

    /// <summary>Captures committed navigation in the configured mode, including while asynchronous preparation is pending.</summary>
    /// <remarks>Call on the session owner thread outside staging, publication, retirement or outlet replacement.</remarks>
    public NavigationCaptureResult Capture(NavigationSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_options.Mode == NavigationRestorationMode.Journal)
            return CaptureJournal(session);
        if (!session.TryCaptureRestorationLocation(RouteTable, out var current))
            return new(NavigationRestorationStatus.InvalidState);
        if (current is null)
            return new(NavigationRestorationStatus.NoSnapshot);
        if (!IsKey(current.DefinitionId.Value))
            return new(NavigationRestorationStatus.InvalidRoute);
        if (!Allows(current.Match))
            return new(NavigationRestorationStatus.NoSnapshot);
        if (StrictUtf8.GetByteCount(current.Location.CanonicalText) > _maximumPayloadBytes)
            return new(NavigationRestorationStatus.TooLarge);
        try
        {
            using var stream = new BoundedSnapshotStream(_maximumPayloadBytes);
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("schema", "lucent.navigation");
                writer.WriteNumber("version", 1);
                writer.WriteString("scope", _scope);
                writer.WriteString("mode", "location");
                writer.WriteStartObject("active");
                writer.WriteString("definition", current.DefinitionId.Value);
                writer.WriteString("location", current.Location.CanonicalText);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            return new(NavigationRestorationStatus.Ready, stream.ToArray());
        }
        catch (SnapshotLimitException)
        {
            return new(NavigationRestorationStatus.TooLarge);
        }
    }

    /// <summary>Decodes untrusted bytes without changing a session; rejected input selects the safe fallback.</summary>
    public NavigationRestorePlan Decode(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length > _maximumPayloadBytes)
            return Rejected(NavigationRestorationStatus.TooLarge);
        if (utf8.IsEmpty)
            return Rejected(NavigationRestorationStatus.NoSnapshot);
        string? schema = null,
            scope = null,
            mode = null,
            definition = null,
            location = null;
        var version = 0;
        var fields = 0;
        var activeKey = 0;
        List<EncodedEntry>? entries = null;
        try
        {
            _ = StrictUtf8.GetCharCount(utf8);
            var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { MaxDepth = 8 });
            RequireRead(ref reader, JsonTokenType.StartObject);
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                RequireToken(reader, JsonTokenType.PropertyName);
                var field =
                    reader.ValueTextEquals("schema") ? 1
                    : reader.ValueTextEquals("version") ? 2
                    : reader.ValueTextEquals("scope") ? 4
                    : reader.ValueTextEquals("mode") ? 8
                    : reader.ValueTextEquals("active") ? 16
                    : reader.ValueTextEquals("activeKey") ? 32
                    : reader.ValueTextEquals("entries") ? 64
                    : 0;
                RequireUnique(ref fields, field);
                if (!reader.Read())
                    throw new JsonException();
                switch (field)
                {
                    case 1:
                        schema = ReadText(ref reader, MaximumKeyBytes);
                        break;
                    case 2:
                        if (
                            reader.TokenType != JsonTokenType.Number
                            || !reader.TryGetInt32(out version)
                        )
                            throw new JsonException();
                        break;
                    case 4:
                        scope = ReadText(ref reader, MaximumKeyBytes);
                        break;
                    case 8:
                        mode = ReadText(ref reader, MaximumKeyBytes);
                        break;
                    case 16:
                        ReadActive(ref reader, out definition, out location);
                        break;
                    case 32:
                        activeKey = ReadKey(ref reader);
                        break;
                    case 64:
                        entries = ReadEntries(ref reader, utf8);
                        break;
                }
            }
            RequireToken(reader, JsonTokenType.EndObject);
            if ((fields & 15) != 15 || reader.Read())
                throw new JsonException();
        }
        catch (SnapshotLimitException)
        {
            return Rejected(NavigationRestorationStatus.TooLarge);
        }
        catch (Exception error)
            when (error
                    is JsonException
                        or DecoderFallbackException
                        or EncoderFallbackException
                        or InvalidOperationException
            )
        {
            return Rejected(NavigationRestorationStatus.InvalidPayload);
        }
        if (schema != "lucent.navigation" || !IsKey(scope))
            return Rejected(NavigationRestorationStatus.InvalidPayload);
        if (version != 1)
            return Rejected(NavigationRestorationStatus.UnsupportedVersion);
        if (scope != _scope)
            return Rejected(NavigationRestorationStatus.ScopeMismatch);
        if (mode == "journal" && _options.Mode == NavigationRestorationMode.Journal)
            return fields == 111 && entries is not null
                ? DecodeJournal(entries, activeKey)
                : Rejected(NavigationRestorationStatus.InvalidPayload);
        if (mode != "location")
            return Rejected(NavigationRestorationStatus.UnsupportedMode);
        if (fields != 31 || !IsKey(definition))
            return Rejected(NavigationRestorationStatus.InvalidPayload);
        var parsed = RouteLocation.Parse(location, RouteTable.Limits);
        if (parsed.Location is not { } canonical || canonical.CanonicalText != location)
            return Rejected(NavigationRestorationStatus.InvalidRoute);
        var match = RouteTable.Match(canonical);
        if (match.Match is null || match.Match.DefinitionId.Value != definition)
            return Rejected(NavigationRestorationStatus.InvalidRoute);
        if (!Allows(match.Match))
            return Rejected(NavigationRestorationStatus.PolicyRejected);
        return new(this, NavigationRestorationStatus.Ready, new(canonical, match.Match));
    }

    internal bool Allows(RouteMatch match)
    {
        try
        {
            return _canPersist(match);
        }
        catch
        {
            return false;
        }
    }

    private NavigationRestorePlan Rejected(NavigationRestorationStatus status) => new(this, status);

    private void ReadActive(ref Utf8JsonReader reader, out string? definition, out string? location)
    {
        RequireToken(reader, JsonTokenType.StartObject);
        definition = null;
        location = null;
        var fields = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            RequireToken(reader, JsonTokenType.PropertyName);
            var field =
                reader.ValueTextEquals("definition") ? 1
                : reader.ValueTextEquals("location") ? 2
                : 0;
            RequireUnique(ref fields, field);
            if (!reader.Read())
                throw new JsonException();
            if (field == 1)
                definition = ReadText(ref reader, MaximumKeyBytes);
            else
                location = ReadText(ref reader, RouteTable.Limits.MaximumUtf8Bytes);
        }
        RequireToken(reader, JsonTokenType.EndObject);
        if (fields != 3)
            throw new JsonException();
    }

    private static string ReadText(ref Utf8JsonReader reader, int maximumBytes)
    {
        RequireToken(reader, JsonTokenType.String);
        var value = reader.GetString()!;
        if (StrictUtf8.GetByteCount(value) > maximumBytes)
            throw new SnapshotLimitException();
        return value;
    }

    private static bool IsKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
            return false;
        try
        {
            return StrictUtf8.GetByteCount(value) <= MaximumKeyBytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static void RequireRead(ref Utf8JsonReader reader, JsonTokenType type)
    {
        if (!reader.Read())
            throw new JsonException();
        RequireToken(reader, type);
    }

    private static void RequireToken(Utf8JsonReader reader, JsonTokenType type)
    {
        if (reader.TokenType != type)
            throw new JsonException();
    }

    private static void RequireUnique(ref int fields, int field)
    {
        if (field == 0 || (fields & field) != 0)
            throw new JsonException();
        fields |= field;
    }

    private sealed class SnapshotLimitException : Exception;

    private sealed class BoundedSnapshotStream(int maximumBytes) : Stream
    {
        private readonly MemoryStream _buffer = new();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _buffer.Length;
        public override long Position
        {
            get => _buffer.Position;
            set => throw new NotSupportedException();
        }

        public byte[] ToArray() => _buffer.ToArray();

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Position + buffer.Length > maximumBytes)
                throw new SnapshotLimitException();
            _buffer.Write(buffer);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _buffer.Dispose();
            base.Dispose(disposing);
        }
    }
}

internal sealed record NavigationRestorationTarget(RouteLocation Location, RouteMatch Match);
