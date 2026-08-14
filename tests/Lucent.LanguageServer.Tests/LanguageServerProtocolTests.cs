using System.Text;
using System.Text.Json;
using Lucent.LanguageServer;

namespace Lucent.LanguageServer.Tests;

[TestClass]
public sealed class LanguageServerProtocolTests
{
    [TestMethod]
    public async Task Initialize_open_shutdown_and_exit_use_stdio_json_rpc()
    {
        var input = BuildInput(
            Request(1, "initialize", new
            {
                processId = (int?)null,
                rootUri = (string?)null,
                capabilities = new { },
            }),
            Notification("initialized", new { }),
            Notification("textDocument/didOpen", new
            {
                textDocument = new
                {
                    uri = "file:///Counter.lui",
                    languageId = "lucent",
                    version = 1,
                    text = InvalidSource,
                },
            }),
            Request(2, "shutdown", null),
            Notification("exit", null));
        using var output = new MemoryStream();

        var exitCode = await LanguageServer.RunAsync(input, output);
        var messages = ReadMessages(output.ToArray());

        Assert.AreEqual(0, exitCode);
        var initialize = messages.Single(message =>
            message.RootElement.TryGetProperty("id", out var id) &&
            id.ValueKind == JsonValueKind.Number &&
            id.GetInt32() == 1);
        var textDocumentSync = initialize.RootElement
            .GetProperty("result")
            .GetProperty("capabilities")
            .GetProperty("textDocumentSync");
        Assert.IsTrue(textDocumentSync.GetProperty("openClose").GetBoolean());
        Assert.AreEqual(1, textDocumentSync.GetProperty("change").GetInt32());
        Assert.AreEqual(
            "utf-16",
            initialize.RootElement
                .GetProperty("result")
                .GetProperty("capabilities")
                .GetProperty("positionEncoding")
                .GetString());

        var published = messages.Single(message =>
            message.RootElement.TryGetProperty("method", out var method) &&
            method.GetString() == "textDocument/publishDiagnostics");
        var diagnostic = published.RootElement
            .GetProperty("params")
            .GetProperty("diagnostics")
            .EnumerateArray()
            .Single(item => item.GetProperty("code").GetString() == "LUC2001");
        var lines = InvalidSource.Split("\r\n");
        var sourceLineIndex = Array.FindIndex(
            lines,
            line => line.Contains("tooltip", StringComparison.Ordinal));
        var sourceLine = lines[sourceLineIndex];
        var tooltip = sourceLine.IndexOf("tooltip", StringComparison.Ordinal);
        Assert.AreEqual(sourceLineIndex, diagnostic.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        Assert.AreEqual(tooltip, diagnostic.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        Assert.AreEqual(1, diagnostic.GetProperty("severity").GetInt32());
        Assert.AreEqual("lucent", diagnostic.GetProperty("source").GetString());
    }

    [TestMethod]
    public async Task Did_change_republishes_diagnostics_and_close_clears_them()
    {
        var uri = "file:///Counter.lui";
        var input = BuildInput(
            Request(1, "initialize", new { capabilities = new { } }),
            Notification("textDocument/didOpen", new
            {
                textDocument = new
                {
                    uri,
                    languageId = "lucent",
                    version = 1,
                    text = "component",
                },
            }),
            Notification("textDocument/didChange", new
            {
                textDocument = new { uri, version = 2 },
                contentChanges = new[] { new { text = ValidSource } },
            }),
            Notification("textDocument/didClose", new
            {
                textDocument = new { uri },
            }),
            Request(2, "shutdown", null),
            Notification("exit", null));
        using var output = new MemoryStream();

        var exitCode = await LanguageServer.RunAsync(input, output);
        var publishes = ReadMessages(output.ToArray())
            .Where(message =>
                message.RootElement.TryGetProperty("method", out var method) &&
                method.GetString() == "textDocument/publishDiagnostics")
            .Select(message => message.RootElement
                .GetProperty("params")
                .GetProperty("diagnostics")
                .GetArrayLength())
            .ToArray();

        Assert.AreEqual(0, exitCode);
        Assert.HasCount(3, publishes);
        Assert.IsGreaterThan(0, publishes[0]);
        Assert.AreEqual(0, publishes[1]);
        Assert.AreEqual(0, publishes[2]);
    }

    [TestMethod]
    public async Task Exit_without_shutdown_returns_failure_status()
    {
        using var input = BuildInput(Notification("exit", null));
        using var output = new MemoryStream();

        var exitCode = await LanguageServer.RunAsync(input, output);

        Assert.AreEqual(1, exitCode);
        Assert.AreEqual(0, output.Length);
    }

    private static MemoryStream BuildInput(params byte[][] messages) =>
        new(messages.SelectMany(message => message).ToArray());

    private static byte[] Request(int id, string method, object? parameters) =>
        Message(new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters,
        });

    private static byte[] Notification(string method, object? parameters) =>
        Message(new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters,
        });

    private static byte[] Message(object message)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        var header = Encoding.ASCII.GetBytes(
            $"Content-Length: {payload.Length}\r\n\r\n");
        return header.Concat(payload).ToArray();
    }

    private static IReadOnlyList<JsonDocument> ReadMessages(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        var messages = new List<JsonDocument>();
        var header = new List<byte>();

        while (input.Position < input.Length)
        {
            header.Clear();
            while (true)
            {
                var value = input.ReadByte();
                Assert.AreNotEqual(-1, value);
                header.Add((byte)value);
                if (header.Count >= 4 &&
                    header[^4..].SequenceEqual("\r\n\r\n"u8.ToArray()))
                {
                    break;
                }
            }

            var contentLength = int.Parse(
                Encoding.ASCII.GetString(header.ToArray())
                    .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
                    .Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    .Split(':', 2)[1]
                    .Trim());
            var payload = new byte[contentLength];
            var read = input.Read(payload, 0, payload.Length);
            Assert.AreEqual(contentLength, read);
            messages.Add(JsonDocument.Parse(payload));
        }

        return messages;
    }

    private const string InvalidSource =
        "namespace N;\r\n" +
        "component Counter()\r\n" +
        "{\r\n" +
        "    private readonly State<int> count = new(0);\r\n" +
        "    Fragment Render()\r\n" +
        "    {\r\n" +
        "        return Column {\r\n" +
        "            Text { text: \"😀\"; tooltip: \"Not supported\"; }\r\n" +
        "            Button {\r\n" +
        "                class: \"primary\";\r\n" +
        "                text: \"Increment\";\r\n" +
        "                onClick: { count.Update(count.Value + 1); }\r\n" +
        "            }\r\n" +
        "        };\r\n" +
        "    }\r\n" +
        "}\r\n";

    private const string ValidSource =
        "namespace N;\r\n" +
        "component Counter()\r\n" +
        "{\r\n" +
        "    private readonly State<int> count = new(0);\r\n" +
        "    Fragment Render()\r\n" +
        "    {\r\n" +
        "        return Column {\r\n" +
        "            Text { text: $\"Count: {count.Value}\"; }\r\n" +
        "            Button {\r\n" +
        "                class: \"primary\";\r\n" +
        "                text: \"Increment\";\r\n" +
        "                onClick: { count.Update(count.Value + 1); }\r\n" +
        "            }\r\n" +
        "        };\r\n" +
        "    }\r\n" +
        "}\r\n";
}
