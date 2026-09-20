using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class ProjectedFormattingTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OrdinaryDeclarationsPreserveTokensAndReachStableLayout(bool includeComponent)
    {
        var source = """
            namespace Sample;
            using System;
            // model comment
            public record Person(string Name);
            public static class Helper{public static string Read(int value){if(value>0){return @"unchanged  literal";}return "// lui-format-ignore: is a literal";}}
            """;
        if (includeComponent)
            source +=
                "\npublic component Card(){<Column><Text>https://example.com/a  b</Text><Text>{Helper.Read(1)}</Text></Column>}";
        var before = LuiSourceComparison.StructuralKey(source);
        Assert.IsNotNull(before);
        var formatted = LuiFormatter.FormatDocument(source);
        Assert.AreEqual(
            LuiFormattingStatus.Changed,
            formatted.Status,
            String.Join(" | ", formatted.Diagnostics.Select(diagnostic => diagnostic.Message))
        );
        Assert.AreEqual(before, LuiSourceComparison.StructuralKey(formatted.Text));
        StringAssert.Contains(formatted.Text, "if (value > 0)");
        Assert.AreEqual(
            LuiFormattingStatus.Clean,
            LuiFormatter.FormatDocument(formatted.Text).Status
        );
    }

    [TestMethod]
    public void SupportDeclarationDirectiveIsNotSilentlyIgnored()
    {
        const string source =
            "namespace Sample;\n// lui-format-ignore: retain\npublic record Person( string Name );";
        var result = LuiFormatter.FormatDocument(source);
        Assert.AreEqual(LuiFormattingStatus.Unavailable, result.Status);
        Assert.AreEqual(source, result.Text);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI6003"));
    }

    [TestMethod]
    public void OrdinaryDeclarationRangeFormatsOnlyCompleteSelectedTypes()
    {
        const string source = """
            namespace Sample;
            public record Person( string Name );
            public static class Helper{public static int Read( int value ){return value+1;}}
            public component Card() { <Text>{Helper.Read(1).ToString()}</Text> }
            """;
        var prefix = LuiAuthoredSourceProjection.Project(source).DeclarationsSource;
        var root = CSharpSyntaxTree.ParseText(prefix).GetCompilationUnitRoot();
        var helper = ((FileScopedNamespaceDeclarationSyntax)root.Members[0]).Members[1];
        var result = LuiFormatter.FormatSelection(
            source,
            new LuiSpan(helper.SpanStart, helper.Span.Length)
        );

        Assert.AreEqual(
            LuiFormattingStatus.Changed,
            result.Status,
            String.Join(" | ", result.Diagnostics.Select(static item => item.Message))
        );
        StringAssert.Contains(result.Text, "public record Person( string Name );");
        StringAssert.Contains(result.Text, "public static class Helper {");
        StringAssert.Contains(result.Text, "return value + 1;");
        StringAssert.Contains(
            result.Text,
            "public component Card() { <Text>{Helper.Read(1).ToString()}</Text> }"
        );
        Assert.AreEqual(
            LuiSourceComparison.StructuralKey(source),
            LuiSourceComparison.StructuralKey(result.Text)
        );
    }

    [TestMethod]
    public void WholeDocumentFormattingPreservesDeclarationsStylesAndNamedComponent()
    {
        const string source = """
            namespace Sample;
            public record Model( string Name );
            style RootStyle{MainGrow:1;}
            public component Card(Model model){<Text style={RootStyle}>{model.Name}</Text>}
            """;

        var result = LuiFormatter.FormatDocument(source);

        Assert.AreEqual(
            LuiFormattingStatus.Changed,
            result.Status,
            String.Join(" | ", result.Diagnostics.Select(static item => item.Message))
        );
        StringAssert.Contains(result.Text, "public record Model(string Name);");
        StringAssert.Contains(result.Text, "style RootStyle {");
        StringAssert.Contains(result.Text, "public component Card(Model model) {");
        Assert.AreEqual(
            LuiSourceComparison.StructuralKey(source),
            LuiSourceComparison.StructuralKey(result.Text)
        );
    }
}
