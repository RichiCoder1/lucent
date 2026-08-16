namespace Lucent.Compiler.Tests;

[TestClass]
public sealed class ComponentCompositionTests
{
    [TestMethod]
    public void Composition_generation_matches_complete_snapshot_in_either_project_order()
    {
        var root = Path.Combine(RepositoryPaths.Root, "tests", "Lucent.Compiler.Tests", "Snapshots", "Composition");
        var inputs = new[]
        {
            new LucentSourceInput("Leaf.lui", File.ReadAllText(Path.Combine(root, "Leaf.lui"))),
            new LucentSourceInput("Host.lui", File.ReadAllText(Path.Combine(root, "Host.lui"))),
            new LucentSourceInput("App.lui", File.ReadAllText(Path.Combine(root, "App.lui"))),
        };
        var expected = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root, "tests", "Lucent.Compiler.Tests", "Snapshots", "Composition.g.cs.snap"));

        foreach (var ordered in new[] { inputs, inputs.Reverse().ToArray() })
        {
            var result = LucentCompiler.CompileProject(ordered);
            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine,
                result.Sources.SelectMany(source => source.Result.Diagnostics)));
            var actual = string.Join(Environment.NewLine,
                result.Sources.OrderBy(source => source.SourcePath, StringComparer.Ordinal)
                    .Select(source => $"// Composition snapshot: {source.SourcePath}{Environment.NewLine}{source.Result.GeneratedSource}"));
            Assert.AreEqual(
                expected.Replace("\r\n", "\n").TrimEnd(),
                actual.Replace("\r\n", "\n").TrimEnd());
        }
    }

    [TestMethod]
    public void Empty_fragment_is_a_valid_zero_root_component()
    {
        var result = LucentCompiler.Compile(
            "namespace Demo; component Empty() => Fragment {};",
            "Empty.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource!, "return Fragment.Empty;");
    }

    [TestMethod]
    public void Css_on_an_empty_fragment_is_diagnosed_before_generation()
    {
        var result = LucentCompiler.CompileProject([
            new LucentSourceInput("Empty.lui", "namespace Demo; component Empty() => Fragment {};",
                "Empty.css", "TextBlock { opacity: 0.5; }")]);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.Sources[0].Result.GeneratedSource);
        Assert.IsTrue(result.Sources[0].Result.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "LUC4001" &&
            diagnostic.Message.Contains("empty Fragment", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Css_on_a_component_that_indirectly_produces_an_empty_fragment_is_diagnosed()
    {
        var result = LucentCompiler.CompileProject([
            new LucentSourceInput("Empty.lui", "namespace Demo; component Empty() => Fragment {};"),
            new LucentSourceInput("Host.lui", "namespace Demo; component Host() => Empty {};", "Host.css",
                "TextBlock { opacity: 0.5; }")]);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Sources.SelectMany(source => source.Result.Diagnostics).Any(diagnostic =>
            diagnostic.Code == "LUC4001" &&
            diagnostic.Message.Contains("empty Fragment", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Keyed_slot_shape_with_nested_native_roots_is_diagnosed()
    {
        var result = LucentCompiler.CompileProject([
            new LucentSourceInput("Row.lui", "namespace Demo; component Row() { slot actions; Fragment Render() => ContentControl { yield actions; }; }"),
            new LucentSourceInput("Leaf.lui", "namespace Demo; component Leaf() { slot actions; Fragment Render() => ContentControl { yield actions; }; }"),
            new LucentSourceInput("Host.lui", """
                namespace Demo;
                component Host()
                {
                    private readonly State<string[]> values = new(["a"]);
                    Fragment Render() => StackPanel {
                        foreach (var value in values.Value) keyed by value {
                            Row { slot actions { Leaf { slot actions { TextBlock {} } } } }
                        }
                    };
                }
                """)]);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Sources.SelectMany(source => source.Result.Diagnostics).Any(diagnostic =>
            diagnostic.Message.Contains("Nested keyed slot supplies", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Keyed_native_rows_mount_and_update_component_children()
    {
        var result = LucentCompiler.CompileProject([
            new LucentSourceInput("Child.lui", "namespace Demo; component Child(string value) { slot actions; Fragment Render() => StackPanel { TextBlock { Text: value; } ContentControl { yield actions; } }; }"),
            new LucentSourceInput("Host.lui", """
                namespace Demo;
                component Host()
                {
                    private readonly State<string[]> values = new(["a"]);
                    Fragment Render() => StackPanel {
                        foreach (var value in values.Value) keyed by value {
                            Border { Child(value) { slot actions { TextBlock { Text: value; } } } }
                        }
                    };
                }
                """)]);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine,
            result.Sources.SelectMany(source => source.Result.Diagnostics)));
        var generated = result.Sources.Single(source => source.SourcePath == "Host.lui").Result.GeneratedSource!;
        var childGenerated = result.Sources.Single(source => source.SourcePath == "Child.lui").Result.GeneratedSource!;
        StringAssert.Contains(childGenerated, "__lucent_control2!.Text = __lucent_inputValue;");
        StringAssert.Contains(generated, "__lucent_rowComponent1_1 = new ChildComponent");
        var refreshStart = generated.IndexOf("void Refresh()", StringComparison.Ordinal);
        var refreshEnd = generated.IndexOf("Refresh();", refreshStart, StringComparison.Ordinal);
        Assert.IsTrue(refreshStart >= 0 && refreshEnd > refreshStart);
        var refresh = generated[refreshStart..refreshEnd];
        StringAssert.Contains(refresh, "__lucent_rowComponent1_1.UpdateInputs(value);");
        Assert.AreEqual(1, generated.Split("__lucent_rowComponent1_1.UpdateInputs(value);", StringSplitOptions.None).Length - 1);
        Assert.IsTrue(generated.IndexOf("__lucent_rowComponent1_1.UpdateInputs(value);", StringComparison.Ordinal) <
                      generated.IndexOf("__lucent_rowSlot1_1ActionsRefresh();", StringComparison.Ordinal));
        StringAssert.Contains(generated, "Text = value;");
        StringAssert.Contains(generated, "control1.Child = __lucent_rowComponent1_1Roots.Count == 0 ? null : __lucent_rowComponent1_1Roots[0];");
        StringAssert.Contains(generated, "Fragment __lucent_rowSlot1_1Actions(ComponentOwner __lucent_slotOwner)");
        StringAssert.Contains(generated, "new global::Avalonia.Controls.TextBlock()");
        StringAssert.Contains(generated, "__lucent_rowSlot1_1ActionsRefresh();");
        StringAssert.Contains(generated, "__lucent_slotOwner.OnDispose");
    }

    [TestMethod]
    public void Keyed_slot_refresh_is_reset_after_yield_owner_disposal()
    {
        var result = LucentCompiler.CompileProject([
            new LucentSourceInput("Row.lui", "namespace Demo; component Row() { slot actions; Fragment Render() => ContentControl { yield actions; }; }"),
            new LucentSourceInput("Host.lui", """
                namespace Demo;
                component Host()
                {
                    private readonly State<string[]> values = new(["a"]);
                    Fragment Render() => StackPanel {
                        foreach (var value in values.Value) keyed by value {
                            Row { slot actions { TextBlock { Text: value; } } }
                        }
                    };
                }
                """)]);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine,
            result.Sources.SelectMany(source => source.Result.Diagnostics)));
        var generated = result.Sources.Single(source => source.SourcePath == "Host.lui").Result.GeneratedSource!;
        StringAssert.Contains(generated, "__lucent_slotOwner.OnDispose");
        StringAssert.Contains(generated, "__lucent_disposed");
        StringAssert.Contains(generated, "= static () => { };");
    }

    [TestMethod]
    public void Scalar_content_rejects_a_component_with_multiple_fixed_roots()
    {
        var result = LucentCompiler.CompileProject([
            new LucentSourceInput("Many.lui", "namespace Demo; component Many() => Fragment { TextBlock {}; Border {}; };") ,
            new LucentSourceInput("Host.lui", "namespace Demo; component Host() => ContentControl { Many {} };")]);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Sources.SelectMany(source => source.Result.Diagnostics)
            .Any(diagnostic => diagnostic.Message.Contains("accepts only one child", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Component_cycles_are_diagnosed_through_structural_regions()
    {
        var result = LucentCompiler.CompileProject([
            new LucentSourceInput("A.lui", "namespace One; component A() => StackPanel { if (true) { B {} } };") ,
            new LucentSourceInput("B.lui", "namespace One; component B() => StackPanel { foreach (var x in new[] { 1 }) keyed by x { A {} } };")]);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Sources.SelectMany(source => source.Result.Diagnostics)
            .Any(diagnostic => diagnostic.Message.Contains("cycle", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Keyed_component_rows_keep_a_fragment_and_update_retained_inputs()
    {
        var result = LucentCompiler.CompileProject([
            new LucentSourceInput("Row.lui", "namespace Demo; component Row(string value) => TextBlock { Text: value; };"),
            new LucentSourceInput("List.lui", """
                namespace Demo;
                component List()
                {
                    private readonly State<string[]> values = new(["a"]);
                    Fragment Render() { return StackPanel { foreach (var value in values.Value) keyed by value { Row(value) {} } }; }
                }
                """)]);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine,
            result.Sources.SelectMany(source => source.Result.Diagnostics)));
        var source = result.Sources.Single(item => item.SourcePath == "List.lui").Result.GeneratedSource!;
        StringAssert.Contains(source, "Fragment Roots { get; }");
        StringAssert.Contains(source, "child.UpdateInputs(value);");
        StringAssert.Contains(source, "foreach (var root in entry.Roots.Roots)");
    }

    [TestMethod]
    public void Keyed_component_rows_reintroduce_current_values_for_component_slot_roots()
    {
        var result = LucentCompiler.CompileProject([
            new LucentSourceInput("Badge.lui", "namespace Demo; component Badge(string value) => TextBlock { Text: value; };"),
            new LucentSourceInput("Row.lui", """
                namespace Demo;
                component Row(string value)
                {
                    slot actions;
                    Fragment Render() => ContentControl { yield actions; };
                }
                """),
            new LucentSourceInput("List.lui", """
                namespace Demo;
                component List()
                {
                    private readonly State<string[]> values = new(["a"]);
                    Fragment Render() => StackPanel {
                        foreach (var value in values.Value) keyed by value {
                            Row(value) { slot actions { Badge(value) {} } }
                        }
                    };
                }
                """)]);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine,
            result.Sources.SelectMany(source => source.Result.Diagnostics)));
        var source = result.Sources.Single(item => item.SourcePath == "List.lui").Result.GeneratedSource!;
        StringAssert.Contains(source, "var value = loopValue.Value;");
        StringAssert.Contains(source, "new BadgeComponent(__lucent_loopSlotChildOwner1");
        StringAssert.Contains(source, "__lucent_loopSlotComponent1.Mount()");
        StringAssert.Contains(source, "child.UpdateInputs(value);");
    }

    [TestMethod]
    public void Conditional_branch_can_mount_a_component_root()
    {
        var result = LucentCompiler.CompileProject(
        [
            new LucentSourceInput("Child.lui", "namespace Demo; component Child() => TextBlock { Text: \"child\"; };"),
            new LucentSourceInput("Parent.lui", """
                namespace Demo;
                component Parent()
                {
                    private readonly State<bool> visible = new(true);
                    Fragment Render() { return StackPanel { if (visible.Value) { Child { } } }; }
                }
                """),
        ]);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine,
            result.Sources.SelectMany(source => source.Result.Diagnostics)));
        var source = result.Sources.Single(source => source.SourcePath == "Parent.lui").Result.GeneratedSource!;
        StringAssert.Contains(source, "new ChildComponent(__lucent_branchRootOwner1True1, static _ => Fragment.Empty)");
        StringAssert.Contains(source, "return Fragment.Concat(__lucent_component1Roots);");
    }

    [TestMethod]
    public void Conditional_component_updates_retained_inputs_without_remounting()
    {
        var result = LucentCompiler.CompileProject([
            new LucentSourceInput("Child.lui", "namespace Demo; component Child(string text) => TextBlock { Text: text; };"),
            new LucentSourceInput("Parent.lui", """
                namespace Demo;
                component Parent()
                {
                    private readonly State<bool> visible = new(true);
                    private readonly State<string> text = new("first");
                    Fragment Render() => ContentControl { if (visible.Value) { Child(text.Value) {} } };
                }
                """),
        ]);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine,
            result.Sources.SelectMany(source => source.Result.Diagnostics)));
        var source = result.Sources.Single(item => item.SourcePath == "Parent.lui").Result.GeneratedSource!;
        StringAssert.Contains(source, "__lucent_component1.UpdateInputs(__lucent_stateText);");
        StringAssert.Contains(source, "__lucent_component1Roots = __lucent_component1!.Mount();");
    }

    [TestMethod]
    public void Static_and_conditional_component_slots_preserve_supplied_component_roots()
    {
        var result = LucentCompiler.CompileProject([
            new LucentSourceInput("Leaf.lui", "namespace Demo; component Leaf(string text) => TextBlock { Text: text; };"),
            new LucentSourceInput("Host.lui", """
                namespace Demo;
                component Host()
                {
                    slot actions;
                    private readonly State<bool> visible = new(true);
                    Fragment Render() => ContentControl {
                        if (visible.Value) { ContentControl { yield actions; } }
                    };
                }
                """),
            new LucentSourceInput("App.lui", """
                namespace Demo;
                component App() => Window {
                    Host { slot actions { Leaf("static") {} } }
                };
                """),
        ]);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine,
            result.Sources.SelectMany(source => source.Result.Diagnostics)));
        var appSource = result.Sources.Single(item => item.SourcePath == "App.lui").Result.GeneratedSource!;
        StringAssert.Contains(appSource, "__lucent_MountSlot2Actions");
        StringAssert.Contains(appSource, "new LeafComponent");
        var hostSource = result.Sources.Single(item => item.SourcePath == "Host.lui").Result.GeneratedSource!;
        StringAssert.Contains(hostSource, "__lucent_branchYieldOwner1TrueActions");
    }

    [TestMethod]
    public void Component_styles_project_to_component_only_roots()
    {
        var result = LucentCompiler.CompileProject([
            new LucentSourceInput("Child.lui", "namespace Demo; component Child() => TextBlock { Text: \"child\"; };"),
            new LucentSourceInput("Host.lui", "namespace Demo; component Host() => Child {};", "Host.css",
                "TextBlock { opacity: 0.5; }")]);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine,
            result.Sources.SelectMany(source => source.Result.Diagnostics)));
        StringAssert.Contains(result.Sources.Single(source => source.SourcePath == "Host.lui")
            .Result.GeneratedSource!, "__lucent_styleRoot1.Styles.Add");
    }

    [TestMethod]
    public void Component_styles_preserve_class_only_projected_rules()
    {
        var result = LucentCompiler.CompileProject([
            new LucentSourceInput("Child.lui", "namespace Demo; component Child() => TextBlock { Text: \"child\"; };"),
            new LucentSourceInput("Host.lui", "namespace Demo; component Host() => Child {};", "Host.css",
                ".surface { opacity: 0.5; }")]);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine,
            result.Sources.SelectMany(source => source.Result.Diagnostics)));
        StringAssert.Contains(result.Sources.Single(source => source.SourcePath == "Host.lui")
            .Result.GeneratedSource!, ".Class(\"surface\")");
    }

    [TestMethod]
    public void Component_styles_project_conditional_and_keyed_component_roots()
    {
        var conditional = LucentCompiler.CompileProject([
            new LucentSourceInput("Child.lui", "namespace Demo; component Child() => TextBlock { Class: \"surface\"; };"),
            new LucentSourceInput("Host.lui", "namespace Demo; component Host() { private readonly State<bool> visible = new(true); Fragment Render() => ContentControl { if (visible.Value) { Child {} } }; }", "Host.css", ".surface { opacity: 0.5; }")]);
        Assert.IsTrue(conditional.Succeeded, string.Join(Environment.NewLine,
            conditional.Sources.SelectMany(source => source.Result.Diagnostics)));
        StringAssert.Contains(conditional.Sources.Single(source => source.SourcePath == "Host.lui")
            .Result.GeneratedSource!, "__lucent_conditionalStyle1True1.Styles.Add");

        var keyed = LucentCompiler.CompileProject([
            new LucentSourceInput("Child.lui", "namespace Demo; component Child() => TextBlock { Class: \"surface\"; };"),
            new LucentSourceInput("Host.lui", "namespace Demo; component Host() { private readonly State<string[]> values = new([\"a\"]); Fragment Render() => StackPanel { foreach (var value in values.Value) keyed by value { Child {} } }; }", "Host.css", ".surface:hover { opacity: 0.5; }")]);
        Assert.IsTrue(keyed.Succeeded, string.Join(Environment.NewLine,
            keyed.Sources.SelectMany(source => source.Result.Diagnostics)));
        StringAssert.Contains(keyed.Sources.Single(source => source.SourcePath == "Host.lui")
            .Result.GeneratedSource!, "__lucent_regionStyle1.Styles.Add");
    }

    [TestMethod]
    public void Ordinary_member_diagnostics_are_mapped_to_the_lui_member()
    {
        var result = LucentCompiler.Compile(
            "namespace Demo; component Host() { private int value; private void Broken() { Missing(); } Fragment Render() => Border {}; }",
            "Host.lui");

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "LUC2001" && diagnostic.Message.Contains("initializer", StringComparison.Ordinal)));
        Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "LUC3001" && diagnostic.Message.Contains("Missing", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Instance_initializers_follow_source_declaration_order()
    {
        var result = LucentCompiler.Compile(
            "namespace Demo; component Host() { private readonly string ordinary = \"ordinary\"; private readonly State<string> state = new(ordinary); private readonly Computed<string> computed = new(ct => Task.FromResult(state.Value), ordinary); Fragment Render() => Border {}; }",
            "Host.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.AreEqual(1, result.Syntax!.Component.AllOrdinaryMembers.Count,
            string.Join("|", (result.Syntax.Component.Members ?? [])
                .Select(member => member.GetType().Name)));
        var source = result.GeneratedSource!;
        source = source[source.IndexOf("internal HostComponent(", StringComparison.Ordinal)..];
        var ordinary = source.IndexOf("ordinary = \"ordinary\"", StringComparison.Ordinal);
        var state = source.IndexOf("__lucent_stateState =", StringComparison.Ordinal);
        var computed = source.IndexOf("__lucent_computedComputed =", StringComparison.Ordinal);
        if (!(ordinary >= 0 && state >= 0 && computed >= 0 && ordinary < state && state < computed))
        {
            Assert.Fail($"{ordinary},{state},{computed}");
        }
    }

    [TestMethod]
    public void Generated_component_type_collision_reports_lucent_and_csharp_locations()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "CardComponent.cs");
        File.WriteAllText(sourcePath, "namespace Demo; internal sealed class CardComponent { }");
        try
        {
            var result = LucentCompiler.Compile(
                "namespace Demo; component Card() => Border {};", "Card.lui",
                new LucentProjectContext(SourcePaths: [sourcePath]));

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.SourcePath == "Card.lui"));
            Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.SourcePath == sourcePath));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Project_sources_resolve_components_in_either_order()
    {
        var caller = new LucentSourceInput("Main.lui", """
            namespace Demo;
            component Main() => Window { Counter(initial: 10) {} };
            """);
        var callee = new LucentSourceInput("Counter.lui", """
            namespace Demo;
            component Counter(int initial = 0) => TextBlock { Text: $"Count: {initial}"; };
            """);

        foreach (var inputs in new[] { new[] { caller, callee }, new[] { callee, caller } })
        {
            var result = LucentCompiler.CompileProject(inputs);
            Assert.IsTrue(result.Succeeded,
                string.Join(Environment.NewLine, result.Sources.SelectMany(source => source.Result.Diagnostics)));
            var main = result.Sources.Single(source => source.SourcePath.EndsWith("Main.lui"));
            StringAssert.Contains(main.Result.GeneratedSource!, "new CounterComponent(");
            StringAssert.Contains(main.Result.GeneratedSource!, ".Mount();");
            Assert.IsTrue(main.Result.Symbols.Any(symbol =>
                symbol.Kind == LucentSemanticSymbolKind.Component && symbol.Name == "Counter"));
        }
    }

    [TestMethod]
    public void Duplicate_components_and_missing_required_arguments_fail_the_batch()
    {
        var result = LucentCompiler.CompileProject([
            new LucentSourceInput("One.lui", "namespace Demo; component Card(string text) => TextBlock { Text: text; };") ,
            new LucentSourceInput("Two.lui", "namespace Demo; component Card() => Border {};") ,
            new LucentSourceInput("Main.lui", "namespace Demo; component Main() => Window { Card {} };")]);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Sources.SelectMany(source => source.Result.Diagnostics).Any(diagnostic =>
            diagnostic.Message.Contains("declared more than once", StringComparison.Ordinal)));
        Assert.IsTrue(result.Sources.All(source => source.Result.GeneratedSource is null));
    }

    [TestMethod]
    public void Parameter_defaults_use_signature_semantics_and_source_namespace_context()
    {
        var valid = LucentCompiler.CompileProject([
            new LucentSourceInput("Good.lui", "namespace Demo; component Good(DayOfWeek day = DayOfWeek.Monday, double value = Math.PI) => Border {};")]);
        Assert.IsTrue(valid.Succeeded, string.Join(Environment.NewLine,
            valid.Sources.SelectMany(source => source.Result.Diagnostics)));

        var invalid = LucentCompiler.Compile(
            "namespace Demo; component Bad(int value = new object()) => Border {};",
            "Bad.lui");
        Assert.IsFalse(invalid.Succeeded);
        Assert.IsTrue(invalid.Diagnostics.Any(diagnostic =>
            diagnostic.Message.Contains("default", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Same_component_name_in_distinct_namespaces_uses_each_declaration_imports()
    {
        var result = LucentCompiler.CompileProject([
            new LucentSourceInput("One.lui", "namespace One; component Leaf() => Fragment { Border {} TextBlock {} };"),
            new LucentSourceInput("Two.lui", "namespace Two; component Leaf() => Border {};"),
            new LucentSourceInput("Host.lui", "namespace Two; component Host() => ContentControl { Leaf {} };")]);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine,
            result.Sources.SelectMany(source => source.Result.Diagnostics)));
    }

    [TestMethod]
    public void Imported_component_cardinality_uses_normalized_using_directives()
    {
        var result = LucentCompiler.CompileProject([
            new LucentSourceInput("Many.lui", "namespace Lib; component Many() => Fragment { Border {} TextBlock {} };"),
            new LucentSourceInput("Host.lui", "namespace App; using Lib; component Host() => ContentControl { Many {} };")]);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Sources.SelectMany(source => source.Result.Diagnostics)
            .Any(diagnostic => diagnostic.Message.Contains("accepts only one child", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Omitted_defaults_are_lowered_in_the_callee_namespace()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var csharpPath = Path.Combine(directory, "Defaults.cs");
        File.WriteAllText(csharpPath, "namespace Lib; public static class Defaults { public const string Value = \"default\"; }");
        try
        {
            var result = LucentCompiler.CompileProject([
                new LucentSourceInput("Child.lui", "namespace Lib; component Child(string text = Defaults.Value) => TextBlock { Text: text; };"),
                new LucentSourceInput("Host.lui", "namespace App; using Lib; component Host() => Child {};")],
                new LucentProjectContext(SourcePaths: [csharpPath]));

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine,
                result.Sources.SelectMany(source => source.Result.Diagnostics)));
            var host = result.Sources.Single(source => source.SourcePath == "Host.lui").Result.GeneratedSource!;
            StringAssert.Contains(host, "global::Lib.Defaults.Value");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Delayed_slot_structural_regions_are_diagnosed_instead_of_lowered_once()
    {
        var result = LucentCompiler.CompileProject([
            new LucentSourceInput("Host.lui", "namespace Demo; component Host() { slot actions; Fragment Render() => ContentControl { yield actions; }; }"),
            new LucentSourceInput("App.lui", "namespace Demo; component App() => Host { slot actions { StackPanel { if (true) { TextBlock {} } } } };")]);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Sources.SelectMany(source => source.Result.Diagnostics)
            .Any(diagnostic => diagnostic.Message.Contains("delayed slot supplies", StringComparison.Ordinal)));
    }
}
