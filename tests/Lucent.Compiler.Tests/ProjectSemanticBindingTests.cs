namespace Lucent.Compiler.Tests;

[TestClass]
public sealed class ProjectSemanticBindingTests
{
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
            StringAssert.Contains(loaded.Documentation!, "FancyControl sender");
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
