using Lucent.Core;
using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class ProjectComponentIndexTests
{
    [TestMethod]
    public void DistinctCSharpOverloadDoesNotMaskSiblingDeclaration()
    {
        var compilation = Compilation(
            """
namespace Sample;
using Lucent.Core;
public static partial class Components
{
    [LucentComponent]
    public static ComponentRecipe A(int value) => null!;
}
"""
        );
        var document = Document(
            "namespace Sample; using Lucent.Core; public component A(string value) { <Text content={value} /> }"
        );
        var index = LuiProjectComponentIndex.Build(compilation, [document]);

        Assert.AreEqual(0, index.Diagnostics.Count);
        var methods = index
            .Augment(compilation, "C:/consumer/B.lui")
            .GetTypeByMetadataName("Sample.Components")!
            .GetMembers("A")
            .OfType<IMethodSymbol>()
            .ToArray();
        Assert.AreEqual(2, methods.Length);
        Assert.IsTrue(
            methods.Any(method =>
                method.Parameters.Single().Type.SpecialType == SpecialType.System_String
            ),
            "The distinct LUI sibling signature was masked by the existing C# overload."
        );
    }

    [TestMethod]
    public void ExactCSharpSignatureDoesNotDuplicateSiblingStub()
    {
        var compilation = Compilation(
            """
namespace Sample;
using Lucent.Core;
public static partial class Components
{
    [LucentComponent]
    public static ComponentRecipe A(string value) => null!;
}
"""
        );
        var document = Document(
            "namespace Sample; using Lucent.Core; public component A(string value) { <Text content={value} /> }"
        );
        var index = LuiProjectComponentIndex.Build(compilation, [document]);

        Assert.AreEqual(0, index.Diagnostics.Count);
        var methods = index
            .Augment(compilation, "C:/consumer/B.lui")
            .GetTypeByMetadataName("Sample.Components")!
            .GetMembers("A")
            .OfType<IMethodSymbol>()
            .ToArray();
        Assert.AreEqual(
            1,
            methods.Length,
            "An exact C# signature gained a duplicate sibling stub."
        );
    }

    private static LuiProjectDocument Document(string source) =>
        new("C:/consumer/A.lui", "A.lui", source, "1");

    private static CSharpCompilation Compilation(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(ComponentRecipe).Assembly.Location));
        return CSharpCompilation.Create(
            "component-index",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            references
        );
    }
}
