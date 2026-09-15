using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lucent.Lui.Compiler;

public static partial class LuiCompiler
{
    private const string StateAttributeName = "Lucent.Core.StateAttribute";
    private const string ComponentStateAttributeName = "Lucent.Core.ComponentStateAttribute";
    private const string ComponentContextTypeName = "Lucent.Core.ComponentContext";

    private static NamedComponentPlan? NamedPlan(
        LuiDocumentSyntax document,
        Compilation compilation,
        List<LuiDiagnostic> diagnostics
    )
    {
        if (document.Component is not { } component)
            return null;
        var metadataName = String.IsNullOrEmpty(document.Namespace)
            ? component.Name.Text
            : document.Namespace + "." + component.Name.Text;
        var type = compilation.GetTypeByMetadataName(metadataName);
        if (type is null)
        {
            diagnostics.Add(
                new LuiDiagnostic(
                    "LUI2050",
                    "Named component preparation did not provide the partial identity '"
                        + metadataName
                        + "'.",
                    component.Name.Span
                )
            );
            return null;
        }
        var preparedFactories = type.GetMembers("Create")
            .OfType<IMethodSymbol>()
            .Where(static method =>
                method.IsStatic
                && method.IsPartialDefinition
                && method.ReturnType.ToDisplayString() == "Lucent.Core.ComponentRecipe"
                && method
                    .GetAttributes()
                    .Any(attribute =>
                        attribute.AttributeClass?.ToDisplayString()
                        == "Lucent.Core.LucentComponentAttribute"
                    )
            )
            .ToArray();
        if (preparedFactories.Length == 0)
            diagnostics.Add(
                new LuiDiagnostic(
                    "LUI2050",
                    "Named component preparation did not provide the defining partial Create factory for '"
                        + metadataName
                        + "'.",
                    component.Name.Span
                )
            );
        if (
            type.GetAttributes()
                .Any(attribute =>
                    attribute.AttributeClass?.ToDisplayString() == ComponentStateAttributeName
                )
        )
            diagnostics.Add(
                new LuiDiagnostic(
                    "LUI2051",
                    "A named LUI component owns its state identity; do not add [ComponentState].",
                    component.Name.Span
                )
            );
        if (type.InstanceConstructors.Any(static constructor => !constructor.IsImplicitlyDeclared))
            diagnostics.Add(
                new LuiDiagnostic(
                    "LUI2052",
                    "A named LUI component cannot declare an instance constructor because the compiler owns mount initialization.",
                    component.Name.Span
                )
            );

        var luiSetup = component.Body.Any(static member =>
            member is LuiMemberSyntax { Kind: LuiMemberKind.Setup }
        );
        var preparationTrees = new HashSet<SyntaxTree>(
            preparedFactories
                .SelectMany(static method => method.DeclaringSyntaxReferences)
                .Select(static reference => reference.SyntaxTree)
        );
        var authoredSetups = compilation
            .SyntaxTrees.SelectMany(static tree => tree.GetRoot().DescendantNodes())
            .OfType<MethodDeclarationSyntax>()
            .Where(static method => method.Identifier.ValueText == "Setup")
            .Where(method =>
                !(
                    preparationTrees.Contains(method.SyntaxTree)
                    && method.Modifiers.Any(SyntaxKind.PartialKeyword)
                    && method.Body is null
                    && method.ExpressionBody is null
                )
            )
            .Select(method =>
                (
                    Syntax: method,
                    Symbol: compilation
                        .GetSemanticModel(method.SyntaxTree)
                        .GetDeclaredSymbol(method)
                )
            )
            .Where(item =>
                item.Symbol is not null
                && SymbolEqualityComparer.Default.Equals(item.Symbol.ContainingType, type)
            )
            .ToArray();
        var setupSymbol = authoredSetups.Length == 1 ? authoredSetups[0].Symbol : null;
        var validCompanionSetup =
            authoredSetups.Length == 1
            && authoredSetups[0].Syntax.Modifiers.Any(SyntaxKind.PartialKeyword)
            && setupSymbol
                is { IsStatic: false, IsGenericMethod: false, IsAsync: false, ReturnsVoid: true }
            && setupSymbol.Parameters.Length == 1
            && setupSymbol.Parameters[0].RefKind == RefKind.None
            && setupSymbol.Parameters[0].Type.ToDisplayString() == ComponentContextTypeName
            && (
                authoredSetups[0].Syntax.Body is not null
                || authoredSetups[0].Syntax.ExpressionBody is not null
            );
        if (authoredSetups.Length != 0 && !validCompanionSetup)
            diagnostics.Add(
                new LuiDiagnostic(
                    "LUI2059",
                    "A companion setup must be one partial void Setup(ComponentContext) implementation.",
                    component.Name.Span
                )
            );
        if (luiSetup && validCompanionSetup)
            diagnostics.Add(
                new LuiDiagnostic(
                    "LUI2056",
                    "Setup(ComponentContext) may be implemented in either the LUI component or its companion, but not both.",
                    component.Name.Span
                )
            );

        var states = new List<NamedCompanionState>();
        foreach (
            var property in type.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(static property =>
                    property
                        .GetAttributes()
                        .Any(attribute =>
                            attribute.AttributeClass?.ToDisplayString() == StateAttributeName
                        )
                )
                .OrderBy(static property => property.Name, StringComparer.Ordinal)
        )
        {
            var syntax =
                property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                as PropertyDeclarationSyntax;
            var valid =
                syntax is not null
                && syntax.Modifiers.Any(SyntaxKind.PartialKeyword)
                && !property.IsStatic
                && property.Parameters.Length == 0
                && property.GetMethod is not null
                && property.SetMethod is not null
                && !property.SetMethod.IsInitOnly
                && syntax.AccessorList is { Accessors.Count: 2 }
                && syntax.AccessorList.Accessors.All(static accessor =>
                    accessor.Body is null && accessor.ExpressionBody is null
                );
            if (!valid)
            {
                diagnostics.Add(
                    new LuiDiagnostic(
                        "LUI2053",
                        "Companion [State] property '"
                            + property.Name
                            + "' must be a non-static partial auto-property with get and set accessors.",
                        component.Name.Span
                    )
                );
                continue;
            }
            var stateAttributes = property
                .GetAttributes()
                .Where(attribute =>
                    attribute.AttributeClass?.ToDisplayString() == StateAttributeName
                )
                .ToArray();
            if (stateAttributes.Length != 1)
            {
                diagnostics.Add(
                    new LuiDiagnostic(
                        "LUI2053",
                        "Companion [State] property '"
                            + property.Name
                            + "' must declare exactly one [State] attribute.",
                        component.Name.Span
                    )
                );
                continue;
            }
            var attribute = stateAttributes[0];
            var initializerName =
                attribute
                    .NamedArguments.FirstOrDefault(static item => item.Key == "Initializer")
                    .Value.Value as string;
            if (attribute.ConstructorArguments.Length == 1 && initializerName is not null)
            {
                diagnostics.Add(
                    new LuiDiagnostic(
                        "LUI2057",
                        "Companion [State] property '"
                            + property.Name
                            + "' cannot specify both a constant and a named initializer.",
                        component.Name.Span
                    )
                );
                continue;
            }
            string expression;
            if (initializerName is not null)
            {
                var methods = type.GetMembers(initializerName)
                    .OfType<IMethodSymbol>()
                    .Where(method =>
                        method.IsStatic
                        && !method.IsGenericMethod
                        && method.Parameters.Length == 1
                        && method.Parameters[0].Type.ToDisplayString() == ComponentContextTypeName
                        && SymbolEqualityComparer.IncludeNullability.Equals(
                            method.ReturnType,
                            property.Type
                        )
                    )
                    .ToArray();
                if (methods.Length != 1)
                {
                    diagnostics.Add(
                        new LuiDiagnostic(
                            "LUI2054",
                            "Companion [State] initializer '"
                                + initializerName
                                + "' must be one static method taking ComponentContext and returning the exact property type.",
                            component.Name.Span
                        )
                    );
                    continue;
                }
                expression = EscapeIdentifier(initializerName) + "(context)";
            }
            else if (attribute.ConstructorArguments.Length == 1)
            {
                var constant = attribute.ConstructorArguments[0];
                if (
                    !NamedStateConstantMatches(property.Type, constant)
                    || !TryNamedStateConstant(constant, out expression)
                )
                {
                    diagnostics.Add(
                        new LuiDiagnostic(
                            "LUI2055",
                            "Companion [State] property '"
                                + property.Name
                                + "' has an unsupported constant initializer.",
                            component.Name.Span
                        )
                    );
                    continue;
                }
            }
            else
            {
                if (
                    property.Type.IsReferenceType
                    && property.NullableAnnotation != NullableAnnotation.Annotated
                )
                {
                    diagnostics.Add(
                        new LuiDiagnostic(
                            "LUI2058",
                            "Companion [State] property '"
                                + property.Name
                                + "' requires a constant or named initializer because its type is non-nullable.",
                            component.Name.Span
                        )
                    );
                    continue;
                }
                expression = "default!";
            }
            states.Add(
                new NamedCompanionState(
                    property.Name,
                    property.Type.ToDisplayString(FullyQualifiedNullableFormat),
                    property.DeclaredAccessibility switch
                    {
                        Accessibility.Public => "public",
                        Accessibility.Internal => "internal",
                        Accessibility.Protected => "protected",
                        Accessibility.ProtectedOrInternal => "protected internal",
                        Accessibility.ProtectedAndInternal => "private protected",
                        _ => "private",
                    },
                    expression
                )
            );
        }
        return new NamedComponentPlan(type, states);
    }

