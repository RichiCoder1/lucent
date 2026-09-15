using System.Collections.Immutable;

namespace Lucent.ApplicationAuthoring.PreparedSdk;

public sealed record PreparedSource(string HintName, string Source, string SourcePath);

public sealed record PreparedPayload(
    ImmutableArray<PreparedSource> EarlyDeclarations,
    ImmutableArray<PreparedSource> FinalSources
)
{
    public static PreparedPayload Create(
        IEnumerable<PreparedSource> earlyDeclarations,
        IEnumerable<PreparedSource> finalSources
    ) => new(earlyDeclarations.ToImmutableArray(), finalSources.ToImmutableArray());
}

public sealed record ForeignGeneratorIdentity(
    int PreparationOrdinal,
    string AnalyzerPath,
    string AnalyzerSha256,
    int GeneratorOrdinal,
    string GeneratorType
)
{
    public string StableIdentity =>
        AnalyzerSha256
        + "/"
        + GeneratorOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed record PreparedForeignOutput(
    string GeneratorIdentity,
    string HintIdentity,
    string Sha256
);

public sealed record PreparedManifest(
    string ProjectPath,
    string ProjectInputSha256,
    string EmitterSha256,
    ImmutableArray<ForeignGeneratorIdentity> ForeignGenerators,
    ImmutableArray<PreparedForeignOutput> ForeignOutputs,
    ImmutableArray<string> OriginalAdditionalFiles,
    ImmutableArray<string> AnalyzerConfigFiles,
    ImmutableArray<string> MetadataReferences,
    ImmutableArray<string> ProjectReferences
);
