using System.Text;
using System.Text.Json;
using Lucent.LanguageServer;

namespace Lucent.LanguageServer.Tests;

[TestClass]
public sealed class NativeCSharpQualityMatrixTests
{
    [TestMethod]
    public async Task Supported_native_csharp_quality_cells_execute_protocol_assertions()
    {
        // This intentionally executes protocol requests; it is not a reflection
        // check that another test happens to exist.
        foreach (var cell in QualityCells)
        {
            var path = Path.Combine(Path.GetTempPath(), $"lucent-matrix-{Guid.NewGuid():N}.lui");
            await File.WriteAllTextAsync(path, cell.Source);
            try
            {
                var uri = new Uri(path).AbsoluteUri;
                using var input = Input(
                    Request(1, "initialize", new { capabilities = new { } }),
                    Notification("initialized", new { }),
                    Notification("textDocument/didOpen", new { textDocument = new { uri, languageId = "lucent", version = 1, text = cell.Source } }),
                    Request(2, "textDocument/completion", new { textDocument = new { uri }, position = Position(cell.Source, cell.CompletionNeedle, cell.CompletionCharacters) }),
                    Request(3, "textDocument/hover", new { textDocument = new { uri }, position = Position(cell.Source, cell.HoverNeedle, 0) }),
                    Request(4, "shutdown", null), Notification("exit", null));
                using var output = new MemoryStream();

                Assert.AreEqual(0, await LanguageServer.RunAsync(input, output), cell.Context);
                var messages = Read(output.ToArray());
                var completion = Response(messages, 2).GetProperty("result").EnumerateArray()
                    .SingleOrDefault(item => item.GetProperty("label").GetString() == cell.ExpectedLabel);
                Assert.AreNotEqual(JsonValueKind.Undefined, completion.ValueKind, $"{cell.Context} completion must contain {cell.ExpectedLabel}.");
                Assert.AreEqual(cell.ExpectedKind, completion.GetProperty("kind").GetInt32(), cell.Context);
                Assert.IsTrue(completion.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(detail.GetString()), cell.Context);
                Assert.IsTrue(completion.TryGetProperty("insertText", out var insert) && insert.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(insert.GetString()), cell.Context);
                Assert.IsFalse(completion.GetProperty("detail").GetString()!.Contains("global::", StringComparison.Ordinal), cell.Context);

                var hover = Response(messages, 3).GetProperty("result");
                Assert.AreNotEqual(JsonValueKind.Null, hover.ValueKind, $"{cell.Context} hover must not be a dead spot.");
                var hoverText = hover.GetProperty("contents").GetProperty("value").GetString()!;
                StringAssert.Contains(hoverText, cell.HoverText, cell.Context);
                Assert.IsFalse(hoverText.Contains("global::", StringComparison.Ordinal), cell.Context);
            }
            finally { File.Delete(path); }
        }
    }

