using Lucent.Lui.Compiler;

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
}
