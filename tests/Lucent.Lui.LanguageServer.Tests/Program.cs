using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Lucent.Lui.Compiler;
using Lucent.Lui.Generator;
using Lucent.Lui.LanguageServer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;

var root = Path.Combine(Path.GetTempPath(), "lucent-lsp-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var projectPath = Path.Combine(root, "Sample.csproj");
    var sourcePath = Path.Combine(root, "Widget.lui");
    var siblingPath = Path.Combine(root, "Card.lui");
    var core = Path.GetFullPath("src/Lucent.Core/Lucent.Core.csproj");
    await File.WriteAllTextAsync(
        projectPath,
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Sample</RootNamespace><LangVersion>preview</LangVersion><DefineConstants>LSP_PARITY</DefineConstants><LucentLuiProjectEpoch>parity-epoch</LucentLuiProjectEpoch><LucentLuiProjectIdentity>parity-project</LucentLuiProjectIdentity><LucentLuiLangVersion>preview</LucentLuiLangVersion><LucentLuiCompilerOptions>parity-options</LucentLuiCompilerOptions><LucentLuiDefines>LSP_PARITY</LucentLuiDefines></PropertyGroup><ItemGroup><ProjectReference Include=\""
            + core
            + "\" /><Using Include=\"System.Collections.Generic\" /><AdditionalFiles Include=\"Widget.lui\" LucentLuiLogicalPath=\"nested/screens/Widget.lui\" /><AdditionalFiles Include=\"Card.lui\" LucentLuiLogicalPath=\"nested/components/Card.lui\" /><CompilerVisibleItemMetadata Include=\"AdditionalFiles\" MetadataName=\"LucentLuiLogicalPath\" /><CompilerVisibleItemMetadata Include=\"AdditionalFiles\" MetadataName=\"LucentLuiDocumentVersion\" /><CompilerVisibleProperty Include=\"LucentLuiProjectEpoch\" /><CompilerVisibleProperty Include=\"LucentLuiProjectIdentity\" /><CompilerVisibleProperty Include=\"LucentLuiLangVersion\" /><CompilerVisibleProperty Include=\"LucentLuiCompilerOptions\" /><CompilerVisibleProperty Include=\"LucentLuiDefines\" /><CompilerVisibleProperty Include=\"RootNamespace\" /></ItemGroup></Project>"
    );
    var source =
        "namespace Sample;\r\nusing Lucent.Core;\r\nusing static Sample.Components;\r\ninternal component Widget() { <Card content={default} /> }";
    var sibling =
        "using CoreAlias = Lucent.Core;\r\nnamespace Sample;\r\nusing static Lucent.Core.Components;\r\ninternal component Card(CoreAlias.ComponentContent content) { <Row /> }";
    await File.WriteAllTextAsync(sourcePath, source);
    await File.WriteAllTextAsync(siblingPath, sibling);
    var sourceUri = new Uri(sourcePath);

    using var context = await LuiProjectContext.LoadAsync(projectPath, CancellationToken.None);
    var published = await context.CompileAsync(sourceUri, CancellationToken.None);
    Assert(
        published is not null,
        "evaluated project did not compile its AdditionalFiles .lui document."
    );
    var compiled = published ?? throw new InvalidOperationException("Missing compiled document.");
    Assert(
        compiled.Result.Identity.RootNamespace == "Sample",
        "evaluated RootNamespace was not retained."
    );
    Assert(
        compiled.Result.Identity.Document.LogicalPath == "nested/screens/Widget.lui"
            && compiled.Result.Identity.DocumentVersion == LuiDocumentIdentity.Hash(source)
            && compiled.Result.Identity.ProjectEpoch == "parity-epoch"
            && compiled.Result.Identity.Options == "parity-options"
            && compiled.Result.Identity.Defines == "LSP_PARITY",
        "evaluated logical path, document version, options, or defines diverged."
    );
    using var buildWorkspace = MSBuildWorkspace.Create();
    var buildProject = await buildWorkspace.OpenProjectAsync(projectPath);
    var buildCompilation =
        await buildProject.GetCompilationAsync()
        ?? throw new InvalidOperationException("Missing evaluated build compilation.");
    var buildFiles = buildProject
        .AnalyzerOptions.AdditionalFiles.Where(file =>
            file.Path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase)
        )
        .ToArray();
    GeneratorDriver buildDriver = CSharpGeneratorDriver.Create(
        System.Collections.Immutable.ImmutableArray.Create(new LuiGenerator().AsSourceGenerator()),
        buildFiles,
        (CSharpParseOptions)buildProject.ParseOptions!,
        buildProject.AnalyzerOptions.AnalyzerConfigOptionsProvider
    );
    buildDriver = buildDriver.RunGenerators(buildCompilation);
    var buildSource = buildDriver
        .GetRunResult()
        .Results.Single()
        .GeneratedSources.Single(item =>
            item.SourceText.ToString().Contains(" Widget(", StringComparison.Ordinal)
        )
        .SourceText.ToString();
    Assert(buildSource == compiled.GeneratedText, "build and LSP generation were not exact.");
    var declarationIdentities = compiled.Index.Declarations.Select(item => item.Identity).ToArray();
    Assert(
        compiled.Index.Generation == compiled.Result.Identity.SiblingIndexGeneration
            && declarationIdentities.Length == 2
            && declarationIdentities[0] == "global::Sample.Components.Widget"
            && declarationIdentities[1] == "global::Sample.Components.Card",
        "build/LSP index generation or canonical declaration identity diverged."
    );
    var card = source.IndexOf("Card", StringComparison.Ordinal);
    var rowPosition = Position(source, card);
    var declarationTarget = await context.NavigateAsync(sourceUri, card, CancellationToken.None);
    var cardDeclarationSpan = LuiParser.Parse(sibling).Component!.Name.Span;
    Assert(
        declarationTarget is not null
            && declarationTarget.Uri == new Uri(siblingPath)
            && declarationTarget.Span.Start == cardDeclarationSpan.Start
            && declarationTarget.Span.Length == cardDeclarationSpan.Length,
        "component reference did not navigate directly to its .lui declaration."
    );
    var generatedEntry = compiled
        .Result.Map.FromSource(new LuiSpan(card, 0))
        .Where(item => !item.Hidden)
        .OrderBy(item => item.Kind == LuiMapKind.Symbol ? 0 : 1)
        .ThenBy(item => item.Generated.Length)
        .First();
    var sourceRoundTrip = await context.NavigateAsync(
        compiled.GeneratedUri,
        generatedEntry.Generated.Start,
        CancellationToken.None
    );
    Assert(
        sourceRoundTrip is not null
            && sourceRoundTrip.Uri == sourceUri
            && sourceRoundTrip.Span.Start == card
            && sourceRoundTrip.Span.Length == "Card".Length,
        "generated navigation did not return to the exact .lui source span."
    );
    using (var foreign = await LuiProjectContext.LoadAsync(projectPath, CancellationToken.None))
        Assert(
            await foreign.NavigateAsync(
                compiled.GeneratedUri,
                generatedEntry.Generated.Start,
                CancellationToken.None
            )
                is null,
            "a generated URI from a foreign context was accepted."
        );
    var disposalContext = await LuiProjectContext.LoadAsync(projectPath, CancellationToken.None);
    var reachedDispose = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    var releaseDispose = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    var disposingNavigation = disposalContext.NavigateAsync(
        sourceUri,
        card,
        async () =>
        {
            reachedDispose.SetResult();
            await releaseDispose.Task;
        },
        CancellationToken.None
    );
    await reachedDispose.Task;
    disposalContext.Dispose();
    releaseDispose.SetResult();
    Assert(await disposingNavigation is null, "in-flight disposed navigation was published.");
    var reachedReplace = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    var releaseReplace = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    var replacingNavigation = context.NavigateAsync(
        sourceUri,
        card,
        async () =>
        {
            reachedReplace.SetResult();
            await releaseReplace.Task;
        },
        CancellationToken.None
    );
    await reachedReplace.Task;
    context.ReplaceText(
        sourceUri,
        source.Replace("{default}", "{default(ComponentContent)}", StringComparison.Ordinal)
    );
    releaseReplace.SetResult();
    Assert(await replacingNavigation is null, "in-flight replaced navigation was published.");
    Assert(
        !await context.IsCurrentAsync(compiled.Result, CancellationToken.None),
        "changed document accepted stale project output."
    );
    Assert(
        context.GetGeneratedText(compiled.GeneratedUri) is null,
        "changed document retained stale generated text."
    );
    Assert(
        await context.CompileAsync(sourceUri, CancellationToken.None) is not null,
        "changed evaluated document did not recompile."
    );
    using (var lsp = LspClient.Start())
    {
        var projectUri = VsCodeUri(new Uri(projectPath));
        var lspSourceUri = VsCodeUri(sourceUri);
        using var initialized = await lsp.RequestAsync(
            "initialize",
            new { initializationOptions = new { projectUri } }
        );
        Assert(
            initialized
                .RootElement.GetProperty("result")
                .GetProperty("capabilities")
                .GetProperty("definitionProvider")
                .GetBoolean(),
            "LSP process did not advertise definition navigation."
        );
        await lsp.NotifyAsync("initialized", new { });
        using var repeatedInitialize = await lsp.RequestAsync(
            "initialize",
            new { initializationOptions = new { projectUri = new Uri(projectPath).AbsoluteUri } }
        );
        Assert(
            repeatedInitialize.RootElement.TryGetProperty("error", out _),
            "repeated initialize was accepted."
        );
        using var generatedDefinition = await lsp.RequestAsync(
            "textDocument/definition",
            new
            {
                textDocument = new { uri = lspSourceUri },
                position = new { line = rowPosition.Line, character = rowPosition.Character },
            }
        );
        var declarationLocation = generatedDefinition.RootElement.GetProperty("result");
        Assert(
            declarationLocation.GetProperty("uri").GetString() == new Uri(siblingPath).AbsoluteUri,
            "LSP component reference did not navigate to its .lui declaration."
        );
        var generatedUri = compiled.GeneratedUri.AbsoluteUri;
        using var generatedSource = await lsp.RequestAsync(
            "lucent/generatedText",
            new { uri = generatedUri }
        );
        Assert(
            generatedSource.RootElement.GetProperty("result").GetString() is not null,
            "LSP did not serve generated text."
        );
        using var sourceDefinition = await lsp.RequestAsync(
            "textDocument/definition",
            new
            {
                textDocument = new { uri = generatedUri },
                position = new
                {
                    line = Position(compiled.GeneratedText, generatedEntry.Generated.Start).Line,
                    character = Position(
                        compiled.GeneratedText,
                        generatedEntry.Generated.Start
                    ).Character,
                },
            }
        );
        Assert(
            sourceDefinition.RootElement.GetProperty("result").GetProperty("uri").GetString()
                == lspSourceUri,
            "LSP generated navigation did not return to .lui source."
        );
        await lsp.NotifyAsync(
            "textDocument/didOpen",
            new
            {
                textDocument = new
                {
                    uri = lspSourceUri,
                    text = source.Replace("Widget", "Opened", StringComparison.Ordinal),
                },
            }
        );
        await lsp.NotifyAsync(
            "textDocument/didChange",
            new
            {
                textDocument = new { uri = lspSourceUri },
                contentChanges = new[]
                {
                    new { text = source.Replace("Card", "Missing", StringComparison.Ordinal) },
                },
            }
        );
        using var changedDefinition = await lsp.RequestAsync(
            "textDocument/definition",
            new
            {
                textDocument = new { uri = lspSourceUri },
                position = new { line = rowPosition.Line, character = rowPosition.Character },
            }
        );
        Assert(
            changedDefinition.RootElement.GetProperty("result").ValueKind == JsonValueKind.Null,
            "an accepted didChange did not replace the open in-memory document."
        );
        Assert(
            await File.ReadAllTextAsync(sourcePath) == source,
            "an accepted didChange wrote the editor overlay to disk."
        );
        await lsp.NotifyAsync(
            "textDocument/didClose",
            new { textDocument = new { uri = lspSourceUri } }
        );
        await lsp.NotifyAsync(
            "textDocument/didChange",
            new
            {
                textDocument = new { uri = lspSourceUri },
                contentChanges = new[] { new { text = "late closed-buffer change" } },
            }
        );
        using var afterClose = await lsp.RequestAsync(
            "textDocument/definition",
            new
            {
                textDocument = new { uri = lspSourceUri },
                position = new { line = rowPosition.Line, character = rowPosition.Character },
            }
        );
        Assert(
            afterClose.RootElement.GetProperty("result").ValueKind == JsonValueKind.Object,
            "late didChange after didClose replaced the restored evaluated document: "
                + afterClose.RootElement.GetRawText()
        );
        Assert(
            afterClose.RootElement.GetProperty("result").GetProperty("uri").GetString()
                == new Uri(siblingPath).AbsoluteUri,
            "restored component reference did not navigate to its declaration."
        );
        using var restoredGenerated = await lsp.RequestAsync(
            "lucent/generatedText",
            new { uri = generatedUri }
        );
        var restoredText = restoredGenerated.RootElement.GetProperty("result").GetString()!;
        Assert(
            restoredText.Contains(" Widget(", StringComparison.Ordinal)
                && !restoredText.Contains(" Opened(", StringComparison.Ordinal),
            "didClose did not restore the evaluated disk document:\n" + restoredText
        );
        await lsp.RequestAsync("shutdown", new { });
        using var afterShutdown = await lsp.RequestAsync(
            "lucent/generatedText",
            new { uri = generatedUri }
        );
        Assert(
            afterShutdown.RootElement.TryGetProperty("error", out _),
            "request after shutdown was accepted."
        );
        Assert(await lsp.ExitAsync() == 0, "normal shutdown/exit returned a failure code.");
    }
    context.Dispose();
    await AssertDisposedAsync(() => context.CompileAsync(sourceUri, CancellationToken.None));
}
finally
{
    Directory.Delete(root, recursive: true);
}

