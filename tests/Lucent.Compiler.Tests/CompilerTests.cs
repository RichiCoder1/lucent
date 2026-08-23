using Lucent.Compiler.Syntax;

namespace Lucent.Compiler.Tests;

[TestClass]
public sealed class CompilerTests
{
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
}
