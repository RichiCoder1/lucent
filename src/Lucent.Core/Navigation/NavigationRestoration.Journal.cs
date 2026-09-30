using System.Text.Json;

namespace Lucent.Core;

public sealed partial class NavigationRestoration
{
    private bool InteractionEnabled =>
        (_options.StateCodecs & NavigationRestorationStateCodecs.Interaction) != 0;

    private NavigationCaptureResult CaptureJournal(NavigationSession session)
    {
        if (!session.TryCaptureRestorationJournal(RouteTable, out var journal))
            return new(NavigationRestorationStatus.InvalidState);
        if (journal.Current is not { } current || !Allows(current.Match))
            return new(NavigationRestorationStatus.NoSnapshot);
        var capacity = Math.Min(_options.MaximumEntries, journal.Capacity);
        var start = Math.Max(0, journal.CurrentIndex - capacity + 1);
        var end = Math.Min(journal.Entries.Count, start + capacity);
        var retained = new List<NavigationSnapshot>();
        var activeKey = 0;
        for (var index = start; index < end; index++)
        {
            var entry = journal.Entries[index];
            if (index != journal.CurrentIndex && !Allows(entry.Match))
                continue;
            if (!IsKey(entry.DefinitionId.Value))
                return new(NavigationRestorationStatus.InvalidRoute);
            if (StrictUtf8.GetByteCount(entry.Location.CanonicalText) > _maximumPayloadBytes)
                return new(NavigationRestorationStatus.TooLarge);
            retained.Add(entry);
            if (index == journal.CurrentIndex)
                activeKey = retained.Count;
        }
        if (
            !session.TryCaptureRestorationStates(
                RouteTable,
                journal,
                new NavigationJournalSnapshot(retained, activeKey - 1, capacity),
                InteractionEnabled,
                out var states
            )
        )
            return new(NavigationRestorationStatus.InvalidState);
        try
        {
            using var stream = new BoundedSnapshotStream(_maximumPayloadBytes);
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("schema", "lucent.navigation");
                writer.WriteNumber("version", 1);
                writer.WriteString("scope", _scope);
                writer.WriteString("mode", "journal");
                writer.WriteNumber("activeKey", activeKey);
                writer.WriteStartArray("entries");
                for (var index = 0; index < retained.Count; index++)
                {
                    var entry = retained[index];
                    writer.WriteStartObject();
                    writer.WriteNumber("key", index + 1);
                    writer.WriteString("definition", entry.DefinitionId.Value);
                    writer.WriteString("location", entry.Location.CanonicalText);
                    if (states?.TryGetValue(entry.EntryId, out var state) == true)
                    {
                        writer.WritePropertyName("state");
                        writer.WriteRawValue(EncodeState(state));
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            return new(NavigationRestorationStatus.Ready, stream.ToArray());
        }
        catch (SnapshotLimitException)
        {
            return new(NavigationRestorationStatus.TooLarge);
        }
    }

    private List<EncodedEntry> ReadEntries(ref Utf8JsonReader reader, ReadOnlySpan<byte> utf8)
    {
        RequireToken(reader, JsonTokenType.StartArray);
        var entries = new List<EncodedEntry>();
        var keys = new HashSet<int>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (entries.Count >= _options.MaximumEntries)
                throw new SnapshotLimitException();
            RequireToken(reader, JsonTokenType.StartObject);
            var fields = 0;
            var key = 0;
            string? definition = null,
                location = null;
            NavigationEntryInteractionState? state = null;
            var droppedState = false;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                RequireToken(reader, JsonTokenType.PropertyName);
                var field =
                    reader.ValueTextEquals("key") ? 1
                    : reader.ValueTextEquals("definition") ? 2
                    : reader.ValueTextEquals("location") ? 4
                    : reader.ValueTextEquals("state") ? 8
                    : 0;
                RequireUnique(ref fields, field);
                if (!reader.Read())
                    throw new JsonException();
                switch (field)
                {
                    case 1:
                        key = ReadKey(ref reader);
                        break;
                    case 2:
                        definition = ReadText(ref reader, MaximumKeyBytes);
                        break;
                    case 4:
                        location = ReadText(ref reader, RouteTable.Limits.MaximumUtf8Bytes);
                        break;
                    case 8:
                        var start = checked((int)reader.TokenStartIndex);
                        reader.Skip();
                        var length = checked((int)reader.BytesConsumed - start);
                        if (length > NavigationRestorationOptions.MaximumStateBytes)
                            throw new SnapshotLimitException();
                        state = InteractionEnabled ? DecodeState(utf8.Slice(start, length)) : null;
                        droppedState = state is null;
                        break;
                }
            }
            RequireToken(reader, JsonTokenType.EndObject);
            if ((fields & 7) != 7 || !keys.Add(key))
                throw new JsonException();
            entries.Add(new(key, definition!, location!, state, droppedState));
        }
        RequireToken(reader, JsonTokenType.EndArray);
        if (entries.Count == 0)
            throw new JsonException();
        return entries;
    }

