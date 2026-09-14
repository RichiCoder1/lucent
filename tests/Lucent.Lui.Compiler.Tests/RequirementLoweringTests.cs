using System.Reflection;
using Lucent.Core;
using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class RequirementLoweringTests
{
    private const string Api = """
namespace RequirementsTrial;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lucent.Core;
public sealed record Config(string Value);
public interface IStore : IDisposable { string Value { get; } }
public sealed class Store(int id) : IStore {
    public string Value => "store" + id;
    public void Dispose() => Harness.Events.Add("service-dispose:" + id);
}
public sealed class Owned(string value) : IDisposable {
    public void Dispose() => Harness.Events.Add("owned-dispose:" + value);
}
public static class Harness {
    public static readonly List<string> Events = new();
    public static string Record(string value) { Events.Add(value); return value; }
    public static string Run() {
        try { LucentApplication.CreateBuilder().UseHost(new Host()).Build().Run(new Lifecycle()); }
        catch (Exception error) { Events.Add("failed:" + error.Message); }
        return string.Join("|", Events);
    }
    private sealed class Host : IApplicationHost {
        public int Run(ApplicationSession session) {
            session.Start();
            Pump(session, () => session.Status.Phase == ApplicationPhase.Running || session.IsCompleted);
            if (!session.IsCompleted) {
                session.Composition.Flush(); session.Composition.Flush();
                session.RequestClose(); Pump(session, () => session.IsCompleted);
            }
            return 0;
        }
        private static void Pump(ApplicationSession session, Func<bool> done) {
            var timeout = Environment.TickCount64 + 5000;
            while (!done()) {
                session.ProcessEvents();
                if (!session.Composition.IsDisposed) session.Composition.Flush();
                if (Environment.TickCount64 > timeout) throw new TimeoutException();
                Thread.Sleep(1);
            }
        }
    }
    private sealed class Lifecycle : IApplicationLifecycle, IComponentServiceSource {
        private readonly List<Store> stores = new();
        private ComponentServiceBinding? binding;
        public ValueTask<ComponentRecipe> StartAsync(ApplicationSession session) {
            binding = session.CreateServiceBinding(this);
            ComponentRecipe recipe = Lucent.Core.Components.Column([Components.Trial(), Components.Trial()]);
            if (Events.Count != 0) throw new Exception("early initialization");
            return ValueTask.FromResult(binding.Attach(Context.Provide(new Config("context"), recipe)));
        }
        public T Resolve<T>() where T : class {
            if (typeof(T) != typeof(IStore)) throw new InvalidOperationException("unexpected service");
            var store = new Store(stores.Count + 1); stores.Add(store);
            Events.Add("resolve:" + store.Value);
            return (T)(object)store;
        }
        public ValueTask<bool> PrepareCloseAsync(CancellationToken cancellationToken) => ValueTask.FromResult(true);
        public ValueTask StopAsync() { binding!.StopAccepting(); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() {
            binding!.Revoke(); foreach (var store in stores) store.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
""";

    [TestMethod]
    public void RequirementsResolveBeforeInitializersAndRemainCachedPerMount()
    {
        var result = Run(
            Source(
                """
readonly string initialized = Harness.Record(config.Value + ":" + store.Value);
inject IStore store;
[Owned] readonly Owned owned = new(initialized);
context Config config;
Setup(owner) { Harness.Record("setup:" + store.Value); }
"""
            )
        );
        StringAssert.StartsWith(
            result,
            "resolve:store1|context:store1|setup:store1|resolve:store2|context:store2|setup:store2|"
        );
        Assert.AreEqual(
            2,
            result.Split('|').Count(item => item.StartsWith("resolve:", StringComparison.Ordinal))
        );
        Assert.AreEqual(
            2,
            result
                .Split('|')
                .Count(item => item.StartsWith("owned-dispose:", StringComparison.Ordinal))
        );
        StringAssert.EndsWith(result, "service-dispose:1|service-dispose:2");
    }

    [TestMethod]
    public void InjectionAloneStillCreatesOnePerMountStateObject()
    {
        var result = Run(Source("inject IStore store;", "<Text>{store.Value}</Text>"));
        Assert.AreEqual(
            "resolve:store1|resolve:store2|service-dispose:1|service-dispose:2",
            result
        );
    }

    [TestMethod]
    public void MissingContextFailsBeforeAnyServiceOrComponentInitialization()
    {
        var result = Run(
            Source(
                "inject IStore store; context Config config; readonly string started = Harness.Record(\"initialized\");"
            ),
            provideContext: false
        );
        StringAssert.StartsWith(result, "failed:");
        StringAssert.Contains(result, "config");
        Assert.IsFalse(result.Contains("resolve:", StringComparison.Ordinal), result);
        Assert.IsFalse(result.Contains("initialized", StringComparison.Ordinal), result);
    }

    [TestMethod]
    public void RequirementValidationUsesBoundSymbolsAndExactTypes()
    {
        foreach (
            var declaration in new[]
            {
                "context Config? value;",
                "context dynamic value;",
                "context Span<int> value;",
                "context int? value;",
            }
        )
            AssertDiagnostic(declaration, "LUI2031");
        foreach (
            var declaration in new[]
            {
                "inject int value;",
                "inject IServiceProvider value;",
                "inject ThemeContext value;",
                "inject IStore[] value;",
                "inject Func<IStore> value;",
                "inject Lazy<IStore> value;",
                "inject System.Threading.Tasks.Task<IStore> value;",
                "inject System.Collections.Generic.IEnumerable<IStore> value;",
            }
        )
            AssertDiagnostic(declaration, "LUI2032");
        AssertDiagnostic("context IStore a; inject IStore b;", "LUI2033");
        var alias = Compile(
            Source(
                "context StoreAlias a; context Config b;",
                imports: "using StoreAlias = RequirementsTrial.Config;"
            )
        );
        Assert.IsTrue(
            alias.Result.Diagnostics.Any(item => item.Id == "LUI2033"),
            Describe(alias.Result)
        );
        // A similarly named application type is not the framework's ThemeContext.
        var ordinary = Compile(
            Source("inject Other.ThemeContext value;"),
            "namespace Other { public sealed class ThemeContext {} }"
        );
        Assert.IsTrue(ordinary.Result.Success, Describe(ordinary.Result));
    }

    [TestMethod]
    public void BorrowedRequirementsCannotBeAssignedOrOwnedAgain()
    {
        AssertDiagnostic(
            "inject IStore store; [Owned] readonly IDisposable duplicate = store;",
            "LUI2030"
        );
        AssertDiagnostic(
            "context IStore store; [Owned] readonly IDisposable duplicate = (IDisposable)store;",
            "LUI2030"
        );
        var assigned = Compile(
            Source("inject IStore store; void Replace() { store = new Store(1); }")
        );
        Assert.IsFalse(assigned.Result.Success);
        Assert.IsNull(assigned.Result.Source);
    }

    [TestMethod]
    public void ProviderIsTransparentAndRejectsUnstableOrUnownedValues()
    {
        foreach (
            var declaration in new[]
            {
                "Config config = new(\"unstable\");",
                "[Once] Config config = new(\"unstable\");",
            }
        )
        {
            var result = Compile(
                Source(declaration, "<Provide value={config}><Text>child</Text></Provide>")
            );
            Assert.IsTrue(
                result.Result.Diagnostics.Any(item => item.Id == "LUI2034"),
                Describe(result.Result)
            );
        }
        foreach (
            var value in new[]
            {
                "new Owned(\"leak\")",
                "(object)new Owned(\"leak\")",
                "Environment.TickCount > 0 ? new Owned(\"a\") : new Owned(\"b\")",
                "borrowed ?? new Owned(\"fallback\")",
                "Environment.TickCount > 0 ? owned : new Owned(\"b\")",
            }
        )
        {
            var inline = Compile(
                Source(
                    "readonly Owned? borrowed = null; [Owned] readonly Owned owned = new(\"owner\");",
                    "<Provide value={" + value + "}><Text>child</Text></Provide>"
                )
            );
            Assert.IsTrue(
                inline.Result.Diagnostics.Any(item => item.Id == "LUI2036"),
                Describe(inline.Result)
            );
        }
        var owned = Compile(
            Source(
                "[Owned] readonly Owned value = new(\"owned\");",
                "<Provide value={value}><Text>child</Text></Provide>"
            )
        );
        Assert.IsTrue(owned.Result.Success, Describe(owned.Result));
        StringAssert.Contains(owned.Result.Source!, "global::Lucent.Core.Context.Provide(");
        var nullable = Compile(
            Source(
                "readonly Config? value = null;",
                "<Provide value={value}><Text>child</Text></Provide>"
            )
        );
        Assert.IsTrue(
            nullable.Result.Diagnostics.Any(item => item.Id == "LUI2035"),
            Describe(nullable.Result)
        );
    }

    [TestMethod]
    public void ProviderExpressionsRequireOneRootRecipe()
    {
        foreach (
            var declaration in new[]
            {
                "readonly ComponentContent children = ComponentContent.Empty;",
                "readonly ComponentContent children = [global::Lucent.Core.Components.Text(\"one\"), global::Lucent.Core.Components.Text(\"two\")];",
                "readonly ContentRecipe children = global::Lucent.Core.Components.Text(\"one\");",
            }
        )
        {
            var result = Compile(
                Source(declaration, "<Provide value={new Config(\"stable\")}>{children}</Provide>")
            );
            Assert.IsTrue(
                result.Result.Diagnostics.Any(item => item.Id == "LUI2037"),
                Describe(result.Result)
            );
            Assert.IsNull(result.Result.Source);
        }
        var valid = Compile(
            Source(
                "readonly ComponentRecipe child = global::Lucent.Core.Components.Text(\"one\");",
                "<Provide value={new Config(\"stable\")}>{child}</Provide>"
            )
        );
        Assert.IsTrue(valid.Result.Success, Describe(valid.Result));
    }

    [TestMethod]
    public void RequirementTypesAndNamesKeepAuthoredSourceMappings()
    {
        var source = Source(
            "context global::RequirementsTrial.Config config; inject IStore store;"
        );
        var (result, _) = Compile(source);
        Assert.IsTrue(result.Success, Describe(result));
        foreach (
            var text in new[] { "global::RequirementsTrial.Config", "config", "IStore", "store" }
        )
        {
            var start = source.IndexOf(text, StringComparison.Ordinal);
            Assert.IsTrue(
                result.Map.Entries.Any(entry =>
                    !entry.Hidden
                    && entry.Source.Start == start
                    && entry.Source.Length == text.Length
                    && result.Source!.Substring(entry.Generated.Start, entry.Generated.Length)
                        == text
                ),
                text
            );
        }
        StringAssert.Contains(result.Source!, "\"Requirements.lui\"");
        StringAssert.Contains(result.Source!, "global::RequirementsTrial.Config");
        StringAssert.Contains(result.Source!, "ComponentRequirementKind.Context");
        StringAssert.Contains(result.Source!, "ComponentRequirementKind.Inject");
    }

    [TestMethod]
    public void SeparateDocumentsKeepRequirementAndProviderPlanFieldsDistinct()
    {
        var firstSource = Source(
            "context Config config;",
            "<Provide value={config}><Text>{config.Value}</Text></Provide>"
        );
        var secondSource = Source(
                "context Config config;",
                "<Provide value={config}><Text>{config.Value}</Text></Provide>"
            )
            .Replace("Trial()", "Sibling()", StringComparison.Ordinal);
        var (first, compilation) = Compile(firstSource);
        var second = LuiCompiler.Compile(
            LuiParser.Parse(secondSource),
            compilation,
            new LuiFreshnessIdentity(
                "requirements",
                "requirements",
                new LuiDocumentIdentity("Sibling.lui"),
                "1",
                "preview"
            )
        );

        Assert.IsTrue(first.Success, Describe(first));
        Assert.IsTrue(second.Success, Describe(second));
        using var output = new MemoryStream();
        var emitted = compilation
            .AddSyntaxTrees(
                CSharpSyntaxTree.ParseText(
                    first.Source!,
                    new CSharpParseOptions(LanguageVersion.Preview)
                ),
                CSharpSyntaxTree.ParseText(
                    second.Source!,
                    new CSharpParseOptions(LanguageVersion.Preview)
                )
            )
            .Emit(output);
        Assert.IsTrue(emitted.Success, string.Join("\n", emitted.Diagnostics));
    }

    private static string Source(
        string declarations,
        string root = "<Text>requirements</Text>",
        string imports = ""
    ) =>
        "namespace RequirementsTrial; using System; using Lucent.Core; "
        + imports
        + " public component Trial() {\n"
        + declarations
        + "\n"
        + root
        + "\n}";

    [TestMethod]
    public void RequirementMetadataInvalidatesTheSignatureIndexButUnrelatedBodyEditsDoNot()
    {
        var source = Source("context Config config; inject IStore store;");
        var (_, compilation) = Compile(source);
        LuiProjectComponentIndex Index(string text) =>
            LuiProjectComponentIndex.Build(
                compilation,
                [
                    new LuiProjectDocument(
                        "C:/project/Requirements.lui",
                        "views/Requirements.lui",
                        text,
                        "1"
                    ),
                ]
            );
        var original = Index(source);
        Assert.AreEqual(0, original.Diagnostics.Count);
        Assert.AreEqual(
            original.Generation,
            Index(
                source.Replace(
                    "<Text>requirements</Text>",
                    "<Text>other body</Text>",
                    StringComparison.Ordinal
                )
            ).Generation
        );
        var changed = Index(
            source.Replace(
                "inject IStore store;",
                "context IStore store;",
                StringComparison.Ordinal
            )
        );
        Assert.AreNotEqual(original.Generation, changed.Generation);
        var method = original
            .Augment(compilation, "C:/project/Caller.lui")
            .GetTypeByMetadataName("RequirementsTrial.Components")!
            .GetMembers("Trial")
            .OfType<IMethodSymbol>()
            .Single();
        var attributes = method
            .GetAttributes()
            .Where(attribute => attribute.AttributeClass?.Name == "ComponentRequirementAttribute")
            .ToArray();
        Assert.AreEqual(2, attributes.Length);
        Assert.AreEqual(0, attributes[0].ConstructorArguments[1].Value);
        Assert.AreEqual(1, attributes[1].ConstructorArguments[1].Value);
        Assert.AreEqual("views/Requirements.lui", attributes[0].ConstructorArguments[3].Value);
    }

    private static void AssertDiagnostic(string declaration, string id)
    {
        var (result, _) = Compile(Source(declaration));
        Assert.IsTrue(
            result.Diagnostics.Any(item => item.Id == id),
            declaration + "\n" + Describe(result)
        );
        Assert.IsNull(result.Source);
    }

    private static (LuiCompilationResult Result, CSharpCompilation Compilation) Compile(
        string source,
        string extra = "",
        bool provideContext = true
    )
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(ComponentRecipe).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "requirements_" + Guid.NewGuid().ToString("N"),
            [
                CSharpSyntaxTree.ParseText(
                    provideContext
                        ? Api
                        : Api.Replace(
                            "Context.Provide(new Config(\"context\"), recipe)",
                            "recipe",
                            StringComparison.Ordinal
                        ),
                    new CSharpParseOptions(LanguageVersion.Preview)
                ),
                CSharpSyntaxTree.ParseText(extra, new CSharpParseOptions(LanguageVersion.Preview)),
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
                "requirements",
                "requirements",
                new LuiDocumentIdentity("Requirements.lui"),
                "1",
                "preview"
            )
        );
        return (result, compilation);
    }

    private static string Run(string source, bool provideContext = true)
    {
        var (result, compilation) = Compile(source, provideContext: provideContext);
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
                .GetType("RequirementsTrial.Harness")!
                .GetMethod("Run")!
                .Invoke(null, null)!;
    }

    private static string Describe(LuiCompilationResult result) =>
        string.Join("\n", result.Diagnostics.Select(item => $"{item.Id}: {item.Message}"));
}
