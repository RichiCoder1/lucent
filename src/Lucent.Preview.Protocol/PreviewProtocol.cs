using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lucent.Preview.Protocol;

/// <summary>Strict, bounded worker messages shared with coordinator fixtures.</summary>
public static class PreviewProtocol
{
    /// <summary>The supported wire version.</summary>
    public const int Version = 1;

    /// <summary>The maximum request or result byte count.</summary>
    public const int MaximumMessageBytes = 65_536;

    /// <summary>The maximum encoded PNG byte count.</summary>
    public const int MaximumFrameBytes = 33_554_432;

    /// <summary>The maximum physical dimension.</summary>
    public const int MaximumDimension = 8192;

    /// <summary>Reads and validates one exact request object.</summary>
    public static PreviewWorkerRequest ReadRequest(ReadOnlyMemory<byte> bytes)
    {
        ValidateObject(bytes);
        var value =
            JsonSerializer.Deserialize(bytes.Span, PreviewJsonContext.Default.PreviewWorkerRequest)
            ?? throw new InvalidDataException("Missing preview request.");
        Validate(value);
        return value;
    }

    /// <summary>Validates and encodes one request without reflection.</summary>
    public static byte[] WriteRequest(PreviewWorkerRequest value)
    {
        Validate(value);
        return JsonSerializer.SerializeToUtf8Bytes(
            value,
            PreviewJsonContext.Default.PreviewWorkerRequest
        );
    }

    /// <summary>Reads and validates one exact result object.</summary>
    public static PreviewWorkerResult ReadResult(ReadOnlyMemory<byte> bytes)
    {
        ValidateObject(bytes);
        var value =
            JsonSerializer.Deserialize(bytes.Span, PreviewJsonContext.Default.PreviewWorkerResult)
            ?? throw new InvalidDataException("Missing preview result.");
        Validate(value);
        return value;
    }

    /// <summary>Validates and encodes one result without reflection.</summary>
    public static byte[] WriteResult(PreviewWorkerResult value)
    {
        Validate(value);
        return JsonSerializer.SerializeToUtf8Bytes(
            value,
            PreviewJsonContext.Default.PreviewWorkerResult
        );
    }

    /// <summary>Validates correlation and the caller-owned output path shape.</summary>
    public static void Validate(PreviewWorkerRequest value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Correlation(
            value.ProtocolVersion,
            value.SessionId,
            value.Generation,
            value.RequestId,
            value.ProjectTargetDigest,
            value.InputDigest,
            value.ArtifactDigest,
            value.ScenarioId,
            value.PresentationId
        );
        if (
            string.IsNullOrWhiteSpace(value.OutputDirectory)
            || value.OutputDirectory.Length > 4096
            || value.OutputDirectory.Length < 3
            || !char.IsAsciiLetter(value.OutputDirectory[0])
            || value.OutputDirectory[1] != ':'
            || value.OutputDirectory[2] is not '\\' and not '/'
            || value.OutputDirectory.AsSpan(2).Contains(':')
            || value.OutputDirectory.Any(char.IsControl)
        )
            throw new InvalidDataException("Invalid preview output directory.");
    }

    /// <summary>Validates a bounded frame and exact physical/logical dimensions.</summary>
    public static void Validate(PreviewWorkerResult value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Correlation(
            value.ProtocolVersion,
            value.SessionId,
            value.Generation,
            value.RequestId,
            value.ProjectTargetDigest,
            value.InputDigest,
            value.ArtifactDigest,
            value.ScenarioId,
            value.PresentationId
        );
        Digest(value.Sha256);
        if (
            value.FrameSequence != 1
            || value.FileName != "frame.png"
            || value.ByteLength is < 33 or > MaximumFrameBytes
            || value.Width is < 1 or > MaximumDimension
            || value.Height is < 1 or > MaximumDimension
            || !double.IsFinite(value.LogicalWidth)
            || value.LogicalWidth <= 0
            || !double.IsFinite(value.LogicalHeight)
            || value.LogicalHeight <= 0
            || !double.IsFinite(value.Scale)
            || value.Scale <= 0
            || MathF.Ceiling((float)value.LogicalWidth * (float)value.Scale) != value.Width
            || MathF.Ceiling((float)value.LogicalHeight * (float)value.Scale) != value.Height
        )
            throw new InvalidDataException("Invalid preview frame metadata.");
    }

    private static void Correlation(int version, params string[] fields)
    {
        if (version != Version)
            throw new InvalidDataException("Unsupported preview protocol.");
        for (var i = 0; i < fields.Length; i++)
        {
            if (i is >= 3 and <= 5)
                Digest(fields[i]);
            else if (i == 6)
            {
                if (
                    string.IsNullOrWhiteSpace(fields[i])
                    || fields[i].Length > 256
                    || fields[i].Any(char.IsControl)
                )
                    throw new InvalidDataException("Invalid preview scenario.");
            }
            else if (
                string.IsNullOrEmpty(fields[i])
                || fields[i].Length > 128
                || fields[i]
                    .Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-' and not '.')
            )
                throw new InvalidDataException("Invalid preview correlation.");
        }
    }

    private static void Digest(string value)
    {
        if (
            value is null
            || value.Length != 64
            || value.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
        )
            throw new InvalidDataException("Invalid preview digest.");
    }

    private static void ValidateObject(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < 2 or > MaximumMessageBytes)
            throw new InvalidDataException("Invalid preview message size.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid preview object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            if (!names.Add(property.Name))
                throw new InvalidDataException("Duplicate preview field.");
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
[JsonSerializable(typeof(PreviewWorkerRequest))]
[JsonSerializable(typeof(PreviewWorkerResult))]
internal sealed partial class PreviewJsonContext : JsonSerializerContext;
