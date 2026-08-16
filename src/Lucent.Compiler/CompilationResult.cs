using Lucent.Compiler.Syntax;
using Lucent.Compiler.CodeGeneration;
using Lucent.Compiler.Parsing;
using Lucent.Compiler.Semantics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynForEachStatementSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ForEachStatementSyntax;
using RoslynLambdaExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.LambdaExpressionSyntax;

namespace Lucent.Compiler;

public sealed record CompilationResult(
    CompilationUnitSyntax? Syntax,
    string? GeneratedSource,
    IReadOnlyList<LucentDiagnostic> Diagnostics)
{
    public IReadOnlyList<LucentSemanticSymbol> Symbols { get; init; } = [];

    public bool Succeeded =>
        GeneratedSource is not null &&
        Diagnostics.All(diagnostic =>
            diagnostic.Severity != LucentDiagnosticSeverity.Error);
}

internal sealed class ComponentSemanticAnalysis
{
    private ComponentSemanticAnalysis(
        CompilationUnitSyntax syntax,
        IReadOnlyList<LucentDiagnostic> diagnostics,
        string sourcePath,
        ProjectSemanticCompilation project,
        NativeSymbolResolver resolver,
        BoundComponentModel? model,
        IReadOnlyList<LucentSemanticSymbol> symbols,
        IReadOnlyList<BoundIslandScope> editorScopes,
        IReadOnlyList<BoundEditorVariable> editorVariables)
    {
        Syntax = syntax;
        Diagnostics = diagnostics;
        SourcePath = sourcePath;
        Project = project;
        Resolver = resolver;
        Model = model;
        Symbols = symbols;
        EditorScopes = editorScopes;
        EditorVariables = editorVariables;
    }

    public CompilationUnitSyntax Syntax { get; }
    public IReadOnlyList<LucentDiagnostic> Diagnostics { get; }
    public string SourcePath { get; }
    public ProjectSemanticCompilation Project { get; }
    public NativeSymbolResolver Resolver { get; }
    public BoundComponentModel? Model { get; }
    public IReadOnlyList<LucentSemanticSymbol> Symbols { get; }
    public IReadOnlyList<BoundIslandScope> EditorScopes { get; }
    public IReadOnlyList<BoundEditorVariable> EditorVariables { get; }

    public static ComponentSemanticAnalysis Create(
        string sourceText,
        string sourcePath,
        LucentProjectContext? projectContext)
    {
        var parser = new Parser(sourceText, sourcePath);
        var syntax = parser.Parse();
        var diagnostics = parser.Diagnostics.ToList();
        var project = new ProjectSemanticCompilation(
            syntax.NamespaceName,
            syntax.AllUsings.Select(directive => directive.Text).ToArray(),
            projectContext);
        var resolver = new NativeSymbolResolver(project);
        BoundComponentModel? model = null;
        IReadOnlyList<LucentSemanticSymbol> symbols = [];
        IReadOnlyList<BoundIslandScope> editorScopes = [];
        IReadOnlyList<BoundEditorVariable> editorVariables = [];
        var bag = new DiagnosticBag(new SourceDocument(sourceText, sourcePath));
        var binder = new GeneralBinder(bag, projectContext, project);
        model = binder.Bind(syntax);
        diagnostics.AddRange(bag.Items);
        symbols = binder.Symbols;
        editorScopes = binder.EditorScopes;
        editorVariables = binder.EditorVariables;

        return new ComponentSemanticAnalysis(
            syntax, diagnostics, sourcePath, project, resolver, model, symbols, editorScopes,
            editorVariables);
    }
}

internal sealed record BoundIslandScope(
    SourceSpan Span,
    IReadOnlyList<BoundLocal> Locals,
    BoundIslandSemanticContext Context,
    CSharpIslandRole Role);

internal enum BoundEditorVariableKind { Local, State, Computed }

internal sealed record BoundEditorVariable(
    string Name,
    ITypeSymbol Type,
    BoundEditorVariableKind Kind,
    string Display);

internal sealed class BoundIslandSemanticContext(
    SemanticModel model,
    TextSpan mapping,
    SourceSpan sourceSpan,
    IReadOnlyDictionary<string, ITypeSymbol> localTypes)
{
    public IReadOnlyList<BoundLocal> LookupLocals(int sourceOffset)
    {
        var position = mapping.Start + Math.Clamp(
            sourceOffset - sourceSpan.Start,
            0,
            mapping.Length);
        var symbols = model.LookupSymbols(position)
            .Where(symbol => symbol is ILocalSymbol or IParameterSymbol)
            .Concat(model.SyntaxTree.GetRoot().FindToken(position).Parent?
                .AncestorsAndSelf()
                .OfType<RoslynForEachStatementSyntax>()
                .Select(statement => model.GetDeclaredSymbol(statement))
                .OfType<ILocalSymbol>() ?? [])
            .Concat(model.SyntaxTree.GetRoot().FindToken(position).Parent?
                .AncestorsAndSelf()
                .OfType<RoslynLambdaExpressionSyntax>()
                .SelectMany(lambda => lambda switch
                {
                    Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedLambdaExpressionSyntax parenthesized =>
                        parenthesized.ParameterList.Parameters,
                    Microsoft.CodeAnalysis.CSharp.Syntax.SimpleLambdaExpressionSyntax simple =>
                        [simple.Parameter],
                    _ => [],
                })
                .Select(parameter => model.GetDeclaredSymbol(parameter))
                .OfType<IParameterSymbol>() ?? [])
            .Select(symbol => new BoundLocal(
                symbol.Name,
                localTypes.TryGetValue(symbol.Name, out var type)
                    ? type
                    : symbol switch
                    {
                        ILocalSymbol local => local.Type,
                        IParameterSymbol parameter => parameter.Type,
                        _ => throw new InvalidOperationException(),
                    }))
            .GroupBy(local => local.Name, StringComparer.Ordinal)
            .Select(group => group.OrderBy(local =>
                local.Type.TypeKind == TypeKind.Dynamic).First())
            .ToArray();
        return symbols;
    }
}
