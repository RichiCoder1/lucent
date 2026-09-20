using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lucent.Lui.Generator.Tests;

[TestClass]
public sealed class RouteGeneratorTests
{
    private static readonly int[] ParentCaptureSlots = [0, 2];
    private static readonly int[] ChildCaptureSlots = [1];
    private static readonly string[] ExpectedInvalidParameterDiagnostics = ["LUI4204", "LUI4203"];
    private const string ValidSource = """
        using System;
        using Lucent.Core;

        namespace Sample;

        public enum ProjectTab { Summary, Activity }

        [LucentRouteModule(RouteFallbackPolicy.Reject)]
        public static partial class AppRoutes { }

        [LucentRoute(typeof(AppRoutes), "/projects/{projectId}?tab={tab}", Id = "project")]
        public readonly record struct ProjectRoute(Guid ProjectId, ProjectTab Tab = ProjectTab.Summary);

        [LucentRoute(typeof(AppRoutes), "/projects/{projectId}/issues/{issueId}?tab={tab}", Id = "issue", Parent = typeof(ProjectRoute))]
        public readonly record struct IssueRoute(Guid ProjectId, int IssueId, ProjectTab Tab = ProjectTab.Summary);
        """;

    [TestMethod]
    public void GeneratesTypedFactoriesAndClosedAncestorProviders()
    {
        var generated = Generate(ValidSource, "routes/AppRoutes.cs");
        AssertNoErrors(generated);
        var source = generated.Run.Results.Single().GeneratedSources.Single().SourceText.ToString();
        StringAssert.Contains(
            source,
            "RouteReference Issue(global::System.Guid projectId, int issueId, global::Sample.ProjectTab tab = global::Sample.ProjectTab.Summary)"
        );
        StringAssert.Contains(source, "RouteContext<global::Sample.ProjectRoute>");
        StringAssert.Contains(source, "static (definition, match, live)");
        StringAssert.Contains(source, "static (context, content)");
        StringAssert.Contains(source, "RouteContext<global::Sample.ProjectRoute>(definition");
        StringAssert.Contains(source, ">)context, content))");
        StringAssert.Contains(source, "new int[] { 0, 2 }");
        StringAssert.Contains(source, "new int[] { 1 }");
        StringAssert.Contains(
            source,
            "new global::Lucent.Core.RouteDeclarationSource(\"routes/AppRoutes.cs\""
        );
    }

