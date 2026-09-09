using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lucent.Lui.Compiler;

/// <summary>One already-evaluated LUI document supplied by a build or editor host.</summary>
public sealed class LuiProjectDocument
{
    /// <summary>Creates one already-evaluated LUI document.</summary>
    public LuiProjectDocument(string path, string logicalPath, string source, string version)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        LogicalPath = new LuiDocumentIdentity(logicalPath).LogicalPath;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Version = version ?? throw new ArgumentNullException(nameof(version));
        Syntax = LuiParser.Parse(source);
    }

    /// <summary>Physical editor/build input path.</summary>
    public string Path { get; }

    /// <summary>Evaluated project-relative logical identity.</summary>
    public string LogicalPath { get; }

    /// <summary>Current source.</summary>
    public string Source { get; }

    /// <summary>Host document version.</summary>
    public string Version { get; }

    /// <summary>Recovered shared parser result.</summary>
    public LuiDocumentSyntax Syntax { get; }
}

/// <summary>Validated component declarations shared by build and editor project adapters.</summary>
public sealed class LuiProjectComponentIndex : IEquatable<LuiProjectComponentIndex>
{
    private readonly Declaration[] declarations;
    private readonly Diagnostic[] diagnostics;

    private LuiProjectComponentIndex(Declaration[] declarations, Diagnostic[] diagnostics)
    {
        this.declarations = declarations;
        this.diagnostics = diagnostics;
        Generation = LuiDocumentIdentity.Hash(
            String.Join(
                "\n",
                declarations
                    .OrderBy(item => item.Document.LogicalPath, StringComparer.Ordinal)
                    .Select(item => item.Document.LogicalPath + "\0" + item.Fingerprint)
            )
        );
    }

    /// <summary>Valid, non-duplicate declarations in deterministic input order.</summary>
    public IReadOnlyList<Declaration> Declarations => declarations;

    /// <summary>Duplicate and unresolved-signature diagnostics found while indexing.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => diagnostics;

    /// <summary>Deterministic signature-only generation, unaffected by component bodies.</summary>
    public string Generation { get; }

