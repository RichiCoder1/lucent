using System.Reflection;
using Lucent.Lui.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.Generator.Tests;

[TestClass]
public sealed class ComponentStateGeneratorTests
{
    private static readonly string[] InvalidShapeDiagnosticIds =
    [
        "LUI4101",
        "LUI4101",
        "LUI4102",
        "LUI4103",
        "LUI4104",
        "LUI4105",
        "LUI4106",
        "LUI4107",
    ];

    private const string RuntimeSource = """
#nullable enable
namespace GeneratedStateTrial;
using System;
using System.Collections.Generic;
using System.Threading;
using Lucent.Core;

[ComponentState]
public sealed partial class CounterState
{
    [State(2)] public partial int Count { get; set; }
    [State(3)] public partial int @class { get; set; }
    [State(float.NaN)] public partial float SingleNaN { get; set; }
    [State(float.PositiveInfinity)] public partial float SinglePositiveInfinity { get; set; }
    [State(float.NegativeInfinity)] public partial float SingleNegativeInfinity { get; set; }
    [State(double.NaN)] public partial double DoubleNaN { get; set; }
    [State(double.PositiveInfinity)] public partial double DoublePositiveInfinity { get; set; }
    [State(double.NegativeInfinity)] public partial double DoubleNegativeInfinity { get; set; }
    [State("ready")] public partial string Name { get; set; }
    [State] public partial int? Optional { get; set; }
    [State(Initializer = nameof(CreateItems))]
    public partial IReadOnlyList<string> Items { get; set; }

    private static IReadOnlyList<string> CreateItems(ComponentContext context)
    {
        Harness.Order.Add("initializer");
        context.OnDispose(() => Harness.Cleanups++);
        return new[] { "one", "two" };
    }

    partial void Initialize(ComponentContext context)
    {
        Harness.Order.Add("hook");
        Count += Items.Count;
    }

    public static CounterState Unattached() => new CounterState();
}

[ComponentState]
internal sealed partial class FailingState
{
    [State(Initializer = nameof(Fail))] public partial string Value { get; set; }
    private static string Fail(ComponentContext context)
    {
        context.OnDispose(() => Harness.FailedCleanups++);
        throw new InvalidOperationException("initializer failed");
    }
}

public static class Harness
{
    public static readonly List<string> Order = new();
    public static readonly List<CounterState> States = new();
    public static int Cleanups;
    public static int FailedCleanups;

    public static string Run()
    {
        var unattached = CounterState.Unattached();
        try { _ = unattached.Count; throw new Exception("unattached read succeeded"); }
        catch (InvalidOperationException) { }

        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "generated-state");
        using var theme = new ThemeContext(composition.Root.Scope, new Theme("generated-state"));
        var recipe = Component.Define<CounterState>("counter", (context, state) =>
        {
            States.Add(state);
            _ = TestComponents.Text(state.Name);
            _ = TestComponents.Text(() => state.Name);
            return ComponentRecipe.Create("counter-root", (mount, root) => root.Present(mount.Theme));
        });
        var first = composition.Mount(composition.Root, theme, recipe);
        var second = composition.Mount(composition.Root, theme, recipe);
        var before = TestComponents.Snapshots[0] + ":" + TestComponents.Live[0]();
        States[0].Name = "changed";
        var after = TestComponents.Snapshots[0] + ":" + TestComponents.Live[0]();
        if (States[0].Count != 4 || States[1].Count != 4 || States[0].@class != 3 || States[0].Optional is not null)
            throw new Exception("generated initialization was incorrect");
        if (!float.IsNaN(States[0].SingleNaN)
            || !float.IsPositiveInfinity(States[0].SinglePositiveInfinity)
            || !float.IsNegativeInfinity(States[0].SingleNegativeInfinity)
            || !double.IsNaN(States[0].DoubleNaN)
            || !double.IsPositiveInfinity(States[0].DoublePositiveInfinity)
            || !double.IsNegativeInfinity(States[0].DoubleNegativeInfinity))
            throw new Exception("generated nonfinite constants were incorrect");
        first.Dispose();
        try { _ = States[0].Count; throw new Exception("disposed read succeeded"); }
        catch (ObjectDisposedException) { }
        Exception? offThreadError = null;
        var offThread = new Thread(() =>
        {
            try { _ = States[1].Count; }
            catch (Exception error) { offThreadError = error; }
        });
        offThread.Start();
        offThread.Join();
        if (offThreadError is not InvalidOperationException)
            throw new Exception("off-thread read succeeded", offThreadError);
        second.Dispose();

        var failing = Component.Define<FailingState>("failing", (_, _) => throw new Exception("unreachable"));
        try { composition.Mount(composition.Root, theme, failing); throw new Exception("failure was hidden"); }
        catch (InvalidOperationException error) when (error.Message == "initializer failed") { }
        return before + ";" + after + ";" + string.Join(",", Order) + ";" + Cleanups + ":" + FailedCleanups;
    }
}

public static class TestComponents
{
    public static readonly List<string> Snapshots = new();
    public static readonly List<Func<string>> Live = new();
    public static ComponentRecipe Text(string value)
    {
        Snapshots.Add(value);
        return ComponentRecipe.Create("snapshot", static (_, _) => { });
    }
    public static ComponentRecipe Text(Func<string> value)
    {
        Live.Add(value);
        return ComponentRecipe.Create("live", static (_, _) => { });
    }
}
""";

