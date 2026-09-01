using Lucent.Lui.Compiler;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

namespace Lucent.Lui.LanguageServer;

internal sealed class LuiProjectContext : IDisposable
{
    private readonly object gate = new();
    private readonly MSBuildWorkspace workspace;
    private readonly ProjectId projectId;
    private readonly Dictionary<Uri, GeneratedDocument> generated = [];
    private Solution solution;
    private long epoch;
    private bool disposed;

    private LuiProjectContext(MSBuildWorkspace workspace, Project project)
    {
        this.workspace = workspace;
        projectId = project.Id;
        solution = project.Solution;
    }

    internal static async Task<LuiProjectContext> LoadAsync(
        string projectPath,
        CancellationToken cancellationToken
    )
    {
        if (!File.Exists(projectPath))
            throw new ArgumentException(
                "An existing project path is required.",
                nameof(projectPath)
            );
        if (!MSBuildLocator.IsRegistered)
            MSBuildLocator.RegisterDefaults();
        var workspace = MSBuildWorkspace.Create();
        try
        {
            return new LuiProjectContext(
                workspace,
                await workspace
                    .OpenProjectAsync(projectPath, cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
            );
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    internal Task<LuiNavigationTarget?> NavigateAsync(
        Uri uri,
        int offset,
        CancellationToken cancellationToken
    ) => NavigateAsync(uri, offset, null, cancellationToken);

    internal async Task<LuiNavigationTarget?> NavigateAsync(
        Uri uri,
        int offset,
        Func<Task>? beforeCommit,
        CancellationToken cancellationToken
    )
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (generated.TryGetValue(uri, out var cached))
                return cached.ToSource(offset);
        }
        if (!Owns(uri))
            return null;
        var published = await CompileAsync(uri, cancellationToken).ConfigureAwait(false);
        if (
            published is null
            || !await IsCurrentAsync(published.Result, cancellationToken).ConfigureAwait(false)
        )
            return null;
        var entry = published
            .Result.Map.FromSource(new LuiSpan(offset, 0))
            .Where(item => !item.Hidden)
            .OrderBy(item => item.Kind == LuiMapKind.Symbol ? 0 : 1)
            .ThenBy(item => item.Generated.Length)
            .FirstOrDefault();
        if (beforeCommit is not null)
            await beforeCommit().ConfigureAwait(false);
        lock (gate)
        {
            if (disposed || !generated.TryGetValue(published.GeneratedUri, out var cached))
                return null;
            if (entry is null)
                return null;
            return DeclarationTarget(published, entry)
                ?? new LuiNavigationTarget(published.GeneratedUri, entry.Generated, cached.Source);
        }
    }

    internal async Task<PublishedDocument?> CompileAsync(
        Uri uri,
        CancellationToken cancellationToken
    )
    {
        lock (gate)
            ThrowIfDisposed();
        if (!Owns(uri))
            return null;
        var snapshot = await SnapshotAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!snapshot.Index.TryGet(snapshot.Document.Path, out _))
            return null;
        var result = LuiCompiler.Compile(
            snapshot.Document.Syntax,
            snapshot.Compilation,
            snapshot.Identity
        );
        if (!result.Success)
            return null;
        var published = new PublishedDocument(
            result,
            new Uri(
                "lucent-lui://generated/"
                    + result.Identity.MapIdentity
                    + "/"
                    + result.Identity.HintName
            ),
            snapshot.Text,
            snapshot.Uri,
            snapshot.Index,
            snapshot.Compilation
        );
        if (!await IsCurrentAsync(result, cancellationToken).ConfigureAwait(false))
            return null;
        lock (gate)
        {
            if (disposed || epoch != snapshot.Epoch)
                return null;
            generated[published.GeneratedUri] = new GeneratedDocument(published);
            return published;
        }
    }

