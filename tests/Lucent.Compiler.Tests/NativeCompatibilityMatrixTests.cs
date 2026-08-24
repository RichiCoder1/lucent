using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.Styling;
using Avalonia.Threading;
using Lucent.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection;

namespace Lucent.Compiler.Tests;

[TestClass]
public sealed class NativeCompatibilityMatrixTests
{
    private const string Supported = "supported";
    private const string Bounded = "bounded subset";
    private const string Escape = "escape through explicit Avalonia C#";

    private static readonly CompatibilityRow[] Rows =
    [
        new("Styled and attached properties", Supported, [Evidence("tests/Lucent.Compiler.Tests/GeneralCompilerTests.cs", "GeneralCompilerTests", "Native_controls_properties_content_and_events_are_lowered_directly"), Evidence("tests/Lucent.Compiler.Tests/ComponentCompositionTests.cs", "ComponentCompositionTests", "Attached_properties_lower_to_static_setters")]),
        new("Routed and CLR events", Supported, [Evidence("tests/Lucent.Compiler.Tests/GeneralCompilerTests.cs", "GeneralCompilerTests", "Native_click_event_is_hooked_by_exact_event_name"), Evidence("tests/Lucent.Compiler.Tests/NativeCompatibilityMatrixTests.cs", "NativeCompatibilityMatrixTests", "Generated_clr_event_invokes_and_unsubscribes_on_dispose")]),
        new("Direct expressions and compiled bindings", Supported, [Evidence("tests/Lucent.Compiler.Tests/CompilerTests.cs", "CompilerTests", "Compiled_binding_generation_matches_checked_in_snapshot"), Evidence("tests/Lucent.Compiler.Tests/GeneralCompilerTests.cs", "GeneralCompilerTests", "Native_compiled_item_binding_uses_inherited_data_context")]),
        new("Static and dynamic resources", Supported, [Evidence("tests/Lucent.Compiler.Tests/NativeCompatibilityMatrixTests.cs", "NativeCompatibilityMatrixTests", "Lucent_css_resource_compiles_and_tracks_native_resource_changes"), Evidence("tests/Lucent.Compiler.Tests/NativeCompatibilityMatrixTests.cs", "NativeCompatibilityMatrixTests", "Explicit_csharp_static_resource_uses_resource_dictionary")]),
        new("Native child/content metadata and ItemTemplate", Bounded, [Evidence("tests/Lucent.Compiler.Tests/GeneralCompilerTests.cs", "GeneralCompilerTests", "Typed_item_template_lowers_native_fragment_and_item_scope"), Evidence("tests/Lucent.Compiler.Tests/GeneralCompilerTests.cs", "GeneralCompilerTests", "Item_template_requires_exactly_one_native_root")]),
        new("Referenced custom and third-party controls", Supported, [Evidence("tests/Lucent.Compiler.Tests/NativeCompatibilityMatrixTests.cs", "NativeCompatibilityMatrixTests", "Referenced_assembly_control_resolves_property_and_event_metadata")]),
        new("Exact root mounting, fragments, lifetime, and automation", Supported, [Evidence("tests/Lucent.Compiler.Tests/NativeCompatibilityMatrixTests.cs", "NativeCompatibilityMatrixTests", "Generated_clr_event_invokes_and_unsubscribes_on_dispose"), Evidence("tests/Lucent.Compiler.Tests/GeneralCompilerTests.cs", "GeneralCompilerTests", "Indirect_and_structural_roots_do_not_emit_approximate_mount_root"), Evidence("tests/Lucent.Workbench.Tests/AccessibilityTests.cs", "AccessibilityTests", "Shown_shell_palette_and_settings_have_exact_accessibility_contract")]),
        new("TemplateContent and IDeferredContent", Bounded, [Evidence("tests/Lucent.Compiler.Tests/GeneralCompilerTests.cs", "GeneralCompilerTests", "Template_content_emits_fresh_public_deferred_content_without_component_capture"), Evidence("tests/Lucent.Compiler.Tests/NativeCompatibilityMatrixTests.cs", "NativeCompatibilityMatrixTests", "Explicit_csharp_template_escape_builds_richer_content")]),
        new("Adjacent, global, and theme precedence/value restoration", Supported, [Evidence("tests/Lucent.Compiler.Tests/NativeCompatibilityMatrixTests.cs", "NativeCompatibilityMatrixTests", "Native_style_order_restores_previous_and_local_values"), Evidence("tests/Lucent.Compiler.Tests/NativeCompatibilityMatrixTests.cs", "NativeCompatibilityMatrixTests", "Global_catalog_order_is_host_controlled_and_adjacent_wins")]),
        new("Optional finite utility catalog", Supported, [Evidence("tests/Lucent.Compiler.Tests/NativeCompatibilityMatrixTests.cs", "NativeCompatibilityMatrixTests", "Utility_catalog_uses_canonical_order_not_class_token_order"), Evidence("tests/Lucent.Compiler.Tests/NativeCompatibilityMatrixTests.cs", "NativeCompatibilityMatrixTests", "Utility_catalog_host_order_is_observable_and_local_values_win"), Evidence("tests/Lucent.Compiler.Tests/GeneralCompilerTests.cs", "GeneralCompilerTests", "Escaped_state_class_selectors_preserve_decoded_names_and_exact_source_spans")]),
    ];

