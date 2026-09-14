using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

var inputDirectory = Path.Combine(AppContext.BaseDirectory, "Inputs");
var references = TrustedPlatformReferences()
    .Append(MetadataReference.CreateFromFile(typeof(Lucent.Core.ComponentRecipe).Assembly.Location))
    .ToImmutableArray();
var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);

try
{
    var valid = Compile(
        ["Counter.lui", "Failing.lui", "Harness"],
        ["Counter", "Failing"],
        references,
        parseOptions
    );
    Check(
        !valid.Diagnostics.Any(static value => value.Severity == DiagnosticSeverity.Error),
        Describe(valid.Diagnostics)
    );
    Check(
        valid.Generated.Length == 4,
        "expected one early and one final generated source per component"
    );
    Check(
        valid.Generated.Count(static value =>
            value.Contains("partial ComponentRecipe Create();", StringComparison.Ordinal)
        ) == 2,
        "expected exactly one erased Create declaration per component"
    );
    Check(
        valid.Generated.Count(static value =>
            value.Contains("partial ComponentRecipe Create() =>", StringComparison.Ordinal)
        ) == 2,
        "expected exactly one Create implementation per component"
    );
    Check(
        !File.ReadAllText(Path.Combine(inputDirectory, "Counter.lui.cs"))
            .Contains("Create(", StringComparison.Ordinal),
        "authored companion must not contain the generated entry point"
    );

    using var assembly = new MemoryStream();
    var emitted = valid.Compilation.Emit(assembly);
    Check(emitted.Success, Describe(emitted.Diagnostics));
    assembly.Position = 0;
    var loaded = AssemblyLoadContext.Default.LoadFromStream(assembly);
    var run = loaded
        .GetType("CompanionFixture.Harness")!
        .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
    Console.WriteLine((string)run.Invoke(null, null)!);

    var duplicate = Compile(["DuplicateSetup.lui"], ["DuplicateSetup"], references, parseOptions);
    Check(
        duplicate.Diagnostics.Count(static value => value.Id == "A0C001") == 1,
        Describe(duplicate.Diagnostics)
    );
    Check(
        !duplicate.Generated.Any(static value =>
            value.StartsWith("DuplicateSetup.final.g.cs\n", StringComparison.Ordinal)
        ),
        "duplicate setup emitted a final implementation"
    );

    var misuse = Compile(["Misuse.lui"], ["Misuse"], references, parseOptions);
    Check(
        misuse.Diagnostics.Count(static value => value.Id == "A0C002") == 1,
        Describe(misuse.Diagnostics)
    );
    Check(
        misuse.Diagnostics.Count(static value => value.Id == "A0C003") == 1,
        Describe(misuse.Diagnostics)
    );
    Check(
        !misuse.Generated.Any(static value =>
            value.StartsWith("Misuse.final.g.cs\n", StringComparison.Ordinal)
        ),
        "invalid companion emitted a final implementation"
    );
    var derived = Compile([], ["Derived"], references, parseOptions);
    Check(
        derived.Diagnostics.Count(static value => value.Id == "A0C004") == 1,
        Describe(derived.Diagnostics)
    );
    Check(
        derived.Generated.Length == 0,
        "unsupported inferred-derived state emitted declarations or an implementation"
    );
    Console.WriteLine(
        "PASS: constrained emitter rejects inferred-derived state, duplicate setup, authored constructors, and [ComponentState] companions."
    );
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1;
}

ProbeResult Compile(
    IEnumerable<string> sourceNames,
    IEnumerable<string> inputNames,
    ImmutableArray<MetadataReference> metadata,
    CSharpParseOptions options
)
{
    var sources = sourceNames.Select(name =>
        CSharpSyntaxTree.ParseText(
            File.ReadAllText(Path.Combine(inputDirectory, name + ".cs")),
            options,
            Path.Combine(inputDirectory, name + ".cs")
        )
    );
    var compilation = CSharpCompilation.Create(
        "CompanionFixture_" + Guid.NewGuid().ToString("N"),
        sources,
        metadata,
        new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable
        )
    );
    var additional = inputNames
        .Select(name => new BufferText(
            Path.Combine(inputDirectory, name + ".lui.input"),
            File.ReadAllText(Path.Combine(inputDirectory, name + ".lui.input"))
        ))
        .Cast<AdditionalText>()
        .ToImmutableArray();
    GeneratorDriver driver = CSharpGeneratorDriver.Create(
        [new CompanionEmitter().AsSourceGenerator()],
        additional,
        options
    );
    driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
    var run = driver.GetRunResult();
    return new ProbeResult(
        (CSharpCompilation)output,
        run.Diagnostics,
        run.Results.SelectMany(static result => result.GeneratedSources)
            .Select(static source => source.HintName + "\n" + source.SourceText)
            .ToImmutableArray()
    );
}