    /// <summary>Builds and semantically validates the sole project component index.</summary>
    public static LuiProjectComponentIndex Build(
        Compilation compilation,
        IEnumerable<LuiProjectDocument> documents,
        CancellationToken cancellationToken = default
    )
    {
        if (compilation is null)
            throw new ArgumentNullException(nameof(compilation));
        if (documents is null)
            throw new ArgumentNullException(nameof(documents));
        cancellationToken.ThrowIfCancellationRequested();
        var inputs = documents.ToArray();
        var duplicatePaths = new HashSet<string>(
            inputs
                .GroupBy(document => document.LogicalPath, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .SelectMany(group => group)
                .Select(document => document.Path),
            StringComparer.Ordinal
        );
        var candidates = inputs
            .Where(document =>
                document.Syntax.Diagnostics.Count == 0
                && document.Syntax.Component is not null
                && !duplicatePaths.Contains(document.Path)
            )
            .ToArray();
        var parseOptions =
            compilation
                .SyntaxTrees.Select(tree => tree.Options)
                .OfType<CSharpParseOptions>()
                .FirstOrDefault()
            ?? CSharpParseOptions.Default;
        var unbound = candidates
            .Select(document => new Declaration(
                document,
                CSharpSyntaxTree.ParseText(
                    DeclarationSource(document.Syntax),
                    parseOptions,
                    document.Path + ".lui.index.g.cs"
                )
            ))
            .ToArray();
        var indexCompilation =
            unbound.Length == 0
                ? compilation
                : compilation.AddSyntaxTrees(unbound.Select(item => item.Tree));
        var indexed = unbound.Select(item => item.Bind(indexCompilation)).ToArray();
        var duplicateComponents = new HashSet<string>(
            indexed
                .Where(item => item.IsValid)
                .GroupBy(item => item.Identity, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .SelectMany(group => group)
                .Select(item => item.Document.Path),
            StringComparer.Ordinal
        );
        var valid = indexed
            .Where(item => item.IsValid && !duplicateComponents.Contains(item.Document.Path))
            .ToArray();
        var found = new List<Diagnostic>();
        found.AddRange(
            inputs
                .Where(document => duplicatePaths.Contains(document.Path))
                .Select(document => new Diagnostic(
                    DiagnosticKind.DuplicateLogicalPath,
                    document,
                    document.LogicalPath,
                    new LuiSpan(0, 0)
                ))
        );
        found.AddRange(
            indexed
                .Where(item => !item.IsValid)
                .Select(item => new Diagnostic(
                    DiagnosticKind.InvalidComponentSignature,
                    item.Document,
                    item.Document.Syntax.Component!.Name.Text,
                    item.Document.Syntax.Component.Name.Span
                ))
        );
        found.AddRange(
            indexed
                .Where(item => duplicateComponents.Contains(item.Document.Path))
                .Select(item => new Diagnostic(
                    DiagnosticKind.DuplicateComponent,
                    item.Document,
                    item.Identity,
                    item.Document.Syntax.Component!.Name.Span
                ))
        );
        cancellationToken.ThrowIfCancellationRequested();
        return new LuiProjectComponentIndex(valid, found.ToArray());
    }

    /// <summary>Returns the valid declaration for a physical document path.</summary>
    public bool TryGet(string path, out Declaration declaration)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));
        var found = declarations.FirstOrDefault(item =>
            StringComparer.Ordinal.Equals(item.Document.Path, path)
        );
        declaration = found!;
        return found is not null;
    }

    /// <summary>Adds only valid sibling signatures to a compilation used to bind one document.</summary>
    public Compilation Augment(Compilation compilation, string currentPath)
    {
        if (compilation is null)
            throw new ArgumentNullException(nameof(compilation));
        if (currentPath is null)
            throw new ArgumentNullException(nameof(currentPath));
        return compilation.AddSyntaxTrees(
            declarations
                .Where(item =>
                    !String.Equals(item.Document.Path, currentPath, StringComparison.Ordinal)
                    && !Exists(compilation, item)
                )
                .Select(item => item.Tree)
        );
    }

    private static bool Exists(Compilation compilation, Declaration declaration) =>
        compilation
            .GetTypeByMetadataName(declaration.ContainingType)
            ?.GetMembers(declaration.MetadataName)
            .OfType<IMethodSymbol>()
            .Any() == true;

    private static string DeclarationSource(LuiDocumentSyntax document)
    {
        var text = new System.Text.StringBuilder();
        var namespaceWritten = false;
        foreach (var node in document.TopLevel)
        {
            if (node is LuiUsingSyntax @using)
                text.Append("using ").Append(@using.Value).Append(";\n");
            else if (node is LuiNamespaceSyntax @namespace)
            {
                text.Append("namespace ").Append(@namespace.Value).Append(";\n");
                namespaceWritten = true;
            }
        }
        if (!namespaceWritten)
            text.Append("namespace Lucent.Lui.Generated;\n");
        var component = document.Component!;
        text.Append(
            "/// <summary>Generated Lucent component recipes.</summary>\npublic static partial class Components {\n"
        );
        foreach (var comment in LuiDocumentation.ForComponent(document, component))
            text.Append(LuiDocumentation.Indent(comment));
        return text.Append("[global::Lucent.Core.LucentComponentAttribute] ")
            .Append(component.Accessibility.IsMissing ? "internal" : component.Accessibility.Text)
            .Append(" static global::Lucent.Core.ComponentRecipe ")
            .Append(component.Name.Text)
            .Append('(')
            .Append(String.Join(", ", component.Parameters.Select(item => item.DeclarationText)))
            .Append(") => null!; }")
            .ToString();
    }

    /// <inheritdoc />
    public bool Equals(LuiProjectComponentIndex? other) =>
        other is not null
        && declarations.SequenceEqual(other.declarations)
        && diagnostics.SequenceEqual(other.diagnostics);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as LuiProjectComponentIndex);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var declaration in declarations)
            hash = hash * 31 + declaration.GetHashCode();
        foreach (var diagnostic in diagnostics)
            hash = hash * 31 + diagnostic.GetHashCode();
        return hash;
    }

    /// <summary>Kind of project-index validation failure.</summary>
    public enum DiagnosticKind
    {
        /// <summary>More than one physical input has the same logical path.</summary>
        DuplicateLogicalPath,

        /// <summary>More than one component has the same canonical declaration identity.</summary>
        DuplicateComponent,

        /// <summary>A component parameter type could not be resolved.</summary>
        InvalidComponentSignature,
    }

    /// <summary>One project-index validation failure tied to its authored document.</summary>
    public sealed class Diagnostic : IEquatable<Diagnostic>
    {
        internal Diagnostic(
            DiagnosticKind kind,
            LuiProjectDocument document,
            string value,
            LuiSpan span
        )
        {
            Kind = kind;
            Document = document;
            Value = value;
            Span = span;
        }

        /// <summary>Failure category.</summary>
        public DiagnosticKind Kind { get; }

        /// <summary>Document that receives the diagnostic.</summary>
        public LuiProjectDocument Document { get; }

        /// <summary>Logical path, component identity, or component name used in the message.</summary>
        public string Value { get; }

        /// <summary>Exact authored diagnostic span.</summary>
        public LuiSpan Span { get; }

        /// <inheritdoc />
        public bool Equals(Diagnostic? other) =>
            other is not null
            && Kind == other.Kind
            && StringComparer.Ordinal.Equals(Document.Path, other.Document.Path)
            && StringComparer.Ordinal.Equals(Document.Version, other.Document.Version)
            && Span.Equals(other.Span)
            && StringComparer.Ordinal.Equals(Value, other.Value);

        /// <inheritdoc />
        public override bool Equals(object? obj) => Equals(obj as Diagnostic);

        /// <inheritdoc />
        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(
                Kind
                    + "\0"
                    + Document.Path
                    + "\0"
                    + Document.Version
                    + "\0"
                    + Span.Start
                    + "\0"
                    + Span.Length
                    + "\0"
                    + Value
            );
    }

    /// <summary>One semantically validated component signature.</summary>
    public sealed class Declaration : IEquatable<Declaration>
    {
        internal Declaration(
            LuiProjectDocument document,
            SyntaxTree tree,
            string identity = "",
            string containingType = "",
            string metadataName = "",
            string fingerprint = "",
            bool isValid = false
        )
        {
            Document = document;
            Tree = tree;
            Identity = identity;
            ContainingType = containingType;
            MetadataName = metadataName;
            Fingerprint = fingerprint;
            IsValid = isValid;
        }

        /// <summary>Authored source of the declaration.</summary>
        public LuiProjectDocument Document { get; }

        /// <summary>Canonical namespace, containing type, and metadata method name.</summary>
        public string Identity { get; }

        internal SyntaxTree Tree { get; }
        internal string ContainingType { get; }
        internal string MetadataName { get; }
        internal string Fingerprint { get; }
        internal bool IsValid { get; }

        internal Declaration Bind(Compilation compilation)
        {
            var method = Tree.GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
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
            return new Declaration(
                Document,
                Tree,
                symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    + "."
                    + symbol.MetadataName,
                symbol.ContainingType.ToDisplayString(),
                symbol.MetadataName,
                Tree.GetRoot().NormalizeWhitespace().ToFullString(),
                true
            );
        }

        private static bool ContainsErrorType(ITypeSymbol type) =>
            type.TypeKind == TypeKind.Error
            || type is IArrayTypeSymbol array && ContainsErrorType(array.ElementType)
            || type is IPointerTypeSymbol pointer && ContainsErrorType(pointer.PointedAtType)
            || type is INamedTypeSymbol named && named.TypeArguments.Any(ContainsErrorType);

        /// <inheritdoc />
        public bool Equals(Declaration? other) =>
            other is not null
            && IsValid == other.IsValid
            && StringComparer.Ordinal.Equals(Document.Path, other.Document.Path)
            && StringComparer.Ordinal.Equals(Document.LogicalPath, other.Document.LogicalPath)
            && StringComparer.Ordinal.Equals(Identity, other.Identity)
            && StringComparer.Ordinal.Equals(Fingerprint, other.Fingerprint);

        /// <inheritdoc />
        public override bool Equals(object? obj) => Equals(obj as Declaration);

        /// <inheritdoc />
        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(
                Document.Path
                    + "\0"
                    + Document.LogicalPath
                    + "\0"
                    + Identity
                    + "\0"
                    + Fingerprint
                    + "\0"
                    + IsValid
            );
    }
}
