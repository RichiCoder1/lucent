using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Lucent.Compiler.MSBuild;

/// <summary>
/// Compiles one or more Lucent source files in-process and writes their
/// generated C# into the MSBuild intermediate directory.
/// </summary>
public sealed class CompileLucent : Task
{
    private static readonly UTF8Encoding Utf8NoBom = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// The .lui files to compile. Each source produces one generated file
    /// named &lt;source-name&gt;Component.g.cs in <see cref="OutputDirectory"/>.
    /// </summary>
    [Required]
    public ITaskItem[] Sources { get; set; } = [];

    /// <summary>
    /// The intermediate directory for generated C#.
    /// </summary>
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Deterministic list of generated files consumed by design-time builds.</summary>
    public string ManifestPath { get; set; } = string.Empty;

    /// <summary>
    /// The consuming project's resolved metadata references.
    /// </summary>
    public ITaskItem[] References { get; set; } = [];

    /// <summary>
    /// The consuming project's C# source files. These let Lucent resolve
    /// project-defined controls and members in the same compilation.
    /// </summary>
    public ITaskItem[] CSharpSources { get; set; } = [];

    /// <summary>
    /// The consuming project path, used to identify the semantic context.
    /// </summary>
    public string? ProjectPath { get; set; }

    /// <summary>Evaluated project semantic settings, passed unchanged to Roslyn.</summary>
    public string? TargetFramework { get; set; }
    public string? LanguageVersion { get; set; }
    public string? Nullable { get; set; }
    public string? DefineConstants { get; set; }

    /// <summary>Evaluated implicit/global using directives.</summary>
    public ITaskItem[] GlobalUsings { get; set; } = [];

    /// <summary>Evaluated project-reference identities.</summary>
    public ITaskItem[] ProjectReferences { get; set; } = [];

    /// <summary>
    /// Generated C# files written by the task.
    /// </summary>
    [Output]
    public ITaskItem[] GeneratedFiles { get; private set; } = [];

