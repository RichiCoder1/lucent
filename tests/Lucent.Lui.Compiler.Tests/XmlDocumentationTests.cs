using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Lucent.Core;
using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class XmlDocumentationTests
{
    [TestMethod]
    public void PublicComponentDocumentationIsPreservedInSourceAndMetadata()
    {
        const string source = """
namespace Sample;
using Lucent.Core;
using static Lucent.Core.Components;

/// <summary>Displays the supplied label.</summary>
/// <param name="label">The text shown by the component.</param>
/// <remarks>This documentation belongs to the generated recipe.</remarks>
public component Documented(string label) { <Text content={label} /> }
""";
        var compilation = CreateCompilation();
        var result = LuiCompiler.Compile(
            LuiParser.Parse(source),
            compilation,
            new LuiFreshnessIdentity(
                "xml-docs",
                "xml-docs",
                new LuiDocumentIdentity("Documented.lui"),
                "1",
                "preview"
            )
        );

        Assert.IsTrue(result.Success, Describe(result));
        var generated = result.Source!;
        StringAssert.Contains(generated, "    /// <summary>Displays the supplied label.</summary>");
        StringAssert.Contains(
            generated,
            "    /// <param name=\"label\">The text shown by the component.</param>"
        );
        StringAssert.Contains(
            generated,
            "    /// <remarks>This documentation belongs to the generated recipe.</remarks>"
        );
        var summaryStart = source.IndexOf("/// <summary>", StringComparison.Ordinal);
        Assert.IsTrue(
            result.Map.Entries.Any(entry =>
                entry.Source.Start == summaryStart
                && entry.Source.Length > 0
                && entry.Generated.Length > 0
                && entry.Kind == LuiMapKind.Structure
                && !entry.Hidden
            ),
            "documentation was emitted without an authored source-map relation."
        );

        using var assembly = new MemoryStream();
        using var xml = new MemoryStream();
        var emitted = compilation
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated, ParseOptions()))
            .Emit(assembly, xmlDocumentationStream: xml);
        Assert.IsTrue(
            emitted.Success,
            string.Join(
                Environment.NewLine,
                emitted.Diagnostics.Select(diagnostic => diagnostic.ToString())
            )
        );

        var assemblyImage = assembly.ToArray();
        var documentationImage = xml.ToArray();
        var documentation = Encoding.UTF8.GetString(documentationImage);
        StringAssert.Contains(
            documentation,
            "<member name=\"M:Sample.Components.Documented(System.String)\">"
        );
        StringAssert.Contains(documentation, "Displays the supplied label.");
        StringAssert.Contains(documentation, "The text shown by the component.");
        StringAssert.Contains(documentation, "This documentation belongs to the generated recipe.");

        var packageReference = MetadataReference.CreateFromImage(
            assemblyImage,
            documentation: new XmlDocumentationProvider(documentationImage)
        );
        var consumerCompilation = CSharpCompilation.Create(
            "consumer",
            [CSharpSyntaxTree.ParseText("internal static class Consumer { }")],
            CreateCompilation().References.Append(packageReference),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                generalDiagnosticOption: ReportDiagnostic.Error
            )
        );
        var packagedMethod = consumerCompilation
            .GetTypeByMetadataName("Sample.Components")!
            .GetMembers("Documented")
            .OfType<IMethodSymbol>()
            .Single();
        var packagedDocumentation = packagedMethod.GetDocumentationCommentXml();
        StringAssert.Contains(packagedDocumentation, "Displays the supplied label.");
        StringAssert.Contains(packagedDocumentation, "The text shown by the component.");
        StringAssert.Contains(
            packagedDocumentation,
            "This documentation belongs to the generated recipe."
        );

        var indexedDocument = new LuiProjectDocument(
            "C:/consumer/Documented.lui",
            "Documented.lui",
            source,
            "1"
        );
        var index = LuiProjectComponentIndex.Build(compilation, [indexedDocument]);
        Assert.AreEqual(0, index.Diagnostics.Count);
        var toolingCompilation = index.Augment(compilation, "C:/consumer/Other.lui");
        var toolingMethod = toolingCompilation
            .GetTypeByMetadataName("Sample.Components")!
            .GetMembers("Documented")
            .OfType<IMethodSymbol>()
            .Single();
        StringAssert.Contains(
            toolingMethod.GetDocumentationCommentXml(),
            "Displays the supplied label."
        );
    }

    [TestMethod]
    public void OrdinaryCommentsBreakDocumentationAssociation()
    {
        const string source = """
namespace Sample;
using Lucent.Core;

/// <summary>This comment must not attach.</summary>
// An ordinary comment separates the documentation from the declaration.
internal component Undocumented() { <Row /> }
""";
        var result = LuiCompiler.Compile(
            LuiParser.Parse(source),
            CreateCompilation(),
            new LuiFreshnessIdentity(
                "xml-docs",
                "xml-docs",
                new LuiDocumentIdentity("Undocumented.lui"),
                "1",
                "preview"
            )
        );

        Assert.IsTrue(result.Success, Describe(result));
        Assert.IsFalse(
            result.Source!.Contains("This comment must not attach.", StringComparison.Ordinal),
            "a documentation comment separated by an ordinary comment was emitted."
        );
    }

    private static CSharpCompilation CreateCompilation()
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(ComponentRecipe).Assembly.Location));
        return CSharpCompilation.Create(
            "xml_documentation",
            [CSharpSyntaxTree.ParseText("internal static class Probe { }", ParseOptions())],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                generalDiagnosticOption: ReportDiagnostic.Error
            )
        );
    }

    private static CSharpParseOptions ParseOptions() =>
        new(LanguageVersion.Preview, DocumentationMode.Diagnose);

    private static string Describe(LuiCompilationResult result) =>
        string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message)
        );

    private sealed class XmlDocumentationProvider : DocumentationProvider
    {
        private readonly string text;
        private readonly XDocument document;

        public XmlDocumentationProvider(byte[] xml)
        {
            text = Encoding.UTF8.GetString(xml);
            document = XDocument.Parse(text);
        }

        public override bool Equals(object? obj) =>
            obj is XmlDocumentationProvider other
            && String.Equals(text, other.text, StringComparison.Ordinal);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(text);

        protected override string? GetDocumentationForSymbol(
            string documentationMemberID,
            CultureInfo preferredCulture,
            CancellationToken cancellationToken
        ) =>
            document
                .Root?.Element("members")
                ?.Elements("member")
                .FirstOrDefault(member =>
                    String.Equals(
                        member.Attribute("name")?.Value,
                        documentationMemberID,
                        StringComparison.Ordinal
                    )
                )
                ?.ToString(SaveOptions.DisableFormatting);
    }
}
