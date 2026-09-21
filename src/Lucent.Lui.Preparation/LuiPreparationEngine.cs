using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Lucent.Lui.Preparation;

public static class LuiPreparationEngine
{
    /// <summary>Gets the deterministic generated hint name for one document's ordinary declarations.</summary>
    public static string DeclarationsHintName(string logicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalPath);
        return Hint("Declarations", logicalPath);
    }

    /// <summary>Gets the deterministic generated hint name for one document's early named-component declaration.</summary>
    public static string ComponentDeclarationsHintName(string logicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalPath);
        return Hint("Component", logicalPath);
    }

    public static LuiPreparationResult Prepare(
        LuiPreparationRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        var projections = request
            .Documents.Select(document =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ProjectedDocument(
                    document,
                    LuiAuthoredSourceProjection.Project(document.Source)
                );
            })
            .ToArray();
        var configurations = projections
            .Select(item => ResolveConfiguration(request, item.Document))
            .ToArray();
        var projectionDiagnostics = projections
            .SelectMany(item =>
                item.Projection.Diagnostics.Select(diagnostic => new LuiPreparationDiagnostic(
                    diagnostic.FilePath ?? item.Document.PhysicalPath,
                    diagnostic
                ))
            )
            .ToImmutableArray();
        var configurationDiagnostics = configurations
            .SelectMany(resolution => resolution.Diagnostics)
            .GroupBy(
                diagnostic =>
                    diagnostic.FilePath
                    + "\0"
                    + diagnostic.Line
                    + "\0"
                    + diagnostic.Id
                    + "\0"
                    + diagnostic.Message,
                StringComparer.Ordinal
            )
            .Select(group =>
            {
                var diagnostic = group.First();
                return new LuiPreparationDiagnostic(
                    diagnostic.FilePath,
                    new LuiDiagnostic(
                        diagnostic.Id,
                        diagnostic.Message,
                        new LuiSpan(0, 0),
                        DiagnosticSeverity.Error,
                        filePath: diagnostic.FilePath
                    ),
                    diagnostic.Line,
                    1
                );
            })
            .ToImmutableArray();
        var initialDiagnostics = projectionDiagnostics.AddRange(configurationDiagnostics);
        if (projections.Any(item => !item.Projection.Success))
            return new LuiPreparationResult(
                false,
                new LuiPreparedPayload([], []),
                [],
                [],
                initialDiagnostics,
                request.Compilation,
                projections
                    .Select(item => new LuiPreparedDocumentResult(
                        item.Document,
                        item.Projection,
                        null
                    ))
                    .ToImmutableArray(),
                null
            );

        var earlySources = projections.SelectMany(EarlySources).ToImmutableArray();
        var canReuse = CanReuse(request, earlySources);
        var generators = canReuse
            ? request.PreviousDriverState!.Generators
            : new[] { new StaticPreparedPayloadGenerator(earlySources).AsSourceGenerator() }
                .Concat(request.ForeignGenerators.Select(item => item.Generator))
                .ToImmutableArray();
        var driver = canReuse
            ? request
                .PreviousDriverState!.Driver.WithUpdatedAnalyzerConfigOptions(
                    request.OptionsProvider
                )
                .ReplaceAdditionalTexts(request.AdditionalTexts)
                .WithUpdatedParseOptions(request.ParseOptions)
            : CSharpGeneratorDriver.Create(
                generators,
                request.AdditionalTexts,
                request.ParseOptions,
                request.OptionsProvider,
                request.DriverOptions
            );
        driver = driver.RunGenerators(request.Compilation, cancellationToken);
        var run = driver.GetRunResult();
        var failures = run.Results.Where(result => result.Exception is not null).ToArray();
        if (failures.Length != 0)
            throw new LuiPreparationException(
                "Preparatory generator execution failed: "
                    + String.Join(
                        " | ",
                        failures.Select(result =>
                            result.Generator.GetType().FullName + ": " + result.Exception!.Message
                        )
                    )
            );
        var foreignOutputs = run
            .Results.Skip(1)
            .SelectMany(
                (result, ordinal) =>
                    result.GeneratedSources.Select(source =>
                    {
                        var text = source.SourceText.ToString();
                        return new LuiPreparedForeignOutput(
                            request.ForeignGenerators[ordinal].StableIdentity,
                            Normalize(source.SyntaxTree.FilePath),
                            Hash(text)
                        );
                    })
            )
            .ToImmutableArray();
        var foreignTrees = run
            .Results.Skip(1)
            .SelectMany(result => result.GeneratedSources.Select(source => source.SyntaxTree))
            .ToImmutableArray();
        var bindingTrees = earlySources
            .Select(source =>
                CSharpSyntaxTree.ParseText(
                    source.Source,
                    request.ParseOptions,
                    source.HintName,
                    cancellationToken: cancellationToken
                )
            )
            .Concat(foreignTrees);
        var bindingCompilation = request.Compilation.AddSyntaxTrees(bindingTrees);
        var driverState = new LuiPreparationDriverState(
            driver,
            generators,
            request.ForeignGenerators.Select(GeneratorIdentity).ToImmutableArray(),
            earlySources,
            request.DriverOptions
        );
        if (run.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            return new LuiPreparationResult(
                false,
                new LuiPreparedPayload(earlySources, []),
                foreignOutputs,
                run.Diagnostics,
                projectionDiagnostics,
                bindingCompilation,
                projections
                    .Select(item => new LuiPreparedDocumentResult(
                        item.Document,
                        item.Projection,
                        null
                    ))
                    .ToImmutableArray(),
                driverState
            );
        var finalSources = ImmutableArray.CreateBuilder<LuiPreparedSource>();
        var refinedComponentSources = ImmutableArray.CreateBuilder<LuiPreparedSource>();
        var loweringDiagnostics = ImmutableArray.CreateBuilder<LuiPreparationDiagnostic>();
        var documents = ImmutableArray.CreateBuilder<LuiPreparedDocumentResult>();
        for (var index = 0; index < projections.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = projections[index];
            if (item.Projection.Document.Component is null)
            {
                documents.Add(new LuiPreparedDocumentResult(item.Document, item.Projection, null));
                continue;
            }
            var identity = new LuiFreshnessIdentity(
                request.ProjectEpoch,
                request.ProjectIdentity,
                new LuiDocumentIdentity(item.Document.LogicalPath),
                item.Document.Version,
                "",
                "",
                request.LanguageVersion,
                typeof(LuiCompiler).Assembly.GetName().Version?.ToString() ?? "",
                "",
                "",
                request.CompilerOptions,
                request.Defines,
                request.RootNamespace
            );
            var lowered = LuiCompiler.CompileNamedComponent(
                item.Projection.Document,
                bindingCompilation,
                identity,
                item.Document.PhysicalPath
            );
            var lint = LuiLintAnalyzer.AnalyzeCompiled(
                item.Projection.Document,
                bindingCompilation,
                identity,
                lowered,
                new LuiLintOptions(configurations[index].DeclarationOrder),
                cancellationToken
            );
            loweringDiagnostics.AddRange(
                ApplyConfiguredSeverities(lint.Diagnostics, configurations[index].DiagnosticSeverities)
                    .Select(diagnostic =>
                        new LuiPreparationDiagnostic(
                            diagnostic.FilePath ?? item.Document.PhysicalPath,
                            diagnostic
                        )
                    )
            );
            documents.Add(new LuiPreparedDocumentResult(item.Document, item.Projection, lowered));
            if (!lowered.Success || lowered.Source is null)
                continue;
            if (String.IsNullOrWhiteSpace(lowered.PreparedComponentDeclaration))
                throw new LuiPreparationException(
                    $"Named component lowering did not provide a refined declaration for '{item.Document.LogicalPath}'."
                );
            refinedComponentSources.Add(
                new LuiPreparedSource(
                    ComponentDeclarationsHintName(item.Document.LogicalPath),
                    MapComponentSource(
                        lowered.PreparedComponentDeclaration!,
                        item.Document.PhysicalPath,
                        AuthoredLine(item)
                    ),
                    item.Document.PhysicalPath
                )
            );
            finalSources.Add(
                new LuiPreparedSource(
                    Hint("Final", item.Document.LogicalPath),
                    lowered.Source,
                    item.Document.PhysicalPath
                )
            );
        }
        var allLuiDiagnostics = initialDiagnostics.AddRange(loweringDiagnostics);
        var success = !allLuiDiagnostics.Any(item =>
            item.Diagnostic.Severity == DiagnosticSeverity.Error
        );
        var publishedEarlySources = success
            ? earlySources
                .Where(source =>
                    source.HintName.StartsWith("Lui.Declarations.", StringComparison.Ordinal)
                )
                .Concat(refinedComponentSources)
                .ToImmutableArray()
            : earlySources;
        var publishedBindingCompilation = success
            ? request.Compilation.AddSyntaxTrees(
                publishedEarlySources
                    .Select(source =>
                        CSharpSyntaxTree.ParseText(
                            source.Source,
                            request.ParseOptions,
                            source.HintName,
                            cancellationToken: cancellationToken
                        )
                    )
                    .Concat(foreignTrees)
            )
            : bindingCompilation;
        return new LuiPreparationResult(
            success,
            new LuiPreparedPayload(publishedEarlySources, finalSources.ToImmutable()),
            foreignOutputs,
            run.Diagnostics,
            allLuiDiagnostics,
            publishedBindingCompilation,
            documents.ToImmutable(),
            driverState
        );
    }

    private static bool CanReuse(
        LuiPreparationRequest request,
        ImmutableArray<LuiPreparedSource> earlySources
    )
    {
        var previous = request.PreviousDriverState;
        if (
            previous is null
            || !previous.Options.Equals(request.DriverOptions)
            || !previous.EarlySources.SequenceEqual(earlySources)
            || previous.Generators.Length != request.ForeignGenerators.Length + 1
            || !previous.ForeignGeneratorIdentities.SequenceEqual(
                request.ForeignGenerators.Select(GeneratorIdentity),
                StringComparer.Ordinal
            )
        )
            return false;
        return true;
    }

    private static LuiEditorConfigResolution ResolveConfiguration(
        LuiPreparationRequest request,
        LuiPreparationDocument document
    ) => request.EditorConfigs.IsDefault
        ? LuiEditorConfigResolver.Resolve(document.PhysicalPath)
        : LuiEditorConfigResolver.Resolve(document.PhysicalPath, request.EditorConfigs);

    private static IEnumerable<LuiDiagnostic> ApplyConfiguredSeverities(
        IEnumerable<LuiDiagnostic> diagnostics,
        IReadOnlyDictionary<string, ReportDiagnostic> configuredSeverities
    )
    {
        foreach (var diagnostic in diagnostics)
        {
            if (
                !configuredSeverities.TryGetValue(diagnostic.Id, out var configured)
                || configured == ReportDiagnostic.Default
            )
            {
                yield return diagnostic;
                continue;
            }
            if (configured == ReportDiagnostic.Suppress)
                continue;
            var severity = configured switch
            {
                ReportDiagnostic.Error => DiagnosticSeverity.Error,
                ReportDiagnostic.Warn => DiagnosticSeverity.Warning,
                ReportDiagnostic.Info => DiagnosticSeverity.Info,
                ReportDiagnostic.Hidden => DiagnosticSeverity.Hidden,
                _ => diagnostic.Severity,
            };
            yield return severity == diagnostic.Severity
                ? diagnostic
                : new LuiDiagnostic(
                    diagnostic.Id,
                    diagnostic.Message,
                    diagnostic.Span,
                    severity,
                    diagnostic.Source,
                    diagnostic.FilePath
                );
        }
    }

    private static string GeneratorIdentity(LuiPreparationGenerator generator) =>
        generator.StableIdentity
        + "\0"
        + generator.AnalyzerSha256
        + "\0"
        + generator.AnalyzerGeneratorOrdinal.ToString(
            System.Globalization.CultureInfo.InvariantCulture
        );

    private static IEnumerable<LuiPreparedSource> EarlySources(ProjectedDocument item)
    {
        if (!String.IsNullOrWhiteSpace(item.Projection.DeclarationsSource))
            yield return new LuiPreparedSource(
                DeclarationsHintName(item.Document.LogicalPath),
                MapAuthoredSource(
                    item.Projection.DeclarationsSource,
                    item.Document.PhysicalPath,
                    1
                ),
                item.Document.PhysicalPath
            );
        if (!String.IsNullOrWhiteSpace(item.Projection.EarlyComponentDeclaration))
            yield return new LuiPreparedSource(
                ComponentDeclarationsHintName(item.Document.LogicalPath),
                MapComponentSource(
                    item.Projection.EarlyComponentDeclaration!,
                    item.Document.PhysicalPath,
                    AuthoredLine(item)
                ),
                item.Document.PhysicalPath
            );
    }

    private static string MapAuthoredSource(string source, string physicalPath, int line) =>
        "#nullable enable\n#line "
        + line.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + " \""
        + physicalPath
        + "\"\n"
        + source
        + (source.EndsWith('\n') ? String.Empty : "\n")
        + "#line default\n";

    private static string MapComponentSource(string source, string physicalPath, int line)
    {
        var directive =
            "#line "
            + line.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " \""
            + physicalPath
            + "\"\n";
        var builder = new StringBuilder("#nullable enable\n#line hidden\n");
        foreach (var text in source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = text.TrimStart();
            if (
                trimmed.StartsWith("public sealed partial class ", StringComparison.Ordinal)
                || trimmed.StartsWith("internal sealed partial class ", StringComparison.Ordinal)
                || trimmed.StartsWith("public static partial ", StringComparison.Ordinal)
                || trimmed.StartsWith("partial void Setup(", StringComparison.Ordinal)
            )
                builder.Append(directive);
            builder.AppendLine(text);
        }
        return builder.AppendLine("#line default").ToString();
    }

    private static int AuthoredLine(ProjectedDocument item)
    {
        var start = item.Projection.Document.Component?.Span.Start ?? 0;
        var line = 1;
        for (var index = 0; index < start && index < item.Document.Source.Length; index++)
            if (item.Document.Source[index] == '\n')
                line++;
        return line;
    }

    private static string Hint(string kind, string logicalPath) =>
        "Lui."
        + kind
        + "."
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(logicalPath)))[..16]
        + ".g.cs";

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string Hash(string source) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));

    private sealed record ProjectedDocument(
        LuiPreparationDocument Document,
        LuiAuthoredSourceProjection Projection
    );
}

internal sealed class StaticPreparedPayloadGenerator(ImmutableArray<LuiPreparedSource> earlySources)
    : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#pragma warning disable RSEXPERIMENTAL007
        context.RegisterPreCompilationSourceOutput(
            context.ParseOptionsProvider,
            (production, _) =>
            {
                foreach (var source in earlySources)
                    production.AddSource(
                        source.HintName,
                        SourceText.From(source.Source, Encoding.UTF8)
                    );
            }
        );
#pragma warning restore RSEXPERIMENTAL007
    }
}
