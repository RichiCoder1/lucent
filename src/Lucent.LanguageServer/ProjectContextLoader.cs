using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Lucent.Compiler;

namespace Lucent.LanguageServer;

internal sealed class ProjectContextLoader
{
    private const int MaxContexts = 8;
    private const int MaxSourceProjects = 256;
    private readonly StringComparer _pathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private readonly Dictionary<string, CachedProjectContext> _contexts;
    private readonly Dictionary<string, string> _sourceProjects;
    private IReadOnlyList<string> _workspaceRoots = [];

    public ProjectContextLoader()
    {
        _contexts = new Dictionary<string, CachedProjectContext>(_pathComparer);
        _sourceProjects = new Dictionary<string, string>(_pathComparer);
    }

    public void Configure(JsonElement initializeParameters)
    {
        var roots = new List<string>();
        if (initializeParameters.ValueKind == JsonValueKind.Object &&
            initializeParameters.TryGetProperty("workspaceFolders", out var folders) &&
            folders.ValueKind == JsonValueKind.Array)
        {
            foreach (var folder in folders.EnumerateArray())
            {
                if (folder.TryGetProperty("uri", out var uri))
                {
                    AddFileUri(roots, uri.GetString());
                }
            }
        }

        if (roots.Count == 0 &&
            initializeParameters.ValueKind == JsonValueKind.Object &&
            initializeParameters.TryGetProperty("rootUri", out var rootUri))
        {
            AddFileUri(roots, rootUri.GetString());
        }

        _workspaceRoots = roots.Distinct(_pathComparer).ToArray();
        _contexts.Clear();
        _sourceProjects.Clear();
        LanguageServerLog.WorkspaceConfigured(
            LanguageServerLog.Logger,
            string.Join(Path.PathSeparator, _workspaceRoots));
    }

