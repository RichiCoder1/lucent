using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucent.ApplicationAuthoring.SdkHost;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;

try
{
    return args.FirstOrDefault() switch
    {
        "prepare" when args.Length == 10 => await PrepareAsync(args.Skip(1).ToArray()),
        "compare" when args.Length == 3 => Compare(args[1], args[2]),
        _ => Usage(),
    };
}
catch (Exception exception)
{
    return Fail($"PROBE0008: generator host failed: {exception.Message}");
}

static async Task<int> PrepareAsync(string[] args)
{
    var projectPath = Path.GetFullPath(args[0]);
    var manifestPath = Path.GetFullPath(args[1]);
    var bindingDirectory = Path.GetFullPath(args[2]);
    var outerIntermediate =
        Path.GetDirectoryName(Path.GetFullPath(args[8]))! + Path.DirectorySeparatorChar;
    var workspaceIntermediate =
        Path.Combine(Path.GetDirectoryName(manifestPath)!, "workspace")
        + Path.DirectorySeparatorChar;
    var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Configuration"] = args[3],
        ["TargetFramework"] = args[4],
        ["ProbeMismatchKind"] = args[5],
        ["ProbeGeneratorPath"] = Path.GetFullPath(args[6]),
        ["ProbeModelPath"] = Path.GetFullPath(args[7]),
        ["BaseIntermediateOutputPath"] = workspaceIntermediate,
        ["BaseOutputPath"] =
            Path.Combine(workspaceIntermediate, "bin") + Path.DirectorySeparatorChar,
        ["MSBuildProjectExtensionsPath"] = outerIntermediate,
        ["ProjectAssetsFile"] = Path.Combine(outerIntermediate, "project.assets.json"),
        ["ProbePreparation"] = "true",
        ["ProbeSkipPrepare"] = "true",
    };
    if (!MSBuildLocator.IsRegistered)
        MSBuildLocator.RegisterDefaults();
    using var workspace = MSBuildWorkspace.Create(properties);
    var workspaceDiagnostics = new ConcurrentQueue<WorkspaceDiagnostic>();
    using var workspaceFailureRegistration = workspace.RegisterWorkspaceFailedHandler(failure =>
    {
        workspaceDiagnostics.Enqueue(failure.Diagnostic);
        Console.Error.WriteLine(
            $"PROBE0001: workspace {failure.Diagnostic.Kind}: {failure.Diagnostic.Message}"
        );
    });
    var project = await workspace.OpenProjectAsync(projectPath).ConfigureAwait(false);
    var workspaceFailures = workspaceDiagnostics.Where(diagnostic =>
        diagnostic.Kind == WorkspaceDiagnosticKind.Failure
    );
    if (workspaceFailures.Any())
        return Fail(
            "PROBE0006: evaluated project workspace failed\n" + String.Join("\n", workspaceFailures)
        );
    var generators = new List<ISourceGenerator>();
    foreach (var reference in project.AnalyzerReferences)
    {
        if (reference is UnresolvedAnalyzerReference)
            return Fail($"PROBE0007: unresolved analyzer reference '{reference.Display}'");
        var loadFailures = new ConcurrentQueue<string>();
        void OnLoadFailed(object? sender, AnalyzerLoadFailureEventArgs failure) =>
            loadFailures.Enqueue(failure.Message);
        var fileReference = reference as AnalyzerFileReference;
        if (fileReference is not null)
            fileReference.AnalyzerLoadFailed += OnLoadFailed;
        try
        {
            var loaded = reference.GetGenerators(LanguageNames.CSharp);
            if (!loadFailures.IsEmpty)
                return Fail(
                    $"PROBE0007: failed to load analyzer reference '{reference.Display}': "
                        + String.Join("; ", loadFailures)
                );
            generators.AddRange(loaded);
        }
        catch (Exception exception)
        {
            return Fail(
                $"PROBE0007: failed to load analyzer reference '{reference.Display}': {exception.Message}"
            );
        }
        finally
        {
            if (fileReference is not null)
                fileReference.AnalyzerLoadFailed -= OnLoadFailed;
        }
    }
    if (generators.Count == 0)
        return Fail("PROBE0002: evaluated project supplied no C# source generators");

    var rawSolution = project.Solution.WithProjectAnalyzerReferences(project.Id, []);
    var rawProject = rawSolution.GetProject(project.Id)!;
    var compilation = await rawProject.GetCompilationAsync().ConfigureAwait(false);
    if (compilation is not CSharpCompilation csharpCompilation)
        return Fail("PROBE0003: evaluated project did not produce a C# compilation");
    var parseOptions = project.ParseOptions as CSharpParseOptions ?? CSharpParseOptions.Default;
    var additionalTexts = project.AnalyzerOptions.AdditionalFiles;
    var result = ProbeGenerationEngine.Prepare(
        csharpCompilation,
        generators,
        additionalTexts,
        parseOptions,
        project.AnalyzerOptions.AnalyzerConfigOptionsProvider
    );
    var errors = result.Diagnostics.Where(diagnostic =>
        diagnostic.Severity == DiagnosticSeverity.Error
    );
    if (errors.Any())
        return Fail("PROBE0004: preparatory generation failed\n" + String.Join("\n", errors));

    Directory.CreateDirectory(bindingDirectory);
    var entries = new List<ManifestEntry>();
    var index = 0;
    foreach (var source in result.Outputs)
    {
        var text = source.Source;
        if (IsLucentOwnedOutput(source.Identity))
            continue;
        var bindingPath = Path.Combine(
            bindingDirectory,
            index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)
                + ".binding.g.cs"
        );
        File.WriteAllText(bindingPath, text, new UTF8Encoding(false));
        entries.Add(new ManifestEntry(source.Identity, source.Sha256));
        index++;
    }
    if (entries.Count == 0)
        return Fail("PROBE0005: preparatory generation captured no binding sources");
    Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
    File.WriteAllText(
        manifestPath,
        JsonSerializer.Serialize(
            new ProbeManifest(
                typeof(Compilation).Assembly.FullName ?? "",
                project.AnalyzerReferences.Select(AnalyzerIdentity).Order().ToArray(),
                additionalTexts.Select(text => Path.GetFullPath(text.Path)).Order().ToArray(),
                entries.OrderBy(entry => entry.Identity, StringComparer.Ordinal).ToArray()
            ),
            new JsonSerializerOptions { WriteIndented = true }
        ),
        new UTF8Encoding(false)
    );
    Console.WriteLine(
        $"Preparation captured {entries.Count} binding-only outputs from {result.GeneratorCount} evaluated generators and {additionalTexts.Length} AdditionalTexts."
    );
    return 0;
}

