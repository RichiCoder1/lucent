using System.Text.Json;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Globbing;
using Microsoft.Build.Graph;

namespace Lucent.Preview.Build;

internal static class EvaluatedInputs
{
    private static readonly string[] ItemKinds =
    [
        "Compile",
        "AdditionalFiles",
        "EditorConfigFiles",
        "Analyzer",
        "Reference",
        "ProjectReference",
        "EmbeddedResource",
        "Resource",
        "Content",
        "None",
        "LucentAsset",
        "LucentPreviewInput",
    ];
    private static readonly string[] PropertyNames =
    [
        "TargetFramework",
        "TargetFrameworks",
        "RuntimeIdentifier",
        "RuntimeIdentifiers",
        "Configuration",
        "Platform",
        "PlatformTarget",
        "OutputType",
        "AssemblyName",
        "RootNamespace",
        "LangVersion",
        "Nullable",
        "DefineConstants",
        "ImplicitUsings",
        "Optimize",
        "AllowUnsafeBlocks",
        "TreatWarningsAsErrors",
        "NoWarn",
        "WarningsAsErrors",
        "ApplicationIcon",
        "ApplicationManifest",
        "IntermediateOutputPath",
        "OutputPath",
        "PublishDir",
        "MSBuildProjectExtensionsPath",
        "RestoreOutputPath",
        "OutDir",
        "TargetPath",
        "NuGetLockFilePath",
        "LucentPreviewOriginalNuGetLockFilePath",
        "RestorePackagesWithLockFile",
        "RestoreLockedMode",
        "ProjectAssetsFile",
        "RestorePackagesPath",
        "LucentLuiNamedComponents",
        "LucentLuiPreparedAuthoring",
        "LucentLuiLangVersion",
        "LucentAssetDomain",
        "LucentAssetAccessorNamespace",
        "LucentAssetAccessorClass",
    ];