    [TestMethod]
    public void Native_compatibility_matrix_matches_the_published_table_and_test_methods()
    {
        var documentation = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "docs", "NATIVE_COMPATIBILITY.md"));

        Assert.HasCount(10, Rows);
        foreach (var row in Rows)
        {
            CollectionAssert.Contains(new[] { Supported, Bounded, Escape }, row.Status);
            StringAssert.Contains(documentation, $"| {row.Seam} | {row.Status} | {row.DocumentationEvidence} |");
            foreach (var evidence in row.Evidence) AssertTestMethod(evidence);
        }
    }

    [TestMethod]
    public void Generated_clr_event_invokes_and_unsubscribes_on_dispose()
    {
        const string fixture = """
            namespace Fixture;
            public sealed class ClrEventControl : Avalonia.Controls.Control
            {
                private event System.EventHandler? _raised;
                public int SubscriptionCount { get; private set; }
                public event System.EventHandler? Raised { add { _raised += value; SubscriptionCount++; } remove { _raised -= value; SubscriptionCount--; } }
                public void Raise() => _raised?.Invoke(this, System.EventArgs.Empty);
            }
            public static class EventProbe { public static int Count; }
            """;
        var assembly = CompileGenerated("namespace Demo; using Fixture; component App() => ClrEventControl { Raised: (sender, e) => EventProbe.Count++; };", fixture);
        var componentType = assembly.GetType("Demo.AppComponent")!;
        var constructor = componentType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().All(parameter => parameter.IsOptional));
        using var component = (IDisposable)constructor.Invoke(constructor.GetParameters().Select(_ => Type.Missing).ToArray())!;
        var control = (Control)componentType.GetMethod("MountRoot")!.Invoke(component, null)!;
        var raise = control.GetType().GetMethod("Raise")!;
        var subscriptions = control.GetType().GetProperty("SubscriptionCount")!;

        raise.Invoke(control, null);
        Assert.AreEqual(1, (int)assembly.GetType("Fixture.EventProbe")!.GetField("Count")!.GetValue(null)!);
        Assert.AreEqual(1, (int)subscriptions.GetValue(control)!);
        var remount = Assert.Throws<TargetInvocationException>(() => componentType.GetMethod("MountRoot")!.Invoke(component, null));
        Assert.IsInstanceOfType<InvalidOperationException>(remount.InnerException);

        component.Dispose();
        Assert.AreEqual(0, (int)subscriptions.GetValue(control)!);
        raise.Invoke(control, null);
        Assert.AreEqual(1, (int)assembly.GetType("Fixture.EventProbe")!.GetField("Count")!.GetValue(null)!);
    }

    [TestMethod]
    public void Referenced_assembly_control_resolves_property_and_event_metadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-reference-control-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var reference = Path.Combine(directory, "Fixture.Controls.dll");
            EmitReferenceAssembly(reference, """
                namespace Fixture;
                public sealed class ReferencedControl : Avalonia.Controls.Control
                {
                    public string? Accent { get; set; }
                    public event System.EventHandler? Activated;
                }
                public static class Probe { public static int Count; }
                """);
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Append(reference).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var result = LucentCompiler.Compile(
                "namespace Demo; using Fixture; component App() => ReferencedControl { Accent: \"blue\"; Activated: (sender, e) => Probe.Count++; };",
                "App.lui", new LucentProjectContext(ReferencePaths: references));

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
            StringAssert.Contains(result.GeneratedSource!, "new global::Fixture.ReferencedControl()");
            StringAssert.Contains(result.GeneratedSource!, ".Accent = \"blue\";");
            StringAssert.Contains(result.GeneratedSource!, ".Activated += __lucent_OnControl1Activated;");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Explicit_csharp_template_escape_builds_richer_content()
    {
        var host = new TemplateHost { Content = new RichContent() };
        var first = (StackPanel)host.Content!.Build(null)!;
        var second = (StackPanel)host.Content.Build(null)!;

        Assert.AreNotSame(first, second);
        Assert.HasCount(2, first.Children);
        Assert.AreEqual("first", ((TextBlock)first.Children[0]).Text);
        Assert.AreEqual("second", ((TextBlock)first.Children[1]).Text);
    }

    [TestMethod]
    public void Lucent_css_resource_compiles_and_tracks_native_resource_changes()
    {
        HeadlessTestHarness.Run(() =>
        {
        var result = LucentCompiler.Compile(
            "namespace Demo; using Avalonia.Controls; component App() => Border { Class: \"surface\"; };",
            "App.lui", projectContext: null, ".surface { background: resource(\"Lucent.Canvas\"); }", "App.css");
        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource!, "DynamicResourceExtension(\"Lucent.Canvas\")");
        var assembly = EmitAssembly(result.GeneratedSource!);
        var componentType = assembly.GetType("Demo.AppComponent")!;
        using var component = CreateComponent(componentType);
        var border = (Border)componentType.GetMethod("MountRoot")!.Invoke(component, null)!;
        var window = new Window { Width = 100, Height = 100 };
        window.Resources["Lucent.Canvas"] = new SolidColorBrush(Colors.Red);
        window.Content = border;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.AreEqual(Colors.Red, ((SolidColorBrush)border.Background!).Color);

        window.Resources["Lucent.Canvas"] = new SolidColorBrush(Colors.Blue);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.AreEqual(Colors.Blue, ((SolidColorBrush)border.Background!).Color);
        window.Close();
        });
    }

    [TestMethod]
    public void Explicit_csharp_static_resource_uses_resource_dictionary()
    {
        var resources = new ResourceDictionary { ["Accent"] = new SolidColorBrush(Colors.Teal) };
        var border = new Border { Background = (IBrush)resources["Accent"]! };

        Assert.AreEqual(Colors.Teal, ((SolidColorBrush)border.Background!).Color);
    }

    [TestMethod]
    public void Native_style_order_restores_previous_and_local_values()
    {
        HeadlessTestHarness.Run(() =>
        {
        var window = new Window { Width = 100, Height = 100 };
        var border = new Border();
        var first = new Style(selector => selector.OfType<Border>())
        {
            Setters = { new Setter(Border.BackgroundProperty, new SolidColorBrush(Colors.Red)) },
        };
        var second = new Style(selector => selector.OfType<Border>())
        {
            Setters = { new Setter(Border.BackgroundProperty, new SolidColorBrush(Colors.Blue)) },
        };
        window.Content = border;
        window.Styles.Add(first);
        window.Styles.Add(second);
        window.Show();
        window.UpdateLayout();
        Assert.AreEqual(Colors.Blue, ((SolidColorBrush)border.Background!).Color);

        window.Styles.Remove(second);
        window.UpdateLayout();
        Assert.AreEqual(Colors.Red, ((SolidColorBrush)border.Background!).Color);

        border.Background = new SolidColorBrush(Colors.Green);
        window.Styles.Remove(first);
        window.UpdateLayout();
        Assert.AreEqual(Colors.Green, ((SolidColorBrush)border.Background!).Color);
        window.Close();
        });
    }

    [TestMethod]
    public void Global_catalog_order_is_host_controlled_and_adjacent_wins()
    {
        HeadlessTestHarness.Run(() =>
        {
        var app = Application.Current!;
        app.Styles.Clear();
        var first = new global::Avalonia.Styling.Styles { new Style(x => x.OfType<Border>()) { Setters = { new Setter(Border.BackgroundProperty, new SolidColorBrush(Colors.Red)) } } };
        var second = new global::Avalonia.Styling.Styles { new Style(x => x.OfType<Border>()) { Setters = { new Setter(Border.BackgroundProperty, new SolidColorBrush(Colors.Blue)) } } };
        var window = new Window { Width = 100, Height = 100, Content = new Border() };
        app.Styles.Add(first); app.Styles.Add(second); window.Show(); window.UpdateLayout();
        Assert.AreEqual(Colors.Blue, ((SolidColorBrush)((Border)window.Content!).Background!).Color);
        app.Styles.Remove(second); app.Styles.Insert(0, second); window.UpdateLayout();
        Assert.AreEqual(Colors.Red, ((SolidColorBrush)((Border)window.Content!).Background!).Color);
        ((Border)window.Content!).Styles.Add(new Style(x => x.OfType<Border>()) { Setters = { new Setter(Border.BackgroundProperty, new SolidColorBrush(Colors.Green)) } });
        window.UpdateLayout();
        Assert.AreEqual(Colors.Green, ((SolidColorBrush)((Border)window.Content!).Background!).Color);
        window.Close(); app.Styles.Clear();
        });
    }

    [TestMethod]
    public void Utility_catalog_uses_canonical_order_not_class_token_order()
    {
        HeadlessTestHarness.Run(() =>
        {
        var app = Application.Current!;
        app.Styles.Clear();
        app.Styles.Add(new global::Lucent.Themes.Shadcn.ShadcnTheme());
        app.Styles.Add(new global::Lucent.Styles.Utilities.LucentStyles());
        var first = new Border { Classes = { "p-2", "p-4", "bg-primary", "bg-muted" } };
        var second = new Border { Classes = { "bg-muted", "bg-primary", "p-4", "p-2" } };
        var firstSize = new Control { Classes = { "w-24", "w-48" } };
        var secondSize = new Control { Classes = { "w-48", "w-24" } };
        var window = new Window { Content = new StackPanel { Children = { first, second, firstSize, secondSize } } };
        window.Show(); window.UpdateLayout();
        Assert.AreEqual(new Thickness(16), first.Padding);
        Assert.AreEqual(new Thickness(16), second.Padding);
        Assert.AreEqual(192d, firstSize.Width); Assert.AreEqual(192d, secondSize.Width);
        var muted = ((SolidColorBrush)first.Background!).Color;
        Assert.AreEqual(muted, ((SolidColorBrush)first.Background!).Color);
        Assert.AreEqual(muted, ((SolidColorBrush)second.Background!).Color);
        foreach (var border in new[] { first, second })
        {
            border.Classes.Remove("p-4"); border.Classes.Remove("bg-muted");
        }
        firstSize.Classes.Remove("w-48"); secondSize.Classes.Remove("w-48");
        window.UpdateLayout();
        var primary = ((SolidColorBrush)first.Background!).Color;
        Assert.AreEqual(new Thickness(8), first.Padding); Assert.AreEqual(new Thickness(8), second.Padding);
        Assert.AreEqual(96d, firstSize.Width); Assert.AreEqual(96d, secondSize.Width);
        Assert.AreEqual(primary, ((SolidColorBrush)first.Background!).Color);
        Assert.AreEqual(primary, ((SolidColorBrush)second.Background!).Color);
        Assert.AreNotEqual(muted, primary);
        window.Close(); app.Styles.Clear();
        });
    }

    [TestMethod]
    public void Utility_states_restore_the_lower_value_and_preserve_non_color_state_evidence()
    {
        HeadlessTestHarness.Run(() =>
        {
        var app = Application.Current!;
        app.Styles.Clear();
        var lower = new global::Avalonia.Styling.Styles
        {
            new Style(x => x.OfType<Button>()) { Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Colors.Red)),
                new Setter(TemplatedControl.BorderBrushProperty, new SolidColorBrush(Colors.Red)),
            } },
            new Style(x => x.OfType<TextBox>()) { Setters = { new Setter(TemplatedControl.BorderBrushProperty, new SolidColorBrush(Colors.Red)) } },
            new Style(x => x.OfType<ToggleButton>()) { Setters = { new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Colors.Red)) } },
            new Style(x => x.OfType<ListBoxItem>()) { Setters = { new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Colors.Red)) } },
        };
        app.Styles.Add(new global::Lucent.Themes.Shadcn.ShadcnTheme());
        app.Styles.Add(lower);
        app.Styles.Add(new global::Lucent.Styles.Utilities.LucentStyles());
        var hover = new Button { Classes = { "hover:bg-primary", "focus-visible:border-ring", "disabled:opacity-50" } };
        var focus = new TextBox { Classes = { "focus:border-ring" } };
        var checkedControl = new ToggleButton { Classes = { "checked:bg-primary" } };
        var selected = new ListBoxItem { Classes = { "selected:bg-muted" } };
        var window = new Window { Content = new StackPanel { Children = { hover, focus, checkedControl, selected } } };
        window.Show(); window.UpdateLayout();

        var hoverStates = (IPseudoClasses)hover.Classes;
        hoverStates.Add(":pointerover"); window.UpdateLayout();
        Assert.AreNotEqual(Colors.Red, ((SolidColorBrush)hover.Background!).Color);
        hoverStates.Remove(":pointerover"); window.UpdateLayout();
        Assert.AreEqual(Colors.Red, ((SolidColorBrush)hover.Background!).Color);

        var focusStates = (IPseudoClasses)focus.Classes;
        focusStates.Add(":focus"); window.UpdateLayout();
        Assert.AreNotEqual(Colors.Red, ((SolidColorBrush)focus.BorderBrush!).Color);
        focusStates.Remove(":focus"); window.UpdateLayout();
        Assert.AreEqual(Colors.Red, ((SolidColorBrush)focus.BorderBrush!).Color);

        ((IPseudoClasses)hover.Classes).Add(":focus-visible"); window.UpdateLayout();
        Assert.AreNotEqual(Colors.Red, ((SolidColorBrush)hover.BorderBrush!).Color);
        ((IPseudoClasses)hover.Classes).Remove(":focus-visible"); window.UpdateLayout();
        Assert.AreEqual(Colors.Red, ((SolidColorBrush)hover.BorderBrush!).Color);
        hover.IsEnabled = false; window.UpdateLayout();
        Assert.AreEqual(.5d, hover.Opacity);
        hover.IsEnabled = true; window.UpdateLayout();
        Assert.AreEqual(1d, hover.Opacity);

        var checkedStates = (IPseudoClasses)checkedControl.Classes;
        checkedStates.Add(":checked"); window.UpdateLayout();
        Assert.AreNotEqual(Colors.Red, ((SolidColorBrush)checkedControl.Background!).Color);
        checkedStates.Remove(":checked"); window.UpdateLayout();
        Assert.AreEqual(Colors.Red, ((SolidColorBrush)checkedControl.Background!).Color);

        var selectedStates = (IPseudoClasses)selected.Classes;
        selectedStates.Add(":selected"); window.UpdateLayout();
        Assert.AreNotEqual(Colors.Red, ((SolidColorBrush)selected.Background!).Color);
        selectedStates.Remove(":selected"); window.UpdateLayout();
        Assert.AreEqual(Colors.Red, ((SolidColorBrush)selected.Background!).Color);
        window.Close(); app.Styles.Clear();
        });
    }

    [TestMethod]
    public void Utility_catalog_host_order_is_observable_and_local_values_win()
    {
        HeadlessTestHarness.Run(() =>
        {
        var app = Application.Current!;
        var host = new global::Avalonia.Styling.Styles { new Style(x => x.OfType<Border>().Class("border")) { Setters = { new Setter(Border.BorderThicknessProperty, new Thickness(3)) } } };
        foreach (var utilityLast in new[] { false, true })
        {
            app.Styles.Clear();
            var utilities = new global::Lucent.Styles.Utilities.LucentStyles();
            if (utilityLast) { app.Styles.Add(host); app.Styles.Add(utilities); }
            else { app.Styles.Add(utilities); app.Styles.Add(host); }
            var border = new Border { Classes = { "border" } };
            var window = new Window { Content = border };
            window.Show(); window.UpdateLayout();
            Assert.AreEqual(new Thickness(utilityLast ? 1 : 3), border.BorderThickness, "Equally specific catalogs follow their host installation order.");
            border.BorderThickness = new Thickness(5); window.UpdateLayout();
            Assert.AreEqual(new Thickness(5), border.BorderThickness);
            window.Close();
        }
        app.Styles.Clear();
        });
    }

    private static EvidenceReference Evidence(string path, string type, string method) => new(path, type, method);

    private static void AssertTestMethod(EvidenceReference evidence)
    {
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(RepositoryPaths.Root, evidence.Path)));
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().SingleOrDefault(candidate =>
            candidate.Identifier.ValueText == evidence.Method &&
            candidate.Ancestors().OfType<ClassDeclarationSyntax>().Any(type => type.Identifier.ValueText == evidence.Type));
        Assert.IsNotNull(method, evidence.Label);
        Assert.IsTrue(method.AttributeLists.SelectMany(list => list.Attributes)
            .Any(attribute => attribute.Name.ToString() is "TestMethod" or "TestMethodAttribute"), evidence.Label);
    }

    private static Assembly CompileGenerated(string lucent, string fixture)
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-native-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var fixturePath = Path.Combine(directory, "Fixture.cs");
            File.WriteAllText(fixturePath, fixture);
            var result = LucentCompiler.Compile(lucent, "App.lui", new LucentProjectContext(SourcePaths: [fixturePath]));
            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
            return EmitAssembly(fixture, result.GeneratedSource!);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static IDisposable CreateComponent(Type componentType)
    {
        var constructor = componentType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().All(parameter => parameter.IsOptional));
        return (IDisposable)constructor.Invoke(constructor.GetParameters().Select(_ => Type.Missing).ToArray())!;
    }

    private static void EmitReferenceAssembly(string path, string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(Control).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("FixtureControls", [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var file = File.Create(path);
        var emitted = compilation.Emit(file);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
    }

    private static Assembly EmitAssembly(params string[] sources)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(Control).Assembly.Location).Append(typeof(ComponentOwner).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("Fixture" + Guid.NewGuid().ToString("N"),
            sources.Select(source => CSharpSyntaxTree.ParseText(source)), references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var inMemory = compilation.Emit(bytes);
        Assert.IsTrue(inMemory.Success, string.Join(Environment.NewLine, inMemory.Diagnostics));
        return Assembly.Load(bytes.ToArray());
    }

    private sealed record CompatibilityRow(string Seam, string Status, EvidenceReference[] Evidence, string Note = "")
    {
        public string DocumentationEvidence => string.Join("; ", Evidence.Select(item => item.Label)) + Note;
    }

    private sealed record EvidenceReference(string Path, string Type, string Method)
    {
        public string Label => Type + "." + Method;
    }

    private sealed class TemplateHost : Control
    {
        [TemplateContent]
        public IDeferredContent? Content { get; set; }
    }

    private sealed class RichContent : IDeferredContent
    {
        public object? Build(IServiceProvider? serviceProvider) => new StackPanel
        {
            Children = { new TextBlock { Text = "first" }, new TextBlock { Text = "second" } },
        };
    }

}
