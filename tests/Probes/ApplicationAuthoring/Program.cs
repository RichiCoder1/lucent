using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Lucent.Core;
using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

// Generator-phase mechanics only: these sources stand in for future LUI projections.
// This is not the complete A0 language/runtime/editor/package acceptance fixture.
try
{
    var jsonAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(
        Path.GetFullPath(args.Single())
    );
    var jsonType = jsonAssembly.GetTypes().Single(type => type.Name == "JsonSourceGenerator");
    ISourceGenerator JsonGenerator() =>
        Activator.CreateInstance(jsonType) switch
        {
            IIncrementalGenerator incremental => incremental.AsSourceGenerator(),
            ISourceGenerator generator => generator,
            _ => throw new InvalidOperationException("JSON generator entry point not found."),
        };
    Console.WriteLine($"Roslyn: {typeof(Compilation).Assembly.FullName}");
    Console.WriteLine(
        $"Compiler: {typeof(Compilation).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}"
    );
    Console.WriteLine($"JSON: {jsonAssembly.FullName}");
    var options = new CSharpParseOptions(LanguageVersion.Preview);
    var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Append(typeof(ComponentRecipe).Assembly.Location)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(path => MetadataReference.CreateFromFile(path));
    var compilation = CSharpCompilation.Create(
        "GenerationPhaseFixture",
        references: references,
        options: new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable
        )
    );
    var declarations = new BufferText(
        "Application.declarations",
        """
        #nullable enable
        namespace Fixture;
        public record Person(string Name);
        [System.Text.Json.Serialization.JsonSerializable(typeof(Person))]
        public partial class JsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
        public sealed partial class Application { public static partial string Create(); }
        """
    );
    const string implementation = """
        #nullable enable
        namespace Fixture;
        public sealed partial class Application
        {
            private readonly Person person = System.Text.Json.JsonSerializer.Deserialize(
                "{\"Name\":\"Ada\"}", JsonContext.Default.Person)!;
            public static partial string Create() => new Application().Read();
            private string Read() => System.Text.Json.JsonSerializer.Serialize(person, JsonContext.Default.Person);
        }
        """;
    GeneratorDriver Driver(AdditionalText input, params ISourceGenerator[] generators) =>
        CSharpGeneratorDriver.Create(generators, [input], options);
    var singleObserver = new BindingObserver([], implementation, options);
    var timer = Stopwatch.StartNew();
    var single = Driver(
            declarations,
            new DeclarationProjection().AsSourceGenerator(),
            JsonGenerator(),
            singleObserver.AsSourceGenerator()
        )
        .RunGenerators(compilation);
    Check(
        !single.GetRunResult().Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
        "single-pass generator execution"
    );
    Check(
        JsonOutputs(single.GetRunResult()).Length > 0,
        "pre-compilation declarations reach real JSON generator"
    );
    Check(
        !singleObserver.GeneratedApiVisible,
        "ordinary generated JSON API absent from another generator compilation"
    );
    Console.WriteLine(
        $"Single pass: {timer.Elapsed.TotalMilliseconds:F2} ms; JSON outputs {JsonOutputs(single.GetRunResult()).Length}; binding cannot see generated API, as expected."
    );
    timer.Restart();
    var preparation = Driver(
            declarations,
            new DeclarationProjection().AsSourceGenerator(),
            JsonGenerator()
        )
        .RunGenerators(compilation);
    var captured = JsonOutputs(preparation.GetRunResult());
    var binding = new BindingObserver(
        captured.Select(source => source.SyntaxTree).ToImmutableArray(),
        implementation,
        options
    );
    var final = Driver(
            declarations,
            new DeclarationProjection().AsSourceGenerator(),
            JsonGenerator(),
            binding.AsSourceGenerator()
        )
        .RunGeneratorsAndUpdateCompilation(
            compilation,
            out var output,
            out var generatorDiagnostics
        );
    Check(
        binding.GeneratedApiVisible && binding.InitializerType == "Fixture.Person",
        "binding-only trees supply actual initializer type"
    );
    Check(
        !generatorDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
        Diagnostics(generatorDiagnostics)
    );
    Check(
        Matches(captured, JsonOutputs(final.GetRunResult())),
        "preparatory/final JSON output matches exactly"
    );
    Check(
        !output.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error),
        Diagnostics(output.GetDiagnostics())
    );
    using var assembly = new MemoryStream();
    var emitted = output.Emit(assembly);
    Check(emitted.Success, Diagnostics(emitted.Diagnostics));
    assembly.Position = 0;
    var loadContext = new AssemblyLoadContext("generation-probe", isCollectible: true);
    var executable = loadContext.LoadFromStream(assembly);
    var result = executable.GetType("Fixture.Application")!.GetMethod("Create")!.Invoke(null, null);
    Check(Equals(result, "{\"Name\":\"Ada\"}"), "generated JSON initializer and handler execute");
    loadContext.Unload();
    Console.WriteLine(
        $"Bounded preparation + final: {timer.Elapsed.TotalMilliseconds:F2} ms; {captured.Length} matching JSON outputs; emitted and executed {result}."
    );
    var changed = new BufferText(
        declarations.Path,
        declarations.Value.Replace(
            "Person(string Name)",
            "Person(string Name, int Age = 0)",
            StringComparison.Ordinal
        )
    );
    var changedPreparation = preparation
        .ReplaceAdditionalText(declarations, changed)
        .RunGenerators(compilation);
    var changedOutputs = JsonOutputs(changedPreparation.GetRunResult());
    Check(!Matches(captured, changedOutputs), "unsaved declaration edit invalidates output");
    Check(
        changedOutputs.Any(source =>
            source.SourceText.ToString().Contains("Age", StringComparison.Ordinal)
        ),
        "unsaved property reaches JSON output"
    );
    var changedBinding = new BindingObserver(
        changedOutputs.Select(source => source.SyntaxTree).ToImmutableArray(),
        implementation,
        options
    );
    var changedFinal = Driver(
            changed,
            new DeclarationProjection().AsSourceGenerator(),
            JsonGenerator(),
            changedBinding.AsSourceGenerator()
        )
        .RunGeneratorsAndUpdateCompilation(compilation, out var changedCompilation, out _);
    Check(
        Matches(changedOutputs, JsonOutputs(changedFinal.GetRunResult()))
            && changedBinding.GeneratedApiVisible,
        "unsaved buffer binds with matching outputs"
    );
    Check(
        !changedCompilation.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error),
        "unsaved buffer compiles"
    );
    var deleted = changedPreparation
        .RemoveAdditionalTexts([changed])
        .RunGeneratorsAndUpdateCompilation(compilation, out var removedCompilation, out _);
    Check(
        JsonOutputs(deleted.GetRunResult()).Length == 0
            && removedCompilation.GetTypeByMetadataName("Fixture.Person") is null,
        "deletion removes projected/generated symbols"
    );
    Console.WriteLine(
        "PASS: phase visibility, bounded binding, unique final emission, JSON execution, unsaved buffer replacement, deletion, mismatch detection."
    );

    var luiSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "JsonView.lui"));
    var identity = new LuiFreshnessIdentity(
        "a0",
        "a0",
        new LuiDocumentIdentity("JsonView.lui"),
        "1",
        "preview"
    );
    var declarationsOnly = compilation.AddSyntaxTrees(
        preparation.GetRunResult().Results[0].GeneratedSources.Select(source => source.SyntaxTree)
    );
    var withoutGeneratedApi = LuiCompiler.Compile(
        LuiParser.Parse(luiSource),
        declarationsOnly,
        identity
    );
    Check(
        !withoutGeneratedApi.Success,
        "real LUI binding must reject missing JSON API before preparation output is supplied"
    );
    var preparedBinding = declarationsOnly.AddSyntaxTrees(
        captured.Select(source => source.SyntaxTree)
    );
    var lui = LuiCompiler.Compile(LuiParser.Parse(luiSource), preparedBinding, identity);
    Check(
        lui.Success,
        string.Join(Environment.NewLine, lui.Diagnostics.Select(d => d.Id + ": " + d.Message))
    );
    var withLui = output.AddSyntaxTrees(CSharpSyntaxTree.ParseText(lui.Source!, options));
    using var luiAssembly = new MemoryStream();
    var luiEmitted = withLui.Emit(luiAssembly);
    Check(luiEmitted.Success, Diagnostics(luiEmitted.Diagnostics));
    luiAssembly.Position = 0;
    var luiLoadContext = new AssemblyLoadContext("lui-generation-probe", isCollectible: true);
    var luiExecutable = luiLoadContext.LoadFromStream(luiAssembly);
    var factory = luiExecutable
        .GetType("Fixture.Components")!
        .GetMethod("JsonView")!
        .CreateDelegate<Func<ComponentRecipe>>();
    using (var composition = new Composition(new ReactiveGraph(), "generated-json"))
    using (var theme = new ThemeContext(composition.Root.Scope, new Theme("probe")))
    {
        var root = composition.Mount(composition.Root, theme, factory());
        Check(
            SemanticNames(composition.SemanticSnapshot()!)
                .Contains("{\"Name\":\"Ada\"}", StringComparer.Ordinal),
            "real LUI state initializer and method execute during mounted text projection"
        );
    }
    luiLoadContext.Unload();
    Console.WriteLine(
        "PASS: actual LUI compiler rejects missing APIs, binds preparatory JSON output, lowers state/methods, emits and mounts with the expected text."
    );
    var combined = PrototypeProjection.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Combined.lui.input"))
    );
    var combinedInput = new BufferText("Combined.lui", combined.Declarations);
    var combinedDriver = Driver(
            combinedInput,
            new DeclarationProjection().AsSourceGenerator(),
            JsonGenerator()
        )
        .RunGenerators(compilation);
    var combinedBinding = compilation.AddSyntaxTrees(
        combinedDriver
            .GetRunResult()
            .Results.SelectMany(result => result.GeneratedSources)
            .Select(source => source.SyntaxTree)
    );
    var combinedLui = LuiCompiler.Compile(
        LuiParser.Parse(combined.Component),
        combinedBinding,
        identity
    );
    Check(
        combinedLui.Success,
        string.Join(
            Environment.NewLine,
            combinedLui.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message)
        )
    );
    using var combinedAssembly = new MemoryStream();
    var combinedEmitted = combinedBinding
        .AddSyntaxTrees(CSharpSyntaxTree.ParseText(combinedLui.Source!, options))
        .Emit(combinedAssembly);
    Check(combinedEmitted.Success, Diagnostics(combinedEmitted.Diagnostics));
    Console.WriteLine(
        "PASS: one combined input contributes ordinary model/context declarations to real JSON generation and component state/methods to current LUI lowering."
    );
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1;
}