    [TestMethod]
    public void GeneratesOwnedStateWithDeterministicInitializationAndRuntimeGuards()
    {
        var (run, compilation) = Generate(RuntimeSource);
        AssertNoErrors(run, compilation);
        var generated = run.Results.Single().GeneratedSources;
        Assert.AreEqual(2, generated.Length);
        var counter = generated
            .Single(source =>
                source.SourceText.ToString().Contains("CounterState", StringComparison.Ordinal)
            )
            .SourceText.ToString();
        Assert.IsTrue(
            counter.IndexOf("CounterState.Count", StringComparison.Ordinal)
                < counter.IndexOf("CounterState.Items", StringComparison.Ordinal)
                && counter.IndexOf("CounterState.Items", StringComparison.Ordinal)
                    < counter.IndexOf("CounterState.Name", StringComparison.Ordinal)
                && counter.IndexOf("CounterState.Name", StringComparison.Ordinal)
                    < counter.IndexOf("CounterState.Optional", StringComparison.Ordinal),
            "State cells were not emitted in ordinal property-name order."
        );
        using var output = new MemoryStream();
        var emission = compilation.Emit(output);
        Assert.IsTrue(emission.Success, String.Join(" | ", emission.Diagnostics));
        var assembly = Assembly.Load(output.ToArray());
        var value = (string)
            assembly.GetType("GeneratedStateTrial.Harness")!.GetMethod("Run")!.Invoke(null, null)!;
        Assert.AreEqual("ready:ready;ready:changed;initializer,hook,initializer,hook;2:1", value);
    }

