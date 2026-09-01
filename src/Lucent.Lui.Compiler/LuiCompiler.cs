using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Lucent.Lui.Compiler;

/// <summary>Roslyn-bound direct lowering to the public recipe surface.</summary>
public static class LuiCompiler
{
    public static LuiCompilationResult Compile(LuiDocumentSyntax document, Compilation compilation, LuiFreshnessIdentity identity)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (compilation is null) throw new ArgumentNullException(nameof(compilation));
        if (identity is null) throw new ArgumentNullException(nameof(identity));
        identity = Snapshot(identity, compilation);
        var diagnostics = new List<LuiDiagnostic>(document.Diagnostics);
        var writer = new Writer(document, identity, null);
        if (diagnostics.Count == 0 && document.Component is not null) writer.Document(diagnostics);
        if (diagnostics.Count != 0) return new LuiCompilationResult(identity, null, new LuiSourceMap(identity, writer.Entries), diagnostics.OrderBy(item => item.Span.Start).ToArray());

        var parseOptions = (CSharpParseOptions?)compilation.SyntaxTrees.FirstOrDefault()?.Options ?? CSharpParseOptions.Default;
        var probeTree = CSharpSyntaxTree.ParseText(writer.Text, parseOptions, identity.HintName);
        var probeCompilation = compilation.AddSyntaxTrees(probeTree);
        var probeModel = probeCompilation.GetSemanticModel(probeTree);
        var probeMap = new LuiSourceMap(identity, writer.Entries);
        var plans = new BindingPlans(ContentPlans(probeModel, probeTree, probeMap, writer, document, identity, diagnostics), PropertyPlans(probeModel, probeTree, probeMap, writer), NullChecks(probeModel, probeTree, document));
        if (diagnostics.Count != 0) return new LuiCompilationResult(identity, null, probeMap, diagnostics.OrderBy(item => item.Span.Start).ToArray());
        writer = new Writer(document, identity, plans);
        writer.Document(diagnostics);
        var map = new LuiSourceMap(identity, writer.Entries);
        if (diagnostics.Count != 0) return new LuiCompilationResult(identity, null, map, diagnostics.OrderBy(item => item.Span.Start).ToArray());