    public override bool Execute()
    {
        if (Sources.Length == 0)
        {
            if (string.IsNullOrWhiteSpace(OutputDirectory))
            {
                LogError("LUC9002", null, 0, 0,
                    "The Lucent compiler output directory is required.");
                return false;
            }

            ManifestPath = string.IsNullOrWhiteSpace(ManifestPath)
                ? Path.Combine(OutputDirectory, "Lucent.GeneratedFiles.props")
                : ManifestPath;
            try
            {
                // This is intentionally an atomic empty replacement, rather than a
                // skipped task: the last .lui may have been deleted or renamed.
                WriteIfChanged(ManifestPath, "<Project>\n  <ItemGroup />\n</Project>\n");
            }
            catch (IOException exception)
            {
                LogError("LUC9002", null, 0, 0,
                    $"Unable to write Lucent generated output: {exception.Message}");
                return false;
            }
            catch (UnauthorizedAccessException exception)
            {
                LogError("LUC9002", null, 0, 0,
                    $"Unable to write Lucent generated output: {exception.Message}");
                return false;
            }
            GeneratedFiles = [];
            return true;
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            LogError(
                code: "LUC9002",
                file: null,
                line: 0,
                column: 0,
                message: "The Lucent compiler output directory is required.");
            return false;
        }

        var pendingOutputs = new List<PendingOutput>(Sources.Length);
        var outputPaths = new HashSet<string>(GetPathComparer());
        var sourcePaths = new HashSet<string>(GetPathComparer());
        var inputs = new List<LucentSourceInput>(Sources.Length);
        var succeeded = true;
        var projectContext = new LucentProjectContext(
            ProjectPath,
            GetExistingPaths(References),
            GetExistingPaths(CSharpSources),
            Sources.Select(GetFullPath).Where(path => path is not null).Cast<string>().ToArray(),
            GetUsingDirectives(GlobalUsings),
            TargetFramework,
            LanguageVersion,
            Nullable,
            DefineConstants,
            GetExistingPaths(ProjectReferences));

        foreach (var sourceItem in Sources)
        {
            var sourcePath = GetFullPath(sourceItem);
            if (sourcePath is null)
            {
                succeeded = false;
                continue;
            }

            var outputPath = GetOutputPath(sourcePath);
            if (!sourcePaths.Add(sourcePath))
            {
                LogError("LUC9004", sourcePath, 1, 1,
                    $"Lucent source '{sourcePath}' was supplied more than once.");
                succeeded = false;
            }
            if (!outputPaths.Add(outputPath))
            {
                LogError("LUC9003", sourcePath, 1, 1,
                    $"Multiple Lucent sources map to the generated output '{outputPath}'.");
                succeeded = false;
            }

            if (!succeeded) continue;

            try
            {
                var sourceText = File.ReadAllText(sourcePath);
                var stylePath = Path.ChangeExtension(sourcePath, ".css");
                var styleText = File.Exists(stylePath)
                    ? File.ReadAllText(stylePath)
                    : null;
                inputs.Add(new LucentSourceInput(sourcePath, sourceText, stylePath, styleText,
                    GetOutputPath(sourcePath)));
            }
            catch (IOException exception)
            {
                LogError(
                    code: "LUC9002",
                    file: sourcePath,
                    line: 1,
                    column: 1,
                    message: $"Unable to read or generate Lucent source: {exception.Message}");
                succeeded = false;
            }
            catch (UnauthorizedAccessException exception)
            {
                LogError(
                    code: "LUC9002",
                    file: sourcePath,
                    line: 1,
                    column: 1,
                    message: $"Unable to read or generate Lucent source: {exception.Message}");
                succeeded = false;
            }
        }

        if (!succeeded)
        {
            // Do not partially update generated output when any source has
            // diagnostics. This preserves the last successful build result.
            GeneratedFiles = [];
            return false;
        }
        ManifestPath = string.IsNullOrWhiteSpace(ManifestPath)
            ? Path.Combine(OutputDirectory, "Lucent.GeneratedFiles.props")
            : ManifestPath;

        var projectResult = LucentCompiler.CompileProject(inputs, projectContext);
        foreach (var source in projectResult.Sources)
        {
            LogDiagnostics(source.SourcePath, source.Result.Diagnostics);
            if (!source.Result.Succeeded || source.Result.GeneratedSource is null)
            {
                succeeded = false;
                continue;
            }
            pendingOutputs.Add(new PendingOutput(
                GetOutputPath(source.SourcePath), source.Result.GeneratedSource));
        }
        if (!succeeded)
        {
            GeneratedFiles = [];
            return false;
        }

        try
        {
            foreach (var pendingOutput in pendingOutputs)
            {
                WriteIfChanged(pendingOutput.Path, pendingOutput.Content);
            }
            var manifestItems = string.Join(Environment.NewLine, pendingOutputs
                .Select(output => Path.GetFullPath(output.Path))
                .OrderBy(path => path, GetPathComparer())
                .Select(path => "    <Compile Include=\"" +
                    System.Security.SecurityElement.Escape(path) +
                    "\" AutoGen=\"true\" DesignTime=\"true\" Visible=\"false\" />"));
            WriteIfChanged(ManifestPath,
                "<Project>" + Environment.NewLine + "  <ItemGroup>" + Environment.NewLine +
                manifestItems + Environment.NewLine + "  </ItemGroup>" + Environment.NewLine +
                "</Project>" + Environment.NewLine);
        }
        catch (IOException exception)
        {
            LogError(
                code: "LUC9002",
                file: null,
                line: 0,
                column: 0,
                message: $"Unable to write Lucent generated output: {exception.Message}");
            GeneratedFiles = [];
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            LogError(
                code: "LUC9002",
                file: null,
                line: 0,
                column: 0,
                message: $"Unable to write Lucent generated output: {exception.Message}");
            GeneratedFiles = [];
            return false;
        }

        GeneratedFiles = pendingOutputs
            .Select(output => new TaskItem(output.Path))
            .ToArray();

        return true;
    }

