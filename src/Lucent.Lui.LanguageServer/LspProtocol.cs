using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Lucent.Lui.LanguageServer;

/// <summary>Byte-oriented JSON-RPC framing shared by the language-server loop and tests.</summary>
internal static class LspProtocol
{
    internal const int MaxHeaderBytes = 16 * 1024;
    internal const int MaxBodyBytes = 16 * 1024 * 1024;

    private static readonly Encoding Utf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    internal static async Task<JsonDocument?> ReadMessageAsync(
        Stream input,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(input);

        cancellationToken.ThrowIfCancellationRequested();
        var header = new byte[MaxHeaderBytes];
        var one = new byte[1];
        var headerLength = 0;
        var terminated = false;
        while (headerLength < header.Length)
        {
            var read = await input
                .ReadAsync(one.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                if (headerLength == 0)
                    return null;
                throw new EndOfStreamException("LSP message ended in its header.");
            }
            header[headerLength++] = one[0];
            if (
                headerLength >= 4
                && header[headerLength - 4] == '\r'
                && header[headerLength - 3] == '\n'
                && header[headerLength - 2] == '\r'
                && header[headerLength - 1] == '\n'
            )
            {
                terminated = true;
                break;
            }
        }

        if (!terminated)
            throw new InvalidOperationException(
                $"LSP header exceeds the {MaxHeaderBytes}-byte limit."
            );
        if (header.Take(headerLength - 4).Any(value => value > 0x7F))
            throw new InvalidOperationException("LSP headers must contain ASCII characters.");

        var headerText = Encoding.ASCII.GetString(header, 0, headerLength - 4);
        int? length = null;
        foreach (var line in headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
                throw new InvalidOperationException("LSP message contains a malformed header.");
            var name = line[..separator].Trim();
            if (!name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;
            if (length is not null)
                throw new InvalidOperationException(
                    "LSP message contains duplicate Content-Length headers."
                );
            var value = line[(separator + 1)..].Trim();
            if (
                !Int32.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsed
                )
                || parsed <= 0
            )
                throw new InvalidOperationException("LSP message has an invalid Content-Length.");
            length = parsed;
        }
        if (length is null)
            throw new InvalidOperationException("LSP message has no Content-Length.");
        if (length.Value > MaxBodyBytes)
            throw new InvalidOperationException(
                $"LSP message exceeds the {MaxBodyBytes}-byte body limit."
            );

        var body = new byte[length.Value];
        for (var read = 0; read < body.Length; )
        {
            var count = await input
                .ReadAsync(body.AsMemory(read), cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
                throw new EndOfStreamException("LSP message ended before its content.");
            read += count;
        }
        return JsonDocument.Parse(body);
    }

    internal static void WriteMessage(Stream output, string message)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(message);

        var body = Utf8.GetBytes(message);
        if (body.Length > MaxBodyBytes)
            throw new InvalidOperationException(
                $"LSP message exceeds the {MaxBodyBytes}-byte body limit."
            );
        var header = Encoding.ASCII.GetBytes(
            "Content-Length: " + body.Length.ToString(CultureInfo.InvariantCulture) + "\r\n\r\n"
        );
        output.Write(header, 0, header.Length);
        output.Write(body, 0, body.Length);
        output.Flush();
    }
}
