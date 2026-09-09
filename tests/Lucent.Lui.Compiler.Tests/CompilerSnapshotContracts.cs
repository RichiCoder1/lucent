using Lucent.Core;
using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class CompilerSnapshotContracts
{
    [TestMethod]
    public void RepeatedImmutableSnapshotsReuseFingerprintWorkAndKeepFreshnessInputs()
    {
        var source =
            "global using System; public static class Unrelated { "
            + string.Join(
                "\n",
                Enumerable.Range(0, 2000).Select(i => $"public static int M{i}() => {i};")
            )
            + " }";
        var tree = CSharpSyntaxTree.ParseText(source, path: "Unrelated.cs");
        var compilation = CreateCompilation(tree);
        var identity = Identity("1");
        var first = LuiCompiler.Snapshot(identity, compilation);
        _ = LuiCompiler.Snapshot(identity, compilation);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < 16; iteration++)
            _ = LuiCompiler.Snapshot(identity, compilation);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.IsTrue(
            allocated < 16 * 16_384,
            $"Repeated immutable snapshots allocated {allocated} bytes; whole-project fingerprint work was repeated."
        );

        var nextDocument = LuiCompiler.Snapshot(Identity("2"), compilation);
        Assert.AreEqual(first.CompilationGeneration, nextDocument.CompilationGeneration);
        Assert.AreNotEqual(first.DocumentVersion, nextDocument.DocumentVersion);
        var changed = LuiCompiler.Snapshot(
            identity,
            compilation.ReplaceSyntaxTree(
                tree,
                CSharpSyntaxTree.ParseText(
                    source.Replace("=> 1999;", "=> -1;"),
                    path: "Unrelated.cs"
                )
            )
        );
        Assert.AreNotEqual(first.CompilationGeneration, changed.CompilationGeneration);
        var changedGlobals = LuiCompiler.Snapshot(
            identity,
            compilation.ReplaceSyntaxTree(
                tree,
                CSharpSyntaxTree.ParseText(
                    source.Replace("using System;", "using System.IO;"),
                    path: "Unrelated.cs"
                )
            )
        );
        Assert.AreNotEqual(first.GlobalUsingsGeneration, changedGlobals.GlobalUsingsGeneration);
    }

    [TestMethod]
    public void DocumentDiagnosticsDoNotIncludeUnrelatedBodies()
    {
        var compilation = CreateCompilation(
            CSharpSyntaxTree.ParseText(
                """
public static class Unrelated {
    public static int Broken() => MissingSymbol;
    public static void Warning() { int unused = 1; }
}
""",
                path: "Unrelated.cs"
            )
        );
        var result = LuiCompiler.Compile(
            LuiParser.Parse(
                """
namespace DocumentDiagnostics;
public component Trial() { <Text content={() => MissingInDocument} /> }
"""
            ),
            compilation,
            Identity("1")
        );
        Assert.IsFalse(result.Success);
        Assert.IsTrue(
            result.Diagnostics.Any(d =>
                d.Message.Contains("MissingInDocument", StringComparison.Ordinal)
            ),
            string.Join("\n", result.Diagnostics.Select(d => d.Id + ": " + d.Message))
        );
        Assert.IsFalse(
            result.Diagnostics.Any(d =>
                d.Message.Contains("MissingSymbol", StringComparison.Ordinal)
                || d.Message.Contains("unused", StringComparison.Ordinal)
            )
        );
    }

    private static LuiFreshnessIdentity Identity(string version) =>
        new("snapshot", "snapshot", new LuiDocumentIdentity("Trial.lui"), version, "preview");

    private static CSharpCompilation CreateCompilation(SyntaxTree tree) =>
        CSharpCompilation.Create(
            "snapshot-contracts",
            [tree],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Append(
                    MetadataReference.CreateFromFile(typeof(ComponentRecipe).Assembly.Location)
                ),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
}
