using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lucent.ApplicationAuthoring.SdkHost;

public static class ProbeGenerationEngine
{
    public static ProbePreparationResult Prepare(
        CSharpCompilation compilation,
        IEnumerable<ISourceGenerator> generators,
        ImmutableArray<AdditionalText> additionalTexts,
        CSharpParseOptions parseOptions,
        AnalyzerConfigOptionsProvider optionsProvider,
        GeneratorDriverOptions driverOptions = default,
        ProbePreparationResult? previousResult = null,
        CancellationToken cancellationToken = default
    )
    {
        var generatorArray = generators.ToArray();
        var driver = previousResult is null
            ? CSharpGeneratorDriver.Create(
                generatorArray,
                additionalTexts,
                parseOptions,
                optionsProvider,
                driverOptions
            )
            : Reuse(previousResult, generatorArray, driverOptions)
                .WithUpdatedAnalyzerConfigOptions(optionsProvider)
                .ReplaceAdditionalTexts(additionalTexts)
                .WithUpdatedParseOptions(parseOptions);
        driver = driver.RunGenerators(compilation, cancellationToken);
        var run = driver.GetRunResult();
        var failures = run.Results.Where(result => result.Exception is not null).ToArray();
        if (failures.Length != 0)
            throw new InvalidOperationException(
                "Preparatory generator execution failed: "
                    + String.Join(
                        " | ",
                        failures.Select(result =>
                            $"{result.Generator.GetType().FullName}: {result.Exception!.Message}"
                        )
                    )
            );
        var outputs = run
            .Results.SelectMany(
                (result, generatorOrdinal) =>
                    result.GeneratedSources.Select(source => (generatorOrdinal, source))
            )
            .Select(item =>
            {
                var text = item.source.SourceText.ToString();
                return new ProbeGeneratedSource(
                    NormalizeIdentity(item.source.SyntaxTree.FilePath),
                    text,
                    Hash(text),
                    item.generatorOrdinal
                );
            })
            .ToImmutableArray();
        return new ProbePreparationResult(
            generatorArray.Length,
            run.Diagnostics,
            outputs,
            driver,
            compilation
        )
        {
            Generators = generatorArray.ToImmutableArray(),
            DriverOptions = driverOptions,
        };
    }

    private static GeneratorDriver Reuse(
        ProbePreparationResult previous,
        ISourceGenerator[] generators,
        GeneratorDriverOptions driverOptions
    )
    {
        if (
            previous.Generators.Length != generators.Length
            || previous
                .Generators.Where(
                    (generator, index) => !ReferenceEquals(generator, generators[index])
                )
                .Any()
        )
            throw new ArgumentException(
                "A previous driver can be reused only with the same ordered generator set.",
                nameof(generators)
            );
        if (!previous.DriverOptions.Equals(driverOptions))
            throw new ArgumentException(
                "A previous driver can be reused only with the same driver options.",
                nameof(driverOptions)
            );
        return previous.Driver;
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

public sealed record ProbePreparationResult(
    int GeneratorCount,
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<ProbeGeneratedSource> Outputs,
    GeneratorDriver Driver,
    CSharpCompilation InputCompilation
)
{
    internal ImmutableArray<ISourceGenerator> Generators { get; init; }

    internal GeneratorDriverOptions DriverOptions { get; init; }
}

public sealed record ProbeGeneratedSource(
    string Identity,
    string Source,
    string Sha256,
    int GeneratorOrdinal = -1
);

internal sealed record ProbeOutputMismatch(string[] Missing, string[] Extra, string[] Changed)
{
    internal bool IsMatch => Missing.Length == 0 && Extra.Length == 0 && Changed.Length == 0;
}
