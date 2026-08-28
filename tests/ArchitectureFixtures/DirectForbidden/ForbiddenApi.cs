using PlatformAlias = SDL3.SDL.WindowFlags;
using System.ComponentModel;

public static class ForbiddenApi
{
    public static
        List<PlatformAlias> ExposesAliasedGenericPlatformType() => [];

    public static Type ExposesRuntimePropertyDiscovery() => typeof(TypeDescriptor);
}
