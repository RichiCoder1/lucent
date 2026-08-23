using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Lucent.LanguageServer;

internal sealed class JsonRpcConnection(Stream input, Stream output)
{
    internal const int MaxPayloadLength = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Stream _input = input;
    private readonly Stream _output = output;

    public async Task<JsonDocument?> ReadAsync(CancellationToken cancellationToken)
    {
        var header = new ArrayBufferWriter<byte>();
        var oneByte = new byte[1];

        while (true)
        {
            var read = await _input.ReadAsync(
                oneByte.AsMemory(),
                cancellationToken);
            if (read == 0)
            {
                return header.WrittenCount == 0
                    ? null
                    : throw new InvalidDataException(
                        "The JSON-RPC header ended before EOF.");
            }

            header.Write(oneByte);
            if (header.WrittenCount >= 4 &&
                header.WrittenSpan[^4..].SequenceEqual("\r\n\r\n"u8))
            {
                break;
            }

            if (header.WrittenCount > 64 * 1024)
            {
                throw new InvalidDataException("The JSON-RPC header is too large.");
            }
        }

        var contentLength = ParseContentLength(header.WrittenSpan[..^4]);
        if (contentLength > MaxPayloadLength)
        {
            throw new InvalidDataException(
                $"The JSON-RPC payload exceeds the {MaxPayloadLength}-byte limit.");
        }

        var payload = new byte[contentLength];
        await _input.ReadExactlyAsync(payload, cancellationToken);
        return JsonDocument.Parse(payload);
    }

    public Task WriteResponseAsync(
        JsonElement id,
        object? result,
        CancellationToken cancellationToken) =>
        WriteMessageAsync(
            new
            {
                jsonrpc = "2.0",
                id,
                result,
            },
            cancellationToken);

    public Task WriteErrorAsync(
        JsonElement id,
        int code,
        string message,
        CancellationToken cancellationToken) =>
        WriteMessageAsync(
            new
            {
                jsonrpc = "2.0",
                id,
                error = new
                {
                    code,
                    message,
                },
            },
            cancellationToken);

    public Task WriteNotificationAsync(
        string method,
        object parameters,
        CancellationToken cancellationToken) =>
        WriteMessageAsync(
            new
            {
                jsonrpc = "2.0",
                method,
                @params = parameters,
            },
            cancellationToken);

    private async Task WriteMessageAsync(
        object message,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            message,
            SerializerOptions);
        var header = Encoding.ASCII.GetBytes(
            $"Content-Length: {payload.Length}\r\n\r\n");
        var frame = new byte[header.Length + payload.Length];
        header.CopyTo(frame, 0);
        payload.CopyTo(frame, header.Length);

        // A cancelled request must not leave half a JSON-RPC frame on stdout.
        // Observe cancellation before publication, then make publication one
        // indivisible, non-request-cancellable write.
        cancellationToken.ThrowIfCancellationRequested();
        await _output.WriteAsync(frame, CancellationToken.None);
        await _output.FlushAsync(CancellationToken.None);
    }

    private static int ParseContentLength(ReadOnlySpan<byte> header)
    {
        foreach (var line in Encoding.ASCII.GetString(header).Split("\r\n"))
        {
            var separator = line.IndexOf(':');
            if (separator < 0 ||
                !line[..separator].Equals(
                    "Content-Length",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(
                    line[(separator + 1)..].Trim(),
                    out var length) &&
                length >= 0)
            {
                return length;
            }

            break;
        }

        throw new InvalidDataException(
            "Every JSON-RPC message must contain a valid Content-Length header.");
    }
}
