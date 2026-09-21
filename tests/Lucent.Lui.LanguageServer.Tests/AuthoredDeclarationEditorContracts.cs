using System.Diagnostics;
using Lucent.Lui.Compiler;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.LanguageServer.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AuthoredDeclarationEditorContracts
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task OrdinaryDeclarationsHaveAuthoredEditorSemanticsAcrossLanguages(
        bool withComponent,
        bool positionalRecord
    )
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-declarations-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        var projectPath = Path.Combine(root, "Sample.csproj");
        var supportPath = Path.Combine(root, "Model.lui");
        var viewPath = Path.Combine(root, "View.lui");
        var consumerPath = Path.Combine(root, "Consumer.cs");
        var hostPath = Path.Combine(root, "Host.lui");
        var feed = Environment.GetEnvironmentVariable("LUCENT_LSP_PACKAGE_FEED");
        var version = Environment.GetEnvironmentVariable("LUCENT_LSP_PACKAGE_VERSION");
        var packageProof = feed is not null || version is not null;
        if (packageProof)
        {
            Assert.IsFalse(
                String.IsNullOrWhiteSpace(feed) || String.IsNullOrWhiteSpace(version),
                "Package proof requires both feed and version."
            );
            Assert.IsTrue(Directory.Exists(feed), "Package proof feed is missing.");
        }
        var support = """
            namespace Sample;
            public class Model {
                public string Label { get; set; } = "ready";
                public string Read() => Label.Trim();
            }
            """;
        if (positionalRecord)
            support =
                "namespace Sample; public record Model(string Label = \"ready\") { public string Read() => Label.Trim(); }";
        if (withComponent)
            support +=
                "\npublic component ModelView(Model model) { <Text content={model.Label} /> }";
        const string view = """
            namespace Sample;
            public component View(Model model) {
                <Text content={model.Label} />
            }
            """;
        try
        {
            await File.WriteAllTextAsync(
                projectPath,
                """
                <Project Sdk="
                """
                    + (
                        packageProof
                            ? "Microsoft.NET.Sdk;Lucent.Lui.Sdk/"
                                + System.Security.SecurityElement.Escape(version)
                            : "Microsoft.NET.Sdk"
                    )
                    + """
                        ">
                      <PropertyGroup>
                        <TargetFramework>net10.0</TargetFramework>
                        <LangVersion>preview</LangVersion>
                        <LucentLuiNamedComponents>true</LucentLuiNamedComponents>
                        <LucentLuiPreparedAuthoring>true</LucentLuiPreparedAuthoring>
                        <EnableDefaultLuiItems>false</EnableDefaultLuiItems>
                      </PropertyGroup>
                      <ItemGroup>
                    """
                    + (
                        packageProof
                            ? "<PackageReference Include=\"Lucent.Core\" Version=\"["
                                + System.Security.SecurityElement.Escape(version)
                                + "]\" />"
                            : LanguageServerTests.CoreMetadataReference
                    )
                    + """
                        <AdditionalFiles Include="*.lui" LucentLuiLogicalPath="%(Filename)%(Extension)" />
                        <CompilerVisibleItemMetadata Include="AdditionalFiles" MetadataName="LucentLuiLogicalPath" />
                        <CompilerVisibleProperty Include="LucentLuiNamedComponents" />
                        <CompilerVisibleProperty Include="LucentLuiPreparedAuthoring" />
                      </ItemGroup>
                    </Project>
                    """
            );
            if (packageProof)
            {
                var nugetConfig = Path.Combine(root, "NuGet.config");
                await File.WriteAllTextAsync(
                    nugetConfig,
                    $"<configuration><packageSources><clear/><add key=\"proof\" value=\"{System.Security.SecurityElement.Escape(Path.GetFullPath(feed!))}\"/></packageSources></configuration>"
                );
                await RestoreAsync(projectPath, nugetConfig);
            }
            await File.WriteAllTextAsync(supportPath, support);
            await File.WriteAllTextAsync(viewPath, view);
            const string host =
                "namespace Sample; public component Host() { <View model={new Model()} /> }";
            await File.WriteAllTextAsync(hostPath, host);
            await File.WriteAllTextAsync(
                consumerPath,
                "namespace Sample; public static class Consumer { public static string Read(Model model) => model.Label; public static Lucent.Core.ComponentRecipe Render() => View.Create(new Model()); }"
            );
            var timer = Stopwatch.StartNew();
            using var context = await LuiProjectContext.LoadAsync(
                projectPath,
                CancellationToken.None
            );
            var supportUri = new Uri(supportPath);
            var viewUri = new Uri(viewPath);
            var propertyOffset = support.IndexOf("Label", StringComparison.Ordinal);
            var hover = await context.HoverAsync(
                supportUri,
                propertyOffset + 1,
                CancellationToken.None
            );
            Assert.IsNotNull(hover, "An ordinary property in a support-only LUI file needs hover.");
            StringAssert.Contains(hover.Value, "Label");
            var coldMilliseconds = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            Assert.IsNotNull(
                await context.HoverAsync(supportUri, propertyOffset + 1, CancellationToken.None)
            );
            var warmMilliseconds = timer.Elapsed.TotalMilliseconds;
            var completion = await context.CompletionsAsync(
                supportUri,
                support.IndexOf("Trim", StringComparison.Ordinal),
                CancellationToken.None
            );
            Assert.IsTrue(
                completion.Any(item => item.Label == "Trim"),
                "Ordinary method bodies need member completion."
            );
            var signature = await context.SignatureHelpAsync(
                supportUri,
                support.IndexOf("Trim(", StringComparison.Ordinal) + 5,
                CancellationToken.None
            );
            Assert.IsNotNull(signature, "Ordinary method bodies need signature help.");
            var symbols = await context.DocumentSymbolsAsync(supportUri, CancellationToken.None);
            Assert.IsNotNull(symbols);
            Assert.IsTrue(symbols.Any(item => item.Name == "Model"));
            var diagnostics = await context.DiagnosticsAsync(supportUri, CancellationToken.None);
            Assert.IsNotNull(diagnostics);
            Assert.AreEqual(
                0,
                diagnostics.Count,
                String.Join("; ", diagnostics.Select(item => item.Message))
            );
            var viewOffset = view.IndexOf("Label", StringComparison.Ordinal);
            var definition = await context.DefinitionAsync(
                viewUri,
                viewOffset + 1,
                CancellationToken.None
            );
            Assert.IsNotNull(definition);
            Assert.AreEqual(supportUri, definition.Uri);
            Assert.AreEqual(new LuiSpan(propertyOffset, "Label".Length), definition.Span);
            var componentDefinition = await context.DefinitionAsync(
                new Uri(hostPath),
                host.IndexOf("<View", StringComparison.Ordinal) + 2,
                CancellationToken.None
            );
            Assert.IsNotNull(
                componentDefinition,
                "Named component tags need authored definitions."
            );
            Assert.AreEqual(viewUri, componentDefinition.Uri);
            Assert.AreEqual(
                new LuiSpan(view.IndexOf("View", StringComparison.Ordinal), 4),
                componentDefinition.Span
            );
            var componentReferences = await context.ReferencesAsync(
                viewUri,
                view.IndexOf("View", StringComparison.Ordinal) + 1,
                true,
                CancellationToken.None
            );
            Assert.IsNotNull(componentReferences);
            foreach (var uri in new[] { viewUri, new Uri(hostPath), new Uri(consumerPath) })
                Assert.IsTrue(
                    componentReferences.Locations.Any(item => item.Uri == uri),
                    "Named component references omitted " + uri
                );
            var componentRename = await context.RenameAsync(
                viewUri,
                view.IndexOf("View", StringComparison.Ordinal) + 1,
                "Card",
                CancellationToken.None
            );
            Assert.IsNotNull(componentRename);
            var componentRenameSources = new Dictionary<Uri, string>
            {
                [viewUri] = view,
                [new Uri(hostPath)] = host,
                [new Uri(consumerPath)] = await File.ReadAllTextAsync(consumerPath),
            };
            foreach (var pair in componentRenameSources)
            {
                var edit = componentRename.Edits.SingleOrDefault(item => item.Uri == pair.Key);
                Assert.IsNotNull(edit, "Named component rename omitted " + pair.Key);
                Assert.IsTrue(
                    edit.Spans.Any(span => pair.Value.Substring(span.Start, span.Length) == "View"),
                    "Named component rename did not select the type name in " + pair.Key
                );
                Assert.IsFalse(
                    edit.Spans.Any(span =>
                        pair.Value.Substring(span.Start, span.Length) == "Create"
                    ),
                    "Named component type rename must not rename the Create factory in " + pair.Key
                );
            }
            var hostDiagnostics = await context.DiagnosticsAsync(
                new Uri(hostPath),
                CancellationToken.None
            );
            Assert.IsNotNull(hostDiagnostics);
            Assert.AreEqual(
                0,
                hostDiagnostics.Count,
                String.Join("; ", hostDiagnostics.Select(item => item.Message))
            );
            var rename = await context.RenameAsync(
                supportUri,
                propertyOffset + 1,
                "Caption",
                CancellationToken.None
            );
            Assert.IsNotNull(rename);
            foreach (var uri in new[] { supportUri, viewUri, new Uri(consumerPath) })
                Assert.IsTrue(rename.Edits.Any(item => item.Uri == uri), "Rename omitted " + uri);
            var tokens = await context.SemanticTokensAsync(supportUri, CancellationToken.None);
            Assert.IsNotNull(tokens);
            Assert.IsTrue(tokens.Length > 0);
            var renamedSupport = support.Replace("Label", "Caption", StringComparison.Ordinal);
            timer.Restart();
            context.ReplaceText(supportUri, renamedSupport);
            var staleConsumer = await context.DiagnosticsAsync(viewUri, CancellationToken.None);
            Assert.IsNotNull(staleConsumer);
            Assert.IsTrue(
                staleConsumer.Any(item => item.Message.Contains("Label", StringComparison.Ordinal)),
                "An unsaved declaration rename must invalidate dependent views."
            );
            context.ReplaceText(
                viewUri,
                view.Replace("Label", "Caption", StringComparison.Ordinal)
            );
            var refreshedDefinition = await context.DefinitionAsync(
                viewUri,
                viewOffset + 1,
                CancellationToken.None
            );
            Assert.IsNotNull(refreshedDefinition);
            Assert.AreEqual(
                new LuiSpan(propertyOffset, "Caption".Length),
                refreshedDefinition.Span
            );
            var declarationMilliseconds = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            context.ReplaceText(
                supportUri,
                renamedSupport.Replace(
                    "Caption.Trim()",
                    "Caption.Trim().ToUpperInvariant()",
                    StringComparison.Ordinal
                )
            );
            Assert.IsNotNull(
                await context.HoverAsync(supportUri, propertyOffset + 1, CancellationToken.None)
            );
            var bodyMilliseconds = timer.Elapsed.TotalMilliseconds;
            Console.WriteLine(
                FormattableString.Invariant(
                    $"Authored editor timing: mixed={withComponent}; record={positionalRecord}; cold={coldMilliseconds:F1}ms; warm-hover={warmMilliseconds:F1}ms; declaration-edit={declarationMilliseconds:F1}ms; body-edit={bodyMilliseconds:F1}ms."
                )
            );
            context.ReplaceText(
                supportUri,
                support.Replace("string Label", "MissingType Label", StringComparison.Ordinal)
            );
            var invalid = await context.DiagnosticsAsync(supportUri, CancellationToken.None);
            Assert.IsNotNull(invalid);
            Assert.IsTrue(
                invalid.Any(item => item.Message.Contains("MissingType", StringComparison.Ordinal)),
                "Ordinary C# binding errors must reach the authored support file."
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task RestoreAsync(string projectPath, string nugetConfig)
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../")
        );
        var localDotnet = Path.Combine(repositoryRoot, ".dotnet", "dotnet.exe");
        var startInfo = new ProcessStartInfo(File.Exists(localDotnet) ? localDotnet : "dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "restore", projectPath, "--configfile", nugetConfig })
            startInfo.ArgumentList.Add(argument);
        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start package-proof restore.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        Assert.AreEqual(0, process.ExitCode, (await output) + Environment.NewLine + (await error));
    }
}
