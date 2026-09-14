using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lucent.ApplicationAuthoring.SdkHost;

internal static class ProbeGenerationEngine
{
    internal static ProbePreparationResult Prepare(
        CSharpCompilation compilation,
        IEnumerable<ISourceGenerator> generators,
        ImmutableArray<AdditionalText> additionalTexts,
        CSharpParseOptions parseOptions,
        AnalyzerConfigOptionsProvider optionsProvider
    )
    {
        var generatorArray = generators.ToArray();
        var driver = CSharpGeneratorDriver.Create(
            generatorArray,
            additionalTexts,
            parseOptions,
            optionsProvider
        );
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        var run = driver.GetRunResult();
        var outputs = run
            .Results.SelectMany(result => result.GeneratedSources)
            .Select(source =>
            {
                var text = source.SourceText.ToString();
                return new ProbeGeneratedSource(
                    NormalizeIdentity(source.SyntaxTree.FilePath),
                    text,
                    Hash(text)
                );
            })
            .ToImmutableArray();
        return new ProbePreparationResult(generatorArray.Length, run.Diagnostics, outputs);
    }

    internal static ProbeOutputMismatch Compare(
        IEnumerable<ManifestEntry> expected,
        IEnumerable<ManifestEntry> actual
    )
    {
        var expectedByIdentity = expected.ToDictionary(
            entry => entry.Identity,
            StringComparer.Ordinal
        );
        var actualByIdentity = actual.ToDictionary(entry => entry.Identity, StringComparer.Ordinal);
        var missing = expectedByIdentity
            .Keys.Except(actualByIdentity.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var extra = actualByIdentity
            .Keys.Except(expectedByIdentity.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var changed = expectedByIdentity
            .Keys.Intersect(actualByIdentity.Keys, StringComparer.Ordinal)
            .Where(identity =>
                expectedByIdentity[identity].Sha256 != actualByIdentity[identity].Sha256
            )
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new ProbeOutputMismatch(missing, extra, changed);
    }

    internal static string NormalizeIdentity(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    internal static string Hash(string source) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
}

internal sealed record ProbePreparationResult(
    int GeneratorCount,
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<ProbeGeneratedSource> Outputs
);

internal sealed record ProbeGeneratedSource(string Identity, string Source, string Sha256);

internal sealed record ProbeOutputMismatch(string[] Missing, string[] Extra, string[] Changed)
{
    internal bool IsMatch => Missing.Length == 0 && Extra.Length == 0 && Changed.Length == 0;
}
