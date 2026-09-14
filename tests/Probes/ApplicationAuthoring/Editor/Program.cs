using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucent.ApplicationAuthoring.SdkHost;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

if (args.Length != 4)
    return Fail("Usage: EditorProbe <fixture-project> <model-path> <fixture-obj> <artifact-root>");

var fixtureProject = Path.GetFullPath(args[0]);
var modelPath = Path.GetFullPath(args[1]);
var fixtureObjectPath = Path.GetFullPath(args[2]) + Path.DirectorySeparatorChar;
var artifactRoot = Path.GetFullPath(args[3]);
var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["Configuration"] = "Release",
    ["TargetFramework"] = "net10.0",
    ["ProbeModelPath"] = modelPath,
    ["BaseIntermediateOutputPath"] = fixtureObjectPath,
    ["MSBuildProjectExtensionsPath"] = fixtureObjectPath,
    ["ProjectAssetsFile"] = Path.Combine(fixtureObjectPath, "project.assets.json"),
    ["BaseOutputPath"] = Path.Combine(artifactRoot, "fixture-bin") + Path.DirectorySeparatorChar,
};

if (!MSBuildLocator.IsRegistered)
    MSBuildLocator.RegisterDefaults();
using var workspace = MSBuildWorkspace.Create(properties);
var workspaceDiagnostics = new ConcurrentQueue<WorkspaceDiagnostic>();
using var registration = workspace.RegisterWorkspaceFailedHandler(failure =>
{
    workspaceDiagnostics.Enqueue(failure.Diagnostic);
    Console.Error.WriteLine(
        $"EDITOR0001: workspace {failure.Diagnostic.Kind}: {failure.Diagnostic.Message}"
    );
});
var project = await workspace.OpenProjectAsync(fixtureProject).ConfigureAwait(false);
Assert(
    !workspaceDiagnostics.Any(diagnostic => diagnostic.Kind == WorkspaceDiagnosticKind.Failure),
    "EDITOR0002: evaluated workspace reported a failure"
);
var model = project.AdditionalDocuments.Single(document => SamePath(document.FilePath, modelPath));
var diskSource = File.ReadAllText(modelPath);
var initialVersion = await model.GetTextVersionAsync().ConfigureAwait(false);
var (externalGenerators, externalGeneratorReferences) = LoadJsonGenerators(project);
var generators = new ISourceGenerator[]
{
    new EditorProjectionGenerator().AsSourceGenerator(),
    new OptionsEchoGenerator().AsSourceGenerator(),
}
    .Concat(externalGenerators)
    .ToArray();
var driverOptions = new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true, null);

var coldWatch = Stopwatch.StartNew();
var initial = await PrepareAsync(project, generators, driverOptions).ConfigureAwait(false);
coldWatch.Stop();
AssertValid(initial, modelPath, "Name");
var initialJson = JsonOutputs(initial).ToArray();
Assert(
    initialJson.Length == 5,
    $"EDITOR0003: expected five JSON outputs, got {initialJson.Length}"
);

var sameWatch = Stopwatch.StartNew();
var same = await PrepareAsync(project, generators, driverOptions, initial).ConfigureAwait(false);
sameWatch.Stop();
Assert(
    initial.Outputs.Select(OutputIdentity).SequenceEqual(same.Outputs.Select(OutputIdentity)),
    "EDITOR0004: unchanged snapshot changed generated output identity/content"
);
var sameCachedSteps = CountSteps(same.Driver, IncrementalStepRunReason.Cached);
Assert(sameCachedSteps > 0, "EDITOR0005: reused driver reported no cached incremental steps");

var editedSource = diskSource.Replace("string Name", "string Title", StringComparison.Ordinal);
var editedSolution = project.Solution.WithAdditionalDocumentText(
    model.Id,
    SourceText.From(editedSource)
);
var editedProject = editedSolution.GetProject(project.Id)!;
var editedModel = editedProject.GetAdditionalDocument(model.Id)!;
var editedVersion = await editedModel.GetTextVersionAsync().ConfigureAwait(false);
Assert(
    editedVersion != initialVersion,
    "EDITOR0006: unsaved edit retained the old document version"
);
Assert(
    File.ReadAllText(modelPath) == diskSource,
    "EDITOR0007: in-memory edit unexpectedly wrote the fixture"
);
var editWatch = Stopwatch.StartNew();
var edited = await PrepareAsync(editedProject, generators, driverOptions, same)
    .ConfigureAwait(false);
