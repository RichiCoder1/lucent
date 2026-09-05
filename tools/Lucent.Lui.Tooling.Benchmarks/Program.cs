using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

if (args is not [var measure])
{
    Console.Error.WriteLine(
        "Usage: Lucent.Lui.Tooling.Benchmarks <warmCompletion|editToDiagnostic|rename>"
    );
    return 2;
}

var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
Directory.SetCurrentDirectory(repositoryRoot);
var fixtureRoot = Path.Combine(
    Path.GetTempPath(),
    "lucent-lsp-measure-" + Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(fixtureRoot);
try
{
    var core = Path.Combine(repositoryRoot, "src/Lucent.Core/Lucent.Core.csproj");
    var project = new Uri(Path.Combine(fixtureRoot, "Measure.csproj"));
    var document = new Uri(Path.Combine(fixtureRoot, "Widget.lui"));
    var source =
        "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; internal component Widget(int count) { <Row><Text content={Helpers.Format(count)} /></Row> }";
    await File.WriteAllTextAsync(
        project.LocalPath,
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Sample</RootNamespace><LangVersion>preview</LangVersion></PropertyGroup><ItemGroup><ProjectReference Include=\""
            + core
            + "\" /><AdditionalFiles Include=\"Widget.lui\" /></ItemGroup></Project>"
    );
    await File.WriteAllTextAsync(
        Path.Combine(fixtureRoot, "Helpers.cs"),
        "namespace Sample; public static class Helpers { public static string Format(int value) => value.ToString(); }"
    );
    await File.WriteAllTextAsync(document.LocalPath, source);

    using var lsp = LspClient.Start();
    using var initialized = await lsp.RequestAsync(
        "initialize",
        new { initializationOptions = new { projectUri = VsCodeUri(project) } }
    );
    await lsp.NotifyAsync("initialized", new { });
    var completionOffset =
        source.IndexOf("Helpers.Format", StringComparison.Ordinal) + "Helpers.".Length;
    var completionLocation = Position(source, completionOffset);
    var completionPosition = new
    {
        line = completionLocation.Line,
        character = completionLocation.Character,
    };
    var renameLocation = Position(source, source.LastIndexOf("count", StringComparison.Ordinal));
    var renamePosition = new { line = renameLocation.Line, character = renameLocation.Character };
    var stopwatch = new Stopwatch();

    switch (measure)
    {
        case "warmCompletion":
            using (
                var warm = await lsp.RequestAsync(
                    "textDocument/completion",
                    new
                    {
                        textDocument = new { uri = VsCodeUri(document) },
                        position = completionPosition,
                    }
                )
            ) { }
            stopwatch.Start();
            using (
                var completion = await lsp.RequestAsync(
                    "textDocument/completion",
                    new
                    {
                        textDocument = new { uri = VsCodeUri(document) },
                        position = completionPosition,
                    }
                )
            )
                Check(
                    completion.RootElement.TryGetProperty("result", out var completionResult)
                        && completionResult.GetProperty("items").GetArrayLength() != 0,
                    "completion measurement returned no completion result: "
                        + completion.RootElement.GetRawText()
                );
            break;
        case "editToDiagnostic":
            await lsp.NotifyAsync(
                "textDocument/didOpen",
                new
                {
                    textDocument = new
                    {
                        uri = VsCodeUri(document),
                        version = 1,
                        text = source,
                    },
                }
            );
            using (
                var warm = await lsp.RequestAsync(
                    "textDocument/diagnostic",
                    new { textDocument = new { uri = VsCodeUri(document) } }
                )
            ) { }
            stopwatch.Start();
            await lsp.NotifyAsync(
                "textDocument/didChange",
                new
                {
                    textDocument = new { uri = VsCodeUri(document), version = 2 },
                    contentChanges = new[]
                    {
                        new { text = source.Replace("Row", "Missing", StringComparison.Ordinal) },
                    },
                }
            );
            using (
                var diagnostic = await lsp.RequestAsync(
                    "textDocument/diagnostic",
                    new { textDocument = new { uri = VsCodeUri(document) } }
                )
            )
                Check(
                    diagnostic
                        .RootElement.GetProperty("result")
                        .GetProperty("items")
                        .EnumerateArray()
                        .Any(item => item.GetProperty("code").GetString() == "LUI2001"),
                    "edit-to-diagnostic measurement returned no current diagnostic."
                );
            break;
        case "rename":
            await lsp.NotifyAsync(
                "textDocument/didOpen",
                new
                {
                    textDocument = new
                    {
                        uri = VsCodeUri(document),
                        version = 1,
                        text = source,
                    },
                }
            );
            using (
                var warm = await lsp.RequestAsync(
                    "textDocument/prepareRename",
                    new
                    {
                        textDocument = new { uri = VsCodeUri(document) },
                        position = renamePosition,
                    }
                )
            ) { }
            stopwatch.Start();
            using (
                var rename = await lsp.RequestAsync(
                    "textDocument/rename",
                    new
                    {
                        textDocument = new { uri = VsCodeUri(document) },
                        position = renamePosition,
                        newName = "FilterPanel",
                    }
                )
            )
                Check(
                    rename
                        .RootElement.GetProperty("result")
                        .GetProperty("changes")
                        .EnumerateObject()
                        .Any(),
                    "rename measurement returned no workspace edit."
                );
            break;
        default:
            Console.Error.WriteLine("Unknown measurement: " + measure);
            return 2;
    }

    stopwatch.Stop();
    await lsp.RequestAsync("shutdown", new { });
    if (await lsp.ExitAsync() != 0)
        return 1;
    Console.WriteLine("MeasureMilliseconds=" + stopwatch.ElapsedMilliseconds);
    return 0;
}
finally
{
    Directory.Delete(fixtureRoot, recursive: true);
}

static string FindRepositoryRoot(string start)
{
    for (
        var directory = new DirectoryInfo(start);
        directory is not null;
        directory = directory.Parent
    )
    {
        if (
            File.Exists(Path.Combine(directory.FullName, "global.json"))
            && File.Exists(Path.Combine(directory.FullName, "Lucent.slnx"))
        )
            return directory.FullName;
    }
    throw new InvalidOperationException("Could not locate the Lucent repository root.");
}

static (int Line, int Character) Position(string text, int offset)
{
    var line = 0;
    var character = 0;
    for (var index = 0; index < offset; index++)
    {
        if (text[index] == '\n')
        {
            line++;
            character = 0;
        }
        else if (text[index] != '\r')
            character++;
    }
    return (line, character);
}

static string VsCodeUri(Uri uri) =>
    uri.AbsoluteUri.Replace("file:///", "file://", StringComparison.Ordinal);

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class LspClient : IDisposable
{
    private readonly Process process;
    private readonly Stream input;
    private readonly Stream output;
    private readonly Queue<JsonDocument> notifications = [];
    private int id;

    private LspClient(Process process)
    {
        this.process = process;
        input = process.StandardOutput.BaseStream;
        output = process.StandardInput.BaseStream;
    }

    internal static LspClient Start()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Lucent.Lui.LanguageServer.exe");
        var process =
            Process.Start(
                new ProcessStartInfo(executable)
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                }
            ) ?? throw new InvalidOperationException("Could not start the LSP process.");
        return new LspClient(process);
    }

    internal async Task<JsonDocument> RequestAsync(string method, object parameters)
    {
        var request = JsonSerializer.Serialize(
            new
            {
                jsonrpc = "2.0",
                id = ++id,
                method,
                @params = parameters,
            }
        );
        var body = Encoding.UTF8.GetBytes(request);
        await WriteAsync(body);
        return await ReadAsync();
    }

    internal async Task NotifyAsync(string method, object parameters)
    {
        var body = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(
                new
                {
                    jsonrpc = "2.0",
                    method,
                    @params = parameters,
                }
            )
        );
        await WriteAsync(body);
    }

    internal async Task<int> ExitAsync()
    {
        await NotifyAsync("exit", new { });
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        return process.ExitCode;
    }

    public void Dispose()
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
        process.Dispose();
    }

    private async Task WriteAsync(byte[] body)
    {
        await output.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"));
        await output.WriteAsync(body);
        await output.FlushAsync();
    }

    private async Task<JsonDocument> ReadAsync()
    {
        while (true)
        {
            var header = new StringBuilder();
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                var byteRead = new byte[1];
                if (await input.ReadAsync(byteRead) == 0)
                    throw new EndOfStreamException("LSP process ended before a response.");
                header.Append((char)byteRead[0]);
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
                    throw new EndOfStreamException("LSP response ended before its content.");
                read += count;
            }
            var message = JsonDocument.Parse(body);
            if (message.RootElement.TryGetProperty("id", out _))
                return message;
            notifications.Enqueue(message);
        }
    }
}
