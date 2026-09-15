using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucent.ApplicationAuthoring.PreparedSdk;
using Lucent.ApplicationAuthoring.SdkHost;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;

if (args.Length == 0)
    return Fail("Usage: PreparedSdk.Host <prepare|compare> ...");

try
{
    return args[0] switch
    {
        "prepare" => await PrepareAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
        "compare" => Compare(args.Skip(1).ToArray()),
        _ => Fail($"Unknown command '{args[0]}'."),
    };
}
catch (Exception error)
{
    Console.Error.WriteLine(error.ToString());
    return 1;
}

static async Task<int> PrepareAsync(string[] args)
{
    if (args.Length != 15)
        return Fail(
            "prepare requires project, manifest, emitter directory, configuration, target framework, runtime identifier, early payload paths, final payload paths, analyzer paths, flavor, and evaluated intermediate paths"
        );
    var projectPath = Path.GetFullPath(args[0]);
    var manifestPath = Path.GetFullPath(args[1]);
    var emitterDirectory = Path.GetFullPath(args[2]);
    var capturedProperties =
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(args[14]))
        ?? throw new InvalidOperationException("The outer global-property snapshot was empty.");
    var properties = new Dictionary<string, string>(
        capturedProperties,
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["Configuration"] = args[3],
        ["TargetFramework"] = args[4],
        ["RuntimeIdentifier"] = args[5],
        ["PreparedSkipPrepare"] = "true",
        ["PreparedForeignGeneratorPath"] = args[8],
        ["PreparedJsonGeneratorPath"] = args[9],
        ["PreparedFlavor"] = args[10],
        ["BaseIntermediateOutputPath"] = args[11],
        ["MSBuildProjectExtensionsPath"] = args[12],
        ["ProjectAssetsFile"] = args[13],
    };
    var payload = PreparedPayload.Create(ReadPayloads(args[6]), ReadPayloads(args[7]));

    if (!MSBuildLocator.IsRegistered)
        MSBuildLocator.RegisterDefaults();
    using var workspace = MSBuildWorkspace.Create(properties);
    var workspaceDiagnostics = new ConcurrentQueue<WorkspaceDiagnostic>();
    using var workspaceFailureRegistration = workspace.RegisterWorkspaceFailedHandler(failure =>
        workspaceDiagnostics.Enqueue(failure.Diagnostic)
    );
    var project = await workspace.OpenProjectAsync(projectPath).ConfigureAwait(false);
    var workspaceFailures = workspaceDiagnostics.Where(diagnostic =>
        diagnostic.Kind == WorkspaceDiagnosticKind.Failure
    );
    if (workspaceFailures.Any())
        throw new InvalidOperationException(
            "MSBuildWorkspace reported failures: " + String.Join(" | ", workspaceFailures)
        );

    var rawProject = project
        .Solution.WithProjectAnalyzerReferences(project.Id, [])
        .GetProject(project.Id)!;
    var compilation =
        await rawProject.GetCompilationAsync().ConfigureAwait(false) as CSharpCompilation
        ?? throw new InvalidOperationException("The evaluated project has no C# compilation.");
    var loaded = LoadGenerators(project.AnalyzerReferences);
    var projection = new StaticPreparedPayloadGenerator(
        new PreparedPayload(payload.EarlyDeclarations, [])
    ).AsSourceGenerator();
    var generators = new[] { projection }.Concat(loaded.Select(item => item.Generator)).ToArray();
    var parseOptions = project.ParseOptions as CSharpParseOptions ?? CSharpParseOptions.Default;
    var prepared = ProbeGenerationEngine.Prepare(
        compilation,
        generators,
        project.AnalyzerOptions.AdditionalFiles,
        parseOptions,
        project.AnalyzerOptions.AnalyzerConfigOptionsProvider
    );
    var errors = prepared.Diagnostics.Where(diagnostic =>
        diagnostic.Severity == DiagnosticSeverity.Error
    );
    if (errors.Any())
        throw new InvalidOperationException(
            "Preparatory generation produced errors: " + String.Join(" | ", errors)
        );

    var foreignOutputs = prepared
        .Outputs.Where(output => output.GeneratorOrdinal > 0)
        .Select(output =>
        {
            var identity = loaded[output.GeneratorOrdinal - 1].Identity;
            return new PreparedForeignOutput(
                identity.StableIdentity,
                output.Identity,
                output.Sha256
            );
        })
        .OrderBy(output => output.HintIdentity, StringComparer.Ordinal)
        .ToImmutableArray();
    var emitter = ProjectEmitterCompiler.Compile(payload, emitterDirectory);
    var manifest = new PreparedManifest(
        projectPath,
        await ProjectInputHashAsync(project, properties).ConfigureAwait(false),
        emitter.ImageSha256,
        loaded.Select(item => item.Identity).ToImmutableArray(),
        foreignOutputs,
        project
            .AnalyzerOptions.AdditionalFiles.Select(file => Path.GetFullPath(file.Path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray(),
        project
            .AnalyzerConfigDocuments.Select(document => document.FilePath ?? document.Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray(),
        compilation
            .References.Select(reference => reference.Display ?? "")
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray(),
        project
            .ProjectReferences.Select(reference =>
                project.Solution.GetProject(reference.ProjectId)?.FilePath
                ?? reference.ProjectId.Id.ToString("D")
            )
            .Order(StringComparer.Ordinal)
            .ToImmutableArray()
    );
    WriteJsonAtomically(manifestPath, manifest);
    File.WriteAllText(
        Path.Combine(Path.GetDirectoryName(manifestPath)!, "emitter-path.txt"),
        emitter.Path,
        new UTF8Encoding(false)
    );
    Console.WriteLine(
        $"prepared emitter={emitter.Path}; foreign-generators={loaded.Count}; foreign-outputs={foreignOutputs.Length}; input={manifest.ProjectInputSha256}"
    );
    return 0;
}

static int Compare(string[] args)
{
    if (args.Length < 4)
        return Fail(
            "compare requires manifest, generated root, emitter assembly name, mismatch mode, and optional cleanup paths"
        );
    var manifest =
        JsonSerializer.Deserialize<PreparedManifest>(File.ReadAllText(args[0]))
        ?? throw new InvalidOperationException("Prepared manifest was empty.");
    var generatedRoot = Path.GetFullPath(args[1]);
    var emitterAssemblyName = args[2];
    var mode = args[3];
    var analyzerMismatches = manifest
        .ForeignGenerators.Where(identity =>
            !File.Exists(identity.AnalyzerPath)
            || !String.Equals(
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(identity.AnalyzerPath))),
                identity.AnalyzerSha256,
                StringComparison.Ordinal
            )
        )
        .Select(identity => identity.AnalyzerPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToList();
    if (String.Equals(mode, "analyzer", StringComparison.Ordinal))
        analyzerMismatches.Add("synthetic-analyzer-identity-change");
    var actual = Directory.Exists(generatedRoot)
        ? Directory
            .EnumerateFiles(generatedRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path =>
            {
                var identity = Normalize(Path.GetRelativePath(generatedRoot, path));
                var source = File.ReadAllText(path);
                return new PreparedForeignOutput("final", identity, Hash(source));
            })
            .Where(output => !IsEmitterOutput(output.HintIdentity, emitterAssemblyName))
            .OrderBy(output => output.HintIdentity, StringComparer.Ordinal)
            .ToList()
        : [];
    ApplyMismatch(mode, actual);
    var expectedByIdentity = manifest.ForeignOutputs.ToDictionary(
        output => Normalize(output.HintIdentity),
        StringComparer.Ordinal
    );
    var actualByIdentity = actual.ToDictionary(
        output => Normalize(output.HintIdentity),
        StringComparer.Ordinal
    );
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
        .Where(identity => expectedByIdentity[identity].Sha256 != actualByIdentity[identity].Sha256)
        .Order(StringComparer.Ordinal)
        .ToArray();
    if (
        missing.Length != 0
        || extra.Length != 0
        || changed.Length != 0
        || analyzerMismatches.Count != 0
    )
    {
        foreach (var path in args.Skip(4).Where(path => !String.IsNullOrWhiteSpace(path)))
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        Console.Error.WriteLine(
            $"Prepared output mismatch: analyzers=[{String.Join(",", analyzerMismatches)}]; missing=[{String.Join(",", missing)}]; extra=[{String.Join(",", extra)}]; changed=[{String.Join(",", changed)}]"
        );
        return 1;
    }
    Console.WriteLine($"matched {actual.Count} foreign generated outputs");
    return 0;
}

static List<LoadedGenerator> LoadGenerators(IEnumerable<AnalyzerReference> references)
{
    var loaded = new List<LoadedGenerator>();
    foreach (var reference in references)
    {
        var failures = new List<string>();
        void OnFailed(object? _, AnalyzerLoadFailureEventArgs failure) =>
            failures.Add(failure.Message);
        if (reference is AnalyzerFileReference fileReference)
            fileReference.AnalyzerLoadFailed += OnFailed;
        ImmutableArray<ISourceGenerator> generators;
        try
        {
            generators = reference.GetGenerators(LanguageNames.CSharp);
        }
        finally
        {
            if (reference is AnalyzerFileReference loadedFileReference)
                loadedFileReference.AnalyzerLoadFailed -= OnFailed;
        }
        if (failures.Count != 0)
            throw new InvalidOperationException(
                $"Analyzer '{reference.Display}' failed to load: {String.Join(" | ", failures)}"
            );
        var path = reference.FullPath;
        var analyzerHash =
            path is not null && File.Exists(path)
                ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
                : Hash(reference.Display ?? reference.GetType().FullName ?? "unknown");
        for (var index = 0; index < generators.Length; index++)
        {
            var identity = new ForeignGeneratorIdentity(
                loaded.Count + 1,
                path ?? reference.Display ?? "",
                analyzerHash,
                index,
                generators[index].GetType().FullName ?? generators[index].GetType().Name
            );
            loaded.Add(new LoadedGenerator(generators[index], identity));
        }
    }
    return loaded;
}

static IEnumerable<PreparedSource> ReadPayloads(string value)
{
    if (String.IsNullOrWhiteSpace(value))
        yield break;
    foreach (var path in value.Split('|', StringSplitOptions.RemoveEmptyEntries))
    {
        var fullPath = Path.GetFullPath(path);
        yield return new PreparedSource(
            Path.GetFileName(fullPath) + ".g.cs",
            File.ReadAllText(fullPath),
            fullPath
        );
    }
}

static async Task<string> ProjectInputHashAsync(
    Project project,
    IReadOnlyDictionary<string, string> globalProperties
)
{
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    foreach (var property in globalProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        Append(hash, "global", property.Key, property.Value);
    var pending = new Queue<Project>();
    var visited = new HashSet<ProjectId>();
    pending.Enqueue(project);
    while (pending.TryDequeue(out var current))
    {
        if (!visited.Add(current.Id))
            continue;
        Append(hash, "project", current.FilePath ?? current.Name, current.AssemblyName ?? "");
        foreach (var document in current.Documents)
            Append(
                hash,
                "source",
                document.FilePath ?? document.Name,
                (await document.GetTextAsync()).ToString()
            );
        foreach (var document in current.AdditionalDocuments)
            Append(
                hash,
                "additional",
                document.FilePath ?? document.Name,
                (await document.GetTextAsync()).ToString()
            );
        foreach (var document in current.AnalyzerConfigDocuments)
            Append(
                hash,
                "config",
                document.FilePath ?? document.Name,
                (await document.GetTextAsync()).ToString()
            );
        foreach (var reference in current.ProjectReferences)
        {
            var referenced = current.Solution.GetProject(reference.ProjectId);
            Append(
                hash,
                "project-reference",
                current.FilePath ?? current.Name,
                (referenced?.FilePath ?? reference.ProjectId.Id.ToString("D"))
                    + "|aliases="
                    + String.Join(",", reference.Aliases)
                    + "|embed="
                    + reference.EmbedInteropTypes
            );
            if (referenced is not null)
                pending.Enqueue(referenced);
        }
    }
    foreach (var reference in project.MetadataReferences)
    {
        AppendFileIdentity(hash, "metadata", reference.Display ?? "");
        Append(
            hash,
            "metadata-properties",
            reference.Display ?? "",
            reference.Properties.Kind
                + "|aliases="
                + String.Join(",", reference.Properties.Aliases)
                + "|embed="
                + reference.Properties.EmbedInteropTypes
        );
    }
    foreach (var reference in project.AnalyzerReferences)
        AppendFileIdentity(hash, "analyzer", reference.FullPath ?? reference.Display ?? "");
    var parse = project.ParseOptions as CSharpParseOptions;
    Append(hash, "parse", "language", parse?.LanguageVersion.ToString() ?? "");
    Append(hash, "parse", "defines", String.Join(";", parse?.PreprocessorSymbolNames ?? []));
    AppendCompilationOptions(hash, project.CompilationOptions as CSharpCompilationOptions);
    return Convert.ToHexString(hash.GetHashAndReset());
}

static void AppendCompilationOptions(IncrementalHash hash, CSharpCompilationOptions? options)
{
    if (options is null)
        return;
    Append(hash, "compilation", "output-kind", options.OutputKind.ToString());
    Append(hash, "compilation", "module", options.ModuleName ?? "");
    Append(hash, "compilation", "main", options.MainTypeName ?? "");
    Append(hash, "compilation", "script", options.ScriptClassName ?? "");
    Append(hash, "compilation", "usings", String.Join("\0", options.Usings));
    Append(hash, "compilation", "optimization", options.OptimizationLevel.ToString());
    Append(hash, "compilation", "overflow", options.CheckOverflow.ToString());
    Append(hash, "compilation", "unsafe", options.AllowUnsafe.ToString());
    Append(hash, "compilation", "platform", options.Platform.ToString());
    Append(
        hash,
        "compilation",
        "warning-level",
        options.WarningLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)
    );
    Append(hash, "compilation", "general-diagnostic", options.GeneralDiagnosticOption.ToString());
    foreach (
        var diagnostic in options.SpecificDiagnosticOptions.OrderBy(
            pair => pair.Key,
            StringComparer.Ordinal
        )
    )
        Append(hash, "diagnostic", diagnostic.Key, diagnostic.Value.ToString());
    Append(hash, "compilation", "concurrent", options.ConcurrentBuild.ToString());
    Append(hash, "compilation", "deterministic", options.Deterministic.ToString());
    Append(hash, "compilation", "nullable", options.NullableContextOptions.ToString());
    Append(hash, "compilation", "metadata-import", options.MetadataImportOptions.ToString());
    Append(hash, "compilation", "public-sign", options.PublicSign.ToString());
    Append(hash, "compilation", "delay-sign", options.DelaySign?.ToString() ?? "");
    Append(hash, "compilation", "crypto-key-file", options.CryptoKeyFile ?? "");
    Append(hash, "compilation", "crypto-key-container", options.CryptoKeyContainer ?? "");
    Append(
        hash,
        "compilation",
        "report-suppressed",
        options.ReportSuppressedDiagnostics.ToString()
    );
    Append(
        hash,
        "compilation",
        "source-resolver",
        options.SourceReferenceResolver?.ToString() ?? ""
    );
    Append(hash, "compilation", "xml-resolver", options.XmlReferenceResolver?.ToString() ?? "");
    Append(
        hash,
        "compilation",
        "metadata-resolver",
        options.MetadataReferenceResolver?.ToString() ?? ""
    );
    Append(hash, "compilation", "strong-name", options.StrongNameProvider?.ToString() ?? "");
    Append(
        hash,
        "compilation",
        "identity-comparer",
        options.AssemblyIdentityComparer?.ToString() ?? ""
    );
}

static void AppendFileIdentity(IncrementalHash hash, string kind, string path)
{
    var fullPath = File.Exists(path) ? Path.GetFullPath(path) : path;
    var value = File.Exists(path)
        ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
        : "missing";
    Append(hash, kind, fullPath, value);
}

static void Append(IncrementalHash hash, string kind, string name, string value)
{
    foreach (var part in new[] { kind, name, value })
    {
        var bytes = Encoding.UTF8.GetBytes(part);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }
}

static void WriteJsonAtomically<T>(string path, T value)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
    File.WriteAllText(
        temporary,
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }),
        new UTF8Encoding(false)
    );
    File.Move(temporary, path, overwrite: true);
}

static bool IsEmitterOutput(string identity, string emitterAssemblyName) =>
    identity
        .Split('/')
        .Any(part => String.Equals(part, emitterAssemblyName, StringComparison.Ordinal));

static void ApplyMismatch(string mode, List<PreparedForeignOutput> outputs)
{
    switch (mode)
    {
        case "none":
            return;
        case "missing":
            if (outputs.Count != 0)
                outputs.RemoveAt(0);
            return;
        case "extra":
            outputs.Add(
                new PreparedForeignOutput("fixture", "Unexpected/Extra.g.cs", Hash("extra"))
            );
            return;
        case "changed":
            if (outputs.Count != 0)
                outputs[0] = outputs[0] with { Sha256 = Hash("changed") };
            return;
        case "analyzer":
            return;
        default:
            throw new ArgumentException($"Unknown mismatch mode '{mode}'.");
    }
}

static string Normalize(string path) => path.Replace('\\', '/');

static string Hash(string source) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}

sealed record LoadedGenerator(ISourceGenerator Generator, ForeignGeneratorIdentity Identity);
