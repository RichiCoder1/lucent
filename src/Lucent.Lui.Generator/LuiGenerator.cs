using System;
using System.Collections.Immutable;
using System.Linq;
using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Lucent.Lui.Generator;

[Generator]
public sealed class LuiGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor InvalidInput = new DiagnosticDescriptor(
        "LUI4001",
        "Unreadable .lui input",
        "LUI input '{0}' is unreadable",
        "Lucent.Lui",
        DiagnosticSeverity.Error,
        true
    );
    private static readonly DiagnosticDescriptor DuplicateInput = new DiagnosticDescriptor(
        "LUI4002",
        "Duplicate .lui input",
        "LUI input '{0}' has duplicate logical path '{1}'",
        "Lucent.Lui",
        DiagnosticSeverity.Error,
        true
    );
    private static readonly DiagnosticDescriptor InvalidLogicalPath = new DiagnosticDescriptor(
        "LUI4003",
        "Invalid .lui logical path",
        "LUI input '{0}' has invalid logical path '{1}'",
        "Lucent.Lui",
        DiagnosticSeverity.Error,
        true
    );
    private static readonly DiagnosticDescriptor DuplicateComponent = new DiagnosticDescriptor(
        "LUI4004",
        "Duplicate .lui component",
        "LUI component '{0}' is declared more than once",
        "Lucent.Lui",
        DiagnosticSeverity.Error,
        true
    );
    private static readonly DiagnosticDescriptor InvalidSibling = new DiagnosticDescriptor(
        "LUI4005",
        "Invalid .lui component signature",
        "LUI component '{0}' has an unresolved signature",
        "Lucent.Lui",
        DiagnosticSeverity.Error,
        true
    );

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var inputs = context
            .AdditionalTextsProvider.Where(static text =>
                text.Path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase)
            )
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(
                static (input, cancellationToken) =>
                    ParseInput.Read(
                        input.Left,
                        input.Right.GetOptions(input.Left),
                        cancellationToken
                    )
            )
            .WithTrackingName("LuiParse");
        var project = context
            .AnalyzerConfigOptionsProvider.Select(
                static (options, _) => ProjectInput.Read(options.GlobalOptions)
            )
            .WithTrackingName("LuiProjectContext");
        var index = context
            .CompilationProvider.Combine(inputs.Collect())
            .Select(
                static (input, cancellationToken) =>
                    ComponentIndex.Build(input.Left, input.Right, cancellationToken)
            )
            .WithTrackingName("LuiComponentIndex");
        var documents = inputs.Combine(index);
        var environment = context.CompilationProvider.Combine(project);
        var results = documents
            .Combine(environment)
            .Select(
                static (input, cancellationToken) =>
                    new Publication(
                        Lower(
                            input.Left.Left,
                            input.Left.Right,
                            input.Right.Left,
                            input.Right.Right,
                            cancellationToken
                        ),
                        Current(
                            input.Left.Left,
                            input.Left.Right,
                            input.Right.Left,
                            input.Right.Right,
                            cancellationToken
                        )
                    )
            )
            .WithTrackingName("LuiDocumentOutput");

        context.RegisterSourceOutput(
            inputs,
            static (production, input) => ReportInputDiagnostics(production, input)
        );
        context.RegisterSourceOutput(
            index,
            static (production, value) => value.ReportDiagnostics(production)
        );
        context.RegisterSourceOutput(
            results.Select(static (input, _) => input).WithTrackingName("LuiPublication"),
            static (production, input) => Publish(production, input.Lowered, input.Current)
        );
    }

    private static void ReportInputDiagnostics(SourceProductionContext production, ParseInput input)
    {
        if (!input.IsReadable)
            production.ReportDiagnostic(
                Diagnostic.Create(
                    InvalidInput,
                    Location.Create(input.Path, new TextSpan(0, 0), new LinePositionSpan()),
                    input.Path
                )
            );
        else if (!input.IsLogicalPathValid)
            production.ReportDiagnostic(
                Diagnostic.Create(
                    InvalidLogicalPath,
                    Location.Create(input.Path, new TextSpan(0, 0), new LinePositionSpan()),
                    input.Path,
                    input.LogicalPath
                )
            );
        else
            foreach (var diagnostic in input.Document!.Diagnostics)
                production.ReportDiagnostic(
                    Diagnostic.Create(
                        ParseDescriptor(diagnostic),
                        input.Location(diagnostic.Span),
                        diagnostic.Message
                    )
                );
    }

    private static DocumentResult Lower(
        ParseInput input,
        ComponentIndex index,
        Compilation compilation,
        ProjectInput project,
        System.Threading.CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!index.TryGet(input.Path, out var declaration))
            return new DocumentResult(input, null);
        var siblings = index
            .Declarations.Where(other => other.Input.Path != input.Path)
            .Select(other => other.Tree)
            .ToArray();
        var augmented = siblings.Length == 0 ? compilation : compilation.AddSyntaxTrees(siblings);
        var identity = CurrentIdentity(input, index, augmented, project);
        var result = LuiCompiler.Compile(input.Document!, augmented, identity);
        cancellationToken.ThrowIfCancellationRequested();
        return new DocumentResult(input, result);
    }

    private static CurrentDocument Current(
        ParseInput input,
        ComponentIndex index,
        Compilation compilation,
        ProjectInput project,
        System.Threading.CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!index.TryGet(input.Path, out _))
            return new CurrentDocument(input, null);
        var siblings = index
            .Declarations.Where(other => other.Input.Path != input.Path)
            .Select(other => other.Tree)
            .ToArray();
        var augmented = siblings.Length == 0 ? compilation : compilation.AddSyntaxTrees(siblings);
        return new CurrentDocument(input, CurrentIdentity(input, index, augmented, project));
    }

    private static LuiFreshnessIdentity CurrentIdentity(
        ParseInput input,
        ComponentIndex index,
        Compilation compilation,
        ProjectInput project
    )
    {
        var document = new LuiDocumentIdentity(input.LogicalPath);
        return LuiCompiler.Snapshot(
            new LuiFreshnessIdentity(
                project.Epoch,
                String.IsNullOrEmpty(project.Identity)
                    ? compilation.AssemblyName ?? ""
                    : project.Identity,
                document,
                input.DocumentVersion,
                "",
                index.Generation,
                project.LanguageVersion,
                "",
                "",
                "",
                project.Options,
                project.Defines
            ),
            compilation
        );
    }

    private static void Publish(
        SourceProductionContext production,
        DocumentResult lowered,
        CurrentDocument current
    )
    {
        production.CancellationToken.ThrowIfCancellationRequested();
        if (lowered.Result is null || current.Identity is null)
            return;
        foreach (var diagnostic in lowered.Result.Diagnostics)
        {
            production.CancellationToken.ThrowIfCancellationRequested();
            production.ReportDiagnostic(
                Diagnostic.Create(
                    ParseDescriptor(diagnostic),
                    lowered.Input.Location(diagnostic.Span),
                    diagnostic.Message
                )
            );
        }
        if (ShouldPublish(lowered.Result, current.Identity))
            production.AddSource(current.Identity.HintName, lowered.Result.Source!);
    }

    internal static bool ShouldPublish(LuiCompilationResult result, LuiFreshnessIdentity current) =>
        result.Success && result.Identity.CanPublishTo(current);

    private sealed class Publication
    {
        internal Publication(DocumentResult lowered, CurrentDocument current)
        {
            Lowered = lowered;
            Current = current;
        }

        internal DocumentResult Lowered { get; }
        internal CurrentDocument Current { get; }
    }

    private sealed class DocumentResult
    {
        internal DocumentResult(ParseInput input, LuiCompilationResult? result)
        {
            Input = input;
            Result = result;
        }

        internal ParseInput Input { get; }
        internal LuiCompilationResult? Result { get; }
    }

    private sealed class CurrentDocument
    {
        internal CurrentDocument(ParseInput input, LuiFreshnessIdentity? identity)
        {
            Input = input;
            Identity = identity;
        }

        internal ParseInput Input { get; }
        internal LuiFreshnessIdentity? Identity { get; }
    }

    private static string Declaration(ParseInput input)
    {
        var component = input.Document!.Component!;
        var access = component.Accessibility.IsMissing ? "internal" : component.Accessibility.Text;
        var directives = new System.Text.StringBuilder();
        var namespaceWritten = false;
        foreach (var node in input.Document.TopLevel)
        {
            if (node is LuiUsingSyntax @using)
                directives.Append("using ").Append(@using.Value).Append(";\n");
            else if (node is LuiNamespaceSyntax @namespace)
            {
                directives.Append("namespace ").Append(@namespace.Value).Append(";\n");
                namespaceWritten = true;
            }
        }
        if (!namespaceWritten)
            directives.Append("namespace Lucent.Lui.Generated;\n");
        return directives
            .Append(
                "public static partial class Components { [global::Lucent.Core.LucentComponentAttribute] "
            )
            .Append(access)
            .Append(" static global::Lucent.Core.ComponentRecipe ")
            .Append(component.Name.Text)
            .Append('(')
            .Append(
                string.Join(
                    ", ",
                    component.Parameters.Select(parameter => parameter.DeclarationText)
                )
            )
            .Append(") => null!; }")
            .ToString();
    }

    private static DiagnosticDescriptor ParseDescriptor(LuiDiagnostic diagnostic) =>
        new DiagnosticDescriptor(
            diagnostic.Id,
            "Invalid .lui syntax",
            "{0}",
            "Lucent.Lui",
            DiagnosticSeverity.Error,
            true
        );

    private sealed class ComponentIndex : IEquatable<ComponentIndex>
    {
        private ComponentIndex(
            ImmutableArray<IndexedDeclaration> declarations,
            ImmutableArray<IndexDiagnostic> diagnostics
        )
        {
            Declarations = declarations;
            Diagnostics = diagnostics;
            Generation = LuiDocumentIdentity.Hash(
                String.Join(
                    "\n",
                    declarations
                        .OrderBy(
                            declaration => declaration.Input.LogicalPath,
                            StringComparer.Ordinal
                        )
                        .Select(declaration =>
                            declaration.Input.LogicalPath + "\0" + declaration.Fingerprint
                        )
                )
            );
        }

        internal ImmutableArray<IndexedDeclaration> Declarations { get; }
        private ImmutableArray<IndexDiagnostic> Diagnostics { get; }
        internal string Generation { get; }

        internal static ComponentIndex Build(
            Compilation compilation,
            ImmutableArray<ParseInput> inputs,
            System.Threading.CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var duplicatePaths = new System.Collections.Generic.HashSet<string>(
                inputs
                    .Where(input => input.IsReadable && input.IsLogicalPathValid)
                    .GroupBy(input => input.LogicalPath, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .SelectMany(group => group)
                    .Select(input => input.Path),
                StringComparer.Ordinal
            );
            var candidates = inputs
                .Where(input =>
                    input.IsReadable
                    && input.IsLogicalPathValid
                    && input.Document!.Diagnostics.Count == 0
                    && !duplicatePaths.Contains(input.Path)
                    && input.Document.Component is not null
                )
                .ToArray();
            var parseOptions =
                (CSharpParseOptions?)compilation.SyntaxTrees.FirstOrDefault()?.Options
                ?? CSharpParseOptions.Default;
            var unbound = candidates
                .Select(input => new IndexedDeclaration(
                    input,
                    CSharpSyntaxTree.ParseText(
                        Declaration(input),
                        parseOptions,
                        input.Path + ".lui.index.g.cs"
                    )
                ))
                .ToArray();
            var indexCompilation =
                unbound.Length == 0
                    ? compilation
                    : compilation.AddSyntaxTrees(unbound.Select(item => item.Tree));
            var indexed = unbound.Select(item => item.Bind(indexCompilation)).ToArray();
            var duplicateComponents = indexed
                .Where(declaration => declaration.Valid)
                .GroupBy(declaration => declaration.Identity, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .SelectMany(group => group)
                .Select(declaration => declaration.Input.Path)
                .ToImmutableHashSet(StringComparer.Ordinal);
            var declarations = indexed
                .Where(declaration =>
                    declaration.Valid && !duplicateComponents.Contains(declaration.Input.Path)
                )
                .ToImmutableArray();
            var diagnostics = ImmutableArray.CreateBuilder<IndexDiagnostic>();
            foreach (var input in inputs.Where(input => duplicatePaths.Contains(input.Path)))
                diagnostics.Add(
                    new IndexDiagnostic(DuplicateInput, input, input.LogicalPath, new LuiSpan(0, 0))
                );
            foreach (var declaration in indexed.Where(declaration => !declaration.Valid))
                diagnostics.Add(
                    new IndexDiagnostic(
                        InvalidSibling,
                        declaration.Input,
                        declaration.Input.Document!.Component!.Name.Text,
                        declaration.Input.Document.Component.Name.Span
                    )
                );
            foreach (
                var declaration in indexed.Where(declaration =>
                    duplicateComponents.Contains(declaration.Input.Path)
                )
            )
                diagnostics.Add(
                    new IndexDiagnostic(
                        DuplicateComponent,
                        declaration.Input,
                        declaration.Identity,
                        declaration.Input.Document!.Component!.Name.Span
                    )
                );
            cancellationToken.ThrowIfCancellationRequested();
            return new ComponentIndex(declarations, diagnostics.ToImmutable());
        }

        internal bool TryGet(string path, out IndexedDeclaration declaration)
        {
            var found = Declarations.FirstOrDefault(item =>
                StringComparer.Ordinal.Equals(item.Input.Path, path)
            );
            declaration = found!;
            return found is not null;
        }

        internal void ReportDiagnostics(SourceProductionContext production)
        {
            foreach (var diagnostic in Diagnostics)
            {
                production.CancellationToken.ThrowIfCancellationRequested();
                var location = diagnostic.Input.Location(diagnostic.Span);
                production.ReportDiagnostic(
                    diagnostic.Descriptor == DuplicateInput
                        ? Diagnostic.Create(
                            diagnostic.Descriptor,
                            location,
                            diagnostic.Input.Path,
                            diagnostic.Value
                        )
                        : Diagnostic.Create(diagnostic.Descriptor, location, diagnostic.Value)
                );
            }
        }

        public bool Equals(ComponentIndex? other) =>
            other is not null
            && Declarations.SequenceEqual(other.Declarations)
            && Diagnostics.SequenceEqual(other.Diagnostics);

        public override bool Equals(object? obj) => Equals(obj as ComponentIndex);

        public override int GetHashCode()
        {
            var hash = 17;
            foreach (var declaration in Declarations)
                hash = hash * 31 + declaration.GetHashCode();
            foreach (var diagnostic in Diagnostics)
                hash = hash * 31 + diagnostic.GetHashCode();
            return hash;
        }
    }

    private sealed class IndexedDeclaration : IEquatable<IndexedDeclaration>
    {
        internal IndexedDeclaration(
            ParseInput input,
            SyntaxTree tree,
            string identity = "",
            string fingerprint = "",
            bool valid = false
        )
        {
            Input = input;
            Tree = tree;
            Identity = identity;
            Fingerprint = fingerprint;
            Valid = valid;
        }

        internal ParseInput Input { get; }
        internal SyntaxTree Tree { get; }
        internal string Identity { get; }
        internal string Fingerprint { get; }
        internal bool Valid { get; }

        internal IndexedDeclaration Bind(Compilation compilation)
        {
            var method = Tree.GetRoot()
                .DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
                .SingleOrDefault();
            var symbol = method is null
                ? null
                : compilation.GetSemanticModel(Tree).GetDeclaredSymbol(method);
            if (
                symbol is null
                || ContainsErrorType(symbol.ReturnType)
                || symbol.Parameters.Any(parameter => ContainsErrorType(parameter.Type))
            )
                return this;
            var identity =
                symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                + "."
                + symbol.MetadataName;
            return new IndexedDeclaration(
                Input,
                Tree,
                identity,
                DeclarationFingerprint(Tree),
                true
            );
        }

        private static string DeclarationFingerprint(SyntaxTree tree)
        {
            var root = tree.GetRoot();
            var method = root.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
                .Single();
            return root.ReplaceNode(
                    method,
                    method.WithBody(null).WithExpressionBody(null).WithSemicolonToken(default)
                )
                .NormalizeWhitespace()
                .ToFullString();
        }

        private static bool ContainsErrorType(ITypeSymbol type) =>
            type.TypeKind == TypeKind.Error
            || type is IArrayTypeSymbol array && ContainsErrorType(array.ElementType)
            || type is IPointerTypeSymbol pointer && ContainsErrorType(pointer.PointedAtType)
            || type is INamedTypeSymbol named && named.TypeArguments.Any(ContainsErrorType);

        public bool Equals(IndexedDeclaration? other) =>
            other is not null
            && Valid == other.Valid
            && StringComparer.Ordinal.Equals(Input.Path, other.Input.Path)
            && StringComparer.Ordinal.Equals(Input.LogicalPath, other.Input.LogicalPath)
            && StringComparer.Ordinal.Equals(Identity, other.Identity)
            && StringComparer.Ordinal.Equals(Fingerprint, other.Fingerprint);

        public override bool Equals(object? obj) => Equals(obj as IndexedDeclaration);

        public override int GetHashCode() =>
            (
                Input.Path
                + "\0"
                + Input.LogicalPath
                + "\0"
                + Identity
                + "\0"
                + Fingerprint
                + "\0"
                + Valid
            ).GetHashCode();
    }

    private sealed class IndexDiagnostic : IEquatable<IndexDiagnostic>
    {
        internal IndexDiagnostic(
            DiagnosticDescriptor descriptor,
            ParseInput input,
            string value,
            LuiSpan span
        )
        {
            Descriptor = descriptor;
            Input = input;
            Value = value;
            Span = span;
        }

        internal DiagnosticDescriptor Descriptor { get; }
        internal ParseInput Input { get; }
        internal string Value { get; }
        internal LuiSpan Span { get; }

        public bool Equals(IndexDiagnostic? other) =>
            other is not null
            && Descriptor.Id == other.Descriptor.Id
            && StringComparer.Ordinal.Equals(Input.Path, other.Input.Path)
            && StringComparer.Ordinal.Equals(Input.DocumentVersion, other.Input.DocumentVersion)
            && Span.Equals(other.Span)
            && StringComparer.Ordinal.Equals(Value, other.Value);

        public override bool Equals(object? obj) => Equals(obj as IndexDiagnostic);

        public override int GetHashCode() =>
            (
                Descriptor.Id
                + "\0"
                + Input.Path
                + "\0"
                + Input.DocumentVersion
                + "\0"
                + Span.Start
                + "\0"
                + Span.Length
                + "\0"
                + Value
            ).GetHashCode();
    }

    private sealed class ParseInput : IEquatable<ParseInput>
    {
        private ParseInput(
            string path,
            string logicalPath,
            string source,
            string documentVersion,
            SourceText? sourceText,
            bool readable,
            bool logicalPathValid
        )
        {
            Path = path;
            LogicalPath = logicalPath;
            Source = source;
            DocumentVersion = documentVersion;
            SourceText = sourceText;
            IsReadable = readable;
            IsLogicalPathValid = logicalPathValid;
            Document = readable && logicalPathValid ? LuiParser.Parse(source) : null;
        }

        public string Path { get; }
        public string LogicalPath { get; }
        public string Source { get; }
        public string DocumentVersion { get; }
        public SourceText? SourceText { get; }
        public bool IsReadable { get; }
        public bool IsLogicalPathValid { get; }
        public LuiDocumentSyntax? Document { get; }

        public static ParseInput Read(
            AdditionalText text,
            AnalyzerConfigOptions options,
            System.Threading.CancellationToken cancellationToken
        )
        {
            var source = text.GetText(cancellationToken);
            var hasLogicalPath = options.TryGetValue(
                "build_metadata.AdditionalFiles.LucentLuiLogicalPath",
                out var logicalPath
            );
            var path = hasLogicalPath ? logicalPath! : System.IO.Path.GetFileName(text.Path);
            var value = source?.ToString() ?? "";
            var version =
                options.TryGetValue(
                    "build_metadata.AdditionalFiles.LucentLuiDocumentVersion",
                    out var configuredVersion
                ) && !String.IsNullOrEmpty(configuredVersion)
                    ? configuredVersion
                    : LuiDocumentIdentity.Hash(value);
            try
            {
                path = new LuiDocumentIdentity(path).LogicalPath;
                return new ParseInput(
                    text.Path,
                    path,
                    value,
                    version,
                    source,
                    source != null,
                    true
                );
            }
            catch (ArgumentException)
            {
                return new ParseInput(
                    text.Path,
                    path,
                    value,
                    version,
                    source,
                    source != null,
                    false
                );
            }
        }

        public Microsoft.CodeAnalysis.Location Location(LuiSpan span)
        {
            var source = SourceText!;
            var bounded = new TextSpan(
                Math.Min(span.Start, source.Length),
                Math.Min(span.Length, source.Length - Math.Min(span.Start, source.Length))
            );
            return Microsoft.CodeAnalysis.Location.Create(
                Path,
                bounded,
                source.Lines.GetLinePositionSpan(bounded)
            );
        }

        public bool Equals(ParseInput? other) =>
            other != null
            && Path == other.Path
            && LogicalPath == other.LogicalPath
            && Source == other.Source
            && DocumentVersion == other.DocumentVersion
            && IsReadable == other.IsReadable
            && IsLogicalPathValid == other.IsLogicalPathValid;

        public override bool Equals(object? obj) => Equals(obj as ParseInput);

        public override int GetHashCode() =>
            (
                Path
                + "\0"
                + LogicalPath
                + "\0"
                + Source
                + "\0"
                + DocumentVersion
                + "\0"
                + IsReadable
                + "\0"
                + IsLogicalPathValid
            ).GetHashCode();
    }

    private sealed class ProjectInput : IEquatable<ProjectInput>
    {
        private ProjectInput(
            string epoch,
            string identity,
            string languageVersion,
            string options,
            string defines
        )
        {
            Epoch = epoch;
            Identity = identity;
            LanguageVersion = languageVersion;
            Options = options;
            Defines = defines;
        }

        public string Epoch { get; }
        public string Identity { get; }
        public string LanguageVersion { get; }
        public string Options { get; }
        public string Defines { get; }

        public static ProjectInput Read(AnalyzerConfigOptions options)
        {
            options.TryGetValue("build_property.LucentLuiProjectEpoch", out var epoch);
            options.TryGetValue("build_property.LucentLuiProjectIdentity", out var identity);
            options.TryGetValue("build_property.LucentLuiLangVersion", out var languageVersion);
            options.TryGetValue("build_property.LucentLuiCompilerOptions", out var compilerOptions);
            options.TryGetValue("build_property.LucentLuiDefines", out var defines);
            return new ProjectInput(
                epoch ?? "",
                identity ?? "",
                languageVersion ?? "",
                compilerOptions ?? "",
                defines ?? ""
            );
        }

        public bool Equals(ProjectInput? other) =>
            other is not null
            && Epoch == other.Epoch
            && Identity == other.Identity
            && LanguageVersion == other.LanguageVersion
            && Options == other.Options
            && Defines == other.Defines;

        public override bool Equals(object? obj) => Equals(obj as ProjectInput);

        public override int GetHashCode() =>
            (
                Epoch + "\0" + Identity + "\0" + LanguageVersion + "\0" + Options + "\0" + Defines
            ).GetHashCode();
    }
}
