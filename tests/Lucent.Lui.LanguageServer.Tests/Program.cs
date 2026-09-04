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

if (args is ["--measure", var measure])
    return await MeasureOperationAsync(measure);

var root = Path.Combine(Path.GetTempPath(), "lucent-lsp-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var projectPath = Path.Combine(root, "Sample.csproj");
    var sourcePath = Path.Combine(root, "Widget.lui");
    var siblingPath = Path.Combine(root, "Card.lui");
    var helperPath = Path.Combine(root, "Helpers.cs");
    var core = Path.GetFullPath("src/Lucent.Core/Lucent.Core.csproj");
    await File.WriteAllTextAsync(
        projectPath,
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Sample</RootNamespace><LangVersion>preview</LangVersion><DefineConstants>LSP_PARITY</DefineConstants><LucentLuiProjectEpoch>parity-epoch</LucentLuiProjectEpoch><LucentLuiProjectIdentity>parity-project</LucentLuiProjectIdentity><LucentLuiLangVersion>preview</LucentLuiLangVersion><LucentLuiCompilerOptions>parity-options</LucentLuiCompilerOptions><LucentLuiDefines>LSP_PARITY</LucentLuiDefines></PropertyGroup><ItemGroup><ProjectReference Include=\""
            + core
            + "\" /><Using Include=\"System.Collections.Generic\" /><AdditionalFiles Include=\"Widget.lui\" LucentLuiLogicalPath=\"nested/screens/Widget.lui\" /><AdditionalFiles Include=\"Card.lui\" LucentLuiLogicalPath=\"nested/components/Card.lui\" /><CompilerVisibleItemMetadata Include=\"AdditionalFiles\" MetadataName=\"LucentLuiLogicalPath\" /><CompilerVisibleItemMetadata Include=\"AdditionalFiles\" MetadataName=\"LucentLuiDocumentVersion\" /><CompilerVisibleProperty Include=\"LucentLuiProjectEpoch\" /><CompilerVisibleProperty Include=\"LucentLuiProjectIdentity\" /><CompilerVisibleProperty Include=\"LucentLuiLangVersion\" /><CompilerVisibleProperty Include=\"LucentLuiCompilerOptions\" /><CompilerVisibleProperty Include=\"LucentLuiDefines\" /><CompilerVisibleProperty Include=\"RootNamespace\" /></ItemGroup></Project>"
    );
    await File.WriteAllTextAsync(
        helperPath,
        "namespace Vendor.Deep { public class Marker {} } namespace Vendor.Deep.Child {} namespace Sample; using Lucent.Core; public static class Helpers {\n/// <summary>Formats <see cref=\"T:System.String\"/> for <paramref name=\"value\"/>.</summary>\n/// <remarks>Second section.</remarks>\npublic static string Format(int value) => value.ToString();\n}\npublic static class ImportedComponents { [LucentComponent] public static ComponentRecipe Choice(string first, int second = 42) => null!; [LucentComponent] public static ComponentRecipe Choice(int first) => null!; }"
    );
    var source =
        "namespace Sample;\r\nusing Lucent.Core;\r\nusing static Sample.Components;\r\nusing static Sample.ImportedComponents;\r\ninternal component Widget(int count) { <Card content={Helpers.Format(count)} name=\"widget\" /> }";
    var sibling =
        "namespace Sample;\r\nusing static Lucent.Core.Components;\r\ninternal component Card(string content, string name = \"\") { <Row /> }";
    await File.WriteAllTextAsync(sourcePath, source);
    await File.WriteAllTextAsync(siblingPath, sibling);
    var sourceUri = new Uri(sourcePath);

    var formatterSource =
        "// formatter comment\r\ninternal component Widget(int count) { <Row><Text content={count . ToString ( )} />  exact  text </Row> }";
    var formatterExpected = LuiFormatter.Format(formatterSource);
    var formatterPath = Path.Combine(root, "Formatter.lui");
    await File.WriteAllTextAsync(formatterPath, formatterSource);
    Assert(
        await ToolingExitCodeAsync("--check", formatterPath) == 1
            && await ToolingExitCodeAsync("--write", formatterPath) == 0
            && await File.ReadAllTextAsync(formatterPath) == formatterExpected
            && await ToolingExitCodeAsync("--check", formatterPath) == 0,
        "formatter CLI check/write diverged from the compiler formatter."
    );

    using var context = await LuiProjectContext.LoadAsync(projectPath, CancellationToken.None);
    context.ReplaceText(sourceUri, formatterSource);
    var lspDocumentFormat = await context.FormatAsync(sourceUri, null, CancellationToken.None);
    var lspRangeFormat = await context.FormatAsync(
        sourceUri,
        LuiParser.Parse(formatterSource).Component!.Span,
        CancellationToken.None
    );
    Assert(
        lspDocumentFormat is not null
            && Apply(formatterSource, lspDocumentFormat) == formatterExpected
            && lspRangeFormat is not null
            && Apply(formatterSource, lspRangeFormat)
                == LuiFormatter.FormatRange(
                    formatterSource,
                    LuiParser.Parse(formatterSource).Component!.Span
                ),
        "LSP document/range formatting diverged from compiler/CLI policy."
    );
    context.ReplaceText(sourceUri, source);
    var published = await context.CompileAsync(sourceUri, CancellationToken.None);
    Assert(
        published is not null,
        "evaluated project did not compile its AdditionalFiles .lui document: "
            + string.Join(
                " | ",
                (await context.DiagnosticsAsync(sourceUri, CancellationToken.None))!.Select(
                    diagnostic => diagnostic.Code + ":" + diagnostic.Message
                )
            )
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
    var helper = source.IndexOf("Format", StringComparison.Ordinal);
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
    var preparedRename = await context.PrepareRenameAsync(
        sourceUri,
        helper,
        CancellationToken.None
    );
    var helperDeclaration = (await File.ReadAllTextAsync(helperPath)).LastIndexOf(
        "Format",
        StringComparison.Ordinal
    );
    var crossLanguageRename = await context.RenameAsync(
        sourceUri,
        helper,
        "Render",
        CancellationToken.None
    );
    Assert(
        preparedRename is not null
            && preparedRename.Span.Start == helper
            && crossLanguageRename is not null
            && crossLanguageRename.Edits.Any(edit =>
                edit.Uri == sourceUri && edit.Spans.Any(span => span.Start == helper)
            )
            && crossLanguageRename.Edits.Any(edit =>
                edit.Uri == new Uri(helperPath)
                && edit.Spans.Any(span => span.Start == helperDeclaration)
            ),
        "cross-language C# expression rename was not a complete workspace edit: "
            + (
                crossLanguageRename is null
                    ? "null"
                    : string.Join(
                        " | ",
                        crossLanguageRename.Edits.Select(edit =>
                            edit.Uri + ":" + string.Join(",", edit.Spans.Select(span => span.Start))
                        )
                    )
            )
            + " prepared="
            + preparedRename?.Span.Start
            + " helper="
            + helper
            + " declaration="
            + helperDeclaration
    );
    var componentRename = await context.RenameAsync(
        sourceUri,
        card,
        "Panel",
        CancellationToken.None
    );
    Assert(
        componentRename is not null
            && componentRename.Edits.Any(edit =>
                edit.Uri == sourceUri && edit.Spans.Any(span => span.Start == card)
            )
            && componentRename.Edits.Any(edit => edit.Uri == new Uri(siblingPath)),
        "cross-language component rename did not include both .lui declaration and reference."
    );
    var renameReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var renameRelease = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    var staleRename = context.RenameAsync(
        sourceUri,
        helper,
        "Stale",
        CancellationToken.None,
        async () =>
        {
            renameReady.SetResult();
            await renameRelease.Task;
        }
    );
    await renameReady.Task;
    context.ReplaceText(
        sourceUri,
        source.Replace("Format(count)", "Format(count + 1)", StringComparison.Ordinal)
    );
    renameRelease.SetResult();
    Assert(await staleRename is null, "stale rename returned partial workspace edits.");
    context.ReplaceText(sourceUri, source);
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
        source.Replace(
            "Helpers.Format(count)",
            "Helpers.Format(count + 1)",
            StringComparison.Ordinal
        )
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
    var incomplete = source[..^1];
    context.ReplaceText(sourceUri, incomplete);
    var recoveredCompletions = await context.CompletionsAsync(
        sourceUri,
        incomplete.IndexOf("Card", StringComparison.Ordinal),
        CancellationToken.None
    );
    Assert(
        recoveredCompletions.Any(item => item.Label == "Card"),
        "recovered component markup lost shared semantic completion."
    );
    var choiceSource = source.Replace(
        "<Card content={Helpers.Format(count)} name=\"widget\" />",
        "<Choice first=\"one\" />",
        StringComparison.Ordinal
    );
    context.ReplaceText(sourceUri, choiceSource);
    var choice = choiceSource.IndexOf("Choice", StringComparison.Ordinal);
    var choiceTagCompletions = await context.CompletionsAsync(
        sourceUri,
        choice,
        CancellationToken.None
    );
    var choiceParameters = await context.CompletionsAsync(
        sourceUri,
        choiceSource.IndexOf("first", StringComparison.Ordinal),
        CancellationToken.None
    );
    Assert(
        choiceTagCompletions.Any(item => item.Label == "Choice" && item.Kind == 2)
            && choiceParameters.Any(item => item.Label == "first")
            && choiceParameters.Any(item => item.Label == "second"),
        "LookupSymbols component completion lost using-static C# component candidates or parameters."
    );
    var malformedImportedChoice = source.Replace(
        "<Card content={Helpers.Format(count)} name=\"widget\" />",
        "<Choice first={} />",
        StringComparison.Ordinal
    );
    context.ReplaceText(sourceUri, malformedImportedChoice);
    Assert(
        (
            await context.DefinitionAsync(
                sourceUri,
                malformedImportedChoice.IndexOf("Choice", StringComparison.Ordinal),
                CancellationToken.None
            )
        )
            ?.Uri
            .LocalPath == helperPath,
        "a map-bound imported component lost definition navigation beside malformed C#."
    );
    context.ReplaceText(sourceUri, source);
    var incompleteChoice = source.Replace(
        "<Card content={Helpers.Format(count)} name=\"widget\" />",
        "<Choice",
        StringComparison.Ordinal
    );
    context.ReplaceText(sourceUri, incompleteChoice);
    var choiceHelp = await context.SignatureHelpAsync(
        sourceUri,
        incompleteChoice.IndexOf("Choice", StringComparison.Ordinal),
        CancellationToken.None
    );
    Assert(
        choiceHelp is { Signatures.Count: 2 },
        "incomplete component tags did not enumerate accessible same-name overloads."
    );
    var malformedAttribute = source.Replace("name=\"widget\"", "name=", StringComparison.Ordinal);
    context.ReplaceText(sourceUri, malformedAttribute);
    Assert(
        await context.DefinitionAsync(
            sourceUri,
            malformedAttribute.IndexOf("Card", StringComparison.Ordinal),
            CancellationToken.None
        )
            is not null,
        "an unrelated malformed attribute suppressed a valid symbol definition."
    );
    var incompleteName = incompleteChoice.Replace("<Choice", "<Cho", StringComparison.Ordinal);
    context.ReplaceText(sourceUri, incompleteName);
    Assert(
        await context.HoverAsync(
            sourceUri,
            incompleteName.IndexOf("Cho", StringComparison.Ordinal),
            CancellationToken.None
        )
            is null,
        "an incomplete component tag leaked its enclosing generated Main symbol."
    );
    var qualifiers = source.Replace(
        "namespace Sample;",
        "namespace Lucent.Core;\nusing static Vendor.Deep.Child;",
        StringComparison.Ordinal
    );
    context.ReplaceText(sourceUri, qualifiers);
    var namespaceQualifier = await context.CompletionsAsync(
        sourceUri,
        qualifiers.IndexOf("Lucent.", StringComparison.Ordinal) + "Lucent.".Length,
        CancellationToken.None
    );
    var usingQualifier = await context.CompletionsAsync(
        sourceUri,
        qualifiers.IndexOf("Vendor.Deep", StringComparison.Ordinal) + "Vendor.Deep".Length,
        CancellationToken.None
    );
    Assert(
        namespaceQualifier.Any(item => item.Label == "Core")
            && !namespaceQualifier.Any(item => item.Label == "Vendor")
            && usingQualifier.Any(item => item.Label == "Child")
            && !usingQualifier.Any(item => item.Label == "Lucent"),
        "namespace and using completion ignored the authored Roslyn qualifier: "
            + String.Join(",", namespaceQualifier.Select(item => item.Label))
            + " / "
            + String.Join(",", usingQualifier.Select(item => item.Label))
    );
    context.ReplaceText(sourceUri, source);
    var reloadedSibling = sibling.Replace(
        "string content",
        "object content",
        StringComparison.Ordinal
    );
    var reachedReload = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    var releaseReload = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    var reloadingNavigation = context.NavigateAsync(
        sourceUri,
        card,
        async () =>
        {
            reachedReload.SetResult();
            await releaseReload.Task;
        },
        CancellationToken.None
    );
    await reachedReload.Task;
    await File.WriteAllTextAsync(siblingPath, reloadedSibling);
    Assert(
        await context.ReloadIfRelevantAsync(new Uri(siblingPath), CancellationToken.None),
        "an owned sibling signature change did not reload the evaluated project."
    );
    releaseReload.SetResult();
    Assert(
        await reloadingNavigation is null,
        "a pre-reload result passed the final publication guard."
    );
    var reloaded = await context.CompileAsync(sourceUri, CancellationToken.None);
    Assert(
        reloaded is not null
            && reloaded.Result.Identity.SiblingIndexGeneration
                != compiled.Result.Identity.SiblingIndexGeneration
            && reloaded
                .Index.Declarations.Single(item =>
                    item.Document.Syntax.Component!.Name.Text == "Card"
                )
                .Document.Syntax.Component!.Parameters[0]
                .DeclarationText == "object content",
        "a sibling signature reload did not replace the evaluated component projection."
    );
    Assert(
        !await context.ReloadIfRelevantAsync(
            new Uri(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cs")),
            CancellationToken.None
        ),
        "a foreign file event reloaded the project."
    );
    await File.WriteAllTextAsync(siblingPath, sibling);
    await context.ReloadIfRelevantAsync(new Uri(siblingPath), CancellationToken.None);
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
        var capabilities = initialized
            .RootElement.GetProperty("result")
            .GetProperty("capabilities");
        Assert(
            capabilities.GetProperty("hoverProvider").GetBoolean()
                && capabilities.TryGetProperty("signatureHelpProvider", out _)
                && capabilities.TryGetProperty("completionProvider", out _)
                && capabilities.TryGetProperty("semanticTokensProvider", out _)
                && capabilities.GetProperty("documentSymbolProvider").GetBoolean(),
            "LSP advertised an incomplete frozen tooling surface."
        );
        Assert(
            capabilities.GetProperty("renameProvider").GetProperty("prepareProvider").GetBoolean()
                && capabilities.GetProperty("documentFormattingProvider").GetBoolean()
                && capabilities.GetProperty("documentRangeFormattingProvider").GetBoolean(),
            "LSP did not advertise rename and formatter providers."
        );
        await lsp.NotifyAsync("initialized", new { });
        await lsp.NotifyAsync(
            "textDocument/didOpen",
            new
            {
                textDocument = new
                {
                    uri = lspSourceUri,
                    version = 1,
                    text = formatterSource,
                },
            }
        );
        using (
            var documentFormatting = await lsp.RequestAsync(
                "textDocument/formatting",
                new { textDocument = new { uri = lspSourceUri }, options = new { } }
            )
        )
            Assert(
                ApplyLspEdits(formatterSource, documentFormatting) == formatterExpected,
                "document formatting RPC diverged from the shared formatter policy."
            );
        var componentSpan = LuiParser.Parse(formatterSource).Component!.Span;
        var componentStart = Position(formatterSource, componentSpan.Start);
        var componentEnd = Position(formatterSource, componentSpan.End);
        using (
            var rangeFormatting = await lsp.RequestAsync(
                "textDocument/rangeFormatting",
                new
                {
                    textDocument = new { uri = lspSourceUri },
                    range = new
                    {
                        start = new
                        {
                            line = componentStart.Line,
                            character = componentStart.Character,
                        },
                        end = new { line = componentEnd.Line, character = componentEnd.Character },
                    },
                    options = new { },
                }
            )
        )
            Assert(
                ApplyLspEdits(formatterSource, rangeFormatting)
                    == LuiFormatter.FormatRange(formatterSource, componentSpan),
                "range formatting RPC diverged from the shared formatter policy."
            );
        await lsp.NotifyAsync(
            "textDocument/didClose",
            new { textDocument = new { uri = lspSourceUri } }
        );
        using var repeatedInitialize = await lsp.RequestAsync(
            "initialize",
            new { initializationOptions = new { projectUri = new Uri(projectPath).AbsoluteUri } }
        );
        Assert(
            repeatedInitialize.RootElement.TryGetProperty("error", out _),
            "repeated initialize was accepted."
        );
        using var completion = await lsp.RequestAsync(
            "textDocument/completion",
            new
            {
                textDocument = new { uri = lspSourceUri },
                position = new { line = rowPosition.Line, character = rowPosition.Character },
            }
        );
        Assert(
            completion
                .RootElement.GetProperty("result")
                .GetProperty("items")
                .EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "Card"),
            "component completion was advertised but did not use the evaluated component index."
        );
        using var hover = await lsp.RequestAsync(
            "textDocument/hover",
            new
            {
                textDocument = new { uri = lspSourceUri },
                position = new { line = rowPosition.Line, character = rowPosition.Character },
            }
        );
        Assert(
            hover.RootElement.GetProperty("result").GetProperty("contents").GetArrayLength() != 0,
            "component hover was advertised but returned no Roslyn symbol information."
        );
        var helperPosition = Position(source, helper);
        var literalPosition = Position(source, source.IndexOf("widget", StringComparison.Ordinal));
        using var helperHover = await lsp.RequestAsync(
            "textDocument/hover",
            new
            {
                textDocument = new { uri = lspSourceUri },
                position = new { line = helperPosition.Line, character = helperPosition.Character },
            }
        );
        Assert(
            helperHover
                .RootElement.GetProperty("result")
                .GetProperty("contents")
                .EnumerateArray()
                .Any(content =>
                    content
                        .GetProperty("value")
                        .GetString()!
                        .Contains("System.String", StringComparison.Ordinal)
                    && content
                        .GetProperty("value")
                        .GetString()!
                        .Contains("value", StringComparison.Ordinal)
                    && content
                        .GetProperty("value")
                        .GetString()!
                        .Contains("Remarks:\nSecond section.", StringComparison.Ordinal)
                ),
            "hover did not render XML documentation references and section boundaries."
        );
        Assert(
            helperHover
                .RootElement.GetProperty("result")
                .GetProperty("contents")
                .EnumerateArray()
                .All(content =>
                    !content
                        .GetProperty("value")
                        .GetString()!
                        .Contains("<member>", StringComparison.Ordinal)
                ),
            "hover leaked raw XML documentation."
        );
        using var literalHover = await lsp.RequestAsync(
            "textDocument/hover",
            new
            {
                textDocument = new { uri = lspSourceUri },
                position = new
                {
                    line = literalPosition.Line,
                    character = literalPosition.Character,
                },
            }
        );
        Assert(
            literalHover
                .RootElement.GetProperty("result")
                .GetProperty("contents")
                .EnumerateArray()
                .Any(content => content.GetProperty("value").GetString() == "string"),
            "literal hover reported the enclosing component instead of its literal type."
        );
        using var helperDefinition = await lsp.RequestAsync(
            "textDocument/definition",
            new
            {
                textDocument = new { uri = lspSourceUri },
                position = new { line = helperPosition.Line, character = helperPosition.Character },
            }
        );
        Assert(
            helperDefinition.RootElement.GetProperty("result").GetProperty("uri").GetString()
                == new Uri(helperPath).AbsoluteUri,
            "expression-island definition did not navigate to the real C# symbol."
        );
        using var signature = await lsp.RequestAsync(
            "textDocument/signatureHelp",
            new
            {
                textDocument = new { uri = lspSourceUri },
                position = new { line = rowPosition.Line, character = rowPosition.Character },
            }
        );
        Assert(
            signature.RootElement.GetProperty("result").GetProperty("signatures").GetArrayLength()
                != 0,
            "component signature help was advertised but returned no overloads."
        );
        using var symbols = await lsp.RequestAsync(
            "textDocument/documentSymbol",
            new { textDocument = new { uri = lspSourceUri } }
        );
        Assert(
            symbols
                .RootElement.GetProperty("result")
                .EnumerateArray()
                .Any(symbol =>
                    symbol.GetProperty("name").GetString() == "Widget"
                    && symbol.GetProperty("children").GetArrayLength() != 0
                ),
            "document symbols omitted the component structure."
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
                    version = 1,
                    text = source.Replace("Widget", "Opened", StringComparison.Ordinal),
                },
            }
        );
        await lsp.NotifyAsync(
            "textDocument/didChange",
            new
            {
                textDocument = new { uri = lspSourceUri, version = 2 },
                contentChanges = new[]
                {
                    new { text = source.Replace("Card", "Missing", StringComparison.Ordinal) },
                },
            }
        );
        using var diagnostics = await lsp.RequestAsync(
            "textDocument/diagnostic",
            new { textDocument = new { uri = lspSourceUri } }
        );
        Assert(
            diagnostics
                .RootElement.GetProperty("result")
                .GetProperty("items")
                .EnumerateArray()
                .Any(item =>
                    item.GetProperty("code").GetString() == "LUI2001"
                    && item.GetProperty("severity").GetInt32() == 1
                    && item.GetProperty("source").GetString() == "Lucent.Lui"
                    && item.GetProperty("range")
                        .GetProperty("start")
                        .GetProperty("character")
                        .GetInt32() == rowPosition.Character
                ),
            "pull diagnostics diverged from compiler ID, severity, or exact authored span."
        );
        using var pushedDiagnostics = lsp.TakeNotification("textDocument/publishDiagnostics");
        Assert(
            pushedDiagnostics is not null
                && pushedDiagnostics
                    .RootElement.GetProperty("params")
                    .GetProperty("version")
                    .GetInt32() == 2
                && pushedDiagnostics
                    .RootElement.GetProperty("params")
                    .GetProperty("diagnostics")
                    .EnumerateArray()
                    .Any(item =>
                        item.GetProperty("code").GetString() == "LUI2001"
                        && item.GetProperty("source").GetString() == "Lucent.Lui"
                        && item.GetProperty("severity").GetInt32() == 1
                    ),
            "push diagnostics did not carry the current LSP document version."
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
        await lsp.NotifyAsync(
            "textDocument/didChange",
            new
            {
                textDocument = new { uri = lspSourceUri, version = 2 },
                contentChanges = new[] { new { text = source } },
            }
        );
        using var staleDefinition = await lsp.RequestAsync(
            "textDocument/definition",
            new
            {
                textDocument = new { uri = lspSourceUri },
                position = new { line = rowPosition.Line, character = rowPosition.Character },
            }
        );
        Assert(
            staleDefinition.RootElement.GetProperty("result").ValueKind == JsonValueKind.Null,
            "a non-increasing didChange replaced the current open document."
        );
        Assert(
            lsp.TakeNotification("textDocument/publishDiagnostics") is null,
            "a stale didChange published an empty diagnostic result."
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
                textDocument = new { uri = lspSourceUri, version = 3 },
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
        using var closeClear = lsp.TakeNotification("textDocument/publishDiagnostics");
        Assert(
            closeClear is not null
                && closeClear.RootElement.GetProperty("params").GetProperty("uri").GetString()
                    == lspSourceUri
                && closeClear.RootElement.GetProperty("params").GetProperty("version").GetInt32()
                    == 2
                && closeClear
                    .RootElement.GetProperty("params")
                    .GetProperty("diagnostics")
                    .GetArrayLength() == 0,
            "didClose did not explicitly clear its pushed diagnostics."
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
    await RunDiagnosticParityAsync(core);
    await RunInvalidLogicalSiblingAsync(core);
    await RunAncestorInputReloadAsync(core);
    await RunFreshnessAndProjectGraphRegressionsAsync(core);
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
    var column = headerText.IndexOf("Column", StringComparison.Ordinal);
    var buttonAttribute = headerText.IndexOf("onInvoke", StringComparison.Ordinal);
    var styleProperty = headerText.IndexOf("Width", StringComparison.Ordinal);
    var styleReference = headerText.LastIndexOf("HeaderTitleStyle", StringComparison.Ordinal);
    Assert(
        await issueBrowser.NavigateAsync(header, filterBar, CancellationToken.None) is not null,
        "evaluated Issue Browser Header.lui could not bind its FilterBar sibling."
    );
    var filterHelp = await issueBrowser.SignatureHelpAsync(
        header,
        headerText.IndexOf("browser={browser}", StringComparison.Ordinal),
        CancellationToken.None
    );
    var buttonHelp = await issueBrowser.SignatureHelpAsync(
        header,
        headerText.IndexOf("Density: Comfortable/Compact", StringComparison.Ordinal),
        CancellationToken.None
    );
    Assert(
        filterHelp is { ActiveParameter: 0 }
            && filterHelp
                .Signatures[filterHelp.ActiveSignature]
                .Label.Contains("FilterBar", StringComparison.Ordinal)
            && buttonHelp is { ActiveParameter: 0 }
            && buttonHelp
                .Signatures[buttonHelp.ActiveSignature]
                .Label.Contains("Button", StringComparison.Ordinal),
        "signature help did not map named attributes and default content to bound parameter ordinals."
    );
    var tagCompletions = await issueBrowser.CompletionsAsync(
        header,
        column,
        CancellationToken.None
    );
    Assert(
        tagCompletions.Any(item => item.Label == "Column")
            && tagCompletions.Any(item => item.Label == "Row"),
        "Header.lui tag completion omitted evaluated component methods."
    );
    Assert(
        tagCompletions.Any(item =>
            item.Label == "FilterBar"
            && item.Kind == 2
            && item.Detail.Contains("FilterBar", StringComparison.Ordinal)
        ),
        "Header.lui custom tag completion lost its real component method detail."
    );
    var attributes = await issueBrowser.CompletionsAsync(
        header,
        buttonAttribute,
        CancellationToken.None
    );
    Assert(
        attributes.Any(item =>
            item.Label == "onInvoke"
            && item.Kind == 6
            && item.Detail.Contains("Action", StringComparison.Ordinal)
        )
            && attributes.Any(item => item.Label == "content" && item.Kind == 6)
            && attributes.Any(item => item.Label == "name" && item.Kind == 6),
        "component parameter/default-content completion omitted typed Button parameters."
    );
    var properties = await issueBrowser.CompletionsAsync(
        header,
        styleProperty,
        CancellationToken.None
    );
    Assert(
        properties.Any(item =>
            item.Label == "Width"
            && item.Kind == 5
            && item.Detail.Contains("Property", StringComparison.Ordinal)
        ) && !properties.Any(item => item.Label == "VirtualRowHeight"),
        "style property completion was not limited to public typed Property<T> declarations: "
            + String.Join(
                ", ",
                properties.Select(item => item.Label + "/" + item.Kind + "/" + item.Detail)
            )
    );
    var references = await issueBrowser.CompletionsAsync(
        header,
        styleReference,
        CancellationToken.None
    );
    Assert(
        references.Any(item => item.Label == "HeaderStyle" && item.Kind == 5)
            && references.Any(item => item.Label == "DensityButtonStyle" && item.Kind == 5),
        "style-reference completion omitted compiler-owned style declarations."
    );
    var namespaceCompletions = await issueBrowser.CompletionsAsync(
        header,
        headerText.IndexOf("Lucent.IssueBrowser", StringComparison.Ordinal),
        CancellationToken.None
    );
    Assert(
        namespaceCompletions.Any(item => item.Label == "Lucent" && item.Kind == 9),
        "namespace completion omitted real compilation namespaces."
    );

    var issueRow = new Uri(Path.GetFullPath("apps/Lucent.IssueBrowser/IssueRow.lui"));
    var issueRowText = await File.ReadAllTextAsync(issueRow.LocalPath);
    var issueRowSymbols = await issueBrowser.DocumentSymbolsAsync(issueRow, CancellationToken.None);
    var variant = LuiParser
        .Parse(issueRowText)
        .Styles.Single()
        .Members.OfType<LuiVariantGroupSyntax>()
        .Single();
    var variantSymbol = issueRowSymbols!
        .Single(symbol => symbol.Name == "IssueRowStyle")
        .Children.Single(symbol => symbol.Name.StartsWith("when ", StringComparison.Ordinal));
    Assert(
        variantSymbol.SelectionSpan.Equals(variant.Condition.Span),
        "variant document-symbol selection range did not select its condition."
    );
    Assert(
        issueRowSymbols!
            .SelectMany(symbol => symbol.Children)
            .Single(symbol => symbol.Name == "Selectable")
            .Children.Single(symbol => symbol.Name == "style")
            .Children.Any(symbol => symbol.Name == "Height"),
        "inline style document symbols omitted the IssueRow Height assignment."
    );
    var error = new Uri(Path.GetFullPath("apps/Lucent.IssueBrowser/Error.lui"));
    var errorText = await File.ReadAllTextAsync(error.LocalPath);
    var tokenCompletions = await issueBrowser.CompletionsAsync(
        issueRow,
        issueRowText.IndexOf("DensitySpacing", StringComparison.Ordinal),
        CancellationToken.None
    );
    var variantCompletions = await issueBrowser.CompletionsAsync(
        issueRow,
        issueRowText.IndexOf("FocusVisible", StringComparison.Ordinal),
        CancellationToken.None
    );
    Assert(
        tokenCompletions.Any(item =>
            item.Label == "DensitySpacing"
            && item.Kind == 5
            && item.Detail.Contains("Token", StringComparison.Ordinal)
        ) && !tokenCompletions.Any(item => item.Label == "AppTheme"),
        "token RHS completion leaked non-Token root members."
    );
    Assert(
        variantCompletions.Any(item => item.Label == "FocusVisible" && item.Kind == 20)
            && !variantCompletions.Any(item => item.Label == "None"),
        "variant completion did not expose the documented VariantState members."
    );
    var axisCompletions = await issueBrowser.CompletionsAsync(
        error,
        errorText.IndexOf("LayoutAxis.", StringComparison.Ordinal) + "LayoutAxis.".Length,
        CancellationToken.None
    );
    Assert(
        axisCompletions.Any(item => item.Label == "Column" && item.Kind == 20),
        "style RHS completion did not merge Roslyn enum members with token candidates: "
            + String.Join(", ", axisCompletions.Select(item => item.Label + "/" + item.Kind))
    );
}

using (var browserLsp = LspClient.Start())
{
    var project = new Uri(Path.GetFullPath("apps/Lucent.IssueBrowser/Lucent.IssueBrowser.csproj"));
    var header = new Uri(Path.GetFullPath("apps/Lucent.IssueBrowser/Header.lui"));
    var headerText = await File.ReadAllTextAsync(header.LocalPath);
    using var browserInitialized = await browserLsp.RequestAsync(
        "initialize",
        new { initializationOptions = new { projectUri = VsCodeUri(project) } }
    );
    var semanticCapability = browserInitialized
        .RootElement.GetProperty("result")
        .GetProperty("capabilities")
        .GetProperty("semanticTokensProvider");
    Assert(
        semanticCapability
            .GetProperty("legend")
            .GetProperty("tokenTypes")
            .EnumerateArray()
            .Select(token => token.GetString())
            .SequenceEqual(["keyword", "type", "property", "enumMember"])
            && semanticCapability.GetProperty("full").GetBoolean(),
        "LSP did not advertise the standard semantic-token legend and full provider."
    );
    await browserLsp.NotifyAsync("initialized", new { });
    var member = headerText.IndexOf("browser.ToggleDensity", StringComparison.Ordinal);
    var memberPosition = Position(headerText, member + "browser.".Length);
    using var browserCompletion = await browserLsp.RequestAsync(
        "textDocument/completion",
        new
        {
            textDocument = new { uri = VsCodeUri(header) },
            position = new { line = memberPosition.Line, character = memberPosition.Character },
        }
    );
    Assert(
        browserCompletion.RootElement.TryGetProperty("result", out _),
        "Roslyn completion request failed: " + browserCompletion.RootElement.GetRawText()
    );
    Assert(
        browserCompletion
            .RootElement.GetProperty("result")
            .GetProperty("items")
            .EnumerateArray()
            .Any(item =>
                item.GetProperty("label").GetString() == "ToggleDensity"
                && item.GetProperty("kind").GetInt32() == 2
            ),
        "Roslyn completion did not provide IssueBrowserState members."
    );
    using var browserDefinition = await browserLsp.RequestAsync(
        "textDocument/definition",
        new
        {
            textDocument = new { uri = VsCodeUri(header) },
            position = new { line = memberPosition.Line, character = memberPosition.Character },
        }
    );
    Assert(
        browserDefinition
            .RootElement.GetProperty("result")
            .GetProperty("uri")
            .GetString()!
            .EndsWith("IssueBrowserState.cs", StringComparison.Ordinal),
        "browser member definition did not use the exact C# declaration span."
    );
    var browserDocument = new Uri(Path.GetFullPath("apps/Lucent.IssueBrowser/IssueBrowser.lui"));
    var browserText = await File.ReadAllTextAsync(browserDocument.LocalPath);
    var headerTokens = await SemanticTokensAsync(browserLsp, header, headerText);
    var errorDocument = new Uri(Path.GetFullPath("apps/Lucent.IssueBrowser/Error.lui"));
    var errorText = await File.ReadAllTextAsync(errorDocument.LocalPath);
    var errorTokens = await SemanticTokensAsync(browserLsp, errorDocument, errorText);
    var browserTokens = await SemanticTokensAsync(browserLsp, browserDocument, browserText);
    var issueRowDocument = new Uri(Path.GetFullPath("apps/Lucent.IssueBrowser/IssueRow.lui"));
    var issueRowText = await File.ReadAllTextAsync(issueRowDocument.LocalPath);
    var issueRowTokens = await SemanticTokensAsync(browserLsp, issueRowDocument, issueRowText);
    AssertSemanticToken(headerTokens, headerText, "Width", "property");
    AssertSemanticToken(headerTokens, headerText, "IssueBrowserState", "type");
    AssertSemanticToken(errorTokens, errorText, "LayoutAxis", "type");
    AssertSemanticToken(errorTokens, errorText, "Column", "enumMember");
    AssertSemanticToken(browserTokens, browserText, "var", "keyword");
    var inKeyword = browserText.IndexOf(" in ", StringComparison.Ordinal) + 1;
    Assert(
        browserTokens.Any(token =>
            token.Start == inKeyword && token.Length == 2 && token.Type == "keyword"
        ),
        "semantic tokens did not classify the foreach 'in' as keyword."
    );
    AssertSemanticToken(issueRowTokens, issueRowText, "with", "keyword");
    foreach (
        var (offset, label) in new[]
        {
            (
                browserText.IndexOf("browser.IsLoading", StringComparison.Ordinal)
                    + "browser.".Length,
                "IsLoading"
            ),
            (
                browserText.IndexOf("issue.Number", StringComparison.Ordinal) + "issue.".Length,
                "Number"
            ),
        }
    )
    {
        var position = Position(browserText, offset);
        using var completion = await browserLsp.RequestAsync(
            "textDocument/completion",
            new
            {
                textDocument = new { uri = VsCodeUri(browserDocument) },
                position = new { line = position.Line, character = position.Character },
            }
        );
        Assert(
            completion
                .RootElement.GetProperty("result")
                .GetProperty("items")
                .EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == label),
            "if/keyed-foreach expression completion lost semantic locals or members."
        );
    }
    await browserLsp.RequestAsync("shutdown", new { });
    Assert(await browserLsp.ExitAsync() == 0, "browser completion LSP did not shut down cleanly.");
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

return 0;

static async Task RunDiagnosticParityAsync(string core)
{
    var cases = new[]
    {
        new DiagnosticCase(
            "parser",
            "namespace Sample;\nusing Lucent.Core;\ninternal component Widget() { <Row />",
            ["Widget.lui"],
            ["Widget.lui"],
            null
        ),
        new DiagnosticCase(
            "semantic",
            "namespace Sample;\nusing Lucent.Core;\ninternal component Widget() { <Missing /> }",
            ["Widget.lui"],
            ["Widget.lui"],
            null
        ),
        new DiagnosticCase(
            "duplicate-component",
            "namespace Sample;\nusing Lucent.Core;\ninternal component Widget() { <Row /> }",
            ["Widget.lui", "Other.lui"],
            ["Widget.lui", "Other.lui"],
            null
        ),
        new DiagnosticCase(
            "duplicate-logical-path",
            "namespace Sample;\nusing Lucent.Core;\ninternal component First() { <Row /> }",
            ["First.lui", "Second.lui"],
            ["shared/Widget.lui", "shared/Widget.lui"],
            null
        ),
        new DiagnosticCase(
            "blank-logical-path",
            "namespace Sample;\nusing Lucent.Core;\ninternal component Widget() { <Row /> }",
            ["Widget.lui"],
            [" "],
            null
        ),
        new DiagnosticCase(
            "invalid-logical-path",
            "namespace Sample;\nusing Lucent.Core;\ninternal component Widget() { <Row /> }",
            ["Widget.lui"],
            ["../Widget.lui"],
            null
        ),
        new DiagnosticCase(
            "warning",
            "namespace Sample;\nusing Lucent.Core;\ninternal component Widget() { <Missing /> }",
            ["Widget.lui"],
            ["Widget.lui"],
            "warning"
        ),
        new DiagnosticCase(
            "suppression",
            "namespace Sample;\nusing Lucent.Core;\ninternal component Widget() { <Missing /> }",
            ["Widget.lui"],
            ["Widget.lui"],
            "none"
        ),
    };

    foreach (var testCase in cases)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-diagnostics-" + testCase.Name + "-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "Sample.csproj");
            var project =
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Sample</RootNamespace><LangVersion>preview</LangVersion></PropertyGroup><ItemGroup><ProjectReference Include=\""
                + core
                + "\" />"
                + String.Join(
                    "",
                    testCase.Files.Select(
                        (file, index) =>
                            "<AdditionalFiles Include=\""
                            + file
                            + "\" LucentLuiLogicalPath=\""
                            + testCase.LogicalPaths[index]
                            + "\" LucentLuiDocumentVersion=\"17\" />"
                    )
                )
                + "<CompilerVisibleItemMetadata Include=\"AdditionalFiles\" MetadataName=\"LucentLuiLogicalPath\" /><CompilerVisibleItemMetadata Include=\"AdditionalFiles\" MetadataName=\"LucentLuiDocumentVersion\" /></ItemGroup></Project>";
            await File.WriteAllTextAsync(projectPath, project);
            foreach (var (file, index) in testCase.Files.Select((file, index) => (file, index)))
            {
                var source =
                    testCase.Name == "duplicate-logical-path" && index == 1
                        ? testCase.Source.Replace("First", "Second", StringComparison.Ordinal)
                        : testCase.Source;
                await File.WriteAllTextAsync(Path.Combine(root, file), source);
            }
            if (testCase.EditorSeverity is not null)
                await File.WriteAllTextAsync(
                    Path.Combine(root, ".editorconfig"),
                    "root = true\n[*.lui]\ndotnet_diagnostic.LUI2001.severity = "
                        + testCase.EditorSeverity
                );

            var currentPath = Path.Combine(root, testCase.Files[0]);
            var sourceText = await File.ReadAllTextAsync(currentPath);
            var build = await BuildDiagnosticsAsync(projectPath, currentPath);
            var (pull, push) = await LspDiagnosticsAsync(
                projectPath,
                new Uri(currentPath),
                sourceText
            );
            Assert(
                build.SequenceEqual(pull) && build.SequenceEqual(push),
                testCase.Name
                    + " diagnostics diverged between generator, pull, and push:\nbuild: "
                    + String.Join(" | ", build)
                    + "\npull: "
                    + String.Join(" | ", pull)
                    + "\npush: "
                    + String.Join(" | ", push)
            );
            Assert(
                build.All(item =>
                    item.Uri == new Uri(currentPath).AbsoluteUri && item.Version == 17
                ),
                testCase.Name + " diagnostics lost the current URI or document version."
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task RunInvalidLogicalSiblingAsync(string core)
{
    var root = Path.Combine(Path.GetTempPath(), "lucent-invalid-sibling-" + Guid.NewGuid());
    Directory.CreateDirectory(root);
    try
    {
        var projectPath = Path.Combine(root, "Sample.csproj");
        var consumer = Path.Combine(root, "Consumer.lui");
        var invalid = Path.Combine(root, "Invalid.lui");
        await File.WriteAllTextAsync(
            projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Sample</RootNamespace></PropertyGroup><ItemGroup><ProjectReference Include=\""
                + core
                + "\" /><AdditionalFiles Include=\"Consumer.lui\" LucentLuiLogicalPath=\"Consumer.lui\" /><AdditionalFiles Include=\"Invalid.lui\" LucentLuiLogicalPath=\"../Invalid.lui\" /><CompilerVisibleItemMetadata Include=\"AdditionalFiles\" MetadataName=\"LucentLuiLogicalPath\" /></ItemGroup></Project>"
        );
        await File.WriteAllTextAsync(
            consumer,
            "namespace Sample; using Lucent.Core; internal component Consumer() { <Invalid /> }"
        );
        await File.WriteAllTextAsync(
            invalid,
            "namespace Sample; using Lucent.Core; internal component Invalid() { <Row /> }"
        );
        using var context = await LuiProjectContext.LoadAsync(projectPath, CancellationToken.None);
        var consumerDiagnostics = await context.DiagnosticsAsync(
            new Uri(consumer),
            CancellationToken.None
        );
        var invalidDiagnostics = await context.DiagnosticsAsync(
            new Uri(invalid),
            CancellationToken.None
        );
        Assert(
            consumerDiagnostics!.Any(item => item.Code == "LUI2001")
                && invalidDiagnostics!.Single().Code == "LUI4003",
            "an invalid logical-path sibling entered the editor component index."
        );
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task RunAncestorInputReloadAsync(string core)
{
    var root = Path.Combine(Path.GetTempPath(), "lucent-ancestor-input-" + Guid.NewGuid());
    var projectRoot = Path.Combine(root, "project");
    Directory.CreateDirectory(projectRoot);
    try
    {
        var projectPath = Path.Combine(projectRoot, "Sample.csproj");
        await File.WriteAllTextAsync(
            projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include=\""
                + core
                + "\" /><AdditionalFiles Include=\"Widget.lui\" /></ItemGroup></Project>"
        );
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "Widget.lui"),
            "namespace Sample; using Lucent.Core; internal component Widget() { <Row /> }"
        );
        var editorConfig = Path.Combine(root, ".editorconfig");
        var packages = Path.Combine(root, "Directory.Packages.props");
        await File.WriteAllTextAsync(editorConfig, "root = true");
        await File.WriteAllTextAsync(packages, "<Project />");
        using var context = await LuiProjectContext.LoadAsync(projectPath, CancellationToken.None);
        Assert(
            await context.ReloadIfRelevantAsync(new Uri(editorConfig), CancellationToken.None)
                && await context.ReloadIfRelevantAsync(new Uri(packages), CancellationToken.None),
            "ancestor editorconfig or package-props changes were not relevant project inputs."
        );
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task RunFreshnessAndProjectGraphRegressionsAsync(string core)
{
    var root = Path.Combine(Path.GetTempPath(), "lucent-project-graph-" + Guid.NewGuid());
    var hostRoot = Path.Combine(root, "host");
    var referencedRoot = Path.Combine(root, "referenced");
    Directory.CreateDirectory(hostRoot);
    Directory.CreateDirectory(referencedRoot);
    var hostProject = Path.Combine(hostRoot, "Host.csproj");
    var referencedProject = Path.Combine(referencedRoot, "Referenced.csproj");
    var source = Path.Combine(hostRoot, "Widget.lui");
    var referencedSource = Path.Combine(referencedRoot, "Components.cs");
    var hostProjectText =
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Host</RootNamespace></PropertyGroup><ItemGroup><ProjectReference Include=\""
        + core
        + "\" /><ProjectReference Include=\"../referenced/Referenced.csproj\" /><AdditionalFiles Include=\"Widget.lui\" /></ItemGroup></Project>";
    try
    {
        await File.WriteAllTextAsync(
            referencedProject,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include=\""
                + core
                + "\" /></ItemGroup></Project>"
        );
        await File.WriteAllTextAsync(
            referencedSource,
            "namespace Referenced; using Lucent.Core; public static class Components { [LucentComponent] public static ComponentRecipe External(Style? style = null, string value = \"\") => null!; }"
        );
        await File.WriteAllTextAsync(hostProject, hostProjectText);
        await File.WriteAllTextAsync(
            source,
            "namespace Host; using Lucent.Core; using static Referenced.Components; using static Host.ImportedProperties; internal component Widget(Style style) { <External style={style} /> } style Local { Imported: PublicToken; }"
        );
        await File.WriteAllTextAsync(
            Path.Combine(hostRoot, "Tokens.cs"),
            "namespace Host; using Lucent.Core; internal static class Tokens { public static readonly Token<float?> PublicToken = new(\"public\", 0f); private static readonly Token<float?> PrivateToken = new(\"private\", 0f); }"
        );
        await File.WriteAllTextAsync(
            Path.Combine(hostRoot, "ImportedProperties.cs"),
            "namespace Host; using Lucent.Core; public static class ImportedProperties { public static readonly Property<float?> Imported = new(\"imported\", 0f); private static readonly Property<float?> PrivateImported = new(\"private\", 0f); }"
        );
        using var context = await LuiProjectContext.LoadAsync(hostProject, CancellationToken.None);
        var uri = new Uri(source);
        var published = await context.CompileAsync(uri, CancellationToken.None);
        if (published is null)
            throw new InvalidOperationException(
                "project-graph fixture did not compile: "
                    + String.Join(
                        " | ",
                        (await context.DiagnosticsAsync(uri, CancellationToken.None))!.Select(
                            item => item.Code + ":" + item.Message
                        )
                    )
            );
        Assert(
            context.ProjectDirectories().Contains(referencedRoot, StringComparer.OrdinalIgnoreCase),
            "evaluated project references did not contribute watched project directories."
        );
        var sourceText = await File.ReadAllTextAsync(source);
        var styleParameter = await context.CompletionsAsync(
            uri,
            sourceText.IndexOf("style={style}", StringComparison.Ordinal) + "style={".Length,
            CancellationToken.None
        );
        var importedProperty = await context.CompletionsAsync(
            uri,
            sourceText.LastIndexOf("Imported", StringComparison.Ordinal),
            CancellationToken.None
        );
        var token = await context.CompletionsAsync(
            uri,
            sourceText.IndexOf("PublicToken", StringComparison.Ordinal),
            CancellationToken.None
        );
        Assert(
            styleParameter.Any(item => item.Label == "style")
                && importedProperty.Any(item => item.Label == "Imported")
                && !importedProperty.Any(item => item.Label == "PrivateImported")
                && token.Any(item => item.Label == "PublicToken")
                && !token.Any(item => item.Label == "PrivateToken"),
            "style completion did not merge Roslyn values with accessible imported properties and tokens: "
                + String.Join(",", styleParameter.Select(item => item.Label))
                + " / "
                + String.Join(",", importedProperty.Select(item => item.Label))
                + " / "
                + String.Join(",", token.Select(item => item.Label))
        );
        await File.WriteAllTextAsync(
            referencedSource,
            "namespace Referenced; using Lucent.Core; public static class Components { [LucentComponent] public static ComponentRecipe External(Style? style, int value) => null!; }"
        );
        Assert(
            await context.ReloadIfRelevantAsync(new Uri(referencedSource), CancellationToken.None)
                && !await context.IsCurrentAsync(published.Result, CancellationToken.None),
            "referenced-project component changes did not reload and invalidate prior output."
        );
        var text = await File.ReadAllTextAsync(source);
        var signature = await context.SignatureHelpAsync(
            uri,
            text.IndexOf("External", StringComparison.Ordinal),
            CancellationToken.None
        );
        Assert(
            signature is not null
                && signature.Signatures.Any(item =>
                    item.Label.Contains("int value", StringComparison.Ordinal)
                )
                && (await context.DiagnosticsAsync(uri, CancellationToken.None))!.Count != 0,
            "referenced-project reload did not refresh completion/signature diagnostics."
        );
        await File.WriteAllTextAsync(hostProject, "not xml");
        Assert(
            await context.ReloadIfRelevantAsync(new Uri(hostProject), CancellationToken.None)
                && await context.CompileAsync(uri, CancellationToken.None) is null,
            "a failed project reload retained valid stale semantics."
        );
        await File.WriteAllTextAsync(hostProject, hostProjectText);
        await context.ReloadIfRelevantAsync(new Uri(hostProject), CancellationToken.None);
        await File.WriteAllTextAsync(
            hostProject,
            hostProjectText.Replace(
                "<AdditionalFiles Include=\"Widget.lui\" />",
                "",
                StringComparison.Ordinal
            )
        );
        Assert(
            await context.ReloadIfRelevantAsync(new Uri(hostProject), CancellationToken.None)
                && !await context.IsCurrentAsync(published.Result, CancellationToken.None),
            "removed AdditionalFiles documents threw or remained fresh."
        );
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task<DiagnosticValue[]> BuildDiagnosticsAsync(string projectPath, string currentPath)
{
    using var workspace = MSBuildWorkspace.Create();
    var project = await workspace.OpenProjectAsync(projectPath);
    var compilation =
        await project.GetCompilationAsync()
        ?? throw new InvalidOperationException("Missing diagnostic test compilation.");
    var files = project.AnalyzerOptions.AdditionalFiles.Where(file =>
        file.Path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase)
    );
    GeneratorDriver driver = CSharpGeneratorDriver.Create(
        System.Collections.Immutable.ImmutableArray.Create(new LuiGenerator().AsSourceGenerator()),
        files,
        (CSharpParseOptions)project.ParseOptions!,
        project.AnalyzerOptions.AnalyzerConfigOptionsProvider
    );
    var diagnostics = driver.RunGenerators(compilation).GetRunResult().Diagnostics;
    return diagnostics
        .Where(diagnostic =>
            diagnostic.Id.StartsWith("LUI", StringComparison.Ordinal)
            && String.Equals(
                diagnostic.Location.GetLineSpan().Path,
                currentPath,
                StringComparison.OrdinalIgnoreCase
            )
        )
        .Select(diagnostic =>
        {
            var span = diagnostic.Location.GetLineSpan().Span;
            return new DiagnosticValue(
                diagnostic.Id,
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Severity(diagnostic.Severity),
                diagnostic.Descriptor.Category,
                span.Start.Line,
                span.Start.Character,
                span.End.Line,
                span.End.Character,
                new Uri(currentPath).AbsoluteUri,
                17
            );
        })
        .OrderBy(item => item.StartLine)
        .ThenBy(item => item.StartCharacter)
        .ThenBy(item => item.Id, StringComparer.Ordinal)
        .ToArray();
}

static async Task<(DiagnosticValue[] Pull, DiagnosticValue[] Push)> LspDiagnosticsAsync(
    string projectPath,
    Uri uri,
    string source
)
{
    using var lsp = LspClient.Start();
    var lspUri = VsCodeUri(uri);
    using var initialized = await lsp.RequestAsync(
        "initialize",
        new { initializationOptions = new { projectUri = VsCodeUri(new Uri(projectPath)) } }
    );
    await lsp.NotifyAsync("initialized", new { });
    await lsp.NotifyAsync(
        "textDocument/didOpen",
        new
        {
            textDocument = new
            {
                uri = lspUri,
                version = 17,
                text = source,
            },
        }
    );
    using var pullResponse = await lsp.RequestAsync(
        "textDocument/diagnostic",
        new { textDocument = new { uri = lspUri } }
    );
    Assert(
        pullResponse.RootElement.TryGetProperty("result", out var pullResult)
            && pullResult.TryGetProperty("items", out _),
        "diagnostic pull failed: " + pullResponse.RootElement.GetRawText()
    );
    var pull = ParseLspDiagnostics(
        pullResponse.RootElement.GetProperty("result").GetProperty("items"),
        uri.AbsoluteUri,
        source,
        17
    );
    using var pushed = lsp.TakeNotification("textDocument/publishDiagnostics");
    Assert(pushed is not null, "diagnostic push output was not captured.");
    var pushParameters = pushed!.RootElement.GetProperty("params");
    Assert(
        pushParameters.GetProperty("uri").GetString() == lspUri
            && pushParameters.GetProperty("version").GetInt32() == 17,
        "diagnostic push output lost the current URI or document version."
    );
    var push = ParseLspDiagnostics(
        pushParameters.GetProperty("diagnostics"),
        uri.AbsoluteUri,
        source,
        17
    );
    await lsp.RequestAsync("shutdown", new { });
    Assert(await lsp.ExitAsync() == 0, "diagnostic LSP did not shut down cleanly.");
    return (pull, push);
}

static DiagnosticValue[] ParseLspDiagnostics(
    JsonElement items,
    string uri,
    string source,
    int version
) =>
    items
        .EnumerateArray()
        .Select(item =>
        {
            var range = item.GetProperty("range");
            var start = range.GetProperty("start");
            var end = range.GetProperty("end");
            return new DiagnosticValue(
                item.GetProperty("code").GetString()!,
                item.GetProperty("message").GetString()!,
                item.GetProperty("severity").GetInt32(),
                item.GetProperty("source").GetString()!,
                start.GetProperty("line").GetInt32(),
                start.GetProperty("character").GetInt32(),
                end.GetProperty("line").GetInt32(),
                end.GetProperty("character").GetInt32(),
                uri,
                version
            );
        })
        .OrderBy(item => item.StartLine)
        .ThenBy(item => item.StartCharacter)
        .ThenBy(item => item.Id, StringComparer.Ordinal)
        .ToArray();

static int Severity(DiagnosticSeverity severity) =>
    severity switch
    {
        DiagnosticSeverity.Error => 1,
        DiagnosticSeverity.Warning => 2,
        DiagnosticSeverity.Info => 3,
        _ => 4,
    };

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

static async Task<int> MeasureOperationAsync(string measure)
{
    var root = Path.Combine(
        Path.GetTempPath(),
        "lucent-lsp-measure-" + Guid.NewGuid().ToString("N")
    );
    Directory.CreateDirectory(root);
    var core = Path.GetFullPath("src/Lucent.Core/Lucent.Core.csproj");
    var project = new Uri(Path.Combine(root, "Measure.csproj"));
    var document = new Uri(Path.Combine(root, "Widget.lui"));
    var source =
        "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; internal component Widget(int count) { <Row><Text content={Helpers.Format(count)} /></Row> }";
    await File.WriteAllTextAsync(
        project.LocalPath,
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Sample</RootNamespace><LangVersion>preview</LangVersion></PropertyGroup><ItemGroup><ProjectReference Include=\""
            + core
            + "\" /><AdditionalFiles Include=\"Widget.lui\" /></ItemGroup></Project>"
    );
    await File.WriteAllTextAsync(
        Path.Combine(root, "Helpers.cs"),
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
    var renameLocation = Position(source, source.IndexOf("Format", StringComparison.Ordinal));
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
                Assert(
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
                Assert(
                    diagnostic
                        .RootElement.GetProperty("result")
                        .GetProperty("items")
                        .EnumerateArray()
                        .Any(item => item.GetProperty("code").GetString() == "LUI2001"),
                    "edit-to-diagnostic measurement returned no current diagnostic."
                );
            break;
        case "rename":
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
                Assert(
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

static string Apply(string source, LuiFormatResult edit) =>
    source[..edit.Span.Start] + edit.NewText + source[edit.Span.End..];

static async Task<int> ToolingExitCodeAsync(params string[] arguments)
{
    var configuration = Directory
        .GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!
        .Name;
    var executable = Path.GetFullPath(
        $"src/Lucent.Lui.Tooling/bin/{configuration}/net10.0/Lucent.Lui.Tooling.exe"
    );
    using var process =
        Process.Start(
            new ProcessStartInfo(
                executable,
                String.Join(" ", arguments.Select(argument => '"' + argument + '"'))
            )
            {
                UseShellExecute = false,
            }
        ) ?? throw new InvalidOperationException("Could not start the LUI tooling CLI.");
    await process.WaitForExitAsync();
    return process.ExitCode;
}

static string ApplyLspEdits(string source, JsonDocument response)
{
    var edits = response.RootElement.GetProperty("result").EnumerateArray().ToArray();
    foreach (
        var edit in edits.OrderByDescending(item =>
            Offset(source, item.GetProperty("range").GetProperty("start"))
        )
    )
    {
        var range = edit.GetProperty("range");
        var start = Offset(source, range.GetProperty("start"));
        var end = Offset(source, range.GetProperty("end"));
        source = source[..start] + edit.GetProperty("newText").GetString() + source[end..];
    }
    return source;
}

static int Offset(string text, JsonElement position)
{
    var line = position.GetProperty("line").GetInt32();
    var character = position.GetProperty("character").GetInt32();
    var offset = 0;
    while (line-- > 0)
    {
        var newline = text.IndexOf('\n', offset);
        if (newline < 0)
            throw new InvalidOperationException("LSP position exceeds the source line count.");
        offset = newline + 1;
    }
    return offset + character;
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

static async Task<SemanticTokenValue[]> SemanticTokensAsync(LspClient lsp, Uri uri, string text)
{
    using var response = await lsp.RequestAsync(
        "textDocument/semanticTokens/full",
        new { textDocument = new { uri = VsCodeUri(uri) } }
    );
    var data = response
        .RootElement.GetProperty("result")
        .GetProperty("data")
        .EnumerateArray()
        .Select(value => value.GetInt32())
        .ToArray();
    var legend = new[] { "keyword", "type", "property", "enumMember" };
    var tokens = new List<SemanticTokenValue>();
    var line = 0;
    var character = 0;
    for (var index = 0; index < data.Length; index += 5)
    {
        line += data[index];
        character = data[index] == 0 ? character + data[index + 1] : data[index + 1];
        var start = text.Split('\n').Take(line).Sum(value => value.Length + 1) + character;
        tokens.Add(new SemanticTokenValue(start, data[index + 2], legend[data[index + 3]]));
    }
    return tokens.ToArray();
}

static void AssertSemanticToken(
    IEnumerable<SemanticTokenValue> tokens,
    string text,
    string value,
    string type
)
{
    var start = text.IndexOf(value, StringComparison.Ordinal);
    Assert(
        tokens.Any(token =>
            token.Start == start && token.Length == value.Length && token.Type == type
        ),
        "semantic tokens did not classify '" + value + "' as " + type + "."
    );
}

sealed record DiagnosticCase(
    string Name,
    string Source,
    string[] Files,
    string[] LogicalPaths,
    string? EditorSeverity
);

sealed record DiagnosticValue(
    string Id,
    string Message,
    int Severity,
    string Source,
    int StartLine,
    int StartCharacter,
    int EndLine,
    int EndCharacter,
    string Uri,
    int Version
);

sealed record SemanticTokenValue(int Start, int Length, string Type);

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
        var built = Path.GetFullPath(
            "src/Lucent.Lui.LanguageServer/bin/Debug/net10.0/Lucent.Lui.LanguageServer.exe"
        );
        var process =
            Process.Start(
                new ProcessStartInfo(
                    File.Exists(built)
                        ? built
                        : Path.Combine(AppContext.BaseDirectory, "Lucent.Lui.LanguageServer.exe")
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

    internal JsonDocument? TakeNotification(string method)
    {
        JsonDocument? found = null;
        for (var index = notifications.Count; index > 0; index--)
        {
            var message = notifications.Dequeue();
            if (message.RootElement.GetProperty("method").GetString() == method)
            {
                found?.Dispose();
                found = message;
            }
            else
                notifications.Enqueue(message);
        }
        return found;
    }

    public void Dispose()
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
        process.Dispose();
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
