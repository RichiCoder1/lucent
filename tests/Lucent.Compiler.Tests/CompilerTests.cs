using Lucent.Compiler.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Diagnostics;
using System.Text;
using Lucent.Styles.Utilities;

namespace Lucent.Compiler.Tests;

[TestClass]
public sealed class CompilerTests
{
    [TestMethod]
    public async Task Utility_package_consumer_installs_the_exact_manifest_catalog_without_repository_paths()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-utility-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var root = FindRepositoryRoot();
            var packages = Path.Combine(directory, "packages");
            var consumer = Path.Combine(directory, "consumer");
            Directory.CreateDirectory(packages); Directory.CreateDirectory(consumer);
            var pack = await DotNetAsync(root, "pack", Path.Combine(root, "src", "Lucent.Styles.Utilities", "Lucent.Styles.Utilities.csproj"),
                "--no-restore", "-o", packages, "--nologo", "-nodeReuse:false");
            Assert.AreEqual(0, pack.ExitCode, pack.Output);
            var fixture = Path.Combine(root, "tests", "Lucent.Compiler.Tests", "Fixtures", "UtilityPackageConsumer");
            foreach (var file in Directory.EnumerateFiles(fixture)) File.Copy(file, Path.Combine(consumer, Path.GetFileName(file)));
            await File.WriteAllTextAsync(Path.Combine(consumer, "NuGet.Config"),
                $"<configuration><packageSources><add key=\"utility-local\" value=\"{packages.Replace("\\", "/")}\" /></packageSources></configuration>");
            var restore = await DotNetAsync(consumer, "restore", "Consumer.csproj", "--nologo", "-nodeReuse:false");
            Assert.AreEqual(0, restore.ExitCode, restore.Output);
            var build = await DotNetAsync(consumer, "build", "Consumer.csproj", "--no-restore", "--nologo", "-nodeReuse:false");
            Assert.AreEqual(0, build.ExitCode, build.Output);
            var run = await DotNetAsync(consumer, "run", "--project", "Consumer.csproj", "--no-build", "--no-restore", "--nologo");
            Assert.AreEqual(0, run.ExitCode, run.Output);
            var assembly = Path.Combine(consumer, "bin", "Debug", "net9.0", "Lucent.Styles.Utilities.dll");
            Assert.IsTrue(LucentModuleManifest.TryReadFromPe(assembly, out var manifest, out var error), error);
            var expected = UtilitySpecification.Entries.OrderBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
            Assert.AreEqual(expected.Length, new LucentStyles().Count);
            Assert.AreEqual(expected.Length, manifest!.StyleClasses.Count);
            foreach (var (entry, metadata) in expected.Zip(manifest.StyleClasses))
            {
                Assert.AreEqual(entry.Name, metadata.Name);
                Assert.AreEqual("Avalonia.Controls." + (entry.Type is "TemplatedControl" or "ToggleButton" ? "Primitives." : "") + entry.Type, metadata.ApplicableType);
                Assert.AreEqual(StyleClassOrigin.Utility, metadata.Origin);
                Assert.IsNull(metadata.Definition);
                Assert.AreEqual(entry.Detail, metadata.Detail);
                Assert.AreEqual("Lucent.Styles.Utilities.LucentStyles", metadata.CatalogType);
            }
            Assert.IsFalse(File.ReadAllText(Path.Combine(consumer, "Consumer.csproj")).Contains(root, StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Source_map_preserves_exact_unicode_ranges_and_real_multi_origin_provenance()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lucent-source-map-{Guid.NewGuid():N}");
        var sourcePath = Path.Combine(directory, "App.lui");
        var generatedPath = Path.Combine(directory, "obj", "AppComponent.g.cs");
        const string source = """
            namespace Demo;
            using System;
            component App(Uri model) =>
                TextBlock {
                    Name: "😀";
                    Text: binding(model.Host);
                };
            """;

        var result = LucentCompiler.CompileProject(
            [new LucentSourceInput(sourcePath, source, GeneratedOutputPath: generatedPath)])
            .Sources.Single().Result;

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var map = result.SourceMap!;
        var generated = result.GeneratedSource!;
        Assert.AreEqual(new Uri(Path.GetFullPath(sourcePath)).AbsoluteUri,
            map.Entries[0].LucentUri);
        Assert.AreEqual(new Uri(Path.GetFullPath(generatedPath)).AbsoluteUri,
            map.Entries[0].GeneratedUri);
        Assert.AreNotEqual(map.Entries[0].LucentUri, map.Entries[0].GeneratedUri);
        Assert.HasCount(map.Entries.Count, map.Entries.Distinct());
        CollectionAssert.AreEqual(map.Entries.OrderBy(entry => entry.GeneratedUri, StringComparer.Ordinal)
            .ThenBy(entry => entry.GeneratedRange.StartLine)
            .ThenBy(entry => entry.GeneratedRange.StartCharacter)
            .ThenBy(entry => entry.GeneratedRange.EndLine)
            .ThenBy(entry => entry.GeneratedRange.EndCharacter)
            .ThenBy(entry => entry.LucentUri, StringComparer.Ordinal)
            .ThenBy(entry => entry.LucentRange.StartLine)
            .ThenBy(entry => entry.LucentRange.StartCharacter)
            .ToArray(), map.Entries.ToArray());

        var unicode = map.Entries.Single(entry => Slice(source, entry.LucentRange) == "\"😀\"");
        Assert.AreEqual("\"😀\"".Length,
            unicode.LucentRange.EndCharacter - unicode.LucentRange.StartCharacter,
            "An astral character occupies two UTF-16 code units.");
        Assert.AreEqual("\"😀\"", Slice(source, unicode.LucentRange));
        Assert.IsTrue(Slice(generated, unicode.GeneratedRange).Contains("\"😀\"", StringComparison.Ordinal));

        var binding = map.Entries.Where(entry =>
            Slice(source, entry.LucentRange) == "model.Host").ToArray();
        Assert.IsGreaterThanOrEqualTo(2, binding.Select(entry => entry.GeneratedRange).Distinct().Count(),
            "The native binding source is emitted both at setup and in its owned updater.");
        Assert.IsTrue(map.Entries.GroupBy(entry => entry.GeneratedRange).Any(group =>
            group.Select(entry => Slice(source, entry.LucentRange)).Contains("model.Host") &&
            group.Select(entry => Slice(source, entry.LucentRange)).Any(span =>
                span.Contains("Text: binding(model.Host)", StringComparison.Ordinal))),
            "The generated Bind statement combines the property target and expression origins.");

        foreach (var entry in map.Entries)
        {
            var mapped = Slice(generated, entry.GeneratedRange).Trim();
            Assert.IsFalse(mapped.StartsWith("#line", StringComparison.Ordinal));
            Assert.IsFalse(mapped is "{" or "}");
            Assert.IsFalse(mapped.StartsWith("private ", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void Counter_parses_and_generates_without_diagnostics()
    {
        var source = File.ReadAllText(RepositoryPaths.CounterSource);

        var result = LucentCompiler.Compile(source, RepositoryPaths.CounterSource);

        Assert.IsTrue(result.Succeeded);
        Assert.HasCount(0, result.Diagnostics);
        Assert.IsNotNull(result.Syntax);
        Assert.AreEqual("Lucent.Examples.Counter", result.Syntax.NamespaceName);
        Assert.AreEqual("Counter", result.Syntax.Component.Name);
        var state = result.Syntax.Component.State
            ?? throw new AssertFailedException("Counter state member was not parsed.");
        Assert.AreEqual("count", state.Name);
        Assert.AreEqual(0, state.InitialValue);

        var root = result.Syntax.Component.RenderMethod.Root;
        Assert.AreEqual("StackPanel", root.Name);
        CollectionAssert.AreEqual(
            new[] { "TextBlock", "Button" },
            root.Children.Select(child => child.Name).ToArray());

        var text = root.Children.First();
        var textProperty = text.Properties.Single(property =>
            property.Name == "Text");
        Assert.IsInstanceOfType<StringValueSyntax>(textProperty.Value);
        Assert.IsTrue(
            ((StringValueSyntax)textProperty.Value).IsInterpolated);

        var button = root.Children.Last();
        var clickProperty = button.Properties.Single(property =>
            property.Name == "Click");
        Assert.IsInstanceOfType<CSharpExpressionValueSyntax>(clickProperty.Value);
    }

    [TestMethod]
    public void Counter_generation_matches_checked_in_snapshot()
    {
        var source = File.ReadAllText(RepositoryPaths.CounterSource);
        var style = File.ReadAllText(RepositoryPaths.CounterStyle);

        const string logicalSourcePath = "examples/counter/Counter.lui";
        var first = LucentCompiler.Compile(
            source, logicalSourcePath, projectContext: null, style, "examples/counter/Counter.css");
        var second = LucentCompiler.Compile(
            source, logicalSourcePath, projectContext: null, style, "examples/counter/Counter.css");

        Assert.IsTrue(first.Succeeded);
        Assert.AreEqual(first.GeneratedSource, second.GeneratedSource);
        StringAssert.Contains(first.GeneratedSource, "public Fragment Mount()");
        StringAssert.Contains(first.GeneratedSource, "return Fragment.From(__lucent_control1!);");
        StringAssert.Contains(first.GeneratedSource!, "FontSize");
        Assert.IsFalse(first.GeneratedSource.Contains("Padding", StringComparison.Ordinal));
        Assert.IsFalse(first.GeneratedSource.Contains(
            "Text = \"Lucent\"",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void Interpolated_string_braces_do_not_terminate_the_ui_element()
    {
        var source = File.ReadAllText(RepositoryPaths.CounterSource);

        var result = LucentCompiler.Compile(source, RepositoryPaths.CounterSource);

        Assert.IsTrue(result.Succeeded);
        var root = result.Syntax!.Component.RenderMethod.Root;
        Assert.HasCount(2, root.Children);
        Assert.AreEqual("Button", root.Children.Last().Name);
    }

    [TestMethod]
    public void Missing_property_semicolon_reports_a_source_diagnostic_and_recovers()
    {
        var source = File.ReadAllText(RepositoryPaths.CounterSource)
            .Replace(
                "Text: $\"Count: {count.Value}\";",
                "Text: $\"Count: {count.Value}\"",
                StringComparison.Ordinal);

        var result = LucentCompiler.Compile(source, "MissingSemicolon.lui");

        Assert.IsFalse(result.Succeeded);
        var diagnostic = result.Diagnostics.Single(candidate =>
            candidate.Code == "LUC1001" &&
            candidate.Message.Contains(
                "property value",
                StringComparison.Ordinal));
        Assert.IsTrue(diagnostic.Line > 0);
        Assert.IsTrue(diagnostic.Column > 0);
        Assert.IsNotNull(result.Syntax);
        Assert.AreEqual(
            "Button",
            result.Syntax.Component.RenderMethod.Root.Children.Last().Name);
    }

    [TestMethod]
    public void Arbitrary_native_property_is_preserved_for_project_compilation()
    {
        var source = File.ReadAllText(RepositoryPaths.CounterSource)
            .Replace(
                "Class: \"primary\";",
                "Class: \"primary\";\n                MinWidth: 120;",
                StringComparison.Ordinal);

        var result = LucentCompiler.Compile(source, "NativeProperty.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource, ".MinWidth = 120;");
    }

    [TestMethod]
    public void Duplicate_optional_property_reports_a_semantic_diagnostic()
    {
        var source = File.ReadAllText(RepositoryPaths.CounterSource)
            .Replace(
                "Class: \"primary\";",
                "Class: \"primary\";\n                Class: \"secondary\";",
                StringComparison.Ordinal);

        var result = LucentCompiler.Compile(source, "DuplicateClass.lui");

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "LUC2001" &&
            diagnostic.Message.Contains(
                "only one 'Class'",
                StringComparison.Ordinal)));
    }

    private static string Slice(string text, LucentSourceMapRange range)
    {
        var start = Offset(text, range.StartLine, range.StartCharacter);
        var end = Offset(text, range.EndLine, range.EndCharacter);
        return text[start..end];
    }

    private static int Offset(string text, int line, int character)
    {
        var offset = 0;
        for (var current = 0; current < line; current++)
        {
            offset = text.IndexOf('\n', offset) + 1;
            Assert.IsGreaterThan(0, offset);
        }
        return offset + character;
    }

    [TestMethod]
    public void Conditional_generation_matches_checked_in_snapshot()
    {
        var source = File.ReadAllText(RepositoryPaths.ConditionalSnapshotSource);
        const string logicalSourcePath = "tests/Lucent.Compiler.Tests/Snapshots/ConditionalRegion.lui";

        var first = LucentCompiler.Compile(source, logicalSourcePath);
        var second = LucentCompiler.Compile(source, logicalSourcePath);

        Assert.IsTrue(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics));
        Assert.AreEqual(first.GeneratedSource, second.GeneratedSource);
        StringAssert.Contains(first.GeneratedSource, "new ConditionalRegion(__lucent_owner, roots =>");
        StringAssert.Contains(first.GeneratedSource, "return Fragment.Concat(Fragment.From(__lucent_conditional1TrueControl1!));");
    }

    [TestMethod]
    [DataRow("CompiledItemTemplate")]
    [DataRow("CompiledNativeBinding")]
    public void Compiled_binding_generation_matches_checked_in_snapshot(string name)
    {
        var relative = $"tests/Lucent.Compiler.Tests/Snapshots/{name}.lui";
        var result = LucentCompiler.Compile(
            File.ReadAllText(RepositoryPaths.Snapshot(name + ".lui")), relative);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.AreEqual(
            File.ReadAllText(RepositoryPaths.Snapshot(name + ".g.cs.snap")).Replace("\r\n", "\n"),
            result.GeneratedSource!.Replace("\r\n", "\n"));
    }

    [TestMethod]
    public void Conditional_parser_preserves_multiple_branch_roots_for_the_binder()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                Fragment Render()
                {
                    return StackPanel {
                        if (true) { TextBlock { } Border { } }
                    };
                }
            }
            """,
            "conditional-roots.lui");

        var conditional = Assert.IsInstanceOfType<UiIfSyntax>(
            result.Syntax!.Component.RenderMethod.Root.Members.Single());
        Assert.HasCount(2, conditional.TrueBranch.Roots);
        Assert.IsFalse(result.Diagnostics.Any(diagnostic => diagnostic.Code == "LUC1001"));
        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [TestMethod]
    public void Module_manifest_rejects_identity_hash_and_version_mismatches_without_loading_an_assembly()
    {
        var identity = new LucentAssemblyIdentity("Demo", "1.0.0.0", "", "");
        var bytes = LucentModuleManifest.Create(
            new LucentProjectContext(ProjectPath: Path.Combine(Path.GetTempPath(), "Demo.csproj")),
            [new LucentSourceInput("App.lui", "namespace Demo; component App() => Border {}; ")], identity);

        Assert.IsTrue(LucentModuleManifest.TryRead(bytes, identity, out var manifest, out var error), error);
        Assert.AreEqual("Demo", manifest!.Assembly.Name);
        Assert.IsFalse(LucentModuleManifest.TryRead(bytes,
            identity with { Name = "Other" }, out _, out error));
        StringAssert.Contains(error!, "invalid");
    }

    [TestMethod]
    public void Module_manifest_golden_current_and_unknown_fields_normalize()
    {
        var identity = new LucentAssemblyIdentity("Demo", "1.0.0.0", "", "");
        var fixtures = Path.Combine(RepositoryPaths.Root, "tests", "Lucent.Compiler.Tests", "Fixtures", "ModuleManifest");
        foreach (var name in new[] { "current", "unknown-fields" })
        {
            Assert.IsTrue(LucentModuleManifest.TryReadNormalized(File.ReadAllBytes(Path.Combine(fixtures, name + ".json")),
                identity, out var manifest, out var error), error);
            Assert.IsNotNull(manifest);
        }
    }

    [TestMethod]
    public void Module_manifest_rejects_duplicate_catalog_class_invalid_hash_and_incompatible_reader()
    {
        var identity = new LucentAssemblyIdentity("Demo", "1.0.0.0", "", "");
        var baseManifest = new LucentModuleManifestModel(1, 0, 0, "1.0", identity, ["Demo.Catalog"], [], []);
        Assert.IsTrue(LucentModuleManifest.TryReadNormalized(LucentModuleManifest.Serialize(baseManifest), identity, out _, out _));
        var duplicateCatalog = baseManifest with { StyleCatalogTypes = ["Demo.Catalog", "Demo.Catalog"] };
        Assert.IsFalse(LucentModuleManifest.TryReadNormalized(LucentModuleManifest.Serialize(duplicateCatalog), identity, out _, out _));
        var invalidHash = baseManifest with { Sources = [new SourceIdentity("App.lui", "not-a-hash", 0, 1)] };
        Assert.IsFalse(LucentModuleManifest.TryReadNormalized(LucentModuleManifest.Serialize(invalidHash), identity, out _, out _));
        var entry = new StyleClassEntry("button", null, StyleClassOrigin.LocalCss, null, ".button");
        var duplicateClass = baseManifest with { StyleClasses = [entry, entry] };
        Assert.IsFalse(LucentModuleManifest.TryReadNormalized(LucentModuleManifest.Serialize(duplicateClass), identity, out _, out _));
        var newerReader = baseManifest with { MinimumReaderMinor = LucentModuleManifest.FormatMinor + 1 };
        Assert.IsFalse(LucentModuleManifest.TryReadNormalized(LucentModuleManifest.Serialize(newerReader), identity, out _, out _));
        var unsupportedMinor = baseManifest with { FormatMinor = LucentModuleManifest.FormatMinor + 1 };
        Assert.IsFalse(LucentModuleManifest.TryReadNormalized(LucentModuleManifest.Serialize(unsupportedMinor), identity, out _, out _));
        var requiredFields = baseManifest with
        {
            StyleClasses = [new StyleClassEntry("button", null, StyleClassOrigin.LocalCss,
                new SourceIdentity("App.css", new string('0', 64), 0, 1), ".button")],
            Sources = [new SourceIdentity("App.lui", new string('0', 64), 0, 1)],
        };
        var requiredJson = Encoding.UTF8.GetString(LucentModuleManifest.Serialize(requiredFields));
        Assert.IsFalse(LucentModuleManifest.TryReadNormalized(
            Encoding.UTF8.GetBytes(requiredJson.Replace("\"origin\":0,", "", StringComparison.Ordinal)), identity, out _, out _));
        Assert.IsFalse(LucentModuleManifest.TryReadNormalized(
            Encoding.UTF8.GetBytes(requiredJson.Replace("\"start\":0,", "", StringComparison.Ordinal)), identity, out _, out _));
        Assert.IsFalse(LucentModuleManifest.TryReadNormalized("{"u8.ToArray(), identity, out _, out _));
    }

    [TestMethod]
    public void Module_manifest_requires_its_version_envelope_and_bounds_resource_lengths()
    {
        var identity = new LucentAssemblyIdentity("Demo", "1.0.0.0", "", "");
        var manifest = new LucentModuleManifestModel(1, 0, 0, "1.0", identity, [], [], []);
        var json = System.Text.Encoding.UTF8.GetString(LucentModuleManifest.Serialize(manifest));
        foreach (var field in new[]
        {
            "\"formatMajor\":1,",
            "\"formatMinor\":0,",
            "\"minimumReaderMinor\":0,",
            "\"producerVersion\":\"1.0\",",
        })
        {
            Assert.IsFalse(LucentModuleManifest.TryReadNormalized(
                System.Text.Encoding.UTF8.GetBytes(json.Replace(field, "", StringComparison.Ordinal)),
                identity, out _, out _), field);
        }

        Assert.IsFalse(LucentModuleManifest.TryReadResourceBytes([], out _, out _));
        Assert.IsFalse(LucentModuleManifest.TryReadResourceBytes(
            System.Collections.Immutable.ImmutableArray.CreateRange(BitConverter.GetBytes(int.MaxValue)),
            out _, out _));

        var boundary = new byte[sizeof(int) + LucentModuleManifest.MaximumResourceBytes];
        BitConverter.GetBytes(LucentModuleManifest.MaximumResourceBytes).CopyTo(boundary, 0);
        Assert.IsTrue(LucentModuleManifest.TryReadResourceBytes(
            System.Collections.Immutable.ImmutableArray.CreateRange(boundary), out var bytes, out var error), error);
        Assert.AreEqual(LucentModuleManifest.MaximumResourceBytes, bytes.Length);
        var overBoundary = System.Collections.Immutable.ImmutableArray.CreateRange(
            BitConverter.GetBytes(LucentModuleManifest.MaximumResourceBytes + 1));
        Assert.IsFalse(LucentModuleManifest.TryReadResourceBytes(overBoundary, out _, out error));
        StringAssert.Contains(error!, "exceeds");
    }

    [TestMethod]
    public void Public_style_catalog_metadata_names_require_an_accessible_declaring_chain()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-catalogs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var assembly = Path.Combine(directory, "Catalogs.dll");
            WriteAssembly(assembly, """
                namespace Fixture;
                public class Outer { public class Catalog { } }
                internal class HiddenOuter { public class Catalog { } }
                public class OuterWithHiddenInner { internal class HiddenInner { public class Catalog { } } }
                """);

            Assert.IsTrue(LucentModuleManifest.HasPublicCatalogTypes(assembly, ["Fixture.Outer+Catalog"], out var error), error);
            Assert.IsFalse(LucentModuleManifest.HasPublicCatalogTypes(assembly, ["Fixture.HiddenOuter+Catalog"], out error));
            Assert.IsFalse(LucentModuleManifest.HasPublicCatalogTypes(assembly, ["Fixture.OuterWithHiddenInner+Catalog"], out error));
            Assert.IsFalse(LucentModuleManifest.HasPublicCatalogTypes(assembly, ["Fixture.Outer.Catalog"], out error));
            Assert.IsFalse(LucentModuleManifest.HasPublicCatalogTypes(assembly, ["Fixture.Outer+Catalog", "Fixture.Outer+Catalog"], out error));
            StringAssert.Contains(error!, "unique");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Referenced_manifest_cache_honors_cancellation_without_file_io()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cache = new ReferencedManifestCache([Path.Combine(Path.GetTempPath(), "missing.dll")]);
        Assert.Throws<OperationCanceledException>(() => cache.Load(cancellation.Token));
    }

    [TestMethod]
    public void Manifest_source_hashes_are_compared_only_when_live_source_is_available()
    {
        var identity = new SourceIdentity("App.lui", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("live"u8)), 0, 4);
        Assert.IsTrue(LucentModuleManifest.MatchesLiveSource(identity, "live"));
        Assert.IsFalse(LucentModuleManifest.MatchesLiveSource(identity, "stale"));
    }

    [TestMethod]
    public void Local_manifest_validation_uses_live_lui_and_css_without_merging_metadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-local-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var project = Path.Combine(directory, "App.csproj");
            var lui = Path.Combine(directory, "App.lui");
            var css = Path.Combine(directory, "App.css");
            var assembly = Path.Combine(directory, "Referenced.dll");
            const string luiText = "namespace Demo; component App() => Border {};";
            const string cssText = "Border.card { width: 1; }";
            var identity = new LucentAssemblyIdentity("Referenced", "0.0.0.0", "", "");
            var manifest = LucentModuleManifest.Create(new LucentProjectContext(ProjectPath: project),
                [new LucentSourceInput(lui, luiText, css, cssText)], identity);
            WriteReferencedAssembly(assembly, manifestBytes: manifest);
            var context = new LucentProjectContext(ProjectPath: project, TargetPath: assembly);
            var live = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [lui] = luiText, [css] = cssText };
            Assert.IsTrue(LucentModuleManifest.TryReadLocalSnapshot(context, live, CancellationToken.None, out _, out var error), error);
            live[lui] += " ";
            Assert.IsFalse(LucentModuleManifest.TryReadLocalSnapshot(context, live, CancellationToken.None, out _, out error));
            StringAssert.Contains(error!, "stale");
            live[lui] = luiText;
            live[css] += "\n";
            Assert.IsTrue(LucentModuleManifest.TryReadLocalSnapshot(context, live, CancellationToken.None, out _, out error), error);
            live[css] = "Border.card { width: 2; }";
            Assert.IsFalse(LucentModuleManifest.TryReadLocalSnapshot(context, live, CancellationToken.None, out _, out error));
            StringAssert.Contains(error!, "stale");

            const string typeOnlyCss = "Border { width: 1; }";
            var typeOnlyManifest = LucentModuleManifest.Create(new LucentProjectContext(ProjectPath: project),
                [new LucentSourceInput(lui, luiText, css, typeOnlyCss)], identity);
            WriteReferencedAssembly(assembly, manifestBytes: typeOnlyManifest);
            live[css] = "Border { width: 2; }";
            Assert.IsFalse(LucentModuleManifest.TryReadLocalSnapshot(context, live, CancellationToken.None, out _, out error));
            StringAssert.Contains(error!, "stale");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Local_manifest_validation_treats_a_missing_manifest_as_silent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-local-manifest-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var assembly = Path.Combine(directory, "Plain.dll");
            WriteReferencedAssembly(assembly, includeManifest: false);
            var context = new LucentProjectContext(
                ProjectPath: Path.Combine(directory, "App.csproj"),
                TargetPath: assembly);

            Assert.IsFalse(LucentModuleManifest.TryReadLocalSnapshot(
                context, new Dictionary<string, string>(), CancellationToken.None,
                out var manifest, out var error));
            Assert.IsNull(manifest);
            Assert.IsNull(error);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Referenced_manifest_generation_normalizes_refreshes_and_bounds_diagnostics_without_execution()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-reference-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var assembly = Path.Combine(directory, "Referenced.dll");
            WriteReferencedAssembly(assembly, "first");
            var context = new LucentProjectContext(ReferencePaths: [assembly]);
            var first = LucentCompiler.LoadReferencedManifestSnapshot(context, CancellationToken.None);
            Assert.HasCount(1, first.Catalog.Entries);
            Assert.AreEqual("first", first.Catalog.Entries[0].Name);
            Assert.HasCount(0, first.Diagnostics);

            WriteReferencedAssembly(assembly, "second");
            var refreshed = LucentCompiler.LoadReferencedManifestSnapshot(context, CancellationToken.None);
            Assert.HasCount(1, refreshed.Catalog.Entries);
            Assert.AreEqual("second", refreshed.Catalog.Entries[0].Name);

            WriteReferencedAssembly(assembly, manifestBytes: "{"u8.ToArray());
            var malformed = LucentCompiler.LoadReferencedManifestSnapshot(context, CancellationToken.None);
            Assert.HasCount(0, malformed.Catalog.Entries);
            Assert.HasCount(1, malformed.Diagnostics);

            WriteReferencedAssembly(assembly, includeManifest: false);
            var missing = LucentCompiler.LoadReferencedManifestSnapshot(context, CancellationToken.None);
            Assert.HasCount(0, missing.Diagnostics);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task Clean_project_and_local_package_consumers_read_the_same_embedded_catalog()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-manifest-consumer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var repository = FindRepositoryRoot();
            var fixture = Path.Combine(repository, "tests", "Lucent.Compiler.Tests", "Fixtures", "ManifestConsumer");
            var producer = Path.Combine(directory, "producer");
            var packages = Path.Combine(directory, "packages");
            var cache = Path.Combine(directory, "package-cache");
            Directory.CreateDirectory(producer);
            foreach (var file in new[] { "Producer.lui", "Producer.css", "InstallableCatalog.cs" })
                File.Copy(Path.Combine(fixture, file), Path.Combine(producer, file));
            var producerProject = Path.Combine(producer, "Producer.csproj");
            await File.WriteAllTextAsync(producerProject, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net9.0</TargetFramework><AssemblyName>Fixture.ManifestProducer</AssemblyName><PackageId>Fixture.ManifestProducer</PackageId><Version>1.0.0</Version><SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking></PropertyGroup>
                  <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.props")}}" />
                  <ItemGroup>
                    <PackageReference Include="Avalonia" Version="12.1.1" />
                    <LucentSource Include="Producer.lui" />
                    <LucentStyleCatalog Include="Fixture.InstallableCatalog" />
                  </ItemGroup>
                  <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.targets")}}" />
                </Project>
                """);
            var build = await DotNetAsync(producer, "build", producerProject, "--nologo", "-nodeReuse:false");
            Assert.AreEqual(0, build.ExitCode, build.Output);
            var producerAssembly = Path.Combine(producer, "bin", "Debug", "net9.0", "Fixture.ManifestProducer.dll");
            var manifest = File.ReadAllText(Path.Combine(producer, "obj", "Debug", "net9.0", "Lucent", "Lucent.ModuleManifest.v1.json"));
            StringAssert.Contains(manifest, "Fixture.InstallableCatalog");
            Assert.IsFalse(manifest.Contains(directory, StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(manifest.Contains("component Producer", StringComparison.Ordinal));
            var lastSuccessfulPath = Path.Combine(producer, "obj", "Debug", "net9.0", "Lucent", "Lucent.ModuleManifest.v1.last-successful.json");
            var lastSuccessful = await File.ReadAllBytesAsync(lastSuccessfulPath);
            var producerProjectText = await File.ReadAllTextAsync(producerProject);
            await File.WriteAllTextAsync(producerProject, producerProjectText.Replace("Fixture.InstallableCatalog", "Fixture.MissingCatalog", StringComparison.Ordinal));
            var invalidCatalog = await DotNetAsync(producer, "build", producerProject, "--nologo", "-nodeReuse:false");
            Assert.AreNotEqual(0, invalidCatalog.ExitCode, invalidCatalog.Output);
            CollectionAssert.AreEqual(lastSuccessful, await File.ReadAllBytesAsync(lastSuccessfulPath));
            await File.WriteAllTextAsync(producerProject, producerProjectText);

            var projectConsumer = Path.Combine(directory, "project-consumer");
            Directory.CreateDirectory(projectConsumer);
            var projectConsumerProject = Path.Combine(projectConsumer, "Consumer.csproj");
            await File.WriteAllTextAsync(projectConsumerProject, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include=\"../producer/Producer.csproj\" /></ItemGroup></Project>");
            var projectBuild = await DotNetAsync(projectConsumer, "build", projectConsumerProject, "--nologo", "-nodeReuse:false");
            Assert.AreEqual(0, projectBuild.ExitCode, projectBuild.Output);

            var pack = await DotNetAsync(producer, "pack", producerProject, "--no-build", "--no-restore",
                "--configuration", "Debug", "-o", packages, "--nologo", "-nodeReuse:false",
                "-p:BuildProjectReferences=false");
            Assert.AreEqual(0, pack.ExitCode, pack.Output);
            var packageConsumer = Path.Combine(directory, "package-consumer");
            Directory.CreateDirectory(packageConsumer);
            await File.WriteAllTextAsync(Path.Combine(packageConsumer, "NuGet.Config"),
                $"<configuration><packageSources><clear /><add key=\"local\" value=\"{packages.Replace("\\", "/")}\" /></packageSources></configuration>");
            var packageConsumerProject = Path.Combine(packageConsumer, "Consumer.csproj");
            await File.WriteAllTextAsync(packageConsumerProject, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Fixture.ManifestProducer\" Version=\"1.0.0\" /></ItemGroup></Project>");
            var restore = await DotNetAsync(packageConsumer,
                ["restore", packageConsumerProject, "--nologo", "-nodeReuse:false"],
                new Dictionary<string, string> { ["NUGET_PACKAGES"] = cache });
            Assert.AreEqual(0, restore.ExitCode, restore.Output);
            var packageBuild = await DotNetAsync(packageConsumer,
                ["build", packageConsumerProject, "--no-restore", "--nologo", "-nodeReuse:false"],
                new Dictionary<string, string> { ["NUGET_PACKAGES"] = cache });
            Assert.AreEqual(0, packageBuild.ExitCode, packageBuild.Output);
            var packageAssembly = Path.Combine(cache, "fixture.manifestproducer", "1.0.0", "lib", "net9.0", "Fixture.ManifestProducer.dll");

            var projectCatalog = LucentCompiler.LoadReferencedManifestSnapshot(new LucentProjectContext(ReferencePaths: [producerAssembly]), CancellationToken.None);
            var packageCatalog = LucentCompiler.LoadReferencedManifestSnapshot(new LucentProjectContext(ReferencePaths: [packageAssembly]), CancellationToken.None);
            Assert.HasCount(0, projectCatalog.Diagnostics);
            Assert.HasCount(0, packageCatalog.Diagnostics);
            CollectionAssert.AreEqual(projectCatalog.Catalog.Entries.ToArray(), packageCatalog.Catalog.Entries.ToArray());
            CollectionAssert.AreEqual(new[] { "fixture-button", "fixture-card" }, projectCatalog.Catalog.Entries.Select(entry => entry.Name).ToArray());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void WriteReferencedAssembly(string path, string? className = null,
        byte[]? manifestBytes = null, bool includeManifest = true)
    {
        var identity = new LucentAssemblyIdentity("Referenced", "0.0.0.0", "", "");
        manifestBytes ??= LucentModuleManifest.Serialize(new LucentModuleManifestModel(
            LucentModuleManifest.FormatMajor, LucentModuleManifest.FormatMinor, 0, "1.0", identity, [],
            [new StyleClassEntry(className!, null, StyleClassOrigin.LocalCss, null, "." + className)], []));
        IEnumerable<ResourceDescription> resources = !includeManifest ? [] : [new ResourceDescription(
            LucentModuleManifest.ResourceName, () => new MemoryStream(manifestBytes), isPublic: true)];
        var compilation = CSharpCompilation.Create("Referenced",
            [CSharpSyntaxTree.ParseText("public sealed class ReferenceMarker { }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = File.Create(path);
        var result = compilation.Emit(stream, manifestResources: resources);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }

    private static void WriteAssembly(string path, string source)
    {
        var compilation = CSharpCompilation.Create("Catalogs",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = File.Create(path);
        var result = compilation.Emit(stream);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }

    private static async Task<(int ExitCode, string Output)> DotNetAsync(string workingDirectory,
        params string[] arguments) => await DotNetAsync(workingDirectory, arguments, null);

    private static Task<(int ExitCode, string Output)> DotNetAsync(string workingDirectory,
        string[] arguments, IReadOnlyDictionary<string, string>? environment)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = workingDirectory };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        if (environment is not null) foreach (var (name, value) in environment) start.Environment[name] = value;
        using var process = Process.Start(start)!;
        process.WaitForExit();
        return Task.FromResult((process.ExitCode, string.Empty));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Lucent.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

}