    private string? GetFullPath(ITaskItem sourceItem)
    {
        var metadataPath = sourceItem.GetMetadata("FullPath");
        var path = string.IsNullOrWhiteSpace(metadataPath)
            ? sourceItem.ItemSpec
            : metadataPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            LogError(
                code: "LUC9002",
                file: null,
                line: 0,
                column: 0,
                message: "A Lucent source item has no path.");
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (ArgumentException exception)
        {
            LogError(
                code: "LUC9002",
                file: path,
                line: 1,
                column: 1,
                message: $"The Lucent source path is invalid: {exception.Message}");
            return null;
        }
    }

    private string GetOutputPath(string sourcePath)
    {
        var sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        var outputDirectory = Path.GetFullPath(OutputDirectory);
        return Path.Combine(outputDirectory, $"{sourceName}Component.g.cs");
    }

    private static IReadOnlyList<string> GetExistingPaths(
        IEnumerable<ITaskItem> items) =>
        items.Select(item =>
            string.IsNullOrWhiteSpace(item.GetMetadata("FullPath"))
                ? item.ItemSpec
                : item.GetMetadata("FullPath"))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(File.Exists)
            .Distinct(GetPathComparer())
            .ToArray();

    private static IReadOnlyList<string> GetUsingDirectives(IEnumerable<ITaskItem> items) =>
        items.Select(item =>
        {
            var identity = item.ItemSpec;
            var alias = item.GetMetadata("Alias");
            if (!string.IsNullOrWhiteSpace(alias)) return $"{alias} = {identity}";
            return string.Equals(item.GetMetadata("Static"), "true", StringComparison.OrdinalIgnoreCase)
                ? $"static {identity}"
                : identity;
        })
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private void LogDiagnostics(
        string sourcePath,
        IReadOnlyList<LucentDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            var diagnosticPath = diagnostic.SourcePath ?? sourcePath;
            var line = Math.Max(1, diagnostic.Line);
            var column = Math.Max(1, diagnostic.Column);
            var endColumn = Math.Max(column, column + Math.Max(0, diagnostic.Span.Length));

            if (diagnostic.Severity == LucentDiagnosticSeverity.Error)
            {
                Log.LogErrorEvent(new BuildErrorEventArgs(
                    subcategory: "Lucent",
                    code: diagnostic.Code,
                    file: diagnosticPath,
                    lineNumber: line,
                    columnNumber: column,
                    endLineNumber: line,
                    endColumnNumber: endColumn,
                    message: diagnostic.Message,
                    helpKeyword: null,
                    senderName: nameof(CompileLucent)));
            }
            else
            {
                Log.LogWarningEvent(new BuildWarningEventArgs(
                    subcategory: "Lucent",
                    code: diagnostic.Code,
                    file: diagnosticPath,
                    lineNumber: line,
                    columnNumber: column,
                    endLineNumber: line,
                    endColumnNumber: endColumn,
                    message: diagnostic.Message,
                    helpKeyword: null,
                    senderName: nameof(CompileLucent)));
            }
        }
    }

    private void LogError(
        string code,
        string? file,
        int line,
        int column,
        string message)
    {
        Log.LogErrorEvent(new BuildErrorEventArgs(
            subcategory: "Lucent",
            code: code,
            file: file,
            lineNumber: line,
            columnNumber: column,
            endLineNumber: line,
            endColumnNumber: column,
            message: message,
            helpKeyword: null,
            senderName: nameof(CompileLucent)));
    }

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static void WriteIfChanged(string outputPath, string content)
    {
        var expected = Utf8NoBom.GetBytes(content);
        if (File.Exists(outputPath) &&
            File.ReadAllBytes(outputPath).AsSpan().SequenceEqual(expected))
        {
            return;
        }

        var outputDirectory = Path.GetDirectoryName(outputPath)
            ?? throw new IOException("The generated output path has no parent directory.");
        Directory.CreateDirectory(outputDirectory);

        var temporaryPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllBytes(temporaryPath, expected);
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record PendingOutput(string Path, string Content);
}
