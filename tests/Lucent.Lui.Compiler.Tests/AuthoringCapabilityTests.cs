using System.Reflection;
using Lucent.Core;
using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class AuthoringCapabilityTests
{
    [TestMethod]
    public void CapabilityFactoriesResolveFromSourceAndCompiledMetadata()
    {
        const string factory = """
namespace CapabilityLibrary;
using Lucent.Core;
public static class Factories
{
    [LucentComponent]
    public static AuthorRecipe<StyledCapability> Styled() => default;
    [LucentComponent]
    public static AuthorRecipe<AccessibleCapability> Accessible() => default;
    [LucentComponent]
    public static AuthorRecipe<StyledAccessibleCapability> Combined() => default;
    [LucentComponent]
    public static ComponentRecipe Group([DefaultContent] ComponentContent content) => default!;
}
""";
        var library = CSharpCompilation.Create(
            "capability-library",
            [CSharpSyntaxTree.ParseText(factory, new CSharpParseOptions(LanguageVersion.Preview))],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        AssertAccepted(library);
        using var image = new MemoryStream();
        var emitted = library.Emit(image);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        AssertAccepted(
            CSharpCompilation.Create(
                "capability-consumer",
                references: References().Append(MetadataReference.CreateFromImage(image.ToArray())),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            )
        );
    }

    [TestMethod]
    public void LookalikeAuthorRecipeFromAnotherAssemblyIdentityIsRejected()
    {
        const string lookalike = """
namespace Lucent.Core;
public sealed class AuthorRecipe<T> { }
public sealed class StyledCapability { }
""";
        var fakeCore = CSharpCompilation.Create(
            "Lucent.Core",
            [
                CSharpSyntaxTree.ParseText(
                    lookalike,
                    new CSharpParseOptions(LanguageVersion.Preview)
                ),
            ],
            PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        var sourceErrors = fakeCore
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.HasCount(
            0,
            sourceErrors,
            string.Join(
                Environment.NewLine,
                sourceErrors.Select(diagnostic => diagnostic.ToString())
            )
        );
        var lookalikeRecipe = fakeCore
            .GetTypeByMetadataName("Lucent.Core.AuthorRecipe`1")!
            .Construct(fakeCore.GetTypeByMetadataName("Lucent.Core.StyledCapability")!);
        var compilation = CSharpCompilation.Create(
            "identity-consumer",
            references: References(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        var returnShape = typeof(LuiCompiler).Assembly.GetType(
            "Lucent.Lui.Compiler.LuiComponentReturnShape",
            throwOnError: true
        )!;
        var tryGet = returnShape.GetMethod("TryGet", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] arguments = [compilation, lookalikeRecipe, null];

        Assert.AreEqual(false, tryGet.Invoke(null, arguments));
    }

    [TestMethod]
    public void UnsupportedCapabilityIsNotAComponentEvenWhenAnnotated()
    {
        const string factory = """
namespace CapabilityLibrary;
using Lucent.Core;
public static class Factories
{
    [LucentComponent]
    public static AuthorRecipe<AuthorCapability> Unsupported() => default;
}
""";
        var compilation = CSharpCompilation.Create(
            "unsupported-capability",
            [CSharpSyntaxTree.ParseText(factory, new CSharpParseOptions(LanguageVersion.Preview))],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        var result = Compile(compilation, "Unsupported");
        Assert.IsTrue(
            result.Diagnostics.Any(diagnostic =>
                diagnostic.Message.Contains("must resolve", StringComparison.Ordinal)
            ),
            string.Join(Environment.NewLine, result.Diagnostics)
        );
    }

    private static void AssertAccepted(CSharpCompilation compilation)
    {
        foreach (var name in new[] { "Styled", "Accessible", "Combined" })
        {
            var result = Compile(compilation, name);
            AssertGenerated(compilation, result);
        }
        AssertGenerated(
            compilation,
            CompileSource(
                compilation,
                """
namespace CapabilityConsumer;
using Lucent.Core;
using static CapabilityLibrary.Factories;
public component ContentConsumer() { <Group>{Styled()}</Group> }
"""
            )
        );
    }

    private static void AssertGenerated(CSharpCompilation compilation, LuiCompilationResult result)
    {
        Assert.AreEqual(
            0,
            result.Diagnostics.Count,
            string.Join(Environment.NewLine, result.Diagnostics)
        );
        var generated = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(
                result.Source!,
                new CSharpParseOptions(LanguageVersion.Preview)
            )
        );
        using var output = new MemoryStream();
        var emitted = generated.Emit(output);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
    }

    private static LuiCompilationResult Compile(CSharpCompilation compilation, string factory) =>
        CompileSource(
            compilation,
            $"namespace CapabilityConsumer; using Lucent.Core; using static CapabilityLibrary.Factories; public component Consumer() {{ <{factory} /> }}"
        );

    private static LuiCompilationResult CompileSource(
        CSharpCompilation compilation,
        string source
    ) =>
        LuiCompiler.Compile(
            LuiParser.Parse(source),
            compilation,
            new LuiFreshnessIdentity(
                "capability-probe",
                "capability-probe",
                new LuiDocumentIdentity("Consumer.lui"),
                "1",
                "preview"
            )
        );

    private static IEnumerable<MetadataReference> PlatformReferences() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

    private static IEnumerable<MetadataReference> References() =>
        PlatformReferences()
            .Append(MetadataReference.CreateFromFile(typeof(ComponentRecipe).Assembly.Location));
}
