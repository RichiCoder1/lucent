using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

if (args.Length != 3)
{
    Console.Error.WriteLine(
        "Usage: IsolatedEmitter <json-generator> <project-emitter> <external-observer>"
    );
    return 2;
}

try
{
    var jsonGenerators = LoadGenerators(args[0]);
    var emitterGenerators = LoadGenerators(args[1]);
    var observerGenerators = LoadGenerators(args[2]);
    Check(jsonGenerators.Length > 0, "real JSON generator was not discovered");
    Check(emitterGenerators.Length == 1, "project emitter assembly did not expose one generator");
    Check(
        observerGenerators.Length == 1,
        "external observer assembly did not expose one generator"
    );

    var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
    var compilation = CSharpCompilation.Create(
        "IsolatedEmitterProof",
        [
            CSharpSyntaxTree.ParseText(
                "namespace Fixture; public static class ConsumerEntry { "
                    + "public static string Read() => Application.Create(); }",
                parseOptions,
                path: "Consumer.cs"
            ),
        ],
        TrustedPlatformReferences(),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
    );
    var authored = new ProbeText(
        "Model.lui",
        "future grammar model declaration; the emitter carries its early C# projection"
    );
    var authoredTexts = ImmutableArray.Create<AdditionalText>(authored);

    var preparation = Run(
        compilation,
        authoredTexts,
        emitterGenerators.AddRange(jsonGenerators).AddRange(observerGenerators)
    );
    AssertNoErrors(preparation.Diagnostics, "preparation");
    var jsonOutputs = FindJsonOutputs(preparation.Run);
    Check(
        jsonOutputs.Length > 0,
        "real JSON generator emitted no sources from emitter declarations"
    );

    var isolated = Run(
        compilation,
        authoredTexts,
        emitterGenerators.AddRange(jsonGenerators).AddRange(observerGenerators)
    );
    AssertNoErrors(isolated.Diagnostics, "isolated final generation");
    AssertNoErrors(isolated.Compilation.GetDiagnostics(), "isolated final compilation");
    Check(
        Matches(jsonOutputs, FindJsonOutputs(isolated.Run)),
        "real JSON output matches preparation without AdditionalFile transport"
    );
    Check(
        GeneratedSource(preparation.Run, "A0ExternalAdditionalFileObservation.g.cs")
            .SourceText.ToString()
            == GeneratedSource(isolated.Run, "A0ExternalAdditionalFileObservation.g.cs")
                .SourceText.ToString(),
        "external observer output matches between preparation and isolated final generation"
    );
    Check(
        isolated.Compilation.GetTypeByMetadataName("Fixture.Application") is not null,
        "emitter final implementation is present in the final compilation"
    );
    Check(
        isolated.Compilation.GetTypeByMetadataName("Fixture.ExternalObservation") is not null,
        "external observer output is present in the final compilation"
    );
    var expectedFingerprint = Fingerprint(authoredTexts);
    Check(
        Observation(isolated.Compilation, "Fixture.ExternalObservation", "Fingerprint")
            == expectedFingerprint,
        "external observer sees exactly the authored AdditionalText set"
    );

    var capturedBinding = jsonOutputs.Select(source =>
        (AdditionalText)
            new ProbeText(
                "Captured/" + source.HintName.Replace('/', '_') + ".binding.g.cs",
                source.SourceText.ToString()
            )
    );
    var transported = Run(
        compilation,
        authoredTexts.AddRange(capturedBinding),
        emitterGenerators.AddRange(jsonGenerators).AddRange(observerGenerators)
    );
    Check(
        transported.Diagnostics.Any(diagnostic => diagnostic.Id == "PROBE2001"),
        "old AdditionalFile transport is observable by the external generator"
    );
    Check(
        Observation(transported.Compilation, "Fixture.ExternalObservation", "Fingerprint")
            != expectedFingerprint,
        "transported binding sources change the external generator's input fingerprint"
    );
    Check(
        transported
            .Run.Results.SelectMany(result => result.GeneratedSources)
            .Any(source => source.HintName == "A0ProjectEmitter.Implementation.g.cs"),
        "transported run still receives the emitter implementation through AddSource"
    );

    Console.WriteLine(
        $"PASS: real JSON outputs={jsonOutputs.Length}; isolated AdditionalTexts=1; transported AdditionalTexts={authoredTexts.Length + capturedBinding.Count()}; emitter early declarations/final implementation are analyzer outputs."
    );
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static ImmutableArray<ISourceGenerator> LoadGenerators(string path)
{
    var loader = new ProofAssemblyLoader();
    var reference = new AnalyzerFileReference(Path.GetFullPath(path), loader);
    var failures = new List<string>();
    void OnLoadFailed(object? _, AnalyzerLoadFailureEventArgs failure) =>
        failures.Add(failure.Message);
    reference.AnalyzerLoadFailed += OnLoadFailed;
    try
    {
        var generators = reference.GetGenerators(LanguageNames.CSharp);
        if (failures.Count != 0)
            throw new InvalidOperationException(
                $"Analyzer '{path}' failed to load: {String.Join("; ", failures)}"
            );
        return generators;
    }
    finally
    {
        reference.AnalyzerLoadFailed -= OnLoadFailed;
    }
}

static ProofRun Run(
    Compilation compilation,
    ImmutableArray<AdditionalText> additionalTexts,
    ImmutableArray<ISourceGenerator> generators
)
{
    GeneratorDriver driver = CSharpGeneratorDriver.Create(
        generators,
        additionalTexts,
        compilation.SyntaxTrees.First().Options as CSharpParseOptions ?? CSharpParseOptions.Default,
        new EmptyOptionsProvider()
    );
    driver = driver.RunGeneratorsAndUpdateCompilation(
        compilation,
        out var updated,
        out var diagnostics
    );
    return new ProofRun(driver.GetRunResult(), updated, diagnostics);
}

static ImmutableArray<GeneratedSourceResult> FindJsonOutputs(GeneratorDriverRunResult run) =>
    run
        .Results.SelectMany(result => result.GeneratedSources)
        .Where(source =>
            !source.HintName.StartsWith("A0ProjectEmitter.", StringComparison.Ordinal)
            && !source.HintName.StartsWith(
                "A0ExternalAdditionalFileObservation.",
                StringComparison.Ordinal
            )
        )
        .ToImmutableArray();

static GeneratedSourceResult GeneratedSource(GeneratorDriverRunResult run, string hintName) =>
    run
        .Results.SelectMany(result => result.GeneratedSources)
        .Single(source => source.HintName == hintName);

static bool Matches(
    ImmutableArray<GeneratedSourceResult> expected,
    ImmutableArray<GeneratedSourceResult> actual
) => Fingerprints(expected).SequenceEqual(Fingerprints(actual));

static IEnumerable<string> Fingerprints(IEnumerable<GeneratedSourceResult> sources) =>
    sources
        .Select(source =>
            source.HintName
            + ":"
            + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(source.SourceText.ToString()))
            )
        )
        .Order(StringComparer.Ordinal);

