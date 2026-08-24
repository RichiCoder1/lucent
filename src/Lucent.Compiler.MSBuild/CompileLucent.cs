using System.Text;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Lucent.Compiler.CodeGeneration;
using Lucent.Compiler.Styling;

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

    /// <summary>Ordered project-wide CSS inputs compiled into LucentStyles.</summary>
    public ITaskItem[] Styles { get; set; } = [];
    public string RootNamespace { get; set; } = string.Empty;
    public string OutputType { get; set; } = string.Empty;

    // Kept for direct task callers from the Plan 008a seam; LucentStyle now owns generated catalogs.
    public ITaskItem[] StyleCatalogTypes { get; set; } = [];

    /// <summary>
    /// The intermediate directory for generated C#.
    /// </summary>
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Deterministic list of generated files consumed by design-time builds.</summary>
    public string ManifestPath { get; set; } = string.Empty;

    /// <summary>Staged non-executable metadata embedded by the normal C# compiler.</summary>
    public string ModuleManifestPath { get; set; } = string.Empty;
    public string AssemblyName { get; set; } = string.Empty;
    public string AssemblyVersion { get; set; } = "0.0.0.0";
    public string AssemblyCulture { get; set; } = string.Empty;
    public string SignAssembly { get; set; } = string.Empty;
    public string AssemblyOriginatorKeyFile { get; set; } = string.Empty;
    public string PublicSign { get; set; } = string.Empty;
    public string DelaySign { get; set; } = string.Empty;

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

    [Output]
    public ITaskItem[] ModuleManifestFiles { get; private set; } = [];

    public override bool Execute()
    {
        // MSBuild can reuse this task instance within one node.
        GeneratedFiles = [];
        ModuleManifestFiles = [];

        if (Sources.Length == 0 && Styles.Length == 0)
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
                ModuleManifestPath = string.IsNullOrWhiteSpace(ModuleManifestPath)
                    ? Path.Combine(OutputDirectory, "Lucent.ModuleManifest.v1.json")
                    : ModuleManifestPath;
                if (File.Exists(ModuleManifestPath)) File.Delete(ModuleManifestPath);
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
            ModuleManifestFiles = [];
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
            GetExistingPaths(ProjectReferences),
            RootNamespace: EffectiveRootNamespace());

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
            ModuleManifestFiles = [];
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
            ModuleManifestFiles = [];
            return false;
        }

        try
        {
            if (!TryGetStyleCatalogTypes(out var explicitCatalogs)) return false;
            var generatedCatalog = $"{EffectiveRootNamespace()}.LucentStyles";
            var globalStyles = ReadGlobalStyles(generatedCatalog);
            if (globalStyles is null) return false;
            string[] styleCatalogTypes = explicitCatalogs.Concat(globalStyles.Count == 0 ? [] : [generatedCatalog])
                .Distinct(StringComparer.Ordinal).OrderBy(type => type, StringComparer.Ordinal).ToArray();
            if (globalStyles.Count > 0)
            {
                var parsed = new List<BoundStyleRule>();
                foreach (var style in globalStyles)
                {
                    var result = StyleSheetParser.Parse(style.Text, style.Path);
                    LogDiagnostics(style.Path, result.Diagnostics);
                    if (result.Diagnostics.Any(diagnostic => diagnostic.Severity == LucentDiagnosticSeverity.Error)) return false;
                    parsed.AddRange(result.Sheet.Rules);
                }
                var output = Path.Combine(OutputDirectory, "LucentStyles.g.cs");
                pendingOutputs.Add(new PendingOutput(output,
                    GeneralCSharpEmitter.EmitGlobalStyles(EffectiveRootNamespace(), new BoundStyleSheet(parsed))));
                if (IsExecutableProject() && !LucentCompiler.HasDirectStyleInstall(projectContext, generatedCatalog))
                    Log.LogWarning(null, "LUC9009", null, ProjectPath, 0, 0, 0, 0,
                        $"LucentStyle items require explicit installation: Styles.Add(new {generatedCatalog}());");
            }
            foreach (var pendingOutput in pendingOutputs)
            {
                WriteIfChanged(pendingOutput.Path, pendingOutput.Content);
            }
            ModuleManifestPath = string.IsNullOrWhiteSpace(ModuleManifestPath)
                ? Path.Combine(OutputDirectory, "Lucent.ModuleManifest.v1.json")
                : ModuleManifestPath;
            var manifestItems = pendingOutputs
                .Select(output => Path.GetFullPath(output.Path))
                .OrderBy(path => path, GetPathComparer())
                .Select(path => "    <Compile Include=\"" +
                    System.Security.SecurityElement.Escape(path) +
                    "\" AutoGen=\"true\" Visible=\"false\" LucentGenerated=\"true\" Condition=\"'$(DesignTimeBuild)' == 'true'\" />")
                .Concat(pendingOutputs.Select(output => Path.GetFullPath(output.Path))
                    .OrderBy(path => path, GetPathComparer())
                    .Select(path => "    <_LucentGeneratedCompile Include=\"" +
                        System.Security.SecurityElement.Escape(path) + "\" />"))
                .Concat(pendingOutputs.Select(output => Path.GetFullPath(output.Path))
                    .OrderBy(path => path, GetPathComparer())
                    .Select(path => "    <FileWrites Include=\"" +
                        System.Security.SecurityElement.Escape(path) + "\" />"))
                .Concat(inputs.Select(input => Path.GetFullPath(input.SourcePath))
                    .Distinct(GetPathComparer())
                    .OrderBy(path => path, GetPathComparer())
                    .Select(path => "    <_LucentTrackedSource Include=\"" +
                        System.Security.SecurityElement.Escape(path) + "\" />"))
                .Concat(inputs.Where(input => input.StylePath is not null)
                    .Select(input => Path.GetFullPath(input.StylePath!))
                    .Concat(globalStyles.Select(style => Path.GetFullPath(style.Path)))
                    .Distinct(GetPathComparer())
                    .OrderBy(path => path, GetPathComparer())
                    .Select(path => "    <_LucentTrackedStyle Include=\"" +
                        System.Security.SecurityElement.Escape(path) + "\" />"))
                .Concat([
                    "    <FileWrites Include=\"" + System.Security.SecurityElement.Escape(Path.GetFullPath(ManifestPath)) + "\" />",
                    "    <FileWrites Include=\"" + System.Security.SecurityElement.Escape(Path.GetFullPath(ModuleManifestPath)) + "\" />",
                ]);
            WriteIfChanged(ManifestPath,
                "<Project>" + Environment.NewLine + "  <ItemGroup>" + Environment.NewLine +
                string.Join(Environment.NewLine, manifestItems) + Environment.NewLine + "  </ItemGroup>" + Environment.NewLine +
                "</Project>" + Environment.NewLine);
            if (!TryGetAssemblyIdentity(out var identity))
            {
                GeneratedFiles = [];
                ModuleManifestFiles = [];
                return false;
            }
            WriteBytesIfChanged(ModuleManifestPath, LucentModuleManifest.Create(
                projectContext,
                inputs,
                identity,
                styleCatalogTypes,
                globalStyles));
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
            ModuleManifestFiles = [];
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
            ModuleManifestFiles = [];
            return false;
        }

        GeneratedFiles = pendingOutputs
            .Select(output => new TaskItem(output.Path))
            .ToArray();
        ModuleManifestFiles = [new TaskItem(ModuleManifestPath)];

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

    private List<LucentGlobalStyleInput>? ReadGlobalStyles(string catalogType)
    {
        var styles = new List<LucentGlobalStyleInput>();
        foreach (var item in Styles)
        {
            var path = GetFullPath(item);
            if (path is null) return null;
            try { styles.Add(new LucentGlobalStyleInput(path, File.ReadAllText(path), catalogType)); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogError("LUC9002", path, 1, 1, $"Unable to read Lucent style: {exception.Message}");
                return null;
            }
        }
        return styles;
    }

    private bool TryGetStyleCatalogTypes(out string[] types)
    {
        types = StyleCatalogTypes.Select(item => item.ItemSpec.Trim()).ToArray();
        if (types.Any(string.IsNullOrWhiteSpace) || types.Distinct(StringComparer.Ordinal).Count() != types.Length)
        {
            LogError("LUC9008", null, 0, 0, "Lucent style catalog metadata names must be non-empty and unique.");
            return false;
        }
        Array.Sort(types, StringComparer.Ordinal);
        return true;
    }

    private string EffectiveRootNamespace() => string.IsNullOrWhiteSpace(RootNamespace)
        ? (string.IsNullOrWhiteSpace(AssemblyName) ? "Lucent" : AssemblyName) : RootNamespace;

    private bool IsExecutableProject() => string.Equals(OutputType, "Exe", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(OutputType, "WinExe", StringComparison.OrdinalIgnoreCase);

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
        WriteBytesIfChanged(outputPath, Utf8NoBom.GetBytes(content));
    }

    private bool TryGetAssemblyIdentity(out LucentAssemblyIdentity identity)
    {
        identity = default!;
        try
        {
            var signAssembly = bool.TryParse(SignAssembly, out var signed) && signed;
            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithCryptoKeyFile(!signAssembly || string.IsNullOrWhiteSpace(AssemblyOriginatorKeyFile) ? null :
                    Path.GetFullPath(AssemblyOriginatorKeyFile, Path.GetDirectoryName(ProjectPath) ?? Environment.CurrentDirectory))
                .WithPublicSign(signAssembly && bool.TryParse(PublicSign, out var publicSign) && publicSign)
                .WithDelaySign(signAssembly && bool.TryParse(DelaySign, out var delaySign) && delaySign);
            var compilation = CSharpCompilation.Create(
                string.IsNullOrWhiteSpace(AssemblyName) ? Path.GetFileNameWithoutExtension(ProjectPath ?? "Lucent") : AssemblyName,
                options: options);
            var assembly = compilation.Assembly.Identity;
            // The SDK properties are the evaluated compiler inputs. A hand-written
            // attribute that disagrees is rejected against the actual PE below.
            var token = assembly.PublicKeyToken.ToArray();
            if (signAssembly)
            {
                using var key = new RSACryptoServiceProvider();
                key.ImportCspBlob(File.ReadAllBytes(options.CryptoKeyFile!));
                var publicKey = key.ExportCspBlob(false);
                publicKey[5] = 0x24; // CALG_RSA_SIGN; ExportCspBlob uses CALG_RSA_KEYX.
                var strongNameKey = new byte[12 + publicKey.Length];
                BitConverter.GetBytes(0x00002400).CopyTo(strongNameKey, 0);
                BitConverter.GetBytes(0x00008004).CopyTo(strongNameKey, 4);
                BitConverter.GetBytes(publicKey.Length).CopyTo(strongNameKey, 8);
                publicKey.CopyTo(strongNameKey, 12);
                token = SHA1.HashData(strongNameKey)[^8..].Reverse().ToArray();
            }
            identity = new LucentAssemblyIdentity(assembly.Name,
                string.IsNullOrWhiteSpace(AssemblyVersion) ? assembly.Version.ToString() : AssemblyVersion,
                AssemblyCulture, Convert.ToHexString(token));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or CryptographicException)
        {
            LogError("LUC9005", null, 0, 0,
                $"Unable to derive the Lucent manifest assembly identity from CoreCompile inputs: {exception.Message}");
            return false;
        }
    }

    private static void WriteBytesIfChanged(string outputPath, byte[] expected)
    {
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

/// <summary>Verifies the compiler-embedded manifest and atomically records the last successful PE.</summary>
public sealed class VerifyLucentModuleManifest : Task
{
    [Required] public string AssemblyPath { get; set; } = string.Empty;
    [Required] public string ManifestPath { get; set; } = string.Empty;
    [Required] public string LastSuccessfulPath { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            var staged = File.ReadAllBytes(ManifestPath);
            if (!LucentModuleManifest.TryReadFromPe(AssemblyPath, out var embedded, out var error) ||
                embedded is null || !LucentModuleManifest.TryRead(staged, embedded.Assembly, out _, out error) ||
                !staged.AsSpan().SequenceEqual(LucentModuleManifest.Serialize(embedded)) ||
                !LucentModuleManifest.HasPublicCatalogTypes(AssemblyPath, embedded.StyleCatalogTypes, out error))
            {
                Log.LogError("LUC9006", null, null, AssemblyPath, 0, 0, 0, 0,
                    $"The compiled assembly does not contain the expected Lucent module manifest: {error}");
                return false;
            }
            var record = Encoding.UTF8.GetBytes($"{{\"fingerprint\":\"{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(AssemblyPath)))}\",\"manifest\":{Encoding.UTF8.GetString(staged)}}}");
            WriteAtomically(LastSuccessfulPath, record);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException or System.Text.Json.JsonException)
        {
            Log.LogError("LUC9006", null, null, AssemblyPath, 0, 0, 0, 0,
                $"Unable to verify the Lucent module manifest: {exception.Message}");
            return false;
        }
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
