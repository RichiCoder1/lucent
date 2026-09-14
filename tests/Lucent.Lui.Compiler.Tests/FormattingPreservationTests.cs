using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
    public void SyntaxOnlyCSharpAdapterCanPreserveRuntimeLiteralsAndChooseSameLineBraces()
    {
        const string source = """"
public static class Specimen {
    public static string Read(int value,string prefix) {
        var raw = """
            first "quoted" line
              indented line
            """;
        // Keep this explanation.
        if(value>0){return $"{prefix}: {value}\n" + raw;}
        return @"literal
spacing";
    }
}
"""";
        var original = SyntaxFactory.ParseCompilationUnit(source);
        var normalized = original.NormalizeWhitespace(indentation: "    ", eol: "\n");
        var tokens = normalized.DescendantTokens().ToArray();
        var replacements = new Dictionary<SyntaxToken, SyntaxToken>();
        foreach (
            var brace in tokens.Where(token =>
                token.IsKind(SyntaxKind.OpenBraceToken)
                && token.Parent is BlockSyntax or BaseTypeDeclarationSyntax
            )
        )
        {
            var previous = brace.GetPreviousToken();
            if (
                previous
                    .TrailingTrivia.Concat(brace.LeadingTrivia)
                    .All(trivia =>
                        trivia.IsKind(SyntaxKind.WhitespaceTrivia)
                        || trivia.IsKind(SyntaxKind.EndOfLineTrivia)
                    )
            )
            {
                replacements[previous] = previous.WithTrailingTrivia(SyntaxFactory.Space);
                replacements[brace] = brace.WithLeadingTrivia(default(SyntaxTriviaList));
            }
        }
        var formatted = normalized
            .ReplaceTokens(replacements.Keys, (token, _) => replacements[token])
            .ToFullString();
        StringAssert.Contains(formatted, "Read(int value, string prefix) {");
        StringAssert.Contains(formatted, "if (value > 0) {");
        StringAssert.Contains(formatted, "// Keep this explanation.");
        Assert.IsFalse(SyntaxFactory.ParseCompilationUnit(formatted).ContainsDiagnostics);
        foreach (var value in new[] { -1, 7 })
            Assert.AreEqual(Evaluate(source, value), Evaluate(formatted, value));

        static string Evaluate(string code, int value)
        {
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create(
                "FormatSpecimen" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(code)],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.IsTrue(result.Success, string.Join(" | ", result.Diagnostics));
            var assembly = System.Reflection.Assembly.Load(stream.ToArray());
            return (string)
                assembly
                    .GetType("Specimen")!
                    .GetMethod("Read")!
                    .Invoke(null, [value, "Unicode 日本語"])!;
        }
    }

    [TestMethod]
    public void ResultsDistinguishCleanChangedMalformedAndUnsupportedPreservation()
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

        // The initial printer loses separator trivia. Until that printer is replaced,
        // valid input must be reported unavailable instead of silently deleting comments.
        const string comments =
            "internal component View(int a, /* keep */ int b) { <Text>Hi</Text> }";
        var unavailable = LuiFormatter.FormatDocument(comments);
        Assert.AreEqual(LuiFormattingStatus.Unavailable, unavailable.Status);
        Assert.AreEqual("LUI6001", unavailable.Diagnostics.Single().Id);
        Assert.AreEqual(comments, unavailable.Text);
        Assert.AreEqual(0, unavailable.Edits.Count);
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
