using System;
using System.IO;
using System.Linq;
using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class LintPolicyTests
{
    private const string ComponentLibrary = """
namespace Sample;
using System;
using System.Collections.Generic;
using Lucent.Core;
public static class Custom
{
    [LucentComponent]
    public static ComponentRecipe Caption([DefaultContent] string label, int tone = 0) => null!;

    [LucentComponent]
    public static ComponentRecipe Live([DefaultContent] Func<string> label, int tone = 0) => null!;

    [LucentComponent]
    public static ComponentRecipe Slot([DefaultContent] ComponentContent children) => null!;

    [LucentComponent]
    public static ComponentRecipe Values([DefaultContent] IReadOnlyList<string> values) => null!;

    [LucentComponent]
    public static ComponentRecipe Ordinary(string content) => null!;

    [LucentComponent]
    public static ComponentRecipe Generic<T>([DefaultContent] T value) => null!;
}
""";

    [TestMethod]
    public void DefaultContentUsesMetadataAndOffersOnlyAReboundEquivalentFix()
    {
        const string source = """
namespace Sample;
using System;
using Lucent.Core;
using static Sample.Custom;
internal component Example(ComponentContent forwarded, string text) {
    <Column>
        <Caption tone={1} label="Hello" />
        <Ordinary content="Keep named" />
        <Slot children={forwarded} />
        <Values values={["one", "two"]} />
        <Caption label={null} />
        <Caption label={text} tone={2} />
    </Column>
}
""";
        var result = Analyze(source);
        var placements = result
            .Diagnostics.Where(item => item.Id == LuiLintCatalog.DefaultContentPlacement)
            .ToArray();

        Assert.AreEqual(LuiLintAnalysisStatus.Complete, result.Status, Diagnostics(result));
        Assert.HasCount(1, placements, Diagnostics(result));
        Assert.AreEqual(
            source.IndexOf("label=\"Hello\"", StringComparison.Ordinal),
            placements[0].Span.Start
        );
        Assert.HasCount(1, result.Fixes);

        Assert.Throws<InvalidOperationException>(() =>
            result.Fixes[0].Apply(source.Replace("Hello", "Jello", StringComparison.Ordinal))
        );

        var fixedSource = result.Fixes[0].Apply(source);
        StringAssert.Contains(fixedSource, "<Caption tone={1}>Hello</Caption>");
        var rebound = Analyze(fixedSource);
        Assert.AreEqual(LuiLintAnalysisStatus.Complete, rebound.Status, Diagnostics(rebound));
        Assert.IsFalse(
            rebound.Diagnostics.Any(item => item.Id == LuiLintCatalog.DefaultContentPlacement),
            Diagnostics(rebound)
        );
    }

    [TestMethod]
    public void ExplicitLiveReaderMovesOnlyWhenGeneratedBindingRemainsEquivalent()
    {
        const string source = """
namespace Sample;
using System;
using Lucent.Core;
using static Sample.Custom;
internal component Example(string text) {
    <Live tone={1} label={() => text} />
}
""";
        var result = Analyze(source);

        Assert.AreEqual(LuiLintAnalysisStatus.Complete, result.Status, Diagnostics(result));
        Assert.IsTrue(
            result.Diagnostics.Any(item => item.Id == LuiLintCatalog.DefaultContentPlacement),
            Diagnostics(result)
        );
        Assert.HasCount(1, result.Fixes);
        var fixedSource = result.Fixes[0].Apply(source);
        StringAssert.Contains(fixedSource, "<Live tone={1}>{() => text}</Live>");
        Assert.AreEqual(LuiLintAnalysisStatus.Complete, Analyze(fixedSource).Status);
    }

    [TestMethod]
    public void ConstructedGenericTargetAndConversionsSurviveTheProvenFix()
    {
        const string source = """
namespace Sample;
using Lucent.Core;
using static Sample.Custom;
internal component Example() {
    <Generic value={1} />
}
""";
        var result = Analyze(source);

        Assert.AreEqual(LuiLintAnalysisStatus.Complete, result.Status, Diagnostics(result));
        Assert.HasCount(1, result.Fixes);
        var fixedSource = result.Fixes[0].Apply(source);
        StringAssert.Contains(fixedSource, "<Generic>{1}</Generic>");
        var rebound = Analyze(fixedSource);
        Assert.AreEqual(LuiLintAnalysisStatus.Complete, rebound.Status, Diagnostics(rebound));
        Assert.IsFalse(
            rebound.Diagnostics.Any(item => item.Id == LuiLintCatalog.DefaultContentPlacement)
        );
    }

    [TestMethod]
    public void ScopedReasonedSuppressionsAttachToTheNextCompleteConstruct()
    {
        const string source = """
namespace Sample;
using System;
using System.Collections.Generic;
using Lucent.Core;
using static Sample.Custom;
internal component Example(IEnumerable<string> items) {
    <Column>
        // lui-lint-disable-next LUI5001: External identity fixture.
        foreach (var item in items) keyed by Guid.NewGuid() { <Text>{item}</Text> }
        // lui-lint-disable-next LUI5003: This fixture demonstrates named content.
        <Caption label="suppressed" />
        <Caption label="diagnosed" />
    </Column>
}
// lui-lint-disable-next LUI5002: Documentation specimen uses this style by name.
style Unused { Spacing: 1f; }
""";
        var result = Analyze(source);

        Assert.AreEqual(LuiLintAnalysisStatus.Complete, result.Status, Diagnostics(result));
        Assert.IsFalse(result.Diagnostics.Any(item => item.Id == LuiLintCatalog.UnstableKey));
        Assert.IsFalse(
            result.Diagnostics.Any(item => item.Id == LuiLintCatalog.UnusedPrivateStyle)
        );
        var placement = result.Diagnostics.Single(item =>
            item.Id == LuiLintCatalog.DefaultContentPlacement
        );
        Assert.AreEqual(
            source.IndexOf("label=\"diagnosed\"", StringComparison.Ordinal),
            placement.Span.Start
        );
        Assert.HasCount(1, result.Fixes);
        Assert.AreEqual(placement.Span.Start, result.Fixes[0].DiagnosticSpan.Start);
        var fixedSource = result.Fixes[0].Apply(source);
        StringAssert.Contains(fixedSource, "<Caption label=\"suppressed\" />");
        StringAssert.Contains(fixedSource, "<Caption>diagnosed</Caption>");

        const string invalidSource = """
namespace Sample;
using Lucent.Core;
using static Sample.Custom;
internal component Invalid() {
    string MarkerText() => "// lui-lint-disable-next LUI5003: String literal.";
    void UnsupportedMarker() {
        // lui-lint-disable-next LUI5003: Member-local markers cannot target LUI constructs.
    }
    // lui-lint-disable-next *: Broad suppression is intentionally invalid.
    <Caption label="still checked" />
}
""";
        var invalid = Analyze(invalidSource);
        Assert.HasCount(
            2,
            invalid.Diagnostics.Where(item => item.Id == LuiLintCatalog.InvalidSuppression)
        );
        Assert.IsTrue(
            invalid.Diagnostics.Any(item => item.Id == LuiLintCatalog.DefaultContentPlacement),
            Diagnostics(invalid)
        );

        const string islandSource = """
namespace Sample;
using Lucent.Core;
using static Sample.Custom;
internal component Island(string text) {
    <Caption label={"// lui-lint-disable-next LUI5003: String literal." + text // lui-lint-disable-next LUI5003: Misplaced.
    } />
}
""";
        var island = Analyze(islandSource);
        Assert.HasCount(
            1,
            island.Diagnostics.Where(item => item.Id == LuiLintCatalog.InvalidSuppression)
        );
    }

    [TestMethod]
    public void DeclarationOrderIsOptInAndSemanticFailureIsNeverReportedClean()
    {
        const string source = """
namespace Sample;
using Lucent.Core;
internal component Example() { <Text>Ready</Text> }
style Later { Spacing: 1f; }
""";
        var none = Analyze(source);
        var stylesFirst = Analyze(source, new LuiLintOptions(LuiDeclarationOrder.StylesFirst));
        var componentFirst = Analyze(
            source,
            new LuiLintOptions(LuiDeclarationOrder.ComponentFirst)
        );

        Assert.IsFalse(none.Diagnostics.Any(item => item.Id == LuiLintCatalog.DeclarationOrder));
        Assert.IsTrue(
            stylesFirst.Diagnostics.Any(item => item.Id == LuiLintCatalog.DeclarationOrder),
            Diagnostics(stylesFirst)
        );
        Assert.IsFalse(
            componentFirst.Diagnostics.Any(item => item.Id == LuiLintCatalog.DeclarationOrder),
            Diagnostics(componentFirst)
        );

        const string unavailableSource = """
namespace Sample;
using Lucent.Core;
internal component Broken() { <MissingControl /> }
""";
        var unavailable = Analyze(unavailableSource);
        Assert.AreEqual(LuiLintAnalysisStatus.Unavailable, unavailable.Status);
        Assert.IsTrue(
            unavailable.Diagnostics.Any(item => item.Id == LuiLintCatalog.AnalysisUnavailable),
            Diagnostics(unavailable)
        );
        Assert.HasCount(0, unavailable.Fixes);

        var compilation = Compilation();
        var parsed = LuiParser.Parse(source);
        var identity = Identity();
        var compiled = LuiCompiler.Compile(parsed, compilation, identity);
        var stale = LuiLintAnalyzer.AnalyzeCompiled(
            parsed,
            compilation,
            new LuiFreshnessIdentity(
                identity.ProjectEpoch,
                identity.ProjectIdentity,
                identity.Document,
                "v2",
                identity.Options
            ),
            compiled
        );
        Assert.AreEqual(LuiLintAnalysisStatus.Unavailable, stale.Status);
        Assert.IsTrue(
            stale.Diagnostics.Any(item => item.Id == LuiLintCatalog.AnalysisUnavailable),
            Diagnostics(stale)
        );
    }

    private static LuiLintResult Analyze(string source, LuiLintOptions? options = null)
    {
        return LuiLintAnalyzer.Analyze(LuiParser.Parse(source), Compilation(), Identity(), options);
    }

    private static CSharpCompilation Compilation() =>
        CSharpCompilation.Create(
            "lint-policy",
            [
                CSharpSyntaxTree.ParseText(
                    ComponentLibrary,
                    new CSharpParseOptions(LanguageVersion.Preview)
                ),
            ],
            References()
        );

    private static LuiFreshnessIdentity Identity() =>
        new LuiFreshnessIdentity(
            "lint-policy",
            "lint-policy",
            new LuiDocumentIdentity("LintPolicy.lui"),
            "v1",
            "preview"
        );

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

    private static string Diagnostics(LuiLintResult result) =>
        string.Join(
            " | ",
            result.Diagnostics.Select(item => item.Id + "@" + item.Span.Start + ": " + item.Message)
        );
}
