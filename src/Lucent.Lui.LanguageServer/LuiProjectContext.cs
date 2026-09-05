using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Lucent.Lui.Compiler;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

namespace Lucent.Lui.LanguageServer;

internal sealed class LuiProjectContext : IDisposable
{
    internal static readonly string[] SemanticTokenTypes =
    [
        "keyword",
        "type",
        "property",
        "enumMember",
    ];

    private static readonly string[] stylePropertyTypes =
    [
        "Lucent.Core.LayoutProperties",
        "Lucent.Core.VisualProperties",
        "Lucent.Core.TypographyProperties",
        "Lucent.Core.InputProperties",
    ];
    private readonly object gate = new();
    private MSBuildWorkspace workspace;
    private ProjectId projectId;
    private readonly string projectPath;
    private readonly Dictionary<Uri, GeneratedDocument> generated = [];
    private readonly ConditionalWeakTable<LuiCompilationResult, SnapshotEpoch> resultEpochs = new();
    private readonly Dictionary<string, SourceText> overlays = new(
        StringComparer.OrdinalIgnoreCase
    );
    private HashSet<string> projectDirectories;
    private Solution solution;
    private long epoch;
    private bool reloadFailed;
    private bool disposed;

    private LuiProjectContext(MSBuildWorkspace workspace, Project project)
    {
        this.workspace = workspace;
        projectId = project.Id;
        projectPath = Path.GetFullPath(project.FilePath!);
        solution = project.Solution;
        projectDirectories = ProjectDirectories(project)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
            if (
                disposed
                || epoch != published.Epoch
                || !generated.TryGetValue(published.GeneratedUri, out var cached)
            )
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
        if (snapshot.MetadataDiagnostic is not null)
            return null;
        if (!snapshot.Index.TryGet(snapshot.Document.Path, out _))
            return null;
        var result = LuiCompiler.Compile(
            snapshot.Document.Syntax,
            snapshot.Compilation,
            snapshot.Identity
        );
        Track(result, snapshot);
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
            snapshot.Compilation,
            snapshot.Epoch,
            snapshot.Document.Syntax,
            snapshot.Freshness
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

    internal async Task<IReadOnlyList<LuiEditorDiagnostic>?> DiagnosticsAsync(
        Uri uri,
        CancellationToken cancellationToken
    )
    {
        lock (gate)
            ThrowIfDisposed();
        if (!Owns(uri))
            return [];
        var snapshot = await SnapshotAsync(uri, cancellationToken).ConfigureAwait(false);
        if (snapshot.MetadataDiagnostic is not null)
        {
            var metadata = EditorDiagnostic(snapshot.Compilation, snapshot.MetadataDiagnostic);
            return await CanPublishAsync(snapshot, cancellationToken).ConfigureAwait(false)
                ? metadata is null
                    ? []
                    : [metadata]
                : null;
        }
        var result = LuiCompiler.Compile(
            snapshot.Document.Syntax,
            snapshot.Compilation,
            snapshot.Identity
        );
        var diagnostics = result
            .Diagnostics.Select(diagnostic => EditorDiagnostic(snapshot.Compilation, diagnostic))
            .Where(diagnostic => diagnostic is not null)
            .Cast<LuiEditorDiagnostic>()
            .ToList();
        foreach (
            var diagnostic in snapshot.Index.Diagnostics.Where(item =>
                String.Equals(item.Document.Path, snapshot.Document.Path, StringComparison.Ordinal)
            )
        )
        {
            var editor = EditorDiagnostic(
                snapshot.Compilation,
                LuiDiagnosticProjection.Index(diagnostic)
            );
            if (editor is not null)
                diagnostics.Add(editor);
        }
        var published = diagnostics
            .OrderBy(diagnostic => diagnostic.Span.Start)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ToArray();
        return await CanPublishAsync(snapshot, cancellationToken).ConfigureAwait(false)
            ? published
            : null;
    }

    internal async Task<IReadOnlyList<LuiDocumentSymbol>?> DocumentSymbolsAsync(
        Uri uri,
        CancellationToken cancellationToken
    )
    {
        lock (gate)
            ThrowIfDisposed();
        if (!Owns(uri))
            return [];
        var snapshot = await SnapshotAsync(uri, cancellationToken).ConfigureAwait(false);
        var syntax = snapshot.Document.Syntax;
        var symbols = new List<LuiDocumentSymbol>();
        if (syntax.Component is { } component)
        {
            var children = component
                .Parameters.Select(parameter => new LuiDocumentSymbol(
                    parameter.Name.Text,
                    13,
                    parameter.Span,
                    parameter.Name.Span,
                    []
                ))
                .Concat(component.Body.SelectMany(StructureSymbols))
                .ToArray();
            symbols.Add(
                new LuiDocumentSymbol(
                    component.Name.Text,
                    12,
                    component.Span,
                    component.Name.Span,
                    children
                )
            );
        }
        symbols.AddRange(
            syntax.Styles.Select(style => new LuiDocumentSymbol(
                style.Name.Text,
                5,
                style.Span,
                style.Name.Span,
                style.Members.SelectMany(StyleSymbols).ToArray()
            ))
        );
        var published = symbols.OrderBy(symbol => symbol.Span.Start).ToArray();
        return await CanPublishAsync(snapshot, cancellationToken).ConfigureAwait(false)
            ? published
            : null;
    }

    internal async Task<int[]?> SemanticTokensAsync(Uri uri, CancellationToken cancellationToken)
    {
        lock (gate)
            ThrowIfDisposed();
        if (!Owns(uri))
            return [];
        var snapshot = await SnapshotAsync(uri, cancellationToken).ConfigureAwait(false);
        if (snapshot.MetadataDiagnostic is not null)
            return [];
        var result = LuiCompiler.Compile(
            snapshot.Document.Syntax,
            snapshot.Compilation,
            snapshot.Identity
        );
        Track(result, snapshot);
        if (result.ProjectionSource is null)
            return [];
        var options =
            snapshot
                .Compilation.SyntaxTrees.Select(tree => tree.Options)
                .OfType<CSharpParseOptions>()
                .FirstOrDefault()
            ?? CSharpParseOptions.Default;
        var tree = CSharpSyntaxTree.ParseText(
            result.ProjectionSource,
            options,
            cancellationToken: cancellationToken
        );
        using var classificationWorkspace = CreateClassificationWorkspace(
            snapshot.Compilation,
            tree,
            result.ProjectionSource
        );
        var classified = await Classifier
            .GetClassifiedSpansAsync(
                classificationWorkspace.Document,
                new TextSpan(0, result.ProjectionSource.Length),
                cancellationToken
            )
            .ConfigureAwait(false);
        var spans = SyntaxSemanticSpans(snapshot.Document.Syntax)
            .Concat(ProjectClassifications(result.Map, classified))
            .Where(span => span.Span.Length != 0 && span.Span.End <= snapshot.Text.Length)
            .OrderBy(span => span.Priority)
            .ThenBy(span => span.Span.Start)
            .ThenBy(span => span.Span.Length)
            .ThenBy(span => span.Type, StringComparer.Ordinal)
            .Aggregate(
                new List<LuiSemanticSpan>(),
                (selected, span) =>
                {
                    if (!selected.Any(other => Overlaps(other.Span, span.Span)))
                        selected.Add(span);
                    return selected;
                }
            )
            .OrderBy(span => span.Span.Start)
            .ThenBy(span => span.Span.Length)
            .ThenBy(span => span.Type, StringComparer.Ordinal)
            .ToArray();
        if (!await CanPublishAsync(snapshot, cancellationToken).ConfigureAwait(false))
            return null;
        return EncodeSemanticTokens(snapshot.Text, spans);
    }

    internal async Task<IReadOnlyList<LuiCompletionItem>> CompletionsAsync(
        Uri uri,
        int offset,
        CancellationToken cancellationToken
    )
    {
        var semantic = await SemanticAsync(uri, offset, cancellationToken).ConfigureAwait(false);
        if (semantic is null)
            return null!;
        var special = SpecialCompletions(semantic, offset);
        if (special is not null)
            return await CanPublishAsync(semantic.Document, cancellationToken).ConfigureAwait(false)
                ? special
                : null!;
        if (semantic.Position < 0)
            return await CanPublishAsync(semantic.Document, cancellationToken).ConfigureAwait(false)
                ? []
                : null!;
        var completions = await RoslynCompletionsAsync(semantic, cancellationToken)
            .ConfigureAwait(false);
        if (
            StyleAssignmentAt(semantic.Document.Syntax, offset) is { } assignment
            && Contains(assignment.Expression.Span, offset)
        )
            completions = DistinctCompletions(Symbols(RootTokens(semantic)).Concat(completions));
        return await CanPublishAsync(semantic.Document, cancellationToken).ConfigureAwait(false)
            ? completions
            : null!;
    }

    private static async Task<IReadOnlyList<LuiCompletionItem>> RoslynCompletionsAsync(
        SemanticDocument semantic,
        CancellationToken cancellationToken
    )
    {
        var host = MefHostServices.Create(
            MefHostServices
                .DefaultAssemblies.Concat([Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features")])
                .Distinct()
        );
        using var workspace = new AdhocWorkspace(host);
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var solution = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "LucentLuiCompletion",
                "LucentLuiCompletion",
                LanguageNames.CSharp,
                compilationOptions: semantic.Model.Compilation.Options,
                parseOptions: semantic.Tree.Options,
                metadataReferences: semantic.Model.Compilation.References
            )
        );
        foreach (
            var tree in semantic.Model.Compilation.SyntaxTrees.Where(tree => tree != semantic.Tree)
        )
        {
            solution = solution.AddDocument(
                DocumentId.CreateNewId(projectId),
                Path.GetFileName(tree.FilePath),
                tree.GetText(cancellationToken),
                filePath: tree.FilePath
            );
        }
        solution = solution.AddDocument(
            documentId,
            "generated.lui.cs",
            SourceText.From(semantic.Document.GeneratedText),
            filePath: semantic.Document.GeneratedUri.AbsoluteUri
        );
        var document = solution.GetDocument(documentId)!;
        var service = CompletionService.GetService(document);
        if (service is null)
            return [];
        var list = await service
            .GetCompletionsAsync(
                document,
                semantic.Position,
                CompletionTrigger.Invoke,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        if (list is null)
            return [];
        var results = new List<LuiCompletionItem>();
        foreach (var item in list.ItemsList)
        {
            var description = await service
                .GetDescriptionAsync(document, item, cancellationToken)
                .ConfigureAwait(false);
            var text = String
                .Concat(description?.TaggedParts.Select(part => part.Text) ?? [])
                .Trim();
            results.Add(
                new LuiCompletionItem(
                    item.DisplayText,
                    CompletionKind(item.Tags),
                    item.InlineDescription ?? text,
                    text.Length == 0 ? null : text
                )
            );
        }
        return DistinctCompletions(results);
    }

    private static LuiCompletionItem[]? SpecialCompletions(SemanticDocument semantic, int offset)
    {
        var syntax = semantic.Document.Syntax;
        var directive = syntax.TopLevel.FirstOrDefault(item =>
            item is LuiNamespaceSyntax or LuiUsingSyntax && Contains(item.Span, offset)
        );
        if (directive is not null)
            return Symbols(NamespaceMembers(DirectiveNamespace(semantic, directive, offset)));

        var element = ElementAt(syntax, offset);
        if (element is not null && Contains(element.Name.Span, offset))
            return Symbols(ComponentSymbols(semantic));
        if (
            element is not null
            && element.OpenAngle.Span.End <= offset
            && offset <= element.OpenCloseAngle.Span.Start
            && !element.Attributes.Any(attribute => Contains(attribute.Value.Span, offset))
        )
            return DistinctCompletions(
                Symbols(ComponentParameters(semantic, element))
                    .Append(new LuiCompletionItem("name", 6, "string name", null))
            );

        if (StyleAssignmentAt(syntax, offset) is { } assignment)
        {
            if (Contains(assignment.Property.Span, offset))
                return Symbols(StyleProperties(semantic));
        }
        if (VariantAt(syntax, offset) is { } variant && Contains(variant.Condition.Span, offset))
            return Symbols(VariantStates(semantic.Model.Compilation));
        if (StyleReferenceAt(syntax, offset))
            return DistinctCompletions(
                syntax.Styles.Select(style => new LuiCompletionItem(
                    style.Name.Text,
                    5,
                    "Style",
                    null
                ))
            );
        return null;
    }

    private static LuiCompletionItem[] Symbols(IEnumerable<ISymbol> symbols) =>
        DistinctCompletions(
            symbols.Select(symbol => new LuiCompletionItem(
                symbol.Name,
                CompletionKind(symbol),
                SymbolText(symbol),
                Documentation(symbol)
            ))
        );

    private static LuiCompletionItem[] DistinctCompletions(IEnumerable<LuiCompletionItem> items) =>
        items
            .GroupBy(item => item.Label + "\0" + item.Kind, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.Label, StringComparer.Ordinal)
            .ToArray();

    internal async Task<LuiHover?> HoverAsync(
        Uri uri,
        int offset,
        CancellationToken cancellationToken
    )
    {
        var semantic = await SemanticAsync(uri, offset, cancellationToken).ConfigureAwait(false);
        if (semantic is not null)
        {
            var root = semantic.Tree.GetRoot(cancellationToken);
            var literal = root.FindToken(
                    Math.Clamp(semantic.Position, 0, Math.Max(0, root.FullSpan.End - 1))
                )
                .Parent?.AncestorsAndSelf()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax>()
                .FirstOrDefault();
            var type = literal is null
                ? null
                : semantic.Model.GetTypeInfo(literal, cancellationToken).Type;
            if (
                type is not null
                && await CanPublishAsync(semantic.Document, cancellationToken).ConfigureAwait(false)
            )
                return new LuiHover(SymbolText(type), null);
        }
        var symbol = semantic is null ? null : SymbolAt(semantic);
        if (
            symbol is null
            || !await CanPublishAsync(semantic!.Document, cancellationToken).ConfigureAwait(false)
        )
            return await GraphHoverAsync(uri, offset, cancellationToken).ConfigureAwait(false);
        symbol = AuthoredLocalSymbol(semantic!, symbol) ?? symbol;
        return new LuiHover(SymbolText(symbol), Documentation(symbol));
    }

    private static ISymbol? AuthoredLocalSymbol(SemanticDocument semantic, ISymbol symbol)
    {
        if (symbol is not IParameterSymbol and not ILocalSymbol)
            return null;
        var root = semantic.Tree.GetRoot();
        var declarations = symbol
            .DeclaringSyntaxReferences.Where(reference => reference.SyntaxTree == semantic.Tree)
            .SelectMany(reference =>
                semantic.Document.Result.Map.FromGenerated(
                    new LuiSpan(reference.Span.Start, reference.Span.Length)
                )
            )
            .Where(entry => !entry.Hidden && entry.Kind == LuiMapKind.Local)
            .Select(entry => entry.Source)
            .Distinct()
            .ToArray();
        if (declarations.Length != 1)
            return null;
        var source = declarations[0];
        var name = semantic.Document.SourceText.ToString().Substring(source.Start, source.Length);
        foreach (
            var entry in semantic
                .Document.Result.Map.FromSource(source)
                .Where(entry =>
                    !entry.Hidden && entry.Kind == LuiMapKind.Local && entry.Source.Equals(source)
                )
                .OrderBy(entry => entry.Generated.Start)
        )
        {
            var token = root.FindToken(entry.Generated.Start);
            if (token.ValueText != name.TrimStart('@') || token.Parent is null)
                continue;
            var declared = semantic.Model.GetDeclaredSymbol(token.Parent);
            if (
                declared is IParameterSymbol or ILocalSymbol
                && IsDeclarationIdentifier(token.Parent, token)
            )
                return declared;
        }
        return null;
    }

    private async Task<LuiHover?> GraphHoverAsync(
        Uri uri,
        int offset,
        CancellationToken cancellationToken
    )
    {
        var snapshot = await RenameSnapshotAsync(uri, cancellationToken).ConfigureAwait(false);
        var target = snapshot is null
            ? null
            : await ResolveRenameTargetAsync(snapshot, uri, offset, cancellationToken)
                .ConfigureAwait(false);
        return
            target is null
            || !await CanPublishAsync(snapshot!, cancellationToken).ConfigureAwait(false)
            ? null
            : new LuiHover(SymbolText(target.Symbol), Documentation(target.Symbol));
    }

    internal async Task<LuiSignatureHelp?> SignatureHelpAsync(
        Uri uri,
        int offset,
        CancellationToken cancellationToken
    )
    {
        var semantic = await SemanticAsync(uri, offset, cancellationToken).ConfigureAwait(false);
        if (semantic is null)
            return null;
        var element = ElementAt(semantic.Document.Syntax, offset);
        if (element is null)
            return null;
        var invocation = InvocationAt(semantic);
        var bound = invocation is null
            ? null
            : semantic.Model.GetSymbolInfo(invocation, CancellationToken.None).Symbol
                as IMethodSymbol;
        var methods = ComponentSymbols(semantic, element.Name.Text)
            .OfType<IMethodSymbol>()
            .Concat(bound is null ? [] : [bound])
            .Distinct(SymbolEqualityComparer.Default)
            .OfType<IMethodSymbol>()
            .ToArray();
        if (methods.Length == 0)
            return null;
        var activeSignature = bound is null
            ? 0
            : Array.FindIndex(
                methods,
                method => SymbolEqualityComparer.Default.Equals(method, bound)
            );
        var activeMethod = methods[Math.Max(0, activeSignature)];
        var attribute = element.Attributes.FirstOrDefault(attribute =>
            Contains(attribute.Span, offset)
        );
        var parameter =
            attribute is not null
                ? activeMethod.Parameters.FirstOrDefault(parameter =>
                    String.Equals(parameter.Name, attribute.Name.Text, StringComparison.Ordinal)
                )
            : element.Children.Any(child => Contains(child.Span, offset))
                ? activeMethod.Parameters.FirstOrDefault(IsDefaultContent)
            : null;
        var help = new LuiSignatureHelp(
            methods
                .Select(method => new LuiSignature(
                    SymbolText(method),
                    method.Parameters.Select(parameter => SymbolText(parameter)).ToArray(),
                    Documentation(method)
                ))
                .ToArray(),
            Math.Max(0, activeSignature),
            parameter?.Ordinal ?? 0
        );
        return await CanPublishAsync(semantic.Document, cancellationToken).ConfigureAwait(false)
            ? help
            : null;
    }

    internal async Task<LuiNavigationTarget?> DefinitionAsync(
        Uri uri,
        int offset,
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
            return await CSharpDefinitionAsync(uri, offset, cancellationToken)
                .ConfigureAwait(false);
        var semantic = await SemanticAsync(uri, offset, cancellationToken).ConfigureAwait(false);
        if (semantic is null)
            return await CSharpDefinitionAsync(uri, offset, cancellationToken)
                .ConfigureAwait(false);
        var symbol = SymbolAt(semantic);
        var target = symbol is null ? null : DeclarationTarget(semantic.Document, symbol);
        if (target is not null)
            return await CanPublishAsync(semantic.Document, cancellationToken).ConfigureAwait(false)
                ? target
                : null;
        var entry = semantic
            .Document.Result.Map.FromSource(new LuiSpan(offset, 0))
            .Where(item => !item.Hidden)
            .OrderBy(item => item.Kind == LuiMapKind.Symbol ? 0 : 1)
            .ThenBy(item => item.Generated.Length)
            .FirstOrDefault();
        var mapped = entry is null ? null : DeclarationTarget(semantic.Document, entry);
        if (mapped is not null)
            return await CanPublishAsync(semantic.Document, cancellationToken).ConfigureAwait(false)
                ? mapped
                : null;
        if (!semantic.Document.Result.Success)
            return await CSharpDefinitionAsync(uri, offset, cancellationToken)
                .ConfigureAwait(false);
        if (
            symbol is not null
            && await CanPublishAsync(semantic.Document, cancellationToken).ConfigureAwait(false)
            && DeclarationTarget(semantic.Document, symbol) is { } declaration
        )
            return declaration;
        return await CSharpDefinitionAsync(uri, offset, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LuiNavigationTarget?> CSharpDefinitionAsync(
        Uri uri,
        int offset,
        CancellationToken cancellationToken
    )
    {
        var snapshot = await RenameSnapshotAsync(uri, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return null;
        var target = await ResolveRenameTargetAsync(snapshot, uri, offset, cancellationToken)
            .ConfigureAwait(false);
        if (target is null)
            return null;
        var definition = await SymbolFinder
            .FindSourceDefinitionAsync(target.Symbol, snapshot.Solution, cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
            return null;
        foreach (var location in definition.Locations.Where(location => location.IsInSource))
        {
            if (
                location.SourceTree is null
                || !TryGenerated(snapshot, location.SourceTree, out var generated)
            )
                continue;
            var token = location
                .SourceTree.GetRoot(cancellationToken)
                .FindToken(location.SourceSpan.Start);
            var spans = RenameSourceSpans(generated, token);
            if (spans.Length != 1)
                return null;
            return await CanPublishAsync(snapshot, cancellationToken).ConfigureAwait(false)
                ? new LuiNavigationTarget(generated.Uri, spans[0], generated.Source)
                : null;
        }
        return null;
    }

    internal async Task<LuiRenameResult?> PrepareRenameAsync(
        Uri uri,
        int offset,
        CancellationToken cancellationToken,
        Func<LuiCompilationResult, LuiCompilationResult>? transformGenerated = null
    )
    {
        var snapshot = await RenameSnapshotAsync(uri, cancellationToken, transformGenerated)
            .ConfigureAwait(false);
        if (snapshot is null)
            return null;
        var target = await ResolveRenameTargetAsync(snapshot, uri, offset, cancellationToken)
            .ConfigureAwait(false);
        if (target is not null && IsCSharp(uri))
        {
            var occurrences = await RenameOccurrencesAsync(
                    snapshot,
                    target,
                    includeDeclaration: false,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (occurrences is null || !occurrences.Any(occurrence => IsLui(occurrence.Uri)))
                return null;
        }
        return
            target is null
            || !await CanPublishAsync(snapshot, cancellationToken).ConfigureAwait(false)
            ? null
            : new LuiRenameResult(target.Uri, target.Span, []);
    }

    internal async Task<LuiRenameResult?> RenameAsync(
        Uri uri,
        int offset,
        string newName,
        CancellationToken cancellationToken,
        Func<Task>? beforeCommit = null,
        Func<LuiCompilationResult, LuiCompilationResult>? transformGenerated = null
    )
    {
        if (
            String.IsNullOrWhiteSpace(newName)
            || !SyntaxFacts.IsValidIdentifier(newName)
            || SyntaxFacts.GetKeywordKind(newName) != SyntaxKind.None
        )
            return null;
        var snapshot = await RenameSnapshotAsync(uri, cancellationToken, transformGenerated)
            .ConfigureAwait(false);
        if (snapshot is null)
            return null;
        var target = await ResolveRenameTargetAsync(snapshot, uri, offset, cancellationToken)
            .ConfigureAwait(false);
        if (target is null)
            return null;
        var edits = new Dictionary<Uri, List<LuiSpan>>();
        var occurrences = await RenameOccurrencesAsync(
                snapshot,
                target,
                includeDeclaration: true,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (
            occurrences is null
            || IsCSharp(uri) && !occurrences.Any(occurrence => IsLui(occurrence.Uri))
        )
            return null;
        foreach (var occurrence in occurrences)
        {
            if (
                occurrence
                    .Model.LookupSymbols(occurrence.Token.SpanStart, name: newName)
                    .Any(symbol => !SameSymbol(symbol, occurrence.Symbol))
            )
                return null;
            if (!AddRenameEdit(edits, occurrence.Uri, occurrence.Span))
                return null;
        }
        if (beforeCommit is not null)
            await beforeCommit().ConfigureAwait(false);
        if (
            !await CanPublishAsync(snapshot, cancellationToken).ConfigureAwait(false)
            || edits.Count == 0
        )
            return null;
        return new LuiRenameResult(
            target.Uri,
            target.Span,
            edits
                .OrderBy(pair => pair.Key.AbsoluteUri, StringComparer.Ordinal)
                .Select(pair => new LuiRenameDocumentEdit(
                    pair.Key,
                    pair.Value.OrderByDescending(span => span.Start).ToArray(),
                    newName
                ))
                .ToArray()
        );
    }

    internal async Task<LuiReferenceResult?> ReferencesAsync(
        Uri uri,
        int offset,
        bool includeDeclaration,
        CancellationToken cancellationToken,
        Func<Task>? beforeCommit = null,
        Func<LuiCompilationResult, LuiCompilationResult>? transformGenerated = null
    )
    {
        var snapshot = await RenameSnapshotAsync(uri, cancellationToken, transformGenerated)
            .ConfigureAwait(false);
        if (snapshot is null)
            return null;
        var target = await ResolveRenameTargetAsync(snapshot, uri, offset, cancellationToken)
            .ConfigureAwait(false);
        if (target is null)
            return null;
        var locations = new Dictionary<Uri, List<LuiSpan>>();
        var occurrences = await RenameOccurrencesAsync(
                snapshot,
                target,
                includeDeclaration,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (
            occurrences is null
            || IsCSharp(uri) && !occurrences.Any(occurrence => IsLui(occurrence.Uri))
        )
            return null;
        foreach (var occurrence in occurrences)
        {
            if (!AddRenameEdit(locations, occurrence.Uri, occurrence.Span))
                return null;
        }
        if (beforeCommit is not null)
            await beforeCommit().ConfigureAwait(false);
        if (!await CanPublishAsync(snapshot, cancellationToken).ConfigureAwait(false))
            return null;
        return new LuiReferenceResult(
            locations
                .OrderBy(pair => pair.Key.AbsoluteUri, StringComparer.Ordinal)
                .SelectMany(pair =>
                    pair.Value.OrderBy(span => span.Start)
                        .Select(span => new LuiReferenceLocation(pair.Key, span))
                )
                .ToArray()
        );
    }

    internal async Task<LuiFormatResult?> FormatAsync(
        Uri uri,
        LuiSpan? range,
        CancellationToken cancellationToken
    )
    {
        var text = await GetTextAsync(uri, cancellationToken).ConfigureAwait(false);
        if (text is null || !Owns(uri))
            return null;
        var formatted = range is { } selection
            ? LuiFormatter.FormatRange(text, selection)
            : LuiFormatter.Format(text);
        var start = 0;
        while (start < text.Length && start < formatted.Length && text[start] == formatted[start])
            start++;
        var oldEnd = text.Length;
        var newEnd = formatted.Length;
        while (oldEnd > start && newEnd > start && text[oldEnd - 1] == formatted[newEnd - 1])
        {
            oldEnd--;
            newEnd--;
        }
        return new LuiFormatResult(new LuiSpan(start, oldEnd - start), formatted[start..newEnd]);
    }

    private async Task<RenameSnapshot?> RenameSnapshotAsync(
        Uri requested,
        CancellationToken cancellationToken,
        Func<LuiCompilationResult, LuiCompilationResult>? transformGenerated = null
    )
    {
        Project project;
        long captured;
        lock (gate)
        {
            ThrowIfDisposed();
            project = Project();
            captured = epoch;
            if (
                !ProjectGraph(project)
                    .Any(graphProject =>
                        graphProject.Documents.Any(document =>
                            SameFile(document.FilePath, requested)
                        )
                        || graphProject.AdditionalDocuments.Any(document =>
                            SameFile(document.FilePath, requested)
                        )
                    )
            )
                return null;
        }
        var sourceByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var generatedDocuments = new List<(DocumentId Id, RenameGeneratedDocument Document)>();
        var renameSolution = project.Solution;
        foreach (var graphProject in ProjectGraph(project))
        {
            var current = renameSolution.GetProject(graphProject.Id);
            var compilation = current is null
                ? null
                : await current.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (current is null || compilation is null)
                return null;
            compilation = compilation.RemoveSyntaxTrees(
                compilation.SyntaxTrees.Where(tree =>
                    (tree.FilePath ?? "").Contains(
                        "Lucent.Lui.Generator",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            );
            var inputs = new List<LuiProjectDocument>();
            foreach (
                var document in current.AdditionalDocuments.Where(document =>
                    document.FilePath!.EndsWith(".lui", StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                var source = (
                    await document.GetTextAsync(cancellationToken).ConfigureAwait(false)
                ).ToString();
                var logical = LogicalPath(current, document);
                if (!LuiDocumentIdentity.TryCreate(logical, out var documentIdentity))
                    return null;
                inputs.Add(
                    new LuiProjectDocument(
                        document.FilePath!,
                        documentIdentity!.LogicalPath,
                        source,
                        DocumentVersion(current, document, source)
                    )
                );
                sourceByPath[document.FilePath!] = source;
            }
            var index = LuiProjectComponentIndex.Build(compilation, inputs, cancellationToken);
            if (index.Diagnostics.Count != 0)
                return null;
            var globals = current.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions;
            globals.TryGetValue("build_property.LucentLuiProjectEpoch", out var projectEpoch);
            globals.TryGetValue("build_property.LucentLuiProjectIdentity", out var projectIdentity);
            globals.TryGetValue("build_property.LucentLuiCompilerOptions", out var options);
            globals.TryGetValue("build_property.LucentLuiDefines", out var defines);
            var parse = current.ParseOptions as CSharpParseOptions ?? CSharpParseOptions.Default;
            foreach (var document in inputs)
            {
                var identity = new LuiFreshnessIdentity(
                    projectEpoch ?? "",
                    projectIdentity ?? current.FilePath ?? current.Name,
                    new LuiDocumentIdentity(document.LogicalPath),
                    document.Version,
                    "",
                    index.Generation,
                    parse.LanguageVersion.ToString(),
                    "",
                    "",
                    "",
                    options ?? "",
                    defines ?? "",
                    current.DefaultNamespace ?? ""
                );
                var result = LuiCompiler.Compile(
                    document.Syntax,
                    index.Augment(compilation, document.Path),
                    identity
                );
                if (transformGenerated is not null)
                    result = transformGenerated(result);
                if (!result.Success || result.ProjectionSource is null)
                    return null;
                var generatedUri = new Uri(
                    "lucent-lui://generated/"
                        + result.Identity.MapIdentity
                        + "/"
                        + result.Identity.HintName
                );
                var documentId = DocumentId.CreateNewId(current.Id, result.Identity.HintName);
                renameSolution = renameSolution.AddDocument(
                    documentId,
                    result.Identity.HintName,
                    SourceText.From(result.ProjectionSource),
                    filePath: generatedUri.AbsoluteUri
                );
                generatedDocuments.Add(
                    (
                        documentId,
                        new RenameGeneratedDocument(
                            current.Id,
                            new Uri(document.Path),
                            document.Source,
                            document.Syntax,
                            result.Map,
                            result.Identity
                        )
                    )
                );
            }
        }
        var generated = new Dictionary<SyntaxTree, RenameGeneratedDocument>();
        var trees = new Dictionary<SyntaxTree, ProjectId>();
        foreach (var item in generatedDocuments)
        {
            var document = renameSolution.GetDocument(item.Id);
            var tree = document is null
                ? null
                : await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            if (document is null || tree is null)
                return null;
            generated[tree] = item.Document;
            trees[tree] = document.Project.Id;
        }
        var csharp = new Dictionary<SyntaxTree, Uri>();
        foreach (var graphProject in ProjectGraph(project))
        {
            var current = renameSolution.GetProject(graphProject.Id);
            if (current is null)
                return null;
            foreach (
                var document in current.Documents.Where(document => document.FilePath is not null)
            )
            {
                var tree = await document
                    .GetSyntaxTreeAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (tree is null)
                    return null;
                csharp[tree] = new Uri(document.FilePath!);
                trees[tree] = current.Id;
            }
        }
        return new RenameSnapshot(
            captured,
            renameSolution,
            project.Id,
            generated,
            csharp,
            sourceByPath,
            trees,
            generated.Values.Select(document => document.Freshness).ToArray()
        );
    }

    private bool RenameSnapshotCurrent(RenameSnapshot snapshot)
    {
        lock (gate)
            return !disposed && !reloadFailed && epoch == snapshot.Epoch;
    }

    private async Task<bool> CanPublishAsync(
        RenameSnapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        foreach (var freshness in snapshot.GeneratedFreshness)
        {
            if (!await IsCurrentAsync(freshness, cancellationToken).ConfigureAwait(false))
                return false;
        }
        return RenameSnapshotCurrent(snapshot);
    }

    private static async Task<RenameTarget?> ResolveRenameTargetAsync(
        RenameSnapshot snapshot,
        Uri uri,
        int offset,
        CancellationToken cancellationToken,
        bool expandOwnerDeclarations = true
    )
    {
        if (
            snapshot.CSharp.FirstOrDefault(pair => SameFile(pair.Value.LocalPath, uri)) is
            { Key: { } csharpTree, Value: { } csharpUri }
        )
        {
            var model = await SemanticModelAsync(snapshot, csharpTree, cancellationToken)
                .ConfigureAwait(false);
            if (model is null)
                return null;
            var token = csharpTree
                .GetRoot(cancellationToken)
                .FindToken(Math.Clamp(offset, 0, Math.Max(0, csharpTree.Length - 1)));
            var symbol = SymbolForToken(model, token);
            var target = symbol is null
                ? null
                : new RenameTarget(
                    csharpUri,
                    new LuiSpan(token.SpanStart, token.Span.Length),
                    [symbol],
                    null
                );
            return target is null || !expandOwnerDeclarations
                ? target
                : await ExpandOwnerComponentTargetAsync(snapshot, target, cancellationToken)
                    .ConfigureAwait(false);
        }
        var ownerTargets = new List<(RenameTarget Target, bool IsComponentDeclaration)>();
        foreach (var pair in snapshot.Generated)
        {
            if (!SameFile(pair.Value.Uri.LocalPath, uri))
                continue;
            var entries = pair
                .Value.Map.FromSource(new LuiSpan(offset, 0))
                .Where(entry => !entry.Hidden && entry.Generated.Length != 0)
                .OrderBy(entry => entry.Kind == LuiMapKind.Symbol ? 0 : 1)
                .ThenBy(entry => entry.Generated.Length)
                .ToArray();
            if (entries.Length == 0)
                return null;
            var root = pair.Key.GetRoot(cancellationToken);
            var model = await SemanticModelAsync(snapshot, pair.Key, cancellationToken)
                .ConfigureAwait(false);
            if (model is null)
                return null;
            var targets = new List<RenameTarget>();
            foreach (var entry in entries)
            {
                var token =
                    entry.Kind == LuiMapKind.Local
                    && root.FindToken(entry.Generated.Start)
                        .Span.Equals(new TextSpan(entry.Generated.Start, entry.Generated.Length))
                        ? root.FindToken(entry.Generated.Start)
                    : entry.Source.Length == entry.Generated.Length
                        ? root.FindToken(
                            entry.Generated.Start
                                + Math.Clamp(
                                    offset - entry.Source.Start,
                                    0,
                                    entry.Generated.Length - 1
                                )
                        )
                    : root.DescendantTokens()
                        .Where(token =>
                            token.SpanStart >= entry.Generated.Start
                            && token.Span.End <= entry.Generated.End
                        )
                        .FirstOrDefault(token =>
                            token.ValueText
                            == pair.Value.Source.Substring(entry.Source.Start, entry.Source.Length)
                        );
                var symbol = token.RawKind == 0 ? null : SymbolForToken(model, token);
                var spans = token.RawKind == 0 ? [] : RenameSourceSpans(pair.Value, token);
                var span =
                    spans.Contains(entry.Source) ? entry.Source
                    : spans.Length == 1 ? spans[0]
                    : (LuiSpan?)null;
                if (symbol is null || span is null)
                    return null;
                targets.Add(
                    new RenameTarget(
                        pair.Value.Uri,
                        span.Value,
                        [symbol],
                        LocalDeclarationFor(snapshot, symbol)
                    )
                );
            }
            var target = targets[0];
            var ownerTarget = target.LocalDeclaration is { } local
                ? targets.All(candidate => candidate.LocalDeclaration?.Equals(local) == true)
                    ? target
                    : null
                : targets.All(candidate =>
                    SameSymbol(candidate.Symbol, target.Symbol)
                    && candidate.Span.Equals(target.Span)
                )
                    ? target
                    : null;
            if (ownerTarget is null)
                return null;
            ownerTargets.Add(
                (
                    ownerTarget,
                    pair.Value.Syntax.Component is { } component
                        && component.Name.Span.Equals(ownerTarget.Span)
                )
            );
        }
        if (ownerTargets.Count == 0)
            return null;
        var first = ownerTargets[0].Target;
        if (
            ownerTargets.Any(candidate =>
                candidate.Target.Uri != first.Uri
                || !candidate.Target.Span.Equals(first.Span)
                || candidate.Target.LocalDeclaration != first.LocalDeclaration
            )
        )
            return null;
        var symbols = ownerTargets.Select(candidate => candidate.Target.Symbol).ToArray();
        if (
            first.LocalDeclaration is null
            && !ownerTargets.All(candidate => candidate.IsComponentDeclaration)
            && !symbols.All(symbol => SameSymbol(symbol, first.Symbol))
        )
            return null;
        var resolved = new RenameTarget(first.Uri, first.Span, symbols, first.LocalDeclaration);
        return !expandOwnerDeclarations
            ? resolved
            : await ExpandOwnerComponentTargetAsync(snapshot, resolved, cancellationToken)
                .ConfigureAwait(false);
    }

    private static async Task<RenameTarget?> ExpandOwnerComponentTargetAsync(
        RenameSnapshot snapshot,
        RenameTarget target,
        CancellationToken cancellationToken
    )
    {
        if (
            target.Symbols.Count != 1
            || target.LocalDeclaration is not null
            || target.Symbol is not IMethodSymbol method
            || !IsComponent(method)
        )
            return target;
        var definition = await SymbolFinder
            .FindSourceDefinitionAsync(target.Symbol, snapshot.Solution, cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
            return target;
        foreach (var location in definition.Locations.Where(location => location.IsInSource))
        {
            if (location.SourceTree is null)
                continue;
            if (!TryGenerated(snapshot, location.SourceTree, out var generated))
                continue;
            var component = generated.Syntax.Component;
            if (component is null)
                return null;
            var token = location
                .SourceTree.GetRoot(cancellationToken)
                .FindToken(location.SourceSpan.Start);
            var spans = RenameSourceSpans(generated, token);
            if (spans.Length != 1 || !spans[0].Equals(component.Name.Span))
                return null;
            var owners = await ResolveRenameTargetAsync(
                    snapshot,
                    generated.Uri,
                    component.Name.Span.Start,
                    cancellationToken,
                    expandOwnerDeclarations: false
                )
                .ConfigureAwait(false);
            return owners is null
                ? null
                : new RenameTarget(
                    target.Uri,
                    target.Span,
                    owners.Symbols,
                    target.LocalDeclaration
                );
        }
        return target;
    }

    private static async Task<SemanticModel?> SemanticModelAsync(
        RenameSnapshot snapshot,
        SyntaxTree tree,
        CancellationToken cancellationToken
    )
    {
        if (!snapshot.Trees.TryGetValue(tree, out var projectId))
            projectId = snapshot
                .Trees.FirstOrDefault(pair =>
                    String.Equals(
                        pair.Key.FilePath,
                        tree.FilePath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .Value;
        if (projectId is null)
            return null;
        var project = snapshot.Solution.GetProject(projectId);
        var compilation = project is null
            ? null
            : await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        return compilation is null || !compilation.SyntaxTrees.Contains(tree)
            ? null
            : compilation.GetSemanticModel(tree);
    }

    private static LuiLocalProvenance? LocalDeclarationFor(RenameSnapshot snapshot, ISymbol symbol)
    {
        if (symbol is not IParameterSymbol and not ILocalSymbol)
            return null;
        var spans = symbol
            .DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
            .Select(node =>
                TryGenerated(snapshot, node.SyntaxTree, out var generated)
                    ? RenameSourceSpan(generated, node.GetFirstToken()) is { } span
                    && generated
                        .Map.FromGenerated(new LuiSpan(node.SpanStart, node.Span.Length))
                        .Any(entry => entry.Kind == LuiMapKind.Local && entry.Source.Equals(span))
                        ? new LuiLocalProvenance(generated.Uri, span)
                        : (LuiLocalProvenance?)null
                    : null
            )
            .Where(span => span is not null)
            .Select(span => span!.Value)
            .Distinct()
            .ToArray();
        return spans.Length == 1 ? spans[0] : null;
    }

    private static async Task<IReadOnlyList<RenameOccurrence>?> RenameOccurrencesAsync(
        RenameSnapshot snapshot,
        RenameTarget target,
        bool includeDeclaration,
        CancellationToken cancellationToken
    )
    {
        var locations = new List<(Location Location, ISymbol Symbol)>();
        if (target.LocalDeclaration is { } local)
        {
            foreach (var pair in snapshot.Generated)
            {
                var model = await SemanticModelAsync(snapshot, pair.Key, cancellationToken)
                    .ConfigureAwait(false);
                if (model is null)
                    return null;
                foreach (var token in pair.Key.GetRoot(cancellationToken).DescendantTokens())
                {
                    var symbol = SymbolForToken(model, token);
                    if (
                        symbol is not null
                        && LocalDeclarationFor(snapshot, symbol)?.Equals(local) == true
                    )
                        locations.Add((token.GetLocation(), symbol));
                }
            }
        }
        else
        {
            foreach (var targetSymbol in target.Symbols)
            {
                var definition = await SymbolFinder
                    .FindSourceDefinitionAsync(targetSymbol, snapshot.Solution, cancellationToken)
                    .ConfigureAwait(false);
                if (
                    definition is null
                    || !definition.Locations.Any(location => location.IsInSource)
                )
                    return null;
                var references = await SymbolFinder
                    .FindReferencesAsync(definition, snapshot.Solution, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var reference in references)
                {
                    if (includeDeclaration)
                        locations.AddRange(
                            reference
                                .Definition.Locations.Where(location => location.IsInSource)
                                .Select(location => (location, reference.Definition))
                        );
                    locations.AddRange(
                        reference.Locations.Select(location =>
                            (location.Location, reference.Definition)
                        )
                    );
                }
            }
        }
        var result = new List<RenameOccurrence>();
        foreach (var (location, symbol) in locations)
        {
            if (!location.IsInSource || location.SourceTree is null)
                return null;
            var tree = location.SourceTree;
            var model = await SemanticModelAsync(snapshot, tree, cancellationToken)
                .ConfigureAwait(false);
            if (model is null)
            {
                return null;
            }
            var token = tree.GetRoot(cancellationToken).FindToken(location.SourceSpan.Start);
            if (!token.Span.IntersectsWith(location.SourceSpan))
                return null;
            if (
                !includeDeclaration
                && token.Parent is { } parent
                && IsDeclarationIdentifier(parent, token)
            )
                continue;
            Uri occurrenceUri;
            LuiSpan[] spans;
            if (TryGenerated(snapshot, tree, out var generated))
            {
                if (IsImplicitContentArgument(generated, token, symbol))
                    continue;
                occurrenceUri = generated.Uri;
                spans = RenameSourceSpans(generated, token);
            }
            else if (TryCSharp(snapshot, tree, out var csharp))
            {
                occurrenceUri = csharp;
                spans = [new LuiSpan(token.SpanStart, token.Span.Length)];
            }
            else
            {
                return null;
            }
            if (spans.Length == 0)
            {
                return null;
            }
            foreach (var span in spans)
                result.Add(new RenameOccurrence(occurrenceUri, span, token, model, symbol));
        }
        return result;
    }

    private static bool IsImplicitContentArgument(
        RenameGeneratedDocument document,
        SyntaxToken token,
        ISymbol symbol
    )
    {
        if (
            symbol is not IParameterSymbol parameter
            || !IsDefaultContent(parameter)
            || token.Parent is not IdentifierNameSyntax { Parent: NameColonSyntax name }
            || name.Parent
                is not ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax invocation }
        )
            return false;
        var mappings = document.Map.FromGenerated(new LuiSpan(token.SpanStart, token.Span.Length));
        if (
            mappings.Count != 1
            || mappings[0] is not { Hidden: true, Kind: LuiMapKind.Scaffolding }
            || !mappings[0].Source.Equals(new LuiSpan(-1, 0))
            || mappings[0].Generated.Start != token.SpanStart
            || mappings[0].Generated.End != name.Span.End + 1
        )
            return false;
        var owners = document
            .Map.FromGenerated(
                new LuiSpan(invocation.Expression.SpanStart, invocation.Expression.Span.Length)
            )
            .Where(entry => !entry.Hidden)
            .Select(entry => entry.Source)
            .ToArray();
        var elements = Elements(document.Syntax)
            .Where(element => owners.Contains(element.Name.Span))
            .ToArray();
        return elements.Length == 1
            && !elements[0]
                .Attributes.Any(attribute => attribute.Name.Text.TrimStart('@') == parameter.Name);
    }

    private static bool TryGenerated(
        RenameSnapshot snapshot,
        SyntaxTree tree,
        out RenameGeneratedDocument document
    )
    {
        if (snapshot.Generated.TryGetValue(tree, out document!))
            return true;
        var pair = snapshot.Generated.FirstOrDefault(pair =>
            String.Equals(pair.Key.FilePath, tree.FilePath, StringComparison.Ordinal)
        );
        document = pair.Value!;
        return pair.Key is not null;
    }

    private static bool TryCSharp(RenameSnapshot snapshot, SyntaxTree tree, out Uri uri)
    {
        if (snapshot.CSharp.TryGetValue(tree, out uri!))
            return true;
        var pair = snapshot.CSharp.FirstOrDefault(pair =>
            String.Equals(pair.Key.FilePath, tree.FilePath, StringComparison.OrdinalIgnoreCase)
        );
        uri = pair.Value!;
        return pair.Key is not null;
    }

    private static ISymbol? SymbolForToken(SemanticModel model, SyntaxToken token)
    {
        var node = token.Parent;
        if (node is null)
            return null;
        var declared = model.GetDeclaredSymbol(node);
        if (declared is not null && IsDeclarationIdentifier(node, token))
            return declared;
        if (node is not IdentifierNameSyntax and not GenericNameSyntax)
            return null;
        var info = model.GetSymbolInfo(node);
        return info.Symbol ?? (info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] : null);
    }

    private static bool IsDeclarationIdentifier(SyntaxNode node, SyntaxToken token) =>
        node switch
        {
            BaseTypeDeclarationSyntax declaration => declaration.Identifier == token,
            DelegateDeclarationSyntax declaration => declaration.Identifier == token,
            MethodDeclarationSyntax declaration => declaration.Identifier == token,
            PropertyDeclarationSyntax declaration => declaration.Identifier == token,
            EventDeclarationSyntax declaration => declaration.Identifier == token,
            EnumMemberDeclarationSyntax declaration => declaration.Identifier == token,
            VariableDeclaratorSyntax declaration => declaration.Identifier == token,
            SingleVariableDesignationSyntax declaration => declaration.Identifier == token,
            ParameterSyntax declaration => declaration.Identifier == token,
            TypeParameterSyntax declaration => declaration.Identifier == token,
            _ => false,
        };

    private static bool SameSymbol(ISymbol? left, ISymbol? right)
    {
        if (left is IAliasSymbol leftAlias)
            left = leftAlias.Target;
        if (right is IAliasSymbol rightAlias)
            right = rightAlias.Target;
        return left is not null
            && right is not null
            && SymbolEqualityComparer.Default.Equals(
                left.OriginalDefinition,
                right.OriginalDefinition
            );
    }

    private static LuiSpan? RenameSourceSpan(RenameGeneratedDocument document, SyntaxToken token)
    {
        var spans = RenameSourceSpans(document, token);
        return spans.Length == 1 ? spans[0] : null;
    }

    private static LuiSpan[] RenameSourceSpans(RenameGeneratedDocument document, SyntaxToken token)
    {
        var candidates = new List<LuiSpan>();
        foreach (
            var entry in document.Map.FromGenerated(new LuiSpan(token.SpanStart, token.Span.Length))
        )
        {
            if (entry.Hidden || entry.Source.Start < 0 || entry.Generated.Length == 0)
                continue;
            var source = document.Source.Substring(entry.Source.Start, entry.Source.Length);
            if (
                source == token.Text
                || source == token.ValueText
                || entry.Kind == LuiMapKind.Local
                    && entry.Generated.Start == token.SpanStart
                    && entry.Generated.Length == token.Span.Length
                    && IsStructuralAlias(token.ValueText, source)
            )
                candidates.Add(entry.Source);
            else if (entry.Source.Length == entry.Generated.Length)
            {
                var start = entry.Source.Start + token.SpanStart - entry.Generated.Start;
                if (
                    start >= entry.Source.Start
                    && start + token.Span.Length <= entry.Source.End
                    && document.Source.Substring(start, token.Span.Length) == token.Text
                )
                    candidates.Add(new LuiSpan(start, token.Span.Length));
            }
        }
        var spans = candidates.Distinct().ToArray();
        if (spans.Length <= 1)
            return spans;
        return Elements(document.Syntax)
            .Any(element =>
                !element.CloseName.IsMissing
                && element.Name.Text == element.CloseName.Text
                && spans.Length == 2
                && spans.All(span =>
                    span.Equals(element.Name.Span) || span.Equals(element.CloseName.Span)
                )
            )
            ? spans
            : [];
    }

    private static bool IsStructuralAlias(string generated, string authored)
    {
        const string prefix = "__luiLocal";
        if (!SyntaxFacts.IsValidIdentifier(authored))
            return false;
        var suffix = "_" + authored.TrimStart('@');
        if (
            !generated.StartsWith(prefix, StringComparison.Ordinal)
            || !generated.EndsWith(suffix, StringComparison.Ordinal)
        )
            return false;
        var ordinalLength = generated.Length - prefix.Length - suffix.Length;
        return ordinalLength > 0
            && generated.AsSpan(prefix.Length, ordinalLength).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    private static bool AddRenameEdit(Dictionary<Uri, List<LuiSpan>> edits, Uri uri, LuiSpan span)
    {
        if (!edits.TryGetValue(uri, out var ranges))
            edits[uri] = ranges = [];
        if (ranges.Any(existing => existing.Equals(span)))
            return true;
        if (ranges.Any(existing => existing.Start < span.End && span.Start < existing.End))
            return false;
        ranges.Add(span);
        return true;
    }

    private static bool IsCSharp(Uri uri) =>
        String.Equals(Path.GetExtension(uri.LocalPath), ".cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsLui(Uri uri) =>
        String.Equals(Path.GetExtension(uri.LocalPath), ".lui", StringComparison.OrdinalIgnoreCase);

    private static bool SameFile(string? path, Uri uri) =>
        path is not null
        && uri.IsFile
        && String.Equals(
            Path.GetFullPath(path),
            Path.GetFullPath(FilePath(uri)),
            StringComparison.OrdinalIgnoreCase
        );

    internal async Task<bool> IsCurrentAsync(
        LuiCompilationResult result,
        CancellationToken cancellationToken,
        Func<Task>? beforeFreshnessSnapshot = null
    )
    {
        SnapshotEpoch? captured;
        lock (gate)
        {
            if (
                !resultEpochs.TryGetValue(result, out captured)
                || disposed
                || captured.Value != epoch
            )
                return false;
        }
        return await IsCurrentAsync(captured.Freshness, cancellationToken, beforeFreshnessSnapshot)
            .ConfigureAwait(false);
    }

    private void Track(LuiCompilationResult result, Snapshot snapshot) =>
        resultEpochs.Add(result, new SnapshotEpoch(snapshot.Epoch, snapshot.Freshness));

    private async Task<bool> IsCurrentAsync(
        LuiFreshnessTarget freshness,
        CancellationToken cancellationToken,
        Func<Task>? beforeSnapshot = null
    )
    {
        try
        {
            lock (gate)
            {
                if (disposed || reloadFailed)
                    return false;
                if (
                    solution.GetProject(freshness.ProjectId) is not { } project
                    || !project.AdditionalDocuments.Any(document =>
                        SameFile(document.FilePath, freshness.Uri)
                    )
                )
                    return false;
            }
            if (beforeSnapshot is not null)
                await beforeSnapshot().ConfigureAwait(false);
            var current = await SnapshotAsync(freshness.ProjectId, freshness.Uri, cancellationToken)
                .ConfigureAwait(false);
            return current.Freshness.ProjectId == freshness.ProjectId
                && current.Freshness.Uri == freshness.Uri
                && current.Identity.Equals(freshness.Identity);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private async Task<bool> CanPublishAsync(Snapshot snapshot, CancellationToken cancellationToken)
    {
        if (!await IsCurrentAsync(snapshot.Freshness, cancellationToken).ConfigureAwait(false))
            return false;
        lock (gate)
            return !disposed && epoch == snapshot.Epoch;
    }

    private async Task<bool> CanPublishAsync(
        PublishedDocument document,
        CancellationToken cancellationToken
    )
    {
        if (!await IsCurrentAsync(document.Freshness, cancellationToken).ConfigureAwait(false))
            return false;
        lock (gate)
            return !disposed && epoch == document.Epoch;
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
        if (CanEdit(uri))
        {
            TextDocument? document;
            lock (gate)
                document = FindTextDocuments(Project(), uri).FirstOrDefault();
            return document is null
                ? null
                : (await document.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
        }
        return null;
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

    internal async Task<bool> ReloadIfRelevantAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!IsRelevantProjectInput(uri))
            return false;
        var reloaded = MSBuildWorkspace.Create();
        try
        {
            var project = await reloaded
                .OpenProjectAsync(projectPath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            lock (gate)
            {
                ThrowIfDisposed();
                foreach (var (path, text) in overlays)
                {
                    var documents = FindTextDocuments(project, new Uri(path)).ToArray();
                    var next = project.Solution;
                    foreach (var document in documents)
                    {
                        next =
                            document is AdditionalDocument
                                ? next.WithAdditionalDocumentText(document.Id, text)
                                : next.WithDocumentText(document.Id, text);
                    }
                    project = next.GetProject(project.Id)!;
                }
                var previous = workspace;
                workspace = reloaded;
                projectId = project.Id;
                solution = project.Solution;
                projectDirectories = ProjectDirectories(project)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                reloadFailed = false;
                epoch++;
                generated.Clear();
                previous.Dispose();
            }
            return true;
        }
        catch
        {
            reloaded.Dispose();
            lock (gate)
            {
                if (!disposed)
                {
                    reloadFailed = true;
                    epoch++;
                    generated.Clear();
                }
            }
            return true;
        }
    }

    internal IReadOnlyList<string> ProjectDirectories()
    {
        lock (gate)
            return projectDirectories
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    internal bool Owns(Uri uri)
    {
        lock (gate)
        {
            if (disposed || reloadFailed || !uri.IsFile)
                return false;
            return ProjectGraph(Project())
                .Any(project =>
                    project.AdditionalDocuments.Any(document => SameFile(document.FilePath, uri))
                );
        }
    }

    internal bool CanEdit(Uri uri)
    {
        lock (gate)
            return !disposed && FindTextDocuments(Project(), uri).Any();
    }

    internal void Close(Uri uri)
    {
        if (!CanEdit(uri))
            return;
        Update(uri, SourceText.From(File.ReadAllText(FilePath(uri))), false);
    }

    private void Update(Uri uri, SourceText text, bool overlay = true)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            var documents = FindTextDocuments(Project(), uri).ToArray();
            if (documents.Length == 0)
                throw new ArgumentException("An evaluated document is required.", nameof(uri));
            foreach (var document in documents)
                solution =
                    document is AdditionalDocument
                        ? solution.WithAdditionalDocumentText(document.Id, text)
                        : solution.WithDocumentText(document.Id, text);
            if (overlay)
                overlays[FilePath(uri)] = text;
            else
                overlays.Remove(FilePath(uri));
            epoch++;
            generated.Clear();
        }
    }

    private bool IsRelevantProjectInput(Uri uri)
    {
        if (!uri.IsFile)
            return false;
        var path = Path.GetFullPath(FilePath(uri));
        var name = Path.GetFileName(path);
        if (
            IsProjectAncestor(Path.GetDirectoryName(path)!)
            && (
                String.Equals(name, "global.json", StringComparison.OrdinalIgnoreCase)
                || String.Equals(name, ".editorconfig", StringComparison.OrdinalIgnoreCase)
                || String.Equals(name, "Directory.Build.props", StringComparison.OrdinalIgnoreCase)
                || String.Equals(
                    name,
                    "Directory.Packages.props",
                    StringComparison.OrdinalIgnoreCase
                )
                || String.Equals(
                    name,
                    "Directory.Build.targets",
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
            return true;
        if (!projectDirectories.Any(directory => IsWithin(path, directory)))
            return false;
        return String.Equals(name, ".editorconfig", StringComparison.OrdinalIgnoreCase)
            || String.Equals(name, "global.json", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".winmd", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWithin(string path, string directory) =>
        String.Equals(path, directory, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(
            directory + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase
        );

    private static IEnumerable<string> ProjectDirectories(Project project) =>
        ProjectGraph(project)
            .Where(project => project.FilePath is not null)
            .Select(project => Path.GetDirectoryName(Path.GetFullPath(project.FilePath!))!);

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
                    reference =>
                        current.Solution.GetProject(reference.ProjectId)?.FilePath
                        ?? current.Solution.GetProject(reference.ProjectId)?.Name
                        ?? "",
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

    private bool IsProjectAncestor(string directory)
    {
        foreach (var root in projectDirectories)
        {
            for (var current = root; current is not null; current = Path.GetDirectoryName(current))
            {
                if (String.Equals(current, directory, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private Task<Snapshot> SnapshotAsync(Uri uri, CancellationToken cancellationToken) =>
        SnapshotAsync(null, uri, cancellationToken);

    private async Task<Snapshot> SnapshotAsync(
        ProjectId? exactProjectId,
        Uri uri,
        CancellationToken cancellationToken
    )
    {
        Project project;
        long captured;
        lock (gate)
        {
            ThrowIfDisposed();
            if (reloadFailed)
                throw new InvalidOperationException("The evaluated project reload failed.");
            var candidate = exactProjectId is null
                ? ProjectGraph(Project())
                    .FirstOrDefault(project =>
                        project.AdditionalDocuments.Any(document =>
                            SameFile(document.FilePath, uri)
                        )
                    )
                : solution.GetProject(exactProjectId);
            if (
                candidate is null
                || !candidate.AdditionalDocuments.Any(document => SameFile(document.FilePath, uri))
            )
                throw new ArgumentException("An evaluated .lui document is required.", nameof(uri));
            project = candidate;
            captured = epoch;
        }
        var current = FindDocument(project, uri);
        var documents = new List<LuiProjectDocument>();
        LuiProjectDocument? input = null;
        LuiDiagnostic? metadataDiagnostic = null;
        foreach (
            var document in project.AdditionalDocuments.Where(item =>
                item.FilePath!.EndsWith(".lui", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var source = text.ToString();
            var logicalPath = LogicalPath(project, document);
            var validLogicalPath = LuiDocumentIdentity.TryCreate(
                logicalPath,
                out var logicalIdentity
            );
            if (
                !validLogicalPath
                && String.Equals(
                    document.FilePath,
                    current.FilePath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                metadataDiagnostic = LuiDiagnosticProjection.InvalidLogicalPath(
                    document.FilePath!,
                    logicalPath
                );
            var projectDocument = new LuiProjectDocument(
                document.FilePath!,
                logicalIdentity?.LogicalPath ?? Path.GetFileName(document.FilePath!),
                source,
                DocumentVersion(project, document, source)
            );
            if (validLogicalPath)
                documents.Add(projectDocument);
            if (
                String.Equals(
                    document.FilePath,
                    current.FilePath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                input = projectDocument;
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
        if (input is null)
            throw new InvalidOperationException("The evaluated project has no current document.");
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
            index,
            metadataDiagnostic,
            new LuiFreshnessTarget(project.Id, uri, identity)
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

    private static IEnumerable<TextDocument> FindTextDocuments(Project project, Uri uri) =>
        !uri.IsFile
            ? []
            : ProjectGraph(project)
                .SelectMany(graphProject =>
                    graphProject
                        .Documents.Cast<TextDocument>()
                        .Concat(graphProject.AdditionalDocuments)
                )
                .Where(document => SameFile(document.FilePath, uri));

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
            if (node.SpanStart < entry.Generated.Start || node.Span.End > entry.Generated.End)
                break;
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

    private async Task<SemanticDocument?> SemanticAsync(
        Uri uri,
        int offset,
        CancellationToken cancellationToken
    )
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (generated.TryGetValue(uri, out var cached))
                return GeneratedSemantic(cached.Document, offset);
        }
        if (!Owns(uri))
            return null;
        var snapshot = await SnapshotAsync(uri, cancellationToken).ConfigureAwait(false);
        if (snapshot.MetadataDiagnostic is not null)
            return null;
        var result = LuiCompiler.Compile(
            snapshot.Document.Syntax,
            snapshot.Compilation,
            snapshot.Identity
        );
        Track(result, snapshot);
        if (
            result.ProjectionSource is null
            || !await IsCurrentAsync(result, cancellationToken).ConfigureAwait(false)
        )
            return null;
        var document = new PublishedDocument(
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
            snapshot.Compilation,
            snapshot.Epoch,
            snapshot.Document.Syntax,
            snapshot.Freshness
        );
        if (result.Success)
        {
            lock (gate)
            {
                if (disposed || epoch != snapshot.Epoch)
                    return null;
                generated[document.GeneratedUri] = new GeneratedDocument(document);
            }
        }
        var entry =
            document
                .Result.Map.FromSource(new LuiSpan(offset, 0))
                .Where(item => !item.Hidden && item.Generated.Length != 0)
                .OrderBy(item => item.Kind == LuiMapKind.Symbol ? 0 : 1)
                .ThenBy(item => item.Generated.Length)
                .FirstOrDefault()
            ?? document
                .Result.Map.Entries.Where(item =>
                    !item.Hidden
                    && item.Kind == LuiMapKind.Expression
                    && item.Generated.Length != 0
                    && item.Source.End == offset
                )
                .OrderBy(item => item.Generated.Length)
                .FirstOrDefault();
        var options =
            document
                .Compilation.SyntaxTrees.Select(tree => tree.Options)
                .OfType<CSharpParseOptions>()
                .FirstOrDefault()
            ?? CSharpParseOptions.Default;
        var tree = CSharpSyntaxTree.ParseText(
            document.GeneratedText,
            options,
            document.GeneratedUri.AbsoluteUri,
            cancellationToken: CancellationToken.None
        );
        var compilation = ProjectionCompilation(document.Compilation, tree);
        var position =
            entry is null ? -1
            : entry.Kind == LuiMapKind.Expression && offset == entry.Source.End
                ? entry.Generated.End
            : entry.Kind is LuiMapKind.Symbol or LuiMapKind.Local
            && entry.Generated.Length > entry.Source.Length
                ? entry.Generated.End - 1
            : entry.Generated.Start
                + Math.Min(
                    Math.Max(0, offset - entry.Source.Start),
                    Math.Max(0, entry.Generated.Length - 1)
                );
        return new SemanticDocument(
            document,
            tree,
            compilation.GetSemanticModel(tree),
            position,
            entry
        );
    }

    private static SemanticDocument GeneratedSemantic(PublishedDocument document, int offset)
    {
        var options =
            document
                .Compilation.SyntaxTrees.Select(tree => tree.Options)
                .OfType<CSharpParseOptions>()
                .FirstOrDefault()
            ?? CSharpParseOptions.Default;
        var tree = CSharpSyntaxTree.ParseText(
            document.GeneratedText,
            options,
            document.GeneratedUri.AbsoluteUri,
            cancellationToken: CancellationToken.None
        );
        var compilation = ProjectionCompilation(document.Compilation, tree);
        return new SemanticDocument(
            document,
            tree,
            compilation.GetSemanticModel(tree),
            Math.Clamp(offset, 0, Math.Max(0, document.GeneratedText.Length - 1)),
            null
        );
    }

    private static LuiEditorDiagnostic? EditorDiagnostic(
        Compilation compilation,
        LuiDiagnostic diagnostic
    )
    {
        var report = compilation.Options.SpecificDiagnosticOptions.TryGetValue(
            diagnostic.Id,
            out var configured
        )
            ? configured
            : compilation.Options.GeneralDiagnosticOption;
        if (report == ReportDiagnostic.Suppress)
            return null;
        var severity = report switch
        {
            ReportDiagnostic.Error => DiagnosticSeverity.Error,
            ReportDiagnostic.Warn => DiagnosticSeverity.Warning,
            ReportDiagnostic.Info => DiagnosticSeverity.Info,
            ReportDiagnostic.Hidden => DiagnosticSeverity.Hidden,
            _ => diagnostic.Severity,
        };
        return new LuiEditorDiagnostic(
            diagnostic.Id,
            diagnostic.Message,
            diagnostic.Span,
            severity switch
            {
                DiagnosticSeverity.Error => 1,
                DiagnosticSeverity.Warning => 2,
                DiagnosticSeverity.Info => 3,
                _ => 4,
            },
            diagnostic.Source
        );
    }

    private static IEnumerable<LuiDocumentSymbol> StructureSymbols(LuiBodySyntax node) =>
        node switch
        {
            LuiElementSyntax element =>
            [
                new LuiDocumentSymbol(
                    element.Name.Text,
                    8,
                    element.Span,
                    element.Name.Span,
                    element
                        .Attributes.Select(attribute => new LuiDocumentSymbol(
                            attribute.Name.Text,
                            7,
                            attribute.Span,
                            attribute.Name.Span,
                            attribute.Value is LuiStyleWithSyntax style
                                ? style.Members.SelectMany(StyleSymbols).ToArray()
                                : []
                        ))
                        .Concat(element.Children.SelectMany(StructureSymbols))
                        .ToArray()
                ),
            ],
            LuiIfSyntax conditional =>
            [
                new LuiDocumentSymbol(
                    "if",
                    6,
                    conditional.Span,
                    conditional.IfKeyword.Span,
                    conditional
                        .ThenBody.Concat(conditional.ElseBody)
                        .SelectMany(StructureSymbols)
                        .ToArray()
                ),
            ],
            LuiForEachSyntax loop =>
            [
                new LuiDocumentSymbol(
                    "foreach " + loop.Variable.Text,
                    6,
                    loop.Span,
                    loop.Variable.Span,
                    loop.Body.SelectMany(StructureSymbols).ToArray()
                ),
            ],
            _ => [],
        };

    private static IEnumerable<LuiDocumentSymbol> StyleSymbols(LuiStyleMemberSyntax member) =>
        member switch
        {
            LuiStyleAssignmentSyntax assignment =>
            [
                new LuiDocumentSymbol(
                    assignment.Property.Text,
                    7,
                    assignment.Span,
                    assignment.Property.Span,
                    []
                ),
            ],
            LuiVariantGroupSyntax variant =>
            [
                new LuiDocumentSymbol(
                    "when " + variant.Condition.Text,
                    6,
                    variant.Span,
                    variant.Condition.Span,
                    variant.Assignments.SelectMany(StyleSymbols).ToArray()
                ),
            ],
            _ => [],
        };

    private static IEnumerable<ISymbol> ComponentSymbols(
        SemanticDocument semantic,
        string? name = null
    )
    {
        return semantic
            .Model.LookupSymbols(semantic.Position)
            .OfType<IMethodSymbol>()
            .Where(method =>
                name is null || String.Equals(method.Name, name, StringComparison.Ordinal)
            )
            .Where(IsComponent)
            .Where(symbol => IsAccessible(semantic, symbol))
            .Cast<ISymbol>();
    }

    private static ClassificationWorkspace CreateClassificationWorkspace(
        Compilation compilation,
        SyntaxTree tree,
        string generatedText
    )
    {
        var host = MefHostServices.Create(
            MefHostServices
                .DefaultAssemblies.Concat([Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features")])
                .Distinct()
        );
        var workspace = new AdhocWorkspace(host);
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var solution = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "LucentLuiClassification",
                "LucentLuiClassification",
                LanguageNames.CSharp,
                compilationOptions: compilation.Options,
                parseOptions: tree.Options,
                metadataReferences: compilation.References
            )
        );
        foreach (var source in compilation.SyntaxTrees)
        {
            solution = solution.AddDocument(
                DocumentId.CreateNewId(projectId),
                Path.GetFileName(source.FilePath),
                source.GetText(),
                filePath: source.FilePath
            );
        }
        solution = solution
            .AddDocument(
                documentId,
                "generated.lui.cs",
                SourceText.From(generatedText),
                filePath: tree.FilePath
            )
            .AddDocument(
                DocumentId.CreateNewId(projectId),
                "LucentLuiStaticUsings.cs",
                SourceText.From("global using static global::Lucent.Core.Components;")
            );
        workspace.TryApplyChanges(solution);
        return new ClassificationWorkspace(
            workspace,
            workspace.CurrentSolution.GetDocument(documentId)!
        );
    }

    private static Compilation ProjectionCompilation(Compilation compilation, SyntaxTree tree) =>
        compilation.AddSyntaxTrees(
            tree,
            CSharpSyntaxTree.ParseText(
                "global using static global::Lucent.Core.Components;",
                (CSharpParseOptions)tree.Options
            )
        );

    private static IEnumerable<ISymbol> ComponentParameters(
        SemanticDocument semantic,
        LuiElementSyntax element
    ) =>
        ComponentSymbols(semantic, element.Name.Text)
            .OfType<IMethodSymbol>()
            .SelectMany(method => method.Parameters);

    private static IEnumerable<ISymbol> StyleProperties(SemanticDocument semantic) =>
        stylePropertyTypes
            .Select(semantic.Model.Compilation.GetTypeByMetadataName)
            .Where(type => type is not null)
            .SelectMany(type => type!.GetMembers())
            .Concat(semantic.Model.LookupSymbols(semantic.Position))
            .Where(symbol =>
                symbol.IsStatic
                && symbol switch
                {
                    IFieldSymbol field => IsStyleProperty(field.Type),
                    IPropertySymbol property => IsStyleProperty(property.Type),
                    _ => false,
                }
            )
            .Where(symbol => IsAccessible(semantic, symbol));

    private static IEnumerable<ISymbol> RootTokens(SemanticDocument semantic)
    {
        var root = semantic.Document.Result.Identity.RootNamespace;
        var tokens = String.IsNullOrWhiteSpace(root)
            ? null
            : semantic.Model.Compilation.GetTypeByMetadataName(root + ".Tokens");
        return tokens is { IsStatic: true }
            ? tokens
                .GetMembers()
                .Where(symbol =>
                    symbol.IsStatic
                    && symbol switch
                    {
                        IFieldSymbol field => IsToken(field.Type),
                        IPropertySymbol property => IsToken(property.Type),
                        _ => false,
                    }
                )
                .Where(symbol => IsAccessible(semantic, symbol))
            : [];
    }

    private static IEnumerable<ISymbol> VariantStates(Compilation compilation)
    {
        var states = compilation.GetTypeByMetadataName("Lucent.Core.VariantState");
        return states is null
            ? []
            : states
                .GetMembers()
                .OfType<IFieldSymbol>()
                .Where(field => field.HasConstantValue && field.Name != "None");
    }

    private static INamespaceSymbol DirectiveNamespace(
        SemanticDocument semantic,
        LuiSyntaxNode directive,
        int offset
    )
    {
        var start = directive switch
        {
            LuiNamespaceSyntax item => item.Keyword.Span.End,
            LuiUsingSyntax item => item.Keyword.Span.End,
            _ => directive.Span.Start,
        };
        var qualifier = semantic
            .Document.SourceText.ToString(new TextSpan(start, Math.Max(0, offset - start)))
            .Trim()
            .TrimEnd(';')
            .TrimEnd('.');
        if (directive is LuiUsingSyntax)
        {
            if (qualifier.Contains('='))
                qualifier = qualifier[(qualifier.LastIndexOf('=') + 1)..].Trim();
            if (qualifier.StartsWith("static ", StringComparison.Ordinal))
                qualifier = qualifier["static ".Length..].TrimStart();
        }
        if (qualifier.StartsWith("global::", StringComparison.Ordinal))
            qualifier = qualifier["global::".Length..];
        var current = semantic.Model.Compilation.GlobalNamespace;
        foreach (var part in qualifier.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = current
                .GetNamespaceMembers()
                .FirstOrDefault(member =>
                    String.Equals(member.Name, part, StringComparison.Ordinal)
                );
            if (next is null)
                break;
            current = next;
        }
        return current;
    }

    private static IEnumerable<ISymbol> NamespaceMembers(INamespaceSymbol @namespace) =>
        @namespace.GetNamespaceMembers().Cast<ISymbol>();

    private static bool IsStyleProperty(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "Property", Arity: 1, ContainingNamespace: { } @namespace }
        && @namespace.ToDisplayString() == "Lucent.Core";

    private static bool IsToken(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "Token", Arity: 1, ContainingNamespace: { } @namespace }
        && @namespace.ToDisplayString() == "Lucent.Core";

    private static LuiStyleAssignmentSyntax? StyleAssignmentAt(
        LuiDocumentSyntax syntax,
        int offset
    ) => StyleAssignments(syntax).FirstOrDefault(assignment => Contains(assignment.Span, offset));

    private static LuiVariantGroupSyntax? VariantAt(LuiDocumentSyntax syntax, int offset) =>
        StyleMembers(syntax)
            .OfType<LuiVariantGroupSyntax>()
            .FirstOrDefault(variant => Contains(variant.Span, offset));

    private static bool StyleReferenceAt(LuiDocumentSyntax syntax, int offset) =>
        Elements(syntax)
            .SelectMany(element => element.Attributes)
            .Any(attribute =>
                (attribute.Value is LuiStyleWithSyntax style && Contains(style.Name.Span, offset))
            );

    private static IEnumerable<LuiStyleMemberSyntax> StyleMembers(LuiDocumentSyntax syntax) =>
        syntax
            .Styles.SelectMany(style => style.Members)
            .Concat(
                Elements(syntax)
                    .SelectMany(element => element.Attributes)
                    .Select(attribute => attribute.Value)
                    .OfType<LuiStyleWithSyntax>()
                    .SelectMany(style => style.Members)
            );

    private static IEnumerable<LuiSemanticSpan> SyntaxSemanticSpans(LuiDocumentSyntax syntax) =>
        StyleAssignments(syntax)
            .Select(assignment => new LuiSemanticSpan(assignment.Property.Span, "property", 0))
            .Concat(
                Elements(syntax)
                    .SelectMany(element => element.Attributes)
                    .Select(attribute => attribute.Value)
                    .OfType<LuiStyleWithSyntax>()
                    .Select(style => new LuiSemanticSpan(style.WithKeyword.Span, "keyword", 0))
            )
            .Concat(
                Loops(syntax.Component?.Body ?? [])
                    .SelectMany(loop => new[] { loop.VarKeyword, loop.InKeyword })
                    .Select(token => new LuiSemanticSpan(token.Span, "keyword", 0))
            );

    private static IEnumerable<LuiSemanticSpan> ProjectClassifications(
        LuiSourceMap map,
        IEnumerable<ClassifiedSpan> classified
    )
    {
        foreach (var item in classified)
        {
            var type = SemanticTokenType(item.ClassificationType);
            if (type is null)
                continue;
            var generated = new LuiSpan(item.TextSpan.Start, item.TextSpan.Length);
            foreach (
                var entry in map.FromGenerated(generated)
                    .Where(entry => !entry.Hidden && entry.Generated.Length != 0)
            )
            {
                var start = Math.Max(generated.Start, entry.Generated.Start);
                var end = Math.Min(generated.End, entry.Generated.End);
                if (start >= end)
                    continue;
                yield return new LuiSemanticSpan(
                    new LuiSpan(entry.Source.Start + start - entry.Generated.Start, end - start),
                    type,
                    1
                );
            }
        }
    }

    private static string? SemanticTokenType(string classification) =>
        classification switch
        {
            "keyword" => "keyword",
            "class name"
            or "struct name"
            or "interface name"
            or "enum name"
            or "delegate name"
            or "type parameter name" => "type",
            "property name" => "property",
            "enum member name" => "enumMember",
            _ => null,
        };

    private static IEnumerable<LuiForEachSyntax> Loops(IEnumerable<LuiBodySyntax> body) =>
        body.SelectMany(node =>
            node switch
            {
                LuiForEachSyntax loop => new[] { loop }.Concat(Loops(loop.Body)),
                LuiIfSyntax conditional => Loops(conditional.ThenBody.Concat(conditional.ElseBody)),
                LuiElementSyntax element => Loops(element.Children),
                _ => [],
            }
        );

    private static bool Overlaps(LuiSpan left, LuiSpan right) =>
        left.Start < right.End && right.Start < left.End;

    private static int[] EncodeSemanticTokens(SourceText text, IEnumerable<LuiSemanticSpan> spans)
    {
        var data = new List<int>();
        var previousLine = 0;
        var previousCharacter = 0;
        foreach (var span in spans)
        {
            var line = text.Lines.GetLineFromPosition(span.Span.Start);
            var character = span.Span.Start - line.Start;
            var type = Array.IndexOf(SemanticTokenTypes, span.Type);
            if (type < 0 || span.Span.End > line.End)
                continue;
            data.Add(line.LineNumber - previousLine);
            data.Add(line.LineNumber == previousLine ? character - previousCharacter : character);
            data.Add(span.Span.Length);
            data.Add(type);
            data.Add(0);
            previousLine = line.LineNumber;
            previousCharacter = character;
        }
        return data.ToArray();
    }

    private static IEnumerable<LuiStyleAssignmentSyntax> StyleAssignments(
        LuiDocumentSyntax syntax
    ) =>
        StyleMembers(syntax)
            .SelectMany(member =>
                member is LuiStyleAssignmentSyntax assignment
                    ? [assignment]
                    : ((LuiVariantGroupSyntax)member).Assignments
            );

    private static bool IsComponent(IMethodSymbol method) =>
        method.IsStatic
        && method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            == "global::Lucent.Core.ComponentRecipe"
        && method
            .GetAttributes()
            .Any(attribute =>
                attribute.AttributeClass?.ToDisplayString()
                == "Lucent.Core.LucentComponentAttribute"
            );

    private static bool IsAccessible(SemanticDocument semantic, ISymbol symbol) =>
        semantic.Model.Compilation.IsSymbolAccessibleWithin(
            symbol,
            semantic.Model.Compilation.Assembly
        );

    private static bool IsDefaultContent(IParameterSymbol parameter) =>
        parameter
            .GetAttributes()
            .Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == "Lucent.Core.DefaultContentAttribute"
            );

    private static Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax? InvocationAt(
        SemanticDocument semantic
    )
    {
        if (semantic.Position < 0)
            return null;
        var root = semantic.Tree.GetRoot();
        return root.FindToken(Math.Clamp(semantic.Position, 0, Math.Max(0, root.FullSpan.End - 1)))
            .Parent?.AncestorsAndSelf()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .FirstOrDefault();
    }

    private static bool Contains(LuiSpan span, int offset) =>
        span.Start <= offset && offset <= span.End;

    private static LuiElementSyntax? ElementAt(LuiDocumentSyntax syntax, int offset) =>
        Elements(syntax)
            .Where(element => Contains(element.Span, offset))
            .OrderBy(element => element.Span.Length)
            .FirstOrDefault();

    private static IEnumerable<LuiElementSyntax> Elements(LuiDocumentSyntax syntax) =>
        syntax.Component?.Body.SelectMany(Elements) ?? [];

    private static IEnumerable<LuiElementSyntax> Elements(LuiBodySyntax node) =>
        node switch
        {
            LuiElementSyntax element => [element, .. element.Children.SelectMany(Elements)],
            LuiIfSyntax conditional => conditional
                .ThenBody.Concat(conditional.ElseBody)
                .SelectMany(Elements),
            LuiForEachSyntax loop => loop.Body.SelectMany(Elements),
            _ => [],
        };

    private static int CompletionKind(ISymbol symbol) =>
        symbol switch
        {
            IMethodSymbol => 2,
            IPropertySymbol => 10,
            IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } => 20,
            IFieldSymbol => 5,
            INamedTypeSymbol { TypeKind: TypeKind.Struct } => 22,
            INamedTypeSymbol => 7,
            IParameterSymbol or ILocalSymbol => 6,
            INamespaceSymbol => 9,
            _ => 1,
        };

    private static int CompletionKind(IEnumerable<string> tags) =>
        tags.Contains("Method") ? 2
        : tags.Contains("Property") ? 10
        : tags.Contains("EnumMember") ? 20
        : tags.Contains("Field") ? 5
        : tags.Contains("Local") || tags.Contains("Parameter") ? 6
        : tags.Contains("Namespace") ? 9
        : tags.Contains("Structure") ? 22
        : tags.Contains("Class") ? 7
        : 1;

    private static string SymbolText(ISymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    private static string? Documentation(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml(cancellationToken: CancellationToken.None);
        if (String.IsNullOrWhiteSpace(xml))
            return null;
        try
        {
            return String.Join(
                "\n\n",
                XDocument
                    .Parse(xml)
                    .Root!.Elements()
                    .Select(section =>
                    {
                        var text = DocumentationText(section.Nodes()).Trim();
                        return section.Name.LocalName == "summary"
                            ? text
                            : Char.ToUpperInvariant(section.Name.LocalName[0])
                                + section.Name.LocalName[1..]
                                + ":\n"
                                + text;
                    })
                    .Where(text => text.Length != 0)
            );
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static string DocumentationText(IEnumerable<XNode> nodes) =>
        String.Concat(
            nodes.Select(node =>
                node switch
                {
                    XText text => text.Value,
                    XElement { Name.LocalName: "see" } element => DocumentationReference(element),
                    XElement { Name.LocalName: "paramref" } element => element
                        .Attribute("name")
                        ?.Value
                        ?? "",
                    XElement { Name.LocalName: "para" } element => "\n\n"
                        + DocumentationText(element.Nodes()),
                    XElement element => DocumentationText(element.Nodes()),
                    _ => "",
                }
            )
        );

    private static string DocumentationReference(XElement element) =>
        (element.Attribute("cref")?.Value ?? element.Value)
            .Replace("T:", "", StringComparison.Ordinal)
            .Replace("M:", "", StringComparison.Ordinal)
            .Replace("P:", "", StringComparison.Ordinal)
            .Replace('#', '.');

    private static ISymbol? SymbolAt(SemanticDocument semantic)
    {
        if (
            semantic.Entry
                is not {
                    Kind: LuiMapKind.Symbol or LuiMapKind.Local or LuiMapKind.Expression
                } entry
            || semantic.Position < 0
        )
            return null;
        var root = semantic.Tree.GetRoot();
        var token = root.FindToken(
            Math.Clamp(semantic.Position, 0, Math.Max(0, root.FullSpan.End - 1))
        );
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            var declared = semantic.Model.GetDeclaredSymbol(node);
            if (declared is not null && IsDeclarationIdentifier(node, token))
                return declared;
            if (node.SpanStart < entry.Generated.Start || node.Span.End > entry.Generated.End)
                break;
            if (declared is not null)
                return declared;
            var info = semantic.Model.GetSymbolInfo(node, CancellationToken.None);
            if (info.Symbol is not null)
                return info.Symbol;
            if (info.CandidateSymbols.Length != 0)
                return info.CandidateSymbols[0];
        }
        return null;
    }

    private static LuiNavigationTarget? DeclarationTarget(
        PublishedDocument document,
        ISymbol symbol
    )
    {
        if (symbol is IAliasSymbol alias)
            symbol = alias.Target;
        if (symbol is IMethodSymbol method)
        {
            var identity =
                method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                + "."
                + method.MetadataName;
            var declaration = document.Index.Declarations.SingleOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Identity, identity)
            );
            if (declaration is not null)
                return new LuiNavigationTarget(
                    new Uri(declaration.Document.Path),
                    declaration.Document.Syntax.Component!.Name.Span,
                    declaration.Document.Source
                );
        }
        var location = symbol.Locations.FirstOrDefault(candidate => candidate.IsInSource);
        if (location is null)
            return null;
        if (location.SourceTree?.FilePath == document.GeneratedUri.AbsoluteUri)
        {
            var mapped = document
                .Result.Map.FromGenerated(
                    new LuiSpan(location.SourceSpan.Start, location.SourceSpan.Length)
                )
                .Where(item => !item.Hidden)
                .OrderBy(item => item.Source.Length)
                .FirstOrDefault();
            return mapped is null
                ? null
                : new LuiNavigationTarget(
                    document.SourceUri,
                    mapped.Source,
                    document.SourceText.ToString()
                );
        }
        if (String.IsNullOrWhiteSpace(location.SourceTree?.FilePath))
            return null;
        var path = location.SourceTree.FilePath;
        return new LuiNavigationTarget(
            new Uri(path),
            new LuiSpan(location.SourceSpan.Start, location.SourceSpan.Length),
            location.SourceTree.GetText().ToString()
        );
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
        LuiProjectComponentIndex index,
        LuiDiagnostic? metadataDiagnostic,
        LuiFreshnessTarget freshness
    )
    {
        internal long Epoch { get; } = epoch;
        internal Compilation Compilation { get; } = compilation;
        internal LuiProjectDocument Document { get; } = document;
        internal LuiFreshnessIdentity Identity { get; } = identity;
        internal SourceText Text { get; } = text;
        internal Uri Uri { get; } = uri;
        internal LuiProjectComponentIndex Index { get; } = index;
        internal LuiDiagnostic? MetadataDiagnostic { get; } = metadataDiagnostic;
        internal LuiFreshnessTarget Freshness { get; } = freshness;
    }

    internal sealed class PublishedDocument(
        LuiCompilationResult result,
        Uri generatedUri,
        SourceText sourceText,
        Uri sourceUri,
        LuiProjectComponentIndex index,
        Compilation compilation,
        long epoch,
        LuiDocumentSyntax syntax,
        LuiFreshnessTarget freshness
    )
    {
        internal LuiCompilationResult Result { get; } = result;
        internal Uri GeneratedUri { get; } = generatedUri;
        internal string GeneratedText { get; } = result.ProjectionSource!;
        internal SourceText SourceText { get; } = sourceText;
        internal Uri SourceUri { get; } = sourceUri;
        internal LuiProjectComponentIndex Index { get; } = index;
        internal Compilation Compilation { get; } = compilation;
        internal long Epoch { get; } = epoch;
        internal LuiDocumentSyntax Syntax { get; } = syntax;
        internal LuiFreshnessTarget Freshness { get; } = freshness;
    }

    private sealed class GeneratedDocument
    {
        internal GeneratedDocument(PublishedDocument document)
        {
            Document = document;
            Source = document.GeneratedText;
        }

        internal PublishedDocument Document { get; }
        internal string Source { get; }

        internal LuiNavigationTarget? ToSource(int offset)
        {
            var entry = Document
                .Result.Map.FromGenerated(new LuiSpan(offset, 0))
                .Where(item => !item.Hidden)
                .OrderBy(item => item.Generated.Length)
                .FirstOrDefault();
            return entry is null
                ? null
                : new LuiNavigationTarget(
                    Document.SourceUri,
                    entry.Source,
                    Document.SourceText.ToString()
                );
        }
    }

    private sealed class ClassificationWorkspace(AdhocWorkspace workspace, Document document)
        : IDisposable
    {
        internal Document Document { get; } = document;

        public void Dispose() => workspace.Dispose();
    }

    private sealed class SemanticDocument(
        PublishedDocument document,
        SyntaxTree tree,
        SemanticModel model,
        int position,
        LuiMapEntry? entry
    )
    {
        internal PublishedDocument Document { get; } = document;
        internal SyntaxTree Tree { get; } = tree;
        internal SemanticModel Model { get; } = model;
        internal int Position { get; } = position;
        internal LuiMapEntry? Entry { get; } = entry;
    }

    private sealed class SnapshotEpoch(long value, LuiFreshnessTarget freshness)
    {
        internal long Value { get; } = value;
        internal LuiFreshnessTarget Freshness { get; } = freshness;
    }
}

internal sealed class LuiNavigationTarget(Uri uri, LuiSpan span, string text)
{
    internal Uri Uri { get; } = uri;
    internal LuiSpan Span { get; } = span;
    internal string Text { get; } = text;
}

internal sealed class LuiEditorDiagnostic(
    string code,
    string message,
    LuiSpan span,
    int severity,
    string source
)
{
    internal string Code { get; } = code;
    internal string Message { get; } = message;
    internal LuiSpan Span { get; } = span;
    internal int Severity { get; } = severity;
    internal string Source { get; } = source;
}

internal sealed class LuiRenameResult(
    Uri uri,
    LuiSpan span,
    IReadOnlyList<LuiRenameDocumentEdit> edits
)
{
    internal Uri Uri { get; } = uri;
    internal LuiSpan Span { get; } = span;
    internal IReadOnlyList<LuiRenameDocumentEdit> Edits { get; } = edits;
}

internal sealed class LuiRenameDocumentEdit(Uri uri, IReadOnlyList<LuiSpan> spans, string newText)
{
    internal Uri Uri { get; } = uri;
    internal IReadOnlyList<LuiSpan> Spans { get; } = spans;
    internal string NewText { get; } = newText;
}

internal sealed class LuiReferenceResult(IReadOnlyList<LuiReferenceLocation> locations)
{
    internal IReadOnlyList<LuiReferenceLocation> Locations { get; } = locations;
}

internal sealed class LuiReferenceLocation(Uri uri, LuiSpan span)
{
    internal Uri Uri { get; } = uri;
    internal LuiSpan Span { get; } = span;
}

internal sealed class LuiFormatResult(LuiSpan span, string newText)
{
    internal LuiSpan Span { get; } = span;
    internal string NewText { get; } = newText;
}

internal sealed class RenameGeneratedDocument(
    ProjectId projectId,
    Uri uri,
    string source,
    LuiDocumentSyntax syntax,
    LuiSourceMap map,
    LuiFreshnessIdentity identity
)
{
    internal ProjectId ProjectId { get; } = projectId;
    internal Uri Uri { get; } = uri;
    internal string Source { get; } = source;
    internal LuiDocumentSyntax Syntax { get; } = syntax;
    internal LuiSourceMap Map { get; } = map;
    internal LuiFreshnessIdentity Identity { get; } = identity;
    internal LuiFreshnessTarget Freshness { get; } = new(projectId, uri, identity);
}

internal sealed class RenameSnapshot(
    long epoch,
    Solution solution,
    ProjectId projectId,
    IReadOnlyDictionary<SyntaxTree, RenameGeneratedDocument> generated,
    IReadOnlyDictionary<SyntaxTree, Uri> csharp,
    IReadOnlyDictionary<string, string> sourceByPath,
    IReadOnlyDictionary<SyntaxTree, ProjectId> trees,
    IReadOnlyList<LuiFreshnessTarget>? generatedFreshness = null
)
{
    internal long Epoch { get; } = epoch;
    internal Solution Solution { get; } = solution;
    internal ProjectId ProjectId { get; } = projectId;
    internal IReadOnlyDictionary<SyntaxTree, RenameGeneratedDocument> Generated { get; } =
        generated;
    internal IReadOnlyList<LuiFreshnessTarget> GeneratedFreshness { get; } =
        generatedFreshness ?? generated.Values.Select(document => document.Freshness).ToArray();
    internal IReadOnlyDictionary<SyntaxTree, Uri> CSharp { get; } = csharp;
    internal IReadOnlyDictionary<string, string> SourceByPath { get; } = sourceByPath;
    internal IReadOnlyDictionary<SyntaxTree, ProjectId> Trees { get; } = trees;
}

internal sealed class RenameTarget(
    Uri uri,
    LuiSpan span,
    IReadOnlyList<ISymbol> symbols,
    LuiLocalProvenance? localDeclaration
)
{
    internal Uri Uri { get; } = uri;
    internal LuiSpan Span { get; } = span;
    internal IReadOnlyList<ISymbol> Symbols { get; } = symbols;
    internal ISymbol Symbol => Symbols[0];
    internal LuiLocalProvenance? LocalDeclaration { get; } = localDeclaration;
}

internal readonly record struct LuiLocalProvenance(Uri Uri, LuiSpan Span);

internal readonly record struct LuiFreshnessTarget(
    ProjectId ProjectId,
    Uri Uri,
    LuiFreshnessIdentity Identity
);

internal sealed class RenameOccurrence(
    Uri uri,
    LuiSpan span,
    SyntaxToken token,
    SemanticModel model,
    ISymbol symbol
)
{
    internal Uri Uri { get; } = uri;
    internal LuiSpan Span { get; } = span;
    internal SyntaxToken Token { get; } = token;
    internal SemanticModel Model { get; } = model;
    internal ISymbol Symbol { get; } = symbol;
}

internal sealed class LuiCompletionItem(
    string label,
    int kind,
    string detail,
    string? documentation
)
{
    internal string Label { get; } = label;
    internal int Kind { get; } = kind;
    internal string Detail { get; } = detail;
    internal string? Documentation { get; } = documentation;
}

internal sealed class LuiHover(string value, string? documentation)
{
    internal string Value { get; } = value;
    internal string? Documentation { get; } = documentation;
}

internal sealed class LuiSignatureHelp(
    IReadOnlyList<LuiSignature> signatures,
    int activeSignature,
    int activeParameter
)
{
    internal IReadOnlyList<LuiSignature> Signatures { get; } = signatures;
    internal int ActiveSignature { get; } = activeSignature;
    internal int ActiveParameter { get; } = activeParameter;
}

internal sealed class LuiSignature(
    string label,
    IReadOnlyList<string> parameters,
    string? documentation
)
{
    internal string Label { get; } = label;
    internal IReadOnlyList<string> Parameters { get; } = parameters;
    internal string? Documentation { get; } = documentation;
}

internal sealed class LuiDocumentSymbol(
    string name,
    int kind,
    LuiSpan span,
    LuiSpan selectionSpan,
    IReadOnlyList<LuiDocumentSymbol> children
)
{
    internal string Name { get; } = name;
    internal int Kind { get; } = kind;
    internal LuiSpan Span { get; } = span;
    internal LuiSpan SelectionSpan { get; } = selectionSpan;
    internal IReadOnlyList<LuiDocumentSymbol> Children { get; } = children;
}

internal sealed class LuiSemanticSpan(LuiSpan span, string type, int priority)
{
    internal LuiSpan Span { get; } = span;
    internal string Type { get; } = type;
    internal int Priority { get; } = priority;
}
