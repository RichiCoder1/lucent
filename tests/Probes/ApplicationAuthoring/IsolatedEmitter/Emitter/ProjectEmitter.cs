using System;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Lucent.ApplicationAuthoring.IsolatedEmitter;

[Generator]
public sealed class ProjectEmitter : IIncrementalGenerator
{
    // These constants stand in for the host's content-addressed, project-specific
    // payload. The proof deliberately compiles them into an analyzer assembly rather
    // than transporting either source as an AdditionalText.
    private const string EarlyDeclarations = """
        #nullable enable
        namespace Fixture;
        public record Person(string Name);
        [System.Text.Json.Serialization.JsonSerializable(typeof(Person))]
        public partial class JsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
        """;

    private const string FinalImplementation = """
        #nullable enable
        namespace Fixture;
        public static class Application
        {
            public static string Create()
            {
                var value = System.Text.Json.JsonSerializer.Deserialize(
                    "{\"Name\":\"Ada\"}",
                    JsonContext.Default.Person
                )!;
                return System.Text.Json.JsonSerializer.Serialize(value, JsonContext.Default.Person);
            }
        }
        """;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#pragma warning disable RSEXPERIMENTAL007
        context.RegisterPreCompilationSourceOutput(
            context.AdditionalTextsProvider.Collect(),
            static (production, _) =>
                production.AddSource(
                    "A0ProjectEmitter.Declarations.g.cs",
                    SourceText.From(EarlyDeclarations, Encoding.UTF8)
                )
        );
#pragma warning restore RSEXPERIMENTAL007

        context.RegisterSourceOutput(
            context.CompilationProvider,
            static (production, _) =>
                production.AddSource(
                    "A0ProjectEmitter.Implementation.g.cs",
                    SourceText.From(FinalImplementation, Encoding.UTF8)
                )
        );
    }
}