static ImmutableArray<MetadataReference> TrustedPlatformReferences() =>
    ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToImmutableArray();

static string Describe(IEnumerable<Diagnostic> diagnostics) =>
    string.Join(Environment.NewLine, diagnostics);
static void Check(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
}

readonly record struct ProbeResult(
    CSharpCompilation Compilation,
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<string> Generated
);

sealed class BufferText(string path, string value) : AdditionalText
{
    public override string Path => path;

    public override SourceText GetText(CancellationToken cancellationToken = default) =>
        SourceText.From(value, Encoding.UTF8);
}

sealed class CompanionEmitter : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor DuplicateSetup = Error(
        "A0C001",
        "Duplicate Setup",
        "Named component '{0}' has more than one Setup implementation."
    );
    private static readonly DiagnosticDescriptor AuthoredConstructor = Error(
        "A0C002",
        "Authored constructor",
        "Named component '{0}' cannot declare a constructor in this prototype."
    );
    private static readonly DiagnosticDescriptor ComponentStateMisuse = Error(
        "A0C003",
        "ComponentState misuse",
        "Named component '{0}' is its state identity and cannot also use [ComponentState]."
    );
    private static readonly DiagnosticDescriptor Unsupported = Error(
        "A0C004",
        "Unsupported prototype input",
        "{0}"
    );

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var inputs = context
            .AdditionalTextsProvider.Where(static text =>
                text.Path.EndsWith(".lui.input", StringComparison.OrdinalIgnoreCase)
            )
            .Select(static (text, cancellationToken) => Parse(text, cancellationToken));
#pragma warning disable RSEXPERIMENTAL007 // Deliberate isolated A0 feasibility proof against the pinned Roslyn host.
        context.RegisterPreCompilationSourceOutput(
            inputs,
            static (production, input) =>
            {
                if (input.Errors.Length == 0)
                    production.AddSource(
                        input.Name + ".early.g.cs",
                        SourceText.From(Early(input), Encoding.UTF8)
                    );
            }
        );
