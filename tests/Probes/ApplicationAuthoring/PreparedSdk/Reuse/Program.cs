using System.Collections.Immutable;
using System.Text;
using Lucent.Lui.Preparation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

var firstCounter = new Counter();
var first = Prepare(
    Generator("stable", "hash-a", 0, firstCounter),
    "first",
    "option-a",
    previous: null
);
Require(
    first.Success && firstCounter.Initialize == 1 && firstCounter.Produce == 1,
    "Initial generator pass failed."
);
var supportPath = Path.GetFullPath("Support.lui");
var supportDeclaration = first
    .BindingCompilation.SyntaxTrees.Single(tree =>
        tree.FilePath.Contains("Declarations", StringComparison.Ordinal)
    )
    .GetRoot()
    .DescendantNodes()
    .OfType<RecordDeclarationSyntax>()
    .Single();
var mappedSupport = supportDeclaration.SyntaxTree.GetMappedLineSpan(
    supportDeclaration.Identifier.Span
);
Require(
    String.Equals(mappedSupport.Path, supportPath, StringComparison.OrdinalIgnoreCase)
        && mappedSupport.StartLinePosition.Line == 0,
    $"Prepared supporting declarations mapped to '{mappedSupport.Path}:{mappedSupport.StartLinePosition.Line + 1}' instead of '{supportPath}:1'."
);

var freshCounter = new Counter();
var updated = Prepare(
    Generator("stable", "hash-a", 0, freshCounter),
    "second",
    "option-b",
    first.DriverState
);
Require(updated.Success, "Updated generator pass failed.");
Require(
    freshCounter.Initialize == 0 && freshCounter.Produce == 0,
    "A fresh wrapper for the same analyzer identity replaced reusable driver state."
);
Require(firstCounter.Produce == 2, "The reused generator did not observe the updated inputs.");
Require(
    Output(updated).Contains("second|option-b", StringComparison.Ordinal),
    "Updated AdditionalText/options were not visible through reused driver state."
);

var hashCounter = new Counter();
var changedHash = Prepare(
    Generator("stable", "hash-b", 0, hashCounter),
    "second",
    "option-b",
    updated.DriverState
);
Require(
    changedHash.Success && hashCounter.Initialize == 1,
    "A changed analyzer hash did not invalidate driver state."
);

var ordinalCounter = new Counter();
var changedOrdinal = Prepare(
    Generator("stable", "hash-b", 1, ordinalCounter),
    "second",
    "option-b",
    changedHash.DriverState
);
Require(
    changedOrdinal.Success && ordinalCounter.Initialize == 1,
    "A changed generator ordinal did not invalidate driver state."
);

Console.WriteLine(
    "PREPARATION REUSE PASS: fresh wrappers reused stable driver state; changed AdditionalText/options flowed; analyzer hash and ordinal changes invalidated."
);
return 0;

static LuiPreparationResult Prepare(
    LuiPreparationGenerator generator,
    string additional,
    string option,
    LuiPreparationDriverState? previous
)
{
    var compilation = CSharpCompilation.Create(
        "ReuseProbe",
        options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
        references: TrustedReferences()
    );
    var text = new MemoryAdditionalText(Path.GetFullPath("Observed.input"), additional);
    return LuiPreparationEngine.Prepare(
        new LuiPreparationRequest(
            compilation,
            [
                new LuiPreparationDocument(
                    Path.GetFullPath("Support.lui"),
                    "Support.lui",
                    "namespace ReuseFixture; public record Model(string Value);",
                    "1"
                ),
            ],
            [generator],
            [text],
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            new ProbeOptionsProvider(text.Path, option),
            "epoch",
            "project",
            "preview",
            "",
            "",
            "ReuseFixture",
            previous
        )
    );
}

static LuiPreparationGenerator Generator(
    string stableIdentity,
    string hash,
    int ordinal,
    Counter counter
) =>
    new(
        stableIdentity,
        "ProbeGenerator.dll",
        hash,
        ordinal,
        new ProbeGenerator(counter).AsSourceGenerator()
    );

static string Output(LuiPreparationResult result) =>
    result
        .BindingCompilation.SyntaxTrees.Single(tree =>
            tree.FilePath.EndsWith("Probe.g.cs", StringComparison.Ordinal)
        )
        .GetText()
        .ToString();

static ImmutableArray<MetadataReference> TrustedReferences() =>
    ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToImmutableArray();

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class Counter
{
    public int Initialize { get; set; }

    public int Produce { get; set; }
}

sealed class ProbeGenerator(Counter counter) : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        counter.Initialize++;
        var value = context
            .AdditionalTextsProvider.Select(
                static (text, cancellationToken) => text.GetText(cancellationToken)!.ToString()
            )
            .Collect()
            .Combine(context.AnalyzerConfigOptionsProvider);
        context.RegisterSourceOutput(
            value,
            (production, input) =>
            {
                counter.Produce++;
                input.Right.GlobalOptions.TryGetValue("build_property.ProbeOption", out var option);
                production.AddSource(
                    "Probe.g.cs",
                    SourceText.From(
                        "// " + String.Join(";", input.Left) + "|" + option,
                        Encoding.UTF8
                    )
                );
            }
        );
    }
}

sealed class MemoryAdditionalText(string path, string source) : AdditionalText
{
    public override string Path { get; } = path;

    public override SourceText GetText(CancellationToken cancellationToken = default) =>
        SourceText.From(source, Encoding.UTF8);
}

sealed class ProbeOptionsProvider(string path, string value) : AnalyzerConfigOptionsProvider
{
    private readonly AnalyzerConfigOptions global = new ProbeOptions(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["build_property.ProbeOption"] = value,
        }
    );

    public override AnalyzerConfigOptions GlobalOptions => global;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => ProbeOptions.Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
        String.Equals(textFile.Path, path, StringComparison.OrdinalIgnoreCase)
            ? global
            : ProbeOptions.Empty;
}

sealed class ProbeOptions(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
{
    public static ProbeOptions Empty { get; } =
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public override bool TryGetValue(string key, out string value) =>
        values.TryGetValue(key, out value!);
}