static int Compare(string manifestPath, string generatedDirectory)
{
    var manifest =
        JsonSerializer.Deserialize<ProbeManifest>(File.ReadAllText(manifestPath))
        ?? throw new InvalidOperationException("Probe manifest is empty.");
    var actual = Directory
        .EnumerateFiles(generatedDirectory, "*.cs", SearchOption.AllDirectories)
        .Select(path => new ManifestEntry(
            ProbeGenerationEngine.NormalizeIdentity(Path.GetRelativePath(generatedDirectory, path)),
            ProbeGenerationEngine.Hash(File.ReadAllText(path))
        ))
        .Where(entry => !IsLucentOwnedOutput(entry.Identity))
        .OrderBy(entry => entry.Identity, StringComparer.Ordinal)
        .ToArray();
    var mismatch = ProbeGenerationEngine.Compare(manifest.Outputs, actual);
    if (!mismatch.IsMatch)
        return Fail(
            "PROBE9001: preparatory/final generator output mismatch; "
                + $"missing=[{String.Join(",", mismatch.Missing)}]; "
                + $"extra=[{String.Join(",", mismatch.Extra)}]; "
                + $"changed=[{String.Join(",", mismatch.Changed)}]"
        );
    Console.WriteLine($"Compared {actual.Length} final outputs by identity and SHA-256: MATCH.");
    return 0;
}

static bool IsLucentOwnedOutput(string identity) =>
    identity.StartsWith(
        "ProbeGenerators/Lucent.ApplicationAuthoring.SdkHost.ProbeProjectionGenerator/",
        StringComparison.Ordinal
    )
    || identity.StartsWith(
        "ProbeGenerators/Lucent.ApplicationAuthoring.SdkHost.ProbeBindingGenerator/",
        StringComparison.Ordinal
    );

static string AnalyzerIdentity(AnalyzerReference reference)
{
    var path = reference.FullPath;
    return String.IsNullOrWhiteSpace(path)
        ? reference.Display ?? ""
        : Path.GetFileName(path)
            + ":"
            + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}

static int Usage() =>
    Fail(
        "Usage: ProbeHost prepare <project> <manifest> <binding-dir> <configuration> <tfm> <mismatch-kind> <generator-path> <model-path> <base-intermediate-marker> | compare <manifest> <generated-dir>"
    );

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}

internal sealed record ManifestEntry(string Identity, string Sha256);

internal sealed record ProbeManifest(
    string RoslynAssembly,
    string[] Analyzers,
    string[] AdditionalTexts,
    ManifestEntry[] Outputs
);
