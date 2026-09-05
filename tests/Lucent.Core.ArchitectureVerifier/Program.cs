using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length != 1)
    return 2;

using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var metadata = pe.GetMetadataReader();
var provider = new TypeNameProvider();
var violations = new List<string>();
foreach (var handle in metadata.TypeReferences)
{
    var referenced = provider.GetTypeFromReference(metadata, handle, 0);
    if (IsRuntimeDiscoveryType(referenced))
        violations.Add($"Forbidden Core runtime discovery type: {referenced}");
}
foreach (var handle in metadata.TypeDefinitions)
{
    var type = metadata.GetTypeDefinition(handle);
    if (
        (type.Attributes & TypeAttributes.VisibilityMask)
        is not (TypeAttributes.Public or TypeAttributes.NestedPublic)
    )
        continue;
    foreach (var exposed in ExposedTypes(metadata, type, provider))
    {
        if (IsForbidden(exposed))
            violations.Add($"Forbidden Core public API type: {exposed}");
    }
}
VerifyLuiMetadata(metadata, provider, violations);
VerifyPropertySurface(metadata, provider, violations);
foreach (var violation in violations.Distinct(StringComparer.Ordinal))
    Console.Error.WriteLine(violation);
return violations.Count == 0 ? 0 : 1;

static void VerifyLuiMetadata(
    MetadataReader metadata,
    TypeNameProvider provider,
    List<string> violations
)
{
    var definitions = metadata.TypeDefinitions.ToArray();
    if (
        definitions.Any(handle =>
            TypeDefinitionName(metadata, handle)
                is "Lucent.Core.LuiComponentAttribute"
                    or "Lucent.Core.LuiContentAttribute"
        )
    )
        violations.Add("Legacy LUI metadata remains.");
    foreach (
        var retired in new[]
        {
            "Lucent.Core.Controls",
            "Lucent.Core.ControlState",
            "Lucent.Core.ScrollViewportState",
            "Lucent.Core.TextFieldState",
            "Lucent.Core.VirtualizedRegion`2",
        }
    )
        if (
            definitions.FirstOrDefault(handle => TypeDefinitionName(metadata, handle) == retired)
                is var handle
            && !handle.IsNil
            && (metadata.GetTypeDefinition(handle).Attributes & TypeAttributes.VisibilityMask)
                == TypeAttributes.Public
        )
            violations.Add("Retired Controls surface remains public: " + retired);
    var components = definitions.FirstOrDefault(handle =>
        TypeDefinitionName(metadata, handle) == "Lucent.Core.Components"
    );
    if (components.IsNil)
    {
        violations.Add("Missing Components metadata.");
        return;
    }
    var recipes = metadata
        .GetTypeDefinition(components)
        .GetMethods()
        .Select(metadata.GetMethodDefinition)
        .ToArray();
    var publicMethods = recipes
        .Where(method =>
            (method.Attributes & (MethodAttributes.Public | MethodAttributes.Static))
            == (MethodAttributes.Public | MethodAttributes.Static)
        )
        .ToArray();
    var annotated = publicMethods
        .Where(method =>
            HasAttribute(
                metadata,
                method.GetCustomAttributes(),
                "Lucent.Core.LucentComponentAttribute"
            )
        )
        .ToArray();
    if (annotated.Length != publicMethods.Length)
        violations.Add("Components exposes an unannotated public method.");
    var expected = new Dictionary<string, int>
    {
        ["Row"] = 1,
        ["Column"] = 1,
        ["Text"] = 2,
        ["Button"] = 1,
        ["TextField"] = 1,
        ["Selectable"] = 2,
        ["ScrollViewport"] = 1,
        ["VirtualizedList"] = 1,
        ["Status"] = 2,
        ["Progress"] = 2,
    };
    if (
        !annotated
            .GroupBy(method => metadata.GetString(method.Name))
            .ToDictionary(group => group.Key, group => group.Count())
            .OrderBy(pair => pair.Key)
            .SequenceEqual(expected.OrderBy(pair => pair.Key))
    )
        violations.Add("Components catalog metadata changed.");
    foreach (var recipe in annotated)
    {
        var signature = recipe.DecodeSignature(provider, null);
        if (
            signature.ReturnType != "Lucent.Core.ComponentRecipe"
            || signature.ParameterTypes.Any(type =>
                type.Contains("Lucent.Core.Element", StringComparison.Ordinal)
                || type.Contains("ControlState", StringComparison.Ordinal)
                || type.Contains("ScrollViewportState", StringComparison.Ordinal)
                || type.Contains("VirtualizedRegion", StringComparison.Ordinal)
                || type.Contains("TextFieldState", StringComparison.Ordinal)
            )
        )
            violations.Add(
                "Components leaked a mounted handle: " + metadata.GetString(recipe.Name)
            );
        var content = recipe
            .GetParameters()
            .Select(metadata.GetParameter)
            .Where(parameter =>
                parameter.SequenceNumber != 0
                && HasAttribute(
                    metadata,
                    parameter.GetCustomAttributes(),
                    "Lucent.Core.DefaultContentAttribute"
                )
            )
            .ToArray();
        if (
            content.Length > 1
            || content.Any(parameter =>
                metadata.GetString(parameter.Name) is not ("content" or "label")
            )
        )
            violations.Add(
                "Unexpected [DefaultContent] metadata: " + metadata.GetString(recipe.Name)
            );
    }
}

