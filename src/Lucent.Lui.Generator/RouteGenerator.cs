using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lucent.Lui.Generator;

/// <summary>Generates explicit typed route factories and NativeAOT-safe descriptors.</summary>
[Generator]
public sealed class RouteGenerator : IIncrementalGenerator
{
    private static readonly char[] QuerySeparator = ['?'];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const int MaximumQueryValueUtf8Bytes = 1024;
    private const string RouteAttribute = "Lucent.Core.LucentRouteAttribute";
    private const string ModuleAttribute = "Lucent.Core.LucentRouteModuleAttribute";
    private static readonly DiagnosticDescriptor InvalidModule = Error(
        "LUI4201",
        "Invalid route module",
        "Route module '{0}' must be a top-level, non-generic static partial class with an explicit Reject fallback policy"
    );
    private static readonly DiagnosticDescriptor InvalidRoute = Error(
        "LUI4202",
        "Invalid route declaration",
        "Route '{0}' must be a top-level readonly record struct with one supported primary-constructor parameter list"
    );
    private static readonly DiagnosticDescriptor InvalidTemplate = Error(
        "LUI4203",
        "Invalid route template",
        "Route '{0}' has an invalid template: {1}"
    );
    private static readonly DiagnosticDescriptor UnsupportedParameter = Error(
        "LUI4204",
        "Unsupported route parameter",
        "Route parameter '{0}' must have exact type string, int, long, Guid, bool, or a non-flags enum without aliases"
    );
    private static readonly DiagnosticDescriptor DuplicateIdentity = Error(
        "LUI4205",
        "Duplicate route identity",
        "Generated route identity or factory '{0}' is duplicated in module '{1}'"
    );
    private static readonly DiagnosticDescriptor InvalidParent = Error(
        "LUI4206",
        "Invalid route parent",
        "Route '{0}' has an invalid parent relationship: {1}"
    );
    private static readonly DiagnosticDescriptor AmbiguousRoute = Error(
        "LUI4207",
        "Ambiguous route shape",
        "Routes '{0}' and '{1}' have overlapping path shapes without a precedence winner"
    );
    private static readonly DiagnosticDescriptor InvalidComponent = Error(
        "LUI4208",
        "Invalid route component",
        "Route '{0}' component must expose exactly one accessible, non-generic static Create method callable without arguments and returning ComponentRecipe"
    );
    private static readonly DiagnosticDescriptor MissingComponent = Error(
        "LUI4209",
        "Missing route component",
        "Route '{0}' must declare Component because route module '{1}' uses generated component mappings"
    );
    private static readonly DiagnosticDescriptor GeneratedMemberCollision = Error(
        "LUI4210",
        "Route module member collision",
        "Route module '{0}' cannot generate member '{1}' because that name is already declared or generated"
    );

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modules = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                ModuleAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (input, _) => BuildModule(input)
            )
            .Collect();
        var routes = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                RouteAttribute,
                static (node, _) => node is RecordDeclarationSyntax,
                static (input, _) => BuildRoute(input)
            )
            .Collect();
        var projectDirectory = context.AnalyzerConfigOptionsProvider.Select(
            static (options, _) =>
                options.GlobalOptions.TryGetValue("build_property.ProjectDir", out var value)
                    ? value
                    : null
        );
        context.RegisterSourceOutput(
            modules.Combine(routes).Combine(projectDirectory),
            static (output, pair) => Publish(output, pair.Left.Left, pair.Left.Right, pair.Right)
        );
    }

    private static ModuleModel BuildModule(GeneratorAttributeSyntaxContext input)
    {
        var type = (INamedTypeSymbol)input.TargetSymbol;
        var syntax = (ClassDeclarationSyntax)input.TargetNode;
        var attribute = input.Attributes[0];
        var valid =
            type.ContainingType is null
            && type.TypeParameters.Length == 0
            && type.IsStatic
            && syntax.Modifiers.Any(SyntaxKind.PartialKeyword)
            && attribute.ConstructorArguments.Length == 1
            && attribute.ConstructorArguments[0].Value is int value
            && value == 0;
        var span = syntax.Identifier.GetLocation().GetMappedLineSpan();
        return new ModuleModel(
            type,
            syntax.Identifier.GetLocation(),
            new SourceModel(
                span.Path,
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1
            ),
            valid
        );
    }

    private static RouteModel BuildRoute(GeneratorAttributeSyntaxContext input)
    {
        var type = (INamedTypeSymbol)input.TargetSymbol;
        var syntax = (RecordDeclarationSyntax)input.TargetNode;
        var attribute = input.Attributes[0];
        var module =
            attribute.ConstructorArguments.Length > 0
                ? attribute.ConstructorArguments[0].Value as INamedTypeSymbol
                : null;
        var template =
            attribute.ConstructorArguments.Length > 1
                ? attribute.ConstructorArguments[1].Value as string
                : null;
        var id = NamedString(attribute, "Id") ?? DefaultName(type.Name);
        var parent = NamedType(attribute, "Parent");
        var component = NamedType(attribute, "Component");
        var hasComponent = attribute.NamedArguments.Any(argument => argument.Key == "Component");
        var factories =
            component is null || module is null
                ? []
                : component
                    .GetMembers("Create")
                    .OfType<IMethodSymbol>()
                    .Where(method =>
                        method.IsStatic
                        && !method.IsGenericMethod
                        && method.Parameters.All(parameter =>
                            parameter.IsOptional || parameter.IsParams
                        )
                        && input.SemanticModel.Compilation.IsSymbolAccessibleWithin(method, module)
                    )
                    .ToArray();
        var componentValid =
            !hasComponent
            || (
                component is not null
                && component.TypeKind != TypeKind.Error
                && !component.IsUnboundGenericType
                && module is not null
                && input.SemanticModel.Compilation.IsSymbolAccessibleWithin(component, module)
                && factories.Length == 1
                && factories[0].ReturnType.ToDisplayString() == "Lucent.Core.ComponentRecipe"
            );
        var parameters = ImmutableArray.CreateBuilder<ParameterModel>();
        var valid =
            type.ContainingType is null
            && type.TypeKind == TypeKind.Struct
            && type.IsRecord
            && type.IsReadOnly
            && type.TypeParameters.Length == 0
            && syntax.ParameterList is not null;
        if (syntax.ParameterList is not null)
        {
            foreach (var parameterSyntax in syntax.ParameterList.Parameters)
            {
                var symbol = type
                    .InstanceConstructors.SelectMany(item => item.Parameters)
                    .FirstOrDefault(item => item.Name == parameterSyntax.Identifier.ValueText);
                if (symbol is null)
                {
                    valid = false;
                    continue;
                }
                parameters.Add(Parameter(symbol, parameterSyntax));
            }
        }
        var span = syntax.Identifier.GetLocation().GetMappedLineSpan();
        return new RouteModel(
            type,
            module,
            parent,
            component,
            componentValid,
            template ?? "",
            id,
            DefaultName(type.Name),
            parameters.ToImmutable(),
            syntax.Identifier.GetLocation(),
            new SourceModel(
                span.Path,
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1
            ),
            valid
        );
    }

    private static ParameterModel Parameter(IParameterSymbol symbol, ParameterSyntax syntax)
    {
        var kind = ParameterKind.Unsupported;
        ImmutableArray<string> names = ImmutableArray<string>.Empty;
        if (symbol.Type.SpecialType == SpecialType.System_String)
            kind = ParameterKind.Text;
        else if (symbol.Type.SpecialType == SpecialType.System_Int32)
            kind = ParameterKind.Int32;
        else if (symbol.Type.SpecialType == SpecialType.System_Int64)
            kind = ParameterKind.Int64;
        else if (symbol.Type.SpecialType == SpecialType.System_Boolean)
            kind = ParameterKind.Boolean;
        else if (symbol.Type.ToDisplayString() == "System.Guid")
            kind = ParameterKind.Guid;
        else if (symbol.Type.TypeKind == TypeKind.Enum)
        {
            var enumType = (INamedTypeSymbol)symbol.Type;
            var fields = enumType
                .GetMembers()
                .OfType<IFieldSymbol>()
                .Where(item => item.HasConstantValue)
                .ToArray();
            if (
                !enumType
                    .GetAttributes()
                    .Any(item => item.AttributeClass?.ToDisplayString() == "System.FlagsAttribute")
                && fields.Select(item => item.ConstantValue).Distinct().Count() == fields.Length
            )
            {
                kind = ParameterKind.Enum;
                names = fields.Select(item => item.Name).ToImmutableArray();
            }
        }
        var defaultExpression = DefaultExpression(symbol, kind);
        var defaultValueValid =
            !symbol.HasExplicitDefaultValue
            || defaultExpression is not null
                && (
                    kind is not (ParameterKind.Text or ParameterKind.Enum)
                    || IsValidQueryDefault(DefaultCanonicalText(symbol, kind)!)
                );
        return new ParameterModel(
            symbol.Name,
            symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            kind,
            names,
            symbol.HasExplicitDefaultValue,
            defaultExpression,
            defaultValueValid,
            syntax.Identifier.GetLocation()
        );
    }

    private static string? DefaultCanonicalText(IParameterSymbol symbol, ParameterKind kind)
    {
        if (!symbol.HasExplicitDefaultValue || symbol.ExplicitDefaultValue is null)
            return null;
        if (kind == ParameterKind.Text)
            return symbol.ExplicitDefaultValue as string;
        if (kind != ParameterKind.Enum)
            return null;
        var type = (INamedTypeSymbol)symbol.Type;
        return type.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(item =>
                item.HasConstantValue && Equals(item.ConstantValue, symbol.ExplicitDefaultValue)
            )
            ?.Name;
    }

    private static bool IsValidQueryDefault(string value)
    {
        if (!IsValidDecodedComponent(value))
            return false;
        try
        {
            return StrictUtf8.GetByteCount(value) <= MaximumQueryValueUtf8Bytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsValidDecodedComponent(string value)
    {
        for (var index = 0; index < value.Length; )
        {
            var current = value[index];
            if (char.IsHighSurrogate(current))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                    return false;
                index += 2;
                continue;
            }
            if (
                char.IsLowSurrogate(current)
                || current == '\\'
                || current == '\0'
                || char.IsControl(current)
            )
                return false;
            index++;
        }
        return true;
    }

    private static string? DefaultExpression(IParameterSymbol symbol, ParameterKind kind)
    {
        if (!symbol.HasExplicitDefaultValue || symbol.ExplicitDefaultValue is null)
            return null;
        if (kind == ParameterKind.Text)
            return SymbolDisplay.FormatLiteral((string)symbol.ExplicitDefaultValue, true);
        if (kind == ParameterKind.Int32)
            return ((int)symbol.ExplicitDefaultValue).ToString(CultureInfo.InvariantCulture);
        if (kind == ParameterKind.Int64)
            return ((long)symbol.ExplicitDefaultValue).ToString(CultureInfo.InvariantCulture) + "L";
        if (kind == ParameterKind.Boolean)
            return (bool)symbol.ExplicitDefaultValue ? "true" : "false";
        if (kind != ParameterKind.Enum)
            return null;
        var type = (INamedTypeSymbol)symbol.Type;
        var field = type.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(item =>
                item.HasConstantValue && Equals(item.ConstantValue, symbol.ExplicitDefaultValue)
            );
        return field is null
            ? null
            : type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                + "."
                + Escape(field.Name);
    }

    private static void Publish(
        SourceProductionContext output,
        ImmutableArray<ModuleModel> modules,
        ImmutableArray<RouteModel> routes,
        string? projectDirectory
    )
    {
        foreach (var module in modules.Where(item => !item.Valid))
            output.ReportDiagnostic(
                Diagnostic.Create(InvalidModule, module.Location, module.Type.Name)
            );
        foreach (var route in routes.Where(item => !item.Valid))
            output.ReportDiagnostic(
                Diagnostic.Create(InvalidRoute, route.Location, route.Type.Name)
            );
        foreach (var route in routes.Where(item => !item.ComponentValid))
            output.ReportDiagnostic(
                Diagnostic.Create(InvalidComponent, route.Location, route.Type.Name)
            );
        foreach (
            var parameter in routes
                .SelectMany(item => item.Parameters)
                .Where(item => item.Kind == ParameterKind.Unsupported)
        )
            output.ReportDiagnostic(
                Diagnostic.Create(UnsupportedParameter, parameter.Location, parameter.Name)
            );
        foreach (var module in modules.Where(item => item.Valid))
        {
            var owned = routes
                .Where(route =>
                    route.Valid && SymbolEqualityComparer.Default.Equals(route.Module, module.Type)
                )
                .ToArray();
            if (owned.Length == 0)
                continue;
            if (owned.Any(route => route.Component is not null))
                foreach (var route in owned.Where(route => route.Component is null))
                    output.ReportDiagnostic(
                        Diagnostic.Create(
                            MissingComponent,
                            route.Location,
                            route.Type.Name,
                            module.Type.Name
                        )
                    );
            var memberCollisions = MemberCollisions(module, owned).ToArray();
            foreach (var collision in memberCollisions)
                output.ReportDiagnostic(
                    Diagnostic.Create(
                        GeneratedMemberCollision,
                        collision.Location,
                        module.Type.Name,
                        collision.Name
                    )
                );
            var parsed = new Dictionary<RouteModel, TemplateModel>();
            foreach (var route in owned)
            {
                if (!TryTemplate(route, out var template, out var error))
                    output.ReportDiagnostic(
                        Diagnostic.Create(InvalidTemplate, route.Location, route.Type.Name, error)
                    );
                else
                    parsed.Add(route, template);
            }
            var duplicateGroups = owned
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .Concat(owned.GroupBy(item => item.FactoryName, StringComparer.Ordinal))
                .Where(group => group.Count() > 1);
            foreach (var group in duplicateGroups)
            foreach (var route in group)
                output.ReportDiagnostic(
                    Diagnostic.Create(
                        DuplicateIdentity,
                        route.Location,
                        group.Key,
                        module.Type.Name
                    )
                );
            ValidateParents(output, owned, parsed);
            var validRoutes = owned.Where(parsed.ContainsKey).ToArray();
            for (var first = 0; first < validRoutes.Length; first++)
            for (var second = first + 1; second < validRoutes.Length; second++)
                if (Ambiguous(parsed[validRoutes[first]], parsed[validRoutes[second]]))
                {
                    output.ReportDiagnostic(
                        Diagnostic.Create(
                            AmbiguousRoute,
                            validRoutes[first].Location,
                            validRoutes[first].Id,
                            validRoutes[second].Id
                        )
                    );
                    output.ReportDiagnostic(
                        Diagnostic.Create(
                            AmbiguousRoute,
                            validRoutes[second].Location,
                            validRoutes[first].Id,
                            validRoutes[second].Id
                        )
                    );
                }
            if (output.CancellationToken.IsCancellationRequested)
                return;
            if (
                memberCollisions.Length != 0
                || owned.Any(route =>
                    !parsed.ContainsKey(route)
                    || !route.ComponentValid
                    || (owned.Any(item => item.Component is not null) && route.Component is null)
                    || route.Parameters.Any(parameter =>
                        parameter.Kind == ParameterKind.Unsupported
                    )
                )
            )
                continue;
            PublishModule(
                output,
                module,
                owned.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
                parsed,
                projectDirectory
            );
        }
        foreach (
            var route in routes.Where(route =>
                route.Module is null
                || !modules.Any(module =>
                    SymbolEqualityComparer.Default.Equals(module.Type, route.Module)
                )
            )
        )
            output.ReportDiagnostic(
                Diagnostic.Create(
                    InvalidTemplate,
                    route.Location,
                    route.Type.Name,
                    "the owning module is missing or invalid"
                )
            );
    }

    private static IEnumerable<(string Name, Location Location)> MemberCollisions(
        ModuleModel module,
        RouteModel[] routes
    )
    {
        var generated = new List<(string Name, Location Location)> { ("Module", module.Location) };
        foreach (var route in routes)
        {
            generated.Add((route.FactoryName, route.Location));
            generated.Add((route.FactoryName + "Definition", route.Location));
            generated.Add(("Create" + route.FactoryName + "Definition", route.Location));
        }
        if (routes.Any(route => route.Component is not null))
            generated.Add(("CreateComponent", module.Location));
        if (routes.All(route => route.Component is not null))
        {
            generated.Add(("Bundle", module.Location));
            generated.Add(("CreateDestination", module.Location));
        }

        foreach (var group in generated.GroupBy(item => item.Name, StringComparer.Ordinal))
        {
            var entries = group.ToArray();
            if (entries.Length > 1)
                foreach (var entry in entries)
                    yield return entry;
        }
        var generatedNames = new HashSet<string>(
            generated.Select(item => item.Name),
            StringComparer.Ordinal
        );
        foreach (
            var member in module
                .Type.GetMembers()
                .Where(member => generatedNames.Contains(member.Name))
        )
            yield return (member.Name, member.Locations.FirstOrDefault() ?? module.Location);
    }

    private static bool TryTemplate(RouteModel route, out TemplateModel model, out string error)
    {
        model = null!;
        error = "";
        var text = route.Template;
        if (
            !text.StartsWith("/", StringComparison.Ordinal)
            || text.IndexOf('#') >= 0
            || text == ""
            || (text.Length > 1 && text.EndsWith("/", StringComparison.Ordinal))
        )
        {
            error = "expected an absolute non-trailing-slash path";
            return false;
        }
        var parts = text.Split(QuerySeparator, 2);
        if (parts.Length == 2 && parts[1].Length == 0)
        {
            error = "a bare query marker is not allowed";
            return false;
        }
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var segments = ImmutableArray.CreateBuilder<SegmentModel>();
        foreach (var raw in parts[0].Split('/').Skip(1))
        {
            if (raw.Length == 0)
            {
                if (parts[0] == "/")
                    continue;
                error = "empty path segments are not allowed";
                return false;
            }
            if (Placeholder(raw, out var name))
            {
                var parameter = route.Parameters.FirstOrDefault(item =>
                    String.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)
                );
                if (parameter is null || parameter.HasDefault)
                {
                    error = "path placeholders must name required constructor parameters";
                    return false;
                }
                used.Add(parameter.Name);
                segments.Add(new SegmentModel(null, parameter));
            }
            else
            {
                if (
                    raw.IndexOf('{') >= 0
                    || raw.IndexOf('}') >= 0
                    || raw.IndexOf('%') >= 0
                    || raw == "."
                    || raw == ".."
                    || !ValidDecoded(raw)
                )
                {
                    error = "invalid literal path segment";
                    return false;
                }
                segments.Add(new SegmentModel(raw, null));
            }
        }
        var query = ImmutableArray.CreateBuilder<QueryModel>();
        if (parts.Length == 2)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in parts[1].Split('&'))
            {
                var equals = pair.IndexOf('=');
                var key = equals > 0 ? pair.Substring(0, equals) : "";
                if (
                    equals <= 0
                    || !Placeholder(pair.Substring(equals + 1), out var name)
                    || !keys.Add(key)
                    || key.IndexOf('%') >= 0
                    || !ValidDecoded(key)
                )
                {
                    error = "query entries must be distinct key={parameter} pairs";
                    return false;
                }
                var parameter = route.Parameters.FirstOrDefault(item =>
                    String.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)
                );
                if (parameter is null)
                {
                    error = "query placeholders must name constructor parameters";
                    return false;
                }
                if (
                    parameter.HasDefault
                    && (!parameter.DefaultValueValid || parameter.DefaultExpression is null)
                )
                {
                    error =
                        "query defaults must be non-null constants accepted by the route location grammar and limits";
                    return false;
                }
                used.Add(parameter.Name);
                query.Add(new QueryModel(key, parameter));
            }
        }
        if (used.Count != route.Parameters.Length)
        {
            error = "every constructor parameter must occur exactly once";
            return false;
        }
        if (
            segments.Count + query.Count
            != used.Count + segments.Count(item => item.Literal is not null)
        )
        {
            error = "a constructor parameter occurs more than once";
            return false;
        }
        var slots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var segment in segments)
            if (segment.Parameter is not null)
                slots.Add(segment.Parameter.Name, index++);
        foreach (var item in query)
            slots.Add(item.Parameter.Name, index++);
        model = new TemplateModel(segments.ToImmutable(), query.ToImmutable(), slots);
        return true;
    }

    private static void ValidateParents(
        SourceProductionContext output,
        RouteModel[] routes,
        Dictionary<RouteModel, TemplateModel> parsed
    )
    {
        foreach (
            var route in routes.Where(item => item.Parent is not null && parsed.ContainsKey(item))
        )
        {
            var parent = routes.FirstOrDefault(item =>
                SymbolEqualityComparer.Default.Equals(item.Type, route.Parent)
            );
            string? error = null;
            if (parent is null || !parsed.ContainsKey(parent))
                error = "the parent is not a valid route in the same module";
            else if (
                ReferenceEquals(parent, route)
                || Ancestors(parent, routes).Any(item => ReferenceEquals(item, route))
            )
                error = "the parent chain contains a cycle";
            else if (!Prefix(parsed[parent], parsed[route]))
                error =
                    "the parent path/query and parameter shapes must be a strict inherited prefix";
            if (error is not null)
                output.ReportDiagnostic(
                    Diagnostic.Create(InvalidParent, route.Location, route.Type.Name, error)
                );
        }
    }

    private static IEnumerable<RouteModel> Ancestors(RouteModel route, RouteModel[] routes)
    {
        var seen = new HashSet<RouteModel>();
        while (route.Parent is not null)
        {
            var parent = routes.FirstOrDefault(item =>
                SymbolEqualityComparer.Default.Equals(item.Type, route.Parent)
            );
            if (parent is null || !seen.Add(parent))
                yield break;
            yield return parent;
            route = parent;
        }
    }

    private static bool Prefix(TemplateModel parent, TemplateModel child)
    {
        if (
            parent.Segments.Length >= child.Segments.Length
            || parent.Query.Length > child.Query.Length
        )
            return false;
        for (var i = 0; i < parent.Segments.Length; i++)
            if (!Same(parent.Segments[i], child.Segments[i]))
                return false;
        foreach (var query in parent.Query)
            if (
                !child.Query.Any(item =>
                    item.Key == query.Key && Same(item.Parameter, query.Parameter)
                )
            )
                return false;
        return true;
    }

    private static bool Same(SegmentModel left, SegmentModel right) =>
        left.Literal == right.Literal
        && (
            left.Parameter is null
                ? right.Parameter is null
                : right.Parameter is not null && Same(left.Parameter, right.Parameter)
        );

    private static bool Same(ParameterModel left, ParameterModel right) =>
        left.Name.Equals(right.Name, StringComparison.OrdinalIgnoreCase)
        && left.Type == right.Type
        && left.DefaultExpression == right.DefaultExpression;

    private static bool Ambiguous(TemplateModel left, TemplateModel right)
    {
        if (left.Segments.Length != right.Segments.Length)
            return false;
        var differentPrecedence = false;
        for (var i = 0; i < left.Segments.Length; i++)
        {
            var a = left.Segments[i];
            var b = right.Segments[i];
            if (a.Literal is not null && b.Literal is not null)
            {
                if (a.Literal != b.Literal)
                    return false;
                continue;
            }
            if ((a.Literal is null) != (b.Literal is null))
            {
                differentPrecedence = true;
                continue;
            }
            var leftParameter = a.Parameter!;
            var rightParameter = b.Parameter!;
            if (
                leftParameter.Kind == ParameterKind.Text
                || rightParameter.Kind == ParameterKind.Text
            )
            {
                if (leftParameter.Kind != rightParameter.Kind)
                    differentPrecedence = true;
                continue;
            }
            if (!Overlaps(leftParameter, rightParameter))
                return false;
        }
        return !differentPrecedence;
    }

    private static bool Overlaps(ParameterModel a, ParameterModel b)
    {
        if (a.Kind == b.Kind)
            return a.Kind != ParameterKind.Enum
                || a.EnumNames.Intersect(b.EnumNames, StringComparer.Ordinal).Any();
        if (
            (a.Kind == ParameterKind.Int32 && b.Kind == ParameterKind.Int64)
            || (a.Kind == ParameterKind.Int64 && b.Kind == ParameterKind.Int32)
        )
            return true;
        if (a.Kind == ParameterKind.Boolean && b.Kind == ParameterKind.Enum)
            return b.EnumNames.Contains("true") || b.EnumNames.Contains("false");
        if (b.Kind == ParameterKind.Boolean && a.Kind == ParameterKind.Enum)
            return a.EnumNames.Contains("true") || a.EnumNames.Contains("false");
        return false;
    }

    private static void PublishModule(
        SourceProductionContext output,
        ModuleModel module,
        RouteModel[] routes,
        Dictionary<RouteModel, TemplateModel> templates,
        string? projectDirectory
    )
    {
        var source = new StringBuilder("// <auto-generated />\n#nullable enable\n");
        if (!module.Type.ContainingNamespace.IsGlobalNamespace)
            source.Append("namespace ").Append(module.Type.ContainingNamespace).AppendLine(";");
        source
            .Append(Accessibility(module.Type.DeclaredAccessibility))
            .Append(" static partial class ")
            .Append(Escape(module.Type.Name))
            .AppendLine("\n{");
        foreach (var route in routes)
            EmitDefinition(source, route, routes, templates, projectDirectory);
        source
            .Append(
                "    public static global::Lucent.Core.RouteModuleDescriptor Module { get; } = new("
            )
            .Append(Literal(module.Type.ToDisplayString()))
            .Append(
                ", global::Lucent.Core.RouteFallbackPolicy.Reject, new global::Lucent.Core.RouteDeclarationSource("
            )
            .Append(Literal(SafePath(module.Source.Path, projectDirectory)))
            .Append(", ")
            .Append(module.Source.Line)
            .Append(", ")
            .Append(module.Source.Column)
            .AppendLine("),");
        source.AppendLine("        new global::Lucent.Core.RouteDefinitionDescriptor[]");
        source.AppendLine("        {");
        foreach (var route in routes)
            source.Append("            ").Append(DefinitionProperty(route)).AppendLine(",");
        source.AppendLine("        });");
        if (routes.Any(route => route.Component is not null))
        {
            source.AppendLine(
                "    public static global::Lucent.Core.ComponentRecipe CreateComponent(global::Lucent.Core.RouteLevelDescriptor level)"
            );
            source.AppendLine("    {");
            foreach (var route in routes)
            {
                var chain = Ancestors(route, routes).Reverse().Concat(new[] { route }).ToArray();
                for (var index = 0; index < chain.Length; index++)
                {
                    if (chain[index].Component is not { } component)
                        continue;
                    source
                        .Append("        if (global::System.Object.ReferenceEquals(level, ")
                        .Append(DefinitionProperty(route))
                        .Append(".Branch[")
                        .Append(index)
                        .AppendLine("]))")
                        .Append("            return ")
                        .Append(component.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                        .AppendLine(".Create();");
                }
            }
            source.AppendLine(
                "        throw new global::System.ArgumentException(\"The route level has no component mapping in this module.\", nameof(level));"
            );
            source.AppendLine("    }");
        }
        if (routes.All(route => route.Component is not null))
        {
            source.AppendLine(
                "    public static global::Lucent.Core.RouteBundle Bundle { get; } = global::Lucent.Core.RouteBundle.Create(new global::Lucent.Core.RouteModuleDescriptor[] { Module }, CreateDestination);"
            );
            source.AppendLine(
                "    private static global::Lucent.Core.RouteDestination CreateDestination(global::Lucent.Core.RouteLevelDescriptor level)"
            );
            source.AppendLine("    {");
            foreach (var route in routes)
            {
                var chain = Ancestors(route, routes).Reverse().Concat(new[] { route }).ToArray();
                for (var index = 0; index < chain.Length; index++)
                {
                    var component = chain[index].Component!;
                    source
                        .Append("        if (global::System.Object.ReferenceEquals(level, ")
                        .Append(DefinitionProperty(route))
                        .Append(".Branch[")
                        .Append(index)
                        .AppendLine("]))")
                        .Append(
                            "            return new global::Lucent.Core.RouteDestination(typeof("
                        )
                        .Append(component.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                        .Append("), ")
                        .Append(component.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                        .AppendLine(".Create());");
                }
            }
            source.AppendLine(
                "        throw new global::System.ArgumentException(\"The route level has no component mapping in this module.\", nameof(level));"
            );
            source.AppendLine("    }");
        }
        source.AppendLine("}");
        output.AddSource(
            "Lucent.Routes." + Hash(module.Type.ToDisplayString()) + ".g.cs",
            source.ToString()
        );
    }

    private static void EmitDefinition(
        StringBuilder source,
        RouteModel route,
        RouteModel[] routes,
        Dictionary<RouteModel, TemplateModel> templates,
        string? projectDirectory
    )
    {
        var template = templates[route];
        source
            .Append("    public static global::Lucent.Core.RouteDefinitionDescriptor ")
            .Append(DefinitionProperty(route))
            .Append(" { get; } = Create")
            .Append(route.FactoryName)
            .AppendLine("Definition();");
        source
            .Append("    public static global::Lucent.Core.RouteReference ")
            .Append(Escape(route.FactoryName))
            .Append('(')
            .Append(String.Join(", ", route.Parameters.Select(ParameterDeclaration)))
            .AppendLine(")");
        source
            .Append("        => global::Lucent.Core.RouteReference.Create(")
            .Append(DefinitionProperty(route))
            .Append(".Pattern, new global::Lucent.Core.RouteValue[] { ")
            .Append(
                String.Join(
                    ", ",
                    route
                        .Parameters.OrderBy(item => template.Slots[item.Name])
                        .Select(ValueExpression)
                )
            )
            .AppendLine(" });");
        source
            .Append("    private static global::Lucent.Core.RouteDefinitionDescriptor Create")
            .Append(route.FactoryName)
            .AppendLine("Definition()");
        source.AppendLine("    {");
        source
            .Append(
                "        var pattern = global::Lucent.Core.RoutePattern.Create(new global::Lucent.Core.RouteDefinitionId("
            )
            .Append(Literal(route.Id))
            .AppendLine("),");
        source.AppendLine("            new global::Lucent.Core.RouteSegmentPattern[]");
        source.AppendLine("            {");
        foreach (var segment in template.Segments)
            source
                .Append("                ")
                .Append(
                    segment.Literal is not null
                        ? "global::Lucent.Core.RouteSegmentPattern.LiteralSegment("
                            + Literal(segment.Literal)
                            + ")"
                        : "global::Lucent.Core.RouteSegmentPattern.Parameter("
                            + Literal(segment.Parameter!.Name)
                            + ", "
                            + template
                                .Slots[segment.Parameter.Name]
                                .ToString(CultureInfo.InvariantCulture)
                            + ", "
                            + Shape(segment.Parameter)
                            + ")"
                )
                .AppendLine(",");
        source.AppendLine("            },");
        source.AppendLine("            new global::Lucent.Core.RouteQueryPattern[]");
        source.AppendLine("            {");
        foreach (var query in template.Query)
            source
                .Append("                global::Lucent.Core.RouteQueryPattern.")
                .Append(query.Parameter.HasDefault ? "Optional" : "Required")
                .Append('(')
                .Append(Literal(query.Key))
                .Append(", ")
                .Append(Literal(query.Parameter.Name))
                .Append(", ")
                .Append(template.Slots[query.Parameter.Name].ToString(CultureInfo.InvariantCulture))
                .Append(", ")
                .Append(Shape(query.Parameter))
                .Append(
                    query.Parameter.HasDefault ? ", " + DefaultValueExpression(query.Parameter) : ""
                )
                .AppendLine("),");
        source.AppendLine("            });");
        source.AppendLine(
            "        return new global::Lucent.Core.RouteDefinitionDescriptor(pattern, new global::Lucent.Core.RouteLevelDescriptor[]"
        );
        source.AppendLine("        {");
        var chain = Ancestors(route, routes).Reverse().Concat(new[] { route }).ToArray();
        RouteModel? previous = null;
        foreach (var level in chain)
        {
            var previousNames = previous is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(
                    previous.Parameters.Select(item => item.Name),
                    StringComparer.OrdinalIgnoreCase
                );
            var owned = level
                .Parameters.Where(item => !previousNames.Contains(item.Name))
                .Select(item => template.Slots[item.Name]);
            source
                .Append(
                    "            new global::Lucent.Core.RouteLevelDescriptor(new global::Lucent.Core.RouteDefinitionId("
                )
                .Append(Literal(level.Id))
                .Append("), new int[] { ")
                .Append(String.Join(", ", owned))
                .Append(" }, new global::Lucent.Core.RouteDeclarationSource(")
                .Append(Literal(SafePath(level.Source.Path, projectDirectory)))
                .Append(", ")
                .Append(level.Source.Line)
                .Append(", ")
                .Append(level.Source.Column)
                .Append(
                    "), static (definition, match, live) => new global::Lucent.Core.RouteContext<"
                )
                .Append(level.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append(">(definition, new ")
                .Append(level.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append('(')
                .Append(
                    String.Join(
                        ", ",
                        level.Parameters.Select(item =>
                            ReadExpression(item, template.Slots[item.Name])
                        )
                    )
                )
                .Append(
                    "), live), static (context, content) => global::Lucent.Core.Context.Provide((global::Lucent.Core.RouteContext<"
                )
                .Append(level.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .AppendLine(">)context, content)),");
            previous = level;
        }
        source.AppendLine("        });");
        source.AppendLine("    }");
    }

    private static string ParameterDeclaration(ParameterModel item) =>
        item.Type
        + " "
        + Escape(Camel(item.Name))
        + (item.HasDefault ? " = " + item.DefaultExpression : "");

    private static string ValueExpression(ParameterModel item) =>
        ValueExpression(item, Escape(Camel(item.Name)));

    private static string ValueExpression(ParameterModel item, string argument) =>
        item.Kind switch
        {
            ParameterKind.Text => "global::Lucent.Core.RouteValue.FromText(" + argument + ")",
            ParameterKind.Int32 => "global::Lucent.Core.RouteValue.FromSigned32(" + argument + ")",
            ParameterKind.Int64 => "global::Lucent.Core.RouteValue.FromSigned64(" + argument + ")",
            ParameterKind.Guid => "global::Lucent.Core.RouteValue.FromUuid(" + argument + ")",
            ParameterKind.Boolean => "global::Lucent.Core.RouteValue.FromBoolean(" + argument + ")",
            ParameterKind.Enum => "global::Lucent.Core.RouteValue.FromEnumName("
                + EnumToName(item, argument)
                + ")",
            _ => "default",
        };

    private static string DefaultValueExpression(ParameterModel item) =>
        ValueExpression(item, item.DefaultExpression!);

    private static string EnumToName(ParameterModel item, string expression) =>
        expression
        + " switch { "
        + String.Join(
            ", ",
            item.EnumNames.Select(name => item.Type + "." + Escape(name) + " => " + Literal(name))
        )
        + ", _ => throw new global::System.ArgumentOutOfRangeException() }";

    private static string ReadExpression(ParameterModel item, int slot)
    {
        var read = "match.GetValue(" + slot.ToString(CultureInfo.InvariantCulture) + ")";
        return item.Kind switch
        {
            ParameterKind.Text => read + ".Text",
            ParameterKind.Int32 => read + ".Signed32",
            ParameterKind.Int64 => read + ".Signed64",
            ParameterKind.Guid => read + ".Uuid",
            ParameterKind.Boolean => read + ".Boolean",
            ParameterKind.Enum => read
                + ".EnumName switch { "
                + String.Join(
                    ", ",
                    item.EnumNames.Select(name =>
                        Literal(name) + " => " + item.Type + "." + Escape(name)
                    )
                )
                + ", _ => throw new global::System.InvalidOperationException(\"Generated enum capture was invalid.\") }",
            _ => "default",
        };
    }

    private static string Shape(ParameterModel item) =>
        item.Kind switch
        {
            ParameterKind.Text => "global::Lucent.Core.RouteValueShape.Text",
            ParameterKind.Int32 => "global::Lucent.Core.RouteValueShape.Signed32",
            ParameterKind.Int64 => "global::Lucent.Core.RouteValueShape.Signed64",
            ParameterKind.Guid => "global::Lucent.Core.RouteValueShape.Uuid",
            ParameterKind.Boolean => "global::Lucent.Core.RouteValueShape.Boolean",
            ParameterKind.Enum => "global::Lucent.Core.RouteValueShape.Enum("
                + String.Join(", ", item.EnumNames.Select(Literal))
                + ")",
            _ => "throw null!",
        };

    private static bool Placeholder(string value, out string name)
    {
        if (value.Length > 2 && value[0] == '{' && value[value.Length - 1] == '}')
        {
            name = value.Substring(1, value.Length - 2);
            return name.Length > 0;
        }
        name = "";
        return false;
    }

    private static string? NamedString(AttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(item => item.Key == name).Value.Value as string;

    private static INamedTypeSymbol? NamedType(AttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(item => item.Key == name).Value.Value
        as INamedTypeSymbol;

    private static bool ValidDecoded(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\\' || char.IsControl(character))
                return false;
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                    return false;
                index++;
            }
            else if (char.IsLowSurrogate(character))
                return false;
        }
        return true;
    }

    private static string SafePath(string path, string? projectDirectory)
    {
        path = path.Replace('\\', '/');
        if (String.IsNullOrWhiteSpace(path))
            return "Routes.cs";
        if (!String.IsNullOrWhiteSpace(projectDirectory))
        {
            var root = projectDirectory!.Replace('\\', '/').TrimEnd('/') + "/";
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return path.Substring(root.Length);
        }
        return Path.IsPathRooted(path) || path.IndexOf(':') >= 0
            ? Path.GetFileName(path)
            : path.TrimStart('/');
    }

    private static string DefaultName(string name) =>
        name.EndsWith("Route", StringComparison.Ordinal) && name.Length > 5
            ? name.Substring(0, name.Length - 5)
            : name;

    private static string DefinitionProperty(RouteModel route) =>
        Escape(route.FactoryName) + "Definition";

    private static string Camel(string value) =>
        value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value.Substring(1);

    private static string Escape(string value) =>
        SyntaxFacts.GetKeywordKind(value) == SyntaxKind.None ? value : "@" + value;

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, true);

    private static string Accessibility(Accessibility value) =>
        value == Microsoft.CodeAnalysis.Accessibility.Public ? "public" : "internal";

    private static string Hash(string value)
    {
        uint hash = 2166136261;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }

    private static DiagnosticDescriptor Error(string id, string title, string message) =>
        new DiagnosticDescriptor(
            id,
            title,
            message,
            "Lucent.Routing",
            DiagnosticSeverity.Error,
            true
        );

    private enum ParameterKind
    {
        Unsupported,
        Text,
        Int32,
        Int64,
        Guid,
        Boolean,
        Enum,
    }

    private sealed class ModuleModel
    {
        internal ModuleModel(
            INamedTypeSymbol type,
            Location location,
            SourceModel source,
            bool valid
        )
        {
            Type = type;
            Location = location;
            Source = source;
            Valid = valid;
        }

        internal INamedTypeSymbol Type { get; }
        internal Location Location { get; }
        internal SourceModel Source { get; }
        internal bool Valid { get; }
    }

    private sealed class RouteModel
    {
        internal RouteModel(
            INamedTypeSymbol type,
            INamedTypeSymbol? module,
            INamedTypeSymbol? parent,
            INamedTypeSymbol? component,
            bool componentValid,
            string template,
            string id,
            string factoryName,
            ImmutableArray<ParameterModel> parameters,
            Location location,
            SourceModel source,
            bool valid
        )
        {
            Type = type;
            Module = module;
            Parent = parent;
            Component = component;
            ComponentValid = componentValid;
            Template = template;
            Id = id;
            FactoryName = factoryName;
            Parameters = parameters;
            Location = location;
            Source = source;
            Valid = valid;
        }

        internal INamedTypeSymbol Type { get; }
        internal INamedTypeSymbol? Module { get; }
        internal INamedTypeSymbol? Parent { get; }
        internal INamedTypeSymbol? Component { get; }
        internal bool ComponentValid { get; }
        internal string Template { get; }
        internal string Id { get; }
        internal string FactoryName { get; }
        internal ImmutableArray<ParameterModel> Parameters { get; }
        internal Location Location { get; }
        internal SourceModel Source { get; }
        internal bool Valid { get; }
    }

    private sealed class ParameterModel
    {
        internal ParameterModel(
            string name,
            string type,
            ParameterKind kind,
            ImmutableArray<string> enumNames,
            bool hasDefault,
            string? defaultExpression,
            bool defaultValueValid,
            Location location
        )
        {
            Name = name;
            Type = type;
            Kind = kind;
            EnumNames = enumNames;
            HasDefault = hasDefault;
            DefaultExpression = defaultExpression;
            DefaultValueValid = defaultValueValid;
            Location = location;
        }

        internal string Name { get; }
        internal string Type { get; }
        internal ParameterKind Kind { get; }
        internal ImmutableArray<string> EnumNames { get; }
        internal bool HasDefault { get; }
        internal string? DefaultExpression { get; }
        internal bool DefaultValueValid { get; }
        internal Location Location { get; }
    }

    private sealed class SegmentModel
    {
        internal SegmentModel(string? literal, ParameterModel? parameter)
        {
            Literal = literal;
            Parameter = parameter;
        }

        internal string? Literal { get; }
        internal ParameterModel? Parameter { get; }
    }

    private sealed class QueryModel
    {
        internal QueryModel(string key, ParameterModel parameter)
        {
            Key = key;
            Parameter = parameter;
        }

        internal string Key { get; }
        internal ParameterModel Parameter { get; }
    }

    private sealed class TemplateModel
    {
        internal TemplateModel(
            ImmutableArray<SegmentModel> segments,
            ImmutableArray<QueryModel> query,
            Dictionary<string, int> slots
        )
        {
            Segments = segments;
            Query = query;
            Slots = slots;
        }

        internal ImmutableArray<SegmentModel> Segments { get; }
        internal ImmutableArray<QueryModel> Query { get; }
        internal Dictionary<string, int> Slots { get; }
    }

    private sealed class SourceModel
    {
        internal SourceModel(string path, int line, int column)
        {
            Path = path;
            Line = line;
            Column = column;
        }

        internal string Path { get; }
        internal int Line { get; }
        internal int Column { get; }
    }
}
