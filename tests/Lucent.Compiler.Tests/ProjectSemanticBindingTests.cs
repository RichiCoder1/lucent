using System.Diagnostics;
using System.Text.Json;
using Lucent.Compiler.Semantics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lucent.Compiler.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ProjectSemanticBindingTests
{
    [TestMethod]
    public void Project_semantic_cache_reuses_unchanged_trees_and_references()
    {
        using var fixture = new SemanticCacheFixture();
        var first = ProjectSemanticCompilation.CreateBaseCompilation(fixture.Context());
        var exact = ProjectSemanticCompilation.CreateBaseCompilation(fixture.Context());
        Assert.AreSame(first, exact);

        fixture.Write("A.cs", "namespace Demo; public class A { public int Changed => 2; }");
        var changed = ProjectSemanticCompilation.CreateBaseCompilation(fixture.Context());
        Assert.AreNotSame(first, changed);
        Assert.AreNotSame(Tree(first, "A.cs"), Tree(changed, "A.cs"));
        Assert.AreSame(Tree(first, "B.cs"), Tree(changed, "B.cs"));
        AssertSameReferences(first, changed);
    }

    [TestMethod]
    public void Project_semantic_cache_reuses_existing_trees_across_source_set_changes()
    {
        using var fixture = new SemanticCacheFixture();
        var first = ProjectSemanticCompilation.CreateBaseCompilation(fixture.Context());
        fixture.Write("C.cs", "namespace Demo; public class C { }");
        var added = ProjectSemanticCompilation.CreateBaseCompilation(fixture.Context(sources: ["A.cs", "B.cs", "C.cs"]));
        Assert.AreSame(Tree(first, "A.cs"), Tree(added, "A.cs"));
        Assert.AreSame(Tree(first, "B.cs"), Tree(added, "B.cs"));
        Assert.IsNotNull(Tree(added, "C.cs"));

        var removed = ProjectSemanticCompilation.CreateBaseCompilation(fixture.Context(sources: ["A.cs", "C.cs"]));
        Assert.AreSame(Tree(added, "A.cs"), Tree(removed, "A.cs"));
        Assert.AreSame(Tree(added, "C.cs"), Tree(removed, "C.cs"));
        Assert.IsFalse(removed.SyntaxTrees.Any(tree => tree.FilePath.EndsWith("B.cs", StringComparison.OrdinalIgnoreCase)));
        AssertSameReferences(added, removed);
    }

    [TestMethod]
    public void Project_semantic_cache_invalidates_configuration_and_isolates_projects()
    {
        using var fixture = new SemanticCacheFixture();
        var first = ProjectSemanticCompilation.CreateBaseCompilation(fixture.Context());
        var changedConfiguration = ProjectSemanticCompilation.CreateBaseCompilation(
            fixture.Context(targetFramework: "net8.0", languageVersion: "12.0", nullable: "enable", defines: "FEATURE"));
        Assert.AreNotSame(first, changedConfiguration);
        Assert.AreNotSame(Tree(first, "A.cs"), Tree(changedConfiguration, "A.cs"));
        Assert.IsFalse(ReferenceEquals(first.References.First(), changedConfiguration.References.First()));

        var otherProject = ProjectSemanticCompilation.CreateBaseCompilation(
            fixture.Context(projectPath: Path.Combine(fixture.Directory, "Other.csproj")));
        Assert.AreNotSame(Tree(first, "A.cs"), Tree(otherProject, "A.cs"));
        Assert.IsFalse(ReferenceEquals(first.References.First(), otherProject.References.First()));
    }

    [TestMethod]
    public void Project_semantic_cache_is_bounded_and_evicts_lru_entries()
    {
        ProjectSemanticCompilation.ClearCachedBasesForTests();
        using var fixture = new SemanticCacheFixture();
        try
        {
            var first = ProjectSemanticCompilation.CreateBaseCompilation(
                fixture.Context(projectPath: Path.Combine(fixture.Directory, "0.csproj")));
            for (var index = 1; index <= 8; index++)
            {
                _ = ProjectSemanticCompilation.CreateBaseCompilation(
                    fixture.Context(projectPath: Path.Combine(fixture.Directory, $"{index}.csproj")));
            }

            Assert.AreEqual(8, ProjectSemanticCompilation.CachedBaseCount);
            var reloaded = ProjectSemanticCompilation.CreateBaseCompilation(
                fixture.Context(projectPath: Path.Combine(fixture.Directory, "0.csproj")));
            Assert.AreNotSame(first, reloaded);
            Assert.AreEqual(8, ProjectSemanticCompilation.CachedBaseCount);
        }
        finally
        {
            ProjectSemanticCompilation.ClearCachedBasesForTests();
        }
    }

    [TestMethod]
    [DataRow("net9.0", "12.0", "enable", "FEATURE", "enable", true, "GeneratedFeature")]
    [DataRow("net9.0-windows", "11.0", "disable", "LEGACY", "disable", false, "GeneratedLegacy")]
    public async Task Design_time_msbuild_snapshot_matches_lucent_supported_semantics(
        string targetFramework,
        string languageVersion,
        string nullable,
        string define,
        string implicitUsings,
        bool includeProjectReference,
        string generatedType)
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-msbuild-parity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "obj"));
        try
        {
            var repository = FindRepositoryRoot();
            var referencedProject = Path.Combine(directory, "Referenced.csproj");
            var project = Path.Combine(directory, "Consumer.csproj");
            var feature = Path.Combine(directory, "Feature.cs");
            var generated = Path.Combine(directory, "obj", "OtherGenerator.g.cs");
            var lucent = Path.Combine(directory, "App.lui");
            await File.WriteAllTextAsync(referencedProject, $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>{targetFramework}</TargetFramework><LangVersion>{languageVersion}</LangVersion><Nullable>{nullable}</Nullable></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(directory, "Referenced.cs"), "namespace RefLib; public static class Referenced { public static string Value => \"ref\"; }");
            await File.WriteAllTextAsync(feature, $$"""
                #if {{define}}
                namespace Demo; public static class Feature { public static string Name => "ok"; }
                #endif
                """);
            await File.WriteAllTextAsync(generated, $"namespace Demo; public static class {generatedType} {{ public static string Value => \"generated\"; }}");
            await File.WriteAllTextAsync(lucent, "namespace Demo; component App() => TextBlock { Text: Feature.Name; };");
            await File.WriteAllTextAsync(project, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>{{targetFramework}}</TargetFramework><LangVersion>{{languageVersion}}</LangVersion><Nullable>{{nullable}}</Nullable><DefineConstants>{{define}}</DefineConstants><ImplicitUsings>{{implicitUsings}}</ImplicitUsings></PropertyGroup>
                  <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.props")}}" />
                  <ItemGroup>
                    <PackageReference Include="Avalonia" Version="12.1.1" />
                    {{(includeProjectReference ? "<ProjectReference Include=\"Referenced.csproj\" />" : string.Empty)}}
                    <ProjectReference Include="{{Path.Combine(repository, "src", "Lucent.Compiler.MSBuild", "Lucent.Compiler.MSBuild.csproj")}}" ReferenceOutputAssembly="false" PrivateAssets="all" />
                    <Compile Include="obj/OtherGenerator.g.cs" AutoGen="true" />
                    <LucentSource Include="App.lui" />
                  </ItemGroup>
                  <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.targets")}}" />
                </Project>
                """);

            var restore = await DotNetAsync(directory, "restore", project, "--nologo");
            Assert.AreEqual(0, restore.ExitCode, restore.Output);
            // Generate first without compiling the consumer: this fixture owns
            // semantic evaluation, not an Avalonia application bootstrap.
            var build = await DotNetAsync(directory, "msbuild", project, "-t:GenerateLucent", "-nologo", "-nodeReuse:false");
            Assert.AreEqual(0, build.ExitCode, build.Output);
            var evaluation = await DotNetAsync(directory, "msbuild", project,
                "-getTargetResult:ResolveReferences", "-getItem:Compile", "-getItem:Using", "-getItem:ProjectReference",
                "-getProperty:TargetFramework,LangVersion,Nullable,DefineConstants,ImplicitUsings",
                "-p:DesignTimeBuild=true", "-p:BuildingProject=false", "-nologo", "-nodeReuse:false");
            Assert.AreEqual(0, evaluation.ExitCode, evaluation.Output);
            using var document = JsonDocument.Parse(evaluation.Json);
            var root = document.RootElement;
            var properties = root.GetProperty("Properties");
            Assert.AreEqual(targetFramework, properties.GetProperty("TargetFramework").GetString());
            Assert.AreEqual(languageVersion, properties.GetProperty("LangVersion").GetString());
            Assert.AreEqual(nullable, properties.GetProperty("Nullable").GetString());
            Assert.IsTrue(properties.GetProperty("DefineConstants").GetString()!
                .Split(';').Contains(define));
            Assert.AreEqual(implicitUsings, properties.GetProperty("ImplicitUsings").GetString());

            var compile = ItemPaths(root.GetProperty("Items").GetProperty("Compile"));
            Assert.IsTrue(compile.Contains(feature));
            Assert.IsTrue(compile.Contains(generated));
            StringAssert.Contains(File.ReadAllText(generated), generatedType);
            Assert.IsTrue(compile.Any(path => path.EndsWith("AppComponent.g.cs", StringComparison.Ordinal)));
            var usings = root.GetProperty("Items").GetProperty("Using").EnumerateArray()
                .Select(item => item.GetProperty("Identity").GetString()).ToArray();
            Assert.AreEqual(implicitUsings == "enable", usings.Contains("System"));
            var references = ItemPaths(root.GetProperty("TargetResults").GetProperty("ResolveReferences").GetProperty("Items"));
            Assert.AreEqual(includeProjectReference,
                references.Any(path => path.EndsWith("Referenced.dll", StringComparison.OrdinalIgnoreCase)));
            var projectReferences = ItemPaths(root.GetProperty("Items").GetProperty("ProjectReference"));
            Assert.AreEqual(includeProjectReference,
                projectReferences.Any(path => path.EndsWith("Referenced.csproj", StringComparison.OrdinalIgnoreCase)));

            var context = new LucentProjectContext(project, references,
                compile.Where(path => !path.EndsWith("Component.g.cs", StringComparison.OrdinalIgnoreCase)).ToArray(),
                [lucent], usings.Where(value => value is not null).Cast<string>().ToArray(),
                properties.GetProperty("TargetFramework").GetString(), properties.GetProperty("LangVersion").GetString(),
                properties.GetProperty("Nullable").GetString(), properties.GetProperty("DefineConstants").GetString(), projectReferences);
            const string completionSource = "namespace Demo; component App() => TextBlock { Text: Feature.Na; };";
            var completions = LucentCompiler.GetCompletions(completionSource,
                completionSource.IndexOf("Feature.Na", StringComparison.Ordinal) + "Feature.Na".Length, lucent, context);
            Assert.IsTrue(completions.Any(item => item.Label == "Name"));
            const string symbolSource = "namespace Demo; component App() => TextBlock { Text: Feature.Name; };";
            var lucentSymbol = LucentCompiler.GetExpressionSymbol(symbolSource,
                symbolSource.IndexOf("Feature.Name", StringComparison.Ordinal) + "Feature.".Length, lucent, context);
            Assert.IsNotNull(lucentSymbol);
            StringAssert.Contains(lucentSymbol.Display, "Name");

            var parseOptions = CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersionFacts.TryParse(languageVersion, out var parsedLanguageVersion)
                    ? parsedLanguageVersion
                    : throw new AssertFailedException($"Unsupported test language version {languageVersion}."))
                .WithPreprocessorSymbols(define);
            var referenceCompilation = CSharpCompilation.Create("Parity", compile
                    .Where(path => !path.EndsWith("Component.g.cs", StringComparison.OrdinalIgnoreCase))
                    .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path))
                    .Append(CSharpSyntaxTree.ParseText(
                        implicitUsings == "enable"
                            ? "global using System; namespace Demo; class Probe { string Value = Feature.Name; }"
                            : "namespace Demo; class Probe { string Value = Feature.Name; }",
                        parseOptions)),
                references.Select(path => MetadataReference.CreateFromFile(path)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(
                    nullable == "enable" ? NullableContextOptions.Enable : NullableContextOptions.Disable));
            Assert.IsFalse(referenceCompilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), string.Join(Environment.NewLine, referenceCompilation.GetDiagnostics()));
            var probe = referenceCompilation.SyntaxTrees.Last();
            var name = probe.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax>().Single(node => node.Identifier.ValueText == "Name");
            var referenceSymbol = referenceCompilation.GetSemanticModel(probe).GetSymbolInfo(name).Symbol;
            Assert.IsNotNull(referenceSymbol);
            Assert.AreEqual(referenceSymbol.Name, lucentSymbol.Name);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Project_context_applies_language_nullable_and_preprocessor_snapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-semantic-snapshot", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var model = Path.Combine(directory, "Feature.cs");
            File.WriteAllText(model, """
                #if FEATURE
                namespace Demo; public static class Feature { public static string? Name => "ok"; }
                #endif
                """);
            const string source = "namespace Demo; component App() => TextBlock { Text: Feature.Name; };";
            var context = new LucentProjectContext(
                ProjectPath: Path.Combine(directory, "Demo.csproj"),
                SourcePaths: [model],
                TargetFramework: "net9.0",
                LanguageVersion: "12.0",
                Nullable: "enable",
                DefineConstants: "FEATURE");

            var result = LucentCompiler.Compile(source, Path.Combine(directory, "App.lui"), context);
            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.IsTrue(LucentCompiler.GetCompletions(source,
                source.IndexOf("Feature.Name", StringComparison.Ordinal) + "Feature.N".Length,
                "App.lui", context).Any(item => item.Label == "Name"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Editor_semantics_tolerate_unresolved_types_and_loop_sources()
    {
        const string source = """
            namespace Demo;
            component Main()
            {
                private readonly State<MissingType> value = new();
                Fragment Render() { return StackPanel {
                    foreach (var item in MissingItems) keyed by item { TextBlock { Text: item.ToString(); } }
                }; }
            }
            """;

        _ = LucentCompiler.GetCompletions(
            source,
            source.IndexOf("item.ToString", StringComparison.Ordinal) + "item.T".Length,
            "unresolved-editor.lui");
    }

    [TestMethod]
    public void Shared_island_semantics_cover_conditional_branches_and_local_shadowing()
    {
        const string source = """
            namespace Demo;
            component Main()
            {
                private readonly State<string> title = new("state");
                private readonly State<bool> visible = new(true);
                Fragment Render()
                {
                    return ContentControl {
                        if (visible.Value) {
                            Button {
                                Content: title.Value;
                                Click: (sender, e) => {
                                    var title = sender.Content;
                                    title.ToString();
                                };
                            }
                        } else {
                            TextBlock { Text: title.Value; }
                        }
                    };
                }
            }
            """;

        foreach (var occurrence in new[] { source.IndexOf("title.Value", StringComparison.Ordinal),
                     source.LastIndexOf("title.Value", StringComparison.Ordinal) })
        {
            var members = LucentCompiler.GetCompletions(
                source,
                occurrence + "title.V".Length,
                "conditional-editor.lui");
            Assert.IsTrue(members.Any(item => item.Label == "Value"));
        }

        var shadowedOffset = source.IndexOf("title.ToString", StringComparison.Ordinal) +
            "title.T".Length;
        var shadowedMembers = LucentCompiler.GetCompletions(
            source,
            shadowedOffset,
            "conditional-editor.lui");
        Assert.IsTrue(shadowedMembers.Any(item => item.Label == "ToString"));
        Assert.IsFalse(shadowedMembers.Any(item => item.Label == "Value"));
    }

    [TestMethod]
    public void Expression_completion_and_hover_resolve_keyed_loop_items()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "lucent-expression-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var modelPath = Path.Combine(temporaryDirectory, "PackageInfo.cs");
            File.WriteAllText(
                modelPath,
                "namespace Demo; " +
                "public sealed record PackageInfo(string Name, string Id, string Description); " +
                "public static class Catalog { " +
                "public static System.Threading.Tasks.Task<PackageInfo[]> Load(" +
                "System.Threading.CancellationToken cancellationToken) => throw null!; }");
            const string source = """
                namespace Demo;

                component Packages()
                {
                    private readonly State<string> query = new("lucent");
                    private readonly Computed<PackageInfo[]> packages = new(ct => Catalog.Load(ct), []);

                    Fragment Render()
                    {
                        return StackPanel {
                            TextBlock { Text: query.Value; }
                            StackPanel {
                                foreach (var package in packages.Value)
                                keyed by package.Id {
                                    TextBlock { Text: package.Name; }
                                }
                            }
                        };
                    }
                }
                """;
            var sourcePath = Path.Combine(temporaryDirectory, "Packages.lui");
            var context = new LucentProjectContext(
                Path.Combine(temporaryDirectory, "Demo.csproj"),
                SourcePaths: [modelPath]);
            var expressionOffset = source.IndexOf("package.Name", StringComparison.Ordinal);

            var roots = LucentCompiler.GetCompletions(
                source,
                expressionOffset,
                sourcePath,
                context);
            Assert.IsTrue(roots.Any(item => item.Label == "package"));
            Assert.IsTrue(roots.Any(item =>
                item.Label == "query" &&
                item.Detail == "private readonly State<string> query"));
            Assert.IsTrue(roots.Any(item =>
                item.Label == "packages" &&
                item.Detail == "private readonly Computed<PackageInfo[]> packages"));

            var queryOffset = source.IndexOf("query.Value", StringComparison.Ordinal);
            var queryMembers = LucentCompiler.GetCompletions(
                source,
                queryOffset + "query.V".Length,
                sourcePath,
                context);
            Assert.IsTrue(queryMembers.Any(item => item.Label == "Value"));
            Assert.IsTrue(queryMembers.Any(item => item.Label == "Update"));

            var packagesOffset = source.IndexOf("packages.Value", StringComparison.Ordinal);
            var computedMembers = LucentCompiler.GetCompletions(
                source,
                packagesOffset + "packages.V".Length,
                sourcePath,
                context);
            Assert.IsTrue(computedMembers.Any(item => item.Label == "Value"));
            Assert.IsTrue(computedMembers.Any(item => item.Label == "IsPending"));
            Assert.IsTrue(computedMembers.Any(item => item.Label == "ErrorMessage"));

            var arraySource = source.Replace(
                "Text: query.Value;",
                "Text: packages.Value.Length;",
                StringComparison.Ordinal);
            var arrayOffset = arraySource.IndexOf("packages.Value.Length", StringComparison.Ordinal);
            var arrayMembers = LucentCompiler.GetCompletions(
                arraySource,
                arrayOffset + "packages.Value.L".Length,
                sourcePath,
                context);
            Assert.IsTrue(arrayMembers.Any(item => item.Label == "Length"));
            var lengthHover = LucentCompiler.GetExpressionSymbol(
                arraySource,
                arrayOffset + "packages.Value.".Length,
                sourcePath,
                context);
            Assert.IsNotNull(lengthHover);
            StringAssert.Contains(lengthHover.Display, "Array.Length");

            var staticOffset = source.IndexOf("Catalog.Load", StringComparison.Ordinal);
            var staticMembers = LucentCompiler.GetCompletions(
                source,
                staticOffset + "Catalog.L".Length,
                sourcePath,
                context);
            Assert.IsTrue(staticMembers.Any(item => item.Label == "Load"));

            var cancellationOffset = source.IndexOf("Load(ct)", StringComparison.Ordinal) +
                "Load(".Length;
            var initializerSymbols = LucentCompiler.GetCompletions(
                source,
                cancellationOffset + 1,
                sourcePath,
                context);
            Assert.IsTrue(initializerSymbols.Any(item => item.Label == "ct"));

            var classSource = source.Replace(
                "Text: package.Name;",
                "Class: \"package-row\";",
                StringComparison.Ordinal);
            var classOffset = classSource.IndexOf("package-row", StringComparison.Ordinal);
            Assert.AreEqual(
                0,
                LucentCompiler.GetCompletions(
                    classSource,
                    classOffset,
                    sourcePath,
                    context).Count);

            var members = LucentCompiler.GetCompletions(
                source,
                expressionOffset + "package.N".Length,
                sourcePath,
                context);
            Assert.IsTrue(members.Any(item => item.Label == "Name"));

            var incompleteSource = source.Replace(
                "Text: package.Name;",
                "Text: package.D",
                StringComparison.Ordinal);
            var incompleteOffset = incompleteSource.IndexOf("package.D", StringComparison.Ordinal) +
                "package.D".Length;
            var incompleteMembers = LucentCompiler.GetCompletions(
                incompleteSource,
                incompleteOffset,
                sourcePath,
                context);
            Assert.IsTrue(incompleteMembers.Any(item => item.Label == "Description"));

            var hover = LucentCompiler.GetExpressionSymbol(
                source,
                expressionOffset + "package.".Length,
                sourcePath,
                context);
            Assert.IsNotNull(hover);
            StringAssert.Contains(hover.Display, "PackageInfo.Name");
            Assert.IsNull(hover.Documentation);

            var queryHover = LucentCompiler.GetExpressionSymbol(
                source,
                source.IndexOf("query =", StringComparison.Ordinal),
                sourcePath,
                context);
            Assert.IsNotNull(queryHover);
            Assert.AreEqual("private readonly State<string> query", queryHover.Display);
            Assert.IsNull(queryHover.Documentation);

            var packagesHover = LucentCompiler.GetExpressionSymbol(
                source,
                source.IndexOf("packages =", StringComparison.Ordinal),
                sourcePath,
                context);
            Assert.IsNotNull(packagesHover);
            Assert.AreEqual(
                "private readonly Computed<PackageInfo[]> packages",
                packagesHover.Display);
            Assert.IsNull(packagesHover.Documentation);

            var stateTypeHover = LucentCompiler.GetExpressionSymbol(
                source,
                source.IndexOf("State<string>", StringComparison.Ordinal),
                sourcePath,
                context);
            Assert.IsNotNull(stateTypeHover);
            Assert.AreEqual("class State<T>", stateTypeHover.Display);

            var newHover = LucentCompiler.GetExpressionSymbol(
                source,
                source.IndexOf("new(\"lucent\")", StringComparison.Ordinal),
                sourcePath,
                context);
            Assert.IsNotNull(newHover);
            StringAssert.Contains(newHover.Display, "State<string>.State");

            var catalogHover = LucentCompiler.GetExpressionSymbol(
                source,
                source.IndexOf("Catalog.Load", StringComparison.Ordinal),
                sourcePath,
                context);
            Assert.IsNotNull(catalogHover);
            Assert.AreEqual("class Demo.Catalog", catalogHover.Display);

            var loadHover = LucentCompiler.GetExpressionSymbol(
                source,
                source.IndexOf("Load(ct)", StringComparison.Ordinal),
                sourcePath,
                context);
            Assert.IsNotNull(loadHover);
            StringAssert.Contains(loadHover.Display, "Catalog.Load");

            var cancellationHover = LucentCompiler.GetExpressionSymbol(
                source,
                source.IndexOf("ct =>", StringComparison.Ordinal),
                sourcePath,
                context);
            Assert.IsNotNull(cancellationHover);
            Assert.AreEqual("CancellationToken ct", cancellationHover.Display);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Project_sources_resolve_custom_controls_properties_and_definitions()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "lucent-semantic-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var controlPath = Path.Combine(temporaryDirectory, "FancyControl.cs");
            File.WriteAllText(
                controlPath,
                """
                namespace Demo.Controls;

                public sealed class FancyControl : Avalonia.Controls.ContentControl
                {
                    public string? Accent { get; set; }
                }
                """);

            const string source = """
                namespace Demo;
                using Demo.Controls;

                component Custom()
                {
                    Fragment Render()
                    {
                        return FancyControl {
                            Accent: "blue";
                            Content: "Hello";
                            Loaded: (sender, e) => {
                                sender.Accent = "loaded";
                            };
                        };
                    }
                }
                """;
            var result = LucentCompiler.Compile(
                source,
                Path.Combine(temporaryDirectory, "Custom.lui"),
                new LucentProjectContext(
                    ProjectPath: Path.Combine(temporaryDirectory, "Demo.csproj"),
                    SourcePaths: [controlPath]));

            Assert.IsTrue(
                result.Succeeded,
                string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
            StringAssert.Contains(
                result.GeneratedSource!,
                "new global::Demo.Controls.FancyControl()");
            StringAssert.Contains(result.GeneratedSource!, ".Accent = \"blue\"");
            StringAssert.Contains(
                result.GeneratedSource!,
                "var sender = (global::Demo.Controls.FancyControl)__sender!");

            var control = result.Symbols.Single(symbol =>
                symbol.Kind == LucentSemanticSymbolKind.NativeControl);
            var property = result.Symbols.Single(symbol =>
                symbol.Kind == LucentSemanticSymbolKind.NativeProperty &&
                symbol.Name == "Accent");
            var loaded = result.Symbols.Single(symbol =>
                symbol.Kind == LucentSemanticSymbolKind.NativeEvent);

            Assert.AreEqual(controlPath, control.Definition?.SourcePath);
            Assert.AreEqual(controlPath, property.Definition?.SourcePath);
            StringAssert.Contains(property.Display, "FancyControl.Accent");
            Assert.IsNull(loaded.Documentation);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Project_semantic_cache_replaces_a_changed_csharp_source()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-semantic-cache", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var control = Path.Combine(directory, "Fancy.cs");
            var context = new LucentProjectContext(Path.Combine(directory, "Demo.csproj"), SourcePaths: [control]);
            const string source = "namespace Demo; using Demo.Controls; component App() => Fancy { Accent: \"x\"; };";
            File.WriteAllText(control, "namespace Demo.Controls; public sealed class Fancy : Avalonia.Controls.Control { public string? Accent { get; set; } }");
            Assert.IsTrue(LucentCompiler.Compile(source, Path.Combine(directory, "App.lui"), context).Succeeded);

            File.WriteAllText(control, "namespace Demo.Controls; public sealed class Fancy : Avalonia.Controls.Control { public string? Tone { get; set; } }");
            var changed = LucentCompiler.Compile(source, Path.Combine(directory, "App.lui"), context);
            Assert.IsFalse(changed.Succeeded);
            Assert.IsTrue(changed.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("Accent", StringComparison.Ordinal)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static SyntaxTree Tree(CSharpCompilation compilation, string fileName) =>
        compilation.SyntaxTrees.Single(tree => tree.FilePath.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

    private static void AssertSameReferences(CSharpCompilation expected, CSharpCompilation actual)
    {
        Assert.AreEqual(expected.References.Count(), actual.References.Count());
        foreach (var (left, right) in expected.References.Zip(actual.References))
            Assert.IsTrue(ReferenceEquals(left, right));
    }

    private sealed class SemanticCacheFixture : IDisposable
    {
        public SemanticCacheFixture()
        {
            Directory = Path.Combine(Path.GetTempPath(), "lucent-semantic-cache", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            Write("A.cs", "namespace Demo; public class A { public int Value => 1; }");
            Write("B.cs", "namespace Demo; public class B { public int Value => 1; }");
            ProjectSemanticCompilation.ClearCachedBasesForTests();
        }

        public string Directory { get; }

        public void Write(string name, string text) => File.WriteAllText(Path.Combine(Directory, name), text);

        public LucentProjectContext Context(
            string[]? sources = null,
            string? projectPath = null,
            string? targetFramework = null,
            string? languageVersion = null,
            string? nullable = null,
            string? defines = null) => new(
            projectPath ?? Path.Combine(Directory, "Demo.csproj"),
            SourcePaths: (sources ?? ["A.cs", "B.cs"]).Select(name => Path.Combine(Directory, name)).ToArray(),
            TargetFramework: targetFramework,
            LanguageVersion: languageVersion,
            Nullable: nullable,
            DefineConstants: defines);

        public void Dispose()
        {
            ProjectSemanticCompilation.ClearCachedBasesForTests();
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }

    private static IReadOnlyList<string> ItemPaths(JsonElement items) => items.EnumerateArray()
        .Select(item => item.TryGetProperty("FullPath", out var path)
            ? path.GetString()
            : item.GetProperty("Identity").GetString())
        .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
        .Select(path => Path.GetFullPath(path!))
        .ToArray();

    private static async Task<(int ExitCode, string Output, string Json)> DotNetAsync(
        string directory,
        params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var jsonStart = output.IndexOf('{');
        var jsonEnd = output.LastIndexOf('}');
        return (process.ExitCode, output + error,
            jsonStart >= 0 && jsonEnd >= jsonStart ? output[jsonStart..(jsonEnd + 1)] : "{}");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Lucent.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Lucent.sln was not found.");
    }
}