using (
    var issueBrowser = await LuiProjectContext.LoadAsync(
        Path.GetFullPath("apps/Lucent.IssueBrowser/Lucent.IssueBrowser.csproj"),
        CancellationToken.None
    )
)
{
    var header = new Uri(Path.GetFullPath("apps/Lucent.IssueBrowser/Header.lui"));
    var headerText = await File.ReadAllTextAsync(header.LocalPath);
    var filterBar = headerText.IndexOf("FilterBar", StringComparison.Ordinal);
    Assert(
        await issueBrowser.NavigateAsync(header, filterBar, CancellationToken.None) is not null,
        "evaluated Issue Browser Header.lui could not bind its FilterBar sibling."
    );
}

using (var premature = LspClient.Start())
{
    using var rejected = await premature.RequestAsync("shutdown", new { });
    Assert(
        rejected.RootElement.TryGetProperty("error", out _),
        "shutdown before initialize passed."
    );
    Assert(await premature.ExitAsync() == 1, "exit before successful shutdown returned success.");
}

using (var messageKinds = LspClient.Start())
{
    var initialize = new
    {
        initializationOptions = new
        {
            projectUri = new Uri(
                Path.GetFullPath("apps/Lucent.IssueBrowser/Lucent.IssueBrowser.csproj")
            ).AbsoluteUri,
        },
    };
    await messageKinds.NotifyAsync("initialize", initialize);
    using var acceptedInitialize = await messageKinds.RequestAsync("initialize", initialize);
    Assert(
        acceptedInitialize.RootElement.TryGetProperty("result", out _),
        "initialize notification advanced lifecycle state."
    );
    using var rejectedInitialized = await messageKinds.RequestAsync("initialized", new { });
    Assert(
        rejectedInitialized.RootElement.TryGetProperty("error", out _),
        "initialized request was accepted."
    );
    await messageKinds.NotifyAsync("initialized", new { });
    using var rejectedExit = await messageKinds.RequestAsync("exit", new { });
    Assert(rejectedExit.RootElement.TryGetProperty("error", out _), "exit request was accepted.");
    await messageKinds.RequestAsync("shutdown", new { });
    Assert(await messageKinds.ExitAsync() == 0, "strict message-kind client did not exit cleanly.");
}

