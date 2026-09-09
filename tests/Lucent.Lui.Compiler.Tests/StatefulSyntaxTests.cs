using System.Linq;
using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    public void MissingFieldSemicolonRecoversMarkupAndRetainsNativeDiagnosticSpan()
    {
        const string source =
            "internal component X() { string label = Missing()\n <Text>{label}</Text> }";
        var document = LuiParser.Parse(source);
        var member = document.Component!.Body.OfType<LuiMemberSyntax>().Single();
        var element = document.Component.Body.OfType<LuiElementSyntax>().Single();
        var missingSemicolon = document.Diagnostics.Single(diagnostic =>
            diagnostic.Id == "LUI1023"
        );
        Assert.AreEqual(
            "string label = Missing()",
            member.Text,
            "member recovery consumed the following markup"
        );
        Assert.AreEqual(source.IndexOf("<Text>", StringComparison.Ordinal), element.Span.Start);
        Assert.AreEqual(source.IndexOf('\n'), missingSemicolon.Span.Start);

        var result = LuiCompiler.Compile(
            document,
            CSharpCompilation.Create("member-recovery", references: References()),
            new LuiFreshnessIdentity(
                "member-recovery",
                "member-recovery",
                new LuiDocumentIdentity("MemberRecovery.lui"),
                "v1",
                "preview"
            )
        );
        var native = result.Diagnostics.Single(diagnostic => diagnostic.Id == "LUI2000");
        Assert.AreEqual(source.IndexOf("Missing", StringComparison.Ordinal), native.Span.Start);
        Assert.IsTrue(
            result.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1023"),
            "the missing-semicolon diagnostic was lost during lowering"
        );
    }

    [TestMethod]
    public void VarFieldsReportAnAuthoredStateDiagnosticWithoutNativeFieldError()
    {
        const string source =
            "internal component X() { var label = \"value\"; <Text>{label}</Text> }";
        var result = LuiCompiler.Compile(
            LuiParser.Parse(source),
            CSharpCompilation.Create("var-field", references: References()),
            new LuiFreshnessIdentity(
                "var-field",
                "var-field",
                new LuiDocumentIdentity("VarField.lui"),
                "v1",
                "preview"
            )
        );
        var diagnostic = result.Diagnostics.Single(item => item.Id == "LUI2023");
        Assert.AreEqual(source.IndexOf("var", StringComparison.Ordinal), diagnostic.Span.Start);
        Assert.IsFalse(
            result.Diagnostics.Any(item => item.Id == "LUI2000"),
            "the unsupported var field should not be replaced by a generated CS0825 diagnostic"
        );
    }

    [TestMethod]
    public void VarFieldKeepsNativeInitializerDiagnosticsAtAuthoredSpan()
    {
        const string source =
            "internal component X() { var label = Missing(); <Column><Row /></Column> }";
        var result = LuiCompiler.Compile(
            LuiParser.Parse(source),
            CSharpCompilation.Create("var-field-native", references: References()),
            new LuiFreshnessIdentity(
                "var-field-native",
                "var-field-native",
                new LuiDocumentIdentity("VarFieldNative.lui"),
                "v1",
                "preview"
            )
        );
        Assert.IsTrue(result.Diagnostics.Any(item => item.Id == "LUI2023"));
        Assert.IsTrue(
            result.Diagnostics.Any(item =>
                item.Id == "LUI2000"
                && item.Span.Start == source.IndexOf("Missing", StringComparison.Ordinal)
            ),
            "the native initializer error was hidden or lost its authored span: "
                + string.Join(
                    " | ",
                    result.Diagnostics.Select(item =>
                        item.Id + "@" + item.Span.Start + ":" + item.Message
                    )
                )
        );
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

    private static MetadataReference[] References() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(
                MetadataReference.CreateFromFile(
                    Path.Combine(AppContext.BaseDirectory, "Lucent.Core.dll")
                )
            )
            .ToArray();
}
