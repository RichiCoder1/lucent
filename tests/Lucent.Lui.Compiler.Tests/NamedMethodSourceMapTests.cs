using Lucent.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class NamedMethodSourceMapTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NamedMethodRewritePreservesExactIdentifierMaps(bool incomplete)
    {
        var source = """
            namespace Sample;
            using Lucent.Core;
            public component Card() {
                int Count = 1;
                string Describe( string prefix = "value" ) => prefix + Count.ToString();
                <Text>{Describe()}</Text>
            }
            """;
        if (incomplete)
            source = source.Replace(
                "prefix + Count.ToString()",
                "prefix.",
                StringComparison.Ordinal
            );
        var projection = LuiAuthoredSourceProjection.Project(source);
        Assert.IsNotNull(projection.EarlyComponentDeclaration);
        var compilation = CSharpCompilation.Create(
            "named-method-map",
            [
                CSharpSyntaxTree.ParseText(
                    projection.EarlyComponentDeclaration,
                    new CSharpParseOptions(LanguageVersion.Preview),
                    "Card.lui.early.g.cs"
                ),
            ],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Append(typeof(ComponentRecipe).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(static path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        var result = LuiCompiler.CompileNamedComponent(
            projection.Document,
            compilation,
            new LuiFreshnessIdentity(
                "named-method-map",
                "named-method-map",
                new LuiDocumentIdentity("Card.lui"),
                "1",
                "preview"
            ),
            "Card.lui"
        );

        Assert.AreEqual(
            !incomplete,
            result.Success,
            String.Join(" | ", result.Diagnostics.Select(item => item.Id + ": " + item.Message))
        );
        var methodStart = source.IndexOf("string Describe", StringComparison.Ordinal);
        AssertExactToken("Describe", methodStart);
        AssertExactToken("prefix", methodStart);
        AssertExactToken("prefix", source.IndexOf("=>", methodStart, StringComparison.Ordinal));
        if (!incomplete)
        {
            AssertExactToken("Count", methodStart);
            AssertExactToken("ToString", methodStart);
        }
        else
        {
            var expected = new LuiSpan(
                source.IndexOf("prefix.;", StringComparison.Ordinal) + "prefix.".Length,
                1
            );
            Assert.IsTrue(
                result.Diagnostics.Any(item => item.Id == "LUI2000" && item.Span.Equals(expected)),
                string.Join(
                    " | ",
                    result.Diagnostics.Select(item =>
                        item.Id + "@" + item.Span.Start + ":" + item.Span.Length
                    )
                )
            );
        }

        void AssertExactToken(string token, int start)
        {
            var authored = source.IndexOf(token, start, StringComparison.Ordinal);
            var entry = result
                .Map.FromSource(new LuiSpan(authored, token.Length))
                .SingleOrDefault(item =>
                    !item.Hidden
                    && item.Kind == LuiMapKind.Symbol
                    && item.Source.Equals(new LuiSpan(authored, token.Length))
                    && item.Generated.Length == token.Length
                    && result
                        .ProjectionSource.AsSpan(item.Generated.Start, item.Generated.Length)
                        .SequenceEqual(token.AsSpan())
                );
            Assert.IsNotNull(entry, $"'{token}' at {authored} did not retain an exact symbol map.");
        }
    }
}
