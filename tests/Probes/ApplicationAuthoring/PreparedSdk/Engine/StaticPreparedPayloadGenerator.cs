using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Lucent.ApplicationAuthoring.PreparedSdk;

public sealed class StaticPreparedPayloadGenerator(PreparedPayload payload) : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#pragma warning disable RSEXPERIMENTAL007
        context.RegisterPreCompilationSourceOutput(
            context.ParseOptionsProvider,
            (production, _) =>
            {
                foreach (var source in payload.EarlyDeclarations)
                {
                    production.CancellationToken.ThrowIfCancellationRequested();
                    production.AddSource(
                        source.HintName,
                        SourceText.From(source.Source, Encoding.UTF8)
                    );
                }
            }
        );
#pragma warning restore RSEXPERIMENTAL007

        context.RegisterSourceOutput(
            context.CompilationProvider,
            (production, _) => Add(production, payload.FinalSources)
        );
    }

    private static void Add(
        SourceProductionContext production,
        ImmutableArray<PreparedSource> sources
    )
    {
        foreach (var source in sources)
        {
            production.CancellationToken.ThrowIfCancellationRequested();
            production.AddSource(source.HintName, SourceText.From(source.Source, Encoding.UTF8));
        }
    }
}
