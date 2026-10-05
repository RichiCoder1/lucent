using System.Buffers;
using System.Buffers.Binary;
using System.Text.Json;

namespace Lucent.Preview.Protocol;

/// <summary>The explicit saved-source launch of a retained development worker.</summary>
public sealed record PreviewLiveRequest(string PipeName, PreviewWorkerRequest Request);

/// <summary>One strictly validated client command; the event is an owned JSON value.</summary>
public sealed record PreviewLiveCommand(
    string Kind,
    long FrameSequence,
    long InputSequence,
    bool Focused,
    JsonElement Event
);

/// <summary>Bounded duplex live packets, independent of process supervision.</summary>
public static class PreviewLiveProtocol
{
    private static readonly SearchValues<char> HexCharacters = SearchValues.Create(
        "0123456789abcdef"
    );

    /// <summary>The opt-in launch discriminator.</summary>
    public const string RequestKind = "preview-live-request";

    private static readonly string[] IdentityFields =
    [
        "sessionId",
        "generation",
        "requestId",
        "projectTargetDigest",
        "inputDigest",
        "artifactDigest",
        "scenarioId",
        "presentationId",
    ];

    /// <summary>Reads a strict live launch without weakening the bounded capture schema.</summary>
    public static PreviewLiveRequest ReadRequest(ReadOnlyMemory<byte> bytes)
    {
        using var document = Parse(bytes);
        var root = document.RootElement;
        Exact(root, "protocolVersion", "kind", "pipeName", "request");
        Version(root);
        if (String(root, "kind") != RequestKind)
            throw new InvalidDataException("Invalid live request kind.");
        var pipe = String(root, "pipeName");
        if (
            pipe.Length != 47
            || !pipe.StartsWith("lucent-preview-", StringComparison.Ordinal)
            || pipe.AsSpan(15).ContainsAnyExcept(HexCharacters)
        )
            throw new InvalidDataException("Invalid live pipe name.");
        return new(
            pipe,
            PreviewProtocol.ReadRequest(
                System.Text.Encoding.UTF8.GetBytes(root.GetProperty("request").GetRawText())
            )
        );
    }

    /// <summary>Reads one exact client command and requires the complete launch identity.</summary>
    public static PreviewLiveCommand ReadCommand(
        ReadOnlyMemory<byte> bytes,
        PreviewWorkerRequest request
    )
    {
        using var document = Parse(bytes);
        var root = document.RootElement;
        Version(root);
        MatchIdentity(root.GetProperty("identity"), request);
        var kind = String(root, "kind");
        long frame = 0,
            input = 0;
        var focused = false;
        JsonElement value = default;
        switch (kind)
        {
            case "preview-live-ack":
                Exact(root, "protocolVersion", "kind", "identity", "frameSequence");
                frame = Sequence(root, "frameSequence");
                break;
            case "preview-live-focus":
                Exact(root, "protocolVersion", "kind", "identity", "inputSequence", "focused");
                input = Sequence(root, "inputSequence");
                focused = root.GetProperty("focused").GetBoolean();
                break;
            case "preview-live-input":
                Exact(
                    root,
                    "protocolVersion",
                    "kind",
                    "identity",
                    "frameSequence",
                    "inputSequence",
                    "event"
                );
                frame = Sequence(root, "frameSequence");
                input = Sequence(root, "inputSequence");
                value = root.GetProperty("event");
                ValidateEvent(value);
                value = value.Clone();
                break;
            default:
                throw new InvalidDataException("Unsupported live client command.");
        }
        return new(kind, frame, input, focused, value);
    }

    /// <summary>Writes ready or frame metadata, with raw frame bytes following separately.</summary>
    public static byte[] WriteServerMessage(
        PreviewWorkerRequest request,
        long frameSequence = 0,
        int byteLength = 0,
        string? sha256 = null,
        int width = 0,
        int height = 0
    )
    {
        PreviewProtocol.Validate(request);
        if (
            frameSequence != 0
            && (
                frameSequence < 1
                || frameSequence > 9_007_199_254_740_991
                || byteLength is < 33 or > PreviewProtocol.MaximumFrameBytes
                || width != MathF.Ceiling((float)request.LogicalWidth * (float)request.Scale)
                || height != MathF.Ceiling((float)request.LogicalHeight * (float)request.Scale)
                || sha256 is null
                || sha256.Length != 64
                || sha256.AsSpan().ContainsAnyExcept(HexCharacters)
            )
        )
            throw new InvalidDataException("Invalid live frame metadata.");
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("protocolVersion", PreviewProtocol.Version);
            writer.WriteString(
                "kind",
                frameSequence == 0 ? "preview-live-ready" : "preview-live-frame"
            );
            writer.WriteStartObject("identity");
            var values = IdentityValues(request);
            for (var i = 0; i < IdentityFields.Length; i++)
                writer.WriteString(IdentityFields[i], values[i]);
            writer.WriteEndObject();
            if (frameSequence != 0)
            {
                writer.WriteNumber("frameSequence", frameSequence);
                writer.WriteNumber("byteLength", byteLength);
                writer.WriteString("sha256", sha256);
                writer.WriteNumber("width", width);
                writer.WriteNumber("height", height);
            }
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    /// <summary>Reads one length-prefixed packet with bounded allocation and cancellation.</summary>
    public static async Task<byte[]> ReadPacketAsync(Stream stream, CancellationToken token)
    {
        var prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, token).ConfigureAwait(false);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
        if (size is < 2 or > PreviewProtocol.MaximumMessageBytes)
            throw new InvalidDataException("Invalid live packet size.");
        var bytes = new byte[(int)size];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return bytes;
    }