editWatch.Stop();
AssertValid(edited, modelPath, "Title");
Assert(
    JsonOutputs(edited)
        .Any(output => output.Source.Contains("\"Title\"", StringComparison.Ordinal)),
    "EDITOR0008: unsaved property edit did not invalidate JSON output"
);
Assert(
    !JsonOutputs(edited)
        .Any(output => output.Source.Contains("\"Name\"", StringComparison.Ordinal)),
    "EDITOR0009: unsaved property edit retained stale JSON output"
);

var deletedProject = editedProject
    .Solution.RemoveAdditionalDocument(model.Id)
    .GetProject(project.Id)!;
var deleted = await PrepareAsync(deletedProject, generators, driverOptions, edited)
    .ConfigureAwait(false);
Assert(JsonOutputs(deleted).Count == 0, "EDITOR0010: deletion retained generated JSON sources");
Assert(
    !deleted.Outputs.Any(output =>
        output.Source.Contains("EDITOR_PROJECTION_OUTPUT", StringComparison.Ordinal)
    ),
    "EDITOR0026: deletion retained the projected declaration"
);

var renamedPath = Path.Combine(Path.GetDirectoryName(modelPath)!, "Renamed.lui");
var renamedId = DocumentId.CreateNewId(project.Id, "Renamed.lui");
var renamedSolution = deletedProject.Solution.AddAdditionalDocument(
    renamedId,
    "Renamed.lui",
    SourceText.From(editedSource),
    filePath: renamedPath
);
var renamedProject = renamedSolution.GetProject(project.Id)!;
var renamed = await PrepareAsync(renamedProject, generators, driverOptions, deleted)
    .ConfigureAwait(false);
AssertValid(renamed, renamedPath, "Title");
Assert(
    renamed.Outputs.Any(output =>
        output.Identity.Contains(
            EditorProjectionGenerator.PathHash(renamedPath),
            StringComparison.Ordinal
        )
    ),
    "EDITOR0011: rename did not change deterministic projection identity"
);

var publisher = new FreshnessPublisher();
var oldEpoch = publisher.Begin();
using var cancellation = new CancellationTokenSource();
var gate = new CancellableGateGenerator();
var rawEdited = await RawCompilationAsync(editedProject).ConfigureAwait(false);
var canceledRun = Task.Run(() =>
    ProbeGenerationEngine.Prepare(
        rawEdited,
        [gate.AsSourceGenerator()],
        CurrentAdditionalTexts(editedProject),
        ParseOptions(editedProject),
        editedProject.AnalyzerOptions.AnalyzerConfigOptionsProvider,
        cancellationToken: cancellation.Token
    )
);
Assert(
    gate.Started.Wait(TimeSpan.FromSeconds(10)),
    "EDITOR0012: cancellable generator did not start"
);
var newEpoch = publisher.Begin();
cancellation.Cancel();
gate.Release.Set();
try
{
    var stale = await canceledRun.ConfigureAwait(false);
    publisher.TryPublish(oldEpoch, stale);
    throw new InvalidOperationException("EDITOR0013: canceled generation completed normally");
}
catch (OperationCanceledException)
{
    // Expected: canceled work has no publication opportunity.
}
Assert(publisher.TryPublish(newEpoch, edited), "EDITOR0014: current generation was not published");
Assert(
    !publisher.TryPublish(oldEpoch, initial) && ReferenceEquals(publisher.Published, edited),
    "EDITOR0015: stale generation replaced the current publication"
);
AssertRejectedReuse(
    () =>
        ProbeGenerationEngine.Prepare(
            initial.InputCompilation,
            new ISourceGenerator[] { new EditorProjectionGenerator().AsSourceGenerator() }
                .Concat(generators.Skip(1))
                .ToArray(),
            CurrentAdditionalTexts(project),
            ParseOptions(project),
            project.AnalyzerOptions.AnalyzerConfigOptionsProvider,
            driverOptions,
            initial
        ),
    "EDITOR0019: changed generator set reused a stale driver"
);
AssertRejectedReuse(
    () =>
        ProbeGenerationEngine.Prepare(
            initial.InputCompilation,
            generators,
            CurrentAdditionalTexts(project),
            ParseOptions(project),
            project.AnalyzerOptions.AnalyzerConfigOptionsProvider,
            default,
            initial
        ),
    "EDITOR0030: changed driver options reused a stale driver"
);
var changedOptions = new DictionaryOptionsProvider("changed");
var optionUpdated = ProbeGenerationEngine.Prepare(
    initial.InputCompilation,
    generators,
    CurrentAdditionalTexts(project),
    ParseOptions(project),
    changedOptions,
    driverOptions,
    initial
);
Assert(
    optionUpdated.Outputs.Any(output =>
        output.Source.Contains("Value = \"changed\"", StringComparison.Ordinal)
    ),
    "EDITOR0020: reused driver retained stale analyzer options"
);
AssertGenerationFailure(() =>
    ProbeGenerationEngine.Prepare(
        initial.InputCompilation,
        [new ThrowingGenerator().AsSourceGenerator()],
        CurrentAdditionalTexts(project),
        ParseOptions(project),
        project.AnalyzerOptions.AnalyzerConfigOptionsProvider
    )
);

