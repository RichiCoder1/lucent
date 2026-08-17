using System.Xml.Linq;
using Lucent.Compiler.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lucent.Compiler.Semantics;

internal sealed class NativeSymbolResolver
{
    private static readonly IReadOnlyDictionary<string, string> LegacyControlAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Column"] = "Avalonia.Controls.StackPanel",
            ["Text"] = "Avalonia.Controls.TextBlock",
        };

    private static readonly SymbolDisplayFormat FullyQualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private readonly CSharpCompilation _compilation;
    private readonly INamedTypeSymbol? _controlType;
    private readonly INamedTypeSymbol? _stringType;
    private readonly IReadOnlyList<string> _imports;
    private readonly Dictionary<string, ITypeSymbol?> _resolvedTypes = new(StringComparer.Ordinal);

    public NativeSymbolResolver(
        ProjectSemanticCompilation project)
    {
        _compilation = project.Compilation;
        _controlType = _compilation.GetTypeByMetadataName("Avalonia.Controls.Control");
        _stringType = _compilation.GetSpecialType(SpecialType.System_String);
        _imports = project.Imports;
    }

    public NativeSymbolResolver(
        string componentNamespace,
        IReadOnlyList<string> usingDirectives,
        LucentProjectContext? projectContext)
        : this(new ProjectSemanticCompilation(componentNamespace, usingDirectives, projectContext))
    {
    }

    public ResolvedNativeControl? ResolveControl(string sourceName)
    {
        var metadataNames = new List<string>();
        if (LegacyControlAliases.TryGetValue(sourceName, out var legacyMetadataName))
        {
            metadataNames.Add(legacyMetadataName);
        }
        else if (sourceName.Contains('.', StringComparison.Ordinal))
        {
            metadataNames.Add(sourceName);
        }
        else
        {
            metadataNames.AddRange(_imports.Select(@namespace => $"{@namespace}.{sourceName}"));
        }

        var resolvedCandidates = metadataNames
            .Select(_compilation.GetTypeByMetadataName)
            .Where(symbol => symbol is not null)
            .Cast<INamedTypeSymbol>()
            .Where(IsControl)
            .ToArray();
        var candidates = new List<INamedTypeSymbol>();
        foreach (var candidate in resolvedCandidates)
        {
            if (!candidates.Any(existing =>
                    SymbolEqualityComparer.Default.Equals(existing, candidate)))
            {
                candidates.Add(candidate);
            }
        }

        if (candidates.Count != 1)
        {
            return null;
        }

        var symbol = candidates[0];
        return new ResolvedNativeControl(
            sourceName,
            symbol,
            symbol.ToDisplayString(FullyQualifiedFormat),
            ResolveContentRoute(symbol));
    }

    public ResolvedNativeProperty? ResolveProperty(
        ResolvedNativeControl control,
        string sourceName)
    {
        var memberName = CanonicalPropertyName(control.SourceName, sourceName);
        var properties = GetNearestMembers<IPropertySymbol>(control.Symbol, memberName)
            .Where(property => !property.IsStatic && property.DeclaredAccessibility == Accessibility.Public)
            .ToArray();
        if (properties.Length != 1)
        {
            return null;
        }

        var property = properties[0];
        return new ResolvedNativeProperty(
            sourceName,
            property.Name,
            property,
            property.Type.ToDisplayString(FullyQualifiedFormat),
            GetNativeValueKind(property.Type));
    }

    public bool RequiresStringConstructor(ITypeSymbol targetType) =>
        !_compilation.ClassifyConversion(_stringType!, targetType).IsImplicit &&
        targetType is INamedTypeSymbol { IsAbstract: false } named &&
        named.InstanceConstructors.Count(constructor =>
            IsAccessible(constructor) &&
            constructor.Parameters is [{ Type.SpecialType: SpecialType.System_String }]) == 1;

    public ResolvedNativeEvent? ResolveEvent(
        ResolvedNativeControl control,
        string sourceName)
    {
        var memberName = sourceName.StartsWith("on", StringComparison.Ordinal) &&
            sourceName.Length > 2
                ? sourceName[2..]
                : sourceName;
        var events = GetNearestMembers<IEventSymbol>(control.Symbol, memberName)
            .Where(@event => !@event.IsStatic && @event.DeclaredAccessibility == Accessibility.Public)
            .ToArray();
        if (events.Length != 1 ||
            events[0].Type is not INamedTypeSymbol delegateType ||
            delegateType.DelegateInvokeMethod is not { ReturnsVoid: true } invoke ||
            invoke.Parameters.Length != 2)
        {
            return null;
        }

        return new ResolvedNativeEvent(
            sourceName,
            events[0].Name,
            events[0],
            events[0].Type.ToDisplayString(FullyQualifiedFormat),
            invoke.Parameters[0].Type.ToDisplayString(FullyQualifiedFormat),
            invoke.Parameters[1].Type.ToDisplayString(FullyQualifiedFormat));
    }

    public IReadOnlyList<ResolvedNativeProperty> GetProperties(
        ResolvedNativeControl control) =>
        EnumerateTypeHierarchy(control.Symbol)
            .SelectMany(type => type.GetMembers().OfType<IPropertySymbol>())
            .Where(property =>
                !property.IsStatic &&
                property.DeclaredAccessibility == Accessibility.Public &&
                property.SetMethod is { DeclaredAccessibility: Accessibility.Public })
            .GroupBy(property => property.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(property => new ResolvedNativeProperty(
                property.Name,
                property.Name,
                property,
                property.Type.ToDisplayString(FullyQualifiedFormat),
                GetNativeValueKind(property.Type)))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<ResolvedNativeEvent> GetEvents(
        ResolvedNativeControl control) =>
        EnumerateTypeHierarchy(control.Symbol)
            .SelectMany(type => type.GetMembers().OfType<IEventSymbol>())
            .Where(@event =>
                !@event.IsStatic &&
                @event.DeclaredAccessibility == Accessibility.Public)
            .Select(@event => ResolveEvent(control, @event.Name))
            .Where(@event => @event is not null)
            .Cast<ResolvedNativeEvent>()
            .GroupBy(@event => @event.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(@event => @event.Name, StringComparer.Ordinal)
            .ToArray();

    public ITypeSymbol? ResolveTypeName(string typeName)
    {
        if (_resolvedTypes.TryGetValue(typeName, out var cached))
        {
            return cached;
        }

        var source = string.Join(
            Environment.NewLine,
            _imports.Select(@namespace => $"using {@namespace};")) +
            $"{Environment.NewLine}internal sealed class __LucentTypeProbe {{ public {typeName} Value = default!; }}";
        var tree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var compilation = _compilation.AddSyntaxTrees(tree);
        var field = tree.GetRoot()
            .DescendantNodes()
            .OfType<FieldDeclarationSyntax>()
            .FirstOrDefault();
        if (field is null)
        {
            return null;
        }
        var type = compilation.GetSemanticModel(tree)
            .GetTypeInfo(field.Declaration.Type)
            .Type;
        var resolved = type?.TypeKind == TypeKind.Error ? null : type;
        _resolvedTypes[typeName] = resolved;
        return resolved;
    }

    public ResolvedNativeAttachedProperty? ResolveAttachedProperty(
        ResolvedNativeControl control,
        string sourceName)
    {
        var separator = sourceName.LastIndexOf('.');
        if (separator <= 0 || separator == sourceName.Length - 1)
        {
            return null;
        }

        var ownerName = sourceName[..separator];
        var memberName = sourceName[(separator + 1)..];
        var owner = ResolveTypeName(ownerName) as INamedTypeSymbol;
        if (owner is null)
        {
            return null;
        }

        var setters = owner.GetMembers("Set" + memberName)
            .OfType<IMethodSymbol>()
            .Where(method => method is { IsStatic: true, IsGenericMethod: false,
                DeclaredAccessibility: Accessibility.Public, ReturnsVoid: true } &&
                method.Parameters.Length == 2 &&
                _compilation.ClassifyConversion(control.Symbol, method.Parameters[0].Type).IsImplicit)
            .ToArray();
        var fields = owner.GetMembers(memberName + "Property")
            .Where(symbol => symbol is IFieldSymbol
                { IsStatic: true, DeclaredAccessibility: Accessibility.Public } field &&
                IsAvaloniaProperty(field.Type))
            .ToArray();
        if (setters.Length != 1 || fields.Length != 1)
        {
            return null;
        }

        var setter = setters[0];
        return new ResolvedNativeAttachedProperty(
            sourceName,
            owner.ToDisplayString(FullyQualifiedFormat),
            setter.Name,
            setter,
            fields[0],
            setter.Parameters[1].Type,
            setter.Parameters[1].Type.ToDisplayString(FullyQualifiedFormat));
    }

    public ResolvedNativeCollection? ResolveMountCollection(
        ResolvedNativeControl control,
        string sourceName)
    {
        var property = ResolveProperty(control, sourceName);
        if (property?.Symbol is not { GetMethod.DeclaredAccessibility: Accessibility.Public } propertySymbol ||
            propertySymbol.SetMethod?.DeclaredAccessibility == Accessibility.Public)
        {
            return null;
        }

        if (propertySymbol.Type is not INamedTypeSymbol collectionType)
        {
            return null;
        }

        var addMethods = EnumerateTypeHierarchy(collectionType)
            .SelectMany(type => type.GetMembers("Add"))
            .OfType<IMethodSymbol>()
            .Where(method => method is { IsStatic: false, IsGenericMethod: false,
                DeclaredAccessibility: Accessibility.Public } && method.Parameters.Length == 1)
            .ToArray();
        return addMethods.Length == 1
            ? new ResolvedNativeCollection(
                property,
                addMethods[0],
                addMethods[0].Parameters[0].Type,
                addMethods[0].Parameters[0].Type.ToDisplayString(FullyQualifiedFormat))
            : null;
    }

    public IReadOnlyList<NativeAttachedCandidate> GetAttachedPropertyCandidates(
        string ownerName,
        ResolvedNativeControl control)
    {
        var owner = ResolveTypeName(ownerName) as INamedTypeSymbol;
        if (owner is null)
        {
            return [];
        }

        return owner.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => method is { IsStatic: true, IsGenericMethod: false,
                DeclaredAccessibility: Accessibility.Public, ReturnsVoid: true } &&
                method.Name.StartsWith("Set", StringComparison.Ordinal) &&
                method.Parameters.Length == 2 &&
                _compilation.ClassifyConversion(control.Symbol, method.Parameters[0].Type).IsImplicit)
            .Select(method => method.Name[3..])
            .Where(name => owner.GetMembers(name + "Property").OfType<IFieldSymbol>().Any(field =>
                field is { IsStatic: true, DeclaredAccessibility: Accessibility.Public } &&
                IsAvaloniaProperty(field.Type)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new NativeAttachedCandidate(
                name,
                $"{owner.ToDisplayString(FullyQualifiedFormat)}.Set{name}",
                owner.GetMembers("Set" + name).OfType<IMethodSymbol>().First()))
            .ToArray();
    }

    public IReadOnlyList<ISymbol> GetExpressionMembers(
        ITypeSymbol type,
        bool staticMembers = false) =>
        EnumerateExpressionTypes(type)
            .SelectMany(candidate => candidate.GetMembers())
            .Where(member =>
                member.IsStatic == staticMembers &&
                IsAccessible(member) &&
                (member is IPropertySymbol or IFieldSymbol or IEventSymbol ||
                 member is IMethodSymbol { MethodKind: MethodKind.Ordinary }))
            .GroupBy(member => member.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .ToArray();

    public ISymbol? ResolveExpressionMember(
        ITypeSymbol type,
        string name,
        bool staticMember = false) =>
        GetExpressionMembers(type, staticMember)
            .FirstOrDefault(member => member.Name == name);

    public static ITypeSymbol? GetExpressionMemberType(ISymbol member) =>
        member switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            IEventSymbol @event => @event.Type,
            IMethodSymbol method => method.ReturnType,
            _ => null,
        };

    public static string? GetExpressionDocumentation(ISymbol symbol) =>
        GetDocumentation(symbol);

    public IReadOnlyList<INamedTypeSymbol> GetExpressionTypes() =>
        _imports
            .Select(ResolveNamespace)
            .Where(@namespace => @namespace is not null)
            .Cast<INamespaceSymbol>()
            .SelectMany(@namespace => @namespace.GetTypeMembers())
            .Where(type => !type.IsImplicitlyDeclared && IsAccessible(type))
            .GroupBy(
                type => type.ToDisplayString(FullyQualifiedFormat),
                StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();

    private INamespaceSymbol? ResolveNamespace(string name)
    {
        INamespaceSymbol current = _compilation.GlobalNamespace;
        foreach (var segment in name.Split('.'))
        {
            var next = current.GetNamespaceMembers()
                .FirstOrDefault(candidate => candidate.Name == segment);
            if (next is null)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    public static ITypeSymbol? GetEnumerableElementType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return array.ElementType;
        }

        return type is INamedTypeSymbol named
            ? new[] { named }.Concat(named.AllInterfaces)
                .FirstOrDefault(candidate =>
                    candidate.OriginalDefinition.SpecialType ==
                    SpecialType.System_Collections_Generic_IEnumerable_T)
                ?.TypeArguments[0]
            : null;
    }

    public LucentSemanticSymbol ToExpressionSemanticSymbol(
        string name,
        SourceSpan referenceSpan,
        string display,
        ISymbol? symbol = null) =>
        symbol is null
            ? new LucentSemanticSymbol(
                name,
                LucentSemanticSymbolKind.Expression,
                referenceSpan,
                display,
                null,
                null)
            : CreateSemanticSymbol(
                name,
                LucentSemanticSymbolKind.Expression,
                referenceSpan,
                display,
                symbol);

    public IReadOnlyList<NativeValueCandidate> GetValueCandidates(
        ResolvedNativeProperty property)
    {
        var type = UnwrapNullable(property.Symbol.Type);
        if (type.TypeKind == TypeKind.Enum)
        {
            return type.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(field => field is { IsStatic: true, HasConstantValue: true })
                .Select(field => new NativeValueCandidate(
                    $"{type.Name}.{field.Name}",
                    $"{type.ToDisplayString(FullyQualifiedFormat)}.{field.Name}",
                    $"{type.ToDisplayString(FullyQualifiedFormat)}.{field.Name}",
                    GetDocumentation(field),
                    field))
                .ToArray();
        }

        if (type.SpecialType == SpecialType.System_Boolean)
        {
            return
            [
                new NativeValueCandidate("true", "true", "bool", null, null),
                new NativeValueCandidate("false", "false", "bool", null, null),
            ];
        }

        var ownValues = type.GetMembers()
            .Where(candidate =>
                candidate.IsStatic &&
                candidate.DeclaredAccessibility == Accessibility.Public)
            .Select(candidate => candidate switch
            {
                IFieldSymbol field => (Symbol: (ISymbol)field, Type: field.Type),
                IPropertySymbol property => (Symbol: (ISymbol)property, Type: property.Type),
                _ => default,
            })
            .Where(candidate =>
                candidate.Symbol is not null &&
                _compilation.ClassifyConversion(candidate.Type!, type).IsImplicit)
            .Select(candidate => new NativeValueCandidate(
                $"{type.Name}.{candidate.Symbol!.Name}",
                $"{type.ToDisplayString(FullyQualifiedFormat)}.{candidate.Symbol.Name}",
                $"{type.ToDisplayString(FullyQualifiedFormat)}.{candidate.Symbol.Name}",
                GetDocumentation(candidate.Symbol),
                candidate.Symbol));

        var brushes = _compilation.GetTypeByMetadataName("Avalonia.Media.Brushes");
        var brushValues = brushes?.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(candidate =>
                candidate.IsStatic &&
                candidate.DeclaredAccessibility == Accessibility.Public &&
                _compilation.ClassifyConversion(candidate.Type, type).IsImplicit)
            .Select(candidate => new NativeValueCandidate(
                $"Brushes.{candidate.Name}",
                $"global::Avalonia.Media.Brushes.{candidate.Name}",
                candidate.Type.ToDisplayString(FullyQualifiedFormat),
                GetDocumentation(candidate),
                candidate)) ?? [];

        return ownValues
            .Concat(brushValues)
            .GroupBy(candidate => candidate.Label, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    public IReadOnlyList<LucentSemanticSymbol> GetValueSymbols(
        ResolvedNativeProperty property,
        string expression,
        int absoluteStart)
    {
        var candidates = GetValueCandidates(property)
            .Where(candidate => candidate.Symbol is not null)
            .ToArray();
        if (candidates.Length == 0)
        {
            return [];
        }

        var root = SyntaxFactory.ParseExpression(expression);
        var result = new List<LucentSemanticSymbol>();
        foreach (var access in root.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            var candidate = candidates.FirstOrDefault(item =>
                item.Label.EndsWith("." + access.Name.Identifier.ValueText, StringComparison.Ordinal) &&
                item.Label.StartsWith(access.Expression.ToString().Split('.').Last() + ".", StringComparison.Ordinal));
            if (candidate?.Symbol is null)
            {
                continue;
            }

            result.Add(CreateSemanticSymbol(
                access.Name.Identifier.ValueText,
                LucentSemanticSymbolKind.NativeValue,
                new SourceSpan(
                    absoluteStart + access.Name.SpanStart,
                    access.Name.Span.Length),
                candidate.Display,
                candidate.Symbol));
        }

        return result;
    }

    public LucentSemanticSymbol ToSemanticSymbol(
        ResolvedNativeControl control,
        SourceSpan referenceSpan) =>
        CreateSemanticSymbol(
            control.SourceName,
            LucentSemanticSymbolKind.NativeControl,
            referenceSpan,
            $"class {control.Symbol.ToDisplayString(FullyQualifiedFormat)}",
            control.Symbol);

    public LucentSemanticSymbol ToSemanticSymbol(
        ResolvedNativeControl control,
        ResolvedNativeProperty property,
        SourceSpan referenceSpan) =>
        CreateSemanticSymbol(
            property.SourceName,
            LucentSemanticSymbolKind.NativeProperty,
            referenceSpan,
            $"{property.TypeName} {control.TypeName}.{property.Name} {{ " +
            $"{(property.Symbol.GetMethod is null ? string.Empty : "get; ")}" +
            $"{(property.Symbol.SetMethod is null ? string.Empty : "set; ")}}}",
            property.Symbol);

    public LucentSemanticSymbol ToSemanticSymbol(
        ResolvedNativeControl control,
        ResolvedNativeEvent @event,
        SourceSpan referenceSpan) =>
        CreateSemanticSymbol(
            @event.SourceName,
            LucentSemanticSymbolKind.NativeEvent,
            referenceSpan,
            $"event {@event.Symbol.Type.ToDisplayString(FullyQualifiedFormat)} " +
            $"{control.TypeName}.{@event.Name}",
            @event.Symbol);

    public bool ContentAcceptsControl(NativeContentRoute route) =>
        _controlType is not null &&
        _compilation.ClassifyConversion(_controlType, route.ValueType).IsImplicit;

    public bool ContentAcceptsString(NativeContentRoute route) =>
        _stringType is not null &&
        _compilation.ClassifyConversion(_stringType, route.ValueType).IsImplicit;

    private NativeContentRoute? ResolveContentRoute(INamedTypeSymbol control)
    {
        foreach (var type in EnumerateTypeHierarchy(control))
        {
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (!property.GetAttributes().Any(attribute =>
                        attribute.AttributeClass?.ToDisplayString() ==
                        "Avalonia.Metadata.ContentAttribute"))
                {
                    continue;
                }

                var resolvedProperty = new ResolvedNativeProperty(
                    property.Name,
                    property.Name,
                    property,
                    property.Type.ToDisplayString(FullyQualifiedFormat),
                    GetNativeValueKind(property.Type));
                var collectionValueType = GetCollectionValueType(property.Type);
                return new NativeContentRoute(
                    resolvedProperty,
                    collectionValueType is not null,
                    collectionValueType ?? property.Type);
            }
        }

        return null;
    }

    public LucentSemanticSymbol ToSemanticSymbol(
        ResolvedNativeAttachedProperty property,
        SourceSpan referenceSpan) =>
        CreateSemanticSymbol(
            property.SourceName,
            LucentSemanticSymbolKind.NativeAttachedProperty,
            referenceSpan,
            $"{property.ValueTypeName} {property.OwnerTypeName}.{property.SetterName} " +
            $"({property.OwnerTypeName}.{property.SetterName.Replace("Set", "", StringComparison.Ordinal)}Property)",
            property.Setter);

    private static ITypeSymbol? GetCollectionValueType(ITypeSymbol type) =>
        GetMembers<IMethodSymbol>(type, "Add")
            .Where(method =>
                !method.IsStatic &&
                method.DeclaredAccessibility == Accessibility.Public &&
                method.Parameters.Length == 1)
            .Select(method => method.Parameters[0].Type)
            .FirstOrDefault();

    private bool IsControl(INamedTypeSymbol candidate)
    {
        if (_controlType is null || candidate.IsAbstract || !IsAccessible(candidate))
        {
            return false;
        }

        return EnumerateTypeHierarchy(candidate)
                .Any(type => SymbolEqualityComparer.Default.Equals(type, _controlType)) &&
            candidate.InstanceConstructors.Any(constructor =>
                constructor.Parameters.All(parameter => parameter.IsOptional) &&
                IsAccessible(constructor));
    }

    private bool IsAccessible(ISymbol symbol) =>
        symbol.DeclaredAccessibility == Accessibility.Public ||
        ((symbol.DeclaredAccessibility is Accessibility.Internal or
            Accessibility.ProtectedOrInternal) &&
         SymbolEqualityComparer.Default.Equals(
             symbol.ContainingAssembly,
             _compilation.Assembly));

    private bool IsAvaloniaProperty(ITypeSymbol type)
    {
        var propertyType = _compilation.GetTypeByMetadataName("Avalonia.AvaloniaProperty");
        return propertyType is not null &&
            (SymbolEqualityComparer.Default.Equals(type, propertyType) ||
             type.BaseType is not null && IsAvaloniaProperty(type.BaseType));
    }

    private static string CanonicalPropertyName(string controlName, string propertyName) =>
        (controlName, propertyName) switch
        {
            ("Text", "text") => "Text",
            ("Button", "text") => "Content",
            (_, "Class") => "Classes",
            _ => propertyName,
        };

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type) =>
        type is INamedTypeSymbol
        {
            IsGenericType: true,
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            TypeArguments: [var underlying],
        }
            ? underlying
            : type;

    private static BoundNativeValueKind GetNativeValueKind(ITypeSymbol type) =>
        type.ToDisplayString() switch
        {
            "Avalonia.Thickness" => BoundNativeValueKind.Thickness,
            "Avalonia.CornerRadius" => BoundNativeValueKind.CornerRadius,
            _ => BoundNativeValueKind.None,
        };

    private static IEnumerable<TSymbol> GetMembers<TSymbol>(
        ITypeSymbol type,
        string name)
        where TSymbol : class, ISymbol
    {
        if (type is not INamedTypeSymbol named)
        {
            yield break;
        }

        foreach (var current in EnumerateTypeHierarchy(named))
        {
            foreach (var member in current.GetMembers(name).OfType<TSymbol>())
            {
                yield return member;
            }
        }

        foreach (var @interface in named.AllInterfaces)
        {
            foreach (var member in @interface.GetMembers(name).OfType<TSymbol>())
            {
                yield return member;
            }
        }
    }

    private static IReadOnlyList<TSymbol> GetNearestMembers<TSymbol>(
        INamedTypeSymbol type,
        string name)
        where TSymbol : class, ISymbol
    {
        foreach (var current in EnumerateTypeHierarchy(type))
        {
            var matches = current.GetMembers(name).OfType<TSymbol>().ToArray();
            if (matches.Length > 0)
            {
                return matches;
            }
        }

        return [];
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypeHierarchy(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateExpressionTypes(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
        {
            yield break;
        }

        foreach (var candidate in EnumerateTypeHierarchy(named))
        {
            yield return candidate;
        }

        foreach (var @interface in named.AllInterfaces)
        {
            yield return @interface;
        }
    }

    private static LucentSemanticSymbol CreateSemanticSymbol(
        string name,
        LucentSemanticSymbolKind kind,
        SourceSpan referenceSpan,
        string display,
        ISymbol symbol,
        string? fallbackDocumentation = null)
    {
        var documentation = GetDocumentation(symbol) ?? fallbackDocumentation;
        var location = symbol.Locations.FirstOrDefault(candidate => candidate.IsInSource);
        LucentDefinition? definition = null;
        if (location?.SourceTree?.FilePath is { Length: > 0 } sourcePath)
        {
            definition = new LucentDefinition(
                sourcePath,
                new SourceSpan(location.SourceSpan.Start, location.SourceSpan.Length));
        }

        return new LucentSemanticSymbol(
            name,
            kind,
            referenceSpan,
            display,
            documentation,
            definition);
    }

    private static string? GetDocumentation(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        try
        {
            return XElement.Parse($"<root>{xml}</root>")
                .Descendants("summary")
                .FirstOrDefault()?
                .Value
                .Trim();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}

internal sealed record ResolvedNativeControl(
    string SourceName,
    INamedTypeSymbol Symbol,
    string TypeName,
    NativeContentRoute? ContentRoute);

internal sealed record ResolvedNativeProperty(
    string SourceName,
    string Name,
    IPropertySymbol Symbol,
    string TypeName,
    BoundNativeValueKind NativeValueKind);

internal sealed record ResolvedNativeAttachedProperty(
    string SourceName,
    string OwnerTypeName,
    string SetterName,
    IMethodSymbol Setter,
    ISymbol PropertyField,
    ITypeSymbol ValueType,
    string ValueTypeName);

internal sealed record ResolvedNativeCollection(
    ResolvedNativeProperty Property,
    IMethodSymbol AddMethod,
    ITypeSymbol ElementType,
    string ElementTypeName);

internal sealed record NativeAttachedCandidate(
    string Name,
    string Display,
    IMethodSymbol Setter);

internal sealed record ResolvedNativeEvent(
    string SourceName,
    string Name,
    IEventSymbol Symbol,
    string DelegateTypeName,
    string SenderTypeName,
    string EventArgsTypeName);

internal sealed record NativeContentRoute(
    ResolvedNativeProperty Property,
    bool IsCollection,
    ITypeSymbol ValueType);

internal sealed record NativeValueCandidate(
    string Label,
    string InsertText,
    string Display,
    string? Documentation,
    ISymbol? Symbol);
