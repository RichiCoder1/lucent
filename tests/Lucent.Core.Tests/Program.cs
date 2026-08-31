using System.Reflection;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Lucent.Core;

if (args.Length == 0)
{
    if (ReactiveContracts.Run() != 0 || CompositionContracts.Run() != 0 || LayoutSceneContracts.Run() != 0 || InputContracts.Run() != 0 || ControlsContracts.Run() != 0 || TextFieldContracts.Run() != 0) return 1;
    return PresentationContracts.Run();
}
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
    if ((type.Attributes & TypeAttributes.VisibilityMask) is not (TypeAttributes.Public or TypeAttributes.NestedPublic))
        continue;
    foreach (var exposed in ExposedTypes(metadata, type, provider))
    {
        if (IsForbidden(exposed))
            violations.Add($"Forbidden Core public API type: {exposed}");
    }
}
VerifyLuiMetadata(metadata, provider, violations);
foreach (var violation in violations.Distinct(StringComparer.Ordinal)) Console.Error.WriteLine(violation);
return violations.Count == 0 ? 0 : 1;

static void VerifyLuiMetadata(MetadataReader metadata, TypeNameProvider provider, List<string> violations)
{
    var controls = metadata.TypeDefinitions.FirstOrDefault(handle => provider.Name(metadata, metadata.GetTypeDefinition(handle).Namespace, metadata.GetTypeDefinition(handle).Name) == "Lucent.Core.Controls");
    if (controls.IsNil) { violations.Add("Missing Controls metadata."); return; }
    foreach (var expected in new[]
    {
        new Recipe("Text", ["context", "Name", "Content", "Style"], ["Lucent.Core.CompositionContext", "System.String", "System.String", "Lucent.Core.Style"], ["-", "-", "-", "null"], "Content"),
        new Recipe("Row", ["context", "Name", "Content", "Style"], ["Lucent.Core.CompositionContext", "System.String", "System.Func`2|Lucent.Core.CompositionContext|Lucent.Core.Element", "Lucent.Core.Style"], ["-", "-", "-", "null"], "Content"),
        new Recipe("TextField", ["context", "Name", "InitialValue", "OnChange", "Style"], ["Lucent.Core.CompositionContext", "System.String", "System.String", "System.Action`1|System.String", "Lucent.Core.Style"], ["-", "-", "", "null", "null"], null),
        new Recipe("Button", ["context", "Name", "Label", "OnInvoke", "Style"], ["Lucent.Core.CompositionContext", "System.String", "System.String", "System.Action", "Lucent.Core.Style"], ["-", "-", "-", "null", "null"], "Label")
    })
    {
        var recipes = metadata.GetTypeDefinition(controls).GetMethods().Select(metadata.GetMethodDefinition)
            .Where(method => metadata.GetString(method.Name) == expected.Name && HasAttribute(metadata, method.GetCustomAttributes(), "Lucent.Core.LuiComponentAttribute") &&
                (method.Attributes & (MethodAttributes.Public | MethodAttributes.Static)) == (MethodAttributes.Public | MethodAttributes.Static) && method.DecodeSignature(provider, null).ReturnType == "Lucent.Core.Element").ToArray();
        if (recipes.Length != 1) { violations.Add($"Missing exact [LuiComponent] recipe: {expected.Name}"); continue; }
        var recipe = recipes[0];
        var signature = recipe.DecodeSignature(provider, null);
        var parameters = recipe.GetParameters().Select(metadata.GetParameter).Where(parameter => parameter.SequenceNumber != 0).ToArray();
        if (!parameters.Select(parameter => metadata.GetString(parameter.Name)).SequenceEqual(expected.ParameterNames) || !signature.ParameterTypes.SequenceEqual(expected.ParameterTypes) ||
            !parameters.Select(parameter => DefaultValue(metadata, parameter)).SequenceEqual(expected.Defaults))
            violations.Add($"Unexpected [LuiComponent] signature: {expected.Name}");
        var contents = parameters.Where(parameter => HasAttribute(metadata, parameter.GetCustomAttributes(), "Lucent.Core.LuiContentAttribute")).Select(parameter => metadata.GetString(parameter.Name)).ToArray();
        var defaults = parameters.Where(parameter => HasDefaultContent(metadata, parameter.GetCustomAttributes())).Select(parameter => metadata.GetString(parameter.Name)).ToArray();
        if (!(expected.DefaultContent is null ? contents.Length == 0 && defaults.Length == 0 : contents.SequenceEqual([expected.DefaultContent]) && defaults.SequenceEqual([expected.DefaultContent])))
            violations.Add($"Unexpected default [LuiContent] metadata: {expected.Name}");
    }
}

