using Lucent.Lui.Compiler;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class FormattingPreservationTests
{
    [TestMethod]
    public void GroupedLayoutWrapsListsAndLetsNestedGroupsStayCompact()
    {
        static LuiLayoutDocument List(string name, params LuiLayoutDocument[] items) =>
            LuiLayoutDocument.Group(
                LuiLayoutDocument.Concat(
                    LuiLayoutDocument.Text(name + "("),
                    LuiLayoutDocument.Indent(
                        LuiLayoutDocument.Concat(
                            LuiLayoutDocument.SoftLine,
                            LuiLayoutDocument.Join(
                                LuiLayoutDocument.Concat(
                                    LuiLayoutDocument.Text(","),
                                    LuiLayoutDocument.Line
                                ),
                                items
                            )
                        )
                    ),
                    LuiLayoutDocument.SoftLine,
                    LuiLayoutDocument.Text(")")
                )
            );
        var document = List(
            "Call",
            LuiLayoutDocument.Text("firstArgument"),
            List("Pair", LuiLayoutDocument.Text("a"), LuiLayoutDocument.Text("b"))
        );
        Assert.AreEqual("Call(firstArgument, Pair(a, b))", document.Render(100));
        Assert.AreEqual("Call(\n    firstArgument,\n    Pair(a, b)\n)", document.Render(22));
        Assert.AreEqual(
            "Call(\r\n\tfirstArgument,\r\n\tPair(a, b)\r\n)",
            document.Render(22, "\t", "\r\n")
        );
        var comment = List(
            "Call",
            LuiLayoutDocument.Concat(
                LuiLayoutDocument.Text("value // keep"),
                LuiLayoutDocument.HardLine
            )
        );
        StringAssert.Contains(comment.Render(100), "// keep\n");
    }

    [TestMethod]
    public void ResultsDistinguishCleanChangedMalformedAndPreserveParameterComments()
    {
        const string source = "internal component View(){<Text>Hi</Text>}";
        var changed = LuiFormatter.FormatDocument(source);
        Assert.AreEqual(LuiFormattingStatus.Changed, changed.Status);
        Assert.AreEqual(1, changed.Edits.Count);
        Assert.AreEqual(
            LuiFormattingStatus.Clean,
            LuiFormatter.FormatDocument(changed.Text).Status
        );
        const string malformed = "internal component Broken() { <Text>";
        var invalid = LuiFormatter.FormatDocument(malformed);
        Assert.AreEqual(LuiFormattingStatus.Unavailable, invalid.Status);
        Assert.AreEqual(malformed, invalid.Text);
        Assert.AreEqual(0, invalid.Edits.Count);
        Assert.IsTrue(invalid.Diagnostics.Count > 0);

        // The original printer lost separator trivia. The grouped printer keeps it.
        const string comments =
            "internal component View(int a, /* keep */ int b) { <Text>Hi</Text> }";
        var preserved = LuiFormatter.FormatDocument(comments);
        Assert.AreEqual(LuiFormattingStatus.Changed, preserved.Status);
        StringAssert.Contains(preserved.Text, "/* keep */");
        Assert.AreEqual(
            LuiFormattingStatus.Clean,
            LuiFormatter.FormatDocument(preserved.Text).Status
        );
    }

    [TestMethod]
    public void LexicalComparisonPreservesLiteralCommentTextAndTokenBoundaries()
    {
        const string source = "internal component View(string label) { <Text>{label}</Text> }";
        Assert.AreEqual(
            LuiSourceComparison.StructuralKey(source),
            LuiSourceComparison.StructuralKey(source.Replace("{ <Text>", "{\n    <Text>"))
        );
        foreach (
            var pair in new[]
            {
                ("<Text>two words</Text>", "<Text>two  words</Text>"),
                ("<Text>{\"two words\"}</Text>", "<Text>{\"two  words\"}</Text>"),
                ("// documented\n<Text />", "// reworded\n<Text />"),
                ("<Text>{label + +1}</Text>", "<Text>{label++ + 1}</Text>"),
                ("<Text>{\"\"\"raw text\"\"\"}</Text>", "<Text>{\"\"\"raw  text\"\"\"}</Text>"),
            }
        )
        {
            var before = "internal component View(string label) { " + pair.Item1 + " }";
            var after = "internal component View(string label) { " + pair.Item2 + " }";
            Assert.IsNotNull(LuiSourceComparison.StructuralKey(before));
            Assert.AreNotEqual(
                LuiSourceComparison.StructuralKey(before),
                LuiSourceComparison.StructuralKey(after)
            );
        }
    }

    [TestMethod]
    public void MalformedRangesAndCancellationCannotPublishEdits()
    {
        const string source = "internal component View() { <Text>Hi</Text> }";
        Assert.AreEqual(
            LuiFormattingStatus.Unavailable,
            LuiFormatter.FormatSelection(source, new LuiSpan(0, 1)).Status
        );
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            LuiFormatter.FormatDocument(source, cancellationToken: cancellation.Token)
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            LuiFormatter.FormatSelection(source, new LuiSpan(int.MaxValue, 1))
        );
    }
}
