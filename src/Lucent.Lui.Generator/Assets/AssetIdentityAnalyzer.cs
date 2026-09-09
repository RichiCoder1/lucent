using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lucent.Lui.Generator.Assets;

/// <summary>Rejects ambiguous asset providers using generated assembly metadata only.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AssetIdentityAnalyzer : DiagnosticAnalyzer
{
    private const string MetadataKey = "Lucent.Asset.v1";
    private static readonly DiagnosticDescriptor Conflict = new DiagnosticDescriptor(
        "LUIA0008",
        "Conflicting packaged asset providers",
        "Asset '{0}/{1}' has different packaged revisions in '{2}' and '{3}'",
        "Lucent.Assets",
        DiagnosticSeverity.Error,
        true,
        customTags: [WellKnownDiagnosticTags.CompilationEnd]
    );
    private static readonly DiagnosticDescriptor CaseConflict = new DiagnosticDescriptor(
        "LUIA0009",
        "Case-ambiguous packaged asset providers",
        "Asset identities '{0}/{1}' and '{2}/{3}' differ only by case in '{4}' and '{5}'",
        "Lucent.Assets",
        DiagnosticSeverity.Error,
        true,
        customTags: [WellKnownDiagnosticTags.CompilationEnd]
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [Conflict, CaseConflict];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics
        );
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(Analyze);
    }

    private static void Analyze(CompilationAnalysisContext context)
    {
        var providers = new Dictionary<AssetIdentity, Provider>();
        var assemblies = context.Compilation.SourceModule.ReferencedAssemblySymbols.Append(
            context.Compilation.Assembly
        );
        foreach (var assembly in assemblies)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            foreach (var attribute in assembly.GetAttributes())
            {
                if (!TryRead(attribute, assembly.Name, out var identity, out var provider))
                    continue;
                if (providers.TryGetValue(identity, out var prior))
                {
                    var location = attribute
                        .ApplicationSyntaxReference?.GetSyntax(context.CancellationToken)
                        .GetLocation();
                    if (!prior.Identity.ExactlyEquals(identity))
                        context.ReportDiagnostic(
                            Diagnostic.Create(
                                CaseConflict,
                                location,
                                prior.Identity.Domain,
                                prior.Identity.Path,
                                identity.Domain,
                                identity.Path,
                                prior.Assembly,
                                provider.Assembly
                            )
                        );
                    else if (!string.Equals(prior.Hash, provider.Hash, StringComparison.Ordinal))
                        context.ReportDiagnostic(
                            Diagnostic.Create(
                                Conflict,
                                location,
                                identity.Domain,
                                identity.Path,
                                prior.Assembly,
                                provider.Assembly
                            )
                        );
                }
                else
                    providers[identity] = provider;
            }
        }
    }

    private static bool TryRead(
        AttributeData attribute,
        string assembly,
        out AssetIdentity identity,
        out Provider provider
    )
    {
        identity = default;
        provider = default;
        if (
            attribute.AttributeClass?.ToDisplayString()
                != "System.Reflection.AssemblyMetadataAttribute"
            || attribute.ConstructorArguments.Length != 2
            || attribute.ConstructorArguments[0].Value is not string key
            || key != MetadataKey
            || attribute.ConstructorArguments[1].Value is not string value
        )
            return false;
        var fields = value.Split('|');
        if (fields.Length != 3 || fields[2].Length != 64)
            return false;
        try
        {
            identity = new(Uri.UnescapeDataString(fields[0]), Uri.UnescapeDataString(fields[1]));
            provider = new(identity, assembly, fields[2]);
            return true;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private readonly struct AssetIdentity : IEquatable<AssetIdentity>
    {
        public AssetIdentity(string domain, string path) => (Domain, Path) = (domain, path);

        public string Domain { get; }
        public string Path { get; }

        public bool Equals(AssetIdentity other) =>
            string.Equals(Domain, other.Domain, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);

        public bool ExactlyEquals(AssetIdentity other) =>
            Domain == other.Domain && Path == other.Path;

        public override bool Equals(object? obj) => obj is AssetIdentity other && Equals(other);

        public override int GetHashCode() =>
            unchecked(
                (StringComparer.OrdinalIgnoreCase.GetHashCode(Domain) * 397)
                ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Path)
            );
    }

    private readonly struct Provider
    {
        public Provider(AssetIdentity identity, string assembly, string hash) =>
            (Identity, Assembly, Hash) = (identity, assembly, hash);

        public AssetIdentity Identity { get; }
        public string Assembly { get; }
        public string Hash { get; }
    }
}