    public async Task<LucentProjectContext?> LoadAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(sourcePath))
        {
            return null;
        }

        var projectPath = FindProject(sourcePath);
        if (projectPath is null)
        {
            LanguageServerLog.ProjectNotFound(LanguageServerLog.Logger, sourcePath);
            return null;
        }

        LanguageServerLog.ProjectSelected(
            LanguageServerLog.Logger,
            projectPath,
            sourcePath);

        var stamp = _contexts.TryGetValue(projectPath, out var cachedForStamp)
            ? GetProjectStamp(projectPath, cachedForStamp.Context)
            : GetProjectStamp(projectPath, context: null);
        if (_contexts.TryGetValue(projectPath, out var cached) &&
            cached.Stamp == stamp)
        {
            LanguageServerLog.ProjectCacheHit(LanguageServerLog.Logger, projectPath);
            return cached.Context;
        }

        LucentProjectContext? context;
        try
        {
            context = await LoadProjectAsync(projectPath, cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or JsonException or
            System.ComponentModel.Win32Exception)
        {
            LanguageServerLog.ProjectLoadFailed(
                LanguageServerLog.Logger,
                projectPath,
                exception);
            context = CreateFallbackContext(projectPath);
        }

        if (context is not null)
        {
            foreach (var source in _sourceProjects.Where(entry => _pathComparer.Equals(entry.Value, projectPath))
                         .Select(entry => entry.Key).ToArray())
                _sourceProjects.Remove(source);
            _contexts[projectPath] = new CachedProjectContext(
                context,
                GetProjectStamp(projectPath, context));
            while (_contexts.Count > MaxContexts)
                _contexts.Remove(_contexts.Keys.First());
            LanguageServerLog.ProjectLoaded(
                LanguageServerLog.Logger,
                projectPath,
                context.Sources.Count,
                context.References.Count);
        }

        return context;
    }

    private static ProjectStamp GetProjectStamp(
        string projectPath,
        LucentProjectContext? context)
    {
        var hash = new HashCode();
        AddFile(projectPath);
        AddDirectory(Path.GetDirectoryName(projectPath)!);
        foreach (var path in context?.Sources ?? [])
        {
            AddFile(path);
            AddDirectory(Path.GetDirectoryName(path)!);
        }
        foreach (var path in context?.References ?? [])
        {
            AddFile(path);
        }

        return new ProjectStamp(hash.ToHashCode());

        void AddFile(string path)
        {
            try
            {
                var file = new FileInfo(path);
                hash.Add(Path.GetFullPath(path), StringComparer.OrdinalIgnoreCase);
                hash.Add(file.Exists);
                hash.Add(file.Exists ? file.Length : 0L);
                hash.Add(file.Exists ? file.LastWriteTimeUtc : DateTime.MinValue);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                hash.Add(path, StringComparer.OrdinalIgnoreCase);
            }
        }

        void AddDirectory(string path)
        {
            try
            {
                hash.Add(Path.GetFullPath(path), StringComparer.OrdinalIgnoreCase);
                hash.Add(Directory.GetLastWriteTimeUtc(path));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                hash.Add(path, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    private string? FindProject(string sourcePath)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (_sourceProjects.TryGetValue(fullSourcePath, out var knownProject) &&
            File.Exists(knownProject))
        {
            return knownProject;
        }
        var roots = _workspaceRoots;
        if (FindFallbackRoot(fullSourcePath) is { } fallbackRoot &&
            !roots.Contains(fallbackRoot, _pathComparer))
        {
            roots = roots.Append(fallbackRoot).ToArray();
        }

        var projects = roots
            .Where(Directory.Exists)
            .SelectMany(EnumerateProjects)
            .Distinct(_pathComparer)
            .ToArray();

        foreach (var project in projects)
        {
            if (ProjectIncludesSource(project, fullSourcePath))
            {
                RememberSourceProject(fullSourcePath, project);
                return project;
            }
        }

        var sourceDirectory = Path.GetDirectoryName(fullSourcePath);
        var fallback = projects
            .Where(project => IsWithin(
                sourceDirectory,
                Path.GetDirectoryName(project)))
            .OrderByDescending(project => Path.GetDirectoryName(project)!.Length)
            .FirstOrDefault();
        if (fallback is not null)
        {
            RememberSourceProject(fullSourcePath, fallback);
        }
        return fallback;
    }

    private void RememberSourceProject(string sourcePath, string projectPath)
    {
        _sourceProjects[sourcePath] = projectPath;
        while (_sourceProjects.Count > MaxSourceProjects)
            _sourceProjects.Remove(_sourceProjects.Keys.First());
    }

    private static string? FindFallbackRoot(string sourcePath)
    {
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(sourcePath)!);
             directory is not null;
             directory = directory.Parent)
        {
            try
            {
                if (directory.EnumerateFiles("*.csproj").Any() ||
                    directory.EnumerateFiles("*.sln").Any() ||
                    Directory.Exists(Path.Combine(directory.FullName, ".git")))
                {
                    return directory.FullName;
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateProjects(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            string[] projects;
            string[] children;
            try
            {
                projects = Directory.GetFiles(directory, "*.csproj");
                children = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var project in projects)
            {
                yield return project;
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (!IgnoredDirectoryNames.Contains(name))
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static readonly IReadOnlySet<string> IgnoredDirectoryNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin",
            "obj",
            ".git",
            "node_modules",
        };

    private bool ProjectIncludesSource(string projectPath, string sourcePath)
    {
        try
        {
            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            var document = XDocument.Load(projectPath);
            var lucentSources = document.Descendants()
                .Where(element => element.Name.LocalName == "LucentSource")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Where(include => !include!.Contains('*') && !include.Contains('$'))
                .Select(include => Path.GetFullPath(include!, projectDirectory))
                .ToArray();
            return lucentSources.Any(path => _pathComparer.Equals(path, sourcePath)) ||
                sourcePath.EndsWith(".css", StringComparison.OrdinalIgnoreCase) &&
                lucentSources.Any(path => _pathComparer.Equals(
                    Path.ChangeExtension(path, ".css"), sourcePath));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            System.Xml.XmlException or ArgumentException)
        {
            return false;
        }
    }

    private static async Task<LucentProjectContext?> LoadProjectAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        LanguageServerLog.MsBuildStarted(LanguageServerLog.Logger, projectPath);
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-getTargetResult:ResolveReferences");
        startInfo.ArgumentList.Add("-getItem:Compile");
        startInfo.ArgumentList.Add("-getItem:LucentSource");
        startInfo.ArgumentList.Add("-getItem:Using");
        startInfo.ArgumentList.Add("-getItem:ProjectReference");
        startInfo.ArgumentList.Add("-getProperty:TargetFramework,LangVersion,Nullable,DefineConstants,ImplicitUsings,TargetPath");
        startInfo.ArgumentList.Add("-p:DesignTimeBuild=true");
        startInfo.ArgumentList.Add("-p:BuildingProject=false");
        startInfo.ArgumentList.Add("-nologo");

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return CreateFallbackContext(projectPath);
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
        var output = await standardOutput;
        var error = await standardError;
        if (process.ExitCode != 0)
        {
            LanguageServerLog.MsBuildFailed(
                LanguageServerLog.Logger,
                projectPath,
                process.ExitCode,
                error.Trim());
            return CreateFallbackContext(projectPath);
        }

        var jsonStart = output.IndexOf('{');
        var jsonEnd = output.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd < jsonStart)
        {
            LanguageServerLog.MsBuildInvalidOutput(
                LanguageServerLog.Logger,
                projectPath);
            return CreateFallbackContext(projectPath);
        }

        using var document = JsonDocument.Parse(
            output.Substring(jsonStart, jsonEnd - jsonStart + 1));
        var root = document.RootElement;
        var references = ReadItems(
                root.GetProperty("TargetResults")
                    .GetProperty("ResolveReferences")
                    .GetProperty("Items"))
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var sources = root.TryGetProperty("Items", out var items) &&
            items.TryGetProperty("Compile", out var compileItems)
                ? ReadItems(compileItems)
                    .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    // Other generators' evaluated Compile items are normal project
                    // semantics, including their obj outputs. Only Lucent's own
                    // generated files require the active manifest as authority.
                    .Where(path => !IsBuildOutput(path) || !IsLucentGenerated(path) ||
                                   IsManifestGenerated(compileItems, path))
                    // The active Lucent manifest is a design-time C# input, but
                    // Lucent rebuilds those same sources from their .lui files.
                    // Feeding its own generated components back into the semantic
                    // base duplicates generated types and makes every edit pay for
                    // stale implementation trees. Keep other SDK generated inputs.
                    .Where(path => !IsLucentGenerated(path))
                    .ToArray()
                : [];
        var lucentSources = root.TryGetProperty("Items", out items) &&
            items.TryGetProperty("LucentSource", out var lucentItems)
                ? ReadItems(lucentItems)
                    .Where(path => path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase))
                    .ToArray()
                : [];
        var globalUsings = root.TryGetProperty("Items", out items) &&
            items.TryGetProperty("Using", out var usingItems)
                ? ReadUsingItems(usingItems).ToArray()
                : [];
        var projectReferences = root.TryGetProperty("Items", out items) &&
            items.TryGetProperty("ProjectReference", out var projectReferenceItems)
                ? ReadItems(projectReferenceItems).ToArray()
                : [];
        var properties = root.TryGetProperty("Properties", out var propertyElement)
            ? propertyElement : default;
        string? Property(string name) => properties.ValueKind == JsonValueKind.Object &&
            properties.TryGetProperty(name, out var value) ? value.GetString() : null;

        return new LucentProjectContext(
            projectPath, references, sources, lucentSources, globalUsings,
            Property("TargetFramework"), Property("LangVersion"), Property("Nullable"),
            Property("DefineConstants"), projectReferences, Property("TargetPath"));
    }

    private static bool IsManifestGenerated(JsonElement compileItems, string path) =>
        compileItems.EnumerateArray().Any(item =>
        {
            var candidate = item.TryGetProperty("FullPath", out var full) ? full.GetString() :
                item.TryGetProperty("Identity", out var identity) ? identity.GetString() : null;
            return !string.IsNullOrWhiteSpace(candidate) &&
                string.Equals(Path.GetFullPath(candidate), path,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
                item.TryGetProperty("AutoGen", out var auto) &&
                string.Equals(auto.GetString(), "true", StringComparison.OrdinalIgnoreCase);
        });

    private static bool IsLucentGenerated(string path) =>
        path.EndsWith("Component.g.cs", StringComparison.OrdinalIgnoreCase);

    private static LucentProjectContext CreateFallbackContext(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var sources = Directory.EnumerateFiles(
                projectDirectory,
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .ToList();

        try
        {
            var document = XDocument.Load(projectPath);
            foreach (var include in document.Descendants()
                         .Where(element => element.Name.LocalName == "Compile")
                         .Select(element => element.Attribute("Include")?.Value)
                         .Where(include =>
                             !string.IsNullOrWhiteSpace(include) &&
                             !include!.Contains('*') &&
                             !include.Contains('$')))
            {
                var path = Path.GetFullPath(include!, projectDirectory);
                if (File.Exists(path))
                {
                    sources.Add(path);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            System.Xml.XmlException or ArgumentException)
        {
            // Project-local sources still provide a useful degraded context.
        }

        var context = new LucentProjectContext(
            projectPath,
            SourcePaths: sources.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            LucentSourcePaths: Directory.EnumerateFiles(projectDirectory, "*.lui", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(path)).ToArray());
        LanguageServerLog.ProjectFallback(
            LanguageServerLog.Logger,
            projectPath,
            context.Sources.Count);
        return context;
    }

    private static IEnumerable<string> ReadItems(JsonElement items)
    {
        foreach (var item in items.EnumerateArray())
        {
            var path = item.TryGetProperty("FullPath", out var fullPath)
                ? fullPath.GetString()
                : item.GetProperty("Identity").GetString();
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                yield return Path.GetFullPath(path);
            }
        }
    }

    private static IEnumerable<string> ReadUsingItems(JsonElement items)
    {
        foreach (var item in items.EnumerateArray())
        {
            var identity = item.GetProperty("Identity").GetString();
            if (string.IsNullOrWhiteSpace(identity))
            {
                continue;
            }

            var alias = item.TryGetProperty("Alias", out var aliasElement)
                ? aliasElement.GetString()
                : null;
            if (!string.IsNullOrWhiteSpace(alias))
            {
                yield return $"{alias} = {identity}";
                continue;
            }

            var isStatic = item.TryGetProperty("Static", out var staticElement) &&
                string.Equals(staticElement.GetString(), "true", StringComparison.OrdinalIgnoreCase);
            yield return isStatic ? $"static {identity}" : identity;
        }
    }

    private static bool IsBuildOutput(string path)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return normalized.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWithin(string? path, string? directory)
    {
        if (path is null || directory is null)
        {
            return false;
        }

        var relative = Path.GetRelativePath(directory, path);
        return relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !Path.IsPathFullyQualified(relative);
    }

    private sealed record CachedProjectContext(
        LucentProjectContext Context,
        ProjectStamp Stamp);

    private readonly record struct ProjectStamp(int Value);

    private static void AddFileUri(ICollection<string> roots, string? uri)
    {
        if (FileUri.TryGetPath(uri, out var path))
        {
            roots.Add(path);
        }
    }
}
