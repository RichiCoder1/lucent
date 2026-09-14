using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Lucent.Core;
using Lucent.Lui.Compiler;
using Lucent.Lui.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

    var projection = PrototypeProjection.Parse(File.ReadAllText(inputPath));
    var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
    var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Append(typeof(ComponentRecipe).Assembly.Location)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(static path => MetadataReference.CreateFromFile(path));
    var declarationsTree = CSharpSyntaxTree.ParseText(
        projection.Declarations,
        parseOptions,
        inputPath + ".declarations.cs"
    );
    var syntaxTrees = new List<SyntaxTree> { declarationsTree };
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
    CheckNoErrors(generatedCompilation.GetDiagnostics());
    var run = driver.GetRunResult();
    var generated = run.Results.SelectMany(static result => result.GeneratedSources).ToArray();
    var routeOutputs = run.Results[1].GeneratedSources;
    if (generated.Length < 2)
        throw new InvalidOperationException("Expected both JSON and route generated outputs.");

    var lui = LuiCompiler.Compile(
        LuiParser.Parse(projection.Component),
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
        projection.Declarations,
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
        TransformIdentity(lui.Source!, identityName),
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

static string TransformIdentity(string source, string identityName)
{
    var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
    var unit = tree.GetCompilationUnitRoot();
    var container = unit.DescendantNodes()
        .OfType<ClassDeclarationSyntax>()
        .Single(static candidate => candidate.Identifier.ValueText == "Components");
    var factory = container
        .Members.OfType<MethodDeclarationSyntax>()
        .Single(static candidate => candidate.Identifier.ValueText == "IntegratedView");
    var state = container
        .Members.OfType<ClassDeclarationSyntax>()
        .Single(static candidate =>
            candidate.Identifier.ValueText.StartsWith("__luiState_", StringComparison.Ordinal)
        );
    if (container.Members.Count != 2)
        throw new InvalidOperationException(
            "The constrained named-state transform expected one factory and one generated state class."
        );

    var rename = new IdentityRename(state.Identifier.ValueText, identityName);
    factory = (MethodDeclarationSyntax)rename.Visit(factory)!;
    factory = factory.WithIdentifier(SyntaxFactory.Identifier("Create"));
    var members = state
        .Members.Select(member => (MemberDeclarationSyntax)rename.Visit(member)!)
        .ToList();
    var identities = SyntaxFactory.ParseMemberDeclaration(
        $"private static readonly global::System.Collections.Generic.HashSet<{identityName}> __probeIdentities = new();"
    )!;
    var count = SyntaxFactory.ParseMemberDeclaration(
        "public static int DistinctMounts => __probeIdentities.Count;"
    )!;
    var last = SyntaxFactory.ParseMemberDeclaration(
        "public static object? LastMount { get; private set; }"
    )!;
    var constructorIndex = members.FindIndex(static member =>
        member is ConstructorDeclarationSyntax
    );
    if (constructorIndex < 0)
        throw new InvalidOperationException("Generated state constructor was not found.");
    var constructor = (ConstructorDeclarationSyntax)members[constructorIndex];
    constructor = constructor.WithBody(
        constructor.Body!.WithStatements(
            constructor.Body.Statements.Insert(
                0,
                SyntaxFactory.ParseStatement("__probeIdentities.Add(this); LastMount = this;")
            )
        )
    );
    members[constructorIndex] = constructor;
    var named = state
        .WithIdentifier(SyntaxFactory.Identifier(identityName))
        .WithModifiers(
            SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.SealedKeyword),
                SyntaxFactory.Token(SyntaxKind.PartialKeyword)
            )
        )
        .WithMembers(SyntaxFactory.List(new[] { identities, count, last, factory }.Concat(members)))
        .WithLeadingTrivia(container.GetLeadingTrivia())
        .WithTrailingTrivia(container.GetTrailingTrivia());
    return unit.ReplaceNode(container, named).NormalizeWhitespace().ToFullString();
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

sealed class IdentityRename(string generatedStateName, string identityName) : CSharpSyntaxRewriter
{
    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) =>
        node.Identifier.ValueText == generatedStateName
            ? node.WithIdentifier(SyntaxFactory.Identifier(identityName))
            : base.VisitIdentifierName(node);

    public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node) =>
        base.VisitConstructorDeclaration(
            node.Identifier.ValueText == generatedStateName
                ? node.WithIdentifier(SyntaxFactory.Identifier(identityName))
                : node
        );
}
