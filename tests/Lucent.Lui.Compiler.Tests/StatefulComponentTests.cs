using System.Reflection;
using Lucent.Core;
using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class StatefulComponentTests
{
    private const string Source = """
namespace StatefulTrial;
using System;
using Lucent.Core;
using static StatefulTrial.TestComponents;
public component Trial(Func<string> initial) {
    int count = Constants.Start;
    [Once] string draft = initial();
    readonly string snapshot = initial();
    string label = count.ToString() + ":" + draft + ":" + snapshot + ":" + initial();

    void Toggle() {
        count += 1;
        draft = "edited";
    }

    Setup(owner) {
        Harness.Setups++;
        owner.OnDispose(() => Harness.Cleanups++);
    }

    <Probe toggle={Toggle}>{label}</Probe>
}
""";

    private const string Api = """
namespace StatefulTrial;
using System;
using System.Collections.Generic;
using Lucent.Core;
public static class Constants { public const int Start = 6 * 7; }
public sealed record Capture(Func<string> Read, Action Toggle, Element Root);
public static class TestComponents {
    [LucentComponent]
    public static ComponentRecipe Probe([DefaultContent] Func<string> content, Action toggle) =>
        ComponentRecipe.Create("probe", (_, root) => Harness.Mounts.Add(new Capture(content, toggle, root)));
}
public static class Harness {
    public static int Setups;
    public static int Cleanups;
    public static readonly List<Capture> Mounts = new();
    public static string Run() {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "state-trial");
        using var theme = new ThemeContext(composition.Root.Scope, new Theme("trial"));
        var input = composition.Root.Scope.Signal("first", "input");
        var recipe = Components.Trial(() => input.Value);
        if (Setups != 0) throw new Exception("Setup ran at recipe construction.");
        var first = composition.Mount(composition.Root, theme, recipe.Named("first"));
        var second = composition.Mount(composition.Root, theme, recipe.Named("second"));
        if (first.Children.Count != 0 || second.Children.Count != 0)
            throw new Exception("Setup added an observable wrapper root.");
        var a = Mounts[0];
        var b = Mounts[1];
        var before = a.Read() + "|" + b.Read();
        a.Toggle();
        input.Value = "next";
        var after = a.Read() + "|" + b.Read();
        first.Dispose();
        if (Cleanups != 1 || second.IsDisposed) throw new Exception("Disposal crossed mounts.");
        second.Dispose();
        return before + ";" + after + ";" + Setups + ":" + Cleanups;
    }
}
""";

    [TestMethod]
    public void MountedStateIsIsolatedAndDerivedValuesStayLive()
    {
        var (result, compilation) = Compile(Source, Api);
        Assert.IsTrue(result.Success, Describe(result));
        var emitted = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(
                result.Source!,
                new CSharpParseOptions(LanguageVersion.Preview)
            )
        );
        using var output = new MemoryStream();
        var emission = emitted.Emit(output);
        Assert.IsTrue(emission.Success, string.Join("\n", emission.Diagnostics));
        var assembly = Assembly.Load(output.ToArray());
        var value = (string)
            assembly.GetType("StatefulTrial.Harness")!.GetMethod("Run")!.Invoke(null, null)!;
        Assert.AreEqual(
            "42:first:first:first|42:first:first:first;43:edited:first:next|42:first:first:next;2:2",
            value
        );
    }

    [TestMethod]
    public void DerivedAssignmentsReportTheAuthoredTarget()
    {
        var source = Source.Replace("count += 1;", "label = \"override\";");
        var (result, _) = Compile(source, Api);
        Assert.IsFalse(result.Success);
        var target = source.IndexOf("label = \"override\"", StringComparison.Ordinal);
        Assert.IsTrue(
            result.Diagnostics.Any(d =>
                d.Span.Start <= target
                && d.Span.End >= target + "label".Length
                && (
                    d.Message.Contains("read", StringComparison.OrdinalIgnoreCase)
                    || d.Message.Contains("derived", StringComparison.OrdinalIgnoreCase)
                )
            ),
            Describe(result)
        );
    }

    [TestMethod]
    [DataRow("int total = count * 2;", "total++;", "LUI2013")]
    [DataRow("int total = count * 2;", "++total;", "LUI2013")]
    [DataRow("int total = count * 2;", "total--;", "LUI2013")]
    [DataRow("int total = count * 2;", "--total;", "LUI2013")]
    [DataRow("readonly int total = count;", "total++;", "LUI2014")]
    [DataRow("readonly int total = count;", "--total;", "LUI2014")]
    public void ReadOnlyStateIncrementReportsTheAuthoredTarget(
        string declaration,
        string statement,
        string diagnosticId
    )
    {
        var source = Source
            .Replace("int count = Constants.Start;", "int count = Constants.Start; " + declaration)
            .Replace("count += 1;", statement);
        var (result, _) = Compile(source, Api);
        Assert.IsFalse(result.Success);
        var diagnostic = result.Diagnostics.SingleOrDefault(d => d.Id == diagnosticId);
        Assert.IsNotNull(diagnostic, Describe(result));
        Assert.AreEqual("total", source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
        Assert.IsFalse(
            result.Diagnostics.Any(d => d.Message.Contains("__lui", StringComparison.Ordinal)),
            Describe(result)
        );
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AuthoredOwnerNamesKeepTheirResourceLifetime(bool declaredField)
    {
        var source = """
namespace AuthoredOwner;
using Lucent.Core;
public component Trial(ReactiveScope owner) {
    readonly Signal<int> value = owner.Signal(0, "external-value");
    Setup() { Harness.Captured = value; Harness.CapturedOwner = owner; }
    <Text>ready</Text>
}
""";
        if (declaredField)
            source = source.Replace(
                "Trial(ReactiveScope owner) {",
                "Trial(ReactiveScope external) { readonly ReactiveScope owner = external;"
            );
        const string api = """
namespace AuthoredOwner;
using Lucent.Core;
public static class Harness {
    public static Signal<int> Captured = null!;
    public static ReactiveScope CapturedOwner = null!;
    public static int Run() {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "authored-owner");
        using var theme = new ThemeContext(composition.Root.Scope, new Theme("trial"));
        using var external = graph.CreateScope("external");
        var mounted = composition.Mount(composition.Root, theme, Components.Trial(external));
        if (!object.ReferenceEquals(external, CapturedOwner))
            throw new System.Exception("The authored owner was not initialized.");
        mounted.Dispose();
        Captured.Value = 7;
        return Captured.Value;
    }
}
""";
        var (result, compilation) = Compile(source, api);
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
        var assembly = Assembly.Load(output.ToArray());
        Assert.AreEqual(
            7,
            (int)assembly.GetType("AuthoredOwner.Harness")!.GetMethod("Run")!.Invoke(null, null)!
        );
    }

    [TestMethod]
    public void ReactiveValuesCannotSilentlyFreezeAtStaticInputs()
    {
        const string source = """
namespace SnapshotBoundary;
public component Trial() {
    string value = "first";
    <TextField initialValue={value} />
}
""";
        var (result, _) = Compile(source, "");
        var diagnostic = result.Diagnostics.Single(d => d.Id == "LUI2016");
        Assert.AreEqual("value", source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
        var (snapshot, _) = Compile(source.Replace("string value", "readonly string value"), "");
        Assert.IsTrue(snapshot.Success, Describe(snapshot));

        var (forward, _) = Compile(
            Source.Replace(
                "int count = Constants.Start;",
                "readonly int initialCount = count; int count = Constants.Start;"
            ),
            Api
        );
        Assert.IsTrue(forward.Diagnostics.Any(d => d.Id == "LUI2018"), Describe(forward));
    }

    [TestMethod]
    public void MutableDerivedCollectionsReportTheirNonReactiveSemantics()
    {
        const string source = """
namespace MutableDerived;
using System.Collections.Generic;
using Lucent.Core;
public component Trial() {
    List<int> items = [];
    <Text>ready</Text>
}
""";
        var (result, _) = Compile(source, "");
        Assert.IsTrue(
            result.Success
                && result.Diagnostics.Any(diagnostic =>
                    diagnostic.Id == "LUI2017"
                    && source
                        .Substring(diagnostic.Span.Start, diagnostic.Span.Length)
                        .Contains("[]", StringComparison.Ordinal)
                ),
            Describe(result)
        );
        var snapshot = Compile(
            source.Replace("List<int> items", "readonly List<int> items"),
            ""
        ).Result;
        Assert.IsTrue(snapshot.Success, Describe(snapshot));
    }

    [TestMethod]
    public void StyleFactoryArgumentsCannotSilentlyCaptureChangingState()
    {
        const string source = """
namespace StyleSnapshot;
using Lucent.Core;
public component Trial() {
    bool selected = false;
    <Text style={RowStyle(selected)}>ready</Text>
}
style RowStyle(bool active) {
    when (active) { Opacity: .5f; }
}
""";
        var (result, _) = Compile(source, "");
        Assert.IsTrue(
            !result.Success
                && result.Diagnostics.Any(diagnostic =>
                    diagnostic.Id == "LUI2016"
                    && source
                        .Substring(diagnostic.Span.Start, diagnostic.Span.Length)
                        .Contains("selected", StringComparison.Ordinal)
                ),
            Describe(result)
        );

        const string capabilitySource = """
namespace StyleCapability;
using Lucent.Core;
public component Trial() {
    [Once] Capability capability = null!;
    Setup(owner) { capability = new Capability(); }
    <Text style={RowStyle(capability)}>ready</Text>
}
style RowStyle(Capability capability) {
    when (capability.Active) { Opacity: .5f; }
}
""";
        const string capabilityApi = """
namespace StyleCapability;
public sealed class Capability { public bool Active => true; }
""";
        var (capabilityResult, _) = Compile(capabilitySource, capabilityApi);
        Assert.IsTrue(
            capabilityResult.Success,
            "A live capability object should remain valid as a style factory argument:\n"
                + Describe(capabilityResult)
        );
    }

    [TestMethod]
    public void MemberAndSetupSourceSpansRemainVisibleToDebuggingAndDiagnostics()
    {
        const string source = """
namespace DebugSpans;
using Lucent.Core;
public component Trial() {
    int count = 0;
    void Toggle() { count += 1; }
    Setup(owner) { }
    <Button onInvoke={Toggle}>ready</Button>
}
""";
        var (result, _) = Compile(source, "");
        Assert.IsTrue(result.Success, Describe(result));
        var methodStart = source.IndexOf("void Toggle", StringComparison.Ordinal);
        var setupStart = source.IndexOf("Setup(owner)", StringComparison.Ordinal);
        Assert.IsTrue(
            result
                .Map.FromSource(new LuiSpan(methodStart, "void Toggle".Length))
                .Any(entry => !entry.Hidden)
                && result
                    .Map.FromSource(new LuiSpan(setupStart, "Setup(owner)".Length))
                    .Any(entry => !entry.Hidden)
                && result.Source!.Contains("#line (", StringComparison.Ordinal)
                && result.Source.Contains("Trial.lui", StringComparison.Ordinal),
            "member or Setup source maps/debug directives were hidden:\n" + result.Source
        );
    }

    [TestMethod]
    public void SetupLocalsDoNotEscapeToMarkup()
    {
        var source = Source
            .Replace("Harness.Setups++;", "string privateValue = \"private\"; Harness.Setups++;")
            .Replace("{label}</Probe>", "{privateValue}</Probe>");
        var (result, _) = Compile(source, Api);
        Assert.IsFalse(result.Success);
        Assert.IsTrue(
            result.Diagnostics.Any(d =>
                d.Message.Contains("privateValue", StringComparison.Ordinal)
            ),
            Describe(result)
        );
    }

    [TestMethod]
    public void OwnerlessSetupUsesAHiddenOwnerParameter()
    {
        const string source = """
namespace OwnerlessSetup;
using Lucent.Core;
public component Probe() {
    Setup() { Harness.Setups++; }
    <Text>ready</Text>
}
""";
        const string api = """
namespace OwnerlessSetup;
using Lucent.Core;
public static class Harness
{
    public static int Setups;
    public static string Run()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "ownerless-setup");
        using var theme = new ThemeContext(composition.Root.Scope, new Theme("ownerless-setup"));
        composition.Mount(composition.Root, theme, Components.Probe());
        return Setups.ToString();
    }
}
""";
        var (result, compilation) = Compile(source, api);
        Assert.IsTrue(result.Success, Describe(result));
        Assert.IsTrue(
            result.Source!.Contains(
                "private void Setup(global::Lucent.Core.ReactiveScope",
                StringComparison.Ordinal
            ),
            Describe(result)
        );
        var emitted = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(
                result.Source!,
                new CSharpParseOptions(LanguageVersion.Preview)
            )
        );
        using var output = new MemoryStream();
        var emission = emitted.Emit(output);
        Assert.IsTrue(emission.Success, string.Join("\n", emission.Diagnostics));
        var assembly = Assembly.Load(output.ToArray());
        Assert.AreEqual(
            "1",
            (string)
                assembly.GetType("OwnerlessSetup.Harness")!.GetMethod("Run")!.Invoke(null, null)!
        );
    }

    [TestMethod]
    public void SnapshotInitializersAllowDeferredReadsAndNameof()
    {
        var source = Source.Replace(
            "int count = Constants.Start;",
            """
[Once] Func<int> read = () => count;
readonly string initialName = nameof(count);
int count = Constants.Start;
"""
        );
        var (result, _) = Compile(source, Api);
        Assert.IsTrue(result.Success, Describe(result));
    }

    [TestMethod]
    public void ExternalValueExpressionsUseLiveScalarOverloads()
    {
        const string source = """
namespace ExternalTrial;
using Lucent.Core;
public component Trial(Signal<string> value) { <Text>{value.Value}</Text> }
""";
        const string api = """
namespace ExternalTrial;
using Lucent.Core;
public static class Harness {
    public static bool Run() {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "external-text");
        using var theme = new ThemeContext(composition.Root.Scope, new Theme("trial"));
        var value = composition.Root.Scope.Signal("before-live-update", "value");
        composition.Mount(composition.Root, theme, Components.Trial(value));
        graph.Drain();
        var before = composition.SemanticSnapshot();
        value.Value = "after-live-update";
        graph.Drain();
        var after = composition.SemanticSnapshot();
        return HasName(before, "before-live-update") && HasName(after, "after-live-update")
            && !HasName(after, "before-live-update");
    }
    private static bool HasName(SemanticSnapshot node, string expected) {
        if (node is null) return false;
        if (node.Name == expected) return true;
        foreach (var child in node.Children)
            if (HasName(child, expected)) return true;
        return false;
    }
}
""";
        var (result, compilation) = Compile(source, api);
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
        var assembly = Assembly.Load(output.ToArray());
        Assert.IsTrue(
            (bool)assembly.GetType("ExternalTrial.Harness")!.GetMethod("Run")!.Invoke(null, null)!,
            "External signal changes did not reach the mounted text semantics.\n" + result.Source
        );
    }

    [TestMethod]
    public void DeclaredTypesContextualizeNullNewAndMethodGroups()
    {
        var source = Source.Replace(
            "int count = Constants.Start;",
            """
string? optional = null;
[Once] object instance = new();
object derivedInstance = new();
[Once] Action callback = Toggle;
int count = Constants.Start;
"""
        );
        var (result, compilation) = Compile(source, Api);
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
    }

    [TestMethod]
    public void UnsupportedFieldSemanticsAreNotSilentlyDiscarded()
    {
        foreach (
            var declaration in new[]
            {
                "static int count = 0;",
                "const int count = 0;",
                "[Obsolete] int count = 0;",
                "[Once] readonly int count = 0;",
            }
        )
        {
            var source = Source.Replace("int count = Constants.Start;", declaration);
            var (result, _) = Compile(source, Api);
            Assert.IsFalse(
                result.Success,
                "Unsupported declaration was silently accepted: " + declaration
            );
        }
    }

    private static (LuiCompilationResult Result, CSharpCompilation Compilation) Compile(
        string source,
        string api
    )
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(ComponentRecipe).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "stateful_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(api, new CSharpParseOptions(LanguageVersion.Preview))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        var result = LuiCompiler.Compile(
            LuiParser.Parse(source),
            compilation,
            new LuiFreshnessIdentity(
                "stateful",
                "stateful",
                new LuiDocumentIdentity("Trial.lui"),
                "1",
                "preview"
            )
        );
        return (result, compilation);
    }

    private static string Describe(LuiCompilationResult result) =>
        string.Join("\n", result.Diagnostics.Select(d => $"{d.Id}@{d.Span.Start}: {d.Message}"))
        + "\n"
        + result.Source;
}
