using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Lucent.Lui.Compiler;

/// <summary>Roslyn-backed discovery of Core authoring property metadata.</summary>
/// <remarks>
/// Property groups and field overrides are declared by Core attributes. No parallel list of
/// property names is maintained by the compiler, so metadata, lowering, generation, and editor
/// tooling all resolve the same symbols.
/// </remarks>
public static class LuiPropertyCatalog
{
    private const string GroupAttributeName = "Lucent.Core.StylePropertyGroupAttribute";
    private const string PropertyAttributeName = "Lucent.Core.StylePropertyAttribute";
    private const string PropertyTypeName = "Lucent.Core.Property`1";

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
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );

    private static readonly ConditionalWeakTable<
        Compilation,
        Lazy<IReadOnlyList<LuiAuthorPropertyDescriptor>>
    > DiscoveryCache = new();

    /// <summary>Discovers every public static <c>Property&lt;T&gt;</c> in an attributed property group.</summary>
    public static IReadOnlyList<LuiAuthorPropertyDescriptor> Discover(Compilation compilation)
    {
        if (compilation is null)
            throw new ArgumentNullException(nameof(compilation));
        return DiscoveryCache
            .GetValue(
                compilation,
                key =>
                    new(
                        () => Array.AsReadOnly(DiscoverUncached(key)),
                        LazyThreadSafetyMode.ExecutionAndPublication
                    )
            )
            .Value;
    }

    private static LuiAuthorPropertyDescriptor[] DiscoverUncached(Compilation compilation)
    {
        var propertyType = compilation.GetTypeByMetadataName(PropertyTypeName);
        var groupAttributeType = compilation.GetTypeByMetadataName(GroupAttributeName);
        if (propertyType is null || groupAttributeType is null)
            return Array.Empty<LuiAuthorPropertyDescriptor>();

        var descriptors = new List<LuiAuthorPropertyDescriptor>();
        var assemblies = new HashSet<ISymbol>(SymbolEqualityComparer.Default)
        {
            compilation.Assembly,
        };
        assemblies.Add(groupAttributeType.ContainingAssembly);

        foreach (var assembly in assemblies.OfType<IAssemblySymbol>())
        foreach (var group in Types(assembly.GlobalNamespace))
        {
            var groupAttribute = FindAttribute(group, GroupAttributeName);
            if (groupAttribute is null || !group.IsStatic)
                continue;
            var groupDefaults = ReadMetadata(groupAttribute).WithDefaults();
            foreach (
                var field in group
                    .GetMembers()
                    .OfType<IFieldSymbol>()
                    .Where(field =>
                        field.IsStatic
                        && field.IsReadOnly
                        && field.DeclaredAccessibility == Accessibility.Public
                        && field.Type is INamedTypeSymbol property
                        && SymbolEqualityComparer.Default.Equals(
                            property.OriginalDefinition,
                            propertyType
                        )
                    )
                    .OrderBy(field => field.Name, StringComparer.Ordinal)
            )
            {
                var fieldAttribute = FindAttribute(field, PropertyAttributeName);
                var fieldMetadata = fieldAttribute is null
                    ? groupDefaults
                    : groupDefaults.Merge(ReadMetadata(fieldAttribute));
                var valueType = ((INamedTypeSymbol)field.Type).TypeArguments[0];
                var symbolIdentity = field.ToDisplayString(FullyQualifiedMemberFormat);
                var authorName = String.IsNullOrWhiteSpace(fieldMetadata.Name)
                    ? field.Name
                    : fieldMetadata.Name!;
                var styleTarget = String.IsNullOrWhiteSpace(fieldMetadata.StyleTarget)
                    ? symbolIdentity
                    : fieldMetadata.StyleTarget!;
                var declaringType = group.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (declaringType.StartsWith("global::", StringComparison.Ordinal))
                    declaringType = declaringType.Substring("global::".Length);
                descriptors.Add(
                    new LuiAuthorPropertyDescriptor(
                        declaringType,
                        field.Name,
                        symbolIdentity,
                        valueType.ToDisplayString(FullyQualifiedNullableFormat),
                        authorName,
                        fieldMetadata.Aliases!,
                        fieldMetadata.Capabilities!.Value,
                        fieldMetadata.InputForms!.Value,
                        styleTarget,
                        fieldMetadata.SemanticTarget,
                        fieldMetadata.TransitionEligible!.Value,
                        fieldMetadata.PaintInvalidating!.Value,
                        SymbolEqualityComparer.Default.Equals(
                            group.ContainingAssembly,
                            compilation.Assembly
                        )
                    )
                );
            }
        }

        return descriptors
            .OrderBy(descriptor => descriptor.SymbolIdentity, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.AuthorName, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Resolves a property by its canonical name, field name, alias, or symbol identity.</summary>
    public static LuiAuthorPropertyDescriptor? Find(Compilation compilation, string nameOrIdentity)
    {
        if (compilation is null)
            throw new ArgumentNullException(nameof(compilation));
        if (nameOrIdentity is null)
            throw new ArgumentNullException(nameof(nameOrIdentity));
        return Discover(compilation)
            .FirstOrDefault(descriptor =>
                StringComparer.Ordinal.Equals(descriptor.SymbolIdentity, nameOrIdentity)
                || descriptor.Names.Contains(nameOrIdentity, StringComparer.Ordinal)
            );
    }

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol @namespace)
    {
        foreach (var type in @namespace.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in NestedTypes(type))
                yield return nested;
        }
        foreach (var child in @namespace.GetNamespaceMembers())
        foreach (var type in Types(child))
            yield return type;
    }

    private static IEnumerable<INamedTypeSymbol> NestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var child in NestedTypes(nested))
                yield return child;
        }
    }

    private static AttributeData? FindAttribute(ISymbol symbol, string name) =>
        symbol
            .GetAttributes()
            .FirstOrDefault(attribute =>
                StringComparer.Ordinal.Equals(attribute.AttributeClass?.ToDisplayString(), name)
            );

    private static Metadata ReadMetadata(AttributeData attribute)
    {
        var metadata = new Metadata();
        foreach (var argument in attribute.NamedArguments)
        {
            switch (argument.Key)
            {
                case "Name":
                    metadata.Name = argument.Value.Value as string;
                    break;
                case "Aliases":
                    metadata.Aliases = Strings(argument.Value);
                    break;
                case "Capabilities":
                    metadata.Capabilities = (LuiAuthoringCapabilities)Integer(argument.Value);
                    break;
                case "InputForms":
                    metadata.InputForms = (LuiAuthoringInputForms)Integer(argument.Value);
                    break;
                case "StyleTarget":
                    metadata.StyleTarget = argument.Value.Value as string;
                    break;
                case "SemanticTarget":
                    metadata.SemanticTarget = argument.Value.Value as string;
                    break;
                case "TransitionEligible":
                    metadata.TransitionEligible = Boolean(argument.Value);
                    break;
                case "PaintInvalidating":
                    metadata.PaintInvalidating = Boolean(argument.Value);
                    break;
            }
        }
        return metadata;
    }

    private static int Integer(TypedConstant value) =>
        value.Value is null
            ? 0
            : Convert.ToInt32(value.Value, System.Globalization.CultureInfo.InvariantCulture);

    private static bool Boolean(TypedConstant value) => value.Value is bool result && result;

    private static string[] Strings(TypedConstant value) =>
        value.Kind == TypedConstantKind.Array
            ? value
                .Values.Where(item => item.Value is string)
                .Select(item => (string)item.Value!)
                .ToArray()
        : value.Value is string single ? new[] { single }
        : Array.Empty<string>();

    private sealed class Metadata
    {
        internal string? Name { get; set; }
        internal IReadOnlyList<string>? Aliases { get; set; }
        internal LuiAuthoringCapabilities? Capabilities { get; set; }
        internal LuiAuthoringInputForms? InputForms { get; set; }
        internal string? StyleTarget { get; set; }
        internal string? SemanticTarget { get; set; }
        internal bool? TransitionEligible { get; set; }
        internal bool? PaintInvalidating { get; set; }

        internal Metadata WithDefaults() =>
            new()
            {
                Name = Name,
                Aliases = Aliases ?? Array.Empty<string>(),
                Capabilities = Capabilities ?? LuiAuthoringCapabilities.Styled,
                InputForms =
                    InputForms
                    ?? (
                        LuiAuthoringInputForms.Value
                        | LuiAuthoringInputForms.Reader
                        | LuiAuthoringInputForms.Token
                    ),
                StyleTarget = StyleTarget,
                SemanticTarget = SemanticTarget,
                TransitionEligible = TransitionEligible ?? false,
                PaintInvalidating = PaintInvalidating ?? true,
            };

        internal Metadata Merge(Metadata overrideMetadata) =>
            new()
            {
                Name = overrideMetadata.Name ?? Name,
                Aliases = overrideMetadata.Aliases ?? Aliases,
                Capabilities = overrideMetadata.Capabilities ?? Capabilities,
                InputForms = overrideMetadata.InputForms ?? InputForms,
                StyleTarget = overrideMetadata.StyleTarget ?? StyleTarget,
                SemanticTarget = overrideMetadata.SemanticTarget ?? SemanticTarget,
                TransitionEligible = overrideMetadata.TransitionEligible ?? TransitionEligible,
                PaintInvalidating = overrideMetadata.PaintInvalidating ?? PaintInvalidating,
            };
    }
}
