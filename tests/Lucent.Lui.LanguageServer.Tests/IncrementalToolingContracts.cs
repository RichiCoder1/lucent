using Lucent.Lui.LanguageServer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.LanguageServer.Tests;

[TestClass]
public sealed class IncrementalToolingContracts
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task WarmProjectEvaluationAndUnrelatedDocumentCompilationAreReused()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-lsp-incremental-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "Sample.csproj");
            var firstPath = Path.Combine(root, "First.lui");
            var secondPath = Path.Combine(root, "Second.lui");
            await WriteProjectAsync(projectPath, root, ["First.lui", "Second.lui"]);
            var firstSource = Component("First");
            await File.WriteAllTextAsync(firstPath, firstSource);
            await File.WriteAllTextAsync(secondPath, Component("Second"));

            using var context = await LuiProjectContext.LoadAsync(
                projectPath,
                CancellationToken.None
            );
            var firstUri = new Uri(firstPath);
            var secondUri = new Uri(secondPath);
            var first = await context.CompileAsync(firstUri, CancellationToken.None);
            Assert.IsNotNull(first, "The first LUI document did not compile.");
            var buildsAfterFirst = context.EvaluationBuildCount;
            var second = await context.CompileAsync(secondUri, CancellationToken.None);
            Assert.IsNotNull(second, "The second LUI document did not compile.");
            Assert.AreEqual(
                buildsAfterFirst,
                context.EvaluationBuildCount,
                "Warm requests rebuilt the unchanged project evaluation."
            );

            context.ReplaceText(firstUri, firstSource + "\n\n");
            var secondAfterWhitespace = await context.CompileAsync(
                secondUri,
                CancellationToken.None
            );
            Assert.IsNotNull(secondAfterWhitespace);
            Assert.AreSame(
                second.Result,
                secondAfterWhitespace.Result,
                "An unrelated whitespace edit invalidated the sibling compilation result."
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildOutputWatchChangesDoNotReloadAndRelevantBatchReloadsOnce()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-lsp-watch-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "Sample.csproj");
            await WriteProjectAsync(projectPath, root, ["First.lui"]);
            await File.WriteAllTextAsync(Path.Combine(root, "First.lui"), Component("First"));
            using var context = await LuiProjectContext.LoadAsync(
                projectPath,
                CancellationToken.None
            );

            var documentUri = new Uri(Path.Combine(root, "First.lui"));
            var beforeReload = await context.CompileAsync(documentUri, CancellationToken.None);
            Assert.IsNotNull(beforeReload, "The watched document did not compile.");
            var before = context.CompletionEpoch;
            Assert.IsFalse(
                await context.ReloadIfRelevantAsync(
                    [
                        new Uri(Path.Combine(root, "bin", "Debug", "net10.0", "Sample.dll")),
                        new Uri(
                            Path.Combine(
                                root,
                                "obj",
                                "Debug",
                                "net10.0",
                                "Sample.csproj.FileListAbsolute.txt"
                            )
                        ),
                    ],
                    CancellationToken.None
                )
            );
            Assert.AreEqual(
                before,
                context.CompletionEpoch,
                "Build-output watcher noise changed project freshness."
            );

            Assert.IsTrue(
                await context.ReloadIfRelevantAsync(
                    [new Uri(projectPath), new Uri(Path.Combine(root, "Directory.Build.props"))],
                    CancellationToken.None
                )
            );
            Assert.AreEqual(
                before + 1,
                context.CompletionEpoch,
                "A watched-file batch caused more than one project reload."
            );
            var afterReload = await context.CompileAsync(documentUri, CancellationToken.None);
            Assert.IsNotNull(afterReload, "The watched document did not compile after reload.");
            Assert.AreNotSame(
                beforeReload.Result,
                afterReload.Result,
                "A relevant project reload reused a pre-reload compilation result."
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ResolvedReferenceUnderBinRemainsRelevant()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-lsp-reference-watch-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "Sample.csproj");
            var referencePath = Path.Combine(
                root,
                "bin",
                "Debug",
                "net10.0",
                "Lucent.Reference.dll"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(referencePath)!);
            File.Copy(typeof(Lucent.Lui.Compiler.LuiCompiler).Assembly.Location, referencePath);
            await WriteProjectAsync(projectPath, root, ["First.lui"], referencePath);
            await File.WriteAllTextAsync(Path.Combine(root, "First.lui"), Component("First"));

            using var context = await LuiProjectContext.LoadAsync(
                projectPath,
                CancellationToken.None
            );
            var before = context.CompletionEpoch;
            Assert.IsTrue(
                await context.ReloadIfRelevantAsync(
                    [new Uri(referencePath)],
                    CancellationToken.None
                ),
                "An explicitly resolved reference under bin was treated as build noise."
            );
            Assert.AreEqual(before + 1, context.CompletionEpoch);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task UnavailableLucentBuildToolsInReferencedProjectDoNotBreakRename()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-lsp-unresolved-analyzer-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "Sample.csproj");
            var documentPath = Path.Combine(root, "Widget.lui");
            var referencedRoot = Path.Combine(root, "Referenced");
            Directory.CreateDirectory(referencedRoot);
            var referencedProject = Path.Combine(referencedRoot, "Referenced.csproj");
            var helperPath = Path.Combine(referencedRoot, "Helpers.cs");
            var core = Path.GetFullPath("src/Lucent.Core/Lucent.Core.csproj");
            var missingAnalyzer = Path.Combine(
                root,
                "src",
                "Lucent.Lui.Generator",
                "bin",
                "Debug",
                "netstandard2.0",
                "Lucent.Lui.Generator.dll"
            );
            await File.WriteAllTextAsync(
                referencedProject,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><Analyzer Include=\"{missingAnalyzer}\" /><Analyzer Include=\"{Path.Combine(root, "Lucent.Lui.Compiler.dll")}\" /></ItemGroup></Project>"
            );
            await File.WriteAllTextAsync(
                projectPath,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><LangVersion>preview</LangVersion></PropertyGroup><ItemGroup><Compile Remove=\"Referenced/**/*.cs\" /><ProjectReference Include=\"{core}\" /><ProjectReference Include=\"{referencedProject}\" /><AdditionalFiles Include=\"Widget.lui\" /></ItemGroup></Project>"
            );
            await File.WriteAllTextAsync(
                helperPath,
                "namespace Sample; public static class Helpers { public static string Format(int value) => value.ToString(); }"
            );
            var source =
                "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; internal component Widget(int count) { <Text content={Helpers.Format(count)} /> }";
            await File.WriteAllTextAsync(documentPath, source);

            using var context = await LuiProjectContext.LoadAsync(
                projectPath,
                CancellationToken.None
            );
            var documentUri = new Uri(documentPath);
            var offset = source.IndexOf("Format", StringComparison.Ordinal);
            var rename = await context.RenameAsync(
                documentUri,
                offset,
                "Render",
                CancellationToken.None
            );
            Assert.IsNotNull(rename, "An unresolved analyzer reference broke LUI rename.");
            Assert.IsTrue(
                rename.Edits.Any(edit =>
                    edit.Uri == documentUri
                    && edit.Spans.Any(span =>
                        span.Start == offset && span.Length == "Format".Length
                    )
                ),
                "Rename did not retain the authored LUI expression span."
            );
            Assert.IsTrue(
                rename.Edits.Any(edit => edit.Uri == new Uri(helperPath)),
                "Rename did not reach the declaration in the referenced project."
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task UnrelatedUnresolvedAnalyzerDoesNotSuppressLuiDiagnostics()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-lsp-unrelated-analyzer-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "Sample.csproj");
            var documentPath = Path.Combine(root, "Widget.lui");
            var core = Path.GetFullPath("src/Lucent.Core/Lucent.Core.csproj");
            var missingAnalyzer = Path.Combine(root, "vendor", "ExternalAnalyzer.dll");
            await File.WriteAllTextAsync(
                projectPath,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><LangVersion>preview</LangVersion></PropertyGroup><ItemGroup><ProjectReference Include=\"{core}\" /><Analyzer Include=\"{missingAnalyzer}\" /><AdditionalFiles Include=\"Widget.lui\" /></ItemGroup></Project>"
            );
            var source =
                "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; internal component Widget() { <Missing /> }";
            await File.WriteAllTextAsync(documentPath, source);

            using var context = await LuiProjectContext.LoadAsync(
                projectPath,
                CancellationToken.None
            );
            var diagnostics = await context.DiagnosticsAsync(
                new Uri(documentPath),
                CancellationToken.None
            );
            Assert.IsNotNull(diagnostics);
            Assert.IsTrue(
                diagnostics.Any(diagnostic => diagnostic.Code == "LUI2001"),
                "An unrelated unresolved analyzer changed the normal LUI diagnostic evaluation."
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task CanceledEvaluationDoesNotBuildOrPublishObsoleteWork()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-lsp-cancel-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "Sample.csproj");
            var documentPath = Path.Combine(root, "First.lui");
            await WriteProjectAsync(projectPath, root, ["First.lui"]);
            await File.WriteAllTextAsync(documentPath, Component("First"));
            using var context = await LuiProjectContext.LoadAsync(
                projectPath,
                CancellationToken.None
            );
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();

            var canceledObserved = false;
            try
            {
                await context.CompileAsync(new Uri(documentPath), canceled.Token);
            }
            catch (OperationCanceledException)
            {
                canceledObserved = true;
            }
            Assert.IsTrue(canceledObserved, "Canceled compilation did not stop before evaluation.");
            Assert.AreEqual(0, context.EvaluationBuildCount);
            Assert.AreEqual(0, context.EvaluationDocumentReadCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task IssueBrowserSizedWarmCompilationReportsBoundedReuse()
    {
        const int documentCount = 10;
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-lsp-sized-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var documents = Enumerable
                .Range(0, documentCount)
                .Select(index => $"Document{index}.lui")
                .ToArray();
            var projectPath = Path.Combine(root, "Sample.csproj");
            await WriteProjectAsync(projectPath, root, documents);
            foreach (var document in documents)
                await File.WriteAllTextAsync(
                    Path.Combine(root, document),
                    Component(Path.GetFileNameWithoutExtension(document))
                );

            using var context = await LuiProjectContext.LoadAsync(
                projectPath,
                CancellationToken.None
            );
            var firstUri = new Uri(Path.Combine(root, documents[0]));
            var siblingUri = new Uri(Path.Combine(root, documents[^1]));
            var cold = System.Diagnostics.Stopwatch.StartNew();
            var first = await context.CompileAsync(firstUri, CancellationToken.None);
            cold.Stop();
            var warm = System.Diagnostics.Stopwatch.StartNew();
            var repeated = await context.CompileAsync(firstUri, CancellationToken.None);
            warm.Stop();
            Assert.IsNotNull(first);
            Assert.IsNotNull(repeated);
            Assert.AreSame(first.Result, repeated.Result);
            Assert.AreEqual(1, context.EvaluationBuildCount);
            Assert.AreEqual(documentCount, context.EvaluationDocumentReadCount);

            var sibling = await File.ReadAllTextAsync(siblingUri.LocalPath);
            context.ReplaceText(siblingUri, sibling + "\n\n");
            var afterEditTimer = System.Diagnostics.Stopwatch.StartNew();
            var afterEdit = await context.CompileAsync(firstUri, CancellationToken.None);
            afterEditTimer.Stop();
            Assert.IsNotNull(afterEdit);
            Assert.AreSame(first.Result, afterEdit.Result);
            Assert.AreEqual(2, context.EvaluationBuildCount);
            Assert.AreEqual(documentCount * 2, context.EvaluationDocumentReadCount);

            TestContext.WriteLine(
                $"IssueBrowserSizedEvaluation documents={documentCount}; "
                    + $"legacyReads={documentCount * 3}; "
                    + $"currentReads={context.EvaluationDocumentReadCount}; "
                    + $"coldMs={cold.ElapsedMilliseconds}; warmMs={warm.ElapsedMilliseconds}; "
                    + $"afterEditMs={afterEditTimer.ElapsedMilliseconds}"
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Component(string name) =>
        "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; internal component "
        + name
        + "() { <Row /> }";

    private static async Task WriteProjectAsync(
        string projectPath,
        string root,
        IReadOnlyList<string> documents,
        string? referencePath = null
    )
    {
        var core = Path.GetFullPath("src/Lucent.Core/Lucent.Core.csproj");
        var additional = String.Join(
            "",
            documents.Select(path => $"<AdditionalFiles Include=\"{path}\" />")
        );
        var reference = referencePath is null
            ? ""
            : $"<Reference Include=\"Lucent.Reference\"><HintPath>{referencePath}</HintPath></Reference>";
        await File.WriteAllTextAsync(
            projectPath,
            $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Sample</RootNamespace><LangVersion>preview</LangVersion></PropertyGroup><ItemGroup><ProjectReference Include=\"{core}\" />{reference}{additional}</ItemGroup></Project>"
        );
    }
}
