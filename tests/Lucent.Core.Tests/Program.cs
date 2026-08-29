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
foreach (var violation in violations.Distinct(StringComparer.Ordinal)) Console.Error.WriteLine(violation);
return violations.Count == 0 ? 0 : 1;

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
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => string.Empty;
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