// Every driver above registers the real JSON generator second, including empty-output runs.
static ImmutableArray<GeneratedSourceResult> JsonOutputs(GeneratorDriverRunResult run) =>
    run.Results[1].GeneratedSources;
static bool Matches(
    ImmutableArray<GeneratedSourceResult> expected,
    ImmutableArray<GeneratedSourceResult> actual
) => Fingerprints(expected).SequenceEqual(Fingerprints(actual));
static IEnumerable<string> Fingerprints(ImmutableArray<GeneratedSourceResult> sources) =>
    sources
        .Select(source =>
            source.HintName
            + ":"
            + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(source.SourceText.ToString()))
            )
        )
        .Order(StringComparer.Ordinal);
static string Diagnostics(IEnumerable<Diagnostic> diagnostics) =>
    string.Join(Environment.NewLine, diagnostics);
static void Check(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
}
static IEnumerable<string?> SemanticNames(SemanticSnapshot snapshot) =>
    new[] { snapshot.Name }.Concat(snapshot.Children.SelectMany(SemanticNames));

sealed class BufferText(string path, string value) : AdditionalText
{
    public override string Path => path;
    public string Value => value;

    public override SourceText GetText(CancellationToken cancellationToken = default) =>
        SourceText.From(value, Encoding.UTF8);
}

