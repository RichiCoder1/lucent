using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Lucent.Core;
using Lucent.Lui.Compiler;
using Lucent.Lui.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

try
{
    if (args.Length is not (4 or 5))
        throw new ArgumentException(
            "Usage: IntegratedHost <input> <output> <identity> <json-generator> [companion-source]"
        );
    var inputPath = Path.GetFullPath(args[0]);
    var outputPath = Path.GetFullPath(args[1]);
    var identityName = args[2];
    Directory.CreateDirectory(outputPath);
    foreach (var stale in Directory.EnumerateFiles(outputPath, "*.g.cs"))
        File.Delete(stale);

    var projection = LuiAuthoredSourceProjection.Project(File.ReadAllText(inputPath));
    if (!projection.Success || projection.EarlyComponentDeclaration is null)
        throw new InvalidOperationException(
            String.Join(
                Environment.NewLine,
                projection.Diagnostics.Select(static diagnostic =>
                    diagnostic.Id + ": " + diagnostic.Message
                )
            )
        );
    var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
    var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Append(typeof(ComponentRecipe).Assembly.Location)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(static path => MetadataReference.CreateFromFile(path));
    var declarationsTree = CSharpSyntaxTree.ParseText(
        projection.DeclarationsSource,
        parseOptions,
        inputPath + ".declarations.cs"
    );
    var syntaxTrees = new List<SyntaxTree> { declarationsTree };
    syntaxTrees.Add(
        CSharpSyntaxTree.ParseText(
            projection.EarlyComponentDeclaration,
            parseOptions,
            inputPath + ".component.early.g.cs"
        )
    );
    if (args.Length == 5)
        syntaxTrees.Add(
            CSharpSyntaxTree.ParseText(
                File.ReadAllText(args[4]),
                parseOptions,
                Path.GetFullPath(args[4])
            )
        );
    var compilation = CSharpCompilation.Create(
        identityName + ".Integrated",
        syntaxTrees,
        references,
        new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable
        )
    );
    var jsonAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[3]));
    var jsonType = jsonAssembly
        .GetTypes()
        .Single(static type => type.Name == "JsonSourceGenerator");
    var jsonGenerator = Activator.CreateInstance(jsonType) switch
    {
        IIncrementalGenerator incremental => incremental.AsSourceGenerator(),
        ISourceGenerator source => source,
        _ => throw new InvalidOperationException("JSON generator entry point was not found."),
    };
    var options = new ProbeOptionsProvider(Path.GetDirectoryName(inputPath)!);
    GeneratorDriver driver = CSharpGeneratorDriver.Create(
        [jsonGenerator, new RouteGenerator().AsSourceGenerator()],
        parseOptions: parseOptions,
        optionsProvider: options
    );
    driver = driver.RunGeneratorsAndUpdateCompilation(
        compilation,
        out var generatedCompilation,
        out var generatorDiagnostics
    );
    CheckNoErrors(generatorDiagnostics);
    CheckPreparationErrors(generatedCompilation.GetDiagnostics());
    var run = driver.GetRunResult();
    var generated = run.Results.SelectMany(static result => result.GeneratedSources).ToArray();
    var routeOutputs = run.Results[1].GeneratedSources;
    if (generated.Length < 2)
        throw new InvalidOperationException("Expected both JSON and route generated outputs.");

    var parsed = projection.Document;
    if (parsed.Component?.Name.Text != identityName)
        throw new InvalidOperationException(
            $"Component '{parsed.Component?.Name.Text}' does not match requested identity '{identityName}'."
        );
    var lui = LuiCompiler.CompileNamedComponent(
        parsed,
        generatedCompilation,
        new LuiFreshnessIdentity(
            identityName,
            identityName,
            new LuiDocumentIdentity(Path.GetFileNameWithoutExtension(inputPath) + ".lui"),
            "1",
            "preview"
        ),
        inputPath
    );
    if (!lui.Success)
        throw new InvalidOperationException(
            String.Join(
                Environment.NewLine,
                lui.Diagnostics.Select(static diagnostic =>
                    diagnostic.Id + ": " + diagnostic.Message
                )
            )
        );

    File.WriteAllText(
        Path.Combine(outputPath, "000.declarations.g.cs"),
        "#nullable enable\n" + projection.DeclarationsSource,
        new UTF8Encoding(false)
    );
    File.WriteAllText(
        Path.Combine(outputPath, "001.component.early.g.cs"),
        "#nullable enable\n" + projection.EarlyComponentDeclaration,
        new UTF8Encoding(false)
    );
    for (var index = 0; index < routeOutputs.Length; index++)
        File.WriteAllText(
            Path.Combine(outputPath, $"{index + 1:000}.route.g.cs"),
            routeOutputs[index].SourceText.ToString(),
            new UTF8Encoding(false)
        );
    File.WriteAllText(
        Path.Combine(outputPath, "900.named-lui.g.cs"),
        lui.Source!,
        new UTF8Encoding(false)
    );
    Console.WriteLine(
        $"PASS: generated {generated.Length} real JSON/route outputs, real LUI markup, and named identity '{identityName}'."
    );
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1;
}

static void CheckNoErrors(IEnumerable<Diagnostic> diagnostics)
{
    var errors = diagnostics
        .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        .ToArray();
    if (errors.Length != 0)
        throw new InvalidOperationException(
            String.Join(Environment.NewLine, errors.AsEnumerable())
        );
}

static void CheckPreparationErrors(IEnumerable<Diagnostic> diagnostics)
{
    CheckNoErrors(
        diagnostics.Where(static diagnostic =>
            !(
                diagnostic.Id == "CS8795"
                && diagnostic.Location.SourceTree?.FilePath.EndsWith(
                    ".component.early.g.cs",
                    StringComparison.Ordinal
                ) == true
            )
            && diagnostic.Id != "CS9248"
        )
    );
}

sealed class ProbeOptionsProvider(string projectDirectory) : AnalyzerConfigOptionsProvider
{
    private readonly AnalyzerConfigOptions global = new ProbeOptions(projectDirectory);
    public override AnalyzerConfigOptions GlobalOptions => global;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => EmptyOptions.Instance;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
        EmptyOptions.Instance;
}

sealed class ProbeOptions(string projectDirectory) : AnalyzerConfigOptions
{
    public override bool TryGetValue(string key, out string value)
    {
        if (key == "build_property.ProjectDir")
        {
            value = projectDirectory + Path.DirectorySeparatorChar;
            return true;
        }
        value = String.Empty;
        return false;
    }
}

sealed class EmptyOptions : AnalyzerConfigOptions
{
    internal static EmptyOptions Instance { get; } = new();

    public override bool TryGetValue(string key, out string value)
    {
        value = String.Empty;
        return false;
    }
}
