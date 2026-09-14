using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class AssignmentCallbackTests
{
    [TestMethod]
    public void AssignmentCallbacksPointToTheAssignmentAndRecommendNamedMethods()
    {
        foreach (
            var assignment in new[]
            {
                "state.Draft = value",
                "state.Draft += value",
                "state.Draft ??= value",
            }
        )
        {
            var source =
                "internal component Editor() { <Field editor={  field => TextField(field, () => state.Draft, value => "
                + assignment
                + ")  } /> }";
            var document = LuiParser.Parse(source);
            var diagnostic = document.Diagnostics.Single(item => item.Id == "LUI1012");
            Assert.AreEqual(
                new LuiSpan(
                    source.IndexOf(assignment, StringComparison.Ordinal),
                    assignment.Length
                ),
                diagnostic.Span
            );
            StringAssert.Contains(diagnostic.Message, "Assignment expressions");
            StringAssert.Contains(diagnostic.Message, "named component or C# method");
        }
    }

    [TestMethod]
    public void InitializerAssignmentsRemainAllowedAndDoNotMaskCallbackAssignments()
    {
        const string accepted =
            "internal component X() { <Text style={original with { Width = Size.Pixels(10) }}>{new Model { Label = \"ok\" }}</Text> }";
        Assert.AreEqual(0, LuiParser.Parse(accepted).Diagnostics.Count);
        const string rejected =
            "internal component X() { <Text>{new Model { Label = \"ok\", Callback = value => state.Draft = value }}</Text> }";
        var diagnostic = LuiParser.Parse(rejected).Diagnostics.Single(item => item.Id == "LUI1012");
        Assert.AreEqual(
            "state.Draft = value",
            rejected.Substring(diagnostic.Span.Start, diagnostic.Span.Length)
        );
    }

    [TestMethod]
    public void NamedCallbacksCompileForCSharpAndComponentOwnedState()
    {
        const string api = """
namespace Sample;
public sealed class Model {
    public string Draft { get; set; } = "";
    public void SetDraft(string value) { Draft = value; }
}
""";
        var sources = new[]
        {
            "namespace Sample; using Lucent.Core; public component Editor(Model state) { <Field label=\"Draft\" editor={field => Lucent.Core.Components.TextField(field, () => state.Draft, value => state.Draft = value)} /> }",
            "namespace Sample; using Lucent.Core; public component Editor() { string draft = \"\"; void SetDraft(string value) { draft = value; } <Field label=\"Draft\" editor={field => Lucent.Core.Components.TextField(field, () => draft, value => draft = value)} /> }",
        };
        for (var index = 0; index < sources.Length; index++)
        {
            var source = sources[index];
            var assignment = index == 0 ? "state.Draft = value" : "draft = value";
            var result = Compile(source, api);
            var diagnostic = result.Diagnostics.Single(item => item.Id == "LUI1012");
            // The component method contains the same assignment before the island.
            Assert.AreEqual(
                new LuiSpan(
                    source.LastIndexOf(assignment, StringComparison.Ordinal),
                    assignment.Length
                ),
                diagnostic.Span
            );
            var fixedSource = source.Replace(
                "value => " + assignment,
                index == 0 ? "state.SetDraft" : "SetDraft",
                StringComparison.Ordinal
            );
            var fixedResult = Compile(fixedSource, api);
            Assert.IsTrue(
                fixedResult.Success,
                string.Join(" | ", fixedResult.Diagnostics.Select(item => item.Message))
            );
        }
    }

    private static LuiCompilationResult Compile(string source, string api)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(
                MetadataReference.CreateFromFile(
                    Path.Combine(AppContext.BaseDirectory, "Lucent.Core.dll")
                )
            );
        return LuiCompiler.Compile(
            LuiParser.Parse(source),
            CSharpCompilation.Create("callbacks", [CSharpSyntaxTree.ParseText(api)], references),
            new LuiFreshnessIdentity(
                "callbacks",
                "callbacks",
                new LuiDocumentIdentity("Editor.lui"),
                "v1",
                "preview"
            )
        );
    }
}
