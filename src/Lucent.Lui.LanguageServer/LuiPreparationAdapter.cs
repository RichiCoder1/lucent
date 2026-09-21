using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Lucent.Lui.Compiler;
using Lucent.Lui.Preparation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Lucent.Lui.LanguageServer;

internal sealed partial class LuiProjectContext
{
    private async Task<(
        CSharpCompilation Compilation,
        LuiPreparationResult Preparation
    )> PrepareNamedProjectGraphAsync(
        Project project,
        long captured,
        CancellationToken cancellationToken
    )
    {
        var editorSolution = EditorSolution(project, namedComponents: true);
        var compilations = new Dictionary<ProjectId, CSharpCompilation>();
        var preparations = new Dictionary<ProjectId, LuiPreparationResult>();
        foreach (var graphProject in ProjectGraph(project))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = editorSolution.GetProject(graphProject.Id)
                ?? throw new InvalidOperationException(
                    "The named-component editor graph lost project '"
                        + (graphProject.FilePath ?? graphProject.Name)
                        + "'."
                );
            var compilation =
                await current.GetCompilationAsync(cancellationToken).ConfigureAwait(false)
                    as CSharpCompilation
                ?? throw new InvalidOperationException(
                    "The named-component editor project did not produce a C# compilation."
                );
            foreach (var reference in current.ProjectReferences)
            {
                if (!compilations.TryGetValue(reference.ProjectId, out var dependency))
                    continue;
                var referencedProject = editorSolution.GetProject(reference.ProjectId);
                var stale = compilation
                    .References.OfType<CompilationReference>()
                    .SingleOrDefault(item =>
                        String.Equals(
                            item.Compilation.AssemblyName,
                            referencedProject?.AssemblyName,
                            StringComparison.Ordinal
                        )
                    );
                if (stale is null)
                    throw new InvalidOperationException(
                        "Named-component preparation could not replace the current editor reference to '"
                            + (referencedProject?.FilePath ?? referencedProject?.Name ?? "unknown")
                            + "'."
                    );
                compilation = compilation
                    .RemoveReferences(stale)
                    .AddReferences(
                        dependency.ToMetadataReference(
                            reference.Aliases,
                            reference.EmbedInteropTypes
                        )
                    );
            }

            if (NamedComponentsEnabled(current))
            {
                var documents = await CurrentNamedDocumentsAsync(current, cancellationToken)
                    .ConfigureAwait(false);
                var preparation = PrepareNamedProject(
                    current,
                    compilation,
                    documents,
                    captured,
                    cancellationToken
                );
                preparations.Add(current.Id, preparation);
                compilation = preparation.BindingCompilation;
            }
            compilations.Add(current.Id, compilation);
        }

