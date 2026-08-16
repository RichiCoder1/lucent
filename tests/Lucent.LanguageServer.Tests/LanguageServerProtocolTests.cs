using System.Text;
using System.Text.Json;
using Lucent.LanguageServer;

namespace Lucent.LanguageServer.Tests;

[TestClass]
public sealed class LanguageServerProtocolTests
{
    [TestMethod]
    public void Windows_file_uris_do_not_duplicate_the_drive()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.IsTrue(FileUri.TryGetPath(
            "file:///d%3A/src/richicoder1/lucent",
            out var path));
        Assert.AreEqual(
            @"D:\src\richicoder1\lucent",
            path,
            ignoreCase: true);
    }

    [TestMethod]
    public async Task Project_context_keeps_local_sources_when_design_time_build_fails()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "lucent-context-fallback-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var projectPath = Path.Combine(temporaryDirectory, "Example.csproj");
            var lucentPath = Path.Combine(temporaryDirectory, "MainWindow.lui");
            var counterPath = Path.Combine(temporaryDirectory, "Counter.cs");
            await File.WriteAllTextAsync(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
                "<TargetFramework>net9.0</TargetFramework></PropertyGroup>" +
                "<ItemGroup><LucentSource Include=\"MainWindow.lui\" /></ItemGroup>" +
                "<Target Name=\"FailDesignTime\" BeforeTargets=\"ResolveReferences\">" +
                "<Error Text=\"forced design-time failure\" /></Target></Project>");
            await File.WriteAllTextAsync(lucentPath, "namespace Demo;");
            await File.WriteAllTextAsync(
                counterPath,
                "namespace Demo; internal sealed class Counter { }");
            var loader = new ProjectContextLoader();
            using var initialize = JsonDocument.Parse("{}");
            loader.Configure(initialize.RootElement);

            var context = await loader.LoadAsync(lucentPath, CancellationToken.None);

            Assert.IsNotNull(context);
            Assert.IsTrue(context.Sources.Contains(counterPath));
            Assert.AreEqual(0, context.References.Count);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Project_context_reloads_when_a_source_file_is_added()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "lucent-context-cache-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var projectPath = Path.Combine(temporaryDirectory, "Example.csproj");
            var lucentPath = Path.Combine(temporaryDirectory, "MainWindow.lui");
            await File.WriteAllTextAsync(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
                "<TargetFramework>net9.0</TargetFramework></PropertyGroup>" +
                "<ItemGroup><LucentSource Include=\"MainWindow.lui\" /></ItemGroup>" +
                "</Project>");
            await File.WriteAllTextAsync(lucentPath, "namespace Demo;");
            var loader = new ProjectContextLoader();
            using var initialize = JsonDocument.Parse("{}");
            loader.Configure(initialize.RootElement);

            var first = await loader.LoadAsync(lucentPath, CancellationToken.None);
            Assert.IsNotNull(first);
            Assert.IsFalse(first.Sources.Any(path => path.EndsWith("Counter.cs")));

            var counterPath = Path.Combine(temporaryDirectory, "Counter.cs");
            await File.WriteAllTextAsync(
                counterPath,
                "namespace Demo; internal sealed class Counter { }");
            Directory.SetLastWriteTimeUtc(
                temporaryDirectory,
                DateTime.UtcNow.AddSeconds(1));

            var second = await loader.LoadAsync(lucentPath, CancellationToken.None);
            Assert.IsNotNull(second);
            Assert.IsTrue(second.Sources.Contains(counterPath));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

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
        Assert.IsTrue(
            initialize.RootElement
                .GetProperty("result")
                .GetProperty("capabilities")
                .GetProperty("hoverProvider")
                .GetBoolean());
        Assert.IsTrue(
            initialize.RootElement
                .GetProperty("result")
                .GetProperty("capabilities")
                .GetProperty("definitionProvider")
                .GetBoolean());
        Assert.AreEqual(
            ":",
            initialize.RootElement
                .GetProperty("result")
                .GetProperty("capabilities")
                .GetProperty("completionProvider")
                .GetProperty("triggerCharacters")[0]
                .GetString());
        Assert.AreEqual(
            ".",
            initialize.RootElement
                .GetProperty("result")
                .GetProperty("capabilities")
                .GetProperty("completionProvider")
                .GetProperty("triggerCharacters")[1]
                .GetString());
        Assert.AreEqual(
            "(",
            initialize.RootElement
                .GetProperty("result")
                .GetProperty("capabilities")
                .GetProperty("completionProvider")
                .GetProperty("triggerCharacters")[2]
                .GetString());
        Assert.AreEqual(
            ",",
            initialize.RootElement
                .GetProperty("result")
                .GetProperty("capabilities")
                .GetProperty("completionProvider")
                .GetProperty("triggerCharacters")[3]
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

    [TestMethod]
    public async Task Malformed_payload_does_not_prevent_later_requests()
    {
        var malformed = Encoding.UTF8.GetBytes("{");
        using var input = BuildInput(
            Frame(malformed),
            Request(1, "shutdown", null),
            Notification("exit", null));
        using var output = new MemoryStream();

        var exitCode = await LanguageServer.RunAsync(input, output);

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual(
            JsonValueKind.Null,
            Response(ReadMessages(output.ToArray()), 1)
                .GetProperty("result")
                .ValueKind);
    }

    [TestMethod]
    public async Task Oversized_payload_is_rejected_before_allocation()
    {
        var header = Encoding.ASCII.GetBytes(
            $"Content-Length: {JsonRpcConnection.MaxPayloadLength + 1}\r\n\r\n");
        using var input = new MemoryStream(header);
        using var output = new MemoryStream();

        var exitCode = await LanguageServer.RunAsync(input, output);

        Assert.AreEqual(1, exitCode);
        Assert.AreEqual(0, output.Length);
    }

    [TestMethod]
    public async Task Unexpected_request_failure_returns_internal_error()
    {
        using var input = BuildInput(
            Request(1, "initialize", new
            {
                workspaceFolders = new[]
                {
                    new { uri = "file:///C:/%00", name = "invalid" },
                },
                capabilities = new { },
            }),
            Request(2, "shutdown", null),
            Notification("exit", null));
        using var output = new MemoryStream();

        var exitCode = await LanguageServer.RunAsync(input, output);
        var error = Response(ReadMessages(output.ToArray()), 1)
            .GetProperty("error");

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual(-32603, error.GetProperty("code").GetInt32());
    }

    [TestMethod]
    public async Task Hover_and_definition_use_project_semantic_symbols()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "lucent-lsp-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var projectDirectory = Path.Combine(temporaryDirectory, "src", "App");
            var exampleDirectory = Path.Combine(temporaryDirectory, "examples");
            Directory.CreateDirectory(projectDirectory);
            Directory.CreateDirectory(exampleDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(temporaryDirectory, "Demo.sln"),
                string.Empty);
            var projectPath = Path.Combine(projectDirectory, "Demo.csproj");
            var controlPath = Path.Combine(projectDirectory, "FancyControl.cs");
            var sourcePath = Path.Combine(exampleDirectory, "Custom.lui");
            await File.WriteAllTextAsync(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
                "<TargetFramework>net9.0</TargetFramework></PropertyGroup>" +
                "<ItemGroup><LucentSource Include=\"..\\..\\examples\\Custom.lui\" />" +
                "</ItemGroup></Project>");
            await File.WriteAllTextAsync(
                controlPath,
                "namespace Demo.Controls; public sealed class FancyControl : " +
                "Avalonia.Controls.ContentControl { public string? Accent { get; set; } } " +
                "public sealed record PackageInfo(string Name, string Id, string Description); " +
                "public static class PackageCatalog { public static System.Threading.Tasks.Task<PackageInfo[]> " +
                "Load(System.Threading.CancellationToken cancellationToken) => throw null!; }");
            const string source =
                "namespace Demo;\r\n" +
                "using Demo.Controls;\r\n" +
                "component Custom()\r\n" +
                "{\r\n" +
                "    private readonly State<string> query = new(\"lucent\");\r\n" +
                "    private readonly Computed<PackageInfo[]> packages = new(ct => PackageCatalog.Load(ct), []);\r\n" +
                "    Fragment Render()\r\n" +
                "    {\r\n" +
                "        return StackPanel {\r\n" +
                "            foreach (var package in packages.Value)\r\n" +
                "            keyed by package.Id {\r\n" +
                "                FancyControl { Accent: package.Description; }\r\n" +
                "            }\r\n" +
                "        };\r\n" +
                "    }\r\n" +
                "}\r\n";
            await File.WriteAllTextAsync(sourcePath, source);
            var uri = new Uri(sourcePath).AbsoluteUri;
            var accentPosition = PositionOf(source, "Accent");
            var expressionOffset = source.IndexOf("package.Description", StringComparison.Ordinal);
            var input = BuildInput(
                Request(1, "initialize", new
                {
                    capabilities = new { },
                }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new
                {
                    textDocument = new
                    {
                        uri,
                        languageId = "lucent",
                        version = 1,
                        text = source,
                    },
                }),
                Request(2, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = accentPosition,
                }),
                Request(3, "textDocument/definition", new
                {
                    textDocument = new { uri },
                    position = accentPosition,
                }),
                Request(4, "textDocument/completion", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(source, expressionOffset + "package.D".Length),
                }),
                Request(5, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(source, expressionOffset + "package.".Length),
                }),
                Request(6, "textDocument/completion", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(
                        source,
                        source.IndexOf("packages.Value", StringComparison.Ordinal) +
                        "packages.V".Length),
                }),
                Request(7, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "packages ="),
                }),
                Request(8, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "State<string>"),
                }),
                Request(9, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "new(\"lucent\")"),
                }),
                Request(10, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "PackageCatalog.Load"),
                }),
                Request(11, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "Load(ct)"),
                }),
                Request(12, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "ct =>"),
                }),
                Request(13, "textDocument/completion", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(
                        source,
                        source.IndexOf("PackageCatalog.Load", StringComparison.Ordinal) +
                        "PackageCatalog.L".Length),
                }),
                Request(14, "textDocument/definition", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "PackageCatalog.Load"),
                }),
                Request(15, "textDocument/definition", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(
                        source,
                        source.LastIndexOf("packages.Value", StringComparison.Ordinal)),
                }),
                Request(16, "textDocument/completion", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(
                        source,
                        source.IndexOf("PackageCatalog.Load", StringComparison.Ordinal) + 1),
                }),
                Request(17, "textDocument/completion", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(
                        source,
                        source.IndexOf("Load(ct)", StringComparison.Ordinal) +
                        "Load(".Length),
                }),
                Request(18, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();

            var exitCode = await LanguageServer.RunAsync(input, output);
            var messages = ReadMessages(output.ToArray());

            Assert.AreEqual(0, exitCode);
            var hover = Response(messages, 2).GetProperty("result");
            StringAssert.Contains(
                hover.GetProperty("contents").GetProperty("value").GetString()!,
                "FancyControl.Accent");
            Assert.IsFalse(
                hover.GetProperty("contents").GetProperty("value").GetString()!
                    .Contains("Native Avalonia", StringComparison.Ordinal));
            var definition = Response(messages, 3).GetProperty("result");
            Assert.AreEqual(
                new Uri(controlPath).AbsoluteUri,
                definition.GetProperty("uri").GetString());
            Assert.AreEqual(
                0,
                definition.GetProperty("range")
                    .GetProperty("start")
                    .GetProperty("line")
                    .GetInt32());
            Assert.IsTrue(Response(messages, 4)
                .GetProperty("result")
                .EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "Description"));
            StringAssert.Contains(
                Response(messages, 5)
                    .GetProperty("result")
                    .GetProperty("contents")
                    .GetProperty("value")
                    .GetString()!,
                "PackageInfo.Description");
            var computedMembers = Response(messages, 6)
                .GetProperty("result")
                .EnumerateArray()
                .Select(item => item.GetProperty("label").GetString())
                .ToArray();
            CollectionAssert.Contains(computedMembers, "Value");
            CollectionAssert.Contains(computedMembers, "IsPending");
            StringAssert.Contains(
                Response(messages, 7)
                    .GetProperty("result")
                    .GetProperty("contents")
                    .GetProperty("value")
                    .GetString()!,
                "private readonly Computed<PackageInfo[]> packages");
            StringAssert.Contains(HoverText(messages, 8), "class State<T>");
            StringAssert.Contains(HoverText(messages, 9), "State<string>.State");
            StringAssert.Contains(HoverText(messages, 10), "class Demo.Controls.PackageCatalog");
            StringAssert.Contains(HoverText(messages, 11), "PackageCatalog.Load");
            StringAssert.Contains(HoverText(messages, 12), "CancellationToken ct");
            Assert.IsTrue(Response(messages, 13)
                .GetProperty("result")
                .EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "Load"));
            Assert.AreEqual(
                new Uri(controlPath).AbsoluteUri,
                Response(messages, 14)
                    .GetProperty("result")
                    .GetProperty("uri")
                    .GetString());
            Assert.AreEqual(
                uri,
                Response(messages, 15)
                    .GetProperty("result")
                    .GetProperty("uri")
                    .GetString());
            Assert.IsTrue(Response(messages, 16)
                .GetProperty("result")
                .EnumerateArray()
                .Any(item =>
                    item.GetProperty("label").GetString() == "PackageCatalog" &&
                    item.GetProperty("kind").GetInt32() == 7));
            var argumentCompletions = Response(messages, 17)
                .GetProperty("result")
                .EnumerateArray()
                .Select(item => item.GetProperty("label").GetString())
                .ToArray();
            CollectionAssert.Contains(argumentCompletions, "ct");
            CollectionAssert.Contains(argumentCompletions, "query");
            CollectionAssert.Contains(argumentCompletions, "PackageCatalog");
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Completion_and_value_hover_use_the_native_property_type()
    {
        const string source =
            "namespace Demo;\r\n" +
            "using Avalonia.Layout;\r\n" +
            "component Main()\r\n" +
            "{\r\n" +
            "    Fragment Render()\r\n" +
            "    {\r\n" +
            "        return StackPanel {\r\n" +
            "            Orientation: Orientation.Horizontal;\r\n" +
            "            Button { Content: \"Go\"; }\r\n" +
            "        };\r\n" +
            "    }\r\n" +
            "}\r\n";
        const string uri = "file:///Completion.lui";
        var memberPosition = PositionAtOffset(
            source,
            source.IndexOf("Button {", StringComparison.Ordinal) + "Button {".Length);
        var valuePosition = PositionOf(source, "Orientation.Horizontal");
        var hoverPosition = PositionOf(source, "Horizontal");
        var input = BuildInput(
            Request(1, "initialize", new { capabilities = new { } }),
            Notification("initialized", new { }),
            Notification("textDocument/didOpen", new
            {
                textDocument = new
                {
                    uri,
                    languageId = "lucent",
                    version = 1,
                    text = source,
                },
            }),
            Request(2, "textDocument/completion", new
            {
                textDocument = new { uri },
                position = memberPosition,
            }),
            Request(3, "textDocument/completion", new
            {
                textDocument = new { uri },
                position = valuePosition,
            }),
            Request(4, "textDocument/hover", new
            {
                textDocument = new { uri },
                position = hoverPosition,
            }),
            Request(5, "shutdown", null),
            Notification("exit", null));
        using var output = new MemoryStream();

        Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
        var messages = ReadMessages(output.ToArray());
        var members = Response(messages, 2).GetProperty("result").EnumerateArray().ToArray();
        Assert.IsTrue(members.Any(item => item.GetProperty("label").GetString() == "Click"));
        Assert.IsTrue(members.Any(item => item.GetProperty("label").GetString() == "Class"));
        var values = Response(messages, 3).GetProperty("result").EnumerateArray().ToArray();
        Assert.IsTrue(values.Any(item =>
            item.GetProperty("label").GetString() == "Orientation.Horizontal"));
        StringAssert.Contains(
            Response(messages, 4)
                .GetProperty("result")
                .GetProperty("contents")
                .GetProperty("value")
                .GetString()!,
            "Orientation.Horizontal");
    }

    [TestMethod]
    public async Task Conditional_branches_share_editor_semantics_and_locals_shadow_state()
    {
        const string source =
            "namespace Demo;\r\n" +
            "component Main()\r\n" +
            "{\r\n" +
            "    private readonly State<bool> visible = new(true);\r\n" +
            "    private readonly State<string> title = new(\"state\");\r\n" +
            "    Fragment Render()\r\n" +
            "    {\r\n" +
            "        return StackPanel {\r\n" +
            "            if (visible.Value) {\r\n" +
            "                Button { Click: (title, e) => Console.WriteLine(title.Content); }\r\n" +
            "            } else {\r\n" +
            "                TextBlock { Text: title.Value; }\r\n" +
            "            }\r\n" +
            "        };\r\n" +
            "    }\r\n" +
            "}\r\n";
        const string uri = "file:///Conditional.lui";
        var localOffset = source.IndexOf("title.Content", StringComparison.Ordinal);
        var stateOffset = source.LastIndexOf("title.Value", StringComparison.Ordinal);
        var input = BuildInput(
            Request(1, "initialize", new { capabilities = new { } }),
            Notification("initialized", new { }),
            Notification("textDocument/didOpen", new
            {
                textDocument = new { uri, languageId = "lucent", version = 1, text = source },
            }),
            Request(2, "textDocument/completion", new
            {
                textDocument = new { uri },
                position = PositionAtOffset(source, localOffset + "title.C".Length),
            }),
            Request(3, "textDocument/hover", new
            {
                textDocument = new { uri },
                position = PositionAtOffset(source, localOffset),
            }),
            Request(4, "textDocument/hover", new
            {
                textDocument = new { uri },
                position = PositionAtOffset(source, stateOffset),
            }),
            Request(5, "shutdown", null),
            Notification("exit", null));
        using var output = new MemoryStream();

        Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
        var messages = ReadMessages(output.ToArray());
        Assert.IsTrue(Response(messages, 2).GetProperty("result").EnumerateArray()
            .Any(item => item.GetProperty("label").GetString() == "Content"));
        StringAssert.Contains(HoverText(messages, 3), "Button title");
        StringAssert.Contains(HoverText(messages, 4), "State<string> title");
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
        return Frame(payload);
    }

    private static byte[] Frame(byte[] payload)
    {
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

    private static JsonElement Response(
        IReadOnlyList<JsonDocument> messages,
        int id) =>
        messages.Single(message =>
                message.RootElement.TryGetProperty("id", out var responseId) &&
                responseId.ValueKind == JsonValueKind.Number &&
                responseId.GetInt32() == id)
            .RootElement;

    private static string HoverText(
        IReadOnlyList<JsonDocument> messages,
        int id) =>
        Response(messages, id)
            .GetProperty("result")
            .GetProperty("contents")
            .GetProperty("value")
            .GetString()!;

    private static object PositionOf(string text, string value)
    {
        var offset = text.IndexOf(value, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, offset);
        return PositionAtOffset(text, offset);
    }

    private static object PositionAtOffset(string text, int offset)
    {
        var prefix = text[..offset];
        var line = prefix.Count(character => character == '\n');
        var lineStart = prefix.LastIndexOf('\n') + 1;
        return new { line, character = offset - lineStart };
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
        "                Class: \"primary\";\r\n" +
        "                text: \"Increment\";\r\n" +
        "                onClick: () => count.Update(count.Value + 1);\r\n" +
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
        "                Class: \"primary\";\r\n" +
        "                text: \"Increment\";\r\n" +
        "                onClick: () => count.Update(count.Value + 1);\r\n" +
        "            }\r\n" +
        "        };\r\n" +
        "    }\r\n" +
        "}\r\n";
}
