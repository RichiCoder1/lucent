using System.ComponentModel;
using PlatformAlias = SDL3.SDL.WindowFlags;

namespace Lucent.ArchitectureFixtures.DirectForbidden;

public static class ForbiddenApi
{
    public static List<PlatformAlias> ExposesAliasedGenericPlatformType() => [];

    public static Type ExposesRuntimePropertyDiscovery() => typeof(TypeDescriptor);
}
