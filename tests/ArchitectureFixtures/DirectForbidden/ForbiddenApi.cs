using PlatformAlias = SDL3.SDL.WindowFlags;

public static class ForbiddenApi
{
    public static
        List<PlatformAlias> ExposesAliasedGenericPlatformType() => [];
}