    [TestMethod]
    public async Task Matrix_executes_malformed_utf16_unsaved_overlay_and_generation_freshness()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lucent-matrix-overlay-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var project = Path.Combine(directory, "Demo.csproj");
            var child = Path.Combine(directory, "Child.lui");
            var app = Path.Combine(directory, "App.lui");
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><LucentSource Include=\"*.lui\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(child, "namespace Demo; component Child() => TextBlock {};");
            const string malformed = "namespace Demo; component App() => Child { Text: ; }; // 😀";
            const string fresh = "namespace Demo; component App() => Child {}; // 😀";
            await File.WriteAllTextAsync(app, malformed);
            var appUri = new Uri(app).AbsoluteUri;
            var childUri = new Uri(child).AbsoluteUri;
            using var input = Input(
                Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = childUri, languageId = "lucent", version = 1, text = "namespace Demo; component RenamedChild() => TextBlock {};" } }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = appUri, languageId = "lucent", version = 1, text = fresh } }),
                Request(2, "textDocument/completion", new { textDocument = new { uri = appUri }, position = Position(fresh, "Child", 3) }),
                Notification("textDocument/didChange", new { textDocument = new { uri = appUri, version = 2 }, contentChanges = new[] { new { text = malformed } } }),
                Notification("textDocument/didChange", new { textDocument = new { uri = appUri, version = 3 }, contentChanges = new[] { new { text = fresh } } }),
                Request(3, "textDocument/completion", new { textDocument = new { uri = appUri }, position = Position(fresh, "Child", 3) }),
                Request(4, "shutdown", null), Notification("exit", null));
            using var output = new MemoryStream();
            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = Read(output.ToArray());
            var first = Labels(Response(messages, 2));
            var second = Labels(Response(messages, 3));
            Assert.AreNotEqual(JsonValueKind.Undefined, Response(messages, 2).GetProperty("result").ValueKind, "Open-buffer completion must return a response.");
            Assert.AreNotEqual(JsonValueKind.Undefined, Response(messages, 3).GetProperty("result").ValueKind, "Newest-generation completion must return a response.");
            Assert.IsFalse(second.Any(label => label.Contains("😀", StringComparison.Ordinal)), "Astral UTF-16 text must not corrupt completion labels.");
            Assert.IsTrue(PublishedDiagnostics(messages, appUri).SelectMany(value => value.EnumerateArray()).Any(), "Malformed source must publish an explicit diagnostic.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task Project_xml_documentation_is_rendered_without_generated_qualification()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-xml-docs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var project = Path.Combine(directory, "Docs.csproj");
            var sourcePath = Path.Combine(directory, "App.lui");
            var docsPath = Path.Combine(directory, "Docs.cs");
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><LucentSource Include=\"App.lui\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(docsPath, """
                namespace Demo;
                public sealed class Docs
                {
                    /// <summary>Summary for <paramref name="value"/> and <typeparamref name="T"/> with <see cref="T:System.String"/>.</summary>
                    /// <remarks><para>Remark with <c>code</c> and <seealso cref="T:System.Uri"/>.</para><list type="bullet"><item><description>List item.</description></item></list></remarks>
                    /// <typeparam name="T">Type parameter.</typeparam>
                    /// <param name="value">Input parameter.</param>
                    /// <returns>Return value.</returns>
                    /// <exception cref="T:System.InvalidOperationException">Failure.</exception>
                    public T Describe<T>(string value) => default!;
                }
                """);
            const string source = "namespace Demo; component App() { private readonly Docs docs = new(); Fragment Render() => Border { Loaded: (sender, e) => { docs.Describe<int>(\"x\"); }; }; }";
            await File.WriteAllTextAsync(sourcePath, source);
            var uri = new Uri(sourcePath
).AbsoluteUri;
            using var input = Input(Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }), Notification("initialized", new { }), Notification("textDocument/didOpen", new { textDocument = new { uri, languageId = "lucent", version = 1, text = source } }), Request(2, "textDocument/hover", new { textDocument = new { uri }, position = Position(source, "Describe", 0) }), Request(3, "shutdown", null), Notification("exit", null));
            using var output = new MemoryStream();
            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var text = Hover(Read(output.ToArray()), 2);
            foreach (var expected in new[] { "Summary for value and T with System.String", "Remarks:", "`code`", "List item.", "T:", "Type parameter.", "value:", "Input parameter.", "Returns:", "Return value.", "Throws System.InvalidOperationException:", "Failure.", "System.Uri" }) StringAssert.Contains(text, expected);
            Assert.IsFalse(text.Contains("global::", StringComparison.Ordinal));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task Project_member_completion_exposes_native_csharp_quality_indicators()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-quality-members", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var project = Path.Combine(directory, "Quality.csproj");
            var sourcePath = Path.Combine(directory, "App.lui");
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup><ItemGroup><LucentSource Include=\"App.lui\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(directory, "QualityApi.cs"), """
                namespace Demo;
                public sealed class QualityApi
                {
                    /// <summary>Nullable project value.</summary>
                    public string? NullableValue => null;
                    /// <summary>Legacy project value.</summary>
                    [System.Obsolete("Use NullableValue")]
                    public string OldValue => "";
                    /// <summary>Generic project method.</summary>
                    public T Generic<T>(T value) => value;
                    public string Overload(string value) => value;
                    public int Overload(int value) => value;
                }
                """);
            const string source = "namespace Demo; component App() { private readonly QualityApi api = new(); Fragment Render() => Border { Loaded: (sender, e) => { api.NullableValue; api.; }; }; }";
            await File.WriteAllTextAsync(sourcePath, source);
            var uri = new Uri(sourcePath).AbsoluteUri;
            using var input = Input(
                Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri, languageId = "lucent", version = 1, text = source } }),
                Request(2, "textDocument/completion", new { textDocument = new { uri }, position = Position(source, "api.", 4) }),
                Request(3, "textDocument/hover", new { textDocument = new { uri }, position = Position(source, "api", 0) }),
                Request(4, "textDocument/definition", new { textDocument = new { uri }, position = Position(source, "NullableValue", 2) }),
                Request(5, "shutdown", null), Notification("exit", null));
            using var output = new MemoryStream();
            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = Read(output.ToArray());
            var items = Response(messages, 2).GetProperty("result").EnumerateArray().ToArray();
            foreach (var label in new[] { "NullableValue", "OldValue", "Generic", "Overload" })
            {
                var item = items.Single(candidate => candidate.GetProperty("label").GetString() == label);
                Assert.AreEqual(item.GetProperty("label").GetString(), item.GetProperty("sortText").GetString());
                Assert.AreEqual(label, item.GetProperty("filterText").GetString());
                Assert.IsFalse(item.GetProperty("detail").GetString()!.Contains("global::", StringComparison.Ordinal));
            }
            var nullable = items.Single(item => item.GetProperty("label").GetString() == "NullableValue");
            StringAssert.Contains(nullable.GetProperty("detail").GetString()!, "string?");
            var generic = items.Single(item => item.GetProperty("label").GetString() == "Generic");
            StringAssert.Contains(generic.GetProperty("detail").GetString()!, "Generic<T>");
            var obsolete = items.Single(item => item.GetProperty("label").GetString() == "OldValue");
            CollectionAssert.Contains(obsolete.GetProperty("tags").EnumerateArray().Select(tag => tag.GetInt32()).ToArray(), 1);
            StringAssert.Contains(obsolete.GetProperty("documentation").GetProperty("value").GetString()!, "Legacy project value");
            StringAssert.Contains(Hover(messages, 3), "QualityApi");
            Assert.AreEqual(new Uri(Path.Combine(directory, "QualityApi.cs")).AbsoluteUri,
                Response(messages, 4).GetProperty("result").GetProperty("uri").GetString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Checked_in_benchmark_final_meets_the_checked_in_workload_budgets()
    {
        var repository = FindRepository();
        using var workload = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository,
            "tools", "Lucent.LanguageServer.Benchmarks", "workload.json")));
        using var final = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository,
            "docs", "quality", "008-tooling", "final.json")));
        var budgets = workload.RootElement.GetProperty("budgets");
        var warm = final.RootElement.GetProperty("warmCompletion");
        var edit = final.RootElement.GetProperty("editCompletion");
        Assert.IsTrue(warm.GetProperty("p95Milliseconds").GetDouble() <= budgets.GetProperty("warmP95Milliseconds").GetDouble());
        Assert.IsTrue(warm.GetProperty("p99Milliseconds").GetDouble() <= budgets.GetProperty("warmP99Milliseconds").GetDouble());
        Assert.IsTrue(warm.GetProperty("p95AllocatedBytes").GetDouble() <= budgets.GetProperty("warmP95AllocatedBytes").GetDouble());
        Assert.IsTrue(edit.GetProperty("p95Milliseconds").GetDouble() <= budgets.GetProperty("editP95Milliseconds").GetDouble());
        Assert.IsTrue(edit.GetProperty("p95AllocatedBytes").GetDouble() <= budgets.GetProperty("editP95AllocatedBytes").GetDouble());
        Assert.IsTrue(final.RootElement.GetProperty("maxActiveProjectGenerations").GetInt32() <= budgets.GetProperty("maxActiveProjectGenerations").GetInt32());
    }

    private static MemoryStream Input(params object[] messages)
    {
        var stream = new MemoryStream();
        foreach (var message in messages)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
            stream.Write(Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n"));
            stream.Write(bytes);
        }
        stream.Position = 0;
        return stream;
    }

    private static object Request(int id, string method, object? parameters) => new { jsonrpc = "2.0", id, method, @params = parameters };
    private static object Notification(string method, object? parameters) => new { jsonrpc = "2.0", method, @params = parameters };
    private static object Position(string text, string needle, int characters)
    {
        var offset = text.LastIndexOf(needle, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, offset);
        offset += characters;
        var prefix = text[..offset];
        return new { line = prefix.Count(character => character == '\n'), character = offset - (prefix.LastIndexOf('\n') + 1) };
    }

    private static IReadOnlyList<JsonDocument> Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var output = new List<JsonDocument>();
        while (stream.Position < stream.Length)
        {
            var header = new List<byte>();
            while (true) { var value = stream.ReadByte(); Assert.AreNotEqual(-1, value); header.Add((byte)value); if (header.Count >= 4 && header[^4..].SequenceEqual("\r\n\r\n"u8.ToArray())) break; }
            var length = int.Parse(Encoding.ASCII.GetString(header.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Split(':', 2)[1]);
            var body = new byte[length]; Assert.AreEqual(length, stream.Read(body)); output.Add(JsonDocument.Parse(body));
        }
        return output;
    }

    private static JsonElement Response(IReadOnlyList<JsonDocument> messages, int id) => messages.Single(message => message.RootElement.TryGetProperty("id", out var response) && response.ValueKind == JsonValueKind.Number && response.GetInt32() == id).RootElement;
    private static string Hover(IReadOnlyList<JsonDocument> messages, int id) => Response(messages, id).GetProperty("result").GetProperty("contents").GetProperty("value").GetString()!;
    private static string[] Labels(JsonElement response) => response.GetProperty("result").EnumerateArray().Select(item => item.GetProperty("label").GetString()!).ToArray();
    private static IEnumerable<JsonElement> PublishedDiagnostics(IReadOnlyList<JsonDocument> messages, string uri) => messages.Where(message => message.RootElement.TryGetProperty("method", out var method) && method.GetString() == "textDocument/publishDiagnostics" && message.RootElement.GetProperty("params").GetProperty("uri").GetString() == uri).Select(message => message.RootElement.GetProperty("params").GetProperty("diagnostics"));

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Lucent.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the Lucent repository.");
    }

    private sealed record MatrixCell(string Context, string Source, string CompletionNeedle, int CompletionCharacters, string HoverNeedle, string ExpectedLabel, int ExpectedKind, string HoverText);
    private static readonly MatrixCell[] QualityCells =
    [
        new("native controls/properties/events/attached", "namespace Demo; using System; component App() => ListBox { template ItemTemplate(Uri item) { TextBlock { Text: binding(item.Host); } } };", "item.Host", 6, "binding", "Host", 10, "inherited DataContext"),
        new("project types/static members/methods/locals", "namespace Demo; using System; component App() { private readonly Uri endpoint = new(\"https://example.com\"); Fragment Render() => Border { Loaded: (sender, e) => { endpoint.H; }; }; }", "endpoint.H", 10, "endpoint", "Host", 10, "Uri"),
        new("components/parameters/state/computed/ordinary members", "namespace Demo; component Child(string title) => TextBlock { Text: title; }; component App() => Chil {};", "Chil", 4, "Child", "Child", 1, "Child"),
        new("event and method islands", "namespace Demo; component App() { private void Focus() {} Fragment Render() => Border { Loaded: (sender, e) => { Foc; }; }; }", "Foc", 3, "Focus", "Focus", 2, "Focus"),
        new("if/loading/catch/loop/template/binding/slots", "namespace Demo; using System; component App() => ListBox { template ItemTemplate(Uri item) { TextBlock { Text: binding(item.Host); } } };", "item.Host", 6, "binding", "Host", 10, "inherited DataContext"),
    ];
}
