using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lucent.Lui.Preparation;

public static class LuiPreparedEmitterCompiler
{
    public static LuiPreparedEmitterArtifact Compile(
        LuiPreparedPayload payload,
        string outputDirectory,
        CancellationToken cancellationToken = default
    )
    {
        Validate(payload);
        var contentHash = ContentHash(payload);
        var assemblyName = "Lucent.PreparedEmitter." + contentHash[..24];
        var outputPath = Path.Combine(outputDirectory, assemblyName + ".dll");
        Directory.CreateDirectory(outputDirectory);

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var source = RenderEmitterSource(payload);
        var tree = CSharpSyntaxTree.ParseText(
            source,
            parseOptions,
            path: assemblyName + ".g.cs",
            cancellationToken: cancellationToken
        );
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            EmitterReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                deterministic: true,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
        using var image = new MemoryStream();
        var result = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!result.Success)
            throw new InvalidOperationException(
                "Prepared emitter compilation failed: "
                    + String.Join(
                        " | ",
                        result.Diagnostics.Where(diagnostic =>
                            diagnostic.Severity == DiagnosticSeverity.Error
                        )
                    )
            );

        var bytes = image.ToArray();
        var imageHash = Convert.ToHexString(SHA256.HashData(bytes));
        if (File.Exists(outputPath))
        {
            var existingHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(outputPath)));
            if (!String.Equals(existingHash, imageHash, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Content-addressed emitter collision at '{outputPath}'."
                );
        }
        else
        {
            var temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporaryPath, bytes);
                try
                {
                    File.Move(temporaryPath, outputPath, overwrite: false);
                }
                catch (IOException) when (File.Exists(outputPath))
                {
                    var concurrentHash = Convert.ToHexString(
                        SHA256.HashData(File.ReadAllBytes(outputPath))
                    );
                    if (!String.Equals(concurrentHash, imageHash, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            $"Content-addressed emitter collision at '{outputPath}'."
                        );
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
        return new LuiPreparedEmitterArtifact(outputPath, assemblyName, contentHash, imageHash);
    }

    private static void Validate(LuiPreparedPayload payload)
    {
        var names = payload
            .EarlyDeclarations.Concat(payload.FinalSources)
            .Select(source => source.HintName)
            .ToArray();
        if (names.Any(String.IsNullOrWhiteSpace))
            throw new ArgumentException(
                "Prepared payload hint names must be non-empty.",
                nameof(payload)
            );
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new ArgumentException(
                "Prepared payload hint names must be unique.",
                nameof(payload)
            );
    }

    private static string ContentHash(LuiPreparedPayload payload)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, typeof(Compilation).Assembly.FullName ?? "");
        Append(hash, typeof(CSharpCompilation).Assembly.FullName ?? "");
        Append(
            hash,
            typeof(LuiPreparedEmitterCompiler).Assembly.ManifestModule.ModuleVersionId.ToString("D")
        );
        Append(
            hash,
            Convert.ToHexString(
                SHA256.HashData(
                    File.ReadAllBytes(typeof(LuiPreparedEmitterCompiler).Assembly.Location)
                )
            )
        );
        foreach (var source in payload.EarlyDeclarations)
            AppendSource(hash, "early", source);
        foreach (var source in payload.FinalSources)
            AppendSource(hash, "final", source);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendSource(IncrementalHash hash, string phase, LuiPreparedSource source)
    {
        Append(hash, phase);
        Append(hash, source.HintName);
        Append(hash, Path.GetFullPath(source.SourcePath));
        Append(hash, source.Source);
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private static string RenderEmitterSource(LuiPreparedPayload payload) =>
        $$"""
            using System;
            using System.Text;
            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.Text;

            namespace Lucent.ApplicationAuthoring.Prepared;

            [Generator]
            public sealed class ProjectEmitter : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
            #pragma warning disable RSEXPERIMENTAL007
                    context.RegisterPreCompilationSourceOutput(
                        context.ParseOptionsProvider,
                        static (production, _) => AddEarly(production));
            #pragma warning restore RSEXPERIMENTAL007
                    context.RegisterSourceOutput(
                        context.CompilationProvider,
                        static (production, _) => AddFinal(production));
                }

            #pragma warning disable RSEXPERIMENTAL007
                private static void AddEarly(PreCompilationSourceProductionContext production)
                {
            {{RenderAdds(payload.EarlyDeclarations)}}
                }
            #pragma warning restore RSEXPERIMENTAL007

                private static void AddFinal(SourceProductionContext production)
                {
            {{RenderAdds(payload.FinalSources)}}
                }

                private static SourceText Decode(string value) =>
                    SourceText.From(Encoding.UTF8.GetString(Convert.FromBase64String(value)), Encoding.UTF8);
            }
            """;

    private static string RenderAdds(ImmutableArray<LuiPreparedSource> sources) =>
        String.Join(
            Environment.NewLine,
            sources.Select(source =>
                "        production.AddSource(\""
                + Escape(source.HintName)
                + "\", Decode(\""
                + Convert.ToBase64String(Encoding.UTF8.GetBytes(source.Source))
                + "\"));"
            )
        );

    private static string Escape(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static ImmutableArray<MetadataReference> EmitterReferences()
    {
        var paths =
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Append(typeof(Compilation).Assembly.Location)
                .Append(typeof(CSharpCompilation).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
            ?? throw new InvalidOperationException(
                "The runtime did not provide trusted platform references."
            );
        return paths
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }
}