static void VerifyPropertySurface(
    MetadataReader metadata,
    TypeNameProvider provider,
    List<string> violations
)
{
    var definitions = metadata
        .TypeDefinitions.Where(handle =>
            metadata.GetString(metadata.GetTypeDefinition(handle).Namespace) == "Lucent.Core"
        )
        .ToDictionary(handle => TypeDefinitionName(metadata, handle));
    foreach (var removed in new[] { "Lucent.Core.Arrangement", "Lucent.Core.SceneProperties" })
        if (definitions.ContainsKey(removed))
            violations.Add("Removed public property group remains: " + removed);
    if (
        !definitions.TryGetValue("Lucent.Core.ProjectionProperties", out var projection)
        || (metadata.GetTypeDefinition(projection).Attributes & TypeAttributes.VisibilityMask)
            == TypeAttributes.Public
    )
        violations.Add("ProjectionProperties must exist and remain non-public.");
    foreach (
        var expected in new Dictionary<string, Dictionary<string, string>>
        {
            ["Lucent.Core.LayoutProperties"] = new()
            {
                ["Axis"] = "Lucent.Core.Property`1|Lucent.Core.LayoutAxis",
                ["Width"] = "Lucent.Core.Property`1|System.Nullable`1|System.Single",
                ["Height"] = "Lucent.Core.Property`1|System.Nullable`1|System.Single",
                ["MinWidth"] = "Lucent.Core.Property`1|System.Single",
                ["MinHeight"] = "Lucent.Core.Property`1|System.Single",
                ["MaxWidth"] = "Lucent.Core.Property`1|System.Single",
                ["MaxHeight"] = "Lucent.Core.Property`1|System.Single",
                ["Spacing"] = "Lucent.Core.Property`1|System.Single",
                ["MainGrow"] = "Lucent.Core.Property`1|System.Single",
                ["MainAlignment"] = "Lucent.Core.Property`1|Lucent.Core.LayoutAlignment",
                ["CrossAlignment"] = "Lucent.Core.Property`1|Lucent.Core.LayoutAlignment",
                ["Padding"] = "Lucent.Core.Property`1|Lucent.Core.Insets",
                ["Clip"] = "Lucent.Core.Property`1|System.Boolean",
                ["Scroll"] = "Lucent.Core.Property`1|Lucent.Core.ScrollOffset",
            },
            ["Lucent.Core.VisualProperties"] = new()
            {
                ["Background"] = "Lucent.Core.Property`1|Lucent.Core.Brush",
                ["Opacity"] = "Lucent.Core.Property`1|System.Single",
                ["Participation"] = "Lucent.Core.Property`1|Lucent.Core.ElementParticipation",
            },
            ["Lucent.Core.TypographyProperties"] = new()
            {
                ["TextColor"] = "Lucent.Core.Property`1|Lucent.Core.Color",
                ["FontFamily"] = "Lucent.Core.Property`1|System.String",
                ["FontSize"] = "Lucent.Core.Property`1|System.Single",
                ["Language"] = "Lucent.Core.Property`1|System.String",
                ["Direction"] = "Lucent.Core.Property`1|Lucent.Core.TextDirection",
            },
            ["Lucent.Core.InputProperties"] = new()
            {
                ["Enabled"] = "Lucent.Core.Property`1|System.Boolean",
                ["Visible"] = "Lucent.Core.Property`1|System.Boolean",
            },
        }
    )
    {
        if (!definitions.TryGetValue(expected.Key, out var handle))
        {
            violations.Add("Missing public property group: " + expected.Key);
            continue;
        }
        var type = metadata.GetTypeDefinition(handle);
        if ((type.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public)
            violations.Add("Property group is not public: " + expected.Key);
        var fields = type.GetFields()
            .Select(metadata.GetFieldDefinition)
            .Where(field =>
                (field.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public
            )
            .ToDictionary(field => metadata.GetString(field.Name));
        if (!fields.Keys.Order().SequenceEqual(expected.Value.Keys.Order()))
        {
            violations.Add("Unexpected public property members: " + expected.Key);
            continue;
        }
        foreach (var field in expected.Value)
            if (fields[field.Key].DecodeSignature(provider, null) != field.Value)
                violations.Add($"Unexpected property type: {expected.Key}.{field.Key}");
    }
}

static bool HasAttribute(
    MetadataReader metadata,
    CustomAttributeHandleCollection attributes,
    string name
) =>
    attributes.Any(handle =>
        AttributeName(metadata, metadata.GetCustomAttribute(handle).Constructor) == name
    );

static string AttributeName(MetadataReader metadata, EntityHandle constructor) =>
    constructor.Kind switch
    {
        HandleKind.MethodDefinition => TypeDefinitionName(
            metadata,
            metadata.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType()
        ),
        HandleKind.MemberReference => metadata
            .GetMemberReference((MemberReferenceHandle)constructor)
            .Parent.Kind switch
        {
            HandleKind.TypeDefinition => TypeDefinitionName(
                metadata,
                (TypeDefinitionHandle)
                    metadata.GetMemberReference((MemberReferenceHandle)constructor).Parent
            ),
            HandleKind.TypeReference => TypeReferenceName(
                metadata,
                (TypeReferenceHandle)
                    metadata.GetMemberReference((MemberReferenceHandle)constructor).Parent
            ),
            _ => "",
        },
        _ => "",
    };

static string TypeDefinitionName(MetadataReader metadata, TypeDefinitionHandle handle)
{
    var type = metadata.GetTypeDefinition(handle);
    return string.IsNullOrEmpty(metadata.GetString(type.Namespace))
        ? metadata.GetString(type.Name)
        : metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
}

static string TypeReferenceName(MetadataReader metadata, TypeReferenceHandle handle)
{
    var type = metadata.GetTypeReference(handle);
    return string.IsNullOrEmpty(metadata.GetString(type.Namespace))
        ? metadata.GetString(type.Name)
        : metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
}

static IEnumerable<string> ExposedTypes(
    MetadataReader metadata,
    TypeDefinition type,
    TypeNameProvider provider
)
{
    yield return provider.Name(metadata, type.Namespace, type.Name);
    if (!type.BaseType.IsNil)
        yield return provider.FromHandle(metadata, type.BaseType);
    foreach (var implementation in type.GetInterfaceImplementations())
        yield return provider.FromHandle(
            metadata,
            metadata.GetInterfaceImplementation(implementation).Interface
        );
    foreach (var handle in type.GetMethods())
    {
        var method = metadata.GetMethodDefinition(handle);
        if (
            (method.Attributes & MethodAttributes.MemberAccessMask)
            is not (
                MethodAttributes.Public
                or MethodAttributes.Family
                or MethodAttributes.FamORAssem
            )
        )
            continue;
        var signature = method.DecodeSignature(provider, genericContext: null);
        yield return signature.ReturnType;
        foreach (var parameter in signature.ParameterTypes)
            yield return parameter;
    }
    foreach (var handle in type.GetProperties())
    {
        var property = metadata.GetPropertyDefinition(handle);
        var accessors = property.GetAccessors();
        if (!IsExposed(accessors.Getter) && !IsExposed(accessors.Setter))
            continue;
        var signature = property.DecodeSignature(provider, genericContext: null);
        yield return signature.ReturnType;
        foreach (var parameter in signature.ParameterTypes)
            yield return parameter;
    }
    foreach (var handle in type.GetFields())
    {
        var field = metadata.GetFieldDefinition(handle);
        if ((field.Attributes & FieldAttributes.FieldAccessMask) is FieldAttributes.Public)
            yield return field.DecodeSignature(provider, genericContext: null);
    }
    foreach (var handle in type.GetEvents())
    {
        var @event = metadata.GetEventDefinition(handle);
        var accessors = @event.GetAccessors();
        if (
            IsExposed(accessors.Adder)
            || IsExposed(accessors.Remover)
            || IsExposed(accessors.Raiser)
        )
            yield return provider.FromHandle(metadata, @event.Type);
    }

    bool IsExposed(MethodDefinitionHandle handle)
    {
        if (handle.IsNil)
            return false;
        var access =
            metadata.GetMethodDefinition(handle).Attributes & MethodAttributes.MemberAccessMask;
        return access
            is MethodAttributes.Public
                or MethodAttributes.Family
                or MethodAttributes.FamORAssem;
    }
}
static bool IsForbidden(string type) =>
    type.Split('|')
        .Any(part =>
            new[]
            {
                "SDL3",
                "SkiaSharp",
                "Windows.Win32",
                "Microsoft.Windows.CsWin32",
                "Lucent.Platform.Windows",
            }.Any(prefix => part.StartsWith(prefix, StringComparison.Ordinal))
        );

static bool IsRuntimeDiscoveryType(string type) =>
    type
        is "System.Reflection.Assembly"
            or "System.Reflection.MemberInfo"
            or "System.Reflection.MethodInfo"
            or "System.Reflection.PropertyInfo"
            or "System.Reflection.FieldInfo"
            or "System.ComponentModel.TypeDescriptor"
            or "System.ComponentModel.PropertyDescriptor"
    || new[]
    {
        "System.Dynamic",
        "System.Linq.Expressions",
        "System.Runtime.Loader",
        "System.Text.Json",
        "System.Xml",
    }.Any(prefix => type.StartsWith(prefix, StringComparison.Ordinal));

internal sealed class TypeNameProvider : ISignatureTypeProvider<string, object?>
{
    public string Name(MetadataReader reader, StringHandle @namespace, StringHandle name) =>
        reader.GetString(@namespace) is { Length: > 0 } ns
            ? ns + "." + reader.GetString(name)
            : reader.GetString(name);

    public string FromHandle(MetadataReader reader, EntityHandle handle) =>
        handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeFromDefinition(
                reader,
                (TypeDefinitionHandle)handle,
                0
            ),
            HandleKind.TypeReference => GetTypeFromReference(
                reader,
                (TypeReferenceHandle)handle,
                0
            ),
            HandleKind.TypeSpecification => GetTypeFromSpecification(
                reader,
                null,
                (TypeSpecificationHandle)handle,
                0
            ),
            _ => string.Empty,
        };

    public string GetArrayType(string elementType, ArrayShape shape) => elementType;

    public string GetByReferenceType(string elementType) => elementType;

    public string GetFunctionPointerType(MethodSignature<string> signature) => signature.ReturnType;

    public string GetGenericInstantiation(
        string genericType,
        ImmutableArray<string> typeArguments
    ) => genericType + "|" + string.Join('|', typeArguments);

    public string GetGenericMethodParameter(object? genericContext, int index) => string.Empty;

    public string GetGenericTypeParameter(object? genericContext, int index) => string.Empty;

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) =>
        unmodifiedType;

    public string GetPinnedType(string elementType) => elementType;

    public string GetPointerType(string elementType) => elementType;

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) =>
        typeCode switch
        {
            PrimitiveTypeCode.Void => "System.Void",
            PrimitiveTypeCode.Boolean => "System.Boolean",
            PrimitiveTypeCode.Char => "System.Char",
            PrimitiveTypeCode.String => "System.String",
            PrimitiveTypeCode.Int32 => "System.Int32",
            PrimitiveTypeCode.Int64 => "System.Int64",
            PrimitiveTypeCode.Single => "System.Single",
            PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.Object => "System.Object",
            _ => typeCode.ToString(),
        };

    public string GetSZArrayType(string elementType) => elementType;

    public string GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind
    )
    {
        var type = reader.GetTypeDefinition(handle);
        return Name(reader, type.Namespace, type.Name);
    }

    public string GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind
    )
    {
        var type = reader.GetTypeReference(handle);
        return type.ResolutionScope.Kind is HandleKind.TypeReference
            ? GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind)
                + "+"
                + reader.GetString(type.Name)
            : Name(reader, type.Namespace, type.Name);
    }

    public string GetTypeFromSpecification(
        MetadataReader reader,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind
    ) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}