    [TestMethod]
    public void ReportsStateShapeAndInitializationErrorsAtAuthoredDeclarations()
    {
        const string source = """
#nullable enable
using Lucent.Core;
[ComponentState] public partial class OpenState { [State] public partial int Value { get; set; } }
[ComponentState] public sealed partial class ConstructedState { private ConstructedState() {} [State] public partial int Value { get; set; } }
[ComponentState] public sealed partial class PropertyState { [State] public int Value { get; set; } }
[ComponentState] public sealed partial class RequiredState { [State] public partial string Value { get; set; } }
[ComponentState] public sealed partial class ConstantState { [State(1)] public partial string Value { get; set; } }
[ComponentState] public sealed partial class ConflictState { [State(1, Initializer = nameof(Create))] public partial int Value { get; set; } private static int Create(ComponentContext context) => 1; }
[ComponentState] public sealed partial class InitializerState { [State(Initializer = nameof(Create))] public partial string Value { get; set; } private static object Create(ComponentContext context) => "wrong"; }
[ComponentState] public sealed partial class AsyncHookState { [State] public partial int Value { get; set; } async partial void Initialize(ComponentContext context) { await System.Threading.Tasks.Task.Yield(); } }
""";
        var (run, _) = Generate(source);
        var diagnostics = run
            .Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)
            .ToArray();
        CollectionAssert.AreEquivalent(
            InvalidShapeDiagnosticIds,
            diagnostics.Select(item => item.Id).ToArray()
        );
        Assert.IsTrue(diagnostics.All(item => item.Location.IsInSource));
    }

    [TestMethod]
    public void GenerationIsStableAcrossUnchangedCompilations()
    {
        var first = Generate(RuntimeSource)
            .Run.Results.Single()
            .GeneratedSources.OrderBy(item => item.HintName, StringComparer.Ordinal)
            .Select(item => item.HintName + "\n" + item.SourceText)
            .ToArray();
        var second = Generate(RuntimeSource)
            .Run.Results.Single()
            .GeneratedSources.OrderBy(item => item.HintName, StringComparer.Ordinal)
            .Select(item => item.HintName + "\n" + item.SourceText)
            .ToArray();
        CollectionAssert.AreEqual(first, second);
    }

    [TestMethod]
    public void RejectsStandaloneStateGenerationForNamedLuiIdentity()
    {
        const string source = """
            using Lucent.Core;
            [ComponentState]
            public sealed partial class View {
                [State] public partial int Count { get; set; }
                [LucentComponent]
                public static partial ComponentRecipe Create();
            }
            """;

        var (run, _) = Generate(source);

        Assert.IsTrue(run.Diagnostics.Any(static diagnostic => diagnostic.Id == "LUI4108"));
        Assert.AreEqual(0, run.Results.Single().GeneratedSources.Length);
    }

    [TestMethod]
    public void DuplicateStateAttributesProduceAnAuthoredDiagnosticWithoutCrashing()
    {
        const string source = """
            using Lucent.Core;
            [ComponentState]
            public sealed partial class DuplicateState {
                [State, State] public partial int Count { get; set; }
            }
            """;

        var (run, _) = Generate(source);

        Assert.IsTrue(run.Diagnostics.Any(static diagnostic => diagnostic.Id == "LUI4102"));
        Assert.IsNull(run.Results.Single().Exception);
        Assert.AreEqual(0, run.Results.Single().GeneratedSources.Length);
    }

    [TestMethod]
    public void StandaloneStateMayExposeAnImplementedLucentComponentFactory()
    {
        const string source = """
            using Lucent.Core;
            [ComponentState]
            public sealed partial class View {
                [State] public partial int Count { get; set; }
                [LucentComponent]
                public static ComponentRecipe Create() => Component.Define<View>("view", static (_, _) => ComponentRecipe.Create("content", static (_, _) => { }));
            }
            """;

        var (run, compilation) = Generate(source);

        Assert.IsFalse(run.Diagnostics.Any(static diagnostic => diagnostic.Id == "LUI4108"));
        AssertNoErrors(run, compilation);
        Assert.AreEqual(1, run.Results.Single().GeneratedSources.Length);
    }

    private static (GeneratorDriverRunResult Run, Compilation Compilation) Generate(string source)
    {
        var compilation = CSharpCompilation.Create(
            "component-state-" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ComponentStateGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview)
        );
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        return (driver.GetRunResult(), updated);
    }

    private static void AssertNoErrors(GeneratorDriverRunResult run, Compilation compilation)
    {
        Assert.IsFalse(
            run.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error),
            String.Join(" | ", run.Diagnostics)
        );
        Assert.IsFalse(
            compilation.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error),
            String.Join(" | ", compilation.GetDiagnostics())
        );
    }

    private static MetadataReference[] References() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(
                MetadataReference.CreateFromFile(
                    Path.Combine(AppContext.BaseDirectory, "Lucent.Core.dll")
                )
            )
            .ToArray();
}