        if (!preparations.TryGetValue(project.Id, out var rootPreparation))
            throw new InvalidOperationException(
                "The requested named-component project was not prepared."
            );
        return (compilations[project.Id], rootPreparation);
    }

    private static async Task<IReadOnlyList<LuiProjectDocument>> CurrentNamedDocumentsAsync(
        Project project,
        CancellationToken cancellationToken
    )
    {
        var documents = new List<LuiProjectDocument>();
        foreach (
            var document in project.AdditionalDocuments.Where(item =>
                item.FilePath!.EndsWith(".lui", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = (
                await document.GetTextAsync(cancellationToken).ConfigureAwait(false)
            ).ToString();
            var logicalPath = LogicalPath(project, document);
            if (!LuiDocumentIdentity.TryCreate(logicalPath, out var identity))
                continue;
            documents.Add(
                new LuiProjectDocument(
                    document.FilePath!,
                    identity!.LogicalPath,
                    source,
                    DocumentVersion(project, document, source)
                )
            );
        }
        return documents;
    }

    private LuiPreparationResult PrepareNamedProject(
        Project project,
        CSharpCompilation compilation,
        IReadOnlyList<LuiProjectDocument> documents,
        long captured,
        CancellationToken cancellationToken
    )
    {
        var globals = project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions;
        globals.TryGetValue("build_property.LucentLuiProjectEpoch", out var projectEpoch);
        globals.TryGetValue("build_property.LucentLuiProjectIdentity", out var projectIdentity);
        globals.TryGetValue("build_property.LucentLuiCompilerOptions", out var options);
        globals.TryGetValue("build_property.LucentLuiDefines", out var defines);
        var parse = project.ParseOptions as CSharpParseOptions ?? CSharpParseOptions.Default;
        LuiPreparationDriverState? previous;
        lock (gate)
            preparationDrivers.TryGetValue(project.Id, out previous);
        var preparation = LuiPreparationEngine.Prepare(
            new LuiPreparationRequest(
                compilation,
                documents
                    .Select(item => new LuiPreparationDocument(
                        item.Path,
                        item.LogicalPath,
                        item.Source,
                        item.Version
                    ))
                    .ToImmutableArray(),
                LoadForeignGenerators(project, cancellationToken),
                CurrentAdditionalTexts(project, documents),
                parse,
                project.AnalyzerOptions.AnalyzerConfigOptionsProvider,
                projectEpoch ?? "",
                projectIdentity ?? project.FilePath ?? project.Name,
                parse.LanguageVersion.ToString(),
                options ?? "",
                defines ?? "",
                project.DefaultNamespace ?? "",
                previous
            ),
            cancellationToken
        );
        if (preparation.DriverState is not null)
            lock (gate)
            {
                if (!disposed && epoch == captured)
                    preparationDrivers[project.Id] = preparation.DriverState;
            }
        return preparation;
    }

    private static ImmutableArray<AdditionalText> CurrentAdditionalTexts(
        Project project,
        IReadOnlyList<LuiProjectDocument> documents
    )
    {
        var byPath = documents.ToDictionary(
            item => Path.GetFullPath(item.Path),
            StringComparer.OrdinalIgnoreCase
        );
        return project
            .AnalyzerOptions.AdditionalFiles.Select(file =>
            {
                if (
                    file.Path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase)
                    && byPath.TryGetValue(Path.GetFullPath(file.Path), out var document)
                )
                    return (AdditionalText)
                        new SnapshotAdditionalText(
                            file.Path,
                            SourceText.From(document.Source, Encoding.UTF8)
                        );
                return file;
            })
            .ToImmutableArray();
    }

    private static ImmutableArray<LuiPreparationGenerator> LoadForeignGenerators(
        Project project,
        CancellationToken cancellationToken
    )
    {
        var loaded = ImmutableArray.CreateBuilder<LuiPreparationGenerator>();
        foreach (var reference in project.AnalyzerReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference is UnresolvedAnalyzerReference)
                throw new InvalidOperationException(
                    "Named-component preparation cannot load unresolved analyzer reference '"
                        + reference.Display
                        + "'."
                );
            if (
                String.Equals(
                    Path.GetFileName(reference.FullPath ?? reference.Display),
                    "Lucent.Lui.Compiler.dll",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                continue;
            var loadFailures = new ConcurrentQueue<string>();
            if (reference is AnalyzerFileReference fileReference)
                fileReference.AnalyzerLoadFailed += OnAnalyzerLoadFailed;
            try
            {
                var generators = reference.GetGenerators(LanguageNames.CSharp).ToArray();
                if (!loadFailures.IsEmpty)
                    throw new InvalidOperationException(
                        "Failed to load analyzer reference '"
                            + reference.Display
                            + "': "
                            + String.Join("; ", loadFailures)
                    );
                var analyzerPath = reference.FullPath ?? reference.Display ?? "";
                var analyzerHash = File.Exists(analyzerPath)
                    ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(analyzerPath)))
                    : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(analyzerPath)));
                for (var ordinal = 0; ordinal < generators.Length; ordinal++)
                {
                    var generator = generators[ordinal];
                    loaded.Add(
                        new LuiPreparationGenerator(
                            analyzerHash
                                + "/"
                                + ordinal.ToString(
                                    System.Globalization.CultureInfo.InvariantCulture
                                ),
                            analyzerPath,
                            analyzerHash,
                            ordinal,
                            generator
                        )
                    );
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    "Failed to load analyzer reference '"
                        + reference.Display
                        + "': "
                        + exception.Message,
                    exception
                );
            }
            finally
            {
                if (reference is AnalyzerFileReference loadedFileReference)
                    loadedFileReference.AnalyzerLoadFailed -= OnAnalyzerLoadFailed;
            }

            void OnAnalyzerLoadFailed(object? sender, AnalyzerLoadFailureEventArgs args) =>
                loadFailures.Enqueue(args.Message);
        }
        return loaded.ToImmutable();
    }

    private static bool IsLucentBuildToolAnalyzer(AnalyzerReference reference)
    {
        var path = reference.FullPath;
        if (String.IsNullOrWhiteSpace(path))
            path = reference.Display;
        var fileName = Path.GetFileName(path);
        return String.Equals(
                fileName,
                "Lucent.Lui.Compiler.dll",
                StringComparison.OrdinalIgnoreCase
            )
            || String.Equals(
                fileName,
                "Lucent.Lui.Generator.dll",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private sealed class SnapshotAdditionalText(string path, SourceText text) : AdditionalText
    {
        private readonly SourceText text = text;

        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return text;
        }
    }
}