sealed class DeclarationProjection : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#pragma warning disable RSEXPERIMENTAL007 // Isolated A0 experiment against pinned SDK host; not production adoption.
        context.RegisterPreCompilationSourceOutput(
            context.AdditionalTextsProvider,
            static (production, text) =>
                production.AddSource(
                    "AuthoredDeclarations.g.cs",
                    text.GetText(production.CancellationToken)!
                )
        );
#pragma warning restore RSEXPERIMENTAL007
    }
}

sealed class BindingObserver(
    ImmutableArray<SyntaxTree> bindingOnly,
    string implementation,
    CSharpParseOptions options
) : IIncrementalGenerator
{
    public bool GeneratedApiVisible { get; private set; }
    public string? InitializerType { get; private set; }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(
            context.CompilationProvider,
            (production, input) =>
            {
                var binding = input.AddSyntaxTrees(bindingOnly);
                GeneratedApiVisible =
                    binding
                        .GetTypeByMetadataName("Fixture.JsonContext")
                        ?.GetMembers("Default")
                        .Length > 0;
                if (!GeneratedApiVisible)
                    return;
                var body = CSharpSyntaxTree.ParseText(implementation, options);
                binding = binding.AddSyntaxTrees(body);
                var initializer = body.GetRoot()
                    .DescendantNodes()
                    .OfType<VariableDeclaratorSyntax>()
                    .Single()
                    .Initializer!.Value;
                InitializerType = binding
                    .GetSemanticModel(body)
                    .GetTypeInfo(initializer, production.CancellationToken)
                    .Type?.ToDisplayString();
                production.AddSource(
                    "Application.implementation.g.cs",
                    SourceText.From(implementation, Encoding.UTF8)
                );
            }
        );
    }
}
