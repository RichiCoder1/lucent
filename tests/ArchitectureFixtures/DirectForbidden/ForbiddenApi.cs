using System.ComponentModel;
using PlatformAlias = SDL3.SDL.WindowFlags;

namespace Lucent.ArchitectureFixtures.DirectForbidden;

public static class ForbiddenApi
{
    public static List<PlatformAlias> ExposesAliasedGenericPlatformType() => [];

    public static Type ExposesRuntimePropertyDiscovery() => typeof(TypeDescriptor);

    public static Type ExposesObjectSerializer() => typeof(System.Text.Json.JsonSerializer);

    public static Type ExposesSerializerOptions() => typeof(System.Text.Json.JsonSerializerOptions);

    public static Type ExposesSerializerContext() =>
        typeof(System.Text.Json.Serialization.JsonSerializerContext);

    public static Type ExposesSerializerMetadata() =>
        typeof(System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver);

    public static Type[] ExplicitJsonTokenTypes() =>
        [
            typeof(System.Text.Json.Utf8JsonReader),
            typeof(System.Text.Json.Utf8JsonWriter),
            typeof(System.Text.Json.JsonReaderOptions),
            typeof(System.Text.Json.JsonWriterOptions),
            typeof(System.Text.Json.JsonTokenType),
            typeof(System.Text.Json.JsonException),
        ];
}
