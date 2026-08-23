using System.Text;
using System.Text.Json;
using Lucent.Compiler;
using Lucent.LanguageServer;

namespace Lucent.LanguageServer.Tests;

[TestClass]
public sealed class LanguageServerProtocolTests
{
    [TestMethod]
    public async Task Native_binding_protocol_completes_paths_and_explains_inherited_context()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"lucent-binding-{Guid.NewGuid():N}.lui");
        const string source = "namespace Demo; using System; using Avalonia.Controls; component App() => ListBox { template ItemTemplate(Uri item) { TextBlock { Text: binding(item.Host); } } };";
        await File.WriteAllTextAsync(sourcePath, source);
        try
        {
            var uri = new Uri(sourcePath).AbsoluteUri;
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
                    position = PositionAtOffset(source,
                        source.IndexOf("item.Host", StringComparison.Ordinal) + "item.H".Length),
                }),
                Request(3, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "binding"),
                }),
                Request(4, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = ReadMessages(output.ToArray());
            Assert.IsTrue(Response(messages, 2).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "Host"));
            StringAssert.Contains(HoverText(messages, 3), "inherited DataContext");
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [TestMethod]
    public async Task Project_implicit_usings_preserve_method_group_conversions()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-implicit-usings",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var projectPath = Path.Combine(directory, "Demo.csproj");
            var sourcePath = Path.Combine(directory, "App.lui");
            await File.WriteAllTextAsync(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
                  <ItemGroup><PackageReference Include="Avalonia" Version="12.1.1" /><LucentSource Include="App.lui" /></ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(directory, "DelegateCommand.cs"), """
                using System.Windows.Input;
                namespace Demo;
                internal sealed class DelegateCommand(Action execute, Func<bool>? canExecute = null) : ICommand
                {
                    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
                    public void Execute(object? parameter) => execute();
                    public event EventHandler? CanExecuteChanged;
                }
                """);
            const string source = """
                namespace Demo;
                component App()
                {
                    private readonly DelegateCommand command = new DelegateCommand(Execute, CanExecute);
                    private void Execute() { }
                    private bool CanExecute() => true;
                    Fragment Render() => Border {};
                }
                """;
            await File.WriteAllTextAsync(sourcePath, source);
            var uri = new Uri(sourcePath).AbsoluteUri;
            var input = BuildInput(
                Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new
                {
                    textDocument = new { uri, languageId = "lucent", version = 1, text = source },
                }),
                Request(2, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var diagnostics = PublishedDiagnostics(ReadMessages(output.ToArray()), uri)
                .SelectMany(items => items.EnumerateArray())
                .ToArray();
            Assert.IsFalse(diagnostics.Any(diagnostic =>
                diagnostic.GetProperty("message").GetString()?.Contains(
                    "cannot convert from 'method group'", StringComparison.Ordinal) == true),
                string.Join(Environment.NewLine, diagnostics.Select(diagnostic =>
                    diagnostic.GetProperty("message").GetString())));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Event_islands_complete_hover_and_define_ordinary_component_members()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"lucent-members-{Guid.NewGuid():N}.lui");
        const string source = """
            namespace Demo;
            using System.Threading;
            component App()
            {
                private readonly CancellationTokenSource focusSidebar = new();
                Fragment Render() => Border {
                    Loaded: (sender, e) => { focusSidebar.Cancel(); };
                };
            }
            """;
        await File.WriteAllTextAsync(sourcePath, source);
        try
        {
            var uri = new Uri(sourcePath).AbsoluteUri;
            var use = source.LastIndexOf("focusSidebar", StringComparison.Ordinal);
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
                    position = PositionAtOffset(source, use + "focusS".Length),
                }),
                Request(3, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(source, use),
                }),
                Request(4, "textDocument/definition", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(source, use),
                }),
                Request(5, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = ReadMessages(output.ToArray());
            Assert.IsTrue(Response(messages, 2).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "focusSidebar"));
            StringAssert.Contains(HoverText(messages, 3), "focusSidebar");
            var definition = Response(messages, 4).GetProperty("result");
            Assert.AreEqual(uri, definition.GetProperty("uri").GetString());
            Assert.AreEqual(source[..source.IndexOf("focusSidebar", StringComparison.Ordinal)]
                    .Count(character => character == '\n'),
                definition.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [TestMethod]
    public async Task Same_batch_generated_components_complete_and_hover_exact_mount_root()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-mount-root-lsp",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var hostPath = Path.Combine(directory, "Host.lui");
            var dialogPath = Path.Combine(directory, "Dialog.lui");
            await File.WriteAllTextAsync(Path.Combine(directory, "Demo.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Avalonia" Version="12.1.1" />
                    <LucentSource Include="Host.lui" />
                    <LucentSource Include="Dialog.lui" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(dialogPath,
                "namespace Demo; component Dialog() => Window { Title: \"Dialog\"; };");
            const string source = """
                namespace Demo;
                component Host()
                {
                    private void Show()
                    {
                        using var dialog = new DialogComponent();
                        dialog.MountRoot();
                    }
                    Fragment Render() => Border {};
                }
                """;
            await File.WriteAllTextAsync(hostPath, source);
            var uri = new Uri(hostPath).AbsoluteUri;
            var member = source.IndexOf("dialog.MountRoot", StringComparison.Ordinal) + "dialog.".Length;
            var input = BuildInput(
                Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new
                {
                    textDocument = new { uri, languageId = "lucent", version = 1, text = source },
                }),
                Request(2, "textDocument/completion", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(source, member),
                }),
                Request(3, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(source, member + 1),
                }),
                Request(4, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = ReadMessages(output.ToArray());
            Assert.IsTrue(Response(messages, 2).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "MountRoot"));
            var hover = HoverText(messages, 3);
            StringAssert.Contains(hover, "MountRoot");
            StringAssert.Contains(hover, "Window DialogComponent.MountRoot()");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Attached_property_completion_uses_native_setter_symbols()
    {
        const string source = "namespace Demo; using Avalonia.Controls; component App() => Border { Grid.; };";
        var offset = source.IndexOf("Grid.", StringComparison.Ordinal) + "Grid.".Length;

        var items = LucentCompiler.GetCompletions(source, offset, "App.lui");

        Assert.IsTrue(items.Any(item => item.Label == "Row"));
    }

    [TestMethod]
    public async Task Attached_property_protocol_completion_hover_and_definition_use_project_symbols()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-attached-lsp-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var projectPath = Path.Combine(directory, "Demo.csproj");
            var ownerPath = Path.Combine(directory, "TestOwner.cs");
            var sourcePath = Path.Combine(directory, "App.lui");
            await File.WriteAllTextAsync(projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>" +
                "<ItemGroup><LucentSource Include=\"App.lui\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(ownerPath,
                "namespace Demo; public static class TestOwner { " +
                "public static readonly Avalonia.AvaloniaProperty GoodProperty = null!; " +
                "public static void SetGood(Avalonia.Controls.Control target, int value) { } }");
            const string source = "namespace Demo; using Avalonia.Controls; component App() => Border { TestOwner.Good: 1; };";
            await File.WriteAllTextAsync(sourcePath, source);
            var uri = new Uri(sourcePath).AbsoluteUri;
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
                    position = PositionAtOffset(source, source.IndexOf("TestOwner.Good", StringComparison.Ordinal) + "TestOwner.".Length),
                }),
                Request(3, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "Good"),
                }),
                Request(4, "textDocument/definition", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "Good"),
                }),
                Request(5, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();
            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = ReadMessages(output.ToArray());
            Assert.IsTrue(Response(messages, 2).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "Good"));
            StringAssert.Contains(HoverText(messages, 3), "TestOwner.SetGood");
            Assert.AreEqual(new Uri(ownerPath).AbsoluteUri,
                Response(messages, 4).GetProperty("result").GetProperty("uri").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
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

    [TestMethod]
    public async Task Async_boundary_catch_local_is_scoped_in_protocol_tooling()
    {
        const string source = """
            namespace Demo;
            using System;
            using System.Threading.Tasks;
            component Main()
            {
                private readonly Computed<int> packages = new(ct => Task.FromResult(1), 0);
                Fragment Render() => ContentControl {
                    try (packages) {
                        TextBlock { Text: packages.Value.ToString(); Tag: erro; }
                    }
                    loading {
                        ProgressBar { Value: 0; Tag: packages.; DefinitelyNotAProperty: 1; }
                    }
                    catch (Exception error) {
                        TextBlock { Text: error.Message; }
                    }
                };
            }
            """;
        var sourcePath = Path.Combine(Path.GetTempPath(), $"lucent-boundary-{Guid.NewGuid():N}.lui");
        await File.WriteAllTextAsync(sourcePath, source);
        try
        {
            var uri = new Uri(sourcePath).AbsoluteUri;
            var outside = source.IndexOf("erro;", StringComparison.Ordinal);
            var loadingControl = source.IndexOf("ProgressBar", StringComparison.Ordinal);
            var loadingSource = source.IndexOf("packages.;", StringComparison.Ordinal) + "packages.".Length;
            var declaration = source.IndexOf("error)", StringComparison.Ordinal);
            var use = source.LastIndexOf("error.Message", StringComparison.Ordinal);
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
                position = PositionAtOffset(source, use + "error.M".Length),
            }),
            Request(3, "textDocument/hover", new
            {
                textDocument = new { uri },
                position = PositionAtOffset(source, use),
            }),
            Request(4, "textDocument/definition", new
            {
                textDocument = new { uri },
                position = PositionAtOffset(source, use),
            }),
            Request(5, "textDocument/completion", new
            {
                textDocument = new { uri },
                position = PositionAtOffset(source, outside + "erro".Length),
            }),
            Request(6, "textDocument/hover", new
            {
                textDocument = new { uri },
                position = PositionAtOffset(source, loadingControl),
            }),
            Request(7, "textDocument/completion", new
            {
                textDocument = new { uri },
                position = PositionAtOffset(source, loadingSource),
            }),
            Request(8, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = ReadMessages(output.ToArray());
            Assert.IsTrue(Response(messages, 2).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "Message"));
            StringAssert.Contains(HoverText(messages, 3), "Exception error");
            var definition = Response(messages, 4).GetProperty("result");
            Assert.AreEqual(uri, definition.GetProperty("uri").GetString());
            var declarationPrefix = source[..declaration];
            var definitionStart = definition.GetProperty("range").GetProperty("start");
            Assert.AreEqual(declarationPrefix.Count(character => character == '\n'),
                definitionStart.GetProperty("line").GetInt32());
            Assert.AreEqual(declaration - (declarationPrefix.LastIndexOf('\n') + 1),
                definitionStart.GetProperty("character").GetInt32());
            Assert.IsFalse(Response(messages, 5).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "error"));
            StringAssert.Contains(HoverText(messages, 6), "ProgressBar");
            Assert.IsTrue(Response(messages, 7).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "IsPending"));
            var loadingDiagnostic = PublishedDiagnostics(messages, uri)
                .SelectMany(batch => batch.EnumerateArray())
                .First(item => item.GetProperty("message").GetString()?.Contains(
                    "DefinitelyNotAProperty", StringComparison.Ordinal) == true);
            var diagnosticOffset = source.IndexOf("DefinitelyNotAProperty", StringComparison.Ordinal);
            var diagnosticPrefix = source[..diagnosticOffset];
            var actualPosition = loadingDiagnostic.GetProperty("range").GetProperty("start");
            Assert.AreEqual(diagnosticPrefix.Count(character => character == '\n'),
                actualPosition.GetProperty("line").GetInt32());
            Assert.AreEqual(diagnosticOffset - (diagnosticPrefix.LastIndexOf('\n') + 1),
                actualPosition.GetProperty("character").GetInt32());
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [TestMethod]
    public async Task Cross_file_component_semantics_follow_unsaved_sibling_generations()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-lsp-composition-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var projectPath = Path.Combine(directory, "Demo.csproj");
            var callerPath = Path.Combine(directory, "Main.lui");
            var calleePath = Path.Combine(directory, "Child.lui");
            var caller = "namespace Demo; component Main() => Window { Child(title: \"caller\") {} };";
            var callee = "namespace Demo; component Child(string title = \"disk\") => TextBlock { Text: title; };";
            await File.WriteAllTextAsync(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><LucentSource Include=\"Main.lui\" /><LucentSource Include=\"Child.lui\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(callerPath, caller);
            await File.WriteAllTextAsync(calleePath, callee);

            var callerUri = new Uri(callerPath).AbsoluteUri;
            var calleeUri = new Uri(calleePath).AbsoluteUri;
            var callPosition = PositionAtOffset(caller, caller.IndexOf("Child", StringComparison.Ordinal) + 5);
            var input = BuildInput(
                Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = callerUri, languageId = "lucent", version = 1, text = caller } }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = calleeUri, languageId = "lucent", version = 1, text = callee } }),
                Request(10, "textDocument/completion", new { textDocument = new { uri = callerUri }, position = callPosition }),
                Request(11, "textDocument/hover", new { textDocument = new { uri = callerUri }, position = callPosition }),
                Request(12, "textDocument/definition", new { textDocument = new { uri = callerUri }, position = callPosition }),
                Notification("textDocument/didChange", new { textDocument = new { uri = calleeUri, version = 2 }, contentChanges = new[] { new { text = "namespace Demo; component Child(int count = 1) => TextBlock { Text: count.ToString(); };" } } }),
                Request(20, "textDocument/completion", new { textDocument = new { uri = callerUri }, position = callPosition }),
                Request(21, "textDocument/hover", new { textDocument = new { uri = callerUri }, position = callPosition }),
                Request(22, "textDocument/definition", new { textDocument = new { uri = callerUri }, position = callPosition }),
                Notification("textDocument/didChange", new { textDocument = new { uri = calleeUri, version = 3 }, contentChanges = new[] { new { text = callee } } }),
                Request(30, "textDocument/definition", new { textDocument = new { uri = callerUri }, position = callPosition }),
                Notification("textDocument/didChange", new { textDocument = new { uri = calleeUri, version = 4 }, contentChanges = new[] { new { text = "namespace Demo; component Child(int count = 1) => TextBlock { Text: count.ToString(); };" } } }),
                Notification("textDocument/didClose", new { textDocument = new { uri = calleeUri } }),
                Request(40, "textDocument/hover", new { textDocument = new { uri = callerUri }, position = callPosition }),
                Request(41, "textDocument/definition", new { textDocument = new { uri = callerUri }, position = callPosition }),
                Request(99, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();
            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = ReadMessages(output.ToArray());

            StringAssert.Contains(HoverText(messages, 11), "component Demo.Child");
            Assert.AreEqual(calleeUri, Response(messages, 12).GetProperty("result").GetProperty("uri").GetString());
            Assert.IsTrue(Response(messages, 20).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "count"));
            StringAssert.Contains(HoverText(messages, 21), "component Demo.Child");
            Assert.AreEqual(calleeUri, Response(messages, 22).GetProperty("result").GetProperty("uri").GetString());
            Assert.IsTrue(PublishedDiagnostics(messages, callerUri).Any(diagnostics => diagnostics.GetArrayLength() > 0));
            var callerDiagnosticBatches = PublishedDiagnostics(messages, callerUri).ToArray();
            Assert.IsTrue(callerDiagnosticBatches.Any(diagnostics => diagnostics.GetArrayLength() == 0),
                string.Join("; ", callerDiagnosticBatches.Select(diagnostics => diagnostics.GetRawText())));
            Assert.AreEqual(calleeUri, Response(messages, 30).GetProperty("result").GetProperty("uri").GetString());
            StringAssert.Contains(HoverText(messages, 40), "component Demo.Child");
            Assert.AreEqual(calleeUri, Response(messages, 41).GetProperty("result").GetProperty("uri").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Project_analysis_does_not_resolve_components_from_another_project()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-lsp-isolation-tests",
            Guid.NewGuid().ToString("N"));
        var first = Path.Combine(directory, "First");
        var second = Path.Combine(directory, "Second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        try
        {
            var firstProject = Path.Combine(first, "First.csproj");
            var secondProject = Path.Combine(second, "Second.csproj");
            var firstMain = Path.Combine(first, "Main.lui");
            var firstChild = Path.Combine(first, "Child.lui");
            var secondMain = Path.Combine(second, "Main.lui");
            var secondChild = Path.Combine(second, "Child.lui");
            const string projectTemplate = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><LucentSource Include=\"Main.lui\" /><LucentSource Include=\"Child.lui\" /></ItemGroup></Project>";
            const string main = "namespace Demo; component Main() => Window { Child {} };";
            const string child = "namespace Demo; component Child() => TextBlock { Text: \"child\"; };";
            await File.WriteAllTextAsync(firstProject, projectTemplate);
            await File.WriteAllTextAsync(secondProject, projectTemplate);
            await File.WriteAllTextAsync(firstMain, main);
            await File.WriteAllTextAsync(firstChild, "namespace Demo; component Other() => TextBlock {};" );
            await File.WriteAllTextAsync(secondMain, main);
            await File.WriteAllTextAsync(secondChild, child);

            var firstMainUri = new Uri(firstMain).AbsoluteUri;
            var firstChildUri = new Uri(firstChild).AbsoluteUri;
            var secondMainUri = new Uri(secondMain).AbsoluteUri;
            var secondChildUri = new Uri(secondChild).AbsoluteUri;
            using var input = BuildInput(
                Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = firstMainUri, languageId = "lucent", version = 1, text = main } }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = firstChildUri, languageId = "lucent", version = 1, text = "namespace Demo; component Other() => TextBlock {};" } }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = secondMainUri, languageId = "lucent", version = 1, text = main } }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = secondChildUri, languageId = "lucent", version = 1, text = child } }),
                Request(2, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();
            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var diagnostics = PublishedDiagnostics(ReadMessages(output.ToArray()), firstMainUri).ToArray();
            Assert.IsTrue(diagnostics.Any(batch => batch.EnumerateArray().Any(item =>
                item.GetProperty("code").GetString() == "LUC2001")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IEnumerable<JsonElement> PublishedDiagnostics(
        IReadOnlyList<JsonDocument> messages,
        string uri) => messages
        .Where(message => message.RootElement.TryGetProperty("method", out var method) &&
            method.GetString() == "textDocument/publishDiagnostics" &&
            message.RootElement.GetProperty("params").GetProperty("uri").GetString() == uri)
        .Select(message => message.RootElement.GetProperty("params").GetProperty("diagnostics"));

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
