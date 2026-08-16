using Lucent.Compiler.Syntax;

namespace Lucent.Compiler.Tests;

[TestClass]
public sealed class CompilerTests
{
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
        var expected = File.ReadAllText(RepositoryPaths.GeneratedCounter);

        const string logicalSourcePath = "examples/counter/Counter.lui";
        var first = LucentCompiler.Compile(source, logicalSourcePath);
        var second = LucentCompiler.Compile(source, logicalSourcePath);

        Assert.IsTrue(first.Succeeded);
        Assert.AreEqual(expected, first.GeneratedSource);
        Assert.AreEqual(first.GeneratedSource, second.GeneratedSource);
        Assert.IsFalse(first.GeneratedSource!.Contains("FontSize", StringComparison.Ordinal));
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
}
