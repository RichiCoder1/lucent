using System.Reflection;
using Lucent.Core;
using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class OwnedDeclarationTests
{
    private const string Api = """
namespace OwnedTrial;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lucent.Core;
public sealed class Resource(string name, bool fail = false) : IDisposable {
    public string Name => name;
    public void Dispose() {
        Harness.Events.Add("dispose:" + name);
        if (fail) throw new InvalidOperationException("cleanup:" + name);
    }
}
public sealed class AsyncResource : IAsyncDisposable {
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
public static class Harness {
    public static readonly List<string> Events = new();
    public static Resource Create(string name, bool fail = false) {
        Events.Add("create:" + name);
        return new Resource(name, fail);
    }
    public static Resource Throw() => throw new InvalidOperationException("initializer");
    public static Resource UnexpectedNull() => null!;
    public static ComponentRecipe FailChild() => ComponentRecipe.Create("failure",
        (context, root) => throw new InvalidOperationException("child"));
    public static Resource FromValue(int value) => Create(value.ToString());
    public static string Run() {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "owned");
        using var theme = new ThemeContext(composition.Root.Scope, new Theme("owned"));
        var recipe = Components.Trial();
        if (Events.Count != 0) throw new Exception("early initializer");
        try {
            var first = composition.Mount(composition.Root, theme, recipe);
            var second = composition.Mount(composition.Root, theme, recipe);
            first.Dispose();
            first.Dispose();
            second.Dispose();
        } catch (Exception error) {
            Events.Add("error:" + Describe(error));
        }
        return string.Join("|", Events);
    }
    private static string Describe(Exception error) => error is AggregateException aggregate
        ? string.Join("+", aggregate.InnerExceptions.Select(Describe))
        : error.Message;
}
""";

    [TestMethod]
    public void OwnedResourcesInitializePerMountAndDisposeOnceInReverseOrder()
    {
        var source = Source(
            """
[Owned] readonly Resource first = Harness.Create("first");
[Owned] readonly Resource second = Harness.Create(first.Name + "-second");
Setup(owner) { Harness.Events.Add("setup"); }
"""
        );
        Assert.AreEqual(
            "create:first|create:first-second|setup|create:first|create:first-second|setup|dispose:first-second|dispose:first|dispose:first-second|dispose:first",
            Run(source)
        );
    }

    [TestMethod]
    public void LaterInitializerSetupAndChildFailureRollBackOwnedResources()
    {
        foreach (
            var suffix in new[]
            {
                "readonly Resource failure = Harness.Throw();",
                "[Owned] readonly Resource failure = Harness.UnexpectedNull();",
                "Setup(owner) { throw new System.InvalidOperationException(\"setup\"); }",
            }
        )
        {
            var result = Run(
                Source("[Owned] readonly Resource first = Harness.Create(\"first\"); " + suffix)
            );
            StringAssert.StartsWith(result, "create:first|dispose:first|error:");
        }
        var child = Run(
            Source(
                "[Owned] readonly Resource first = Harness.Create(\"first\");",
                "<Column>{Harness.FailChild()}</Column>"
            )
        );
        StringAssert.StartsWith(child, "create:first|dispose:first|error:");
        StringAssert.Contains(child, "child");
    }

    [TestMethod]
    public void CleanupErrorsDoNotHideTheMountErrorOrSkipEarlierResources()
    {
        var result = Run(
            Source(
                """
[Owned] readonly Resource first = Harness.Create("first", true);
[Owned] readonly Resource second = Harness.Create("second", true);
readonly Resource failure = Harness.Throw();
"""
            )
        );
        StringAssert.StartsWith(
            result,
            "create:first|create:second|dispose:second|dispose:first|error:"
        );
        foreach (var expected in new[] { "initializer", "cleanup:first", "cleanup:second" })
            StringAssert.Contains(result, expected);
    }

    [TestMethod]
    public void OwnedDeclarationsRejectInvalidShapesAndPointAtAuthoredInitializers()
    {
        foreach (
            var declaration in new[]
            {
                "[Owned] Resource value = new Resource(\"x\");",
                "[Owned] readonly Resource a = new Resource(\"a\"), b = new Resource(\"b\");",
                "[Owned, Once] readonly Resource value = new Resource(\"x\");",
                "[Owned()] readonly Resource value = new Resource(\"x\");",
                "[Owned] readonly Resource value;",
            }
        )
        {
            var (result, _) = Compile(Source(declaration));
            Assert.IsFalse(result.Success, declaration);
        }
        foreach (
            var pair in new[]
            {
                ("int", "1"),
                ("AsyncResource", "new AsyncResource()"),
                ("Resource?", "new Resource(\"x\")"),
                ("Resource", "null!"),
                ("object", "new Resource(\"x\")"),
            }
        )
        {
            var source = Source($"[Owned] readonly {pair.Item1} value = {pair.Item2};");
            var (result, _) = Compile(source);
            var diagnostic = result.Diagnostics.Single(item => item.Id == "LUI2029");
            Assert.AreEqual(
                pair.Item2,
                source.Substring(diagnostic.Span.Start, diagnostic.Span.Length)
            );
        }
    }

    [TestMethod]
    public void KnownOwnerAwareFactoriesAndOwnedAliasesAreRejected()
    {
        foreach (
            var declaration in new[]
            {
                "[Owned] readonly Resource value = owner.Own(new Resource(\"x\"));",
                "[Owned] readonly Signal<int> value = owner.Signal(0, \"value\");",
                "[Owned] readonly ReactiveScope value = owner.CreateChild(\"value\");",
                "[Owned] readonly NavigationSession value = new(owner, RouteTable.Create([]));",
                "[Owned] readonly NavigationInteraction value = new(owner, new NavigationSession(owner, RouteTable.Create([])));",
                "[Owned] readonly Resource first = new Resource(\"x\"); [Owned] readonly Resource value = first;",
                "[Owned] readonly Resource first = new Resource(\"x\"); [Owned] readonly IDisposable value = (IDisposable)first;",
            }
        )
        {
            var (result, _) = Compile(Source(declaration));
            Assert.IsTrue(result.Diagnostics.Any(item => item.Id == "LUI2030"), Describe(result));
        }
        var (borrowed, _) = Compile(
            Source("readonly Signal<int> value = owner.Signal(0, \"value\");")
        );
        Assert.IsTrue(borrowed.Success, Describe(borrowed));
        var (factory, _) = Compile(
            Source(
                "[Owned] readonly Resource value = Harness.FromValue(owner.Signal(1, \"input\").Value);"
            )
        );
        Assert.IsTrue(factory.Success, Describe(factory));
    }

    [TestMethod]
    public void OwnedResourcesRemainGetOnlyAndForwardReadsStayInvalid()
    {
        var (assignment, _) = Compile(
            Source(
                """
[Owned] readonly Resource value = new Resource("x");
void Replace() { value = new Resource("y"); }
"""
            )
        );
        Assert.IsTrue(
            assignment.Diagnostics.Any(item => item.Id == "LUI2014"),
            Describe(assignment)
        );
        var (forward, _) = Compile(
            Source(
                """
[Owned] readonly Resource value = Harness.Create(later.Name);
readonly Resource later = new Resource("later");
"""
            )
        );
        Assert.IsFalse(forward.Success);
    }

    [TestMethod]
    public void OwnershipScaffoldingPreservesTheExactInitializerSourceMap()
    {
        const string initializer = "Harness.Create(\"mapped\")";
        var source = Source("[Owned] readonly Resource value = " + initializer + ";");
        var (result, _) = Compile(source);
        Assert.IsTrue(result.Success, Describe(result));
        var entry = result.Map.Entries.Single(item =>
            !item.Hidden
            && item.Kind == LuiMapKind.Expression
            && item.Source.Start == source.IndexOf(initializer, StringComparison.Ordinal)
            && item.Source.Length == initializer.Length
        );
        Assert.AreEqual(
            initializer,
            result.Source!.Substring(entry.Generated.Start, entry.Generated.Length)
        );
        Assert.IsTrue(
            result.Map.FromGenerated(entry.Generated).Any(item => item.Source.Equals(entry.Source))
        );
    }

    private static string Source(string declarations, string root = "<Text>owned</Text>") =>
        "namespace OwnedTrial; using System; using Lucent.Core; public component Trial() {\n"
        + declarations
        + "\n"
        + root
        + "\n}";

    private static (LuiCompilationResult Result, CSharpCompilation Compilation) Compile(
        string source
    )
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(ComponentRecipe).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "owned_" + Guid.NewGuid().ToString("N"),
            [
                CSharpSyntaxTree.ParseText(
                    "global using System.Linq;\n" + Api,
                    new CSharpParseOptions(LanguageVersion.Preview)
                ),
            ],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
        var result = LuiCompiler.Compile(
            LuiParser.Parse(source),
            compilation,
            new LuiFreshnessIdentity(
                "owned",
                "owned",
                new LuiDocumentIdentity("Owned.lui"),
                "1",
                "preview"
            )
        );
        return (result, compilation);
    }

    private static string Run(string source)
    {
        var (result, compilation) = Compile(source);
        Assert.IsTrue(result.Success, Describe(result));
        using var output = new MemoryStream();
        var emitted = compilation
            .AddSyntaxTrees(
                CSharpSyntaxTree.ParseText(
                    result.Source!,
                    new CSharpParseOptions(LanguageVersion.Preview)
                )
            )
            .Emit(output);
        Assert.IsTrue(emitted.Success, string.Join("\n", emitted.Diagnostics));
        return (string)
            Assembly
                .Load(output.ToArray())
                .GetType("OwnedTrial.Harness")!
                .GetMethod("Run")!
                .Invoke(null, null)!;
    }

    private static string Describe(LuiCompilationResult result) =>
        string.Join("\n", result.Diagnostics.Select(item => $"{item.Id}: {item.Message}"));
}
