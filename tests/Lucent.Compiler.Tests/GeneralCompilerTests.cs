using Lucent.Compiler.Syntax;

namespace Lucent.Compiler.Tests;

[TestClass]
public sealed class GeneralCompilerTests
{
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
                        class: "root";
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
}
