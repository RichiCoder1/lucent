using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Lucent.Compiler;

namespace Lucent.LanguageServer;

internal sealed class ProjectContextLoader
{
    private readonly StringComparer _pathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private readonly Dictionary<string, LucentProjectContext?> _contexts;
    private IReadOnlyList<string> _workspaceRoots = [];

    public ProjectContextLoader()
    {
        _contexts = new Dictionary<string, LucentProjectContext?>(_pathComparer);
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
            return null;
        }

        if (_contexts.TryGetValue(projectPath, out var cached))
        {
            return cached;
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
            context = null;
        }

        if (context is not null)
        {
            _contexts[projectPath] = context;
        }

        return context;
    }

    private string? FindProject(string sourcePath)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var projects = _workspaceRoots
            .Where(Directory.Exists)
            .SelectMany(EnumerateProjects)
            .ToArray();

        foreach (var project in projects)
        {
            if (ProjectIncludesSource(project, fullSourcePath))
            {
                return project;
            }
        }

        var sourceDirectory = Path.GetDirectoryName(fullSourcePath);
        return projects
            .Where(project => IsWithin(
                sourceDirectory,
                Path.GetDirectoryName(project)))
            .OrderByDescending(project => Path.GetDirectoryName(project)!.Length)
            .FirstOrDefault();
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
            return document.Descendants()
                .Where(element => element.Name.LocalName == "LucentSource")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Where(include => !include!.Contains('*') && !include.Contains('$'))
                .Select(include => Path.GetFullPath(include!, projectDirectory))
                .Any(path => _pathComparer.Equals(path, sourcePath));
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
        startInfo.ArgumentList.Add("-p:DesignTimeBuild=true");
        startInfo.ArgumentList.Add("-p:BuildingProject=false");
        startInfo.ArgumentList.Add("-nologo");

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await standardOutput;
        _ = await standardError;
        if (process.ExitCode != 0)
        {
            return null;
        }

        var jsonStart = output.IndexOf('{');
        var jsonEnd = output.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd < jsonStart)
        {
            return null;
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
                    .Where(path => !IsBuildOutput(path))
                    .ToArray()
                : [];

        return new LucentProjectContext(projectPath, references, sources);
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

    private static void AddFileUri(ICollection<string> roots, string? uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile)
        {
            roots.Add(Path.GetFullPath(parsed.LocalPath));
        }
    }
}
