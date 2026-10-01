using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Lucent.Preview.Protocol;

/// <summary>Strict bounded development worker V2; build and supervisor protocols remain independent.</summary>
public static class PreviewProtocol
{
    /// <summary>The supported worker version.</summary>
    public const int Version = 2;

    /// <summary>The capture request discriminator.</summary>
    public const string CaptureRequestKind = "preview-capture-request";

    /// <summary>The frame result discriminator.</summary>
    public const string FrameResultKind = "preview-frame-result";

    /// <summary>The catalog request discriminator.</summary>
    public const string CatalogRequestKind = "preview-catalog-request";

    /// <summary>The catalog result discriminator.</summary>
    public const string CatalogResultKind = "preview-catalog-result";

    /// <summary>The maximum encoded message size.</summary>
    public const int MaximumMessageBytes = 65_536;

    /// <summary>The maximum encoded frame size.</summary>
    public const int MaximumFrameBytes = 33_554_432;

    /// <summary>The maximum physical dimension.</summary>
    public const int MaximumDimension = 8192;

    /// <summary>The maximum aggregate frame pixels, checked before host allocation.</summary>
    public const int MaximumPixels = 16_777_216;

    /// <summary>The maximum discovery entries.</summary>
    public const int MaximumScenarios = 64;

    /// <summary>Reads a strict request discriminator before dispatch.</summary>
    public static string ReadKind(ReadOnlyMemory<byte> bytes)
    {
        using var document = ValidateObject(bytes);
        return document.RootElement.GetProperty("kind").GetString()
            ?? throw new InvalidDataException("Missing worker operation.");
    }

    /// <summary>Reads a fully resolved capture request.</summary>
    public static PreviewWorkerRequest ReadRequest(ReadOnlyMemory<byte> bytes) =>
        Read(bytes, PreviewJsonContext.Default.PreviewWorkerRequest, Validate);

    /// <summary>Reads a completed frame result.</summary>
    public static PreviewWorkerResult ReadResult(ReadOnlyMemory<byte> bytes) =>
        Read(bytes, PreviewJsonContext.Default.PreviewWorkerResult, Validate);

    /// <summary>Reads a catalog discovery request.</summary>
    public static PreviewCatalogRequest ReadCatalogRequest(ReadOnlyMemory<byte> bytes) =>
        Read(bytes, PreviewJsonContext.Default.PreviewCatalogRequest, Validate);

    /// <summary>Reads an owned bounded catalog result.</summary>
    public static PreviewCatalogResult ReadCatalogResult(ReadOnlyMemory<byte> bytes) =>
        Read(bytes, PreviewJsonContext.Default.PreviewCatalogResult, Validate);

    /// <summary>Writes a validated capture request.</summary>
    public static byte[] WriteRequest(PreviewWorkerRequest value) =>
        Write(value, PreviewJsonContext.Default.PreviewWorkerRequest, Validate);

    /// <summary>Writes a validated frame result.</summary>
    public static byte[] WriteResult(PreviewWorkerResult value) =>
        Write(value, PreviewJsonContext.Default.PreviewWorkerResult, Validate);

    /// <summary>Writes a validated catalog request.</summary>
    public static byte[] WriteCatalogRequest(PreviewCatalogRequest value) =>
        Write(value, PreviewJsonContext.Default.PreviewCatalogRequest, Validate);

    /// <summary>Writes a validated catalog result.</summary>
    public static byte[] WriteCatalogResult(PreviewCatalogResult value) =>
        Write(value, PreviewJsonContext.Default.PreviewCatalogResult, Validate);

    /// <summary>Validates capture identity and binary32 presentation limits.</summary>
    public static void Validate(PreviewWorkerRequest value)
    {
        Identity(value, CaptureRequestKind);
        Text(value.ScenarioId, 256);
        Nonce(value.PresentationId);
        Output(value.OutputDirectory);
        Presentation(
            value.LogicalWidth,
            value.LogicalHeight,
            value.Scale,
            value.ColorScheme,
            value.Contrast,
            value.Density
        );
    }

    /// <summary>Validates completed frame correlation, effective values and aggregate allocation bounds.</summary>
    public static void Validate(PreviewWorkerResult value)
    {
        Identity(value, FrameResultKind);
        Text(value.ScenarioId, 256);
        Nonce(value.PresentationId);
        Presentation(
            value.LogicalWidth,
            value.LogicalHeight,
            value.Scale,
            value.ColorScheme,
            value.Contrast,
            value.Density
        );
        Defaults(value.Culture, value.UICulture, value.InitialTime);
        Digest(value.Sha256);
        if (
            value.FrameSequence != 1
            || value.FileName != "frame.png"
            || value.ByteLength is < 33 or > MaximumFrameBytes
            || value.Width != MathF.Ceiling((float)value.LogicalWidth * (float)value.Scale)
            || value.Height != MathF.Ceiling((float)value.LogicalHeight * (float)value.Scale)
        )
            throw new InvalidDataException("Invalid preview frame metadata.");
    }

    /// <summary>Validates catalog request ownership.</summary>
    public static void Validate(PreviewCatalogRequest value)
    {
        Identity(value, CatalogRequestKind);
        Output(value.OutputDirectory);
    }

