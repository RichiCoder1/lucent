namespace Lucent.Compiler.Tests;

[TestClass]
public sealed class ProjectSemanticBindingTests
{
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
}
