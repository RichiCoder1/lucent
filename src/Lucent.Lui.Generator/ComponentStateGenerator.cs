using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lucent.Lui.Generator;

/// <summary>Generates explicit partial properties for opt-in per-mount component state.</summary>
[Generator]
public sealed class ComponentStateGenerator : IIncrementalGenerator
{
    private const string StateAttributeName = "Lucent.Core.StateAttribute";
    private const string ContextTypeName = "Lucent.Core.ComponentContext";

    private static readonly DiagnosticDescriptor InvalidType = Descriptor(
        "LUI4101",
        "Invalid component state type",
        "Component state '{0}' must be a top-level, non-generic, sealed partial class with no declared instance constructor"
    );
    private static readonly DiagnosticDescriptor InvalidProperty = Descriptor(
        "LUI4102",
        "Invalid component state property",
        "State property '{0}' must be a non-static explicit partial property with get and set accessors"
    );
    private static readonly DiagnosticDescriptor MissingInitialValue = Descriptor(
        "LUI4103",
        "Component state requires initialization",
        "State property '{0}' has a non-nullable reference type and requires a constant or named initializer"
    );
    private static readonly DiagnosticDescriptor InvalidConstant = Descriptor(
        "LUI4104",
        "Invalid component state constant",
        "State property '{0}' has a constant that is not assignable to its exact property type"
    );
    private static readonly DiagnosticDescriptor InvalidInitializer = Descriptor(
        "LUI4105",
        "Invalid component state initializer",
        "Initializer '{1}' for state property '{0}' must be one static, non-generic method taking ComponentContext and returning the exact property type"
    );
    private static readonly DiagnosticDescriptor ConflictingInitialization = Descriptor(
        "LUI4106",
        "Conflicting component state initialization",
        "State property '{0}' cannot specify both a constant and a named initializer"
    );
    private static readonly DiagnosticDescriptor AsyncInitializeHook = Descriptor(
        "LUI4107",
        "Component state initialization must be synchronous",
        "Initialize hook for component state '{0}' cannot be async"
    );
    private static readonly DiagnosticDescriptor NamedComponentState = Descriptor(
        "LUI4108",
        "Named LUI component owns its state",
        "Named LUI component '{0}' cannot also use [ComponentState]"
    );

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var states = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Lucent.Core.ComponentStateAttribute",
            static (node, _) => node is TypeDeclarationSyntax,
            static (input, _) => Build(input)
        );
        context.RegisterSourceOutput(states, static (output, state) => Publish(output, state));
    }

    private static StateModel Build(GeneratorAttributeSyntaxContext input)
    {
        var type = (INamedTypeSymbol)input.TargetSymbol;
        var declaration = (TypeDeclarationSyntax)input.TargetNode;
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var validType =
            type.ContainingType is null
            && type.TypeParameters.Length == 0
            && type.TypeKind == TypeKind.Class
            && declaration is ClassDeclarationSyntax
            && type.IsSealed
            && type.BaseType?.SpecialType == SpecialType.System_Object
            && type.DeclaredAccessibility
                is Microsoft.CodeAnalysis.Accessibility.Public
                    or Microsoft.CodeAnalysis.Accessibility.Internal
            && declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
            && !type.InstanceConstructors.Any(constructor => !constructor.IsImplicitlyDeclared);
        var namedComponent = type.GetMembers("Create")
            .OfType<IMethodSymbol>()
            .Any(static method =>
                method.IsPartialDefinition
                && method
                    .GetAttributes()
                    .Any(attribute =>
                        attribute.AttributeClass?.ToDisplayString()
                        == "Lucent.Core.LucentComponentAttribute"
                    )
            );
        if (namedComponent)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    NamedComponentState,
                    declaration.Identifier.GetLocation(),
                    type.Name
                )
            );
            validType = false;
        }
        if (!validType)
            diagnostics.Add(
                Diagnostic.Create(InvalidType, declaration.Identifier.GetLocation(), type.Name)
            );
        var asyncHook = type.GetMembers("Initialize")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method =>
                method.IsAsync
                && !method.IsStatic
                && !method.IsGenericMethod
                && method.ReturnsVoid
                && method.Parameters.Length == 1
                && method.Parameters[0].Type.ToDisplayString() == ContextTypeName
            );
        if (asyncHook is not null)
            diagnostics.Add(
                Diagnostic.Create(
                    AsyncInitializeHook,
                    asyncHook.Locations.FirstOrDefault(),
                    type.Name
                )
            );

        var properties = ImmutableArray.CreateBuilder<PropertyModel>();
        foreach (
            var property in type.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(HasStateAttribute)
                .OrderBy(property => property.Name, StringComparer.Ordinal)
        )
        {
            var syntax =
                property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                as PropertyDeclarationSyntax;
            var location = syntax?.Identifier.GetLocation() ?? property.Locations.FirstOrDefault();
            var validProperty =
                syntax is not null
                && syntax.Modifiers.Any(SyntaxKind.PartialKeyword)
                && !property.IsStatic
                && property.Parameters.Length == 0
                && property.GetMethod is not null
                && property.SetMethod is not null
                && !property.IsRequired
                && syntax.AccessorList is { Accessors.Count: 2 }
                && syntax.AccessorList.Accessors.Any(accessor =>
                    accessor.IsKind(SyntaxKind.GetAccessorDeclaration)
                )
                && syntax.AccessorList.Accessors.Any(accessor =>
                    accessor.IsKind(SyntaxKind.SetAccessorDeclaration)
                )
                && syntax.AccessorList.Accessors.All(accessor =>
                    accessor.Body is null
                    && accessor.ExpressionBody is null
                    && accessor.Modifiers.Count == 0
                )
                && syntax.Modifiers.All(modifier =>
                    modifier.IsKind(SyntaxKind.PartialKeyword)
                    || modifier.IsKind(SyntaxKind.PublicKeyword)
                    || modifier.IsKind(SyntaxKind.InternalKeyword)
                    || modifier.IsKind(SyntaxKind.PrivateKeyword)
                    || modifier.IsKind(SyntaxKind.ProtectedKeyword)
                );
            if (!validProperty)
            {
                diagnostics.Add(
                    Diagnostic.Create(InvalidProperty, location, type.Name + "." + property.Name)
                );
                continue;
            }

            var attributes = property
                .GetAttributes()
                .Where(item => item.AttributeClass?.ToDisplayString() == StateAttributeName)
                .ToArray();
            if (attributes.Length != 1)
            {
                diagnostics.Add(
                    Diagnostic.Create(InvalidProperty, location, type.Name + "." + property.Name)
                );
                continue;
            }
            var attribute = attributes[0];
            var hasConstant = attribute.ConstructorArguments.Length == 1;
            var initializer = attribute
                .NamedArguments.FirstOrDefault(pair => pair.Key == "Initializer")
                .Value;
            var initializerName = initializer.Value as string;
            if (hasConstant && initializerName is not null)
            {
                diagnostics.Add(
                    Diagnostic.Create(
                        ConflictingInitialization,
                        location,
                        type.Name + "." + property.Name
                    )
                );
                continue;
            }

            string? expression;
            if (initializerName is not null)
            {
                var methods = type.GetMembers(initializerName)
                    .OfType<IMethodSymbol>()
                    .Where(method =>
                        method.IsStatic
                        && !method.IsGenericMethod
                        && method.Parameters.Length == 1
                        && method.Parameters[0].Type.ToDisplayString() == ContextTypeName
                        && SymbolEqualityComparer.IncludeNullability.Equals(
                            method.ReturnType,
                            property.Type
                        )
                    )
                    .ToArray();
                if (methods.Length != 1)
                {
                    diagnostics.Add(
                        Diagnostic.Create(
                            InvalidInitializer,
                            location,
                            type.Name + "." + property.Name,
                            initializerName
                        )
                    );
                    continue;
                }
                expression = Escape(initializerName) + "(context)";
            }
            else if (hasConstant)
            {
                var constant = attribute.ConstructorArguments[0];
                if (!TryConstant(property.Type, constant, out expression))
                {
                    diagnostics.Add(
                        Diagnostic.Create(
                            InvalidConstant,
                            location,
                            type.Name + "." + property.Name
                        )
                    );
                    continue;
                }
            }
            else if (
                property.Type.IsReferenceType
                && property.NullableAnnotation != NullableAnnotation.Annotated
            )
            {
                diagnostics.Add(
                    Diagnostic.Create(
                        MissingInitialValue,
                        location,
                        type.Name + "." + property.Name
                    )
                );
                continue;
            }
            else
                expression = "default!";

            properties.Add(
                new PropertyModel(
                    property.Name,
                    property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Accessibility(property.DeclaredAccessibility),
                    expression!
                )
            );
        }
        return new StateModel(type, validType, properties.ToImmutable(), diagnostics.ToImmutable());
    }

    private static void Publish(SourceProductionContext output, StateModel state)
    {
        foreach (var diagnostic in state.Diagnostics)
            output.ReportDiagnostic(diagnostic);
        if (!state.ValidType || state.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return;
        var type = state.Type;
        var name = Escape(type.Name);
        var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");
        if (!type.ContainingNamespace.IsGlobalNamespace)
            source
                .Append("namespace ")
                .Append(type.ContainingNamespace.ToDisplayString())
                .AppendLine(";");
        source
            .Append(Accessibility(type.DeclaredAccessibility))
            .Append(" sealed partial class ")
            .Append(name)
            .Append(" : global::Lucent.Core.IComponentState<")
            .Append(fullName)
            .AppendLine(">");
        source.AppendLine("{");
        foreach (var property in state.Properties)
            source
                .Append("    private global::Lucent.Core.Signal<")
                .Append(property.Type)
                .Append(">? ")
                .Append(StateField(property.Name))
                .AppendLine(";");
        source.Append("    private ").Append(name).AppendLine("() { }");
        source
            .Append("    static ")
            .Append(fullName)
            .Append(" global::Lucent.Core.IComponentState<")
            .Append(fullName)
            .AppendLine(">.CreateComponentState(global::Lucent.Core.ComponentContext context)");
        source.AppendLine("    {");
        source.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(context);");
        source.Append("        var state = new ").Append(name).AppendLine("();");
        foreach (var property in state.Properties)
            source
                .Append("        state.")
                .Append(StateField(property.Name))
                .Append(" = context.State<")
                .Append(property.Type)
                .Append(">(")
                .Append(property.Expression)
                .Append(", ")
                .Append(SymbolDisplay.FormatLiteral(type.Name + "." + property.Name, true))
                .AppendLine(");");
        source.AppendLine("        state.Initialize(context);");
        source.AppendLine("        return state;");
        source.AppendLine("    }");
        source.AppendLine(
            "    partial void Initialize(global::Lucent.Core.ComponentContext context);"
        );
        foreach (var property in state.Properties)
        {
            var field = StateField(property.Name);
            var message = SymbolDisplay.FormatLiteral(
                type.Name + "." + property.Name + " is not attached to a component mount.",
                true
            );
            source
                .Append("    ")
                .Append(property.Accessibility)
                .Append(" partial ")
                .Append(property.Type)
                .Append(' ')
                .Append(Escape(property.Name))
                .AppendLine()
                .AppendLine("    {")
                .Append("        get => (")
                .Append(field)
                .Append(" ?? throw new global::System.InvalidOperationException(")
                .Append(message)
                .AppendLine(")).Value;")
                .Append("        set => (")
                .Append(field)
                .Append(" ?? throw new global::System.InvalidOperationException(")
                .Append(message)
                .AppendLine(")).Value = value;")
                .AppendLine("    }");
        }
        source.AppendLine("}");
        output.AddSource("Lucent.ComponentState." + Hash(fullName) + ".g.cs", source.ToString());
    }

    private static bool HasStateAttribute(IPropertySymbol property) =>
        property
            .GetAttributes()
            .Any(attribute => attribute.AttributeClass?.ToDisplayString() == StateAttributeName);

    private static bool TryConstant(
        ITypeSymbol type,
        TypedConstant constant,
        out string? expression
    )
    {
        expression = null;
        if (constant.IsNull)
        {
            if (
                type.IsReferenceType
                || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            )
            {
                expression = "default!";
                return true;
            }
            return false;
        }
        var target =
            type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? ((INamedTypeSymbol)type).TypeArguments[0]
                : type;
        if (!SymbolEqualityComparer.Default.Equals(target, constant.Type))
            return false;
        var value = constant.Value!;
        string literal;
        if (target.TypeKind == TypeKind.Enum)
        {
            var underlying = ((INamedTypeSymbol)target).EnumUnderlyingType!;
            literal =
                "("
                + target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                + ")"
                + Numeric(value, underlying.SpecialType);
        }
        else if (value is string text)
            literal = SymbolDisplay.FormatLiteral(text, true);
        else if (value is char character)
            literal = SymbolDisplay.FormatLiteral(character, true);
        else if (value is bool boolean)
            literal = boolean ? "true" : "false";
        else
            literal = Numeric(value, target.SpecialType);
        expression =
            type is INamedTypeSymbol named
            && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? "("
                    + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    + ")"
                    + literal
                : literal;
        return true;
    }

    private static string Numeric(object value, SpecialType type)
    {
        if (type == SpecialType.System_Single)
        {
            var single = (float)value;
            if (float.IsNaN(single))
                return "global::System.Single.NaN";
            if (float.IsPositiveInfinity(single))
                return "global::System.Single.PositiveInfinity";
            if (float.IsNegativeInfinity(single))
                return "global::System.Single.NegativeInfinity";
        }
        if (type == SpecialType.System_Double)
        {
            var number = (double)value;
            if (double.IsNaN(number))
                return "global::System.Double.NaN";
            if (double.IsPositiveInfinity(number))
                return "global::System.Double.PositiveInfinity";
            if (double.IsNegativeInfinity(number))
                return "global::System.Double.NegativeInfinity";
        }
        var text = Convert.ToString(value, CultureInfo.InvariantCulture)!;
        return type switch
        {
            SpecialType.System_Single => text + "F",
            SpecialType.System_Double => text + "D",
            SpecialType.System_Decimal => text + "M",
            SpecialType.System_Int64 => text + "L",
            SpecialType.System_UInt32 => text + "U",
            SpecialType.System_UInt64 => text + "UL",
            _ => text,
        };
    }

    private static string Accessibility(Accessibility accessibility) =>
        accessibility switch
        {
            Microsoft.CodeAnalysis.Accessibility.Public => "public",
            Microsoft.CodeAnalysis.Accessibility.Internal => "internal",
            Microsoft.CodeAnalysis.Accessibility.Private => "private",
            Microsoft.CodeAnalysis.Accessibility.Protected => "protected",
            Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => "protected internal",
            Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => "private protected",
            _ => "internal",
        };

    private static string Escape(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    private static string StateField(string propertyName) => Escape("__state_" + propertyName);

    private static string Hash(string value)
    {
        uint hash = 2166136261;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= 16777619;
        }
        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }

    private static DiagnosticDescriptor Descriptor(string id, string title, string message) =>
        new(id, title, message, "Lucent.State", DiagnosticSeverity.Error, true);

    private sealed class StateModel(
        INamedTypeSymbol type,
        bool validType,
        ImmutableArray<PropertyModel> properties,
        ImmutableArray<Diagnostic> diagnostics
    )
    {
        internal INamedTypeSymbol Type { get; } = type;
        internal bool ValidType { get; } = validType;
        internal ImmutableArray<PropertyModel> Properties { get; } = properties;
        internal ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;
    }

    private sealed class PropertyModel(
        string name,
        string type,
        string accessibility,
        string expression
    )
    {
        internal string Name { get; } = name;
        internal string Type { get; } = type;
        internal string Accessibility { get; } = accessibility;
        internal string Expression { get; } = expression;
    }
}