    private NavigationRestorePlan DecodeJournal(List<EncodedEntry> entries, int activeKey)
    {
        if (!entries.Any(entry => entry.Key == activeKey))
            return Rejected(NavigationRestorationStatus.InvalidPayload);
        var retained = new List<NavigationRestorationEntry>();
        var activeIndex = -1;
        var droppedStates = entries.Count(entry => entry.DroppedState);
        foreach (var entry in entries)
        {
            var target = MatchEntry(entry.Definition, entry.Location);
            var failure =
                target is null ? NavigationRestorationStatus.InvalidRoute
                : !Allows(target.Match) ? NavigationRestorationStatus.PolicyRejected
                : NavigationRestorationStatus.Ready;
            if (failure != NavigationRestorationStatus.Ready)
            {
                if (entry.Key == activeKey)
                    return Rejected(failure);
                continue;
            }
            if (entry.Key == activeKey)
                activeIndex = retained.Count;
            retained.Add(new(entry.Key, target!, entry.State));
        }
        var journal = new NavigationRestorationJournal(retained.ToArray(), activeIndex);
        return new(
            this,
            NavigationRestorationStatus.Ready,
            retained[activeIndex].Target,
            journal,
            entries.Count - retained.Count,
            droppedStates
        );
    }

    private NavigationRestorationTarget? MatchEntry(string definition, string location)
    {
        if (!IsKey(definition))
            return null;
        var parsed = RouteLocation.Parse(location, RouteTable.Limits);
        if (parsed.Location is not { } canonical || canonical.CanonicalText != location)
            return null;
        var match = RouteTable.Match(canonical).Match;
        return match?.DefinitionId.Value == definition ? new(canonical, match) : null;
    }

    private static int ReadKey(ref Utf8JsonReader reader)
    {
        if (
            reader.TokenType != JsonTokenType.Number
            || !reader.TryGetInt32(out var key)
            || key is < 1 or > NavigationRestorationOptions.MaximumJournalEntries
        )
            throw new JsonException();
        return key;
    }

    private static byte[] EncodeState(NavigationEntryInteractionState state)
    {
        if (state.Viewports.Count > NavigationRestorationOptions.MaximumViewports)
            throw new SnapshotLimitException();
        using var stream = new BoundedSnapshotStream(
            NavigationRestorationOptions.MaximumStateBytes
        );
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("codec", "lucent.interaction");
            writer.WriteNumber("version", 1);
            writer.WriteString("focus", state.FocusTargetId);
            writer.WriteStartArray("viewports");
            foreach (var viewport in state.Viewports)
            {
                writer.WriteStartObject();
                writer.WriteString("target", viewport.TargetId);
                writer.WriteNumber("x", viewport.Offset.X);
                writer.WriteNumber("y", viewport.Offset.Y);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static NavigationEntryInteractionState? DecodeState(ReadOnlySpan<byte> utf8)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { MaxDepth = 8 });
            RequireRead(ref reader, JsonTokenType.StartObject);
            var fields = 0;
            string? codec = null,
                focus = null;
            var version = 0;
            var viewports = new List<NavigationViewportPosition>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                RequireToken(reader, JsonTokenType.PropertyName);
                var field =
                    reader.ValueTextEquals("codec") ? 1
                    : reader.ValueTextEquals("version") ? 2
                    : reader.ValueTextEquals("focus") ? 4
                    : reader.ValueTextEquals("viewports") ? 8
                    : 0;
                RequireUnique(ref fields, field);
                if (!reader.Read())
                    throw new JsonException();
                switch (field)
                {
                    case 1:
                        codec = ReadText(ref reader, MaximumKeyBytes);
                        break;
                    case 2:
                        if (
                            reader.TokenType != JsonTokenType.Number
                            || !reader.TryGetInt32(out version)
                        )
                            throw new JsonException();
                        break;
                    case 4:
                        if (reader.TokenType != JsonTokenType.Null)
                        {
                            focus = ReadText(ref reader, MaximumKeyBytes);
                            if (!IsKey(focus))
                                throw new JsonException();
                        }
                        break;
                    case 8:
                        ReadViewports(ref reader, viewports);
                        break;
                }
            }
            RequireToken(reader, JsonTokenType.EndObject);
            return fields == 15 && !reader.Read() && codec == "lucent.interaction" && version == 1
                ? new(focus, viewports, requiresViewportClamp: true)
                : null;
        }
        catch (Exception error)
            when (error
                    is JsonException
                        or SnapshotLimitException
                        or InvalidOperationException
                        or System.Text.EncoderFallbackException
            )
        {
            return null;
        }
    }

    private static void ReadViewports(
        ref Utf8JsonReader reader,
        List<NavigationViewportPosition> viewports
    )
    {
        RequireToken(reader, JsonTokenType.StartArray);
        var targets = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (viewports.Count >= NavigationRestorationOptions.MaximumViewports)
                throw new JsonException();
            RequireToken(reader, JsonTokenType.StartObject);
            var fields = 0;
            string? target = null;
            float x = 0,
                y = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                RequireToken(reader, JsonTokenType.PropertyName);
                var field =
                    reader.ValueTextEquals("target") ? 1
                    : reader.ValueTextEquals("x") ? 2
                    : reader.ValueTextEquals("y") ? 4
                    : 0;
                RequireUnique(ref fields, field);
                if (!reader.Read())
                    throw new JsonException();
                if (field == 1)
                    target = ReadText(ref reader, MaximumKeyBytes);
                else
                {
                    if (
                        reader.TokenType != JsonTokenType.Number
                        || !reader.TryGetSingle(out var value)
                        || !float.IsFinite(value)
                        || value < 0
                    )
                        throw new JsonException();
                    if (field == 2)
                        x = value;
                    else
                        y = value;
                }
            }
            RequireToken(reader, JsonTokenType.EndObject);
            if (fields != 7 || !IsKey(target) || !targets.Add(target!))
                throw new JsonException();
            viewports.Add(new(target!, new(x, y)));
        }
        RequireToken(reader, JsonTokenType.EndArray);
    }

    private sealed record EncodedEntry(
        int Key,
        string Definition,
        string Location,
        NavigationEntryInteractionState? State,
        bool DroppedState
    );
}
