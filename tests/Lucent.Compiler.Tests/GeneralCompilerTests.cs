using Lucent.Compiler.Syntax;

namespace Lucent.Compiler.Tests;

[TestClass]
public sealed class GeneralCompilerTests
{
    [TestMethod]
    public void Direct_native_roots_emit_typed_mount_root_and_generated_code_attribute()
    {
        var result = LucentCompiler.Compile(
            "namespace Demo; using Avalonia.Controls; component App() => Window { Title: \"App\"; };",
            "App.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource!,
            "[global::System.CodeDom.Compiler.GeneratedCode(\"Lucent.Compiler\", \"1.0\")]");
        StringAssert.Contains(result.GeneratedSource!,
            "public global::Avalonia.Controls.Window MountRoot()");
        StringAssert.Contains(result.GeneratedSource!, "var __lucent_roots = Mount();");

        var control = LucentCompiler.Compile(
            "namespace Demo; using Avalonia.Controls; component ControlApp() => Border {};",
            "ControlApp.lui");
        Assert.IsTrue(control.Succeeded, string.Join(Environment.NewLine, control.Diagnostics));
        StringAssert.Contains(control.GeneratedSource!,
            "public global::Avalonia.Controls.Border MountRoot()");
    }

    [TestMethod]
    public void Indirect_and_structural_roots_do_not_emit_approximate_mount_root()
    {
        var indirect = LucentCompiler.CompileProject([
            new LucentSourceInput("Child.lui", "namespace Demo; using Avalonia.Controls; component Child() => Border {};"),
            new LucentSourceInput("App.lui", "namespace Demo; using Avalonia.Controls; component App() => Child {};"),
        ]);
        var indirectSource = indirect.Sources.Single(item => item.SourcePath == "App.lui").Result.GeneratedSource!;
        Assert.IsFalse(indirectSource.Contains("MountRoot()", StringComparison.Ordinal));

        var many = LucentCompiler.Compile(
            "namespace Demo; using Avalonia.Controls; component App() => Fragment { Border {} Border {} };",
            "App.lui");
        Assert.IsTrue(many.Succeeded, string.Join(Environment.NewLine, many.Diagnostics));
        Assert.IsFalse(many.GeneratedSource!.Contains("MountRoot()", StringComparison.Ordinal));

        var empty = LucentCompiler.Compile(
            "namespace Demo; component App() => Fragment {};",
            "App.lui");
        Assert.IsTrue(empty.Succeeded, string.Join(Environment.NewLine, empty.Diagnostics));
        Assert.IsFalse(empty.GeneratedSource!.Contains("MountRoot()", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Generated_semantic_stubs_keep_each_sources_imports_isolated()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-stub-imports", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var types = Path.Combine(directory, "Inputs.cs");
            File.WriteAllText(types,
                "namespace OneTypes { public sealed class Input { } } " +
                "namespace TwoTypes { public sealed class Input { } }");
            var result = LucentCompiler.CompileProject([
                new LucentSourceInput("A.lui", """
                    namespace One;
                    using OneTypes;
                    component A(Input input)
                    {
                        private Two.BComponent child = null!;
                        Fragment Render() => Border {};
                    }
                    """),
                new LucentSourceInput("B.lui", """
                    namespace Two;
                    using TwoTypes;
                    component B(Input input) => Window {};
                    """),
            ], new LucentProjectContext(SourcePaths: [types]));

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine,
                result.Sources.SelectMany(source => source.Result.Diagnostics)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
    [TestMethod]
    public void Native_controls_properties_content_and_events_are_lowered_directly()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Todo()
            {
                private readonly State<int> clicks = new(0);
                Fragment Render()
                {
                    return Border {
                        Padding: new Avalonia.Thickness(8);
                        StackPanel {
                            Spacing: 6;
                            TextBlock { Text: $"Clicks: {clicks.Value}"; }
                            Button {
                                "Add";
                                Click: (sender, e) => clicks.Update(clicks.Value + 1);
                            }
                        }
                    };
                }
            }
            """,
            "native-todo.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.IsNotNull(result.GeneratedSource);
        StringAssert.Contains(
            result.GeneratedSource,
            "private global::Avalonia.Controls.Border? __lucent_control1;");
        StringAssert.Contains(
            result.GeneratedSource,
            "private global::Avalonia.Controls.StackPanel? __lucent_control2;");
        StringAssert.Contains(result.GeneratedSource, ".Padding = new Avalonia.Thickness(8);");
        StringAssert.Contains(
            result.GeneratedSource,
            ".Spacing = 6;");
        StringAssert.Contains(result.GeneratedSource, ".Content = \"Add\";");
        StringAssert.Contains(result.GeneratedSource, "__lucent_control1!.Child = __lucent_control2!;");
        StringAssert.Contains(result.GeneratedSource, ".Click += __lucent_OnControl4Click;");
    }

    [TestMethod]
    public void Scalar_native_content_conflicts_with_a_nested_child_in_either_order()
    {
        foreach (var body in new[]
        {
            "\"Add\"; TextBlock { Text: \"child\"; }",
            "TextBlock { Text: \"child\"; } Content: \"Add\";",
        })
        {
            var result = LucentCompiler.Compile(
                $$"""
                namespace Demo;
                component Main()
                {
                    Fragment Render()
                    {
                        return Button { {{body}} };
                    }
                }
                """,
                "content-conflict.lui");

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == "LUC2001" &&
                diagnostic.Message.Contains("child", StringComparison.OrdinalIgnoreCase)));
        }
    }

    [TestMethod]
    public void Text_controls_reject_nested_control_content()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                Fragment Render()
                {
                    return TextBlock { Button { "No"; } };
                }
            }
            """,
            "text-child.lui");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(
            result.Diagnostics.Single(diagnostic => diagnostic.Code == "LUC2001").Message,
            "does not accept a native Control");
    }

    [TestMethod]
    public void Nested_controls_and_expression_event_are_lowered()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;

            component Main()
            {
                private readonly State<int> count = new(1);

                Fragment Render()
                {
                    return Column {
                        Class: "root";
                        Text {
                            text: $"Count: {count.Value}";
                        }
                        Column {
                            Text {
                                text: "Nested";
                            }
                            Button {
                                text: "Increment";
                                onClick: () => count.Update(count.Value + 1);
                            }
                        }
                    };
                }
            }
            """,
            "nested.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.IsNotNull(result.GeneratedSource);
        Assert.HasCount(2, result.Syntax!.Component.RenderMethod.Root.Children);
        StringAssert.Contains(result.GeneratedSource, "__lucent_control5");
        StringAssert.Contains(result.GeneratedSource, "__lucent_OnControl5Click");
        StringAssert.Contains(result.GeneratedSource, "#line");
        StringAssert.Contains(result.GeneratedSource, "__lucent_SetCount(__lucent_stateCount + 1);");
    }

    [TestMethod]
    public void Roslyn_expression_diagnostic_uses_absolute_lui_span()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                Fragment Render()
                {
                    return Text {
                        text: Missing(;
                    };
                }
            }
            """,
            "invalid-expression.lui");

