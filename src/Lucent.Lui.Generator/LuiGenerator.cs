using System;
using System.Linq;
using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Lucent.Lui.Generator;

/// <summary>Build-time incremental generator that lowers <c>.lui</c> additional files into Lucent component recipe C#.</summary>
/// <remarks>The analyzer runs only during compilation, honors cancellation and freshness checks, reports authored diagnostics, and adds no runtime dependency to consuming applications.</remarks>
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

    /// <summary>Registers incremental parsing, sibling indexing, binding, diagnostics, and stale-output-safe publication steps.</summary>
    /// <param name="context">Roslyn initialization context supplied during analyzer setup.</param>
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
                    LuiProjectComponentIndex.Build(
                        input.Left,
                        input
                            .Right.Where(item => item.ProjectDocument is not null)
                            .Select(item => item.ProjectDocument!),
                        cancellationToken
                    )
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
            static (production, value) => ReportIndexDiagnostics(production, value)
        );
        context.RegisterSourceOutput(
            results.Select(static (input, _) => input).WithTrackingName("LuiPublication"),
            static (production, input) => Publish(production, input.Lowered, input.Current)
        );
    }

    private static void ReportInputDiagnostics(SourceProductionContext production, ParseInput input)
    {
        if (!input.IsReadable)
            Report(
                production,
                InvalidInput,
                Location.Create(input.Path, new TextSpan(0, 0), new LinePositionSpan()),
                LuiDiagnosticProjection.Unreadable(input.Path),
                input.Path
            );
        else if (!input.IsLogicalPathValid)
            Report(
                production,
                InvalidLogicalPath,
                Location.Create(input.Path, new TextSpan(0, 0), new LinePositionSpan()),
                LuiDiagnosticProjection.InvalidLogicalPath(input.Path, input.LogicalPath),
                input.Path,
                input.LogicalPath
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
        LuiProjectComponentIndex index,
        Compilation compilation,
        ProjectInput project,
        System.Threading.CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!index.TryGet(input.Path, out _))
            return new DocumentResult(input, null);
        var augmented = index.Augment(compilation, input.Path);
        var identity = CurrentIdentity(input, index, augmented, project);
        var result = LuiCompiler.Compile(input.Document!, augmented, identity);
        cancellationToken.ThrowIfCancellationRequested();
        return new DocumentResult(input, result);
    }

    private static CurrentDocument Current(
        ParseInput input,
        LuiProjectComponentIndex index,
        Compilation compilation,
        ProjectInput project,
        System.Threading.CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!index.TryGet(input.Path, out _))
            return new CurrentDocument(input, null);
        var augmented = index.Augment(compilation, input.Path);
        return new CurrentDocument(input, CurrentIdentity(input, index, augmented, project));
    }

    private static LuiFreshnessIdentity CurrentIdentity(
        ParseInput input,
        LuiProjectComponentIndex index,
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
                project.Defines,
                project.RootNamespace
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

    private static void ReportIndexDiagnostics(
        SourceProductionContext production,
        LuiProjectComponentIndex index
    )
    {
        foreach (var diagnostic in index.Diagnostics)
        {
            production.CancellationToken.ThrowIfCancellationRequested();
            var input = new ParseInput(diagnostic.Document);
            var descriptor = diagnostic.Kind switch
            {
                LuiProjectComponentIndex.DiagnosticKind.DuplicateLogicalPath => DuplicateInput,
                LuiProjectComponentIndex.DiagnosticKind.DuplicateComponent => DuplicateComponent,
                _ => InvalidSibling,
            };
            var projection = LuiDiagnosticProjection.Index(diagnostic);
            Report(
                production,
                descriptor,
                input.Location(diagnostic.Span),
                projection,
                descriptor == DuplicateInput
                    ? new object[] { input.Path, diagnostic.Value }
                    : new object[] { diagnostic.Value }
            );
        }
    }

    private static void Report(
        SourceProductionContext production,
        DiagnosticDescriptor descriptor,
        Location location,
        LuiDiagnostic projection,
        params object[] arguments
    ) => production.ReportDiagnostic(Diagnostic.Create(descriptor, location, arguments));

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

    private static DiagnosticDescriptor ParseDescriptor(LuiDiagnostic diagnostic) =>
        new DiagnosticDescriptor(
            diagnostic.Id,
            "Invalid .lui syntax",
            "{0}",
            diagnostic.Source,
            diagnostic.Severity,
            true
        );

    private sealed class ParseInput : IEquatable<ParseInput>
    {
        internal ParseInput(LuiProjectDocument document)
            : this(
                document.Path,
                document.LogicalPath,
                document.Source,
                document.Version,
                SourceText.From(document.Source),
                true,
                true
            ) { }

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
            ProjectDocument =
                readable && logicalPathValid
                    ? new LuiProjectDocument(path, logicalPath, source, documentVersion)
                    : null;
            Document = ProjectDocument?.Syntax;
        }

        public string Path { get; }
        public string LogicalPath { get; }
        public string Source { get; }
        public string DocumentVersion { get; }
        public SourceText? SourceText { get; }
        public bool IsReadable { get; }
        public bool IsLogicalPathValid { get; }
        public LuiProjectDocument? ProjectDocument { get; }
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
            string defines,
            string rootNamespace
        )
        {
            Epoch = epoch;
            Identity = identity;
            LanguageVersion = languageVersion;
            Options = options;
            Defines = defines;
            RootNamespace = rootNamespace;
        }

        public string Epoch { get; }
        public string Identity { get; }
        public string LanguageVersion { get; }
        public string Options { get; }
        public string Defines { get; }
        public string RootNamespace { get; }

        public static ProjectInput Read(AnalyzerConfigOptions options)
        {
            options.TryGetValue("build_property.LucentLuiProjectEpoch", out var epoch);
            options.TryGetValue("build_property.LucentLuiProjectIdentity", out var identity);
            options.TryGetValue("build_property.LucentLuiLangVersion", out var languageVersion);
            options.TryGetValue("build_property.LucentLuiCompilerOptions", out var compilerOptions);
            options.TryGetValue("build_property.LucentLuiDefines", out var defines);
            options.TryGetValue("build_property.RootNamespace", out var rootNamespace);
            return new ProjectInput(
                epoch ?? "",
                identity ?? "",
                languageVersion ?? "",
                compilerOptions ?? "",
                defines ?? "",
                rootNamespace ?? ""
            );
        }

        public bool Equals(ProjectInput? other) =>
            other is not null
            && Epoch == other.Epoch
            && Identity == other.Identity
            && LanguageVersion == other.LanguageVersion
            && Options == other.Options
            && Defines == other.Defines
            && RootNamespace == other.RootNamespace;

        public override bool Equals(object? obj) => Equals(obj as ProjectInput);

        public override int GetHashCode() =>
            (
                Epoch
                + "\0"
                + Identity
                + "\0"
                + LanguageVersion
                + "\0"
                + Options
                + "\0"
                + Defines
                + "\0"
                + RootNamespace
            ).GetHashCode();
    }
}
