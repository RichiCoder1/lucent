using Lucent.Lui.Compiler;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class RequirementSyntaxTests
{
    [TestMethod]
    public void RequirementsHaveDedicatedKindsAndExactTypeAndMemberSpans()
    {
        const string source = """
namespace Example;
public component Editor() {
    context RouteContext<Issue> route;
    inject global::Example.IStore store;
    int context = 0;
    <Provide value={route}><Text>{context}</Text></Provide>
}
""";
        var document = LuiParser.Parse(source);
        Assert.AreEqual(0, document.Diagnostics.Count);
        var requirements = document.Component!.Body.OfType<LuiRequirementSyntax>().ToArray();
        Assert.AreEqual(2, requirements.Length);
        Assert.AreEqual(LuiRequirementKind.Context, requirements[0].Kind);
        Assert.AreEqual(LuiRequirementKind.Inject, requirements[1].Kind);
        Assert.AreEqual(
            "RouteContext<Issue>",
            source.Substring(requirements[0].TypeSpan.Start, requirements[0].TypeSpan.Length)
        );
        Assert.AreEqual(
            "store",
            source.Substring(requirements[1].Name.Span.Start, requirements[1].Name.Span.Length)
        );
        Assert.AreEqual(1, document.Component.Body.OfType<LuiMemberSyntax>().Count());
        Assert.IsInstanceOfType<LuiProvideSyntax>(document.Component.Body[^1]);
        var formatted = LuiFormatter.Format(source);
        Assert.AreEqual(
            2,
            LuiParser.Parse(formatted).Component!.Body.OfType<LuiRequirementSyntax>().Count()
        );
    }

    [TestMethod]
    public void InvalidRequirementsRecoverAtTheNextDeclarationOrRoot()
    {
        foreach (
            var invalid in new[]
            {
                "context Thing value = null;",
                "inject readonly IStore store;",
                "context Thing a, b;",
                "context Thing missing",
                "context Thing value { get; }",
            }
        )
        {
            var source =
                "public component X() {\n" + invalid + "\ninject IStore store;\n<Text>ok</Text>\n}";
            var document = LuiParser.Parse(source);
            Assert.IsTrue(document.Diagnostics.Any(item => item.Id == "LUI1024"), invalid);
            Assert.IsTrue(
                document
                    .Component!.Body.OfType<LuiRequirementSyntax>()
                    .Any(item => item.Name.Text == "store"),
                invalid
            );
            Assert.IsTrue(
                document
                    .Component.Body.OfType<LuiElementSyntax>()
                    .Any(item => item.Name.Text == "Text"),
                invalid
            );
        }

        const string sameLine =
            "public component X() { context Thing missing inject IStore recovered; <Text>ok</Text> }";
        var recovered = LuiParser.Parse(sameLine);
        Assert.IsTrue(recovered.Diagnostics.Any(item => item.Id == "LUI1024"));
        Assert.IsTrue(
            recovered
                .Component!.Body.OfType<LuiRequirementSyntax>()
                .Any(item => item.Name.Text == "recovered")
        );
        Assert.IsTrue(
            recovered
                .Component.Body.OfType<LuiElementSyntax>()
                .Any(item => item.Name.Text == "Text")
        );
    }

    [TestMethod]
    public void ProvideHasOneRootAndOnlyTheUnqualifiedNameIsReserved()
    {
        foreach (
            var invalid in new[]
            {
                "<Provide />",
                "<Provide value={model} />",
                "<Provide value={model} name=\"bad\"><Text>one</Text></Provide>",
                "<Provide value={model}><Text>one</Text><Text>two</Text></Provide>",
                "<Provide value={model}>if (true) { <Text>one</Text> }</Provide>",
                "<Provide value=\"literal\"><Text>one</Text></Provide>",
            }
        )
            Assert.IsTrue(
                LuiParser
                    .Parse("public component X() { " + invalid + " }")
                    .Diagnostics.Any(item => item.Id == "LUI1025"),
                invalid
            );
        var qualified = LuiParser.Parse("public component X() { <Custom.Provide /> }");
        Assert.AreEqual(0, qualified.Diagnostics.Count);
        Assert.IsFalse(qualified.Component!.Body.Single() is LuiProvideSyntax);
    }
}