return;

static async Task AssertDisposedAsync(Func<Task<LuiProjectContext.PublishedDocument?>> action)
{
    try
    {
        await action();
        throw new InvalidOperationException("disposed project accepted work.");
    }
    catch (ObjectDisposedException) { }
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static (int Line, int Character) Position(string text, int offset)
{
    var line = 0;
    var start = 0;
    while (true)
    {
        var end = text.IndexOf('\n', start);
        if (end < 0 || end >= offset)
            return (line, offset - start);
        line++;
        start = end + 1;
    }
}

static string VsCodeUri(Uri uri)
{
    var value = uri.AbsoluteUri;
    if (!OperatingSystem.IsWindows())
        return value;
    var drive = Path.GetPathRoot(uri.LocalPath)![0];
    return value.Replace($"/{drive}:", $"/{drive}%3A", StringComparison.OrdinalIgnoreCase);
}

sealed class LspClient : IDisposable
{
    private readonly Process process;
    private readonly Stream input;
    private readonly Stream output;
    private int id;

    private LspClient(Process process)
    {
        this.process = process;
        input = process.StandardOutput.BaseStream;
        output = process.StandardInput.BaseStream;
    }

    internal static LspClient Start()
    {
        var process =
            Process.Start(
                new ProcessStartInfo(
                    Path.Combine(AppContext.BaseDirectory, "Lucent.Lui.LanguageServer.exe")
                )
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
        await output.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"));
        await output.WriteAsync(body);
        await output.FlushAsync();
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
        await output.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"));
        await output.WriteAsync(body);
        await output.FlushAsync();
    }

    internal async Task<int> ExitAsync()
    {
        await NotifyAsync("exit", new { });
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("LSP did not exit after shutdown/exit.");
        }
        return process.ExitCode;
    }

    public void Dispose()
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
        process.Dispose();
    }

    private async Task<JsonDocument> ReadAsync()
    {
        var header = new StringBuilder();
        while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            var byteRead = new byte[1];
            if (await input.ReadAsync(byteRead) == 0)
                throw new EndOfStreamException("LSP process ended before a response.");
            header.Append((char)byteRead[0]);
        }
        var length = Int32.Parse(header.ToString().Split(':', 2)[1], CultureInfo.InvariantCulture);
        var body = new byte[length];
        for (var read = 0; read < body.Length; )
        {
            var count = await input.ReadAsync(body.AsMemory(read));
            if (count == 0)
                throw new EndOfStreamException("LSP response ended before its content.");
            read += count;
        }
        return JsonDocument.Parse(body);
    }
}
