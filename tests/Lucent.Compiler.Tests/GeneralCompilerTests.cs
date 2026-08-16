using Lucent.Compiler.Syntax;

namespace Lucent.Compiler.Tests;

[TestClass]
public sealed class GeneralCompilerTests
{
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
            "private global::Avalonia.Controls.Border? _control1;");
        StringAssert.Contains(
            result.GeneratedSource,
            "private global::Avalonia.Controls.StackPanel? _control2;");
        StringAssert.Contains(result.GeneratedSource, ".Padding = new Avalonia.Thickness(8);");
        StringAssert.Contains(
            result.GeneratedSource,
            ".Spacing = 6;");
        StringAssert.Contains(result.GeneratedSource, ".Content = \"Add\";");
        StringAssert.Contains(result.GeneratedSource, "_control1!.Child = _control2!;");
        StringAssert.Contains(result.GeneratedSource, ".Click += OnControl4Click;");
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
        StringAssert.Contains(result.GeneratedSource, "_control5");
        StringAssert.Contains(result.GeneratedSource, "OnControl5Click");
        StringAssert.Contains(result.GeneratedSource, "#line");
        StringAssert.Contains(result.GeneratedSource, "SetCount(_count + 1);");
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
            "private global::Avalonia.Controls.StackPanel? _control1;");
        StringAssert.Contains(result.GeneratedSource, "_control1!.Orientation = Orientation.Horizontal;");
        StringAssert.Contains(
            result.GeneratedSource,
            "_control1!.Spacing = 8;");
        StringAssert.Contains(result.GeneratedSource, "_control1!.Children.Add(_control2!);");
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
            "private global::Avalonia.Controls.Border? _control1;");
        StringAssert.Contains(result.GeneratedSource, "_control1!.Child = _control2!;");
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
        StringAssert.Contains(result.GeneratedSource, "_control1!.Content = \"Add task\";");
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
        StringAssert.Contains(implicitResult.GeneratedSource, "_control1!.Content = \"Add task\";");
        StringAssert.Contains(explicitResult.GeneratedSource, "_control1!.Content = \"Add task\";");
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
            "_control1!.Padding = new global::Avalonia.Thickness(12, 8)");
        StringAssert.Contains(
            result.GeneratedSource,
            "_control1!.CornerRadius = new global::Avalonia.CornerRadius(6)");
        StringAssert.Contains(
            result.GeneratedSource,
            "_control2!.FontSize = 24;");
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
        StringAssert.Contains(result.GeneratedSource, "private string _title = \"Todos\";");
        StringAssert.Contains(result.GeneratedSource, "private int[] _items = [1, 2, 3];");
        StringAssert.Contains(
            result.GeneratedSource,
            "private readonly Dictionary<object, ILoopEntry> _region1 = new();");
        StringAssert.Contains(
            result.GeneratedSource,
            "var sourceItems = (_items.Where(value => value > 1)).ToArray();");
        StringAssert.Contains(result.GeneratedSource, "foreach (var item in sourceItems)");
        StringAssert.Contains(result.GeneratedSource, "entry.Update(item);");
        StringAssert.Contains(result.GeneratedSource, "nativeItems.Add(entry.Root);");
        StringAssert.Contains(
            result.GeneratedSource,
            "ReferenceEquals(nativeItems[index], nextEntries[index].Root)");
        StringAssert.Contains(result.GeneratedSource, "if (orderChanged)");
        StringAssert.Contains(result.GeneratedSource, "var rowOwner = _owner.CreateChild();");
        StringAssert.Contains(result.GeneratedSource, "rowOwner.OnDispose(() =>");
        StringAssert.Contains(result.GeneratedSource, "return (control1, (Action)Refresh, rowOwner.Dispose);");
        StringAssert.Contains(result.GeneratedSource, "_owner.OnDispose(_region1.Clear);");
        Assert.IsFalse(result.GeneratedSource.Contains("Dispatcher.UIThread", StringComparison.Ordinal));
        Assert.IsFalse(result.GeneratedSource.Contains("disposeActions", StringComparison.Ordinal));
        Assert.IsTrue(
            result.GeneratedSource.IndexOf(
                "duplicate key",
                StringComparison.Ordinal) <
            result.GeneratedSource.IndexOf(
                "_region1.TryGetValue",
                StringComparison.Ordinal),
            "All keys should be validated before the existing region is mutated.");
        StringAssert.Contains(
            result.GeneratedSource,
            "private void SetItems(Func<int[], int[]> update)");
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
        StringAssert.Contains(result.GeneratedSource, "_control1!.Click += OnControl1Click;");
        StringAssert.Contains(result.GeneratedSource, "SetCount(_count + 1);");
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
        StringAssert.Contains(result.GeneratedSource, ".Loaded += OnControl1Loaded;");
        StringAssert.Contains(
            result.GeneratedSource,
            "_owner.OnDispose(() => _control1!.Loaded -= OnControl1Loaded);");
        StringAssert.Contains(result.GeneratedSource, "public void Dispose() => _owner.Dispose();");
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
            "ObjectDisposedException.ThrowIf(_owner.IsDisposed, this);");
        StringAssert.Contains(
            result.GeneratedSource,
            "A Main component can only be mounted once.");
        StringAssert.Contains(result.GeneratedSource, "public void Dispose() => _owner.Dispose();");
        StringAssert.Contains(
            result.GeneratedSource,
            "_owner.Dispatch(() => SetCountCore(value));");
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
                        TextBlock { Text: other.title.Value; }
                        TextBlock { Text: title.Value; }
                    };
                }
            }
            """,
            "state-rewrite.lui");

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        StringAssert.Contains(result.GeneratedSource, ".Text = \"title.Value\";");
        StringAssert.Contains(result.GeneratedSource, ".Text = other.title.Value;");
        StringAssert.Contains(result.GeneratedSource, ".Text = _title;");
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
    public void Computed_values_keep_stale_data_cancel_prior_work_and_ignore_late_results()
    {
        var result = LucentCompiler.Compile(
            """
            namespace Demo;
            component Search()
            {
                private readonly State<string> query = new("lucent");
                private readonly Computed<string[]> results = new(
                    cancellationToken => Catalog.SearchAsync(query.Value, cancellationToken),
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
        StringAssert.Contains(result.GeneratedSource, "private string[] _results = [];");
        StringAssert.Contains(result.GeneratedSource, "++_resultsGeneration");
        StringAssert.Contains(result.GeneratedSource, "_resultsCancellation?.Cancel();");
        StringAssert.Contains(result.GeneratedSource, "generation != _resultsGeneration");
        StringAssert.Contains(
            result.GeneratedSource,
            "CreateLinkedTokenSource(_owner.CancellationToken)");
        StringAssert.Contains(result.GeneratedSource, "_owner.Dispatch(() =>");
        Assert.IsFalse(result.GeneratedSource.Contains("Dispatcher.UIThread", StringComparison.Ordinal));
        StringAssert.Contains(result.GeneratedSource, "Catalog.SearchAsync(_query, cancellationToken)");
        StringAssert.Contains(result.GeneratedSource, "var sourceItems = (_results).ToArray();");
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
            .shell { gap: 8px; padding: 16px; }
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
}
