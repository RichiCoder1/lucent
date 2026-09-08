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
