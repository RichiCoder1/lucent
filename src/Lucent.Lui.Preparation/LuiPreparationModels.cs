using System.Collections.Immutable;
using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lucent.Lui.Preparation;

public sealed record LuiPreparationDocument(
    string PhysicalPath,
    string LogicalPath,
    string Source,
    string Version
);

public sealed record LuiPreparationGenerator(
    string StableIdentity,
    string AnalyzerPath,
    string AnalyzerSha256,
    int AnalyzerGeneratorOrdinal,
    ISourceGenerator Generator
);

public sealed record LuiPreparationRequest(
    CSharpCompilation Compilation,
    ImmutableArray<LuiPreparationDocument> Documents,
    ImmutableArray<LuiPreparationGenerator> ForeignGenerators,
    ImmutableArray<AdditionalText> AdditionalTexts,
    CSharpParseOptions ParseOptions,
    AnalyzerConfigOptionsProvider OptionsProvider,
    string ProjectEpoch,
    string ProjectIdentity,
    string LanguageVersion,
    string CompilerOptions,
    string Defines,
    string RootNamespace,
    LuiPreparationDriverState? PreviousDriverState = null,
    GeneratorDriverOptions DriverOptions = default
);

public sealed record LuiPreparedSource(string HintName, string Source, string SourcePath);

public sealed record LuiPreparedPayload(
    ImmutableArray<LuiPreparedSource> EarlyDeclarations,
    ImmutableArray<LuiPreparedSource> FinalSources
);

public sealed record LuiPreparedForeignOutput(
    string GeneratorIdentity,
    string HintIdentity,
    string Sha256
);

public sealed record LuiPreparationResult(
    bool Success,
    LuiPreparedPayload Payload,
    ImmutableArray<LuiPreparedForeignOutput> ForeignOutputs,
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    ImmutableArray<LuiPreparationDiagnostic> LuiDiagnostics,
    CSharpCompilation BindingCompilation,
    ImmutableArray<LuiPreparedDocumentResult> Documents,
    LuiPreparationDriverState? DriverState
);

public sealed record LuiPreparationDiagnostic(string PhysicalPath, LuiDiagnostic Diagnostic);

public sealed record LuiPreparedDocumentResult(
    LuiPreparationDocument Document,
    LuiAuthoredSourceProjection Projection,
    LuiCompilationResult? Compilation
);

public sealed class LuiPreparationDriverState
{
    internal LuiPreparationDriverState(
        GeneratorDriver driver,
        ImmutableArray<ISourceGenerator> generators,
        ImmutableArray<string> foreignGeneratorIdentities,
        ImmutableArray<LuiPreparedSource> earlySources,
        GeneratorDriverOptions options
    )
    {
        Driver = driver;
        Generators = generators;
        ForeignGeneratorIdentities = foreignGeneratorIdentities;
        EarlySources = earlySources;
        Options = options;
    }

    internal GeneratorDriver Driver { get; }

    internal ImmutableArray<ISourceGenerator> Generators { get; }

    internal ImmutableArray<string> ForeignGeneratorIdentities { get; }

    internal ImmutableArray<LuiPreparedSource> EarlySources { get; }

    internal GeneratorDriverOptions Options { get; }
}

public sealed class LuiPreparationException : Exception
{
    public LuiPreparationException(string message)
        : base(message) { }
}

public sealed record LuiPreparedEmitterArtifact(
    string Path,
    string AssemblyName,
    string ContentHash,
    string ImageSha256
);