        var diagnostic = result.Diagnostics.First(candidate =>
            candidate.Code == "LUC3001");
        Assert.AreEqual(7, diagnostic.Line);
        Assert.IsTrue(diagnostic.Column > 15);
        Assert.IsTrue(diagnostic.Span.Start > 0);
    }

    [TestMethod]
    public void Multiple_components_are_diagnosed_before_emission()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component First()
            {
                Fragment Render()
                {
                    return Text { text: "first"; };
                }
            }
            component Second()
            {
                Fragment Render()
                {
                    return Button { text: "second"; };
                }
            }
            """,
            "multiple.lui");

        Assert.IsFalse(result.Succeeded);
        Assert.HasCount(2, result.Syntax!.AllComponents);
        Assert.AreEqual("First", result.Syntax.Component.Name);
        StringAssert.Contains(
            result.Diagnostics.Single(diagnostic => diagnostic.Code == "LUC2001").Message,
            "additional components");
    }

    [TestMethod]
    public void Raw_and_interpolated_verbatim_strings_do_not_terminate_islands()
    {
        var source = string.Join(
            Environment.NewLine,
            "namespace Demo;",
            "component Main()",
            "{",
            "    Fragment Render()",
            "    {",
            "        return Column {",
            "            Text { text: \"\"\"raw; { braces }\"\"\"; }",
            "            Text { text: $@\"verbatim; {1}\"; }",
            "            Text { text: @$\"also verbatim; {2}\"; }",
            "        };",
            "    }",
            "}");
        var result = LucentCompiler.Compile(
            source,
            "strings.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.HasCount(3, result.Syntax!.Component.RenderMethod.Root.Children);
        Assert.IsFalse(result.Diagnostics.Any(diagnostic => diagnostic.Code == "LUC1001"));
    }

    [TestMethod]
    public void Statement_block_diagnostic_maps_past_synthetic_open_brace()
    {
        var source = """
            namespace Demo;
            component Main()
            {
                Fragment Render()
                {
                    return Button {
                        onClick: {
                            count.Update(;
                        }
                    };
                }
            }
            """;

        var result = LucentCompiler.Compile(source, "invalid-event.lui");
        var diagnostic = result.Diagnostics.First(candidate =>
            candidate.Code == "LUC3001");
        var bodyStart = source.IndexOf("count.Update", StringComparison.Ordinal);

        Assert.AreEqual(bodyStart + 13, diagnostic.Span.Start);
        Assert.AreEqual(8, diagnostic.Line);
    }

    [TestMethod]
    public void Native_panel_properties_and_children_are_lowered_directly()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                Fragment Render()
                {
                    return StackPanel {
                        Orientation: Orientation.Horizontal;
                        Spacing: 8;
                        TextBlock { Text: "Todo"; }
                    };
                }
            }
            """,
            "native-panel.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(
            result.GeneratedSource,
            "private global::Avalonia.Controls.StackPanel? __lucent_control1;");
        StringAssert.Contains(result.GeneratedSource, "__lucent_control1!.Orientation = Orientation.Horizontal;");
        StringAssert.Contains(
            result.GeneratedSource,
            "__lucent_control1!.Spacing = 8;");
        StringAssert.Contains(result.GeneratedSource, "__lucent_control1!.Children.Add(__lucent_control2!);");
    }

    [TestMethod]
    public void Native_decorator_child_uses_the_direct_child_slot()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                Fragment Render()
                {
                    return Border {
                        Padding: 4;
                        TextBlock { Text: "Todo"; }
                    };
                }
            }
            """,
            "native-border.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(
            result.GeneratedSource,
            "private global::Avalonia.Controls.Border? __lucent_control1;");
        StringAssert.Contains(result.GeneratedSource, "__lucent_control1!.Child = __lucent_control2!;");
        Assert.IsFalse(result.GeneratedSource.Contains("AttachChild", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Native_button_accepts_implicit_scalar_content()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                Fragment Render()
                {
                    return Button { "Add task" };
                }
            }
            """,
            "native-button-content.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource, "__lucent_control1!.Content = \"Add task\";");
    }

    [TestMethod]
    public void Native_button_accepts_explicit_content_with_the_same_lowering()
    {
        var implicitResult = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                Fragment Render()
                {
                    return Button { "Add task" };
                }
            }
            """,
            "implicit-content.lui");
        var explicitResult = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                Fragment Render()
                {
                    return Button { Content: "Add task"; };
                }
            }
            """,
            "explicit-content.lui");

        Assert.IsTrue(implicitResult.Succeeded);
        Assert.IsTrue(explicitResult.Succeeded);
        StringAssert.Contains(implicitResult.GeneratedSource, "__lucent_control1!.Content = \"Add task\";");
        StringAssert.Contains(explicitResult.GeneratedSource, "__lucent_control1!.Content = \"Add task\";");
    }

    [TestMethod]
    public void Explicit_and_implicit_scalar_content_conflict_in_either_order()
    {
        foreach (var body in new[]
        {
            "\"Add\"; Content: \"Other\";",
            "Content: \"Other\"; \"Add\";",
        })
        {
            var result = LucentCompiler.Compile(
                $$"""
                namespace Demo;
                component Main()
                {
                    Fragment Render()
                    {
                        return Button { {{body}} };
                    }
                }
                """,
                "duplicate-content.lui");

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(
                result.Diagnostics.Single(diagnostic => diagnostic.Code == "LUC2001").Message,
                "only one");
        }
    }

    [TestMethod]
    public void Numeric_and_tuple_literals_are_adapted_from_the_target_property_type()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                Fragment Render()
                {
                    return Border {
                        Padding: (12, 8);
                        CornerRadius: 6;
                        TextBlock { FontSize: 24; }
                    };
                }
            }
            """,
            "typed-values.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(
            result.GeneratedSource,
            "__lucent_control1!.Padding = new global::Avalonia.Thickness(12, 8)");
        StringAssert.Contains(
            result.GeneratedSource,
            "__lucent_control1!.CornerRadius = new global::Avalonia.CornerRadius(6)");
        StringAssert.Contains(
            result.GeneratedSource,
            "__lucent_control2!.FontSize = 24;");
    }

    [TestMethod]
    public void General_state_and_keyed_foreach_emit_a_retained_native_region()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            using System.Linq;

            component Main()
            {
                private readonly State<string> title = new("Todos");
                private readonly State<int[]> items = new([1, 2, 3]);

                Fragment Render()
                {
                    return StackPanel {
                        foreach (var item in items.Value.Where(value => value > 1))
                        keyed by item {
                            Button {
                                Content: $"{title.Value}: {item}";
                                Click: (sender, e) => {
                                    items.Update(current => current.Append(item).ToArray());
                                };
                            }
                        }
                    };
                }
            }
            """,
            "keyed-region.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.HasCount(1, result.Syntax!.AllUsings);
        Assert.IsInstanceOfType<UiForEachSyntax>(
            result.Syntax.Component.RenderMethod.Root.Members.Single());
        StringAssert.Contains(result.GeneratedSource, "private string __lucent_stateTitle;");
        StringAssert.Contains(result.GeneratedSource, "__lucent_stateTitle = \"Todos\";");
        StringAssert.Contains(result.GeneratedSource, "private int[] __lucent_stateItems;");
        StringAssert.Contains(result.GeneratedSource, "__lucent_stateItems = [1, 2, 3];");
        StringAssert.Contains(
            result.GeneratedSource,
            "private readonly Dictionary<object, __lucent_ILoopEntry> __lucent_region1 = new();");
        StringAssert.Contains(
            result.GeneratedSource,
            "var sourceItems = (__lucent_stateItems.Where(value => value > 1)).ToArray();");
        StringAssert.Contains(result.GeneratedSource, "foreach (var item in sourceItems)");
        StringAssert.Contains(result.GeneratedSource, "entry.Update(item);");
        StringAssert.Contains(result.GeneratedSource, "foreach (var root in entry.Roots.Roots) nativeItems.Add(root);");
        StringAssert.Contains(
            result.GeneratedSource,
            "ReferenceEquals(nativeItems[index], nextEntries[index].Roots[0])");
        StringAssert.Contains(result.GeneratedSource, "if (orderChanged)");
        StringAssert.Contains(result.GeneratedSource, "var rowOwner = __lucent_owner.CreateChild();");
        StringAssert.Contains(result.GeneratedSource, "rowOwner.OnDispose(() =>");
        StringAssert.Contains(result.GeneratedSource, "return (Fragment.From(control1), (Action)Refresh, rowOwner.Dispose);");
        StringAssert.Contains(result.GeneratedSource, "__lucent_owner.OnDispose(__lucent_region1.Clear);");
        Assert.IsFalse(result.GeneratedSource.Contains("Dispatcher.UIThread", StringComparison.Ordinal));
        Assert.IsFalse(result.GeneratedSource.Contains("disposeActions", StringComparison.Ordinal));
        Assert.IsTrue(
            result.GeneratedSource.IndexOf(
                "duplicate key",
                StringComparison.Ordinal) <
            result.GeneratedSource.IndexOf(
                "__lucent_region1.TryGetValue",
                StringComparison.Ordinal),
            "All keys should be validated before the existing region is mutated.");
        StringAssert.Contains(
            result.GeneratedSource,
            "private void __lucent_SetItems(Func<int[], int[]> update)");
        StringAssert.Contains(Method(result.GeneratedSource!, "private void __lucent_InvalidateSource0()"), "__lucent_UpdateRegion1();");
        StringAssert.Contains(Method(result.GeneratedSource!, "private void __lucent_InvalidateSource1()"), "__lucent_UpdateRegion1();");
    }

    [TestMethod]
    public void Native_click_event_is_hooked_by_exact_event_name()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                private readonly State<int> count = new(0);

                Fragment Render()
                {
                    return Button {
                        Click: (sender, e) => count.Update(count.Value + 1);
                    };
                }
            }
            """,
            "native-click.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource, "__lucent_control1!.Click += __lucent_OnControl1Click;");
        StringAssert.Contains(result.GeneratedSource, "__lucent_SetCount(__lucent_stateCount + 1);");
        StringAssert.Contains(result.GeneratedSource, "#line");
    }

    [TestMethod]
    public void Explicit_event_lambda_preserves_parameters_and_narrows_sender()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                Fragment Render()
                {
                    return Button {
                        Click: (button, args) => Console.WriteLine(button);
                    };
                }
            }
            """,
            "event-parameters.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(
            result.GeneratedSource,
            "var button = (global::Avalonia.Controls.Button)__sender!;");
        StringAssert.Contains(result.GeneratedSource, "var args = __eventArgs;");
        StringAssert.Contains(result.GeneratedSource, "Console.WriteLine(button);");
    }

    [TestMethod]
    public void Resolved_native_events_use_deterministic_named_cleanup()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                Fragment Render()
                {
                    return Border {
                        Loaded: (sender, e) => Console.WriteLine(sender);
                    };
                }
            }
            """,
            "unsupported-event.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource, ".Loaded += __lucent_OnControl1Loaded;");
        StringAssert.Contains(
            result.GeneratedSource,
            "__lucent_owner.OnDispose(() => __lucent_control1!.Loaded -= __lucent_OnControl1Loaded);");
        StringAssert.Contains(result.GeneratedSource, "public void Dispose() => __lucent_owner.Dispose();");
    }

    [TestMethod]
    public void Generated_owner_guards_mount_and_state_commits()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                private readonly State<int> count = new(0);

                Fragment Render()
                {
                    return Button {
                        Click: (sender, e) => count.Update(count.Value + 1);
                    };
                }
            }
            """,
            "owner-contract.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(
            result.GeneratedSource,
            "ObjectDisposedException.ThrowIf(__lucent_owner.IsDisposed, this);");
        StringAssert.Contains(
            result.GeneratedSource,
            "A Main component can only be mounted once.");
        StringAssert.Contains(result.GeneratedSource, "public void Dispose() => __lucent_owner.Dispose();");
        StringAssert.Contains(
            result.GeneratedSource,
            "__lucent_owner.Dispatch(() => __lucent_SetCountCore(value));");
    }

    [TestMethod]
    public void State_named_owner_does_not_collide_with_generated_locals()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                private readonly State<int> owner = new(1);
                Fragment Render() => Button { Click: () => owner.Update(value => value + 1); };
            }
            """,
            "owner-state.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.IsFalse(result.GeneratedSource!.Contains("var _owner", StringComparison.Ordinal));
        StringAssert.Contains(result.GeneratedSource!, "var __lucent_stateOwner = __lucent_owner;");
        StringAssert.Contains(result.GeneratedSource!, "__lucent_previousOwner");
    }

    [TestMethod]
    public void Bare_event_blocks_and_one_parameter_lambdas_are_rejected()
    {
        foreach (var handler in new[]
        {
            "{ Console.WriteLine(\"loaded\"); }",
            "sender => Console.WriteLine(sender)",
        })
        {
            var result = LucentCompiler.Compile(
                $$"""
                namespace Demo;
                component Main()
                {
                    Fragment Render()
                    {
                        return Border { Loaded: {{handler}}; };
                    }
                }
                """,
                "explicit-events.lui");

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == "LUC2001"));
        }
    }

    [TestMethod]
    public void State_rewriting_ignores_strings_and_qualified_member_names()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                private readonly State<string> title = new("Todos");

                Fragment Render()
                {
                    return StackPanel {
                        TextBlock { Text: "title.Value"; }
                        TextBlock { Text: System.Environment.Version.ToString(); }
                        TextBlock { Text: title.Value; }
                    };
                }
            }
            """,
            "state-rewrite.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource, ".Text = \"title.Value\";");
        StringAssert.Contains(result.GeneratedSource, ".Text = System.Environment.Version.ToString();");
        StringAssert.Contains(result.GeneratedSource, ".Text = __lucent_stateTitle;");
    }

    [TestMethod]
    public void Editor_completion_uses_native_member_and_value_types()
    {
        const string source = """
            namespace Demo;
            component Main()
            {
                Fragment Render()
                {
                    return StackPanel {
                        Orientation: Orientation.Horizontal;
                        Button {
                        }
                    };
                }
            }
            """;

        var memberOffset = source.IndexOf("Button {", StringComparison.Ordinal) + "Button {".Length;
        var members = LucentCompiler.GetCompletions(source, memberOffset, "Completion.lui");
        Assert.IsTrue(members.Any(item =>
            item.Label == "Content" &&
            item.Kind == LucentCompletionItemKind.Property));
        Assert.IsTrue(members.Any(item =>
            item.Label == "Click" &&
            item.Kind == LucentCompletionItemKind.Event));
        Assert.IsTrue(members.Any(item => item.Label == "Class"));

        var valueOffset = source.IndexOf("Orientation.Horizontal", StringComparison.Ordinal);
        var values = LucentCompiler.GetCompletions(source, valueOffset, "Completion.lui");
        Assert.IsTrue(values.Any(item =>
            item.Label == "Orientation.Horizontal" &&
            item.InsertText.Contains("Avalonia.Layout.Orientation.Horizontal", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Editor_completion_tracks_event_parameters_and_local_variables()
    {
        const string source = """
            namespace Demo;
            component Main()
            {
                Fragment Render()
                {
                    return TextBox {
                        TextChanged: (sender, e) => {
                            var current = sender.Text;
                            current.Contains("x");
                        };
                    };
                }
            }
            """;

        var senderOffset = source.IndexOf("sender.Text", StringComparison.Ordinal) +
            "sender.T".Length;
        var senderMembers = LucentCompiler.GetCompletions(source, senderOffset, "Events.lui");
        Assert.IsTrue(senderMembers.Any(item => item.Label == "Text"));

        var localOffset = source.IndexOf("current.Contains", StringComparison.Ordinal) +
            "current.C".Length;
        var localMembers = LucentCompiler.GetCompletions(source, localOffset, "Events.lui");
        Assert.IsTrue(localMembers.Any(item => item.Label == "Contains"));

        var eventArgsOffset = source.IndexOf("e) =>", StringComparison.Ordinal);
        var eventArgsHover = LucentCompiler.GetExpressionSymbol(
            source,
            eventArgsOffset,
            "Events.lui");
        Assert.IsNotNull(eventArgsHover);
        StringAssert.Contains(eventArgsHover.Display, "TextChangedEventArgs e");
    }

    [TestMethod]
    public void Editor_completion_and_hover_include_ordinary_component_members()
    {
        const string source = """
            namespace Demo;
            using System.Threading;
            component Main()
            {
                private readonly CancellationTokenSource focusSidebar = new();
                private void FocusEditor() { }
                Fragment Render() => Border {
                    Loaded: (sender, e) => { focusSidebar.Cancel(); FocusEditor(); };
                };
            }
            """;

        var rootOffset = source.IndexOf("focusSidebar.Cancel", StringComparison.Ordinal) +
            "focusS".Length;
        Assert.IsTrue(LucentCompiler.GetCompletions(source, rootOffset, "Events.lui")
            .Any(item => item.Label == "focusSidebar" && item.Kind == LucentCompletionItemKind.Field));

        var memberOffset = source.IndexOf("focusSidebar.Cancel", StringComparison.Ordinal) +
            "focusSidebar.C".Length;
        var memberCompletions = LucentCompiler.GetCompletions(source, memberOffset, "Events.lui");
        if (!memberCompletions.Any(item => item.Label == "Cancel"))
            Assert.Fail(string.Join(", ", memberCompletions.Select(item => item.Label)));

        var useOffset = source.LastIndexOf("focusSidebar", StringComparison.Ordinal);
        var symbol = LucentCompiler.GetExpressionSymbol(source, useOffset, "Events.lui");
        Assert.IsNotNull(symbol);
        StringAssert.Contains(symbol.Display, "focusSidebar");
        Assert.AreEqual(source.IndexOf("focusSidebar", StringComparison.Ordinal),
            symbol.Definition?.Span.Start);

        var methodUse = source.LastIndexOf("FocusEditor", StringComparison.Ordinal);
        Assert.IsTrue(LucentCompiler.GetCompletions(source, methodUse + "FocusE".Length, "Events.lui")
            .Any(item => item.Label == "FocusEditor" && item.Kind == LucentCompletionItemKind.Method));
        var methodSymbol = LucentCompiler.GetExpressionSymbol(source, methodUse, "Events.lui");
        Assert.IsNotNull(methodSymbol);
        StringAssert.Contains(methodSymbol.Display, "FocusEditor");
        Assert.AreEqual(source.IndexOf("FocusEditor", StringComparison.Ordinal),
            methodSymbol.Definition?.Span.Start);
    }

    [TestMethod]
    public void Generated_event_handlers_report_authored_failures_through_the_owner()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                Fragment Render() => StackPanel {
                    Button { Click: () => throw new InvalidOperationException("event"); }
                    ContentControl { if (true) { Button { Click: () => throw new InvalidOperationException("conditional"); } } }
                };
            }
            """,
            "Events.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource!, "catch (global::System.Exception __lucent_error)");
        StringAssert.Contains(result.GeneratedSource!, "__lucent_owner.ReportUnhandled(__lucent_error)");
        StringAssert.Contains(result.GeneratedSource!, "branchOwner.ReportUnhandled(__lucent_error)");
    }

    [TestMethod]
    public void Computed_values_keep_stale_data_cancel_prior_work_and_ignore_late_results()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Search()
            {
                private readonly State<string> query = new("lucent");
                private readonly Computed<string[]> results = new(
                    cancellationToken => Task.FromResult(new[] { query.Value }),
                    []);

                Fragment Render()
                {
                    return StackPanel {
                        TextBlock { Text: results.IsPending ? "Updating" : results.ErrorMessage; }
                        StackPanel {
                            foreach (var result in results.Value) keyed by result {
                                TextBlock { Text: result; }
                            }
                        }
                    };
                }
            }
            """,
            "Search.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource, "private OwnedComputed<string[]> __lucent_computedResults");
        StringAssert.Contains(result.GeneratedSource, "new OwnedComputed<string[]>(__lucent_owner");
        StringAssert.Contains(result.GeneratedSource, "__lucent_computedResults.Refresh();");
        Assert.IsFalse(result.GeneratedSource.Contains("CancellationTokenSource", StringComparison.Ordinal));
        Assert.IsFalse(result.GeneratedSource.Contains("__lucent_computedResultsGeneration", StringComparison.Ordinal));
        Assert.IsFalse(result.GeneratedSource.Contains("__lucent_RunResultsAsync", StringComparison.Ordinal));
        Assert.IsFalse(result.GeneratedSource.Contains("Dispatcher.UIThread", StringComparison.Ordinal));
        StringAssert.Contains(result.GeneratedSource, "Task.FromResult(new[] { __lucent_stateQuery })");
        StringAssert.Contains(result.GeneratedSource, "var sourceItems = (__lucent_computedResults.Value).ToArray();");
    }

    [TestMethod]
    public void Invalid_css_reports_a_css_diagnostic_instead_of_crashing()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                Fragment Render() { return Border { Class: "card"; }; }
            }
            """,
            "Main.lui",
            projectContext: null,
            ".card { background: definitely-not-a-color; }",
            "Main.css");

        Assert.IsFalse(result.Succeeded);
        var diagnostic = result.Diagnostics.Single(candidate => candidate.Code == "LUC4001");
        Assert.AreEqual("Main.css", diagnostic.SourcePath);
        StringAssert.Contains(diagnostic.Message, "color");
    }

    [TestMethod]
    public void Invalid_css_values_and_transitions_report_declaration_spans()
    {
        var result = LucentCompiler.Compile(
            "namespace Demo; component Main() => Border { Class: \"card\"; };",
            "Main.lui",
            projectContext: null,
            "\n.card { opacity: NaN; transition: font-family 120ms; }",
            "Main.css");

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Diagnostics.Count(diagnostic => diagnostic.Code == "LUC4001") >= 2);
        Assert.IsTrue(result.Diagnostics.All(diagnostic => diagnostic.SourcePath == "Main.css"));
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Line == 2 && diagnostic.Column > 0));
    }

    [TestMethod]
    public void Css_subset_lowers_variables_pseudo_classes_and_native_transitions()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                Fragment Render()
                {
                    return StackPanel {
                        Class: "shell";
                        Button { Class: "primary"; Content: "Search"; }
                    };
                }
            }
            """,
            "Main.lui",
            projectContext: null,
            """
            :root { --accent: #7357e6; }
            .shell { gap: 8px; }
            .primary {
                background: var(--accent);
                opacity: 0.8;
                transition: opacity 150ms ease-out, background 150ms ease-out;
            }
            .primary:pointerover { opacity: 1; }
            """,
            "Main.css");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource, ".OfType<global::Avalonia.Controls.Button>().Class(\"primary\").Class(\":pointerover\")");
        StringAssert.Contains(result.GeneratedSource, "global::Avalonia.Controls.StackPanel.SpacingProperty");
        StringAssert.Contains(result.GeneratedSource, "global::Avalonia.Media.Color.FromRgb(0x73, 0x57, 0xe6)");
        StringAssert.Contains(result.GeneratedSource, "global::Avalonia.Animation.BrushTransition");
        StringAssert.Contains(result.GeneratedSource, "global::Avalonia.Animation.DoubleTransition");
    }

    [TestMethod]
    public void Css_catalog_lowers_names_combinators_resources_and_extended_values()
    {
        var result = LucentCompiler.Compile(
            "namespace Demo; using Avalonia.Controls; component Main() => Border { Name: \"Shell\"; Class: \"toolbar primary\"; Border { Name: \"SearchBox\"; Class: \"toolbar primary\"; } };",
            "Main.lui",
            projectContext: null,
            """
            :root { --canvas: resource("Lucent.Canvas"); }
            #Shell > #SearchBox.toolbar.primary:focus-visible {
                background: var(--canvas);
                border-color: #007a7b;
                border-width: 2px;
                border-radius: 4px;
                visibility: true;
                transition: background 120ms ease-out, border-radius 180ms ease-in-out;
            }
            #Shell .toolbar { opacity: 0.9; }
            """,
            "Main.css");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource!, ".Child().OfType<global::Avalonia.Controls.Border>()");
        StringAssert.Contains(result.GeneratedSource!, ".Descendant().OfType<global::Avalonia.Controls.Border>()");
        StringAssert.Contains(result.GeneratedSource!, ".Name(\"SearchBox\")");
        StringAssert.Contains(result.GeneratedSource!, "DynamicResourceExtension(\"Lucent.Canvas\")");
        StringAssert.Contains(result.GeneratedSource!, "CornerRadiusTransition");
    }

    [TestMethod]
    public void Css_resource_accepts_quoted_keys_without_preserving_the_quotes()
    {
        var result = LucentCompiler.Compile(
            "namespace Demo; component Main() => Border { Class: \"surface\"; };",
            "Main.lui",
            projectContext: null,
            ".surface { background: resource(\"Lucent.Canvas\"); }",
            "Main.css");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource!, "DynamicResourceExtension(\"Lucent.Canvas\")");
        Assert.IsFalse(result.GeneratedSource!.Contains("\\\"Lucent.Canvas\\\"",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void Css_untyped_ancestor_keeps_its_own_control_type()
    {
        var result = LucentCompiler.Compile(
            "namespace Demo; component Main() => Border { Class: \"todo-row completed\"; TextBox { Class: \"editor\"; } };",
            "Main.lui",
            projectContext: null,
            ".todo-row.completed TextBox { opacity: 0.5; }",
            "Main.css");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource!,
            "x.Class(\"todo-row\").Class(\"completed\").Descendant().OfType<global::Avalonia.Controls.TextBox>()");
        Assert.IsFalse(result.GeneratedSource!.Contains(
            "x.OfType<global::Avalonia.Controls.TextBox>().Class(\"todo-row\")",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void Css_runtime_class_and_name_selectors_are_not_dropped_when_statically_unmatched()
    {
        var result = LucentCompiler.Compile(
            "namespace Demo; component Main() => Border {};",
            "Main.lui",
            projectContext: null,
            ".future #Later { background: #112233; }",
            "Main.css");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource!, ".Class(\"future\").Descendant()");
        StringAssert.Contains(result.GeneratedSource!, ".Name(\"Later\")");
        StringAssert.Contains(result.GeneratedSource!, "global::Avalonia.Controls.Border.BackgroundProperty");
        Assert.IsFalse(result.GeneratedSource!.Contains(
            "global::Avalonia.Controls.Control.BackgroundProperty", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Css_selector_without_a_common_native_property_owner_is_diagnosed()
    {
        var result = LucentCompiler.Compile(
            "namespace Demo; component Main() => Border {};",
            "Main.lui",
            projectContext: null,
            ".future { gap: 8px; box-shadow: 0 2px 4px #112233; }",
            "Main.css");

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Code == "LUC4001" &&
            diagnostic.Message.Contains("no common native property owner", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Css_font_weight_medium_lowers_to_native_medium()
    {
        var result = LucentCompiler.Compile(
            "namespace Demo; component Main() => TextBlock { Class: \"copy\"; };",
            "Main.lui",
            projectContext: null,
            ".copy { font-weight: medium; }",
            "Main.css");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource!, "global::Avalonia.Media.FontWeight.Medium");
    }

    [TestMethod]
    public void Css_eight_digit_color_lowers_rgba_to_native_argb_order()
    {
        var result = LucentCompiler.Compile(
            "namespace Demo; component Main() => Border { Class: \"surface\"; };",
            "Main.lui",
            projectContext: null,
            ".surface { background: #11223344; }",
            "Main.css");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource!,
            "global::Avalonia.Media.Color.FromArgb(0x44, 0x11, 0x22, 0x33)");
    }

    [TestMethod]
    public void Css_completions_are_backed_by_the_typed_catalog()
    {
        var items = LucentCompiler.GetCompletions(".card { border-", 15, "Main.css");
        Assert.IsTrue(items.Any(item => item.Label == "border-color"));
        Assert.IsTrue(items.Any(item => item.Label == "border-radius"));
        Assert.IsTrue(items.All(item => item.Kind == LucentCompletionItemKind.Property));
    }

    [TestMethod]
    public void Foreach_header_scanning_ignores_parentheses_inside_strings()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                private readonly State<string[]> items = new(["a)"]);

                Fragment Render()
                {
                    return StackPanel {
                        foreach (var value in items.Value.Where(item => item.Contains(")")))
                        keyed by value {
                            TextBlock { Text: value; }
                        }
                    };
                }
            }
            """,
            "foreach-string-delimiter.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource, "Contains(\")\")");
        StringAssert.Contains(result.GeneratedSource, "loopValue =>");
    }

    [TestMethod]
    public void Async_boundary_uses_computed_error_as_a_transactional_branch()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                private readonly Computed<string> value = new(ct => Task.FromResult("ok"), "loading");
                Fragment Render() => Border {
                    try (value) {
                        ContentControl { Content: value.HasCommittedValue ? value.Value : "loading"; }
                    }
                    catch (Exception error) {
                        TextBlock { Text: error.Message; }
                    }
                };
            }
            """,
            "Main.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource!, "__lucent_computedValue.Error is null");
        StringAssert.Contains(result.GeneratedSource!, "__lucent_computedValue.Error.Message");
        Assert.IsFalse(result.GeneratedSource!.Contains("CancellationTokenSource", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Async_boundary_catch_local_is_available_only_in_fallback()
    {
        const string source = """
            namespace Demo;
            component Main()
            {
                private readonly Computed<string> value = new(ct => Task.FromResult("ok"), "loading");
                Fragment Render() => Border {
                    try (value) { TextBlock { Text: value.Value; } }
                    catch (Exception error) { TextBlock { Text: error.Message; } }
                };
            }
            """;
        var inside = source.IndexOf("error.Message", StringComparison.Ordinal);
        var symbol = LucentCompiler.GetExpressionSymbol(source, inside, "Boundary.lui");
        Assert.IsNotNull(symbol);
        StringAssert.Contains(symbol.Display, "Exception error");
        Assert.IsNotNull(symbol.Definition);
        Assert.IsFalse(LucentCompiler.GetCompletions(source, source.IndexOf("catch (Exception error", StringComparison.Ordinal), "Boundary.lui")
            .Any(item => item.Label == "error"));
    }

    [TestMethod]
    public void Computed_value_reads_outside_the_matching_boundary_are_rejected()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                private readonly Computed<string> value = new(ct => Task.FromResult("ok"), "loading");
                Fragment Render() => StackPanel {
                    TextBlock { Text: value.Value; }
                    ContentControl {
                        try (value) { TextBlock { Text: value.Value; } }
                        catch (Exception error) { TextBlock { Text: error.Message; } }
                    }
                };
            }
            """,
            "computed-boundary-guard.lui");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(string.Join(Environment.NewLine, result.Diagnostics),
            "must be read inside its try (value) content branch");
    }

    [TestMethod]
    public void Reactive_sources_invalidate_only_their_bound_targets_and_computed_factories()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            using System.Linq;
            component Main()
            {
                private readonly State<string> title = new("Title");
                private readonly State<int> count = new(1);
                private readonly Computed<int> doubled = new(ct => Task.FromResult(count.Value * 2), 0);
                Fragment Render()
                {
                    return StackPanel {
                        TextBlock { Text: $"{title.Value}: {new[] { count.Value }.Select(value => value).Single()}"; }
                        TextBlock { Text: doubled.IsPending ? doubled.ErrorMessage : doubled.Value.ToString(); }
                    };
                }
            }
            """,
            "dependencies.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var source = result.GeneratedSource!;
        var titleInvalidation = Method(source, "private void __lucent_InvalidateSource0()");
        var countInvalidation = Method(source, "private void __lucent_InvalidateSource1()");
        var computedInvalidation = Method(source, "private void __lucent_InvalidateSource2()");
        StringAssert.Contains(titleInvalidation, "__lucent_UpdateBinding1();");
        Assert.IsFalse(titleInvalidation.Contains("__lucent_RefreshDoubled", StringComparison.Ordinal));
        StringAssert.Contains(countInvalidation, "__lucent_UpdateBinding1();");
        StringAssert.Contains(countInvalidation, "__lucent_RefreshDoubled();");
        StringAssert.Contains(computedInvalidation, "__lucent_UpdateBinding2();");
        Assert.IsFalse(computedInvalidation.Contains("__lucent_UpdateBinding1", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Symbol_lowering_preserves_shadowing_and_treats_update_as_a_mutation()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                private readonly State<string> title = new("State");
                Fragment Render()
                {
                    return Button {
                        Content: new[] { "local" }.Select(title => title.ToUpperInvariant()).Single();
                        Click: (title, e) => this.title.Update(value => value + title.Content);
                    };
                }
            }
            """,
            "shadowing.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource!, "Select(title => title.ToUpperInvariant())");
        StringAssert.Contains(result.GeneratedSource!, "SetTitle(value => value + title.Content);");
        Assert.IsFalse(result.GeneratedSource!.Contains("_title.ToUpperInvariant", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Computed_self_and_two_node_cycles_report_each_back_edge_read()
    {
        var self = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                private readonly Computed<int> value = new(ct => Task.FromResult(value.Value + value.Value), 0);
                Fragment Render() { return TextBlock { Text: value.Value.ToString(); }; }
            }
            """,
            "self-cycle.lui");
        Assert.IsFalse(self.Succeeded);
        Assert.AreEqual(2, self.Diagnostics.Count(diagnostic =>
            diagnostic.Message.Contains("cycle", StringComparison.OrdinalIgnoreCase)));

        var pair = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                private readonly Computed<int> first = new(ct => Task.FromResult(second.Value), 0);
                private readonly Computed<int> second = new(ct => Task.FromResult(first.Value), 0);
                Fragment Render() { return TextBlock { Text: first.Value.ToString(); }; }
            }
            """,
            "pair-cycle.lui");
        Assert.IsFalse(pair.Succeeded);
        Assert.IsTrue(pair.Diagnostics.Any(diagnostic =>
            diagnostic.Message.Contains("cycle", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Conditional_regions_parse_bind_and_emit_owned_branches()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                private readonly State<bool> visible = new(true);
                private readonly State<string> title = new("Latest");
                Fragment Render()
                {
                    return StackPanel {
                        if (visible.Value) {
                            Border { TextBlock { Text: title.Value; } }
                        } else {
                            TextBlock { Text: "Hidden"; }
                        }
                    };
                }
            }
            """,
            "conditional.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.IsInstanceOfType<UiIfSyntax>(result.Syntax!.Component.RenderMethod.Root.Members.Single());
        var source = result.GeneratedSource!;
        StringAssert.Contains(source, "private ConditionalRegion? __lucent_conditional1;");
        StringAssert.Contains(source, "__lucent_conditional1!.Show(0, branchOwner =>");
        StringAssert.Contains(source, "__lucent_conditional1!.Show(1, branchOwner =>");
        StringAssert.Contains(source, "if (__lucent_conditional1!.ActiveBranch != 0)");
        StringAssert.Contains(source, "if (__lucent_conditional1!.ActiveBranch != 1)");
        StringAssert.Contains(Method(source, "private void __lucent_InvalidateSource0()"), "__lucent_UpdateConditional1();");
        StringAssert.Contains(Method(source, "private void __lucent_InvalidateSource1()"), "__lucent_UpdateBinding1();");
    }

    [TestMethod]
    public void Conditional_subset_rejects_mixed_nested_keyed_and_incompatible_hosts()
    {
        foreach (var root in new[]
        {
            "StackPanel { TextBlock { Text: \"ordinary\"; } if (true) { TextBlock { Text: \"branch\"; } } }",
            "StackPanel { if (true) { StackPanel { if (true) { TextBlock { Text: \"nested\"; } } } } }",
            "StackPanel { foreach (var item in new[] { 1 }) keyed by item { StackPanel { if (true) { TextBlock { Text: item.ToString(); } } } } }",
            "TextBlock { if (true) { TextBlock { Text: \"no route\"; } } }",
            "ContentControl { if (true) { TextBlock { Text: \"branch\"; } } Content: \"later\"; }",
            "Border { if (true) { TextBlock { Text: \"branch\"; } } Child: new TextBlock(); }",
        })
        {
            var result = LucentCompiler.Compile(
                $$"""
                namespace Demo;
                component Main()
                {
                    Fragment Render() { return {{root}}; }
                }
                """,
                "invalid-conditional.lui");
            Assert.IsFalse(result.Succeeded, root);
            Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Code == "LUC2001"), root);
        }
    }

    [TestMethod]
    public void Conditional_branches_project_fixed_fragments_to_collection_routes()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                private readonly State<bool> visible = new(true);
                Fragment Render() { return StackPanel {
                    if (visible.Value) { TextBlock { Text: "one"; } Border { } }
                    else { }
                }; }
            }
            """, "conditional-fragments.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var source = result.GeneratedSource!;
        StringAssert.Contains(source, "return Fragment.Concat(Fragment.From(__lucent_conditional1TrueControl1!), Fragment.From(__lucent_conditional1TrueControl2!));");
        StringAssert.Contains(source, "return Fragment.Empty;");
        StringAssert.Contains(source, ".Children.Clear();");
    }

    [TestMethod]
    public void Conditional_fragments_reject_multiple_scalar_roots_before_emission()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                Fragment Render() { return ContentControl {
                    if (true) { TextBlock { } Border { } }
                }; }
            }
            """, "conditional-scalar-overflow.lui");

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
            diagnostic.Message.Contains("scalar content route", StringComparison.Ordinal)));
        Assert.IsNull(result.GeneratedSource);
    }

    [TestMethod]
    public void Initial_computed_startup_does_not_recursively_start_dependents()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                private readonly Computed<int> first = new(ct => Task.FromResult(1), 0);
                private readonly Computed<int> second = new(ct => Task.FromResult(first.Value + 1), 0);
                Fragment Render() { return TextBlock { Text: second.Value.ToString(); }; }
            }
            """,
            "computed-startup.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var source = result.GeneratedSource!;
        StringAssert.Contains(source, "__lucent_startingComputedWork = true;");
        StringAssert.Contains(Method(source, "private void __lucent_InvalidateSource0()"),
            "if (!__lucent_startingComputedWork) __lucent_RefreshSecond();");
        Assert.AreEqual(1, Method(source, "public Fragment Mount()")
            .Split("__lucent_RefreshFirst();", StringSplitOptions.None).Length - 1);
        Assert.AreEqual(1, Method(source, "public Fragment Mount()")
            .Split("__lucent_RefreshSecond();", StringSplitOptions.None).Length - 1);
    }

    [TestMethod]
    public void Island_binding_preserves_target_typing_and_maps_type_failures()
    {
        var valid = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                private readonly State<int[]> items = new([1, 2]);
                private readonly State<object> value = new(new());
                private readonly Computed<int[]> copy = new(ct => Task.FromResult(items.Value), []);
                Fragment Render() { return Border { Tag: new(); }; }
            }
            """,
            "target-typed.lui");
        Assert.IsTrue(valid.Succeeded, string.Join(Environment.NewLine, valid.Diagnostics));
        StringAssert.Contains(valid.GeneratedSource!, "private int[] __lucent_stateItems;");
        StringAssert.Contains(valid.GeneratedSource!, "__lucent_stateItems = [1, 2];");
        StringAssert.Contains(valid.GeneratedSource!, "private object __lucent_stateValue;");
        StringAssert.Contains(valid.GeneratedSource!, "__lucent_stateValue = new();");
        StringAssert.Contains(valid.GeneratedSource!, ".Tag = new();");

        foreach (var source in new[]
        {
            "private readonly State<int> value = new(\"wrong\");",
            "private readonly Computed<int> value = new(ct => Task.FromResult(1), \"wrong\");",
        })
        {
            var invalid = LucentCompiler.Compile(
                $$"""
                namespace Demo;
                component Main()
                {
                    {{source}}
                    Fragment Render() { return TextBlock { Text: "x"; }; }
                }
                """,
                "typed-failure.lui");
            Assert.IsFalse(invalid.Succeeded);
            Assert.IsTrue(invalid.Diagnostics.Any(diagnostic =>
                diagnostic.Code == "LUC3001" && diagnostic.Span.Start > 0));
        }

        var nonBoolean = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                Fragment Render() { return StackPanel { if (1) { TextBlock { Text: "x"; } } }; }
            }
            """,
            "non-boolean.lui");
        Assert.IsFalse(nonBoolean.Succeeded);
        Assert.IsTrue(nonBoolean.Diagnostics.Any(diagnostic => diagnostic.Code == "LUC3001"));

        var unknown = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main() { Fragment Render() { return TextBlock { Text: missingValue; }; } }
            """,
            "unknown-name.lui");
        Assert.IsFalse(unknown.Succeeded);
        Assert.IsTrue(unknown.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "LUC3001" && diagnostic.Span.Start > 0));

        var uppercaseUnknown = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main() { Fragment Render() { return TextBlock { Text: MissingValue; }; } }
            """,
            "uppercase-unknown-name.lui");
        Assert.IsFalse(uppercaseUnknown.Succeeded);
        Assert.IsTrue(uppercaseUnknown.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "LUC3001" && diagnostic.Message.Contains("MissingValue", StringComparison.Ordinal)));

        var invalidDynamicExtension = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                private readonly State<dynamic[]> items = new([]);
                private readonly State<int[]> numbers = new([]);
                Fragment Render()
                {
                    return StackPanel {
                        foreach (var item in items.Value) keyed by item {
                            TextBlock { Text: numbers.Value.Append(item).Count().ToString(); }
                        }
                    };
                }
            }
            """,
            "dynamic-extension.lui");
        Assert.IsFalse(invalidDynamicExtension.Succeeded);
        Assert.IsTrue(invalidDynamicExtension.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "LUC3001" &&
            diagnostic.Message.Contains("dynamically dispatched", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Reactive_class_values_are_updated_by_their_source()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Main()
            {
                private readonly State<string> cssClass = new("first");
                Fragment Render() { return Border { Class: cssClass.Value; }; }
            }
            """,
            "reactive-class.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource!, "private string? __lucent_binding1Class;");
        StringAssert.Contains(Method(result.GeneratedSource!, "private void __lucent_UpdateBinding1()"), "Classes.Remove(__lucent_binding1Class)");
        StringAssert.Contains(Method(result.GeneratedSource!, "private void __lucent_InvalidateSource0()"), "__lucent_UpdateBinding1();");
    }

    private static string Method(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start, signature);
        var next = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return source[start..(next < 0 ? source.Length : next)];
    }
}
