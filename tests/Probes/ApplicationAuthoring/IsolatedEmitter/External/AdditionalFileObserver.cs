using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Lucent.ApplicationAuthoring.IsolatedEmitter;

[Generator]
public sealed class AdditionalFileObserver : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor TransportLeak = new(
        "PROBE2001",
        "Preparation source was transported as an AdditionalText",
        "The final generator saw a captured binding source in its AdditionalTexts",
        "IsolatedEmitter",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var fingerprint = context
            .AdditionalTextsProvider.Collect()
            .Select(
                (texts, cancellationToken) =>
                {
                    var entries = texts
                        .Select(text =>
                        {
                            var content = text.GetText(cancellationToken)?.ToString() ?? "";
                            var name = System.IO.Path.GetFileName(text.Path);
                            var hash = Hex(
                                SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(content))
                            );
                            return name + ":" + hash;
                        })
                        .OrderBy(entry => entry, StringComparer.Ordinal);
                    return String.Join("|", entries);
                }
            );

        context.RegisterSourceOutput(
            fingerprint,
            (production, value) =>
            {
                if (value.IndexOf(".binding.g.cs:", StringComparison.OrdinalIgnoreCase) >= 0)
                    production.ReportDiagnostic(Diagnostic.Create(TransportLeak, Location.None));

                production.AddSource(
                    "A0ExternalAdditionalFileObservation.g.cs",
                    SourceText.From(
                        "namespace Fixture; internal static class ExternalObservation { "
                            + "public const string Fingerprint = \""
                            + Escape(value)
                            + "\"; }",
                        Encoding.UTF8
                    )
                );
            }
        );
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");

    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "");
}
