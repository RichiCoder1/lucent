using System;
using System.Collections.Generic;
using System.Globalization;
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
    private static readonly string[] ConfigurableDiagnosticIds = Enumerable
        .Range(1000, 26)
        .Concat(Enumerable.Range(2000, 38))
        .Concat(Enumerable.Range(3000, 5))
        .Concat(Enumerable.Range(4001, 5))
        .Concat(Enumerable.Range(5001, 7))
        .Concat(Enumerable.Range(6000, 4))
        .Append(6100)
        .Append(6102)
        .Select(id => "LUI" + id.ToString(CultureInfo.InvariantCulture))
        .ToArray();
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
        var configurations = context
            .AdditionalTextsProvider.Where(static text =>
                String.Equals(
                    System.IO.Path.GetFileName(text.Path),
                    ".editorconfig",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(
                static (input, cancellationToken) =>
                    ConfigurationInput.Read(
                        input.Left,
                        input.Right.GetOptions(input.Left),
                        cancellationToken
                    )
            )
            .Where(static input => input.IsTransported)
            .Collect()
            .WithTrackingName("LuiConfiguration");
        var inputs = context
            .AdditionalTextsProvider.Where(static text =>
                text.Path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase)
            )
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Combine(configurations)
            .Select(
                static (input, cancellationToken) =>
                    ParseInput.Read(
                        input.Left.Left,
                        input.Left.Right.GetOptions(input.Left.Left),
                        input.Right,
                        cancellationToken
                    )
            )
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Where(static input =>
                !(
                    input.Right.GlobalOptions.TryGetValue(
                        "build_property.LucentLuiPreparedAuthoring",
                        out var value
                    ) && String.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                )
            )
            .Select(static (input, _) => input.Left)
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
        {
            foreach (var diagnostic in input.Document!.Diagnostics)
                ReportConfiguredDiagnostic(production, input, diagnostic);
            if (
                input.Document.Diagnostics.FirstOrDefault(diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error
                ) is
                { } firstError
            )
                ReportConfiguredDiagnostic(
                    production,
                    input,
                    new LuiDiagnostic(
                        LuiLintCatalog.AnalysisUnavailable,
                        "Lint analysis is incomplete because the document has syntax errors.",
                        firstError.Span
                    )
                );
            if (input.LintConfigurationDiagnostic is { } lintConfiguration)
                production.ReportDiagnostic(
                    Diagnostic.Create(
                        ParseDescriptor(lintConfiguration, ReportDiagnostic.Error),
                        input.Location(lintConfiguration.Span),
                        lintConfiguration.Message
                    )
                );
        }
    }

    private static void ReportConfiguredDiagnostic(
        SourceProductionContext production,
        ParseInput input,
        LuiDiagnostic diagnostic
    )
    {
        input.LintDiagnosticSeverities.TryGetValue(diagnostic.Id, out var configuredSeverity);
        if (configuredSeverity == ReportDiagnostic.Suppress)
            return;
        production.ReportDiagnostic(
            Diagnostic.Create(
                ParseDescriptor(diagnostic, configuredSeverity),
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
        var result = LuiCompiler.Compile(input.Document!, augmented, identity, input.Path);
        var lint = LuiLintAnalyzer.AnalyzeCompiled(
            input.Document!,
            augmented,
            identity,
            result,
            new LuiLintOptions(input.DeclarationOrder),
            cancellationToken
        );
        cancellationToken.ThrowIfCancellationRequested();
        return new DocumentResult(input, result, lint);
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
        var shouldPublish = ShouldPublish(lowered.Result, current.Identity);
        foreach (var diagnostic in lowered.Lint?.Diagnostics ?? lowered.Result.Diagnostics)
        {
            production.CancellationToken.ThrowIfCancellationRequested();
            if (shouldPublish && diagnostic.Source != "Lucent.Lui")
                continue;
            ReportConfiguredDiagnostic(production, lowered.Input, diagnostic);
        }
        if (shouldPublish)
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
        internal DocumentResult(
            ParseInput input,
            LuiCompilationResult? result,
            LuiLintResult? lint = null
        )
        {
            Input = input;
            Result = result;
            Lint = lint;
        }

        internal ParseInput Input { get; }
        internal LuiCompilationResult? Result { get; }
        internal LuiLintResult? Lint { get; }
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

    private static DiagnosticDescriptor ParseDescriptor(
        LuiDiagnostic diagnostic,
        ReportDiagnostic configuredSeverity = ReportDiagnostic.Default
    ) =>
        new DiagnosticDescriptor(
            diagnostic.Id,
            "Invalid .lui syntax",
            "{0}",
            diagnostic.Source,
            configuredSeverity switch
            {
                ReportDiagnostic.Error => DiagnosticSeverity.Error,
                ReportDiagnostic.Warn => DiagnosticSeverity.Warning,
                ReportDiagnostic.Info => DiagnosticSeverity.Info,
                ReportDiagnostic.Hidden => DiagnosticSeverity.Hidden,
                _ => diagnostic.Severity,
            },
            true
        );

    private sealed class ConfigurationInput : IEquatable<ConfigurationInput>
    {
        private ConfigurationInput(string path, string? source, bool isTransported)
        {
            Path = path;
            Source = source;
            IsTransported = isTransported;
        }

        internal string Path { get; }
        internal string? Source { get; }
        internal bool IsTransported { get; }

        internal static ConfigurationInput Read(
            AdditionalText text,
            AnalyzerConfigOptions options,
            System.Threading.CancellationToken cancellationToken
        )
        {
            var transported =
                options.TryGetValue(
                    "build_metadata.AdditionalFiles.LucentLuiEditorConfig",
                    out var marker
                ) && String.Equals(marker, "true", StringComparison.OrdinalIgnoreCase);
            return new ConfigurationInput(
                System.IO.Path.GetFullPath(text.Path),
                text.GetText(cancellationToken)?.ToString(),
                transported
            );
        }

        public bool Equals(ConfigurationInput? other) =>
            other is not null
            && StringComparer.OrdinalIgnoreCase.Equals(Path, other.Path)
            && Source == other.Source
            && IsTransported == other.IsTransported;

        public override bool Equals(object? obj) => Equals(obj as ConfigurationInput);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(Path);
                hash = (hash * 397) ^ (Source?.GetHashCode() ?? 0);
                return (hash * 397) ^ IsTransported.GetHashCode();
            }
        }
    }

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
                true,
                LuiDeclarationOrder.None,
                new Dictionary<string, ReportDiagnostic>(StringComparer.OrdinalIgnoreCase),
                null
            ) { }

        private ParseInput(
            string path,
            string logicalPath,
            string source,
            string documentVersion,
            SourceText? sourceText,
            bool readable,
            bool logicalPathValid,
            LuiDeclarationOrder declarationOrder,
            IReadOnlyDictionary<string, ReportDiagnostic> lintDiagnosticSeverities,
            LuiDiagnostic? lintConfigurationDiagnostic
        )
        {
            Path = path;
            LogicalPath = logicalPath;
            Source = source;
            DocumentVersion = documentVersion;
            SourceText = sourceText;
            IsReadable = readable;
            IsLogicalPathValid = logicalPathValid;
            DeclarationOrder = declarationOrder;
            LintDiagnosticSeverities = lintDiagnosticSeverities;
            LintConfigurationDiagnostic = lintConfigurationDiagnostic;
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
        public LuiDeclarationOrder DeclarationOrder { get; }
        public IReadOnlyDictionary<string, ReportDiagnostic> LintDiagnosticSeverities { get; }
        public LuiDiagnostic? LintConfigurationDiagnostic { get; }
        public LuiProjectDocument? ProjectDocument { get; }
        public LuiDocumentSyntax? Document { get; }

        public static ParseInput Read(
            AdditionalText text,
            AnalyzerConfigOptions options,
            IReadOnlyList<ConfigurationInput> configurations,
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
            var declarationOrder = LuiDeclarationOrder.None;
            var lintDiagnosticSeverities = new Dictionary<string, ReportDiagnostic>(
                StringComparer.OrdinalIgnoreCase
            );
            LuiDiagnostic? lintConfigurationDiagnostic = null;
            var applicableConfigurations = configurations
                .Where(configuration => IsConfigurationFor(configuration.Path, text.Path))
                .ToArray();
            var unreadableConfiguration = applicableConfigurations.FirstOrDefault(configuration =>
                configuration.Source is null
            );
            if (unreadableConfiguration is not null)
                lintConfigurationDiagnostic = new LuiDiagnostic(
                    "LUI6100",
                    "The transported EditorConfig file '"
                        + unreadableConfiguration.Path
                        + "' is unreadable.",
                    new LuiSpan(0, 0)
                );
            else
            {
                var resolution = LuiEditorConfigResolver.Resolve(
                    text.Path,
                    applicableConfigurations.Select(configuration => new LuiEditorConfigSnapshot(
                        configuration.Path,
                        configuration.Source!
                    ))
                );
                declarationOrder = resolution.DeclarationOrder;
                foreach (var severity in resolution.DiagnosticSeverities)
                    lintDiagnosticSeverities[severity.Key] = severity.Value;
                if (resolution.Diagnostics.Count != 0)
                {
                    var diagnostic = resolution.Diagnostics[0];
                    lintConfigurationDiagnostic ??= new LuiDiagnostic(
                        diagnostic.Id,
                        diagnostic.Message
                            + " ("
                            + diagnostic.FilePath
                            + ":"
                            + diagnostic.Line
                            + ")",
                        new LuiSpan(0, 0)
                    );
                }
            }
            if (options.TryGetValue("lucent_lui_declaration_order", out var configuredOrder))
            {
                declarationOrder = configuredOrder?.Trim().ToLowerInvariant() switch
                {
                    null or "" or "none" or "unset" => LuiDeclarationOrder.None,
                    "component_first" => LuiDeclarationOrder.ComponentFirst,
                    "styles_first" => LuiDeclarationOrder.StylesFirst,
                    _ => LuiDeclarationOrder.None,
                };
                if (
                    configuredOrder is not null
                    && configuredOrder.Trim().Length != 0
                    && configuredOrder.Trim().ToLowerInvariant()
                        is not ("none" or "unset" or "component_first" or "styles_first")
                )
                    lintConfigurationDiagnostic ??= new LuiDiagnostic(
                        "LUI6102",
                        "Unsupported lucent_lui_declaration_order value '"
                            + configuredOrder
                            + "'; expected none, component_first, or styles_first.",
                        new LuiSpan(0, 0)
                    );
            }
            foreach (var diagnosticId in ConfigurableDiagnosticIds)
            {
                if (
                    !options.TryGetValue(
                        "dotnet_diagnostic." + diagnosticId + ".severity",
                        out var configuredSeverity
                    )
                )
                    continue;
                if (
                    LuiEditorConfigResolver.TryParseDiagnosticSeverity(
                        configuredSeverity,
                        out var parsedSeverity
                    )
                )
                    lintDiagnosticSeverities[diagnosticId] = parsedSeverity;
                else
                    lintConfigurationDiagnostic ??= new LuiDiagnostic(
                        "LUI6102",
                        "Unsupported severity '"
                            + configuredSeverity
                            + "' for "
                            + diagnosticId
                            + "; expected default, none, silent, suggestion, warning or error.",
                        new LuiSpan(0, 0)
                    );
            }
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
                    true,
                    declarationOrder,
                    lintDiagnosticSeverities,
                    lintConfigurationDiagnostic
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
                    false,
                    declarationOrder,
                    lintDiagnosticSeverities,
                    lintConfigurationDiagnostic
                );
            }
        }

        private static bool IsConfigurationFor(string configurationPath, string sourcePath)
        {
            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(sourcePath));
            while (!String.IsNullOrEmpty(directory))
            {
                if (
                    StringComparer.OrdinalIgnoreCase.Equals(
                        System.IO.Path.Combine(directory, ".editorconfig"),
                        configurationPath
                    )
                )
                    return true;
                directory = System.IO.Path.GetDirectoryName(directory);
            }
            return false;
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
            && IsLogicalPathValid == other.IsLogicalPathValid
            && DeclarationOrder == other.DeclarationOrder
            && LintDiagnosticSeverities.Count == other.LintDiagnosticSeverities.Count
            && LintDiagnosticSeverities.All(item =>
                other.LintDiagnosticSeverities.TryGetValue(item.Key, out var severity)
                && item.Value == severity
            )
            && StringComparer.Ordinal.Equals(
                LintConfigurationDiagnostic?.Message,
                other.LintConfigurationDiagnostic?.Message
            );

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
                + "\0"
                + DeclarationOrder
                + "\0"
                + String.Join(
                    "\0",
                    LintDiagnosticSeverities
                        .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(item => item.Key + "=" + item.Value)
                )
                + "\0"
                + LintConfigurationDiagnostic?.Message
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
