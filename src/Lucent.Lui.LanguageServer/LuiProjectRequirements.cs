using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;

namespace Lucent.Lui.LanguageServer;

/// <summary>Trusted, project-aware evidence emitted before a semantic LSP session begins.</summary>
internal static partial class LuiProjectRequirements
{
    private const long MaximumJsonBytes = 64 * 1024 * 1024;
    private const int MaximumProjects = 128;
    private const int MaximumInputs = 512;

    internal static async Task<int> WriteAsync(
        string projectPath,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (
                !Path.IsPathFullyQualified(projectPath)
                || !projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            )
                throw new RequirementFailure(
                    "invalid-project",
                    "A fully qualified .csproj path is required."
                );
            projectPath = Path.GetFullPath(projectPath);
            if (!File.Exists(projectPath))
                throw new RequirementFailure(
                    "invalid-project",
                    "The selected project does not exist."
                );

            if (!MSBuildLocator.IsRegistered)
                MSBuildLocator.RegisterDefaults();
            using var graph = await CurrentRestoreGraphAsync(projectPath, cancellationToken)
                .ConfigureAwait(false);
            var initialInputs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var initialEvaluations = new Dictionary<string, EvaluatedProject>(
                StringComparer.OrdinalIgnoreCase
            );
            foreach (var entry in graph.RootElement.GetProperty("projects").EnumerateObject())
            {
                var file = Path.GetFullPath(entry.Name);
                AddRestoreInputs(file, entry.Value, initialInputs);
                initialEvaluations.Add(
                    file,
                    EvaluateProjectImports(file, entry.Value, initialInputs)
                );
            }
            if (initialInputs.Count > MaximumInputs)
                throw new RequirementFailure(
                    "project-too-large",
                    "Project freshness inputs exceed the supported bound."
                );
            var initialHashes = SnapshotInputs(initialInputs, cancellationToken);
            using var context = await LuiProjectContext
                .LoadAsync(projectPath, cancellationToken)
                .ConfigureAwait(false);
            if (
                context
                    .EvaluationDiagnostics()
                    .Any(diagnostic => diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            )
                throw new RequirementFailure(
                    "workspace-incomplete",
                    "MSBuildWorkspace reported project load diagnostics; semantic requirements are unavailable."
                );
            var projects = context.EvaluatedProjectGraph();
            if (projects.Count > MaximumProjects)
                throw new RequirementFailure(
                    "project-too-large",
                    "The evaluated project graph exceeds the supported bound."
                );

            var inputs = new SortedSet<string>(initialInputs, StringComparer.OrdinalIgnoreCase);
            var descriptions = new List<object>();
            var compilerHashes = new HashSet<string>(StringComparer.Ordinal);
            var states = new HashSet<string>(StringComparer.Ordinal);
            var sdkIdentities = new HashSet<string>(StringComparer.Ordinal);
            var allPackages = new SortedDictionary<string, LucentPackage>(StringComparer.Ordinal);
            foreach (var project in projects)
            {
                var file = project.FilePath;
                if (file is null)
                    throw new RequirementFailure(
                        "workspace-incomplete",
                        "An evaluated project has no physical path."
                    );
                var spec = RestoreSpec(graph.RootElement, file);
                AddRestoreInputs(file, spec, inputs);
                using var assets = ReadAssets(spec, file, inputs);
                if (!EquivalentRestoreSpec(spec, assets.RootElement.GetProperty("project")))
                    throw new RequirementFailure(
                        "stale-restore",
                        "Current evaluated NuGet restore inputs differ from project.assets.json."
                    );
                var evaluated = EvaluateProjectImports(file, spec, inputs);
                if (
                    !initialEvaluations.TryGetValue(
                        Path.GetFullPath(file),
                        out var initialEvaluation
                    ) || !SameEvaluation(initialEvaluation, evaluated)
                )
                    throw new RequirementFailure(
                        "project-changed",
                        "Evaluated project imports changed during requirements discovery."
                    );
                var restoredPackages = ReadRestoredPackages(assets.RootElement);
                if (
                    restoredPackages
                        .GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
                        .Any(group =>
                            group
                                .Select(package => package.Version + "/" + package.ContentHash)
                                .Distinct(StringComparer.Ordinal)
                                .Skip(1)
                                .Any()
                        )
                )
                    throw new RequirementFailure(
                        "project-unsupported",
                        "A single-target project resolves multiple versions of one package; target-specific requirements are unavailable."
                    );
                var packages = restoredPackages
                    .Where(package => package.Id.StartsWith("Lucent.", StringComparison.Ordinal))
                    .ToArray();
                foreach (var package in packages)
                {
                    if (
                        allPackages.TryGetValue(package.Id, out var previous)
                        && previous != package
                    )
                        throw new RequirementFailure(
                            "package-ambiguous",
                            "The graph restores different versions or contents of a Lucent package."
                        );
                    allPackages[package.Id] = package;
                }
                VerifyOptionalLock(file, spec, restoredPackages, inputs);

                var analyzers = project
                    .AnalyzerReferences.Select(reference => reference.FullPath)
                    .Where(path =>
                        path is not null
                        && Path.GetFileName(path)
                            .Equals("Lucent.Lui.Compiler.dll", StringComparison.OrdinalIgnoreCase)
                    )
                    .Select(path => Path.GetFullPath(path!))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (analyzers.Length == 0)
                    continue;
                var hashes = analyzers
                    .Select(path => HashFile(path, cancellationToken))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (hashes.Length != 1)
                    throw new RequirementFailure(
                        "compiler-ambiguous",
                        "A project resolves different Lucent compiler analyzers."
                    );
                var compilerPath = analyzers[0];
                compilerHashes.Add(hashes[0]);
                inputs.Add(compilerPath);
                var sdkImports = evaluated.Imports.Where(IsLucentSdkImport).ToArray();
                if (sdkImports.Length == 0)
                {
                    if (
                        !evaluated.CompilerProjectTargets.Contains(
                            compilerPath,
                            StringComparer.OrdinalIgnoreCase
                        )
                    )
                        throw new RequirementFailure(
                            "sdk-metadata-unavailable",
                            "The resolved Lucent compiler has no verified package SDK or development project provenance."
                        );
                    states.Add("development-source");
                    descriptions.Add(
                        new
                        {
                            projectPath = Path.GetFullPath(file),
                            state = "development-source",
                            target = new
                            {
                                framework = evaluated.TargetFramework,
                                runtimeIdentifier = evaluated.RuntimeIdentifier,
                            },
                            sdk = (object?)null,
                        }
                    );
                    continue;
                }

                if (sdkImports.Length != 1)
                    throw new RequirementFailure(
                        "sdk-metadata-unavailable",
                        "The project imports multiple Lucent SDK targets."
                    );
                var sdk = ReadSdkPackage(sdkImports[0], compilerPath, inputs, cancellationToken);
                sdkIdentities.Add(sdk.Version + "/" + sdk.Commit + "/" + sdk.Hash);
                if (
                    !packages.Any(package =>
                        package.Id == "Lucent.Core" && package.Version == sdk.Version
                    )
                )
                    throw new RequirementFailure(
                        "package-unverified",
                        "The restored Lucent.Core package does not match the evaluated Lucent SDK."
                    );
                states.Add("package");
                descriptions.Add(
                    new
                    {
                        projectPath = Path.GetFullPath(file),
                        state = "package",
                        target = new
                        {
                            framework = evaluated.TargetFramework,
                            runtimeIdentifier = evaluated.RuntimeIdentifier,
                        },
                        sdk = new
                        {
                            id = "Lucent.Lui.Sdk",
                            version = sdk.Version,
                            repositoryCommit = sdk.Commit,
                            packageSha256 = sdk.Hash,
                        },
                    }
                );
            }
            if (descriptions.Count == 0)
                throw new RequirementFailure(
                    "no-lucent-project",
                    "The evaluated graph has no Lucent compiler requirement."
                );
            if (compilerHashes.Count != 1 || states.Count != 1)
                throw new RequirementFailure(
                    "compiler-ambiguous",
                    "The graph has mixed Lucent compiler or SDK modes."
                );
            if (states.Contains("package") && sdkIdentities.Count != 1)
                throw new RequirementFailure(
                    "compiler-ambiguous",
                    "The graph has mixed Lucent SDK package identities."
                );
            var compiler = projects
                .SelectMany(project => project.AnalyzerReferences)
                .Select(reference => reference.FullPath)
                .First(path =>
                    path is not null
                    && Path.GetFileName(path)
                        .Equals("Lucent.Lui.Compiler.dll", StringComparison.OrdinalIgnoreCase)
                )!;
            var version = FileVersionInfo.GetVersionInfo(compiler).ProductVersion ?? "";
            var commitMatch = CommitPattern().Match(version);
            if (
                states.Contains("package")
                && commitMatch.Success
                && !sdkIdentities
                    .Single()
                    .Contains(
                        "/" + commitMatch.Groups[1].Value.ToLowerInvariant() + "/",
                        StringComparison.Ordinal
                    )
            )
                throw new RequirementFailure(
                    "package-unverified",
                    "The compiler source identity differs from the SDK package."
                );
            using var finalGraph = await CurrentRestoreGraphAsync(projectPath, cancellationToken)
                .ConfigureAwait(false);
            if (!JsonElement.DeepEquals(graph.RootElement, finalGraph.RootElement))
                throw new RequirementFailure(
                    "project-changed",
                    "Evaluated restore inputs changed during requirements discovery."
                );
            if (!SameInputs(initialHashes, SnapshotInputs(initialInputs, cancellationToken)))
                throw new RequirementFailure(
                    "project-changed",
                    "A project input changed during requirements discovery."
                );
            var beforeOutput = SnapshotInputs(inputs, cancellationToken);
            var hashedInputs = beforeOutput
                .Select(entry => new { path = entry.Key, sha256 = entry.Value })
                .ToArray();
            if (!SameInputs(beforeOutput, SnapshotInputs(inputs, cancellationToken)))
                throw new RequirementFailure(
                    "project-changed",
                    "A project input changed during requirements discovery."
                );
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = 1,
                        kind = "project-requirements",
                        state = states.Single(),
                        semanticReady = false,
                        projectPath,
                        compiler = new
                        {
                            sha256 = compilerHashes.Single(),
                            informationalVersion = version,
                            sourceCommit = commitMatch.Success
                                ? commitMatch.Groups[1].Value.ToLowerInvariant()
                                : null,
                        },
                        projects = descriptions,
                        packages = allPackages
                            .Values.Select(package => new
                            {
                                id = package.Id,
                                version = package.Version,
                                contentHash = package.ContentHash,
                            })
                            .ToArray(),
                        inputs = hashedInputs,
                        restoreGraphSha256 = HashJson(graph.RootElement),
                    }
                )
            );
            return 0;
        }
        catch (RequirementFailure failure)
        {
            return Reject(failure.Code, failure.Message);
        }
        catch (OperationCanceledException)
        {
            return Reject("cancelled", "Project requirements discovery was cancelled.");
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or JsonException
                        or InvalidOperationException
                        or ArgumentException
            )
        {
            return Reject(
                "project-evaluation-failed",
                "The trusted project requirements evaluation did not complete."
            );
        }
    }

    private static int Reject(string code, string message)
    {
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 1,
                    kind = "project-requirements",
                    state = "unavailable",
                    error = new { code, message },
                }
            )
        );
        return 3;
    }

    private static async Task<JsonDocument> CurrentRestoreGraphAsync(
        string projectPath,
        CancellationToken cancellationToken
    )
    {
        var temporary = Directory.CreateTempSubdirectory("lucent-requirements-");
        try
        {
            var output = Path.Combine(temporary.FullName, "restore-graph.json");
            var start = new ProcessStartInfo(
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet"
            )
            {
                WorkingDirectory = Path.GetDirectoryName(projectPath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (
                var argument in new[]
                {
                    "msbuild",
                    projectPath,
                    "-target:GenerateRestoreGraphFile",
                    "-property:RestoreGraphOutputPath=" + output,
                    "-verbosity:quiet",
                    "-nologo",
                    "-nr:false",
                    "-m:1",
                }
            )
                start.ArgumentList.Add(argument);
            using var process =
                Process.Start(start)
                ?? throw new RequirementFailure(
                    "project-evaluation-failed",
                    "MSBuild could not start."
                );
            var standardOutput = process.StandardOutput.BaseStream.CopyToAsync(
                Stream.Null,
                CancellationToken.None
            );
            var standardError = process.StandardError.BaseStream.CopyToAsync(
                Stream.Null,
                CancellationToken.None
            );
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                throw cancellationToken.IsCancellationRequested
                    ? new OperationCanceledException(cancellationToken)
                    : new RequirementFailure(
                        "project-evaluation-failed",
                        "MSBuild restore-graph evaluation timed out."
                    );
            }
            await standardOutput.ConfigureAwait(false);
            await standardError.ConfigureAwait(false);
            if (process.ExitCode != 0 || !File.Exists(output))
                throw new RequirementFailure(
                    "project-evaluation-failed",
                    "MSBuild could not produce the current restore graph."
                );
            return ReadJson(output);
        }
        finally
        {
            var tempRoot =
                Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (temporary.FullName.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                temporary.Delete(recursive: true);
        }
    }

    private static JsonDocument ReadJson(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0 || file.Length > MaximumJsonBytes)
            throw new RequirementFailure(
                "restore-unavailable",
                "A bounded NuGet restore record is required."
            );
        using var stream = file.OpenRead();
        return JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 64 });
    }

    private static JsonElement RestoreSpec(JsonElement graph, string projectPath)
    {
        if (
            !graph.TryGetProperty("projects", out var projects)
            || projects.ValueKind != JsonValueKind.Object
        )
            throw new RequirementFailure(
                "restore-unavailable",
                "MSBuild returned no project restore graph."
            );
        foreach (var project in projects.EnumerateObject())
            if (
                Path.GetFullPath(project.Name)
                    .Equals(Path.GetFullPath(projectPath), StringComparison.OrdinalIgnoreCase)
            )
                return project.Value;
        throw new RequirementFailure(
            "restore-unavailable",
            "The evaluated project is absent from the restore graph."
        );
    }

    private static JsonDocument ReadAssets(
        JsonElement spec,
        string projectPath,
        SortedSet<string> inputs
    )
    {
        var outputPath = spec.GetProperty("restore").GetProperty("outputPath").GetString();
        if (string.IsNullOrWhiteSpace(outputPath) || !Path.IsPathFullyQualified(outputPath))
            throw new RequirementFailure(
                "restore-unavailable",
                "NuGet did not report a project assets location."
            );
        var assetsPath = Path.GetFullPath(Path.Combine(outputPath, "project.assets.json"));
        inputs.Add(assetsPath);
        if (!File.Exists(assetsPath))
            throw new RequirementFailure(
                "restore-unavailable",
                "The evaluated project has no restored assets."
            );
        var assets = ReadJson(assetsPath);
        if (
            !assets.RootElement.TryGetProperty("project", out var restored)
            || !Path.GetFullPath(
                    restored.GetProperty("restore").GetProperty("projectPath").GetString()!
                )
                .Equals(Path.GetFullPath(projectPath), StringComparison.OrdinalIgnoreCase)
        )
        {
            assets.Dispose();
            throw new RequirementFailure(
                "stale-restore",
                "Restored assets belong to another project."
            );
        }
        return assets;
    }

    private static EvaluatedProject EvaluateProjectImports(
        string projectPath,
        JsonElement spec,
        SortedSet<string> inputs
    )
    {
        AddControlInputs(projectPath, inputs);
        var frameworks = spec.GetProperty("restore")
            .GetProperty("originalTargetFrameworks")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        if (frameworks.Length != 1 || !ValidTargetIdentity(frameworks[0]))
            throw new RequirementFailure(
                "project-unsupported",
                "Project requirements currently need one selected target framework per project."
            );
        using var collection = new ProjectCollection();
        var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TargetFramework"] = frameworks[0]!,
        };
        var evaluated = collection.LoadProject(projectPath, globals, null);
        var framework = evaluated.GetPropertyValue("TargetFramework");
        var runtimeIdentifier = evaluated.GetPropertyValue("RuntimeIdentifier");
        if (
            !ValidTargetIdentity(framework)
            || !framework.Equals(frameworks[0], StringComparison.OrdinalIgnoreCase)
            || runtimeIdentifier.Length != 0 && !ValidTargetIdentity(runtimeIdentifier)
        )
            throw new RequirementFailure(
                "project-unsupported",
                "The evaluated target identity is malformed or differs from the selected restore framework."
            );
        var imports = evaluated
            .Imports.Select(import => Path.GetFullPath(import.ImportedProject.FullPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var import in imports)
        {
            inputs.Add(import);
            if (IsLucentSdkImport(import))
            {
                var packageRoot = Directory.GetParent(Path.GetDirectoryName(import)!)!.FullName;
                foreach (
                    var archive in Directory.GetFiles(
                        packageRoot,
                        "*.nupkg",
                        SearchOption.TopDirectoryOnly
                    )
                )
                    inputs.Add(archive);
                inputs.Add(
                    Path.Combine(
                        packageRoot,
                        "analyzers",
                        "dotnet",
                        "cs",
                        "Lucent.Lui.Compiler.dll"
                    )
                );
            }
        }
        var compilerTargets = new List<string>();
        foreach (var reference in evaluated.GetItems("ProjectReference"))
        {
            if (
                !reference
                    .GetMetadataValue("OutputItemType")
                    .Equals("Analyzer", StringComparison.OrdinalIgnoreCase)
            )
                continue;
            var referencedPath = Path.GetFullPath(
                reference.EvaluatedInclude,
                Path.GetDirectoryName(projectPath)!
            );
            if (
                !Path.GetFileName(referencedPath)
                    .Equals("Lucent.Lui.Compiler.csproj", StringComparison.OrdinalIgnoreCase)
            )
                continue;
            var compilerProject = collection.LoadProject(referencedPath);
            var targetPath = compilerProject.GetPropertyValue("TargetPath");
            if (Path.IsPathFullyQualified(targetPath))
            {
                compilerTargets.Add(Path.GetFullPath(targetPath));
                inputs.Add(Path.GetFullPath(targetPath));
            }
            inputs.Add(referencedPath);
            foreach (var import in compilerProject.Imports)
                inputs.Add(Path.GetFullPath(import.ImportedProject.FullPath));
        }
        return new(
            imports,
            compilerTargets,
            framework,
            runtimeIdentifier.Length == 0 ? null : runtimeIdentifier
        );
    }

    private static bool ValidTargetIdentity(string? value) =>
        value is { Length: > 0 and <= 128 }
        && char.IsAsciiLetterOrDigit(value[0])
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'
        );

    private static bool SameEvaluation(EvaluatedProject first, EvaluatedProject second) =>
        first.TargetFramework == second.TargetFramework
        && first.RuntimeIdentifier == second.RuntimeIdentifier
        && first
            .Imports.Order(StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(
                second.Imports.Order(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase
            )
        && first
            .CompilerProjectTargets.Order(StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(
                second.CompilerProjectTargets.Order(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase
            );

    private static void AddRestoreInputs(
        string projectPath,
        JsonElement spec,
        SortedSet<string> inputs
    )
    {
        AddControlInputs(projectPath, inputs);
        var restore = spec.GetProperty("restore");
        foreach (var config in restore.GetProperty("configFilePaths").EnumerateArray())
            if (config.GetString() is { } configPath && Path.IsPathFullyQualified(configPath))
                inputs.Add(Path.GetFullPath(configPath));
        var outputPath = restore.GetProperty("outputPath").GetString();
        if (!string.IsNullOrWhiteSpace(outputPath) && Path.IsPathFullyQualified(outputPath))
            inputs.Add(Path.GetFullPath(Path.Combine(outputPath, "project.assets.json")));
        foreach (var lockPath in LockSelection(projectPath, spec).Paths)
            inputs.Add(lockPath);
    }

    private static bool IsLucentSdkImport(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (
            directory is null
            || !Path.GetFileName(path).Equals("Sdk.targets", StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(directory).Equals("Sdk", StringComparison.OrdinalIgnoreCase)
        )
            return false;
        var packageRoot = Directory.GetParent(directory)?.Parent;
        return packageRoot?.Name.Equals("lucent.lui.sdk", StringComparison.OrdinalIgnoreCase)
            == true;
    }

    private static void AddControlInputs(string projectPath, SortedSet<string> inputs)
    {
        inputs.Add(Path.GetFullPath(projectPath));
        for (
            var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
            directory is not null;
            directory = Path.GetDirectoryName(directory)
        )
        {
            foreach (
                var name in new[]
                {
                    "global.json",
                    "Directory.Packages.props",
                    "Directory.Build.props",
                    "Directory.Build.targets",
                    "NuGet.Config",
                    "nuget.config",
                }
            )
                inputs.Add(Path.Combine(directory, name));
            if (inputs.Count > MaximumInputs)
                throw new RequirementFailure(
                    "project-too-large",
                    "Project freshness inputs exceed the supported bound."
                );
        }
    }

    private static SortedDictionary<string, string?> SnapshotInputs(
        IEnumerable<string> inputs,
        CancellationToken cancellationToken
    )
    {
        var snapshot = new SortedDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in inputs)
            snapshot[path] = File.Exists(path) ? HashFile(path, cancellationToken) : null;
        return snapshot;
    }

    private static bool SameInputs(
        SortedDictionary<string, string?> first,
        SortedDictionary<string, string?> second
    ) =>
        first.Count == second.Count
        && first.All(entry => second.TryGetValue(entry.Key, out var hash) && hash == entry.Value);

    private static bool EquivalentRestoreSpec(JsonElement current, JsonElement restored)
    {
        var left = JsonNode.Parse(current.GetRawText())!;
        var right = JsonNode.Parse(restored.GetRawText())!;
        // --locked-mode records the policy used by an earlier restore. The current graph
        // does not carry it; lock contents are checked separately below.
        left["restore"]?["restoreLockProperties"]?.AsObject().Remove("restoreLockedMode");
        right["restore"]?["restoreLockProperties"]?.AsObject().Remove("restoreLockedMode");
        if (
            OperatingSystem.IsWindows()
            && left["restore"] is JsonObject currentRestore
            && right["restore"] is JsonObject restoredRestore
        )
        {
            // VS Code file URIs can use a lower-case drive while NuGet preserves
            // the restore invocation's casing. Only declared physical path fields
            // receive Windows path comparison; package IDs and other values stay exact.
            foreach (
                var property in new[]
                {
                    "projectUniqueName",
                    "projectPath",
                    "outputPath",
                    "packagesPath",
                }
            )
                if (CaseEquivalentWindowsPath(currentRestore[property], restoredRestore[property]))
                    currentRestore[property] = restoredRestore[property]!.DeepClone();
            if (
                currentRestore["configFilePaths"] is JsonArray currentConfigs
                && restoredRestore["configFilePaths"] is JsonArray restoredConfigs
                && currentConfigs.Count == restoredConfigs.Count
            )
                for (var index = 0; index < currentConfigs.Count; index++)
                    if (CaseEquivalentWindowsPath(currentConfigs[index], restoredConfigs[index]))
                        currentConfigs[index] = restoredConfigs[index]!.DeepClone();
        }
        return JsonNode.DeepEquals(left, right);
    }

    private static bool CaseEquivalentWindowsPath(JsonNode? current, JsonNode? restored) =>
        current is JsonValue currentValue
        && restored is JsonValue restoredValue
        && currentValue.TryGetValue<string>(out var currentPath)
        && restoredValue.TryGetValue<string>(out var restoredPath)
        && Path.IsPathFullyQualified(currentPath)
        && Path.IsPathFullyQualified(restoredPath)
        && string.Equals(currentPath, restoredPath, StringComparison.OrdinalIgnoreCase);

    private static List<LucentPackage> ReadRestoredPackages(JsonElement assets)
    {
        var result = new List<LucentPackage>();
        foreach (var library in assets.GetProperty("libraries").EnumerateObject())
        {
            var slash = library.Name.IndexOf('/');
            if (slash < 0 || library.Value.GetProperty("type").GetString() != "package")
                continue;
            result.Add(
                new(
                    library.Name[..slash],
                    library.Name[(slash + 1)..],
                    library.Value.GetProperty("sha512").GetString() ?? ""
                )
            );
        }
        return result;
    }

    private static void VerifyOptionalLock(
        string projectPath,
        JsonElement spec,
        IReadOnlyList<LucentPackage> packages,
        SortedSet<string> inputs
    )
    {
        var selection = LockSelection(projectPath, spec);
        foreach (var path in selection.Paths)
            inputs.Add(path);
        var present = selection.Paths.Where(File.Exists).ToArray();
        if (present.Length > 1)
            throw new RequirementFailure(
                "lock-ambiguous",
                "Multiple evaluated NuGet lock files are present."
            );
        if (present.Length == 0)
        {
            if (selection.Required)
                throw new RequirementFailure(
                    "restore-unavailable",
                    "The evaluated project requires a NuGet lock file that is absent."
                );
            return;
        }
        var lockPath = present[0];
        using var document = ReadJson(lockPath);
        var dependencies = document.RootElement.GetProperty("dependencies");
        foreach (var package in packages)
        {
            var matches = dependencies
                .EnumerateObject()
                .Select(framework =>
                    framework.Value.TryGetProperty(package.Id, out var item) ? item : default
                )
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .ToArray();
            if (
                matches.Length == 0
                || matches.Any(item =>
                    item.GetProperty("resolved").GetString() != package.Version
                    || item.GetProperty("contentHash").GetString() != package.ContentHash
                )
            )
                throw new RequirementFailure(
                    "stale-restore",
                    "Lucent package lock and restored assets differ."
                );
        }
    }

    private static EvaluatedLockSelection LockSelection(string projectPath, JsonElement spec)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        var restore = spec.GetProperty("restore");
        if (!restore.TryGetProperty("restoreLockProperties", out var properties))
            return DefaultLockSelection(directory, projectPath, required: false);
        var required =
            LockEnabled(properties, "restorePackagesWithLockFile")
            || LockEnabled(properties, "restoreLockedMode");
        if (
            properties.TryGetProperty("nuGetLockFilePath", out var custom)
            && custom.GetString() is { Length: > 0 } customPath
        )
        {
            var resolved = Path.IsPathFullyQualified(customPath)
                ? Path.GetFullPath(customPath)
                : Path.GetFullPath(customPath, directory);
            return new([resolved], required);
        }
        return DefaultLockSelection(directory, projectPath, required);
    }

    private static EvaluatedLockSelection DefaultLockSelection(
        string directory,
        string projectPath,
        bool required
    ) =>
        new(
            [
                Path.Combine(directory, "packages.lock.json"),
                Path.Combine(
                    directory,
                    "packages." + Path.GetFileNameWithoutExtension(projectPath) + ".lock.json"
                ),
            ],
            required
        );

    private static bool LockEnabled(JsonElement properties, string name) =>
        properties.TryGetProperty(name, out var value)
        && (
            value.ValueKind == JsonValueKind.True
            || value.ValueKind == JsonValueKind.String
                && value.GetString()!.Equals("true", StringComparison.OrdinalIgnoreCase)
        );

    private static SdkPackage ReadSdkPackage(
        string targetsPath,
        string compilerPath,
        SortedSet<string> inputs,
        CancellationToken cancellationToken
    )
    {
        targetsPath = Path.GetFullPath(targetsPath);
        var sdkDirectory = Path.GetDirectoryName(targetsPath)!;
        if (
            Path.GetFileName(targetsPath) != "Sdk.targets"
            || Path.GetFileName(sdkDirectory) != "Sdk"
        )
            throw new RequirementFailure(
                "sdk-metadata-unavailable",
                "The evaluated SDK import is not a supported package layout."
            );
        var root = Directory.GetParent(sdkDirectory)!.FullName;
        var archives = Directory.GetFiles(root, "*.nupkg", SearchOption.TopDirectoryOnly);
        if (archives.Length != 1)
            throw new RequirementFailure(
                "package-unverified",
                "The evaluated SDK has no unique package archive."
            );
        var archivePath = archives[0];
        inputs.Add(targetsPath);
        inputs.Add(archivePath);
        inputs.Add(compilerPath);
        using var archive = ZipFile.OpenRead(archivePath);
        var nuspec = archive.Entries.SingleOrDefault(entry =>
            entry.FullName.Equals("Lucent.Lui.Sdk.nuspec", StringComparison.OrdinalIgnoreCase)
        );
        var targets = archive.Entries.SingleOrDefault(entry =>
            entry.FullName.Equals("Sdk/Sdk.targets", StringComparison.OrdinalIgnoreCase)
        );
        var compiler = archive.Entries.SingleOrDefault(entry =>
            entry.FullName.Equals(
                "analyzers/dotnet/cs/Lucent.Lui.Compiler.dll",
                StringComparison.OrdinalIgnoreCase
            )
        );
        if (nuspec is null || targets is null || compiler is null)
            throw new RequirementFailure(
                "package-unverified",
                "The SDK package omits its provenance or compiler."
            );
        if (
            HashEntry(targets, cancellationToken) != HashFile(targetsPath, cancellationToken)
            || HashEntry(compiler, cancellationToken) != HashFile(compilerPath, cancellationToken)
        )
            throw new RequirementFailure(
                "package-unverified",
                "The evaluated SDK import or compiler differs from the package archive."
            );
        using var stream = nuspec.Open();
        var xml = XDocument.Load(stream, LoadOptions.None);
        var metadata = xml
            .Root?.Elements()
            .SingleOrDefault(item => item.Name.LocalName == "metadata");
        var id = metadata?.Elements().SingleOrDefault(item => item.Name.LocalName == "id")?.Value;
        var version = metadata
            ?.Elements()
            .SingleOrDefault(item => item.Name.LocalName == "version")
            ?.Value;
        var commit = metadata
            ?.Elements()
            .SingleOrDefault(item => item.Name.LocalName == "repository")
            ?.Attribute("commit")
            ?.Value;
        if (
            id != "Lucent.Lui.Sdk"
            || string.IsNullOrWhiteSpace(version)
            || commit is null
            || !SourceCommitPattern().IsMatch(commit)
        )
            throw new RequirementFailure(
                "package-unverified",
                "The SDK package has no supported version and source identity."
            );
        return new(version, commit.ToLowerInvariant(), HashFile(archivePath, cancellationToken));
    }

    private static string HashEntry(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        using var stream = entry.Open();
        return HashStream(stream, cancellationToken);
    }

    private static string HashFile(string path, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        return HashStream(stream, cancellationToken);
    }

    private static string HashStream(Stream stream, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            int read;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    break;
                hash.AppendData(buffer, 0, read);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string HashJson(JsonElement element) =>
        Convert
            .ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(element)))
            .ToLowerInvariant();

    [GeneratedRegex(@"\+([0-9a-fA-F]{40})(?:\.|$)")]
    private static partial Regex CommitPattern();

    [GeneratedRegex(@"^[0-9a-fA-F]{40}$")]
    private static partial Regex SourceCommitPattern();

    private sealed record LucentPackage(string Id, string Version, string ContentHash);

    private sealed record SdkPackage(string Version, string Commit, string Hash);

    private sealed record EvaluatedProject(
        string[] Imports,
        List<string> CompilerProjectTargets,
        string TargetFramework,
        string? RuntimeIdentifier
    );

    private sealed record EvaluatedLockSelection(string[] Paths, bool Required);

    private sealed class RequirementFailure(string code, string message) : Exception(message)
    {
        internal string Code { get; } = code;
    }
}
