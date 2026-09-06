using System.Linq;
using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class StatefulSyntaxTests
{
    [TestMethod]
    public void ParsesMembersBeforeTheSingleMarkupRootWithExactSpans()
    {
        const string source = """
internal component Disclosure(string value) {
    bool expanded = false;
    [Once] string draft = value;
    readonly string initial = value;
    void Toggle() { expanded = !expanded; }
    Setup(owner) { owner.Own(new object()); }
    <Text>{draft}</Text>
}
""";
        var document = LuiParser.Parse(source);
        Assert.AreEqual(0, document.Diagnostics.Count, Diagnostics(document));
        var members = document.Component!.Body.OfType<LuiMemberSyntax>().ToArray();
        Assert.AreEqual(5, members.Length);
        CollectionAssert.AreEqual(
            new[]
            {
                LuiMemberKind.Field,
                LuiMemberKind.Field,
                LuiMemberKind.Field,
                LuiMemberKind.Method,
                LuiMemberKind.Setup,
            },
            members.Select(member => member.Kind).ToArray()
        );
        Assert.IsTrue(members.Take(3).All(member => member.Declaration is FieldDeclarationSyntax));
        Assert.IsInstanceOfType<MethodDeclarationSyntax>(members[3].Declaration);
        Assert.IsInstanceOfType<MethodDeclarationSyntax>(members[4].Declaration);
        Assert.AreEqual("owner", members[4].SetupOwner!.Text);
        foreach (var member in members)
            Assert.AreEqual(member.Text, source.Substring(member.Span.Start, member.Span.Length));
    }

    [TestMethod]
    public void SetupIsUniqueSynchronousAndHasOnlyAnOptionalOwner()
    {
        var duplicate = LuiParser.Parse(
            "internal component X() { Setup() { } Setup(owner) { } <Text /> }"
        );
        Assert.IsTrue(duplicate.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1019"));

        var awaited = LuiParser.Parse(
            "internal component X() { Setup(owner) { await Work(); } <Text /> }"
        );
        var awaitDiagnostic = awaited.Diagnostics.Single(diagnostic => diagnostic.Id == "LUI1021");
        Assert.AreEqual(
            "await",
            awaited.Source.Substring(awaitDiagnostic.Span.Start, awaitDiagnostic.Span.Length)
        );

        var parameters = LuiParser.Parse(
            "internal component X() { Setup(first, second) { } <Text /> }"
        );
        Assert.IsTrue(parameters.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1020"));
    }

    [TestMethod]
    public void FormatterPreservesMemberTextAndIsIdempotent()
    {
        const string source =
            "internal component X(){bool expanded=false;void Toggle(){ expanded = !expanded; }Setup(owner){ owner.Own(value); }<Text>{expanded}</Text>}";
        var formatted = LuiFormatter.Format(source, LuiLineEnding.Lf);
        StringAssert.Contains(formatted, "    bool expanded=false;\n");
        StringAssert.Contains(formatted, "    void Toggle(){ expanded = !expanded; }\n");
        StringAssert.Contains(formatted, "    Setup(owner){ owner.Own(value); }\n");
        Assert.AreEqual(formatted, LuiFormatter.Format(formatted, LuiLineEnding.Lf));
    }

    private static string Diagnostics(LuiDocumentSyntax document) =>
        string.Join(" | ", document.Diagnostics.Select(item => item.Id + ": " + item.Message));
}
