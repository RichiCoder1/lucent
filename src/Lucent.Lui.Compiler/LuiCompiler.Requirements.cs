using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Lucent.Lui.Compiler;

public static partial class LuiCompiler
{
    internal static string RequirementAttributeText(
        LuiRequirementSyntax requirement,
        string logicalPath,
        string source,
        string typeName
    )
    {
        var line = SourceText.From(source).Lines.GetLinePosition(requirement.Name.Span.Start);
        return "[global::Lucent.Core.ComponentRequirementAttribute(typeof("
            + typeName
            + "), global::Lucent.Core.ComponentRequirementKind."
            + requirement.Kind
            + ", "
            + SymbolDisplay.FormatLiteral(requirement.Name.Text.TrimStart('@'), true)
            + ", "
            + SymbolDisplay.FormatLiteral(logicalPath, true)
            + ", "
            + (line.Line + 1).ToString(CultureInfo.InvariantCulture)
            + ", "
            + (line.Character + 1).ToString(CultureInfo.InvariantCulture)
            + ")]";
    }

    private static Dictionary<int, string> RequirementPlans(
        SemanticModel model,
        SyntaxTree tree,
        Writer writer,
        List<LuiDiagnostic> diagnostics
    )
    {
        var types = new Dictionary<int, string>();
        var seen = new Dictionary<ITypeSymbol, LuiRequirementSyntax>(
            SymbolEqualityComparer.Default
        );
        foreach (var mapping in writer.RequirementMappings)
        {
            var syntax = mapping.Source;
            var property = tree.GetRoot()
                .FindNode(mapping.Generated)
                .FirstAncestorOrSelf<PropertyDeclarationSyntax>();
            var type = property is null ? null : model.GetDeclaredSymbol(property)?.Type;
            if (type is null || type.TypeKind == TypeKind.Error)
                continue;
            if (
                !ClosedRequirementType(type)
                || type.NullableAnnotation == NullableAnnotation.Annotated
                || (
                    type is INamedTypeSymbol nullable
                    && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                )
            )
            {
                diagnostics.Add(
                    new LuiDiagnostic(
                        "LUI2031",
                        "A context or inject declaration requires one closed, non-null, non-dynamic type that can be retained in a component.",
                        syntax.TypeSpan
                    )
                );
                continue;
            }
            if (
                syntax.Kind == LuiRequirementKind.Inject
                && (!type.IsReferenceType || ForbiddenInjection(type, model.Compilation))
            )
            {
                diagnostics.Add(
                    new LuiDiagnostic(
                        "LUI2032",
                        "Inject requires an ordinary closed reference service. Service locators, framework context values, and optional, collection, factory, lazy or async request shapes are not supported; declare context values with context and inject an explicit application service instead.",
                        syntax.TypeSpan
                    )
                );
                continue;
            }
            if (seen.TryGetValue(type, out var previous))
                diagnostics.Add(
                    new LuiDiagnostic(
                        "LUI2033",
                        "This exact type is already required by '"
                            + previous.Name.Text
                            + "'. Share that borrowed value instead of requesting the type twice.",
                        syntax.TypeSpan
                    )
                );
            else
                seen.Add(type, syntax);
            types[syntax.Span.Start] = type.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );
        }
        return types;
    }

    private static bool ClosedRequirementType(ITypeSymbol type) =>
        type switch
        {
            IArrayTypeSymbol array => ClosedRequirementType(array.ElementType),
            INamedTypeSymbol named => !named.IsRefLikeType
                && !named.IsUnboundGenericType
                && named.TypeArguments.All(ClosedRequirementType)
                && (named.ContainingType is null || ClosedRequirementType(named.ContainingType)),
            _ => false,
        };

    private static bool ForbiddenInjection(ITypeSymbol type, Compilation compilation)
    {
        if (type is IArrayTypeSymbol || type.TypeKind == TypeKind.Delegate)
            return true;
        string[] forbidden =
        [
            "System.IServiceProvider",
            "Lucent.Core.IComponentServiceSource",
            "Lucent.Core.ComponentServiceBinding",
            "Lucent.Core.ApplicationSession",
            "Lucent.Core.Composition",
            "Lucent.Core.MountContext",
            "Lucent.Core.ComponentContext",
            "Lucent.Core.ReactiveScope",
            "Lucent.Core.ThemeContext",
            "Lucent.Core.NavigationSession",
            "Lucent.Core.NavigationInteraction",
            "Lucent.Core.RouteContext`1",
            "Microsoft.Extensions.DependencyInjection.IServiceScope",
            "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory",
            "Microsoft.Extensions.DependencyInjection.IServiceProviderIsService",
            "Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider",
            "System.Collections.IEnumerable",
            "System.Collections.Generic.IEnumerable`1",
            "System.Collections.Generic.IAsyncEnumerable`1",
            "System.Lazy`1",
            "System.Lazy`2",
            "System.Threading.Tasks.Task",
            "System.Threading.Tasks.Task`1",
            "System.Threading.Tasks.ValueTask",
            "System.Threading.Tasks.ValueTask`1",
        ];
        return forbidden.Any(name =>
        {
            var symbol = compilation.GetTypeByMetadataName(name);
            return symbol is not null
                && (
                    SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, symbol)
                    || type.AllInterfaces.Any(item =>
                        SymbolEqualityComparer.Default.Equals(item.OriginalDefinition, symbol)
                    )
                );
        });
    }

    private static Dictionary<int, string> ProviderDiagnostics(
        SemanticModel model,
        SyntaxTree tree,
        Writer writer,
        IReadOnlyDictionary<string, StatePlan> states,
        List<LuiDiagnostic> diagnostics
    )
    {
        var types = new Dictionary<int, string>();
        foreach (var mapping in writer.ProviderMappings)
        {
            var expression = tree.GetRoot()
                .FindNode(mapping.Generated, getInnermostNodeForTie: true)
                .AncestorsAndSelf()
                .OfType<ExpressionSyntax>()
                .FirstOrDefault(node => node.Span == mapping.Generated);
            if (expression is null)
                continue;
            var owner = expression.FirstAncestorOrSelf<MethodDeclarationSyntax>() is { } method
                ? model.GetDeclaredSymbol(method)?.ContainingType
                : null;
            var unstable = expression
                .DescendantNodesAndSelf()
                .OfType<ExpressionSyntax>()
                .Any(node =>
                    model.GetSymbolInfo(node).Symbol is IPropertySymbol property
                    && SymbolEqualityComparer.Default.Equals(property.ContainingType, owner)
                    && states.TryGetValue(property.Name, out var state)
                    && state.Kind != StateKind.Snapshot
                );
            if (unstable)
                diagnostics.Add(
                    new LuiDiagnostic(
                        "LUI2034",
                        "Provide requires a stable per-mount value. Move this writable or derived expression into a readonly declaration, or provide a stable model whose own signals carry changes.",
                        mapping.Source.Span
                    )
                );
            var typeInfo = model.GetTypeInfo(expression);
            if (typeInfo.Type is not { TypeKind: not TypeKind.Error } type)
                continue;
            types[mapping.Source.Span.Start] = type.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );
            if (
                !ClosedRequirementType(type)
                || type.NullableAnnotation == NullableAnnotation.Annotated
                || typeInfo.Nullability.FlowState == NullableFlowState.MaybeNull
                || (model.GetConstantValue(expression) is { HasValue: true, Value: null })
            )
                diagnostics.Add(
                    new LuiDiagnostic(
                        "LUI2035",
                        "Provide requires a non-null stable value with a closed exact type.",
                        mapping.Source.Span
                    )
                );
            if (
                CreatesUnownedDisposable(
                    model.GetOperation(expression),
                    owner,
                    writer,
                    model.Compilation
                )
            )
                diagnostics.Add(
                    new LuiDiagnostic(
                        "LUI2036",
                        "Provide borrows its value and does not dispose it. Create this disposable in an [Owned] readonly declaration first, then provide that declaration.",
                        mapping.Source.Span
                    )
                );
        }
        foreach (var root in writer.ProviderRoots)
        {
            var node = tree.GetRoot().FindNode(root.Generated, getInnermostNodeForTie: true);
            var expression = node.AncestorsAndSelf()
                .OfType<ExpressionSyntax>()
                .FirstOrDefault(candidate => candidate.Span == root.Generated);
            if (
                expression is not null
                && model.GetTypeInfo(expression).Type is { TypeKind: not TypeKind.Error } type
                && !LuiComponentReturnShape.TryGet(model.Compilation, type, out _)
            )
                diagnostics.Add(
                    new LuiDiagnostic(
                        "LUI2037",
                        "Provide requires one ComponentRecipe root. Put zero or multiple content contributions inside a layout element.",
                        root.Source
                    )
                );
        }
        return types;
    }

    private static bool CreatesUnownedDisposable(
        IOperation? operation,
        INamedTypeSymbol? owner,
        Writer writer,
        Compilation compilation
    ) =>
        operation switch
        {
            IConversionOperation conversion => CreatesUnownedDisposable(
                conversion.Operand,
                owner,
                writer,
                compilation
            ),
            IParenthesizedOperation parenthesized => CreatesUnownedDisposable(
                parenthesized.Operand,
                owner,
                writer,
                compilation
            ),
            IConditionalOperation conditional => CreatesUnownedDisposable(
                conditional.WhenTrue,
                owner,
                writer,
                compilation
            ) || CreatesUnownedDisposable(conditional.WhenFalse, owner, writer, compilation),
            ICoalesceOperation coalesce => CreatesUnownedDisposable(
                coalesce.Value,
                owner,
                writer,
                compilation
            ) || CreatesUnownedDisposable(coalesce.WhenNull, owner, writer, compilation),
            ISwitchExpressionOperation choice => choice.Arms.Any(arm =>
                CreatesUnownedDisposable(arm.Value, owner, writer, compilation)
            ),
            IObjectCreationOperation or IInvocationOperation => operation.Type is { } type
                && ImplementsDisposable(type, compilation)
                && !IsKnownOwnedValue(operation, owner, writer),
            _ => false,
        };

    private static bool ImplementsDisposable(ITypeSymbol type, Compilation compilation)
    {
        var symbol = compilation.GetTypeByMetadataName("System.IDisposable");
        return symbol is not null
            && (
                SymbolEqualityComparer.Default.Equals(type, symbol)
                || type.AllInterfaces.Any(item =>
                    SymbolEqualityComparer.Default.Equals(item, symbol)
                )
            );
    }

    private sealed partial class Writer
    {
        internal readonly List<(
            LuiRequirementSyntax Source,
            TextSpan Generated
        )> RequirementMappings = [];
        internal readonly List<(LuiExpressionSyntax Source, TextSpan Generated)> ProviderMappings =
        [];
        internal readonly List<(LuiSpan Source, TextSpan Generated)> ProviderRoots = [];
        private readonly List<string> requirementArguments = [];
        private readonly Dictionary<int, string> providerDescriptors = [];

        private void RequirementAttributes(IReadOnlyList<LuiRequirementSyntax> requirements)
        {
            if (plans is null)
                return;
            foreach (var requirement in requirements)
            {
                Hidden(
                    "    "
                        + RequirementAttributeText(
                            requirement,
                            identity.Document.LogicalPath,
                            document.Source,
                            RequirementType(requirement)
                        )
                        + "\n"
                );
            }
        }

        private void ProviderDescriptors()
        {
            if (plans is null)
                return;
            foreach (var pair in plans.ProviderTypes.OrderBy(pair => pair.Key))
            {
                var name = UniqueGeneratedName(
                    "__luiProvider_" + LuiDocumentIdentity.Hash(identity.Document.LogicalPath) + "_"
                );
                providerDescriptors.Add(pair.Key, name);
                var line = SourceText.From(document.Source).Lines.GetLinePosition(pair.Key);
                Hidden(
                    "    private static readonly global::Lucent.Core.ContextProviderSource "
                        + name
                        + " = new(typeof("
                        + pair.Value
                        + "), "
                        + Escape(pair.Value)
                        + ", "
                        + Escape(identity.Document.LogicalPath)
                        + ", "
                        + (line.Line + 1).ToString(CultureInfo.InvariantCulture)
                        + ", "
                        + (line.Character + 1).ToString(CultureInfo.InvariantCulture)
                        + ");\n"
                );
            }
        }

        private string RequirementType(LuiRequirementSyntax requirement) =>
            plans is not null
            && plans.RequirementTypes.TryGetValue(requirement.Span.Start, out var resolved)
                ? resolved
                : requirement.Type.ToString();

        private void RequirementPlan(IReadOnlyList<LuiRequirementSyntax> requirements, string name)
        {
            var valueType = RequirementType(requirements[0]);
            for (var i = 1; i < requirements.Count; i++)
                valueType =
                    "(" + valueType + " Previous, " + RequirementType(requirements[i]) + " Value)";
            Hidden(
                "    private static readonly global::Lucent.Core.ComponentRequirementPlan<"
                    + valueType
                    + "> "
                    + name
                    + " =\n        global::Lucent.Core.ComponentRequirements."
            );
            for (var i = 0; i < requirements.Count; i++)
            {
                var requirement = requirements[i];
                Hidden(i == 0 ? "" : ".And");
                Hidden(requirement.Kind == LuiRequirementKind.Context ? "Context<" : "Service<");
                Hidden(
                    RequirementType(requirement)
                        + ">(new global::Lucent.Core.ComponentRequirementSource("
                );
                var line = SourceText
                    .From(document.Source)
                    .Lines.GetLinePosition(requirement.Name.Span.Start);
                Hidden(
                    Escape(requirement.Name.Text.TrimStart('@'))
                        + ", "
                        + Escape(RequirementType(requirement))
                        + ", "
                        + Escape(identity.Document.LogicalPath)
                        + ", "
                        + (line.Line + 1).ToString(CultureInfo.InvariantCulture)
                        + ", "
                        + (line.Character + 1).ToString(CultureInfo.InvariantCulture)
                        + "))"
                );
            }
            Hidden(";\n");
        }

        private static string RequirementAccess(string values, int index, int count)
        {
            for (var i = count - 1; i > index; i--)
                values += ".Previous";
            return index == 0 ? values : values + ".Value";
        }

        private void RequirementProperties(IReadOnlyList<LuiRequirementSyntax> requirements)
        {
            foreach (var requirement in requirements)
            {
                if (namedComponent)
                {
                    Hidden("        private ");
                    Mapped(
                        document.Source.Substring(
                            requirement.TypeSpan.Start,
                            requirement.TypeSpan.Length
                        ),
                        requirement.TypeSpan,
                        LuiMapKind.Symbol
                    );
                    Hidden(
                        " __luiRequirement_"
                            + requirement.Name.Text.TrimStart('@')
                            + " = default!;\n"
                    );
                }
                Hidden("        private ");
                var start = text.Length;
                Mapped(
                    document.Source.Substring(
                        requirement.TypeSpan.Start,
                        requirement.TypeSpan.Length
                    ),
                    requirement.TypeSpan,
                    LuiMapKind.Symbol
                );
                RequirementMappings.Add((requirement, new TextSpan(start, text.Length - start)));
                Hidden(" ");
                Mapped(requirement.Name.Text, requirement.Name.Span, LuiMapKind.Symbol);
                Hidden(
                    namedComponent
                        ? " => __luiRequirement_" + requirement.Name.Text.TrimStart('@') + ";\n"
                        : " { get; }\n"
                );
                requirementArguments.Add(UniqueGeneratedName("__luiRequirement"));
                Mark(requirement.Keyword.Span, LuiMapKind.Structure);
                Mark(requirement.Semicolon.Span, LuiMapKind.Structure);
            }
        }

        private void Provider(LuiProvideSyntax provider, List<LuiDiagnostic> diagnostics)
        {
            var value =
                provider
                    .Attributes.SingleOrDefault(attribute => attribute.Name.Text == "value")
                    ?.Value as LuiExpressionSyntax;
            var children = provider
                .Children.Where(child => child is not LuiCommentSyntax)
                .ToArray();
            if (value is null || children.Length != 1)
            {
                Hidden("default!");
                return;
            }
            Hidden("global::Lucent.Core.Context.Provide(");
            var span = Expression(value);
            ProviderMappings.Add((value, new TextSpan(span.Start, span.Length)));
            Hidden(", ");
            if (providerDescriptors.TryGetValue(value.Span.Start, out var descriptor))
                Hidden(descriptor + ", ");
            if (children[0] is LuiElementSyntax element)
                Element(element, diagnostics);
            else if (children[0] is LuiExpressionBodySyntax expression)
            {
                ContentExpression(expression, false, diagnostics);
                var generated = ContentExpressions[ContentExpressions.Count - 1].Generated;
                ProviderRoots.Add(
                    (expression.Span, new TextSpan(generated.Start, generated.Length))
                );
            }
            else
                Hidden("default!");
            Hidden(")");
            Mark(provider.Name.Span, LuiMapKind.Structure);
        }
    }
}