Directory.CreateDirectory(artifactRoot);
var measurements = new EditorMeasurements(
    typeof(Compilation).Assembly.FullName ?? "",
    externalGeneratorReferences,
    coldWatch.Elapsed.TotalMilliseconds,
    sameWatch.Elapsed.TotalMilliseconds,
    editWatch.Elapsed.TotalMilliseconds,
    sameCachedSteps,
    CountSteps(edited.Driver, IncrementalStepRunReason.Modified),
    initial.Outputs.Length,
    edited.Outputs.Length,
    renamed.Outputs.Length
);
File.WriteAllText(
    Path.Combine(artifactRoot, "measurements.json"),
    JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }),
    new UTF8Encoding(false)
);
Console.WriteLine(
    $"PASS: cold={measurements.ColdMilliseconds:F2}ms, same={measurements.SameSnapshotMilliseconds:F2}ms, "
        + $"edit={measurements.WarmEditMilliseconds:F2}ms, cached-steps={sameCachedSteps}; "
        + "unsaved edit/deletion/rename, mapped source, cancellation and freshness passed."
);
return 0;

static async Task<ProbePreparationResult> PrepareAsync(
    Project project,
    ISourceGenerator[] generators,
    GeneratorDriverOptions driverOptions,
    ProbePreparationResult? previousResult = null
)
{
    var compilation = await RawCompilationAsync(project).ConfigureAwait(false);
    return ProbeGenerationEngine.Prepare(
        compilation,
        generators,
        CurrentAdditionalTexts(project),
        ParseOptions(project),
        project.AnalyzerOptions.AnalyzerConfigOptionsProvider,
        driverOptions,
        previousResult
    );
}

static async Task<CSharpCompilation> RawCompilationAsync(Project project)
{
    var raw = project
        .Solution.WithProjectAnalyzerReferences(project.Id, [])
        .GetProject(project.Id)!;
    return await raw.GetCompilationAsync().ConfigureAwait(false) as CSharpCompilation
        ?? throw new InvalidOperationException("EDITOR0016: project has no C# compilation");
}

static ImmutableArray<AdditionalText> CurrentAdditionalTexts(Project project)
{
    var documents = project.AdditionalDocuments.Where(document =>
        document.FilePath?.EndsWith(".lui", StringComparison.OrdinalIgnoreCase) == true
    );
    var nonLui = project.AnalyzerOptions.AdditionalFiles.Where(text =>
        !text.Path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase)
    );
    return nonLui
        .Concat(
            documents.Select(document =>
                (AdditionalText)
                    new SnapshotAdditionalText(
                        document.FilePath!,
                        document.GetTextAsync().GetAwaiter().GetResult()
                    )
            )
        )
        .ToImmutableArray();
}

