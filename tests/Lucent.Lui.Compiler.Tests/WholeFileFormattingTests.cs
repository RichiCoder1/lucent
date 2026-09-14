using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class WholeFileFormattingTests
{
    [TestMethod]
    public void FormattedMemberExecutionPreservesRawVerbatimAndInterpolatedValues()
    {
        const string source = """"
internal component Example() {
string Read(int value) {
var raw="""
    first "quoted" line
      indentation
    """;
if(value>0){return $"value {value+1, 8:X} {raw}";}
return @"literal
spacing";
}
<Text>{Read(2)}</Text>
}
"""";
        var input = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var formatted = Format(input, 40);
        foreach (var value in new[] { -1, 7 })
            Assert.AreEqual(Evaluate(input, value), Evaluate(formatted, value));

        static string Evaluate(string input, int value)
        {
            var member = LuiParser.Parse(input).Component!.Body.OfType<LuiMemberSyntax>().Single();
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create(
                "FormatRuntime" + Guid.NewGuid().ToString("N"),
                [
                    CSharpSyntaxTree.ParseText(
                        "public static class Specimen { public static " + member.Text + " }"
                    ),
                ],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
            using var stream = new MemoryStream();
            var emitted = compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(" | ", emitted.Diagnostics));
            return (string)
                System
                    .Reflection.Assembly.Load(stream.ToArray())
                    .GetType("Specimen")!
                    .GetMethod("Read")!
                    .Invoke(null, [value])!;
        }
    }

    [TestMethod]
    public void WrappingPreservesCommentsInlineStylesAndMeaningfulText()
    {
        const string source = """
namespace Sample;
internal component Example(string firstParameter, /* parameter context */ string secondParameter) {
int value=0;
void Increment(){value++;}
// The recipe keeps this explanation.
<Button label="A long label that keeps its spaces" onInvoke={Increment} style={Base with { Padding: 12; Spacing: 8; }}>Read https://example.com/now</Button>
}
style Base { Spacing: 4; }
""";
        var formatted = Format(source, 60);
        StringAssert.Contains(formatted, "Example(\n    string firstParameter,");
        StringAssert.Contains(formatted, "/* parameter context */");
        StringAssert.Contains(
            formatted,
            "\n\n    // The recipe keeps this explanation.\n    <Button"
        );
        StringAssert.Contains(formatted, "Padding: 12;\n");
        StringAssert.Contains(formatted, "Spacing: 8;\n");
        StringAssert.Contains(formatted, "Read https://example.com/now");
        Assert.IsFalse(formatted.Split('\n').Any(line => line.EndsWith(' ')), formatted);
    }

    [TestMethod]
    public void RecipeSeparatorIncludesStructuralStartsAndAttachedComments()
    {
        foreach (
            var recipe in new[]
            {
                "if (visible) { <Text>Hi</Text> }",
                "foreach (var item in items) keyed by item.Id { <Text>{item.Name}</Text> }",
            }
        )
        {
            var source =
                "internal component Example(bool visible, Item[] items) { int value=0;\n// attached\n"
                + recipe
                + " }";
            StringAssert.Contains(Format(source), "int value = 0;\n\n    // attached\n");
        }
        Assert.AreEqual(
            "internal component Example() {\n    <Text>Hi</Text>\n}\n",
            Format("internal component Example(){<Text>Hi</Text>}")
        );
    }

    [TestMethod]
    public void MemberGroupingAndLiteralContentsSurviveStructuralLineEndingOverrides()
    {
        const string source = """"
internal component Example() {
string Read() { var first=1;


var second=2;
return $"{first}:{second}" + @"verbatim
literal" + """
  raw
    indentation
  """;
}
<Text>{Read()}</Text>
}
"""";
        var input = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var formatted = Format(input);
        StringAssert.Contains(formatted, "var first = 1;\n\n        var second = 2;");
        foreach (var ending in new[] { LuiLineEnding.CrLf, LuiLineEnding.Cr })
        {
            var result = LuiFormatter.FormatDocument(input, ending);
            Assert.AreEqual(LuiFormattingStatus.Changed, result.Status, Diagnostics(result));
            StringAssert.Contains(result.Text, "@\"verbatim\nliteral\"");
            StringAssert.Contains(result.Text, "\"\"\"\n  raw\n    indentation\n  \"\"\"");
            Assert.AreEqual(result.Text, LuiFormatter.FormatDocument(result.Text, ending).Text);
        }
    }

    [TestMethod]
    public void ScopedIgnorePreservesExactlyOneNodeAndRejectsUnboundedOrUnreasonedMarkers()
    {
        const string source =
            "internal component Example() { <Root>\n// lui-format-ignore: column alignment is intentional\n// attached\n<Column><Text>{  value }</Text></Column>\n<Text>{  value }</Text>\n</Root>}";
        var formatted = Format(source);
        StringAssert.Contains(formatted, "<Column><Text>{  value }</Text></Column>");
        StringAssert.Contains(formatted, "\n        <Text>{value}</Text>");
        var ignoredChild = source.IndexOf("<Text>", StringComparison.Ordinal);
        var range = new LuiSpan(ignoredChild, "<Text>{  value }</Text>".Length);
        Assert.AreEqual(
            LuiFormattingStatus.Clean,
            LuiFormatter.FormatSelection(source, range).Status
        );
        foreach (
            var marker in new[]
            {
                "// lui-format-ignore:",
                "// lui-format-off: keep",
                "// lui-format-ignore: dangling",
            }
        )
        {
            var input = "internal component Example() { <Root>\n" + marker + "\n</Root>}";
            var result = LuiFormatter.FormatDocument(input);
            Assert.AreEqual(LuiFormattingStatus.Unavailable, result.Status, marker);
            Assert.AreEqual(input, result.Text);
            Assert.IsTrue(result.Diagnostics.Any(item => item.Id == "LUI6003"));
        }
    }

    [TestMethod]
    public void StyleRangeIncludesCompleteAssignmentsWithoutChangingNeighbors()
    {
        const string source =
            "internal component Example() {<Text>Hi</Text>}\nstyle Example {\n    Padding:  12;\n    Spacing:  8;\n}";
        var span = LuiParser.Parse(source).Styles.Single().Members[0].Span;
        var result = LuiFormatter.FormatSelection(source, span);
        Assert.AreEqual(LuiFormattingStatus.Changed, result.Status, Diagnostics(result));
        Assert.AreEqual(
            source.Replace("Padding:  12;", "Padding: 12;", StringComparison.Ordinal),
            result.Text
        );
    }

    [TestMethod]
    public void EmbeddedListsWrapAtTheirBlockIndentationAndRespectTabs()
    {
        const string source =
            "internal component Example() { string Summary() { return Format(firstLongArgument, secondLongArgument); } <Text>{Summary()}</Text> }";
        var formatted = Format(source, 50);
        StringAssert.Contains(
            formatted,
            "return Format(\n            firstLongArgument,\n            secondLongArgument\n        );"
        );
        var options = new LuiFormattingOptions(
            useTabs: true,
            lineWidth: 50,
            lineEnding: LuiLineEnding.Lf
        );
        var tabs = LuiFormatter.FormatDocument(source, options);
        Assert.AreEqual(LuiFormattingStatus.Changed, tabs.Status, Diagnostics(tabs));
        StringAssert.Contains(
            tabs.Text,
            "\t\treturn Format(\n\t\t\tfirstLongArgument,\n\t\t\tsecondLongArgument\n\t\t);"
        );
        Assert.AreEqual(tabs.Text, LuiFormatter.FormatDocument(tabs.Text, options).Text);
    }

    [TestMethod]
    public void AdjacentRangeSiblingsFormatWithoutChangingSurroundingSource()
    {
        const string source =
            "internal component Example(){\n    <Column>\n        <Text>{  first  }</Text>\n        <Text>{  second  }</Text>\n    </Column>\n}";
        var children = (
            (LuiElementSyntax)LuiParser.Parse(source).Component!.Body.Single()
        ).Children;
        var range = LuiSpan.From(children[0].Span.Start, children[1].Span.End);
        var result = LuiFormatter.FormatSelection(source, range);
        Assert.AreEqual(LuiFormattingStatus.Changed, result.Status, Diagnostics(result));
        StringAssert.Contains(result.Text, "<Text>{first}</Text>\n        <Text>{second}</Text>");
        Assert.IsTrue(result.Text.StartsWith(source[..range.Start], StringComparison.Ordinal));
        Assert.IsTrue(result.Text.EndsWith(source[range.End..], StringComparison.Ordinal));
    }

    private static string Format(string source, int width = 100)
    {
        var options = new LuiFormattingOptions(lineWidth: width, lineEnding: LuiLineEnding.Lf);
        var result = LuiFormatter.FormatDocument(source, options);
        Assert.IsTrue(
            result.Status is LuiFormattingStatus.Clean or LuiFormattingStatus.Changed,
            Diagnostics(result)
        );
        Assert.AreEqual(
            result.Text,
            LuiFormatter.FormatDocument(result.Text, options).Text,
            result.Text
        );
        Assert.AreEqual(
            LuiSourceComparison.StructuralKey(source),
            LuiSourceComparison.StructuralKey(result.Text),
            result.Text
        );
        return result.Text;
    }

    private static string Diagnostics(LuiFormattingResult result) =>
        string.Join(" | ", result.Diagnostics.Select(item => item.Id + ": " + item.Message));
}
