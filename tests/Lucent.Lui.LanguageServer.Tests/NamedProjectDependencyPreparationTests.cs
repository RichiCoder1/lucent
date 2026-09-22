using System.Collections.Immutable;
using System.Security;
using Lucent.Lui.Preparation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.LanguageServer.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NamedProjectDependencyPreparationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CurrentNamedDependenciesPrepareColdDiamondAndUnsavedChanges(
        bool includeOldEmitter
    )
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-named-dependencies-" + Guid.NewGuid().ToString("N")
        );
        var sharedRoot = Path.Combine(root, "shared");
        var leftRoot = Path.Combine(root, "left");
        var rightRoot = Path.Combine(root, "right");
        var hostRoot = Path.Combine(root, "host");
        foreach (var directory in new[] { sharedRoot, leftRoot, rightRoot, hostRoot })
            Directory.CreateDirectory(directory);
        var sharedProject = Path.Combine(sharedRoot, "Shared.csproj");
        var sharedLui = Path.Combine(sharedRoot, "Shared.lui");
        var leftProject = Path.Combine(leftRoot, "Left.csproj");
        var leftLui = Path.Combine(leftRoot, "Left.lui");
        var rightProject = Path.Combine(rightRoot, "Right.csproj");
        var rightLui = Path.Combine(rightRoot, "Right.lui");
        var hostProject = Path.Combine(hostRoot, "Host.csproj");
        var hostLui = Path.Combine(hostRoot, "Host.lui");
        const string shared = """
            namespace Shared;
            using Lucent.Core;
            public static partial class StaleMarker { }
            public record SharedModel(string Label);
            public component SharedCard(SharedModel model) {
                string Read() => model.Label;
                <Text content={Read()} />
            }
            """;
        const string left = """
            namespace Left;
            using Shared;
            public component LeftCard() {
                <SharedCard model={new SharedModel("left")} />
            }
            """;
        const string right = """
            namespace Right;
            using Shared;
            public component RightCard() {
                <SharedCard model={new SharedModel("right")} />
            }
            """;
        const string host = """
            namespace Host;
            using Left;
            using Right;
            public component HostCard() {
                <Column><LeftCard /><RightCard /></Column>
            }
            """;
        try
        {
            string? emitter = null;
            if (includeOldEmitter)
                emitter = LuiPreparedEmitterCompiler
                    .Compile(
                        new LuiPreparedPayload(
                            ImmutableArray<LuiPreparedSource>.Empty,
                            [
                                new LuiPreparedSource(
                                    "Old.Shared.g.cs",
                                    "namespace Shared; public static partial class StaleMarker { }",
                                    sharedLui
                                ),
                            ]
                        ),
                        Path.Combine(Path.GetTempPath(), "lucent-lui-test-old-emitters")
                    )
                    .Path;

            await File.WriteAllTextAsync(
                sharedProject,
                Project(
                    "<AdditionalFiles Include=\"Shared.lui\" />"
                        + (
                            emitter is null
                                ? ""
                                : "<Analyzer Include=\"" + SecurityElement.Escape(emitter) + "\" />"
                        )
                )
            );
            await File.WriteAllTextAsync(
                leftProject,
                Project(
                    "<ProjectReference Include=\"../shared/Shared.csproj\" />"
                        + "<AdditionalFiles Include=\"Left.lui\" />"
                )
            );
            await File.WriteAllTextAsync(
                rightProject,
                Project(
                    "<ProjectReference Include=\"../shared/Shared.csproj\" />"
                        + "<AdditionalFiles Include=\"Right.lui\" />"
                )
            );
            await File.WriteAllTextAsync(
                hostProject,
                Project(
                    "<ProjectReference Include=\"../left/Left.csproj\" />"
                        + "<ProjectReference Include=\"../right/Right.csproj\" />"
                        + "<AdditionalFiles Include=\"Host.lui\" />"
                )
            );
            await File.WriteAllTextAsync(sharedLui, shared);
            await File.WriteAllTextAsync(leftLui, left);
            await File.WriteAllTextAsync(rightLui, right);
            await File.WriteAllTextAsync(hostLui, host);

            using var context = await LuiProjectContext.LoadAsync(
                hostProject,
                CancellationToken.None
            );
            await AssertCleanAsync(context, hostLui);
            await AssertCleanAsync(context, leftLui);
            await AssertCleanAsync(context, rightLui);

            var withoutMarker = shared.Replace(
                "public static partial class StaleMarker { }",
                "",
                StringComparison.Ordinal
            );
            context.ReplaceText(new Uri(sharedLui), withoutMarker);
            context.ReplaceText(
                new Uri(leftLui),
                left.Replace(
                    "public component",
                    "public sealed class UsesStale : StaleMarker { }\npublic component",
                    StringComparison.Ordinal
                )
            );
            await AssertDiagnosticAsync(context, leftLui, "StaleMarker");
            context.ReplaceText(new Uri(sharedLui), shared);
            context.ReplaceText(new Uri(leftLui), left);
            await AssertCleanAsync(context, leftLui);

            var renamedShared = shared.Replace(
                "SharedModel",
                "RenamedModel",
                StringComparison.Ordinal
            );
            context.ReplaceText(new Uri(sharedLui), renamedShared);
            await AssertDiagnosticAsync(context, leftLui, "SharedModel");
            context.ReplaceText(
                new Uri(leftLui),
                left.Replace("SharedModel", "RenamedModel", StringComparison.Ordinal)
            );
            context.ReplaceText(
                new Uri(rightLui),
                right.Replace("SharedModel", "RenamedModel", StringComparison.Ordinal)
            );
            await AssertCleanAsync(context, hostLui);

            var changedType = shared.Replace(
                "SharedModel(string Label)",
                "SharedModel(int Label)",
                StringComparison.Ordinal
            );
            context.ReplaceText(new Uri(sharedLui), changedType);
            context.ReplaceText(new Uri(leftLui), left);
            context.ReplaceText(new Uri(rightLui), right);
            await AssertDiagnosticAsync(context, rightLui, "string");
            context.ReplaceText(
                new Uri(leftLui),
                left.Replace(
                    "new SharedModel(\"left\")",
                    "new SharedModel(1)",
                    StringComparison.Ordinal
                )
            );
            context.ReplaceText(
                new Uri(rightLui),
                right.Replace(
                    "new SharedModel(\"right\")",
                    "new SharedModel(2)",
                    StringComparison.Ordinal
                )
            );
            await AssertCleanAsync(context, hostLui);

            context.ReplaceText(
                new Uri(sharedLui),
                shared[..shared.IndexOf("public component", StringComparison.Ordinal)]
            );
            await AssertDiagnosticAsync(context, leftLui, "SharedCard");
            context.ReplaceText(new Uri(sharedLui), shared);
            context.ReplaceText(new Uri(leftLui), left);
            context.ReplaceText(new Uri(rightLui), right);
            await AssertCleanAsync(context, hostLui);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        static string Project(string items) =>
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>"
            + "<TargetFramework>net10.0</TargetFramework>"
            + "<LangVersion>preview</LangVersion>"
            + "<LucentLuiNamedComponents>true</LucentLuiNamedComponents>"
            + "<LucentLuiPreparedAuthoring>true</LucentLuiPreparedAuthoring>"
            + "</PropertyGroup><ItemGroup>"
            + LanguageServerTests.CoreMetadataReference
            + items
            + "<CompilerVisibleProperty Include=\"LucentLuiNamedComponents\" />"
            + "<CompilerVisibleProperty Include=\"LucentLuiPreparedAuthoring\" />"
            + "</ItemGroup></Project>";
    }

    [TestMethod]
    public async Task UnsavedEditorConfigChangesNamedPreparationGeneration()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lucent-named-editorconfig-" + Guid.NewGuid().ToString("N")
        );
        var projectRoot = Path.Combine(root, "project");
        var sourceRoot = Path.Combine(root, "linked");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(sourceRoot);
        var projectPath = Path.Combine(projectRoot, "Sample.csproj");
        var luiPath = Path.Combine(sourceRoot, "Card.lui");
        var editorConfigPath = Path.Combine(sourceRoot, ".editorconfig");
        const string source = """
            namespace Sample;
            using Lucent.Core;
            public component Card() { <Text content={"ready"} /> }
            """;
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
            await File.WriteAllTextAsync(luiPath, source);
            await File.WriteAllTextAsync(
                editorConfigPath,
                "root = true\n\n[*.lui]\nlucent_lui_declaration_order = none\n"
            );

            using var context = await LuiProjectContext.LoadAsync(
                projectPath,
                CancellationToken.None
            );
            var initial = await context.CompileAsync(new Uri(luiPath), CancellationToken.None);
            Assert.IsNotNull(initial);
            context.ReplaceText(
                new Uri(editorConfigPath),
                "root = true\n\n[*.lui]\nlucent_lui_declaration_order = component_first\n"
            );
            var changed = await context.CompileAsync(new Uri(luiPath), CancellationToken.None);
            Assert.IsNotNull(changed);
            Assert.AreNotEqual(
                initial.Result.Identity.SiblingIndexGeneration,
                changed.Result.Identity.SiblingIndexGeneration,
                "An unsaved EditorConfig change did not invalidate named preparation freshness."
            );
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AssertCleanAsync(LuiProjectContext context, string path)
    {
        var diagnostics = await context.DiagnosticsAsync(new Uri(path), CancellationToken.None);
        Assert.IsNotNull(diagnostics);
        Assert.AreEqual(
            0,
            diagnostics.Count,
            String.Join(" | ", diagnostics.Select(item => item.Code + ": " + item.Message))
        );
    }

    private static async Task AssertDiagnosticAsync(
        LuiProjectContext context,
        string path,
        string expected
    )
    {
        var diagnostics = await context.DiagnosticsAsync(new Uri(path), CancellationToken.None);
        Assert.IsNotNull(diagnostics);
        Assert.IsTrue(
            diagnostics.Any(item => item.Message.Contains(expected, StringComparison.Ordinal)),
            String.Join(" | ", diagnostics.Select(item => item.Code + ": " + item.Message))
        );
    }
}
