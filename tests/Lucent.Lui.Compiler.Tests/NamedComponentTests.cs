using Lucent.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class NamedComponentTests
{
    private const string Source = """
        namespace Sample;
        using Lucent.Core;
        public component Card() {
            context Config config;
            [Once]
            int count = Harness.Record("lui:" + config.Value, 0);
            int derived = Organization.Length + CompanionCount + Alpha + Zebra + count;
            readonly int snapshot = Organization.Length;

            string Describe() => Organization + ":" + derived + ":" + snapshot;

            <Text>{Describe()}</Text>
        }
        """;

    private const string Companion = """
        namespace Sample;
        public sealed record Config(string Value);
        public sealed partial class Card {
            private readonly int ordinary = Harness.Record("ordinary", 1);
            private string Organization => "lucent";
            [Lucent.Core.State(Initializer = nameof(CreateCompanionCount))]
            public partial int CompanionCount { get; set; }
            [Lucent.Core.State(4)]
            public partial int Zebra { get; set; }
            [Lucent.Core.State(1)]
            public partial int Alpha { get; set; }
            partial void Setup(Lucent.Core.ComponentContext context) {
                Harness.Record("setup", 0);
                Harness.Cards.Add(this);
                context.OnDispose(() => CompanionCount = 0);
                context.OnDispose(() => Harness.Cleanups++);
            }
            public string CompanionRead() => Describe();
            public int ReadLuiCount() => count;
            public int ReadLuiDerived() => derived;
            public int ReadLuiSnapshot() => snapshot;
            private static int CreateCompanionCount(Lucent.Core.ComponentContext context) {
                Harness.Record("companion", 2);
                if (Harness.FailNext) {
                    context.OnDispose(() => Harness.FailedCleanups++);
                    throw new System.InvalidOperationException("initializer failed");
                }
                return 2;
            }
        }
        public static class Harness {
            public static readonly System.Collections.Generic.List<string> Order = new();
            public static readonly System.Collections.Generic.List<Card> Cards = new();
            public static int Cleanups;
            public static int FailedCleanups;
            public static bool FailNext;
            public static int Record(string name, int value) { Order.Add(name); return value; }
            public static string Run() {
                using var composition = new Lucent.Core.Composition(new Lucent.Core.ReactiveGraph(), "named-test");
                using var theme = new Lucent.Core.ThemeContext(composition.Root.Scope, new Lucent.Core.Theme("named-test"));
                var recipe = Lucent.Core.Context.Provide(new Config("ready"), Card.Create());
                using (composition.Mount(composition.Root, theme, recipe))
                using (composition.Mount(composition.Root, theme, recipe)) {
                    if (Cards.Count != 2 || object.ReferenceEquals(Cards[0], Cards[1]) || Cards[0].CompanionCount != 2 || Cards[0].ReadLuiCount() != 0 || Cards[0].ReadLuiDerived() != 13 || Cards[0].ReadLuiSnapshot() != 6)
                        throw new System.InvalidOperationException("named mount state was incorrect");
                }
                FailNext = true;
                try { composition.Mount(composition.Root, theme, Lucent.Core.Context.Provide(new Config("ready"), Card.Create())); throw new System.Exception("failure hidden"); }
                catch (System.InvalidOperationException error) when (error.Message == "initializer failed") { }
                return string.Join(",", Order) + ";" + Cleanups + ":" + FailedCleanups;
            }
        }
        """;

    [TestMethod]
    public void NamedPartialIsTheBindingAndPerMountStateIdentity()
    {
        var projection = LuiAuthoredSourceProjection.Project(Source);
        Assert.IsTrue(projection.Success);
        Assert.IsNotNull(projection.EarlyComponentDeclaration);
        var compilation = Compilation(Companion, projection.EarlyComponentDeclaration);
        Assert.IsFalse(
            compilation.GetDiagnostics().Any(static item => item.Id == "CS0103"),
            "The preparatory component declaration must make LUI method signatures visible to its companion."
        );
        var result = LuiCompiler.CompileNamedComponent(
            projection.Document,
            compilation,
            Identity(),
            "Card.lui"
        );

        Assert.IsTrue(
            result.Success,
            String.Join(
                " | ",
                result.Diagnostics.Select(static item => item.Id + ": " + item.Message)
            )
        );
        StringAssert.Contains(result.Source, "public sealed partial class Card");
        StringAssert.Contains(result.Source, "ComponentRecipe Create()");
        StringAssert.Contains(result.Source, "new Card(");
        StringAssert.Contains(result.Source, "Signal<int>");
        StringAssert.Contains(result.Source, "Derived<int>");
        StringAssert.Contains(result.Source, "IComponentState<Card>");
        StringAssert.Contains(result.Source, "Component.Define<Card>");
        StringAssert.Contains(result.Source, "__luiCompanionState_CompanionCount");
        StringAssert.Contains(result.Source, "Setup(");
        Assert.IsTrue(
            result.Source.IndexOf("Card.Alpha", StringComparison.Ordinal)
                < result.Source.IndexOf("Card.Zebra", StringComparison.Ordinal),
            "Companion state cells must initialize in deterministic property-name order."
        );
        Assert.IsFalse(result.Source!.Contains("class __luiState_", StringComparison.Ordinal));
        Assert.IsFalse(
            result.Source.Contains("static partial class Components", StringComparison.Ordinal)
        );

        var generated = CSharpSyntaxTree.ParseText(
            result.Source,
            new CSharpParseOptions(LanguageVersion.Preview),
            result.Identity.HintName
        );
        using var assembly = new MemoryStream();
        var emitted = compilation.AddSyntaxTrees(generated).Emit(assembly);
        Assert.IsTrue(emitted.Success, String.Join(Environment.NewLine, emitted.Diagnostics));
        var loaded = System.Reflection.Assembly.Load(assembly.ToArray());
        var runtime = (string)
            loaded.GetType("Sample.Harness")!.GetMethod("Run")!.Invoke(null, null)!;
        Assert.AreEqual(
            "ordinary,companion,lui:ready,setup,ordinary,companion,lui:ready,setup,ordinary,companion;2:1",
            runtime
        );
    }

    [TestMethod]
    public void LegacyEmissionRemainsStaticAndCannotBorrowCompanionInstanceMembers()
    {
        var result = LuiCompiler.Compile(
            LuiParser.Parse(Source),
            Compilation(Companion),
            Identity(),
            "Card.lui"
        );

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(static item => item.Id == "LUI2000"));
        StringAssert.Contains(result.ProjectionSource, "static partial class Components");
    }

    [TestMethod]
    public void PreparedSetupProvenanceDoesNotDependOnTheSyntaxTreeHintName()
    {
        var projection = LuiAuthoredSourceProjection.Project(Source);
        var result = LuiCompiler.CompileNamedComponent(
            projection.Document,
            Compilation(
                Companion,
                projection.EarlyComponentDeclaration,
                "Card.lui.component.preparation.cs"
            ),
            Identity(),
            "Card.lui"
        );

        Assert.IsTrue(
            result.Success,
            String.Join(
                " | ",
                result.Diagnostics.Select(static item => item.Id + ": " + item.Message)
            )
        );
    }

    [TestMethod]
    public void ProjectionPreservesOffsetsAndPublishesSupportTypesBeforeNamedIdentity()
    {
        const string source = """
            namespace Sample;
            using Lucent.Core;
            public record Model(string Name);
            public component View(Model model) {
                <Text>{model.Name}</Text>
            }
            """;

        var result = LuiAuthoredSourceProjection.Project(source);

        Assert.IsTrue(
            result.Success,
            String.Join(
                " | ",
                result.Diagnostics.Select(static diagnostic =>
                    diagnostic.Id + ": " + diagnostic.Message
                )
            )
        );
        StringAssert.Contains(result.DeclarationsSource, "public record Model(string Name);");
        StringAssert.Contains(result.EarlyComponentDeclaration, "partial class View");
        StringAssert.Contains(
            result.EarlyComponentDeclaration,
            "partial global::Lucent.Core.ComponentRecipe Create(Model model);"
        );
        Assert.IsTrue(
            result.EarlyComponentDeclaration!.IndexOf("namespace Sample;", StringComparison.Ordinal)
                < result.EarlyComponentDeclaration.IndexOf(
                    "using Lucent.Core;",
                    StringComparison.Ordinal
                ),
            "Namespace-scoped using directives must retain their authored scope."
        );
        Assert.AreEqual(
            source.IndexOf("public component", StringComparison.Ordinal),
            result.Document.Component!.Span.Start
        );
        Assert.AreEqual(source.Length, result.Document.Source.Length);
    }

    [TestMethod]
    public void NamedLuiSetupPreservesItsOwnerParameterInTheDefiningDeclaration()
    {
        const string source = """
            namespace Sample;
            using Lucent.Core;
            public component Card() {
                Setup(owner) { owner.OnDispose(() => { }); }
                <Column />
            }
            """;
        var projection = LuiAuthoredSourceProjection.Project(source);
        StringAssert.Contains(projection.EarlyComponentDeclaration, "ComponentContext owner);");
        var result = LuiCompiler.CompileNamedComponent(
            projection.Document,
            Compilation(String.Empty, projection.EarlyComponentDeclaration),
            Identity(),
            "Card.lui"
        );

        Assert.IsTrue(
            result.Success,
            String.Join(" | ", result.Diagnostics.Select(static item => item.Message))
        );
        var compilation = Compilation(String.Empty, projection.EarlyComponentDeclaration)
            .AddSyntaxTrees(
                CSharpSyntaxTree.ParseText(
                    result.Source!,
                    new CSharpParseOptions(LanguageVersion.Preview),
                    result.Identity.HintName
                )
            );
        Assert.IsFalse(
            compilation
                .GetDiagnostics()
                .Any(static item => item.Severity == DiagnosticSeverity.Error),
            String.Join(Environment.NewLine, compilation.GetDiagnostics())
        );
    }

    [TestMethod]
    public void NamedCreatePublishesParameterMetadataOnlyOnItsDefiningDeclaration()
    {
        const string source = """
            namespace Sample;
            using Lucent.Core;
            public component Card([DefaultContent] ComponentContent content, string label = "ready") {
                <Column>{content}</Column>
            }
            """;
        const string companion = """
            namespace Sample;
            public sealed partial class Card { public string ReadLabel() => label; }
            """;
        var projection = LuiAuthoredSourceProjection.Project(source);
        var compilation = Compilation(companion, projection.EarlyComponentDeclaration);
        Assert.IsFalse(compilation.GetDiagnostics().Any(static item => item.Id == "CS0103"));
        var result = LuiCompiler.CompileNamedComponent(
            projection.Document,
            compilation,
            Identity(),
            "Card.lui"
        );

        Assert.IsTrue(
            result.Success,
            String.Join(" | ", result.Diagnostics.Select(static item => item.Message))
        );
        Assert.IsFalse(result.Source!.Contains("DefaultContent", StringComparison.Ordinal));
        Assert.IsFalse(result.Source.Contains("= \"ready\"", StringComparison.Ordinal));
        var generated = CSharpSyntaxTree.ParseText(
            result.Source,
            new CSharpParseOptions(LanguageVersion.Preview),
            result.Identity.HintName
        );
        var diagnostics = compilation
            .AddSyntaxTrees(generated)
            .GetDiagnostics()
            .Where(static item => item.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.AreEqual(0, diagnostics.Length, String.Join(Environment.NewLine, diagnostics));
    }

    [TestMethod]
    public void ForeignBoundConstantRefinesToTheActualWritableStateSignature()
    {
        const string source = """
            namespace Sample;
            using Lucent.Core;
            public component Card() {
                int count = Defaults.Count;
                <Column />
            }
            """;
        const string companion = """
            namespace Sample;
            public static class Defaults { public const int Count = 1; }
            public sealed partial class Card { public int ReadCount() => count; }
            """;
        var projection = LuiAuthoredSourceProjection.Project(source);
        Assert.IsFalse(
            projection.EarlyComponentDeclaration!.Contains(
                "count { get; set;",
                StringComparison.Ordinal
            )
        );
        var result = LuiCompiler.CompileNamedComponent(
            projection.Document,
            Compilation(companion, projection.EarlyComponentDeclaration),
            Identity(),
            "Card.lui"
        );

        Assert.IsTrue(
            result.Success,
            String.Join(" | ", result.Diagnostics.Select(static item => item.Message))
        );
        StringAssert.Contains(result.PreparedComponentDeclaration, "count { get; set;");
        var finalCompilation = Compilation(
                companion,
                result.PreparedComponentDeclaration,
                "Card.lui.refined.g.cs"
            )
            .AddSyntaxTrees(
                CSharpSyntaxTree.ParseText(
                    result.Source!,
                    new CSharpParseOptions(LanguageVersion.Preview),
                    result.Identity.HintName
                )
            );
        var errors = finalCompilation
            .GetDiagnostics()
            .Where(static item => item.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.AreEqual(0, errors.Length, String.Join(Environment.NewLine, errors));
    }

    [TestMethod]
    public void ProjectionAcceptsSupportOnlyDocumentWithoutInventingAComponent()
    {
        const string source = """
            namespace Sample;
            public record Model(string Name);
            public delegate Model ModelFactory(string name);
            """;

        var result = LuiAuthoredSourceProjection.Project(source);

        Assert.IsTrue(
            result.Success,
            String.Join(
                " | ",
                result.Diagnostics.Select(static diagnostic =>
                    diagnostic.Id + ": " + diagnostic.Message
                )
            )
        );
        Assert.IsNull(result.Document.Component);
        Assert.IsNull(result.EarlyComponentDeclaration);
        Assert.AreEqual(source, result.DeclarationsSource);
        Assert.AreEqual(source.Length, result.Document.Source.Length);
    }

    [TestMethod]
    public void NamedCompanionMisuseFailsBeforePublishingGeneratedSource()
    {
        AssertNamedDiagnostic(
            """
            namespace Sample;
            public sealed partial class Card {
                private Card() { }
            }
            """,
            "LUI2052"
        );
        AssertNamedDiagnostic(
            """
            namespace Sample;
            [Lucent.Core.ComponentState]
            public sealed partial class Card { }
            """,
            "LUI2051"
        );
        AssertNamedDiagnostic(
            """
            namespace Sample;
            public sealed partial class Card {
                partial void Setup(Lucent.Core.ComponentContext context) { }
            }
            """,
            "LUI2056",
            """
            namespace Sample;
            using Lucent.Core;
            public component Card() {
                Setup(owner) { owner.OnDispose(() => { }); }
                <Text />
            }
            """
        );
        AssertNamedDiagnostic(
            """
            namespace Sample;
            public sealed partial class Card {
                [Lucent.Core.State, Lucent.Core.State]
                public partial int Count { get; set; }
            }
            """,
            "LUI2053"
        );
        AssertNamedDiagnostic(
            """
            namespace Sample;
            public sealed partial class Card {
                partial void Setup(int value) { }
            }
            """,
            "LUI2059"
        );
    }

    [TestMethod]
    public void NamedRequirementsWrapTheSameComponentContextOwner()
    {
        const string source = """
            namespace Sample;
            using Lucent.Core;
            public component Card() {
                context ThemeContext theme;
                <Text>{theme.Theme.Name}</Text>
            }
            """;
        const string companion = "namespace Sample; public sealed partial class Card { }";
        var projection = LuiAuthoredSourceProjection.Project(source);
        var compilation = Compilation(companion, projection.EarlyComponentDeclaration);

        var result = LuiCompiler.CompileNamedComponent(
            projection.Document,
            compilation,
            Identity(),
            "Card.lui"
        );

        Assert.IsTrue(
            result.Success,
            String.Join(" | ", result.Diagnostics.Select(diagnostic => diagnostic.Message))
        );
        StringAssert.Contains(result.Source, "ComponentRecipe.Defer(");
        StringAssert.Contains(result.Source, "Component.Define<Card>");
        StringAssert.Contains(result.Source, "__luiValues");
        var generated = CSharpSyntaxTree.ParseText(
            result.Source,
            new CSharpParseOptions(LanguageVersion.Preview),
            result.Identity.HintName
        );
        using var assembly = new MemoryStream();
        var emitted = compilation.AddSyntaxTrees(generated).Emit(assembly);
        Assert.IsTrue(emitted.Success, String.Join(Environment.NewLine, emitted.Diagnostics));
    }

    private static void AssertNamedDiagnostic(
        string companion,
        string diagnosticId,
        string source = Source
    )
    {
        var projection = LuiAuthoredSourceProjection.Project(source);
        var result = LuiCompiler.CompileNamedComponent(
            projection.Document,
            Compilation(companion, projection.EarlyComponentDeclaration),
            Identity(),
            "Card.lui"
        );
        Assert.IsFalse(result.Success);
        Assert.IsTrue(
            result.Diagnostics.Any(diagnostic => diagnostic.Id == diagnosticId),
            String.Join(" | ", result.Diagnostics.Select(diagnostic => diagnostic.Id))
        );
    }

    private static CSharpCompilation Compilation(
        string source,
        string? early = null,
        string earlyPath = "Card.lui.early.g.cs"
    ) =>
        CSharpCompilation.Create(
            "named-component",
            [
                CSharpSyntaxTree.ParseText(
                    source,
                    new CSharpParseOptions(LanguageVersion.Preview),
                    "Card.lui.cs"
                ),
                .. (
                    early is null
                        ? Array.Empty<SyntaxTree>()
                        :
                        [
                            CSharpSyntaxTree.ParseText(
                                early,
                                new CSharpParseOptions(LanguageVersion.Preview),
                                earlyPath
                            ),
                        ]
                ),
            ],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Append(typeof(ComponentRecipe).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(static path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

    private static LuiFreshnessIdentity Identity() =>
        new(
            "named-component",
            "named-component",
            new LuiDocumentIdentity("Card.lui"),
            "1",
            "preview"
        );
}
