using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Lucent.Lui.Compiler;
using Lucent.Lui.Generator;
using Lucent.Lui.LanguageServer;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.LanguageServer.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LanguageServerTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        "../../../../..",
        AppContext.BaseDirectory
    );

    [AssemblyInitialize]
    public static void Initialize(TestContext _)
    {
        Directory.SetCurrentDirectory(RepositoryRoot);
        if (!MSBuildLocator.IsRegistered)
            MSBuildLocator.RegisterDefaults();
    }

    [TestMethod]
    public async Task ProtocolFramingUsesUtf8BytesForSequentialMessages()
    {
        var originalOutputEncoding = Console.OutputEncoding;
        try
        {
            Console.OutputEncoding = Encoding.Latin1;
            const string first =
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"hover\":\"café 日本語\"}}";
            const string second =
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"diagnostic\":\"naïve\"}}";
            using var wire = new MemoryStream();
            LspProtocol.WriteMessage(wire, first);
            LspProtocol.WriteMessage(wire, second);

            var bytes = wire.ToArray();
            var firstBody = Encoding.UTF8.GetBytes(first);
            var firstHeader = Encoding.ASCII.GetBytes(
                $"Content-Length: {firstBody.Length}\r\n\r\n"
            );
            Assert(
                bytes.AsSpan(0, firstHeader.Length).SequenceEqual(firstHeader)
                    && bytes.AsSpan(firstHeader.Length, firstBody.Length).SequenceEqual(firstBody),
                "the first UTF-8 payload was not written byte-for-byte with its UTF-8 length."
            );

            wire.Position = 0;
            using var firstMessage = await LspProtocol.ReadMessageAsync(wire);
            using var secondMessage = await LspProtocol.ReadMessageAsync(wire);
            Assert(
                firstMessage?.RootElement.GetProperty("result").GetProperty("hover").GetString()
                    == "café 日本語"
                    && secondMessage
                        ?.RootElement.GetProperty("result")
                        .GetProperty("diagnostic")
                        .GetString() == "naïve"
                    && wire.Position == wire.Length,
                "sequential non-ASCII protocol messages did not round-trip without framing residue."
            );
        }
        finally
        {
            Console.OutputEncoding = originalOutputEncoding;
        }
    }

    [TestMethod]
    public async Task ProtocolFramingRejectsAmbiguousOversizedAndTruncatedMessages()
    {
        async Task ReadAndDisposeAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            using var message = await LspProtocol.ReadMessageAsync(stream, cancellationToken);
        }

        await ExpectExceptionAsync<InvalidOperationException>(() =>
            ReadAndDisposeAsync(
                new MemoryStream(
                    Encoding.ASCII.GetBytes(new string('x', LspProtocol.MaxHeaderBytes))
                )
            )
        );
        await ExpectExceptionAsync<InvalidOperationException>(() =>
            ReadAndDisposeAsync(
                new MemoryStream(
                    Encoding.ASCII.GetBytes("Content-Length: 2\r\nContent-Length: 2\r\n\r\n{}")
                )
            )
        );
        await ExpectExceptionAsync<InvalidOperationException>(() =>
            ReadAndDisposeAsync(
                new MemoryStream(Encoding.ASCII.GetBytes("Content-Length: invalid\r\n\r\n"))
            )
        );
        await ExpectExceptionAsync<InvalidOperationException>(() =>
            ReadAndDisposeAsync(
                new MemoryStream(
                    Encoding.ASCII.GetBytes(
                        $"Content-Length: {LspProtocol.MaxBodyBytes + 1}\r\n\r\n"
                    )
                )
            )
        );
        await ExpectExceptionAsync<EndOfStreamException>(() =>
            ReadAndDisposeAsync(new MemoryStream(Encoding.ASCII.GetBytes("Content-Length: 2\r\n")))
        );
        await ExpectExceptionAsync<EndOfStreamException>(() =>
            ReadAndDisposeAsync(
                new MemoryStream(Encoding.ASCII.GetBytes("Content-Length: 2\r\n\r\na"))
            )
        );
        await ExpectExceptionAsync<OperationCanceledException>(() =>
            ReadAndDisposeAsync(new MemoryStream(), new CancellationToken(canceled: true))
        );
    }

    [TestMethod]
    public async Task StatefulDeclarationsExposeAuthoredEditorInformation()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-stateful-lsp-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "Stateful.csproj");
            var path = Path.Combine(root, "Counter.lui");
            await File.WriteAllTextAsync(
                project,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><LangVersion>preview</LangVersion></PropertyGroup><ItemGroup>{CoreMetadataReference}<AdditionalFiles Include=\"Counter.lui\"/></ItemGroup></Project>"
            );
            const string source = """
namespace StatefulEditor;
public component Counter() {
    int count = 0;
    string label = count.ToString();
    string stable = BuildLabel();
    string BuildLabel() => count.ToString();
    void Increment() { count++; }
    Setup(owner) { var local = "setup"; _ = local.Length; }
    <Column><Button onInvoke={Increment}>{label}</Button><Text>{stable}</Text></Column>
}
""";
            await File.WriteAllTextAsync(path, source);
            var uri = new Uri(path);
            using var context = await LuiProjectContext.LoadAsync(project, CancellationToken.None);
            var use = source.LastIndexOf("{label}", StringComparison.Ordinal) + 1;
            var hover = await context.HoverAsync(uri, use, CancellationToken.None);
            Assert(
                hover is not null
                    && hover.Value.Contains("label", StringComparison.Ordinal)
                    && (
                        hover.Documentation?.Contains("derived", StringComparison.OrdinalIgnoreCase)
                        ?? false
                    ),
                "Derived field hover did not explain its authored semantics: "
                    + hover?.Value
                    + " / "
                    + hover?.Documentation
            );
            var stableUse = source.IndexOf("{stable}", StringComparison.Ordinal) + 1;
            var stableHover = await context.HoverAsync(uri, stableUse, CancellationToken.None);
            Assert(
                stableHover is not null
                    && stableHover.Documentation is not null
                    && stableHover.Documentation.Contains(
                        "no direct component-state reads were identified",
                        StringComparison.Ordinal
                    )
                    && stableHover.Documentation.Contains(
                        "Runtime reads, including those made by helpers",
                        StringComparison.Ordinal
                    )
                    && !stableHover.Documentation.Contains(
                        "unchanged after mount",
                        StringComparison.Ordinal
                    ),
                "Derived hover overstated static dependency analysis: " + stableHover?.Documentation
            );
            var definition = await context.DefinitionAsync(uri, use, CancellationToken.None);
            Assert(
                definition is not null
                    && definition.Uri == uri
                    && definition.Span.Start == source.IndexOf("label =", StringComparison.Ordinal),
                "Derived field navigation did not return its authored declaration."
            );
            var symbols = await context.DocumentSymbolsAsync(uri, CancellationToken.None);
            var names = symbols!
                .Single(symbol => symbol.Name == "Counter")
                .Children.Select(child => child.Name)
                .ToArray();
            Assert(
                names.Contains("count")
                    && names.Contains("label")
                    && names.Contains("Increment")
                    && names.Contains("Setup"),
                "Stateful outline omitted authored declarations."
            );
            var completions = await context.CompletionsAsync(
                uri,
                source.IndexOf("local.Length", StringComparison.Ordinal) + "local.".Length,
                CancellationToken.None
            );
            Assert(
                completions.Any(item => item.Label == "Length"),
                "Setup local completion lost C# type information."
            );
            var tokens = await context.SemanticTokensAsync(uri, CancellationToken.None);
            Assert(tokens is { Length: > 0 }, "Stateful source has no semantic highlighting.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ExpressionCompletionHidesGeneratedHelpersButKeepsAuthoredNames()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-generated-completion-lsp-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "Completion.csproj");
            var path = Path.Combine(root, "Main.lui");
            await File.WriteAllTextAsync(
                project,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><LangVersion>preview</LangVersion></PropertyGroup><ItemGroup>{CoreMetadataReference}<AdditionalFiles Include=\"Main.lui\"/></ItemGroup></Project>"
            );
            const string source = """
namespace CompletionEditor;
public component Main() {
    string __luiUser = "user";
    <Text>{__lui}</Text>
}
""";
            await File.WriteAllTextAsync(path, source);
            var uri = new Uri(path);
            using var context = await LuiProjectContext.LoadAsync(project, CancellationToken.None);
            var offset = source.IndexOf("{__lui}", StringComparison.Ordinal) + 1 + "__lui".Length;
            var completions = await context.CompletionsAsync(uri, offset, CancellationToken.None);
            Assert(
                completions.Any(item => item.Label == "__luiUser")
                    && completions.All(item =>
                        !item.Label.StartsWith("__lui", StringComparison.Ordinal)
                        || item.Label == "__luiUser"
                    ),
                "expression completion leaked compiler helpers or dropped the authored __lui symbol: "
                    + string.Join(", ", completions.Select(item => item.Label))
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task StructureAndWhitespaceHoverAvoidWholeGraphCompilation()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-structure-hover-lsp-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "StructureHover.csproj");
            await File.WriteAllTextAsync(
                project,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><LangVersion>preview</LangVersion><RunAnalyzersDuringBuild>false</RunAnalyzersDuringBuild></PropertyGroup><ItemGroup>{CoreMetadataReference}<AdditionalFiles Include=\"*.lui\"/></ItemGroup></Project>"
            );
            const string source = """
namespace HoverScale;
using Lucent.Core;
using static Lucent.Core.Components;
internal component Main(bool visible) {
    <Column>if (visible) { <Row style={Panel} /> }</Column>
}
style Panel { Opacity: .5f; }
""";
            var uri = new Uri(Path.Combine(root, "Main.lui"));
            await File.WriteAllTextAsync(uri.LocalPath, source);
            for (var index = 0; index < 9; index++)
                await File.WriteAllTextAsync(
                    Path.Combine(root, $"Sibling{index}.lui"),
                    $"namespace HoverScale; using Lucent.Core; using static Lucent.Core.Components; internal component Sibling{index}() {{ <Row /> }}"
                );

            using var context = await LuiProjectContext.LoadAsync(project, CancellationToken.None);
            foreach (
                var offset in new[]
                {
                    source.IndexOf("component", StringComparison.Ordinal),
                    source.IndexOf("if (", StringComparison.Ordinal),
                    source.IndexOf("style Panel", StringComparison.Ordinal),
                    source.IndexOf("<Column>", StringComparison.Ordinal) + "<Column>".Length,
                }
            )
                Assert(
                    await context.HoverAsync(uri, offset, CancellationToken.None) is null,
                    "Structure or whitespace unexpectedly produced hover content."
                );
            Assert(
                context.GraphCompilationCount == 0,
                "Structure or whitespace hover compiled the whole LUI project graph "
                    + context.GraphCompilationCount
                    + " times."
            );

            var componentHover = await context.HoverAsync(
                uri,
                source.IndexOf("Row style", StringComparison.Ordinal),
                CancellationToken.None
            );
            var styleHover = await context.HoverAsync(
                uri,
                source.IndexOf("Panel}", StringComparison.Ordinal),
                CancellationToken.None
            );
            Assert(
                componentHover is not null && styleHover is not null,
                "The fast structure path removed component or authored-style hover."
            );
            Assert(
                context.GraphCompilationCount == 0,
                "Ordinary LUI symbol hover unexpectedly compiled the project graph."
            );

            await ExpectExceptionAsync<OperationCanceledException>(() =>
                context.HoverAsync(
                    uri,
                    source.IndexOf("if (", StringComparison.Ordinal),
                    new CancellationToken(canceled: true)
                )
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task LiveTokenChoiceHasProjectDiagnosticsHoverAndNavigation()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-token-choice-lsp-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "TokenChoice.csproj");
            var helperPath = Path.Combine(root, "Helpers.cs");
            var luiPath = Path.Combine(root, "MenuButton.lui");
            await File.WriteAllTextAsync(
                project,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><LangVersion>preview</LangVersion></PropertyGroup><ItemGroup>{CoreMetadataReference}<AdditionalFiles Include=\"MenuButton.lui\"/></ItemGroup></Project>"
            );
            const string helper = """
namespace TokenChoice;
using Lucent.Core;
public static class Helpers
{
    public static readonly Token<FocusRing> KeyboardFocus = new("keyboard-focus", FocusRing.None);
}
""";
            const string lui = """
namespace TokenChoice;
using Lucent.Core;
public component MenuButton() {
    bool menuOpen = false;
    void OpenMenu() { menuOpen = true; }
    <Button onInvoke={OpenMenu} style={Style.Empty with {
        FocusRing: menuOpen ? Helpers.KeyboardFocus : FocusRing.None;
    }}>Menu</Button>
}
""";
            await File.WriteAllTextAsync(helperPath, helper);
            await File.WriteAllTextAsync(luiPath, lui);
            var uri = new Uri(luiPath);
            using var context = await LuiProjectContext.LoadAsync(project, CancellationToken.None);

            var diagnostics = await context.DiagnosticsAsync(uri, CancellationToken.None);
            Assert(
                diagnostics is { Count: 0 },
                "A live token/concrete style choice produced project diagnostics: "
                    + string.Join(
                        " | ",
                        diagnostics?.Select(item => item.Code + ":" + item.Message) ?? []
                    )
            );
            Assert(
                await context.CompileAsync(uri, CancellationToken.None) is not null,
                "The clean live token-choice document did not compile."
            );
            var tokenUse = lui.IndexOf("KeyboardFocus", StringComparison.Ordinal);
            var tokenDeclaration = helper.IndexOf("KeyboardFocus", StringComparison.Ordinal);
            var hover = await context.HoverAsync(uri, tokenUse, CancellationToken.None);
            var definition = await context.DefinitionAsync(uri, tokenUse, CancellationToken.None);
            Assert(
                hover is not null
                    && hover.Value.Contains("KeyboardFocus", StringComparison.Ordinal)
                    && hover.Value.Contains("Token", StringComparison.Ordinal),
                "The authored token branch lost typed hover information: " + hover?.Value
            );
            Assert(
                definition is not null
                    && definition.Uri == new Uri(helperPath)
                    && definition.Span.Start == tokenDeclaration,
                "The authored token branch did not navigate to its C# declaration: "
                    + definition?.Uri
                    + "@"
                    + definition?.Span.Start
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ProjectContextFormattingNavigationCompletionDiagnosticsAndProtocol()
    {
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
                "namespace Vendor.Deep { public class Marker {} } namespace Vendor.Deep.Child {} namespace Sample; using Lucent.Core; public static class Helpers {\n/// <summary>Formats <see cref=\"T:System.String\"/> for <paramref name=\"value\"/>.</summary>\n/// <remarks>Second section.</remarks>\npublic static string Format(int value) => value.ToString(); public static string AAAA(int value) => value.ToString(); public static ComponentRecipe UseCard() => Components.Card(\"\", \"\");\n}\npublic static class ImportedComponents { [LucentComponent] public static ComponentRecipe Choice(string first, int second = 42) => null!; [LucentComponent] public static ComponentRecipe Choice(int first) => null!; [LucentComponent] public static int ChoiceInvalid() => 1; }"
            );
            var source =
                "namespace Sample;\r\nusing Lucent.Core;\r\nusing static Sample.Components;\r\nusing static Sample.ImportedComponents;\r\nstyle WidgetStyle { Background: Brush.Solid(default); }\r\ninternal component Widget(int count) { <Card content={Helpers.Format(count)} name=\"widget\" /> }";
            var sibling =
                "namespace Sample;\r\nusing static Lucent.Core.Components;\r\ninternal component Card(string content, string name = \"\") { <Row /> }";
            await File.WriteAllTextAsync(sourcePath, source);
            await File.WriteAllTextAsync(siblingPath, sibling);
            var sourceUri = new Uri(sourcePath);

            var formatterSource =
                "// formatter comment\r\ninternal component Widget(int count) { <Row><Text>{count . ToString ( )}</Text>  exact  text </Row> }";
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

            using var context = await LuiProjectContext.LoadAsync(
                projectPath,
                CancellationToken.None
            );
            context.ReplaceText(sourceUri, formatterSource);
            var lspDocumentFormat = await context.FormatAsync(
                sourceUri,
                null,
                CancellationToken.None
            );
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
            var whitespaceHoverOffset =
                source.IndexOf("Helpers.Format", StringComparison.Ordinal) + "Helpers.".Length;
            var beforeWhitespaceHover = await context.HoverAsync(
                sourceUri,
                whitespaceHoverOffset,
                CancellationToken.None
            );
            Assert(beforeWhitespaceHover is not null, "The whitespace hover fixture did not bind.");
            context.ReplaceText(sourceUri, "\n  " + source);
            var afterWhitespaceHover = await context.HoverAsync(
                sourceUri,
                whitespaceHoverOffset + 3,
                CancellationToken.None
            );
            Assert(
                ReferenceEquals(beforeWhitespaceHover, afterWhitespaceHover),
                "A structural whitespace edit unnecessarily recalculated the hovered symbol."
            );
            context.ReplaceText(
                sourceUri,
                source.Replace("widget\"", "wid get\"", StringComparison.Ordinal)
            );
            var literalWhitespaceHover = await context.HoverAsync(
                sourceUri,
                whitespaceHoverOffset,
                CancellationToken.None
            );
            Assert(
                literalWhitespaceHover is not null
                    && !ReferenceEquals(afterWhitespaceHover, literalWhitespaceHover),
                "Whitespace inside a string literal incorrectly reused the tooltip."
            );
            context.ReplaceText(sourceUri, source);
            var originalHelperText = await File.ReadAllTextAsync(helperPath);
            context.ReplaceText(
                new Uri(helperPath),
                originalHelperText.Replace("Formats <see", "Updated <see", StringComparison.Ordinal)
            );
            var changedDocumentationHover = await context.HoverAsync(
                sourceUri,
                whitespaceHoverOffset,
                CancellationToken.None
            );
            Assert(
                changedDocumentationHover?.Documentation?.Contains(
                    "Updated",
                    StringComparison.Ordinal
                ) == true,
                "A C# documentation edit reused an obsolete tooltip."
            );
            context.ReplaceText(new Uri(helperPath), originalHelperText);
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
            var compiled =
                published ?? throw new InvalidOperationException("Missing compiled document.");
            var repeated = await context.CompileAsync(sourceUri, CancellationToken.None);
            Assert(
                ReferenceEquals(compiled.Result, repeated?.Result),
                "Unchanged editor operations did not reuse the same compilation result."
            );
            context.ReplaceText(sourceUri, source + "\n// cache invalidation");
            var edited = await context.CompileAsync(sourceUri, CancellationToken.None);
            Assert(
                edited is not null
                    && !ReferenceEquals(compiled.Result, edited.Result)
                    && edited.Result.Identity.DocumentVersion
                        == LuiDocumentIdentity.Hash(source + "\n// cache invalidation"),
                "An editor change reused an obsolete compilation result."
            );
            context.ReplaceText(sourceUri, source);
            compiled = (await context.CompileAsync(sourceUri, CancellationToken.None))!;

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
                System.Collections.Immutable.ImmutableArray.Create(
                    new LuiGenerator().AsSourceGenerator()
                ),
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
            Assert(
                buildSource == compiled.GeneratedText,
                "build and LSP generation were not exact."
            );
            var declarationIdentities = compiled
                .Index.Declarations.Select(item => item.Identity)
                .ToArray();
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
            var declarationTarget = await context.NavigateAsync(
                sourceUri,
                card,
                CancellationToken.None
            );
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
                                    edit.Uri
                                    + ":"
                                    + string.Join(",", edit.Spans.Select(span => span.Start))
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
            var scalarBodySource = source.Replace(
                "<Card content={Helpers.Format(count)} name=\"widget\" />",
                "<Text>{Helpers.Format(count)}</Text>",
                StringComparison.Ordinal
            );
            context.ReplaceText(sourceUri, scalarBodySource);
            var scalarBodyPublished = await context.CompileAsync(sourceUri, CancellationToken.None);
            var scalarBodyHelper = scalarBodySource.IndexOf("Format", StringComparison.Ordinal);
            var scalarBodyHover = await context.HoverAsync(
                sourceUri,
                scalarBodyHelper,
                CancellationToken.None
            );
            var scalarBodyRename = await context.RenameAsync(
                sourceUri,
                scalarBodyHelper,
                "Render",
                CancellationToken.None
            );
            var scalarBodyReferences = await context.ReferencesAsync(
                new Uri(VsCodeUri(new Uri(helperPath))),
                helperDeclaration,
                true,
                CancellationToken.None
            );
            Assert(
                scalarBodyPublished is not null
                    && scalarBodyHover is not null
                    && scalarBodyRename is not null
                    && scalarBodyRename.Edits.Any(edit =>
                        edit.Uri == sourceUri
                        && edit.Spans.Any(span =>
                            span.Equals(new LuiSpan(scalarBodyHelper, "Format".Length))
                        )
                    )
                    && scalarBodyReferences is not null
                    && scalarBodyReferences.Locations.Any(location =>
                        location.Uri == sourceUri
                        && location.Span.Equals(new LuiSpan(scalarBodyHelper, "Format".Length))
                    ),
                "hover, rename, or references did not retain the exact scalar body-expression symbol span."
            );
            var incompleteScalarBody = scalarBodySource.Replace(
                "Helpers.Format(count)",
                "Helpers.",
                StringComparison.Ordinal
            );
            context.ReplaceText(sourceUri, incompleteScalarBody);
            var incompleteScalarPosition =
                incompleteScalarBody.IndexOf("Helpers.", StringComparison.Ordinal)
                + "Helpers.".Length;
            var incompleteScalarCompletions = await context.CompletionsAsync(
                sourceUri,
                incompleteScalarPosition,
                CancellationToken.None
            );
            Assert(
                incompleteScalarCompletions.Any(item => item.Label == "Format"),
                "incomplete scalar body-expression input lost Roslyn member completion."
            );
            var invalidScalarBody = scalarBodySource.Replace(
                "Helpers.Format(count)",
                "Helpers.Format(\"bad\")",
                StringComparison.Ordinal
            );
            context.ReplaceText(sourceUri, invalidScalarBody);
            var invalidScalarDiagnostics = await context.DiagnosticsAsync(
                sourceUri,
                CancellationToken.None
            );
            var invalidScalarSpan = new LuiSpan(
                invalidScalarBody.IndexOf("\"bad\"", StringComparison.Ordinal),
                "\"bad\"".Length
            );
            Assert(
                invalidScalarDiagnostics is not null
                    && invalidScalarDiagnostics.Any(diagnostic =>
                        diagnostic.Code == "LUI2000" && diagnostic.Span.Equals(invalidScalarSpan)
                    ),
                "scalar body-expression diagnostics did not retain the exact authored argument span."
            );
            context.ReplaceText(sourceUri, source);
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
            var componentReferences = await context.ReferencesAsync(
                sourceUri,
                card,
                true,
                CancellationToken.None
            );
            var componentReferencesWithoutDeclaration = await context.ReferencesAsync(
                sourceUri,
                card,
                false,
                CancellationToken.None
            );
            var content = source.IndexOf("content", StringComparison.Ordinal);
            var contentDeclaration = sibling.IndexOf("content", StringComparison.Ordinal);
            var propertyReferences = await context.ReferencesAsync(
                sourceUri,
                content,
                true,
                CancellationToken.None
            );
            var visualProperty = source.IndexOf("Background", StringComparison.Ordinal);
            var visualPropertyReferences = await context.ReferencesAsync(
                sourceUri,
                visualProperty,
                true,
                CancellationToken.None
            );
            var normalizedHelperUri = new Uri(VsCodeUri(new Uri(helperPath)));
            var expressionReferences = await context.ReferencesAsync(
                normalizedHelperUri,
                helperDeclaration,
                true,
                CancellationToken.None
            );
            Assert(
                componentReferences is not null
                    && componentReferences.Locations.Any(location =>
                        location.Uri == sourceUri
                        && location.Span.Equals(new LuiSpan(card, "Card".Length))
                    )
                    && componentReferences.Locations.Any(location =>
                        location.Uri == new Uri(siblingPath)
                        && location.Span.Equals(
                            new LuiSpan(cardDeclarationSpan.Start, "Card".Length)
                        )
                    )
                    && componentReferencesWithoutDeclaration is not null
                    && !componentReferencesWithoutDeclaration.Locations.Any(location =>
                        location.Uri == new Uri(siblingPath)
                        && location.Span.Start == cardDeclarationSpan.Start
                    )
                    && propertyReferences is not null
                    && propertyReferences.Locations.Any(location =>
                        location.Uri == sourceUri
                        && location.Span.Equals(new LuiSpan(content, "content".Length))
                    )
                    && propertyReferences.Locations.Any(location =>
                        location.Uri == new Uri(siblingPath)
                        && location.Span.Equals(new LuiSpan(contentDeclaration, "content".Length))
                    )
                    && visualPropertyReferences is not null
                    && visualPropertyReferences.Locations.Any(location =>
                        location.Uri == sourceUri
                        && location.Span.Equals(new LuiSpan(visualProperty, "Background".Length))
                    )
                    && expressionReferences is not null
                    && expressionReferences.Locations.Any(location =>
                        location.Uri == new Uri(helperPath)
                        && location.Span.Equals(new LuiSpan(helperDeclaration, "Format".Length))
                    )
                    && expressionReferences.Locations.Any(location =>
                        location.Uri == sourceUri
                        && location.Span.Equals(new LuiSpan(helper, "Format".Length))
                    )
                    && expressionReferences.Locations.All(location =>
                        location.Uri.Scheme != "lucent-lui"
                    ),
                "cross-language references did not return exact mapped component, property, and expression spans: component="
                    + (componentReferences is null ? "null" : "present")
                    + " componentWithoutDeclaration="
                    + (componentReferencesWithoutDeclaration is null ? "null" : "present")
                    + " property="
                    + (propertyReferences is null ? "null" : "present")
                    + " visual="
                    + (visualPropertyReferences is null ? "null" : "present")
                    + " expression="
                    + (expressionReferences is null ? "null" : "present")
            );
            var pairedSource = source.Replace(
                "<Card content={Helpers.Format(count)} name=\"widget\" />",
                "<Card content={Helpers.Format(count)} name=\"widget\"></Card>",
                StringComparison.Ordinal
            );
            context.ReplaceText(sourceUri, pairedSource);
            var pairedOpen = pairedSource.IndexOf("Card", StringComparison.Ordinal);
            var pairedClose = pairedSource.LastIndexOf("Card", StringComparison.Ordinal);
            var pairedReferences = await context.ReferencesAsync(
                sourceUri,
                pairedClose,
                true,
                CancellationToken.None
            );
            var pairedRename = await context.RenameAsync(
                sourceUri,
                pairedOpen,
                "Panel",
                CancellationToken.None
            );
            Assert(
                pairedReferences is not null
                    && pairedReferences.Locations.Count(location =>
                        location.Uri == sourceUri
                        && (location.Span.Start == pairedOpen || location.Span.Start == pairedClose)
                    ) == 2
                    && pairedRename is not null
                    && pairedRename
                        .Edits.Single(edit => edit.Uri == sourceUri)
                        .Spans.Count(span => span.Start == pairedOpen || span.Start == pairedClose)
                        == 2,
                "paired tag references or rename omitted an authored tag name."
            );
            var pairedWithUnrelatedMap = pairedSource + "\r\n// Card";
            context.ReplaceText(sourceUri, pairedWithUnrelatedMap);
            var unrelatedCard = pairedWithUnrelatedMap.LastIndexOf(
                "Card",
                StringComparison.Ordinal
            );
            Func<LuiCompilationResult, LuiCompilationResult> unrelatedPairedMap = result =>
            {
                if (result.Identity.Document.LogicalPath != "nested/screens/Widget.lui")
                    return result;
                var token = result
                    .Map.FromSource(new LuiSpan(pairedOpen, 0))
                    .Where(entry => !entry.Hidden && entry.Kind == LuiMapKind.Symbol)
                    .Select(entry => entry.Generated)
                    .First();
                return WithMap(
                    result,
                    result.Map.Entries.Append(
                        new LuiMapEntry(
                            new LuiSpan(unrelatedCard, "Card".Length),
                            token,
                            LuiMapKind.Symbol,
                            false
                        )
                    )
                );
            };
            Assert(
                await context.ReferencesAsync(
                    sourceUri,
                    pairedOpen,
                    true,
                    CancellationToken.None,
                    transformGenerated: unrelatedPairedMap
                )
                    is null
                    && await context.RenameAsync(
                        sourceUri,
                        pairedOpen,
                        "Panel",
                        CancellationToken.None,
                        transformGenerated: unrelatedPairedMap
                    )
                        is null,
                "an unrelated disjoint same-symbol source-map candidate was accepted for rename."
            );
            var localSource = source.Replace(
                "<Card content={Helpers.Format(count)} name=\"widget\" />",
                "<Row>foreach (var item in new[] { count }) keyed by item { <Card content={item.ToString()} /> }</Row>",
                StringComparison.Ordinal
            );
            context.ReplaceText(sourceUri, localSource);
            var localDeclaration = localSource.IndexOf("item in", StringComparison.Ordinal);
            var localKey = localSource.IndexOf("by item", StringComparison.Ordinal) + "by ".Length;
            var localBody = localSource.LastIndexOf("item.ToString", StringComparison.Ordinal);
            var localRename = await context.RenameAsync(
                sourceUri,
                localKey,
                "value",
                CancellationToken.None
            );
            var localReferences = await context.ReferencesAsync(
                sourceUri,
                localBody,
                true,
                CancellationToken.None
            );
            var localHover = await context.HoverAsync(
                sourceUri,
                localDeclaration,
                CancellationToken.None
            );
            var localDefinition = await context.DefinitionAsync(
                sourceUri,
                localBody,
                CancellationToken.None
            );
            var localBodyHover = await context.HoverAsync(
                sourceUri,
                localBody,
                CancellationToken.None
            );
            var localBodyMembers = await context.CompletionsAsync(
                sourceUri,
                localBody + "item.".Length,
                CancellationToken.None
            );
            Assert(
                localBodyHover is not null
                    && !localBodyHover.Value.Contains("CurrentItem", StringComparison.Ordinal)
                    && localBodyMembers.Any(item => item.Label == "CompareTo"),
                "Retained foreach tooling exposed the reader instead of the authored item type."
            );
            Assert(
                localRename is not null
                    && localRename.Edits.Single(edit => edit.Uri == sourceUri).Spans.Count == 3
                    && localReferences is not null
                    && localReferences.Locations.Count(location =>
                        location.Uri == sourceUri
                        && (
                            location.Span.Start == localDeclaration
                            || location.Span.Start == localKey
                            || location.Span.Start == localBody
                        )
                    ) == 3
                    && localHover is not null
                    && localDefinition is not null
                    && localDefinition.Uri == sourceUri
                    && localDefinition.Span.Start == localDeclaration,
                "keyed foreach local provenance did not join declaration, key, and body uses."
            );
            var renamedLocal = localSource;
            foreach (var span in localRename!.Edits.Single(edit => edit.Uri == sourceUri).Spans)
                renamedLocal = renamedLocal[..span.Start] + "value" + renamedLocal[span.End..];
            context.ReplaceText(sourceUri, renamedLocal);
            Assert(
                await context.CompileAsync(sourceUri, CancellationToken.None) is not null,
                "applying keyed foreach rename did not compile."
            );
            var patternSource = source.Replace(
                "<Card content={Helpers.Format(count)} name=\"widget\" />",
                "<Row>if (Helpers.Format(count) is { } detail) { <Card content={detail} /> }</Row>",
                StringComparison.Ordinal
            );
            context.ReplaceText(sourceUri, patternSource);
            var patternPublished = await context.CompileAsync(sourceUri, CancellationToken.None);
            Assert(
                patternPublished is not null,
                "Conditional local fixture did not compile: "
                    + string.Join(
                        " | ",
                        (await context.DiagnosticsAsync(sourceUri, CancellationToken.None))!.Select(
                            diagnostic => diagnostic.Code + ":" + diagnostic.Message
                        )
                    )
            );
            var patternDeclaration = patternSource.IndexOf("detail)", StringComparison.Ordinal);
            var patternUse = patternSource.LastIndexOf("detail}", StringComparison.Ordinal);
            var patternDefinition = await context.DefinitionAsync(
                sourceUri,
                patternUse,
                CancellationToken.None
            );
            var patternRename = await context.RenameAsync(
                sourceUri,
                patternUse,
                "currentDetail",
                CancellationToken.None
            );
            var patternHover = await context.HoverAsync(
                sourceUri,
                patternUse,
                CancellationToken.None
            );
            Assert(
                patternDefinition is not null
                    && patternDefinition.Uri == sourceUri
                    && patternDefinition.Span.Start == patternDeclaration
                    && patternRename is not null
                    && patternRename.Edits.Single(edit => edit.Uri == sourceUri).Spans.Count == 2
                    && patternHover is not null
                    && !patternHover.Value.Contains("CurrentItem", StringComparison.Ordinal),
                "Retained conditional pattern local lost its authored type or declaration provenance. "
                    + "definition="
                    + patternDefinition?.Span.Start
                    + " expected="
                    + patternDeclaration
                    + " rename="
                    + patternRename?.Edits.Count
                    + " hover="
                    + patternHover?.Value
            );
            var twoPatternSource = patternSource
                .Replace(
                    "is { } detail)",
                    "is { } detail && Helpers.Format(count) is { } other)",
                    StringComparison.Ordinal
                )
                .Replace("content={detail}", "content={detail + other}", StringComparison.Ordinal);
            context.ReplaceText(sourceUri, twoPatternSource);
            var firstPatternDeclaration = twoPatternSource.IndexOf(
                "detail &&",
                StringComparison.Ordinal
            );
            var otherPatternDeclaration = twoPatternSource.IndexOf(
                "other)",
                StringComparison.Ordinal
            );
            var firstPatternUse = twoPatternSource.IndexOf("detail +", StringComparison.Ordinal);
            Func<LuiCompilationResult, LuiCompilationResult> crosswiredPatternMap = result =>
            {
                if (result.Identity.Document.LogicalPath != "nested/screens/Widget.lui")
                    return result;
                return WithMap(
                    result,
                    result.Map.Entries.Select(entry =>
                        entry.Kind == LuiMapKind.Local
                        && entry.Source.Start == firstPatternDeclaration
                        && result
                            .Source!.Substring(entry.Generated.Start, entry.Generated.Length)
                            .StartsWith("__luiLocal", StringComparison.Ordinal)
                            ? new LuiMapEntry(
                                new LuiSpan(otherPatternDeclaration, "other".Length),
                                entry.Generated,
                                entry.Kind,
                                entry.Hidden
                            )
                            : entry
                    )
                );
            };
            Assert(
                await context.RenameAsync(
                    sourceUri,
                    firstPatternUse,
                    "renamed",
                    CancellationToken.None,
                    transformGenerated: crosswiredPatternMap
                )
                    is null
                    && await context.ReferencesAsync(
                        sourceUri,
                        firstPatternUse,
                        true,
                        CancellationToken.None,
                        transformGenerated: crosswiredPatternMap
                    )
                        is null,
                "A cross-wired retained alias map joined distinct authored pattern locals."
            );
            var sameOffsetOne =
                "namespace Sample; using static Lucent.Core.Components; internal component One(int count) { <Row>foreach (var item in new[] { count }) keyed by item { <Text content={item.ToString()} /> }</Row> }";
            var sameOffsetTwo = sameOffsetOne.Replace(
                "One(int",
                "Two(int",
                StringComparison.Ordinal
            );
            context.ReplaceText(sourceUri, sameOffsetOne);
            context.ReplaceText(new Uri(siblingPath), sameOffsetTwo);
            var sameOffset = sameOffsetOne.IndexOf("item in", StringComparison.Ordinal);
            var sameOffsetRename = await context.RenameAsync(
                sourceUri,
                sameOffset,
                "value",
                CancellationToken.None
            );
            var sameOffsetReferences = await context.ReferencesAsync(
                sourceUri,
                sameOffset,
                true,
                CancellationToken.None
            );
            Assert(
                sameOffsetRename is not null
                    && sameOffsetRename.Edits.Count == 1
                    && sameOffsetRename.Edits.Single().Uri == sourceUri
                    && sameOffsetReferences is not null
                    && sameOffsetReferences.Locations.All(location => location.Uri == sourceUri),
                "equal local offsets from separate .lui documents were aliased."
            );
            context.ReplaceText(sourceUri, source);
            context.Close(new Uri(siblingPath));
            var renameReady = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
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
            var referencesReady = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var referencesRelease = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var staleReferences = context.ReferencesAsync(
                sourceUri,
                helper,
                true,
                CancellationToken.None,
                async () =>
                {
                    referencesReady.SetResult();
                    await referencesRelease.Task;
                }
            );
            await referencesReady.Task;
            context.ReplaceText(
                sourceUri,
                source.Replace("Format(count)", "Format(count + 1)", StringComparison.Ordinal)
            );
            referencesRelease.SetResult();
            Assert(await staleReferences is null, "stale references returned a partial result.");
            context.ReplaceText(sourceUri, source);
            Func<LuiCompilationResult, LuiCompilationResult> staleIdentity = result =>
                result.Identity.Document.LogicalPath == "nested/screens/Widget.lui"
                    ? WithIdentity(result, result.Identity.DocumentVersion + "-stale")
                    : result;
            var staleIdentityReferences = await context.ReferencesAsync(
                sourceUri,
                helper,
                true,
                CancellationToken.None,
                transformGenerated: staleIdentity
            );
            var staleIdentityRename = await context.RenameAsync(
                sourceUri,
                helper,
                "Identity",
                CancellationToken.None,
                transformGenerated: staleIdentity
            );
            var staleIdentityPrepareRename = await context.PrepareRenameAsync(
                sourceUri,
                helper,
                CancellationToken.None,
                staleIdentity
            );
            Assert(
                staleIdentityReferences is null
                    && staleIdentityRename is null
                    && staleIdentityPrepareRename is null,
                "a non-current generated identity published references or rename without an epoch change."
            );
            Func<LuiCompilationResult, LuiCompilationResult> ambiguousMap = result =>
            {
                if (result.Identity.Document.LogicalPath != "nested/screens/Widget.lui")
                    return result;
                var entries = result.Map.Entries.ToList();
                entries.Add(
                    new LuiMapEntry(
                        new LuiSpan(card, "Card".Length),
                        GeneratedTokenSpan(result, helper, "Format"),
                        LuiMapKind.Symbol,
                        false
                    )
                );
                return WithMap(result, entries);
            };
            Func<LuiCompilationResult, LuiCompilationResult> unmappableMap = result =>
            {
                if (result.Identity.Document.LogicalPath != "nested/screens/Widget.lui")
                    return result;
                var entries = result.Map.Entries.Append(
                    new LuiMapEntry(
                        new LuiSpan(card, "Card".Length),
                        new LuiSpan(result.ProjectionSource!.IndexOf('('), 1),
                        LuiMapKind.Symbol,
                        false
                    )
                );
                return WithMap(result, entries);
            };
            Assert(
                await context.ReferencesAsync(
                    sourceUri,
                    card,
                    true,
                    CancellationToken.None,
                    transformGenerated: ambiguousMap
                )
                    is null
                    && await context.ReferencesAsync(
                        sourceUri,
                        card,
                        true,
                        CancellationToken.None,
                        transformGenerated: unmappableMap
                    )
                        is null,
                "ambiguous or unmappable source-map targets returned references."
            );
            var overlapSource =
                source
                    .Replace(
                        "Helpers.Format(count)",
                        "Helpers.AAAA(count)",
                        StringComparison.Ordinal
                    )
                    .Replace(
                        "name=\"widget\"",
                        "name={Helpers.AAAA(count)}",
                        StringComparison.Ordinal
                    ) + "\r\n// AAAAA";
            var firstAaaa =
                overlapSource.IndexOf("Helpers.AAAA", StringComparison.Ordinal) + "Helpers.".Length;
            var secondAaaa =
                overlapSource.IndexOf("Helpers.AAAA", firstAaaa + 1, StringComparison.Ordinal)
                + "Helpers.".Length;
            var overlapComment = overlapSource.LastIndexOf("AAAAA", StringComparison.Ordinal);
            context.ReplaceText(sourceUri, overlapSource);
            Func<LuiCompilationResult, LuiCompilationResult> overlappingMap = result =>
            {
                if (result.Identity.Document.LogicalPath != "nested/screens/Widget.lui")
                    return result;
                var firstToken = GeneratedTokenSpan(result, firstAaaa, "AAAA");
                var secondToken = GeneratedTokenSpan(result, secondAaaa, "AAAA");
                var entries = result
                    .Map.Entries.Where(entry =>
                        !Intersects(entry.Generated, firstToken)
                        && !Intersects(entry.Generated, secondToken)
                    )
                    .Append(
                        new LuiMapEntry(
                            new LuiSpan(overlapComment, "AAAA".Length),
                            firstToken,
                            LuiMapKind.Symbol,
                            false
                        )
                    )
                    .Append(
                        new LuiMapEntry(
                            new LuiSpan(overlapComment + 1, "AAAA".Length),
                            secondToken,
                            LuiMapKind.Symbol,
                            false
                        )
                    );
                return WithMap(result, entries);
            };
            Assert(
                await context.ReferencesAsync(
                    sourceUri,
                    overlapComment,
                    true,
                    CancellationToken.None,
                    transformGenerated: overlappingMap
                )
                    is null,
                "overlapping mapped references returned a partial result."
            );
            context.ReplaceText(sourceUri, source);
            using (
                var foreign = await LuiProjectContext.LoadAsync(projectPath, CancellationToken.None)
            )
                Assert(
                    await foreign.NavigateAsync(
                        compiled.GeneratedUri,
                        generatedEntry.Generated.Start,
                        CancellationToken.None
                    )
                        is null,
                    "a generated URI from a foreign context was accepted."
                );
            var disposalContext = await LuiProjectContext.LoadAsync(
                projectPath,
                CancellationToken.None
            );
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
            Assert(
                await disposingNavigation is null,
                "in-flight disposed navigation was published."
            );
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
            Assert(
                await replacingNavigation is null,
                "in-flight replaced navigation was published."
            );
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
                    && choiceTagCompletions.All(item => item.Label != "ChoiceInvalid")
                    && choiceParameters.Any(item => item.Label == "first")
                    && choiceParameters.Any(item => item.Label == "second"),
                "Component completion lost eligible candidates/parameters or included an invalid return type."
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
            var malformedAttribute = source.Replace(
                "name=\"widget\"",
                "name=",
                StringComparison.Ordinal
            );
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
            var incompleteName = incompleteChoice.Replace(
                "<Choice",
                "<Cho",
                StringComparison.Ordinal
            );
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
                    capabilities
                        .GetProperty("renameProvider")
                        .GetProperty("prepareProvider")
                        .GetBoolean()
                        && capabilities.GetProperty("referencesProvider").GetBoolean()
                        && capabilities.GetProperty("documentFormattingProvider").GetBoolean()
                        && capabilities.GetProperty("documentRangeFormattingProvider").GetBoolean(),
                    "LSP did not advertise rename, references, and formatter providers."
                );
                await lsp.NotifyAsync("initialized", new { });
                using var lspComponentReferences = await lsp.RequestAsync(
                    "textDocument/references",
                    new
                    {
                        textDocument = new { uri = lspSourceUri },
                        position = new
                        {
                            line = rowPosition.Line,
                            character = rowPosition.Character,
                        },
                        context = new { includeDeclaration = false },
                    }
                );
                var lspHelperText = await File.ReadAllTextAsync(helperPath);
                var lspHelperDeclaration = Position(lspHelperText, helperDeclaration);
                using var lspExpressionReferences = await lsp.RequestAsync(
                    "textDocument/references",
                    new
                    {
                        textDocument = new { uri = VsCodeUri(new Uri(helperPath)) },
                        position = new
                        {
                            line = lspHelperDeclaration.Line,
                            character = lspHelperDeclaration.Character,
                        },
                        context = new { includeDeclaration = true },
                    }
                );
                var lspComponentLocations = lspComponentReferences
                    .RootElement.GetProperty("result")
                    .EnumerateArray()
                    .ToArray();
                var lspExpressionLocations = lspExpressionReferences
                    .RootElement.GetProperty("result")
                    .EnumerateArray()
                    .ToArray();
                Assert(
                    lspComponentLocations.Any(location =>
                        location.GetProperty("uri").GetString() == sourceUri.AbsoluteUri
                        && location
                            .GetProperty("range")
                            .GetProperty("start")
                            .GetProperty("line")
                            .GetInt32() == rowPosition.Line
                        && location
                            .GetProperty("range")
                            .GetProperty("start")
                            .GetProperty("character")
                            .GetInt32() == rowPosition.Character
                    )
                        && !lspComponentLocations.Any(location =>
                            location.GetProperty("uri").GetString()
                            == new Uri(siblingPath).AbsoluteUri
                        )
                        && lspExpressionLocations.Any(location =>
                            location.GetProperty("uri").GetString()
                                == new Uri(helperPath).AbsoluteUri
                            && location
                                .GetProperty("range")
                                .GetProperty("start")
                                .GetProperty("character")
                                .GetInt32() == lspHelperDeclaration.Character
                        )
                        && lspExpressionLocations.Any(location =>
                            location.GetProperty("uri").GetString() == sourceUri.AbsoluteUri
                            && location
                                .GetProperty("range")
                                .GetProperty("start")
                                .GetProperty("line")
                                .GetInt32() == Position(source, helper).Line
                            && location
                                .GetProperty("range")
                                .GetProperty("start")
                                .GetProperty("character")
                                .GetInt32() == Position(source, helper).Character
                        )
                        && lspExpressionLocations.All(location =>
                            location
                                .GetProperty("uri")
                                .GetString()!
                                .StartsWith("file:", StringComparison.Ordinal)
                        ),
                    "LSP references did not preserve includeDeclaration, C# URI normalization, exact ranges, or source-only locations."
                );
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
                                end = new
                                {
                                    line = componentEnd.Line,
                                    character = componentEnd.Character,
                                },
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
                    new
                    {
                        initializationOptions = new
                        {
                            projectUri = new Uri(projectPath).AbsoluteUri,
                        },
                    }
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
                        position = new
                        {
                            line = rowPosition.Line,
                            character = rowPosition.Character,
                        },
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
                        position = new
                        {
                            line = rowPosition.Line,
                            character = rowPosition.Character,
                        },
                    }
                );
                Assert(
                    hover.RootElement.GetProperty("result").GetProperty("contents").GetArrayLength()
                        != 0,
                    "component hover was advertised but returned no Roslyn symbol information."
                );
                var helperPosition = Position(source, helper);
                var literalPosition = Position(
                    source,
                    source.IndexOf("widget", StringComparison.Ordinal)
                );
                using var helperHover = await lsp.RequestAsync(
                    "textDocument/hover",
                    new
                    {
                        textDocument = new { uri = lspSourceUri },
                        position = new
                        {
                            line = helperPosition.Line,
                            character = helperPosition.Character,
                        },
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
                        position = new
                        {
                            line = helperPosition.Line,
                            character = helperPosition.Character,
                        },
                    }
                );
                Assert(
                    helperDefinition
                        .RootElement.GetProperty("result")
                        .GetProperty("uri")
                        .GetString() == new Uri(helperPath).AbsoluteUri,
                    "expression-island definition did not navigate to the real C# symbol."
                );
                var helperText = await File.ReadAllTextAsync(helperPath);
                var csharpCard = helperText.IndexOf("Card(\"\", \"\")", StringComparison.Ordinal);
                var csharpFormat = helperText.IndexOf("Format", StringComparison.Ordinal);
                var csharpOnly = helperText.IndexOf("AAAA", StringComparison.Ordinal);
                var csharpCardPosition = Position(helperText, csharpCard);
                var csharpFormatPosition = Position(helperText, csharpFormat);
                var csharpOnlyPosition = Position(helperText, csharpOnly);
                using var csharpLuiDefinition = await lsp.RequestAsync(
                    "textDocument/definition",
                    new
                    {
                        textDocument = new { uri = VsCodeUri(new Uri(helperPath)) },
                        position = new
                        {
                            line = csharpCardPosition.Line,
                            character = csharpCardPosition.Character,
                        },
                    }
                );
                using var ordinaryCsharpDefinition = await lsp.RequestAsync(
                    "textDocument/definition",
                    new
                    {
                        textDocument = new { uri = VsCodeUri(new Uri(helperPath)) },
                        position = new
                        {
                            line = csharpFormatPosition.Line,
                            character = csharpFormatPosition.Character,
                        },
                    }
                );
                Assert(
                    csharpLuiDefinition
                        .RootElement.GetProperty("result")
                        .GetProperty("uri")
                        .GetString() == new Uri(siblingPath).AbsoluteUri
                        && ordinaryCsharpDefinition.RootElement.GetProperty("result").ValueKind
                            == JsonValueKind.Null,
                    "C# definition did not route only participating .lui symbols to Lucent."
                );
                using var ordinaryCsharpPrepareRename = await lsp.RequestAsync(
                    "textDocument/prepareRename",
                    new
                    {
                        textDocument = new { uri = VsCodeUri(new Uri(helperPath)) },
                        position = new
                        {
                            line = csharpOnlyPosition.Line,
                            character = csharpOnlyPosition.Character,
                        },
                    }
                );
                using var ordinaryCsharpReferences = await lsp.RequestAsync(
                    "textDocument/references",
                    new
                    {
                        textDocument = new { uri = VsCodeUri(new Uri(helperPath)) },
                        position = new
                        {
                            line = csharpOnlyPosition.Line,
                            character = csharpOnlyPosition.Character,
                        },
                        context = new { includeDeclaration = true },
                    }
                );
                using var ordinaryCsharpRename = await lsp.RequestAsync(
                    "textDocument/rename",
                    new
                    {
                        textDocument = new { uri = VsCodeUri(new Uri(helperPath)) },
                        position = new
                        {
                            line = csharpOnlyPosition.Line,
                            character = csharpOnlyPosition.Character,
                        },
                        newName = "BBBB",
                    }
                );
                Assert(
                    ordinaryCsharpPrepareRename.RootElement.GetProperty("result").ValueKind
                        == JsonValueKind.Null
                        && ordinaryCsharpReferences.RootElement.GetProperty("result").ValueKind
                            == JsonValueKind.Null
                        && ordinaryCsharpRename.RootElement.GetProperty("result").ValueKind
                            == JsonValueKind.Null,
                    "Lucent claimed rename/references for an ordinary C#-only symbol."
                );
                using var signature = await lsp.RequestAsync(
                    "textDocument/signatureHelp",
                    new
                    {
                        textDocument = new { uri = lspSourceUri },
                        position = new
                        {
                            line = rowPosition.Line,
                            character = rowPosition.Character,
                        },
                    }
                );
                Assert(
                    signature
                        .RootElement.GetProperty("result")
                        .GetProperty("signatures")
                        .GetArrayLength() != 0,
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
                        position = new
                        {
                            line = rowPosition.Line,
                            character = rowPosition.Character,
                        },
                    }
                );
                var declarationLocation = generatedDefinition.RootElement.GetProperty("result");
                Assert(
                    declarationLocation.GetProperty("uri").GetString()
                        == new Uri(siblingPath).AbsoluteUri,
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
                            line = Position(
                                compiled.GeneratedText,
                                generatedEntry.Generated.Start
                            ).Line,
                            character = Position(
                                compiled.GeneratedText,
                                generatedEntry.Generated.Start
                            ).Character,
                        },
                    }
                );
                Assert(
                    sourceDefinition
                        .RootElement.GetProperty("result")
                        .GetProperty("uri")
                        .GetString() == lspSourceUri,
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
                for (var version = 2; version <= 11; version++)
                {
                    await lsp.NotifyAsync(
                        "textDocument/didChange",
                        new
                        {
                            textDocument = new { uri = lspSourceUri, version },
                            contentChanges = new[]
                            {
                                new
                                {
                                    text = source.Replace(
                                        "Card",
                                        "Missing",
                                        StringComparison.Ordinal
                                    ),
                                },
                            },
                        }
                    );
                }
                using var idleDiagnostics = await lsp.WaitForNotificationAsync(
                    "textDocument/publishDiagnostics"
                );
                Assert(
                    idleDiagnostics
                        .RootElement.GetProperty("params")
                        .GetProperty("version")
                        .GetInt32() == 11,
                    "Idle diagnostics did not publish the latest edit without a follow-up request."
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
                using var pushedDiagnostics = lsp.TakeNotification(
                    "textDocument/publishDiagnostics"
                );
                Assert(
                    pushedDiagnostics is not null
                        && pushedDiagnostics
                            .RootElement.GetProperty("params")
                            .GetProperty("version")
                            .GetInt32() == 11
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
                        position = new
                        {
                            line = rowPosition.Line,
                            character = rowPosition.Character,
                        },
                    }
                );
                Assert(
                    changedDefinition.RootElement.GetProperty("result").ValueKind
                        == JsonValueKind.Null,
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
                        position = new
                        {
                            line = rowPosition.Line,
                            character = rowPosition.Character,
                        },
                    }
                );
                Assert(
                    staleDefinition.RootElement.GetProperty("result").ValueKind
                        == JsonValueKind.Null,
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
                        position = new
                        {
                            line = rowPosition.Line,
                            character = rowPosition.Character,
                        },
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
                        && closeClear
                            .RootElement.GetProperty("params")
                            .GetProperty("uri")
                            .GetString() == lspSourceUri
                        && closeClear
                            .RootElement.GetProperty("params")
                            .GetProperty("version")
                            .GetInt32() == 11
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
                var lspProjectText = await File.ReadAllTextAsync(projectPath);
                var recoveryText = source.Replace("Widget", "Recovered", StringComparison.Ordinal);
                await lsp.NotifyAsync(
                    "textDocument/didOpen",
                    new
                    {
                        textDocument = new
                        {
                            uri = lspSourceUri,
                            version = 3,
                            text = source.Replace("Widget", "Opened", StringComparison.Ordinal),
                        },
                    }
                );
                // Await server reads before replacing the project file on Windows.
                using var beforeInvalidProject = await lsp.RequestAsync(
                    "textDocument/diagnostic",
                    new { textDocument = new { uri = lspSourceUri } }
                );
                await File.WriteAllTextAsync(projectPath, "not xml");
                await lsp.NotifyAsync(
                    "workspace/didChangeWatchedFiles",
                    new { changes = new[] { new { uri = projectUri, type = 2 } } }
                );
                await lsp.NotifyAsync(
                    "textDocument/didChange",
                    new
                    {
                        textDocument = new { uri = lspSourceUri, version = 4 },
                        contentChanges = new[] { new { text = recoveryText } },
                    }
                );
                using var afterInvalidProject = await lsp.RequestAsync(
                    "textDocument/diagnostic",
                    new { textDocument = new { uri = lspSourceUri } }
                );
                await File.WriteAllTextAsync(projectPath, lspProjectText);
                await lsp.NotifyAsync(
                    "workspace/didChangeWatchedFiles",
                    new { changes = new[] { new { uri = projectUri, type = 2 } } }
                );
                var recoveryPosition = Position(
                    recoveryText,
                    recoveryText.IndexOf("Card", StringComparison.Ordinal)
                );
                using var recoveredDefinition = await lsp.RequestAsync(
                    "textDocument/definition",
                    new
                    {
                        textDocument = new { uri = lspSourceUri },
                        position = new
                        {
                            line = recoveryPosition.Line,
                            character = recoveryPosition.Character,
                        },
                    }
                );
                Assert(
                    recoveredDefinition.RootElement.TryGetProperty("result", out var recoveryResult)
                        && recoveryResult.ValueKind == JsonValueKind.Object
                        && recoveryResult.GetProperty("uri").GetString()
                            == new Uri(siblingPath).AbsoluteUri,
                    "an open overlay was not resynchronized after a transient project reload failure: "
                        + recoveredDefinition.RootElement.GetRawText()
                );
                await lsp.NotifyAsync(
                    "textDocument/didClose",
                    new { textDocument = new { uri = lspSourceUri } }
                );
                await lsp.RequestAsync("shutdown", new { });
                using var afterShutdown = await lsp.RequestAsync(
                    "lucent/generatedText",
                    new { uri = generatedUri }
                );
                using var referencesAfterShutdown = await lsp.RequestAsync(
                    "textDocument/references",
                    new
                    {
                        textDocument = new { uri = lspSourceUri },
                        position = new
                        {
                            line = rowPosition.Line,
                            character = rowPosition.Character,
                        },
                        context = new { includeDeclaration = true },
                    }
                );
                Assert(
                    afterShutdown.RootElement.TryGetProperty("error", out _)
                        && referencesAfterShutdown.RootElement.TryGetProperty("error", out _),
                    "request or references after shutdown was accepted."
                );
                Assert(await lsp.ExitAsync() == 0, "normal shutdown/exit returned a failure code.");
            }
            context.Dispose();
            await AssertDisposedAsync(() =>
                context.CompileAsync(sourceUri, CancellationToken.None)
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task DeclaredDefaultContentSupportsForwardingAndParameterRename()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-content-lsp-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "Content.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><LangVersion>preview</LangVersion><Nullable>enable</Nullable></PropertyGroup><ItemGroup>{CoreMetadataReference}<AdditionalFiles Include=\"*.lui\" /></ItemGroup></Project>"
            );
            var shellUri = new Uri(Path.Combine(root, "Shell.lui"));
            var callerUri = new Uri(Path.Combine(root, "Caller.lui"));
            var shell =
                "namespace Content; using Lucent.Core; internal component Shell([DefaultContent] ComponentContent children) { <Column><Text>Header</Text>{children}<Text>Footer</Text></Column> }";
            var caller =
                "namespace Content; using Lucent.Core; internal component Caller(ComponentContent supplied) { <Column><Shell children={supplied} /><Shell><Text>Body</Text></Shell><Shell /></Column> }";
            await File.WriteAllTextAsync(shellUri.LocalPath, shell);
            await File.WriteAllTextAsync(callerUri.LocalPath, caller);
            using var context = await LuiProjectContext.LoadAsync(
                projectPath,
                CancellationToken.None
            );
            foreach (var uri in new[] { shellUri, callerUri })
                Assert(
                    await context.CompileAsync(uri, CancellationToken.None) is not null,
                    "Explicit default-content fixture did not compile: "
                        + string.Join(
                            " | ",
                            (await context.DiagnosticsAsync(uri, CancellationToken.None))!.Select(
                                diagnostic => diagnostic.Code + ":" + diagnostic.Message
                            )
                        )
                );
            var declaration = shell.IndexOf("children)", StringComparison.Ordinal);
            var forwarding = shell.IndexOf("children}", StringComparison.Ordinal);
            var argument = caller.IndexOf("children=", StringComparison.Ordinal);
            var definition = await context.DefinitionAsync(
                shellUri,
                forwarding,
                CancellationToken.None
            );
            var completions = await context.CompletionsAsync(
                callerUri,
                argument,
                CancellationToken.None
            );
            var references = await context.ReferencesAsync(
                shellUri,
                declaration,
                true,
                CancellationToken.None
            );
            var rename = await context.RenameAsync(
                shellUri,
                forwarding,
                "parts",
                CancellationToken.None
            );
            Assert(
                definition is not null
                    && definition.Uri == shellUri
                    && definition.Span.Start <= declaration
                    && definition.Span.End >= declaration + "children".Length,
                "Forwarded content did not navigate to its authored parameter."
            );
            Assert(
                completions.Any(item => item.Label == "children"),
                "Explicit content parameter was absent from attribute completion."
            );
            Assert(
                references is not null && references.Locations.Count == 3,
                "Content references must include declaration, forwarding read, and explicit argument only."
            );
            Assert(
                rename is not null && rename.Edits.Sum(edit => edit.Spans.Count) == 3,
                "Content parameter rename omitted authored uses or edited implicit child markup."
            );
            Func<LuiCompilationResult, LuiCompilationResult> hiddenExplicitArgument = result =>
                result.Identity.Document.LogicalPath.EndsWith(
                    "Caller.lui",
                    StringComparison.Ordinal
                )
                    ? WithMap(
                        result,
                        result.Map.Entries.Select(entry =>
                            entry.Source.Start == argument
                                ? new LuiMapEntry(entry.Source, entry.Generated, entry.Kind, true)
                                : entry
                        )
                    )
                    : result;
            Assert(
                await context.RenameAsync(
                    shellUri,
                    forwarding,
                    "parts",
                    CancellationToken.None,
                    transformGenerated: hiddenExplicitArgument
                )
                    is null,
                "An authored content argument with missing provenance was mistaken for implicit content."
            );
            Func<LuiCompilationResult, LuiCompilationResult> malformedImplicitArgument = result =>
                result.Identity.Document.LogicalPath.EndsWith(
                    "Caller.lui",
                    StringComparison.Ordinal
                )
                    ? WithMap(
                        result,
                        result.Map.Entries.Select(entry =>
                            entry.Hidden
                            && entry.Generated.Length == "children: ".Length
                            && result.ProjectionSource!.Substring(
                                entry.Generated.Start,
                                entry.Generated.Length
                            ) == "children: "
                                ? new LuiMapEntry(
                                    new LuiSpan(argument, "children".Length),
                                    entry.Generated,
                                    LuiMapKind.Symbol,
                                    true
                                )
                                : entry
                        )
                    )
                    : result;
            Assert(
                await context.RenameAsync(
                    shellUri,
                    forwarding,
                    "parts",
                    CancellationToken.None,
                    transformGenerated: malformedImplicitArgument
                )
                    is null,
                "A malformed source-backed implicit argument mapping was trusted as compiler scaffolding."
            );
            foreach (var edit in rename!.Edits)
            {
                var text = edit.Uri == shellUri ? shell : caller;
                foreach (var span in edit.Spans.OrderByDescending(span => span.Start))
                    text = text[..span.Start] + "parts" + text[span.End..];
                context.ReplaceText(edit.Uri, text);
            }
            foreach (var uri in new[] { shellUri, callerUri })
                Assert(
                    await context.CompileAsync(uri, CancellationToken.None) is not null,
                    "Renaming the explicit default-content parameter changed nested-content semantics."
                );
            context.ReplaceText(
                callerUri,
                caller.Replace(
                    "<Shell children={supplied} />",
                    "<Shell parts={supplied}><Text>Duplicate</Text></Shell>",
                    StringComparison.Ordinal
                )
            );
            Assert(
                (await context.DiagnosticsAsync(callerUri, CancellationToken.None))!.Any(
                    diagnostic => diagnostic.Code == "LUI2008"
                ),
                "Explicit attribute plus nested content lost the duplicate-content diagnostic."
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ParameterizedStylesExposeSymbolsCompletionAndHover()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-parameterized-style-lsp-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "ParameterizedStyle.csproj");
            var uri = new Uri(Path.Combine(root, "ParameterizedStyle.lui"));
            await File.WriteAllTextAsync(
                projectPath,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><LangVersion>preview</LangVersion><Nullable>enable</Nullable><RunAnalyzersDuringBuild>false</RunAnalyzersDuringBuild></PropertyGroup><ItemGroup>{CoreMetadataReference}<AdditionalFiles Include=\"*.lui\" /></ItemGroup></Project>"
            );
            var source = """
namespace Sample;
using Lucent.Core;
using static Lucent.Core.Components;
using static Lucent.Core.VisualProperties;
using static Lucent.Core.LayoutProperties;
internal component Example(float width) { <Row style={Workspace(width)} /> }
style Workspace(float width) {
    Width: width;
    when (width >= 820) { Opacity: .5f; }
}
""";
            await File.WriteAllTextAsync(uri.LocalPath, source);
            using var context = await LuiProjectContext.LoadAsync(
                projectPath,
                CancellationToken.None
            );
            var published = await context.CompileAsync(uri, CancellationToken.None);
            var symbols = await context.DocumentSymbolsAsync(uri, CancellationToken.None);
            var styleOffset = source.IndexOf("Workspace(width)", StringComparison.Ordinal);
            var completions = await context.CompletionsAsync(
                uri,
                styleOffset,
                CancellationToken.None
            );
            var hover = await context.HoverAsync(uri, styleOffset, CancellationToken.None);
            var style = symbols!.Single(symbol => symbol.Name == "Workspace");
            Assert(
                published is not null
                    && style.Children.Any(child => child.Name == "width")
                    && style.Children.Any(child => child.Name == "when (width >= 820)")
                    && completions.Any(item => item.Label == "Workspace" && item.Kind == 2)
                    && hover is not null,
                "parameterized style tooling lost compilation, declaration symbols, method completion, or hover information."
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ScrollBarPropertiesShareCompilerCompletionHoverAndDefinition()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-scrollbar-property-lsp-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var core = Path.GetFullPath("src/Lucent.Core/Lucent.Core.csproj");
            var projectPath = Path.Combine(root, "ScrollBarProperties.csproj");
            var uri = new Uri(Path.Combine(root, "ScrollBarProperties.lui"));
            await File.WriteAllTextAsync(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><LangVersion>preview</LangVersion><Nullable>enable</Nullable><RunAnalyzersDuringBuild>false</RunAnalyzersDuringBuild></PropertyGroup><ItemGroup><ProjectReference Include=\""
                    + core
                    + "\" /><AdditionalFiles Include=\"*.lui\" /></ItemGroup></Project>"
            );
            var source = """
namespace Sample;
using Lucent.Core;
using static Lucent.Core.Components;
internal component Example() { <Text style={ScrollStyle}>Scrollbar theme</Text> }
style ScrollStyle {
    Visibility: ScrollBarVisibility.Auto;
    ThumbBrush: Brush.Solid(Color.Parse("#123456"));
}
""";
            await File.WriteAllTextAsync(uri.LocalPath, source);
            using var context = await LuiProjectContext.LoadAsync(
                projectPath,
                CancellationToken.None
            );
            var compiled = await context.CompileAsync(uri, CancellationToken.None);
            var visibility = source.IndexOf("Visibility", StringComparison.Ordinal);
            var completions = await context.CompletionsAsync(
                uri,
                visibility,
                CancellationToken.None
            );
            var hover = await context.HoverAsync(uri, visibility, CancellationToken.None);
            var definition = await context.DefinitionAsync(uri, visibility, CancellationToken.None);
            var hoverValue = hover?.Value;
            var definitionPath = definition?.Uri.LocalPath;
            Assert(
                compiled is not null
                    && completions.Any(item =>
                        item.Label == "Visibility"
                        && item.Kind == 5
                        && item.Detail.Contains("Property", StringComparison.Ordinal)
                    )
                    && completions.Any(item => item.Label == "ThumbBrush" && item.Kind == 5)
                    && hoverValue?.Contains("ScrollBarVisibility", StringComparison.Ordinal) == true
                    && definitionPath is not null
                    && definitionPath.EndsWith(
                        Path.Combine(
                            "src",
                            "Lucent.Core",
                            "Components",
                            "Scrolling",
                            "ScrollBar.cs"
                        ),
                        StringComparison.OrdinalIgnoreCase
                    ),
                "ScrollBarProperties were not resolved consistently by compilation and editor tooling: "
                    + String.Join(
                        ", ",
                        completions.Select(item => item.Label + "/" + item.Kind + "/" + item.Detail)
                    )
                    + " hover="
                    + (hover?.Value ?? "null")
                    + " definition="
                    + (definition?.Uri.ToString() ?? "null")
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task TransitionPropertiesShareCompletionHoverDefinitionAndSymbols()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-transition-property-lsp-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var core = Path.GetFullPath("src/Lucent.Core/Lucent.Core.csproj");
            var projectPath = Path.Combine(root, "Transitions.csproj");
            var uri = new Uri(Path.Combine(root, "Transitions.lui"));
            await File.WriteAllTextAsync(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><LangVersion>preview</LangVersion><Nullable>enable</Nullable><RunAnalyzersDuringBuild>false</RunAnalyzersDuringBuild></PropertyGroup><ItemGroup><ProjectReference Include=\""
                    + core
                    + "\" /><AdditionalFiles Include=\"*.lui\" /></ItemGroup></Project>"
            );
            var source = """
namespace Sample;
using Lucent.Core;
using static Lucent.Core.Components;
internal component Example() { <Button style={MotionStyle}>Save</Button> }
style MotionStyle {
    transition Background: Motion.Quick;
    transition Opacity: Motion.Duration(120, Easing.EaseOut);
}
""";
            await File.WriteAllTextAsync(uri.LocalPath, source);
            using var context = await LuiProjectContext.LoadAsync(
                projectPath,
                CancellationToken.None
            );
            var compiled = await context.CompileAsync(uri, CancellationToken.None);
            var property = source.IndexOf("Background", StringComparison.Ordinal);
            var completions = await context.CompletionsAsync(uri, property, CancellationToken.None);
            var hover = await context.HoverAsync(uri, property, CancellationToken.None);
            var definition = await context.DefinitionAsync(uri, property, CancellationToken.None);
            var symbols = await context.DocumentSymbolsAsync(uri, CancellationToken.None);
            var style = symbols!.Single(symbol => symbol.Name == "MotionStyle");
            Assert(
                compiled is not null
                    && completions.Any(item => item.Label == "Background" && item.Kind == 5)
                    && completions.Any(item => item.Label == "Opacity" && item.Kind == 5)
                    && completions.Any(item => item.Label == "TextColor" && item.Kind == 5)
                    && !completions.Any(item => item.Label == "Width")
                    && hover?.Value.Contains("Property", StringComparison.Ordinal) == true
                    && definition?.Uri.LocalPath.EndsWith(
                        Path.Combine("src", "Lucent.Core", "LayoutScene.cs"),
                        StringComparison.OrdinalIgnoreCase
                    ) == true
                    && style.Children.Any(child => child.Name == "transition Background")
                    && style.Children.Any(child => child.Name == "transition Opacity"),
                "Transition property completion, hover, definition, or symbols were not shared across tooling: "
                    + String.Join(
                        ", ",
                        completions.Select(item => item.Label + "/" + item.Kind + "/" + item.Detail)
                    )
                    + " hover="
                    + (hover?.Value ?? "null")
                    + " definition="
                    + (definition?.Uri.ToString() ?? "null")
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task RealIssueBrowserProjectSupportsFormattingNavigationAndCompletion()
    {
        var expectedLayoutLocations = new HashSet<(Uri Uri, LuiSpan Span)>();
        var projectPath = Path.GetFullPath("apps/Lucent.IssueBrowser/Lucent.IssueBrowser.csproj");
        // MSBuildWorkspace evaluates the real project with its default Debug configuration.
        // Prepare that exact graph so project-reference analyzers resolve in a fresh checkout.
        await BuildProjectAsync(projectPath, "Debug");
        using (
            var issueBrowser = await LuiProjectContext.LoadAsync(
                projectPath,
                CancellationToken.None
            )
        )
        {
            var header = new Uri(Path.GetFullPath("apps/Lucent.IssueBrowser/Header.lui"));
            var headerText = await File.ReadAllTextAsync(header.LocalPath);
            var filterBar = headerText.IndexOf("FilterBar", StringComparison.Ordinal);
            var layoutTag = headerText.IndexOf("<Layout", StringComparison.Ordinal);
            var buttonAttribute = headerText.IndexOf("onInvoke", StringComparison.Ordinal);
            var styleDeclaration = headerText.IndexOf("    Axis:", StringComparison.Ordinal);
            Assert(
                styleDeclaration >= 0,
                "Header.lui no longer contains the authored HeaderStyle Axis declaration."
            );
            var styleProperty = styleDeclaration + "    ".Length;
            var styleReferenceMarker = headerText.IndexOf(
                "style={HeaderStyle}",
                StringComparison.Ordinal
            );
            Assert(
                styleReferenceMarker >= 0,
                "Header.lui no longer contains the authored HeaderStyle reference."
            );
            var styleReference = styleReferenceMarker + "style={".Length;
            Assert(
                await issueBrowser.NavigateAsync(header, filterBar, CancellationToken.None)
                    is not null,
                "evaluated Issue Browser Header.lui could not bind its FilterBar sibling."
            );
            var filterHelp = await issueBrowser.SignatureHelpAsync(
                header,
                headerText.IndexOf("browser={browser}", StringComparison.Ordinal),
                CancellationToken.None
            );
            var buttonHelp = await issueBrowser.SignatureHelpAsync(
                header,
                headerText.IndexOf("Density: comfortable", StringComparison.Ordinal),
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
            Assert(layoutTag >= 0, "Header.lui no longer contains an authored generic Layout tag.");
            var tagCompletions = await issueBrowser.CompletionsAsync(
                header,
                layoutTag + 1,
                CancellationToken.None
            );
            Assert(
                tagCompletions.Any(item => item.Label == "Layout")
                    && tagCompletions.Any(item => item.Label == "Row"),
                "Header.lui tag completion omitted evaluated Layout or Row component methods."
            );
            var issueBrowserRoot = new Uri(
                Path.GetFullPath("apps/Lucent.IssueBrowser/IssueBrowser.lui")
            );
            var issueBrowserRootText = await File.ReadAllTextAsync(issueBrowserRoot.LocalPath);
            var layout = issueBrowserRootText.IndexOf("<Layout", StringComparison.Ordinal);
            Assert(
                layout >= 0,
                "IssueBrowser.lui no longer contains the authored generic Layout root."
            );
            var layoutPosition = layout + 1;
            var layoutCompletions = await issueBrowser.CompletionsAsync(
                issueBrowserRoot,
                layoutPosition,
                CancellationToken.None
            );
            Assert(
                layoutCompletions.Any(item => item.Label == "Layout" && item.Kind == 2),
                "IssueBrowser.lui tag completion omitted the evaluated generic Layout component."
            );
            var layoutDeclarationPath = Path.GetFullPath(
                "src/Lucent.Core/Components/Layout/Layout.cs"
            );
            var coreComponentsText = await File.ReadAllTextAsync(layoutDeclarationPath);
            var layoutDeclaration = coreComponentsText.IndexOf(
                "ComponentRecipe Layout(",
                StringComparison.Ordinal
            );
            Assert(
                layoutDeclaration >= 0,
                "Core Layout.cs no longer contains the public Layout component declaration."
            );
            layoutDeclaration += "ComponentRecipe ".Length;
            var layoutReferences = await issueBrowser.ReferencesAsync(
                issueBrowserRoot,
                layoutPosition,
                true,
                CancellationToken.None
            );
            var layoutRename = await issueBrowser.RenameAsync(
                issueBrowserRoot,
                layoutPosition,
                "LayoutReplacement",
                CancellationToken.None
            );
            expectedLayoutLocations.Add(
                (new Uri(layoutDeclarationPath), new LuiSpan(layoutDeclaration, "Layout".Length))
            );
            foreach (
                var path in Directory
                    .GetFiles("apps/Lucent.IssueBrowser", "*.lui")
                    .Concat(
                        Directory.GetFiles("src/Lucent.Core", "*.lui", SearchOption.AllDirectories)
                    )
            )
            {
                var text = await File.ReadAllTextAsync(path);
                var start = 0;
                while ((start = text.IndexOf("Layout", start, StringComparison.Ordinal)) >= 0)
                {
                    var isOpeningTag = start > 0 && text[start - 1] == '<';
                    var isClosingTag =
                        start > 1 && text[start - 2] == '<' && text[start - 1] == '/';
                    var end = start + "Layout".Length;
                    var hasTagBoundary =
                        end == text.Length
                        || char.IsWhiteSpace(text[end])
                        || text[end] is '/' or '>';
                    if ((isOpeningTag || isClosingTag) && hasTagBoundary)
                        expectedLayoutLocations.Add(
                            (new Uri(Path.GetFullPath(path)), new LuiSpan(start, "Layout".Length))
                        );
                    start = end;
                }
            }
            // Component recipes are also composed in authored C#. Keep these call sites in the
            // exact expected set so adding a Core consumer does not weaken the rename contract.
            foreach (var sourceRoot in new[] { "src/Lucent.Core", "apps/Lucent.IssueBrowser" })
            foreach (
                var path in Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            )
            {
                var segments = path.Split(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                );
                if (
                    segments.Contains("obj", StringComparer.OrdinalIgnoreCase)
                    || segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
                )
                    continue;
                var syntax = CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(path));
                foreach (
                    var call in syntax
                        .GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                )
                {
                    var name = call.Expression switch
                    {
                        IdentifierNameSyntax identifier
                            when call.Ancestors()
                                .OfType<ClassDeclarationSyntax>()
                                .Any(type => type.Identifier.ValueText == "Components") =>
                            identifier,
                        MemberAccessExpressionSyntax member
                            when member.Expression.ToString()
                                is "Components"
                                    or "Lucent.Core.Components"
                                    or "global::Lucent.Core.Components" => member.Name
                            as IdentifierNameSyntax,
                        _ => null,
                    };
                    if (name?.Identifier.ValueText == "Layout")
                        expectedLayoutLocations.Add(
                            (
                                new Uri(Path.GetFullPath(path)),
                                new LuiSpan(name.SpanStart, name.Span.Length)
                            )
                        );
                }
            }
            var actualLayoutLocations = layoutReferences
                ?.Locations.Select(location => (location.Uri, location.Span))
                .ToHashSet();
            var actualLayoutEdits = layoutRename
                ?.Edits.SelectMany(edit => edit.Spans.Select(span => (edit.Uri, span)))
                .ToHashSet();
            var missingLayoutReferences = expectedLayoutLocations
                .Except(actualLayoutLocations ?? [])
                .ToArray();
            var unexpectedLayoutReferences = (actualLayoutLocations ?? [])
                .Except(expectedLayoutLocations)
                .ToArray();
            var missingLayoutEdits = expectedLayoutLocations
                .Except(actualLayoutEdits ?? [])
                .ToArray();
            var unexpectedLayoutEdits = (actualLayoutEdits ?? [])
                .Except(expectedLayoutLocations)
                .ToArray();
            Assert(
                actualLayoutLocations is not null
                    && actualLayoutLocations.SetEquals(expectedLayoutLocations)
                    && actualLayoutEdits is not null
                    && actualLayoutEdits.SetEquals(expectedLayoutLocations),
                $"Issue Browser Layout references/rename did not cover every paired tag and the Core project declaration exactly. Missing references: {String.Join(", ", missingLayoutReferences.Select(item => $"{item.Item1}@{item.Item2.Start}+{item.Item2.Length}"))}; unexpected references: {String.Join(", ", unexpectedLayoutReferences.Select(item => $"{item.Item1}@{item.Item2.Start}+{item.Item2.Length}"))}; missing edits: {String.Join(", ", missingLayoutEdits.Select(item => $"{item.Item1}@{item.Item2.Start}+{item.Item2.Length}"))}; unexpected edits: {String.Join(", ", unexpectedLayoutEdits.Select(item => $"{item.Item1}@{item.Item2.Start}+{item.Item2.Length}"))}."
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
                )
                    && !properties.Any(item => item.Label == "VirtualRowHeight")
                    && !properties.Any(item =>
                        item.Label
                            is "Bind"
                                or "BindValue"
                                or "Equals"
                                or "GetHashCode"
                                or "GetType"
                                or "Set"
                                or "SetValue"
                                or "ToString"
                                or "When"
                                or "With"
                    ),
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
                    && references.Any(item => item.Label == "HeaderTitleStyle" && item.Kind == 5)
                    && references.Any(item => item.Label == "HeaderContentStyle" && item.Kind == 5),
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
            // Exercise token completion and authored variants through editor overlays,
            // without requiring the stock-theme example to carry decorative app fields.
            var components = new Uri(Path.GetFullPath("apps/Lucent.IssueBrowser/Components.cs"));
            var componentsText = await File.ReadAllTextAsync(components.LocalPath);
            issueBrowser.ReplaceText(
                components,
                componentsText
                    + """

                    public static partial class Components
                    {
                        public static readonly Token<Brush> EditorFixtureSurface = new("editor-fixture-surface", Color.Parse("#ffffff"));
                        public static readonly Token<Brush> EditorFixtureHover = new("editor-fixture-hover", Color.Parse("#eeeeee"));
                    }
                    """
            );
            issueRowText += """

                style EditorFixtureStyle {
                    Background: EditorFixtureSurface;
                    when Hover { Background: EditorFixtureHover; }
                    when Pressed { Background: EditorFixtureSurface; }
                    when Hover | Pressed { Background: EditorFixtureHover; }
                    when FocusVisible { Background: EditorFixtureSurface; }
                }
                """;
            issueBrowser.ReplaceText(issueRow, issueRowText);
            var issueRowSymbols = await issueBrowser.DocumentSymbolsAsync(
                issueRow,
                CancellationToken.None
            );
            var variants = LuiParser
                .Parse(issueRowText)
                .Styles.Single(style => style.Name.Text == "EditorFixtureStyle")
                .Members.OfType<LuiVariantGroupSyntax>()
                .ToArray();
            var variantSymbols = issueRowSymbols!
                .Single(symbol => symbol.Name == "EditorFixtureStyle")
                .Children.Where(symbol => symbol.Name.StartsWith("when ", StringComparison.Ordinal))
                .ToArray();
            Assert(
                variantSymbols.Length == variants.Length,
                "variant document symbols did not cover every authored state rule."
            );
            foreach (var variant in variants)
                Assert(
                    variantSymbols.Any(symbol =>
                        symbol.SelectionSpan.Equals(variant.Condition.Span)
                    ),
                    "variant document-symbol selection range did not select its condition."
                );
            var error = new Uri(
                Path.GetFullPath("src/Lucent.Core/Components/Status/ErrorNotice.lui")
            );
            var errorText = await File.ReadAllTextAsync(error.LocalPath);
            var tokenCompletions = await issueBrowser.CompletionsAsync(
                issueRow,
                issueRowText.IndexOf("EditorFixtureSurface", StringComparison.Ordinal),
                CancellationToken.None
            );
            var variantCompletions = await issueBrowser.CompletionsAsync(
                issueRow,
                issueRowText.IndexOf("FocusVisible", StringComparison.Ordinal),
                CancellationToken.None
            );
            Assert(
                tokenCompletions.Any(item => item.Label == "EditorFixtureSurface" && item.Kind == 5)
                    && !tokenCompletions.Any(item => item.Label == "AppTheme"),
                "token RHS completion omitted the controlled editor token or retained the removed app theme: "
                    + String.Join(
                        ", ",
                        tokenCompletions.Select(item =>
                            item.Label + "/" + item.Kind + "/" + item.Detail
                        )
                    )
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
                    + String.Join(
                        ", ",
                        axisCompletions.Select(item => item.Label + "/" + item.Kind)
                    )
            );
        }

        using (var browserLsp = LspClient.Start())
        {
            var project = new Uri(
                Path.GetFullPath("apps/Lucent.IssueBrowser/Lucent.IssueBrowser.csproj")
            );
            var header = new Uri(Path.GetFullPath("apps/Lucent.IssueBrowser/Header.lui"));
            var headerText = await File.ReadAllTextAsync(header.LocalPath);
            var issueBrowserRoot = new Uri(
                Path.GetFullPath("apps/Lucent.IssueBrowser/IssueBrowser.lui")
            );
            var issueBrowserRootText = await File.ReadAllTextAsync(issueBrowserRoot.LocalPath);
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
            var layout = issueBrowserRootText.IndexOf("<Layout", StringComparison.Ordinal);
            var layoutPosition = Position(issueBrowserRootText, layout + 1);
            using var browserLayoutReferences = await browserLsp.RequestAsync(
                "textDocument/references",
                new
                {
                    textDocument = new { uri = VsCodeUri(issueBrowserRoot) },
                    position = new
                    {
                        line = layoutPosition.Line,
                        character = layoutPosition.Character,
                    },
                    context = new { includeDeclaration = true },
                }
            );
            using var browserLayoutRename = await browserLsp.RequestAsync(
                "textDocument/rename",
                new
                {
                    textDocument = new { uri = VsCodeUri(issueBrowserRoot) },
                    position = new
                    {
                        line = layoutPosition.Line,
                        character = layoutPosition.Character,
                    },
                    newName = "LayoutReplacement",
                }
            );
            Assert(
                browserLayoutReferences.RootElement.TryGetProperty("result", out _)
                    && browserLayoutRename.RootElement.TryGetProperty(
                        "result",
                        out var renameResult
                    )
                    && renameResult.ValueKind != JsonValueKind.Null
                    && renameResult.TryGetProperty("changes", out _),
                "Issue Browser Layout RPC returned no result: "
                    + browserLayoutReferences.RootElement.GetRawText()
                    + " / "
                    + browserLayoutRename.RootElement.GetRawText()
            );
            var rpcLayoutReferences = browserLayoutReferences
                .RootElement.GetProperty("result")
                .EnumerateArray()
                .ToArray();
            var rpcLayoutChanges = browserLayoutRename
                .RootElement.GetProperty("result")
                .GetProperty("changes");
            var authoredLayoutReferences = Directory
                .GetFiles("apps/Lucent.IssueBrowser", "*.lui")
                .Concat(Directory.GetFiles("src/Lucent.Core", "*.lui", SearchOption.AllDirectories))
                .Sum(path =>
                    System.Text.RegularExpressions.Regex.Count(
                        File.ReadAllText(path),
                        @"</?Layout\b"
                    )
                );
            Assert(
                rpcLayoutReferences.Length == expectedLayoutLocations.Count
                    && rpcLayoutChanges
                        .EnumerateObject()
                        .Sum(change => change.Value.GetArrayLength())
                        == expectedLayoutLocations.Count
                    && rpcLayoutReferences.Count(location =>
                        location
                            .GetProperty("uri")
                            .GetString()!
                            .EndsWith("Layout.cs", StringComparison.Ordinal)
                    ) == 1
                    && rpcLayoutChanges
                        .EnumerateObject()
                        .Single(change =>
                            change.Name.EndsWith("Layout.cs", StringComparison.Ordinal)
                        )
                        .Value.GetArrayLength() == 1
                    && rpcLayoutChanges
                        .EnumerateObject()
                        .Where(change =>
                            change.Name.EndsWith(".lui", StringComparison.OrdinalIgnoreCase)
                        )
                        .Sum(change => change.Value.GetArrayLength()) == authoredLayoutReferences,
                "Issue Browser Layout RPC references/rename did not preserve all paired tags and the Core declaration."
            );
            var layoutSource = new Uri(
                Path.GetFullPath("src/Lucent.Core/Components/Layout/Layout.cs")
            );
            var layoutSourceText = await File.ReadAllTextAsync(layoutSource.LocalPath);
            var layoutSourceOffset =
                layoutSourceText.IndexOf("ComponentRecipe Layout(", StringComparison.Ordinal)
                + "ComponentRecipe ".Length;
            var insertedLine = "\n" + layoutSourceText;
            await browserLsp.NotifyAsync(
                "textDocument/didOpen",
                new
                {
                    textDocument = new
                    {
                        uri = VsCodeUri(layoutSource),
                        version = 1,
                        text = insertedLine,
                    },
                }
            );
            var shiftedLayout = Position(insertedLine, layoutSourceOffset + 1);
            using var insertedLineReferences = await browserLsp.RequestAsync(
                "textDocument/references",
                new
                {
                    textDocument = new { uri = VsCodeUri(layoutSource) },
                    position = new
                    {
                        line = shiftedLayout.Line,
                        character = shiftedLayout.Character,
                    },
                    context = new { includeDeclaration = true },
                }
            );
            var sameLine = "  " + insertedLine;
            await browserLsp.NotifyAsync(
                "textDocument/didChange",
                new
                {
                    textDocument = new { uri = VsCodeUri(layoutSource), version = 2 },
                    contentChanges = new[] { new { text = sameLine } },
                }
            );
            var sameLineLayout = Position(sameLine, layoutSourceOffset + 3);
            using var sameLineRename = await browserLsp.RequestAsync(
                "textDocument/rename",
                new
                {
                    textDocument = new { uri = VsCodeUri(layoutSource) },
                    position = new
                    {
                        line = sameLineLayout.Line,
                        character = sameLineLayout.Character,
                    },
                    newName = "LayoutReplacement",
                }
            );
            Assert(
                insertedLineReferences.RootElement.GetProperty("result").GetArrayLength()
                    == expectedLayoutLocations.Count
                    && sameLineRename
                        .RootElement.GetProperty("result")
                        .GetProperty("changes")
                        .EnumerateObject()
                        .Single(change =>
                            change.Name.EndsWith("Layout.cs", StringComparison.Ordinal)
                        )
                        .Value.GetArrayLength() == 1,
                "dirty C# inserted-line or same-line offsets used stale source positions."
            );
            await browserLsp.NotifyAsync(
                "textDocument/didClose",
                new { textDocument = new { uri = VsCodeUri(layoutSource) } }
            );
            var member = headerText.IndexOf("browser.ToggleDensity", StringComparison.Ordinal);
            var memberPosition = Position(headerText, member + "browser.".Length);
            using var browserCompletion = await browserLsp.RequestAsync(
                "textDocument/completion",
                new
                {
                    textDocument = new { uri = VsCodeUri(header) },
                    position = new
                    {
                        line = memberPosition.Line,
                        character = memberPosition.Character,
                    },
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
                    position = new
                    {
                        line = memberPosition.Line,
                        character = memberPosition.Character,
                    },
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
            var browserDocument = new Uri(
                Path.GetFullPath("apps/Lucent.IssueBrowser/IssueBrowser.lui")
            );
            var browserText = await File.ReadAllTextAsync(browserDocument.LocalPath);
            var headerTokens = await SemanticTokensAsync(browserLsp, header, headerText);
            var errorDocument = new Uri(
                Path.GetFullPath("src/Lucent.Core/Components/Status/ErrorNotice.lui")
            );
            var errorText = await File.ReadAllTextAsync(errorDocument.LocalPath);
            var errorTokens = await SemanticTokensAsync(browserLsp, errorDocument, errorText);
            var browserTokens = await SemanticTokensAsync(browserLsp, browserDocument, browserText);
            var detailsDocument = new Uri(
                Path.GetFullPath("apps/Lucent.IssueBrowser/IssueDetailPane.lui")
            );
            var detailsText = await File.ReadAllTextAsync(detailsDocument.LocalPath);
            var detailsTokens = await SemanticTokensAsync(browserLsp, detailsDocument, detailsText);
            foreach (var literal in new[] { "Issues</Text>", "Density: comfortable" })
            {
                var start = headerText.IndexOf(literal, StringComparison.Ordinal);
                Assert(
                    !headerTokens.Any(token =>
                        token.Start < start + literal.Length && token.Start + token.Length > start
                    ),
                    "Generated C# semantic tokens leaked into authored text or closing markup: "
                        + literal
                );
            }
            AssertSemanticToken(headerTokens, headerText, "Axis", "property");
            AssertSemanticToken(headerTokens, headerText, "IssueBrowserState", "type");
            AssertSemanticToken(errorTokens, errorText, "LayoutAxis", "type");
            AssertSemanticToken(errorTokens, errorText, "Column", "enumMember");
            var enumPosition = Position(
                errorText,
                errorText.IndexOf("LayoutAxis.", StringComparison.Ordinal) + "LayoutAxis.".Length
            );
            using var lazyCompletion = await browserLsp.RequestAsync(
                "textDocument/completion",
                new
                {
                    textDocument = new { uri = VsCodeUri(errorDocument) },
                    position = new { line = enumPosition.Line, character = enumPosition.Character },
                }
            );
            var columnItem = lazyCompletion
                .RootElement.GetProperty("result")
                .GetProperty("items")
                .EnumerateArray()
                .First(item => item.GetProperty("label").GetString() == "Column");
            Assert(
                columnItem.GetProperty("documentation").ValueKind == JsonValueKind.Null,
                "Completion eagerly produced Roslyn documentation before selection."
            );
            using var resolvedColumn = await browserLsp.RequestAsync(
                "completionItem/resolve",
                columnItem
            );
            Assert(
                resolvedColumn
                    .RootElement.GetProperty("result")
                    .GetProperty("documentation")
                    .GetProperty("value")
                    .GetString()!
                    .Contains("LayoutAxis.Column", StringComparison.Ordinal),
                "Resolving the selected completion lost its symbol description."
            );
            await browserLsp.NotifyAsync(
                "textDocument/didOpen",
                new
                {
                    textDocument = new
                    {
                        uri = VsCodeUri(errorDocument),
                        version = 1,
                        text = errorText + "\n// edited",
                    },
                }
            );
            using var staleColumn = await browserLsp.RequestAsync(
                "completionItem/resolve",
                columnItem
            );
            Assert(
                staleColumn.RootElement.GetProperty("result").GetProperty("documentation").ValueKind
                    == JsonValueKind.Null,
                "A completion from an earlier document epoch was resolved after an edit."
            );
            await browserLsp.NotifyAsync(
                "textDocument/didClose",
                new { textDocument = new { uri = VsCodeUri(errorDocument) } }
            );

            AssertSemanticToken(detailsTokens, detailsText, "var", "keyword");
            var inKeyword = detailsText.IndexOf(" in ", StringComparison.Ordinal) + 1;
            Assert(
                detailsTokens.Any(token =>
                    token.Start == inKeyword && token.Length == 2 && token.Type == "keyword"
                ),
                "semantic tokens did not classify the foreach 'in' as keyword."
            );
            AssertSemanticToken(detailsTokens, detailsText, "with", "keyword");
            foreach (
                var (document, text, offset, label) in new[]
                {
                    (
                        browserDocument,
                        browserText,
                        browserText.IndexOf("browser.IsLoading", StringComparison.Ordinal)
                            + "browser.".Length,
                        "IsLoading"
                    ),
                    (
                        detailsDocument,
                        detailsText,
                        detailsText.IndexOf("issue.Number", StringComparison.Ordinal)
                            + "issue.".Length,
                        "Number"
                    ),
                }
            )
            {
                var position = Position(text, offset);
                using var completion = await browserLsp.RequestAsync(
                    "textDocument/completion",
                    new
                    {
                        textDocument = new { uri = VsCodeUri(document) },
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
            Assert(
                await browserLsp.ExitAsync() == 0,
                "browser completion LSP did not shut down cleanly."
            );
        }
    }

    [TestMethod]
    public async Task ProtocolLifecycleRejectsInvalidMessageOrder()
    {
        using (var premature = LspClient.Start())
        {
            using var rejected = await premature.RequestAsync("shutdown", new { });
            Assert(
                rejected.RootElement.TryGetProperty("error", out _),
                "shutdown before initialize passed."
            );
            Assert(
                await premature.ExitAsync() == 1,
                "exit before successful shutdown returned success."
            );
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
            using var acceptedInitialize = await messageKinds.RequestAsync(
                "initialize",
                initialize
            );
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
            Assert(
                rejectedExit.RootElement.TryGetProperty("error", out _),
                "exit request was accepted."
            );
            await messageKinds.RequestAsync("shutdown", new { });
            Assert(
                await messageKinds.ExitAsync() == 0,
                "strict message-kind client did not exit cleanly."
            );
        }
    }

    [TestMethod]
    public async Task DiagnosticsMatchCompilerAndGenerator()
    {
        await RunDiagnosticParityAsync();
    }

    [TestMethod]
    public async Task InvalidLogicalSiblingDoesNotPoisonProject()
    {
        await RunInvalidLogicalSiblingAsync();
    }

    [TestMethod]
    public async Task AncestorInputReloadInvalidatesDependents()
    {
        await RunAncestorInputReloadAsync();
    }

    [TestMethod]
    public async Task FreshnessAndProjectGraphChangesInvalidatePublishedDocuments()
    {
        await RunFreshnessAndProjectGraphRegressionsAsync();
    }

    [TestMethod]
    public async Task DiamondProjectGraphDeduplicatesSharedInputs()
    {
        await RunDiamondProjectGraphRegressionAsync();
    }

    [TestMethod]
    public async Task ExactFreshnessIdentityControlsPublication()
    {
        await RunExactFreshnessIdentityRegressionAsync();
    }

    private static string CoreMetadataReference
    {
        get
        {
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name;
            if (String.IsNullOrWhiteSpace(configuration))
                throw new InvalidOperationException(
                    "Cannot determine the test build configuration."
                );
            var assembly = Path.GetFullPath(
                Path.Combine(
                    "src",
                    "Lucent.Core",
                    "bin",
                    configuration,
                    "net10.0",
                    "Lucent.Core.dll"
                )
            );
            if (!File.Exists(assembly))
                throw new FileNotFoundException(
                    "Build Lucent.Core for the active test configuration before loading metadata-only LSP fixtures.",
                    assembly
                );
            return $"<Reference Include=\"Lucent.Core\"><HintPath>{System.Security.SecurityElement.Escape(assembly)}</HintPath></Reference>";
        }
    }

    static async Task RunDiagnosticParityAsync()
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
                    "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Sample</RootNamespace><LangVersion>preview</LangVersion></PropertyGroup><ItemGroup>"
                    + CoreMetadataReference
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

    static async Task RunInvalidLogicalSiblingAsync()
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
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Sample</RootNamespace></PropertyGroup><ItemGroup>{CoreMetadataReference}<AdditionalFiles Include=\"Consumer.lui\" LucentLuiLogicalPath=\"Consumer.lui\" /><AdditionalFiles Include=\"Invalid.lui\" LucentLuiLogicalPath=\"../Invalid.lui\" /><CompilerVisibleItemMetadata Include=\"AdditionalFiles\" MetadataName=\"LucentLuiLogicalPath\" /></ItemGroup></Project>"
            );
            await File.WriteAllTextAsync(
                consumer,
                "namespace Sample; using Lucent.Core; internal component Consumer() { <Invalid /> }"
            );
            await File.WriteAllTextAsync(
                invalid,
                "namespace Sample; using Lucent.Core; internal component Invalid() { <Row /> }"
            );
            using var context = await LuiProjectContext.LoadAsync(
                projectPath,
                CancellationToken.None
            );
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

    static async Task RunAncestorInputReloadAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "lucent-ancestor-input-" + Guid.NewGuid());
        var projectRoot = Path.Combine(root, "project");
        Directory.CreateDirectory(projectRoot);
        try
        {
            var projectPath = Path.Combine(projectRoot, "Sample.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>{CoreMetadataReference}<AdditionalFiles Include=\"Widget.lui\" /></ItemGroup></Project>"
            );
            await File.WriteAllTextAsync(
                Path.Combine(projectRoot, "Widget.lui"),
                "namespace Sample; using Lucent.Core; internal component Widget() { <Row /> }"
            );
            var editorConfig = Path.Combine(root, ".editorconfig");
            var packages = Path.Combine(root, "Directory.Packages.props");
            await File.WriteAllTextAsync(editorConfig, "root = true");
            await File.WriteAllTextAsync(packages, "<Project />");
            using var context = await LuiProjectContext.LoadAsync(
                projectPath,
                CancellationToken.None
            );
            Assert(
                await context.ReloadIfRelevantAsync(new Uri(editorConfig), CancellationToken.None)
                    && await context.ReloadIfRelevantAsync(
                        new Uri(packages),
                        CancellationToken.None
                    ),
                "ancestor editorconfig or package-props changes were not relevant project inputs."
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static async Task RunFreshnessAndProjectGraphRegressionsAsync()
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
        var referencedLui = Path.Combine(referencedRoot, "Imported.lui");
        var hostProjectText =
            $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Host</RootNamespace></PropertyGroup><ItemGroup>{CoreMetadataReference}<ProjectReference Include=\"../referenced/Referenced.csproj\" /><AdditionalFiles Include=\"Widget.lui\" /></ItemGroup></Project>";
        try
        {
            await File.WriteAllTextAsync(
                referencedProject,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>{CoreMetadataReference}</ItemGroup></Project>"
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
            using var context = await LuiProjectContext.LoadAsync(
                hostProject,
                CancellationToken.None
            );
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
            var survivedReloadRace = await context.IsCurrentAsync(
                published.Result,
                CancellationToken.None,
                async () =>
                {
                    await context.ReloadIfRelevantAsync(
                        new Uri(hostProject),
                        CancellationToken.None
                    );
                }
            );
            Assert(
                !survivedReloadRace,
                "a project reload between freshness selection and snapshot publication was not rejected."
            );
            Assert(
                context
                    .ProjectDirectories()
                    .Contains(referencedRoot, StringComparer.OrdinalIgnoreCase),
                "evaluated project references did not contribute watched project directories."
            );
            var sourceText = await File.ReadAllTextAsync(source);
            var external = sourceText.IndexOf("External", StringComparison.Ordinal);
            var externalDeclaration = (await File.ReadAllTextAsync(referencedSource)).IndexOf(
                "External",
                StringComparison.Ordinal
            );
            var externalReferences = await context.ReferencesAsync(
                uri,
                external,
                true,
                CancellationToken.None
            );
            var externalRename = await context.RenameAsync(
                uri,
                external,
                "ImportedExternal",
                CancellationToken.None
            );
            var externalCSharpReferences = await context.ReferencesAsync(
                new Uri(referencedSource),
                externalDeclaration,
                true,
                CancellationToken.None
            );
            var externalCSharpRename = await context.RenameAsync(
                new Uri(referencedSource),
                externalDeclaration,
                "ImportedExternal",
                CancellationToken.None
            );
            Assert(
                externalReferences is not null
                    && externalReferences.Locations.Any(location =>
                        location.Uri == uri
                        && location.Span.Equals(new LuiSpan(external, "External".Length))
                    )
                    && externalReferences.Locations.Any(location =>
                        location.Uri == new Uri(referencedSource)
                        && location.Span.Equals(new LuiSpan(externalDeclaration, "External".Length))
                    )
                    && externalRename is not null
                    && externalRename.Edits.Any(edit =>
                        edit.Uri == new Uri(referencedSource)
                        && edit.Spans.Any(span => span.Start == externalDeclaration)
                    )
                    && externalCSharpReferences is not null
                    && externalCSharpReferences.Locations.Any(location =>
                        location.Uri == uri
                        && location.Span.Equals(new LuiSpan(external, "External".Length))
                    )
                    && externalCSharpRename is not null
                    && externalCSharpRename.Edits.Any(edit =>
                        edit.Uri == uri && edit.Spans.Any(span => span.Start == external)
                    ),
                "project-reference rename or references omitted an editable source declaration or C# origin."
            );
            await File.WriteAllTextAsync(
                referencedLui,
                "namespace Referenced; using static Lucent.Core.Components; public component Imported() { <Row /> }"
            );
            await File.WriteAllTextAsync(
                referencedProject,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>{CoreMetadataReference}<AdditionalFiles Include=\"Imported.lui\" /></ItemGroup></Project>"
            );
            Assert(
                await context.ReloadIfRelevantAsync(
                    new Uri(referencedProject),
                    CancellationToken.None
                ),
                "referenced-project .lui input change did not reload the evaluated graph."
            );
            var importedHost =
                "namespace Host; using static Referenced.Components; internal component Widget() { <Imported /> }";
            context.ReplaceText(uri, importedHost);
            var imported = importedHost.IndexOf("Imported", StringComparison.Ordinal);
            var importedDeclaration = (await File.ReadAllTextAsync(referencedLui)).IndexOf(
                "Imported",
                StringComparison.Ordinal
            );
            var importedReferences = await context.ReferencesAsync(
                uri,
                imported,
                true,
                CancellationToken.None
            );
            var importedRename = await context.RenameAsync(
                uri,
                imported,
                "SharedImported",
                CancellationToken.None
            );
            var importedDefinition = await context.DefinitionAsync(
                new Uri(referencedLui),
                importedDeclaration,
                CancellationToken.None
            );
            var importedHostDefinition = await context.DefinitionAsync(
                uri,
                imported,
                CancellationToken.None
            );
            var importedHover = await context.HoverAsync(
                new Uri(referencedLui),
                importedDeclaration,
                CancellationToken.None
            );
            var importedHostHover = await context.HoverAsync(uri, imported, CancellationToken.None);
            var importedPublished = await context.CompileAsync(
                new Uri(referencedLui),
                CancellationToken.None
            );
            Assert(
                importedReferences is not null
                    && importedReferences.Locations.Any(location =>
                        location.Uri == uri
                        && location.Span.Equals(new LuiSpan(imported, "Imported".Length))
                    )
                    && importedReferences.Locations.Any(location =>
                        location.Uri == new Uri(referencedLui)
                        && location.Span.Equals(new LuiSpan(importedDeclaration, "Imported".Length))
                    )
                    && importedRename is not null
                    && importedRename.Edits.Any(edit =>
                        edit.Uri == new Uri(referencedLui)
                        && edit.Spans.Any(span => span.Start == importedDeclaration)
                    )
                    && importedDefinition is not null
                    && importedHover is not null
                    && importedHostDefinition is { Uri: var definitionUri }
                    && definitionUri == new Uri(referencedLui)
                    && importedHostHover is not null
                    && importedPublished is not null,
                "referenced-project .lui source did not provide exact graph tooling: refs="
                    + (importedReferences is null ? "null" : "present")
                    + " rename="
                    + (importedRename is null ? "null" : "present")
                    + " definition="
                    + (importedDefinition is null ? "null" : "present")
                    + " hover="
                    + (importedHover is null ? "null" : "present")
                    + " hostDefinition="
                    + importedHostDefinition?.Uri
                    + " hostHover="
                    + (importedHostHover is null ? "null" : "present")
                    + " compile="
                    + (importedPublished is null ? "null" : "present")
            );
            context.ReplaceText(uri, sourceText);
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
                await context.ReloadIfRelevantAsync(
                    new Uri(referencedSource),
                    CancellationToken.None
                ) && !await context.IsCurrentAsync(published.Result, CancellationToken.None),
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

    static async Task RunDiamondProjectGraphRegressionAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "lucent-diamond-" + Guid.NewGuid());
        var sharedRoot = Path.Combine(root, "shared");
        var leftRoot = root;
        var rightRoot = root;
        var hostRoot = Path.Combine(root, "host");
        var linkedRoot = root;
        Directory.CreateDirectory(sharedRoot);
        Directory.CreateDirectory(leftRoot);
        Directory.CreateDirectory(rightRoot);
        Directory.CreateDirectory(hostRoot);
        Directory.CreateDirectory(linkedRoot);
        var sharedProject = Path.Combine(sharedRoot, "Shared.csproj");
        var sharedLui = Path.Combine(sharedRoot, "Shared.lui");
        var leftProject = Path.Combine(leftRoot, "Left.csproj");
        var leftLui = Path.Combine(leftRoot, "Left.lui");
        var rightProject = Path.Combine(rightRoot, "Right.csproj");
        var rightLui = Path.Combine(rightRoot, "Right.lui");
        var hostProject = Path.Combine(hostRoot, "Host.csproj");
        var hostLui = Path.Combine(hostRoot, "Host.lui");
        var linkedLui = Path.Combine(linkedRoot, "Linked.lui");
        try
        {
            await File.WriteAllTextAsync(
                sharedProject,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>{CoreMetadataReference}<AdditionalFiles Include=\"Shared.lui\" /></ItemGroup></Project>"
            );
            await File.WriteAllTextAsync(
                sharedLui,
                "namespace Shared; using static Lucent.Core.Components; public component SharedWidget() { <Row /> }"
            );
            await File.WriteAllTextAsync(
                linkedLui,
                "namespace Linked; using static Lucent.Core.Components; internal component LinkedWidget() { <Row /> }"
            );
            var branchProject =
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>{CoreMetadataReference}<ProjectReference Include=\"shared/Shared.csproj\" /><AdditionalFiles Include=\"{{0}}.lui\" /><AdditionalFiles Include=\"Linked.lui\" /></ItemGroup></Project>";
            await File.WriteAllTextAsync(
                leftProject,
                String.Format(CultureInfo.InvariantCulture, branchProject, "Left")
            );
            await File.WriteAllTextAsync(
                rightProject,
                String.Format(CultureInfo.InvariantCulture, branchProject, "Right")
            );
            var branchSource =
                "namespace {0}; using static Shared.Components; using static Linked.Components; public component {0}Widget() {{ <Row><SharedWidget /><LinkedWidget /></Row> }}";
            await File.WriteAllTextAsync(
                leftLui,
                String.Format(CultureInfo.InvariantCulture, branchSource, "Left")
            );
            await File.WriteAllTextAsync(
                rightLui,
                String.Format(CultureInfo.InvariantCulture, branchSource, "Right")
            );
            await File.WriteAllTextAsync(
                hostProject,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>{CoreMetadataReference}<ProjectReference Include=\"../Left.csproj\" /><ProjectReference Include=\"../Right.csproj\" /><AdditionalFiles Include=\"Host.lui\" /></ItemGroup></Project>"
            );
            var hostSource =
                "namespace Host; using static Left.Components; internal component Host() { <LeftWidget /> }";
            await File.WriteAllTextAsync(hostLui, hostSource);
            using var context = await LuiProjectContext.LoadAsync(
                hostProject,
                CancellationToken.None
            );
            var sharedName = (await File.ReadAllTextAsync(sharedLui)).IndexOf(
                "SharedWidget",
                StringComparison.Ordinal
            );
            var leftSource = await File.ReadAllTextAsync(leftLui);
            var rightSource = await File.ReadAllTextAsync(rightLui);
            var leftShared = leftSource.IndexOf("SharedWidget", StringComparison.Ordinal);
            var linkedSource = await File.ReadAllTextAsync(linkedLui);
            var linkedName = linkedSource.IndexOf("LinkedWidget", StringComparison.Ordinal);
            var leftLinked = leftSource.IndexOf("LinkedWidget", StringComparison.Ordinal);
            var rightLinked = rightSource.IndexOf("LinkedWidget", StringComparison.Ordinal);
            var hostName = hostSource.IndexOf("LeftWidget", StringComparison.Ordinal);
            var hostReferences = await context.ReferencesAsync(
                new Uri(hostLui),
                hostName,
                true,
                CancellationToken.None
            );
            var sharedReferences = await context.ReferencesAsync(
                new Uri(leftLui),
                leftShared,
                true,
                CancellationToken.None
            );
            var linkedReferences = await context.ReferencesAsync(
                new Uri(linkedLui),
                linkedName,
                true,
                CancellationToken.None
            );
            var linkedRename = await context.RenameAsync(
                new Uri(linkedLui),
                linkedName,
                "RenamedLinkedWidget",
                CancellationToken.None
            );
            var linkedUsageReferences = await context.ReferencesAsync(
                new Uri(leftLui),
                leftLinked,
                true,
                CancellationToken.None
            );
            var linkedUsageRename = await context.RenameAsync(
                new Uri(leftLui),
                leftLinked,
                "RenamedLinkedWidget",
                CancellationToken.None
            );
            var linkedUsagePrepareRename = await context.PrepareRenameAsync(
                new Uri(leftLui),
                leftLinked,
                CancellationToken.None
            );
            Assert(
                hostReferences is not null
                    && hostReferences.Locations.Any(location =>
                        location.Uri == new Uri(hostLui)
                        && location.Span.Equals(new LuiSpan(hostName, "LeftWidget".Length))
                    )
                    && hostReferences.Locations.Any(location =>
                        location.Uri == new Uri(leftLui)
                        && location.Span.Equals(
                            new LuiSpan(
                                leftSource.IndexOf("LeftWidget", StringComparison.Ordinal),
                                "LeftWidget".Length
                            )
                        )
                    )
                    && sharedReferences is not null
                    && sharedReferences.Locations.Any(location =>
                        location.Uri == new Uri(leftLui)
                        && location.Span.Equals(new LuiSpan(leftShared, "SharedWidget".Length))
                    )
                    && sharedReferences.Locations.Any(location =>
                        location.Uri == new Uri(sharedLui)
                        && location.Span.Equals(new LuiSpan(sharedName, "SharedWidget".Length))
                    ),
                "a diamond ProjectReference graph did not bind the shared .lui generated projection dependency-first: host="
                    + (
                        hostReferences is null
                            ? "null"
                            : String.Join(
                                ",",
                                hostReferences.Locations.Select(location => location.Uri)
                            )
                    )
                    + " shared="
                    + (
                        sharedReferences is null
                            ? "null"
                            : String.Join(
                                ",",
                                sharedReferences.Locations.Select(location => location.Uri)
                            )
                    )
            );
            Assert(
                linkedReferences is not null
                    && linkedReferences.Locations.Any(location =>
                        location.Uri == new Uri(linkedLui)
                        && location.Span.Equals(new LuiSpan(linkedName, "LinkedWidget".Length))
                    )
                    && linkedReferences.Locations.Any(location =>
                        location.Uri == new Uri(leftLui)
                        && location.Span.Equals(new LuiSpan(leftLinked, "LinkedWidget".Length))
                    )
                    && linkedReferences.Locations.Any(location =>
                        location.Uri == new Uri(rightLui)
                        && location.Span.Equals(new LuiSpan(rightLinked, "LinkedWidget".Length))
                    )
                    && linkedRename is not null
                    && linkedRename.Edits.Any(edit =>
                        edit.Uri == new Uri(linkedLui)
                        && edit.Spans.Contains(new LuiSpan(linkedName, "LinkedWidget".Length))
                    )
                    && linkedRename.Edits.Any(edit =>
                        edit.Uri == new Uri(leftLui)
                        && edit.Spans.Contains(new LuiSpan(leftLinked, "LinkedWidget".Length))
                    )
                    && linkedRename.Edits.Any(edit =>
                        edit.Uri == new Uri(rightLui)
                        && edit.Spans.Contains(new LuiSpan(rightLinked, "LinkedWidget".Length))
                    )
                    && linkedUsageReferences is not null
                    && linkedUsageReferences.Locations.Any(location =>
                        location.Uri == new Uri(linkedLui)
                        && location.Span.Equals(new LuiSpan(linkedName, "LinkedWidget".Length))
                    )
                    && linkedUsageReferences.Locations.Any(location =>
                        location.Uri == new Uri(rightLui)
                        && location.Span.Equals(new LuiSpan(rightLinked, "LinkedWidget".Length))
                    )
                    && linkedUsageRename is not null
                    && linkedUsageRename.Edits.Any(edit =>
                        edit.Uri == new Uri(linkedLui)
                        && edit.Spans.Contains(new LuiSpan(linkedName, "LinkedWidget".Length))
                    )
                    && linkedUsageRename.Edits.Any(edit =>
                        edit.Uri == new Uri(rightLui)
                        && edit.Spans.Contains(new LuiSpan(rightLinked, "LinkedWidget".Length))
                    )
                    && linkedUsagePrepareRename is not null
                    && linkedUsagePrepareRename.Uri == new Uri(leftLui)
                    && linkedUsagePrepareRename.Span.Equals(
                        new LuiSpan(leftLinked, "LinkedWidget".Length)
                    ),
                "references or rename from a multiply owned linked .lui declaration or owner usage omitted an owning project."
            );
            LuiCompilationResult UnmappableLinkedDeclaration(LuiCompilationResult result)
            {
                if (
                    result.Identity.Document.LogicalPath != "Linked.lui"
                    || !String.Equals(
                        result.Identity.ProjectIdentity,
                        leftProject,
                        StringComparison.Ordinal
                    )
                )
                    return result;
                var generatedName = GeneratedTokenSpan(result, linkedName, "LinkedWidget");
                return WithMap(
                    result,
                    result.Map.Entries.Where(entry => !Intersects(entry.Generated, generatedName))
                );
            }
            var unmappableUsageReferences = await context.ReferencesAsync(
                new Uri(leftLui),
                leftLinked,
                false,
                CancellationToken.None,
                transformGenerated: UnmappableLinkedDeclaration
            );
            var unmappableUsageRename = await context.RenameAsync(
                new Uri(leftLui),
                leftLinked,
                "RenamedLinkedWidget",
                CancellationToken.None,
                transformGenerated: UnmappableLinkedDeclaration
            );
            var unmappableUsagePrepareRename = await context.PrepareRenameAsync(
                new Uri(leftLui),
                leftLinked,
                CancellationToken.None,
                UnmappableLinkedDeclaration
            );
            Assert(
                unmappableUsageReferences is null
                    && unmappableUsageRename is null
                    && unmappableUsagePrepareRename is null,
                "an owner usage published tooling when its generated component declaration map was incomplete."
            );
            var linkedEdit = "// shifted\n" + await File.ReadAllTextAsync(linkedLui);
            context.ReplaceText(new Uri(linkedLui), linkedEdit);
            var dirtyVersions = new List<(string Project, string Version)>();
            var dirtyReferences = await context.ReferencesAsync(
                new Uri(hostLui),
                hostName,
                true,
                CancellationToken.None,
                transformGenerated: result =>
                {
                    if (result.Identity.Document.LogicalPath == "Linked.lui")
                        dirtyVersions.Add(
                            (result.Identity.ProjectIdentity, result.Identity.DocumentVersion)
                        );
                    return result;
                }
            );
            var hostProjectText = await File.ReadAllTextAsync(hostProject);
            await File.WriteAllTextAsync(hostProject, "not xml");
            await context.ReloadIfRelevantAsync(new Uri(hostProject), CancellationToken.None);
            await File.WriteAllTextAsync(hostProject, hostProjectText);
            await context.ReloadIfRelevantAsync(new Uri(hostProject), CancellationToken.None);
            var recoveredVersions = new List<(string Project, string Version)>();
            var recoveredReferences = await context.ReferencesAsync(
                new Uri(hostLui),
                hostName,
                true,
                CancellationToken.None,
                transformGenerated: result =>
                {
                    if (result.Identity.Document.LogicalPath == "Linked.lui")
                        recoveredVersions.Add(
                            (result.Identity.ProjectIdentity, result.Identity.DocumentVersion)
                        );
                    return result;
                }
            );
            var linkedVersion = LuiDocumentIdentity.Hash(linkedEdit);
            Assert(
                dirtyReferences is not null
                    && recoveredReferences is not null
                    && dirtyVersions.Count == 2
                    && recoveredVersions.Count == 2
                    && dirtyVersions.All(item => item.Version == linkedVersion)
                    && recoveredVersions.All(item => item.Version == linkedVersion)
                    && dirtyVersions.Select(item => item.Project).Distinct().Count() == 2
                    && recoveredVersions.Select(item => item.Project).Distinct().Count() == 2,
                "a dirty linked .lui overlay did not fan out across both project snapshots and reload recovery."
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static async Task RunExactFreshnessIdentityRegressionAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "lucent-exact-freshness-" + Guid.NewGuid());
        var hostRoot = Path.Combine(root, "host");
        var referencedRoot = Path.Combine(root, "referenced");
        Directory.CreateDirectory(hostRoot);
        Directory.CreateDirectory(referencedRoot);
        var hostProject = Path.Combine(hostRoot, "Host.csproj");
        var referencedProject = Path.Combine(referencedRoot, "Referenced.csproj");
        var hostLui = Path.Combine(hostRoot, "Widget.lui");
        var referencedLui = Path.Combine(referencedRoot, "Widget.lui");
        try
        {
            var metadata =
                " LucentLuiLogicalPath=\"Widget.lui\" LucentLuiDocumentVersion=\"same\" /><CompilerVisibleItemMetadata Include=\"AdditionalFiles\" MetadataName=\"LucentLuiLogicalPath\" /><CompilerVisibleItemMetadata Include=\"AdditionalFiles\" MetadataName=\"LucentLuiDocumentVersion\" />";
            await File.WriteAllTextAsync(
                referencedProject,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>{CoreMetadataReference}<AdditionalFiles Include=\"Widget.lui\""
                    + metadata
                    + "</ItemGroup></Project>"
            );
            await File.WriteAllTextAsync(
                referencedLui,
                "namespace Referenced; using static Lucent.Core.Components; public component Imported() { <Row /> }"
            );
            await File.WriteAllTextAsync(
                hostProject,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>{CoreMetadataReference}<ProjectReference Include=\"../referenced/Referenced.csproj\" /><AdditionalFiles Include=\"Widget.lui\""
                    + metadata
                    + "</ItemGroup></Project>"
            );
            await File.WriteAllTextAsync(
                hostLui,
                "namespace Host; using static Referenced.Components; internal component Host() { <Imported /> }"
            );
            using var context = await LuiProjectContext.LoadAsync(
                hostProject,
                CancellationToken.None
            );
            var referencedSource = await File.ReadAllTextAsync(referencedLui);
            var imported = referencedSource.IndexOf("Imported", StringComparison.Ordinal);
            var baseline = await context.PrepareRenameAsync(
                new Uri(referencedLui),
                imported,
                CancellationToken.None
            );
            Func<LuiCompilationResult, LuiCompilationResult> staleCompilation = result =>
            {
                if (
                    !String.Equals(
                        result.Identity.ProjectIdentity,
                        referencedProject,
                        StringComparison.Ordinal
                    )
                )
                    return result;
                var current = result.Identity;
                return WithFreshnessIdentity(
                    result,
                    new LuiFreshnessIdentity(
                        current.ProjectEpoch,
                        current.ProjectIdentity,
                        current.Document,
                        current.DocumentVersion,
                        current.CompilationGeneration + "-stale",
                        current.SiblingIndexGeneration,
                        current.LanguageVersion,
                        current.CompilerVersion,
                        current.ReferencesGeneration,
                        current.GlobalUsingsGeneration,
                        current.Options,
                        current.Defines,
                        current.RootNamespace
                    )
                );
            };
            var staleReferences = await context.ReferencesAsync(
                new Uri(referencedLui),
                imported,
                true,
                CancellationToken.None,
                transformGenerated: staleCompilation
            );
            var staleRename = await context.RenameAsync(
                new Uri(referencedLui),
                imported,
                "Renamed",
                CancellationToken.None,
                transformGenerated: staleCompilation
            );
            var stalePrepared = await context.PrepareRenameAsync(
                new Uri(referencedLui),
                imported,
                CancellationToken.None,
                staleCompilation
            );
            Assert(
                baseline is not null
                    && staleReferences is null
                    && staleRename is null
                    && stalePrepared is null,
                "duplicate logical Widget.lui documents accepted a stale non-document freshness identity: baseline="
                    + (baseline is null ? "null" : "present")
                    + " references="
                    + (staleReferences is null ? "null" : "present")
                    + " rename="
                    + (staleRename is null ? "null" : "present")
                    + " prepare="
                    + (stalePrepared is null ? "null" : "present")
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static async Task<DiagnosticValue[]> BuildDiagnosticsAsync(
        string projectPath,
        string currentPath
    )
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
            System.Collections.Immutable.ImmutableArray.Create(
                new LuiGenerator().AsSourceGenerator()
            ),
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

    static async Task ExpectExceptionAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException(
            "Expected " + typeof(TException).Name + " from the protocol operation."
        );
    }

    static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    static string Apply(string source, LuiFormatResult edit) =>
        source[..edit.Span.Start] + edit.NewText + source[edit.Span.End..];

    static LuiCompilationResult WithMap(
        LuiCompilationResult result,
        IEnumerable<LuiMapEntry> entries
    ) =>
        new(
            result.Identity,
            result.Source,
            new LuiSourceMap(result.Identity, entries.ToArray()),
            result.Diagnostics,
            result.ProjectionSource
        );

    static LuiCompilationResult WithIdentity(LuiCompilationResult result, string documentVersion)
    {
        var current = result.Identity;
        return WithFreshnessIdentity(
            result,
            new LuiFreshnessIdentity(
                current.ProjectEpoch,
                current.ProjectIdentity,
                current.Document,
                documentVersion,
                current.CompilationGeneration,
                current.SiblingIndexGeneration,
                current.LanguageVersion,
                current.CompilerVersion,
                current.ReferencesGeneration,
                current.GlobalUsingsGeneration,
                current.Options,
                current.Defines,
                current.RootNamespace
            )
        );
    }

    static LuiCompilationResult WithFreshnessIdentity(
        LuiCompilationResult result,
        LuiFreshnessIdentity identity
    )
    {
        return new LuiCompilationResult(
            identity,
            result.Source,
            new LuiSourceMap(identity, result.Map.Entries),
            result.Diagnostics,
            result.ProjectionSource
        );
    }

    static LuiSpan GeneratedTokenSpan(LuiCompilationResult result, int sourceStart, string text)
    {
        foreach (var entry in result.Map.FromSource(new LuiSpan(sourceStart, 0)))
        {
            if (
                entry.Hidden
                || entry.Source.Length != entry.Generated.Length
                || sourceStart < entry.Source.Start
                || sourceStart + text.Length > entry.Source.End
            )
                continue;
            var generatedStart = entry.Generated.Start + sourceStart - entry.Source.Start;
            if (result.ProjectionSource!.Substring(generatedStart, text.Length) == text)
                return new LuiSpan(generatedStart, text.Length);
        }
        throw new InvalidOperationException("Missing generated token span for " + text + ".");
    }

    static bool Intersects(LuiSpan left, LuiSpan right) =>
        left.Start < right.End && right.Start < left.End;

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

    static async Task BuildProjectAsync(string projectPath, string configuration)
    {
        await RunDotnetAsync(
            ["restore", projectPath, "--locked-mode"],
            $"Restoring '{projectPath}'"
        );
        await RunDotnetAsync(
            ["build", projectPath, "--no-restore", "-c", configuration, "-warnaserror"],
            $"Building '{projectPath}' for {configuration}"
        );
    }

    static async Task RunDotnetAsync(string[] arguments, string operation)
    {
        var localDotnet = Path.Combine(RepositoryRoot, ".dotnet", "dotnet.exe");
        var startInfo = new ProcessStartInfo(File.Exists(localDotnet) ? localDotnet : "dotnet")
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start: {operation}.");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            var timeoutOutput = await standardOutputTask;
            var timeoutError = await standardErrorTask;
            throw new InvalidOperationException(
                $"{operation} timed out after three minutes."
                    + Environment.NewLine
                    + timeoutOutput
                    + Environment.NewLine
                    + timeoutError
            );
        }
        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{operation} failed ({process.ExitCode})."
                    + Environment.NewLine
                    + standardOutput
                    + Environment.NewLine
                    + standardError
            );
        }
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
#if DEBUG
            const string configuration = "Debug";
#else
            const string configuration = "Release";
#endif
            var built = Path.GetFullPath(
                $"src/Lucent.Lui.LanguageServer/bin/{configuration}/net10.0/Lucent.Lui.LanguageServer.exe"
            );
            var process =
                Process.Start(
                    new ProcessStartInfo(
                        File.Exists(built)
                            ? built
                            : Path.Combine(
                                AppContext.BaseDirectory,
                                "Lucent.Lui.LanguageServer.exe"
                            )
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
            await output.WriteAsync(
                Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n")
            );
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
            await output.WriteAsync(
                Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n")
            );
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

        internal Task<JsonDocument> WaitForNotificationAsync(string method) =>
            ReadAsync(method).WaitAsync(TimeSpan.FromSeconds(15));

        private async Task<JsonDocument> ReadAsync(string? notificationMethod = null)
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
                if (message.RootElement.GetProperty("method").GetString() == notificationMethod)
                    return JsonDocument.Parse(message.RootElement.GetRawText());
            }
        }
    }
}
