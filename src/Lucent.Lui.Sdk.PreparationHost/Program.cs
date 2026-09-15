using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucent.Lui.Preparation;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;

if (args.Length == 0)
    return Fail("Usage: Lucent.Lui.Sdk.PreparationHost <prepare|compare> ...");

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
    if (args.Length != 8)
        return Fail(
            "prepare requires project, manifest, emitter directory, configuration, target framework, runtime identifier, outer globals, and compiler-generated output directory"
        );
    var projectPath = Path.GetFullPath(args[0]);
    var manifestPath = Path.GetFullPath(args[1]);
    var emitterDirectory = Path.GetFullPath(args[2]);
    var generatedRoot = Path.GetFullPath(args[7]);
    var previousManifest = File.Exists(manifestPath)
        ? JsonSerializer.Deserialize<PreparedManifest>(File.ReadAllText(manifestPath))
        : null;
    var capturedProperties =
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(args[6]))
        ?? throw new InvalidOperationException("The outer global-property snapshot was empty.");
    var properties = new Dictionary<string, string>(
        capturedProperties,
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["Configuration"] = args[3],
        ["TargetFramework"] = args[4],
        ["RuntimeIdentifier"] = args[5],
        ["LucentLuiSkipPreparation"] = "true",
        ["LucentLuiPreparedAuthoring"] = "true",
    };

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
    var parseOptions = project.ParseOptions as CSharpParseOptions ?? CSharpParseOptions.Default;
    var documents = new List<LuiPreparationDocument>();
    foreach (
        var document in project.AdditionalDocuments.Where(document =>
            document.FilePath?.EndsWith(".lui", StringComparison.OrdinalIgnoreCase) == true
        )
    )
    {
        var path = document.FilePath!;
        var additional = project.AnalyzerOptions.AdditionalFiles.First(file =>
            String.Equals(
                Path.GetFullPath(file.Path),
                Path.GetFullPath(path),
                StringComparison.OrdinalIgnoreCase
            )
        );
        var options = project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(additional);
        options.TryGetValue(
            "build_metadata.AdditionalFiles.LucentLuiLogicalPath",
            out var logicalPath
        );
        options.TryGetValue(
            "build_metadata.AdditionalFiles.LucentLuiDocumentVersion",
            out var version
        );
        documents.Add(
            new LuiPreparationDocument(
                path,
                String.IsNullOrWhiteSpace(logicalPath)
                    ? Path.GetRelativePath(
                        project.FilePath is null
                            ? Environment.CurrentDirectory
                            : Path.GetDirectoryName(project.FilePath)!,
                        path
                    )
                    : logicalPath,
                (await document.GetTextAsync().ConfigureAwait(false)).ToString(),
                version ?? ""
            )
        );
    }
    if (documents.Count == 0)
        throw new InvalidOperationException(
            "Prepared authoring requires at least one evaluated .lui input."
        );
    var global = project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions;
    var prepared = LuiPreparationEngine.Prepare(
        new LuiPreparationRequest(
            compilation,
            documents.ToImmutableArray(),
            loaded.Select(item => item.Generator).ToImmutableArray(),
            project.AnalyzerOptions.AdditionalFiles,
            parseOptions,
            project.AnalyzerOptions.AnalyzerConfigOptionsProvider,
            Global(global, "LucentLuiProjectEpoch"),
            Global(global, "LucentLuiProjectIdentity", project.AssemblyName ?? ""),
            Global(global, "LucentLuiLangVersion", "preview"),
            Global(global, "LucentLuiCompilerOptions"),
            Global(global, "LucentLuiDefines"),
            Global(global, "RootNamespace")
        )
    );
    var errors = prepared.GeneratorDiagnostics.Where(diagnostic =>
        diagnostic.Severity == DiagnosticSeverity.Error
    );
    if (!prepared.Success || errors.Any())
        throw new InvalidOperationException(
            "Prepared LUI generation failed: "
                + String.Join(" | ", errors)
                + " | "
                + String.Join(
                    " | ",
                    prepared.LuiDiagnostics.Select(item =>
                        item.PhysicalPath
                        + ": "
                        + item.Diagnostic.Id
                        + ": "
                        + item.Diagnostic.Message
                    )
                )
        );

    var foreignOutputs = prepared
        .ForeignOutputs.Select(output => new PreparedForeignOutput(
            output.GeneratorIdentity,
            output.HintIdentity,
            output.Sha256
        ))
        .OrderBy(output => output.HintIdentity, StringComparer.Ordinal)
        .ToImmutableArray();
    var emitter = LuiPreparedEmitterCompiler.Compile(prepared.Payload, emitterDirectory);
    var inputFiles = SnapshotInputFiles(project, compilation);
    var manifest = new PreparedManifest(
        projectPath,
        await ProjectInputHashAsync(project, properties).ConfigureAwait(false),
        emitter.AssemblyName,
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
            .ToImmutableArray(),
        inputFiles
    );
    CleanOwnedGeneratedFiles(generatedRoot, previousManifest, manifest);
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
    var inputMismatches = (manifest.InputFiles.IsDefault ? [] : manifest.InputFiles)
        .Where(input =>
            !File.Exists(input.Path)
            || !String.Equals(FileHash(input.Path), input.Sha256, StringComparison.Ordinal)
        )
        .Select(input => input.Path)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();
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
        || inputMismatches.Length != 0
    )
    {
        foreach (var path in args.Skip(4).Where(path => !String.IsNullOrWhiteSpace(path)))
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        Console.Error.WriteLine(
            $"Prepared output mismatch: inputs=[{String.Join(",", inputMismatches)}]; analyzers=[{String.Join(",", analyzerMismatches)}]; missing=[{String.Join(",", missing)}]; extra=[{String.Join(",", extra)}]; changed=[{String.Join(",", changed)}]"
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
            loaded.Add(
                new LoadedGenerator(
                    new LuiPreparationGenerator(
                        identity.StableIdentity,
                        identity.AnalyzerPath,
                        identity.AnalyzerSha256,
                        identity.GeneratorOrdinal,
                        generators[index]
                    ),
                    identity
                )
            );
        }
    }
    return loaded;
}