    [TestMethod]
    public void ProjectedRoutesRetainAuthoredOriginAcrossGeneratorHosts()
    {
        var source = "#line 1 \"routes/AppRoutes.lui\"\n" + ValidSource;
        var preparation = Generate(source, "preparation/Declarations.g.cs");
        var final = Generate(source, "final/ContentAddressedEmitter/Declarations.g.cs");
        AssertNoErrors(preparation);
        AssertNoErrors(final);
        var preparedText = preparation
            .Run.Results.Single()
            .GeneratedSources.Single()
            .SourceText.ToString();
        var finalText = final.Run.Results.Single().GeneratedSources.Single().SourceText.ToString();
        Assert.AreEqual(preparedText, finalText);
        StringAssert.Contains(finalText, "RouteDeclarationSource(\"routes/AppRoutes.lui\"");
        Assert.IsFalse(finalText.Contains("Declarations.g.cs", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ComponentMappingInvokesStaticFactoryAndRejectsForeignLevels()
    {
        var generated = Generate(
            """
            using System;
            using Lucent.Core;
            namespace Sample;
            [LucentRouteModule(RouteFallbackPolicy.Reject)]
            public static partial class Routes { }
            [LucentRoute(typeof(Routes), "/", Id = "shell", Component = typeof(Page))]
            public readonly record struct ShellRoute();
            [LucentRoute(typeof(Routes), "/items/{id}", Parent = typeof(ShellRoute), Component = typeof(Page))]
            public readonly record struct ItemRoute(int Id);
            public sealed partial class Page {
                public static int Creations;
                public static ComponentRecipe Create() {
                    Creations++;
                    return ComponentRecipe.Create("mapped-page", static (_, _) => { });
                }
            }
            public static class Verify {
                public static void Run() {
                    _ = Routes.CreateComponent(Routes.ShellDefinition.Branch[0]);
                    _ = Routes.CreateComponent(Routes.ItemDefinition.Branch[0]);
                    _ = Routes.CreateComponent(Routes.ItemDefinition.Branch[1]);
                    if (Page.Creations != 3) throw new Exception("Factory was not called directly.");
                    try { _ = Routes.CreateComponent(null!); }
                    catch (ArgumentException) { return; }
                    throw new Exception("Unmapped level was accepted.");
                }
            }
            """
        );
        AssertNoErrors(generated);
        var source = generated.Run.Results.Single().GeneratedSources.Single().SourceText.ToString();
        StringAssert.Contains(source, "RouteBundle Bundle");
        Assert.IsFalse(source.Contains("RouteBundle Routes", StringComparison.Ordinal));
        using var stream = new MemoryStream();
        var emit = generated.Compilation.Emit(stream);
        Assert.IsTrue(emit.Success, String.Join(" | ", emit.Diagnostics));
        Assembly
            .Load(stream.ToArray())
            .GetType("Sample.Verify")!
            .GetMethod("Run")!
            .Invoke(null, null);
    }

    [TestMethod]
    [DataRow("private static ComponentRecipe Create() => null!;")]
    [DataRow("public static ComponentRecipe Create(int required) => null!;")]
    [DataRow("public ComponentRecipe Create() => null!;")]
    [DataRow("public static string Create() => \"wrong\";")]
    [DataRow("public static ComponentRecipe Create<T>() => null!;")]
    [DataRow(
        "public static ComponentRecipe Create(int a = 0) => null!; public static string Create(string b = \"\") => \"wrong\";"
    )]
    public void InvalidComponentMappingIsAnAuthoredDiagnostic(string declaration)
    {
        var result = Generate(
            """
            using Lucent.Core;
            [LucentRouteModule(RouteFallbackPolicy.Reject)] public static partial class Routes { }
            [LucentRoute(typeof(Routes), "/", Component = typeof(Page))] public readonly record struct HomeRoute();
            public class Page {
            """
                + declaration
                + "}"
        );
        Assert.AreEqual(1, result.Run.Diagnostics.Count(diagnostic => diagnostic.Id == "LUI4208"));
        Assert.AreEqual(0, result.Run.Results.Single().GeneratedSources.Length);
    }

    [TestMethod]
    public void PartiallyMappedModuleReportsMissingComponentAndEmitsNoBundle()
    {
        var result = Generate(
            """
            using Lucent.Core;
            [LucentRouteModule(RouteFallbackPolicy.Reject)] public static partial class AppRoutes { }
            [LucentRoute(typeof(AppRoutes), "/", Component = typeof(Home))] public readonly record struct HomeRoute();
            [LucentRoute(typeof(AppRoutes), "/about")] public readonly record struct AboutRoute();
            public static class Home { public static ComponentRecipe Create() => ComponentRecipe.Create("home", static (_, _) => { }); }
            """
        );

        Assert.IsTrue(
            result.Run.Diagnostics.Any(item => item.Id == "LUI4209"),
            string.Join(Environment.NewLine, result.Run.Diagnostics)
        );
        Assert.AreEqual(0, result.Run.Results.Single().GeneratedSources.Length);
    }

    [TestMethod]
    [DataRow("public static object Module => null!;")]
    [DataRow("public static object Bundle => null!;")]
    [DataRow("private static void CreateDestination() { }")]
    [DataRow("public static object HomeDefinition => null!;")]
    public void AuthoredGeneratedMemberCollisionIsDiagnosedBeforeEmission(string member)
    {
        var result = Generate(
            """
            using Lucent.Core;
            [LucentRouteModule(RouteFallbackPolicy.Reject)]
            public static partial class AppRoutes {
            """
                + member
                + """
                }
                [LucentRoute(typeof(AppRoutes), "/", Component = typeof(Home))]
                public readonly record struct HomeRoute();
                public static class Home { public static ComponentRecipe Create() => ComponentRecipe.Create("home", static (_, _) => { }); }
                """
        );

        Assert.AreEqual(1, result.Run.Diagnostics.Count(item => item.Id == "LUI4210"));
        Assert.AreEqual(0, result.Run.Results.Single().GeneratedSources.Length);
    }

    [TestMethod]
    public void GeneratedAssemblyRoundTripsAndProvidesExactTypedContexts()
    {
        var generated = Generate(ValidSource, "routes/AppRoutes.cs");
        AssertNoErrors(generated);
        using var stream = new MemoryStream();
        var emit = generated.Compilation.Emit(stream);
        Assert.IsTrue(emit.Success, String.Join(" | ", emit.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        var module = assembly.GetType("Sample.AppRoutes")!;
        var tab = assembly.GetType("Sample.ProjectTab")!;
        var issue = module.GetMethod("Issue", BindingFlags.Public | BindingFlags.Static)!;
        var reference = (Lucent.Core.RouteReference)
            issue.Invoke(
                null,
                [
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    42,
                    Enum.Parse(tab, "Activity"),
                ]
            )!;
        Assert.AreEqual(
            "/projects/11111111-1111-1111-1111-111111111111/issues/42?tab=Activity",
            reference.Location.CanonicalText
        );
        var descriptor = (Lucent.Core.RouteDefinitionDescriptor)
            module.GetProperty("IssueDefinition")!.GetValue(null)!;
        Assert.AreSame(reference.Pattern, descriptor.Pattern);
        CollectionAssert.AreEqual(
            ParentCaptureSlots,
            descriptor.Branch[0].OwnedCaptureSlots.ToArray()
        );
        CollectionAssert.AreEqual(
            ChildCaptureSlots,
            descriptor.Branch[1].OwnedCaptureSlots.ToArray()
        );
    }

    [TestMethod]
    public void ZeroParameterRecordRouteGeneratesParameterlessFactory()
    {
        var generated = Generate(
            """
            using Lucent.Core;
            [LucentRouteModule(RouteFallbackPolicy.Reject)] public static partial class Routes { }
            [LucentRoute(typeof(Routes), "/issues", Id = "issues")] public readonly record struct IssuesRoute();
            """
        );
        AssertNoErrors(generated);
        var source = generated.Run.Results.Single().GeneratedSources.Single().SourceText.ToString();
        StringAssert.Contains(source, "RouteReference Issues()");
        StringAssert.Contains(source, "new global::Lucent.Core.RouteValue[] {  }");

        using var stream = new MemoryStream();
        var emit = generated.Compilation.Emit(stream);
        Assert.IsTrue(emit.Success, String.Join(" | ", emit.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        var module = assembly.GetType("Routes")!;
        var reference = (Lucent.Core.RouteReference)
            module
                .GetMethod("Issues", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [])!;
        Assert.AreEqual("/issues", reference.Location.CanonicalText);
    }

    [TestMethod]
    public void RejectsGenericRouteDeclarationsBeforeFactoryEmission()
    {
        var result = Generate(
            """
            using Lucent.Core;
            [LucentRouteModule(RouteFallbackPolicy.Reject)] public static partial class Routes { }
            [LucentRoute(typeof(Routes), "/items/{id}")] public readonly record struct GenericRoute<T>(int Id);
            """
        );

        Assert.IsTrue(result.Run.Diagnostics.Any(item => item.Id == "LUI4202"));
        Assert.IsFalse(result.Run.Results.SelectMany(item => item.GeneratedSources).Any());
    }

    [TestMethod]
    public void RejectsQueryDefaultsOutsideDecodedRouteGrammar()
    {
        var result = Generate(
            """
            using Lucent.Core;
            [LucentRouteModule(RouteFallbackPolicy.Reject)] public static partial class Routes { }
            [LucentRoute(typeof(Routes), "/items?filter={filter}")] public readonly record struct FilterRoute(string Filter = "\u0001");
            """
        );

        Assert.AreEqual(1, result.Run.Diagnostics.Count(item => item.Id == "LUI4203"));
        StringAssert.Contains(
            result
                .Run.Diagnostics.Single(item => item.Id == "LUI4203")
                .GetMessage(CultureInfo.InvariantCulture),
            "route location grammar"
        );
    }

    [TestMethod]
    public void RejectsQueryDefaultsOverTheRouteLocationValueLimit()
    {
        var oversized = new string('x', 1025);
        var result = Generate(
            $$"""
            using Lucent.Core;
            [LucentRouteModule(RouteFallbackPolicy.Reject)] public static partial class Routes { }
            [LucentRoute(typeof(Routes), "/items?filter={filter}")] public readonly record struct FilterRoute(string Filter = "{{oversized}}");
            """
        );

        Assert.AreEqual(1, result.Run.Diagnostics.Count(item => item.Id == "LUI4203"));
        StringAssert.Contains(
            result
                .Run.Diagnostics.Single(item => item.Id == "LUI4203")
                .GetMessage(CultureInfo.InvariantCulture),
            "limits"
        );
    }

    [TestMethod]
    public void ReportsUnsupportedMissingAndRepeatedParameters()
    {
        var result = Generate(
            """
            using Lucent.Core;
            [LucentRouteModule(RouteFallbackPolicy.Reject)] public static partial class Routes { }
            [LucentRoute(typeof(Routes), "/bad/{value}/{value}")] public readonly record struct BadRoute(decimal Value);
            """
        );
        CollectionAssert.IsSubsetOf(
            ExpectedInvalidParameterDiagnostics,
            result.Run.Diagnostics.Select(item => item.Id).ToArray()
        );
    }

    [TestMethod]
    public void ReportsDuplicateAndAmbiguousDefinitions()
    {
        var result = Generate(
            """
            using Lucent.Core;
            [LucentRouteModule(RouteFallbackPolicy.Reject)] public static partial class Routes { }
            [LucentRoute(typeof(Routes), "/items/{id}", Id="item")] public readonly record struct FirstRoute(int Id);
            [LucentRoute(typeof(Routes), "/items/{value}", Id="item")] public readonly record struct SecondRoute(long Value);
            """
        );
        Assert.IsTrue(result.Run.Diagnostics.Any(item => item.Id == "LUI4205"));
        Assert.AreEqual(2, result.Run.Diagnostics.Count(item => item.Id == "LUI4207"));
    }

    [TestMethod]
    public void ReportsInvalidParentPrefixAndMissingFallbackModule()
    {
        var result = Generate(
            """
            using Lucent.Core;
            public static partial class MissingPolicy { }
            [LucentRoute(typeof(MissingPolicy), "/missing")] public readonly record struct MissingRoute();
            [LucentRouteModule(RouteFallbackPolicy.Reject)] public static partial class Routes { }
            [LucentRoute(typeof(Routes), "/parents/{id}")] public readonly record struct ParentRoute(int Id);
            [LucentRoute(typeof(Routes), "/other/{id}/child", Parent=typeof(ParentRoute))] public readonly record struct ChildRoute(int Id);
            """
        );
        Assert.IsTrue(
            result.Run.Diagnostics.Any(item =>
                item.Id == "LUI4203"
                && item.GetMessage(CultureInfo.InvariantCulture)
                    .Contains("owning module", StringComparison.Ordinal)
            )
        );
        Assert.IsTrue(result.Run.Diagnostics.Any(item => item.Id == "LUI4206"));
    }

    [TestMethod]
    public void AbsoluteCompilerPathsAreReducedBeforeRuntimeMetadata()
    {
        var result = Generate(ValidSource, @"C:\agent\work\routes\AppRoutes.cs", @"C:\agent\work");
        AssertNoErrors(result);
        var source = result.Run.Results.Single().GeneratedSources.Single().SourceText.ToString();
        StringAssert.Contains(source, "RouteDeclarationSource(\"routes/AppRoutes.cs\"");
        Assert.IsFalse(source.Contains("C:\\\\agent", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ShortParameterNamesDoNotRewriteGeneratedApiNames()
    {
        var result = Generate(
            """
            using Lucent.Core;
            [LucentRouteModule(RouteFallbackPolicy.Reject)] public static partial class Routes { }
            [LucentRoute(typeof(Routes), "/values?i={i}&global={global}")] public readonly record struct ValueRoute(int I = 7, string Global = "all");
            """
        );
        AssertNoErrors(result);
        var source = result.Run.Results.Single().GeneratedSources.Single().SourceText.ToString();
        StringAssert.Contains(source, "RouteValue.FromSigned32(7)");
        StringAssert.Contains(source, "RouteValue.FromText(\"all\")");
        StringAssert.Contains(source, "global::Lucent.Core.RouteValue");
    }

    [TestMethod]
    public void RejectsEncodedAndInvalidAuthoredTemplateComponents()
    {
        var result = Generate(
            """
            using Lucent.Core;
            [LucentRouteModule(RouteFallbackPolicy.Reject)] public static partial class Routes { }
            [LucentRoute(typeof(Routes), "/%61")] public readonly record struct EncodedRoute();
            [LucentRoute(typeof(Routes), "/ok?a={a}&a={b}")] public readonly record struct DuplicateQueryRoute(string A, string B);
            """
        );
        Assert.AreEqual(2, result.Run.Diagnostics.Count(item => item.Id == "LUI4203"));
    }

    private static (GeneratorDriverRunResult Run, Compilation Compilation) Generate(
        string source,
        string path = "Routes.cs",
        string? projectDirectory = null
    )
    {
        var compilation = CSharpCompilation.Create(
            "route-generator-" + Guid.NewGuid().ToString("N"),
            [
                CSharpSyntaxTree.ParseText(
                    source,
                    new CSharpParseOptions(LanguageVersion.Preview),
                    path
                ),
            ],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new RouteGenerator().AsSourceGenerator()],
            optionsProvider: new TestOptionsProvider(projectDirectory),
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview)
        );
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        return (driver.GetRunResult(), updated);
    }

    private static void AssertNoErrors(
        (GeneratorDriverRunResult Run, Compilation Compilation) result
    )
    {
        Assert.IsFalse(
            result.Run.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error),
            String.Join(" | ", result.Run.Diagnostics)
        );
        Assert.IsFalse(
            result
                .Compilation.GetDiagnostics()
                .Any(item => item.Severity == DiagnosticSeverity.Error),
            String.Join(" | ", result.Compilation.GetDiagnostics())
        );
    }

    private static MetadataReference[] References() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(
                MetadataReference.CreateFromFile(typeof(Lucent.Core.RoutePattern).Assembly.Location)
            )
            .ToArray();

    private sealed class TestOptionsProvider(string? projectDirectory)
        : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _global = new TestOptions(projectDirectory);

        public override AnalyzerConfigOptions GlobalOptions => _global;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => TestOptions.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
            TestOptions.Empty;
    }

    private sealed class TestOptions(string? projectDirectory) : AnalyzerConfigOptions
    {
        internal static TestOptions Empty { get; } = new(null);

        public override bool TryGetValue(string key, out string value)
        {
            if (key == "build_property.ProjectDir" && projectDirectory is not null)
            {
                value = projectDirectory;
                return true;
            }
            value = "";
            return false;
        }
    }
}
