using System;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Lucent.ApplicationAuthoring.PreparedSdk;

[Generator]
public sealed class PreparedForeignGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor MissingPerson = new(
        "PREP1001",
        "Prepared declaration was not visible",
        "Fixture.Person was unavailable to the foreign generator",
        "PreparedSdk",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var inputs = context
            .AdditionalTextsProvider.Select(
                static (text, cancellationToken) =>
                {
                    var source = text.GetText(cancellationToken)?.ToString() ?? "";
                    return new Input(
                        System.IO.Path.GetFileName(text.Path),
                        source.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    );
                }
            )
            .Collect();
        var environment = context
            .CompilationProvider.Combine(context.AnalyzerConfigOptionsProvider)
            .Combine(inputs);
        context.RegisterSourceOutput(
            environment,
            static (production, value) =>
            {
                var compilation = value.Left.Left;
                var options = value.Left.Right;
                if (
                    compilation.GetTypeByMetadataName("Fixture.Person") is null
                    || compilation.GetTypeByMetadataName("Fixture.SupportMarker") is null
                )
                {
                    production.ReportDiagnostic(Diagnostic.Create(MissingPerson, Location.None));
                    return;
                }
                options.GlobalOptions.TryGetValue("build_property.PreparedFlavor", out var flavor);
                options.GlobalOptions.TryGetValue("build_property.RuntimeIdentifier", out var rid);
                options.GlobalOptions.TryGetValue(
                    "build_property.PreparedArbitrary",
                    out var arbitrary
                );
                options.GlobalOptions.TryGetValue(
                    "build_property.PreparedNondeterministic",
                    out var nondeterministic
                );
                var parse = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
                var symbols = String.Join(
                    ",",
                    parse is null
                        ? Enumerable.Empty<string>()
                        : parse.PreprocessorSymbolNames.OrderBy(x => x)
                );
                var files = String.Join(
                    "|",
                    value
                        .Right.OrderBy(input => input.Name, StringComparer.Ordinal)
                        .Select(input => input.Name + ":" + input.Hash)
                );
                var nonce = String.Equals(
                    nondeterministic,
                    "true",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? Guid.NewGuid().ToString("N")
                    : "stable";
                production.AddSource(
                    "PreparedForeign.g.cs",
                    SourceText.From(
                        "namespace Fixture; internal static class PreparedEnvironment { "
                            + "internal const string Flavor = \""
                            + Escape(flavor ?? "")
                            + "\"; internal const string Runtime = \""
                            + Escape(rid ?? "")
                            + "\"; internal const string Defines = \""
                            + Escape(symbols)
                            + "\"; internal const string Additional = \""
                            + Escape(files)
                            + "\"; internal const string Arbitrary = \""
                            + Escape(arbitrary ?? "")
                            + "\"; internal const string Nonce = \""
                            + nonce
                            + "\"; }",
                        Encoding.UTF8
                    )
                );
            }
        );
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private sealed class Input
    {
        internal Input(string name, string hash)
        {
            Name = name;
            Hash = hash;
        }

        internal string Name { get; }

        internal string Hash { get; }
    }
}