    private static bool TryNamedStateConstant(TypedConstant constant, out string expression)
    {
        if (constant.IsNull)
        {
            expression = "default!";
            return true;
        }
        if (constant.Type?.TypeKind == TypeKind.Enum)
        {
            expression =
                "("
                + constant.Type.ToDisplayString(FullyQualifiedNullableFormat)
                + ")"
                + Convert.ToString(constant.Value, CultureInfo.InvariantCulture);
            return true;
        }
        expression = constant.Value switch
        {
            string value => SymbolDisplay.FormatLiteral(value, true),
            char value => SymbolDisplay.FormatLiteral(value, true),
            bool value => value ? "true" : "false",
            float value when Single.IsNaN(value) => "global::System.Single.NaN",
            float value when Single.IsPositiveInfinity(value) =>
                "global::System.Single.PositiveInfinity",
            float value when Single.IsNegativeInfinity(value) =>
                "global::System.Single.NegativeInfinity",
            double value when Double.IsNaN(value) => "global::System.Double.NaN",
            double value when Double.IsPositiveInfinity(value) =>
                "global::System.Double.PositiveInfinity",
            double value when Double.IsNegativeInfinity(value) =>
                "global::System.Double.NegativeInfinity",
            float value => value.ToString("R", CultureInfo.InvariantCulture) + "F",
            double value => value.ToString("R", CultureInfo.InvariantCulture) + "D",
            decimal value => value.ToString(CultureInfo.InvariantCulture) + "M",
            uint value => value.ToString(CultureInfo.InvariantCulture) + "U",
            long value => value.ToString(CultureInfo.InvariantCulture) + "L",
            ulong value => value.ToString(CultureInfo.InvariantCulture) + "UL",
            byte or sbyte or short or ushort or int => Convert.ToString(
                constant.Value,
                CultureInfo.InvariantCulture
            )!,
            _ => String.Empty,
        };
        return expression.Length != 0;
    }

    private static bool NamedStateConstantMatches(ITypeSymbol type, TypedConstant constant)
    {
        if (constant.IsNull)
            return type.IsReferenceType
                || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        var target =
            type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? ((INamedTypeSymbol)type).TypeArguments[0]
                : type;
        return SymbolEqualityComparer.Default.Equals(target, constant.Type);
    }

    private sealed class NamedComponentPlan
    {
        internal NamedComponentPlan(
            INamedTypeSymbol type,
            IReadOnlyList<NamedCompanionState> states
        )
        {
            Type = type;
            States = states;
        }

        internal INamedTypeSymbol Type { get; }
        internal IReadOnlyList<NamedCompanionState> States { get; }
    }

    private sealed class NamedCompanionState
    {
        internal NamedCompanionState(
            string name,
            string type,
            string accessibility,
            string initializer
        )
        {
            Name = name;
            Type = type;
            Accessibility = accessibility;
            Initializer = initializer;
        }

        internal string Name { get; }
        internal string Type { get; }
        internal string Accessibility { get; }
        internal string Initializer { get; }
    }
}
