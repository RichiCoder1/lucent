using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.LanguageServer.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NamedComponentLanguageServerContracts
{
    [TestMethod]
    public async Task NamedMethodTokensSupportPublicLspOperationsAfterUnsavedFormatting()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-named-method-map-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        var projectPath = Path.Combine(root, "Sample.csproj");
        var luiPath = Path.Combine(root, "Card.lui");
        const string source = """
            namespace Sample;
            using Lucent.Core;
            public component Card() {
                int Count = 1;

                string Describe( string prefix = "count:" ) =>
                    prefix + Count.ToString();
                string Format(int value) => value.ToString();
                string Format(string value) => value;

                <Text content={Describe() + Format(Count) + Format("!")} />
            }
            """;
        try
        {
            await File.WriteAllTextAsync(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>"
                    + "<TargetFramework>net10.0</TargetFramework>"
                    + "<RootNamespace>Sample</RootNamespace>"
                    + "<LangVersion>preview</LangVersion>"
                    + "<LucentLuiNamedComponents>true</LucentLuiNamedComponents>"
                    + "<LucentLuiPreparedAuthoring>true</LucentLuiPreparedAuthoring>"
                    + "</PropertyGroup><ItemGroup>"
                    + LanguageServerTests.CoreMetadataReference
                    + "<AdditionalFiles Include=\"Card.lui\" />"
                    + "<CompilerVisibleProperty Include=\"LucentLuiNamedComponents\" />"
                    + "<CompilerVisibleProperty Include=\"LucentLuiPreparedAuthoring\" />"
                    + "</ItemGroup></Project>"
            );
            await File.WriteAllTextAsync(
                luiPath,
                source.Replace(
                    "string Describe( string",
                    "string Describe(string",
                    StringComparison.Ordinal
                )
            );

            using var lsp = ProtocolClient.Start();
            using var initialize = await lsp.RequestAsync(
                "initialize",
                new
                {
                    initializationOptions = new { projectUri = new Uri(projectPath).AbsoluteUri },
                    capabilities = new { },
                }
            );
            await lsp.NotifyAsync("initialized", new { });
            await lsp.NotifyAsync(
                "textDocument/didOpen",
                new
                {
                    textDocument = new
                    {
                        uri = new Uri(luiPath).AbsoluteUri,
                        languageId = "lui",
                        version = 1,
                        text = source,
                    },
                }
            );
            using var diagnostics = await lsp.WaitForDiagnosticsAsync(
                new Uri(luiPath).AbsoluteUri,
                1
            );
            Assert.AreEqual(
                0,
                diagnostics
                    .RootElement.GetProperty("params")
                    .GetProperty("diagnostics")
                    .GetArrayLength(),
                diagnostics.RootElement.GetRawText()
            );

            var countUse = source.IndexOf("Count.ToString", StringComparison.Ordinal);
            using var hover = await lsp.RequestAsync(
                "textDocument/hover",
                new
                {
                    textDocument = new { uri = new Uri(luiPath).AbsoluteUri },
                    position = Position(source, countUse + 1),
                }
            );
            Assert.AreNotEqual(
                JsonValueKind.Null,
                hover.RootElement.GetProperty("result").ValueKind
            );
            StringAssert.Contains(hover.RootElement.GetRawText(), "Count");

            using var definition = await lsp.RequestAsync(
                "textDocument/definition",
                new
                {
                    textDocument = new { uri = new Uri(luiPath).AbsoluteUri },
                    position = Position(source, countUse + 1),
                }
            );
            Assert.IsTrue(
                definition.RootElement.TryGetProperty("result", out var definitionResult)
                    && definitionResult.ValueKind != JsonValueKind.Null,
                definition.RootElement.GetRawText()
            );
            StringAssert.Contains(
                definition.RootElement.GetRawText(),
                new Uri(luiPath).AbsoluteUri
            );

            using var references = await lsp.RequestAsync(
                "textDocument/references",
                new
                {
                    textDocument = new { uri = new Uri(luiPath).AbsoluteUri },
                    position = Position(source, countUse + 1),
                    context = new { includeDeclaration = true },
                }
            );
            var referenceResult = references.RootElement.GetProperty("result");
            Assert.IsTrue(
                referenceResult.ValueKind == JsonValueKind.Array
                    && referenceResult.GetArrayLength() >= 2,
                references.RootElement.GetRawText()
            );

            using var rename = await lsp.RequestAsync(
                "textDocument/rename",
                new
                {
                    textDocument = new { uri = new Uri(luiPath).AbsoluteUri },
                    position = Position(source, countUse + 1),
                    newName = "Total",
                }
            );
            var edits = rename
                .RootElement.GetProperty("result")
                .GetProperty("changes")
                .GetProperty(new Uri(luiPath).AbsoluteUri);
            Assert.IsTrue(
                edits
                    .EnumerateArray()
                    .Count(item => item.GetProperty("newText").GetString() == "Total") >= 2,
                rename.RootElement.GetRawText()
            );

            using var completion = await lsp.RequestAsync(
                "textDocument/completion",
                new
                {
                    textDocument = new { uri = new Uri(luiPath).AbsoluteUri },
                    position = Position(source, countUse + "Count.".Length),
                }
            );
            Assert.AreNotEqual(
                JsonValueKind.Null,
                completion.RootElement.GetProperty("result").ValueKind,
                completion.RootElement.GetRawText()
            );

            using var shutdown = await lsp.RequestAsync("shutdown", new { });
            await lsp.NotifyAsync("exit", new { });
            Assert.AreEqual(0, await lsp.WaitForExitAsync());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task AdditionalFileEditorConfigRefreshesPublicDiagnosticsWhileUnsaved()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-named-config-protocol-" + Guid.NewGuid().ToString("N")
        );
        var projectRoot = Path.Combine(root, "project");
        var sourceRoot = Path.Combine(root, "linked");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(sourceRoot);
        var projectPath = Path.Combine(projectRoot, "Sample.csproj");
        var luiPath = Path.Combine(sourceRoot, "Card.lui");
        var editorConfigPath = Path.Combine(sourceRoot, ".editorconfig");
        const string source =
            "namespace Sample; using Lucent.Core; using static Sample.Custom; "
            + "public component Card() { <Caption label=\"Ready\" /> }";
        const string warning =
            "root = true\n[*.lui]\ndotnet_diagnostic.LUI5003.severity = warning\n";
        const string suppressed =
            "root = true\n[*.lui]\ndotnet_diagnostic.LUI5003.severity = none\n";
        const string error = "root = true\n[*.lui]\ndotnet_diagnostic.LUI5003.severity = error\n";
        try
        {
            await File.WriteAllTextAsync(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>"
                    + "<TargetFramework>net10.0</TargetFramework>"
                    + "<LangVersion>preview</LangVersion>"
                    + "<LucentLuiNamedComponents>true</LucentLuiNamedComponents>"
                    + "<LucentLuiPreparedAuthoring>true</LucentLuiPreparedAuthoring>"
                    + "</PropertyGroup><ItemGroup>"
                    + LanguageServerTests.CoreMetadataReference
                    + "<AdditionalFiles Include=\"../linked/Card.lui\" LucentLuiLogicalPath=\"Card.lui\" />"
                    + "<AdditionalFiles Include=\"../linked/.editorconfig\" />"
                    + "<CompilerVisibleItemMetadata Include=\"AdditionalFiles\" MetadataName=\"LucentLuiLogicalPath\" />"
                    + "<CompilerVisibleProperty Include=\"LucentLuiNamedComponents\" />"
                    + "<CompilerVisibleProperty Include=\"LucentLuiPreparedAuthoring\" />"
                    + "</ItemGroup></Project>"
            );
            await File.WriteAllTextAsync(
                Path.Combine(projectRoot, "Custom.cs"),
                "namespace Sample; using Lucent.Core; public static class Custom { "
                    + "[LucentComponent] public static ComponentRecipe Caption("
                    + "[DefaultContent] string label) => null!; }"
            );
            await File.WriteAllTextAsync(luiPath, source);
            await File.WriteAllTextAsync(editorConfigPath, warning);

            using var lsp = ProtocolClient.Start();
            using var initialize = await lsp.RequestAsync(
                "initialize",
                new
                {
                    initializationOptions = new { projectUri = new Uri(projectPath).AbsoluteUri },
                    capabilities = new { },
                }
            );
            await lsp.NotifyAsync("initialized", new { });
            await lsp.NotifyAsync(
                "textDocument/didOpen",
                new
                {
                    textDocument = new
                    {
                        uri = new Uri(luiPath).AbsoluteUri,
                        languageId = "lui",
                        version = 1,
                        text = source,
                    },
                }
            );
            using (
                var diagnostics = await lsp.WaitForDiagnosticsAsync(new Uri(luiPath).AbsoluteUri, 1)
            )
                AssertSeverity(diagnostics, 2);

            await lsp.NotifyAsync(
                "textDocument/didOpen",
                new
                {
                    textDocument = new
                    {
                        uri = new Uri(editorConfigPath).AbsoluteUri,
                        languageId = "editorconfig",
                        version = 1,
                        text = suppressed,
                    },
                }
            );
            using (
                var diagnostics = await lsp.WaitForDiagnosticsAsync(new Uri(luiPath).AbsoluteUri, 1)
            )
                AssertSeverity(diagnostics, null);

            await lsp.NotifyAsync(
                "textDocument/didChange",
                new
                {
                    textDocument = new { uri = new Uri(editorConfigPath).AbsoluteUri, version = 2 },
                    contentChanges = new[] { new { text = error } },
                }
            );
            using (
                var diagnostics = await lsp.WaitForDiagnosticsAsync(new Uri(luiPath).AbsoluteUri, 1)
            )
                AssertSeverity(diagnostics, 1);

            await lsp.NotifyAsync(
                "textDocument/didClose",
                new { textDocument = new { uri = new Uri(editorConfigPath).AbsoluteUri } }
            );
            using (
                var diagnostics = await lsp.WaitForDiagnosticsAsync(new Uri(luiPath).AbsoluteUri, 1)
            )
                AssertSeverity(diagnostics, 2);

            using var shutdown = await lsp.RequestAsync("shutdown", new { });
            await lsp.NotifyAsync("exit", new { });
            Assert.AreEqual(0, await lsp.WaitForExitAsync());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        static void AssertSeverity(JsonDocument message, int? expected)
        {
            var diagnostics = message
                .RootElement.GetProperty("params")
                .GetProperty("diagnostics")
                .EnumerateArray()
                .Where(item => item.GetProperty("code").GetString() == "LUI5003")
                .ToArray();
            if (expected is null)
                Assert.AreEqual(0, diagnostics.Length, message.RootElement.GetRawText());
            else
                Assert.IsTrue(
                    diagnostics.Any(item => item.GetProperty("severity").GetInt32() == expected),
                    message.RootElement.GetRawText()
                );
        }
    }

    [TestMethod]
    public async Task NamedPreparationSupportsUnsavedLspNavigationRenameDiagnosticsAndDeletion()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-named-lsp-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        var projectPath = Path.Combine(root, "Sample.csproj");
        var luiPath = Path.Combine(root, "Card.lui");
        var companionPath = Path.Combine(root, "Card.cs");
        var source = """
            namespace Sample;
            using Lucent.Core;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            public record Model(string Name);
            [JsonSerializable(typeof(Model))]
            public partial class JsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
            public component Card(Model? model) {
                <Text content={Title(JsonSerializer.Serialize(model!, JsonContext.Default.Model))} />
            }
            """;
        var diskSource = source
            .Replace("using System.Text.Json;\n", "", StringComparison.Ordinal)
            .Replace("using System.Text.Json.Serialization;\n", "", StringComparison.Ordinal)
            .Replace(
                "[JsonSerializable(typeof(Model))]\npublic partial class JsonContext : System.Text.Json.Serialization.JsonSerializerContext { }\n",
                "",
                StringComparison.Ordinal
            )
            .Replace(
                "<Text content={Title(JsonSerializer.Serialize(model!, JsonContext.Default.Model))} />",
                "<Text content={Title(model!.Name)} />",
                StringComparison.Ordinal
            );
        var companion = """
            namespace Sample;
            using System.Text.Json;
            public sealed partial class Card
            {
                private string Title(string value) => "title:" + value;

                private static string Serialize(Model value) =>
                    JsonSerializer.Serialize(value, JsonContext.Default.Model);
            }
            """;
        try
        {
            await File.WriteAllTextAsync(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>"
                    + "<TargetFramework>net10.0</TargetFramework>"
                    + "<RootNamespace>Sample</RootNamespace>"
                    + "<LangVersion>preview</LangVersion>"
                    + "<Nullable>disable</Nullable>"
                    + "<LucentLuiNamedComponents>true</LucentLuiNamedComponents>"
                    + "<LucentLuiPreparedAuthoring>true</LucentLuiPreparedAuthoring>"
                    + "</PropertyGroup><ItemGroup>"
                    + LanguageServerTests.CoreMetadataReference
                    + "<AdditionalFiles Include=\"Card.lui\" LucentLuiLogicalPath=\"Card.lui\" LucentLuiDocumentVersion=\"1\" />"
                    + "<CompilerVisibleItemMetadata Include=\"AdditionalFiles\" MetadataName=\"LucentLuiLogicalPath\" />"
                    + "<CompilerVisibleItemMetadata Include=\"AdditionalFiles\" MetadataName=\"LucentLuiDocumentVersion\" />"
                    + "<CompilerVisibleProperty Include=\"LucentLuiNamedComponents\" />"
                    + "<CompilerVisibleProperty Include=\"LucentLuiPreparedAuthoring\" />"
                    + "</ItemGroup></Project>"
            );
            await File.WriteAllTextAsync(luiPath, diskSource);
            await File.WriteAllTextAsync(companionPath, companion);

            var coldTimer = Stopwatch.StartNew();
            using var lsp = ProtocolClient.Start();
            using var initialize = await lsp.RequestAsync(
                "initialize",
                new
                {
                    initializationOptions = new { projectUri = new Uri(projectPath).AbsoluteUri },
                    capabilities = new { },
                }
            );
            Assert.IsNotNull(initialize.RootElement.GetProperty("result"));
            await lsp.NotifyAsync("initialized", new { });
            await lsp.NotifyAsync(
                "textDocument/didOpen",
                new
                {
                    textDocument = new
                    {
                        uri = new Uri(luiPath).AbsoluteUri,
                        languageId = "lui",
                        version = 1,
                        text = source,
                    },
                }
            );
            using var initialDiagnostics = await lsp.WaitForDiagnosticsAsync(
                new Uri(luiPath).AbsoluteUri,
                1
            );
            var initialDiagnosticArray = initialDiagnostics
                .RootElement.GetProperty("params")
                .GetProperty("diagnostics");
            Assert.AreEqual(
                0,
                initialDiagnosticArray.GetArrayLength(),
                $"the prepared named component did not bind cleanly in the LSP project: {initialDiagnosticArray.GetRawText()}"
            );
            var coldMilliseconds = coldTimer.Elapsed.TotalMilliseconds;

            var titleOffset = source.IndexOf("Title", StringComparison.Ordinal);
            var unchangedTimer = Stopwatch.StartNew();
            using var hover = await lsp.RequestAsync(
                "textDocument/hover",
                new
                {
                    textDocument = new { uri = new Uri(luiPath).AbsoluteUri },
                    position = Position(source, titleOffset + 1),
                }
            );
            Assert.IsNotNull(hover.RootElement.GetProperty("result"));
            StringAssert.Contains(
                hover
                    .RootElement.GetProperty("result")
                    .GetProperty("contents")[0]
                    .GetProperty("value")
                    .GetString(),
                "Title"
            );
            var unchangedMilliseconds = unchangedTimer.Elapsed.TotalMilliseconds;

            using var prepareRename = await lsp.RequestAsync(
                "textDocument/prepareRename",
                new
                {
                    textDocument = new { uri = new Uri(luiPath).AbsoluteUri },
                    position = Position(source, titleOffset + 1),
                }
            );
            Assert.AreNotEqual(
                JsonValueKind.Null,
                prepareRename.RootElement.GetProperty("result").ValueKind,
                $"prepareRename did not resolve the companion symbol: {prepareRename.RootElement.GetRawText()}"
            );

            using var definition = await lsp.RequestAsync(
                "textDocument/definition",
                new
                {
                    textDocument = new { uri = new Uri(luiPath).AbsoluteUri },
                    position = Position(source, titleOffset + 1),
                }
            );
            var definitionUri = definition
                .RootElement.GetProperty("result")
                .GetProperty("uri")
                .GetString();
            Assert.AreEqual(new Uri(companionPath).AbsoluteUri, definitionUri);

            using var rename = await lsp.RequestAsync(
                "textDocument/rename",
                new
                {
                    textDocument = new { uri = new Uri(luiPath).AbsoluteUri },
                    position = Position(source, titleOffset + 1),
                    newName = "RenamedTitle",
                }
            );
            var renameResult = rename.RootElement.GetProperty("result");
            Assert.AreNotEqual(
                JsonValueKind.Null,
                renameResult.ValueKind,
                $"rename did not resolve the companion symbol: {rename.RootElement.GetRawText()}"
            );
            var changes = renameResult.GetProperty("changes");
            Assert.IsTrue(changes.TryGetProperty(new Uri(luiPath).AbsoluteUri, out var luiEdits));
            Assert.IsTrue(
                changes.TryGetProperty(new Uri(companionPath).AbsoluteUri, out var companionEdits)
            );
            Assert.IsTrue(
                luiEdits
                    .EnumerateArray()
                    .Any(edit => edit.GetProperty("newText").GetString() == "RenamedTitle")
            );
            Assert.IsTrue(
                companionEdits
                    .EnumerateArray()
                    .Any(edit => edit.GetProperty("newText").GetString() == "RenamedTitle")
            );

            var invalid = source.Replace(
                "public record Model(string Name);",
                "public record Model(string Name)",
                StringComparison.Ordinal
            );
            var editTimer = Stopwatch.StartNew();
            await lsp.NotifyAsync(
                "textDocument/didChange",
                new
                {
                    textDocument = new { uri = new Uri(luiPath).AbsoluteUri, version = 2 },
                    contentChanges = new[] { new { text = invalid } },
                }
            );
            using var invalidDiagnostics = await lsp.WaitForDiagnosticsAsync(
                new Uri(luiPath).AbsoluteUri,
                2
            );
            Assert.IsTrue(
                invalidDiagnostics
                    .RootElement.GetProperty("params")
                    .GetProperty("diagnostics")
                    .EnumerateArray()
                    .Any(item => item.GetProperty("code").GetString() == "LUI1027")
            );
            var editMilliseconds = editTimer.Elapsed.TotalMilliseconds;

            await lsp.NotifyAsync(
                "textDocument/didChange",
                new
                {
                    textDocument = new { uri = new Uri(luiPath).AbsoluteUri, version = 3 },
                    contentChanges = new[] { new { text = source } },
                }
            );
            using var recoveredDiagnostics = await lsp.WaitForDiagnosticsAsync(
                new Uri(luiPath).AbsoluteUri,
                3
            );
            Assert.AreEqual(
                0,
                recoveredDiagnostics
                    .RootElement.GetProperty("params")
                    .GetProperty("diagnostics")
                    .EnumerateArray()
                    .Count(item => item.GetProperty("code").GetString() == "LUI1027")
            );

            var withoutJsonDeclaration = source
                .Replace("[JsonSerializable(typeof(Model))]\r\n", "", StringComparison.Ordinal)
                .Replace("[JsonSerializable(typeof(Model))]\n", "", StringComparison.Ordinal);
            await lsp.NotifyAsync(
                "textDocument/didChange",
                new
                {
                    textDocument = new { uri = new Uri(luiPath).AbsoluteUri, version = 4 },
                    contentChanges = new[] { new { text = withoutJsonDeclaration } },
                }
            );
            using var removedDeclarationDiagnostics = await lsp.WaitForDiagnosticsAsync(
                new Uri(luiPath).AbsoluteUri,
                4
            );
            var removedDeclarationArray = removedDeclarationDiagnostics
                .RootElement.GetProperty("params")
                .GetProperty("diagnostics");
            Assert.IsTrue(
                removedDeclarationArray
                    .EnumerateArray()
                    .Any(item =>
                        item.GetProperty("code").GetString() == "LUI2000"
                        && item.GetProperty("message")
                            .GetString()
                            ?.Contains("JsonContext", StringComparison.Ordinal) == true
                    ),
                $"removing the JSON source-generator declaration left its generated API silently available: {removedDeclarationArray.GetRawText()}"
            );
            await lsp.NotifyAsync(
                "textDocument/didChange",
                new
                {
                    textDocument = new { uri = new Uri(luiPath).AbsoluteUri, version = 5 },
                    contentChanges = new[] { new { text = source } },
                }
            );
            using var restoredDeclarationDiagnostics = await lsp.WaitForDiagnosticsAsync(
                new Uri(luiPath).AbsoluteUri,
                5
            );
            Assert.AreEqual(
                0,
                restoredDeclarationDiagnostics
                    .RootElement.GetProperty("params")
                    .GetProperty("diagnostics")
                    .GetArrayLength(),
                "restoring the JSON source-generator declaration did not restore the generated API."
            );

            await lsp.NotifyAsync(
                "textDocument/didClose",
                new { textDocument = new { uri = new Uri(luiPath).AbsoluteUri } }
            );
            using var closedDiagnostics = await lsp.WaitForDiagnosticsAsync(
                new Uri(luiPath).AbsoluteUri,
                5
            );
            File.Delete(luiPath);
            using var reload = await lsp.RequestAsync(
                "workspace/didChangeWatchedFiles",
                new { changes = new[] { new { uri = new Uri(luiPath).AbsoluteUri, type = 3 } } }
            );
            using var deletedHover = await lsp.RequestAsync(
                "textDocument/hover",
                new
                {
                    textDocument = new { uri = new Uri(luiPath).AbsoluteUri },
                    position = Position(source, titleOffset + 1),
                }
            );
            Assert.IsTrue(
                deletedHover.RootElement.TryGetProperty("result", out var deletedResult),
                $"deleted-source hover returned an error: {deletedHover.RootElement.GetRawText()}"
            );
            Assert.AreEqual(
                JsonValueKind.Null,
                deletedResult.ValueKind,
                "deleting the source file left the named component and its generated JSON API reachable."
            );

            using var shutdown = await lsp.RequestAsync("shutdown", new { });
            Assert.AreEqual(
                JsonValueKind.Null,
                shutdown.RootElement.GetProperty("result").ValueKind
            );
            await lsp.NotifyAsync("exit", new { });
            Assert.AreEqual(0, await lsp.WaitForExitAsync());
            Console.WriteLine(
                $"Named LSP timings: cold={coldMilliseconds:F1}ms unchanged={unchangedMilliseconds:F1}ms edit={editMilliseconds:F1}ms"
            );
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task NamedPreparationHonorsCancellationBeforeEvaluation()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-named-lsp-cancel-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "Sample.csproj");
            var documentPath = Path.Combine(root, "Card.lui");
            await File.WriteAllTextAsync(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>"
                    + "<TargetFramework>net10.0</TargetFramework>"
                    + "<LangVersion>preview</LangVersion>"
                    + "<LucentLuiNamedComponents>true</LucentLuiNamedComponents>"
                    + "<LucentLuiPreparedAuthoring>true</LucentLuiPreparedAuthoring>"
                    + "</PropertyGroup><ItemGroup>"
                    + LanguageServerTests.CoreMetadataReference
                    + "<AdditionalFiles Include=\"Card.lui\" LucentLuiLogicalPath=\"Card.lui\" />"
                    + "<CompilerVisibleItemMetadata Include=\"AdditionalFiles\" MetadataName=\"LucentLuiLogicalPath\" />"
                    + "<CompilerVisibleProperty Include=\"LucentLuiNamedComponents\" />"
                    + "<CompilerVisibleProperty Include=\"LucentLuiPreparedAuthoring\" />"
                    + "</ItemGroup></Project>"
            );
            await File.WriteAllTextAsync(
                documentPath,
                "public component Card() { <Text content={\"ready\"} /> }"
            );

            using var context = await LuiProjectContext.LoadAsync(
                projectPath,
                CancellationToken.None
            );
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var observed = false;
            try
            {
                await context.CompileAsync(new Uri(documentPath), canceled.Token);
            }
            catch (OperationCanceledException)
            {
                observed = true;
            }

            Assert.IsTrue(observed, "Canceled named preparation did not stop before evaluation.");
            Assert.AreEqual(0, context.EvaluationBuildCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static object Position(string text, int offset)
    {
        var line = 0;
        var lineStart = 0;
        while (true)
        {
            var newline = text.IndexOf('\n', lineStart);
            if (newline < 0 || newline >= offset)
                return new { line, character = offset - lineStart };
            line++;
            lineStart = newline + 1;
        }
    }

    private sealed class ProtocolClient : IDisposable
    {
        private readonly Process process;
        private readonly Stream input;
        private readonly Stream output;
        private readonly Queue<JsonDocument> notifications = [];
        private int id;

        private ProtocolClient(Process process)
        {
            this.process = process;
            input = process.StandardOutput.BaseStream;
            output = process.StandardInput.BaseStream;
        }

        internal static ProtocolClient Start()
        {
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
            var built = Path.GetFullPath(
                $"src/Lucent.Lui.LanguageServer/bin/{configuration}/net10.0/Lucent.Lui.LanguageServer.exe"
            );
            var executable = File.Exists(built)
                ? built
                : Path.Combine(AppContext.BaseDirectory, "Lucent.Lui.LanguageServer.exe");
            return new ProtocolClient(
                Process.Start(
                    new ProcessStartInfo(executable)
                    {
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                    }
                ) ?? throw new InvalidOperationException("Could not start the LSP process.")
            );
        }

        internal async Task<JsonDocument> RequestAsync(string method, object parameters)
        {
            var requestId = ++id;
            await WriteAsync(
                JsonSerializer.Serialize(
                    new
                    {
                        jsonrpc = "2.0",
                        id = requestId,
                        method,
                        @params = parameters,
                    }
                )
            );
            while (true)
            {
                var message = await ReadAsync();
                if (
                    message.RootElement.TryGetProperty("id", out var responseId)
                    && responseId.GetInt32() == requestId
                )
                    return message;
                notifications.Enqueue(message);
            }
        }

        internal async Task NotifyAsync(string method, object parameters) =>
            await WriteAsync(
                JsonSerializer.Serialize(
                    new
                    {
                        jsonrpc = "2.0",
                        method,
                        @params = parameters,
                    }
                )
            );

        internal async Task<JsonDocument> WaitForNotificationAsync(string method)
        {
            for (var count = notifications.Count; count > 0; count--)
            {
                var message = notifications.Dequeue();
                if (
                    message.RootElement.TryGetProperty("method", out var name)
                    && name.GetString() == method
                )
                    return message;
                notifications.Enqueue(message);
            }
            while (true)
            {
                var message = await ReadAsync().WaitAsync(TimeSpan.FromSeconds(20));
                if (
                    message.RootElement.TryGetProperty("method", out var name)
                    && name.GetString() == method
                )
                    return message;
                notifications.Enqueue(message);
            }
        }

        internal async Task<JsonDocument> WaitForDiagnosticsAsync(string uri, int version)
        {
            while (true)
            {
                var message = await WaitForNotificationAsync("textDocument/publishDiagnostics");
                var parameters = message.RootElement.GetProperty("params");
                if (
                    parameters.GetProperty("uri").GetString() == uri
                    && parameters.TryGetProperty("version", out var actualVersion)
                    && actualVersion.GetInt32() == version
                )
                    return message;
                message.Dispose();
            }
        }

        internal async Task<int> WaitForExitAsync()
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            return process.ExitCode;
        }

        private async Task WriteAsync(string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            await output.WriteAsync(
                Encoding.ASCII.GetBytes(
                    "Content-Length: "
                        + bytes.Length.ToString(CultureInfo.InvariantCulture)
                        + "\r\n\r\n"
                )
            );
            await output.WriteAsync(bytes);
            await output.FlushAsync();
        }

        private async Task<JsonDocument> ReadAsync()
        {
            var header = new StringBuilder();
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                var next = new byte[1];
                if (await input.ReadAsync(next) == 0)
                    throw new EndOfStreamException("LSP ended before a response.");
                header.Append((char)next[0]);
            }
            var length = Int32.Parse(
                header.ToString().Split(':', 2)[1],
                CultureInfo.InvariantCulture
            );
            var body = new byte[length];
            for (var read = 0; read < body.Length; )
            {
                var count = await input.ReadAsync(body.AsMemory(read));
                if (count == 0)
                    throw new EndOfStreamException("LSP ended before its message body.");
                read += count;
            }
            return JsonDocument.Parse(body);
        }

        public void Dispose()
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.Dispose();
        }
    }
}