static CSharpParseOptions ParseOptions(Project project) =>
    project.ParseOptions as CSharpParseOptions ?? CSharpParseOptions.Default;

static (ISourceGenerator[] Generators, string[] References) LoadJsonGenerators(Project project)
{
    var references = project.AnalyzerReferences.ToArray();
    var jsonReferences = references
        .Where(reference =>
            String.Equals(
                reference.Display,
                "System.Text.Json.SourceGeneration",
                StringComparison.Ordinal
            )
        )
        .ToArray();
    var generators = jsonReferences
        .SelectMany(reference => reference.GetGenerators(LanguageNames.CSharp))
        .ToArray();
    Assert(
        generators.Length == 1,
        $"EDITOR0017: expected one real JSON generator, got {generators.Length}; "
            + $"references=[{String.Join(",", references.Select(reference => reference.Display))}]"
    );
    return (
        generators,
        jsonReferences
            .Select(reference =>
            {
                var path =
                    reference.FullPath
                    ?? throw new InvalidOperationException(
                        "EDITOR0029: JSON generator reference has no path"
                    );
                return reference.Display
                    + "|"
                    + path
                    + "|product="
                    + FileVersionInfo.GetVersionInfo(path).ProductVersion
                    + "|sha256="
                    + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            })
            .ToArray()
    );
}

static IReadOnlyList<ProbeGeneratedSource> JsonOutputs(ProbePreparationResult result) =>
    result
        .Outputs.Where(output =>
            output.Identity.Contains("System.Text.Json", StringComparison.Ordinal)
        )
        .ToArray();

static void AssertValid(ProbePreparationResult result, string sourcePath, string property)
{
    var errors = result.Diagnostics.Where(diagnostic =>
        diagnostic.Severity == DiagnosticSeverity.Error
    );
    Assert(!errors.Any(), "EDITOR0018: generation errors: " + String.Join(" | ", errors));
    var parseOptions =
        result.InputCompilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions
        ?? CSharpParseOptions.Default;
    var syntaxTrees = result.Outputs.Select(output =>
        CSharpSyntaxTree.ParseText(output.Source, parseOptions, output.Identity)
    );
    var binding = result.InputCompilation.AddSyntaxTrees(syntaxTrees);
    var bindingErrors = binding
        .GetDiagnostics()
        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    Assert(
        !bindingErrors.Any(),
        "EDITOR0021: binding errors: " + String.Join(" | ", bindingErrors)
    );
    var person =
        binding.GetTypeByMetadataName("Fixture.Person")
        ?? throw new InvalidOperationException(
            "EDITOR0022: projected Person declaration is absent"
        );
    Assert(
        person.GetMembers(property).Length == 1,
        $"EDITOR0023: projected Person.{property} is absent"
    );
    var mapped = person
        .DeclaringSyntaxReferences.Single()
        .GetSyntax()
        .GetLocation()
        .GetMappedLineSpan();
    Assert(SamePath(mapped.Path, sourcePath), $"EDITOR0024: declaration mapped to '{mapped.Path}'");
    Assert(mapped.StartLinePosition.Line == 3, "EDITOR0027: declaration mapped to the wrong line");
    var context = binding.GetTypeByMetadataName("Fixture.JsonContext");
    Assert(
        context?.GetMembers("Default").Length > 0 && context.GetMembers("Person").Length > 0,
        "EDITOR0025: real generated JsonContext.Default.Person API is absent"
    );
}

static void AssertRejectedReuse(Action action, string message)
{
    try
    {
        action();
    }
    catch (ArgumentException)
    {
        return;
    }
    throw new InvalidOperationException(message);
}

static void AssertGenerationFailure(Action action)
{
    try
    {
        action();
    }
    catch (InvalidOperationException exception)
        when (exception.Message.StartsWith(
                "Preparatory generator execution failed:",
                StringComparison.Ordinal
            )
        )
    {
        return;
    }
    throw new InvalidOperationException("EDITOR0028: generator exception was treated as success");
}

static int CountSteps(GeneratorDriver driver, IncrementalStepRunReason reason) =>
    driver
        .GetRunResult()
        .Results.SelectMany(result => result.TrackedSteps.Values)
        .SelectMany(steps => steps)
        .SelectMany(step => step.Outputs)
        .Count(output => output.Reason == reason);

