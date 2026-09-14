using Lucent.Lui.Compiler;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;

namespace Lucent.Lui.Tooling;

internal sealed class LuiLintProjectContext : IDisposable
{
    private readonly MSBuildWorkspace workspace;
    private readonly Project rootProject;
    private readonly Dictionary<ProjectId, Compilation> compilations = [];
    private bool disposed;

    private LuiLintProjectContext(MSBuildWorkspace workspace, Project rootProject)
    {
        this.workspace = workspace;
        this.rootProject = rootProject;
    }

    public static async Task<LuiLintProjectContext> LoadAsync(
        string projectPath,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !File.Exists(projectPath)
            || !projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
        )
            throw new ArgumentException(
                "An existing .csproj path is required.",
                nameof(projectPath)
            );
        if (!MSBuildLocator.IsRegistered)
            MSBuildLocator.RegisterDefaults();
        var workspace = MSBuildWorkspace.Create();
        try
        {
            var project = await workspace
                .OpenProjectAsync(
                    Path.GetFullPath(projectPath),
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
            return new LuiLintProjectContext(workspace, project);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    public async Task<LuiLintResult> AnalyzeAsync(
        string sourcePath,
        string source,
        LuiDeclarationOrder declarationOrder,
        CancellationToken cancellationToken = default
    )
    {
        ThrowIfDisposed();
        var absolutePath = Path.GetFullPath(sourcePath);
        var project =
            ProjectGraph(rootProject)
                .SingleOrDefault(candidate =>
                    candidate.AdditionalDocuments.Any(document =>
                        SamePath(document.FilePath, absolutePath)
                    )
                )
            ?? throw new InvalidOperationException(
                $"The evaluated project graph does not contain '{absolutePath}' as a .lui AdditionalFile."
            );
        var compilation = await CompilationAsync(project, cancellationToken).ConfigureAwait(false);
        var documents = await ProjectDocumentsAsync(
                project,
                absolutePath,
                source,
                cancellationToken
            )
            .ConfigureAwait(false);
        var current = documents.Single(document => SamePath(document.Path, absolutePath));
        var index = LuiProjectComponentIndex.Build(compilation, documents, cancellationToken);
        if (index.Diagnostics.Count != 0)
        {
            var detail = String.Join(
                "; ",
                index.Diagnostics.Select(diagnostic =>
                    diagnostic.Kind + " in " + diagnostic.Document.LogicalPath
                )
            );
            throw new InvalidOperationException(
                "The evaluated LUI component index is invalid: " + detail
            );
        }

        compilation = index.Augment(compilation, absolutePath);
        var globals = project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions;
        globals.TryGetValue("build_property.LucentLuiProjectEpoch", out var projectEpoch);
        globals.TryGetValue("build_property.LucentLuiProjectIdentity", out var projectIdentity);
        globals.TryGetValue("build_property.LucentLuiCompilerOptions", out var options);
        globals.TryGetValue("build_property.LucentLuiDefines", out var defines);
        var parse = project.ParseOptions as CSharpParseOptions ?? CSharpParseOptions.Default;
        var identity = LuiCompiler.Snapshot(
            new LuiFreshnessIdentity(
                projectEpoch ?? "",
                projectIdentity ?? project.FilePath ?? project.Name,
                new LuiDocumentIdentity(current.LogicalPath),
                current.Version,
                "",
                index.Generation,
                parse.LanguageVersion.ToString(),
                "",
                "",
                "",
                options ?? "",
                defines ?? "",
                project.DefaultNamespace ?? ""
            ),
            compilation
        );
        return LuiLintAnalyzer.Analyze(
            current.Syntax,
            compilation,
            identity,
            new LuiLintOptions(declarationOrder),
            cancellationToken
        );
    }

    public ReportDiagnostic EffectiveSeverity(
        string sourcePath,
        LuiDiagnostic diagnostic,
        ReportDiagnostic configuredSeverity
    )
    {
        ThrowIfDisposed();
        if (diagnostic.Severity == DiagnosticSeverity.Error)
            return ReportDiagnostic.Error;
        var project = ProjectGraph(rootProject)
            .Single(candidate =>
                candidate.AdditionalDocuments.Any(document =>
                    SamePath(document.FilePath, sourcePath)
                )
            );
        if (configuredSeverity != ReportDiagnostic.Default)
            return configuredSeverity;
        if (
            project.CompilationOptions?.SpecificDiagnosticOptions.TryGetValue(
                diagnostic.Id,
                out var specific
            ) == true
            && specific != ReportDiagnostic.Default
        )
            return specific;
        return diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => ReportDiagnostic.Error,
            DiagnosticSeverity.Warning
                when project.CompilationOptions?.GeneralDiagnosticOption
                    == ReportDiagnostic.Error => ReportDiagnostic.Error,
            DiagnosticSeverity.Warning => ReportDiagnostic.Warn,
            DiagnosticSeverity.Info => ReportDiagnostic.Info,
            DiagnosticSeverity.Hidden => ReportDiagnostic.Hidden,
            _ => ReportDiagnostic.Default,
        };
    }

    public static string DiscoverProject(string sourcePath)
    {
        for (
            var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            directory is not null;
            directory = Directory.GetParent(directory)?.FullName
        )
        {
            var projects = Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly);
            if (projects.Length == 1)
                return projects[0];
            if (projects.Length > 1)
                throw new InvalidOperationException(
                    $"More than one project was found in '{directory}'; pass --project explicitly."
                );
        }

        throw new InvalidOperationException(
            $"No project was found for '{sourcePath}'; pass --project explicitly."
        );
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        workspace.Dispose();
    }

