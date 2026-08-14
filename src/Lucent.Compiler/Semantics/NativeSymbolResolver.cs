using System.Collections.Immutable;
using System.Xml.Linq;
using Lucent.Compiler.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lucent.Compiler.Semantics;

internal sealed class NativeSymbolResolver
{
    private static readonly IReadOnlyDictionary<string, string> LegacyControlAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Column"] = "Avalonia.Controls.StackPanel",
            ["Text"] = "Avalonia.Controls.TextBlock",
        };

    private static readonly string[] DefaultControlNamespaces =
    [
        "Avalonia.Controls",
        "Avalonia.Controls.Primitives",
        "Avalonia.Controls.Presenters",
    ];

    private static readonly SymbolDisplayFormat FullyQualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private readonly CSharpCompilation _compilation;
    private readonly INamedTypeSymbol? _controlType;
    private readonly INamedTypeSymbol? _stringType;
    private readonly IReadOnlyList<string> _imports;

    public NativeSymbolResolver(
        string componentNamespace,
        IReadOnlyList<string> usingDirectives,
        LucentProjectContext? projectContext)
    {
        _compilation = CreateCompilation(projectContext);
        _controlType = _compilation.GetTypeByMetadataName("Avalonia.Controls.Control");
        _stringType = _compilation.GetSpecialType(SpecialType.System_String);
        _imports = BuildImports(componentNamespace, usingDirectives);
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

    public LucentSemanticSymbol ToSemanticSymbol(
        ResolvedNativeControl control,
        SourceSpan referenceSpan) =>
        CreateSemanticSymbol(
            control.SourceName,
            LucentSemanticSymbolKind.NativeControl,
            referenceSpan,
            $"class {control.Symbol.ToDisplayString(FullyQualifiedFormat)}",
            control.Symbol,
            "Native Avalonia control.");

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
            property.Symbol,
            "Native Avalonia property.");

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
            @event.Symbol,
            $"Native Avalonia event. Handler: ({control.TypeName} sender, {@event.EventArgsTypeName} e) => ...");

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

    private static string CanonicalPropertyName(string controlName, string propertyName) =>
        (controlName, propertyName) switch
        {
            ("Text", "text") => "Text",
            ("Button", "text") => "Content",
            (_, "class") => "Classes",
            _ => propertyName,
        };

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

    private static IReadOnlyList<string> BuildImports(
        string componentNamespace,
        IReadOnlyList<string> usingDirectives)
    {
        var imports = new List<string>();
        if (!string.IsNullOrWhiteSpace(componentNamespace))
        {
            imports.Add(componentNamespace);
        }

        imports.AddRange(DefaultControlNamespaces);
        foreach (var directive in usingDirectives)
        {
            var text = directive.Trim();
            if (text.StartsWith("using ", StringComparison.Ordinal))
            {
                text = text[6..].Trim();
            }

            text = text.TrimEnd(';').Trim();
            if (text.Length > 0 &&
                !text.StartsWith("static ", StringComparison.Ordinal) &&
                !text.Contains('=', StringComparison.Ordinal))
            {
                imports.Add(text);
            }
        }

        return imports.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static CSharpCompilation CreateCompilation(LucentProjectContext? context)
    {
        var referencePaths = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var path in context?.References ?? [])
        {
            if (File.Exists(path))
            {
                referencePaths[Path.GetFileName(path)] = Path.GetFullPath(path);
            }
        }

        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (referencePaths.Count == 0 && !string.IsNullOrWhiteSpace(trustedAssemblies))
        {
            foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
            {
                referencePaths.TryAdd(Path.GetFileName(path), path);
            }
        }

        if (!referencePaths.ContainsKey("Avalonia.Base.dll"))
        {
            var avaloniaDirectory = new[]
                {
                    AppContext.BaseDirectory,
                    Path.GetDirectoryName(typeof(NativeSymbolResolver).Assembly.Location),
                }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .FirstOrDefault(path =>
                    File.Exists(Path.Combine(path!, "Avalonia.Controls.dll")));
            if (avaloniaDirectory is null)
            {
                throw new InvalidOperationException(
                    "The standalone Lucent context could not locate Avalonia.Controls.dll. " +
                    "Supply consuming-project reference paths through LucentProjectContext.");
            }

            foreach (var path in Directory.EnumerateFiles(avaloniaDirectory, "Avalonia*.dll"))
            {
                referencePaths.TryAdd(Path.GetFileName(path), path);
            }
        }

        var references = referencePaths.Values
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
        var syntaxTrees = (context?.Sources ?? [])
            .Where(File.Exists)
            .Select(path => CSharpSyntaxTree.ParseText(
                File.ReadAllText(path),
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
                path))
            .ToArray();

        return CSharpCompilation.Create(
            "Lucent.ProjectSemantics",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static LucentSemanticSymbol CreateSemanticSymbol(
        string name,
        LucentSemanticSymbolKind kind,
        SourceSpan referenceSpan,
        string display,
        ISymbol symbol,
        string fallbackDocumentation)
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