static string Fingerprint(ImmutableArray<AdditionalText> texts) =>
    String.Join(
        "|",
        texts
            .Select(text =>
                Path.GetFileName(text.Path)
                + ":"
                + Convert.ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(
                            text.GetText(CancellationToken.None)?.ToString() ?? ""
                        )
                    )
                )
            )
            .Order(StringComparer.Ordinal)
    );

static string Observation(Compilation compilation, string typeName, string memberName)
{
    var type =
        compilation.GetTypeByMetadataName(typeName)
        ?? throw new InvalidOperationException($"Missing generated type {typeName}.");
    var field = type.GetMembers(memberName).OfType<IFieldSymbol>().Single();
    return field.ConstantValue as string
        ?? throw new InvalidOperationException(
            $"Generated member {typeName}.{memberName} is not a string constant."
        );
}

static void AssertNoErrors(IEnumerable<Diagnostic> diagnostics, string phase)
{
    var errors = diagnostics
        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        .ToArray();
    if (errors.Length != 0)
        throw new InvalidOperationException(
            $"{phase} produced errors: {String.Join(" | ", errors.Select(diagnostic => diagnostic.ToString()))}"
        );
}

static ImmutableArray<MetadataReference> TrustedPlatformReferences() =>
    ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
        ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToImmutableArray()
    ?? throw new InvalidOperationException(
        "The runtime did not provide trusted platform references."
    );

static void Check(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
}

sealed record ProofRun(
    GeneratorDriverRunResult Run,
    Compilation Compilation,
    ImmutableArray<Diagnostic> Diagnostics
);

sealed class ProbeText(string path, string value) : AdditionalText
{
    public override string Path => path;

    public override SourceText GetText(CancellationToken cancellationToken = default) =>
        SourceText.From(value, Encoding.UTF8);
}

sealed class EmptyOptionsProvider : AnalyzerConfigOptionsProvider
{
    private static readonly AnalyzerConfigOptions Empty = new EmptyOptions();

    public override AnalyzerConfigOptions GlobalOptions => Empty;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Empty;

    private sealed class EmptyOptions : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            value = String.Empty;
            return false;
        }
    }
}

sealed class ProofAssemblyLoader : IAnalyzerAssemblyLoader
{
    private readonly AssemblyLoadContext _loadContext = new(
        "a0-isolated-emitter",
        isCollectible: true
    );

    public void AddDependencyLocation(string fullPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(fullPath));
        if (directory is null)
            return;
        _loadContext.Resolving -= Resolve;
        _loadContext.Resolving += Resolve;

        Assembly? Resolve(AssemblyLoadContext _, AssemblyName name)
        {
            var candidate = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(candidate) ? _loadContext.LoadFromAssemblyPath(candidate) : null;
        }
    }

    public Assembly LoadFromPath(string fullPath) => _loadContext.LoadFromAssemblyPath(fullPath);
}