    internal static Dictionary<string, string> Globals(PreviewBuildRequest request, bool isolated)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = request.Configuration,
            ["TargetFramework"] = request.TargetFramework,
            ["RuntimeIdentifier"] = request.RuntimeIdentifier,
            ["SelfContained"] = "true",
            ["PublishAot"] = "false",
            ["PublishTrimmed"] = "false",
            ["UseSharedCompilation"] = "false",
            ["MSBuildNodeReuse"] = "false",
        };
        if (isolated)
        {
            values["UseArtifactsOutput"] = "true";
            values["ArtifactsPath"] = Path.Combine(request.OutputDirectory, "artifacts");
            values["CustomAfterDirectoryBuildProps"] = Path.Combine(
                AppContext.BaseDirectory,
                "Preview.props"
            );
            values["CustomAfterDirectoryBuildTargets"] = Path.Combine(
                AppContext.BaseDirectory,
                "Preview.targets"
            );
            values["LucentPreviewBuildAssembly"] = typeof(EvaluatedInputs).Assembly.Location;
        }
        return values;
    }

    internal static ProjectSnapshot[] Capture(
        PreviewBuildRequest request,
        bool isolated,
        bool restoreContext = false
    )
    {
        using var collection = new ProjectCollection();
        if (!isolated)
        {
            var authored = collection.LoadProject(
                request.ProjectPath,
                new Dictionary<string, string> { ["Configuration"] = request.Configuration },
                null
            );
            var declaredRid = authored.GetPropertyValue("RuntimeIdentifier");
            var declaredPlatform = authored.GetPropertyValue("PlatformTarget");
            if (
                !String.IsNullOrEmpty(declaredRid) && declaredRid != "win-x64"
                || declaredPlatform is not ("" or "AnyCPU" or "x64")
            )
                throw new InvalidOperationException(
                    "The preview executable declares an unsupported runtime/platform target."
                );
            var declaredFramework = authored.GetPropertyValue("TargetFramework");
            var frameworkList = authored
                .GetPropertyValue("TargetFrameworks")
                .Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (
                declaredFramework != request.TargetFramework
                && !frameworkList.Contains(request.TargetFramework, StringComparer.Ordinal)
            )
                throw new InvalidOperationException(
                    "The requested preview framework is not declared by the executable."
                );
            collection.UnloadAllProjects();
        }
        var graphGlobals = Globals(request, isolated);
        if (restoreContext)
            graphGlobals.Remove("TargetFramework");
        var graph = new ProjectGraph(request.ProjectPath, graphGlobals, collection);
        if (graph.ProjectNodes.Count > 128)
            throw new InvalidOperationException("The preview graph exceeds 128 nodes.");
        var snapshots = new List<ProjectSnapshot>();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in graph.ProjectNodes)
        {
            var instance = node.ProjectInstance;
            if (!instance.FullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "The initial preview graph supports C# projects only."
                );
            var project = collection.LoadProject(
                instance.FullPath,
                instance.GlobalProperties,
                null
            );
            var globals = new SortedDictionary<string, string>(
                instance.GlobalProperties,
                StringComparer.Ordinal
            );
            var canonical = BuildData.CanonicalPath(project.FullPath);
            var outputRelative = Path.GetRelativePath(
                project.DirectoryPath,
                request.OutputDirectory
            );
            if (
                !outputRelative.StartsWith(
                    ".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal
                )
                && outputRelative != ".."
                && !Path.IsPathFullyQualified(outputRelative)
            )
                throw new InvalidOperationException(
                    "Preview generation outputs must be outside every source project directory."
                );
            if (!isolated)
            {
                var knownArtifactsHook = BuildData.CanonicalPath(
                    Path.Combine(
                        project.GetPropertyValue("MSBuildSDKsPath"),
                        "Microsoft.NET.Sdk",
                        "Sdk",
                        "UseArtifactsOutputPath.props"
                    )
                );
                foreach (
                    var hook in new[]
                    {
                        "CustomAfterDirectoryBuildProps",
                        "CustomAfterDirectoryBuildTargets",
                    }
                )
                    if (
                        project
                            .GetPropertyValue(hook)
                            .Split(';', StringSplitOptions.RemoveEmptyEntries)
                            .Any(value =>
                                hook != "CustomAfterDirectoryBuildProps"
                                || BuildData.CanonicalPath(value) != knownArtifactsHook
                            )
                    )
                        throw new InvalidOperationException(
                            "Preview output isolation requires an unoccupied "
                                + hook
                                + " extension point."
                        );
            }
            if (project.GetPropertyValue("IsCrossTargetingBuild") == "true")
                throw new InvalidOperationException(
                    "Cross-targeting preview graph nodes are unsupported. Select a single target."
                );
            var platform = project.GetPropertyValue("PlatformTarget");
            if (platform is not ("" or "AnyCPU" or "x64"))
                throw new InvalidOperationException(
                    "The preview project declares an unsupported platform target."
                );
            var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in PropertyNames)
                properties.Add(name, project.GetPropertyValue(name));
            var items = new List<EvaluatedItem>();
            if (isolated)
                foreach (var element in project.GetLogicalProject().OfType<ProjectItemElement>())
                {
                    if (!ItemKinds.Contains(element.ItemType, StringComparer.Ordinal))
                        continue;
                    var parent = element.Parent;
                    while (parent is not null && parent is not ProjectTargetElement)
                        parent = parent.Parent;
                    if (parent is null)
                        continue;
                    var include = project.ExpandString(element.Include);
                    if (!include.Contains('*') && !include.Contains('?'))
                        continue;
                    var parsed = MSBuildGlob.Parse(project.DirectoryPath, include);
                    if (!parsed.IsLegal)
                        throw new InvalidOperationException(
                            "A target-produced input glob is unsupported; declare its inputs explicitly."
                        );
                    var relative = Path.GetRelativePath(
                        request.OutputDirectory,
                        parsed.FixedDirectoryPart
                    );
                    if (
                        Path.IsPathFullyQualified(relative)
                        || relative == ".."
                        || relative.StartsWith(
                            ".." + Path.DirectorySeparatorChar,
                            StringComparison.Ordinal
                        )
                    )
                        throw new InvalidOperationException(
                            "Target-time external input globs are unsupported; move the glob to evaluated items and declare custom dependencies."
                        );
                }
            var globs = new List<EvaluatedGlob>();
            foreach (var result in project.GetAllGlobs())
            {
                if (!ItemKinds.Contains(result.ItemElement.ItemType, StringComparer.Ordinal))
                    continue;
                foreach (var pattern in result.IncludeGlobs)
                {
                    var glob = MSBuildGlob.Parse(project.DirectoryPath, pattern);
                    if (!glob.IsLegal)
                        throw new InvalidOperationException(
                            "An evaluated preview glob is unsupported: "
                                + result.ItemElement.ItemType
                        );
                    var watchRoot = BuildData
                        .CanonicalPath(glob.FixedDirectoryPart)
                        .TrimEnd(Path.DirectorySeparatorChar);
                    var sdkRoot = BuildData.CanonicalPath(
                        project.GetPropertyValue("MSBuildToolsPath")
                    );
                    var packageRoot = project.GetPropertyValue("NuGetPackageRoot");
                    if (
                        watchRoot.StartsWith(
                            sdkRoot + Path.DirectorySeparatorChar,
                            StringComparison.Ordinal
                        )
                        || watchRoot == sdkRoot
                        || !String.IsNullOrEmpty(packageRoot)
                            && watchRoot.StartsWith(
                                BuildData
                                    .CanonicalPath(packageRoot)
                                    .TrimEnd(Path.DirectorySeparatorChar)
                                    + Path.DirectorySeparatorChar,
                                StringComparison.Ordinal
                            )
                    )
                        throw new InvalidOperationException(
                            "Recursive preview globs over SDK/package caches are unsupported; use explicit file references."
                        );
                    if (
                        watchRoot
                            == Path.GetPathRoot(watchRoot)?.TrimEnd(Path.DirectorySeparatorChar)
                        || globs.Count >= 512
                    )
                        throw new InvalidOperationException(
                            "A preview glob requires an unsupported broad watch root."
                        );
                    globs.Add(
                        new(
                            result.ItemElement.ItemType,
                            pattern,
                            watchRoot,
                            result.Excludes.Order(StringComparer.Ordinal).ToArray(),
                            result.Removes.Order(StringComparer.Ordinal).ToArray()
                        )
                    );
                }
            }
            var paths = new SortedSet<string>(StringComparer.Ordinal);
            paths.Add(canonical);
            paths.Add(BuildData.CanonicalPath(typeof(EvaluatedInputs).Assembly.Location));
            var sdkTools = project.GetPropertyValue("MSBuildToolsPath");
            foreach (
                var tool in new[]
                {
                    "MSBuild.dll",
                    "Microsoft.Build.dll",
                    "Microsoft.Build.Framework.dll",
                    "Microsoft.Build.Tasks.Core.dll",
                    "Microsoft.Build.Utilities.Core.dll",
                }
            )
                paths.Add(BuildData.CanonicalPath(Path.Combine(sdkTools, tool)));
            var compilerDirectory = Path.Combine(sdkTools, "Roslyn", "bincore");
            foreach (
                var tool in new[]
                {
                    "csc.dll",
                    "csc.deps.json",
                    "csc.runtimeconfig.json",
                    "Microsoft.CodeAnalysis.dll",
                    "Microsoft.CodeAnalysis.CSharp.dll",
                }
            )
                paths.Add(BuildData.CanonicalPath(Path.Combine(compilerDirectory, tool)));
            foreach (var import in project.Imports)
                paths.Add(BuildData.CanonicalPath(import.ImportedProject.FullPath));
            for (
                var directory = Path.GetDirectoryName(canonical);
                directory is not null;
                directory = Path.GetDirectoryName(directory)
            )
                foreach (
                    var name in new[]
                    {
                        "global.json",
                        "Directory.Build.props",
                        "Directory.Build.targets",
                        "Directory.Packages.props",
                        "NuGet.Config",
                        "packages.lock.json",
                        ".editorconfig",
                    }
                )
                    paths.Add(BuildData.CanonicalPath(Path.Combine(directory, name)));
            paths.Add(
                BuildData.CanonicalPath(Path.Combine(project.DirectoryPath, "packages.lock.json"))
            );
            paths.Add(
                BuildData.CanonicalPath(
                    Path.Combine(
                        project.DirectoryPath,
                        "packages."
                            + Path.GetFileNameWithoutExtension(project.FullPath).Replace(' ', '_')
                            + ".lock.json"
                    )
                )
            );
            var originalLock = project.GetPropertyValue("LucentPreviewOriginalNuGetLockFilePath");
            if (isolated)
            {
                if (String.IsNullOrEmpty(originalLock))
                    throw new InvalidOperationException(
                        "The preview lock isolation hook did not run."
                    );
                paths.Add(
                    BuildData.CanonicalPath(Path.GetFullPath(originalLock, project.DirectoryPath))
                );
            }
            var lockFile = project.GetPropertyValue("NuGetLockFilePath");
            if (!String.IsNullOrEmpty(lockFile))
                paths.Add(
                    BuildData.CanonicalPath(Path.GetFullPath(lockFile, project.DirectoryPath))
                );
            var assets = project.GetPropertyValue("ProjectAssetsFile");
            if (!String.IsNullOrEmpty(assets))
            {
                paths.Add(BuildData.CanonicalPath(assets));
                if (File.Exists(assets))
                {
                    if (new FileInfo(assets).Length > 64 * 1024 * 1024)
                        throw new InvalidOperationException(
                            "Preview restore assets exceed their supported bound."
                        );
                    using var restored = JsonDocument.Parse(File.ReadAllText(assets));
                    if (
                        restored.RootElement.TryGetProperty("project", out var restoredProject)
                        && restoredProject.TryGetProperty("restore", out var restore)
                        && restore.TryGetProperty("configFilePaths", out var configs)
                    )
                        foreach (var config in configs.EnumerateArray())
                            paths.Add(BuildData.CanonicalPath(config.GetString()!));
                }
            }
            foreach (var name in new[] { "ApplicationIcon", "ApplicationManifest" })
                if (!String.IsNullOrEmpty(properties[name]))
                    paths.Add(
                        BuildData.CanonicalPath(
                            Path.GetFullPath(properties[name], project.DirectoryPath)
                        )
                    );
            foreach (var kind in ItemKinds)
            {
                foreach (var item in project.GetItems(kind))
                {
                    var metadata = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    foreach (var value in item.Metadata)
                        metadata[value.Name] = value.EvaluatedValue;
                    items.Add(new(kind, item.EvaluatedInclude, metadata));
                    // Assembly names are not filesystem references; resolved ReferencePath is captured at compilation.
                    if (kind == "Reference")
                    {
                        var hint = item.GetMetadataValue("HintPath");
                        if (!String.IsNullOrEmpty(hint))
                            paths.Add(
                                BuildData.CanonicalPath(
                                    Path.GetFullPath(hint, project.DirectoryPath)
                                )
                            );
                    }
                    else
                        paths.Add(BuildData.CanonicalPath(item.GetMetadataValue("FullPath")));
                    if (
                        kind == "LucentAsset"
                        && !String.IsNullOrEmpty(item.GetMetadataValue("IconFile"))
                    )
                        paths.Add(
                            BuildData.CanonicalPath(
                                Path.GetFullPath(
                                    item.GetMetadataValue("IconFile"),
                                    project.DirectoryPath
                                )
                            )
                        );
                }
            }
            foreach (var extra in request.ExtraInputs)
                paths.Add(BuildData.CanonicalPath(extra));
            if (items.Count > BuildData.MaximumInputs || paths.Count > BuildData.MaximumInputs)
                throw new InvalidOperationException(
                    "Evaluated preview inputs exceed the supported bound."
                );
            if (isolated)
            {
                foreach (
                    var output in new[]
                    {
                        "IntermediateOutputPath",
                        "OutputPath",
                        "PublishDir",
                        "MSBuildProjectExtensionsPath",
                        "RestoreOutputPath",
                        "OutDir",
                        "TargetPath",
                        "NuGetLockFilePath",
                    }
                )
                {
                    if (String.IsNullOrEmpty(properties[output]))
                        continue;
                    BuildData.RequireInside(
                        Path.GetFullPath(properties[output], project.DirectoryPath),
                        request.OutputDirectory
                    );
                }
                var artifactsName = project.GetPropertyValue("ArtifactsProjectName");
                var isolatedLock = BuildData.CanonicalPath(
                    Path.Combine(
                        request.OutputDirectory,
                        "artifacts",
                        "locks",
                        artifactsName,
                        "packages.lock.json"
                    )
                );
                if (
                    BuildData.CanonicalPath(
                        Path.GetFullPath(properties["NuGetLockFilePath"], project.DirectoryPath)
                    ) != isolatedLock
                )
                    throw new InvalidOperationException(
                        "A later customization overrode preview NuGet lock isolation."
                    );
                if (names.TryGetValue(artifactsName, out var previous) && previous != canonical)
                    throw new InvalidOperationException(
                        "Two preview projects share an artifacts namespace."
                    );
                names[artifactsName] = canonical;
            }
            var digest = BuildData.Digest(
                new { projectPath = canonical, globalProperties = globals }
            );
            if (isolated)
            {
                var directory = BuildData.CanonicalPath(
                    Path.GetFullPath(properties["IntermediateOutputPath"], project.DirectoryPath)
                );
                if (
                    names.TryGetValue(directory, out var previousContext)
                    && previousContext != digest
                )
                    throw new InvalidOperationException(
                        "Distinct preview graph contexts share intermediate outputs."
                    );
                names[directory] = digest;
            }
            snapshots.Add(
                new(
                    canonical,
                    digest,
                    globals,
                    properties,
                    items.ToArray(),
                    globs.ToArray(),
                    paths.Select(BuildData.Snapshot).ToArray()
                )
            );
        }
        return snapshots.OrderBy(project => project.NodeDigest, StringComparer.Ordinal).ToArray();
    }
}