static string Global(AnalyzerConfigOptions options, string property, string fallback = "")
{
    return options.TryGetValue("build_property." + property, out var value) ? value : fallback;
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

static ImmutableArray<PreparedInputFile> SnapshotInputFiles(
    Project project,
    CSharpCompilation compilation
) =>
    project
        .Documents.Select(document => document.FilePath)
        .Concat(project.AdditionalDocuments.Select(document => document.FilePath))
        .Concat(project.AnalyzerConfigDocuments.Select(document => document.FilePath))
        .Concat(compilation.References.Select(reference => reference.Display))
        .Where(path => path is not null && File.Exists(path))
        .Select(path => Path.GetFullPath(path!))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .Select(path => new PreparedInputFile(path, FileHash(path)))
        .ToImmutableArray();

static string FileHash(string path) =>
    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

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

static void CleanOwnedGeneratedFiles(
    string generatedRoot,
    PreparedManifest? previous,
    PreparedManifest current
)
{
    if (!Directory.Exists(generatedRoot))
        return;
    foreach (
        var identity in (previous?.ForeignOutputs ?? [])
            .Concat(current.ForeignOutputs)
            .Select(output => output.HintIdentity)
            .Distinct(StringComparer.Ordinal)
    )
    {
        var path = OwnedGeneratedPath(generatedRoot, identity);
        if (File.Exists(path))
            File.Delete(path);
    }
    foreach (
        var assemblyName in new[] { previous?.EmitterAssemblyName, current.EmitterAssemblyName }
            .Where(name => !String.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
    )
    {
        var owned = OwnedGeneratedPath(generatedRoot, assemblyName!);
        if (Directory.Exists(owned))
            Directory.Delete(owned, recursive: true);
    }
}

static string OwnedGeneratedPath(string generatedRoot, string relativePath)
{
    if (Path.IsPathRooted(relativePath))
        throw new InvalidOperationException(
            $"Generated output identity must be relative: '{relativePath}'."
        );
    var root =
        Path.GetFullPath(generatedRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        + Path.DirectorySeparatorChar;
    var candidate = Path.GetFullPath(
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))
    );
    if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(
            $"Generated output identity escapes its owned root: '{relativePath}'."
        );
    return candidate;
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

sealed record LoadedGenerator(LuiPreparationGenerator Generator, ForeignGeneratorIdentity Identity);

sealed record ForeignGeneratorIdentity(
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

sealed record PreparedForeignOutput(string GeneratorIdentity, string HintIdentity, string Sha256);

sealed record PreparedInputFile(string Path, string Sha256);

sealed record PreparedManifest(
    string ProjectPath,
    string ProjectInputSha256,
    string EmitterAssemblyName,
    string EmitterSha256,
    ImmutableArray<ForeignGeneratorIdentity> ForeignGenerators,
    ImmutableArray<PreparedForeignOutput> ForeignOutputs,
    ImmutableArray<string> OriginalAdditionalFiles,
    ImmutableArray<string> AnalyzerConfigFiles,
    ImmutableArray<string> MetadataReferences,
    ImmutableArray<string> ProjectReferences,
    ImmutableArray<PreparedInputFile> InputFiles
);