    /// <summary>Writes a bounded packet; callers serialize writes to the duplex connection.</summary>
    public static async Task WritePacketAsync(
        Stream stream,
        ReadOnlyMemory<byte> bytes,
        CancellationToken token
    )
    {
        if (bytes.Length is < 2 or > PreviewProtocol.MaximumMessageBytes)
            throw new InvalidDataException("Invalid live packet size.");
        var prefix = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, (uint)bytes.Length);
        await stream.WriteAsync(prefix, token).ConfigureAwait(false);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
    }

    private static void ValidateEvent(JsonElement value)
    {
        switch (String(value, "type"))
        {
            case "pointer":
                Exact(value, "type", "action", "pointerId", "x", "y", "button", "modifiers");
                var action = String(value, "action");
                var button = String(value, "button");
                if (
                    action is not ("move" or "down" or "up" or "cancel")
                    || button is not ("none" or "primary" or "secondary" or "middle")
                    || action == "down" && button == "none"
                    || action is "move" or "cancel" && button != "none"
                )
                    throw new InvalidDataException("Invalid live pointer.");
                Integer(value, "pointerId", 0, 15);
                Coordinates(value);
                Integer(value, "modifiers", 0, 15);
                break;
            case "wheel":
                Exact(value, "type", "x", "y", "deltaX", "deltaY", "modifiers");
                Coordinates(value);
                Number(value, "deltaX", 4096);
                Number(value, "deltaY", 4096);
                Integer(value, "modifiers", 0, 15);
                break;
            case "key":
                Exact(value, "type", "action", "key", "modifiers", "repeat");
                var phase = String(value, "action");
                if (
                    phase is not ("down" or "up")
                    || value.GetProperty("repeat").GetBoolean() && phase != "down"
                    || String(value, "key")
                        is not (
                            "F10"
                            or "ContextMenu"
                            or "Tab"
                            or "Enter"
                            or "Space"
                            or "Escape"
                            or "Left"
                            or "Right"
                            or "Up"
                            or "Down"
                            or "Home"
                            or "End"
                            or "PageUp"
                            or "PageDown"
                            or "Backspace"
                            or "Delete"
                            or "A"
                            or "C"
                            or "F"
                            or "N"
                            or "S"
                            or "V"
                            or "X"
                            or "Y"
                            or "Z"
                            or "F4"
                        )
                )
                    throw new InvalidDataException("Invalid live key.");
                Integer(value, "modifiers", 0, 15);
                break;
            case "text":
                Exact(value, "type", "text");
                var text = String(value, "text");
                if (text.Length is < 1 or > 4096 || text.Contains('\0'))
                    throw new InvalidDataException("Invalid live committed text.");
                for (var index = 0; index < text.Length; index++)
                {
                    if (!char.IsSurrogate(text[index]))
                        continue;
                    if (
                        !char.IsHighSurrogate(text[index])
                        || index + 1 == text.Length
                        || !char.IsLowSurrogate(text[index + 1])
                    )
                        throw new InvalidDataException("Invalid live Unicode scalar.");
                    index++;
                }
                break;
            default:
                throw new InvalidDataException("Unsupported live input.");
        }
    }

    private static void Coordinates(JsonElement value)
    {
        Number(value, "x", 65536);
        Number(value, "y", 65536);
    }

    private static void Number(JsonElement value, string name, double maximum)
    {
        var number = value.GetProperty(name).GetDouble();
        if (!double.IsFinite(number) || Math.Abs(number) > maximum)
            throw new InvalidDataException("Invalid live number.");
    }

    private static void Integer(JsonElement value, string name, int minimum, int maximum)
    {
        var number = value.GetProperty(name).GetInt32();
        if (number < minimum || number > maximum)
            throw new InvalidDataException("Invalid live integer.");
    }

    private static long Sequence(JsonElement value, string name)
    {
        var number = value.GetProperty(name).GetInt64();
        if (number is < 1 or > 9_007_199_254_740_991)
            throw new InvalidDataException("Invalid live sequence.");
        return number;
    }

    private static string String(JsonElement value, string name) =>
        value.GetProperty(name).GetString()
        ?? throw new InvalidDataException("Missing live string.");

    private static string[] IdentityValues(PreviewWorkerRequest request) =>
        [
            request.SessionId,
            request.Generation,
            request.RequestId,
            request.ProjectTargetDigest,
            request.InputDigest,
            request.ArtifactDigest,
            request.ScenarioId,
            request.PresentationId,
        ];

    private static void MatchIdentity(JsonElement identity, PreviewWorkerRequest request)
    {
        Exact(identity, IdentityFields);
        var expected = IdentityValues(request);
        for (var i = 0; i < IdentityFields.Length; i++)
            if (String(identity, IdentityFields[i]) != expected[i])
                throw new InvalidDataException("Mismatched live identity.");
    }

    private static void Version(JsonElement value)
    {
        if (value.GetProperty("protocolVersion").GetInt32() != PreviewProtocol.Version)
            throw new InvalidDataException("Unsupported live version.");
    }

    private static void Exact(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid live object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (
                !fields.Contains(property.Name, StringComparer.Ordinal) || !names.Add(property.Name)
            )
                throw new InvalidDataException("Unknown or duplicate live field.");
        if (names.Count != fields.Length)
            throw new InvalidDataException("Missing live field.");
    }

    private static JsonDocument Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < 2 or > PreviewProtocol.MaximumMessageBytes)
            throw new InvalidDataException("Invalid live message size.");
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
    }
}