static string DefaultValue(MetadataReader metadata, Parameter parameter)
{
    if (!parameter.Attributes.HasFlag(ParameterAttributes.HasDefault)) return "-";
    var constant = metadata.GetConstant(parameter.GetDefaultValue());
    if (constant.TypeCode == ConstantTypeCode.NullReference) return "null";
    if (constant.TypeCode != ConstantTypeCode.String) return "?";
    var value = metadata.GetBlobReader(constant.Value);
    return value.ReadUTF16(value.Length);
}

static bool HasDefaultContent(MetadataReader metadata, CustomAttributeHandleCollection attributes)
{
    foreach (var handle in attributes)
    {
        var attribute = metadata.GetCustomAttribute(handle);
        if (AttributeName(metadata, attribute.Constructor) != "Lucent.Core.LuiContentAttribute") continue;
        var value = metadata.GetBlobReader(attribute.Value);
        return value.ReadUInt16() == 1 && value.ReadUInt16() == 1 && value.ReadByte() == 0x54 && value.ReadByte() == 0x02 && value.ReadSerializedString() == "IsDefault" && value.ReadByte() == 1;
    }
    return false;
}

static bool HasAttribute(MetadataReader metadata, CustomAttributeHandleCollection attributes, string name)
    => attributes.Any(handle => AttributeName(metadata, metadata.GetCustomAttribute(handle).Constructor) == name);

static string AttributeName(MetadataReader metadata, EntityHandle constructor) => constructor.Kind switch
{
    HandleKind.MethodDefinition => TypeDefinitionName(metadata, metadata.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType()),
    HandleKind.MemberReference => metadata.GetMemberReference((MemberReferenceHandle)constructor).Parent.Kind switch
    {
        HandleKind.TypeDefinition => TypeDefinitionName(metadata, (TypeDefinitionHandle)metadata.GetMemberReference((MemberReferenceHandle)constructor).Parent),
        HandleKind.TypeReference => TypeReferenceName(metadata, (TypeReferenceHandle)metadata.GetMemberReference((MemberReferenceHandle)constructor).Parent),
        _ => ""
    },
    _ => ""
};

static string TypeDefinitionName(MetadataReader metadata, TypeDefinitionHandle handle)
{
    var type = metadata.GetTypeDefinition(handle);
    return string.IsNullOrEmpty(metadata.GetString(type.Namespace)) ? metadata.GetString(type.Name) : metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
}

static string TypeReferenceName(MetadataReader metadata, TypeReferenceHandle handle)
{
    var type = metadata.GetTypeReference(handle);
    return string.IsNullOrEmpty(metadata.GetString(type.Namespace)) ? metadata.GetString(type.Name) : metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
}