        var tree = CSharpSyntaxTree.ParseText(writer.Text, parseOptions, identity.HintName);
        var bound = compilation.AddSyntaxTrees(tree);
        var model = bound.GetSemanticModel(tree);
        foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var mapped = map.Entries.FirstOrDefault(entry => !entry.Hidden && entry.Generated.Start == invocation.Expression.SpanStart && entry.Generated.Length == invocation.Expression.Span.Length && writer.ElementNames.Contains(entry.Source.Start));
            if (mapped is null) continue;
            var method = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (method is null || !IsComponent(method)) diagnostics.Add(new LuiDiagnostic("LUI2001", "Element tags must resolve to an accessible static [LucentComponent] method returning ComponentRecipe.", mapped.Source));
        }
        foreach (var diagnostic in bound.GetDiagnostics().Where(item => item.Severity >= DiagnosticSeverity.Warning && item.Location.SourceTree == tree))
        {
            var generated = new LuiSpan(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);
            var source = Translate(map, generated) ?? document.Component!.Span;
            diagnostics.Add(new LuiDiagnostic("LUI2000", diagnostic.GetMessage(), source));
        }
        RejectProhibitedOperations(model, tree, map, diagnostics);
        return new LuiCompilationResult(identity, diagnostics.Count == 0 ? writer.Text : null, map, diagnostics.OrderBy(item => item.Span.Start).ToArray());
    }

    private static void RejectProhibitedOperations(SemanticModel model, SyntaxTree tree, LuiSourceMap map, List<LuiDiagnostic> diagnostics)
    {
        var rejected = new HashSet<int>();
        foreach (var expression in tree.GetRoot().DescendantNodes().OfType<ExpressionSyntax>())
        {
            var entry = map.FromGenerated(new LuiSpan(expression.SpanStart, expression.Span.Length)).FirstOrDefault(item => !item.Hidden && item.Kind == LuiMapKind.Expression);
            if (entry is null || !IsProhibitedOperation(model, expression) || !rejected.Add(entry.Source.Start)) continue;
            diagnostics.Add(new LuiDiagnostic("LUI2007", "Expression islands cannot use reflection or runtime compilation APIs.", entry.Source));
        }
    }

    private static bool IsProhibitedOperation(SemanticModel model, ExpressionSyntax expression)
    {
        if (expression is TypeOfExpressionSyntax) return true;
        var info = model.GetSymbolInfo(expression);
        if (IsProhibitedSymbol(info.Symbol) || info.CandidateSymbols.Any(IsProhibitedSymbol)) return true;
        return IsProhibitedType(model.GetTypeInfo(expression).Type) || IsProhibitedType(model.GetTypeInfo(expression).ConvertedType);
    }

    private static bool IsProhibitedSymbol(ISymbol? symbol)
    {
        if (symbol is IAliasSymbol alias) symbol = alias.Target;
        if (symbol is IMethodSymbol operation &&
            ((operation.Name == "GetType" && operation.ContainingType.SpecialType == SpecialType.System_Object)
            || (operation.Name == "DynamicInvoke" && operation.ContainingType.SpecialType == SpecialType.System_Delegate)
            || (operation.Name == "Compile" && operation.ContainingType.ContainingNamespace.ToDisplayString() == "System.Linq.Expressions"))) return true;
        var type = symbol switch
        {
            IMethodSymbol method => method.ContainingType,
            IPropertySymbol property => property.ContainingType,
            IFieldSymbol field => field.ContainingType,
            IEventSymbol @event => @event.ContainingType,
            INamedTypeSymbol named => named,
            _ => null
        };
        return IsProhibitedType(type);
    }

    private static bool IsProhibitedType(ITypeSymbol? type)
    {
        if (type is null) return false;
        if (type.TypeKind == TypeKind.Dynamic) return true;
        if (type is not INamedTypeSymbol named) return false;
        var ns = named.ContainingNamespace.ToDisplayString();
        return ns == "System.Reflection" || ns.StartsWith("System.Reflection.", StringComparison.Ordinal)
            || ns == "Microsoft.CodeAnalysis" || ns.StartsWith("Microsoft.CodeAnalysis.", StringComparison.Ordinal)
            || ns == "System.CodeDom.Compiler" || ns.StartsWith("System.CodeDom.Compiler.", StringComparison.Ordinal)
            || named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) is "global::System.Type" or "global::System.Activator"
            || IsCodeDomProvider(named);
    }

    private static bool IsCodeDomProvider(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.CodeDom.Compiler.CodeDomProvider") return true;
        }
        return false;
    }

    /// <summary>Returns a generation from the actual Roslyn snapshot, never host strings masquerading as one.</summary>
    public static LuiFreshnessIdentity Snapshot(LuiFreshnessIdentity identity, Compilation compilation)
    {
        var parse = compilation.SyntaxTrees.Select(tree => tree.Options).OfType<CSharpParseOptions>().FirstOrDefault() ?? CSharpParseOptions.Default;
        var trees = compilation.SyntaxTrees.OrderBy(tree => tree.FilePath, StringComparer.Ordinal).Select(tree => (tree.FilePath ?? "") + "\0" + LuiDocumentIdentity.Hash(tree.GetText().ToString()) + "\0" + ParseOptionsIdentity(tree.Options));
        var references = compilation.References.OrderBy(reference => reference.Display, StringComparer.Ordinal).Select(reference => ReferenceIdentity(compilation, reference));
        var globals = compilation.SyntaxTrees.SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>()).Where(@using => @using.GlobalKeyword.RawKind != 0).OrderBy(@using => @using.ToString(), StringComparer.Ordinal).Select(@using => @using.ToString());
        return new LuiFreshnessIdentity(identity.ProjectEpoch, identity.ProjectIdentity, identity.Document, identity.DocumentVersion,
            LuiDocumentIdentity.Hash(String.Join("\n", trees) + "\0" + compilation.Options), identity.SiblingIndexGeneration,
            parse.LanguageVersion.ToString(), typeof(Compilation).Assembly.GetName().Version?.ToString() ?? "unknown",
            LuiDocumentIdentity.Hash(String.Join("\n", references)), LuiDocumentIdentity.Hash(String.Join("\n", globals)), identity.Options, identity.Defines);
    }

    private static string ParseOptionsIdentity(ParseOptions options)
    {
        if (options is not CSharpParseOptions parse) return options.ToString();
        return parse.Kind + "\0" + parse.LanguageVersion + "\0" + parse.DocumentationMode + "\0"
            + String.Join("\u001f", parse.PreprocessorSymbolNames.OrderBy(symbol => symbol, StringComparer.Ordinal)) + "\0"
            + String.Join("\u001f", parse.Features.OrderBy(feature => feature.Key, StringComparer.Ordinal).ThenBy(feature => feature.Value, StringComparer.Ordinal).Select(feature => feature.Key + "=" + feature.Value));
    }

    private static string ReferenceIdentity(Compilation compilation, MetadataReference reference)
    {
        var symbol = compilation.GetAssemblyOrModuleSymbol(reference);
        var semanticIdentity = symbol switch
        {
            IAssemblySymbol assembly => assembly.Identity + "\0" + MetadataVersion(reference),
            IModuleSymbol module => module.Name + "\0" + MetadataVersion(reference),
            _ => "unresolved"
        };
        return (reference.Display ?? "") + "\0" + reference.Properties.Kind + "\0" + String.Join(",", reference.Properties.Aliases.OrderBy(alias => alias, StringComparer.Ordinal)) + "\0" + semanticIdentity;
    }

    private static string MetadataVersion(MetadataReference reference) => reference is not PortableExecutableReference portable ? "" : portable.GetMetadata() switch
    {
        AssemblyMetadata assembly => String.Join(",", assembly.GetModules().Select(module => module.GetModuleVersionId()).OrderBy(id => id)),
        ModuleMetadata module => module.GetModuleVersionId().ToString(),
        _ => ""
    };

    private static LuiSpan? Translate(LuiSourceMap map, LuiSpan generated)
    {
        var entry = map.FromGenerated(generated).Where(item => !item.Hidden && item.Source.Start >= 0 && item.Generated.Length != 0)
            .OrderBy(item => item.Kind == LuiMapKind.Expression ? 0 : 1).ThenBy(item => item.Generated.Length).FirstOrDefault();
        if (entry is null) return null;
        var start = generated.Start <= entry.Generated.Start ? entry.Source.Start : entry.Source.Start + (int)((long)(generated.Start - entry.Generated.Start) * entry.Source.Length / entry.Generated.Length);
        var endOffset = Math.Min(generated.End, entry.Generated.End) - entry.Generated.Start;
        var end = entry.Source.Start + (int)((long)endOffset * entry.Source.Length / entry.Generated.Length);
        return new LuiSpan(start, Math.Max(0, end - start));
    }

    private static bool IsComponent(IMethodSymbol method) => method.IsStatic && method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Lucent.Core.ComponentRecipe" && method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "Lucent.Core.LucentComponentAttribute");

    private static IReadOnlyDictionary<int, ContentPlan> ContentPlans(SemanticModel model, SyntaxTree tree, LuiSourceMap map, Writer writer, LuiDocumentSyntax document, LuiFreshnessIdentity identity, List<LuiDiagnostic> diagnostics)
    {
        var plans = new Dictionary<int, ContentPlan>();
        foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var mapped = map.FromGenerated(new LuiSpan(invocation.Expression.SpanStart, invocation.Expression.Span.Length)).FirstOrDefault(entry => writer.ElementNames.Contains(entry.Source.Start));
            if (mapped is null || plans.ContainsKey(mapped.Source.Start)) continue;
            var info = model.GetSymbolInfo(invocation);
            var methods = new[] { info.Symbol as IMethodSymbol }.Concat(info.CandidateSymbols.OfType<IMethodSymbol>()).Where(method => method is not null && IsComponent(method!)).Cast<IMethodSymbol>().ToArray();
            if (methods.Length == 0) continue;
            var defaults = new List<IParameterSymbol>();
            foreach (var method in methods)
            {
                var annotated = method.Parameters.Where(parameter => parameter.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "Lucent.Core.DefaultContentAttribute")).ToArray();
                if (annotated.Length > 1) diagnostics.Add(new LuiDiagnostic("LUI2005", "A [LucentComponent] method may declare only one [DefaultContent] parameter.", mapped.Source));
                defaults.AddRange(annotated);
            }
            if (defaults.Count == 0) continue;
            var candidates = defaults.GroupBy(parameter => parameter.ContainingSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "\0" + parameter.Name, StringComparer.Ordinal).Select(group => group.First()).ToArray();
            if (ElementChildren(document, mapped.Source.Start).Count == 0 && candidates.Length != 1) continue;
            var applicable = candidates.Where(candidate => BindsContentCandidate(model.Compilation, document, identity, mapped.Source.Start, candidate)).ToArray();
            if (applicable.Length == 1) plans[mapped.Source.Start] = ContentPlan.For(applicable[0]);
        }
        return plans;
    }

    private static IReadOnlyList<LuiBodySyntax> ElementChildren(LuiDocumentSyntax document, int start)
    {
        var pending = new Stack<LuiBodySyntax>(document.Component?.Body.Reverse() ?? Enumerable.Empty<LuiBodySyntax>());
        while (pending.Count != 0)
        {
            var node = pending.Pop();
            if (node is LuiElementSyntax element)
            {
                if (element.Name.Span.Start == start) return element.Children.Where(child => child is not LuiCommentSyntax).ToArray();
                foreach (var child in element.Children.Reverse()) pending.Push(child);
            }
            else if (node is LuiIfSyntax conditional)
            {
                foreach (var child in conditional.ElseBody.Reverse()) pending.Push(child);
                foreach (var child in conditional.ThenBody.Reverse()) pending.Push(child);
            }
            else if (node is LuiForEachSyntax loop) foreach (var child in loop.Body.Reverse()) pending.Push(child);
        }
        return Array.Empty<LuiBodySyntax>();
    }

    private static bool BindsContentCandidate(Compilation compilation, LuiDocumentSyntax document, LuiFreshnessIdentity identity, int elementStart, IParameterSymbol parameter)
    {
        var candidate = ContentPlan.For(parameter);
        var writer = new Writer(document, identity, new BindingPlans(new Dictionary<int, ContentPlan> { [elementStart] = candidate }, new Dictionary<int, string>(), new HashSet<int>()));
        var diagnostics = new List<LuiDiagnostic>();
        writer.Document(diagnostics);
        if (diagnostics.Count != 0) return false;
        var parse = (CSharpParseOptions?)compilation.SyntaxTrees.FirstOrDefault()?.Options ?? CSharpParseOptions.Default;
        var tree = CSharpSyntaxTree.ParseText(writer.Text, parse, identity.HintName + ".candidate.g.cs");
        var bound = compilation.RemoveSyntaxTrees(compilation.SyntaxTrees.Where(item => item.FilePath == identity.HintName)).AddSyntaxTrees(tree);
        var model = bound.GetSemanticModel(tree);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().FirstOrDefault(item => writer.ElementNames.Contains(elementStart) && writer.Entries.Any(entry => !entry.Hidden && entry.Source.Start == elementStart && entry.Generated.Start == item.Expression.SpanStart && entry.Generated.Length == item.Expression.Span.Length));
        var target = invocation is null ? null : model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        return target is not null && target.Parameters.Any(item => item.Name == parameter.Name && item.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) && item.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "Lucent.Core.DefaultContentAttribute"));
    }

    private static IReadOnlyDictionary<int, string> PropertyPlans(SemanticModel model, SyntaxTree tree, LuiSourceMap map, Writer writer)
    {
        var plans = new Dictionary<int, string>();
        foreach (var name in tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (model.GetSymbolInfo(name).Symbol is not IFieldSymbol property || !IsStyleProperty(property.Type)) continue;
            var entry = map.Entries.FirstOrDefault(candidate => !candidate.Hidden && candidate.Source.Start >= 0 && candidate.Generated.Start <= name.SpanStart && candidate.Generated.End >= name.Span.End);
            if (entry is not null) plans[entry.Source.Start] = property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + property.Name;
        }
        return plans;
    }

    private static bool IsStyleProperty(ITypeSymbol property)
    {
        var type = property as INamedTypeSymbol;
        return type is not null && type.Name == "Property" && type.Arity == 1 && type.ContainingNamespace.ToDisplayString() == "Lucent.Core";
    }

    private static HashSet<int> NullChecks(SemanticModel model, SyntaxTree tree, LuiDocumentSyntax document)
    {
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().SingleOrDefault(candidate => candidate.Identifier.ValueText == document.Component!.Name.Text);
        if (method is null) return new HashSet<int>();
        return new HashSet<int>(method.ParameterList.Parameters.Select((parameter, index) => (parameter, index)).Where(item =>
        {
            var type = model.GetDeclaredSymbol(item.parameter)?.Type;
            return type is not null && (type.IsReferenceType || type.TypeKind == TypeKind.TypeParameter) && type.NullableAnnotation == NullableAnnotation.NotAnnotated;
        }).Select(item => item.index));
    }

    private sealed class ContentPlan
    {
        internal ContentPlan(string name, bool isCollection) { Name = name; IsCollection = isCollection; }
        internal static ContentPlan For(IParameterSymbol parameter) => new ContentPlan(parameter.Name, parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Lucent.Core.ComponentContent");
        internal string Name { get; }
        internal bool IsCollection { get; }
    }

    private sealed class BindingPlans
    {
        internal BindingPlans(IReadOnlyDictionary<int, ContentPlan> content, IReadOnlyDictionary<int, string> properties, HashSet<int> nullChecks) { Content = content; Properties = properties; NullChecks = nullChecks; }
        internal IReadOnlyDictionary<int, ContentPlan> Content { get; }
        internal IReadOnlyDictionary<int, string> Properties { get; }
        internal HashSet<int> NullChecks { get; }
    }

    private sealed class Writer
    {
        private readonly LuiDocumentSyntax document;
        private readonly LuiFreshnessIdentity identity;
        private readonly StringBuilder text = new StringBuilder();
        private readonly BindingPlans? plans;
        internal readonly List<LuiMapEntry> Entries = new List<LuiMapEntry>();
        internal readonly HashSet<int> ElementNames = new HashSet<int>();
        internal readonly HashSet<int> StylePropertyNames = new HashSet<int>();
        internal Writer(LuiDocumentSyntax document, LuiFreshnessIdentity identity, BindingPlans? plans) { this.document = document; this.identity = identity; this.plans = plans; }
        internal string Text => text.ToString();
        // Every generated character is either related to a source span or explicitly hidden.
        private void Write(string value) => Hidden(value);
        private void Mapped(string value, LuiSpan source, LuiMapKind kind) { var start = text.Length; text.Append(value); Entries.Add(new LuiMapEntry(source, new LuiSpan(start, value.Length), kind, false)); }
        private void Hidden(string value) { var start = text.Length; text.Append(value); Entries.Add(new LuiMapEntry(new LuiSpan(-1, 0), new LuiSpan(start, value.Length), LuiMapKind.Scaffolding, true)); }
        internal void Document(List<LuiDiagnostic> diagnostics)
        {
            Hidden("// <auto-generated/>\n// lui-document: " + identity.Document.LogicalPath + "\n// lui-map: " + identity.MapIdentity + "\n#nullable enable\n#line hidden\n");
            var namespaceWritten = false;
            foreach (var node in document.TopLevel)
            {
                if (node is LuiTopLevelCommentSyntax comment) Mark(comment.Span, LuiMapKind.Structure);
                else if (node is LuiUsingSyntax @using) Directive("using", @using.Keyword, @using.Value, @using.Semicolon);
                else if (node is LuiNamespaceSyntax @namespace) { Directive("namespace", @namespace.Keyword, @namespace.Value, @namespace.Semicolon); namespaceWritten = true; }
            }
            if (!namespaceWritten) Hidden("namespace Lucent.Lui.Generated;\n");
            Hidden("\npublic static partial class Components\n{\n");
            foreach (var style in document.Styles) Style(style, diagnostics);
            Component(document.Component!, diagnostics);
            Hidden("}\n");
        }
        private void Directive(string keyword, LuiToken sourceKeyword, string value, LuiToken semicolon)
        {
            Mapped(keyword, sourceKeyword.Span, LuiMapKind.Structure); Hidden(" ");
            var start = document.Source.IndexOf(value, sourceKeyword.Span.End, StringComparison.Ordinal);
            if (start < 0) Hidden(value); else Mapped(value, new LuiSpan(start, value.Length), LuiMapKind.Symbol);
            Mapped(";", semicolon.Span, LuiMapKind.Structure); Hidden("\n");
        }
        private void Component(LuiComponentSyntax component, List<LuiDiagnostic> diagnostics)
        {
            Hidden("    [global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"Lucent.Lui.Generator\", \"0.2.0\")]\n    [global::Lucent.Core.LucentComponentAttribute]\n    ");
            if (component.Accessibility.IsMissing) Hidden("internal"); else Mapped(component.Accessibility.Text, component.Accessibility.Span, LuiMapKind.Symbol);
            Write(" static global::Lucent.Core.ComponentRecipe "); Mapped(component.Name.Text, component.Name.Span, LuiMapKind.Symbol); Write("(");
            for (var i = 0; i < component.Parameters.Count; i++)
            {
                if (i != 0) Write(", ");
                var parameter = component.Parameters[i];
                Mapped(parameter.DeclarationText, parameter.Span, LuiMapKind.Symbol);
            }
            Hidden(")\n    {\n");
            foreach (var parameter in component.Parameters.Where((parameter, index) => plans?.NullChecks.Contains(index) == true))
            {
                Hidden("        global::System.ArgumentNullException.ThrowIfNull("); Mapped(parameter.Name.Text, parameter.Name.Span, LuiMapKind.Symbol); Hidden(");\n");
            }
            Hidden("        return ");
            var root = component.Body.FirstOrDefault(node => !(node is LuiCommentSyntax));
            Comments(component.Body);
            if (root is LuiElementSyntax element) Element(element, diagnostics);
            else diagnostics.Add(new LuiDiagnostic("LUI3000", "A component root must be an element.", component.Span));
            Hidden(";\n    }\n");
            Mark(component.ComponentKeyword.Span, LuiMapKind.Structure); Mark(component.OpenParameters.Span, LuiMapKind.Structure); Mark(component.CloseParameters.Span, LuiMapKind.Structure); Mark(component.OpenBrace.Span, LuiMapKind.Structure); Mark(component.CloseBrace.Span, LuiMapKind.Structure);
            foreach (var parameter in component.Parameters) Mark(parameter.Separator.Span, LuiMapKind.Structure);
        }
        private void Element(LuiElementSyntax element, List<LuiDiagnostic> diagnostics)
        {
            var name = element.Name.Text;
            ElementNames.Add(element.Name.Span.Start);
            var simpleName = name.Substring(Math.Max(name.LastIndexOf('.'), name.LastIndexOf(':')) + 1);
            if (!String.IsNullOrEmpty(simpleName) && Char.IsLower(simpleName[0])) diagnostics.Add(new LuiDiagnostic("LUI2002", "Element tags must be PascalCase.", element.Name.Span));
            foreach (var group in element.Attributes.GroupBy(attribute => attribute.Name.Text, StringComparer.Ordinal).Where(group => group.Count() > 1)) diagnostics.Add(new LuiDiagnostic("LUI2003", "Duplicate attribute '" + group.Key + "'.", group.First().Name.Span));
            Mapped(name, element.Name.Span, LuiMapKind.Symbol); Write("(");
            var arguments = new List<Action>();
            foreach (var attribute in element.Attributes)
            {
                if (attribute.Name.Text == "name") continue;
                arguments.Add(() => { Mapped(attribute.Name.Text, attribute.Name.Span, LuiMapKind.Symbol); Write(": "); Value(attribute.Value, diagnostics); });
            }
            var children = element.Children.Where(child => !(child is LuiCommentSyntax)).ToArray();
            var plan = plans is not null && plans.Content.TryGetValue(element.Name.Span.Start, out var resolved) ? resolved : null;
            if (plan is null)
            {
                if (children.Length != 0) arguments.Add(() => Content("content", children.Length != 1 || children[0] is not LuiTextSyntax, children, diagnostics));
            }
            else if (children.Length == 0 && plan.IsCollection) arguments.Add(() => Content(plan.Name, true, children, diagnostics));
            else if (children.Length != 0) arguments.Add(() => Content(plan.Name, plan.IsCollection, children, diagnostics));
            for (var i = 0; i < arguments.Count; i++) { if (i != 0) Write(", "); arguments[i](); }
            Write(")");
            var explicitName = element.Attributes.FirstOrDefault(attribute => attribute.Name.Text == "name");
            if (explicitName is not null) { Hidden("."); Mapped("Named", explicitName.Name.Span, LuiMapKind.Symbol); Hidden("("); Value(explicitName.Value, diagnostics); Hidden(")"); }
            foreach (var attribute in element.Attributes) { Mark(attribute.EqualsToken.Span, LuiMapKind.Structure); TraceValue(attribute.Value); }
            foreach (var comment in element.Children.OfType<LuiCommentSyntax>()) Mark(comment.Span, LuiMapKind.Structure);
            Mark(element.OpenAngle.Span, LuiMapKind.Structure); Mark(element.OpenCloseAngle.Span, LuiMapKind.Structure); Mark(element.SelfClosingSlash.Span, LuiMapKind.Structure); Mark(element.CloseOpenAngle.Span, LuiMapKind.Structure); Mark(element.CloseName.Span, LuiMapKind.Symbol); Mark(element.CloseAngle.Span, LuiMapKind.Structure);
        }
        private void Content(string name, bool collection, IReadOnlyList<LuiBodySyntax> children, List<LuiDiagnostic> diagnostics)
        {
            Write(name + ": ");
            if (!collection && children.Count == 1 && children[0] is LuiTextSyntax textNode) { Mapped(Escape(textNode.Text), textNode.Span, LuiMapKind.Expression); return; }
            if (!collection) { diagnostics.Add(new LuiDiagnostic("LUI2004", "Default content requires one scalar text value.", children.Count == 0 ? new LuiSpan(0, 0) : children[0].Span)); return; }
            Write("[");
            for (var i = 0; i < children.Count; i++) { if (i != 0) Write(", "); ContentNode(children[i], diagnostics); }
            Write("]");
        }
        private void ContentNode(LuiBodySyntax node, List<LuiDiagnostic> diagnostics)
        {
            switch (node)
            {
                case LuiElementSyntax element: Element(element, diagnostics); break;
                case LuiIfSyntax conditional: Conditional(conditional, diagnostics); break;
                case LuiForEachSyntax loop: ForEach(loop, diagnostics); break;
                case LuiTextSyntax textNode: diagnostics.Add(new LuiDiagnostic("LUI3001", "Text content cannot be mixed with component content.", textNode.Span)); break;
                default: diagnostics.Add(new LuiDiagnostic("LUI3001", "Unsupported content construct.", node.Span)); break;
            }
        }
        private void Conditional(LuiIfSyntax conditional, List<LuiDiagnostic> diagnostics)
        {
            Comments(conditional.ThenBody); Comments(conditional.ElseBody);
            var thenElement = SingleElement(conditional.ThenBody, diagnostics, conditional.Span);
            if (thenElement is null) return;
            Write("global::Lucent.Core.ContentRecipe.Switch(\"if-"); Write(conditional.Span.Start.ToString(System.Globalization.CultureInfo.InvariantCulture)); Write("\", () => { if ("); Expression(conditional.Condition); Write(") return new global::Lucent.Core.ConditionalChoice(1, "); Element(thenElement, diagnostics); Write("); return new global::Lucent.Core.ConditionalChoice(2, ");
            if (!conditional.ElseKeyword.IsMissing && conditional.ElseBody.Any(node => node is not LuiCommentSyntax))
            {
                var elseElement = SingleElement(conditional.ElseBody, diagnostics, conditional.Span);
                if (elseElement is null) return;
                Element(elseElement, diagnostics);
            }
            else Hidden("null");
            Write("); })");
            Mark(conditional.IfKeyword.Span, LuiMapKind.Structure); Mark(conditional.OpenCondition.Span, LuiMapKind.Structure); Mark(conditional.CloseCondition.Span, LuiMapKind.Structure); Mark(conditional.OpenBrace.Span, LuiMapKind.Structure); Mark(conditional.CloseBrace.Span, LuiMapKind.Structure); Mark(conditional.ElseKeyword.Span, LuiMapKind.Structure); Mark(conditional.ElseOpenBrace.Span, LuiMapKind.Structure); Mark(conditional.ElseCloseBrace.Span, LuiMapKind.Structure);
        }
        private void ForEach(LuiForEachSyntax loop, List<LuiDiagnostic> diagnostics)
        {
            Comments(loop.Body);
            var body = SingleElement(loop.Body, diagnostics, loop.Span);
            if (body is null) return;
            Write("global::Lucent.Core.ContentRecipe.ForEach(\"foreach-"); Write(loop.Span.Start.ToString(System.Globalization.CultureInfo.InvariantCulture)); Write("\", () => "); Expression(loop.Source); Write(", "); Mapped(loop.Variable.Text, loop.Variable.Span, LuiMapKind.Symbol); Hidden(" => "); Expression(loop.Key); Write(", "); Mapped(loop.Variable.Text, loop.Variable.Span, LuiMapKind.Symbol); Hidden(" => "); Element(body, diagnostics); Write(")");
            Mark(loop.ForeachKeyword.Span, LuiMapKind.Structure); Mark(loop.OpenHeader.Span, LuiMapKind.Structure); Mark(loop.VarKeyword.Span, LuiMapKind.Structure); Mark(loop.InKeyword.Span, LuiMapKind.Structure); Mark(loop.CloseHeader.Span, LuiMapKind.Structure); Mark(loop.KeyedKeyword.Span, LuiMapKind.Structure); Mark(loop.ByKeyword.Span, LuiMapKind.Structure); Mark(loop.OpenBrace.Span, LuiMapKind.Structure); Mark(loop.CloseBrace.Span, LuiMapKind.Structure);
        }
        private static LuiElementSyntax? SingleElement(IReadOnlyList<LuiBodySyntax> nodes, List<LuiDiagnostic> diagnostics, LuiSpan span)
        {
            var elements = nodes.Where(node => node is not LuiCommentSyntax).OfType<LuiElementSyntax>().ToArray();
            if (elements.Length == 1 && nodes.All(node => node is LuiCommentSyntax || node is LuiElementSyntax)) return elements[0];
            diagnostics.Add(new LuiDiagnostic("LUI3002", "A retained region requires one element root.", span)); return null;
        }
        private void Value(LuiValueSyntax value, List<LuiDiagnostic> diagnostics)
        {
            switch (value)
            {
                case LuiScalarSyntax scalar: Mapped(Escape(scalar.Value), scalar.Span, LuiMapKind.Expression); break;
                case LuiExpressionSyntax expression: Expression(expression); break;
                case LuiStyleWithSyntax style: StyleWith(style, diagnostics); break;
                default: diagnostics.Add(new LuiDiagnostic("LUI3003", "Unsupported attribute value.", value.Span)); break;
            }
        }
        private void Style(LuiStyleSyntax style, List<LuiDiagnostic> diagnostics)
        {
            Hidden("    private static readonly global::Lucent.Core.Style "); Mapped(style.Name.Text, style.Name.Span, LuiMapKind.Symbol); Write(" = global::Lucent.Core.Style.Empty");
            foreach (var member in style.Members)
            {
                if (member is LuiStyleAssignmentSyntax assignment) Assignment(assignment, false);
                else if (member is LuiVariantGroupSyntax variant) { Write(".When("); Variant(variant, diagnostics); Write(", global::Lucent.Core.Style.Empty"); foreach (var variantAssignment in variant.Assignments) Assignment(variantAssignment, false); Write(")"); Mark(variant.WhenKeyword.Span, LuiMapKind.Structure); Mark(variant.OpenBrace.Span, LuiMapKind.Structure); Mark(variant.CloseBrace.Span, LuiMapKind.Structure); }
            }
            Hidden(";\n");
            Mark(style.StyleKeyword.Span, LuiMapKind.Structure); Mark(style.OpenBrace.Span, LuiMapKind.Structure); Mark(style.CloseBrace.Span, LuiMapKind.Structure);
        }
        private void StyleWith(LuiStyleWithSyntax style, List<LuiDiagnostic> diagnostics)
        {
            Write("global::Lucent.Core.Style.Empty.With("); Mapped(style.Name.Text, style.Name.Span, LuiMapKind.Symbol); Write(")");
            if (style.Tail is { } tail) { Write(".With("); Mapped(tail.Text, tail.Span, LuiMapKind.Symbol); Write(")"); return; }
            Write(".With(global::Lucent.Core.Style.Empty"); foreach (var member in style.Members) { if (member is LuiStyleAssignmentSyntax normal) Assignment(normal, true); else { var variant = (LuiVariantGroupSyntax)member; Write(".When("); Variant(variant, diagnostics); Write(", global::Lucent.Core.Style.Empty"); foreach (var variantAssignment in variant.Assignments) Assignment(variantAssignment, true); Write(")"); Mark(variant.WhenKeyword.Span, LuiMapKind.Structure); Mark(variant.OpenBrace.Span, LuiMapKind.Structure); Mark(variant.CloseBrace.Span, LuiMapKind.Structure); } } Write(")");
        }
        private void Variant(LuiVariantGroupSyntax variant, List<LuiDiagnostic> diagnostics)
        {
            var states = variant.Condition.Text.Split('|').Select(value => value.Trim()).ToArray();
            if (states.Length == 0 || states.Any(state => state is not ("Hover" or "FocusVisible" or "Selected" or "Pressed" or "Invalid" or "Disabled")))
            {
                diagnostics.Add(new LuiDiagnostic("LUI2006", "Variant conditions must use documented VariantState names joined by '|'.", variant.Condition.Span));
                Hidden("global::Lucent.Core.VariantState.None"); return;
            }
            var offset = 0;
            for (var i = 0; i < states.Length; i++) { if (i != 0) Write(" | "); Write("global::Lucent.Core.VariantState."); var start = variant.Condition.Text.IndexOf(states[i], offset, StringComparison.Ordinal); Mapped(states[i], new LuiSpan(variant.Condition.Span.Start + start, states[i].Length), LuiMapKind.Symbol); offset = start + states[i].Length; }
        }
        private void Assignment(LuiStyleAssignmentSyntax assignment, bool live)
        {
            StylePropertyNames.Add(assignment.Property.Span.Start);
            var property = plans is not null && plans.Properties.TryGetValue(assignment.Property.Span.Start, out var resolved) ? resolved : assignment.Property.Text;
            Write(live ? ".Bind(" : ".Set("); Mapped(property, assignment.Property.Span, LuiMapKind.Symbol); Write(", "); if (live) Write("() => "); Expression(assignment.Expression); Write(")");
            Mark(assignment.Colon.Span, LuiMapKind.Structure); Mark(assignment.Terminator.Span, LuiMapKind.Structure);
        }
        private void Expression(LuiExpressionSyntax expression)
        {
            var leading = expression.Text.Length - expression.Text.TrimStart().Length; var value = expression.Text.Trim(); var source = new LuiSpan(expression.Span.Start + leading, value.Length);
            var start = Position(source.Start); var end = Position(source.End);
            var directive = "\n#line (" + start.Line.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + start.Column.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")-(" + end.Line.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + end.Column.ToString(System.Globalization.CultureInfo.InvariantCulture) + ") \"" + identity.Document.LogicalPath.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"\n"; Hidden(directive);
            Mapped(value, source, LuiMapKind.Expression);
            Hidden("\n#line hidden\n");
            Mark(expression.OpenBrace.Span, LuiMapKind.Structure); Mark(expression.CloseBrace.Span, LuiMapKind.Structure);
        }
        private static string Escape(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        private void TraceValue(LuiValueSyntax value)
        {
            if (value is LuiScalarSyntax scalar) { Mark(scalar.OpenQuote.Span, LuiMapKind.Structure); Mark(scalar.CloseQuote.Span, LuiMapKind.Structure); }
            else if (value is LuiStyleWithSyntax style) { Mark(style.OuterOpenBrace.Span, LuiMapKind.Structure); Mark(style.WithKeyword.Span, LuiMapKind.Structure); Mark(style.OpenBrace.Span, LuiMapKind.Structure); Mark(style.CloseBrace.Span, LuiMapKind.Structure); Mark(style.OuterCloseBrace.Span, LuiMapKind.Structure); }
        }
        private void Mark(LuiSpan source, LuiMapKind kind)
        {
            if (source.Length != 0) Entries.Add(new LuiMapEntry(source, new LuiSpan(text.Length, 0), kind, false));
        }
        private void Comments(IEnumerable<LuiBodySyntax> nodes)
        {
            foreach (var comment in nodes.OfType<LuiCommentSyntax>()) Mark(comment.Span, LuiMapKind.Structure);
        }
        private (int Line, int Column) Position(int offset)
        {
            var line = 1; var column = 1;
            for (var i = 0; i < offset && i < document.Source.Length; i++) { if (document.Source[i] == '\n') { line++; column = 1; } else column++; }
            return (line, column);
        }
    }
}