static string OutputIdentity(ProbeGeneratedSource output) => output.Identity + ":" + output.Sha256;

static bool SamePath(string? left, string right) =>
    left is not null
    && String.Equals(
        Path.GetFullPath(left),
        Path.GetFullPath(right),
        StringComparison.OrdinalIgnoreCase
    );

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}

internal sealed class SnapshotAdditionalText(string path, SourceText text) : AdditionalText
{
    public override string Path { get; } = path;

    public override SourceText GetText(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return text;
    }
}

internal sealed class EditorProjectionGenerator : IIncrementalGenerator
{
    internal static string PathHash(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path))))[..16];

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var inputs = context
            .AdditionalTextsProvider.Where(static text =>
                text.Path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase)
            )
            .Select(
                static (text, cancellationToken) =>
                    new ProjectionInput(text.Path, text.GetText(cancellationToken)!.ToString())
            )
            .WithTrackingName("EditorProjectionInput");
#pragma warning disable RSEXPERIMENTAL007
        context.RegisterPreCompilationSourceOutput(
            inputs,
            static (production, input) =>
            {
                var escapedPath = input
                    .Path.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("\"", "\\\"", StringComparison.Ordinal);
                production.AddSource(
                    "Projection_" + PathHash(input.Path) + ".g.cs",
                    SourceText.From(
                        "// EDITOR_PROJECTION_OUTPUT\n#line 1 \""
                            + escapedPath
                            + "\"\n"
                            + input.Source
                            + "\n#line default\n",
                        Encoding.UTF8
                    )
                );
            }
        );
#pragma warning restore RSEXPERIMENTAL007
    }

    private sealed record ProjectionInput(string Path, string Source);
}

internal sealed class CancellableGateGenerator : IIncrementalGenerator
{
    internal ManualResetEventSlim Started { get; } = new(false);

    internal ManualResetEventSlim Release { get; } = new(false);

    public void Initialize(IncrementalGeneratorInitializationContext context) =>
        context.RegisterSourceOutput(
            context.AdditionalTextsProvider.Collect(),
            (production, _) =>
            {
                Started.Set();
                Release.Wait(production.CancellationToken);
                production.AddSource("Gate.g.cs", "internal static class Gate;");
            }
        );
}

internal sealed class ThrowingGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context) =>
        context.RegisterSourceOutput(
            context.AdditionalTextsProvider.Collect(),
            static (_, _) => throw new InvalidOperationException("expected probe failure")
        );
}

internal sealed class FreshnessPublisher
{
    private long latest;

    internal ProbePreparationResult? Published { get; private set; }

    internal long Begin() => Interlocked.Increment(ref latest);

    internal bool TryPublish(long epoch, ProbePreparationResult result)
    {
        if (epoch != Volatile.Read(ref latest))
            return false;
        Published = result;
        return true;
    }
}

internal sealed class DictionaryOptionsProvider(string value) : AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions { get; } = new DictionaryOptions(value);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

    private sealed class DictionaryOptions(string value) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string result)
        {
            if (String.Equals(key, "build_property.ProbeEditorValue", StringComparison.Ordinal))
            {
                result = value;
                return true;
            }
            result = "";
            return false;
        }
    }
}

internal sealed class OptionsEchoGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context) =>
        context.RegisterSourceOutput(
            context.AnalyzerConfigOptionsProvider,
            static (production, provider) =>
            {
                provider.GlobalOptions.TryGetValue(
                    "build_property.ProbeEditorValue",
                    out var value
                );
                production.AddSource(
                    "EditorOptions.g.cs",
                    "internal static class EditorOptions { internal const string Value = \""
                        + (value ?? "unset")
                        + "\"; }"
                );
            }
        );
}

internal sealed record EditorMeasurements(
    string RoslynAssembly,
    string[] ExternalGeneratorReferences,
    double ColdMilliseconds,
    double SameSnapshotMilliseconds,
    double WarmEditMilliseconds,
    int SameSnapshotCachedSteps,
    int EditModifiedSteps,
    int InitialOutputCount,
    int EditedOutputCount,
    int RenamedOutputCount
);
