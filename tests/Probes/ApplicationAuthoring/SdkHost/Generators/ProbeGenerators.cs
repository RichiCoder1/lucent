using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Lucent.ApplicationAuthoring.SdkHost;

[Generator]
public sealed class ProbeProjectionGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var declarations = context.AdditionalTextsProvider.Where(static text =>
            text.Path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase)
        );
#pragma warning disable RSEXPERIMENTAL007
        context.RegisterPreCompilationSourceOutput(
            declarations,
            static (production, text) =>
                production.AddSource(
                    "ProbeDeclarations.g.cs",
                    SourceText.From(
                        "// PROBE_PROJECTION_OUTPUT\n"
                            + text.GetText(production.CancellationToken)!.ToString(),
                        Encoding.UTF8
                    )
                )
        );
#pragma warning restore RSEXPERIMENTAL007
    }
}

[Generator]
public sealed class ProbeExternalGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor MissingProjection = new(
        "PROBE1001",
        "Projected declaration unavailable",
        "The external generator cannot see Fixture.Person",
        "SdkHost",
        DiagnosticSeverity.Error,
        true
    );

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var options = context.AnalyzerConfigOptionsProvider.Select(
            static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue(
                    "build_property.ProbePreparation",
                    out var preparation
                );
                provider.GlobalOptions.TryGetValue(
                    "build_property.ProbeMismatchKind",
                    out var mismatch
                );
                return new ProbeOptions(
                    String.Equals(preparation, "true", StringComparison.OrdinalIgnoreCase),
                    mismatch ?? "none"
                );
            }
        );
        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(options),
            static (production, input) =>
            {
                if (input.Left.GetTypeByMetadataName("Fixture.Person") is null)
                {
                    production.ReportDiagnostic(Diagnostic.Create(MissingProjection, null));
                    return;
                }

                var emit = input.Right.MismatchKind switch
                {
                    "missing" => input.Right.IsPreparation,
                    "extra" => !input.Right.IsPreparation,
                    _ => true,
                };
                if (!emit)
                    return;
                var phase =
                    input.Right.MismatchKind == "changed"
                        ? input.Right.IsPreparation
                            ? "preparation"
                            : "final"
                        : "stable";
                production.AddSource(
                    "ProbeExternal.g.cs",
                    SourceText.From(
                        "// PROBE_EXTERNAL_OUTPUT\n"
                            + "namespace Fixture; public static class ExternalProbe { "
                            + "public const string Phase = \""
                            + phase
                            + "\"; public static Person Echo(Person value) => value; }",
                        Encoding.UTF8
                    )
                );
            }
        );
    }

    private sealed class ProbeOptions
    {
        internal ProbeOptions(bool isPreparation, string mismatchKind)
        {
            IsPreparation = isPreparation;
            MismatchKind = mismatchKind;
        }

        internal bool IsPreparation { get; }

        internal string MismatchKind { get; }
    }
}

[Generator]
public sealed class ProbeBindingGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor MissingGeneratedApi = new(
        "PROBE1002",
        "Binding source unavailable",
        "The final Lucent-style binding phase cannot see JsonContext.Default.Person",
        "SdkHost",
        DiagnosticSeverity.Error,
        true
    );

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var preparation = context.AnalyzerConfigOptionsProvider.Select(
            static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue(
                    "build_property.ProbePreparation",
                    out var value
                );
                return String.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            }
        );
        var bindingSources = context
            .AdditionalTextsProvider.Where(static text =>
                text.Path.EndsWith(".binding.g.cs", StringComparison.OrdinalIgnoreCase)
            )
            .Select(
                static (text, cancellationToken) =>
                    new BindingSource(text.Path, text.GetText(cancellationToken)!)
            )
            .Collect();
        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(bindingSources).Combine(preparation),
            static (production, input) =>
            {
                if (input.Right)
                    return;
                var parseOptions =
                    input.Left.Left.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions
                    ?? CSharpParseOptions.Default;
                var bindingTrees = input.Left.Right.Select(source =>
                    CSharpSyntaxTree.ParseText(source.Text, parseOptions, source.Path)
                );
                var binding = input.Left.Left.AddSyntaxTrees(bindingTrees);
                var contextType = binding.GetTypeByMetadataName("Fixture.JsonContext");
                var hasDefault = contextType?.GetMembers("Default").Length > 0;
                var hasPerson = contextType?.GetMembers("Person").Length > 0;
                if (!hasDefault || !hasPerson)
                {
                    production.ReportDiagnostic(Diagnostic.Create(MissingGeneratedApi, null));
                    return;
                }

                production.AddSource(
                    "ProbeApplication.g.cs",
                    SourceText.From(
                        "// PROBE_BINDING_OUTPUT\n"
                            + "namespace Fixture; public static class Application { "
                            + "public static string Create() { var value = "
                            + "System.Text.Json.JsonSerializer.Deserialize(\"{\\\"Name\\\":\\\"Ada\\\"}\", JsonContext.Default.Person)!; "
                            + "return System.Text.Json.JsonSerializer.Serialize(value, JsonContext.Default.Person); } }",
                        Encoding.UTF8
                    )
                );
            }
        );
    }

    private sealed class BindingSource
    {
        internal BindingSource(string path, SourceText text)
        {
            Path = path;
            Text = text;
        }

        internal string Path { get; }

        internal SourceText Text { get; }
    }
}