#pragma warning restore RSEXPERIMENTAL007
        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(inputs.Collect()),
            static (production, pair) =>
            {
                foreach (var input in pair.Right)
                    Final(production, pair.Left, input);
            }
        );
    }

    private static Input Parse(AdditionalText text, CancellationToken cancellationToken)
    {
        var source = text.GetText(cancellationToken)?.ToString() ?? string.Empty;
        var document = LuiParser.Parse(source);
        var errors = document
            .Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.Message)
            .ToImmutableArray();
        var component = document.Component;
        if (component is null)
            return new Input(
                text.Path,
                source,
                document.Namespace ?? string.Empty,
                "Missing",
                "internal",
                [],
                [],
                [],
                errors.Add("No component declaration.")
            );
        if (component.Parameters.Count != 0)
            errors = errors.Add("Parameters are outside this constrained companion probe.");
        var fields = ImmutableArray.CreateBuilder<Field>();
        var methods = ImmutableArray.CreateBuilder<MethodDeclarationSyntax>();
        var setups = ImmutableArray.CreateBuilder<LuiMemberSyntax>();
        foreach (var member in component.Body.OfType<LuiMemberSyntax>())
        {
            switch (member.Kind)
            {
                case LuiMemberKind.Field
                    when member.Declaration is FieldDeclarationSyntax field
                        && field.Declaration.Variables.Count == 1
                        && field.Declaration.Variables[0].Initializer is not null
                        && field
                            .AttributeLists.SelectMany(static list => list.Attributes)
                            .Any(static attribute =>
                                AttributeName(attribute).EndsWith("Once", StringComparison.Ordinal)
                            )
                        && !field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword):
                    fields.Add(
                        new Field(
                            field.Declaration.Type.ToString(),
                            field.Declaration.Variables[0].Identifier.ValueText,
                            field.Declaration.Variables[0].Initializer!.Value.ToString()
                        )
                    );
                    break;
                case LuiMemberKind.Method when member.Declaration is MethodDeclarationSyntax method:
                    methods.Add(method);
                    break;
                case LuiMemberKind.Setup:
                    setups.Add(member);
                    break;
                default:
                    errors = errors.Add(
                        "Only explicit [Once] initialized writable fields and ordinary methods are supported; inferred-derived and readonly state are outside this prototype."
                    );
                    break;
            }
        }
        return new Input(
            text.Path,
            source,
            document.Namespace ?? string.Empty,
            component.Name.Text,
            component.Accessibility.Text,
            fields.ToImmutable(),
            methods.ToImmutable(),
            setups.ToImmutable(),
            errors
        );
    }

    private static string Early(Input input)
    {
        var builder = Header(input);
        builder.AppendLine($"{input.Accessibility} sealed partial class {input.Name}");
        builder.AppendLine("{");
        builder.AppendLine("    public static partial ComponentRecipe Create();");
        foreach (var field in input.Fields)
            builder.AppendLine($"    public partial {field.Type} {field.Name} {{ get; set; }}");
        foreach (var method in input.Methods)
            builder.AppendLine(Indent(Partial(method, declaration: true), 4));
        builder.AppendLine("    partial void Setup(ComponentContext context);");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void Final(
        SourceProductionContext production,
        Compilation compilation,
        Input input
    )
    {
        if (input.Errors.Length != 0)
        {
            foreach (var error in input.Errors)
                production.ReportDiagnostic(Diagnostic.Create(Unsupported, Location.None, error));
            return;
        }
        var companion = compilation
            .SyntaxTrees.Where(static tree =>
                tree.FilePath.EndsWith(".lui.cs", StringComparison.OrdinalIgnoreCase)
            )
            .SelectMany(static tree =>
                tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            )
            .SingleOrDefault(candidate => candidate.Identifier.ValueText == input.Name);
        if (companion is null)
        {
            production.ReportDiagnostic(
                Diagnostic.Create(
                    Unsupported,
                    Location.None,
                    $"Companion for '{input.Name}' was not found."
                )
            );
            return;
        }

        var invalid = false;
        if (companion.Members.OfType<ConstructorDeclarationSyntax>().Any())
        {
            production.ReportDiagnostic(
                Diagnostic.Create(
                    AuthoredConstructor,
                    companion.Identifier.GetLocation(),
                    input.Name
                )
            );
            invalid = true;
        }
        if (
            companion
                .AttributeLists.SelectMany(static list => list.Attributes)
                .Any(static attribute =>
                    AttributeName(attribute).EndsWith("ComponentState", StringComparison.Ordinal)
                )
        )
        {
            production.ReportDiagnostic(
                Diagnostic.Create(
                    ComponentStateMisuse,
                    companion.Identifier.GetLocation(),
                    input.Name
                )
            );
            invalid = true;
        }
        var companionSetups = companion
            .Members.OfType<MethodDeclarationSyntax>()
            .Where(static method =>
                method.Identifier.ValueText == "Setup"
                && (method.Body is not null || method.ExpressionBody is not null)
            )
            .ToArray();
        if (companionSetups.Length + input.Setups.Length > 1)
        {
            production.ReportDiagnostic(
                Diagnostic.Create(DuplicateSetup, companion.Identifier.GetLocation(), input.Name)
            );
            invalid = true;
        }
        var states = companion
            .Members.OfType<PropertyDeclarationSyntax>()
            .Where(static property =>
                property
                    .AttributeLists.SelectMany(static list => list.Attributes)
                    .Any(static attribute =>
                        AttributeName(attribute).EndsWith("State", StringComparison.Ordinal)
                    )
            )
            .Select(State)
            .ToImmutableArray();
        if (states.Any(static state => state is null))
        {
            production.ReportDiagnostic(
                Diagnostic.Create(
                    Unsupported,
                    companion.Identifier.GetLocation(),
                    "[State] requires a partial auto-property and nameof(static initializer) in this probe."
                )
            );
            invalid = true;
        }
        if (invalid)
            return;
        production.AddSource(
            input.Name + ".final.g.cs",
            SourceText.From(
                Implementation(
                    input,
                    states.Cast<StateInfo>().ToImmutableArray(),
                    companionSetups.Length == 1
                ),
                Encoding.UTF8
            )
        );
    }

    private static string Implementation(
        Input input,
        ImmutableArray<StateInfo> states,
        bool hasCompanionSetup
    )
    {
        var builder = Header(input);
        builder.AppendLine($"{input.Accessibility} sealed partial class {input.Name}");
        builder.AppendLine("{");
        foreach (var state in states)
            StateMembers(builder, state);
        foreach (var field in input.Fields)
            StateMembers(builder, new StateInfo(field.Type, field.Name, field.Initializer));
        builder.AppendLine(
            "    public static partial ComponentRecipe Create() => Component.Define(\""
                + input.Name
                + "\", context =>"
        );
        builder.AppendLine("    {");
        builder.AppendLine($"        var state = new {input.Name}();");
        builder.AppendLine("        state.Initialize(context);");
        builder.AppendLine(
            $"        return ComponentRecipe.Create(\"{input.Name}.root\", static (_, _) => {{ }});"
        );
        builder.AppendLine("    });");
        builder.AppendLine("    private void Initialize(ComponentContext context)");
        builder.AppendLine("    {");
        foreach (var state in states)
            builder.AppendLine(
                $"        __state_{state.Name} = context.State({state.Initializer}, \"{input.Name}.{state.Name}\");"
            );
        foreach (var field in input.Fields)
            builder.AppendLine(
                $"        __state_{field.Name} = context.State({field.Initializer}, \"{input.Name}.{field.Name}\");"
            );
        if (hasCompanionSetup)
            builder.AppendLine("        Setup(context);");
        builder.AppendLine("    }");
        foreach (var method in input.Methods)
            builder.AppendLine(Indent(Partial(method, declaration: false), 4));
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void StateMembers(StringBuilder builder, StateInfo state)
    {
        builder.AppendLine($"    private Signal<{state.Type}>? __state_{state.Name};");
        builder.AppendLine($"    public partial {state.Type} {state.Name}");
        builder.AppendLine("    {");
        builder.AppendLine(
            $"        get => __state_{state.Name}?.Value ?? throw new InvalidOperationException(\"State is not mounted.\");"
        );
        builder.AppendLine(
            $"        set => (__state_{state.Name} ?? throw new InvalidOperationException(\"State is not mounted.\")).Value = value;"
        );
        builder.AppendLine("    }");
    }

    private static StateInfo? State(PropertyDeclarationSyntax property)
    {
        if (
            !property.Modifiers.Any(SyntaxKind.PartialKeyword)
            || property.AccessorList?.Accessors.Any(static accessor =>
                accessor.Body is not null || accessor.ExpressionBody is not null
            ) != false
        )
            return null;
        var attribute = property
            .AttributeLists.SelectMany(static list => list.Attributes)
            .Single(static value =>
                AttributeName(value).EndsWith("State", StringComparison.Ordinal)
            );
        var initializer = attribute
            .ArgumentList?.Arguments.SingleOrDefault(static argument =>
                argument.NameEquals?.Name.Identifier.ValueText == "Initializer"
            )
            ?.Expression;
        if (
            initializer
            is not InvocationExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
                ArgumentList.Arguments.Count: 1
            } invocation
        )
            return null;
        var method = invocation.ArgumentList.Arguments[0].Expression.ToString();
        return new StateInfo(
            property.Type.ToString(),
            property.Identifier.ValueText,
            inputName(property, method)
        );

        static string inputName(PropertyDeclarationSyntax property, string method) =>
            property.Parent is ClassDeclarationSyntax owner
                ? owner.Identifier.ValueText + "." + method + "(context)"
                : method + "(context)";
    }

    private static string AttributeName(AttributeSyntax attribute) =>
        attribute.Name.ToString().Replace("Attribute", string.Empty, StringComparison.Ordinal);

    private static StringBuilder Header(Input input)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#nullable enable");
        foreach (var @using in input.Usings)
            builder.AppendLine("using " + @using.Trim().TrimEnd(';') + ";");
        builder.AppendLine("using Lucent.Core;");
        builder.AppendLine();
        builder.AppendLine("namespace " + input.Namespace + ";");
        builder.AppendLine();
        return builder;
    }

    private static string Partial(MethodDeclarationSyntax method, bool declaration)
    {
        var modifiers = method.Modifiers.Any(SyntaxKind.PartialKeyword)
            ? method.Modifiers
            : method.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword));
        var result = method.WithModifiers(modifiers);
        if (declaration)
            result = result
                .WithBody(null)
                .WithExpressionBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        return result.NormalizeWhitespace().ToFullString();
    }

    private static string Indent(string value, int spaces)
    {
        var prefix = new string(' ', spaces);
        return prefix + value.Replace("\n", "\n" + prefix, StringComparison.Ordinal);
    }

    private static DiagnosticDescriptor Error(string id, string title, string message) =>
        new(id, title, message, "A0.CompanionPrototype", DiagnosticSeverity.Error, true);

    private sealed record Input(
        string Path,
        string Source,
        string Namespace,
        string Name,
        string Accessibility,
        ImmutableArray<Field> Fields,
        ImmutableArray<MethodDeclarationSyntax> Methods,
        ImmutableArray<LuiMemberSyntax> Setups,
        ImmutableArray<string> Errors
    )
    {
        public ImmutableArray<string> Usings { get; init; } = ["System"];
    }

    private sealed record Field(string Type, string Name, string Initializer);

    private sealed record StateInfo(string Type, string Name, string Initializer);
}