static IEnumerable<string> ExposedTypes(MetadataReader metadata, TypeDefinition type, TypeNameProvider provider)
{
    yield return provider.Name(metadata, type.Namespace, type.Name);
    if (!type.BaseType.IsNil) yield return provider.FromHandle(metadata, type.BaseType);
    foreach (var implementation in type.GetInterfaceImplementations())
        yield return provider.FromHandle(metadata, metadata.GetInterfaceImplementation(implementation).Interface);
    foreach (var handle in type.GetMethods())
    {
        var method = metadata.GetMethodDefinition(handle);
        if ((method.Attributes & MethodAttributes.MemberAccessMask) is not (MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem)) continue;
        var signature = method.DecodeSignature(provider, genericContext: null);
        yield return signature.ReturnType;
        foreach (var parameter in signature.ParameterTypes) yield return parameter;
    }
    foreach (var handle in type.GetProperties())
    {
        var property = metadata.GetPropertyDefinition(handle);
        var accessors = property.GetAccessors();
        if (!IsExposed(accessors.Getter) && !IsExposed(accessors.Setter)) continue;
        var signature = property.DecodeSignature(provider, genericContext: null);
        yield return signature.ReturnType;
        foreach (var parameter in signature.ParameterTypes) yield return parameter;
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
        if (IsExposed(accessors.Adder) || IsExposed(accessors.Remover) || IsExposed(accessors.Raiser))
            yield return provider.FromHandle(metadata, @event.Type);
    }

    bool IsExposed(MethodDefinitionHandle handle)
    {
        if (handle.IsNil) return false;
        var access = metadata.GetMethodDefinition(handle).Attributes & MethodAttributes.MemberAccessMask;
        return access is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;
    }
}
static bool IsForbidden(string type) => type.Split('|').Any(part =>
    new[] { "SDL3", "SkiaSharp", "Windows.Win32", "Microsoft.Windows.CsWin32", "Lucent.Platform.Windows" }
        .Any(prefix => part.StartsWith(prefix, StringComparison.Ordinal)));

static bool IsRuntimeDiscoveryType(string type) => type is "System.Reflection.Assembly" or "System.Reflection.MemberInfo" or "System.Reflection.MethodInfo" or "System.Reflection.PropertyInfo" or "System.Reflection.FieldInfo" or "System.ComponentModel.TypeDescriptor" or "System.ComponentModel.PropertyDescriptor"
    || new[] { "System.Dynamic", "System.Linq.Expressions", "System.Runtime.Loader", "System.Text.Json", "System.Xml" }.Any(prefix => type.StartsWith(prefix, StringComparison.Ordinal));

internal sealed class TypeNameProvider : ISignatureTypeProvider<string, object?>
{
    public string Name(MetadataReader reader, StringHandle @namespace, StringHandle name) => reader.GetString(@namespace) is { Length: > 0 } ns ? ns + "." + reader.GetString(name) : reader.GetString(name);
    public string FromHandle(MetadataReader reader, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeReference => GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeSpecification => GetTypeFromSpecification(reader, null, (TypeSpecificationHandle)handle, 0),
        _ => string.Empty
    };
    public string GetArrayType(string elementType, ArrayShape shape) => elementType;
    public string GetByReferenceType(string elementType) => elementType;
    public string GetFunctionPointerType(MethodSignature<string> signature) => signature.ReturnType;
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "|" + string.Join('|', typeArguments);
    public string GetGenericMethodParameter(object? genericContext, int index) => string.Empty;
    public string GetGenericTypeParameter(object? genericContext, int index) => string.Empty;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType;
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Void => "System.Void", PrimitiveTypeCode.Boolean => "System.Boolean", PrimitiveTypeCode.Char => "System.Char",
        PrimitiveTypeCode.String => "System.String", PrimitiveTypeCode.Int32 => "System.Int32", PrimitiveTypeCode.Int64 => "System.Int64",
        PrimitiveTypeCode.Single => "System.Single", PrimitiveTypeCode.Double => "System.Double", PrimitiveTypeCode.Object => "System.Object",
        _ => typeCode.ToString()
    };
    public string GetSZArrayType(string elementType) => elementType;
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) { var type = reader.GetTypeDefinition(handle); return Name(reader, type.Namespace, type.Name); }
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        return type.ResolutionScope.Kind is HandleKind.TypeReference
            ? GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind) + "+" + reader.GetString(type.Name)
            : Name(reader, type.Namespace, type.Name);
    }
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}

internal sealed record Recipe(string Name, string[] ParameterNames, string[] ParameterTypes, string[] Defaults, string? DefaultContent);