    /// <summary>Validates catalog count, duplicate scenario IDs and bounded inert defaults.</summary>
    public static void Validate(PreviewCatalogResult value)
    {
        Identity(value, CatalogResultKind);
        if (value.Scenarios is null || value.Scenarios.Length > MaximumScenarios)
            throw new InvalidDataException("Preview catalog count exceeds its bound.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in value.Scenarios)
        {
            Validate(entry);
            if (!ids.Add(entry.Id))
                throw new InvalidDataException("Duplicate preview scenario.");
        }
    }

    /// <summary>Validates one bounded entry without executing author callbacks.</summary>
    public static void Validate(PreviewCatalogEntry value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Text(value.Id, 256);
        Text(value.Title, 256);
        Text(value.SourceProject, 2048);
        Text(value.SourceDocument, 2048);
        Text(value.SourceComponent, 512);
        Presentation(
            value.LogicalWidth,
            value.LogicalHeight,
            value.Scale,
            value.ColorScheme,
            value.Contrast,
            value.Density
        );
        Defaults(value.Culture, value.UICulture, value.InitialTime);
    }

    /// <summary>Normalizes and bounds one full presentation, including its physical pixel budget.</summary>
    public static void Presentation(
        double width,
        double height,
        double scale,
        string scheme,
        string contrast,
        double density
    )
    {
        Number(width, 1, MaximumDimension);
        Number(height, 1, MaximumDimension);
        Number(scale, 0.25, 4);
        Number(density, 0.25, 4);
        if (scheme is not "light" and not "dark" || contrast is not "normal" and not "high")
            throw new InvalidDataException("Unsupported preview appearance.");
        var physicalWidth = MathF.Ceiling((float)width * (float)scale);
        var physicalHeight = MathF.Ceiling((float)height * (float)scale);
        if (
            physicalWidth is < 1 or > MaximumDimension
            || physicalHeight is < 1 or > MaximumDimension
            || (long)physicalWidth * (long)physicalHeight > MaximumPixels
        )
            throw new InvalidDataException("Preview viewport exceeds its pixel bound.");
    }

    private static void Number(double value, double minimum, double maximum)
    {
        if (
            !double.IsFinite(value)
            || value < minimum
            || value > maximum
            || !float.IsFinite((float)value)
            || (float)value < minimum
            || (float)value > maximum
        )
            throw new InvalidDataException("Invalid preview presentation number.");
    }

    private static void Defaults(string culture, string uiCulture, string initialTime)
    {
        Text(culture, 64, allowEmpty: true);
        Text(uiCulture, 64, allowEmpty: true);
        if (
            initialTime is null
            || initialTime.Length != 33
            || !DateTimeOffset.TryParseExact(
                initialTime,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var instant
            )
            || instant.Offset != TimeSpan.Zero
            || instant.ToString("O", CultureInfo.InvariantCulture) != initialTime
        )
            throw new InvalidDataException("Invalid preview initial instant.");
    }

    private static void Identity(PreviewWorkerIdentity value, string kind)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.ProtocolVersion != Version || value.Kind != kind)
            throw new InvalidDataException("Unsupported preview operation.");
        Nonce(value.SessionId);
        Nonce(value.Generation);
        Nonce(value.RequestId);
        Digest(value.ProjectTargetDigest);
        Digest(value.InputDigest);
        Digest(value.ArtifactDigest);
    }

    private static void Nonce(string value)
    {
        if (
            string.IsNullOrEmpty(value)
            || value.Length > 128
            || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-' and not '.')
        )
            throw new InvalidDataException("Invalid preview correlation.");
    }

    private static void Text(string value, int maximum, bool allowEmpty = false)
    {
        if (
            value is null
            || value.Length > maximum
            || value.Any(char.IsControl)
            || !allowEmpty && string.IsNullOrWhiteSpace(value)
        )
            throw new InvalidDataException("Invalid preview label.");
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

    private static void Output(string value)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || value.Length is < 3 or > 4096
            || !char.IsAsciiLetter(value[0])
            || value[1] != ':'
            || value[2] is not '\\' and not '/'
            || value.AsSpan(2).Contains(':')
            || value.Any(char.IsControl)
        )
            throw new InvalidDataException("Invalid preview output directory.");
    }

    private static T Read<T>(ReadOnlyMemory<byte> bytes, JsonTypeInfo<T> type, Action<T> validate)
        where T : class
    {
        using var document = ValidateObject(bytes);
        var value =
            JsonSerializer.Deserialize(bytes.Span, type)
            ?? throw new InvalidDataException("Missing preview object.");
        validate(value);
        return value;
    }

    private static byte[] Write<T>(T value, JsonTypeInfo<T> type, Action<T> validate)
    {
        validate(value);
        using var buffer = new BoundedBuffer();
        JsonSerializer.Serialize(buffer, value, type);
        return buffer.ToArray();
    }

    private static JsonDocument ValidateObject(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < 2 or > MaximumMessageBytes)
            throw new InvalidDataException("Invalid preview message size.");
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        try
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Invalid preview object.");
            Unique(document.RootElement);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException("Duplicate preview field.");
                Unique(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            if (value.GetArrayLength() > MaximumScenarios)
                throw new InvalidDataException("Preview catalog count exceeds its bound.");
            foreach (var item in value.EnumerateArray())
                Unique(item);
        }
    }

    private sealed class BoundedBuffer : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Length + count > MaximumMessageBytes)
                throw new InvalidDataException("Preview message exceeds its bound.");
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Length + buffer.Length > MaximumMessageBytes)
                throw new InvalidDataException("Preview message exceeds its bound.");
            base.Write(buffer);
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
[JsonSerializable(typeof(PreviewWorkerRequest))]
[JsonSerializable(typeof(PreviewWorkerResult))]
[JsonSerializable(typeof(PreviewCatalogRequest))]
[JsonSerializable(typeof(PreviewCatalogResult))]
internal sealed partial class PreviewJsonContext : JsonSerializerContext;