    internal async Task<bool> IsCurrentAsync(
        LuiCompilationResult result,
        CancellationToken cancellationToken
    )
    {
        Uri uri;
        lock (gate)
        {
            if (disposed)
                return false;
            uri = new Uri(
                Project()
                    .AdditionalDocuments.Single(document =>
                        new LuiDocumentIdentity(LogicalPath(Project(), document)).Equals(
                            result.Identity.Document
                        )
                    )
                    .FilePath!,
                UriKind.Absolute
            );
        }
        try
        {
            return result.Identity.CanPublishTo(
                (await SnapshotAsync(uri, cancellationToken).ConfigureAwait(false)).Identity
            );
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal async Task<string?> GetTextAsync(Uri uri, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (generated.TryGetValue(uri, out var cached))
                return cached.Source;
        }
        lock (gate)
            ThrowIfDisposed();
        if (!Owns(uri))
            return null;
        var snapshot = await SnapshotAsync(uri, cancellationToken).ConfigureAwait(false);
        return snapshot.Text.ToString();
    }

    internal string? GetGeneratedText(Uri uri)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            return generated.TryGetValue(uri, out var cached) ? cached.Source : null;
        }
    }

    internal void ReplaceText(Uri uri, string text) => Update(uri, SourceText.From(text));

    internal bool Owns(Uri uri)
    {
        lock (gate)
        {
            if (disposed || !uri.IsFile)
                return false;
            return Project()
                .AdditionalDocuments.Any(document =>
                    String.Equals(
                        Path.GetFullPath(document.FilePath!),
                        Path.GetFullPath(FilePath(uri)),
                        StringComparison.OrdinalIgnoreCase
                    )
                );
        }
    }

    internal void Close(Uri uri)
    {
        if (!Owns(uri))
            return;
        Update(uri, SourceText.From(File.ReadAllText(FilePath(uri))));
    }

    private void Update(Uri uri, SourceText text)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            var document = FindDocument(Project(), uri);
            solution = solution.WithAdditionalDocumentText(document.Id, text);
            epoch++;
            generated.Clear();
        }
    }

    private async Task<Snapshot> SnapshotAsync(Uri uri, CancellationToken cancellationToken)
    {
        Project project;
        long captured;
        lock (gate)
        {
            ThrowIfDisposed();
            project = Project();
            captured = epoch;
        }
        var current = FindDocument(project, uri);
        var documents = new List<LuiProjectDocument>();
        foreach (
            var document in project.AdditionalDocuments.Where(item =>
                item.FilePath!.EndsWith(".lui", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var source = text.ToString();
            documents.Add(
                new LuiProjectDocument(
                    document.FilePath!,
                    LogicalPath(project, document),
                    source,
                    DocumentVersion(project, document, source)
                )
            );
        }
        var compilation =
            await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The evaluated project has no compilation.");
        compilation = compilation.RemoveSyntaxTrees(
            compilation.SyntaxTrees.Where(tree =>
                (tree.FilePath ?? "").Contains(
                    "Lucent.Lui.Generator",
                    StringComparison.OrdinalIgnoreCase
                )
            )
        );
        var index = LuiProjectComponentIndex.Build(compilation, documents, cancellationToken);
        compilation = index.Augment(compilation, current.FilePath!);
        var input = documents.Single(document =>
            String.Equals(document.Path, current.FilePath, StringComparison.OrdinalIgnoreCase)
        );
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
                new LuiDocumentIdentity(input.LogicalPath),
                input.Version,
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
        return new Snapshot(
            captured,
            compilation,
            input,
            identity,
            await current.GetTextAsync(cancellationToken).ConfigureAwait(false),
            uri,
            index
        );
    }

    private static string LogicalPath(Project project, TextDocument document)
    {
        var additional = AdditionalFile(project, document);
        if (
            additional is not null
            && project
                .AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(additional)
                .TryGetValue("build_metadata.AdditionalFiles.LucentLuiLogicalPath", out var path)
            && !String.IsNullOrWhiteSpace(path)
        )
            return path;
        return Path.GetRelativePath(Path.GetDirectoryName(project.FilePath)!, document.FilePath!);
    }

    private static string DocumentVersion(Project project, TextDocument document, string source)
    {
        var additional = AdditionalFile(project, document);
        return
            additional is not null
            && project
                .AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(additional)
                .TryGetValue(
                    "build_metadata.AdditionalFiles.LucentLuiDocumentVersion",
                    out var version
                )
            && !String.IsNullOrEmpty(version)
            ? version
            : LuiDocumentIdentity.Hash(source);
    }

    private static AdditionalText? AdditionalFile(Project project, TextDocument document) =>
        project.AnalyzerOptions.AdditionalFiles.FirstOrDefault(item =>
            String.Equals(
                Path.GetFullPath(item.Path),
                Path.GetFullPath(document.FilePath!),
                StringComparison.OrdinalIgnoreCase
            )
        );

    private Project Project() =>
        solution.GetProject(projectId)
        ?? throw new InvalidOperationException("The evaluated project was removed.");

    private static TextDocument FindDocument(Project project, Uri uri) =>
        !uri.IsFile
            ? throw new ArgumentException("A file URI is required.", nameof(uri))
            : project.AdditionalDocuments.Single(document =>
                String.Equals(
                    Path.GetFullPath(document.FilePath!),
                    Path.GetFullPath(FilePath(uri)),
                    StringComparison.OrdinalIgnoreCase
                )
            );

    private static LuiNavigationTarget? DeclarationTarget(
        PublishedDocument document,
        LuiMapEntry entry
    )
    {
        var options =
            document
                .Compilation.SyntaxTrees.Select(tree => tree.Options)
                .OfType<CSharpParseOptions>()
                .FirstOrDefault()
            ?? CSharpParseOptions.Default;
        var tree = CSharpSyntaxTree.ParseText(document.GeneratedText, options);
        var compilation = document.Compilation.AddSyntaxTrees(tree);
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var position = Math.Min(entry.Generated.Start, root.FullSpan.End - 1);
        for (var node = root.FindToken(position).Parent; node is not null; node = node.Parent)
        {
            var symbol = model.GetSymbolInfo(node).Symbol as IMethodSymbol;
            if (symbol is null)
                continue;
            var identity =
                symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                + "."
                + symbol.MetadataName;
            var declaration = document.Index.Declarations.SingleOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Identity, identity)
            );
            if (declaration is null)
                return null;
            return new LuiNavigationTarget(
                new Uri(declaration.Document.Path),
                declaration.Document.Syntax.Component!.Name.Span,
                declaration.Document.Source
            );
        }
        return null;
    }

    internal static string FilePath(Uri uri)
    {
        if (!uri.IsFile)
            throw new ArgumentException("A file URI is required.", nameof(uri));
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        return
            OperatingSystem.IsWindows()
            && path.Length >= 3
            && path[0] == '/'
            && Char.IsAsciiLetter(path[1])
            && path[2] == ':'
            ? path[1..].Replace('/', Path.DirectorySeparatorChar)
            : path;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            epoch++;
            generated.Clear();
            workspace.Dispose();
        }
    }

    private sealed class Snapshot(
        long epoch,
        Compilation compilation,
        LuiProjectDocument document,
        LuiFreshnessIdentity identity,
        SourceText text,
        Uri uri,
        LuiProjectComponentIndex index
    )
    {
        internal long Epoch { get; } = epoch;
        internal Compilation Compilation { get; } = compilation;
        internal LuiProjectDocument Document { get; } = document;
        internal LuiFreshnessIdentity Identity { get; } = identity;
        internal SourceText Text { get; } = text;
        internal Uri Uri { get; } = uri;
        internal LuiProjectComponentIndex Index { get; } = index;
    }

    internal sealed class PublishedDocument(
        LuiCompilationResult result,
        Uri generatedUri,
        SourceText sourceText,
        Uri sourceUri,
        LuiProjectComponentIndex index,
        Compilation compilation
    )
    {
        internal LuiCompilationResult Result { get; } = result;
        internal Uri GeneratedUri { get; } = generatedUri;
        internal string GeneratedText { get; } = result.Source!;
        internal SourceText SourceText { get; } = sourceText;
        internal Uri SourceUri { get; } = sourceUri;
        internal LuiProjectComponentIndex Index { get; } = index;
        internal Compilation Compilation { get; } = compilation;
    }

    private sealed class GeneratedDocument(PublishedDocument document)
    {
        internal string Source { get; } = document.GeneratedText;

        internal LuiNavigationTarget? ToSource(int offset)
        {
            var entry = document
                .Result.Map.FromGenerated(new LuiSpan(offset, 0))
                .Where(item => !item.Hidden)
                .OrderBy(item => item.Generated.Length)
                .FirstOrDefault();
            return entry is null
                ? null
                : new LuiNavigationTarget(
                    document.SourceUri,
                    entry.Source,
                    document.SourceText.ToString()
                );
        }
    }
}

internal sealed class LuiNavigationTarget(Uri uri, LuiSpan span, string text)
{
    internal Uri Uri { get; } = uri;
    internal LuiSpan Span { get; } = span;
    internal string Text { get; } = text;
}
