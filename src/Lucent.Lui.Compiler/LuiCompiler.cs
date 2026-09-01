using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Lucent.Lui.Compiler;

/// <summary>Roslyn-bound lowering from recovered <c>.lui</c> syntax to generated C# recipe code.</summary>
/// <remarks>Use from build or editor tooling only. The output source and map have no runtime dependency or runtime role.</remarks>
public static class LuiCompiler
{
    private static readonly string[] ImplicitStylePropertyTypes =
    [
        "Lucent.Core.LayoutProperties",
        "Lucent.Core.VisualProperties",
        "Lucent.Core.TypographyProperties",
        "Lucent.Core.InputProperties",
    ];
    private static readonly SymbolDisplayFormat FullyQualifiedNullableFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );
    private static readonly SymbolDisplayFormat FullyQualifiedMemberFormat = SymbolDisplayFormat
        .FullyQualifiedFormat.WithMemberOptions(SymbolDisplayMemberOptions.IncludeContainingType)
        .WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
        );

    /// <summary>Binds a parsed document against a Roslyn compilation and produces generated C# only when diagnostics are absent.</summary>
    /// <param name="document">Recovered syntax whose spans identify the authored <c>.lui</c> text.</param>
    /// <param name="compilation">Current Roslyn compilation used for component and expression binding.</param>
    /// <param name="identity">Host freshness inputs; the compiler snapshots the actual Roslyn state before publication.</param>
    /// <returns>Generated source, source map, diagnostics, and the identity that must match before publishing.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public static LuiCompilationResult Compile(
        LuiDocumentSyntax document,
        Compilation compilation,
        LuiFreshnessIdentity identity
    )
    {
        if (document is null)
            throw new ArgumentNullException(nameof(document));
        if (compilation is null)
            throw new ArgumentNullException(nameof(compilation));
        if (identity is null)
            throw new ArgumentNullException(nameof(identity));
        identity = Snapshot(identity, compilation);
        var rootTokens = RootTokens(compilation, identity.RootNamespace);
        var diagnostics = new List<LuiDiagnostic>(document.Diagnostics);
        var writer = new Writer(document, identity, null, []);
        if (diagnostics.Count == 0 && document.Component is not null)
            writer.Document(diagnostics);
        if (diagnostics.Count != 0)
            return new LuiCompilationResult(
                identity,
                null,
                new LuiSourceMap(identity, writer.Entries),
                diagnostics.OrderBy(item => item.Span.Start).ToArray()
            );

        var parseOptions =
            (CSharpParseOptions?)compilation.SyntaxTrees.FirstOrDefault()?.Options
            ?? CSharpParseOptions.Default;
        var probeTree = CSharpSyntaxTree.ParseText(writer.Text, parseOptions, identity.HintName);
        var probeCompilation = compilation.AddSyntaxTrees(probeTree);
        var probeModel = probeCompilation.GetSemanticModel(probeTree);
        var probeMap = new LuiSourceMap(identity, writer.Entries);
        var componentWriter = new Writer(document, identity, null, ["Lucent.Core.Components"]);
        componentWriter.Document(diagnostics);
        var componentTree = CSharpSyntaxTree.ParseText(
            componentWriter.Text,
            parseOptions,
            identity.HintName + ".components.g.cs"
        );
        var componentCompilation = compilation.AddSyntaxTrees(componentTree);
        var componentModel = componentCompilation.GetSemanticModel(componentTree);
        var componentMap = new LuiSourceMap(identity, componentWriter.Entries);
        var propertyWriter = new Writer(document, identity, null, ImplicitStylePropertyTypes);
        propertyWriter.Document(diagnostics);
        var propertyTree = CSharpSyntaxTree.ParseText(
            propertyWriter.Text,
            parseOptions,
            identity.HintName + ".properties.g.cs"
        );
        var propertyCompilation = compilation.AddSyntaxTrees(propertyTree);
        var propertyModel = propertyCompilation.GetSemanticModel(propertyTree);
        var propertyMap = new LuiSourceMap(identity, propertyWriter.Entries);
        IReadOnlyDictionary<int, StyleValuePlan> styleValues =
            new Dictionary<int, StyleValuePlan>();
        var tokenExpressions = new HashSet<int>();
        if (rootTokens is not null)
        {
            var tokenWriter = new Writer(document, identity, null, [rootTokens.ToDisplayString()]);
            tokenWriter.Document(diagnostics);
            var tokenTree = CSharpSyntaxTree.ParseText(
                tokenWriter.Text,
                parseOptions,
                identity.HintName + ".tokens.g.cs"
            );
            var tokenCompilation = compilation.AddSyntaxTrees(tokenTree);
            var tokenModel = tokenCompilation.GetSemanticModel(tokenTree);
            var tokenMap = new LuiSourceMap(identity, tokenWriter.Entries);
            styleValues = StyleValuePlans(
                tokenModel,
                tokenTree,
                tokenMap,
                tokenWriter,
                rootTokens,
                tokenExpressions
            );
            TokenAmbiguityDiagnostics(
                tokenModel,
                tokenTree,
                tokenMap,
                tokenWriter,
                rootTokens,
                diagnostics
            );
        }
        var contentPlans = ContentPlans(
                probeModel,
                probeTree,
                probeMap,
                writer,
                document,
                identity,
                diagnostics
            )
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var implicitComponentDiagnostics = new List<LuiDiagnostic>();
        foreach (
            var pair in ContentPlans(
                componentModel,
                componentTree,
                componentMap,
                componentWriter,
                document,
                identity,
                implicitComponentDiagnostics
            )
        )
            contentPlans[pair.Key] = pair.Value;
        foreach (
            var diagnostic in implicitComponentDiagnostics.Where(candidate =>
                !diagnostics.Any(existing =>
                    existing.Id == candidate.Id
                    && existing.Span.Start == candidate.Span.Start
                    && existing.Span.Length == candidate.Span.Length
                    && existing.Message == candidate.Message
                )
            )
        )
            diagnostics.Add(diagnostic);
        var plans = new BindingPlans(
            ComponentPlans(componentModel, componentTree, componentMap, componentWriter),
            contentPlans,
            PropertyPlans(propertyModel, propertyTree, propertyMap, propertyWriter),
            styleValues,
            tokenExpressions,
            NullChecks(probeModel, probeTree, document)
        );
        if (diagnostics.Count != 0)
            return new LuiCompilationResult(
                identity,
                null,
                probeMap,
                diagnostics.OrderBy(item => item.Span.Start).ToArray()
            );
        writer = new Writer(document, identity, plans, []);
        writer.Document(diagnostics);
        var map = new LuiSourceMap(identity, writer.Entries);
        if (diagnostics.Count != 0)
            return new LuiCompilationResult(
                identity,
                null,
                map,
                diagnostics.OrderBy(item => item.Span.Start).ToArray()
            );

        var tree = CSharpSyntaxTree.ParseText(writer.Text, parseOptions, identity.HintName);
        var bound = compilation.AddSyntaxTrees(tree);
        var model = bound.GetSemanticModel(tree);
        foreach (
            var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
        )
        {
            var mapped = map.Entries.FirstOrDefault(entry =>
                !entry.Hidden
                && entry.Generated.Start == invocation.Expression.SpanStart
                && entry.Generated.Length == invocation.Expression.Span.Length
                && writer.ElementNames.Contains(entry.Source.Start)
            );
            if (mapped is null)
                continue;
            if (ComponentMethods(model, invocation).Length == 0)
                diagnostics.Add(
                    new LuiDiagnostic(
                        "LUI2001",
                        "Element tag '"
                            + invocation.Expression
                            + "' must resolve to an accessible static [LucentComponent] method returning ComponentRecipe.",
                        mapped.Source
                    )
                );
        }
        foreach (
            var diagnostic in bound
                .GetDiagnostics()
                .Where(item =>
                    item.Severity >= DiagnosticSeverity.Warning && item.Location.SourceTree == tree
                )
        )
        {
            var generated = new LuiSpan(
                diagnostic.Location.SourceSpan.Start,
                diagnostic.Location.SourceSpan.Length
            );
            var source = Translate(map, generated) ?? document.Component!.Span;
            diagnostics.Add(
                new LuiDiagnostic(
                    "LUI2000",
                    diagnostic.GetMessage(CultureInfo.InvariantCulture),
                    source
                )
            );
        }
        RejectProhibitedOperations(model, tree, map, diagnostics);
        return new LuiCompilationResult(
            identity,
            diagnostics.Count == 0 ? writer.Text : null,
            map,
            diagnostics.OrderBy(item => item.Span.Start).ToArray()
        );
    }

    private static void RejectProhibitedOperations(
        SemanticModel model,
        SyntaxTree tree,
        LuiSourceMap map,
        List<LuiDiagnostic> diagnostics
    )
    {
        var rejected = new HashSet<int>();
        foreach (var expression in tree.GetRoot().DescendantNodes().OfType<ExpressionSyntax>())
        {
            var entry = map.FromGenerated(new LuiSpan(expression.SpanStart, expression.Span.Length))
                .FirstOrDefault(item => !item.Hidden && item.Kind == LuiMapKind.Expression);
            if (
                entry is null
                || !IsProhibitedOperation(model, expression)
                || !rejected.Add(entry.Source.Start)
            )
                continue;
            diagnostics.Add(
                new LuiDiagnostic(
                    "LUI2007",
                    "Expression islands cannot use reflection or runtime compilation APIs.",
                    entry.Source
                )
            );
        }
    }

    private static bool IsProhibitedOperation(SemanticModel model, ExpressionSyntax expression)
    {
        if (expression is TypeOfExpressionSyntax)
            return true;
        var info = model.GetSymbolInfo(expression);
        if (IsProhibitedSymbol(info.Symbol) || info.CandidateSymbols.Any(IsProhibitedSymbol))
            return true;
        return IsProhibitedType(model.GetTypeInfo(expression).Type)
            || IsProhibitedType(model.GetTypeInfo(expression).ConvertedType);
    }

    private static bool IsProhibitedSymbol(ISymbol? symbol)
    {
        if (symbol is IAliasSymbol alias)
            symbol = alias.Target;
        if (
            symbol is IMethodSymbol operation
            && (
                (
                    operation.Name == "GetType"
                    && operation.ContainingType.SpecialType == SpecialType.System_Object
                )
                || (
                    operation.Name == "DynamicInvoke"
                    && operation.ContainingType.SpecialType == SpecialType.System_Delegate
                )
                || (
                    operation.Name == "Compile"
                    && operation.ContainingType.ContainingNamespace.ToDisplayString()
                        == "System.Linq.Expressions"
                )
            )
        )
            return true;
        var type = symbol switch
        {
            IMethodSymbol method => method.ContainingType,
            IPropertySymbol property => property.ContainingType,
            IFieldSymbol field => field.ContainingType,
            IEventSymbol @event => @event.ContainingType,
            INamedTypeSymbol named => named,
            _ => null,
        };
        return IsProhibitedType(type);
    }

    private static bool IsProhibitedType(ITypeSymbol? type)
    {
        if (type is null)
            return false;
        if (type.TypeKind == TypeKind.Dynamic)
            return true;
        if (type is not INamedTypeSymbol named)
            return false;
        var ns = named.ContainingNamespace.ToDisplayString();
        return ns == "System.Reflection"
            || ns.StartsWith("System.Reflection.", StringComparison.Ordinal)
            || ns == "Microsoft.CodeAnalysis"
            || ns.StartsWith("Microsoft.CodeAnalysis.", StringComparison.Ordinal)
            || ns == "System.CodeDom.Compiler"
            || ns.StartsWith("System.CodeDom.Compiler.", StringComparison.Ordinal)
            || named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                is "global::System.Type"
                    or "global::System.Activator"
            || IsCodeDomProvider(named);
    }

    private static bool IsCodeDomProvider(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (
                current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                == "global::System.CodeDom.Compiler.CodeDomProvider"
            )
                return true;
        }
        return false;
    }

    /// <summary>Derives a freshness identity from the actual Roslyn snapshot rather than host-provided generation strings.</summary>
    /// <param name="identity">Host-provided document and project identity inputs.</param>
    /// <param name="compilation">Compilation whose trees, references, options, and global usings are fingerprinted.</param>
    /// <returns>An immutable identity suitable for stale-output rejection.</returns>
    public static LuiFreshnessIdentity Snapshot(
        LuiFreshnessIdentity identity,
        Compilation compilation
    )
    {
        var parse =
            compilation
                .SyntaxTrees.Select(tree => tree.Options)
                .OfType<CSharpParseOptions>()
                .FirstOrDefault()
            ?? CSharpParseOptions.Default;
        var trees = compilation
            .SyntaxTrees.OrderBy(tree => tree.FilePath, StringComparer.Ordinal)
            .Select(tree =>
                (tree.FilePath ?? "")
                + "\0"
                + LuiDocumentIdentity.Hash(tree.GetText().ToString())
                + "\0"
                + ParseOptionsIdentity(tree.Options)
            );
        var references = compilation
            .References.OrderBy(reference => reference.Display, StringComparer.Ordinal)
            .Select(reference => ReferenceIdentity(compilation, reference));
        var globals = compilation
            .SyntaxTrees.SelectMany(tree =>
                tree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>()
            )
            .Where(@using => @using.GlobalKeyword.RawKind != 0)
            .OrderBy(@using => @using.ToString(), StringComparer.Ordinal)
            .Select(@using => @using.ToString());
        return new LuiFreshnessIdentity(
            identity.ProjectEpoch,
            identity.ProjectIdentity,
            identity.Document,
            identity.DocumentVersion,
            LuiDocumentIdentity.Hash(String.Join("\n", trees) + "\0" + compilation.Options),
            identity.SiblingIndexGeneration,
            parse.LanguageVersion.ToString(),
            typeof(LuiCompiler).Assembly.GetName().Version?.ToString() ?? "unknown",
            LuiDocumentIdentity.Hash(String.Join("\n", references)),
            LuiDocumentIdentity.Hash(String.Join("\n", globals)),
            identity.Options,
            identity.Defines,
            identity.RootNamespace
        );
    }

    private static INamedTypeSymbol? RootTokens(Compilation compilation, string rootNamespace)
    {
        if (String.IsNullOrWhiteSpace(rootNamespace))
            return null;
        var tokens = compilation.GetTypeByMetadataName(rootNamespace + ".Tokens");
        return tokens is { IsStatic: true } ? tokens : null;
    }

    private static string ParseOptionsIdentity(ParseOptions options)
    {
        if (options is not CSharpParseOptions parse)
            return options.ToString();
        return parse.Kind
            + "\0"
            + parse.LanguageVersion
            + "\0"
            + parse.DocumentationMode
            + "\0"
            + String.Join(
                "\u001f",
                parse.PreprocessorSymbolNames.OrderBy(symbol => symbol, StringComparer.Ordinal)
            )
            + "\0"
            + String.Join(
                "\u001f",
                parse
                    .Features.OrderBy(feature => feature.Key, StringComparer.Ordinal)
                    .ThenBy(feature => feature.Value, StringComparer.Ordinal)
                    .Select(feature => feature.Key + "=" + feature.Value)
            );
    }

    private static string ReferenceIdentity(Compilation compilation, MetadataReference reference)
    {
        var symbol = compilation.GetAssemblyOrModuleSymbol(reference);
        var semanticIdentity = symbol switch
        {
            IAssemblySymbol assembly => assembly.Identity + "\0" + MetadataVersion(reference),
            IModuleSymbol module => module.Name + "\0" + MetadataVersion(reference),
            _ => "unresolved",
        };
        return (reference.Display ?? "")
            + "\0"
            + reference.Properties.Kind
            + "\0"
            + String.Join(
                ",",
                reference.Properties.Aliases.OrderBy(alias => alias, StringComparer.Ordinal)
            )
            + "\0"
            + semanticIdentity;
    }

    private static string MetadataVersion(MetadataReference reference) =>
        reference is not PortableExecutableReference portable
            ? ""
            : portable.GetMetadata() switch
            {
                AssemblyMetadata assembly => String.Join(
                    ",",
                    assembly
                        .GetModules()
                        .Select(module => module.GetModuleVersionId())
                        .OrderBy(id => id)
                ),
                ModuleMetadata module => module.GetModuleVersionId().ToString(),
                _ => "",
            };

    private static LuiSpan? Translate(LuiSourceMap map, LuiSpan generated)
    {
        var entry = map.FromGenerated(generated)
            .Where(item => !item.Hidden && item.Source.Start >= 0 && item.Generated.Length != 0)
            .OrderBy(item => item.Kind == LuiMapKind.Expression ? 0 : 1)
            .ThenBy(item => item.Generated.Length)
            .FirstOrDefault();
        if (entry is null)
            return null;
        var start =
            generated.Start <= entry.Generated.Start
                ? entry.Source.Start
                : entry.Source.Start
                    + (int)(
                        (long)(generated.Start - entry.Generated.Start)
                        * entry.Source.Length
                        / entry.Generated.Length
                    );
        var endOffset = Math.Min(generated.End, entry.Generated.End) - entry.Generated.Start;
        var end =
            entry.Source.Start
            + (int)((long)endOffset * entry.Source.Length / entry.Generated.Length);
        return new LuiSpan(start, Math.Max(0, end - start));
    }

    private static bool IsComponent(IMethodSymbol method) =>
        method.IsStatic
        && method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            == "global::Lucent.Core.ComponentRecipe"
        && method
            .GetAttributes()
            .Any(attribute =>
                attribute.AttributeClass?.ToDisplayString()
                == "Lucent.Core.LucentComponentAttribute"
            );

    private static IMethodSymbol[] ComponentMethods(
        SemanticModel model,
        InvocationExpressionSyntax invocation
    )
    {
        var info = model.GetSymbolInfo(invocation);
        return new[] { info.Symbol as IMethodSymbol }
            .Concat(info.CandidateSymbols.OfType<IMethodSymbol>())
            .Concat(model.GetMemberGroup(invocation.Expression).OfType<IMethodSymbol>())
            .Concat(
                invocation.Expression is IdentifierNameSyntax identifier
                    ? model
                        .LookupSymbols(
                            invocation.Expression.SpanStart,
                            name: identifier.Identifier.ValueText
                        )
                        .OfType<IMethodSymbol>()
                    : Enumerable.Empty<IMethodSymbol>()
            )
            .Concat(
                invocation.Expression is IdentifierNameSyntax builtIn
                    ? model
                        .Compilation.GetTypeByMetadataName("Lucent.Core.Components")
                        ?.GetMembers(builtIn.Identifier.ValueText)
                        .OfType<IMethodSymbol>()
                        ?? Enumerable.Empty<IMethodSymbol>()
                    : Enumerable.Empty<IMethodSymbol>()
            )
            .Where(method => method is not null && IsComponent(method!))
            .Cast<IMethodSymbol>()
            .GroupBy(
                method => method.ToDisplayString(FullyQualifiedNullableFormat),
                StringComparer.Ordinal
            )
            .Select(group => group.First())
            .ToArray();
    }

    private static string ComponentName(IMethodSymbol method)
    {
        var name = method.Name;
        if (
            SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
            || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None
        )
            name = "@" + name;
        return method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            + "."
            + name;
    }

    private static IReadOnlyDictionary<int, string> ComponentPlans(
        SemanticModel model,
        SyntaxTree tree,
        LuiSourceMap map,
        Writer writer
    )
    {
        var plans = new Dictionary<int, string>();
        foreach (
            var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
        )
        {
            var mapped = map.FromGenerated(
                    new LuiSpan(invocation.Expression.SpanStart, invocation.Expression.Span.Length)
                )
                .FirstOrDefault(entry => writer.ElementNames.Contains(entry.Source.Start));
            if (mapped is null)
                continue;
            var names = ComponentMethods(model, invocation)
                .Select(ComponentName)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (names.Length == 1)
                plans[mapped.Source.Start] = names[0];
        }
        return plans;
    }

    private static IReadOnlyDictionary<int, ContentPlan> ContentPlans(
        SemanticModel model,
        SyntaxTree tree,
        LuiSourceMap map,
        Writer writer,
        LuiDocumentSyntax document,
        LuiFreshnessIdentity identity,
        List<LuiDiagnostic> diagnostics
    )
    {
        var plans = new Dictionary<int, ContentPlan>();
        foreach (
            var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
        )
        {
            var mapped = map.FromGenerated(
                    new LuiSpan(invocation.Expression.SpanStart, invocation.Expression.Span.Length)
                )
                .FirstOrDefault(entry => writer.ElementNames.Contains(entry.Source.Start));
            if (mapped is null || plans.ContainsKey(mapped.Source.Start))
                continue;
            var info = model.GetSymbolInfo(invocation);
            var methods = new[] { info.Symbol as IMethodSymbol }
                .Concat(info.CandidateSymbols.OfType<IMethodSymbol>())
                .Where(method => method is not null && IsComponent(method!))
                .Cast<IMethodSymbol>()
                .ToArray();
            if (methods.Length == 0)
                methods = ComponentMethods(model, invocation);
            if (methods.Length == 0)
                continue;
            var defaults = new List<IParameterSymbol>();
            foreach (var method in methods)
            {
                var annotated = method
                    .Parameters.Where(parameter =>
                        parameter
                            .GetAttributes()
                            .Any(attribute =>
                                attribute.AttributeClass?.ToDisplayString()
                                == "Lucent.Core.DefaultContentAttribute"
                            )
                    )
                    .ToArray();
                if (annotated.Length > 1)
                    diagnostics.Add(
                        new LuiDiagnostic(
                            "LUI2005",
                            "A [LucentComponent] method may declare only one [DefaultContent] parameter.",
                            mapped.Source
                        )
                    );
                defaults.AddRange(annotated);
            }
            if (defaults.Count == 0)
                continue;
            var candidates = defaults
                .GroupBy(
                    parameter =>
                        parameter.ContainingSymbol.ToDisplayString(
                            SymbolDisplayFormat.FullyQualifiedFormat
                        )
                        + "\0"
                        + parameter.Name,
                    StringComparer.Ordinal
                )
                .Select(group => group.First())
                .ToArray();
            if (ElementChildren(document, mapped.Source.Start).Count == 0 && candidates.Length != 1)
                continue;
            var applicable = candidates
                .Where(candidate =>
                    BindsContentCandidate(
                        model.Compilation,
                        document,
                        identity,
                        mapped.Source.Start,
                        candidate
                    )
                )
                .ToArray();
            if (applicable.Length == 1)
                plans[mapped.Source.Start] = ContentPlan.For(applicable[0]);
        }
        return plans;
    }

    private static IReadOnlyList<LuiBodySyntax> ElementChildren(
        LuiDocumentSyntax document,
        int start
    )
    {
        var pending = new Stack<LuiBodySyntax>(
            document.Component?.Body.Reverse() ?? Enumerable.Empty<LuiBodySyntax>()
        );
        while (pending.Count != 0)
        {
            var node = pending.Pop();
            if (node is LuiElementSyntax element)
            {
                if (element.Name.Span.Start == start)
                    return element.Children.Where(child => child is not LuiCommentSyntax).ToArray();
                foreach (var child in element.Children.Reverse())
                    pending.Push(child);
            }
            else if (node is LuiIfSyntax conditional)
            {
                foreach (var child in conditional.ElseBody.Reverse())
                    pending.Push(child);
                foreach (var child in conditional.ThenBody.Reverse())
                    pending.Push(child);
            }
            else if (node is LuiForEachSyntax loop)
                foreach (var child in loop.Body.Reverse())
                    pending.Push(child);
        }
        return Array.Empty<LuiBodySyntax>();
    }

    private static bool BindsContentCandidate(
        Compilation compilation,
        LuiDocumentSyntax document,
        LuiFreshnessIdentity identity,
        int elementStart,
        IParameterSymbol parameter
    )
    {
        var candidate = ContentPlan.For(parameter);
        var writer = new Writer(
            document,
            identity,
            new BindingPlans(
                new Dictionary<int, string>(),
                new Dictionary<int, ContentPlan> { [elementStart] = candidate },
                new Dictionary<int, StylePropertyPlan>(),
                new Dictionary<int, StyleValuePlan>(),
                new HashSet<int>(),
                new HashSet<int>()
            ),
            [parameter.ContainingSymbol.ContainingType.ToDisplayString()]
        );
        var diagnostics = new List<LuiDiagnostic>();
        writer.Document(diagnostics);
        if (diagnostics.Count != 0)
            return false;
        var parse =
            (CSharpParseOptions?)compilation.SyntaxTrees.FirstOrDefault()?.Options
            ?? CSharpParseOptions.Default;
        var tree = CSharpSyntaxTree.ParseText(
            writer.Text,
            parse,
            identity.HintName + ".candidate.g.cs"
        );
        var bound = compilation
            .RemoveSyntaxTrees(
                compilation.SyntaxTrees.Where(item => item.FilePath == identity.HintName)
            )
            .AddSyntaxTrees(tree);
        var model = bound.GetSemanticModel(tree);
        var invocation = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(item =>
                writer.ElementNames.Contains(elementStart)
                && writer.Entries.Any(entry =>
                    !entry.Hidden
                    && entry.Source.Start == elementStart
                    && entry.Generated.Start == item.Expression.SpanStart
                    && entry.Generated.Length == item.Expression.Span.Length
                )
            );
        var target = invocation is null
            ? null
            : model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        return target is not null
            && target.Parameters.Any(item =>
                item.Name == parameter.Name
                && item.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    == parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                && item.GetAttributes()
                    .Any(attribute =>
                        attribute.AttributeClass?.ToDisplayString()
                        == "Lucent.Core.DefaultContentAttribute"
                    )
            );
    }

    private static IReadOnlyDictionary<int, StylePropertyPlan> PropertyPlans(
        SemanticModel model,
        SyntaxTree tree,
        LuiSourceMap map,
        Writer writer
    )
    {
        var plans = new Dictionary<int, StylePropertyPlan>();
        foreach (var name in tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (
                model.GetSymbolInfo(name).Symbol is not IFieldSymbol property
                || !IsStyleProperty(property.Type)
            )
                continue;
            var entry = map.Entries.FirstOrDefault(candidate =>
                !candidate.Hidden
                && candidate.Source.Start >= 0
                && candidate.Generated.Start <= name.SpanStart
                && candidate.Generated.End >= name.Span.End
            );
            if (entry is not null)
                plans[entry.Source.Start] = new StylePropertyPlan(
                    property.ToDisplayString(FullyQualifiedMemberFormat),
                    ((INamedTypeSymbol)property.Type)
                        .TypeArguments[0]
                        .ToDisplayString(FullyQualifiedNullableFormat)
                );
        }
        return plans;
    }

    private static bool IsStyleProperty(ITypeSymbol property)
    {
        var type = property as INamedTypeSymbol;
        return type is not null
            && type.Name == "Property"
            && type.Arity == 1
            && type.ContainingNamespace.ToDisplayString() == "Lucent.Core";
    }

    private static IReadOnlyDictionary<int, StyleValuePlan> StyleValuePlans(
        SemanticModel model,
        SyntaxTree tree,
        LuiSourceMap map,
        Writer writer,
        INamedTypeSymbol? rootTokens,
        HashSet<int> tokenExpressions
    )
    {
        var plans = new Dictionary<int, StyleValuePlan>();
        if (rootTokens is null)
            return plans;
        foreach (var lambda in tree.GetRoot().DescendantNodes().OfType<LambdaExpressionSyntax>())
        {
            if (lambda.Body is not ExpressionSyntax body || !IsToken(model.GetTypeInfo(body).Type!))
                continue;
            var source = Translate(map, new LuiSpan(body.SpanStart, body.Span.Length));
            if (
                source is { } span
                && writer.StyleExpressionSpans.Any(style =>
                    style.Start <= span.Start && style.End >= span.End
                )
            )
                tokenExpressions.Add(span.Start);
        }
        foreach (var name in tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (name.Parent is MemberAccessExpressionSyntax member && member.Name == name)
                continue;
            var symbol = model.GetSymbolInfo(name).Symbol;
            var type = symbol switch
            {
                IFieldSymbol field when field.IsStatic => field.Type,
                IPropertySymbol property when property.IsStatic => property.Type,
                _ => null,
            };
            if (
                type is null
                || !IsToken(type)
                || symbol!.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    != rootTokens.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            )
                continue;
            var translated = Translate(map, new LuiSpan(name.SpanStart, name.Span.Length));
            if (
                translated is not { } source
                || !writer.StyleExpressionSpans.Any(span =>
                    span.Start <= source.Start && span.End >= source.End
                )
            )
                continue;
            plans[source.Start] = new StyleValuePlan(
                source,
                symbol!.ToDisplayString(FullyQualifiedMemberFormat)
            );
        }
        return plans;
    }

    private static void TokenAmbiguityDiagnostics(
        SemanticModel model,
        SyntaxTree tree,
        LuiSourceMap map,
        Writer writer,
        INamedTypeSymbol rootTokens,
        List<LuiDiagnostic> diagnostics
    )
    {
        foreach (
            var diagnostic in model
                .Compilation.GetDiagnostics()
                .Where(item =>
                    item.Location.SourceTree == tree && (item.Id == "CS0104" || item.Id == "CS0229")
                )
        )
        {
            var name = tree.GetRoot()
                .FindNode(diagnostic.Location.SourceSpan)
                .FirstAncestorOrSelf<IdentifierNameSyntax>();
            if (
                name is null
                || !model
                    .GetSymbolInfo(name)
                    .CandidateSymbols.Any(candidate => IsRootTokenMember(candidate, rootTokens))
            )
                continue;
            var source = Translate(
                map,
                new LuiSpan(
                    diagnostic.Location.SourceSpan.Start,
                    diagnostic.Location.SourceSpan.Length
                )
            );
            if (
                source is not { } span
                || !writer.StyleExpressionSpans.Any(style =>
                    style.Start <= span.Start && style.End >= span.End
                )
            )
                continue;
            diagnostics.Add(
                new LuiDiagnostic(
                    "LUI2000",
                    diagnostic.GetMessage(CultureInfo.InvariantCulture),
                    span
                )
            );
        }
    }

    private static bool IsRootTokenMember(ISymbol symbol, INamedTypeSymbol rootTokens)
    {
        var type = symbol switch
        {
            IFieldSymbol field when field.IsStatic => field.Type,
            IPropertySymbol property when property.IsStatic => property.Type,
            _ => null,
        };
        return type is not null
            && IsToken(type)
            && symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                == rootTokens.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static bool IsToken(ITypeSymbol symbol) =>
        symbol
            is INamedTypeSymbol
            {
                Name: "Token",
                Arity: 1,
                ContainingNamespace: { } containingNamespace,
            }
        && containingNamespace.ToDisplayString() == "Lucent.Core";

    private static HashSet<int> NullChecks(
        SemanticModel model,
        SyntaxTree tree,
        LuiDocumentSyntax document
    )
    {
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .SingleOrDefault(candidate =>
                candidate.Identifier.ValueText == document.Component!.Name.Text
            );
        if (method is null)
            return new HashSet<int>();
        return new HashSet<int>(
            method
                .ParameterList.Parameters.Select((parameter, index) => (parameter, index))
                .Where(item =>
                {
                    var type = model.GetDeclaredSymbol(item.parameter)?.Type;
                    return type is not null
                        && (type.IsReferenceType || type.TypeKind == TypeKind.TypeParameter)
                        && type.NullableAnnotation == NullableAnnotation.NotAnnotated;
                })
                .Select(item => item.index)
        );
    }

    private sealed class ContentPlan
    {
        internal ContentPlan(string name, bool isCollection)
        {
            Name = name;
            IsCollection = isCollection;
        }

        internal static ContentPlan For(IParameterSymbol parameter) =>
            new ContentPlan(
                parameter.Name,
                parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    == "global::Lucent.Core.ComponentContent"
            );

        internal string Name { get; }
        internal bool IsCollection { get; }
    }

    private sealed class StylePropertyPlan
    {
        internal StylePropertyPlan(string name, string valueType)
        {
            Name = name;
            ValueType = valueType;
        }

        internal string Name { get; }
        internal string ValueType { get; }
    }

    private sealed class StyleValuePlan
    {
        internal StyleValuePlan(LuiSpan source, string name)
        {
            Source = source;
            Name = name;
        }

        internal LuiSpan Source { get; }
        internal string Name { get; }
    }

    private sealed class BindingPlans
    {
        internal BindingPlans(
            IReadOnlyDictionary<int, string> components,
            IReadOnlyDictionary<int, ContentPlan> content,
            IReadOnlyDictionary<int, StylePropertyPlan> properties,
            IReadOnlyDictionary<int, StyleValuePlan> values,
            HashSet<int> tokenExpressions,
            HashSet<int> nullChecks
        )
        {
            Components = components;
            Content = content;
            Properties = properties;
            Values = values;
            TokenExpressions = tokenExpressions;
            NullChecks = nullChecks;
        }

        internal IReadOnlyDictionary<int, string> Components { get; }
        internal IReadOnlyDictionary<int, ContentPlan> Content { get; }
        internal IReadOnlyDictionary<int, StylePropertyPlan> Properties { get; }
        internal IReadOnlyDictionary<int, StyleValuePlan> Values { get; }
        internal HashSet<int> TokenExpressions { get; }
        internal HashSet<int> NullChecks { get; }
    }

    private sealed class Writer
    {
        private readonly LuiDocumentSyntax document;
        private readonly LuiFreshnessIdentity identity;
        private readonly StringBuilder text = new StringBuilder();
        private readonly BindingPlans? plans;
        private readonly IReadOnlyList<string> implicitStaticTypes;
        private int regionOrdinal;
        internal readonly List<LuiMapEntry> Entries = new List<LuiMapEntry>();
        internal readonly HashSet<int> ElementNames = new HashSet<int>();
        internal readonly HashSet<int> StylePropertyNames = new HashSet<int>();
        internal readonly List<LuiSpan> StyleExpressionSpans = new List<LuiSpan>();

        internal Writer(
            LuiDocumentSyntax document,
            LuiFreshnessIdentity identity,
            BindingPlans? plans,
            IReadOnlyList<string> implicitStaticTypes
        )
        {
            this.document = document;
            this.identity = identity;
            this.plans = plans;
            this.implicitStaticTypes = implicitStaticTypes;
        }

        internal string Text => text.ToString();

        // Every generated character is either related to a source span or explicitly hidden.
        private void Write(string value) => Hidden(value);

        private void Mapped(string value, LuiSpan source, LuiMapKind kind)
        {
            var start = text.Length;
            text.Append(value);
            Entries.Add(new LuiMapEntry(source, new LuiSpan(start, value.Length), kind, false));
        }

        private void Hidden(string value)
        {
            var start = text.Length;
            text.Append(value);
            Entries.Add(
                new LuiMapEntry(
                    new LuiSpan(-1, 0),
                    new LuiSpan(start, value.Length),
                    LuiMapKind.Scaffolding,
                    true
                )
            );
        }

        internal void Document(List<LuiDiagnostic> diagnostics)
        {
            Hidden(
                "// <auto-generated/>\n// lui-document: "
                    + identity.Document.LogicalPath
                    + "\n// lui-map: "
                    + identity.MapIdentity
                    + "\n#nullable enable\n#line hidden\n"
            );
            foreach (var type in implicitStaticTypes)
                Hidden("using static global::" + type + ";\n");
            var namespaceWritten = false;
            foreach (var node in document.TopLevel)
            {
                if (node is LuiTopLevelCommentSyntax comment)
                    Mark(comment.Span, LuiMapKind.Structure);
                else if (node is LuiUsingSyntax @using)
                    Directive("using", @using.Keyword, @using.Value, @using.Semicolon);
                else if (node is LuiNamespaceSyntax @namespace)
                {
                    Directive(
                        "namespace",
                        @namespace.Keyword,
                        @namespace.Value,
                        @namespace.Semicolon
                    );
                    namespaceWritten = true;
                }
            }
            if (!namespaceWritten)
                Hidden("namespace Lucent.Lui.Generated;\n");
            Hidden("\npublic static partial class Components\n{\n");
            foreach (var style in document.Styles)
                Style(style, diagnostics);
            Component(document.Component!, diagnostics);
            Hidden("}\n");
        }

        private void Directive(
            string keyword,
            LuiToken sourceKeyword,
            string value,
            LuiToken semicolon
        )
        {
            Mapped(keyword, sourceKeyword.Span, LuiMapKind.Structure);
            Hidden(" ");
            var start = document.Source.IndexOf(
                value,
                sourceKeyword.Span.End,
                StringComparison.Ordinal
            );
            if (start < 0)
                Hidden(value);
            else
                Mapped(value, new LuiSpan(start, value.Length), LuiMapKind.Symbol);
            Mapped(";", semicolon.Span, LuiMapKind.Structure);
            Hidden("\n");
        }

        private void Component(LuiComponentSyntax component, List<LuiDiagnostic> diagnostics)
        {
            Hidden(
                "    [global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"Lucent.Lui.Generator\", \""
                    + typeof(LuiCompiler).Assembly.GetName().Version
                    + "\")]\n    [global::Lucent.Core.LucentComponentAttribute]\n    "
            );
            if (component.Accessibility.IsMissing)
                Hidden("internal");
            else
                Mapped(
                    component.Accessibility.Text,
                    component.Accessibility.Span,
                    LuiMapKind.Symbol
                );
            Write(" static global::Lucent.Core.ComponentRecipe ");
            Mapped(component.Name.Text, component.Name.Span, LuiMapKind.Symbol);
            Write("(");
            for (var i = 0; i < component.Parameters.Count; i++)
            {
                if (i != 0)
                    Write(", ");
                var parameter = component.Parameters[i];
                Mapped(parameter.DeclarationText, parameter.Span, LuiMapKind.Symbol);
            }
            Hidden(")\n    {\n");
            foreach (
                var parameter in component.Parameters.Where(
                    (parameter, index) => plans?.NullChecks.Contains(index) == true
                )
            )
            {
                Hidden("        global::System.ArgumentNullException.ThrowIfNull(");
                Mapped(parameter.Name.Text, parameter.Name.Span, LuiMapKind.Symbol);
                Hidden(");\n");
            }
            Hidden("        return ");
            var root = component.Body.FirstOrDefault(node => !(node is LuiCommentSyntax));
            Comments(component.Body);
            if (root is LuiElementSyntax element)
                Element(element, diagnostics);
            else
                diagnostics.Add(
                    new LuiDiagnostic(
                        "LUI3000",
                        "A component root must be an element.",
                        component.Span
                    )
                );
            Hidden(";\n    }\n");
            Mark(component.ComponentKeyword.Span, LuiMapKind.Structure);
            Mark(component.OpenParameters.Span, LuiMapKind.Structure);
            Mark(component.CloseParameters.Span, LuiMapKind.Structure);
            Mark(component.OpenBrace.Span, LuiMapKind.Structure);
            Mark(component.CloseBrace.Span, LuiMapKind.Structure);
            foreach (var parameter in component.Parameters)
                Mark(parameter.Separator.Span, LuiMapKind.Structure);
        }

        private void Element(LuiElementSyntax element, List<LuiDiagnostic> diagnostics)
        {
            var name = element.Name.Text;
            ElementNames.Add(element.Name.Span.Start);
            var simpleName = name.Substring(
                Math.Max(name.LastIndexOf('.'), name.LastIndexOf(':')) + 1
            );
            if (!String.IsNullOrEmpty(simpleName) && Char.IsLower(simpleName[0]))
                diagnostics.Add(
                    new LuiDiagnostic(
                        "LUI2002",
                        "Element tags must be PascalCase.",
                        element.Name.Span
                    )
                );
            foreach (
                var group in element
                    .Attributes.GroupBy(attribute => attribute.Name.Text, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
            )
                diagnostics.Add(
                    new LuiDiagnostic(
                        "LUI2003",
                        "Duplicate attribute '" + group.Key + "'.",
                        group.First().Name.Span
                    )
                );
            var component =
                plans is not null
                && plans.Components.TryGetValue(element.Name.Span.Start, out var resolvedComponent)
                    ? resolvedComponent
                    : name;
            Mapped(component, element.Name.Span, LuiMapKind.Symbol);
            Write("(");
            var arguments = new List<Action>();
            foreach (var attribute in element.Attributes)
            {
                if (attribute.Name.Text == "name")
                    continue;
                arguments.Add(() =>
                {
                    Mapped(attribute.Name.Text, attribute.Name.Span, LuiMapKind.Symbol);
                    Write(": ");
                    Value(attribute.Value, diagnostics);
                });
            }
            var children = element.Children.Where(child => !(child is LuiCommentSyntax)).ToArray();
            var plan =
                plans is not null
                && plans.Content.TryGetValue(element.Name.Span.Start, out var resolved)
                    ? resolved
                    : null;
            if (plan is null)
            {
                if (children.Length != 0)
                    arguments.Add(() =>
                        Content(
                            "content",
                            children.Length != 1 || children[0] is not LuiTextSyntax,
                            children,
                            diagnostics
                        )
                    );
            }
            else if (children.Length == 0 && plan.IsCollection)
                arguments.Add(() => Content(plan.Name, true, children, diagnostics));
            else if (children.Length != 0)
                arguments.Add(() => Content(plan.Name, plan.IsCollection, children, diagnostics));
            for (var i = 0; i < arguments.Count; i++)
            {
                if (i != 0)
                    Write(", ");
                arguments[i]();
            }
            Write(")");
            var explicitName = element.Attributes.FirstOrDefault(attribute =>
                attribute.Name.Text == "name"
            );
            if (explicitName is not null)
            {
                Hidden(".");
                Mapped("Named", explicitName.Name.Span, LuiMapKind.Symbol);
                Hidden("(");
                Value(explicitName.Value, diagnostics);
                Hidden(")");
            }
            foreach (var attribute in element.Attributes)
            {
                Mark(attribute.EqualsToken.Span, LuiMapKind.Structure);
                TraceValue(attribute.Value);
            }
            foreach (var comment in element.Children.OfType<LuiCommentSyntax>())
                Mark(comment.Span, LuiMapKind.Structure);
            Mark(element.OpenAngle.Span, LuiMapKind.Structure);
            Mark(element.OpenCloseAngle.Span, LuiMapKind.Structure);
            Mark(element.SelfClosingSlash.Span, LuiMapKind.Structure);
            Mark(element.CloseOpenAngle.Span, LuiMapKind.Structure);
            Mark(element.CloseName.Span, LuiMapKind.Symbol);
            Mark(element.CloseAngle.Span, LuiMapKind.Structure);
        }

        private void Content(
            string name,
            bool collection,
            IReadOnlyList<LuiBodySyntax> children,
            List<LuiDiagnostic> diagnostics
        )
        {
            Write(name + ": ");
            if (!collection && children.Count == 1 && children[0] is LuiTextSyntax textNode)
            {
                Mapped(Escape(textNode.Text), textNode.Span, LuiMapKind.Expression);
                return;
            }
            if (!collection)
            {
                diagnostics.Add(
                    new LuiDiagnostic(
                        "LUI2004",
                        "Default content requires one scalar text value.",
                        children.Count == 0 ? new LuiSpan(0, 0) : children[0].Span
                    )
                );
                return;
            }
            Write("[");
            for (var i = 0; i < children.Count; i++)
            {
                if (i != 0)
                    Write(", ");
                ContentNode(children[i], diagnostics);
            }
            Write("]");
        }

        private void ContentNode(LuiBodySyntax node, List<LuiDiagnostic> diagnostics)
        {
            switch (node)
            {
                case LuiElementSyntax element:
                    Element(element, diagnostics);
                    break;
                case LuiIfSyntax conditional:
                    Conditional(conditional, diagnostics);
                    break;
                case LuiForEachSyntax loop:
                    ForEach(loop, diagnostics);
                    break;
                case LuiTextSyntax textNode:
                    diagnostics.Add(
                        new LuiDiagnostic(
                            "LUI3001",
                            "Text content cannot be mixed with component content.",
                            textNode.Span
                        )
                    );
                    break;
                default:
                    diagnostics.Add(
                        new LuiDiagnostic("LUI3001", "Unsupported content construct.", node.Span)
                    );
                    break;
            }
        }

        private void Conditional(LuiIfSyntax conditional, List<LuiDiagnostic> diagnostics)
        {
            Comments(conditional.ThenBody);
            Comments(conditional.ElseBody);
            var thenElement = SingleElement(conditional.ThenBody, diagnostics, conditional.Span);
            if (thenElement is null)
                return;
            Write("global::Lucent.Core.ContentRecipe.Switch(\"if-");
            Write((regionOrdinal++).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Write("\", () => { if (");
            Expression(conditional.Condition);
            Write(") return new global::Lucent.Core.ConditionalChoice(1, ");
            Element(thenElement, diagnostics);
            Write("); return new global::Lucent.Core.ConditionalChoice(2, ");
            if (
                !conditional.ElseKeyword.IsMissing
                && conditional.ElseBody.Any(node => node is not LuiCommentSyntax)
            )
            {
                var elseElement = SingleElement(
                    conditional.ElseBody,
                    diagnostics,
                    conditional.Span
                );
                if (elseElement is null)
                    return;
                Element(elseElement, diagnostics);
            }
            else
                Hidden("null");
            Write("); })");
            Mark(conditional.IfKeyword.Span, LuiMapKind.Structure);
            Mark(conditional.OpenCondition.Span, LuiMapKind.Structure);
            Mark(conditional.CloseCondition.Span, LuiMapKind.Structure);
            Mark(conditional.OpenBrace.Span, LuiMapKind.Structure);
            Mark(conditional.CloseBrace.Span, LuiMapKind.Structure);
            Mark(conditional.ElseKeyword.Span, LuiMapKind.Structure);
            Mark(conditional.ElseOpenBrace.Span, LuiMapKind.Structure);
            Mark(conditional.ElseCloseBrace.Span, LuiMapKind.Structure);
        }

        private void ForEach(LuiForEachSyntax loop, List<LuiDiagnostic> diagnostics)
        {
            Comments(loop.Body);
            var body = SingleElement(loop.Body, diagnostics, loop.Span);
            if (body is null)
                return;
            Write("global::Lucent.Core.ContentRecipe.ForEach(\"foreach-");
            Write((regionOrdinal++).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Write("\", () => ");
            Expression(loop.Source);
            Write(", ");
            Mapped(loop.Variable.Text, loop.Variable.Span, LuiMapKind.Symbol);
            Hidden(" => ");
            Expression(loop.Key);
            Write(", ");
            Mapped(loop.Variable.Text, loop.Variable.Span, LuiMapKind.Symbol);
            Hidden(" => ");
            Element(body, diagnostics);
            Write(")");
            Mark(loop.ForeachKeyword.Span, LuiMapKind.Structure);
            Mark(loop.OpenHeader.Span, LuiMapKind.Structure);
            Mark(loop.VarKeyword.Span, LuiMapKind.Structure);
            Mark(loop.InKeyword.Span, LuiMapKind.Structure);
            Mark(loop.CloseHeader.Span, LuiMapKind.Structure);
            Mark(loop.KeyedKeyword.Span, LuiMapKind.Structure);
            Mark(loop.ByKeyword.Span, LuiMapKind.Structure);
            Mark(loop.OpenBrace.Span, LuiMapKind.Structure);
            Mark(loop.CloseBrace.Span, LuiMapKind.Structure);
        }

        private static LuiElementSyntax? SingleElement(
            IReadOnlyList<LuiBodySyntax> nodes,
            List<LuiDiagnostic> diagnostics,
            LuiSpan span
        )
        {
            var elements = nodes
                .Where(node => node is not LuiCommentSyntax)
                .OfType<LuiElementSyntax>()
                .ToArray();
            if (
                elements.Length == 1
                && nodes.All(node => node is LuiCommentSyntax || node is LuiElementSyntax)
            )
                return elements[0];
            diagnostics.Add(
                new LuiDiagnostic("LUI3002", "A retained region requires one element root.", span)
            );
            return null;
        }

        private void Value(LuiValueSyntax value, List<LuiDiagnostic> diagnostics)
        {
            switch (value)
            {
                case LuiScalarSyntax scalar:
                    Mapped(Escape(scalar.Value), scalar.Span, LuiMapKind.Expression);
                    break;
                case LuiExpressionSyntax expression:
                    Expression(expression);
                    break;
                case LuiStyleWithSyntax style:
                    StyleWith(style, diagnostics);
                    break;
                default:
                    diagnostics.Add(
                        new LuiDiagnostic("LUI3003", "Unsupported attribute value.", value.Span)
                    );
                    break;
            }
        }

        private void Style(LuiStyleSyntax style, List<LuiDiagnostic> diagnostics)
        {
            Hidden("    private static readonly global::Lucent.Core.Style ");
            Mapped(style.Name.Text, style.Name.Span, LuiMapKind.Symbol);
            Write(" = global::Lucent.Core.Style.Empty");
            foreach (var member in style.Members)
            {
                if (member is LuiStyleAssignmentSyntax assignment)
                    Assignment(assignment, false);
                else if (member is LuiVariantGroupSyntax variant)
                {
                    Write(".When(");
                    Variant(variant, diagnostics);
                    Write(", global::Lucent.Core.Style.Empty");
                    foreach (var variantAssignment in variant.Assignments)
                        Assignment(variantAssignment, false);
                    Write(")");
                    Mark(variant.WhenKeyword.Span, LuiMapKind.Structure);
                    Mark(variant.OpenBrace.Span, LuiMapKind.Structure);
                    Mark(variant.CloseBrace.Span, LuiMapKind.Structure);
                }
            }
            Hidden(";\n");
            Mark(style.StyleKeyword.Span, LuiMapKind.Structure);
            Mark(style.OpenBrace.Span, LuiMapKind.Structure);
            Mark(style.CloseBrace.Span, LuiMapKind.Structure);
        }

        private void StyleWith(LuiStyleWithSyntax style, List<LuiDiagnostic> diagnostics)
        {
            Write("global::Lucent.Core.Style.Empty.With(");
            Mapped(style.Name.Text, style.Name.Span, LuiMapKind.Symbol);
            Write(")");
            if (style.Tail is { } tail)
            {
                Write(".With(");
                Mapped(tail.Text, tail.Span, LuiMapKind.Symbol);
                Write(")");
                return;
            }
            Write(".With(global::Lucent.Core.Style.Empty");
            foreach (var member in style.Members)
            {
                if (member is LuiStyleAssignmentSyntax normal)
                    Assignment(normal, true);
                else
                {
                    var variant = (LuiVariantGroupSyntax)member;
                    Write(".When(");
                    Variant(variant, diagnostics);
                    Write(", global::Lucent.Core.Style.Empty");
                    foreach (var variantAssignment in variant.Assignments)
                        Assignment(variantAssignment, true);
                    Write(")");
                    Mark(variant.WhenKeyword.Span, LuiMapKind.Structure);
                    Mark(variant.OpenBrace.Span, LuiMapKind.Structure);
                    Mark(variant.CloseBrace.Span, LuiMapKind.Structure);
                }
            }
            Write(")");
        }

        private void Variant(LuiVariantGroupSyntax variant, List<LuiDiagnostic> diagnostics)
        {
            var states = variant.Condition.Text.Split('|').Select(value => value.Trim()).ToArray();
            if (
                states.Length == 0
                || states.Any(state =>
                    state
                        is not (
                            "Hover"
                            or "FocusVisible"
                            or "Selected"
                            or "Pressed"
                            or "Invalid"
                            or "Disabled"
                        )
                )
            )
            {
                diagnostics.Add(
                    new LuiDiagnostic(
                        "LUI2006",
                        "Variant conditions must use documented VariantState names joined by '|'.",
                        variant.Condition.Span
                    )
                );
                Hidden("global::Lucent.Core.VariantState.None");
                return;
            }
            var offset = 0;
            for (var i = 0; i < states.Length; i++)
            {
                if (i != 0)
                    Write(" | ");
                Write("global::Lucent.Core.VariantState.");
                var start = variant.Condition.Text.IndexOf(
                    states[i],
                    offset,
                    StringComparison.Ordinal
                );
                Mapped(
                    states[i],
                    new LuiSpan(variant.Condition.Span.Start + start, states[i].Length),
                    LuiMapKind.Symbol
                );
                offset = start + states[i].Length;
            }
        }

        private void Assignment(LuiStyleAssignmentSyntax assignment, bool live)
        {
            StylePropertyNames.Add(assignment.Property.Span.Start);
            StyleExpressionSpans.Add(assignment.Expression.Span);
            var leading =
                assignment.Expression.Text.Length - assignment.Expression.Text.TrimStart().Length;
            var expression = new LuiSpan(
                assignment.Expression.Span.Start + leading,
                assignment.Expression.Text.Trim().Length
            );
            var bind = live && plans?.TokenExpressions.Contains(expression.Start) != true;
            var property =
                plans is not null
                && plans.Properties.TryGetValue(assignment.Property.Span.Start, out var resolved)
                    ? resolved
                    : new StylePropertyPlan(assignment.Property.Text, "");
            Write(bind ? ".Bind" : ".Set");
            if (bind && property.ValueType.Length != 0)
                Write("<" + property.ValueType + ">");
            Write("(");
            Mapped(property.Name, assignment.Property.Span, LuiMapKind.Symbol);
            Write(", ");
            if (bind)
                Write("() => ");
            Expression(assignment.Expression);
            Write(")");
            Mark(assignment.Colon.Span, LuiMapKind.Structure);
            Mark(assignment.Terminator.Span, LuiMapKind.Structure);
        }

        private void Expression(LuiExpressionSyntax expression)
        {
            var leading = expression.Text.Length - expression.Text.TrimStart().Length;
            var value = expression.Text.Trim();
            var source = new LuiSpan(expression.Span.Start + leading, value.Length);
            var start = Position(source.Start);
            var end = Position(source.End);
            var directive =
                "\n#line ("
                + start.Line.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ","
                + start.Column.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ")-("
                + end.Line.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ","
                + end.Column.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ") \""
                + identity.Document.LogicalPath.Replace("\\", "\\\\").Replace("\"", "\\\"")
                + "\"\n";
            Hidden(directive);
            var values =
                plans
                    ?.Values.Values.Where(plan =>
                        plan.Source.Start >= source.Start && plan.Source.End <= source.End
                    )
                    .OrderBy(plan => plan.Source.Start)
                    .ToArray()
                ?? Array.Empty<StyleValuePlan>();
            var offset = source.Start;
            foreach (var plan in values)
            {
                if (plan.Source.Start > offset)
                {
                    var segment = new LuiSpan(offset, plan.Source.Start - offset);
                    Mapped(
                        document.Source.Substring(segment.Start, segment.Length),
                        segment,
                        LuiMapKind.Expression
                    );
                }
                Mapped(plan.Name, plan.Source, LuiMapKind.Symbol);
                offset = plan.Source.End;
            }
            if (offset < source.End)
            {
                var segment = new LuiSpan(offset, source.End - offset);
                Mapped(
                    document.Source.Substring(segment.Start, segment.Length),
                    segment,
                    LuiMapKind.Expression
                );
            }
            else if (values.Length == 0)
                Mapped(value, source, LuiMapKind.Expression);
            Hidden("\n#line hidden\n");
            Mark(expression.OpenBrace.Span, LuiMapKind.Structure);
            Mark(expression.CloseBrace.Span, LuiMapKind.Structure);
        }

        private static string Escape(string value) =>
            "\""
            + value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
            + "\"";

        private void TraceValue(LuiValueSyntax value)
        {
            if (value is LuiScalarSyntax scalar)
            {
                Mark(scalar.OpenQuote.Span, LuiMapKind.Structure);
                Mark(scalar.CloseQuote.Span, LuiMapKind.Structure);
            }
            else if (value is LuiStyleWithSyntax style)
            {
                Mark(style.OuterOpenBrace.Span, LuiMapKind.Structure);
                Mark(style.WithKeyword.Span, LuiMapKind.Structure);
                Mark(style.OpenBrace.Span, LuiMapKind.Structure);
                Mark(style.CloseBrace.Span, LuiMapKind.Structure);
                Mark(style.OuterCloseBrace.Span, LuiMapKind.Structure);
            }
        }

        private void Mark(LuiSpan source, LuiMapKind kind)
        {
            if (source.Length != 0)
                Entries.Add(new LuiMapEntry(source, new LuiSpan(text.Length, 0), kind, false));
        }

        private void Comments(IEnumerable<LuiBodySyntax> nodes)
        {
            foreach (var comment in nodes.OfType<LuiCommentSyntax>())
                Mark(comment.Span, LuiMapKind.Structure);
        }

        private (int Line, int Column) Position(int offset)
        {
            var line = 1;
            var column = 1;
            for (var i = 0; i < offset && i < document.Source.Length; i++)
            {
                if (document.Source[i] == '\n')
                {
                    line++;
                    column = 1;
                }
                else
                    column++;
            }
            return (line, column);
        }
    }
}
