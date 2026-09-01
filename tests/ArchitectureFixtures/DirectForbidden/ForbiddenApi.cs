using System.ComponentModel;
using PlatformAlias = SDL3.SDL.WindowFlags;

public static class ForbiddenApi
{
    public static List<PlatformAlias> ExposesAliasedGenericPlatformType() => [];

    public static Type ExposesRuntimePropertyDiscovery() => typeof(TypeDescriptor);
}
