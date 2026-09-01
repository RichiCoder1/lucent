using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Lucent.Lui.Compiler;

namespace Lucent.Lui.Generator;

[Generator]
public sealed class LuiGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor InvalidInput = new DiagnosticDescriptor("LUI4001", "Unreadable .lui input", "LUI input '{0}' is unreadable", "Lucent.Lui", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor DuplicateInput = new DiagnosticDescriptor("LUI4002", "Duplicate .lui input", "LUI input '{0}' has duplicate logical path '{1}'", "Lucent.Lui", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor InvalidLogicalPath = new DiagnosticDescriptor("LUI4003", "Invalid .lui logical path", "LUI input '{0}' has invalid logical path '{1}'", "Lucent.Lui", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor DuplicateComponent = new DiagnosticDescriptor("LUI4004", "Duplicate .lui component", "LUI component '{0}' is declared more than once", "Lucent.Lui", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor InvalidSibling = new DiagnosticDescriptor("LUI4005", "Invalid .lui component signature", "LUI component '{0}' has an unresolved signature", "Lucent.Lui", DiagnosticSeverity.Error, true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var inputs = context.AdditionalTextsProvider
            .Where(static text => text.Path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (input, cancellationToken) => ParseInput.Read(input.Left, input.Right.GetOptions(input.Left), cancellationToken));
        var project = context.AnalyzerConfigOptionsProvider.Select(static (options, _) => ProjectInput.Read(options.GlobalOptions));
        context.RegisterSourceOutput(context.CompilationProvider.Combine(inputs.Collect()).Combine(project), static (production, input) => Emit(production, input.Left.Left, input.Left.Right, input.Right));
    }

    private static void Emit(SourceProductionContext production, Compilation compilation, ImmutableArray<ParseInput> inputs, ProjectInput project)
    {
        foreach (var input in inputs.Where(input => !input.IsReadable)) production.ReportDiagnostic(Diagnostic.Create(InvalidInput, Location.Create(input.Path, new TextSpan(0, 0), new LinePositionSpan()), input.Path));
        foreach (var input in inputs.Where(input => !input.IsLogicalPathValid)) production.ReportDiagnostic(Diagnostic.Create(InvalidLogicalPath, Location.Create(input.Path, new TextSpan(0, 0), new LinePositionSpan()), input.Path, input.LogicalPath));
        foreach (var input in inputs.Where(input => input.IsReadable && input.IsLogicalPathValid)) foreach (var diagnostic in input.Document!.Diagnostics) production.ReportDiagnostic(Diagnostic.Create(ParseDescriptor(diagnostic), input.Location(diagnostic.Span), diagnostic.Message));
        var duplicates = inputs.Where(input => input.IsReadable && input.IsLogicalPathValid).GroupBy(input => input.LogicalPath, StringComparer.Ordinal).Where(group => group.Count() > 1).SelectMany(group => group);
        var duplicatePaths = new System.Collections.Generic.HashSet<string>(duplicates.Select(input => input.Path), StringComparer.Ordinal);
        foreach (var input in inputs.Where(input => duplicatePaths.Contains(input.Path))) production.ReportDiagnostic(Diagnostic.Create(DuplicateInput, Location.Create(input.Path, new TextSpan(0, 0), new LinePositionSpan()), input.Path, input.LogicalPath));
        var candidates = inputs.Where(input => input.IsReadable && input.IsLogicalPathValid && input.Document!.Diagnostics.Count == 0 && !duplicatePaths.Contains(input.Path) && input.Document!.Component is not null).ToArray();
        var parseOptions = (CSharpParseOptions?)compilation.SyntaxTrees.FirstOrDefault()?.Options ?? CSharpParseOptions.Default;
        var indexed = candidates.Select(input => new IndexedDeclaration(input, CSharpSyntaxTree.ParseText(Declaration(input), parseOptions, input.Path + ".lui.index.g.cs"))).ToArray();
        var indexCompilation = indexed.Length == 0 ? compilation : compilation.AddSyntaxTrees(indexed.Select(item => item.Tree));
        foreach (var declaration in indexed)
        {
            declaration.Bind(indexCompilation);
            if (!declaration.Valid) production.ReportDiagnostic(Diagnostic.Create(InvalidSibling, declaration.Input.Location(declaration.Input.Document!.Component!.Name.Span), declaration.Input.Document.Component.Name.Text));
        }
        var duplicateComponents = indexed.Where(declaration => declaration.Valid).GroupBy(declaration => declaration.Identity, StringComparer.Ordinal).Where(group => group.Count() > 1).SelectMany(group => group).ToArray();
        foreach (var declaration in duplicateComponents) production.ReportDiagnostic(Diagnostic.Create(DuplicateComponent, declaration.Input.Location(declaration.Input.Document!.Component!.Name.Span), declaration.Identity));
        var duplicateComponentPaths = new System.Collections.Generic.HashSet<string>(duplicateComponents.Select(declaration => declaration.Input.Path), StringComparer.Ordinal);
        var declarations = indexed.Where(declaration => declaration.Valid && !duplicateComponentPaths.Contains(declaration.Input.Path)).ToArray();
        var siblingIndexGeneration = LuiDocumentIdentity.Hash(String.Join("\n", declarations.OrderBy(declaration => declaration.Input.LogicalPath, StringComparer.Ordinal).Select(declaration => declaration.Input.LogicalPath + "\0" + declaration.Freshness)));
        foreach (var declaration in declarations)
        {
            var input = declaration.Input;
            var document = new LuiDocumentIdentity(input.LogicalPath);
            var siblings = declarations.Where(other => other.Input.Path != input.Path).Select(other => other.Tree).ToArray();
            var augmented = siblings.Length == 0 ? compilation : compilation.AddSyntaxTrees(siblings);
            var identity = LuiCompiler.Snapshot(new LuiFreshnessIdentity(project.Epoch, String.IsNullOrEmpty(project.Identity) ? compilation.AssemblyName ?? "" : project.Identity, document, input.DocumentVersion, "", siblingIndexGeneration, project.LanguageVersion, "", "", "", project.Options, project.Defines), augmented);
            var result = LuiCompiler.Compile(input.Document!, augmented, identity);
            foreach (var diagnostic in result.Diagnostics) production.ReportDiagnostic(Diagnostic.Create(ParseDescriptor(diagnostic), input.Location(diagnostic.Span), diagnostic.Message));
            if (result.Success && result.Identity.CanPublishTo(identity)) production.AddSource(identity.HintName, result.Source!);
        }
    }

    private static string Declaration(ParseInput input)
    {
        var component = input.Document!.Component!;
        var access = component.Accessibility.IsMissing ? "internal" : component.Accessibility.Text;
        var directives = new System.Text.StringBuilder(); var namespaceWritten = false;
        foreach (var node in input.Document.TopLevel)
        {
            if (node is LuiUsingSyntax @using) directives.Append("using ").Append(@using.Value).Append(";\n");
            else if (node is LuiNamespaceSyntax @namespace) { directives.Append("namespace ").Append(@namespace.Value).Append(";\n"); namespaceWritten = true; }
        }
        if (!namespaceWritten) directives.Append("namespace Lucent.Lui.Generated;\n");
        return directives.Append("public static partial class Components { [global::Lucent.Core.LucentComponentAttribute] ").Append(access).Append(" static global::Lucent.Core.ComponentRecipe ").Append(component.Name.Text).Append("(").Append(string.Join(", ", component.Parameters.Select(parameter => parameter.DeclarationText))).Append(") => null!; }").ToString();
    }

    private static DiagnosticDescriptor ParseDescriptor(LuiDiagnostic diagnostic) => new DiagnosticDescriptor(diagnostic.Id, "Invalid .lui syntax", "{0}", "Lucent.Lui", DiagnosticSeverity.Error, true);

    private sealed class IndexedDeclaration
    {
        internal IndexedDeclaration(ParseInput input, SyntaxTree tree) { Input = input; Tree = tree; }
        internal ParseInput Input { get; }
        internal SyntaxTree Tree { get; }
        internal string Identity { get; private set; } = "";
        internal string Freshness { get; private set; } = "";
        internal bool Valid { get; private set; }

        internal void Bind(Compilation compilation)
        {
            var method = Tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().SingleOrDefault();
            var symbol = method is null ? null : compilation.GetSemanticModel(Tree).GetDeclaredSymbol(method);
            if (symbol is null || ContainsErrorType(symbol.ReturnType) || symbol.Parameters.Any(parameter => ContainsErrorType(parameter.Type))) return;
            Identity = symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + symbol.MetadataName;
            Freshness = Identity + "(" + string.Join(",", symbol.Parameters.Select(parameter => parameter.RefKind + ":" + parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) + ")";
            Valid = true;
        }

        private static bool ContainsErrorType(ITypeSymbol type) => type.TypeKind == TypeKind.Error || type is IArrayTypeSymbol array && ContainsErrorType(array.ElementType) || type is IPointerTypeSymbol pointer && ContainsErrorType(pointer.PointedAtType) || type is INamedTypeSymbol named && named.TypeArguments.Any(ContainsErrorType);
    }

    private sealed class ParseInput : IEquatable<ParseInput>
    {
        private ParseInput(string path, string logicalPath, string source, string documentVersion, SourceText? sourceText, bool readable, bool logicalPathValid) { Path = path; LogicalPath = logicalPath; Source = source; DocumentVersion = documentVersion; SourceText = sourceText; IsReadable = readable; IsLogicalPathValid = logicalPathValid; Document = readable && logicalPathValid ? LuiParser.Parse(source) : null; }
        public string Path { get; } public string LogicalPath { get; } public string Source { get; } public string DocumentVersion { get; } public SourceText? SourceText { get; } public bool IsReadable { get; } public bool IsLogicalPathValid { get; } public LuiDocumentSyntax? Document { get; }
        public static ParseInput Read(AdditionalText text, AnalyzerConfigOptions options, System.Threading.CancellationToken cancellationToken)
        {
            var source = text.GetText(cancellationToken);
            var hasLogicalPath = options.TryGetValue("build_metadata.AdditionalFiles.LucentLuiLogicalPath", out var logicalPath);
            var path = hasLogicalPath ? logicalPath! : System.IO.Path.GetFileName(text.Path);
            var value = source?.ToString() ?? "";
            var version = options.TryGetValue("build_metadata.AdditionalFiles.LucentLuiDocumentVersion", out var configuredVersion) && !String.IsNullOrEmpty(configuredVersion) ? configuredVersion : LuiDocumentIdentity.Hash(value);
            try { path = new LuiDocumentIdentity(path).LogicalPath; return new ParseInput(text.Path, path, value, version, source, source != null, true); }
            catch (ArgumentException) { return new ParseInput(text.Path, path, value, version, source, source != null, false); }
        }
        public Microsoft.CodeAnalysis.Location Location(LuiSpan span) { var source = SourceText!; var bounded = new TextSpan(Math.Min(span.Start, source.Length), Math.Min(span.Length, source.Length - Math.Min(span.Start, source.Length))); return Microsoft.CodeAnalysis.Location.Create(Path, bounded, source.Lines.GetLinePositionSpan(bounded)); }
        public bool Equals(ParseInput? other) => other != null && Path == other.Path && LogicalPath == other.LogicalPath && Source == other.Source && DocumentVersion == other.DocumentVersion && IsReadable == other.IsReadable && IsLogicalPathValid == other.IsLogicalPathValid;
        public override bool Equals(object? obj) => Equals(obj as ParseInput);
        public override int GetHashCode() => (Path + "\0" + LogicalPath + "\0" + Source + "\0" + DocumentVersion + "\0" + IsReadable + "\0" + IsLogicalPathValid).GetHashCode();
    }

    private sealed class ProjectInput : IEquatable<ProjectInput>
    {
        private ProjectInput(string epoch, string identity, string languageVersion, string options, string defines) { Epoch = epoch; Identity = identity; LanguageVersion = languageVersion; Options = options; Defines = defines; }
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
            return new ProjectInput(epoch ?? "", identity ?? "", languageVersion ?? "", compilerOptions ?? "", defines ?? "");
        }
        public bool Equals(ProjectInput? other) => other is not null && Epoch == other.Epoch && Identity == other.Identity && LanguageVersion == other.LanguageVersion && Options == other.Options && Defines == other.Defines;
        public override bool Equals(object? obj) => Equals(obj as ProjectInput);
        public override int GetHashCode() => (Epoch + "\0" + Identity + "\0" + LanguageVersion + "\0" + Options + "\0" + Defines).GetHashCode();
    }
}