    private async Task<Compilation> CompilationAsync(
        Project project,
        CancellationToken cancellationToken
    )
    {
        if (compilations.TryGetValue(project.Id, out var compilation))
            return compilation;
        var editorSolution = EditorSolution(project);
        compilation =
            await editorSolution
                .GetProject(project.Id)!
                .GetCompilationAsync(cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "The evaluated project did not produce a compilation."
            );
        compilations.Add(project.Id, compilation);
        return compilation;
    }

    private static async Task<LuiProjectDocument[]> ProjectDocumentsAsync(
        Project project,
        string currentPath,
        string currentSource,
        CancellationToken cancellationToken
    )
    {
        var documents = new List<LuiProjectDocument>();
        foreach (
            var document in project.AdditionalDocuments.Where(item =>
                item.FilePath?.EndsWith(".lui", StringComparison.OrdinalIgnoreCase) == true
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = SamePath(document.FilePath, currentPath)
                ? currentSource
                : SourceFileSnapshot.Read(document.FilePath!).Text;
            var logicalPath = LogicalPath(project, document);
            if (!LuiDocumentIdentity.TryCreate(logicalPath, out var identity))
                throw new InvalidOperationException(
                    $"'{document.FilePath}' has invalid logical path '{logicalPath}'."
                );
            documents.Add(
                new LuiProjectDocument(
                    document.FilePath!,
                    identity!.LogicalPath,
                    source,
                    LuiDocumentIdentity.Hash(source)
                )
            );
        }

        return documents.ToArray();
    }

    private static Solution EditorSolution(Project project)
    {
        var solution = project.Solution;
        foreach (var current in ProjectGraph(project))
        {
            solution = solution.WithProjectAnalyzerReferences(
                current.Id,
                current.AnalyzerReferences.Where(reference =>
                    !IsUnavailableLucentBuildToolAnalyzer(reference)
                )
            );
            if (current.Id != project.Id)
                continue;
            foreach (
                var document in current.AdditionalDocuments.Where(document =>
                    document.FilePath?.EndsWith(".lui", StringComparison.OrdinalIgnoreCase) == true
                )
            )
                solution = solution.RemoveAdditionalDocument(document.Id);
        }

        return solution;
    }

    private static bool IsUnavailableLucentBuildToolAnalyzer(AnalyzerReference reference)
    {
        if (reference is not UnresolvedAnalyzerReference)
            return false;
        var path = String.IsNullOrWhiteSpace(reference.FullPath)
            ? reference.Display
            : reference.FullPath;
        var fileName = Path.GetFileName(path);
        return fileName.Equals("Lucent.Lui.Compiler.dll", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Lucent.Lui.Generator.dll", StringComparison.OrdinalIgnoreCase);
    }

    private static string LogicalPath(Project project, TextDocument document)
    {
        var additional = project.AnalyzerOptions.AdditionalFiles.FirstOrDefault(item =>
            SamePath(item.Path, document.FilePath)
        );
        if (
            additional is not null
            && project
                .AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(additional)
                .TryGetValue("build_metadata.AdditionalFiles.LucentLuiLogicalPath", out var path)
        )
            return path;
        return Path.GetRelativePath(Path.GetDirectoryName(project.FilePath)!, document.FilePath!);
    }

    private static List<Project> ProjectGraph(Project project)
    {
        var visited = new HashSet<ProjectId>();
        var ordered = new List<Project>();
        void Visit(Project current)
        {
            if (!visited.Add(current.Id))
                return;
            foreach (
                var reference in current.ProjectReferences.OrderBy(
                    reference => current.Solution.GetProject(reference.ProjectId)?.FilePath ?? "",
                    StringComparer.OrdinalIgnoreCase
                )
            )
            {
                var referenced = current.Solution.GetProject(reference.ProjectId);
                if (referenced is not null)
                    Visit(referenced);
            }

            ordered.Add(current);
        }

        Visit(project);
        return ordered;
    }

    private static bool SamePath(string? left, string? right) =>
        left is not null
        && right is not null
        && String.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase
        );

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
