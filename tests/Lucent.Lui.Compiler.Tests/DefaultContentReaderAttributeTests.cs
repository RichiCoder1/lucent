using Lucent.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class DefaultContentReaderAttributeTests
{
    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, true, false)]
    [DataRow(false, false, true)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [DataRow(true, false, true)]
    public void CommandButtonContentBindsWithImplicitAndExplicitIconReaders(
        bool named,
        bool expressionContent,
        bool explicitReader
    )
    {
        var icon = explicitReader ? "() => icon" : "icon";
        var content = expressionContent ? "{label}" : "Apply";
        var source =
            "namespace Sample; using Lucent.Core; internal component Probe(ApplicationCommand command, ImageSource icon, string label) { <Button command={command} leadingIcon={"
            + icon
            + "}>"
            + content
            + "</Button> }";
        var projection = LuiAuthoredSourceProjection.Project(source);
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "default-content-readers",
            named
                ? [CSharpSyntaxTree.ParseText(projection.EarlyComponentDeclaration!, parseOptions)]
                : [],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Append(typeof(ComponentRecipe).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        var identity = new LuiFreshnessIdentity(
            "reader-content",
            "reader-content",
            new LuiDocumentIdentity("Probe.lui"),
            "v1",
            "preview"
        );
        var result = named
            ? LuiCompiler.CompileNamedComponent(
                projection.Document,
                compilation,
                identity,
                "Probe.lui"
            )
            : LuiCompiler.Compile(projection.Document, compilation, identity);
        Assert.IsTrue(
            result.Success,
            string.Join(" | ", result.Diagnostics.Select(item => item.Id + ": " + item.Message))
        );
        var generated = CSharpSyntaxTree.ParseText(result.Source!, parseOptions);
        var output = compilation.AddSyntaxTrees(generated);
        Assert.IsFalse(
            output.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error),
            string.Join(" | ", output.GetDiagnostics())
        );
        var invocation = generated
            .GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(item =>
                item.Expression
                    is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Button" }
            );
        var method = (IMethodSymbol)
            output.GetSemanticModel(generated).GetSymbolInfo(invocation).Symbol!;
        Assert.AreEqual("Lucent.Core.Components", method.ContainingType.ToDisplayString());
        Assert.AreEqual(
            expressionContent ? "System.Func<string>" : "string",
            method.Parameters.Single(item => item.Name == "content").Type.ToDisplayString()
        );
        Assert.IsInstanceOfType<LambdaExpressionSyntax>(
            invocation
                .ArgumentList.Arguments.Single(item =>
                    item.NameColon?.Name.Identifier.ValueText == "leadingIcon"
                )
                .Expression
        );
        var iconOffset =
            source.IndexOf("leadingIcon={", StringComparison.Ordinal) + "leadingIcon={".Length;
        Assert.IsTrue(
            result
                .Map.FromSource(new LuiSpan(iconOffset, icon.Length))
                .Any(entry =>
                    !entry.Hidden
                    && entry.Source.Start == iconOffset
                    && entry.Source.Length == icon.Length
                    && result.Source!.Substring(entry.Generated.Start, entry.Generated.Length)
                        == icon
                )
        );
    }
}
